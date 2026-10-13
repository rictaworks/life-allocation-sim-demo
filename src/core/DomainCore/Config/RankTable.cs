// このファイルの数値は、すべて架空のモデル値です。実在の統計・金融商品・個別の投資判断を示すものではありません（requirements.md 1.2）。
using System;
using System.Collections.ObjectModel;

namespace DomainCore.Config
{
    /// <summary>
    /// 評価ランク 5 段階（requirements.md 3.2・4.4）。上位から順に並べる。
    /// 各ランクは、総資産（貯蓄＋投資評価額）の下限（円）と、平均満足度の下限を持つ。
    /// 上位のランクほど下限が高い。最下位は 0・0 で、どの結果もあてはまる（破綻は資産額によらず最下位：4.4-3）。
    /// 最上位（と次の A）には満足度の下限を置き、「生活費を削りすぎて満足度が低い高資産」を最上位にしない（4.4-4）。
    /// ランクを決める処理（evaluate）は #4 で作る。
    /// </summary>
    public static class RankTable
    {
        public static readonly ReadOnlyCollection<Rank> All = Array.AsReadOnly(new[]
        {
            new Rank(code: "S", minTotalAssetsYen: 60_000_000, minAverageSatisfaction: 60),
            new Rank(code: "A", minTotalAssetsYen: 40_000_000, minAverageSatisfaction: 40),
            new Rank(code: "B", minTotalAssetsYen: 20_000_000, minAverageSatisfaction: 0),
            new Rank(code: "C", minTotalAssetsYen: 5_000_000, minAverageSatisfaction: 0),
            new Rank(code: "D", minTotalAssetsYen: 0, minAverageSatisfaction: 0),
        });
    }
}
