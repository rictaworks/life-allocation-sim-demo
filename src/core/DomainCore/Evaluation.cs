namespace DomainCore
{
    /// <summary>
    /// 最終評価（requirements.md 4.4）：総資産・平均満足度・破綻の有無・困窮年数・投資の売却年数・ランク・3 観点のコメント。
    /// 生成は Evaluator だけが行う。
    /// </summary>
    public sealed class Evaluation
    {
        /// <summary>総資産（貯蓄＋投資評価額・円）。</summary>
        public long TotalAssetsYen { get; internal set; }

        /// <summary>平均満足度（各年の年末の値の平均。切り捨て）。</summary>
        public int AverageSatisfaction { get; internal set; }

        /// <summary>破綻したら true。</summary>
        public bool IsBankrupt { get; internal set; }

        /// <summary>満足度が閾値未満だった年数（生活困窮の年）。</summary>
        public int StruggleYears { get; internal set; }

        /// <summary>取り崩しが投資の売却まで及んだ年数。</summary>
        public int InvestmentSoldYears { get; internal set; }

        /// <summary>ランク（S〜D）。破綻は資産額によらず D。</summary>
        public Rank Rank { get; internal set; }

        /// <summary>観点 1：老後資金（総資産と目安額の比較）。</summary>
        public string RetirementComment { get; internal set; }

        /// <summary>観点 2：生活の質（平均満足度と困窮年数）。</summary>
        public string QualityComment { get; internal set; }

        /// <summary>観点 3：備え（投資を売却した年数）。</summary>
        public string PreparednessComment { get; internal set; }
    }
}
