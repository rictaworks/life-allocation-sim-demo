using System;
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
        public static ValidationResult<Allocation, AllocationViolation> ValidateAllocation(Allocation allocation)
        {
            if (allocation.LivingPct < 0 || allocation.SavingPct < 0 || allocation.InvestPct < 0)
            {
                return ValidationResult<Allocation, AllocationViolation>.Invalid(AllocationViolation.Negative);
            }

            if (!IsOnStep(allocation.LivingPct) || !IsOnStep(allocation.SavingPct) || !IsOnStep(allocation.InvestPct))
            {
                return ValidationResult<Allocation, AllocationViolation>.Invalid(AllocationViolation.StepMismatch);
            }

            // int の範囲いっぱいの値でも桁あふれしないように、long で足す
            var total = (long)allocation.LivingPct + allocation.SavingPct + allocation.InvestPct;
            if (total != GameRules.AllocationTotalPct)
            {
                return ValidationResult<Allocation, AllocationViolation>.Invalid(AllocationViolation.SumMismatch);
            }

            return ValidationResult<Allocation, AllocationViolation>.Valid(allocation);
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
    }
}
