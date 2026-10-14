namespace DomainCore
{
    /// <summary>
    /// 再計算が失敗した場所と理由（requirements.md 4.5：どの年で失敗したかと、Domain Core の理由コード）。
    /// </summary>
    public sealed class ReplayFailure
    {
        public ReplayFailure(int turnNo, int age, ReplayViolation violation, AllocationViolation? allocationReason)
        {
            TurnNo = turnNo;
            Age = age;
            Violation = violation;
            AllocationReason = allocationReason;
        }

        /// <summary>失敗したターンの番号（1 始まり。1 ターン＝1 年）。</summary>
        public int TurnNo { get; }

        /// <summary>失敗したターンの年齢。</summary>
        public int Age { get; }

        /// <summary>失敗の理由（配分の不正・終了後のターン）。</summary>
        public ReplayViolation Violation { get; }

        /// <summary>配分が不正な理由。Violation が InvalidAllocation のときだけ値がある。</summary>
        public AllocationViolation? AllocationReason { get; }
    }
}
