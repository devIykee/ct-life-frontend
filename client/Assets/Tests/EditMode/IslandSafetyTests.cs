using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.EditorTools.IslandV2;
using CTLife.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    public sealed class IslandSafetyTests
    {
        private static IslandHeightfield Flat(float y) => new IslandHeightfield { N = 17, Cell = 2, MinX = -16, MinZ = -16,
            Size = 32, Sea = -6, H = Enumerable.Repeat(y, 17 * 17).ToArray() };

        [Test]
        public void Water_QueryInterpolatesRiver_AndDoesNotFloodDryBanks()
        {
            var water = ScriptableObject.CreateInstance<IslandWaterBodies>();
            try
            {
                water.triangles = new[] { new IslandWaterBodies.Triangle { a = new Vector3(0, 8, 0), b = new Vector3(8, 4, 0), c = new Vector3(0, 8, 8) } };
                Assert.AreEqual(7f, water.SurfaceAt(2, 2, Flat(0), -6), 1e-4f);
                Assert.IsTrue(float.IsNaN(water.SurfaceAt(7, 7, Flat(0), -6)), "outside the river triangle is dry");
                Assert.IsTrue(float.IsNaN(water.SurfaceAt(2, 2, Flat(10), -6)), "the drawn ribbon is occluded by a higher bank");
                Assert.AreEqual(-6f, water.SurfaceAt(-8, -8, Flat(-10), -6));
                Assert.IsTrue(float.IsNaN(water.SurfaceAt(-8, -8, Flat(-6), -6)), "shore equality is not submerged");
                Assert.IsTrue(float.IsNaN(water.SurfaceAt(0, 0, null, -6)));
            }
            finally { Object.DestroyImmediate(water); }
        }

        [Test]
        public void AuthoredWater_QueryMatchesEveryLakeAndRiverTriangle()
        {
            var prefab = Resources.Load<GameObject>(IslandWorld.ResourcePath);
            var world = prefab.GetComponent<IslandWorld>();
            var water = ScriptableObject.CreateInstance<IslandWaterBodies>();
            try
            {
                water.triangles = IslandWaterBodies.Capture(prefab.transform.Find("Water"));
                Assert.Greater(water.triangles.Length, 72, "two lakes plus river are captured");
                var h = world.LoadHeightfield();
                int wet = 0;
                foreach (var t in water.triangles)
                {
                    var p = (t.a + t.b + t.c) / 3f;
                    if (h.Sample(p.x, p.z) >= p.y) continue;
                    Assert.GreaterOrEqual(water.SurfaceAt(p.x, p.z, h, world.seaLevel), p.y - 0.001f);
                    wet++;
                }
                Assert.Greater(wet, 72);
                Assert.IsTrue(float.IsNaN(water.SurfaceAt(0, 0, h, world.seaLevel)), "Timeline plaza is dry");
                Assert.AreEqual(world.seaLevel, water.SurfaceAt(0, -600, h, world.seaLevel));
            }
            finally { Object.DestroyImmediate(water); }
        }

        [Test]
        public void FallbackEnvelope_DoesNotCreateInvisibleHills_AndRetainsTunnelHoles()
        {
            var h = Flat(12);
            h.H[8 * h.N + 7] = -3; // narrow road cut which plain 8m corner sampling would bridge over
            h.Hole = new bool[h.H.Length]; h.Hole[3 * h.N + 3] = true;
            var lower = IslandGroundFallback.LowerEnvelope(h, 4);
            Assert.AreSame(h.Hole, lower.Hole);
            var mesh = lower.Grid(0, 0, 16, 4, false, "fallback_test", Vector3.zero);
            try
            {
                var v = mesh.vertices; var idx = mesh.triangles;
                for (int i = 0; i < idx.Length; i += 3)
                {
                    var t = new IslandWaterBodies.Triangle { a = v[idx[i]], b = v[idx[i + 1]], c = v[idx[i + 2]] };
                    for (int a = 0; a <= 4; a++) for (int b = 0; b <= 4 - a; b++)
                    {
                        var p = t.a * (a / 4f) + t.b * (b / 4f) + t.c * (1f - (a + b) / 4f);
                        Assert.LessOrEqual(p.y, h.Sample(p.x, p.z) - IslandGroundFallback.Clearance + 0.001f);
                    }
                }
                Assert.Less(idx.Length, 4 * 4 * 6, "a tunnel portal remains an opening");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void FallbackOwner_IsolatesCoarseFloorFromRoadAndTunnelHits()
        {
            var root = new GameObject("island-test");
            root.transform.position = new Vector3(3000, 0, 3000);
            try
            {
                var fallback = IslandGroundFallback.Create(root.transform);
                var cell = new GameObject("cell"); cell.transform.SetParent(root.transform, false);
                var road = new GameObject("road") { layer = 11 }; road.transform.SetParent(cell.transform, false);
                road.transform.localPosition = new Vector3(-4, 3, 0);
                var roadCollider = road.AddComponent<BoxCollider>();
                var tunnel = new GameObject("tunnel-floor") { layer = 11 }; tunnel.transform.SetParent(cell.transform, false);
                tunnel.transform.localPosition = new Vector3(0, -5, 0);
                var tunnelCollider = tunnel.AddComponent<BoxCollider>();
                var coarse = new GameObject("coarse-floor") { layer = 10 }; coarse.transform.SetParent(fallback.transform, false);
                coarse.transform.localPosition = new Vector3(4, 0, 0);
                var coarseCollider = coarse.AddComponent<BoxCollider>();
                Physics.SyncTransforms();
                Assert.IsNull(root.GetComponent<IslandGroundFallback>(), "the world must not own fallback classification");
                foreach (var floor in new[] { roadCollider, tunnelCollider, coarseCollider })
                {
                    Assert.IsTrue(Physics.Raycast(floor.transform.position + Vector3.up * 10, Vector3.down,
                        out var hit, 12, (1 << 10) | (1 << 11), QueryTriggerInteraction.Ignore));
                    Assert.AreSame(floor, hit.collider);
                    Assert.AreEqual(floor == coarseCollider ? fallback : null,
                        hit.collider.GetComponentInParent<IslandGroundFallback>(), floor.name);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CollisionMetadata_LeavesFoliageAndAwningsOpen_BlocksFurnitureAtItsScale()
        {
            foreach (string id in new[] { "bench_city", "trash_can", "kyc_firehydrant", "pp_vending_machine", "pp_scooter", "pp_vendor_cart", "pp_market_stalls", "planter_round" })
            {
                var p = new IslandKit.Proto { id = id, kind = "prop", bounds = new Bounds(Vector3.up, new Vector3(2, 2, 1)) };
                Assert.AreEqual(IslandCollisionSpec.Shape.Box, IslandCollisionRules.Simple(p).shape, id);
            }
            foreach (string id in new[] { "flowers_pink_group", "bush_round", "kc_detail_awning", "w_pebble_round_1" })
                Assert.AreEqual(IslandCollisionSpec.Shape.None, IslandCollisionRules.ShapeFor(new IslandKit.Proto { id = id, kind = "prop" }), id);
            var rock = new IslandKit.Proto { id = "rock", kind = "rock", bounds = new Bounds(Vector3.zero, Vector3.one) };
            Assert.IsFalse(IslandCollisionRules.SolidAtScale(rock, 0.3f, 0.3f));
            Assert.IsTrue(IslandCollisionRules.SolidAtScale(rock, 0.8f, 0.8f));
            Assert.AreEqual(IslandCollisionSpec.Shape.Capsule, IslandCollisionRules.ShapeFor(new IslandKit.Proto { id = "kc_detail_parasol_a", kind = "prop" }));
        }

        [Test]
        public void BuildingCompound_PreservesLShapedRecess()
        {
            var polygon = new[] { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(2, 0), new Vector2(0, 0), new Vector2(0, 2), new Vector2(-2, 2) };
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length]; int k = vertices.Count;
                vertices.AddRange(new[] { new Vector3(a.x, 0, a.y), new Vector3(a.x, 6, a.y), new Vector3(b.x, 0, b.y), new Vector3(b.x, 6, b.y) });
                triangles.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
            }
            var boxes = IslandV2Collision.FootprintBoxes(vertices.ToArray(), triangles.ToArray(), new Bounds(Vector3.up * 3, new Vector3(4, 6, 4)));
            Assert.That(boxes.Length, Is.InRange(2, 3));
            Assert.IsTrue(boxes.Any(b => b.Contains(new Vector3(-1, 1, 1))));
            Assert.IsTrue(boxes.Any(b => b.Contains(new Vector3(1, 1, -1))));
            Assert.IsFalse(boxes.Any(b => b.Contains(new Vector3(1, 1, 1))), "the recessed corner must remain walkable");
        }

        [Test]
        public void BuildingCompound_OverlappingClosedPartsDoNotPunchFalseHoles()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            // Main wall body plus an overlapping facade volume with the same outward winding.
            foreach (var polygon in new[] {
                new[] { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(2, 2), new Vector2(-2, 2) },
                new[] { new Vector2(-1, -2), new Vector2(1, -2), new Vector2(1, 0), new Vector2(-1, 0) } })
                for (int i = 0; i < polygon.Length; i++)
                {
                    var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length]; int k = vertices.Count;
                    vertices.AddRange(new[] { new Vector3(a.x, 0, a.y), new Vector3(a.x, 6, a.y), new Vector3(b.x, 0, b.y), new Vector3(b.x, 6, b.y) });
                    triangles.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
                }
            var boxes = IslandV2Collision.FootprintBoxes(vertices.ToArray(), triangles.ToArray(), new Bounds(Vector3.up * 3, new Vector3(4, 6, 4)));
            Assert.AreEqual(1, boxes.Length, "overlapping facades form one filled footprint");
            Assert.IsTrue(boxes[0].Contains(new Vector3(0, 1, -1)));
        }

        [Test]
        public void StaticFootprintMesh_HasOutwardWallsClosedCapsAndOpenLRecess()
        {
            var boxes = new[] { new Bounds(new Vector3(-1, 3, 0), new Vector3(2, 6, 4)), new Bounds(new Vector3(1, 3, -1), new Vector3(2, 6, 2)) };
            var mesh = IslandV2Collision.FootprintMesh(boxes); var go = new GameObject("isolated_footprint_physics_test");
            try
            {
                var origin = new Vector3(20000, 0, 20000); go.transform.position = origin;
                var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                Assert.IsFalse(collider.convex, "static concave footprint must retain the recess");
                Physics.SyncTransforms();
                Assert.IsTrue(collider.Raycast(new Ray(origin + new Vector3(5, 1, -1), Vector3.left), out var wall, 10));
                Assert.AreEqual(2, wall.point.x - origin.x, 0.01f); Assert.Greater(wall.normal.x, 0.9f);
                Assert.IsTrue(collider.Raycast(new Ray(origin + new Vector3(5, 1, 1), Vector3.left), out var recess, 10));
                Assert.AreEqual(0, recess.point.x - origin.x, 0.01f, "the open L corner is not filled by a convex hull");
                Assert.IsFalse(collider.Raycast(new Ray(origin + new Vector3(1, 10, 1), Vector3.down), out _, 12));
                Assert.IsTrue(collider.Raycast(new Ray(origin + new Vector3(-1, 10, 1), Vector3.down), out var roof, 12));
                Assert.AreEqual(6, roof.point.y, 0.01f);
                Assert.IsTrue(collider.Raycast(new Ray(origin + new Vector3(-1, -2, 1), Vector3.up), out var floor, 12));
                Assert.AreEqual(0, floor.point.y, 0.01f);
                Assert.IsTrue(Physics.CapsuleCast(origin + new Vector3(5, 0.4f, -1), origin + new Vector3(5, 1.4f, -1), 0.3f,
                    Vector3.left, out var sweep, 6, ~0, QueryTriggerInteraction.Ignore));
                Assert.AreSame(collider, sweep.collider); Assert.That(sweep.distance, Is.InRange(2.65f, 2.75f));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void PlacementRepair_ReplaysOnlyExactOriginalAndIsIdempotent()
        {
            var moves = JArray.Parse("[{\"proto\":\"bench\",\"from\":[1,2,3],\"to\":[4,5,6]}]");
            var result = IslandV2PlacementRepair.ApplyPosition("bench", new Vector3(1, 2, 3), moves);
            Assert.AreEqual(new Vector3(4, 5, 6), result);
            Assert.AreEqual(result, IslandV2PlacementRepair.ApplyPosition("bench", result, moves));
            Assert.AreEqual(new Vector3(1, 2, 3), IslandV2PlacementRepair.ApplyPosition("rock", new Vector3(1, 2, 3), moves));
            Assert.AreEqual(new Vector3(1.1f, 2, 3), IslandV2PlacementRepair.ApplyPosition("bench", new Vector3(1.1f, 2, 3), moves));
        }

        [Test]
        public void BakedSafetyMetadata_IsCompleteAndMatchesCurrentRenderWater()
        {
            var kit = AssetDatabase.LoadAssetAtPath<IslandKit>(IslandV2Kit.KitAsset);
            foreach (var p in kit.protos)
            {
                Assert.IsNotNull(p.collision, p.id);
                Assert.AreEqual(IslandCollisionRules.ShapeFor(p), p.collision.shape, p.id + ": run RefreshWorldSafety");
                if (p.collision.shape == IslandCollisionSpec.Shape.Box || p.collision.shape == IslandCollisionSpec.Shape.Compound)
                    Assert.IsNotEmpty(p.collision.boxes, p.id);
                if (p.collision.shape == IslandCollisionSpec.Shape.Compound)
                {
                    Assert.IsNotNull(p.collision.footprint, p.id + ": run RefreshWorldSafety");
                    Assert.Less(p.collision.footprint.triangles.Length / 3, 2000, p.id + ": collision is a simplified footprint, not render LOD0");
                }
            }
            var root = Resources.Load<GameObject>(IslandWorld.ResourcePath);
            var world = root.GetComponent<IslandWorld>();
            Assert.IsNotNull(world.waterBodies, "run RefreshWorldSafety");
            Assert.AreEqual(IslandWaterBodies.Capture(root.transform.Find("Water")).Length, world.waterBodies.triangles.Length);
        }

        [Test]
        public void LodgeDecorCars_BlockTheBody_WithoutDuplicatingSwappableParkedCollision()
        {
            var authored = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.CellsDir + "/cell_3_5.prefab");
            var source = authored.GetComponentInChildren<IslandInstances>(true);
            var cell = new GameObject("lodge-decor-collision-test");
            try
            {
                var dress = new GameObject("Dress"); dress.transform.SetParent(cell.transform, false);
                var set = dress.AddComponent<IslandInstances>(); set.kit = source.kit; set.data = source.data;
                foreach (var original in authored.GetComponentsInChildren<ParkedCar>(true))
                {
                    var copy = Object.Instantiate(original.gameObject, cell.transform).GetComponent<ParkedCar>();
                    copy.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                    copy.set = set;
                }
                set.Decode();
                var boxes = dress.GetComponentsInChildren<BoxCollider>();
                Assert.AreEqual(2, boxes.Length, "only the two unmarked lodge dressing cars need runtime boxes");
                foreach (var box in boxes)
                {
                    Assert.AreEqual(IslandRenderSettings.Prop, box.gameObject.layer);
                    Assert.IsNull(box.attachedRigidbody, "decor remains static, without drivable-car physics");
                    var pos = box.transform.position;
                    Assert.That(pos.x, Is.InRange(-239.83f, -239.68f));
                    Assert.That(pos.z, Is.InRange(401.88f, 404.92f));
                    var front = box.transform.forward;
                    var start = pos + front * (box.size.z * .5f + 1f);
                    Physics.SyncTransforms();
                    Assert.IsTrue(Physics.CapsuleCast(start + Vector3.up * .3f, start + Vector3.up * 1.4f, .25f,
                        -front, out var hit, 4f, CharacterLayers.BodyMask, QueryTriggerInteraction.Ignore), box.name);
                    Assert.AreSame(box, hit.collider, "a player-height capsule must hit the car's visible body");
                }
                set.Decode();
                Assert.AreEqual(2, dress.GetComponentsInChildren<BoxCollider>().Length, "decoding again must reuse collision");
                var marker = cell.GetComponentsInChildren<ParkedCar>(true).First();
                var owned = marker.GetComponent<BoxCollider>();
                var center = owned.transform.TransformPoint(owned.center);
                marker.Hide(true); Physics.SyncTransforms();
                Assert.IsFalse(owned.enabled);
                Assert.IsEmpty(Physics.OverlapBox(center, Vector3.one * .1f, Quaternion.identity,
                    CharacterLayers.BodyMask, QueryTriggerInteraction.Ignore), "the swap must leave no duplicate decorative collider");
                marker.Hide(false);
                Assert.IsTrue(owned.enabled);
            }
            finally { Object.DestroyImmediate(cell); }
        }

        [Test]
        public void FastStreaming_IsBounded_AndPrefetchesDistanceRatherThanFixedShortTime()
        {
            Assert.AreEqual(1, IslandStreaming.LoadConcurrency(6));
            Assert.AreEqual(3, IslandStreaming.LoadConcurrency(40));
            Assert.AreEqual(10f, IslandStreaming.PrefetchHorizon(16));
            Assert.AreEqual(6.25f, IslandStreaming.PrefetchHorizon(40));
            Assert.AreEqual(4f, IslandStreaming.PrefetchHorizon(100));
        }
    }
}
