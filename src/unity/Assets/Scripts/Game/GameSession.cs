using System.Collections.Generic;
using DomainCore;
using LifeAllocationSim.Config;

namespace LifeAllocationSim.Game
{
    /// <summary>
    /// 進行中のゲーム。配分の履歴と seed から、Domain Core の再計算で状態を求める。
    /// 乱数の続きは再計算の中で正しく使われるため、年を 1 つずつ別々に計算しない。
    /// </summary>
    public sealed class GameSession
    {
        private readonly List<Allocation> history;

        private GameSession(string difficultyCode, GameStart start, long seed, List<Allocation> history, ReplayOutcome outcome)
        {
            DifficultyCode = difficultyCode;
            Start = start;
            Seed = seed;
            this.history = history;
            Outcome = outcome;
        }

        public string DifficultyCode { get; }

        public GameStart Start { get; }

        public long Seed { get; }

        /// <summary>今までの再計算の結果（年ごとの内訳と、今の状態）。</summary>
        public ReplayOutcome Outcome { get; private set; }

        public IReadOnlyList<Allocation> History => history;

        public PlayerState State => Outcome.FinalState;

        public IReadOnlyList<YearResult> Years => Outcome.Years;

        public bool IsFinished => Outcome.IsFinished;

        /// <summary>次のターンの番号（1 から始まる）。</summary>
        public int NextTurnNo => history.Count + 1;

        /// <summary>初期表示の配分。前の年の配分を引き継ぐ。最初の年は既定の配分。</summary>
        public Allocation SuggestedAllocation =>
            history.Count == 0
                ? new Allocation(AppConfig.DefaultLivingPct, AppConfig.DefaultSavingPct, AppConfig.DefaultInvestPct)
                : history[history.Count - 1];

        /// <summary>
        /// 保存済みの履歴から、ゲームを復元する。始まりか再計算が受け付けなければ false（session は null）。
        /// </summary>
        public static bool TryRestore(string difficultyCode, long seed, IReadOnlyList<Allocation> history, out GameSession session)
        {
            session = null;
            var start = GameEngine.StartNew(difficultyCode);
            if (!start.IsValid)
            {
                return false;
            }

            var result = GameEngine.Replay(start.Value, seed, history);
            if (!result.IsValid)
            {
                return false;
            }

            session = new GameSession(difficultyCode, start.Value, seed, new List<Allocation>(history), result.Outcome);
            return true;
        }

        /// <summary>次の年の配分を加えたときの再計算の結果。保存の前に確かめるためで、この時点では履歴を変えない。</summary>
        public ReplayResult PreviewNext(Allocation allocation)
        {
            var extended = new List<Allocation>(history) { allocation };
            return GameEngine.Replay(Start, Seed, extended);
        }

        /// <summary>保存が確かめられた年の配分を、履歴に加える。</summary>
        public void Commit(Allocation allocation, ReplayOutcome outcome)
        {
            history.Add(allocation);
            Outcome = outcome;
        }
    }
}
