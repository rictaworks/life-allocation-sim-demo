using System.Globalization;
using System.Numerics;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// 入力の配分（AllocationInput。任意の桁数の整数）の検証。規則は requirements.md 4.1 と同じ（AllocationValidationTests と揃える）。
    /// int の範囲の外の値も、桁あふれや例外にせず、負値 → 刻み違反 → 合計不一致 の順で理由を返す。
    /// </summary>
    public class AllocationInputValidationTests
    {
        [Theory]
        [InlineData(100, 0, 0)]
        [InlineData(70, 20, 10)]
        [InlineData(35, 30, 35)]
        public void AcceptsValidValuesAndReturnsThemAsAllocation(int living, int saving, int invest)
        {
            var result = Simulator.ValidateAllocationInput(new AllocationInput(living, saving, invest));

            Assert.True(result.IsValid);
            Assert.Equal(new Allocation(living, saving, invest), result.Value);
        }

        [Theory]
        [InlineData("-2147483649", "0", "102")]
        [InlineData("0", "-4294967295", "104")]
        [InlineData("-10000000000000000000000000000000", "0", "100")]
        public void RejectsValuesBelowInt32AsNegative(string living, string saving, string invest)
        {
            var result = Simulator.ValidateAllocationInput(Input(living, saving, invest));

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.Negative, result.Reason);
        }

        [Theory]
        [InlineData("4294967296", "0", "0")]
        [InlineData("2147483648", "0", "0")]
        [InlineData("4294967296", "5", "95")]
        public void RejectsOffStepValuesOutsideInt32AsStepMismatch(string living, string saving, string invest)
        {
            // 刻み違反は合計不一致より先に判定する（4294967296 は 5 の倍数でない）
            var result = Simulator.ValidateAllocationInput(Input(living, saving, invest));

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.StepMismatch, result.Reason);
        }

        [Theory]
        [InlineData("4294967295", "0", "0")]
        [InlineData("1000000000000000000000000000000", "0", "0")]
        [InlineData("100", "4294967295", "0")]
        public void RejectsOnStepValuesOutsideInt32BySum(string living, string saving, string invest)
        {
            // 4294967295 は 5 の倍数だが、合計が 100 でない
            var result = Simulator.ValidateAllocationInput(Input(living, saving, invest));

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.SumMismatch, result.Reason);
        }

        [Fact]
        public void AgreesWithTheIntegerValidationForValuesNearTheRange()
        {
            var values = new[] { -5, -3, 0, 3, 5, 50, 95, 100, 102, 105 };

            foreach (var living in values)
            {
                foreach (var saving in values)
                {
                    foreach (var invest in values)
                    {
                        var viaAllocation = Simulator.ValidateAllocation(new Allocation(living, saving, invest));
                        var viaInput = Simulator.ValidateAllocationInput(new AllocationInput(living, saving, invest));

                        Assert.Equal(viaAllocation.IsValid, viaInput.IsValid);
                        if (viaAllocation.IsValid)
                        {
                            Assert.Equal(viaAllocation.Value, viaInput.Value);
                        }
                        else
                        {
                            Assert.Equal(viaAllocation.Reason, viaInput.Reason);
                        }
                    }
                }
            }
        }

        [Fact]
        public void AllocationConvertsToAnInputWithTheSameValues()
        {
            AllocationInput input = new Allocation(60, 25, 15);

            Assert.Equal(new BigInteger(60), input.Living);
            Assert.Equal(new BigInteger(25), input.Saving);
            Assert.Equal(new BigInteger(15), input.Invest);
        }

        private static AllocationInput Input(string living, string saving, string invest) =>
            new AllocationInput(Parse(living), Parse(saving), Parse(invest));

        private static BigInteger Parse(string text) =>
            BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }
}
