using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Several streamed venues at once (Phase B): each brings its own baked lightmaps (VenueLightmaps). The shared lightmap
    /// table is rebuilt from every loaded venue on each add/remove, and each venue's renderers are pointed at their slots.
    /// (VenueLightmaps alone appends and restores a snapshot, which breaks when venues leave out of order.)
    /// </summary>
    public static class IslandLightmaps
    {
        private static readonly List<VenueLightmaps> Active = new List<VenueLightmaps>();

        public static void Add(VenueLightmaps v) { if (v != null && !Active.Contains(v)) { Active.Add(v); Rebuild(); } }
        public static void Remove(VenueLightmaps v) { if (Active.Remove(v)) Rebuild(); }
        public static void Clear() { Active.Clear(); LightmapSettings.lightmaps = new LightmapData[0]; }
        public static int Count => Active.Count;

        private static void Rebuild()
        {
            Active.RemoveAll(v => v == null);
            var table = new List<LightmapData>();
            foreach (var v in Active)
            {
                if (v.colors == null || v.colors.Length == 0) continue;
                int start = table.Count;
                foreach (var c in v.colors) table.Add(new LightmapData { lightmapColor = c });
                foreach (var b in v.bindings)
                {
                    if (b.renderer == null || b.lightmap < 0 || b.lightmap >= v.colors.Length) continue;
                    b.renderer.lightmapIndex = start + b.lightmap;
                    b.renderer.lightmapScaleOffset = b.scaleOffset;
                }
            }
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            LightmapSettings.lightmaps = table.ToArray();
        }
    }
}
