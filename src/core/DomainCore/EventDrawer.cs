using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// ライフイベントの抽選（requirements.md 4.2 drawEvent）。
    /// - 候補は、その年齢で発生しうるイベント（一度きりで発生済みのものは外す）。並びは LifeEventTable の順で固定する。
    /// - 乱数は 1 年に必ず 1 回だけ、SeededRandom.NextInt で引く（22 歳の初年度でも引く）。NextInt は棄却法で引き直すことがあるため、
    ///   内部で使う 64 ビットの値の数はシードによって変わるが、同じシードからは同じ結果になる。
    /// - 万分率の抽選：0〜9999 の一様な値を、候補の確率の累積で区切る。合計が 10000 未満なら、残りの分は「発生なし」。
    /// </summary>
    public static class EventDrawer
    {
        /// <summary>この年のイベントを抽選する。乱数は 1 回だけ使う。</summary>
        public static EventDraw Draw(PlayerState state, SeededRandom random)
        {
            return Pick(state, random.NextInt(GameRules.BasisPointsPerUnit));
        }

        /// <summary>抽選の値（0 以上 10000 未満）から、イベントを決める。乱数は使わない。</summary>
        internal static EventDraw Pick(PlayerState state, int roll)
        {
            if (state.Age == GameRules.StartAge)
            {
                return EventDraw.None();
            }

            var cumulativeBp = 0;
            foreach (var lifeEvent in LifeEventTable.All)
            {
                if (!IsCandidate(lifeEvent, state))
                {
                    continue;
                }

                cumulativeBp += lifeEvent.ProbabilityBp;
                if (roll < cumulativeBp)
                {
                    return EventDraw.Of(lifeEvent);
                }
            }

            return EventDraw.None();
        }

        internal static bool IsCandidate(LifeEvent lifeEvent, PlayerState state)
        {
            var inAgeRange = lifeEvent.MinAge <= state.Age && state.Age <= lifeEvent.MaxAge;
            var alreadyOccurredOnce = lifeEvent.OccursOnce && state.HasOccurred(lifeEvent.Code);
            return inAgeRange && !alreadyOccurredOnce;
        }
    }
}
