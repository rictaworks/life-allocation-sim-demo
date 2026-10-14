namespace CoreHost.Config
{
    /// <summary>
    /// エラーのコード（応答の error.code）。規則の違反は Domain Core の理由コードを、この名前に変えて返す。
    /// </summary>
    public static class ProtocolCodes
    {
        // 入力・操作の不正（ホストの検査）
        public const string InputTooLarge = "input_too_large";

        public const string RequestInvalid = "request_invalid";

        public const string CommandUnknown = "command_unknown";

        // 内部エラー（詳細は出さない）
        public const string InternalError = "internal_error";

        // 難易度（Domain Core の理由コード）
        public const string DifficultyMissing = "difficulty_missing";

        public const string DifficultyUnknown = "difficulty_unknown";

        // 配分（Domain Core の理由コード）
        public const string AllocationNegative = "allocation_negative";

        public const string AllocationStepMismatch = "allocation_step_mismatch";

        public const string AllocationSumMismatch = "allocation_sum_mismatch";

        // ターン（Domain Core の理由コード）
        public const string GameFinished = "game_finished";
    }
}
