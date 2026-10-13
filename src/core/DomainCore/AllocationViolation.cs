namespace DomainCore
{
    /// <summary>
    /// 配分が不正な理由（requirements.md 4.1-3）。保存 API・再計算ホストに渡すため、番号を固定する。
    /// 表示文字列は Config の ReasonTexts にあり、ReasonDescriptions で引く。
    /// </summary>
    public enum AllocationViolation
    {
        /// <summary>負値：0 未満の項目がある。</summary>
        Negative = 1,

        /// <summary>刻み違反：5 の倍数でない項目がある。</summary>
        StepMismatch = 2,

        /// <summary>合計不一致：合計が 100 でない。</summary>
        SumMismatch = 3,
    }
}
