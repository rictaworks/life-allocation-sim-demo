using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DomainCore.Config;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// 理由コードの表示文字列（Config の ReasonTexts）と、例外の文言（Config の ErrorMessages）。
    /// 文章はですます調で書く（CLAUDE.md の開発ルール）。
    /// </summary>
    public class ReasonDescriptionsTests
    {
        [Fact]
        public void EveryAllocationViolationHasText()
        {
            foreach (AllocationViolation violation in Enum.GetValues(typeof(AllocationViolation)))
            {
                AssertPoliteSentence(ReasonDescriptions.Describe(violation));
            }
        }

        [Fact]
        public void EveryDifficultyCodeViolationHasText()
        {
            foreach (DifficultyCodeViolation violation in Enum.GetValues(typeof(DifficultyCodeViolation)))
            {
                AssertPoliteSentence(ReasonDescriptions.Describe(violation));
            }
        }

        [Fact]
        public void AllocationTextsShowTheRuleNumbers()
        {
            Assert.Contains(GameRules.AllocationStepPct.ToString(CultureInfo.InvariantCulture), ReasonDescriptions.Describe(AllocationViolation.StepMismatch));
            Assert.Contains(GameRules.AllocationTotalPct.ToString(CultureInfo.InvariantCulture), ReasonDescriptions.Describe(AllocationViolation.SumMismatch));
        }

        [Fact]
        public void ReasonTextsAreDistinct()
        {
            var texts = Enum.GetValues(typeof(AllocationViolation)).Cast<AllocationViolation>().Select(violation => ReasonDescriptions.Describe(violation))
                .Concat(Enum.GetValues(typeof(DifficultyCodeViolation)).Cast<DifficultyCodeViolation>().Select(violation => ReasonDescriptions.Describe(violation)))
                .ToArray();

            Assert.Equal(texts.Length, texts.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void RejectsUndefinedReasons()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ReasonDescriptions.Describe((AllocationViolation)0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReasonDescriptions.Describe((DifficultyCodeViolation)0));
        }

        [Fact]
        public void ConfigSentencesArePolite()
        {
            var sentences = new[] { typeof(ReasonTexts), typeof(ErrorMessages) }
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!)
                .ToArray();

            Assert.NotEmpty(sentences);
            Assert.All(sentences, AssertPoliteSentence);
        }

        private static void AssertPoliteSentence(string text)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(
                text.EndsWith("ます。", StringComparison.Ordinal)
                || text.EndsWith("ません。", StringComparison.Ordinal)
                || text.EndsWith("です。", StringComparison.Ordinal)
                || text.EndsWith("ください。", StringComparison.Ordinal),
                $"ですます調の文になっていません: {text}");
        }
    }
}
