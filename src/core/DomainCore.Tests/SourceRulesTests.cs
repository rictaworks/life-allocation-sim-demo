using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DomainCore.Tests.Support;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// Domain Core のソースを読んで、開発ルールを検査する。
    /// - 文字列は Config/ に集める（Config 以外の .cs に文字列リテラル・文字リテラルを書かない）。
    /// - 数値は Config/ に集める（Config 以外の .cs には、意味の無い 0 と 1 だけを書く。列挙体の値は状態の ID なので対象外）。
    /// - 金額は long（円）、リターンは万分率の int で持ち、float・double・decimal を使わない（requirements.md 5章）。
    /// - Config の各ファイルの先頭に、数値が架空のモデル値である旨を書く（requirements.md 1.2）。
    /// </summary>
    public class SourceRulesTests
    {
        private const string ModelValueNoticeMarker = "架空のモデル値";
        private const string LineCommentPrefix = "//";

        // Config 以外で書いてよい数値。意味の無い値だけを名前で列挙する
        private static readonly string[] AllowedNumbersOutsideConfig = { "0", "1" };

        // 列挙体の宣言（enum 名 { 値 = 数, ... }）。本体に入れ子の波括弧は無い
        private static readonly Regex EnumDeclaration = new Regex(@"\benum\s+\w+\s*\{[^}]*\}", RegexOptions.CultureInvariant);

        [Fact]
        public void FindsDomainCoreSourcesInsideAndOutsideConfig()
        {
            var files = DomainCoreSources.All();

            Assert.Contains(files, file => file.IsConfig);
            Assert.Contains(files, file => !file.IsConfig);
            Assert.DoesNotContain(files, file => file.IsBuildOutput);
        }

        [Fact]
        public void ConfigHoldsStringLiterals()
        {
            // 検査が素通りしていないことの確認：実際の Config のファイルからは、文字列リテラルが見つかる
            var count = DomainCoreSources.All()
                .Where(file => file.IsConfig)
                .SelectMany(file => CSharpLexer.Tokenize(file.ReadAllText()))
                .Count(token => token.Kind == CSharpTokenKind.StringLiteral);

            Assert.True(count > 0, "Config から文字列リテラルが見つかりません");
        }

        [Fact]
        public void NoStringOrCharLiteralsOutsideConfig()
        {
            var violations = Violations(
                DomainCoreSources.All().Where(file => !file.IsConfig),
                token => token.Kind == CSharpTokenKind.StringLiteral || token.Kind == CSharpTokenKind.CharLiteral);

            Assert.Empty(violations);
        }

        [Fact]
        public void NoFloatingPointTypesOrLiteralsInDomainCore()
        {
            var violations = Violations(
                DomainCoreSources.All(),
                token => token.Kind == CSharpTokenKind.RealLiteral
                    || (token.Kind == CSharpTokenKind.Identifier && IsFloatingPointName(token.Text)));

            Assert.Empty(violations);
        }

        [Fact]
        public void ConfigHoldsNumericLiterals()
        {
            // 検査が素通りしていないことの確認：実際の Config のファイルからは、数値のリテラルが見つかる
            var count = DomainCoreSources.All()
                .Where(file => file.IsConfig)
                .SelectMany(file => CSharpLexer.Tokenize(file.ReadAllText()))
                .Count(IsNumericLiteral);

            Assert.True(count > 0, "Config から数値のリテラルが見つかりません");
        }

        [Fact]
        public void NoNumericLiteralsOutsideConfigExceptZeroAndOne()
        {
            var violations = DomainCoreSources.All()
                .Where(file => !file.IsConfig)
                .SelectMany(file => CSharpLexer.Tokenize(WithoutEnumDeclarations(file.ReadAllText()))
                    .Where(token => IsNumericLiteral(token) && !AllowedNumbersOutsideConfig.Contains(WithoutTypeSuffix(token.Text)))
                    .Select(token => $"{file.RelativePath}:{token.Line}: {token.Text}"))
                .ToList();

            Assert.Empty(violations);
        }

        [Fact]
        public void EveryConfigFileStartsWithModelValueNotice()
        {
            var configFiles = DomainCoreSources.All().Where(file => file.IsConfig).ToList();

            Assert.NotEmpty(configFiles);
            Assert.All(configFiles, file =>
            {
                var firstLine = file.ReadAllText().Split('\n')[0].TrimEnd('\r');
                Assert.StartsWith(LineCommentPrefix, firstLine);
                Assert.Contains(ModelValueNoticeMarker, firstLine);
            });
        }

        private static bool IsNumericLiteral(CSharpToken token) =>
            token.Kind == CSharpTokenKind.IntegerLiteral || token.Kind == CSharpTokenKind.RealLiteral;

        // 型の接尾辞（0L・1UL など）は値の意味を変えないので、外してから比べる
        private static string WithoutTypeSuffix(string numericText) => numericText.TrimEnd('u', 'U', 'l', 'L');

        // 列挙体の値は、名前と結び付いた状態の ID（既定値の 0 と区別するために 1 から書く）。規則の数値ではないので、検査の前に外す。
        // 外した所は改行の数だけ残し、行の番号がずれないようにする。
        private static string WithoutEnumDeclarations(string text) =>
            EnumDeclaration.Replace(text, match => new string('\n', match.Value.Count(character => character == '\n')));

        private static bool IsFloatingPointName(string identifier) =>
            identifier == "float" || identifier == "double" || identifier == "decimal"
            || identifier == "Single" || identifier == "Double" || identifier == "Decimal"
            || identifier == "MathF" || identifier == "Half";

        private static List<string> Violations(IEnumerable<SourceFile> files, Func<CSharpToken, bool> isViolation) =>
            files
                .SelectMany(file => CSharpLexer.Tokenize(file.ReadAllText())
                    .Where(isViolation)
                    .Select(token => $"{file.RelativePath}:{token.Line}: {token.Text}"))
                .ToList();
    }
}
