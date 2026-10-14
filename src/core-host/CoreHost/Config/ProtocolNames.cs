namespace CoreHost.Config
{
    /// <summary>
    /// JSON のプロパティ名・操作の名前・状態の文字列（Protocol.md の形と一致させる）。
    /// 名前は、ここにだけ書く（コードに直接書かない）。
    /// </summary>
    public static class ProtocolNames
    {
        // 操作の名前（コマンド引数）
        public const string InitCommand = "init";

        public const string ReplayCommand = "replay";

        // 要求
        public const string Difficulty = "difficulty";

        public const string Seed = "seed";

        public const string History = "history";

        public const string LivingPct = "living_pct";

        public const string SavingPct = "saving_pct";

        public const string InvestPct = "invest_pct";

        // 応答：状態（HOST_STATE）
        public const string State = "state";

        public const string Age = "age";

        public const string IncomeYen = "income_yen";

        public const string SavingsYen = "savings_yen";

        public const string InvestmentYen = "investment_yen";

        public const string Satisfaction = "satisfaction";

        public const string PriceIndexBp = "price_index_bp";

        public const string Status = "status";

        public const string Struggling = "struggling";

        public const string RaiseTotalYen = "raise_total_yen";

        public const string OccurredOnceEvents = "occurred_once_events";

        // 状態の文字列（GAME.status の 3 つ。生活困窮は status が playing で struggling が true）
        public const string StatusPlaying = "playing";

        public const string StatusCompleted = "completed";

        public const string StatusBankrupt = "bankrupt";

        // 応答：年（TURN）
        public const string Turns = "turns";

        public const string TurnNo = "turn_no";

        public const string EventCode = "event_code";

        public const string SavingsAfter = "savings_after";

        public const string InvestAfter = "invest_after";

        public const string SatisfactionAfter = "satisfaction_after";

        // 応答：評価（HOST_EVALUATION）
        public const string Evaluation = "evaluation";

        public const string TotalAssetsYen = "total_assets_yen";

        public const string AverageSatisfaction = "average_satisfaction";

        public const string IsBankrupt = "is_bankrupt";

        public const string StruggleYears = "struggle_years";

        public const string InvestmentSoldYears = "investment_sold_years";

        public const string Rank = "rank";

        public const string Comments = "comments";

        public const string RetirementComment = "retirement";

        public const string QualityComment = "quality";

        public const string PreparednessComment = "preparedness";

        // 応答：エラー
        public const string Error = "error";

        public const string ErrorCode = "code";

        public const string ErrorMessage = "message";
    }
}
