// このファイルの数値は、すべて架空のモデル値です。実在の統計・金融商品・個別の投資判断を示すものではありません（requirements.md 1.2）。
namespace DomainCore.Config
{
    /// <summary>
    /// 評価コメントの定型文（requirements.md 4.4-2：ルールベース、定型文の組み合わせ）。画面に出すため、ですます調で書く。
    /// {0}・{1} は Evaluator が埋める。金額は AmountFormat で桁区切りにする。
    /// </summary>
    public static class EvaluationTexts
    {
        /// <summary>金額の書式（円の桁区切り）。文ではないので、ですます調の検査の対象にしない。</summary>
        public const string AmountFormat = "#,##0";

        // 観点 1：老後資金（総資産を目安額と比べる）
        public const string RetirementAboveTarget = "総資産は {0} 円で、老後資金の目安の {1} 円を上回りました。";

        public const string RetirementBelowTarget = "総資産は {0} 円で、老後資金の目安の {1} 円には届きませんでした。";

        public const string RetirementBankrupt = "途中で資金が尽きたため、老後資金は確保できませんでした。";

        // 観点 2：生活の質（平均満足度）
        public const string QualityHigh = "生活の満足度は高い水準を保てました。";

        public const string QualityMiddle = "生活の満足度はおおむね保てました。";

        public const string QualityLow = "生活の満足度は低めでした。生活費を切り詰めすぎないように注意してください。";

        public const string QualityStruggleYears = "生活困窮となった年が {0} 年ありました。";

        public const string QualityHighAssetsLowSatisfaction = "資産は目安を上回りましたが、生活の満足度は低めでした。貯めることだけが正解ではありません。";

        // 観点 3：備え（投資の売却まで取り崩した年）
        public const string PreparednessNoSale = "投資を売却して生活費を補った年はありませんでした。";

        public const string PreparednessSold = "投資を売却して生活費を補った年が {0} 年ありました。";
    }
}
