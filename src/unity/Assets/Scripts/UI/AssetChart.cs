using System;
using System.Collections.Generic;
using DomainCore;
using DomainCore.Config;
using LifeAllocationSim.Config;
using UnityEngine;
using UnityEngine.UI;

namespace LifeAllocationSim.Ui
{
    /// <summary>
    /// 資産推移の積み上げグラフ。貯蓄は無地、投資は斜線で描く。色だけに頼らず、凡例の文字でも区別する。
    /// 横軸は年齢（全期間の枠を用意し、経過した年だけを描く）。
    /// </summary>
    public sealed class AssetChart
    {
        private const float ColumnInset = 0.15f;
        private const int AgeLabelInterval = 5;

        private readonly UiFactory factory;
        private readonly Texture2D investmentPattern;

        public AssetChart(UiFactory factory)
        {
            this.factory = factory;
            investmentPattern = PatternTextures.CreateStripes(AppConfig.InvestmentColor, AppConfig.InvestmentStripeColor);
        }

        /// <summary>years の各年の年末の残高（貯蓄・投資評価額）を描く。years は年齢の順。</summary>
        public void AddTo(Transform parent, IReadOnlyList<YearResult> years)
        {
            var slots = GameRules.FinalAge - GameRules.StartAge;
            var max = 1L;
            foreach (var year in years)
            {
                max = Math.Max(max, year.Next.SavingsYen + year.Next.InvestmentYen);
            }

            var plot = factory.CreateRect(parent, AppConfig.PlotObjectName, AppConfig.ChartHeight);
            factory.AddImage(plot, AppConfig.PlotBackgroundColor);
            for (var index = 0; index < years.Count && index < slots; index++)
            {
                AddColumn(plot, slots, index, years[index], max);
            }

            AddMaxLabel(plot, max);
            AddAgeLabels(parent, slots);
            AddLegend(parent);
        }

        private void AddColumn(RectTransform plot, int slots, int index, YearResult year, long max)
        {
            var slot = 1f / slots;
            var left = (index + ColumnInset) * slot;
            var right = (index + 1 - ColumnInset) * slot;
            var savingsShare = (float)year.Next.SavingsYen / max;
            var investmentShare = (float)year.Next.InvestmentYen / max;

            var savings = factory.CreateRect(plot, AppConfig.SavingsObjectName);
            UiFactory.Place(savings, new Vector2(left, 0f), new Vector2(right, savingsShare), Vector2.zero, Vector2.zero);
            factory.AddImage(savings, AppConfig.SavingsColor);

            var investment = factory.CreateRect(plot, AppConfig.InvestmentObjectName);
            UiFactory.Place(investment, new Vector2(left, savingsShare), new Vector2(right, savingsShare + investmentShare), Vector2.zero, Vector2.zero);
            var tiles = investmentShare * AppConfig.ChartHeight / AppConfig.PatternTileUnits;
            factory.AddRawImage(investment, investmentPattern, new Rect(0f, 0f, 1f, tiles));
        }

        private void AddMaxLabel(RectTransform plot, long max)
        {
            var label = factory.AddText(plot, Display.Format(UiTexts.GraphMaxFormat, Display.ManYen(max)), AppConfig.NoteFontSize, TextAnchor.UpperLeft, fitHeight: false);
            UiFactory.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(AppConfig.ContentGutter / 2f, 4f), new Vector2(-AppConfig.ContentGutter / 2f, -4f));
        }

        private void AddAgeLabels(Transform parent, int slots)
        {
            var row = factory.CreateRect(parent, AppConfig.AgeLabelsObjectName, AppConfig.ChartLabelHeight);
            for (var age = GameRules.StartAge; age < GameRules.FinalAge; age += AgeLabelInterval)
            {
                var x = (float)(age - GameRules.StartAge) / slots;
                AddAgeLabel(row, age, x, TextAnchor.UpperLeft, new Vector2(0f, 0f), new Vector2(AppConfig.AgeLabelWidth, 0f));
            }

            AddAgeLabel(row, GameRules.FinalAge, 1f, TextAnchor.UpperRight, new Vector2(-AppConfig.AgeLabelWidth, 0f), new Vector2(0f, 0f));
        }

        private void AddAgeLabel(RectTransform row, int age, float x, TextAnchor alignment, Vector2 offsetMin, Vector2 offsetMax)
        {
            var label = factory.AddText(row, Display.Format(UiTexts.GraphAgeFormat, age), AppConfig.NoteFontSize, alignment, fitHeight: false);
            UiFactory.Place(label.rectTransform, new Vector2(x, 0f), new Vector2(x, 1f), offsetMin, offsetMax);
        }

        private void AddLegend(Transform parent)
        {
            var row = factory.AddRow(parent, forceExpand: false);
            var savingsSwatch = factory.CreateRect(row, AppConfig.SwatchObjectName);
            UiFactory.SetPreferredSize(savingsSwatch, AppConfig.LegendSwatchSize, AppConfig.LegendSwatchSize);
            factory.AddImage(savingsSwatch, AppConfig.SavingsColor);
            factory.AddText(row, UiTexts.LegendSavings, AppConfig.NoteFontSize, TextAnchor.MiddleLeft, fitHeight: true);

            var investmentSwatch = factory.CreateRect(row, AppConfig.SwatchObjectName);
            UiFactory.SetPreferredSize(investmentSwatch, AppConfig.LegendSwatchSize, AppConfig.LegendSwatchSize);
            factory.AddRawImage(investmentSwatch, investmentPattern, new Rect(0f, 0f, 1f, 1f));
            factory.AddText(row, UiTexts.LegendInvestment, AppConfig.NoteFontSize, TextAnchor.MiddleLeft, fitHeight: true);
        }
    }
}
