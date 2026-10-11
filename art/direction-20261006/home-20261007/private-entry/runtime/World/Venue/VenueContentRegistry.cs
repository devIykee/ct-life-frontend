using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Shipped venue art, addressed by contract venue id. Only referenced prefabs are included in the player;
    /// later venue scenes can still override this catalog through Addressables. No model discovery at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/Venue Content Registry")]
    public sealed class VenueContentRegistry : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string venueId;
            public VenueSceneRoot prefab;
        }

        [Serializable]
        public sealed class HomeEntry
        {
            public string homeId;
            public VenueSceneRoot prefab;
        }

        public List<Entry> venues = new List<Entry>();
        // Private own-home art is deliberately separate from the public venue lookup.
        public List<HomeEntry> homes = new List<HomeEntry>();

        public VenueSceneRoot FindHome(string homeId)
        {
            // Only the inspected basement is enabled in this release. Never substitute it for another home.
            if (homeId != "parents_basement") return null;
            foreach (var entry in homes)
                if (entry.homeId == homeId && entry.prefab != null && entry.prefab.venueId == "home") return entry.prefab;
            return null;
        }

        public VenueSceneRoot Find(string venueId)
        {
            foreach (var entry in venues)
                if (entry.venueId == venueId && entry.prefab != null) return entry.prefab;
            return null;
        }
    }
}
