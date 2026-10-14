using System;
using System.Globalization;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 理由コードを、画面に出す文章（Config の ReasonTexts）にする。文中の数値は GameRules の値で埋める。
    /// </summary>
    public static class ReasonDescriptions
    {
        /// <summary>配分が不正な理由の文章。未定義の理由コードは ArgumentOutOfRangeException。</summary>
        public static string Describe(AllocationViolation violation)
        {
            switch (violation)
            {
                case AllocationViolation.Negative:
                    return ReasonTexts.AllocationNegative;
                case AllocationViolation.StepMismatch:
                    return Format(ReasonTexts.AllocationStepMismatch, GameRules.AllocationStepPct);
                case AllocationViolation.SumMismatch:
                    return Format(ReasonTexts.AllocationSumMismatch, GameRules.AllocationTotalPct);
                default:
                    throw new ArgumentOutOfRangeException(nameof(violation), violation, ErrorMessages.UndefinedReason);
            }
        }

        /// <summary>難易度コードが不正な理由の文章。未定義の理由コードは ArgumentOutOfRangeException。</summary>
        public static string Describe(DifficultyCodeViolation violation)
        {
            switch (violation)
            {
                case DifficultyCodeViolation.Missing:
                    return ReasonTexts.DifficultyMissing;
                case DifficultyCodeViolation.Unknown:
                    return ReasonTexts.DifficultyUnknown;
                default:
                    throw new ArgumentOutOfRangeException(nameof(violation), violation, ErrorMessages.UndefinedReason);
            }
        }

        /// <summary>再計算が受け付けない理由の文章。未定義の理由コードは ArgumentOutOfRangeException。</summary>
        public static string Describe(ReplayViolation violation)
        {
            switch (violation)
            {
                case ReplayViolation.InvalidAllocation:
                    return ReasonTexts.ReplayInvalidAllocation;
                case ReplayViolation.GameFinished:
                    return ReasonTexts.ReplayGameFinished;
                default:
                    throw new ArgumentOutOfRangeException(nameof(violation), violation, ErrorMessages.UndefinedReason);
            }
        }

        private static string Format(string format, int value) => string.Format(CultureInfo.InvariantCulture, format, value);
    }
}
