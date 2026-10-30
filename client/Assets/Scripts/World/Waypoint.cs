using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// The player's map pin: one destination on CT Island, in atlas metres (on the island, world x/z). Set from the map
    /// (long-press or tap empty ground, or Guide me on a venue card); the HUD's overhead arrow leads to it and it clears on
    /// arrival. Local to this device and session.
    /// </summary>
    public static class Waypoint
    {
        /// <summary>Within this distance (metres, flat) the pin counts as reached.</summary>
        public const float ArriveRadius = 8f;

        public static bool Active { get; private set; }
        public static Vector2 Atlas { get; private set; }
        public static string Label { get; private set; }
        public static event Action Changed;

        public static void Set(Vector2 atlas, string label)
        {
            Active = true;
            Atlas = atlas;
            Label = label;
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (!Active) return;
            Active = false;
            Label = null;
            Changed?.Invoke();
        }

        /// <summary>Flat distance in metres from an island position to the pin.</summary>
        public static float DistanceFrom(Vector3 world) => Active ? Vector2.Distance(new Vector2(world.x, world.z), Atlas) : float.PositiveInfinity;
    }
}
