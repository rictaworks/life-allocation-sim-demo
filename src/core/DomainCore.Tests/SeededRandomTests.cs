using System;
using System.Linq;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// SeededRandom：シードだけから決まる整数列（SplitMix64）。処理系が違っても同じ結果になること。
    /// 期待値は、別の実装（Python の任意精度整数で書いた SplitMix64 と、Java 21 の java.util.SplittableRandom）で求めた値。
    /// SplittableRandom(seed).nextLong() は SplitMix64 と同じ列を返す。
    /// </summary>
    public class SeededRandomTests
    {
        [Theory]
        [InlineData(0L, 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL, 0xF88BB8A8724C81ECUL, 0x1B39896A51A8749BUL)]
        [InlineData(1234567L, 0x599ED017FB08FC85UL, 0x2C73F08458540FA5UL, 0x883EBCE5A3F27C77UL, 0x3FBEF740E9177B3FUL, 0xE3B8346708CB5ECDUL)]
        [InlineData(42L, 0xBDD732262FEB6E95UL, 0x28EFE333B266F103UL, 0x47526757130F9F52UL, 0x581CE1FF0E4AE394UL, 0x09BC585A244823F2UL)]
        [InlineData(-1L, 0xE4D971771B652C20UL, 0xE99FF867DBF682C9UL, 0x382FF84CB27281E9UL, 0x6D1DB36CCBA982D2UL, 0xB4A0472E578069AEUL)]
        public void ProducesKnownSplitMix64Sequence(long seed, ulong first, ulong second, ulong third, ulong fourth, ulong fifth)
        {
            var random = new SeededRandom(seed);

            var actual = Enumerable.Range(0, 5).Select(_ => random.NextUInt64()).ToArray();

            Assert.Equal(new[] { first, second, third, fourth, fifth }, actual);
        }

        [Fact]
        public void ProducesKnownBoundedIntegers()
        {
            var random = new SeededRandom(42);

            var actual = Enumerable.Range(0, 10).Select(_ => random.NextInt(10000)).ToArray();

            Assert.Equal(new[] { 5413, 2291, 3858, 5764, 3250, 9062, 4925, 5908, 2005, 8974 }, actual);
        }

        [Fact]
        public void ProducesKnownIntegersForLargeAndSmallBounds()
        {
            var large = new SeededRandom(7);
            var small = new SeededRandom(7);

            Assert.Equal(
                new[] { 550097313, 2094325707, 132229895, 297009326, 1371838632 },
                Enumerable.Range(0, 5).Select(_ => large.NextInt(int.MaxValue)).ToArray());
            Assert.Equal(
                new[] { 0, 0, 0, 0, 1, 0, 1, 0, 2, 2 },
                Enumerable.Range(0, 10).Select(_ => small.NextInt(3)).ToArray());
        }

        [Fact]
        public void SameSeedGivesSameSequence()
        {
            var a = new SeededRandom(20261013);
            var b = new SeededRandom(20261013);

            for (var i = 0; i < 1000; i++)
            {
                Assert.Equal(a.NextUInt64(), b.NextUInt64());
                Assert.Equal(a.NextInt(10000), b.NextInt(10000));
            }
        }

        [Fact]
        public void DifferentSeedsGiveDifferentSequences()
        {
            var a = new SeededRandom(1);
            var b = new SeededRandom(2);

            var first = Enumerable.Range(0, 10).Select(_ => a.NextUInt64()).ToArray();
            var second = Enumerable.Range(0, 10).Select(_ => b.NextUInt64()).ToArray();

            Assert.NotEqual(first, second);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(10000)]
        [InlineData(int.MaxValue)]
        public void BoundedIntegersStayInRange(int bound)
        {
            var random = new SeededRandom(bound);

            for (var i = 0; i < 2000; i++)
            {
                var value = random.NextInt(bound);
                Assert.InRange(value, 0, bound - 1);
            }
        }

        [Fact]
        public void BoundedIntegersCoverAllValuesEvenly()
        {
            // 固定のシードなので結果は毎回同じ。10 通りが極端に偏っていないことだけを見る
            var random = new SeededRandom(99);
            var counts = new int[10];

            for (var i = 0; i < 10000; i++)
            {
                counts[random.NextInt(10)]++;
            }

            Assert.All(counts, count => Assert.InRange(count, 900, 1100));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void RejectsBoundBelowOne(int bound)
        {
            var random = new SeededRandom(1);

            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(bound));
        }
    }
}
