using System;
using System.Linq;
using DomainCore.AcceptanceTests.Support;
using Xunit;

namespace DomainCore.AcceptanceTests
{
    /// <summary>
    /// 受け入れ条件 2：AllocationAdjuster（requirements.md 4.1-4）。1 本を動かすと、残り 2 本が比率を保って自動調整され、合計が常に 100 になる。
    /// 端数は最後に動かしていない項目で吸収する。残り 2 本の合計が 0 のときの扱いを決め、テストで固定する。
    /// 判定は Support/AdjusterExpectations（比率と端数の吸収）と Support/MoveHistory（最後に動かした時刻）で行う。
    /// </summary>
    public class Criterion2AllocationAdjusterTests
    {
        private const int RandomRuns = 2000;
        private const int MovesPerRun = 50;
        private const ulong InputSeed = 0x2026_1013_0000_0014UL;

        [Fact]
        public void EveryTwoMoveSequenceFromEveryValidStartFollowsTheRules()
        {
            // 正しい配分 231 通りから、2 回続けて動かす全通り（231 × (3 項目 × 21 値)^2 = 916,839 通り）。2 回目は 1 回目の履歴を引き継ぐ
            var sequences = 0;
            foreach (var start in AllocationSpace.AllValid())
            {
                var adjuster = new AllocationAdjuster(start);
                var initial = MoveHistory.Initial();
                foreach (var firstItem in AllocationSpace.Items())
                {
                    foreach (var firstValue in AllocationSpace.SliderValues())
                    {
                        var first = adjuster.Move(firstItem, firstValue);
                        AdjusterExpectations.AssertMove(start, firstItem, firstValue, initial, first.Current);
                        var history = initial.After(firstItem);
                        foreach (var secondItem in AllocationSpace.Items())
                        {
                            foreach (var secondValue in AllocationSpace.SliderValues())
                            {
                                var second = first.Move(secondItem, secondValue);
                                AdjusterExpectations.AssertMove(first.Current, secondItem, secondValue, history, second.Current);
                                sequences++;
                            }
                        }
                    }
                }
            }

            var movesPerStep = AllocationSpace.Items().Length * AllocationSpace.SliderValueCount;
            Assert.Equal(AllocationSpace.ValidAllocationCount * movesPerStep * movesPerStep, sequences);
        }

        [Fact]
        public void LongSequencesOfMovesFollowTheRules()
        {
            // 2,000 通りの開始から、50 回ずつ続けて動かす（項目と値はテスト側の xorshift64 で決める）
            var input = new XorShift64(InputSeed);
            var starts = AllocationSpace.AllValid().ToArray();
            var items = AllocationSpace.Items();
            for (var run = 0; run < RandomRuns; run++)
            {
                var adjuster = new AllocationAdjuster(starts[input.NextBelow(starts.Length)]);
                var history = MoveHistory.Initial();
                for (var move = 0; move < MovesPerRun; move++)
                {
                    var item = items[input.NextBelow(items.Length)];
                    var value = input.NextBelow(AllocationSpace.SliderValueCount) * AllocationSpace.Step;
                    var before = adjuster.Current;

                    adjuster = adjuster.Move(item, value);

                    AdjusterExpectations.AssertMove(before, item, value, history, adjuster.Current);
                    history = history.After(item);
                }
            }
        }

        [Fact]
        public void TheItemNotMovedForTheLongestTimeAbsorbsTheRemainder()
        {
            // 40/30/30 の貯蓄を 35 にする：残りは生活費と投資。まだどちらも動かしていないので、初めの順序（貯蓄 → 生活費 → 投資）で生活費が吸収する。
            //   生活費:投資 = 40:30 を 65 で分けると 37.1:27.9。投資は 25 に切り捨て、生活費が 40
            var first = new AllocationAdjuster(new Allocation(40, 30, 30)).Move(AllocationItem.Saving, 35);
            AssertAllocation(40, 35, 25, first.Current);

            // 続けて投資を 20 にする：残りは生活費と貯蓄。貯蓄は直前に動かしたので、最後に動かしていない生活費が吸収する。
            //   生活費:貯蓄 = 40:35 を 80 で分けると 42.7:37.3。貯蓄は 35 に切り捨て、生活費が 45（貯蓄が吸収すると 40/40/20 になる）
            var second = first.Move(AllocationItem.Invest, 20);
            AssertAllocation(45, 35, 20, second.Current);
        }

