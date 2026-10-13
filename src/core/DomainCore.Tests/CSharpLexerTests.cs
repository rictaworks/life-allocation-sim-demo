using System;
using System.Linq;
using DomainCore.Tests.Support;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// ソースの検査（SourceRulesTests）に使う字句解析器が、文字列・文字・数値リテラルを正しく見分けるかを確かめる。
    /// 検査が素通りしないこと（見落とさない）と、コメントの中身を誤って拾わないことの両方を見る。
    /// </summary>
    public class CSharpLexerTests
    {
        [Fact]
        public void FindsRegularStringLiteralWithItsLine()
        {
            var tokens = CSharpLexer.Tokenize("\n\nvar a = \"x\";");

            var literal = Assert.Single(tokens, token => token.Kind == CSharpTokenKind.StringLiteral);
            Assert.Equal("\"x\"", literal.Text);
            Assert.Equal(3, literal.Line);
        }

        [Fact]
        public void IgnoresQuotesInsideComments()
        {
            var source = "// \"a\" 'b'\n/* \"c\"\n 'd' */\n/// <c>\"e\"</c>\nvar x = 1;";

            var tokens = CSharpLexer.Tokenize(source);

            Assert.DoesNotContain(tokens, token => token.Kind == CSharpTokenKind.StringLiteral || token.Kind == CSharpTokenKind.CharLiteral);
            Assert.Contains(tokens, token => token.Kind == CSharpTokenKind.Identifier && token.Text == "x" && token.Line == 5);
        }

        [Fact]
        public void IgnoresPreprocessorDirectiveText()
        {
            var tokens = CSharpLexer.Tokenize("#region Don't stop\nvar a = \"x\";\n#endregion");

            var literal = Assert.Single(tokens, token => token.Kind == CSharpTokenKind.StringLiteral);
            Assert.Equal(2, literal.Line);
            Assert.DoesNotContain(tokens, token => token.Kind == CSharpTokenKind.CharLiteral);
        }

        [Fact]
        public void TreatsQuoteInsideCharLiteralAsCharacter()
        {
            var tokens = CSharpLexer.Tokenize("var c = '\"'; var d = '\\''; var e = '\\\\';");

            Assert.DoesNotContain(tokens, token => token.Kind == CSharpTokenKind.StringLiteral);
            Assert.Equal(3, tokens.Count(token => token.Kind == CSharpTokenKind.CharLiteral));
        }

        [Fact]
        public void ReadsEscapesInRegularAndVerbatimStrings()
        {
            var tokens = CSharpLexer.Tokenize("var a = \"q\\\"//\"; var b = @\"a\"\"b\\\"; var c = 1;");

            var literals = tokens.Where(token => token.Kind == CSharpTokenKind.StringLiteral).Select(token => token.Text).ToArray();
            Assert.Equal(new[] { "\"q\\\"//\"", "@\"a\"\"b\\\"" }, literals);
            Assert.Contains(tokens, token => token.Kind == CSharpTokenKind.Identifier && token.Text == "c");
        }

        [Fact]
        public void ReadsNestedStringsInInterpolationAndSkipsFormat()
        {
            var tokens = CSharpLexer.Tokenize("var s = $\"{(flag ? \"a\" : \"b\")} {x:0.00} {{y}}\";");

            Assert.Equal(3, tokens.Count(token => token.Kind == CSharpTokenKind.StringLiteral));
            Assert.StartsWith("$\"", tokens.First(token => token.Kind == CSharpTokenKind.StringLiteral).Text);
            Assert.DoesNotContain(tokens, token => token.Kind == CSharpTokenKind.RealLiteral);
            Assert.Contains(tokens, token => token.Kind == CSharpTokenKind.Identifier && token.Text == "flag");
            Assert.DoesNotContain(tokens, token => token.Kind == CSharpTokenKind.Identifier && token.Text == "y");
        }

        [Fact]
        public void FindsVerbatimInterpolatedStrings()
        {
            var tokens = CSharpLexer.Tokenize("var a = $@\"{b}\"\"\"; var c = @$\"x\";");

            Assert.Equal(2, tokens.Count(token => token.Kind == CSharpTokenKind.StringLiteral));
        }

        [Theory]
        [InlineData("1.5")]
        [InlineData(".5")]
        [InlineData("2f")]
        [InlineData("2D")]
        [InlineData("3m")]
        [InlineData("1e3")]
        [InlineData("1E-3")]
        [InlineData("1_000.25")]
        public void ClassifiesRealLiterals(string literal)
        {
            var token = Assert.Single(CSharpLexer.Tokenize($"var v = {literal};"), t => t.Kind != CSharpTokenKind.Identifier);

            Assert.Equal(CSharpTokenKind.RealLiteral, token.Kind);
            Assert.Equal(literal, token.Text);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("1_000")]
        [InlineData("10UL")]
        [InlineData("0x1F")]
        [InlineData("0x9E3779B97F4A7C15UL")]
        [InlineData("0b1010")]
        public void ClassifiesIntegerLiterals(string literal)
        {
            var token = Assert.Single(CSharpLexer.Tokenize($"var v = {literal};"), t => t.Kind != CSharpTokenKind.Identifier);

            Assert.Equal(CSharpTokenKind.IntegerLiteral, token.Kind);
            Assert.Equal(literal, token.Text);
        }

        [Fact]
        public void TreatsMemberAccessOnIntegerAsInteger()
        {
            var tokens = CSharpLexer.Tokenize("var t = 1.ToString(); var r = 1..5;");

            Assert.DoesNotContain(tokens, token => token.Kind == CSharpTokenKind.RealLiteral);
            Assert.Equal(3, tokens.Count(token => token.Kind == CSharpTokenKind.IntegerLiteral));
        }

        [Fact]
        public void ReadsKeywordsAndVerbatimIdentifiersAsIdentifiers()
        {
            var tokens = CSharpLexer.Tokenize("double a; @decimal b; System.Single c;");

            var identifiers = tokens.Where(token => token.Kind == CSharpTokenKind.Identifier).Select(token => token.Text).ToArray();
            Assert.Equal(new[] { "double", "a", "decimal", "b", "System", "Single", "c" }, identifiers);
        }

        [Theory]
        [InlineData("var a = \"x;")]
        [InlineData("var a = \"x\ny\";")]
        [InlineData("var a = @\"x;")]
        [InlineData("var a = $\"{x\";")]
        [InlineData("var a = 'x;")]
        [InlineData("/* x")]
        public void RejectsUnterminatedInput(string source)
        {
            Assert.Throws<FormatException>(() => CSharpLexer.Tokenize(source));
        }
    }
}
