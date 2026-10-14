using System;
using System.Linq;
using DomainCore.Config;
using DomainCore.Tests.Support;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// requirements.md 4.3 advanceYear（1 年の精算）。処理順は固定：1 配分・2 生活費判定・3 積立・4 イベント・5 運用・6 取り崩し・7 物価・8 状態判定・9 結果。
    /// 各ステップは Settle（イベントと市場の値を引数で渡す）で単体に確かめ、乱数の使い方は Advance で確かめる。
    /// 金額の期待値は、手で計算した値（例：手取り 2,800,000 円 × 65% は 1,820,000 円）。
    /// </summary>
    public class YearSettlementTests
    {
        private static readonly Difficulty Normal = GameFixtures.DifficultyNamed("normal");

        [Fact]
        public void SplitsIncomeByAllocationAndGivesRoundingRemainderToSaving()
        {
            // 1,000,001 円：生活費 65% = 650,000（切り捨て）、投資 20% = 200,000（切り捨て）、貯蓄枠は残りの 150,001（端数を含む）
            var state = GameFixtures.State(age: 30, incomeYen: 1_000_001);

            var year = YearSettlement.Settle(state, new Allocation(65, 15, 20), Normal, EventDraw.None(), 0);

            Assert.Equal(650_000, year.LivingBudgetYen);
            Assert.Equal(200_000, year.InvestDepositYen);
            Assert.Equal(150_001, year.SavingDepositYen);
            Assert.Equal(1_000_001, year.LivingBudgetYen + year.InvestDepositYen + year.SavingDepositYen);
        }

        [Fact]
        public void LivingCostComesFromStageBaseTimesPriceIndex()
        {
            // 22〜29 歳の基準 2,000,000 円。物価係数 1.01（10100）なら、必要生活費は 2,020,000 円
            var state = GameFixtures.State(age: 22, priceIndexBp: 10100);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(2_020_000, year.RequiredLivingYen);
        }

        [Fact]
        public void SurplusGoesToSavingAndRaisesSatisfaction()
        {
            // 生活費枠 2,800,000 円、必要生活費 2,000,000 円：余剰 800,000 円は貯蓄へ。充足は 140%、満足度は +8（60 → 68）
            var year = YearSettlement.Settle(GameFixtures.State(age: 22), new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(800_000, year.SurplusYen);
            Assert.Equal(800_000, year.SavingDepositYen);
            Assert.Equal(68, year.Next.Satisfaction);
        }

        [Fact]
        public void SatisfactionFromSurplusStopsAtOneAndAHalfTimesRequiredButSurplusIsKept()
        {
            // 生活費枠 4,000,000 円（必要額の 2 倍）：満足度に効くのは 1.5 倍まで（+10 → 70）。余剰 2,000,000 円は全部貯蓄へ
            var state = GameFixtures.State(age: 22, incomeYen: 4_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(2_000_000, year.SurplusYen);
            Assert.Equal(2_000_000, year.SavingDepositYen);
            Assert.Equal(70, year.Next.Satisfaction);
        }

        [Theory]
        [InlineData(2_000_000L, 2_000_000L, 0)]
        [InlineData(0L, 2_000_000L, -20)]
        [InlineData(1_000_000L, 2_000_000L, -10)]
        [InlineData(2_400_000L, 2_000_000L, 4)]
        [InlineData(3_000_000L, 2_000_000L, 10)]
        [InlineData(9_000_000L, 2_000_000L, 10)]
        public void SatisfactionChangeIsTwoPointsPerTenPercentOfGapCappedAtOneAndAHalf(long livingYen, long requiredYen, int expected)
        {
            Assert.Equal(expected, YearSettlement.SatisfactionChange(livingYen, requiredYen));
        }

        [Theory]
        [InlineData(1_999_999L)]
        [InlineData(1_990_000L)]
        [InlineData(1_947_500L)]
        public void ShortfallUnderFivePercentStillLowersSatisfaction(long livingYen)
        {
            // 充足の差が 5% 未満でも、不足なら満足度は下がる（4.3-2）。増減の大きさは仕様側で決める
            Assert.True(YearSettlement.SatisfactionChange(livingYen, 2_000_000) < 0);
        }

        [Theory]
        [InlineData(2_010_000L)]
        [InlineData(2_052_000L)]
        public void SurplusUnderFivePercentStillRaisesSatisfaction(long livingYen)
        {
            // 充足の差が 5% 未満でも、余剰なら満足度は上がる（4.3-2）。増減の大きさは仕様側で決める
            Assert.True(YearSettlement.SatisfactionChange(livingYen, 2_000_000) > 0);
        }

        [Theory]
        [InlineData(2_000_001L, 1)]
        [InlineData(2_000_100L, 1)]
        [InlineData(2_000_199L, 1)]
        [InlineData(1_999_900L, -1)]
        [InlineData(1_999_999L, -1)]
        public void GapUnderOneHundredthOfRequiredStillMovesSatisfactionBySignOfTheGap(long livingYen, int expected)
        {
            // 必要額の 0.01% 未満（2,000,000 円なら 200 円未満）の差は、比率の切り捨てで 0 になる。差の符号に合わせて ±1 点にする（4.3-2）
            Assert.Equal(expected, YearSettlement.SatisfactionChange(livingYen, 2_000_000));
        }

        [Fact]
        public void ShortfallOfFiftyTwoThousandYenLowersSatisfactionInTheYear()
        {
            // 生活費枠 1,947,500 円、必要生活費 2,000,000 円（不足 52,500 円、充足 97.4%）。満足度は 60 から下がる
            var state = GameFixtures.State(age: 22, incomeYen: 1_947_500, savingsYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(52_500, year.LivingShortfallYen);
            Assert.True(year.Next.Satisfaction < 60);
        }

        [Fact]
        public void SurplusOfFiftyTwoThousandYenRaisesSatisfactionInTheYear()
        {
            // 生活費枠 2,052,000 円、必要生活費 2,000,000 円（余剰 52,000 円、充足 102.6%）。満足度は 60 から上がる
            var state = GameFixtures.State(age: 22, incomeYen: 2_052_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(52_000, year.SurplusYen);
            Assert.True(year.Next.Satisfaction > 60);
        }

        [Fact]
        public void SurplusOfOneHundredYenRaisesSatisfactionInTheYear()
        {
            // 生活費枠 2,000,100 円、必要生活費 2,000,000 円（余剰 100 円）。比率は切り捨てで 100% になるが、満足度は 60 から 61 に上がる
            var state = GameFixtures.State(age: 22, incomeYen: 2_000_100);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(100, year.SurplusYen);
            Assert.Equal(61, year.Next.Satisfaction);
        }

        [Fact]
        public void ShortfallIsWithdrawnFromSavingsFirstAndLowersSatisfaction()
        {
            // 生活費枠 1,400,000 円、必要 2,000,000 円：不足 600,000 円。充足 70%で満足度 −6（60 → 54）。
            // 貯蓄枠 1,400,000 円に利息 1,400 円（0.1%）を付け、不足を貯蓄から取り崩すと残りは 801,400 円
            var state = GameFixtures.State(age: 22);

            var year = YearSettlement.Settle(state, new Allocation(50, 50, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(600_000, year.LivingShortfallYen);
            Assert.Equal(600_000, year.WithdrawnFromSavingsYen);
            Assert.Equal(0, year.WithdrawnFromInvestmentYen);
            Assert.Equal(0, year.UnmetYen);
            Assert.Equal(801_400, year.Next.SavingsYen);
            Assert.Equal(54, year.Next.Satisfaction);
            Assert.Equal(PlayerStatus.Playing, year.Next.Status);
        }

        [Fact]
        public void ZeroLivingAllocationAlwaysCausesAShortfall()
        {
            // 生活費 0% は受け付ける。その年は必要生活費の全額が不足になり、満足度は −20（60 → 40）
            var year = YearSettlement.Settle(GameFixtures.State(age: 22), new Allocation(0, 100, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(2_000_000, year.LivingShortfallYen);
            Assert.Equal(40, year.Next.Satisfaction);
            Assert.Equal(802_800, year.Next.SavingsYen);
        }

        [Fact]
        public void ZeroAllocationsAreAccepted()
        {
            var state = GameFixtures.State(age: 22);

            Assert.True(YearSettlement.Advance(state, new Allocation(70, 30, 0), Normal, new SeededRandom(1)).IsValid);
            Assert.True(YearSettlement.Advance(state, new Allocation(0, 0, 100), Normal, new SeededRandom(1)).IsValid);
            Assert.True(YearSettlement.Advance(state, new Allocation(100, 0, 0), Normal, new SeededRandom(1)).IsValid);
        }

        [Fact]
        public void ExpenseIsPaidFromSavingsThenTheRestFromInvestment()
        {
            // 出費 500,000 円（出産）：貯蓄 100,000 円から払い、残り 400,000 円は投資を売却（評価額どおり）
            var state = GameFixtures.State(age: 22, incomeYen: 2_000_000, savingsYen: 100_000, investmentYen: 500_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.Of(GameFixtures.EventNamed("childbirth")), 0);

            Assert.Equal(500_000, year.EventExpenseYen);
            Assert.Equal(400_000, year.ShortfallYen);
            Assert.Equal(0, year.WithdrawnFromSavingsYen);
            Assert.Equal(400_000, year.WithdrawnFromInvestmentYen);
            Assert.Equal(0, year.Next.SavingsYen);
            Assert.Equal(100_000, year.Next.InvestmentYen);
            Assert.Equal(0, year.UnmetYen);
        }

        [Fact]
        public void OneTimeIncomeIsAddedToSavings()
        {
            var year = YearSettlement.Settle(
                GameFixtures.State(age: 22, incomeYen: 2_000_000),
                new Allocation(100, 0, 0),
                Normal,
                EventDraw.Of(GameFixtures.EventNamed("windfall")),
                0);

            Assert.Equal(300_000, year.EventIncomeYen);
            Assert.Equal(300_300, year.Next.SavingsYen);
        }

        [Fact]
        public void PayRaiseAddsToNextYearIncomeNotThisYear()
        {
            var state = GameFixtures.State(age: 22, incomeYen: 2_800_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.Of(GameFixtures.EventNamed("pay_raise")), 0);

            Assert.Equal(2_800_000, year.LivingBudgetYen);
            Assert.Equal(200_000, year.Next.RaiseTotalYen);
            Assert.Equal(LifeStage.ForAge(23).TakeHomeIncomeYen + 200_000, year.Next.IncomeYen);
            Assert.Equal(200_000, year.EventIncomeYen);
        }

        [Fact]
        public void MarketCrashReplacesDrawnReturnAndLosesValue()
        {
            // 投資 1,000,000 円、暴落の年は −30%（−3000）。抽選の値（+1000）は使わない
            var state = GameFixtures.State(age: 22, incomeYen: 2_000_000, investmentYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.Of(GameFixtures.EventNamed("market_crash")), 1000);

            Assert.Equal(-3000, year.MarketReturnBp);
            Assert.Equal(-300_000, year.InvestmentGainYen);
            Assert.Equal(700_000, year.Next.InvestmentYen);
        }

        [Fact]
        public void MarketBoomReplacesDrawnReturn()
        {
            var state = GameFixtures.State(age: 22, incomeYen: 2_000_000, investmentYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.Of(GameFixtures.EventNamed("market_boom")), -1000);

            Assert.Equal(2500, year.MarketReturnBp);
            Assert.Equal(1_250_000, year.Next.InvestmentYen);
        }

        [Fact]
        public void WithoutMarketEventTheDrawnReturnApplies()
        {
            var state = GameFixtures.State(age: 22, incomeYen: 2_000_000, investmentYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 400);

            Assert.Equal(400, year.MarketReturnBp);
            Assert.Equal(40_000, year.InvestmentGainYen);
            Assert.Equal(1_040_000, year.Next.InvestmentYen);
        }

        [Fact]
        public void InvestmentValueNeverGoesBelowZero()
        {
            var state = GameFixtures.State(age: 22, incomeYen: 2_000_000, investmentYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), -20000);

            Assert.Equal(0, year.Next.InvestmentYen);
            Assert.Equal(-1_000_000, year.InvestmentGainYen);
        }

        [Fact]
        public void SavingsEarnFixedInterestOnTheWholeBalance()
        {
            var state = GameFixtures.State(age: 22, incomeYen: 2_000_000, savingsYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(1_000, year.SavingsInterestYen);
            Assert.Equal(1_001_000, year.Next.SavingsYen);
        }

        [Fact]
        public void InvestmentIsSoldAtThisYearsValuationWhenSavingsRunOut()
        {
            // 不足 1,000,000 円。貯蓄はゼロ。投資は +10%（+1000）で 1,100,000 円に評価され、その評価額から 1,000,000 円を売る
            var state = GameFixtures.State(age: 22, incomeYen: 1_000_000, investmentYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 1000);

            Assert.Equal(1_000_000, year.WithdrawnFromInvestmentYen);
            Assert.Equal(100_000, year.Next.InvestmentYen);
            Assert.Equal(50, year.Next.Satisfaction);
            Assert.Equal(PlayerStatus.Playing, year.Next.Status);
        }

        [Fact]
        public void ShortfallThatSavingsAndInvestmentCannotCoverIsBankruptcy()
        {
            var state = GameFixtures.State(age: 22, incomeYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(1_000_000, year.UnmetYen);
            Assert.Equal(PlayerStatus.Bankrupt, year.Next.Status);
            Assert.True(year.Next.IsFinished);
            Assert.Equal(0, year.Next.SavingsYen);
            Assert.Equal(0, year.Next.InvestmentYen);
        }

        [Fact]
        public void ExpenseLargerThanIncomeWithNoAssetsIsBankruptcy()
        {
            // 出費 3,000,000 円（結婚）は年収 2,000,000 円を上回る。資産はゼロなので、3,000,000 円すべてが未充当になり破綻
            var state = GameFixtures.State(age: 25, incomeYen: 2_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.Of(GameFixtures.EventNamed("marriage")), 0);

            Assert.Equal(3_000_000, year.EventExpenseYen);
            Assert.Equal(3_000_000, year.UnmetYen);
            Assert.Equal(PlayerStatus.Bankrupt, year.Next.Status);
            Assert.True(year.Next.IsFinished);
        }

        [Fact]
        public void ExpenseLargerThanIncomeIsPaidFromSavingsThenInvestmentAndTheRestIsBankruptcy()
        {
            // 出費 3,000,000 円。貯蓄 1,000,000 円を払い、残り 2,000,000 円は投資 500,000 円を売っても足りず、未充当 1,500,000 円で破綻
            var state = GameFixtures.State(age: 25, incomeYen: 2_000_000, savingsYen: 1_000_000, investmentYen: 500_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.Of(GameFixtures.EventNamed("marriage")), 0);

            Assert.Equal(2_000_000, year.ShortfallYen);
            Assert.Equal(0, year.WithdrawnFromSavingsYen);
            Assert.Equal(500_000, year.WithdrawnFromInvestmentYen);
            Assert.Equal(1_500_000, year.UnmetYen);
            Assert.Equal(PlayerStatus.Bankrupt, year.Next.Status);
        }

        [Fact]
        public void SatisfactionBelowTwentyIsStruggling()
        {
            // 満足度 25 で 0% の生活費：−20 で 5。貯蓄は残るので破綻ではなく生活困窮
            var state = GameFixtures.State(age: 22, incomeYen: 1_000_000, savingsYen: 10_000_000, satisfaction: 25);

            var year = YearSettlement.Settle(state, new Allocation(0, 100, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(5, year.Next.Satisfaction);
            Assert.Equal(PlayerStatus.Struggling, year.Next.Status);
            Assert.False(year.Next.IsFinished);
        }

        [Fact]
        public void StruggleIsNotStickyAndEndsWhenSatisfactionRecovers()
        {
            // 満足度 15 から、充足 200%（+10）で 25。20 以上なので、プレイ中に戻る
            var state = GameFixtures.State(age: 22, incomeYen: 4_000_000, satisfaction: 15);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(25, year.Next.Satisfaction);
            Assert.Equal(PlayerStatus.Playing, year.Next.Status);
        }

        [Fact]
        public void SatisfactionIsCappedAtOneHundred()
        {
            // 満足度 99 から、充足 200%（+10）。109 にはならず、100 に収まる
            var state = GameFixtures.State(age: 22, incomeYen: 4_000_000, satisfaction: 99);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(100, year.Next.Satisfaction);
            Assert.Equal(PlayerStatus.Playing, year.Next.Status);
        }

        [Fact]
        public void SatisfactionIsFlooredAtZeroAndTheYearIsStruggling()
        {
            // 満足度 5 で生活費 0%：−20 で −15 になるが、0 に収まる。貯蓄で不足は払えるので、破綻ではなく生活困窮
            var state = GameFixtures.State(age: 22, incomeYen: 1_000_000, savingsYen: 10_000_000, satisfaction: 5);

            var year = YearSettlement.Settle(state, new Allocation(0, 100, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(0, year.Next.Satisfaction);
            Assert.Equal(PlayerStatus.Struggling, year.Next.Status);
        }

        [Fact]
        public void ReachingAgeSixtyFiveCompletesEvenWhenStruggling()
        {
            // 64 歳の年の終わりに 65 歳。満足度は 25 から 5 になり困窮の条件でも、完走が優先される
            var state = GameFixtures.State(age: 64, incomeYen: 3_000_000, savingsYen: 10_000_000, satisfaction: 25);

            var year = YearSettlement.Settle(state, new Allocation(0, 100, 0), Normal, EventDraw.None(), 0);

            Assert.Equal(65, year.Next.Age);
            Assert.Equal(PlayerStatus.Completed, year.Next.Status);
            Assert.True(year.Next.IsFinished);
        }

        [Fact]
        public void BankruptcyTakesPriorityOverCompletionAtSixtyFive()
        {
            var state = GameFixtures.State(age: 64, incomeYen: 1_000_000);

            var year = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0);

            Assert.True(year.UnmetYen > 0);
            Assert.Equal(PlayerStatus.Bankrupt, year.Next.Status);
        }

        [Fact]
        public void PriceIndexRisesOnePercentEveryYearWithIntegerArithmetic()
        {
            // 10000 → 10100 → 10201 → 10303（各年の値は、前年の値 × 10100 ÷ 10000 を切り捨てた整数）
            var state = GameFixtures.State(age: 22, priceIndexBp: 10000);

            var first = YearSettlement.Settle(state, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0).Next;
            var second = YearSettlement.Settle(first, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0).Next;
            var third = YearSettlement.Settle(second, new Allocation(100, 0, 0), Normal, EventDraw.None(), 0).Next;

            Assert.Equal(10100, first.PriceIndexBp);
            Assert.Equal(10201, second.PriceIndexBp);
            Assert.Equal(10303, third.PriceIndexBp);
        }

        [Fact]
        public void AdvanceDrawsEventThenMarketReturnFromTheSameSeed()
        {
            // 乱数は 1 年に 2 回（イベントの抽選・市場のリターン）。同じ順で使うと、結果は同じになる
            var state = GameFixtures.State(age: 30, incomeYen: 3_000_000, investmentYen: 1_000_000);
            var random = new SeededRandom(77);
            var expectedRandom = new SeededRandom(77);
            var draw = EventDrawer.Draw(state, expectedRandom);
            var drawnReturn = YearSettlement.DrawReturnBp(Normal, expectedRandom);

            var result = YearSettlement.Advance(state, new Allocation(60, 20, 20), Normal, random);

            // 市場系のイベントの年は、抽選のリターンの代わりにイベントのリターンを使う
            var expectedReturn = draw.HasEvent && draw.Event.Effect == LifeEventEffect.MarketReturnOverride ? draw.Event.MarketReturnBp : drawnReturn;
            Assert.True(result.IsValid);
            Assert.Equal(CodeOrNull(draw), CodeOrNull(result.Value.Event));
            Assert.Equal(expectedReturn, result.Value.MarketReturnBp);
        }

        [Fact]
        public void InvalidAllocationIsRejectedWithoutDrawingRandomNumbers()
        {
            var random = new SeededRandom(9);

            var result = YearSettlement.Advance(GameFixtures.State(), new Allocation(50, 50, 50), Normal, random);

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.SumMismatch, result.Reason);
            Assert.Equal(new SeededRandom(9).NextUInt64(), random.NextUInt64());
        }

        [Fact]
        public void AdvanceRefusesAFinishedGame()
        {
            var finished = GameFixtures.State(age: 65, status: PlayerStatus.Completed);

            Assert.Throws<InvalidOperationException>(() => YearSettlement.Advance(finished, new Allocation(100, 0, 0), Normal, new SeededRandom(1)));
        }

        [Fact]
        public void FirstYearHasNoEventThroughAdvance()
        {
            for (var seed = 1L; seed <= 100; seed++)
            {
                var result = YearSettlement.Advance(GameFixtures.State(age: GameRules.StartAge), new Allocation(70, 20, 10), Normal, new SeededRandom(seed));

                Assert.False(result.Value.Event.HasEvent);
            }
        }

        [Fact]
        public void FortyThreeYearsOfSafeSavingReachAgeSixtyFiveAndComplete()
        {
            // 22 歳から 43 回の精算で 65 歳。生活費を 100% にすると、貯蓄が途中で尽きないので完走する
            var state = GameFixtures.State(age: GameRules.StartAge);
            var random = new SeededRandom(1);
            var years = 0;

            while (!state.IsFinished)
            {
                var result = YearSettlement.Advance(state, new Allocation(100, 0, 0), Normal, random);
                state = result.Value.Next;
                years++;
            }

            Assert.Equal(43, years);
            Assert.Equal(GameRules.FinalAge, state.Age);
            Assert.Equal(PlayerStatus.Completed, state.Status);
        }

        [Fact]
        public void SameSeedGivesSameCareer()
        {
            var first = Career(7);
            var second = Career(7);

            Assert.Equal(first, second);
        }

        [Fact]
        public void MarketReturnStaysWithinMeanPlusMinusVolatility()
        {
            var hard = GameFixtures.DifficultyNamed("hard");
            var random = new SeededRandom(5);
            var values = Enumerable.Range(0, 2000).Select(_ => YearSettlement.DrawReturnBp(hard, random)).ToArray();

            Assert.All(values, value => Assert.InRange(value, hard.MeanReturnBp - hard.VolatilityBp, hard.MeanReturnBp + hard.VolatilityBp));
            Assert.Contains(values, value => value < hard.MeanReturnBp);
            Assert.Contains(values, value => value > hard.MeanReturnBp);
        }

        private static string[] Career(long seed)
        {
            var state = GameFixtures.State(age: GameRules.StartAge);
            var random = new SeededRandom(seed);
            var log = new System.Collections.Generic.List<string>();

            while (!state.IsFinished)
            {
                var result = YearSettlement.Advance(state, new Allocation(60, 20, 20), Normal, random);
                state = result.Value.Next;
                log.Add($"{state.Age}:{state.SavingsYen}:{state.InvestmentYen}:{state.Satisfaction}:{state.Status}");
            }

            return log.ToArray();
        }

        private static string? CodeOrNull(EventDraw draw) => draw.HasEvent ? draw.Event.Code : null;
    }
}
