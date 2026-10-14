using LifeAllocationSim.Config;
using UnityEngine;

namespace LifeAllocationSim.Ui
{
    /// <summary>投資の帯に使う斜線の模様を、コードで作る（画像ファイルは使わない）。</summary>
    public static class PatternTextures
    {
        /// <summary>斜線の模様（繰り返し可能な正方形）。baseColor の上に stripeColor の斜線を描く。</summary>
        public static Texture2D CreateStripes(Color baseColor, Color stripeColor)
        {
            var size = AppConfig.PatternTexels;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var stripe = ((x + y) / AppConfig.PatternStripeTexels) % 2 == 0;
                    texture.SetPixel(x, y, stripe ? stripeColor : baseColor);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
