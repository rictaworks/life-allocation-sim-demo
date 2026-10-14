using System;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 再計算の結果。正常なら Outcome、不正なら Failure を読む。反対側を読むと InvalidOperationException（既定値は返さない）。
    /// </summary>
    public sealed class ReplayResult
    {
        private readonly ReplayOutcome outcome;
        private readonly ReplayFailure failure;

        private ReplayResult(ReplayOutcome outcome, ReplayFailure failure)
        {
            this.outcome = outcome;
            this.failure = failure;
        }

        /// <summary>再計算が最後まで成功したら true。</summary>
        public bool IsValid => failure == null;

        /// <summary>正常なときの結果。不正なら InvalidOperationException。</summary>
        public ReplayOutcome Outcome => IsValid ? outcome : throw new InvalidOperationException(ErrorMessages.InvalidResultHasNoValue);

        /// <summary>不正なときの失敗の場所と理由。正常なら InvalidOperationException。</summary>
        public ReplayFailure Failure => IsValid ? throw new InvalidOperationException(ErrorMessages.ValidResultHasNoReason) : failure;

        internal static ReplayResult Valid(ReplayOutcome outcome) => new ReplayResult(outcome, null);

        internal static ReplayResult Invalid(ReplayFailure failure) => new ReplayResult(null, failure);
    }
}
