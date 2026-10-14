using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// ライフイベントの抽選（requirements.md 4.2 drawEvent）。
    /// - 候補は、その年齢で発生しうるイベント（一度きりで発生済みのものは外す）。並びは LifeEventTable の順で固定する。
    /// - 乱数は 1 年に必ず 1 回だけ使う（22 歳の初年度でも使う）。そのため、乱数の位置は年数だけで決まる。
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
