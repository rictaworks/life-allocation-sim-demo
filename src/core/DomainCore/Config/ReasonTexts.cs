// このファイルの数値は、すべて架空のモデル値です。実在の統計・金融商品・個別の投資判断を示すものではありません（requirements.md 1.2）。
namespace DomainCore.Config
{
    /// <summary>
    /// 理由コード（AllocationViolation・DifficultyCodeViolation）の表示文字列。画面に出すため、ですます調で書く。
    /// {0} は ReasonDescriptions が GameRules の値で埋める（数値を文章に二重に書かない）。
    /// </summary>
    public static class ReasonTexts
    {
        public const string AllocationNegative = "配分にマイナスの値があります。0 以上の値にしてください。";

        public const string AllocationStepMismatch = "配分は {0}% 刻みで指定してください。";

        public const string AllocationSumMismatch = "配分の合計を {0}% にしてください。";

        public const string DifficultyMissing = "難易度が指定されていません。";

        public const string DifficultyUnknown = "指定された難易度はありません。一覧から選んでください。";
    }
}
