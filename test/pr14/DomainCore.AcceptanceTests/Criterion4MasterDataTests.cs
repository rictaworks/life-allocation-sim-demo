using System;
using System.Collections;
using System.Linq;
using DomainCore.Config;
using Xunit;

namespace DomainCore.AcceptanceTests
{
    /// <summary>
    /// 受け入れ条件 4：マスタ定数（requirements.md 3.2・3.3・4.4 の枠組み）。
    /// ライフステージ 5 段階（手取り年収・必要生活費の基準）、難易度 3 段階（市場の平均と振れ幅を万分率の整数で。コード名は easy・normal・hard）、
    /// 評価ランク 5 段階（閾値）、ライフイベント 12 種（出費系 8・収入系 2・市場系 2。年齢の範囲・一度きりの有無・確率・金額）。
    /// 「PR の表と一致する」テストは、PR 本文の「マスタの値」の表と実装の値が同じであることを確かめる。
    /// </summary>
    public class Criterion4MasterDataTests
    {
        private const int StartAge = 22;
        private const int FinalAge = 65;
        private const int BasisPointsPerUnit = 10000;

        [Fact]
        public void LifeStagesAreTheFiveAgeRangesOfTheRequirements()
        {
            // requirements.md 3.2：22–29 / 30–39 / 40–49 / 50–59 / 60–65
            var ranges = LifeStageTable.All.Select(stage => (stage.MinAge, stage.MaxAge)).ToArray();

            Assert.Equal(new[] { (22, 29), (30, 39), (40, 49), (50, 59), (60, 65) }, ranges);
            Assert.All(LifeStageTable.All, stage =>
            {
                Assert.True(stage.TakeHomeIncomeYen > 0);
                Assert.True(stage.BaseLivingCostYen > 0);
            });
        }

        [Theory]
        [InlineData(0, 22, 29, 2_800_000L, 2_000_000L)]
        [InlineData(1, 30, 39, 3_600_000L, 2_500_000L)]
        [InlineData(2, 40, 49, 4_300_000L, 3_000_000L)]
        [InlineData(3, 50, 59, 4_600_000L, 3_200_000L)]
        [InlineData(4, 60, 65, 3_000_000L, 2_400_000L)]
        public void LifeStagesMatchThePrTable(int index, int minAge, int maxAge, long takeHomeIncomeYen, long baseLivingCostYen)
        {
            var stage = LifeStageTable.All[index];

            Assert.Equal(minAge, stage.MinAge);
            Assert.Equal(maxAge, stage.MaxAge);
            Assert.Equal(takeHomeIncomeYen, stage.TakeHomeIncomeYen);
            Assert.Equal(baseLivingCostYen, stage.BaseLivingCostYen);
        }

        [Fact]
        public void DifficultiesAreEasyNormalHardWithWideningVolatility()
        {
            Assert.Equal(new[] { "easy", "normal", "hard" }, DifficultyTable.All.Select(difficulty => difficulty.Code).ToArray());

            // 難易度は市場の振れ幅（requirements.md 3.2）。やさしいほど振れ幅が小さい
            var volatilities = DifficultyTable.All.Select(difficulty => difficulty.VolatilityBp).ToArray();
            Assert.True(volatilities[0] < volatilities[1] && volatilities[1] < volatilities[2], $"振れ幅が easy < normal < hard の順になっていません: {string.Join(", ", volatilities)}");
        }

        [Theory]
        [InlineData(0, "easy", "やさしい", 400, 500)]
        [InlineData(1, "normal", "ふつう", 400, 1200)]
        [InlineData(2, "hard", "むずかしい", 400, 2000)]
        public void DifficultiesMatchThePrTable(int index, string code, string label, int meanReturnBp, int volatilityBp)
        {
            var difficulty = DifficultyTable.All[index];

            Assert.Equal(code, difficulty.Code);
            Assert.Equal(label, difficulty.Label);
            Assert.Equal(meanReturnBp, difficulty.MeanReturnBp);
            Assert.Equal(volatilityBp, difficulty.VolatilityBp);
        }

