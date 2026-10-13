using System;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 検証の結果。正常（値を持つ）か、不正（理由コードを持つ）のどちらか。
    /// 不正な入力は、例外ではなくこの結果で返す（requirements.md 4.1-3）。
    /// 持っていないもの（正常の結果の理由・不正の結果の値）を読むと InvalidOperationException にする（既定値を返さない）。
    /// </summary>
    /// <typeparam name="TValue">正常なときの値の型。</typeparam>
    /// <typeparam name="TReason">理由コードの列挙型。</typeparam>
    public sealed class ValidationResult<TValue, TReason>
        where TReason : struct, Enum
    {
        private readonly TValue value;
        private readonly TReason reason;

        private ValidationResult(bool isValid, TValue value, TReason reason)
        {
            IsValid = isValid;
            this.value = value;
            this.reason = reason;
        }

        /// <summary>正常なら true。</summary>
        public bool IsValid { get; }

        /// <summary>正常なときの値。不正な結果で読むと InvalidOperationException。</summary>
        public TValue Value => IsValid ? value : throw new InvalidOperationException(ErrorMessages.InvalidResultHasNoValue);

        /// <summary>不正な理由。正常な結果で読むと InvalidOperationException。</summary>
        public TReason Reason => IsValid ? throw new InvalidOperationException(ErrorMessages.ValidResultHasNoReason) : reason;

        public static ValidationResult<TValue, TReason> Valid(TValue value) =>
            new ValidationResult<TValue, TReason>(true, value, default);

        /// <summary>不正の結果を作る。定義されていない理由コードは ArgumentOutOfRangeException。</summary>
        public static ValidationResult<TValue, TReason> Invalid(TReason reason)
        {
            if (!Enum.IsDefined(typeof(TReason), reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason), reason, ErrorMessages.UndefinedReason);
            }

            return new ValidationResult<TValue, TReason>(false, default, reason);
        }
    }
}
