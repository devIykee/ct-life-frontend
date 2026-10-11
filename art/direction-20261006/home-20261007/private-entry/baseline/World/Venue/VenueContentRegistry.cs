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

        public List<Entry> venues = new List<Entry>();

        public VenueSceneRoot Find(string venueId)
        {
            foreach (var entry in venues)
                if (entry.venueId == venueId && entry.prefab != null) return entry.prefab;
            return null;
        }
    }
}
