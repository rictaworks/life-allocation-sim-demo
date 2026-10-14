using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DomainCore.AcceptanceTests.Support;
using Xunit;

namespace DomainCore.AcceptanceTests
{
    /// <summary>
    /// 受け入れ条件 3：SeededRandom。引数のシードだけから決まる整数列を返す。System.Random は使わず、アルゴリズムを自前で持つ
    /// （処理系が違っても同じ結果になるように）。同じシードなら同じ列。
    /// 期待値は、別の処理系・別の計算で求めた値（Node.js の BigInt：splitmix64-vectors.json、.NET の BigInteger：Support/BigIntegerSplitMix64）。
    /// </summary>
    public class Criterion3SeededRandomTests
    {
        private const int ReferenceDraws = 200;
        private const int GeneratedSeeds = 200;
        private const int BoundedSeeds = 50;
        private const ulong InputSeed = 0x5EED_0000_0014_0003UL;

        [Fact]
        public void MatchesTheKnownValuesComputedWithNodeBigInt()
        {
            var vectors = SplitMix64Vectors.Load();

            foreach (var vector in vectors.UInt64Vectors)
            {
                var random = new SeededRandom(vector.Seed);
                var actual = vector.Values.Select(_ => random.NextUInt64()).ToArray();
                Assert.Equal(vector.Values, actual);
            }

            foreach (var vector in vectors.BoundedVectors)
            {
                var random = new SeededRandom(vector.Seed);
                var actual = vector.Values.Select(_ => random.NextInt(vector.Bound)).ToArray();
                Assert.Equal(vector.Values, actual);
            }
        }

        [Fact]
        public void MatchesTheBigIntegerReferenceForManySeeds()
        {
            foreach (var seed in Seeds(GeneratedSeeds))
            {
                var actual = new SeededRandom(seed);
                var reference = new BigIntegerSplitMix64(seed);
                for (var draw = 0; draw < ReferenceDraws; draw++)
                {
                    var expected = reference.NextUInt64();
                    var value = actual.NextUInt64();
                    if (value != expected)
                    {
                        Assert.Fail($"シード {seed} の {draw + 1} 個目：{value:X16}（参照実装は {expected:X16}）");
                    }
                }
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(6)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(10000)]
        [InlineData(1073741824)]
        [InlineData(1073741825)]
        [InlineData(int.MaxValue)]
        public void NextIntMatchesTheBigIntegerReference(int bound)
        {
            foreach (var seed in Seeds(BoundedSeeds))
            {
                var actual = new SeededRandom(seed);
                var reference = new BigIntegerSplitMix64(seed);
                for (var draw = 0; draw < ReferenceDraws; draw++)
                {
                    var expected = reference.NextInt(bound);
                    var value = actual.NextInt(bound);
                    if (value != expected)
                    {
                        Assert.Fail($"シード {seed}・上限 {bound} の {draw + 1} 個目：{value}（参照実装は {expected}）");
                    }
                }
            }
        }

        [Fact]
        public void SameSeedGivesTheSameSequenceEvenIfOtherInstancesDrawInBetween()
        {
            var first = Draw(new SeededRandom(20261013), 1000);

            var other = new SeededRandom(7);
            for (var i = 0; i < 1234; i++)
            {
                other.NextUInt64();
                other.NextInt(10);
            }

            var second = Draw(new SeededRandom(20261013), 1000);

            Assert.Equal(first, second);
        }

        [Fact]
        public void TwoInstancesWithTheSameSeedStayInStepWhenInterleaved()
        {
            var a = new SeededRandom(-123456789);
            var b = new SeededRandom(-123456789);

            for (var i = 0; i < 10000; i++)
            {
                Assert.Equal(a.NextUInt64(), b.NextUInt64());
                Assert.Equal(a.NextInt(10000), b.NextInt(10000));
            }
        }

        [Fact]
        public void DifferentSeedsGiveDifferentSequences()
        {
            var firstValues = new HashSet<ulong>();
            for (long seed = -50; seed < 50; seed++)
            {
                Assert.True(firstValues.Add(new SeededRandom(seed).NextUInt64()), $"シード {seed} の最初の値が、他のシードと重なりました");
            }
        }

        [Fact]
        public void NextIntStaysInRangeAndIsNotGrosslyBiased()
        {
            // 固定のシードなので結果は毎回同じ。10 通りの出現回数が、期待値 10,000 から ±5%（標準偏差のおよそ 5 倍）に収まることを見る
            var random = new SeededRandom(2026);
            var counts = new int[10];
            for (var i = 0; i < 100000; i++)
            {
                var value = random.NextInt(10);
                Assert.InRange(value, 0, 9);
                counts[value]++;
            }

            Assert.All(counts, count => Assert.InRange(count, 9500, 10500));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void NextIntRejectsBoundsBelowOne(int bound)
        {
            var random = new SeededRandom(1);

            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(bound));
        }

        [Fact]
        public void HoldsItsOwnAlgorithmInsteadOfSystemRandom()
        {
            var type = typeof(SeededRandom);
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.Equal(typeof(object), type.BaseType);
            Assert.DoesNotContain(fields, field => typeof(Random).IsAssignableFrom(field.FieldType));
            Assert.Equal(new[] { typeof(long) }, type.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        }

        private static IEnumerable<long> Seeds(int generated)
        {
            foreach (var seed in new[] { 0L, 1L, -1L, 42L, long.MinValue, long.MaxValue })
            {
                yield return seed;
            }

            var input = new XorShift64(InputSeed);
            for (var i = 0; i < generated; i++)
            {
                yield return input.NextInt64();
            }
        }

        private static ulong[] Draw(SeededRandom random, int count)
        {
            var values = new ulong[count * 2];
            for (var i = 0; i < count; i++)
            {
                values[2 * i] = random.NextUInt64();
                values[(2 * i) + 1] = (ulong)random.NextInt(int.MaxValue);
            }

            return values;
        }
    }
}
