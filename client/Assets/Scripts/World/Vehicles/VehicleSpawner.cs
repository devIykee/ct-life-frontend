using System.Collections;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>Explicit offline filming fleet, parked outside the venue after the surrounding collision is loaded.</summary>
    public static class VehicleSpawner
    {
        public static readonly string[] ShowcaseCars = { "compact", "sports", "roadstar", "police", "sedan", "taxi", "suv", "scooter" };
        private static int SolidMask => CarPhysics.GroundMask | (1 << CharacterLayers.VehicleBlocker);
        private static int OccupancyMask => CharacterLayers.WorldMask | CharacterLayers.CharacterMask;
        private static Vector3 Footprint(VehicleSpec s) => s.kind == VehicleKind.Helicopter ? new Vector3(9f, 4f, 11f) :
            s.kind == VehicleKind.Scooter ? new Vector3(1f, 1.6f, 2.1f) : new Vector3(2.5f, 1.9f, 5f);

        public static void SpawnShowcase(VehicleSystem system, Vector3 around, Vector3 facing)
        {
            if (system == null || !VehicleSystem.ShowcaseEnabled || !RuntimeMode.IsOfflineIsland) return;
            system.StartCoroutine(SpawnWhenReady(system, around, facing));
        }

        private static IEnumerator SpawnWhenReady(VehicleSystem system, Vector3 around, Vector3 facing)
        {
            var world = IslandWorld.Current;
            var anchor = ExteriorAnchor(around, facing, world != null ? world.venues : null, out _);
            var load = IslandStreamer.EnsureLoaded(anchor, 100f);
            while (!load.IsCompleted) yield return null;
            if (load.IsFaulted || load.IsCanceled)
            {
                Log.Warn("vehicles", "showcase: exterior ground unavailable; fleet skipped (" +
                    (load.Exception?.GetBaseException().GetType().Name ?? "cancelled") + ")");
                yield break;
            }
            var taken = new List<Vector3>();
            var ids = new List<string>(ShowcaseCars) { "heli" };
            foreach (var id in ids)
            {
                if (system == null || world != IslandWorld.Current || !VehicleSystem.ShowcaseEnabled || !RuntimeMode.IsOfflineIsland) yield break;
                bool exists = false;
                foreach (var v in system.Vehicles)
                    if (v != null && v.specId == id) { exists = true; taken.Add(v.transform.position); break; }
                if (exists) continue;
                if (id == "heli" && NearestPad(anchor) is HeliPad pad)
                {
                    var padLoad = IslandStreamer.EnsureLoaded(pad.transform.position, 12f);
                    while (!padLoad.IsCompleted) yield return null;
                    if (padLoad.IsFaulted || padLoad.IsCanceled)
                        Log.Warn("vehicles", "showcase heli: pad ground unavailable; trying nearby exterior ground (" +
                            (padLoad.Exception?.GetBaseException().GetType().Name ?? "cancelled") + ")");
                }
                if (system == null || world != IslandWorld.Current) yield break;
                // New cell/vehicle transforms must reach PhysX before immediate overlap queries.
                Physics.SyncTransforms();
                if (TryShowcasePosition(VehicleCatalog.Get(id), around, facing, taken, out var pos, out var rot, world))
                {
                    var vehicle = system.Spawn(id, pos, rot);
                    if (vehicle != null)
                    {
                        taken.Add(pos);
                        Log.Info("vehicles", $"showcase {id}: parked at ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
                    }
                }
                else Log.Warn("vehicles", $"showcase {id}: no safe exterior placement near ({anchor.x:F2}, {anchor.y:F2}, {anchor.z:F2})");
                yield return null;
            }
        }

        /// <summary>Shared runtime/authored-world query. It does not instantiate or move anything.</summary>
        public static bool TryShowcasePosition(VehicleSpec spec, Vector3 around, Vector3 facing, List<Vector3> taken,
            out Vector3 pos, out Quaternion rot, IslandWorld world = null)
        {
            world = world != null ? world : IslandWorld.Current;
            pos = default; rot = Quaternion.identity;
            if (spec == null) return false;
            var anchor = ExteriorAnchor(around, facing, world != null ? world.venues : null, out var outward);
            var size = Footprint(spec);
            if (spec.kind == VehicleKind.Helicopter)
            {
                var pad = NearestPad(anchor);
                if (pad != null && pad.radius >= new Vector2(size.x, size.z).magnitude * 0.5f)
                {
                    var face = Vector3.ProjectOnPlane(pad.transform.position - anchor, Vector3.up);
                    rot = Quaternion.LookRotation(face.sqrMagnitude > 1f ? face.normalized : outward);
                    if (TryPlacement(pad.transform.position, rot, size, taken, out pos, world)) return true;
                }
            }
            else
            {
                float score = float.MaxValue; bool found = false;
                foreach (var bay in VehicleWorld.BaysNear(anchor, 90f, spec.spawnClass))
                {
                    if (bay == null) continue;
                    float s = Vector3.Distance(bay.transform.position, anchor) - bay.showcase * 60f;
                    if (s >= score || !TryPlacement(bay.transform.position, bay.transform.rotation, size, taken, out var p, world)) continue;
                    score = s; pos = p; rot = bay.transform.rotation; found = true;
                }
                if (found) return true;
            }
            bool heli = spec.kind == VehicleKind.Helicopter;
            return FindClearGround(anchor, outward, size, taken, heli ? 14f : 6f, heli ? 70f : 34f, out pos, out rot, world);
        }

        private static HeliPad NearestPad(Vector3 around)
        {
            HeliPad best = null; float distance = 260f;
            foreach (var pad in VehicleWorld.HeliPads)
            {
                if (pad == null) continue;
                float d = Vector3.Distance(pad.transform.position, around);
                if (d < distance) { distance = d; best = pad; }
            }
            return best;
        }

        /// <summary>Walk the authored door's outward axis past the rotated visual frame, including any porch.</summary>
        public static Vector3 ExteriorAnchor(Vector3 around, Vector3 facing, IReadOnlyList<IslandVenue> venues, out Vector3 outward)
        {
            outward = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
            if (outward.sqrMagnitude < 0.5f) outward = Vector3.forward;
            if (venues == null) return around;
            foreach (var venue in venues)
            {
                if (!venue.HasLocal || !venue.localFrame.Contains(venue.ToLocal(new Vector2(around.x, around.z)))) continue;
                var door = venue.ToLocal(venue.exit);
                var direction = (door - venue.localWalk.center).normalized;
                if (direction.sqrMagnitude < 0.5f) direction = Vector2.down;
                float distance = 0f;
                for (; distance < 80f && venue.localFrame.Contains(door + direction * distance); distance += 0.5f) { }
                var p = venue.ToIsland(door + direction * (distance + 3f));
                outward = venue.Rotation * new Vector3(direction.x, 0f, direction.y);
                return new Vector3(p.x, venue.elevation, p.y);
            }
            return around;
        }

        public static bool TryClearGround(Vector3 around, Vector3 facing, Vector3 size, List<Vector3> taken,
            float minR, float maxR, out Vector3 pos, out Quaternion rot) =>
            FindClearGround(around, facing, size, taken, minR, maxR, out pos, out rot, IslandWorld.Current);

        private static bool FindClearGround(Vector3 around, Vector3 facing, Vector3 size, List<Vector3> taken,
            float minR, float maxR, out Vector3 pos, out Quaternion rot, IslandWorld world)
        {
            facing.y = 0f; if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
            facing.Normalize();
            rot = Quaternion.LookRotation(-Vector3.Cross(Vector3.up, facing), Vector3.up);
            for (float r = minR; r <= maxR; r += 2.5f)
            {
                int steps = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * r / 3f));
                for (int i = 0; i < steps; i++)
                {
                    float a = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * (360f / steps);
                    var p = around + Quaternion.AngleAxis(a, Vector3.up) * facing * r;
                    if (TryPlacement(p, rot, size, taken, out pos, world) && ClearApproach(around, pos)) return true;
                }
            }
            pos = default; return false;
        }

        /// <summary>Bays, pads and fallback positions use the same loaded, dry, supported and unobstructed footprint.</summary>
        public static bool TryPlacement(Vector3 candidate, Quaternion rotation, Vector3 size, List<Vector3> taken,
            out Vector3 position, IslandWorld world = null)
        {
            position = default;
            if (world != null && !OutsideVenues(candidate, rotation, size, world.venues)) return false;
            if (!Support(candidate, candidate.y, world, out var center)) return false;
            if (taken != null)
                foreach (var t in taken)
                    if ((t - center).sqrMagnitude < Mathf.Pow(size.z * 0.6f + 2.5f, 2f)) return false;
            if (Physics.CheckBox(center + Vector3.up * (size.y * 0.5f + 0.15f), size * 0.5f, rotation,
                    OccupancyMask, QueryTriggerInteraction.Ignore)) return false;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && z == 0) continue;
                var sample = center + rotation * new Vector3(size.x * 0.5f * x, 0f, size.z * 0.5f * z);
                if (!Support(sample, center.y, world, out var point) || Mathf.Abs(point.y - center.y) > 0.25f) return false;
            }
            position = center + Vector3.up * 0.05f; return true;
        }

        private static bool Support(Vector3 sample, float referenceY, IslandWorld world, out Vector3 point)
        {
            point = default;
            if (world != null && !world.IsReady(sample)) return false;
            // Start above roofs, below the ceiling collider: rays starting inside a collider do not hit that collider.
            float top = world != null ? world.flightCeiling - 0.5f : sample.y + 6f;
            if (!Physics.Raycast(new Vector3(sample.x, top, sample.z), Vector3.down, out var hit,
                    world != null ? world.flightCeiling - world.seaLevel + 20f : 14f, SolidMask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.normal.y < 0.97f || Mathf.Abs(hit.point.y - referenceY) > 2.5f) return false;
            if (world != null && hit.collider.gameObject.layer != IslandTerrainCell.GroundLayer && hit.collider.gameObject.layer != 11) return false;
            // The dedicated fallback owner contains only coarse meshes, never ordinary streamed floors.
            if (hit.collider.GetComponentInParent<IslandGroundFallback>() != null) return false;
            float water = world != null ? world.WaterSurfaceAt(sample.x, sample.z) : float.NaN;
            if (!float.IsNaN(water) && hit.point.y <= water + 0.15f) return false;
            point = hit.point; return true;
        }

        /// <summary>Separating-axis footprint test against every authored venue frame, including rotated porches.</summary>
        public static bool OutsideVenues(Vector3 center, Quaternion rotation, Vector3 size, IReadOnlyList<IslandVenue> venues)
        {
            if (venues == null) return true;
            foreach (var venue in venues)
            {
                bool local = venue.HasLocal && venue.localFrame.width > 0f;
                var frame = local ? venue.localFrame : venue.frame;
                var p = local ? venue.ToLocal(new Vector2(center.x, center.z)) : new Vector2(center.x, center.z);
                var relative = local ? Quaternion.Inverse(venue.Rotation) * rotation : rotation;
                var r = relative * Vector3.right; var f = relative * Vector3.forward;
                var right = new Vector2(r.x, r.z); var forward = new Vector2(f.x, f.z);
                var delta = p - frame.center; var half = frame.size * 0.5f + Vector2.one * 0.25f;
                bool separated = false;
                for (int i = 0; i < 4; i++)
                {
                    var axis = i == 0 ? Vector2.right : i == 1 ? Vector2.up : i == 2 ? right : forward;
                    float vehicleRadius = Mathf.Abs(Vector2.Dot(axis, right)) * size.x * 0.5f + Mathf.Abs(Vector2.Dot(axis, forward)) * size.z * 0.5f;
                    float frameRadius = Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y;
                    if (Mathf.Abs(Vector2.Dot(delta, axis)) > vehicleRadius + frameRadius) { separated = true; break; }
                }
                if (!separated) return false;
            }
            return true;
        }

        private static bool ClearApproach(Vector3 from, Vector3 spot) =>
            !Physics.Linecast(from + Vector3.up * 0.45f, spot + Vector3.up * 0.45f, SolidMask, QueryTriggerInteraction.Ignore);

        public static bool Reachable(Vector3 from, Vector3 spot) =>
            VehicleWorld.Dry(spot, 0.15f) && VehicleWorld.IsReady(spot) && ClearApproach(from, spot);
    }
}
