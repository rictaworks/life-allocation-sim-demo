using System;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// ライフステージ（requirements.md 3.2・3.3）。年齢の範囲ごとの、手取り年収と必要生活費の基準。
    /// 値は Config の LifeStageTable にある架空のモデル値。金額は円の整数。
    /// </summary>
    public sealed class LifeStage
    {
        public LifeStage(int minAge, int maxAge, long takeHomeIncomeYen, long baseLivingCostYen)
        {
            MinAge = minAge;
            MaxAge = maxAge;
            TakeHomeIncomeYen = takeHomeIncomeYen;
            BaseLivingCostYen = baseLivingCostYen;
        }

        /// <summary>このステージの最初の年齢（この年齢を含む）。</summary>
        public int MinAge { get; }

        /// <summary>このステージの最後の年齢（この年齢を含む）。</summary>
        public int MaxAge { get; }

        /// <summary>手取り年収（円/年）。</summary>
        public long TakeHomeIncomeYen { get; }

        /// <summary>必要生活費の基準（円/年）。物価係数を掛ける前の値（requirements.md 3.3）。</summary>
        public long BaseLivingCostYen { get; }

        /// <summary>年齢に対応するライフステージを返す。どの段階にも入らない年齢は ArgumentOutOfRangeException。</summary>
        public static LifeStage ForAge(int age)
        {
            foreach (var stage in LifeStageTable.All)
            {
                if (stage.MinAge <= age && age <= stage.MaxAge)
                {
                    return stage;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(age), age, ErrorMessages.LifeStageNotFound);
        }
    }
}
