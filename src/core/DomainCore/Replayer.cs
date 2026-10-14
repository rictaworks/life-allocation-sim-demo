using System.Collections.Generic;

namespace DomainCore
{
    /// <summary>
    /// 配分履歴からの状態の再構築（requirements.md 4.5 replayFromHistory）。
    /// 初期状態・難易度・シード・配分の列から、年次精算（YearSettlement.Advance）を 1 年ずつ順に適用する。
    /// 保存データの改ざん検知と再開に使う。同じ入力からは、常に同じ結果になる。
    /// - 不正な配分が 1 つでもあれば、その年（ターン番号・年齢）と理由を返す。
    /// - 完走か破綻のあとのターンは、GameFinished の理由で拒否する。
    /// </summary>
    public static class Replayer
    {
        /// <summary>配分の列を順に適用する。失敗したら、どのターンで何が起きたかを返す。</summary>
        public static ReplayResult Replay(GameStart start, long seed, IReadOnlyList<Allocation> history)
        {
            var random = new SeededRandom(seed);
            var state = start.InitialState;
            var years = new List<YearResult>(history.Count);

            for (var index = 0; index < history.Count; index++)
            {
                var turnNo = index + 1;
                if (state.IsFinished)
                {
                    return ReplayResult.Invalid(new ReplayFailure(turnNo, state.Age, ReplayViolation.GameFinished, null));
                }

                var step = YearSettlement.Advance(state, history[index], start.Difficulty, random);
                if (!step.IsValid)
                {
                    return ReplayResult.Invalid(new ReplayFailure(turnNo, state.Age, ReplayViolation.InvalidAllocation, step.Reason));
                }

                years.Add(step.Value);
                state = step.Value.Next;
            }

            var evaluation = state.IsFinished ? Evaluator.Evaluate(state, years) : null;
            return ReplayResult.Valid(new ReplayOutcome(years, state, evaluation));
        }
    }
}