        [Theory]
        [InlineData(100, 0, 0, AllocationItem.Living, 90, 90, 5, 5)]
        [InlineData(100, 0, 0, AllocationItem.Living, 95, 95, 5, 0)]
        [InlineData(100, 0, 0, AllocationItem.Living, 0, 0, 50, 50)]
        [InlineData(0, 100, 0, AllocationItem.Saving, 85, 10, 85, 5)]
        [InlineData(0, 0, 100, AllocationItem.Invest, 85, 5, 10, 85)]
        public void SplitsOneToOneWhenTheOtherTwoSumToZero(
            int living, int saving, int invest, AllocationItem item, int value, int expectedLiving, int expectedSaving, int expectedInvest)
        {
            // PR 本文の決め事：残り 2 本の合計が 0 なら 1:1 とみなす。例：100/0/0 から生活費を 90 にすると 90/5/5、95 にすると 95/5/0（貯蓄が吸収）
            var moved = new AllocationAdjuster(new Allocation(living, saving, invest)).Move(item, value);

            AssertAllocation(expectedLiving, expectedSaving, expectedInvest, moved.Current);
        }

        [Theory]
        [InlineData(80, 20, 0, AllocationItem.Saving, 40, 60, 40, 0)]
        [InlineData(50, 0, 50, AllocationItem.Living, 20, 20, 0, 80)]
        public void KeepsAZeroItemAtZeroWhenTheOtherIsNotZero(
            int living, int saving, int invest, AllocationItem item, int value, int expectedLiving, int expectedSaving, int expectedInvest)
        {
            // 0% は正当な選択（requirements.md 5章）。比率を保つので、残り 2 本の片方が 0 なら 0 のまま
            var moved = new AllocationAdjuster(new Allocation(living, saving, invest)).Move(item, value);

            AssertAllocation(expectedLiving, expectedSaving, expectedInvest, moved.Current);
        }

        [Fact]
        public void MoveReturnsANewAdjusterAndKeepsTheOriginal()
        {
            var original = new AllocationAdjuster(new Allocation(50, 30, 20));

            var moved = original.Move(AllocationItem.Invest, 40);

            Assert.NotSame(original, moved);
            AssertAllocation(50, 30, 20, original.Current);
            Assert.Equal(40, moved.Current.InvestPct);
        }

        [Theory]
        [InlineData(-5)]
        [InlineData(101)]
        [InlineData(103)]
        [InlineData(105)]
        [InlineData(7)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void RejectsSliderValuesOutsideZeroToHundredOrOffTheStep(int value)
        {
            var adjuster = new AllocationAdjuster(new Allocation(50, 30, 20));

            Assert.Throws<ArgumentOutOfRangeException>(() => adjuster.Move(AllocationItem.Living, value));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(-1)]
        public void RejectsUndefinedItems(int item)
        {
            var adjuster = new AllocationAdjuster(new Allocation(50, 30, 20));

            Assert.Throws<ArgumentOutOfRangeException>(() => adjuster.Move((AllocationItem)item, 50));
        }

        [Theory]
        [InlineData(50, 30, 15)]
        [InlineData(-5, 55, 50)]
        [InlineData(33, 33, 34)]
        [InlineData(105, 0, -5)]
        public void RejectsAnInvalidInitialAllocation(int living, int saving, int invest)
        {
            Assert.Throws<ArgumentException>(() => new AllocationAdjuster(new Allocation(living, saving, invest)));
        }

        private static void AssertAllocation(int living, int saving, int invest, Allocation actual) =>
            Assert.Equal(new[] { living, saving, invest }, new[] { actual.LivingPct, actual.SavingPct, actual.InvestPct });
    }
}
