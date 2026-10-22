using System.Collections.Generic;
using CTLife.World.Roads;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// What vehicles use from the World lane (world-v2 contract, W1): streaming prefetch, the flight ceiling, sea level,
    /// terrain height, parking bays (<see cref="VehicleSpawn"/>), helipads and the road network. One place, so the
    /// vehicle code reads the same everywhere.
    /// </summary>
    public static class VehicleWorld
    {
        /// <summary>The contract parking class of a vehicle spec (compact | suv | sports | moped | roadstar).</summary>
        public static VehicleClass ClassOf(string spawnClass) => spawnClass switch
        {
            "compact" => VehicleClass.Compact, "suv" => VehicleClass.Suv, "sports" => VehicleClass.Sports,
            "moped" => VehicleClass.Moped, "roadstar" => VehicleClass.Roadstar, _ => VehicleClass.Any,
        };

        public static int ClassBit(string spawnClass) => (int)ClassOf(spawnClass);

        /// <summary>The CT Island v2 world is loaded (its markers and roads exist).</summary>
        public static bool OnIsland => IslandWorld.Current != null;
        public static RoadNetwork Roads => RoadNetwork.Active;
        public static float FlightCeiling => IslandStreamer.FlightCeiling;
        public static float SeaLevel => IslandStreamer.SeaLevel;
        public static float WaterSurfaceAt(Vector3 p) => IslandStreamer.WaterSurfaceAt(p.x, p.z);
        public static bool Dry(Vector3 p, float margin = 0.3f)
        {
            float water = WaterSurfaceAt(p);
            return float.IsNaN(water) || p.y > water + margin;
        }
        public static void Prefetch(Vector3 position, Vector3 velocity, float horizonSeconds) => IslandStreamer.Prefetch(position, velocity, horizonSeconds);
        public static bool IsReady(Vector3 position) => IslandStreamer.IsReady(position);

        public static bool TryGroundHeight(float x, float z, out float y)
        {
            y = IslandStreamer.GroundHeight(x, z);
            return !float.IsNaN(y) && !float.IsInfinity(y);
        }

        /// <summary>Parking bays within <paramref name="radius"/> that take this class, nearest first.</summary>
        public static List<VehicleSpawn> BaysNear(Vector3 p, float radius, string spawnClass) => IslandMarkers.SpawnsNear(p, radius, ClassOf(spawnClass));

        public static IReadOnlyList<HeliPad> HeliPads => IslandMarkers.HeliPads;

        /// <summary>Traffic density where a point is (cars per 100 m of lane; the contract's districts run 0.3 to 1.6).</summary>
        public static float TrafficDensityAt(Vector3 p, float fallback = 0.6f)
        {
            var z = IslandMarkers.ZoneAt(p);
            return z != null ? z.density : fallback;
        }
    }
}
