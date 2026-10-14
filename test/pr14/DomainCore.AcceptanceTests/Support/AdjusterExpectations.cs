using Xunit;

namespace DomainCore.AcceptanceTests.Support
{
    /// <summary>
    /// 受け入れ条件 2（requirements.md 4.1-4）の判定。1 回の Move の結果が次を満たすかを確かめる。
    /// - 正しい配分（各項目が 0〜100 の 5 刻みで、合計 100）で、動かした項目は指定した値。
    /// - 残り 2 本は比率を保つ。比率どおりの値は「残り × 自分の値 ÷ 2 本の合計」。
    ///   吸収しない側は、比率どおりの値を超えない最大の 5 の倍数（切り捨て）。
    ///   吸収する側（最後に動かしていない項目）は残りの全部を受け取り、比率どおりの値以上・それ + 5 未満になる。
    /// - 残り 2 本の合計が 0 のときは 1:1 とみなす（PR 本文の決め事）。
    /// 実数は使わず、整数の掛け算で比べる。
    /// </summary>
    internal static class AdjusterExpectations
    {
        public static void AssertMove(Allocation before, AllocationItem item, int value, MoveHistory history, Allocation after)
        {
            var values = new[] { after.LivingPct, after.SavingPct, after.InvestPct };
            foreach (var pct in values)
            {
                if (pct < 0 || pct > AllocationSpace.Total || pct % AllocationSpace.Step != 0)
                {
                    Assert.Fail($"{Context(before, item, value, after)}：0〜100 の 5 刻みでない値があります");
                }
            }

            if (after.LivingPct + after.SavingPct + after.InvestPct != AllocationSpace.Total)
            {
                Assert.Fail($"{Context(before, item, value, after)}：合計が 100 ではありません");
            }

            if (after.Get(item) != value)
            {
                Assert.Fail($"{Context(before, item, value, after)}：動かした項目が指定の値になっていません");
            }

            var absorber = history.Absorber(item);
            var kept = history.Kept(item);
            long remaining = AllocationSpace.Total - value;
            long keptWeight = before.Get(kept);
            long absorberWeight = before.Get(absorber);
            if (keptWeight + absorberWeight == 0)
            {
                keptWeight = 1;
                absorberWeight = 1;
            }

            var weightTotal = keptWeight + absorberWeight;
            long keptValue = after.Get(kept);
            long absorberValue = after.Get(absorber);
            long step = AllocationSpace.Step;

            // 吸収しない側：keptValue ≦ 比率どおりの値 < keptValue + 5（5 の倍数への切り捨て）
            var keptIsTruncatedShare = keptValue % step == 0
                && keptValue * weightTotal <= remaining * keptWeight
                && remaining * keptWeight < (keptValue + step) * weightTotal;
            if (!keptIsTruncatedShare)
            {
                Assert.Fail($"{Context(before, item, value, after)}：吸収しない側（{kept}）が、比率どおりの値を 5 の倍数に切り捨てた値ではありません");
            }

            // 吸収する側：比率どおりの値 ≦ absorberValue < 比率どおりの値 + 5
            var absorberTakesRemainder = absorberValue * weightTotal >= remaining * absorberWeight
                && absorberValue * weightTotal < (remaining * absorberWeight) + (step * weightTotal);
            if (!absorberTakesRemainder)
            {
                Assert.Fail($"{Context(before, item, value, after)}：最後に動かしていない項目（{absorber}）が端数を吸収していません");
            }
        }

        private static string Context(Allocation before, AllocationItem item, int value, Allocation after) =>
            $"{AllocationSpace.Describe(before)}（生活費/貯蓄/投資）の {item} を {value} にした結果 {AllocationSpace.Describe(after)}";
    }
}
