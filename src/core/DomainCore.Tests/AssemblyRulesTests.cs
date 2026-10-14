using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DomainCore.Tests.Support;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// コンパイル済みの Domain Core を調べて、ソースの字句だけでは見えない規則違反を見つける。
    /// - 浮動小数点（float・double・decimal）を、型・IL の命令・呼び出し先のどこでも使わない（requirements.md 5章）。
    /// - 非決定的な API（System.Random・時刻・Guid など）を使わない（requirements.md 4章：乱数は引数のシードだけ）。
    /// - 書き換えられる static の状態（グローバル変数）を持たない（CLAUDE.md の開発ルール）。
    /// - 外部パッケージに依存しない（Unity の DLL として使う）。
    /// </summary>
    public class AssemblyRulesTests
    {
        private const string NetStandardAssemblyName = "netstandard";
        private const string YenSuffix = "Yen";
        private const string BasisPointSuffix = "Bp";
        private const string PercentSuffix = "Pct";

        // コンパイラが無名関数の呼び出しを速くするために作る置き場（<>c の <>9__ で始まる static フィールド）
        private const string LambdaCacheTypeName = "<>c";
        private const string LambdaCacheFieldPrefix = "<>9__";

        private static Assembly DomainCoreAssembly => typeof(Allocation).Assembly;

        [Fact]
        public void DomainCoreDoesNotUseFloatingPointTypes()
        {
            var findings = TypeUsageInspector.FindInAssembly(DomainCoreAssembly, IsFloatingPoint, FloatingPointOpCodes());

            Assert.Empty(findings);
        }

        [Theory]
        [InlineData(nameof(FloatingPointSamples.CallsMathPowWithIntegers))]
        [InlineData(nameof(FloatingPointSamples.CastsThroughDouble))]
        [InlineData(nameof(FloatingPointSamples.MultipliesByDecimal))]
        [InlineData(nameof(FloatingPointSamples.KeepsDoubleLocal))]
        [InlineData(nameof(FloatingPointSamples.ConvertsToHalf))]
        public void InspectorDetectsFloatingPointUsage(string methodName)
        {
            var findings = TypeUsageInspector.FindInMethod(SampleMethod(methodName), IsFloatingPoint, FloatingPointOpCodes());

            Assert.NotEmpty(findings);
        }

        [Fact]
        public void InspectorAcceptsIntegerArithmetic()
        {
            var method = SampleMethod(nameof(FloatingPointSamples.UsesIntegersOnly));

            Assert.Empty(TypeUsageInspector.FindInMethod(method, IsFloatingPoint, FloatingPointOpCodes()));
            Assert.Empty(TypeUsageInspector.FindInMethod(method, IsNonDeterministicApi, Array.Empty<OpCode>()));
        }

        [Fact]
        public void DomainCoreDoesNotUseNonDeterministicApis()
        {
            var findings = TypeUsageInspector.FindInAssembly(DomainCoreAssembly, IsNonDeterministicApi, Array.Empty<OpCode>());

            Assert.Empty(findings);
        }

        [Fact]
        public void InspectorDetectsSystemRandom()
        {
            var method = SampleMethod(nameof(FloatingPointSamples.UsesSystemRandom));

            Assert.NotEmpty(TypeUsageInspector.FindInMethod(method, IsNonDeterministicApi, Array.Empty<OpCode>()));
        }

        [Fact]
        public void DomainCoreHasNoMutableStaticState()
        {
            var findings = MutableStaticFindings(DomainCoreAssembly.GetTypes());

            Assert.Empty(findings);
        }

        [Fact]
        public void GeneratedTypesAreNotExemptFromTheStaticStateRule()
        {
            // コンパイラが生成した型の印があっても、書き換えられる static フィールドは見つける（型の印だけでは除外しない）
            var findings = MutableStaticFindings(new[] { typeof(GeneratedStaticSamples) });

            Assert.Contains(findings, finding => finding.Contains(nameof(GeneratedStaticSamples.Counter), StringComparison.Ordinal));
        }

        [Fact]
        public void LambdaCacheFieldsAreExemptedByName()
        {
            // 無名関数の置き場は、コンパイラが実際に作ったものを使う。見本が置き場を作ったことを確かめてから、除外されることを見る
            var cacheType = typeof(LambdaCacheSamples).GetNestedType(LambdaCacheTypeName, BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"見本の無名関数の置き場がありません: {LambdaCacheTypeName}");
            Assert.Contains(
                cacheType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
                field => field.Name.StartsWith(LambdaCacheFieldPrefix, StringComparison.Ordinal));

            Assert.Empty(MutableStaticFindings(new[] { cacheType }));
        }

        /// <summary>
        /// 書き換えられる static フィールドと、中身を書き換えられるコレクションの static フィールドを列挙する。
        /// コンパイラが生成した型も、印（CompilerGenerated）だけでは除かない。除くのは、無名関数の置き場（IsLambdaCacheField）だけ。
        /// </summary>
        private static List<string> MutableStaticFindings(IEnumerable<Type> types)
        {
            var findings = new List<string>();
            foreach (var type in types)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral || IsLambdaCacheField(type, field))
                    {
                        continue;
                    }

                    if (!field.IsInitOnly)
                    {
                        findings.Add($"{type.FullName}.{field.Name}: 書き換えられる static フィールドです");
                    }
                    else if (!type.ContainsGenericParameters && IsMutableCollection(field.GetValue(null)))
                    {
                        findings.Add($"{type.FullName}.{field.Name}: 中身を書き換えられるコレクションです");
                    }
                }
            }

            return findings;
        }

        /// <summary>
        /// 無名関数の置き場（コンパイラが生成した <>c の <>9__ フィールド）だけを、名前で除く。利用者の書いた static と、ほかの生成された static は検査する。
        /// 置き場は、同じ無名関数を 2 回目以降に使うための参照で、ゲームの状態ではない。
        /// </summary>
        private static bool IsLambdaCacheField(Type type, FieldInfo field) =>
            type.Name == LambdaCacheTypeName && field.Name.StartsWith(LambdaCacheFieldPrefix, StringComparison.Ordinal);

        [Fact]
        public void DomainCoreReferencesOnlyNetStandard()
        {
            var references = DomainCoreAssembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

            Assert.Equal(new[] { NetStandardAssemblyName }, references);
        }

        [Fact]
        public void MoneyIsLongYenAndRatesAreIntBasisPointsOrPercent()
        {
            var members = DomainCoreAssembly.GetExportedTypes()
                .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(property => (property.Name, Type: property.PropertyType))
                    .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Where(field => field.IsLiteral && !type.IsEnum)
                        .Select(field => (field.Name, Type: field.FieldType))))
                .ToList();

            var yen = members.Where(member => member.Name.EndsWith(YenSuffix, StringComparison.Ordinal)).ToList();
            var basisPoints = members.Where(member => member.Name.EndsWith(BasisPointSuffix, StringComparison.Ordinal)).ToList();
            var percents = members.Where(member => member.Name.EndsWith(PercentSuffix, StringComparison.Ordinal)).ToList();

            Assert.NotEmpty(yen);
            Assert.NotEmpty(basisPoints);
            Assert.NotEmpty(percents);
            Assert.All(yen, member => Assert.Equal(typeof(long), member.Type));
            Assert.All(basisPoints, member => Assert.Equal(typeof(int), member.Type));
            Assert.All(percents, member => Assert.Equal(typeof(int), member.Type));
        }

        private static bool IsFloatingPoint(Type type) =>
            type == typeof(float) || type == typeof(double) || type == typeof(decimal) || type == typeof(Half);

        private static bool IsNonDeterministicApi(Type type) =>
            type == typeof(Random) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(Guid) || type == typeof(Environment) || type == typeof(Stopwatch);

        private static OpCode[] FloatingPointOpCodes() => new[]
        {
            OpCodes.Ldc_R4, OpCodes.Ldc_R8, OpCodes.Conv_R4, OpCodes.Conv_R8, OpCodes.Conv_R_Un, OpCodes.Ckfinite,
            OpCodes.Ldelem_R4, OpCodes.Ldelem_R8, OpCodes.Stelem_R4, OpCodes.Stelem_R8,
            OpCodes.Ldind_R4, OpCodes.Ldind_R8, OpCodes.Stind_R4, OpCodes.Stind_R8,
        };

        private static bool IsMutableCollection(object? value) =>
            (value is IList list && !list.IsReadOnly) || (value is IDictionary dictionary && !dictionary.IsReadOnly);

        private static MethodInfo SampleMethod(string name) =>
            typeof(FloatingPointSamples).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"見本のメソッドがありません: {name}");
    }

    /// <summary>
    /// TypeUsageInspector が規則違反を見つけられることを確かめるための見本（テストのアセンブリにだけ置く）。
    /// </summary>
    public static class FloatingPointSamples
    {
        // ソースに double の字句は無いが、int を double に変換して Math.Pow を呼ぶ
        public static long CallsMathPowWithIntegers(int value) => (long)Math.Pow(value, 2);

        public static long CastsThroughDouble(long value) => (long)(double)value;

        public static int MultipliesByDecimal(int value) => (int)(value * 1.5m);

        public static int KeepsDoubleLocal(int value)
        {
            double half = value / 2.0;
            return (int)half;
        }

        // System.Half は浮動小数点の型（float・double・decimal と同じく使わない）
        public static int ConvertsToHalf(int value) => (int)(Half)value;

        public static int UsesSystemRandom(int seed) => new Random(seed).Next();

        public static long UsesIntegersOnly(long value) => (value * 3) + 1;
    }

    /// <summary>
    /// コンパイラが生成した型の印を付けた見本（書き換えられる static を、型の印があっても見つけることの確認）。
    /// </summary>
    [CompilerGenerated]
    public static class GeneratedStaticSamples
    {
        public static int Counter;
    }

    /// <summary>
    /// 無名関数の見本。コンパイラが呼び出しを速くするための置き場（<>c の <>9__ フィールド）を作らせる。
    /// </summary>
    public static class LambdaCacheSamples
    {
        public static Func<int, int> Increment() => value => value + 1;
    }
}
