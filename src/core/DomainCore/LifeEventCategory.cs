namespace DomainCore
{
    /// <summary>ライフイベントの区分（requirements.md 3.2：出費系 8・収入系 2・市場系 2）。効果から決まる。</summary>
    public enum LifeEventCategory
    {
        /// <summary>出費系。</summary>
        Expense = 1,

        /// <summary>収入系。</summary>
        Income = 2,

        /// <summary>市場系。</summary>
        Market = 3,
    }
}
