using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// The order the creator shows its looks in (user feedback 2026-10-09: every first character was dark-skinned, which read as
    /// one region's game; CT Life is global). Consecutive looks alternate skin-tone band (light, medium, deep) and body family
    /// (m, f) wherever the catalog allows, and the starting band, family and look rotate with a per-install seed, so first
    /// impressions vary across players. Presentation only: the authored catalog order is unchanged, because
    /// <see cref="CreatorLookCatalog.FallbackLook"/> must pick the same look for a remote player on every device.
    /// EditMode tests: HomeUiTests.
    /// </summary>
    public static class CreatorLookOrder
    {
        public enum Band { Light = 0, Medium = 1, Deep = 2, Unknown = 3 }

        private const string SeedKey = "ctlife.creator.lookSeed";

        /// <summary>The skin-tone id's band (look[0]): fair/light; olive/tan/warm; brown/dark/deep (and Snow/Rain's tone3).</summary>
        public static Band BandOf(string skinTone)
        {
            switch ((skinTone ?? "").Replace("skin_", ""))
            {
                case "fair": case "light": case "tone1": return Band.Light;
                case "olive": case "tan": case "warm": case "tone2": return Band.Medium;
                case "brown": case "dark": case "deep": case "tone3": case "tone4": return Band.Deep;
                default: return Band.Unknown;
            }
        }

        /// <summary>Body family from the head id: player_m_* / Snow → m, player_f_* / Rain → f, else x.</summary>
        public static char FamilyOf(string headId)
        {
            var h = headId ?? "";
            if (h.Contains("_m_") || h == "head_snow") return 'm';
            if (h.Contains("_f_") || h == "head_rain") return 'f';
            return 'x';
        }

        /// <summary>Interleaves `looks` (each a 7-id look array) by band and family. Returns indices into `looks`.</summary>
        public static List<int> Arrange(IReadOnlyList<string[]> looks, int seed)
        {
            var result = new List<int>();
            if (looks == null || looks.Count == 0) return result;
            seed = seed & 0x7fffffff;
            // Queues per band, each split by family; within a queue the authored order is kept, rotated by the seed.
            var queues = new Dictionary<(Band, char), Queue<int>>();
            var buckets = new Dictionary<(Band, char), List<int>>();
            for (int i = 0; i < looks.Count; i++)
            {
                var l = looks[i];
                var key = (BandOf(l != null && l.Length > 0 ? l[0] : null), FamilyOf(l != null && l.Length > 1 ? l[1] : null));
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(i);
            }
            foreach (var kv in buckets)
            {
                int offset = kv.Value.Count > 0 ? (seed / 7) % kv.Value.Count : 0;
                var q = new Queue<int>();
                for (int k = 0; k < kv.Value.Count; k++) q.Enqueue(kv.Value[(k + offset) % kv.Value.Count]);
                queues[kv.Key] = q;
            }
            var bands = new[] { Band.Light, Band.Medium, Band.Deep, Band.Unknown };
            int bandStart = seed % 3;
            char family = (seed / 3) % 2 == 0 ? 'f' : 'm';
            int remaining = looks.Count;
            int b = bandStart;
            int guard = 0;
            while (remaining > 0 && guard++ < looks.Count * 8)
            {
                var band = bands[b % bands.Length];
                b++;
                // This band: the alternating family first, then the other, then unknown.
                foreach (var f in new[] { family, family == 'f' ? 'm' : 'f', 'x' })
                {
                    if (!queues.TryGetValue((band, f), out var q) || q.Count == 0) continue;
                    result.Add(q.Dequeue());
                    remaining--;
                    family = f == 'f' ? 'm' : 'f';
                    break;
                }
            }
            // Anything left (only possible with odd data) keeps its authored order at the end.
            if (remaining > 0)
                for (int i = 0; i < looks.Count; i++) if (!result.Contains(i)) result.Add(i);
            return result;
        }

        /// <summary>A stable random seed for this install (PlayerPrefs), created on first use.</summary>
        public static int InstallSeed()
        {
            try
            {
                if (PlayerPrefs.HasKey(SeedKey)) return PlayerPrefs.GetInt(SeedKey);
                int seed = new System.Random(Environment.TickCount ^ Guid.NewGuid().GetHashCode()).Next(0, int.MaxValue);
                PlayerPrefs.SetInt(SeedKey, seed);
                PlayerPrefs.Save();
                return seed;
            }
            catch (Exception e)
            {
                Log.Warn("creator", "Look seed unavailable: " + e.Message);
                return 0;
            }
        }

        /// <summary>Captures and tests: pin the install seed.</summary>
        public static void SetInstallSeed(int seed)
        {
            PlayerPrefs.SetInt(SeedKey, seed & 0x7fffffff);
            PlayerPrefs.Save();
        }

        /// <summary>The catalog's entries in creator order for this install.</summary>
        public static List<CreatorLookCatalog.Entry> ForCreator(List<CreatorLookCatalog.Entry> available, int seed)
        {
            var arrays = available.ConvertAll(e => e.look);
            var order = Arrange(arrays, seed);
            return order.ConvertAll(i => available[i]);
        }
    }
}
