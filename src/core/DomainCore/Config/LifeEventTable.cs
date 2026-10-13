// このファイルの数値は、すべて架空のモデル値です。実在の統計・金融商品・個別の投資判断を示すものではありません（requirements.md 1.2）。
using System;
using System.Collections.ObjectModel;

namespace DomainCore.Config
{
    /// <summary>
    /// ライフイベント 12 種（requirements.md 3.2：出費系 8・収入系 2・市場系 2）。
    /// - 年齢は両端を含む。年齢の範囲は 4.2 の例（結婚・出産は 25〜45 歳、家電故障は全年齢、介護は 50 歳以上）に合わせる。
    /// - 一度きり（occursOnce）は、発生済みなら候補から外す（4.2-2）。
    /// - 確率は 1 年あたりの万分率（10000 で 100%）。どの年齢でも、候補の確率の合計は 10000 以下にする（1 年に 0 件か 1 件）。
    /// - 金額は円。出費は支払う額、収入は受け取る額（昇給は手取り年収に加える額）。
    /// - 市場系は金額を持たず、その年の投資のリターン（万分率）を置き換える（4.3-5）。
    /// - コードは保存 API が turns.event_code に保存する文字列。
    /// - 並び順は抽選で候補を並べる順序に使う想定のため、変えると同じシードでも結果が変わりうる。
    /// </summary>
    public static class LifeEventTable
    {
        public static readonly ReadOnlyCollection<LifeEvent> All = Array.AsReadOnly(new[]
        {
            // 出費系（8 種）
            new LifeEvent(code: "marriage", label: "結婚", effect: LifeEventEffect.Expense, minAge: 25, maxAge: 45, occursOnce: true, probabilityBp: 800, amountYen: 3_000_000, marketReturnBp: 0),
            new LifeEvent(code: "childbirth", label: "出産", effect: LifeEventEffect.Expense, minAge: 25, maxAge: 45, occursOnce: false, probabilityBp: 500, amountYen: 500_000, marketReturnBp: 0),
            new LifeEvent(code: "appliance_breakdown", label: "家電の故障", effect: LifeEventEffect.Expense, minAge: 22, maxAge: 65, occursOnce: false, probabilityBp: 600, amountYen: 150_000, marketReturnBp: 0),
            new LifeEvent(code: "nursing_care", label: "親の介護", effect: LifeEventEffect.Expense, minAge: 50, maxAge: 65, occursOnce: false, probabilityBp: 400, amountYen: 1_000_000, marketReturnBp: 0),
            new LifeEvent(code: "illness", label: "病気やけが", effect: LifeEventEffect.Expense, minAge: 22, maxAge: 65, occursOnce: false, probabilityBp: 300, amountYen: 300_000, marketReturnBp: 0),
            new LifeEvent(code: "relocation", label: "引っ越し", effect: LifeEventEffect.Expense, minAge: 22, maxAge: 50, occursOnce: false, probabilityBp: 300, amountYen: 400_000, marketReturnBp: 0),
            new LifeEvent(code: "car_purchase", label: "車の買い替え", effect: LifeEventEffect.Expense, minAge: 25, maxAge: 60, occursOnce: false, probabilityBp: 300, amountYen: 2_000_000, marketReturnBp: 0),
            new LifeEvent(code: "home_repair", label: "住まいの修繕", effect: LifeEventEffect.Expense, minAge: 35, maxAge: 65, occursOnce: false, probabilityBp: 300, amountYen: 1_000_000, marketReturnBp: 0),

            // 収入系（2 種）
            new LifeEvent(code: "pay_raise", label: "昇給", effect: LifeEventEffect.IncomeRaise, minAge: 25, maxAge: 55, occursOnce: false, probabilityBp: 1000, amountYen: 200_000, marketReturnBp: 0),
            new LifeEvent(code: "windfall", label: "臨時収入", effect: LifeEventEffect.OneTimeIncome, minAge: 22, maxAge: 65, occursOnce: false, probabilityBp: 300, amountYen: 300_000, marketReturnBp: 0),

            // 市場系（2 種）
            new LifeEvent(code: "market_crash", label: "市場の暴落", effect: LifeEventEffect.MarketReturnOverride, minAge: 22, maxAge: 65, occursOnce: false, probabilityBp: 300, amountYen: 0, marketReturnBp: -3000),
            new LifeEvent(code: "market_boom", label: "市場の好況", effect: LifeEventEffect.MarketReturnOverride, minAge: 22, maxAge: 65, occursOnce: false, probabilityBp: 300, amountYen: 0, marketReturnBp: 2500),
        });
    }
}
