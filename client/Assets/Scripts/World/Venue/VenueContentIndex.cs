using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Ids only, no prefab references: `Resources/World/VenueContentIndex.asset`. Cheap to load and keep.</summary>
    public sealed class VenueContentIndex : ScriptableObject
    {
        public List<string> venueIds = new List<string>();
        /// <summary>Homes whose prefab has navigation and baked lightmaps (checked when the catalog is synced).</summary>
        public List<string> playableHomeIds = new List<string>();
    }

}
