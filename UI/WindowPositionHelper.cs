using System.Linq;
using UnityEngine;
using SFS.UI.ModGUI;

namespace WorldBuild.Mod.UI
{
    public enum Anchor
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        MiddleLeft,
        MiddleRight,
        MiddleCenter,
        TopCenter,
        BottomCenter,
    }

    public enum Origin
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        MiddleLeft,
        MiddleRight,
        MiddleCenter,
        TopCenter,
        BottomCenter,
    }
    public static class WindowPositionHelper
    {
        public static Vector2Int ToCenterAnchor(Vector2Int coords, Anchor anchor)
        {
            return coords + new Vector2Int(
                (
                    anchor.EqualsAny(Anchor.TopLeft, Anchor.MiddleLeft, Anchor.BottomLeft) ? -1
                    : (
                        anchor.EqualsAny(Anchor.TopRight, Anchor.MiddleRight, Anchor.BottomRight) ? 1 : 0
                    )
                ) * (int)GetCanvasSize().x,
                (
                    anchor.EqualsAny(Anchor.TopLeft, Anchor.TopCenter, Anchor.TopRight) ? 1
                    : (
                        anchor.EqualsAny(Anchor.BottomLeft, Anchor.BottomCenter, Anchor.BottomRight) ? -1 : 0
                    )
                ) * (int)GetCanvasSize().y) / 2;
        }

        public static Vector2Int GenerateWindowCoords(int x, int y, int width, int height, Anchor anchor = Anchor.MiddleCenter, Origin origin = Origin.TopCenter)
        {
            var offsetX = (origin.EqualsAny(Origin.TopLeft, Origin.MiddleLeft, Origin.BottomLeft) ? 1
                : (origin.EqualsAny(Origin.TopRight, Origin.MiddleRight, Origin.BottomRight) ? -1 : 0)) * width / 2;

            var offsetY = (origin.EqualsAny(Origin.MiddleLeft, Origin.MiddleCenter, Origin.MiddleRight) ? 1
                : (origin.EqualsAny(Origin.BottomLeft, Origin.BottomCenter, Origin.BottomRight) ? 2 : 0)) * height / 2;

            return ToCenterAnchor(new Vector2Int(x, y), anchor) + new Vector2Int(offsetX, offsetY);
        }

        private static bool EqualsAny(this Anchor a, params Anchor[] b)
        {
            return b.Any(e => e == a);
        }

        private static bool EqualsAny(this Origin a, params Origin[] b)
        {
            return b.Any(e => e == a);
        }

        private static RectTransform canvas;

        private static Vector2 GetCanvasSize()
        {
            canvas = canvas ?? GetCanvasRect();
            return canvas.sizeDelta;
        }

        private static RectTransform GetCanvasRect()
        {
            var temp = Builder.CreateHolder(Builder.SceneToAttach.BaseScene, "TEMP");
            var result = temp.transform.parent as RectTransform;
            Object.Destroy(temp);
            return result;
        }
    }
}
