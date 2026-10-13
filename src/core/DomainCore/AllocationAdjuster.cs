using System;
using System.Globalization;
using System.Linq;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 配分スライダーの自動調整（requirements.md 4.1-4）。1 本を動かすと、残り 2 本が比率を保って調整され、合計は常に 100 になる。
    /// 不変オブジェクト：Move は新しい AllocationAdjuster を返し、自分は変えない。
    ///
    /// 規則：
    /// - 動かした項目を新しい値にし、残り 2 本の合計を「100 − 新しい値」にする。
    /// - 端数は「最後に動かしていない項目」が吸収する。残り 2 本のうち、動かした時期が古い方。
    ///   もう一方は、比率どおりの値（残り × 自分の値 ÷ 2 本の合計）を 5 の倍数に切り捨てる。吸収する側は残りの全部を受け取る
    ///   （4.3-1 の「切り捨て、端数は貯蓄枠へ」と同じ形）。
    /// - まだ動かしていない項目の順序は GameRules.AdjusterInitialAbsorbOrder（初めは貯蓄が吸収する）。
    /// - 残り 2 本の合計が 0（動かした項目が 100 だった）のときは、比率が無いので 1:1（均等）とみなし、同じ規則で分ける。
    ///   例：100/0/0 から生活費を 90 にすると 90/5/5、95 にすると 95/5/0（貯蓄が吸収）。
    /// - 0% の項目は、もう一方が 0 でなければ 0 のまま（比率を保つ。0% は正当な選択：requirements.md 5章）。
    ///
    /// スライダーの値が 0〜100 の 5 刻みでないとき・項目が未定義のときは ArgumentOutOfRangeException、
    /// 初期値が正しい配分でないときは ArgumentException にする（呼び出し側の誤りであり、黙って直さない）。
    /// </summary>
    public sealed class AllocationAdjuster
    {
        // 残り 2 本の合計が 0 のときに使う重み。2 本を同じ重みにして、均等に分ける
        private const int EqualWeight = 1;

        // 3 項目を、動かした時期が古い順に並べたもの
        private readonly AllocationItem[] leastRecentFirst;

        /// <summary>初期の配分から始める。初期値が正しい配分でなければ ArgumentException。</summary>
        public AllocationAdjuster(Allocation initial)
            : this(RequireValid(initial), GameRules.AdjusterInitialAbsorbOrder.ToArray())
        {
        }

        private AllocationAdjuster(Allocation current, AllocationItem[] leastRecentFirst)
        {
            Current = current;
            this.leastRecentFirst = leastRecentFirst;
        }

        /// <summary>今の配分。常に正しい配分（0 以上・5 刻み・合計 100）。</summary>
        public Allocation Current { get; }

        /// <summary>item を newPct にし、残り 2 本を自動調整した、新しい AllocationAdjuster を返す。</summary>
        public AllocationAdjuster Move(AllocationItem item, int newPct)
        {
            RequireDefined(item);
            RequireSliderValue(newPct);

            // 残り 2 本を、動かした時期が古い順に並べる。先頭が端数を吸収し、もう一方は比率どおりの値を切り捨てる
            var others = leastRecentFirst.Where(other => other != item).ToArray();
            var absorber = others[0];
            var kept = others[1];

            var remaining = GameRules.AllocationTotalPct - newPct;
            var keptPct = TruncatedShare(remaining, Current.Get(kept), Current.Get(absorber));
            var next = Current.With(item, newPct).With(kept, keptPct).With(absorber, remaining - keptPct);

            var movedLast = leastRecentFirst.Where(other => other != item).Append(item).ToArray();
            return new AllocationAdjuster(next, movedLast);
        }

        /// <summary>
        /// remaining を keptWeight : absorberWeight で分けたときの、kept 側の取り分を 5 の倍数に切り捨てて返す。
        /// どの値も 0 以上なので、整数の割り算は切り捨てになる。重みが両方 0 なら 1:1 とみなす。
        /// </summary>
        private static int TruncatedShare(int remaining, int keptWeight, int absorberWeight)
        {
            if (keptWeight + absorberWeight == 0)
            {
                keptWeight = EqualWeight;
                absorberWeight = EqualWeight;
            }

            var step = GameRules.AllocationStepPct;
            return remaining * keptWeight / ((keptWeight + absorberWeight) * step) * step;
        }

        private static Allocation RequireValid(Allocation initial)
        {
            var validation = Simulator.ValidateAllocation(initial);
            if (!validation.IsValid)
            {
                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    ErrorMessages.InvalidInitialAllocation,
                    ReasonDescriptions.Describe(validation.Reason));
                throw new ArgumentException(message, nameof(initial));
            }

            return initial;
        }

        private static void RequireDefined(AllocationItem item)
        {
            if (!Enum.IsDefined(typeof(AllocationItem), item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), item, ErrorMessages.UndefinedAllocationItem);
            }
        }

        private static void RequireSliderValue(int newPct)
        {
            if (newPct < 0 || newPct > GameRules.AllocationTotalPct || !Simulator.IsOnStep(newPct))
            {
                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    ErrorMessages.SliderValueOutOfRange,
                    GameRules.AllocationTotalPct,
                    GameRules.AllocationStepPct);
                throw new ArgumentOutOfRangeException(nameof(newPct), newPct, message);
            }
        }
    }
}
