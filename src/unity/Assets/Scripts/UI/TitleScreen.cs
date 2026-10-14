using System;
using System.Collections.Generic;
using DomainCore;
using DomainCore.Config;
using LifeAllocationSim.Config;
using UnityEngine.UI;

namespace LifeAllocationSim.Ui
{
    /// <summary>タイトル：難易度の選択・新規開始・続きから。</summary>
    public sealed class TitleScreen
    {
        private readonly ScreenHost host;

        public TitleScreen(ScreenHost host)
        {
            this.host = host;
        }

        /// <summary>hasSavedGame は保存されたゲームがあるか。notice があれば最初に出す。</summary>
        public void Show(bool hasSavedGame, string notice, Action<string> onNewGame, Action onResume)
        {
            var factory = host.Factory;
            var content = host.BeginScreen();
            factory.AddText(content, UiTexts.AppTitle, AppConfig.TitleFontSize, bold: true);
            factory.AddText(content, Display.Format(UiTexts.TitleIntroFormat, GameRules.StartAge, GameRules.FinalAge), AppConfig.BodyFontSize);
            factory.AddText(content, UiTexts.DataResetInfo, AppConfig.NoteFontSize);
            if (!string.IsNullOrEmpty(notice))
            {
                factory.AddText(content, notice, AppConfig.BodyFontSize, bold: true);
            }

            factory.AddHeading(content, UiTexts.DifficultyHeading);
            var selected = AppConfig.DefaultDifficultyCode;
            var choices = new List<DifficultyChoice>();
            var row = factory.AddRow(content);
            foreach (var difficulty in DifficultyTable.All)
            {
                var code = difficulty.Code;
                var label = difficulty.Label;
                var button = factory.AddButton(row, label, () =>
                {
                    selected = code;
                    RefreshChoices(choices, selected);
                });
                choices.Add(new DifficultyChoice(code, label, button.GetComponentInChildren<Text>()));
            }

            RefreshChoices(choices, selected);
            factory.AddButton(content, UiTexts.NewGame, () => onNewGame(selected));
            factory.AddButton(content, UiTexts.ResumeGame, onResume, hasSavedGame);
            if (!hasSavedGame)
            {
                factory.AddText(content, UiTexts.ResumeUnavailable, AppConfig.NoteFontSize);
            }
        }

        private static void RefreshChoices(List<DifficultyChoice> choices, string selected)
        {
            foreach (var choice in choices)
            {
                choice.Text.text = choice.Code == selected
                    ? Display.Format(UiTexts.DifficultySelectedFormat, choice.Label)
                    : choice.Label;
            }
        }

        private sealed class DifficultyChoice
        {
            public DifficultyChoice(string code, string label, Text text)
            {
                Code = code;
                Label = label;
                Text = text;
            }

            public string Code { get; }

            public string Label { get; }

            public Text Text { get; }
        }
    }
}
