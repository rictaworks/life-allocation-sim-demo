using LifeAllocationSim.Config;
using UnityEngine;

namespace LifeAllocationSim.Ui
{
    /// <summary>
    /// 標準の部品（ボタン・スライダー・スクロール）に使う枠の画像を、コードで作る。
    /// Unity の内蔵の画像（UI/Skin）は、プレイヤー（WebGL の実行時）では読めないため使わない。
    /// </summary>
    public static class UiSprites
    {
        /// <summary>内側を fill、縁を border で塗った画像。角は伸びない（9 スライス）。</summary>
        public static Sprite CreateFrame(Color fill, Color border)
        {
            var size = AppConfig.SpriteTexels;
            var edge = AppConfig.SpriteBorderTexels;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var onEdge = x < edge || y < edge || x >= size - edge || y >= size - edge;
                    texture.SetPixel(x, y, onEdge ? border : fill);
                }
            }

            texture.Apply();
            var border4 = new Vector4(edge, edge, edge, edge);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                AppConfig.SpritePixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                border4);
        }
    }
}
