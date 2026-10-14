namespace DomainCore
{
    /// <summary>
    /// 再計算（Replayer）が受け付けない理由。保存 API・再計算ホストに渡すため、番号を固定する。
    /// 表示文字列は Config の ReasonTexts にあり、ReasonDescriptions で引く。
    /// </summary>
    public enum ReplayViolation
    {
        /// <summary>不正な配分でターンを進めようとした。理由は AllocationViolation（ReplayFailure.AllocationReason）。</summary>
        InvalidAllocation = 1,

        /// <summary>完走または破綻したゲームに、さらにターンを追加しようとした。</summary>
        GameFinished = 2,
    }
}
