using System;
using System.Linq;
using DomainCore.AcceptanceTests.Support;
using Xunit;

namespace DomainCore.AcceptanceTests
{
    /// <summary>
    /// 受け入れ条件 1：Allocation（生活費・貯蓄・投資の整数の %）と ValidateAllocation（requirements.md 4.1）を作る。
    /// 結果は「正常」か「不正（理由は負値・刻み違反・合計不一致のいずれか）」。例外は使わない。
    /// </summary>
    public class Criterion1AllocationValidationTests
    {
        private const int RangeMin = -10;
        private const int RangeMax = 110;

        [Fact]
        public void AllocationHoldsThreeIntegerPercentages()
        {
            var allocation = new Allocation(10, 25, 65);

            Assert.Equal(10, allocation.LivingPct);
            Assert.Equal(25, allocation.SavingPct);
            Assert.Equal(65, allocation.InvestPct);
            Assert.Equal(10, allocation.Get(AllocationItem.Living));
            Assert.Equal(25, allocation.Get(AllocationItem.Saving));
            Assert.Equal(65, allocation.Get(AllocationItem.Invest));
            foreach (var name in new[] { "LivingPct", "SavingPct", "InvestPct" })
            {
                var property = typeof(Allocation).GetProperty(name);
                Assert.NotNull(property);
                Assert.Equal(typeof(int), property!.PropertyType);
            }
        }

        [Fact]
        public void InvalidReasonsAreNegativeStepMismatchAndSumMismatchOnly()
        {
            var names = Enum.GetNames(typeof(AllocationViolation)).OrderBy(name => name, StringComparer.Ordinal).ToArray();

            Assert.Equal(new[] { "Negative", "StepMismatch", "SumMismatch" }, names);
        }

        [Fact]
        public void ClassifiesEveryCombinationFromMinus10To110AsTheSpecificationSays()
        {
            // -10〜110 の 1 刻みの全組み合わせ（121^3 = 1,771,561 通り）。4.1 の条件（各項目が 0 以上 100 以下の 5 の倍数、合計がちょうど 100）で
            // 正常・不正を判定し、実装の判定と一致するかを確かめる。不正のときは、返した理由が実際にあてはまるかも確かめる
            var checkedCount = 0;
            var validCount = 0;
            for (var living = RangeMin; living <= RangeMax; living++)
            {
                for (var saving = RangeMin; saving <= RangeMax; saving++)
                {
                    for (var invest = RangeMin; invest <= RangeMax; invest++)
                    {
                        var allocation = new Allocation(living, saving, invest);
                        var result = Simulator.ValidateAllocation(allocation);
                        var expectedValid = IsValidBySpecification(living, saving, invest);
                        if (result.IsValid != expectedValid)
                        {
                            Assert.Fail($"{AllocationSpace.Describe(allocation)}：IsValid={result.IsValid}（4.1 の条件では {expectedValid}）");
                        }

                        if (expectedValid)
                        {
                            validCount++;
                            if (result.Value != allocation)
                            {
                                Assert.Fail($"{AllocationSpace.Describe(allocation)}：正常の結果の値が入力と異なります");
                            }
                        }
                        else
                        {
                            AssertReasonApplies(allocation, result.Reason);
                        }

                        checkedCount++;
                    }
                }
            }

            Assert.Equal(121 * 121 * 121, checkedCount);
            Assert.Equal(AllocationSpace.ValidAllocationCount, validCount);
        }

        [Theory]
        [InlineData(-5, 7, 98, AllocationViolation.Negative)]
        [InlineData(-5, 55, 40, AllocationViolation.Negative)]
        [InlineData(105, -5, 0, AllocationViolation.Negative)]
        [InlineData(3, 7, 90, AllocationViolation.StepMismatch)]
        [InlineData(3, 7, 80, AllocationViolation.StepMismatch)]
        [InlineData(50, 30, 15, AllocationViolation.SumMismatch)]
        [InlineData(105, 0, 0, AllocationViolation.SumMismatch)]
        public void ReturnsTheFirstReasonInTheOrderWrittenInThePr(int living, int saving, int invest, AllocationViolation expected)
        {
            // PR 本文の確認事項 4：理由が複数あてはまるときは 負値 → 刻み違反 → 合計不一致 の順に先の理由を 1 つ返す。
            // 100 を超える値は合計不一致になる（105/-5/0 は合計 100 だが負値）
            var result = Simulator.ValidateAllocation(new Allocation(living, saving, invest));

            Assert.False(result.IsValid);
            Assert.Equal(expected, result.Reason);
        }

        [Theory]
        [InlineData(int.MinValue, int.MinValue, int.MinValue)]
        [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
        [InlineData(2147483645, 2147483645, 2147483645)]
        [InlineData(int.MaxValue, int.MinValue, 100)]
        [InlineData(-2147483645, 2147483645, 100)]
        public void DoesNotThrowForExtremeValues(int living, int saving, int invest)
        {
            // 例外は使わない：int の範囲の端の値でも、例外ではなく「不正」を返す（int で足すと桁あふれする組み合わせを含む）
            ValidationResult<Allocation, AllocationViolation>? result = null;
            var exception = Record.Exception(() => { result = Simulator.ValidateAllocation(new Allocation(living, saving, invest)); });

            Assert.Null(exception);
            Assert.NotNull(result);
            Assert.False(result!.IsValid);
            AssertReasonApplies(new Allocation(living, saving, invest), result.Reason);
        }

        [Fact]
        public void ValidResultHasValueAndInvalidResultHasReason()
        {
            var valid = Simulator.ValidateAllocation(new Allocation(50, 30, 20));
            var invalid = Simulator.ValidateAllocation(new Allocation(50, 30, 15));

            Assert.True(valid.IsValid);
            Assert.Equal(new Allocation(50, 30, 20), valid.Value);
            Assert.Throws<InvalidOperationException>(() => valid.Reason);

            Assert.False(invalid.IsValid);
            Assert.Equal(AllocationViolation.SumMismatch, invalid.Reason);
            Assert.Throws<InvalidOperationException>(() => invalid.Value);
        }

        private static bool IsValidBySpecification(int living, int saving, int invest) =>
            IsPercentOnStep(living) && IsPercentOnStep(saving) && IsPercentOnStep(invest)
            && (long)living + saving + invest == AllocationSpace.Total;

        private static bool IsPercentOnStep(int pct) =>
            pct >= 0 && pct <= AllocationSpace.Total && pct % AllocationSpace.Step == 0;

        private static void AssertReasonApplies(Allocation allocation, AllocationViolation reason)
        {
            var values = new[] { allocation.LivingPct, allocation.SavingPct, allocation.InvestPct };
            var applies = reason switch
            {
                AllocationViolation.Negative => values.Any(value => value < 0),
                AllocationViolation.StepMismatch => values.Any(value => value % AllocationSpace.Step != 0),
                AllocationViolation.SumMismatch => values.Sum(value => (long)value) != AllocationSpace.Total,
                _ => false,
            };

            if (!applies)
            {
                Assert.Fail($"{AllocationSpace.Describe(allocation)}：返した理由 {reason} は、この配分にあてはまりません");
            }
        }
    }
}
