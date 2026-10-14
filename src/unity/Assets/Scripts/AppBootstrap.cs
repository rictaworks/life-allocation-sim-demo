using LifeAllocationSim.Api;
using LifeAllocationSim.Game;
using LifeAllocationSim.Ui;
using UnityEngine;

namespace LifeAllocationSim
{
    /// <summary>シーンに 1 つだけ置く起動点。画面（UI）とゲームの流れを、コードだけで組み立てる。</summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        private GameController controller;

        private void Start()
        {
            var factory = new UiFactory();
            var canvas = factory.CreateCanvas();
            var host = new ScreenHost(factory, canvas);
            controller = new GameController(new ApiClient(this), host);
            controller.Begin();
        }
    }
}
