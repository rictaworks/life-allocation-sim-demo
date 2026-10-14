using System;
using System.Collections.Generic;
using System.Linq;
using DomainCore.Tests.Support;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// Domain Core のソースを読んで、開発ルールを検査する。
    /// - 文字列は Config/ に集める（Config 以外の .cs に文字列リテラル・文字リテラルを書かない）。
    /// - 金額は long（円）、リターンは万分率の int で持ち、float・double・decimal を使わない（requirements.md 5章）。
    /// - Config の各ファイルの先頭に、数値が架空のモデル値である旨を書く（requirements.md 1.2）。
    /// </summary>
    public class SourceRulesTests
    {
        private const string ModelValueNoticeMarker = "架空のモデル値";
        private const string LineCommentPrefix = "//";

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
