using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// The vehicle prefabs (Resources/Vehicles/&lt;id&gt;), loaded asynchronously ahead of need (perf lane 2, 2026-10-23): a
    /// synchronous Resources.Load of a v3 car (three 2048 px textures) took ~200-300 ms on the Seeker, so the first traffic
    /// car of a kind, or the first parked stand-in, froze a frame mid-drive. Traffic asks with <c>wait: false</c> (and
    /// spawns another kind until this one is in); the player-facing spawns fall back to a synchronous load.
    /// </summary>
    public static class VehiclePrefabs
    {
        private static readonly Dictionary<string, GameObject> Loaded = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, ResourceRequest> Pending = new Dictionary<string, ResourceRequest>();

        public static void Preload(IEnumerable<string> ids) { foreach (var id in ids) Ready(id); }

        /// <summary>True once loaded (starting the async load the first time it is asked).</summary>
        public static bool Ready(string id)
        {
            if (string.IsNullOrEmpty(id) || Loaded.ContainsKey(id)) return true;
            if (!Pending.TryGetValue(id, out var req)) Pending[id] = req = Resources.LoadAsync<GameObject>("Vehicles/" + id);
            if (!req.isDone) return false;
            Pending.Remove(id);
            Loaded[id] = req.asset as GameObject;
            return true;
        }

        /// <summary>The prefab, or null when <paramref name="wait"/> is false and it is still loading.</summary>
        public static GameObject Get(string id, bool wait)
        {
            if (Ready(id)) return Loaded.TryGetValue(id, out var p) ? p : null;
            if (!wait) return null;
            var sync = Resources.Load<GameObject>("Vehicles/" + id);   // completes the pending request too
            Pending.Remove(id);
            Loaded[id] = sync;
            return sync;
        }
    }
}
