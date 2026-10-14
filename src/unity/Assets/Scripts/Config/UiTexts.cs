namespace LifeAllocationSim.Config
{
    /// <summary>
    /// 画面に出す文字列（すべて ですます調）。数値は {0} などの書式に string.Format で埋める。
    /// 文字列リテラルは Config/ にだけ書く（tests/static-checks.test.mjs が検査する）。
    /// </summary>
    public static class UiTexts
    {
        // 共通
        public const string AppTitle = "人生配分シミュレーション";
        public const string Disclaimer = "利回り・イベント・物価は、すべて架空のモデル値です。実在の金融商品や個別の投資判断を示すものではありません。";
        public const string DataResetInfo = "保存されたデータは、毎日 日本時間の午前3時に削除されます。";
        public const string Busy = "通信中です。しばらくお待ちください。";
        public const string BackToTitle = "タイトルへ戻る";
        public const string Next = "次へ";
        public const string Retry = "再試行";
        public const string Reload = "最新の状態を読み込む";

        // タイトル
        public const string TitleIntroFormat = "給料を「生活費・貯蓄・投資」に配分し、{0}歳から{1}歳までの資産推移を体験できます。";
        public const string DifficultyHeading = "難易度（市場の振れ幅）";
        public const string DifficultySelectedFormat = "{0}（選択中）";
        public const string NewGame = "新規開始";
        public const string ResumeGame = "続きから";
        public const string ResumeUnavailable = "続きから遊べるゲームはありません。";
        public const string OverwriteHeading = "新しく始めますか？";
        public const string OverwriteBody = "保存されているゲームは上書きされ、元に戻せません。新しく始めてよろしいですか？";
        public const string OverwriteYes = "はい、上書きして始めます";
        public const string OverwriteNo = "いいえ、やめます";

        // メイン
        public const string AgeFormat = "年齢：{0}歳";
        public const string YearProgressFormat = "{0}年目（全{1}年）";
        public const string StatusFormat = "状態：{0}";
        public const string StatusPlaying = "プレイ中";
        public const string StatusStruggling = "生活困窮";
        public const string StatusBankrupt = "破綻";
        public const string StatusCompleted = "完走";
        public const string StruggleWarning = "満足度が低い状態です（生活困窮）。生活費の配分を見直してみてください。";
        public const string IncomeFormat = "手取り年収：{0}円";
        public const string SavingsFormat = "貯蓄残高：{0}円";
        public const string InvestmentFormat = "投資評価額：{0}円";
        public const string SatisfactionFormat = "生活満足度：{0} / {1}";
        public const string AllocationHeading = "今年の配分（5% 刻み・合計 100%）";
        public const string LivingLabel = "生活費";
        public const string SavingLabel = "貯蓄";
        public const string InvestLabel = "投資";
        public const string AllocationValueFormat = "{0}：{1}%";
        public const string AllocationNote = "初期値は前の年の配分です。変更しなければ、そのまま「次の年へ」を押せます。";
        public const string GraphHeading = "資産推移（貯蓄と投資を積み上げ）";
        public const string LegendSavings = "貯蓄：無地の帯";
        public const string LegendInvestment = "投資：斜線の帯";
        public const string GraphMaxFormat = "最大 {0}万円";
        public const string GraphAgeFormat = "{0}歳";
        public const string NextYear = "次の年へ";

        // イベント
        public const string EventHeading = "今年の出来事";
        public const string EventAgeFormat = "{0}歳の年の出来事";
        public const string EventNone = "今年は特別な出来事はありませんでした。";
        public const string EventCategoryFormat = "分類：{0}";
        public const string CategoryExpense = "出費系";
        public const string CategoryIncome = "収入系";
        public const string CategoryMarket = "市場系";
        public const string EventExpenseFormat = "出費：{0}円（貯蓄から支払います）";
        public const string EventIncomeFormat = "臨時の収入：{0}円";
        public const string EventRaiseFormat = "昇給：手取り年収が{0}円増えました。";
        public const string EventMarketFormat = "市場の変動：今年の投資のリターンは{0}%になりました。";

        // 年次結果
        public const string YearResultHeadingFormat = "{0}歳の年の結果";
        public const string ResultIncomeFormat = "手取り年収：{0}円";
        public const string ResultLivingBudgetFormat = "生活費の枠：{0}円";
        public const string ResultRequiredLivingFormat = "必要な生活費：{0}円";
        public const string ResultSurplusFormat = "余剰（貯蓄へ）：{0}円";
        public const string ResultShortfallFormat = "不足（取り崩しへ）：{0}円";
        public const string ResultSavingDepositFormat = "貯蓄への積立：{0}円";
        public const string ResultInvestDepositFormat = "投資への積立：{0}円";
        public const string ResultEventExpenseFormat = "イベントの支出：{0}円";
        public const string ResultEventIncomeFormat = "イベントの収入：{0}円";
        public const string ResultMarketReturnFormat = "今年の市場リターン：{0}%";
        public const string ResultSavingsInterestFormat = "貯蓄の利息：{0}円";
        public const string ResultInvestmentGainFormat = "投資の損益：{0}円";
        public const string ResultWithdrawnSavingsFormat = "貯蓄から取り崩した額：{0}円";
        public const string ResultWithdrawnInvestmentFormat = "投資を売却して取り崩した額：{0}円";
        public const string ResultUnmetFormat = "取り崩しても不足した額：{0}円";
        public const string ResultSatisfactionFormat = "年末の生活満足度：{0}";
        public const string ResultBankrupt = "取り崩しても生活費が不足したため、ゲームは終了しました。";
        public const string ResultCompletedFormat = "{0}歳に達したため、ゲームは完了です。";
        public const string ResultStruggling = "満足度が低い状態です（生活困窮）。";

        // 最終評価
        public const string FinalHeading = "最終評価";
        public const string RankFormat = "ランク {0}";
        public const string FinalTotalFormat = "総資産（貯蓄と投資評価額の合計）：{0}円";
        public const string FinalAverageSatisfactionFormat = "平均満足度：{0}";
        public const string FinalStruggleYearsFormat = "生活困窮だった年数：{0}年";
        public const string FinalInvestmentSoldYearsFormat = "投資を売却した年数：{0}年";
        public const string FinalBankrupt = "結果：破綻しました。";
        public const string FinalCompletedFormat = "結果：{0}歳まで完走しました。";
        public const string CommentRetirementHeading = "老後資金";
        public const string CommentQualityHeading = "生活の質";
        public const string CommentPreparednessHeading = "備え";
        public const string FullGraphHeading = "全期間の資産推移";
        public const string PlayAgain = "もう一度遊ぶ";

        // お知らせとエラー
        public const string ResetNotice = "データがリセットされました。新しく始めてください。";
        public const string ResumeMissingNotice = "保存されたゲームが見つかりませんでした。新しく始めてください。";
        public const string ErrorHeading = "お知らせ";
        public const string ErrorGeneric = "問題が発生しました。時間をおいて、もう一度お試しください。";
        public const string ErrorNetwork = "通信できませんでした。接続を確認して、もう一度お試しください。";
        public const string ErrorRateLimited = "アクセスが集中しています。少し時間をおいてから、もう一度お試しください。";
        public const string ErrorUnavailable = "ただいま混み合っています。少し時間をおいてから、もう一度お試しください。";
        public const string ErrorSession = "セッションを確認できませんでした。ページを再読み込みしてください。";
        public const string ErrorAllocation = "配分が正しくありません。配分を確認してから、もう一度お試しください。";
        public const string ErrorDifficulty = "難易度が正しくありません。選び直してください。";
        public const string ErrorConflict = "保存されたデータが別の操作で更新されていました。最新の状態を読み込んでください。";
        public const string ErrorFinished = "このゲームはすでに終了しています。結果を確認してください。";
        public const string ErrorDataUnreadable = "保存されたデータを読み込めませんでした。新しく始めてください。";
        public const string ErrorMismatch = "保存された結果を確認できませんでした。最新の状態を読み込んでください。";
    }
}
