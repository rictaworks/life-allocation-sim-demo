using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// requirements.md 4.1 validateAllocation：0 以上・5 の倍数・合計 100。満たさなければ理由（負値・刻み違反・合計不一致）を返す。
    /// 理由が複数あてはまるときは、負値 → 刻み違反 → 合計不一致 の順に、先の理由を返す。
    /// </summary>
    public class AllocationValidationTests
    {
        private const int Step = 5;
        private const int Total = 100;

        [Theory]
        [InlineData(100, 0, 0)]
        [InlineData(0, 100, 0)]
        [InlineData(0, 0, 100)]
        [InlineData(70, 20, 10)]
        [InlineData(35, 30, 35)]
        [InlineData(5, 90, 5)]
        public void AcceptsStepValuesThatSumToHundred(int living, int saving, int invest)
        {
            var allocation = new Allocation(living, saving, invest);

            var result = Simulator.ValidateAllocation(allocation);

            Assert.True(result.IsValid);
            Assert.Equal(allocation, result.Value);
        }

        [Theory]
        [InlineData(-5, 55, 50)]
        [InlineData(50, -5, 55)]
        [InlineData(50, 55, -5)]
        [InlineData(-3, 50, 53)]
        [InlineData(-5, 0, 0)]
        [InlineData(int.MinValue, 0, 100)]
        public void RejectsNegativeValuesBeforeOtherReasons(int living, int saving, int invest)
        {
            var result = Simulator.ValidateAllocation(new Allocation(living, saving, invest));

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.Negative, result.Reason);
        }

        [Theory]
        [InlineData(33, 33, 34)]
        [InlineData(52, 28, 20)]
        [InlineData(1, 99, 0)]
        [InlineData(3, 0, 0)]
        [InlineData(50, 3, 0)]
        [InlineData(0, 50, 7)]
        [InlineData(int.MaxValue, 0, 0)]
        public void RejectsValuesOffTheStepBeforeSum(int living, int saving, int invest)
        {
            var result = Simulator.ValidateAllocation(new Allocation(living, saving, invest));

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.StepMismatch, result.Reason);
        }

        [Theory]
        [InlineData(50, 30, 15)]
        [InlineData(50, 30, 25)]
        [InlineData(0, 0, 0)]
        [InlineData(105, 0, 0)]
        [InlineData(100, 100, 100)]
        [InlineData(2147483645, 2147483645, 2147483645)]
        public void RejectsSumOtherThanHundred(int living, int saving, int invest)
        {
            // 100 を超える値は、他が 0 以上なら必ず合計不一致になる。合計は long で計算するため、桁あふれで 100 にならない
            var result = Simulator.ValidateAllocation(new Allocation(living, saving, invest));

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.SumMismatch, result.Reason);
        }

        [Fact]
        public void DefaultAllocationIsRejected()
        {
            var result = Simulator.ValidateAllocation(default);

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.SumMismatch, result.Reason);
        }

        [Fact]
        public void ClassifiesEveryStepCombinationBySum()
        {
            // 0〜100 の 5 刻みの全組み合わせ（21 × 21 × 21）。合計 100 なら正常、それ以外は合計不一致
            var validCount = 0;
            for (var living = 0; living <= Total; living += Step)
            {
                for (var saving = 0; saving <= Total; saving += Step)
                {
                    for (var invest = 0; invest <= Total; invest += Step)
                    {
                        var result = Simulator.ValidateAllocation(new Allocation(living, saving, invest));
                        if (living + saving + invest == Total)
                        {
                            Assert.True(result.IsValid);
                            validCount++;
                        }
                        else
                        {
                            Assert.False(result.IsValid);
                            Assert.Equal(AllocationViolation.SumMismatch, result.Reason);
                        }
                    }
                }
            }

            Assert.Equal(231, validCount);
        }

        [Fact]
        public void AllocationExposesEachItemByKind()
        {
            var allocation = new Allocation(60, 25, 15);

            Assert.Equal(60, allocation.LivingPct);
            Assert.Equal(25, allocation.SavingPct);
            Assert.Equal(15, allocation.InvestPct);
            Assert.Equal(60, allocation.Get(AllocationItem.Living));
            Assert.Equal(25, allocation.Get(AllocationItem.Saving));
            Assert.Equal(15, allocation.Get(AllocationItem.Invest));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => allocation.Get((AllocationItem)0));
        }

        [Fact]
        public void AllocationsWithSameValuesAreEqual()
        {
            var a = new Allocation(60, 25, 15);
            var b = new Allocation(60, 25, 15);
            var c = new Allocation(60, 15, 25);

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a, c);
            Assert.True(a != c);
        }
    }
}
