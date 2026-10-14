using System;
using System.Linq;
using DomainCore.Config;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// ライフステージの引き方（LifeStage.ForAge）。22〜65 歳のどの年齢も、ちょうど 1 つの段階に入る。
    /// </summary>
    public class LifeStageTests
    {
        [Fact]
        public void ForAgeFindsTheBandContainingEveryAgeOfTheGame()
        {
            for (var age = GameRules.StartAge; age <= GameRules.FinalAge; age++)
            {
                var stage = LifeStage.ForAge(age);

                Assert.InRange(age, stage.MinAge, stage.MaxAge);
            }
        }

        [Theory]
        [InlineData(22, 22, 29)]
        [InlineData(29, 22, 29)]
        [InlineData(30, 30, 39)]
        [InlineData(49, 40, 49)]
        [InlineData(50, 50, 59)]
        [InlineData(60, 60, 65)]
        [InlineData(65, 60, 65)]
        public void ForAgeReturnsTheBandAtItsBoundaries(int age, int minAge, int maxAge)
        {
            var stage = LifeStage.ForAge(age);

            Assert.Equal((minAge, maxAge), (stage.MinAge, stage.MaxAge));
        }

        [Theory]
        [InlineData(21)]
        [InlineData(66)]
        public void ForAgeRejectsAgesOutsideTheGame(int age)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LifeStage.ForAge(age));
        }
    }
}
