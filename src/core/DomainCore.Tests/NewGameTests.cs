using System.Linq;
using DomainCore.Config;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// 新規ゲームの初期状態（requirements.md 3.1・3.3）。難易度コードの検証の結果を含み、失敗は理由コードで返す。
    /// </summary>
    public class NewGameTests
    {
        [Fact]
        public void StartsAt22WithStageIncomeNoSavingsAndDefaultSatisfaction()
        {
            var start = NewGame.Start("normal");

            Assert.True(start.IsValid);
            var state = start.Value.InitialState;
            Assert.Equal(GameRules.StartAge, state.Age);
            Assert.Equal(LifeStage.ForAge(GameRules.StartAge).TakeHomeIncomeYen, state.IncomeYen);
            Assert.Equal(0, state.SavingsYen);
            Assert.Equal(0, state.InvestmentYen);
            Assert.Equal(GameRules.InitialSatisfaction, state.Satisfaction);
            Assert.Equal(GameRules.InitialPriceIndexBp, state.PriceIndexBp);
            Assert.Equal(PlayerStatus.Playing, state.Status);
            Assert.Equal(0, state.RaiseTotalYen);
            Assert.Empty(state.OccurredOnceEventCodes);
        }

        [Fact]
        public void KeepsTheDifficultyEntryOfTheCode()
        {
            foreach (var difficulty in DifficultyTable.All)
            {
                var start = NewGame.Start(difficulty.Code);

                Assert.Same(difficulty, start.Value.Difficulty);
            }
        }

        [Theory]
        [InlineData(null, DifficultyCodeViolation.Missing)]
        [InlineData("", DifficultyCodeViolation.Missing)]
        [InlineData("unknown", DifficultyCodeViolation.Unknown)]
        [InlineData("Normal", DifficultyCodeViolation.Unknown)]
        public void FailsWithReasonForInvalidCodes(string code, DifficultyCodeViolation reason)
        {
            var start = NewGame.Start(code);

            Assert.False(start.IsValid);
            Assert.Equal(reason, start.Reason);
        }
    }
}
