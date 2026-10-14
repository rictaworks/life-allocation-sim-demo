namespace CoreHost.Config
{
    /// <summary>
    /// 終了コードと入力の上限。終了コード：0 は成功、2 は入力不正・規則違反（理由コードを JSON で返す）、1 は内部エラー（詳細は出さない）。
    /// </summary>
    public static class ProtocolLimits
    {
        /// <summary>終了コード：成功。</summary>
        public const int ExitSuccess = 0;

        /// <summary>終了コード：内部エラー。標準エラーには、例外の型名だけを 1 行で出します（メッセージ・入力値・スタックトレースは出しません）。</summary>
        public const int ExitInternalError = 1;

        /// <summary>終了コード：入力不正・規則違反。</summary>
        public const int ExitRuleViolation = 2;

        /// <summary>入力の上限（バイト）。64KB を超えたら、解析せずに拒否する。</summary>
        public const int MaxInputBytes = 64 * 1024;

        /// <summary>入力を読むときの 1 回の大きさ（バイト）。上限の確認は、読むたびに行う。</summary>
        public const int ReadChunkBytes = 4096;
    }
}
