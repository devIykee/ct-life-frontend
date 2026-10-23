using System.Collections.Generic;
using CTLife.World;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    public sealed class ShowcasePlacementTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private static readonly Vector3 CarSize = new Vector3(2.5f, 1.9f, 5f);

        [TearDown]
        public void Cleanup()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        private GameObject Own(string name)
        {
            var go = new GameObject(name); _owned.Add(go); return go;
        }

        private BoxCollider Box(string name, Vector3 center, Vector3 size, int layer)
        {
            var go = Own(name); go.layer = layer; go.transform.position = center;
            var box = go.AddComponent<BoxCollider>(); box.size = size; return box;
        }

        private IslandWorld World()
        {
            var world = Own("showcase-test-world").AddComponent<IslandWorld>(); world.enabled = false;
            world.waterBodies = ScriptableObject.CreateInstance<IslandWaterBodies>(); _owned.Add(world.waterBodies);
            return world;
        }

        [Test]
        public void EmptyInteriorFloor_IsNotParkingGround()
        {
            var world = World();
            Box("lodge-floor", new Vector3(0, -0.2f, 0), new Vector3(30, 0.4f, 30), IslandWorld.InteriorLayer);
            Physics.SyncTransforms();
            // This is the old acceptance condition: floor hit, with an empty body-height box above it.
            Assert.IsTrue(Physics.Raycast(Vector3.up * 6, Vector3.down, out var oldHit, 14, CarPhysics.GroundMask));
            Assert.IsFalse(Physics.CheckBox(oldHit.point + Vector3.up * 1.1f, CarSize * 0.5f, Quaternion.identity,
                CarPhysics.GroundMask | (1 << CharacterLayers.Vehicle), QueryTriggerInteraction.Ignore));
            Assert.IsFalse(VehicleSpawner.TryPlacement(Vector3.zero, Quaternion.identity, CarSize, null, out _, world));
        }

        [TestCase(12)] // shell
        [TestCase(13)] // furniture
        [TestCase(15)] // vehicle-only doorway blocker
        [TestCase(20)] // parked car
        [TestCase(22)] // parked helicopter
        public void Candidate_RejectsOccupiedBayAcrossRelevantLayers(int layer)
        {
            var world = World();
            Box("ground", new Vector3(0, -0.5f, 0), new Vector3(30, 1, 30), 10);
            Box("obstacle", new Vector3(0, 1, 1), Vector3.one, layer);
            Physics.SyncTransforms();
            Assert.IsFalse(VehicleSpawner.TryPlacement(Vector3.zero, Quaternion.identity, CarSize, null, out _, world));
        }

        [Test]
        public void Candidate_RejectsUnloadedCornerAndFallbackOnlyGround()
        {
            var world = World();
            world.cells.Add(new IslandWorld.Cell { id = "not-loaded", rect = new Rect(1, -5, 10, 10) });
            var ground = Box("ground_fallback_0_0", new Vector3(0, -0.5f, 0), new Vector3(30, 1, 30), 10);
            ground.transform.SetParent(IslandGroundFallback.Create(world.transform).transform, true);
            Physics.SyncTransforms();
            Assert.IsFalse(VehicleSpawner.TryPlacement(Vector3.zero, Quaternion.identity, CarSize, null, out _, world));
            ground.name = "loaded-ground";
            ground.transform.SetParent(null, true);
            Assert.IsFalse(VehicleSpawner.TryPlacement(Vector3.zero, Quaternion.identity, CarSize, null, out _, world), "center ready but right corners not ready");
            Assert.IsTrue(VehicleSpawner.TryPlacement(Vector3.zero, Quaternion.identity, CarSize, null, out _, World()), "same ground with no unloaded cells");
        }

        [Test]
        public void Candidate_RejectsNarrowSupportAndRoofAboveRayOrigin()
        {
            var world = World();
            var deck = Box("narrow-deck", new Vector3(0, -0.5f, 0), new Vector3(1, 1, 10), 11);
            Physics.SyncTransforms();
            Assert.IsFalse(VehicleSpawner.TryPlacement(Vector3.zero, Quaternion.identity, CarSize, null, out _, world));
            deck.size = new Vector3(30, 1, 30);
            Box("roof", new Vector3(0, 9, 0), new Vector3(20, 0.5f, 20), 12);
            Physics.SyncTransforms();
            Assert.IsFalse(VehicleSpawner.TryPlacement(Vector3.zero, Quaternion.identity, CarSize, null, out _, world), "the old 6m probe starts under this roof");
        }

        [Test]
        public void Candidate_RejectsWaterUnderAnEdgeEvenWhenCenterIsDry()
        {
            var world = World();
            world.hfSamples = 5; world.hfCell = 1; world.hfMinX = -2; world.hfMinZ = -2; world.hfMin = -1; world.hfMax = 1;
            world.heightBytes = new TextAsset(new string('\0', 50)); _owned.Add(world.heightBytes);
            world.waterBodies.triangles = new[] { new IslandWaterBodies.Triangle {
                a = new Vector3(1, 0.1f, -3), b = new Vector3(3, 0.1f, -3), c = new Vector3(1, 0.1f, 3) } };
            Box("ground", new Vector3(0, -0.5f, 0), new Vector3(30, 1, 30), 10);
            Physics.SyncTransforms();
            Assert.IsTrue(float.IsNaN(world.WaterSurfaceAt(0, 0)));
            Assert.IsFalse(VehicleSpawner.TryPlacement(Vector3.zero, Quaternion.identity, CarSize, null, out _, world));
        }

        [TestCase(0f)]
        [TestCase(267.904f)]
        public void ExteriorAnchorAndFootprint_RespectRotatedPorch(float yaw)
        {
            var venue = new IslandVenue { origin = new Vector2(20, 30), yaw = yaw, elevation = 4,
                localFrame = new Rect(-13, -15, 26, 26), localWalk = new Rect(-12, -10, 24, 20) };
            venue.exit = venue.ToIsland(new Vector2(0, -10));
            var start = venue.ToIsland(new Vector2(0, -6.5f));
            var venues = new[] { venue };
            var anchor = VehicleSpawner.ExteriorAnchor(new Vector3(start.x, 4, start.y), venue.Rotation * Vector3.forward, venues, out var outward);
            Assert.Less(venue.ToLocal(new Vector2(anchor.x, anchor.z)).y, -18f);
            Assert.Greater(Vector3.Dot(outward, venue.Rotation * Vector3.back), 0.99f);
            var overlap = venue.ToIsland(new Vector2(0, -16));
            Assert.IsFalse(VehicleSpawner.OutsideVenues(new Vector3(overlap.x, 4, overlap.y), venue.Rotation, CarSize, venues), "center outside, rear still on porch");
            Assert.IsTrue(VehicleSpawner.OutsideVenues(anchor + outward * 6, venue.Rotation, CarSize, venues));
        }

        [TestCase("timeline_square")]
        [TestCase("hackathon_house")]
        public void AuthoredWorld_AllSixCraftFindExteriorSupportedSpace(string venueId)
        {
            // Actual shipped collider shapes/marker poses + full-resolution terrain, without starting game services.
            var source = Resources.Load<GameObject>(IslandWorld.ResourcePath).GetComponent<IslandWorld>();
            var world = World();
            world.venues = new List<IslandVenue>(source.venues);
            world.heightBytes = source.heightBytes; world.holeBytes = source.holeBytes;
            world.hfSamples = source.hfSamples; world.hfCell = source.hfCell;
            world.hfMinX = source.hfMinX; world.hfMinZ = source.hfMinZ; world.hfMin = source.hfMin; world.hfMax = source.hfMax;
            world.waterBodies = source.waterBodies; world.seaLevel = source.seaLevel; world.flightCeiling = source.flightCeiling;
            var venue = world.Find(venueId);
            var facing = venue.Rotation * Vector3.forward;
            var arrival = new Vector3(venue.exit.x, venue.elevation, venue.exit.y) + facing * 3.5f;
            var anchor = VehicleSpawner.ExteriorAnchor(arrival, facing, world.venues, out _);
            var target = Own("authored-physics").transform;
            var copies = new Dictionary<Transform, Transform>();
            foreach (var pad in source.GetComponentsInChildren<HeliPad>(true))
            {
                if (Vector3.Distance(pad.transform.position, anchor) > 260f) continue;
                CopyPhysics(pad.transform, target, copies, false);
                var clone = CopyTransform(pad.transform, target, copies).gameObject.AddComponent<HeliPad>(); clone.radius = pad.radius;
            }
            var heightfield = world.LoadHeightfield();
            foreach (var cell in source.cells)
            {
                if (IslandStreaming.DistanceToRect(new Vector2(anchor.x, anchor.z), cell.rect) > 100f) continue;
                var prefab = Resources.Load<GameObject>(IslandWorld.CellFolder + "/" + cell.id);
                CopyPhysics(prefab.transform, target, copies, false);
                foreach (var bay in prefab.GetComponentsInChildren<VehicleSpawn>(true))
                {
                    var clone = CopyTransform(bay.transform, target, copies).gameObject.AddComponent<VehicleSpawn>();
                    clone.classes = bay.classes; clone.showcase = bay.showcase; clone.size = bay.size;
                }
                var authoredTerrain = prefab.GetComponentInChildren<IslandTerrainCell>(true);
                if (authoredTerrain == null) continue;
                var terrain = Own("terrain-" + cell.id).AddComponent<IslandTerrainCell>();
                terrain.chunks = authoredTerrain.chunks; terrain.BuildNow(heightfield);
                foreach (var mesh in terrain.GetComponentsInChildren<MeshFilter>()) _owned.Add(mesh.sharedMesh);
            }
            var entry = Resources.Load<VenueContentEntry>("World/VenueContent/venue_" + venueId);
            var venueTransform = CopyTransform(entry.prefab.transform, target, copies);
            venueTransform.SetPositionAndRotation(venue.Position, venue.Rotation);
            CopyPhysics(entry.prefab.transform, target, copies, true);
            Physics.SyncTransforms();
            var taken = new List<Vector3>();
            foreach (var id in new[] { "compact", "sports", "roadstar", "suv", "scooter", "heli" })
            {
                Assert.IsTrue(VehicleSpawner.TryShowcasePosition(VehicleCatalog.Get(id), arrival, facing, taken, out var pos, out var rot, world), venueId + " " + id);
                TestContext.WriteLine($"showcase fixture {venueId} {id}: {pos:F2}");
                var size = id == "heli" ? new Vector3(9, 4, 11) : id == "scooter" ? new Vector3(1, 1.6f, 2.1f) : CarSize;
                Assert.IsTrue(VehicleSpawner.OutsideVenues(pos, rot, size, world.venues));
                taken.Add(pos);
                // A parked craft must also participate in the next craft's real overlap queries.
                var obstacle = Box("parked-" + id, pos + Vector3.up * (size.y * 0.5f), size, 20);
                obstacle.transform.rotation = rot;
                Physics.SyncTransforms();
            }
        }

        private static Transform CopyTransform(Transform source, Transform target, Dictionary<Transform, Transform> copies)
        {
            if (copies.TryGetValue(source, out var found)) return found;
            var go = new GameObject(source.name);
            go.transform.SetParent(source.parent != null ? CopyTransform(source.parent, target, copies) : target, false);
            go.transform.localPosition = source.localPosition; go.transform.localRotation = source.localRotation; go.transform.localScale = source.localScale;
            go.SetActive(source.gameObject.activeSelf);
            copies[source] = go.transform; return go.transform;
        }

        private static void CopyPhysics(Transform source, Transform target, Dictionary<Transform, Transform> copies, bool interior)
        {
            foreach (var original in source.GetComponentsInChildren<Collider>(true))
            {
                if (!original.enabled || original.isTrigger || !original.gameObject.activeSelf) continue;
                bool backdrop = false;
                if (interior)
                    for (var p = original.transform; p != null; p = p.parent)
                        if (p.name == "Backdrop") { backdrop = true; break; }
                if (backdrop) continue; // IslandWorld.Prepare disables the venue's standalone backdrop.
                var go = CopyTransform(original.transform, target, copies).gameObject;
                go.layer = interior && original.gameObject.layer == 0 ? 17 : original.gameObject.layer;
                switch (original)
                {
                    case BoxCollider b: var box = go.AddComponent<BoxCollider>(); box.center = b.center; box.size = b.size; break;
                    case SphereCollider s: var sphere = go.AddComponent<SphereCollider>(); sphere.center = s.center; sphere.radius = s.radius; break;
                    case CapsuleCollider c: var capsule = go.AddComponent<CapsuleCollider>(); capsule.center = c.center; capsule.radius = c.radius; capsule.height = c.height; capsule.direction = c.direction; break;
                    case MeshCollider m: var mesh = go.AddComponent<MeshCollider>(); mesh.sharedMesh = m.sharedMesh; mesh.convex = m.convex; break;
                    default: Assert.Fail("Uncovered authored collider " + original.GetType().Name); break;
                }
            }
        }
    }
}
