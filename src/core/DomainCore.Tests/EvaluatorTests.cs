using System;
using System.Globalization;
using System.Linq;
using DomainCore.Config;
using DomainCore.Tests.Support;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// requirements.md 4.4 evaluate：総資産・平均満足度・破綻・困窮年数・投資の売却年数を算出し、3 観点のコメントを定型文で組み立て、5 段階のランクを決める。
    /// 破綻は最下位。「生活費を削りすぎて満足度が低い高資産」は最上位にしない。
    /// </summary>
    public class EvaluatorTests
    {
        [Fact]
        public void TotalAssetsAreSavingsPlusInvestmentValuation()
        {
            var evaluation = Evaluator.Evaluate(Finished(savingsYen: 40_000_000, investmentYen: 5_000_000), Years(60, 70, 81));

            Assert.Equal(45_000_000, evaluation.TotalAssetsYen);
        }

        [Fact]
        public void AverageSatisfactionIsTheFlooredAverageOfEveryYear()
        {
            // (60 + 70 + 81) ÷ 3 = 70.33… は 70
            var evaluation = Evaluator.Evaluate(Finished(), Years(60, 70, 81));

            Assert.Equal(70, evaluation.AverageSatisfaction);
        }

        [Fact]
        public void CountsYearsWithSatisfactionBelowTwentyAsStruggling()
        {
            var evaluation = Evaluator.Evaluate(Finished(), Years(19, 20, 5));

            Assert.Equal(2, evaluation.StruggleYears);
        }

        [Fact]
        public void CountsYearsWhereInvestmentWasSold()
        {
            var history = new[] { Year(60), Year(60, withdrawnFromInvestmentYen: 100), Year(60), Year(60, withdrawnFromInvestmentYen: 5) };

            var evaluation = Evaluator.Evaluate(Finished(), history);

            Assert.Equal(2, evaluation.InvestmentSoldYears);
        }

        [Fact]
        public void BankruptcyIsLowestRankWhateverTheAssets()
        {
            var evaluation = Evaluator.Evaluate(Finished(savingsYen: 50_000_000, status: PlayerStatus.Bankrupt), Years(90));

            Assert.True(evaluation.IsBankrupt);
            Assert.Equal("D", evaluation.Rank.Code);
        }

        [Theory]
        [InlineData(60_000_000, 60, "S")]
        [InlineData(59_999_999, 60, "A")]
        [InlineData(40_000_000, 40, "A")]
        [InlineData(39_999_999, 40, "B")]
        [InlineData(20_000_000, 0, "B")]
        [InlineData(5_000_000, 0, "C")]
        [InlineData(4_999_999, 0, "D")]
        [InlineData(0, 0, "D")]
        public void RankIsTheHighestOneWhoseAssetAndSatisfactionBothReachTheLine(long totalAssetsYen, int averageSatisfaction, string expected)
        {
            var evaluation = Evaluator.Evaluate(Finished(savingsYen: totalAssetsYen), Years(averageSatisfaction));

            Assert.Equal(expected, evaluation.Rank.Code);
        }

        [Fact]
        public void HighAssetsWithLowSatisfactionDoNotReachTheTopRank()
        {
            // 総資産 70,000,000 円でも、平均満足度 30 では S（60 以上が必要）にも A（40 以上が必要）にもならない
            var evaluation = Evaluator.Evaluate(Finished(savingsYen: 70_000_000), Years(30));

            Assert.Equal("B", evaluation.Rank.Code);
        }

        [Fact]
        public void RetirementCommentSaysAboveTargetWithAmounts()
        {
            var evaluation = Evaluator.Evaluate(Finished(savingsYen: 30_000_000), Years(60));

            Assert.Equal(Fmt(EvaluationTexts.RetirementAboveTarget, "30,000,000", "30,000,000"), evaluation.RetirementComment);
        }

        [Fact]
        public void RetirementCommentSaysBelowTargetWithAmounts()
        {
            var evaluation = Evaluator.Evaluate(Finished(savingsYen: 29_999_999), Years(60));

            Assert.Equal(Fmt(EvaluationTexts.RetirementBelowTarget, "29,999,999", "30,000,000"), evaluation.RetirementComment);
        }

        [Fact]
        public void RetirementCommentForBankruptcyDoesNotCompareAmounts()
        {
            var evaluation = Evaluator.Evaluate(Finished(status: PlayerStatus.Bankrupt), Years(10));

            Assert.Equal(EvaluationTexts.RetirementBankrupt, evaluation.RetirementComment);
        }

        [Theory]
        [InlineData(60, "high")]
        [InlineData(59, "middle")]
        [InlineData(40, "middle")]
        [InlineData(39, "low")]
        public void QualityCommentFollowsAverageSatisfactionLevel(int averageSatisfaction, string level)
        {
            var evaluation = Evaluator.Evaluate(Finished(), Years(averageSatisfaction));

            var expected = level == "high" ? EvaluationTexts.QualityHigh : level == "middle" ? EvaluationTexts.QualityMiddle : EvaluationTexts.QualityLow;
            Assert.Equal(expected, evaluation.QualityComment);
        }

        [Fact]
        public void QualityCommentAddsTheNumberOfStruggleYears()
        {
            // 平均 26（80 ÷ 3 の切り捨て）は「低め」。困窮の年が 2 年あったと付け足す
            var evaluation = Evaluator.Evaluate(Finished(), Years(60, 10, 10));

            Assert.Equal(EvaluationTexts.QualityLow + Fmt(EvaluationTexts.QualityStruggleYears, 2), evaluation.QualityComment);
        }

        [Fact]
        public void QualityCommentSaysSavingIsNotTheOnlyAnswerForHighAssetsWithLowSatisfaction()
        {
            var evaluation = Evaluator.Evaluate(Finished(savingsYen: 35_000_000), Years(30));

            Assert.Equal(EvaluationTexts.QualityLow + EvaluationTexts.QualityHighAssetsLowSatisfaction, evaluation.QualityComment);
        }

        [Fact]
        public void PreparednessCommentWithoutAnySale()
        {
            var evaluation = Evaluator.Evaluate(Finished(), Years(60, 60));

            Assert.Equal(EvaluationTexts.PreparednessNoSale, evaluation.PreparednessComment);
        }

        [Fact]
        public void PreparednessCommentCountsYearsWithInvestmentSales()
        {
            var history = new[] { Year(60, 1), Year(60, 2), Year(60), Year(60, 3) };

            var evaluation = Evaluator.Evaluate(Finished(), history);

            Assert.Equal(Fmt(EvaluationTexts.PreparednessSold, 3), evaluation.PreparednessComment);
        }

        [Fact]
        public void RefusesGameThatIsStillRunning()
        {
            Assert.Throws<InvalidOperationException>(() => Evaluator.Evaluate(GameFixtures.State(age: 40), Years(60)));
        }

        [Fact]
        public void RefusesEmptyHistory()
        {
            Assert.Throws<ArgumentException>(() => Evaluator.Evaluate(Finished(), new YearResult[0]));
        }

        private static PlayerState Finished(long savingsYen = 0, long investmentYen = 0, PlayerStatus status = PlayerStatus.Completed) =>
            GameFixtures.State(age: GameRules.FinalAge, savingsYen: savingsYen, investmentYen: investmentYen, status: status);

        private static YearResult Year(int satisfaction, long withdrawnFromInvestmentYen = 0) =>
            new YearResult
            {
                Next = GameFixtures.State(age: 30, satisfaction: satisfaction),
                WithdrawnFromInvestmentYen = withdrawnFromInvestmentYen,
            };

        private static YearResult[] Years(params int[] satisfactions) => satisfactions.Select(satisfaction => Year(satisfaction)).ToArray();

        private static string Fmt(string template, params object[] args) => string.Format(CultureInfo.InvariantCulture, template, args);
    }
}
