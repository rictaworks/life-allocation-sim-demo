using System;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// ライフイベントの定義（requirements.md 3.2・4.2・10 章のクラス図）。値は Config の LifeEventTable にある架空のモデル値。
    /// 区分（出費系・収入系・市場系）は効果から決める。抽選は EventDrawer、適用は YearSettlement が行う。
    /// </summary>
    public sealed class LifeEvent
    {
        /// <summary>イベントを定義する。未定義の効果は ArgumentOutOfRangeException。</summary>
        public LifeEvent(
            string code,
            string label,
            LifeEventEffect effect,
            int minAge,
            int maxAge,
            bool occursOnce,
            int probabilityBp,
            long amountYen,
            int marketReturnBp)
        {
            Code = code;
            Label = label;
            Effect = effect;
            Category = CategoryOf(effect);
            MinAge = minAge;
            MaxAge = maxAge;
            OccursOnce = occursOnce;
            ProbabilityBp = probabilityBp;
            AmountYen = amountYen;
            MarketReturnBp = marketReturnBp;
        }

        /// <summary>コード。保存 API が turns.event_code に保存する文字列。</summary>
        public string Code { get; }

        /// <summary>画面に出す名前。</summary>
        public string Label { get; }

        /// <summary>効果（年次精算での扱い）。</summary>
        public LifeEventEffect Effect { get; }

        /// <summary>区分（出費系・収入系・市場系）。効果から決まる。</summary>
        public LifeEventCategory Category { get; }

        /// <summary>発生しうる最初の年齢（この年齢を含む）。</summary>
        public int MinAge { get; }

        /// <summary>発生しうる最後の年齢（この年齢を含む）。</summary>
        public int MaxAge { get; }

        /// <summary>一度きりなら true。発生済みなら候補から外す（requirements.md 4.2-2）。</summary>
        public bool OccursOnce { get; }

        /// <summary>1 年あたりの発生確率（万分率。10000 で 100%）。</summary>
        public int ProbabilityBp { get; }

        /// <summary>金額（円）。出費は支払う額、収入は受け取る額（昇給は手取り年収に加える額）。市場系は 0。</summary>
        public long AmountYen { get; }

        /// <summary>市場系の年に、投資のリターンを置き換える値（万分率）。市場系以外は 0。</summary>
        public int MarketReturnBp { get; }

        private static LifeEventCategory CategoryOf(LifeEventEffect effect)
        {
            switch (effect)
            {
                case LifeEventEffect.Expense:
                    return LifeEventCategory.Expense;
                case LifeEventEffect.OneTimeIncome:
                case LifeEventEffect.IncomeRaise:
                    return LifeEventCategory.Income;
                case LifeEventEffect.MarketReturnOverride:
                    return LifeEventCategory.Market;
                default:
                    throw new ArgumentOutOfRangeException(nameof(effect), effect, ErrorMessages.UndefinedLifeEventEffect);
            }
        }
    }
}
