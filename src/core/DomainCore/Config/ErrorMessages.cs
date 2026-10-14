// このファイルの数値は、すべて架空のモデル値です。実在の統計・金融商品・個別の投資判断を示すものではありません（requirements.md 1.2）。
namespace DomainCore.Config
{
    /// <summary>
    /// 例外の文言（プログラムの誤りを開発者に知らせるためのもの。画面や API の応答には出さない）。
    /// {0} などは、例外を投げる側が埋める。
    /// </summary>
    public static class ErrorMessages
    {
        public const string ValidResultHasNoReason = "正常な結果には理由コードがありません。IsValid を確かめてから読んでください。";

        public const string InvalidResultHasNoValue = "不正な結果には値がありません。IsValid を確かめてから読んでください。";

        public const string UndefinedReason = "定義されていない理由コードです。";

        public const string UndefinedAllocationItem = "定義されていない配分の項目です。";

        public const string UndefinedLifeEventEffect = "定義されていないイベントの効果です。";

        public const string SliderValueOutOfRange = "配分の値は 0 以上 {0} 以下で、{1} の倍数にしてください。";

        public const string InvalidInitialAllocation = "自動調整の初期値が正しくありません（{0}）。正しい配分を渡してください。";

        public const string RandomBoundNotPositive = "乱数の上限は 1 以上にしてください。";

        public const string FinishedStateCannotAdvance = "終了したゲームは精算できません。精算の前に状態を確かめてください。";

        public const string LifeStageNotFound = "年齢に対応するライフステージがありません。";

        public const string NoEventDrawn = "この年はイベントが発生していません。HasEvent を確かめてから読んでください。";

        public const string EvaluationNeedsFinishedGame = "評価は、完走または破綻したゲームに対してだけ行えます。";

        public const string EvaluationNeedsHistory = "評価には 1 年以上の履歴が必要です。";

        public const string OutcomeNotFinished = "ゲームはまだ終了していないため、評価はありません。IsFinished を確かめてから読んでください。";
    }
}
