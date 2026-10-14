namespace DomainCore
{
    /// <summary>
    /// 評価ランク（requirements.md 3.2・4.4）。Config の RankTable に上位から順に並ぶ。
    /// 値は架空のモデル値。ランクを決める処理は Evaluator が行う。
    /// </summary>
    public sealed class Rank
    {
        public Rank(string code, long minTotalAssetsYen, int minAverageSatisfaction)
        {
            Code = code;
            MinTotalAssetsYen = minTotalAssetsYen;
            MinAverageSatisfaction = minAverageSatisfaction;
        }

        /// <summary>ランクの記号（S・A・B・C・D）。画面にもこの記号を出す。</summary>
        public string Code { get; }

        /// <summary>このランクに必要な総資産（貯蓄＋投資評価額）の下限（円）。</summary>
        public long MinTotalAssetsYen { get; }

        /// <summary>このランクに必要な平均満足度の下限（0〜100）。</summary>
        public int MinAverageSatisfaction { get; }
    }
}
