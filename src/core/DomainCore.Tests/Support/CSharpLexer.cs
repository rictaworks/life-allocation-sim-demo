using System;
using System.Collections.Generic;

namespace DomainCore.Tests.Support
{
    internal enum CSharpTokenKind
    {
        Identifier,
        IntegerLiteral,
        RealLiteral,
        StringLiteral,
        CharLiteral,
    }

    internal sealed class CSharpToken
    {
        public CSharpToken(CSharpTokenKind kind, string text, int line)
        {
            Kind = kind;
            Text = text;
            Line = line;
        }

        public CSharpTokenKind Kind { get; }

        public string Text { get; }

        /// <summary>トークンが始まる行（1 始まり）。</summary>
        public int Line { get; }

        public override string ToString() => $"{Kind} {Line}: {Text}";
    }

    /// <summary>
    /// ソースの検査に使う、C# の最小限の字句解析器。
    /// コメントと前処理命令を読み飛ばし、識別子・数値リテラル・文字列リテラル・文字リテラルを取り出す（記号は読み飛ばす）。
    /// 補間文字列の式の部分も字句解析し、書式指定（{x:0.00} の 0.00 など）は読み飛ばす。
    /// 対象は C# 9（Domain Core の LangVersion）。生文字列リテラル（C# 11）は扱わない。
    /// 解釈できない入力（閉じていない文字列など）は FormatException にする（検査を素通りさせない）。
    /// </summary>
    internal sealed class CSharpLexer
    {
        private readonly string source;
        private readonly List<CSharpToken> tokens = new List<CSharpToken>();
        private int position;
        private int line = 1;
        private bool atLineStart = true;

        private CSharpLexer(string source)
        {
            this.source = source;
        }

        private bool AtEnd => position >= source.Length;

        private char Current => Peek(0);

        public static IReadOnlyList<CSharpToken> Tokenize(string source)
        {
            var lexer = new CSharpLexer(source);
            lexer.ScanCode(insideHole: false);
            return lexer.tokens;
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static bool IsHexDigit(char c) => Uri.IsHexDigit(c);

        private static bool IsRealSuffix(char c) => c == 'f' || c == 'F' || c == 'd' || c == 'D' || c == 'm' || c == 'M';

        private static bool IsIntegerSuffix(char c) => c == 'u' || c == 'U' || c == 'l' || c == 'L';

        private char Peek(int offset) => position + offset < source.Length ? source[position + offset] : '\0';

        private void Advance()
        {
            var c = source[position];
            if (c == '\n')
            {
                line++;
                atLineStart = true;
            }
            else if (!char.IsWhiteSpace(c))
            {
                atLineStart = false;
            }

            position++;
        }

        private void Advance(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Advance();
            }
        }

        private FormatException Unterminated(string what, int startLine) =>
            new FormatException($"{what}が閉じていません（{startLine} 行目）");

        /// <summary>
        /// コードを読む。補間の式の中（insideHole）では、深さ 0 の '}' か ':' の手前で戻る。
        /// </summary>
        private void ScanCode(bool insideHole)
        {
            var depth = 0;
            var startLine = line;
            while (!AtEnd)
            {
                var c = Current;
                if (insideHole && depth == 0 && (c == '}' || c == ':'))
                {
                    return;
                }

                if (c == '#' && atLineStart)
                {
                    SkipRestOfLine();
                }
                else if (c == '/' && Peek(1) == '/')
                {
                    SkipRestOfLine();
                }
                else if (c == '/' && Peek(1) == '*')
                {
                    SkipBlockComment();
                }
                else if (c == '"')
                {
                    ScanRegularString();
                }
                else if (c == '@' && Peek(1) == '"')
                {
                    ScanVerbatimString();
                }
                else if (c == '$' && Peek(1) == '"')
                {
                    ScanInterpolatedString(prefixLength: 1, verbatim: false);
                }
                else if (((c == '$' && Peek(1) == '@') || (c == '@' && Peek(1) == '$')) && Peek(2) == '"')
                {
                    ScanInterpolatedString(prefixLength: 2, verbatim: true);
                }
                else if (c == '\'')
                {
                    ScanCharLiteral();
                }
                else if (c == '.' && Peek(1) == '.')
                {
                    // 範囲演算子（1..5）。2 つ目の . を実数リテラル（.5）の始まりと読まない
                    Advance(2);
                }
                else if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
                {
                    ScanNumber();
                }
                else if (c == '@' && IsIdentifierStart(Peek(1)))
                {
                    Advance();
                    ScanIdentifier();
                }
                else if (IsIdentifierStart(c))
                {
                    ScanIdentifier();
                }
                else
                {
                    if (c == '(' || c == '[' || c == '{')
                    {
                        depth++;
                    }
                    else if ((c == ')' || c == ']' || c == '}') && depth > 0)
                    {
                        depth--;
                    }

                    Advance();
                }
            }

            if (insideHole)
            {
                throw Unterminated("補間の式", startLine);
            }
        }

        private void SkipRestOfLine()
        {
            while (!AtEnd && Current != '\n')
            {
                Advance();
            }
        }

        private void SkipBlockComment()
        {
            var startLine = line;
            Advance(2);
            while (!(Current == '*' && Peek(1) == '/'))
            {
                if (AtEnd)
                {
                    throw Unterminated("コメント", startLine);
                }

                Advance();
            }

            Advance(2);
        }

