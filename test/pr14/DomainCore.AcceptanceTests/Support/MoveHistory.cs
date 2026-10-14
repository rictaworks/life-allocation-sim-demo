using System;

namespace DomainCore.AcceptanceTests.Support
{
    /// <summary>
    /// 各項目を最後に動かした時刻。受け入れ条件 2 の「端数は最後に動かしていない項目で吸収する」を、
    /// 残り 2 本のうち最後に動かした時刻が古い方として表す（PR 本文の解釈）。
    /// まだ動かしていない項目は、PR 本文の順序（貯蓄 → 生活費 → 投資。貯蓄が最も古い）で、どの操作よりも前に動かしたものとみなす。
    /// 実装（AllocationAdjuster の並べ替え）とは別の形（時刻）で書き、同じ結果になるかを確かめる。
    /// </summary>
    internal sealed class MoveHistory
    {
        private const int InitialSavingTime = -3;
        private const int InitialLivingTime = -2;
        private const int InitialInvestTime = -1;

        private readonly int livingTime;
        private readonly int savingTime;
        private readonly int investTime;
        private readonly int clock;

        private MoveHistory(int livingTime, int savingTime, int investTime, int clock)
        {
            this.livingTime = livingTime;
            this.savingTime = savingTime;
            this.investTime = investTime;
            this.clock = clock;
        }

        public static MoveHistory Initial() => new MoveHistory(InitialLivingTime, InitialSavingTime, InitialInvestTime, 0);

        /// <summary>moved を今の時刻に動かした後の履歴を返す（自分は変えない）。</summary>
        public MoveHistory After(AllocationItem moved) => moved switch
        {
            AllocationItem.Living => new MoveHistory(clock, savingTime, investTime, clock + 1),
            AllocationItem.Saving => new MoveHistory(livingTime, clock, investTime, clock + 1),
            AllocationItem.Invest => new MoveHistory(livingTime, savingTime, clock, clock + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(moved), moved, "定義されていない配分の項目です"),
        };

        /// <summary>moved 以外の 2 本のうち、最後に動かした時刻が古い方（端数を吸収する側）。</summary>
        public AllocationItem Absorber(AllocationItem moved)
        {
            var (first, second) = Others(moved);
            return TimeOf(first) < TimeOf(second) ? first : second;
        }

        /// <summary>moved 以外の 2 本のうち、端数を吸収しない側（比率どおりの値を切り捨てる側）。</summary>
        public AllocationItem Kept(AllocationItem moved)
        {
            var (first, second) = Others(moved);
            return Absorber(moved) == first ? second : first;
        }

        private static (AllocationItem First, AllocationItem Second) Others(AllocationItem moved) => moved switch
        {
            AllocationItem.Living => (AllocationItem.Saving, AllocationItem.Invest),
            AllocationItem.Saving => (AllocationItem.Living, AllocationItem.Invest),
            AllocationItem.Invest => (AllocationItem.Living, AllocationItem.Saving),
            _ => throw new ArgumentOutOfRangeException(nameof(moved), moved, "定義されていない配分の項目です"),
        };

        private int TimeOf(AllocationItem item) => item switch
        {
            AllocationItem.Living => livingTime,
            AllocationItem.Saving => savingTime,
            AllocationItem.Invest => investTime,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "定義されていない配分の項目です"),
        };
    }
}
