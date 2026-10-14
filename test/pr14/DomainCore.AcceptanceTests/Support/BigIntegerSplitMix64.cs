using System;
using System.Globalization;
using System.Numerics;

namespace DomainCore.AcceptanceTests.Support
{
    /// <summary>
    /// SplitMix64 の参照実装。任意精度の整数（BigInteger）で、2^64 を法とする剰余を明示的に取って計算する。
    /// 実装（SeededRandom）の ulong・unchecked の演算とは別の計算で、同じ列になるかを確かめるために使う。
    /// 定数は S. Vigna の splitmix64.c（Java の java.util.SplittableRandom と同じ）による。
    /// </summary>
    internal sealed class BigIntegerSplitMix64
    {
        private const string GammaHex = "9E3779B97F4A7C15";
        private const string FirstMultiplierHex = "BF58476D1CE4E5B9";
        private const string SecondMultiplierHex = "94D049BB133111EB";
        private const int FirstShift = 30;
        private const int SecondShift = 27;
        private const int FinalShift = 31;
        private const int WordBits = 64;

        private readonly BigInteger modulus;
        private readonly BigInteger gamma;
        private readonly BigInteger firstMultiplier;
        private readonly BigInteger secondMultiplier;
        private BigInteger state;

        public BigIntegerSplitMix64(long seed)
        {
            modulus = BigInteger.One << WordBits;
            gamma = Hex(GammaHex);
            firstMultiplier = Hex(FirstMultiplierHex);
            secondMultiplier = Hex(SecondMultiplierHex);

            // 負のシードは 2 の補数のビット列（seed mod 2^64）を状態にする
            state = ((new BigInteger(seed) % modulus) + modulus) % modulus;
        }

        public ulong NextUInt64() => (ulong)Next();

        /// <summary>
        /// 0 以上 bound 未満の整数。64 ビットの値が 2^64 − (2^64 mod bound) 以上なら捨てて引き直し、
        /// 残りを bound で割った余りを返す（PR 本文の「棄却法で偏りの無い NextInt」）。
        /// </summary>
        public int NextInt(int bound)
        {
            if (bound < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bound), bound, "上限は 1 以上にしてください");
            }

            var divisor = new BigInteger(bound);
            var limit = modulus - (modulus % divisor);
            while (true)
            {
                var value = Next();
                if (value < limit)
                {
                    return (int)(value % divisor);
                }
            }
        }

        // 先頭に 0 を付けて、最上位ビットが 1 の値を負数と読まないようにする
        private static BigInteger Hex(string digits) =>
            BigInteger.Parse("0" + digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

        private BigInteger Next()
        {
            state = (state + gamma) % modulus;
            var z = state;
            z = ((z ^ (z >> FirstShift)) * firstMultiplier) % modulus;
            z = ((z ^ (z >> SecondShift)) * secondMultiplier) % modulus;
            return z ^ (z >> FinalShift);
        }
    }
}