        private void ScanRegularString()
        {
            var start = position;
            var startLine = line;
            Advance();
            while (true)
            {
                if (AtEnd || Current == '\n')
                {
                    throw Unterminated("文字列リテラル", startLine);
                }

                if (Current == '\\' && position + 1 < source.Length)
                {
                    Advance(2);
                }
                else if (Current == '"')
                {
                    Advance();
                    break;
                }
                else
                {
                    Advance();
                }
            }

            Add(CSharpTokenKind.StringLiteral, start, startLine);
        }

        private void ScanVerbatimString()
        {
            var start = position;
            var startLine = line;
            Advance(2);
            while (true)
            {
                if (AtEnd)
                {
                    throw Unterminated("逐語的文字列リテラル", startLine);
                }

                if (Current == '"' && Peek(1) == '"')
                {
                    Advance(2);
                }
                else if (Current == '"')
                {
                    Advance();
                    break;
                }
                else
                {
                    Advance();
                }
            }

            Add(CSharpTokenKind.StringLiteral, start, startLine);
        }

        private void ScanInterpolatedString(int prefixLength, bool verbatim)
        {
            var start = position;
            var startLine = line;
            var index = tokens.Count;
            Advance(prefixLength + 1);
            while (true)
            {
                if (AtEnd || (!verbatim && Current == '\n'))
                {
                    throw Unterminated("補間文字列", startLine);
                }

                var c = Current;
                if (!verbatim && c == '\\' && position + 1 < source.Length)
                {
                    Advance(2);
                }
                else if (verbatim && c == '"' && Peek(1) == '"')
                {
                    Advance(2);
                }
                else if (c == '"')
                {
                    Advance();
                    break;
                }
                else if ((c == '{' && Peek(1) == '{') || (c == '}' && Peek(1) == '}'))
                {
                    Advance(2);
                }
                else if (c == '{')
                {
                    Advance();
                    ScanHole();
                }
                else if (c == '}')
                {
                    throw new FormatException($"補間文字列の '}}' が対応していません（{line} 行目）");
                }
                else
                {
                    Advance();
                }
            }

            // 式の中のトークンより前に、外側の文字列を置く（ソースの順にする）
            tokens.Insert(index, new CSharpToken(CSharpTokenKind.StringLiteral, source.Substring(start, position - start), startLine));
        }

        /// <summary>補間の { の次から読み、対応する } の次まで進む。書式指定（: の後）は読み飛ばす。</summary>
        private void ScanHole()
        {
            var startLine = line;
            ScanCode(insideHole: true);
            if (Current == ':')
            {
                Advance();
                while (Current != '}')
                {
                    if (AtEnd || Current == '"' || Current == '\n')
                    {
                        throw Unterminated("補間の書式指定", startLine);
                    }

                    Advance();
                }
            }

            Advance();
        }

        private void ScanCharLiteral()
        {
            var start = position;
            var startLine = line;
            Advance();
            while (true)
            {
                if (AtEnd || Current == '\n')
                {
                    throw Unterminated("文字リテラル", startLine);
                }

                if (Current == '\\' && position + 1 < source.Length)
                {
                    Advance(2);
                }
                else if (Current == '\'')
                {
                    Advance();
                    break;
                }
                else
                {
                    Advance();
                }
            }

            Add(CSharpTokenKind.CharLiteral, start, startLine);
        }

        private void ScanNumber()
        {
            var start = position;
            var startLine = line;
            var isReal = false;
            if (Current == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
            {
                Advance(2);
                while (IsHexDigit(Current) || Current == '_')
                {
                    Advance();
                }

                SkipIntegerSuffix();
            }
            else if (Current == '0' && (Peek(1) == 'b' || Peek(1) == 'B'))
            {
                Advance(2);
                while (Current == '0' || Current == '1' || Current == '_')
                {
                    Advance();
                }

                SkipIntegerSuffix();
            }
            else
            {
                SkipDecimalDigits();
                if (Current == '.' && char.IsDigit(Peek(1)))
                {
                    isReal = true;
                    Advance();
                    SkipDecimalDigits();
                }

                if (Current == 'e' || Current == 'E')
                {
                    var digitOffset = Peek(1) == '+' || Peek(1) == '-' ? 2 : 1;
                    if (char.IsDigit(Peek(digitOffset)))
                    {
                        isReal = true;
                        Advance(digitOffset);
                        SkipDecimalDigits();
                    }
                }

                if (IsRealSuffix(Current))
                {
                    isReal = true;
                    Advance();
                }
                else if (!isReal)
                {
                    SkipIntegerSuffix();
                }
            }

            Add(isReal ? CSharpTokenKind.RealLiteral : CSharpTokenKind.IntegerLiteral, start, startLine);
        }

        private void SkipDecimalDigits()
        {
            while (char.IsDigit(Current) || Current == '_')
            {
                Advance();
            }
        }

        private void SkipIntegerSuffix()
        {
            while (IsIntegerSuffix(Current))
            {
                Advance();
            }
        }

        private void ScanIdentifier()
        {
            var start = position;
            var startLine = line;
            while (!AtEnd && IsIdentifierPart(Current))
            {
                Advance();
            }

            Add(CSharpTokenKind.Identifier, start, startLine);
        }

        private void Add(CSharpTokenKind kind, int start, int startLine)
        {
            tokens.Add(new CSharpToken(kind, source.Substring(start, position - start), startLine));
        }
    }
}
