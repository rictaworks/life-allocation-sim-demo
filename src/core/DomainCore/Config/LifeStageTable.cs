// このファイルの数値は、すべて架空のモデル値です。実在の統計・金融商品・個別の投資判断を示すものではありません（requirements.md 1.2）。
using System;
using System.Collections.ObjectModel;

namespace DomainCore.Config
{
    /// <summary>
    /// ライフステージ 5 段階（requirements.md 3.2）。年齢の範囲ごとの、手取り年収と必要生活費の基準（円/年）。
    /// 必要生活費の基準は、物価係数を掛ける前の値（requirements.md 3.3）。どの段階も、手取り年収のおおむね 7〜8 割に置いた。
    /// </summary>
    public static class LifeStageTable
    {
        public static readonly ReadOnlyCollection<LifeStage> All = Array.AsReadOnly(new[]
        {
            new LifeStage(minAge: 22, maxAge: 29, takeHomeIncomeYen: 2_800_000, baseLivingCostYen: 2_000_000),
            new LifeStage(minAge: 30, maxAge: 39, takeHomeIncomeYen: 3_600_000, baseLivingCostYen: 2_500_000),
            new LifeStage(minAge: 40, maxAge: 49, takeHomeIncomeYen: 4_300_000, baseLivingCostYen: 3_000_000),
            new LifeStage(minAge: 50, maxAge: 59, takeHomeIncomeYen: 4_600_000, baseLivingCostYen: 3_200_000),
            new LifeStage(minAge: 60, maxAge: 65, takeHomeIncomeYen: 3_000_000, baseLivingCostYen: 2_400_000),
        });
    }
}
