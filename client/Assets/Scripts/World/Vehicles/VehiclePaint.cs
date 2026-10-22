using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Car colours for the v3 models (Feel lane F3): the Paint material is baked near-white, so a tint through
    /// <c>_BaseColor</c> in a per-slot property block gives the real colour without a material instance per car. The traffic
    /// palette is what streets actually look like: mostly white, black, silver and graphite, then deep blue, red, teal and
    /// sand, and now and then a Solana purple. No pale, washed-out greens.
    /// </summary>
    public static class VehiclePaint
    {
        public static readonly (Color color, float weight)[] Traffic =
        {
            (new Color(0.92f, 0.92f, 0.9f), 0.2f),     // white
            (new Color(0.07f, 0.075f, 0.085f), 0.16f), // black (any darker reads as a hole in the street on the Seeker)
            (new Color(0.6f, 0.62f, 0.64f), 0.15f),    // silver
            (new Color(0.2f, 0.21f, 0.23f), 0.1f),     // graphite
            (new Color(0.05f, 0.14f, 0.4f), 0.1f),     // deep blue
            (new Color(0.6f, 0.04f, 0.05f), 0.1f),     // red
            (new Color(0f, 0.4f, 0.43f), 0.07f),       // teal
            (new Color(0.7f, 0.6f, 0.43f), 0.07f),     // sand
            (new Color(0.42f, 0.16f, 0.82f), 0.05f),   // Solana purple
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock _block;

        /// <summary>A palette colour for a 0..1 roll (the traffic RNG, or a hash for parked cars).</summary>
        public static Color Pick(double roll)
        {
            float total = 0f;
            foreach (var p in Traffic) total += p.weight;
            double r = roll * total;
            foreach (var p in Traffic) { r -= p.weight; if (r <= 0) return p.color; }
            return Traffic[0].color;
        }

        /// <summary>Tint every Paint slot of <paramref name="v"/> (all LODs). A model without one (taxi, police) is unchanged.</summary>
        public static void Apply(Vehicle v, Color color)
        {
            if (v == null || v.PaintSlots.Count == 0) return;
            _block ??= new MaterialPropertyBlock();
            foreach (var (r, slot) in v.PaintSlots)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block, slot);
                _block.SetColor(BaseColorId, color);
                r.SetPropertyBlock(_block, slot);
            }
        }
    }
}
