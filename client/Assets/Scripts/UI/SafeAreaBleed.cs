using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Full-bleed art for screens that sit inside the safe area: the art element is pushed out under the safe-area insets
    /// to the panel's edges. The insets are measured from the screen layer's world rect against the panel's, because
    /// `resolvedStyle.right` read 0 with the camera cutout on the right (phone held the other way, ROTATION_270), which left
    /// a light strip down the right edge of the title and onboarding screens on the Seeker (2026-10-08).
    /// </summary>
    public static class SafeAreaBleed
    {
        /// <summary>
        /// Keeps <paramref name="art"/> (a child of <paramref name="screenRoot"/>, absolutely positioned at 0 insets)
        /// extended to the panel edges, re-measuring whenever the screen or its layer changes geometry. The safe-area
        /// insets can swap sides (rotation 90 ↔ 270) without the screen's own size changing, so the layer is watched too.
        /// </summary>
        public static void Attach(VisualElement screenRoot, VisualElement art)
        {
            VisualElement layer = null;
            void Remeasure(GeometryChangedEvent _) => Apply(screenRoot, art);
            screenRoot.RegisterCallback<GeometryChangedEvent>(Remeasure);
            screenRoot.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                layer = screenRoot.parent;
                layer?.RegisterCallback<GeometryChangedEvent>(Remeasure);
            });
            screenRoot.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                layer?.UnregisterCallback<GeometryChangedEvent>(Remeasure);
                layer = null;
            });
        }

        public static void Apply(VisualElement screenRoot, VisualElement art)
        {
            var layer = screenRoot?.parent;
            var panelRoot = screenRoot?.panel?.visualTree;
            if (layer == null || panelRoot == null || art == null) return;
            Rect outer = panelRoot.worldBound, inner = layer.worldBound;
            if (float.IsNaN(inner.width) || inner.width <= 0f) return;
            var insets = Insets(outer, inner);
            art.style.left = -insets.x;
            art.style.top = -insets.y;
            art.style.right = -insets.z;
            art.style.bottom = -insets.w;
        }

        /// <summary>Left, top, right, bottom distances from <paramref name="inner"/> to <paramref name="outer"/> (never negative).</summary>
        public static Vector4 Insets(Rect outer, Rect inner) => new Vector4(
            Mathf.Max(0f, inner.xMin - outer.xMin), Mathf.Max(0f, inner.yMin - outer.yMin),
            Mathf.Max(0f, outer.xMax - inner.xMax), Mathf.Max(0f, outer.yMax - inner.yMax));
    }
}
