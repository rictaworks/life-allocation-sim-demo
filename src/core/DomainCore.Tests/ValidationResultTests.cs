using System;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// 検証の結果：正常なら値だけ、不正なら理由コードだけを読める。読めないものを読むと例外にする（既定値を返さない）。
    /// </summary>
    public class ValidationResultTests
    {
        [Fact]
        public void ValidResultHasValueButNoReason()
        {
            var allocation = new Allocation(70, 20, 10);

            var result = ValidationResult<Allocation, AllocationViolation>.Valid(allocation);

            Assert.True(result.IsValid);
            Assert.Equal(allocation, result.Value);
            Assert.Throws<InvalidOperationException>(() => result.Reason);
        }

        [Fact]
        public void InvalidResultHasReasonButNoValue()
        {
            var result = ValidationResult<Allocation, AllocationViolation>.Invalid(AllocationViolation.StepMismatch);

            Assert.False(result.IsValid);
            Assert.Equal(AllocationViolation.StepMismatch, result.Reason);
            Assert.Throws<InvalidOperationException>(() => result.Value);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(99)]
        public void RejectsUndefinedReason(int reason)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ValidationResult<Allocation, AllocationViolation>.Invalid((AllocationViolation)reason));
        }

        [Fact]
        public void ReasonCodesHaveStableNumbers()
        {
            // 理由コードは保存 API・再計算ホストに渡る。並べ替えで番号が変わらないように、番号を固定する
            Assert.Equal(1, (int)AllocationViolation.Negative);
            Assert.Equal(2, (int)AllocationViolation.StepMismatch);
            Assert.Equal(3, (int)AllocationViolation.SumMismatch);
            Assert.Equal(1, (int)DifficultyCodeViolation.Missing);
            Assert.Equal(2, (int)DifficultyCodeViolation.Unknown);
        }
    }
}
