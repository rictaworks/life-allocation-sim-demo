using System;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 1 年分の配分（requirements.md 3.1・10 章のクラス図）。手取り年収に対する生活費・貯蓄・投資の割合（整数の %）。
    /// 値が正しいか（0 以上・5 刻み・合計 100）は、この型では確かめない。Simulator.ValidateAllocation で確かめる。
    /// </summary>
    public readonly struct Allocation : IEquatable<Allocation>
    {
        public Allocation(int livingPct, int savingPct, int investPct)
        {
            LivingPct = livingPct;
            SavingPct = savingPct;
            InvestPct = investPct;
        }

        /// <summary>生活費の割合（%）。</summary>
        public int LivingPct { get; }

        /// <summary>貯蓄の割合（%）。</summary>
        public int SavingPct { get; }

        /// <summary>投資の割合（%）。</summary>
        public int InvestPct { get; }

        public static bool operator ==(Allocation left, Allocation right) => left.Equals(right);

        public static bool operator !=(Allocation left, Allocation right) => !left.Equals(right);

        /// <summary>項目の割合を返す。未定義の項目は ArgumentOutOfRangeException。</summary>
        public int Get(AllocationItem item)
        {
            switch (item)
            {
                case AllocationItem.Living:
                    return LivingPct;
                case AllocationItem.Saving:
                    return SavingPct;
                case AllocationItem.Invest:
                    return InvestPct;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item), item, ErrorMessages.UndefinedAllocationItem);
            }
        }

        public bool Equals(Allocation other) =>
            LivingPct == other.LivingPct && SavingPct == other.SavingPct && InvestPct == other.InvestPct;

        public override bool Equals(object obj) => obj is Allocation other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(LivingPct, SavingPct, InvestPct);

        /// <summary>1 項目だけを置き換えた配分を返す。未定義の項目は ArgumentOutOfRangeException。</summary>
        internal Allocation With(AllocationItem item, int pct)
        {
            switch (item)
            {
                case AllocationItem.Living:
                    return new Allocation(pct, SavingPct, InvestPct);
                case AllocationItem.Saving:
                    return new Allocation(LivingPct, pct, InvestPct);
                case AllocationItem.Invest:
                    return new Allocation(LivingPct, SavingPct, pct);
                default:
                    throw new ArgumentOutOfRangeException(nameof(item), item, ErrorMessages.UndefinedAllocationItem);
            }
        }
    }
}
