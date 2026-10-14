namespace DomainCore
{
    /// <summary>
    /// ライフイベントの効果。年次精算（YearSettlement）は、イベントのコードではなく、この効果で処理を決める。
    /// </summary>
    public enum LifeEventEffect
    {
        /// <summary>出費：AmountYen を貯蓄から支払う（requirements.md 4.3-4）。</summary>
        Expense = 1,

        /// <summary>臨時の収入：AmountYen をその年に受け取る。</summary>
        OneTimeIncome = 2,

        /// <summary>昇給：手取り年収に AmountYen を加える（requirements.md 3.3）。</summary>
        IncomeRaise = 3,

        /// <summary>市場：その年の投資のリターンを MarketReturnBp に置き換える（requirements.md 4.3-5）。</summary>
        MarketReturnOverride = 4,
    }
}
