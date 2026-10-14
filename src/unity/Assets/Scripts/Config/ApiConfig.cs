namespace LifeAllocationSim.Config
{
    /// <summary>
    /// 保存 API（同一オリジン）の経路・項目名・エラーコード（契約 2）。
    /// Cookie はブラウザが扱うため、この中には含めない。セッション ID も扱わない。
    /// </summary>
    public static class ApiConfig
    {
        // 経路
        public const string SessionPath = "/api/session";
        public const string GamePath = "/api/game";
        public const string GamesPath = "/api/games";
        public const string TurnsPath = "/api/games/turns";

        // ヘッダーと本体
        public const string HeaderAccept = "Accept";
        public const string HeaderContentType = "Content-Type";
        public const string HeaderRetryAfter = "Retry-After";
        public const string ContentTypeJson = "application/json";
        public const string SessionBody = "{}";
        public const string EmptyWebsite = "";

        // 項目名
        public const string KeyError = "error";
        public const string KeyCode = "code";
        public const string KeyGame = "game";
        public const string KeyTurn = "turn";
        public const string KeyTurns = "turns";
        public const string KeySeed = "seed";
        public const string KeyDifficulty = "difficulty";
        public const string KeyTurnNo = "turn_no";
        public const string KeyLivingPct = "living_pct";
        public const string KeySavingPct = "saving_pct";
        public const string KeyInvestPct = "invest_pct";
        public const string KeyWebsite = "website";
        public const string KeySavingsAfter = "savings_after";
        public const string KeyInvestAfter = "invest_after";
        public const string KeySatisfactionAfter = "satisfaction_after";

        // エラーコード（契約 2 の一覧）
        public const string ErrorSessionRequired = "session_required";
        public const string ErrorOriginForbidden = "origin_forbidden";
        public const string ErrorUnsupportedMediaType = "unsupported_media_type";
        public const string ErrorPayloadTooLarge = "payload_too_large";
        public const string ErrorBadRequest = "bad_request";
        public const string ErrorAllocationNegative = "allocation_negative";
        public const string ErrorAllocationStepMismatch = "allocation_step_mismatch";
        public const string ErrorAllocationSumMismatch = "allocation_sum_mismatch";
        public const string ErrorDifficultyMissing = "difficulty_missing";
        public const string ErrorDifficultyUnknown = "difficulty_unknown";
        public const string ErrorGameNotFound = "game_not_found";
        public const string ErrorTurnConflict = "turn_conflict";
        public const string ErrorTurnAllocationMismatch = "turn_allocation_mismatch";
        public const string ErrorGameFinished = "game_finished";
        public const string ErrorRateLimited = "rate_limited";
        public const string ErrorServiceUnavailable = "service_unavailable";
        public const string ErrorInternal = "internal_error";

        // 状態コードの範囲と、対応を分ける必要があるもの
        public const long StatusSuccessMin = 200;
        public const long StatusSuccessMax = 299;
        public const long StatusUnauthorized = 401;
        public const long StatusTooManyRequests = 429;
        public const long StatusServiceUnavailable = 503;

        // 応答を待つ秒数（超えたら通信の失敗として扱う）
        public const int TimeoutSeconds = 20;
    }
}
