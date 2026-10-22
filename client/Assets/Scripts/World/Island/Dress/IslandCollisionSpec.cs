using System;
using UnityEngine;

namespace CTLife.World
{
    [Serializable]
    public sealed class IslandCollisionSpec
    {
        public enum Shape { Unbaked, None, Box, Capsule, Compound }
        public Shape shape;
        public Bounds[] boxes = Array.Empty<Bounds>();
        public Mesh footprint;
        public Vector3 center;
        public float radius, height;
        public string source;
    }

    /// <summary>Explicit collision categories. Render kinds such as "prop" also include flowers and overhead awnings.</summary>
    public static class IslandCollisionRules
    {
        public static IslandCollisionSpec.Shape ShapeFor(IslandKit.Proto p)
        {
            string id = (p.id ?? "").ToLowerInvariant();
            if (p.kind == "building") return IslandCollisionSpec.Shape.Compound;
            // Trees, lamps, poles and hero statues get no baked collider: IslandSolidProxies gives the ones near the player
            // trunk / post capsules and boxes at runtime. Vehicles have their own; the rest is soft vegetation or water dressing.
            if (p.kind == "tree" || p.kind == "lamp" || p.kind == "pole" || p.kind == "vehicle" || p.kind == "hero" ||
                p.kind == "crop" || p.kind == "grass" || p.kind == "bush" || p.kind == "boat" || p.kind == "ship" ||
                id.Contains("bush") || id.Contains("flower") || id.Contains("pebble") || id.Contains("awning"))
                return IslandCollisionSpec.Shape.None;
            if (id.Contains("parasol") || id.Contains("sign_stop") || id.Contains("sign_post")) return IslandCollisionSpec.Shape.Capsule;
            if (p.kind == "rock") return IslandCollisionSpec.Shape.Box;
            if (p.kind == "prop" || p.kind == "large_prop") return IslandCollisionSpec.Shape.Box;
            return IslandCollisionSpec.Shape.None;
        }

        public static bool SolidAtScale(IslandKit.Proto p, float horizontalScale, float verticalScale)
        {
            if (ShapeFor(p) == IslandCollisionSpec.Shape.None) return false;
            // A rock's decision is per instance: small dressing must not become ankle-high invisible obstacles.
            if (p.kind == "rock")
                return Mathf.Max(p.bounds.size.x * Mathf.Abs(horizontalScale), p.bounds.size.y * Mathf.Abs(verticalScale),
                    p.bounds.size.z * Mathf.Abs(horizontalScale)) >= 0.6f;
            return true;
        }

        public static IslandCollisionSpec Simple(IslandKit.Proto p)
        {
            var shape = ShapeFor(p);
            var spec = new IslandCollisionSpec { shape = shape, source = "LOD0 bounds / semantic kind" };
            if (shape == IslandCollisionSpec.Shape.Box)
            {
                // Keep within 3 cm of the visible surface; a percentage shrink is too large for sizable props.
                var size = p.bounds.size;
                size = new Vector3(Mathf.Max(0.04f, size.x - 0.04f), Mathf.Max(0.04f, size.y - 0.02f), Mathf.Max(0.04f, size.z - 0.04f));
                spec.boxes = new[] { new Bounds(p.bounds.center, size) };
            }
            else if (shape == IslandCollisionSpec.Shape.Capsule)
            {
                spec.height = p.bounds.size.y;
                spec.radius = Mathf.Min(0.10f, Mathf.Min(p.bounds.size.x, p.bounds.size.z) * 0.5f);
                spec.center = p.bounds.center;
            }
            return spec;
        }
    }
}
