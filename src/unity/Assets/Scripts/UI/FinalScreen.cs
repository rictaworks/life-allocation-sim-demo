using System;
using DomainCore;
using LifeAllocationSim.Config;
using LifeAllocationSim.Game;

namespace LifeAllocationSim.Ui
{
    /// <summary>最終評価：ランク・3 観点のコメント・全期間のグラフ。評価は Domain Core（Evaluator）の結果を表示する。</summary>
    public sealed class FinalScreen
    {
        private readonly ScreenHost host;

        public FinalScreen(ScreenHost host)
        {
            this.host = host;
        }

        /// <summary>session は完走か破綻で終わっていること。</summary>
        public void Show(GameSession session, Action onPlayAgain)
        {
            var factory = host.Factory;
            var content = host.BeginScreen();
            var evaluation = session.Outcome.Evaluation;
            var body = AppConfig.BodyFontSize;

            factory.AddHeading(content, UiTexts.FinalHeading);
            factory.AddText(content, Display.Format(UiTexts.RankFormat, evaluation.Rank.Code), AppConfig.HeadingFontSize, bold: true);
            factory.AddText(content, evaluation.IsBankrupt ? UiTexts.FinalBankrupt : Display.Format(UiTexts.FinalCompletedFormat, session.State.Age), body);
            factory.AddText(content, Display.Format(UiTexts.FinalTotalFormat, Display.Yen(evaluation.TotalAssetsYen)), body);
            factory.AddText(content, Display.Format(UiTexts.FinalAverageSatisfactionFormat, evaluation.AverageSatisfaction), body);
            factory.AddText(content, Display.Format(UiTexts.FinalStruggleYearsFormat, evaluation.StruggleYears), body);
            factory.AddText(content, Display.Format(UiTexts.FinalInvestmentSoldYearsFormat, evaluation.InvestmentSoldYears), body);

            factory.AddHeading(content, UiTexts.CommentRetirementHeading);
            factory.AddText(content, evaluation.RetirementComment, body);
            factory.AddHeading(content, UiTexts.CommentQualityHeading);
            factory.AddText(content, evaluation.QualityComment, body);
            factory.AddHeading(content, UiTexts.CommentPreparednessHeading);
            factory.AddText(content, evaluation.PreparednessComment, body);

            factory.AddHeading(content, UiTexts.FullGraphHeading);
            new AssetChart(factory).AddTo(content, session.Years);
            factory.AddButton(content, UiTexts.PlayAgain, onPlayAgain);
        }
    }
}
