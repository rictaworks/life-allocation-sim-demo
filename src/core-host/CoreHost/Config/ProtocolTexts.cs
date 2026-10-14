namespace CoreHost.Config
{
    /// <summary>
    /// ホストの入出力の文言（応答の error.message）。Domain Core の理由の文言は、ReasonDescriptions が返す。
    /// </summary>
    public static class ProtocolTexts
    {
        public const string InputTooLarge = "入力が大きすぎます。";

        public const string RequestInvalid = "リクエストの形式が正しくありません。";

        public const string CommandUnknown = "実行する操作が正しくありません。";

        public const string InternalError = "内部エラーが発生しました。";
    }
}
