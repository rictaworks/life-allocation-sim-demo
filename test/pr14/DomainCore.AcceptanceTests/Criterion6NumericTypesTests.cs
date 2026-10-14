using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace DomainCore.AcceptanceTests
{
    /// <summary>
    /// 受け入れ条件 6：金額は long（円）、リターンは万分率の int。Domain Core で float・double・decimal を使わない。
    /// ここでは、公開されている型・メンバーの形（プロパティ・フィールド・メソッドの戻り値と引数・コンストラクタの引数）を調べる。
    /// ソースの字句の検査は acceptance.check.mjs、コンパイル済みの IL の検査は PR のテスト（AssemblyRulesTests）が行う。
    /// </summary>
    public class Criterion6NumericTypesTests
    {
        private const string YenSuffix = "Yen";
        private const string BasisPointSuffix = "Bp";
        private const string PercentSuffix = "Pct";

        private static Assembly DomainCoreAssembly => typeof(Allocation).Assembly;

        [Fact]
        public void PublicSurfaceHasNoFloatingPointTypes()
        {
            var signatures = AllPublicSignatures();
            var findings = signatures
                .Where(signature => ContainsFloatingPoint(signature.Type))
                .Select(signature => $"{signature.Qualified}: {signature.Type}")
                .ToList();

            Assert.NotEmpty(signatures);
            Assert.Empty(findings);
        }

        [Fact]
        public void MoneyIsLongYenAndRatesAreIntBasisPointsOrPercent()
        {
            var signatures = AllPublicSignatures();
            var yen = signatures.Where(signature => EndsWithName(signature.Member, YenSuffix)).ToList();
            var basisPoints = signatures.Where(signature => EndsWithName(signature.Member, BasisPointSuffix)).ToList();
            var percents = signatures.Where(signature => EndsWithName(signature.Member, PercentSuffix)).ToList();

            // 受け入れ条件に出てくる値が、名前の付け方（…Yen・…Bp・…Pct）の対象に入っていることを確かめてから、型を確かめる
            foreach (var expected in new[] { "LifeStage.TakeHomeIncomeYen", "LifeStage.BaseLivingCostYen", "LifeEvent.AmountYen", "Rank.MinTotalAssetsYen" })
            {
                Assert.Contains(yen, signature => signature.Qualified == expected);
            }

            foreach (var expected in new[] { "Difficulty.MeanReturnBp", "Difficulty.VolatilityBp", "LifeEvent.MarketReturnBp", "LifeEvent.ProbabilityBp" })
            {
                Assert.Contains(basisPoints, signature => signature.Qualified == expected);
            }

            foreach (var expected in new[] { "Allocation.LivingPct", "Allocation.SavingPct", "Allocation.InvestPct" })
            {
                Assert.Contains(percents, signature => signature.Qualified == expected);
            }

            Assert.All(yen, signature => Assert.True(signature.Type == typeof(long), $"{signature.Qualified} は {signature.Type}（long であるべき）"));
            Assert.All(basisPoints, signature => Assert.True(signature.Type == typeof(int), $"{signature.Qualified} は {signature.Type}（int であるべき）"));
            Assert.All(percents, signature => Assert.True(signature.Type == typeof(int), $"{signature.Qualified} は {signature.Type}（int であるべき）"));
        }

        private static List<Signature> AllPublicSignatures() =>
            DomainCoreAssembly.GetExportedTypes().SelectMany(PublicSignatures).ToList();

        /// <summary>公開の署名に現れる型（プロパティ・フィールド・メソッドの戻り値と引数・コンストラクタの引数）。</summary>
        private static IEnumerable<Signature> PublicSignatures(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var property in type.GetProperties(flags))
            {
                yield return new Signature(type, property.Name, property.PropertyType);
            }

            if (!type.IsEnum)
            {
                foreach (var field in type.GetFields(flags))
                {
                    yield return new Signature(type, field.Name, field.FieldType);
                }
            }

            foreach (var method in type.GetMethods(flags))
            {
                yield return new Signature(type, method.Name, method.ReturnType);
                foreach (var parameter in method.GetParameters())
                {
                    yield return new Signature(type, parameter.Name ?? string.Empty, parameter.ParameterType);
                }
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    yield return new Signature(type, parameter.Name ?? string.Empty, parameter.ParameterType);
                }
            }
        }

        // 名前の末尾。コンストラクタの引数（amountYen など）も対象にするため、大文字・小文字を区別しない
        private static bool EndsWithName(string name, string suffix) => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        private static bool ContainsFloatingPoint(Type type)
        {
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                return true;
            }

            if (type.HasElementType && type.GetElementType() is Type element)
            {
                return ContainsFloatingPoint(element);
            }

            return type.IsGenericType && type.GetGenericArguments().Any(ContainsFloatingPoint);
        }

        private sealed class Signature
        {
            public Signature(Type owner, string member, Type type)
            {
                Member = member;
                Qualified = $"{owner.Name}.{member}";
                Type = type;
            }

            public string Member { get; }

            public string Qualified { get; }

            public Type Type { get; }
        }
    }
}
