using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DomainCore.Config;

namespace DomainCore
{
    /// <summary>
    /// 最終評価（requirements.md 4.4 evaluate）。ルールベース：数値を算出し、観点ごとの定型文を組み合わせ、ランクを決める。
    /// - ランク：上位から順に、総資産と平均満足度の両方が下限に届いた最初のもの。破綻は資産額によらず最下位。
    /// - 「生活費を削りすぎて満足度が低い高資産」は上位に上げない（満足度の下限がある）。定型文でも、貯めることだけが正解ではないと伝える。
    /// </summary>
    public static class Evaluator
    {
        /// <summary>完走か破綻で終わったゲームを評価する。終わっていなければ InvalidOperationException、履歴が空なら ArgumentException。</summary>
        public static Evaluation Evaluate(PlayerState finalState, IReadOnlyList<YearResult> history)
        {
            if (!finalState.IsFinished)
            {
                throw new InvalidOperationException(ErrorMessages.EvaluationNeedsFinishedGame);
            }

            if (history.Count == 0)
            {
                throw new ArgumentException(ErrorMessages.EvaluationNeedsHistory, nameof(history));
            }

            var totalAssetsYen = finalState.SavingsYen + finalState.InvestmentYen;
            var averageSatisfaction = (int)(history.Sum(year => (long)year.Next.Satisfaction) / history.Count);
            var isBankrupt = finalState.Status == PlayerStatus.Bankrupt;
            var struggleYears = history.Count(year => year.Next.Satisfaction < GameRules.StruggleSatisfactionThreshold);
            var investmentSoldYears = history.Count(year => year.WithdrawnFromInvestmentYen > 0);

            return new Evaluation
            {
                TotalAssetsYen = totalAssetsYen,
                AverageSatisfaction = averageSatisfaction,
                IsBankrupt = isBankrupt,
                StruggleYears = struggleYears,
                InvestmentSoldYears = investmentSoldYears,
                Rank = SelectRank(isBankrupt, totalAssetsYen, averageSatisfaction),
                RetirementComment = RetirementComment(isBankrupt, totalAssetsYen),
                QualityComment = QualityComment(totalAssetsYen, averageSatisfaction, struggleYears),
                PreparednessComment = PreparednessComment(investmentSoldYears),
            };
        }

        private static Rank SelectRank(bool isBankrupt, long totalAssetsYen, int averageSatisfaction)
        {
            var ranks = RankTable.All;
            if (isBankrupt)
            {
                return ranks[ranks.Count - 1];
            }

            return ranks.First(rank => totalAssetsYen >= rank.MinTotalAssetsYen && averageSatisfaction >= rank.MinAverageSatisfaction);
        }

        private static string RetirementComment(bool isBankrupt, long totalAssetsYen)
        {
            if (isBankrupt)
            {
                return EvaluationTexts.RetirementBankrupt;
            }

            var template = totalAssetsYen >= GameRules.RetirementTargetYen
                ? EvaluationTexts.RetirementAboveTarget
                : EvaluationTexts.RetirementBelowTarget;
            return Format(template, Yen(totalAssetsYen), Yen(GameRules.RetirementTargetYen));
        }

        private static string QualityComment(long totalAssetsYen, int averageSatisfaction, int struggleYears)
        {
            var level = averageSatisfaction >= GameRules.QualityHighMinSatisfaction ? EvaluationTexts.QualityHigh
                : averageSatisfaction >= GameRules.QualityMiddleMinSatisfaction ? EvaluationTexts.QualityMiddle
                : EvaluationTexts.QualityLow;

            var sentences = new List<string> { level };
            if (struggleYears > 0)
            {
                sentences.Add(Format(EvaluationTexts.QualityStruggleYears, struggleYears));
            }

            var highAssetsButLowSatisfaction = totalAssetsYen >= GameRules.RetirementTargetYen
                && averageSatisfaction < GameRules.QualityMiddleMinSatisfaction;
            if (highAssetsButLowSatisfaction)
            {
                sentences.Add(EvaluationTexts.QualityHighAssetsLowSatisfaction);
            }

            return string.Concat(sentences);
        }

        private static string PreparednessComment(int investmentSoldYears) =>
            investmentSoldYears == 0
                ? EvaluationTexts.PreparednessNoSale
                : Format(EvaluationTexts.PreparednessSold, investmentSoldYears);

        private static string Yen(long amount) => amount.ToString(EvaluationTexts.AmountFormat, CultureInfo.InvariantCulture);

        private static string Format(string template, params object[] args) => string.Format(CultureInfo.InvariantCulture, template, args);
    }
}
