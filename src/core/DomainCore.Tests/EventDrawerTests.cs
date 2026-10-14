using System.Collections.Generic;
using System.Linq;
using DomainCore.Config;
using DomainCore.Tests.Support;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// requirements.md 4.2 drawEvent：年齢の候補・一度きりの除外・確率による抽選（0 件か 1 件）・22 歳の初年度は発生させない。
    /// 候補の並びは LifeEventTable の順。期待の区間は、表の確率と並びから手で求めた値。
    /// </summary>
    public class EventDrawerTests
    {
        [Fact]
        public void FirstYearNeverProducesAnEventForAnySeed()
        {
            for (var seed = 1L; seed <= 200; seed++)
            {
                var draw = EventDrawer.Draw(GameFixtures.State(age: GameRules.StartAge), new SeededRandom(seed));

                Assert.False(draw.HasEvent);
            }
        }

        [Fact]
        public void FirstYearIgnoresEvenRollZero()
        {
            Assert.False(EventDrawer.Pick(GameFixtures.State(age: GameRules.StartAge), 0).HasEvent);
        }

        [Fact]
        public void PicksCandidatesInTableOrderByCumulativeProbabilityAtAge30()
        {
            // 30 歳の候補（表の順・確率）：結婚 800・出産 500・家電 600・病気 300・引っ越し 300・車 300・昇給 1000・臨時収入 300・暴落 300・好況 300。合計 4700
            var state = GameFixtures.State(age: 30);

            Assert.Equal("marriage", CodeAt(state, 0));
            Assert.Equal("marriage", CodeAt(state, 799));
            Assert.Equal("childbirth", CodeAt(state, 800));
            Assert.Equal("appliance_breakdown", CodeAt(state, 1300));
            Assert.Equal("illness", CodeAt(state, 1900));
            Assert.Equal("relocation", CodeAt(state, 2200));
            Assert.Equal("car_purchase", CodeAt(state, 2500));
            Assert.Equal("pay_raise", CodeAt(state, 2800));
            Assert.Equal("windfall", CodeAt(state, 3800));
            Assert.Equal("market_crash", CodeAt(state, 4100));
            Assert.Equal("market_boom", CodeAt(state, 4400));
            Assert.Equal("market_boom", CodeAt(state, 4699));
            Assert.False(EventDrawer.Pick(state, 4700).HasEvent);
            Assert.False(EventDrawer.Pick(state, 9999).HasEvent);
        }

        [Fact]
        public void AgeRangeLeavesOutEventsThatDoNotApplyAtAge24()
        {
            // 24 歳の候補：家電 600・病気 300・引っ越し 300・臨時収入 300・暴落 300・好況 300。結婚・出産・車・昇給は対象外
            var state = GameFixtures.State(age: 24);

            Assert.Equal("appliance_breakdown", CodeAt(state, 0));
            Assert.Equal("illness", CodeAt(state, 600));
            Assert.Equal("relocation", CodeAt(state, 900));
            Assert.Equal("windfall", CodeAt(state, 1200));
            Assert.Equal("market_crash", CodeAt(state, 1500));
            Assert.Equal("market_boom", CodeAt(state, 1800));
            Assert.False(EventDrawer.Pick(state, 2100).HasEvent);
        }

        [Fact]
        public void OnceOnlyEventIsLeftOutAfterItOccurred()
        {
            // 結婚が発生済みなら候補から外れ、出産（500）が先頭になる。合計は 4700 − 800 = 3900
            var state = GameFixtures.State(age: 30, occurredOnce: new[] { "marriage" });

            Assert.Equal("childbirth", CodeAt(state, 0));
            Assert.Equal("childbirth", CodeAt(state, 499));
            Assert.Equal("appliance_breakdown", CodeAt(state, 500));
            Assert.False(EventDrawer.Pick(state, 3900).HasEvent);
        }

        [Fact]
        public void RepeatableEventStaysCandidateAfterItOccurred()
        {
            var state = GameFixtures.State(age: 30, occurredOnce: new[] { "childbirth" });

            Assert.Equal("childbirth", CodeAt(state, 800));
        }

        [Fact]
        public void EachEventHappensExactlyItsProbabilityOverAllRolls()
        {
            // 万分率の抽選：0〜9999 の全部の値を試すと、各イベントの回数は確率（万分率）と一致する
            var state = GameFixtures.State(age: 30);
            var candidates = LifeEventTable.All.Where(lifeEvent => lifeEvent.MinAge <= 30 && 30 <= lifeEvent.MaxAge).ToArray();
            var counts = candidates.ToDictionary(lifeEvent => lifeEvent.Code, lifeEvent => 0);
            var none = 0;

            for (var roll = 0; roll < GameRules.BasisPointsPerUnit; roll++)
            {
                var draw = EventDrawer.Pick(state, roll);
                if (draw.HasEvent)
                {
                    counts[draw.Event.Code]++;
                }
                else
                {
                    none++;
                }
            }

            foreach (var candidate in candidates)
            {
                Assert.Equal(candidate.ProbabilityBp, counts[candidate.Code]);
            }

            Assert.Equal(GameRules.BasisPointsPerUnit - candidates.Sum(lifeEvent => lifeEvent.ProbabilityBp), none);
        }

        [Theory]
        [InlineData(22)]
        [InlineData(30)]
        public void EveryDrawUsesExactlyOneRandomNumber(int age)
        {
            var drawn = new SeededRandom(12345);
            EventDrawer.Draw(GameFixtures.State(age: age), drawn);

            var reference = new SeededRandom(12345);
            reference.NextUInt64();

            Assert.Equal(reference.NextUInt64(), drawn.NextUInt64());
        }

        [Fact]
        public void SameSeedGivesSameEventsOverWholeCareer()
        {
            var first = new SeededRandom(2026);
            var second = new SeededRandom(2026);
            var events = new List<string?>();

            for (var age = GameRules.StartAge; age < GameRules.FinalAge; age++)
            {
                var state = GameFixtures.State(age: age);
                var a = CodeOrNull(EventDrawer.Draw(state, first));
                var b = CodeOrNull(EventDrawer.Draw(state, second));

                Assert.Equal(a, b);
                events.Add(a);
            }

            Assert.Contains(events, code => code != null);
        }

        private static string CodeAt(PlayerState state, int roll)
        {
            var draw = EventDrawer.Pick(state, roll);
            Assert.True(draw.HasEvent, $"roll {roll} でイベントが選ばれませんでした");
            return draw.Event.Code;
        }

        private static string? CodeOrNull(EventDraw draw) => draw.HasEvent ? draw.Event.Code : null;
    }
}
