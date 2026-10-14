using System;
using LifeAllocationSim.Config;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LifeAllocationSim.Ui
{
    /// <summary>
    /// 標準の部品（uGUI）を、コードだけで組み立てる。見た目は既定のまま、配置は縦の並びだけにする（デザインはしない）。
    /// 日本語の字形は既定のフォントに無いため、同梱の OFL フォント（Resources/Fonts）を使う。
    /// </summary>
    public sealed class UiFactory
    {
        private readonly Font font;
        private readonly DefaultControls.Resources resources;

        public UiFactory()
        {
            font = Resources.Load<Font>(AppConfig.FontResourcePath);
            if (font == null)
            {
                throw new InvalidOperationException(string.Format(LogTexts.FontMissing, AppConfig.FontResourcePath));
            }

            resources = new DefaultControls.Resources
            {
                standard = UiSprites.CreateFrame(AppConfig.ControlFillColor, AppConfig.ControlBorderColor),
                background = UiSprites.CreateFrame(AppConfig.TrackColor, AppConfig.ControlBorderColor),
                knob = UiSprites.CreateFrame(AppConfig.KnobColor, AppConfig.KnobColor),
            };
        }

        /// <summary>キャンバスと入力の受け口（EventSystem）を作る。戻り値はキャンバスの RectTransform。</summary>
        public RectTransform CreateCanvas()
        {
            var canvasObject = new GameObject(
                AppConfig.CanvasObjectName,
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(AppConfig.CanvasReferenceWidth, AppConfig.CanvasReferenceHeight);
            scaler.matchWidthOrHeight = AppConfig.CanvasMatchWidthOrHeight;

            new GameObject(AppConfig.EventSystemObjectName, typeof(EventSystem), typeof(StandaloneInputModule));
            return (RectTransform)canvasObject.transform;
        }

        /// <summary>親の全面に広がる要素を作る。background があれば塗る。</summary>
        public RectTransform CreateStretched(Transform parent, string name, Color? background)
        {
            var rect = CreateRect(parent, name);
            Place(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            if (background.HasValue)
            {
                AddImage(rect, background.Value);
            }

            return rect;
        }

        /// <summary>通信中の覆い。入力を止め、文字で状況を示す。最初は非表示。</summary>
        public GameObject CreateBusyOverlay(RectTransform canvas)
        {
            var overlay = CreateStretched(canvas, AppConfig.BusyObjectName, AppConfig.BusyOverlayColor);
            var label = AddText(overlay, UiTexts.Busy, AppConfig.BodyFontSize, TextAnchor.MiddleCenter, fitHeight: false);
            Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(16f, -32f), new Vector2(-16f, 32f));
            overlay.gameObject.SetActive(false);
            return overlay.gameObject;
        }

        /// <summary>縦に並べる本文の領域（スクロールできる）。footer の高さ分だけ下を空ける。</summary>
        public ScrollRect CreateScrollArea(RectTransform parent)
        {
            var go = DefaultControls.CreateScrollView(resources);
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            Place(rect, Vector2.zero, Vector2.one, new Vector2(0f, AppConfig.FooterHeight), Vector2.zero);

            var scroll = go.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            var content = scroll.content.gameObject;
            var gutter = (int)AppConfig.ContentGutter;
            var group = content.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(gutter, gutter, gutter, gutter);
            group.spacing = AppConfig.ContentSpacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return scroll;
        }

        /// <summary>下端に常に出す注意書き（利回り等が架空のモデル値であること）。</summary>
        public void CreateFooter(RectTransform parent, string text)
        {
            var rect = CreateRect(parent, AppConfig.FooterObjectName);
            Place(rect, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, AppConfig.FooterHeight));
            AddImage(rect, AppConfig.BackgroundColor);
            var label = AddText(rect, text, AppConfig.NoteFontSize, TextAnchor.MiddleLeft, fitHeight: false);
            Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(AppConfig.ContentGutter, 4f), new Vector2(-AppConfig.ContentGutter, -4f));
        }

        public RectTransform CreateRect(Transform parent, string name, float preferredHeight = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            if (preferredHeight > 0f)
            {
                SetPreferredSize(rect, 0f, preferredHeight);
            }

            return rect;
        }

        public Text AddText(Transform parent, string content, int size, TextAnchor alignment = TextAnchor.UpperLeft, bool fitHeight = true, bool bold = false)
        {
            var go = new GameObject(AppConfig.TextObjectName, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.color = AppConfig.TextColor;
            text.alignment = alignment;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            if (fitHeight)
            {
                var fitter = go.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            return text;
        }

        public Text AddHeading(Transform parent, string content) =>
            AddText(parent, content, AppConfig.HeadingFontSize, TextAnchor.UpperLeft, fitHeight: true, bold: true);

        public Button AddButton(Transform parent, string label, Action onClick, bool interactable = true)
        {
            var go = DefaultControls.CreateButton(resources);
            go.transform.SetParent(parent, false);
            var text = go.GetComponentInChildren<Text>();
            text.font = font;
            text.text = label;
            text.fontSize = AppConfig.ButtonFontSize;
            text.color = AppConfig.TextColor;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetPreferredSize((RectTransform)go.transform, 0f, AppConfig.ButtonHeight);

            var button = go.GetComponent<Button>();
            button.interactable = interactable;
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            return button;
        }

        public Slider AddSlider(Transform parent, float min, float max, bool wholeNumbers, Action<float> onChanged)
        {
            var go = DefaultControls.CreateSlider(resources);
            go.transform.SetParent(parent, false);
            var slider = go.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = min;
            var fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
            if (fill != null)
            {
                fill.color = AppConfig.SliderFillColor;
            }

            if (onChanged != null)
            {
                slider.onValueChanged.AddListener(value => onChanged(value));
            }

            SetPreferredSize((RectTransform)go.transform, 0f, AppConfig.SliderHeight);
            return slider;
        }

        /// <summary>ボタンや凡例を横に並べる行。forceExpand が true なら、子を横幅いっぱいに広げる（ボタン用）。</summary>
        public RectTransform AddRow(Transform parent, bool forceExpand = true)
        {
            var go = new GameObject(AppConfig.RowObjectName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var group = go.GetComponent<HorizontalLayoutGroup>();
            group.spacing = AppConfig.ContentSpacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = forceExpand;
            group.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        public Image AddImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public RawImage AddRawImage(RectTransform rect, Texture texture, Rect uvRect)
        {
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.uvRect = uvRect;
            return raw;
        }

        /// <summary>行や列の中で、幅と高さを固定する（0 の寸法は指定しない扱い）。</summary>
        public static void SetPreferredSize(RectTransform rect, float width, float height)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = rect.gameObject.AddComponent<LayoutElement>();
            }

            if (width > 0f)
            {
                element.minWidth = width;
                element.preferredWidth = width;
            }

            if (height > 0f)
            {
                element.minHeight = height;
                element.preferredHeight = height;
            }
        }

        public static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

    }
}
