using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Runtime lookup that replaces the single Resources registry, which hard-referenced every venue prefab so the
    /// first lookup (even a Home availability check) loaded all venue art (QA code-architecture §3 item 10).
    /// </summary>
    public static class VenueContent
    {
        public const string IndexPath = "World/VenueContentIndex";
        public const string EntryFolder = "World/VenueContent";

        private static VenueContentIndex _index;
        private static bool _indexLoaded;

        public static string EntryName(string id, bool home) => (home ? "home_" : "venue_") + id;

        private static VenueContentIndex Index
        {
            get
            {
                if (!_indexLoaded)
                {
                    _index = Resources.Load<VenueContentIndex>(IndexPath);
                    _indexLoaded = true;
                    if (_index == null) Log.Warn("venue", "No " + IndexPath + "; run CT Life → Venues → Sync runtime catalog");
                }
                return _index;
            }
        }

        /// <summary>Is shipped art available for this public venue? Does not load any prefab.</summary>
        public static bool Has(string venueId) => Index != null && !string.IsNullOrEmpty(venueId) && Index.venueIds.Contains(venueId);

        /// <summary>Only the inspected basement is enabled in this release; never substitute it for another home.</summary>
        public static bool HasPlayableHome(string homeId) =>
            homeId == "parents_basement" && Index != null && Index.playableHomeIds.Contains(homeId);

        public static VenueSceneRoot Find(string venueId) => Has(venueId) ? Load(venueId, false) : null;

        public static VenueSceneRoot FindHome(string homeId)
        {
            if (!HasPlayableHome(homeId)) return null;
            var prefab = Load(homeId, true);
            return prefab != null && prefab.venueId == "home" ? prefab : null;
        }

        private static VenueSceneRoot Load(string id, bool home)
        {
            var entry = Resources.Load<VenueContentEntry>(EntryFolder + "/" + EntryName(id, home));
            return entry != null ? entry.prefab : null;
        }

        /// <summary>Test hook.</summary>
        public static void ResetForTests() { _index = null; _indexLoaded = false; }
    }
}
