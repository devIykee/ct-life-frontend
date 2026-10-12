using System.Collections.Generic;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>Pure panel-space placement: keep an object action near its anchor, clear of live HUD rectangles.</summary>
    public static class ContextPromptPlacement
    {
        public const float EdgeInset = 18f, Gap = 24f, Clearance = 12f;

        public static bool TryPlace(Vector2 anchor, Vector2 size, Rect safe, IReadOnlyList<Rect> obstacles,
            out Rect placed, Vector2? previousOffset = null)
        {
            placed = default;
            if (!Finite(anchor.x) || !Finite(anchor.y) || !Finite(size.x) || !Finite(size.y) ||
                !Finite(safe.x) || !Finite(safe.y) || !Finite(safe.width) || !Finite(safe.height) ||
                size.x <= 0 || size.y <= 0 || size.x > safe.width || size.y > safe.height) return false;

            var ideal = new Vector2(anchor.x - size.x * .5f, anchor.y - size.y - Gap);
            int count = obstacles?.Count ?? 0;
            float best = float.PositiveInfinity;
            // Candidate boundaries are the nearest-above location, viewport edges and each obstacle edge.
            // Their Cartesian product also finds free pockets between two different HUD clusters.
            for (int xi = 0; xi < 5 + count * 2; xi++)
            for (int yi = 0; yi < 5 + count * 2; yi++)
            {
                float x = Axis(xi, true, anchor, ideal, size, safe, obstacles);
                float y = Axis(yi, false, anchor, ideal, size, safe, obstacles);
                var r = new Rect(Mathf.Clamp(x, safe.xMin, safe.xMax - size.x),
                    Mathf.Clamp(y, safe.yMin, safe.yMax - size.y), size.x, size.y);
                bool clear = true;
                for (int i = 0; i < count; i++)
                    if (r.Overlaps(Inflate(obstacles[i], Clearance))) { clear = false; break; }
                // A displaced/clamped button must not cover the exact object/player point it describes.
                if (!clear || r.Overlaps(new Rect(anchor.x - 12, anchor.y - 8, 24, 24))) continue;
                float score = (r.position - ideal).sqrMagnitude;
                if (previousOffset.HasValue)
                    score += (r.position - anchor - previousOffset.Value).sqrMagnitude * .15f;
                if (score >= best) continue;
                best = score; placed = r;
            }
            return !float.IsPositiveInfinity(best);
        }

        private static float Axis(int index, bool x, Vector2 anchor, Vector2 ideal, Vector2 size, Rect safe, IReadOnlyList<Rect> obstacles)
        {
            if (index == 0) return x ? ideal.x : ideal.y;
            if (index == 1) return x ? safe.xMin : safe.yMin;
            if (index == 2) return x ? safe.xMax - size.x : safe.yMax - size.y;
            if (index == 3) return (x ? anchor.x : anchor.y) + Gap;
            if (index == 4) return (x ? anchor.x - size.x : anchor.y - size.y) - Gap;
            var r = obstacles[(index - 5) / 2];
            return (index - 5) % 2 == 0
                ? (x ? r.xMin - size.x : r.yMin - size.y) - Clearance
                : (x ? r.xMax : r.yMax) + Clearance;
        }

        public static Rect Inflate(Rect r, float amount) => Rect.MinMaxRect(r.xMin - amount, r.yMin - amount, r.xMax + amount, r.yMax + amount);
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    }
}
