namespace DomainCore
{
    /// <summary>
    /// 難易度コードが不正な理由。保存 API・再計算ホストに渡すため、番号を固定する。
    /// 表示文字列は Config の ReasonTexts にあり、ReasonDescriptions で引く。
    /// </summary>
    public enum DifficultyCodeViolation
    {
        /// <summary>未指定：null か空文字列。</summary>
        Missing = 1,

        /// <summary>不明：easy・normal・hard のどれとも完全には一致しない。</summary>
        Unknown = 2,
    }
}
