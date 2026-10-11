using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// Creator presentation ordering. ForModels shuffles whole identities from a canonical identity-ID order, using the
    /// install seed. Outfit counts, clothing IDs and inferred demographics do not influence the character roster.
    /// Arrange/ForCreator retain the earlier authored-look ordering for existing callers and tests. None of these helpers
    /// changes the source catalog used by <see cref="CreatorLookCatalog.FallbackLook"/> for remote players.
    /// EditMode tests: CharacterIdentityTests and HomeUiTests.
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

        /// <summary>Legacy authoring-family metadata, including the two known UBC sources; never used to group identities.</summary>
        public static char FamilyOf(string headId)
        {
            var h = headId ?? "";
            if (h.Contains("_m_") || h == "head_snow" || h == "head_ubc_m") return 'm';
            if (h.Contains("_f_") || h == "head_rain" || h == "head_ubc_f") return 'f';
            return 'x';
        }

        /// <summary>
        /// Orders the already-grouped roster (unique identity IDs), preserving each model and its outfits by reference.
        /// Canonical ordinal sorting makes the result independent of catalog order. The explicit seeded shuffle stays
        /// reproducible across runtimes without using string hashes, outfit metadata or System.Random's implementation.
        /// </summary>
        public static List<BaseModel> ForModels(List<BaseModel> models, int seed)
        {
            var result = models == null ? new List<BaseModel>() : new List<BaseModel>(models);
            result.Sort((a, b) => StringComparer.Ordinal.Compare(a?.id, b?.id));
            uint state = (uint)(seed & 0x7fffffff) ^ 0x9e3779b9u;
            for (int i = result.Count - 1; i > 0; i--)
            {
                // Xorshift32; the high-bit salt keeps the normalized install seed away from the zero state.
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int j = (int)(state % (uint)(i + 1));
                var model = result[i]; result[i] = result[j]; result[j] = model;
            }
            return result;
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

        /// <summary>Legacy ordering of individual authored looks; the character roster uses ForModels after grouping.</summary>
        public static List<CreatorLookCatalog.Entry> ForCreator(List<CreatorLookCatalog.Entry> available, int seed)
        {
            var arrays = available.ConvertAll(e => e.look);
            var order = Arrange(arrays, seed);
            return order.ConvertAll(i => available[i]);
        }
    }
}