        [Fact]
        public void RanksAreFiveLevelsWithThresholdsDescendingToZero()
        {
            var ranks = RankTable.All;

            Assert.Equal(5, ranks.Count);
            for (var i = 1; i < ranks.Count; i++)
            {
                Assert.True(ranks[i - 1].MinTotalAssetsYen >= ranks[i].MinTotalAssetsYen, $"{ranks[i - 1].Code} の総資産の下限が {ranks[i].Code} より低くなっています");
                Assert.True(ranks[i - 1].MinAverageSatisfaction >= ranks[i].MinAverageSatisfaction, $"{ranks[i - 1].Code} の満足度の下限が {ranks[i].Code} より低くなっています");
            }

            // 最下位は 0・0（どの結果もあてはまる。破綻は資産額によらず最下位：requirements.md 4.4-3）
            Assert.Equal(0L, ranks[ranks.Count - 1].MinTotalAssetsYen);
            Assert.Equal(0, ranks[ranks.Count - 1].MinAverageSatisfaction);

            // 最上位には満足度の下限がある（「満足度が低い高資産」を最上位にしない：requirements.md 4.4-4）
            Assert.True(ranks[0].MinAverageSatisfaction > 0);
        }

        [Theory]
        [InlineData(0, "S", 60_000_000L, 60)]
        [InlineData(1, "A", 40_000_000L, 40)]
        [InlineData(2, "B", 20_000_000L, 0)]
        [InlineData(3, "C", 5_000_000L, 0)]
        [InlineData(4, "D", 0L, 0)]
        public void RanksMatchThePrTable(int index, string code, long minTotalAssetsYen, int minAverageSatisfaction)
        {
            var rank = RankTable.All[index];

            Assert.Equal(code, rank.Code);
            Assert.Equal(minTotalAssetsYen, rank.MinTotalAssetsYen);
            Assert.Equal(minAverageSatisfaction, rank.MinAverageSatisfaction);
        }

        [Fact]
        public void LifeEventsAreTwelveWithEightExpenseTwoIncomeAndTwoMarket()
        {
            var events = LifeEventTable.All;

            Assert.Equal(12, events.Count);
            Assert.Equal(8, events.Count(lifeEvent => lifeEvent.Category == LifeEventCategory.Expense));
            Assert.Equal(2, events.Count(lifeEvent => lifeEvent.Category == LifeEventCategory.Income));
            Assert.Equal(2, events.Count(lifeEvent => lifeEvent.Category == LifeEventCategory.Market));
            Assert.Equal(events.Count, events.Select(lifeEvent => lifeEvent.Code).Distinct(StringComparer.Ordinal).Count());
        }

        [Theory]
        [InlineData(0, "marriage", "結婚", LifeEventEffect.Expense, 25, 45, true, 800, 3_000_000L, 0)]
        [InlineData(1, "childbirth", "出産", LifeEventEffect.Expense, 25, 45, false, 500, 500_000L, 0)]
        [InlineData(2, "appliance_breakdown", "家電の故障", LifeEventEffect.Expense, 22, 65, false, 600, 150_000L, 0)]
        [InlineData(3, "nursing_care", "親の介護", LifeEventEffect.Expense, 50, 65, false, 400, 1_000_000L, 0)]
        [InlineData(4, "illness", "病気やけが", LifeEventEffect.Expense, 22, 65, false, 300, 300_000L, 0)]
        [InlineData(5, "relocation", "引っ越し", LifeEventEffect.Expense, 22, 50, false, 300, 400_000L, 0)]
        [InlineData(6, "car_purchase", "車の買い替え", LifeEventEffect.Expense, 25, 60, false, 300, 2_000_000L, 0)]
        [InlineData(7, "home_repair", "住まいの修繕", LifeEventEffect.Expense, 35, 65, false, 300, 1_000_000L, 0)]
        [InlineData(8, "pay_raise", "昇給", LifeEventEffect.IncomeRaise, 25, 55, false, 1000, 200_000L, 0)]
        [InlineData(9, "windfall", "臨時収入", LifeEventEffect.OneTimeIncome, 22, 65, false, 300, 300_000L, 0)]
        [InlineData(10, "market_crash", "市場の暴落", LifeEventEffect.MarketReturnOverride, 22, 65, false, 300, 0L, -3000)]
        [InlineData(11, "market_boom", "市場の好況", LifeEventEffect.MarketReturnOverride, 22, 65, false, 300, 0L, 2500)]
        public void LifeEventsMatchThePrTable(
            int index, string code, string label, LifeEventEffect effect, int minAge, int maxAge, bool occursOnce, int probabilityBp, long amountYen, int marketReturnBp)
        {
            var lifeEvent = LifeEventTable.All[index];

            Assert.Equal(code, lifeEvent.Code);
            Assert.Equal(label, lifeEvent.Label);
            Assert.Equal(effect, lifeEvent.Effect);
            Assert.Equal(minAge, lifeEvent.MinAge);
            Assert.Equal(maxAge, lifeEvent.MaxAge);
            Assert.Equal(occursOnce, lifeEvent.OccursOnce);
            Assert.Equal(probabilityBp, lifeEvent.ProbabilityBp);
            Assert.Equal(amountYen, lifeEvent.AmountYen);
            Assert.Equal(marketReturnBp, lifeEvent.MarketReturnBp);
        }

