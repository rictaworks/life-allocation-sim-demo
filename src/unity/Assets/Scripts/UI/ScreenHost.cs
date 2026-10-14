using LifeAllocationSim.Config;
using UnityEngine;

namespace LifeAllocationSim.Ui
{
    /// <summary>画面の入れ替えと、通信中の覆い（入力を止める）を受け持つ。</summary>
    public sealed class ScreenHost
    {
        private readonly UiFactory factory;
        private readonly RectTransform canvas;
        private readonly GameObject busyOverlay;
        private GameObject currentScreen;

        public ScreenHost(UiFactory factory, RectTransform canvas)
        {
            this.factory = factory;
            this.canvas = canvas;
            busyOverlay = factory.CreateBusyOverlay(canvas);
        }

        public UiFactory Factory => factory;

        /// <summary>前の画面を外し、新しい画面を作る。戻り値は本文を置く場所。</summary>
        public RectTransform BeginScreen()
        {
            if (currentScreen != null)
            {
                UnityEngine.Object.Destroy(currentScreen);
            }

            var root = factory.CreateStretched(canvas, AppConfig.ScreenObjectName, AppConfig.BackgroundColor);
            var scroll = factory.CreateScrollArea(root);
            factory.CreateFooter(root, UiTexts.Disclaimer);
            currentScreen = root.gameObject;
            busyOverlay.transform.SetAsLastSibling();
            return scroll.content;
        }

        /// <summary>通信の間は覆いを出して、操作を止める。</summary>
        public void SetBusy(bool busy)
        {
            busyOverlay.SetActive(busy);
            if (busy)
            {
                busyOverlay.transform.SetAsLastSibling();
            }
        }
    }
}
