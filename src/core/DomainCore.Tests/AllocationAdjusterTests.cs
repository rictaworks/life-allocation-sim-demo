using System;
using System.Collections.Generic;
using System.Linq;
using DomainCore.Config;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// requirements.md 4.1-4：1 本を動かすと、残り 2 本が比率を保って自動調整され、合計は常に 100。
    /// 端数は「最後に動かしていない項目」（残り 2 本のうち、動かした時期が古い方）が吸収する。
    /// もう一方は比率どおりの値を 5 の倍数に切り捨てる。
    /// まだ動かしていないときは、貯蓄 → 生活費 → 投資 の順に古いものとみなす（初めは貯蓄が吸収する）。
    /// 残り 2 本の合計が 0 のときは、1:1（均等）とみなして同じ規則で分ける。
    /// </summary>
    public class AllocationAdjusterTests
    {
        private const int Step = 5;
        private const int Total = 100;

        [Fact]
        public void KeepsRatioOfOtherTwoAndSavingAbsorbsRemainderAtFirst()
        {
            // 生活費:貯蓄 = 50:30 を 60 で分けると 37.5:22.5。生活費は 35 に切り捨て、貯蓄が端数を吸収して 25
            var adjuster = new AllocationAdjuster(new Allocation(50, 30, 20));

            var moved = adjuster.Move(AllocationItem.Invest, 40);

            AssertAllocation(35, 25, 40, moved.Current);
        }

        [Fact]
        public void LeastRecentlyMovedItemAbsorbsRemainder()
        {
            var adjuster = new AllocationAdjuster(new Allocation(50, 30, 20));

            // 1 回目：投資を動かす → 貯蓄が吸収（初期の順序）
            var first = adjuster.Move(AllocationItem.Invest, 40);
            AssertAllocation(35, 25, 40, first.Current);

            // 2 回目：生活費を動かす → 残りは貯蓄と投資。投資は直前に動かしたので、貯蓄が吸収
            // 貯蓄:投資 = 25:40 を 50 で分けると 19.2:30.8。投資は 30、貯蓄は 20
            var second = first.Move(AllocationItem.Living, 50);
            AssertAllocation(50, 20, 30, second.Current);

            // 3 回目：貯蓄を動かす → 残りは生活費と投資。生活費は直前に動かしたので、投資が吸収
            // 生活費:投資 = 50:30 を 70 で分けると 43.75:26.25。生活費は 40、投資は 30
            var third = second.Move(AllocationItem.Saving, 30);
            AssertAllocation(40, 30, 30, third.Current);
        }

        [Fact]
        public void ItemMovedJustBeforeDoesNotAbsorb()
        {
            // 生活費を動かした直後に貯蓄を動かすと、吸収するのは投資（生活費ではない）
            // 1 回目：50/30/20 の生活費を 40 → 貯蓄:投資 = 30:20 を 60 で分ける。投資 24 → 20、貯蓄 40
            // 2 回目：貯蓄を 20 → 生活費:投資 = 40:20 を 80 で分ける。生活費 53.3 → 50、投資が吸収して 30
            var adjuster = new AllocationAdjuster(new Allocation(50, 30, 20))
                .Move(AllocationItem.Living, 40);
            AssertAllocation(40, 40, 20, adjuster.Current);

            var moved = adjuster.Move(AllocationItem.Saving, 20);

            AssertAllocation(50, 20, 30, moved.Current);
        }

        [Theory]
        [InlineData(100, 0, 0, AllocationItem.Living, 90, 90, 5, 5)]
        [InlineData(100, 0, 0, AllocationItem.Living, 95, 95, 5, 0)]
        [InlineData(100, 0, 0, AllocationItem.Living, 0, 0, 50, 50)]
        [InlineData(0, 100, 0, AllocationItem.Saving, 80, 10, 80, 10)]
        [InlineData(0, 100, 0, AllocationItem.Saving, 85, 10, 85, 5)]
        [InlineData(0, 0, 100, AllocationItem.Invest, 85, 5, 10, 85)]
        public void SplitsEvenlyWhenOtherTwoSumToZero(
            int living, int saving, int invest, AllocationItem item, int value, int expectedLiving, int expectedSaving, int expectedInvest)
        {
            // 残り 2 本がどちらも 0 で比率が無いときは、1:1 とみなす。半分を 5 の倍数に切り捨て、端数は吸収する側へ
            var moved = new AllocationAdjuster(new Allocation(living, saving, invest)).Move(item, value);

            AssertAllocation(expectedLiving, expectedSaving, expectedInvest, moved.Current);
        }

        [Theory]
        [InlineData(80, 20, 0, AllocationItem.Saving, 40, 60, 40, 0)]
        [InlineData(80, 0, 20, AllocationItem.Living, 50, 50, 0, 50)]
        [InlineData(0, 70, 30, AllocationItem.Invest, 50, 0, 50, 50)]
        public void KeepsZeroItemAtZero(
            int living, int saving, int invest, AllocationItem item, int value, int expectedLiving, int expectedSaving, int expectedInvest)
        {
            // 0% は正当な選択（requirements.md 5章）。比率を保つので、残り 2 本の片方が 0 なら 0 のまま
            var moved = new AllocationAdjuster(new Allocation(living, saving, invest)).Move(item, value);

            AssertAllocation(expectedLiving, expectedSaving, expectedInvest, moved.Current);
        }

        [Theory]
        [InlineData(AllocationItem.Living, 100, 100, 0, 0)]
        [InlineData(AllocationItem.Living, 0, 0, 60, 40)]
        [InlineData(AllocationItem.Living, 50, 50, 30, 20)]
        public void MovesToBoundsAndToSameValue(AllocationItem item, int value, int expectedLiving, int expectedSaving, int expectedInvest)
        {
            var moved = new AllocationAdjuster(new Allocation(50, 30, 20)).Move(item, value);

            AssertAllocation(expectedLiving, expectedSaving, expectedInvest, moved.Current);
        }

        [Fact]
        public void MoveReturnsNewAdjusterAndKeepsOriginal()
        {
            var original = new AllocationAdjuster(new Allocation(50, 30, 20));

            var moved = original.Move(AllocationItem.Invest, 40);

            Assert.NotSame(original, moved);
            AssertAllocation(50, 30, 20, original.Current);
        }

        [Fact]
        public void EverySingleMoveKeepsTotalAndRatio()
        {
            // 正しい配分 231 通り × 3 項目 × 21 値の全数で、性質を確かめる
            var cases = 0;
            foreach (var start in ValidAllocations())
            {
                foreach (var item in Items())
                {
                    for (var value = 0; value <= Total; value += Step)
                    {
                        var moved = new AllocationAdjuster(start).Move(item, value).Current;
                        var absorber = InitialAbsorberFor(item);
                        var kept = Items().Single(other => other != item && other != absorber);

                        AssertValid(moved);
                        Assert.Equal(value, moved.Get(item));
                        AssertKeptShare(start.Get(kept), start.Get(absorber), Total - value, moved.Get(kept));
                        Assert.Equal(Total - value - moved.Get(kept), moved.Get(absorber));
                        cases++;
                    }
                }
            }

            Assert.Equal(231 * 3 * 21, cases);
        }

        [Fact]
        public void LongSequencesOfMovesKeepTotal()
        {
            // 連続して動かしても、合計 100・5 刻み・0 以上が保たれ、動かした項目は指定どおりの値になる
            var items = Items().ToArray();
            var startIndex = 0;
            foreach (var start in ValidAllocations())
            {
                var adjuster = new AllocationAdjuster(start);
                for (var step = 0; step < 20; step++)
                {
                    var item = items[(step + startIndex) % items.Length];
                    var value = ((step * 7) + startIndex) % 21 * Step;

                    adjuster = adjuster.Move(item, value);

                    AssertValid(adjuster.Current);
                    Assert.Equal(value, adjuster.Current.Get(item));
                }

                startIndex++;
            }
        }

        [Theory]
        [InlineData(-5)]
        [InlineData(105)]
        [InlineData(7)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void RejectsSliderValueOutsideRangeOrStep(int value)
        {
            var adjuster = new AllocationAdjuster(new Allocation(50, 30, 20));

            Assert.Throws<ArgumentOutOfRangeException>(() => adjuster.Move(AllocationItem.Living, value));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        public void RejectsUndefinedItem(int item)
        {
            var adjuster = new AllocationAdjuster(new Allocation(50, 30, 20));

            Assert.Throws<ArgumentOutOfRangeException>(() => adjuster.Move((AllocationItem)item, 50));
        }

        [Theory]
        [InlineData(50, 30, 15)]
        [InlineData(-5, 55, 50)]
        [InlineData(33, 33, 34)]
        public void RejectsInvalidInitialAllocation(int living, int saving, int invest)
        {
            var error = Assert.Throws<ArgumentException>(() => new AllocationAdjuster(new Allocation(living, saving, invest)));

            var reason = Simulator.ValidateAllocation(new Allocation(living, saving, invest)).Reason;
            Assert.Contains(ReasonDescriptions.Describe(reason), error.Message);
        }

        [Fact]
        public void InitialAbsorbOrderIsSavingLivingInvest()
        {
            Assert.Equal(
                new[] { AllocationItem.Saving, AllocationItem.Living, AllocationItem.Invest },
                GameRules.AdjusterInitialAbsorbOrder);
        }

        private static IEnumerable<AllocationItem> Items() =>
            new[] { AllocationItem.Living, AllocationItem.Saving, AllocationItem.Invest };

        private static IEnumerable<Allocation> ValidAllocations()
        {
            for (var living = 0; living <= Total; living += Step)
            {
                for (var saving = 0; saving <= Total - living; saving += Step)
                {
                    yield return new Allocation(living, saving, Total - living - saving);
                }
            }
        }

        private static AllocationItem InitialAbsorberFor(AllocationItem moved) =>
            moved == AllocationItem.Saving ? AllocationItem.Living : AllocationItem.Saving;

        /// <summary>
        /// 切り捨てた値 kept は、比率どおりの値（remaining × keptWeight ÷ 合計の重み）を超えない最大の 5 の倍数。
        /// 重みが両方 0 のときは 1:1 とみなす。整数の掛け算で確かめる（実数を使わない）。
        /// </summary>
        private static void AssertKeptShare(int keptWeight, int absorberWeight, int remaining, int kept)
        {
            if (keptWeight + absorberWeight == 0)
            {
                keptWeight = 1;
                absorberWeight = 1;
            }

            var weightTotal = keptWeight + absorberWeight;
            Assert.Equal(0, kept % Step);
            Assert.True(kept * weightTotal <= remaining * keptWeight, $"切り捨て後の値 {kept} が比率どおりの値を超えています");
            Assert.True(remaining * keptWeight < (kept + Step) * weightTotal, $"切り捨て後の値 {kept} が小さすぎます");
        }

        private static void AssertValid(Allocation allocation)
        {
            Assert.True(Simulator.ValidateAllocation(allocation).IsValid, $"{allocation.LivingPct}/{allocation.SavingPct}/{allocation.InvestPct} が正しくありません");
        }

        private static void AssertAllocation(int living, int saving, int invest, Allocation actual)
        {
            Assert.Equal(new[] { living, saving, invest }, new[] { actual.LivingPct, actual.SavingPct, actual.InvestPct });
        }
    }
}
