using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A screen on CT Island's streets (World lane): a roadside or rooftop billboard whose panel material is named
    /// Screen_bb_island_* (house_ads.json rules: kind billboard). Registered while its cell is streamed in, so
    /// LiveSurfaces can add the billboards near the current venue to its live ad rotation.
    /// </summary>
    public sealed class IslandScreen : MonoBehaviour
    {
        private static readonly List<IslandScreen> Live = new List<IslandScreen>();
        public static IReadOnlyList<IslandScreen> All => Live;

        public MeshRenderer screen;

        private void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        private void OnDisable() => Live.Remove(this);

        /// <summary>The nearest loaded island screens to a point (closest first), at most max of them.</summary>
        public static List<IslandScreen> Near(Vector3 p, float radius, int max)
        {
            var list = new List<IslandScreen>();
            float r2 = radius * radius;
            foreach (var s in Live)
                if (s != null && s.screen != null && (s.transform.position - p).sqrMagnitude <= r2) list.Add(s);
            list.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }
    }
}
