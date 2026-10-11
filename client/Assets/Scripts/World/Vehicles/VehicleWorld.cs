using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// What vehicles need from the World lane (world-v2 contract), bound by name at runtime so the Feel branch builds with or
    /// without World's W1 code: IslandStreamer.Prefetch / FlightCeiling / SeaLevel / GroundHeight and IslandMarkers'
    /// parking bays and helipads. Without them (the v1 island, a test scene) the defaults apply and spawns come from the
    /// clear-ground finder in <see cref="VehicleSpawner"/>.
    /// </summary>
    public static class VehicleWorld
    {
        public struct Bay { public Vector3 position; public Quaternion rotation; public int classes; public Vector2 size; }

        private static bool _bound;
        private static Action<Vector3, Vector3, float> _prefetch;
        private static Func<float> _ceiling, _sea;
        private static Func<float, float, float> _groundHeight;
        private static MethodInfo _spawnsNear;
        private static PropertyInfo _heliPads;
        private static Type _vehicleClass;

        /// <summary>World v2 VehicleClass bits: Compact 1, Suv 2, Sports 4, Moped 8, Roadstar 16.</summary>
        public static int ClassBit(string spawnClass) => spawnClass switch
        {
            "compact" => 1, "suv" => 2, "sports" => 4, "moped" => 8, "roadstar" => 16, _ => 31,
        };

        private static void Bind()
        {
            if (_bound) return;
            _bound = true;
            var asm = typeof(VenueSceneRoot).Assembly;
            var streamer = asm.GetType("CTLife.World.IslandStreamer");
            if (streamer != null)
            {
                var pf = streamer.GetMethod("Prefetch", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3), typeof(Vector3), typeof(float) }, null);
                if (pf != null) _prefetch = (Action<Vector3, Vector3, float>)Delegate.CreateDelegate(typeof(Action<Vector3, Vector3, float>), pf);
                var fc = streamer.GetProperty("FlightCeiling", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                if (fc != null) _ceiling = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), fc);
                var sl = streamer.GetProperty("SeaLevel", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                if (sl != null) _sea = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), sl);
                var gh = streamer.GetMethod("GroundHeight", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(float), typeof(float) }, null);
                if (gh != null) _groundHeight = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), gh);
            }
            var markers = asm.GetType("CTLife.World.IslandMarkers");
            _vehicleClass = asm.GetType("CTLife.World.VehicleClass");
            if (markers != null && _vehicleClass != null)
            {
                _spawnsNear = markers.GetMethod("SpawnsNear", BindingFlags.Public | BindingFlags.Static);
                _heliPads = markers.GetProperty("HeliPads", BindingFlags.Public | BindingFlags.Static);
            }
        }

        public static bool HasWorldV2 { get { Bind(); return _spawnsNear != null; } }

        public static float FlightCeiling { get { Bind(); return _ceiling != null ? _ceiling() : 214f; } }

        /// <summary>Sea level, or -infinity when the world has none (nothing to fall into).</summary>
        public static float SeaLevel { get { Bind(); return _sea != null ? _sea() : float.NegativeInfinity; } }

        public static void Prefetch(Vector3 position, Vector3 velocity, float horizonSeconds)
        {
            Bind();
            try { _prefetch?.Invoke(position, velocity, horizonSeconds); }
            catch (Exception) { /* the island may be mid-teardown; streaming is a hint */ }
        }

        public static bool TryGroundHeight(float x, float z, out float y)
        {
            Bind();
            y = 0f;
            if (_groundHeight == null) return false;
            y = _groundHeight(x, z);
            return !float.IsNaN(y) && !float.IsInfinity(y);
        }

        /// <summary>World v2 parking bays near a point that accept a class (by its contract name: compact, suv, sports, moped,
        /// roadstar; nearest first); empty without World v2. The name is matched to World's VehicleClass enum, so its
        /// numbering does not matter; the bit is the fallback.</summary>
        public static List<Bay> BaysNear(Vector3 p, float radius, string spawnClass)
        {
            Bind();
            if (_vehicleClass == null) return new List<Bay>();
            object cls = null;
            try { cls = Enum.Parse(_vehicleClass, spawnClass ?? "", true); } catch (ArgumentException) { }
            return BaysNear(p, radius, cls ?? Enum.ToObject(_vehicleClass, ClassBit(spawnClass)));
        }

        /// <summary>World v2 parking bays near a point that accept a class bit (see <see cref="ClassBit"/>).</summary>
        public static List<Bay> BaysNear(Vector3 p, float radius, int classBit)
        {
            Bind();
            if (_vehicleClass == null) return new List<Bay>();
            return BaysNear(p, radius, Enum.ToObject(_vehicleClass, classBit));
        }

        private static List<Bay> BaysNear(Vector3 p, float radius, object cls)
        {
            var list = new List<Bay>();
            if (_spawnsNear == null) return list;
            if (!(_spawnsNear.Invoke(null, new[] { (object)p, radius, cls }) is IEnumerable found)) return list;
            foreach (var o in found)
            {
                if (!(o is Component c) || c == null) continue;
                var t = c.GetType();
                int classes = Convert.ToInt32(t.GetField("classes")?.GetValue(c) ?? 31);
                var size = t.GetField("size")?.GetValue(c) is Vector2 v ? v : new Vector2(2.5f, 5.5f);
                list.Add(new Bay { position = c.transform.position, rotation = c.transform.rotation, classes = classes, size = size });
            }
            return list;
        }

        /// <summary>World v2 helipads (centre, radius); empty without World v2.</summary>
        public static List<(Vector3 center, float radius)> HeliPads()
        {
            Bind();
            var list = new List<(Vector3, float)>();
            if (_heliPads == null || !(_heliPads.GetValue(null) is IEnumerable pads)) return list;
            foreach (var o in pads)
            {
                if (!(o is Component c) || c == null) continue;
                float r = Convert.ToSingle(c.GetType().GetField("radius")?.GetValue(c) ?? 9f);
                list.Add((c.transform.position, r));
            }
            return list;
        }
    }
}
