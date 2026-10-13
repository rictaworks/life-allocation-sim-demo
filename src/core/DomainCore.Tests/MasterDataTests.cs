using System;
using System.Linq;
using DomainCore.Config;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// マスタ定数（requirements.md 3.1・3.2・3.3・4.2・4.4 の枠組み）。
    /// 数値は架空のモデル値なので、個々の値ではなく、仕様が決めている形（件数・年齢の区切り・コード・単位・大小関係）を確かめる。
    /// </summary>
    public class MasterDataTests
    {
        private const string MachineCodePattern = "^[a-z][a-z_]*[a-z]$";

        [Fact]
        public void GameRulesMatchSpecification()
        {
            Assert.Equal(22, GameRules.StartAge);
            Assert.Equal(65, GameRules.FinalAge);
            Assert.Equal(100, GameRules.AllocationTotalPct);
            Assert.Equal(5, GameRules.AllocationStepPct);
            Assert.Equal(10000, GameRules.BasisPointsPerUnit);
            Assert.Equal(0, GameRules.SatisfactionMin);
            Assert.Equal(100, GameRules.SatisfactionMax);
        }

        [Fact]
        public void LifeStagesAreFiveFixedAgeBands()
        {
            var bands = LifeStageTable.All.Select(stage => (stage.MinAge, stage.MaxAge)).ToArray();

            Assert.Equal(new[] { (22, 29), (30, 39), (40, 49), (50, 59), (60, 65) }, bands);
        }

        [Fact]
        public void LifeStagesCoverEveryAgeOnce()
        {
            for (var age = GameRules.StartAge; age <= GameRules.FinalAge; age++)
            {
                var matches = LifeStageTable.All.Count(stage => stage.MinAge <= age && age <= stage.MaxAge);
                Assert.True(matches == 1, $"{age} 歳にあてはまるライフステージが {matches} 件あります");
            }
        }

        [Fact]
        public void LifeStageLivingCostIsPositiveAndBelowIncome()
        {
            Assert.All(LifeStageTable.All, stage =>
            {
                Assert.True(stage.TakeHomeIncomeYen > 0);
                Assert.True(stage.BaseLivingCostYen > 0);
                Assert.True(stage.BaseLivingCostYen < stage.TakeHomeIncomeYen, $"{stage.MinAge}〜{stage.MaxAge} 歳の必要生活費が手取り年収以上です");
            });
        }

        [Fact]
        public void DifficultiesAreEasyNormalHard()
        {
            Assert.Equal(new[] { "easy", "normal", "hard" }, DifficultyTable.All.Select(difficulty => difficulty.Code).ToArray());
        }

        [Fact]
        public void DifficultyLabelsAreUniqueAndPresent()
        {
            var labels = DifficultyTable.All.Select(difficulty => difficulty.Label).ToArray();

            Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
            Assert.Equal(labels.Length, labels.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void HarderDifficultyHasWiderMarketSwing()
        {
            // 難易度は市場の振れ幅（requirements.md 3.2）。易しい順に振れ幅が大きくなる
            var volatilities = DifficultyTable.All.Select(difficulty => difficulty.VolatilityBp).ToArray();

            Assert.True(volatilities[0] > 0);
            Assert.True(volatilities[0] < volatilities[1] && volatilities[1] < volatilities[2]);
        }

        [Fact]
        public void DifficultyReturnsStayWithinOneHundredPercent()
        {
            Assert.All(DifficultyTable.All, difficulty =>
            {
                Assert.InRange(difficulty.MeanReturnBp, -GameRules.BasisPointsPerUnit + 1, GameRules.BasisPointsPerUnit - 1);
                Assert.True(difficulty.MeanReturnBp - difficulty.VolatilityBp > -GameRules.BasisPointsPerUnit);
            });
        }

        [Fact]
        public void RanksAreFiveFromTopToBottom()
        {
            Assert.Equal(new[] { "S", "A", "B", "C", "D" }, RankTable.All.Select(rank => rank.Code).ToArray());
        }

        [Fact]
        public void RankThresholdsDescendToZero()
        {
            var ranks = RankTable.All;

            for (var i = 1; i < ranks.Count; i++)
            {
                Assert.True(ranks[i].MinTotalAssetsYen < ranks[i - 1].MinTotalAssetsYen, $"{ranks[i].Code} の資産の下限が上のランク以上です");
                Assert.True(ranks[i].MinAverageSatisfaction <= ranks[i - 1].MinAverageSatisfaction, $"{ranks[i].Code} の満足度の下限が上のランクより高いです");
            }

            // 最下位には、どの結果もあてはまる（破綻も最下位になる。requirements.md 4.4-3）
            Assert.Equal(0, ranks[ranks.Count - 1].MinTotalAssetsYen);
            Assert.Equal(0, ranks[ranks.Count - 1].MinAverageSatisfaction);
        }

        [Fact]
        public void TopRankRequiresSatisfaction()
        {
            // 「生活費を削りすぎて満足度が低い高資産」は最上位にしない（requirements.md 4.4-4）
            Assert.True(RankTable.All[0].MinAverageSatisfaction > GameRules.SatisfactionMin);
            Assert.All(RankTable.All, rank => Assert.InRange(rank.MinAverageSatisfaction, GameRules.SatisfactionMin, GameRules.SatisfactionMax));
        }

        [Fact]
        public void LifeEventsAreTwelveOfEightExpenseTwoIncomeTwoMarket()
        {
            Assert.Equal(12, LifeEventTable.All.Count);
            Assert.Equal(8, LifeEventTable.All.Count(lifeEvent => lifeEvent.Category == LifeEventCategory.Expense));
            Assert.Equal(2, LifeEventTable.All.Count(lifeEvent => lifeEvent.Category == LifeEventCategory.Income));
            Assert.Equal(2, LifeEventTable.All.Count(lifeEvent => lifeEvent.Category == LifeEventCategory.Market));
        }

        [Fact]
        public void LifeEventCodesAndLabelsAreUnique()
        {
            var codes = LifeEventTable.All.Select(lifeEvent => lifeEvent.Code).ToArray();
            var labels = LifeEventTable.All.Select(lifeEvent => lifeEvent.Label).ToArray();

            Assert.All(codes, code => Assert.Matches(MachineCodePattern, code));
            Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
            Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(labels.Length, labels.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void LifeEventAgesAndProbabilitiesAreInRange()
        {
            Assert.All(LifeEventTable.All, lifeEvent =>
            {
                Assert.InRange(lifeEvent.MinAge, GameRules.StartAge, GameRules.FinalAge);
                Assert.InRange(lifeEvent.MaxAge, lifeEvent.MinAge, GameRules.FinalAge);
                Assert.InRange(lifeEvent.ProbabilityBp, 1, GameRules.BasisPointsPerUnit);
            });
        }

        [Fact]
        public void CandidateProbabilitiesFitInOneDrawAtEveryAge()
        {
            // 1 年に 0 件か 1 件を選べるように、どの年齢でも候補の確率の合計は 100% 以下
            for (var age = GameRules.StartAge; age <= GameRules.FinalAge; age++)
            {
                var total = LifeEventTable.All
                    .Where(lifeEvent => lifeEvent.MinAge <= age && age <= lifeEvent.MaxAge)
                    .Sum(lifeEvent => lifeEvent.ProbabilityBp);
                Assert.True(total <= GameRules.BasisPointsPerUnit, $"{age} 歳の候補の確率の合計が {total} です");
            }
        }

        [Fact]
        public void LifeEventAmountsMatchTheirEffect()
        {
            Assert.All(LifeEventTable.All, lifeEvent =>
            {
                if (lifeEvent.Effect == LifeEventEffect.MarketReturnOverride)
                {
                    Assert.Equal(0, lifeEvent.AmountYen);
                    Assert.NotEqual(0, lifeEvent.MarketReturnBp);
                }
                else
                {
                    Assert.True(lifeEvent.AmountYen > 0, $"{lifeEvent.Code} の金額が 0 以下です");
                    Assert.Equal(0, lifeEvent.MarketReturnBp);
                }
            });
        }

        [Theory]
        [InlineData(LifeEventEffect.Expense, LifeEventCategory.Expense)]
        [InlineData(LifeEventEffect.OneTimeIncome, LifeEventCategory.Income)]
        [InlineData(LifeEventEffect.IncomeRaise, LifeEventCategory.Income)]
        [InlineData(LifeEventEffect.MarketReturnOverride, LifeEventCategory.Market)]
        public void LifeEventCategoryFollowsEffect(LifeEventEffect effect, LifeEventCategory category)
        {
            var lifeEvent = new LifeEvent("sample", "見本", effect, 22, 65, false, 1, 0, 0);

            Assert.Equal(category, lifeEvent.Category);
        }

        [Fact]
        public void RejectsUndefinedLifeEventEffect()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LifeEvent("sample", "見本", (LifeEventEffect)0, 22, 65, false, 1, 0, 0));
        }

        [Fact]
        public void LifeEventsFollowExamplesInSpecification()
        {
            // requirements.md 4.2：結婚・出産は 25〜45 歳、家電故障は全年齢、介護は 50 歳以上。結婚は一度きり
            var marriage = Event("marriage");
            var childbirth = Event("childbirth");
            var appliance = Event("appliance_breakdown");
            var nursingCare = Event("nursing_care");

            Assert.Equal((25, 45), (marriage.MinAge, marriage.MaxAge));
            Assert.True(marriage.OccursOnce);
            Assert.Equal((25, 45), (childbirth.MinAge, childbirth.MaxAge));
            Assert.Equal((GameRules.StartAge, GameRules.FinalAge), (appliance.MinAge, appliance.MaxAge));
            Assert.Equal(50, nursingCare.MinAge);
            Assert.All(new[] { marriage, childbirth, appliance, nursingCare }, lifeEvent => Assert.Equal(LifeEventCategory.Expense, lifeEvent.Category));
        }

        [Fact]
        public void IncomeEventsIncludePayRaise()
        {
            // 手取り年収は昇給イベントで加算する（requirements.md 3.3）
            Assert.Equal(LifeEventEffect.IncomeRaise, Event("pay_raise").Effect);
            Assert.Equal(LifeEventEffect.OneTimeIncome, Event("windfall").Effect);
        }

        [Fact]
        public void MarketCrashAndBoomExceedNormalSwing()
        {
            // 暴落の年はリターンを大きなマイナスに置き換える（requirements.md 4.3-5）。どの難易度の通常の振れ幅よりも下にする
            var lowestNormal = DifficultyTable.All.Min(difficulty => difficulty.MeanReturnBp - difficulty.VolatilityBp);
            var highestNormal = DifficultyTable.All.Max(difficulty => difficulty.MeanReturnBp + difficulty.VolatilityBp);

            Assert.True(Event("market_crash").MarketReturnBp < lowestNormal);
            Assert.True(Event("market_crash").MarketReturnBp > -GameRules.BasisPointsPerUnit);
            Assert.True(Event("market_boom").MarketReturnBp > highestNormal);
        }

        [Fact]
        public void TablesCannotBeModified()
        {
            Assert.True(((System.Collections.IList)LifeStageTable.All).IsReadOnly);
            Assert.True(((System.Collections.IList)DifficultyTable.All).IsReadOnly);
            Assert.True(((System.Collections.IList)RankTable.All).IsReadOnly);
            Assert.True(((System.Collections.IList)LifeEventTable.All).IsReadOnly);
            Assert.True(((System.Collections.IList)GameRules.AdjusterInitialAbsorbOrder).IsReadOnly);
        }

        private static LifeEvent Event(string code) => LifeEventTable.All.Single(lifeEvent => lifeEvent.Code == code);
    }
}
