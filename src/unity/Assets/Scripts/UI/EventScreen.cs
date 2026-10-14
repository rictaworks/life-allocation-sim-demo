using System;
using DomainCore;
using LifeAllocationSim.Config;

namespace LifeAllocationSim.Ui
{
    /// <summary>イベント表示：その年に起きた出来事と、金額への影響。出来事が無い年は、その旨を出す。</summary>
    public sealed class EventScreen
    {
        private readonly ScreenHost host;

        public EventScreen(ScreenHost host)
        {
            this.host = host;
        }

        public void Show(YearResult year, Action onNext)
        {
            var factory = host.Factory;
            var content = host.BeginScreen();
            factory.AddHeading(content, UiTexts.EventHeading);
            factory.AddText(content, Display.Format(UiTexts.EventAgeFormat, year.Age), AppConfig.NoteFontSize);

            if (!year.Event.HasEvent)
            {
                factory.AddText(content, UiTexts.EventNone, AppConfig.BodyFontSize);
            }
            else
            {
                var lifeEvent = year.Event.Event;
                factory.AddText(content, lifeEvent.Label, AppConfig.HeadingFontSize, bold: true);
                factory.AddText(content, Display.Format(UiTexts.EventCategoryFormat, CategoryText(lifeEvent.Category)), AppConfig.NoteFontSize);
                factory.AddText(content, EffectText(lifeEvent), AppConfig.BodyFontSize);
            }

            factory.AddButton(content, UiTexts.Next, onNext);
        }

        private static string CategoryText(LifeEventCategory category)
        {
            switch (category)
            {
                case LifeEventCategory.Expense:
                    return UiTexts.CategoryExpense;
                case LifeEventCategory.Income:
                    return UiTexts.CategoryIncome;
                case LifeEventCategory.Market:
                    return UiTexts.CategoryMarket;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, LogTexts.UndefinedValue);
            }
        }

        private static string EffectText(LifeEvent lifeEvent)
        {
            switch (lifeEvent.Effect)
            {
                case LifeEventEffect.Expense:
                    return Display.Format(UiTexts.EventExpenseFormat, Display.Yen(lifeEvent.AmountYen));
                case LifeEventEffect.OneTimeIncome:
                    return Display.Format(UiTexts.EventIncomeFormat, Display.Yen(lifeEvent.AmountYen));
                case LifeEventEffect.IncomeRaise:
                    return Display.Format(UiTexts.EventRaiseFormat, Display.Yen(lifeEvent.AmountYen));
                case LifeEventEffect.MarketReturnOverride:
                    return Display.Format(UiTexts.EventMarketFormat, Display.PercentFromBasisPoints(lifeEvent.MarketReturnBp));
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifeEvent), lifeEvent.Effect, LogTexts.UndefinedValue);
            }
        }
    }
}
