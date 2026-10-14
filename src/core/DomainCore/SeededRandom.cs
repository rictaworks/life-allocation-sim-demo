using System;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// シード付きの擬似乱数（requirements.md 4章：乱数は引数で渡すシード付き乱数だけを使う）。
    /// 同じシードからは、処理系（.NET、Unity の Mono・IL2CPP）によらず同じ整数列を返す。
    /// System.Random は処理系や版によって列が変わりうるため使わず、アルゴリズムをここに持つ。
    ///
    /// アルゴリズム：SplitMix64（Java の java.util.SplittableRandom と同じ列。S. Vigna の参照実装 splitmix64.c と同じ計算）。
    /// 1. 64 ビットの状態に、0x9E3779B97F4A7C15（2^64 を黄金比で割った値に近い奇数）を足す。
    /// 2. 足した後の状態 z を、次の混合関数で 64 ビットの出力にする。
    ///      z = (z xor (z >> 30)) × 0xBF58476D1CE4E5B9
    ///      z = (z xor (z >> 27)) × 0x94D049BB133111EB
    ///      出力 = z xor (z >> 31)
    /// 演算は ulong の加算・乗算・xor・右シフトだけで、桁あふれは unchecked で 2^64 を法として捨てる。浮動小数点は使わない。
    /// 状態を持つため、1 つのインスタンスを複数のスレッドから同時に使わない。
    /// </summary>
    public sealed class SeededRandom
    {
        private ulong state;

        /// <summary>シードから乱数列を作る。負のシードは、2 の補数のビット列をそのまま状態にする。</summary>
        public SeededRandom(long seed)
        {
            state = unchecked((ulong)seed);
        }

        /// <summary>次の 64 ビットの値（0〜2^64−1）を返す。</summary>
        public ulong NextUInt64()
        {
            unchecked
            {
                state += SplitMix64Constants.GoldenGamma;
                var z = state;
                z = (z ^ (z >> SplitMix64Constants.FirstShift)) * SplitMix64Constants.FirstMultiplier;
                z = (z ^ (z >> SplitMix64Constants.SecondShift)) * SplitMix64Constants.SecondMultiplier;
                return z ^ (z >> SplitMix64Constants.FinalShift);
            }
        }

        /// <summary>
        /// 0 以上 exclusiveMax 未満の整数を、偏りなく返す（棄却法）。
        /// 64 ビットの値のうち、exclusiveMax で割り切れない上端の部分（2^64 mod exclusiveMax 個）が出たら捨てて引き直し、
        /// 残りを exclusiveMax で割った余りを返す。引き直しの回数もシードだけで決まるため、結果は再現できる。
        /// exclusiveMax が 1 未満なら ArgumentOutOfRangeException。
        /// </summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax), exclusiveMax, ErrorMessages.RandomBoundNotPositive);
            }

            var bound = (ulong)exclusiveMax;

            // 2^64 mod bound。ulong.MaxValue は 2^64 − 1 なので、余りに 1 を足してから、もう一度 bound で割った余りを取る
            var rejectedCount = ((ulong.MaxValue % bound) + 1) % bound;
            var maxAccepted = ulong.MaxValue - rejectedCount;

            ulong value;
            do
            {
                value = NextUInt64();
            }
            while (value > maxAccepted);

            return (int)(value % bound);
        }
    }
}