        [Fact]
        public void LifeEventsFollowTheExamplesOfRequirements42()
        {
            // requirements.md 4.2-1：結婚・出産は 25〜45 歳、家電故障は全年齢、介護は 50 歳以上。4.2-2：結婚は一度きり
            var marriage = Event("marriage");
            var childbirth = Event("childbirth");
            var appliance = Event("appliance_breakdown");
            var nursing = Event("nursing_care");

            Assert.Equal((25, 45), (marriage.MinAge, marriage.MaxAge));
            Assert.Equal((25, 45), (childbirth.MinAge, childbirth.MaxAge));
            Assert.Equal((StartAge, FinalAge), (appliance.MinAge, appliance.MaxAge));
            Assert.Equal(50, nursing.MinAge);
            Assert.Equal(FinalAge, nursing.MaxAge);
            Assert.True(marriage.OccursOnce);
        }

        [Fact]
        public void LifeEventFieldsAreConsistentWithTheirCategory()
        {
            Assert.All(LifeEventTable.All, lifeEvent =>
            {
                Assert.InRange(lifeEvent.MinAge, StartAge, FinalAge);
                Assert.InRange(lifeEvent.MaxAge, lifeEvent.MinAge, FinalAge);
                Assert.InRange(lifeEvent.ProbabilityBp, 1, BasisPointsPerUnit);

                var expectedCategory = lifeEvent.Effect switch
                {
                    LifeEventEffect.Expense => LifeEventCategory.Expense,
                    LifeEventEffect.OneTimeIncome => LifeEventCategory.Income,
                    LifeEventEffect.IncomeRaise => LifeEventCategory.Income,
                    LifeEventEffect.MarketReturnOverride => LifeEventCategory.Market,
                    _ => throw new InvalidOperationException($"想定していない効果です: {lifeEvent.Effect}"),
                };
                Assert.Equal(expectedCategory, lifeEvent.Category);

                if (lifeEvent.Category == LifeEventCategory.Market)
                {
                    Assert.Equal(0L, lifeEvent.AmountYen);
                    Assert.NotEqual(0, lifeEvent.MarketReturnBp);
                }
                else
                {
                    Assert.True(lifeEvent.AmountYen > 0, $"{lifeEvent.Code} の金額が 0 以下です");
                    Assert.Equal(0, lifeEvent.MarketReturnBp);
                }
            });
        }

        [Fact]
        public void CandidateProbabilitiesFitInOneDrawAtEveryAge()
        {
            // PR 本文：どの年齢でも、候補の確率の合計は 10000 以下（#3 で 1 回の抽選から 0 件か 1 件を選べるように）
            for (var age = StartAge; age <= FinalAge; age++)
            {
                var total = LifeEventTable.All.Where(lifeEvent => lifeEvent.MinAge <= age && age <= lifeEvent.MaxAge).Sum(lifeEvent => lifeEvent.ProbabilityBp);
                Assert.True(total <= BasisPointsPerUnit, $"{age} 歳の候補の確率の合計が {total} です");
            }
        }

        [Fact]
        public void TablesCannotBeModified()
        {
            var tables = new IList[] { LifeStageTable.All, DifficultyTable.All, RankTable.All, LifeEventTable.All };

            Assert.All(tables, table =>
            {
                Assert.True(table.IsReadOnly);
                Assert.Throws<NotSupportedException>(() => table.Clear());
            });
        }

        private static LifeEvent Event(string code) =>
            LifeEventTable.All.Single(lifeEvent => string.Equals(lifeEvent.Code, code, StringComparison.Ordinal));
    }
}
