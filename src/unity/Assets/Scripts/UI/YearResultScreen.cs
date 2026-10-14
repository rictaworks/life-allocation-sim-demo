using System;
using DomainCore;
using LifeAllocationSim.Config;

namespace LifeAllocationSim.Ui
{
    /// <summary>年次結果：その年の内訳（Domain Core の精算結果をそのまま表示する）。</summary>
    public sealed class YearResultScreen
    {
        private readonly ScreenHost host;

        public YearResultScreen(ScreenHost host)
        {
            this.host = host;
        }

        public void Show(YearResult year, Action onNext)
        {
            var factory = host.Factory;
            var content = host.BeginScreen();
            var body = AppConfig.BodyFontSize;
            factory.AddHeading(content, Display.Format(UiTexts.YearResultHeadingFormat, year.Age));
            factory.AddText(content, Display.Format(UiTexts.ResultIncomeFormat, Display.Yen(year.IncomeYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultLivingBudgetFormat, Display.Yen(year.LivingBudgetYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultRequiredLivingFormat, Display.Yen(year.RequiredLivingYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultSurplusFormat, Display.Yen(year.SurplusYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultShortfallFormat, Display.Yen(year.LivingShortfallYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultSavingDepositFormat, Display.Yen(year.SavingDepositYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultInvestDepositFormat, Display.Yen(year.InvestDepositYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultEventExpenseFormat, Display.Yen(year.EventExpenseYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultEventIncomeFormat, Display.Yen(year.EventIncomeYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultMarketReturnFormat, Display.PercentFromBasisPoints(year.MarketReturnBp)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultSavingsInterestFormat, Display.Yen(year.SavingsInterestYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultInvestmentGainFormat, Display.Yen(year.InvestmentGainYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultWithdrawnSavingsFormat, Display.Yen(year.WithdrawnFromSavingsYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultWithdrawnInvestmentFormat, Display.Yen(year.WithdrawnFromInvestmentYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultUnmetFormat, Display.Yen(year.UnmetYen)), body);
            factory.AddText(content, Display.Format(UiTexts.ResultSatisfactionFormat, year.Next.Satisfaction), body);

            switch (year.Next.Status)
            {
                case PlayerStatus.Bankrupt:
                    factory.AddText(content, UiTexts.ResultBankrupt, body, bold: true);
                    break;
                case PlayerStatus.Completed:
                    factory.AddText(content, Display.Format(UiTexts.ResultCompletedFormat, year.Next.Age), body, bold: true);
                    break;
                case PlayerStatus.Struggling:
                    factory.AddText(content, UiTexts.ResultStruggling, body, bold: true);
                    break;
            }

            factory.AddButton(content, UiTexts.Next, onNext);
        }
    }
}
