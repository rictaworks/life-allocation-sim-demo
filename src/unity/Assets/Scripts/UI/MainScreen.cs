using System;
using System.Collections.Generic;
using DomainCore;
using DomainCore.Config;
using LifeAllocationSim.Config;
using LifeAllocationSim.Game;
using UnityEngine;
using UnityEngine.UI;

namespace LifeAllocationSim.Ui
{
    /// <summary>
    /// メイン：年齢・状態、配分の 3 本のスライダー、資産推移、満足度、「次の年へ」。
    /// 配分の自動調整（AllocationAdjuster）と検証は Domain Core が行い、この画面は値を表示して受け付けるだけ。
    /// </summary>
    public sealed class MainScreen
    {
        private static readonly AllocationItem[] Items =
        {
            AllocationItem.Living,
            AllocationItem.Saving,
            AllocationItem.Invest,
        };

        private readonly ScreenHost host;
        private readonly Dictionary<AllocationItem, Slider> sliders = new Dictionary<AllocationItem, Slider>();
        private readonly Dictionary<AllocationItem, Text> valueTexts = new Dictionary<AllocationItem, Text>();
        private AllocationAdjuster adjuster;
        private bool syncing;

        public MainScreen(ScreenHost host)
        {
            this.host = host;
        }

        /// <summary>session の今の状態を表示する。初期の配分は前の年の配分（最初の年は既定の配分）。</summary>
        public void Show(GameSession session, Action<Allocation> onNextYear, Action onBackToTitle)
        {
            var factory = host.Factory;
            var content = host.BeginScreen();
            var state = session.State;

            factory.AddText(content, Display.Format(UiTexts.AgeFormat, state.Age), AppConfig.HeadingFontSize, bold: true);
            factory.AddText(content, Display.Format(UiTexts.YearProgressFormat, session.NextTurnNo, GameRules.FinalAge - GameRules.StartAge), AppConfig.NoteFontSize);
            factory.AddText(content, Display.Format(UiTexts.StatusFormat, StatusText(state.Status)), AppConfig.BodyFontSize);
            if (state.Status == PlayerStatus.Struggling)
            {
                factory.AddText(content, UiTexts.StruggleWarning, AppConfig.BodyFontSize, bold: true);
            }

            factory.AddText(content, Display.Format(UiTexts.IncomeFormat, Display.Yen(state.IncomeYen)), AppConfig.BodyFontSize);
            factory.AddText(content, Display.Format(UiTexts.SavingsFormat, Display.Yen(state.SavingsYen)), AppConfig.BodyFontSize);
            factory.AddText(content, Display.Format(UiTexts.InvestmentFormat, Display.Yen(state.InvestmentYen)), AppConfig.BodyFontSize);

            factory.AddText(content, Display.Format(UiTexts.SatisfactionFormat, state.Satisfaction, GameRules.SatisfactionMax), AppConfig.BodyFontSize);
            var satisfaction = factory.AddSlider(content, GameRules.SatisfactionMin, GameRules.SatisfactionMax, true, null);
            satisfaction.value = state.Satisfaction;
            satisfaction.interactable = false;

            factory.AddHeading(content, UiTexts.AllocationHeading);
            factory.AddText(content, UiTexts.AllocationNote, AppConfig.NoteFontSize);
            adjuster = GameEngine.CreateAdjuster(session.SuggestedAllocation);
            var steps = GameRules.AllocationTotalPct / GameRules.AllocationStepPct;
            foreach (var item in Items)
            {
                valueTexts[item] = factory.AddText(content, string.Empty, AppConfig.BodyFontSize);
                sliders[item] = factory.AddSlider(content, 0f, steps, true, value => OnSliderChanged(item, value));
            }

            Refresh();
            factory.AddButton(content, UiTexts.NextYear, () => onNextYear(adjuster.Current));

            factory.AddHeading(content, UiTexts.GraphHeading);
            new AssetChart(factory).AddTo(content, session.Years);
            factory.AddButton(content, UiTexts.BackToTitle, onBackToTitle);
        }

        private void OnSliderChanged(AllocationItem item, float value)
        {
            if (syncing)
            {
                return;
            }

            var pct = Mathf.RoundToInt(value) * GameRules.AllocationStepPct;
            adjuster = adjuster.Move(item, pct);
            Refresh();
        }

        /// <summary>今の配分を、スライダーと文字に反映する（ユーザーの操作による変更の後も、この処理で揃える）。</summary>
        private void Refresh()
        {
            syncing = true;
            foreach (var item in Items)
            {
                var pct = adjuster.Current.Get(item);
                sliders[item].value = pct / GameRules.AllocationStepPct;
                valueTexts[item].text = Display.Format(UiTexts.AllocationValueFormat, LabelOf(item), pct);
            }

            syncing = false;
        }

        private static string LabelOf(AllocationItem item)
        {
            switch (item)
            {
                case AllocationItem.Living:
                    return UiTexts.LivingLabel;
                case AllocationItem.Saving:
                    return UiTexts.SavingLabel;
                case AllocationItem.Invest:
                    return UiTexts.InvestLabel;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item), item, LogTexts.UndefinedValue);
            }
        }

        private static string StatusText(PlayerStatus status)
        {
            switch (status)
            {
                case PlayerStatus.Playing:
                    return UiTexts.StatusPlaying;
                case PlayerStatus.Struggling:
                    return UiTexts.StatusStruggling;
                case PlayerStatus.Bankrupt:
                    return UiTexts.StatusBankrupt;
                case PlayerStatus.Completed:
                    return UiTexts.StatusCompleted;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, LogTexts.UndefinedValue);
            }
        }
    }
}
