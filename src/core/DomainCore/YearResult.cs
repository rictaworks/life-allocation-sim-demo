namespace DomainCore
{
    /// <summary>
    /// 1 年の精算の結果（requirements.md 4.3-9）：次の状態と、その年の内訳（収入・各枠・イベント・運用損益・取り崩し額）。
    /// 生成は YearSettlement だけが行う（プロパティは内部からだけ設定する）。
    /// </summary>
    public sealed class YearResult
    {
        /// <summary>この年の年齢（精算の前の年齢）。</summary>
        public int Age { get; internal set; }

        /// <summary>この年の配分（生活費・貯蓄・投資の %）。</summary>
        public Allocation Allocation { get; internal set; }

        /// <summary>この年の手取り年収（円）。</summary>
        public long IncomeYen { get; internal set; }

        /// <summary>生活費枠（円）：手取り年収 × 生活費の %（円未満切り捨て）。</summary>
        public long LivingBudgetYen { get; internal set; }

        /// <summary>必要生活費（円）：ライフステージの基準 × 物価係数。</summary>
        public long RequiredLivingYen { get; internal set; }

        /// <summary>生活費の余剰（円）：生活費枠が必要生活費を超えた分。貯蓄に入る。</summary>
        public long SurplusYen { get; internal set; }

        /// <summary>生活費の不足（円）：必要生活費に足りなかった分。取り崩しに回す。</summary>
        public long LivingShortfallYen { get; internal set; }

        /// <summary>貯蓄への積立（円）：貯蓄枠と余剰の合計。</summary>
        public long SavingDepositYen { get; internal set; }

        /// <summary>投資への積立（円）：投資枠。</summary>
        public long InvestDepositYen { get; internal set; }

        /// <summary>この年のイベント（0 件か 1 件）。</summary>
        public EventDraw Event { get; internal set; }

        /// <summary>イベントの出費（円）。出費系以外は 0。</summary>
        public long EventExpenseYen { get; internal set; }

        /// <summary>イベントの収入（円）。臨時収入は受け取る額、昇給は加算する年収の額。それ以外は 0。</summary>
        public long EventIncomeYen { get; internal set; }

        /// <summary>この年の投資のリターン（万分率）。市場系の年は、イベントの値で置き換える。</summary>
        public int MarketReturnBp { get; internal set; }

        /// <summary>貯蓄の利息（円）。</summary>
        public long SavingsInterestYen { get; internal set; }

        /// <summary>投資評価額の増減（円）。運用の結果。</summary>
        public long InvestmentGainYen { get; internal set; }

        /// <summary>運用損益（円）：貯蓄の利息と投資評価額の増減の合計。</summary>
        public long MarketGainYen => SavingsInterestYen + InvestmentGainYen;

        /// <summary>取り崩しの必要額（円）：生活費の不足とイベントの出費の不足の合計。</summary>
        public long ShortfallYen { get; internal set; }

        /// <summary>取り崩しで貯蓄から使った額（円）。</summary>
        public long WithdrawnFromSavingsYen { get; internal set; }

        /// <summary>取り崩しで投資を売却した額（円）。その年の評価額で売る。</summary>
        public long WithdrawnFromInvestmentYen { get; internal set; }

        /// <summary>取り崩し後も残った不足（円）。0 より大きければ破綻。</summary>
        public long UnmetYen { get; internal set; }

        /// <summary>次の年の状態。</summary>
        public PlayerState Next { get; internal set; }
    }
}
