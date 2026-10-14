using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 新規ゲームの初期状態（requirements.md 3.1・3.3）。難易度コードの検証（#2）の結果を含み、失敗は理由コードで返す。
    /// 乱数は使わない（初期状態は決まっている）。
    /// </summary>
    public static class NewGame
    {
        /// <summary>難易度コードから、新規ゲームの開始情報を作る。コードが不正なら理由（未指定・不明）を返す。</summary>
        public static ValidationResult<GameStart, DifficultyCodeViolation> Start(string difficultyCode)
        {
            var validation = Simulator.ValidateDifficultyCode(difficultyCode);
            if (!validation.IsValid)
            {
                return ValidationResult<GameStart, DifficultyCodeViolation>.Invalid(validation.Reason);
            }

            var initial = new PlayerState(
                GameRules.StartAge,
                LifeStage.ForAge(GameRules.StartAge).TakeHomeIncomeYen,
                0,
                0,
                GameRules.InitialSatisfaction,
                GameRules.InitialPriceIndexBp,
                PlayerStatus.Playing,
                0,
                new string[0]);

            return ValidationResult<GameStart, DifficultyCodeViolation>.Valid(new GameStart(validation.Value, initial));
        }
    }
}
