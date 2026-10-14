using System;
using System.Numerics;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 入力の検証（requirements.md 4.1・10 章のクラス図の Simulator）。副作用の無い純粋関数だけを持つ。
    /// 年次精算は YearSettlement（イベントの抽選は EventDrawer）、評価は Evaluator、配分履歴からの再現は Replayer が担う。
    /// </summary>
    public static class Simulator
    {
        /// <summary>
        /// 配分を検証する（requirements.md 4.1）。3 項目が 0 以上で 5 の倍数、合計がちょうど 100 なら正常。
        /// 不正なら理由を 1 つ返す。複数あてはまるときは、負値 → 刻み違反 → 合計不一致 の順に、先の理由を返す。
        /// 100 を超える項目は、他の項目が 0 以上なら合計が 100 を超えるため、合計不一致になる。
        /// </summary>
        public static ValidationResult<Allocation, AllocationViolation> ValidateAllocation(Allocation allocation) =>
            ValidateAllocationInput(allocation);

        /// <summary>
        /// 入力の配分を検証する（規則は ValidateAllocation と同じ）。値は任意の桁数の整数なので、int の範囲の外の値も例外にせず、理由で返す。
        /// 正常なときは、int の Allocation にして返す（合計が 100 で各値が 0 以上なので、各値は 0〜100 に収まり、変換で桁あふれしない）。
        /// </summary>
        public static ValidationResult<Allocation, AllocationViolation> ValidateAllocationInput(AllocationInput input)
        {
            if (input.Living < 0 || input.Saving < 0 || input.Invest < 0)
            {
                return ValidationResult<Allocation, AllocationViolation>.Invalid(AllocationViolation.Negative);
            }

            if (!IsOnStep(input.Living) || !IsOnStep(input.Saving) || !IsOnStep(input.Invest))
            {
                return ValidationResult<Allocation, AllocationViolation>.Invalid(AllocationViolation.StepMismatch);
            }

            // 任意の桁数で足す（BigInteger）。ここまでで各値は 0 以上なので、合計が 100 のときだけ、各値は 0〜100 に収まる
            var total = input.Living + input.Saving + input.Invest;
            if (total != GameRules.AllocationTotalPct)
            {
                return ValidationResult<Allocation, AllocationViolation>.Invalid(AllocationViolation.SumMismatch);
            }

            return ValidationResult<Allocation, AllocationViolation>.Valid(
                new Allocation((int)input.Living, (int)input.Saving, (int)input.Invest));
        }

        /// <summary>
        /// 難易度のコードを検証する。DifficultyTable のコード（easy・normal・hard）と完全に一致すれば、その難易度を返す。
        /// 大文字・小文字の違いや前後の空白も、一致とはみなさない。null・空文字列は未指定（Missing）、それ以外は不明（Unknown）。
        /// </summary>
        public static ValidationResult<Difficulty, DifficultyCodeViolation> ValidateDifficultyCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return ValidationResult<Difficulty, DifficultyCodeViolation>.Invalid(DifficultyCodeViolation.Missing);
            }

            foreach (var difficulty in DifficultyTable.All)
            {
                if (string.Equals(difficulty.Code, code, StringComparison.Ordinal))
                {
                    return ValidationResult<Difficulty, DifficultyCodeViolation>.Valid(difficulty);
                }
            }

            return ValidationResult<Difficulty, DifficultyCodeViolation>.Invalid(DifficultyCodeViolation.Unknown);
        }

        /// <summary>配分の刻み（5%）の倍数なら true。</summary>
        internal static bool IsOnStep(int pct) => pct % GameRules.AllocationStepPct == 0;

        /// <summary>任意の桁数の整数について、配分の刻み（5%）の倍数なら true。</summary>
        internal static bool IsOnStep(BigInteger pct) => pct % GameRules.AllocationStepPct == 0;
    }
}
