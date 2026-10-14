using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DomainCore
{
    /// <summary>
    /// ある年の始めのプレイヤーの状態（requirements.md 3.3・10 章のクラス図）。不変オブジェクト。
    /// 金額は円（long）、物価係数と利回りは万分率（int）。
    /// - IncomeYen：この年の手取り年収（ライフステージの基準に、昇給の累計 RaiseTotalYen を足した額）。
    /// - OccurredOnceEventCodes：発生済みの一度きりのイベントのコード。候補から外すために使う（requirements.md 4.2-2）。
    /// </summary>
    public sealed class PlayerState
    {
        private readonly ReadOnlyCollection<string> occurredOnceEventCodes;

        public PlayerState(
            int age,
            long incomeYen,
            long savingsYen,
            long investmentYen,
            int satisfaction,
            int priceIndexBp,
            PlayerStatus status,
            long raiseTotalYen,
            IEnumerable<string> occurredOnceEventCodes)
        {
            Age = age;
            IncomeYen = incomeYen;
            SavingsYen = savingsYen;
            InvestmentYen = investmentYen;
            Satisfaction = satisfaction;
            PriceIndexBp = priceIndexBp;
            Status = status;
            RaiseTotalYen = raiseTotalYen;
            this.occurredOnceEventCodes = new ReadOnlyCollection<string>(occurredOnceEventCodes.ToArray());
        }

        /// <summary>年齢（22〜65）。</summary>
        public int Age { get; }

        /// <summary>この年の手取り年収（円）。</summary>
        public long IncomeYen { get; }

        /// <summary>貯蓄残高（円）。</summary>
        public long SavingsYen { get; }

        /// <summary>投資評価額（円）。</summary>
        public long InvestmentYen { get; }

        /// <summary>生活満足度（0〜100）。</summary>
        public int Satisfaction { get; }

        /// <summary>物価係数（万分率。10000 は 1.0 倍）。</summary>
        public int PriceIndexBp { get; }

        /// <summary>状態（プレイ中・生活困窮・破綻・完走）。</summary>
        public PlayerStatus Status { get; }

        /// <summary>昇給イベントの累計（円/年）。次の年の手取り年収に加える。</summary>
        public long RaiseTotalYen { get; }

        /// <summary>発生済みの一度きりのイベントのコード。</summary>
        public IReadOnlyList<string> OccurredOnceEventCodes => occurredOnceEventCodes;

        /// <summary>破綻か完走で、ゲームが終了していれば true。</summary>
        public bool IsFinished => Status == PlayerStatus.Completed || Status == PlayerStatus.Bankrupt;

        /// <summary>指定したコードの一度きりのイベントが発生済みなら true。</summary>
        public bool HasOccurred(string code) => occurredOnceEventCodes.Contains(code);
    }
}
