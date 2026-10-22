using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// World v3 (2026-10-22): an open-air venue diorama on the island (Timeline Square's towers and halls, and the like)
    /// keeps its geometry, doors and palette colours, but its big shell pieces swap the baked palette material for the
    /// island's venue reskin (CT Life/World/Venue Reskin: real plaster walls and clay-tile roofs, lit by the island sun and
    /// sky), so the landmarks stop reading as toys next to the v3 buildings. Small props keep their own look; venues
    /// inside a street-side shell (interiors) are left alone.
    /// </summary>
    public static class IslandVenueReskin
    {
        private static readonly Dictionary<(Material, Texture), Material> Made = new Dictionary<(Material, Texture), Material>();
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Renderers whose bounds reach this size (m) count as shell pieces.</summary>
        public const float MinShellSize = 4.5f;

        public static int Apply(Transform root, Material reskin)
        {
            if (root == null || reskin == null) return 0;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var b = r.bounds;
                if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < MinShellSize) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !IsPalette(m)) continue;
                    var pal = m.HasProperty(BaseMapId) ? m.GetTexture(BaseMapId) : null;
                    if (pal == null) continue;
                    if (!Made.TryGetValue((reskin, pal), out var mm) || mm == null)
                    {
                        mm = new Material(reskin) { name = reskin.name + " (" + pal.name + ")" };
                        mm.SetTexture(BaseMapId, pal);
                        Made[(reskin, pal)] = mm;
                    }
                    if (m.HasProperty(BaseColorId)) { /* palette swatches carry the colour; the material tint stays white */ }
                    mats[i] = mm;
                    changed = true;
                }
                if (!changed) continue;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                n++;
            }
            return n;
        }

        /// <summary>The venues' palette-atlas materials (baked ctl palette, VB palette, kit colour maps).</summary>
        private static bool IsPalette(Material m)
        {
            string n = m.name;
            return n.Contains("ctl_palette") || n.Contains("VB_Palette") || n.Contains("colormap");
        }
    }
}
