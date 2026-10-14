using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DomainCore.Config;
using DomainCore.Tests.Support;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// requirements.md 4.5 replayFromHistory：初期状態・配分履歴・シードから、年次精算を順に適用して状態を再構築する。
    /// 不正な配分があれば、どの年で失敗したかと理由を返す。完走・破綻のあとのターンは受け付けない。
    /// </summary>
    public class ReplayerTests
    {
        private static readonly GameStart Normal = NewGame.Start("normal").Value;

        private static readonly Allocation[] Mixed =
        {
            new Allocation(60, 20, 20),
            new Allocation(70, 20, 10),
            new Allocation(100, 0, 0),
            new Allocation(50, 30, 20),
            new Allocation(80, 10, 10),
        };

        [Fact]
        public void ReplayMatchesStepByStepAdvance()
        {
            var outcome = Replayer.Replay(Normal, 42, Mixed).Outcome;

            var state = Normal.InitialState;
            var random = new SeededRandom(42);
            for (var i = 0; i < Mixed.Length; i++)
            {
                var step = YearSettlement.Advance(state, Mixed[i], Normal.Difficulty, random).Value;

                Assert.Equal(step.Next.Age, outcome.Years[i].Next.Age);
                Assert.Equal(step.Next.SavingsYen, outcome.Years[i].Next.SavingsYen);
                Assert.Equal(step.Next.InvestmentYen, outcome.Years[i].Next.InvestmentYen);
                Assert.Equal(step.Next.Satisfaction, outcome.Years[i].Next.Satisfaction);
                state = step.Next;
            }

            Assert.Equal(state.SavingsYen, outcome.FinalState.SavingsYen);
            Assert.Equal(Mixed.Length, outcome.Years.Count);
        }

        [Fact]
        public void SameInputGivesSameResult()
        {
            var first = Replayer.Replay(Normal, 42, Mixed).Outcome;
            var second = Replayer.Replay(Normal, 42, Mixed).Outcome;

            Assert.Equal(first.Years.Select(year => year.Next.SavingsYen), second.Years.Select(year => year.Next.SavingsYen));
            Assert.Equal(first.FinalState.InvestmentYen, second.FinalState.InvestmentYen);
        }

        [Fact]
        public void DifferentSeedsGiveDifferentCareers()
        {
            var history = Enumerable.Repeat(new Allocation(60, 20, 20), 10).ToArray();

            var first = Replayer.Replay(Normal, 1, history).Outcome.Years.Select(year => year.Next.SavingsYen).ToArray();
            var second = Replayer.Replay(Normal, 2, history).Outcome.Years.Select(year => year.Next.SavingsYen).ToArray();

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void InvalidAllocationReportsItsTurnAgeAndReason()
        {
            var history = new[] { new Allocation(60, 20, 20), new Allocation(60, 20, 20), new Allocation(50, 50, 50), new Allocation(60, 20, 20) };

            var result = Replayer.Replay(Normal, 7, history);

            Assert.False(result.IsValid);
            Assert.Equal(3, result.Failure.TurnNo);
            Assert.Equal(24, result.Failure.Age);
            Assert.Equal(ReplayViolation.InvalidAllocation, result.Failure.Violation);
            Assert.Equal(AllocationViolation.SumMismatch, result.Failure.AllocationReason);
        }

        [Theory]
        [InlineData(-5, 55, 50, AllocationViolation.Negative)]
        [InlineData(33, 33, 34, AllocationViolation.StepMismatch)]
        [InlineData(50, 50, 50, AllocationViolation.SumMismatch)]
        public void ReportsEveryAllocationReason(int living, int saving, int invest, AllocationViolation reason)
        {
            var result = Replayer.Replay(Normal, 7, new[] { new Allocation(living, saving, invest) });

            Assert.Equal(reason, result.Failure.AllocationReason);
        }

        [Fact]
        public void PartialHistoryIsNotFinishedAndHasNoEvaluation()
        {
            var outcome = Replayer.Replay(Normal, 42, Mixed).Outcome;

            Assert.False(outcome.IsFinished);
            Assert.Throws<InvalidOperationException>(() => outcome.Evaluation);
        }

        [Fact]
        public void FullCareerCompletesAndHasEvaluation()
        {
            var history = Enumerable.Repeat(new Allocation(100, 0, 0), 43).ToArray();

            var outcome = Replayer.Replay(Normal, 1, history).Outcome;

            Assert.True(outcome.IsFinished);
            Assert.Equal(43, outcome.Years.Count);
            Assert.Equal(GameRules.FinalAge, outcome.FinalState.Age);
            Assert.False(outcome.Evaluation.IsBankrupt);
            Assert.NotNull(outcome.Evaluation.Rank);
        }

        [Fact]
        public void TurnAfterCompletionIsRejectedAsGameFinished()
        {
            var history = Enumerable.Repeat(new Allocation(100, 0, 0), 44).ToArray();

            var result = Replayer.Replay(Normal, 1, history);

            Assert.False(result.IsValid);
            Assert.Equal(44, result.Failure.TurnNo);
            Assert.Equal(GameRules.FinalAge, result.Failure.Age);
            Assert.Equal(ReplayViolation.GameFinished, result.Failure.Violation);
        }

        [Fact]
        public void EmptyHistoryGivesTheStartingState()
        {
            var outcome = Replayer.Replay(Normal, 1, new Allocation[0]).Outcome;

            Assert.Empty(outcome.Years);
            Assert.Equal(GameRules.StartAge, outcome.FinalState.Age);
            Assert.False(outcome.IsFinished);
        }

        [Fact]
        public void RawHistoryWithAValueOutsideInt32ReportsItsTurnAndReason()
        {
            var history = new[] { new AllocationInput(60, 20, 20), InputOf("4294967296", "0", "0") };

            var result = Replayer.Replay(Normal, 7, history);

            Assert.False(result.IsValid);
            Assert.Equal(2, result.Failure.TurnNo);
            Assert.Equal(ReplayViolation.InvalidAllocation, result.Failure.Violation);
            Assert.Equal(AllocationViolation.StepMismatch, result.Failure.AllocationReason);
        }

        [Fact]
        public void RawHistoryOfValidValuesGivesTheSameResultAsAllocations()
        {
            var raw = Mixed.Select(allocation => (AllocationInput)allocation).ToArray();

            var fromRaw = Replayer.Replay(Normal, 42, raw).Outcome;
            var fromAllocations = Replayer.Replay(Normal, 42, Mixed).Outcome;

            Assert.Equal(fromAllocations.Years.Select(year => year.Next.SavingsYen), fromRaw.Years.Select(year => year.Next.SavingsYen));
            Assert.Equal(fromAllocations.FinalState.InvestmentYen, fromRaw.FinalState.InvestmentYen);
        }

        private static AllocationInput InputOf(string living, string saving, string invest) =>
            new AllocationInput(
                BigInteger.Parse(living, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                BigInteger.Parse(saving, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                BigInteger.Parse(invest, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));

        [Fact]
        public void ResultsRefuseTheWrongSide()
        {
            var valid = Replayer.Replay(Normal, 1, new Allocation[0]);
            var invalid = Replayer.Replay(Normal, 1, new[] { new Allocation(1, 1, 1) });

            Assert.Throws<InvalidOperationException>(() => valid.Failure);
            Assert.Throws<InvalidOperationException>(() => invalid.Outcome);
        }
    }
}
