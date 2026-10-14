using System;

namespace DomainCore.AcceptanceTests.Support
{
    /// <summary>
    /// テストの入力（操作の列・シード）を作るための小さな擬似乱数（Marsaglia の xorshift64、シフト 13・7・17）。
    /// 実装の SeededRandom とは無関係で、入力の列を固定するためだけに使う。偏りは問わない。
    /// </summary>
    internal sealed class XorShift64
    {
        private ulong state;

        public XorShift64(ulong seed)
        {
            if (seed == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), seed, "xorshift64 のシードは 0 以外にしてください");
            }

            state = seed;
        }

        public ulong NextUInt64()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }

        public long NextInt64() => unchecked((long)NextUInt64());

        /// <summary>0 以上 bound 未満（テストの入力用。偏りは問わない）。</summary>
        public int NextBelow(int bound)
        {
            if (bound < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bound), bound, "上限は 1 以上にしてください");
            }

            return (int)(NextUInt64() % (ulong)bound);
        }
    }
}
