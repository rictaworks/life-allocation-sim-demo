using System.Numerics;

namespace DomainCore
{
    /// <summary>
    /// 外から読んだ 1 年分の配分（整数の %。任意の桁数）。範囲・刻み・合計は、この型では確かめない（Simulator.ValidateAllocationInput が確かめる）。
    /// 再計算ホストが JSON から読む入力の型。32 ビットの範囲の外の値も、桁あふれさせずに規則で判定するため、BigInteger で持つ。
    /// 検証に通った値だけを Allocation（int）にする。
    /// </summary>
    public readonly struct AllocationInput
    {
        public AllocationInput(BigInteger living, BigInteger saving, BigInteger invest)
        {
            Living = living;
            Saving = saving;
            Invest = invest;
        }

        /// <summary>生活費の割合（%）の入力値。</summary>
        public BigInteger Living { get; }

        /// <summary>貯蓄の割合（%）の入力値。</summary>
        public BigInteger Saving { get; }

        /// <summary>投資の割合（%）の入力値。</summary>
        public BigInteger Invest { get; }

        /// <summary>検証済みの配分（int の値）は、そのまま入力にできる。</summary>
        public static implicit operator AllocationInput(Allocation allocation) =>
            new AllocationInput(allocation.LivingPct, allocation.SavingPct, allocation.InvestPct);
    }
}
