using UnityEngine;

namespace CTLife.UI.Map
{
    /// <summary>
    /// The map's coordinate chain, all pure math (EditMode-tested):
    ///   world (Unity) --VenueSceneRoot.ToLocal--> venue-local (x, z) --placement--> atlas metres (x east, y north)
    ///   --view--> minimap or full-map panel pixels.
    /// Angles: headings are radians clockwise from north (+z); placement/area rotations are degrees counter-clockwise.
    /// </summary>
    public static class MapMath
    {
        public static Vector2 Rotate(Vector2 v, float radiansCcw)
        {
            float c = Mathf.Cos(radiansCcw), s = Mathf.Sin(radiansCcw);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ------------------------------------------------------------------ area <-> uv

        /// <summary>Venue-local metres to the area texture's uv (0..1, v up).</summary>
        public static Vector2 AreaUv(MapArea a, Vector2 local)
        {
            var p = a.RotationDeg != 0f ? a.Center + Rotate(local - a.Center, -a.RotationDeg * Mathf.Deg2Rad) : local;
            return new Vector2((p.x - a.Min.x) / a.Size.x, (p.y - a.Min.y) / a.Size.y);
        }

        public static Vector2 LocalFromAreaUv(MapArea a, Vector2 uv)
        {
            var p = a.Min + Vector2.Scale(uv, a.Size);
            return a.RotationDeg != 0f ? a.Center + Rotate(p - a.Center, a.RotationDeg * Mathf.Deg2Rad) : p;
        }

        // ------------------------------------------------------------------ area <-> atlas

        public static Vector2 AtlasFromLocal(MapPlacement p, Vector2 local) =>
            p.Origin + Rotate(local * p.Scale, p.RotationDeg * Mathf.Deg2Rad);

        public static Vector2 LocalFromAtlas(MapPlacement p, Vector2 atlas) =>
            Rotate(atlas - p.Origin, -p.RotationDeg * Mathf.Deg2Rad) / p.Scale;

        public static Vector2 AtlasUv(MapAtlas a, Vector2 atlas) => new Vector2((atlas.x - a.Min.x) / a.Size.x, (atlas.y - a.Min.y) / a.Size.y);

        /// <summary>Unity world position to venue-local (x, z) given the venue root's pose (what VenueSceneRoot.ToLocal does).</summary>
        public static Vector2 LocalFromWorld(Vector3 world, Vector3 rootPosition, Quaternion rootRotation)
        {
            var l = Quaternion.Inverse(rootRotation) * (world - rootPosition);
            return new Vector2(l.x, l.z);
        }

        /// <summary>Heading (radians clockwise from north) of a forward vector in venue-local space.</summary>
        public static float Heading(Vector3 localForward) => Mathf.Atan2(localForward.x, localForward.z);

        // ------------------------------------------------------------------ minimap (player-up)

        /// <summary>
        /// Atlas point to minimap coordinates in [-1, 1] (x right, y up) for a player at <paramref name="center"/> facing
        /// <paramref name="heading"/>: the map turns so the heading points straight up. <paramref name="radius"/> metres = 1.
        /// </summary>
        public static Vector2 ToMinimap(Vector2 atlas, Vector2 center, float heading, float radius) =>
            Rotate(atlas - center, heading) / radius;

        public static Vector2 FromMinimap(Vector2 mini, Vector2 center, float heading, float radius) =>
            center + Rotate(mini * radius, -heading);

        /// <summary>
        /// Off-screen targets: a minimap point outside the rounded square (half-size 1 minus <paramref name="inset"/>) is pulled
        /// onto its edge along the ray from the centre. Returns whether it was clamped; <paramref name="angle"/> is the ray's
        /// direction (radians clockwise from up) for the edge arrow.
        /// </summary>
        public static bool ClampToEdge(Vector2 mini, float inset, out Vector2 clamped, out float angle)
        {
            angle = Mathf.Atan2(mini.x, mini.y);
            float edge = 1f - inset;
            float m = Mathf.Max(Mathf.Abs(mini.x), Mathf.Abs(mini.y));
            if (m <= edge) { clamped = mini; return false; }
            clamped = mini * (edge / m);
            return true;
        }

        /// <summary>Exponential smoothing of an angle along the short way round (frame-rate independent).</summary>
        public static float SmoothAngle(float current, float target, float sharpness, float dt)
        {
            float delta = Mathf.DeltaAngle(current * Mathf.Rad2Deg, target * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            return current + delta * (1f - Mathf.Exp(-sharpness * dt));
        }
    }

    /// <summary>
    /// The full map's tilted painterly view (ref 8): the map plane leans back by <see cref="Tilt"/> under a perspective camera.
    /// Points are panel pixels relative to the viewport's top-left. Pure math; the inverse is exact, so a finger-drag or a tap
    /// lands on the map point under it.
    /// </summary>
    public struct MapView
    {
        public Vector2 Center;          // atlas metres at the viewport centre
        public float PixelsPerMeter;    // scale at the centre line
        public float Tilt;              // radians the map leans back (0 = flat)
        public Vector2 Viewport;        // panel pixels
        public float Focal => Mathf.Max(Viewport.x, Viewport.y) * 1.6f;
        /// <summary>The fixed point of the tilt: a little below the middle, so the far edge recedes and the near edge stays readable.</summary>
        public Vector2 Pivot => new Vector2(Viewport.x / 2f, Viewport.y * 0.54f);

        public Vector2 Project(Vector2 atlas)
        {
            var d = (atlas - Center) * PixelsPerMeter;
            float vy = -d.y;                     // screen y grows down; north is up
            float depth = Focal - vy * Mathf.Sin(Tilt);
            float k = Focal / Mathf.Max(1f, depth);
            return Pivot + new Vector2(d.x * k, vy * Mathf.Cos(Tilt) * k);
        }

        /// <summary>Scale (pixels per metre) of the map at a screen point; marker and label sizes follow it.</summary>
        public float ScaleAt(Vector2 atlas)
        {
            var d = (atlas - Center) * PixelsPerMeter;
            return PixelsPerMeter * Focal / Mathf.Max(1f, Focal + d.y * Mathf.Sin(Tilt));
        }

        public Vector2 Unproject(Vector2 screen)
        {
            var s = screen - Pivot;
            float sin = Mathf.Sin(Tilt), cos = Mathf.Cos(Tilt);
            float vy = s.y * Focal / (Focal * cos + s.y * sin);
            float depth = Focal - vy * sin;
            float dx = s.x * depth / Focal;
            return Center + new Vector2(dx, -vy) / PixelsPerMeter;
        }

        /// <summary>Keep the view inside the atlas: the centre stays within <paramref name="bounds"/>, zoom within limits.</summary>
        public void Clamp(Rect bounds, float minPpm, float maxPpm)
        {
            PixelsPerMeter = Mathf.Clamp(PixelsPerMeter, minPpm, maxPpm);
            Center = new Vector2(Mathf.Clamp(Center.x, bounds.xMin, bounds.xMax), Mathf.Clamp(Center.y, bounds.yMin, bounds.yMax));
        }
    }
}
