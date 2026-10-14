using UnityEngine;

namespace LifeAllocationSim.Config
{
    /// <summary>
    /// 表示の設定（資源の名前・既定の初期値・寸法・色）。ゲームの規則の数値は持たない（Domain Core の GameRules を読む）。
    /// </summary>
    public static class AppConfig
    {
        // 作成するオブジェクトの名前（ヒエラルキーで見分けるため。表示文字列ではない）
        public const string CanvasObjectName = "LifeAllocationSimCanvas";
        public const string EventSystemObjectName = "EventSystem";
        public const string BusyObjectName = "BusyOverlay";
        public const string FooterObjectName = "Footer";
        public const string ScreenObjectName = "Screen";
        public const string TextObjectName = "Text";
        public const string RowObjectName = "Row";
        public const string PlotObjectName = "Plot";
        public const string SavingsObjectName = "Savings";
        public const string InvestmentObjectName = "Investment";
        public const string SwatchObjectName = "Swatch";
        public const string AgeLabelsObjectName = "AgeLabels";
        public const string ColumnObjectName = "Column";

        // 枠の画像（UiSprites がコードで作る。内蔵の画像は実行時に読めないため使わない）
        public const int SpriteTexels = 8;
        public const int SpriteBorderTexels = 2;
        public const float SpritePixelsPerUnit = 100f;

        // 資源（Resources）
        public const string FontResourcePath = "Fonts/MPLUS1p-Regular";

        // 初期の選択（表示の初期値であり、規則ではない）
        public const string DefaultDifficultyCode = "normal";
        public const int DefaultLivingPct = 50;
        public const int DefaultSavingPct = 30;
        public const int DefaultInvestPct = 20;

        // 表示の単位と書式
        public const string ThousandsFormat = "N0";
        public const string DecimalFormat = "0.0";
        public const long ManYenUnit = 10000L;
        public const int BasisPointsPerPercent = 100;

        // キャンバス（基準の解像度。縦の並びが主なので、高さに合わせて拡縮する）
        public const float CanvasReferenceWidth = 960f;
        public const float CanvasReferenceHeight = 540f;
        public const float CanvasMatchWidthOrHeight = 1f;

        // 寸法（キャンバスの単位）
        public const int TitleFontSize = 30;
        public const int HeadingFontSize = 22;
        public const int BodyFontSize = 16;
        public const int NoteFontSize = 14;
        public const int ButtonFontSize = 16;
        public const float ButtonHeight = 44f;
        public const float SliderHeight = 24f;
        public const float ContentSpacing = 10f;
        public const float ContentGutter = 16f;
        public const float FooterHeight = 64f;
        public const float ChartHeight = 220f;
        public const float ChartLabelHeight = 22f;
        public const float LegendSwatchSize = 16f;
        public const float AgeLabelWidth = 48f;

        // 模様（投資の帯の斜線）：1 つの模様の大きさ（キャンバスの単位）と、模様の画素数
        public const float PatternTileUnits = 8f;
        public const int PatternTexels = 8;
        public const int PatternStripeTexels = 4;

        // 色（色だけに頼らず、模様と文字で区別する）
        public static readonly Color TextColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        public static readonly Color BackgroundColor = new Color(1f, 1f, 1f, 1f);
        public static readonly Color PlotBackgroundColor = new Color(0.95f, 0.95f, 0.96f, 1f);
        public static readonly Color SavingsColor = new Color(0.16f, 0.36f, 0.66f, 1f);
        public static readonly Color InvestmentColor = new Color(0.13f, 0.5f, 0.45f, 1f);
        public static readonly Color InvestmentStripeColor = new Color(0.85f, 0.95f, 0.93f, 1f);
        public static readonly Color BusyOverlayColor = new Color(1f, 1f, 1f, 0.7f);
        public static readonly Color ControlFillColor = new Color(0.97f, 0.97f, 0.98f, 1f);
        public static readonly Color ControlBorderColor = new Color(0.45f, 0.45f, 0.5f, 1f);
        public static readonly Color TrackColor = new Color(0.86f, 0.86f, 0.88f, 1f);
        public static readonly Color SliderFillColor = new Color(0.36f, 0.5f, 0.74f, 1f);
        public static readonly Color KnobColor = new Color(0.2f, 0.3f, 0.5f, 1f);
    }
}
