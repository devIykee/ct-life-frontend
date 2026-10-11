using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Vehicle classes a parking bay accepts (world-v2 contract: compact | suv | sports | moped | roadstar).</summary>
    [Flags]
    public enum VehicleClass { None = 0, Compact = 1, Suv = 2, Sports = 4, Moped = 8, Roadstar = 16, Cars = Compact | Suv | Sports, Any = 31 }

    /// <summary>Live registry of the island's markers (whatever is streamed in right now).</summary>
    public static class IslandMarkers
    {
        private static readonly List<VehicleSpawn> Spawns = new List<VehicleSpawn>();
        private static readonly List<HeliPad> Pads = new List<HeliPad>();
        private static readonly List<TrafficZone> Zones = new List<TrafficZone>();
        public static IReadOnlyList<VehicleSpawn> VehicleSpawns => Spawns;
        public static IReadOnlyList<HeliPad> HeliPads => Pads;
        public static IReadOnlyList<TrafficZone> TrafficZones => Zones;
        public static event Action Changed;

        internal static void Register(VehicleSpawn s) { if (!Spawns.Contains(s)) { Spawns.Add(s); Changed?.Invoke(); } }
        internal static void Unregister(VehicleSpawn s) { if (Spawns.Remove(s)) Changed?.Invoke(); }
        internal static void Register(HeliPad p) { if (!Pads.Contains(p)) { Pads.Add(p); Changed?.Invoke(); } }
        internal static void Unregister(HeliPad p) { if (Pads.Remove(p)) Changed?.Invoke(); }
        internal static void Register(TrafficZone z) { if (!Zones.Contains(z)) { Zones.Add(z); Changed?.Invoke(); } }
        internal static void Unregister(TrafficZone z) { if (Zones.Remove(z)) Changed?.Invoke(); }

        /// <summary>Bays within <paramref name="radius"/> of a point accepting a class (nearest first).</summary>
        public static List<VehicleSpawn> SpawnsNear(Vector3 p, float radius, VehicleClass c = VehicleClass.Any)
        {
            var o = new List<VehicleSpawn>();
            float r2 = radius * radius;
            foreach (var s in Spawns)
                if (s != null && s.Accepts(c) && (s.transform.position - p).sqrMagnitude <= r2) o.Add(s);
            o.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
            return o;
        }

        /// <summary>The traffic zone containing a point (null outside every zone).</summary>
        public static TrafficZone ZoneAt(Vector3 p)
        {
            foreach (var z in Zones) if (z != null && z.Contains(p)) return z;
            return null;
        }
    }
}
