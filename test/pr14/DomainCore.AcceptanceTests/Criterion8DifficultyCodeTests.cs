using System;
using System.Linq;
using DomainCore.Config;
using Xunit;

namespace DomainCore.AcceptanceTests
{
    /// <summary>
    /// 受け入れ条件 8：難易度コードの検証（easy・normal・hard 以外は失敗の理由コードを返す）も、この Domain Core に書く。
    /// 理由コードの分け方（null・空は Missing、それ以外は Unknown）と、完全一致（大文字・小文字・前後の空白も区別）は PR 本文の確認事項 5 による。
    /// </summary>
    public class Criterion8DifficultyCodeTests
    {
        [Theory]
        [InlineData("easy")]
        [InlineData("normal")]
        [InlineData("hard")]
        public void AcceptsTheThreeCodes(string code)
        {
            var result = Simulator.ValidateDifficultyCode(code);

            Assert.True(result.IsValid);
            Assert.Equal(code, result.Value.Code);
            Assert.Same(DifficultyTable.All.Single(difficulty => difficulty.Code == code), result.Value);
        }

        [Theory]
        [InlineData(null, DifficultyCodeViolation.Missing)]
        [InlineData("", DifficultyCodeViolation.Missing)]
        [InlineData(" ", DifficultyCodeViolation.Unknown)]
        [InlineData("EASY", DifficultyCodeViolation.Unknown)]
        [InlineData("Easy", DifficultyCodeViolation.Unknown)]
        [InlineData(" easy", DifficultyCodeViolation.Unknown)]
        [InlineData("easy ", DifficultyCodeViolation.Unknown)]
        [InlineData("normal\n", DifficultyCodeViolation.Unknown)]
        [InlineData("hard\0", DifficultyCodeViolation.Unknown)]
        [InlineData("medium", DifficultyCodeViolation.Unknown)]
        [InlineData("ｅａｓｙ", DifficultyCodeViolation.Unknown)]
        [InlineData("やさしい", DifficultyCodeViolation.Unknown)]
        [InlineData("easy,hard", DifficultyCodeViolation.Unknown)]
        public void RejectsOtherCodesWithAReasonCodeInsteadOfAnException(string? code, DifficultyCodeViolation expected)
        {
            ValidationResult<Difficulty, DifficultyCodeViolation>? result = null;
            var exception = Record.Exception(() => { result = Simulator.ValidateDifficultyCode(code!); });

            Assert.Null(exception);
            Assert.NotNull(result);
            Assert.False(result!.IsValid);
            Assert.Equal(expected, result.Reason);
        }

        [Fact]
        public void ReasonCodesAreMissingAndUnknownOnly()
        {
            var names = Enum.GetNames(typeof(DifficultyCodeViolation)).OrderBy(name => name, StringComparer.Ordinal).ToArray();

            Assert.Equal(new[] { "Missing", "Unknown" }, names);
        }
    }
}
