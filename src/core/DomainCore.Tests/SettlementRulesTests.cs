using DomainCore.Config;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// 年次精算の数値（Config の GameRules）が、仕様の決まりと大小関係を満たすこと。数値そのものは架空のモデル値。
    /// requirements.md 4.3：満足度の上限は必要額の 1.5 倍、困窮は 20 未満、物価は毎年わずかに上がる、貯蓄は低い固定金利。
    /// </summary>
    public class SettlementRulesTests
    {
        [Fact]
        public void SatisfactionCountsSurplusOnlyUpToOneAndAHalfTimesRequiredLiving()
        {
            Assert.Equal(GameRules.BasisPointsPerUnit * 3 / 2, GameRules.SatisfactionLivingCapBp);
        }

        [Fact]
        public void StruggleStartsBelowTwenty()
        {
            Assert.Equal(20, GameRules.StruggleSatisfactionThreshold);
        }

        [Fact]
        public void InitialSatisfactionIsInsideTheScale()
        {
            Assert.InRange(GameRules.InitialSatisfaction, GameRules.SatisfactionMin, GameRules.SatisfactionMax);
        }

        [Fact]
        public void InitialPriceIndexIsOneTimesBase()
        {
            Assert.Equal(GameRules.BasisPointsPerUnit, GameRules.InitialPriceIndexBp);
        }

        [Fact]
        public void PriceIndexRisesOnlySlightlyEachYear()
        {
            Assert.InRange(GameRules.InflationBp, 1, 300);
        }

        [Fact]
        public void SavingsEarnALowPositiveFixedRate()
        {
            Assert.InRange(GameRules.SavingsInterestBp, 1, 100);
        }

        [Fact]
        public void SatisfactionMovesInSmallStepsPerTenPercentOfGap()
        {
            Assert.Equal(1000, GameRules.BasisPointsPerTenPercent);
            Assert.InRange(GameRules.SatisfactionPointsPerTenPercent, 1, 10);
        }

        [Fact]
        public void RetirementTargetIsPositiveYen()
        {
            Assert.True(GameRules.RetirementTargetYen > 0);
        }
    }
}
