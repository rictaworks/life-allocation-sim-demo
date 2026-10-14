// このファイルの数値は、乱数のアルゴリズム（SplitMix64）で決まる定数です。架空のモデル値（ゲームの調整用の値）ではなく、変えると同じシードから別の列になります。
namespace DomainCore.Config
{
    /// <summary>
    /// SplitMix64 の定数（SeededRandom が使う）。アルゴリズムで決まる値で、ゲームの調整用の値ではない（requirements.md 4章：乱数はシードだけ）。
    /// 変えると、同じシードから別の乱数列になり、保存されたゲームの再計算の結果が変わる。
    /// </summary>
    public static class SplitMix64Constants
    {
        /// <summary>状態に足す定数（0x9E3779B97F4A7C15。2^64 を黄金比で割った値に近い奇数）。</summary>
        public const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

        /// <summary>混合の 1 回目の乗数。</summary>
        public const ulong FirstMultiplier = 0xBF58476D1CE4E5B9UL;

        /// <summary>混合の 2 回目の乗数。</summary>
        public const ulong SecondMultiplier = 0x94D049BB133111EBUL;

        /// <summary>混合の 1 回目の右シフト量。</summary>
        public const int FirstShift = 30;

        /// <summary>混合の 2 回目の右シフト量。</summary>
        public const int SecondShift = 27;

        /// <summary>出力の前の右シフト量。</summary>
        public const int FinalShift = 31;
    }
}
