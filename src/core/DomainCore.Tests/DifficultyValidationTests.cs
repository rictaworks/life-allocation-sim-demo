using System.Linq;
using DomainCore.Config;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// 難易度コードの検証。easy・normal・hard（大文字・小文字と前後の空白も含めて完全一致）だけを受け付け、
    /// それ以外は理由コード（未指定・不明）を返す。例外は使わない。
    /// </summary>
    public class DifficultyValidationTests
    {
        [Theory]
        [InlineData("easy")]
        [InlineData("normal")]
        [InlineData("hard")]
        public void AcceptsKnownCodesAndReturnsMasterEntry(string code)
        {
            var result = Simulator.ValidateDifficultyCode(code);

            Assert.True(result.IsValid);
            Assert.Same(DifficultyTable.All.Single(difficulty => difficulty.Code == code), result.Value);
        }

        [Theory]
        [InlineData("Easy")]
        [InlineData("EASY")]
        [InlineData(" easy")]
        [InlineData("easy ")]
        [InlineData("medium")]
        [InlineData("やさしい")]
        [InlineData("easy\0")]
        [InlineData(" ")]
        public void RejectsUnknownCodes(string code)
        {
            var result = Simulator.ValidateDifficultyCode(code);

            Assert.False(result.IsValid);
            Assert.Equal(DifficultyCodeViolation.Unknown, result.Reason);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void RejectsMissingCode(string? code)
        {
            var result = Simulator.ValidateDifficultyCode(code!);

            Assert.False(result.IsValid);
            Assert.Equal(DifficultyCodeViolation.Missing, result.Reason);
        }
    }
}
