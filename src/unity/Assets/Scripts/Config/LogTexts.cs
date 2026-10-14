namespace LifeAllocationSim.Config
{
    /// <summary>
    /// ブラウザのコンソールに出すログの文字列（開発者向け）。
    /// Cookie・セッション ID・リクエストの本体は書かない。状態コードとエラーコードだけを出す。
    /// </summary>
    public static class LogTexts
    {
        public const string ApiNetworkFailure = "保存 API に接続できませんでした。";
        public const string ApiErrorResponse = "保存 API がエラーを返しました。状態：{0}、code：{1}";
        public const string ApiErrorResponseWithoutCode = "保存 API がエラーを返しました。状態：{0}、code：なし";
        public const string ApiUnexpectedBody = "保存 API の応答を解釈できませんでした。状態：{0}";
        public const string ApiBodyMissing = "保存 API の応答の本文がありません。";
        public const string ApiErrorBodyUnreadable = "保存 API のエラーの本文を読めませんでした。例外の種類：{0}";
        public const string FieldInvalid = "保存 API の応答の項目が想定と違います：{0}";
        public const string FieldTypeUnexpected = "保存 API の応答の項目の型が想定と違います：{0}（型：{1}）";
        public const string ReplayRejected = "再計算が履歴を受け付けませんでした。";
        public const string DifficultyRejected = "難易度の code を Domain Core が受け付けませんでした。";
        public const string ResultMismatch = "保存結果と再計算の結果が一致しませんでした。ターン番号：{0}";
        public const string EvaluationMissing = "終了したゲームに評価がありません。";
        public const string FontMissing = "表示用のフォントが読み込めませんでした：{0}";
        public const string SessionMissing = "進行中のゲームがありません。";
        public const string UndefinedValue = "定義されていない値です。";
    }
}
