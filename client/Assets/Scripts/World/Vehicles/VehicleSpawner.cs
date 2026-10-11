using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Parks the showcase fleet near the player (Feel lane F2). On CT Island v2: World's street bays (VehicleSpawn) that take
    /// each class, the demo-visible ones (showcase priority) first, and the nearest helipad for the helicopter. Where there
    /// are none (a test scene, a bay-less district): clear, flat, reachable ground found by rays and box checks in rings
    /// round the arrival, cars side by side like a car park, the helicopter on the largest clear patch close by.
    /// </summary>
    public static class VehicleSpawner
    {
        public static readonly string[] ShowcaseCars = { "compact", "sports", "roadstar", "suv", "scooter" };

        public static void SpawnShowcase(VehicleSystem system, Vector3 around, Vector3 facing)
        {
            var taken = new List<Vector3>();
            foreach (var id in ShowcaseCars)
            {
                var spec = VehicleCatalog.Get(id);
                if (spec == null) continue;
                if (TryBay(around, spec, taken, out var pos, out var rot) || TryClearGround(around, facing, Footprint(spec), taken, 6f, 34f, out pos, out rot))
                {
                    var v = system.Spawn(id, pos, rot);
                    if (v != null) taken.Add(pos);
                }
            }
            HeliPad pad = null;
            float best = 260f;
            foreach (var p in VehicleWorld.HeliPads)
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.transform.position, around);
                if (d < best) { best = d; pad = p; }
            }
            if (pad != null)
            {
                var face = Vector3.ProjectOnPlane(around - pad.transform.position, Vector3.up);
                system.Spawn("heli", pad.transform.position + Vector3.up * 0.05f, Quaternion.LookRotation(face.sqrMagnitude > 1f ? -face.normalized : Vector3.forward));
            }
            else if (TryClearGround(around, facing, new Vector3(9f, 4f, 11f), taken, 14f, 70f, out var hpos, out var hrot))
                system.Spawn("heli", hpos + Vector3.up * 0.05f, hrot);
        }

        private static Vector3 Footprint(VehicleSpec s) => s.kind == VehicleKind.Scooter ? new Vector3(1.0f, 1.6f, 2.1f) : new Vector3(2.5f, 1.9f, 5.0f);

        /// <summary>A free World bay for this class within 90 m: showcase bays first (they face the demo), then the nearest.</summary>
        private static bool TryBay(Vector3 around, VehicleSpec spec, List<Vector3> taken, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            VehicleSpawn best = null; float score = float.MaxValue;
            foreach (var b in VehicleWorld.BaysNear(around, 90f, spec.spawnClass))
            {
                if (b == null) continue;
                var p = b.transform.position;
                bool used = false;
                foreach (var t in taken) if ((t - p).sqrMagnitude < 16f) { used = true; break; }
                if (used || Physics.CheckBox(p + Vector3.up * 1.1f, new Vector3(1.0f, 0.8f, 2.2f), b.transform.rotation, 1 << CharacterLayers.Vehicle, QueryTriggerInteraction.Ignore)) continue;
                float s = Vector3.Distance(p, around) - b.showcase * 60f;   // a showcase bay beats a bay up to 60 m nearer
                if (s < score) { score = s; best = b; }
            }
            if (best == null) return false;
            pos = best.transform.position + Vector3.up * 0.05f; rot = best.transform.rotation;
            return true;
        }

        /// <summary>
        /// The nearest spot (rings from <paramref name="minR"/> to <paramref name="maxR"/>) where a box of
        /// <paramref name="size"/> (x wide, y tall, z long) sits on flat ground with nothing in it, away from what is
        /// already parked. Cars line up with <paramref name="facing"/>'s right (side by side, like a car park).
        /// </summary>
        public static bool TryClearGround(Vector3 around, Vector3 facing, Vector3 size, List<Vector3> taken, float minR, float maxR, out Vector3 pos, out Quaternion rot)
        {
            facing.y = 0f; if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
            facing.Normalize();
            var right = Vector3.Cross(Vector3.up, facing);
            rot = Quaternion.LookRotation(-right, Vector3.up);   // parked across the view, side on, the driver's door (left) to the player
            for (float r = minR; r <= maxR; r += 2.5f)
            {
                int steps = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * r / 3f));
                for (int i = 0; i < steps; i++)
                {
                    float a = (i % 2 == 0 ? 1 : -1) * (i / 2) * (2f * Mathf.PI / steps);
                    var dir = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * facing;
                    var p = around + dir * r;
                    if (!Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 14f, CarPhysics.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.97f || Mathf.Abs(hit.point.y - around.y) > 2.5f) continue;
                    bool near = false;
                    foreach (var t in taken) if ((t - hit.point).sqrMagnitude < (size.z * 0.6f + 2.5f) * (size.z * 0.6f + 2.5f)) { near = true; break; }
                    if (near) continue;
                    var center = hit.point + Vector3.up * (size.y * 0.5f + 0.15f);
                    if (Physics.CheckBox(center, size * 0.5f, rot, CarPhysics.GroundMask | (1 << CharacterLayers.Vehicle), QueryTriggerInteraction.Ignore)) continue;
                    if (!Corners(hit.point, size, rot, hit.point.y)) continue;
                    if (!Reachable(around, hit.point)) continue;
                    pos = hit.point + Vector3.up * 0.05f;
                    return true;
                }
            }
            pos = default;
            return false;
        }

        /// <summary>
        /// A spot the player could walk or drive to: on the walkable NavMesh where there is one (not in a fountain basin or a
        /// planter), above the sea, and in plain sight at knee height from where the player stands (no rim or wall between).
        /// </summary>
        public static bool Reachable(Vector3 from, Vector3 spot)
        {
            float sea = VehicleWorld.SeaLevel;
            if (!float.IsInfinity(sea) && spot.y < sea + 0.15f) return false;
            if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var nav, 3f, UnityEngine.AI.NavMesh.AllAreas))
            {
                if (Mathf.Abs(nav.position.y - spot.y) > 0.35f || (Flat(nav.position) - Flat(spot)).magnitude > 0.8f) return false;
            }
            var a = from + Vector3.up * 0.45f; var b = spot + Vector3.up * 0.45f;
            return !Physics.Linecast(a, b, CarPhysics.GroundMask, QueryTriggerInteraction.Ignore);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>All four corners of the footprint have ground within 0.25 m of the centre's height (no overhangs).</summary>
        private static bool Corners(Vector3 c, Vector3 size, Quaternion rot, float y)
        {
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var p = c + rot * new Vector3(size.x * 0.5f * sx, 0f, size.z * 0.5f * sz);
                    if (!Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 4f, CarPhysics.GroundMask, QueryTriggerInteraction.Ignore)) return false;
                    if (Mathf.Abs(hit.point.y - y) > 0.25f) return false;
                }
            return true;
        }
    }
}
