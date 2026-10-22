using System.Collections.Generic;
using System.Linq;
using CTLife.EditorTools.IslandV2;
using CTLife.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    public sealed class IslandPlazaTests
    {
        private readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        private T Own<T>(T item) where T : Object { _owned.Add(item); return item; }

        [TestCase(0f)]
        [TestCase(354f)]
        [TestCase(37f)]
        public void RotatedVenue_HasOpenVisualFrameContinuousApproachAndMatchingPhysics(float yaw)
        {
            var venue = new IslandVenue { origin = new Vector2(3, -4), yaw = yaw, elevation = 0,
                localFrame = new Rect(-16.5f, -17.4f, 33f, 34.4f) };
            const float height = 0.26057452f;
            var boundary = new[] { new Vector2(-60, -60), new Vector2(60, -60), new Vector2(60, 60), new Vector2(-60, 60) };
            var geometry = IslandV2Plazas.Create(boundary, height, venue);
            var visual = Own(geometry.Visual.ToMesh("test-plaza"));
            var support = Own(geometry.Collision.ToMesh("test-support"));
            var ground = Own(new GameObject("plaza-support")); ground.layer = Layers.Road;
            ground.AddComponent<MeshCollider>().sharedMesh = support;
            Physics.SyncTransforms();

            float area = 0;
            var vertices = visual.vertices; var triangles = visual.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var cross = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
                Assert.Greater(cross.y, 0, "every top triangle faces the downward ground ray");
                area += cross.y * 0.5f;
            }
            Assert.That(area, Is.EqualTo(120f * 120f - venue.localFrame.width * venue.localFrame.height).Within(0.03f), "no overlap or missing exterior area");
            var center = venue.ToIsland(Vector2.zero);
            Assert.IsTrue(float.IsNaN(Surface(visual, Matrix4x4.identity, center, 1f)), "no island paving over the venue");
            Assert.That(Floor(center), Is.EqualTo(0f).Within(0.001f), "venue paving remains supported beyond its smaller contract walk rectangle");

            // Sample each side at the venue seam, halfway across the approach, and on the unchanged outside paving.
            var r = venue.localFrame;
            foreach (var pair in new[] { (new Vector2(r.xMin, 0), Vector2.left), (new Vector2(r.xMax, 0), Vector2.right),
                (new Vector2(0, r.yMin), Vector2.down), (new Vector2(0, r.yMax), Vector2.up) })
                foreach (float distance in new[] { 0.01f, 0.75f, 1.49f, 2f })
                {
                    var p = venue.ToIsland(pair.Item1 + pair.Item2 * distance);
                    float expected = height * Mathf.Clamp01(distance / IslandV2Plazas.ApproachWidth);
                    Assert.That(Surface(visual, Matrix4x4.identity, p, 1f), Is.EqualTo(expected).Within(0.001f));
                    Assert.That(Floor(p), Is.EqualTo(expected).Within(0.001f));
                }
        }

        [Test]
        public void TargetedRemoval_PreservesOtherTrianglesAndAllVertexStreams_AndIsRepeatable()
        {
            var quad = new[] { new Vector3(-5, .26f, -5), new Vector3(5, .26f, -5), new Vector3(5, .26f, 5), new Vector3(-5, .26f, 5) };
            var builder = new MeshBuilder(); builder.FlatQuad(quad[0], quad[3], quad[2], quad[1]);
            // A different material-batched quad shares an original corner, but must survive unchanged.
            builder.FlatQuad(quad[0], new Vector3(-8, .26f, -5), new Vector3(-8, .26f, -8), new Vector3(-5, .26f, -8));
            var mesh = Own(builder.ToMesh("combined-plazas"));
            var vertices = mesh.vertices; var normals = mesh.normals; var uv = mesh.uv; var colors = mesh.colors32;
            var remaining = IslandV2Plazas.WithoutOriginalQuad(mesh, quad, out int removed);
            Assert.AreEqual(2, removed); CollectionAssert.AreEqual(mesh.triangles.Skip(6), remaining);
            mesh.SetTriangles(remaining, 0, false);
            CollectionAssert.AreEqual(vertices, mesh.vertices); CollectionAssert.AreEqual(normals, mesh.normals);
            CollectionAssert.AreEqual(uv, mesh.uv); CollectionAssert.AreEqual(colors, mesh.colors32);
            CollectionAssert.AreEqual(remaining, IslandV2Plazas.WithoutOriginalQuad(mesh, quad, out removed));
            Assert.AreEqual(0, removed);
        }

        [Test]
        public void ShippedTimeline_CombinedStreamedPlazaAndVenue_MatchGroundAtLiveArrivalAndApproaches()
        {
            // The standalone venue passed while its separately streamed island overlay buried the actor by 26 cm.
            // This fixture deliberately loads both contributors, at their real authored transforms.
            var world = Resources.Load<GameObject>(IslandWorld.ResourcePath).GetComponent<IslandWorld>();
            var venue = world.Find("timeline_square");
            var target = Own(new GameObject("combined-timeline-physics")).transform;
            var copies = new Dictionary<Transform, Transform>();
            var visuals = new List<(Mesh mesh, Matrix4x4 matrix)>();
            foreach (var cell in world.cells)
            {
                if (IslandStreaming.DistanceToRect(venue.origin, cell.rect) > 80f) continue;
                var prefab = Resources.Load<GameObject>(IslandWorld.CellFolder + "/" + cell.id);
                CopyPhysics(prefab.transform, target, copies, false);
                AddVisuals(prefab.transform, Matrix4x4.identity, visuals);
            }
            var entry = Resources.Load<VenueContentEntry>("World/VenueContent/venue_timeline_square");
            var pose = Matrix4x4.TRS(venue.Position, venue.Rotation, Vector3.one);
            var venueTransform = CopyTransform(entry.prefab.transform, target, copies);
            venueTransform.SetPositionAndRotation(venue.Position, venue.Rotation);
            CopyPhysics(entry.prefab.transform, target, copies, true);
            AddVisuals(entry.prefab.transform, pose, visuals);
            Physics.SyncTransforms();

            var samples = new List<(string name, Vector2 p)> {
                ("actual Bootstrap arrival (grounding-play.json)", new Vector2(0.773511946f, -7.359462f)) };
            foreach (var p in new[] { new Vector2(-4f, -9f), new Vector2(0, -10f), new Vector2(4f, -9f), new Vector2(0, -14.5f) })
                samples.Add(("venue entry paving " + p, venue.ToIsland(p)));
            foreach (var p in new[] { new Vector2(0, venue.localFrame.yMin - 0.75f), new Vector2(0, venue.localFrame.yMin - 2f),
                new Vector2(venue.localFrame.xMin - 0.75f, 0), new Vector2(venue.localFrame.xMax + 0.75f, 0) })
                samples.Add(("exterior approach " + p, venue.ToIsland(p)));
            foreach (var sample in samples)
            {
                float rendered = float.NaN;
                foreach (var visual in visuals)
                {
                    float y = Surface(visual.mesh, visual.matrix, sample.p, venue.elevation + 0.5f);
                    if (!float.IsNaN(y) && (float.IsNaN(rendered) || y > rendered)) rendered = y;
                }
                Assert.IsFalse(float.IsNaN(rendered), sample.name + " requires a visible floor");
                float collision = Floor(sample.p);
                TestContext.WriteLine($"Timeline {sample.name} world={sample.p:F6} render={rendered:F6} collision={collision:F6}");
                Assert.That(rendered - collision, Is.InRange(-0.01f, 0.01f), sample.name + ": combined rendered surface must meet physical feet");
                if (sample.name.StartsWith("actual")) Assert.That(collision, Is.EqualTo(venue.elevation).Within(.002f), "keep original venue/furniture floor elevation");
            }
            float previous = float.NaN;
            for (float z = venue.localFrame.yMin - 2f; z <= venue.localWalk.yMin + 1f; z += 0.1f)
            {
                float floor = Floor(venue.ToIsland(new Vector2(0f, z)));
                if (!float.IsNaN(previous)) Assert.Less(Mathf.Abs(floor - previous), .025f, "continuous supported entry, no 26cm lip");
                previous = floor;
            }
        }

        private static float Floor(Vector2 p)
        {
            Assert.IsTrue(Physics.Raycast(new Vector3(p.x, 0.5f, p.y), Vector3.down, out var hit, 2f,
                (1 << Layers.Road) | (1 << Layers.Interior), QueryTriggerInteraction.Ignore), "missing support " + p);
            return hit.point.y;
        }

        private static bool Backdrop(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name == "Backdrop") return true;
            return false;
        }

        private static void AddVisuals(Transform root, Matrix4x4 pose, List<(Mesh, Matrix4x4)> list)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !filter.gameObject.activeSelf || Backdrop(filter.transform)) continue;
                list.Add((filter.sharedMesh, pose * root.worldToLocalMatrix * filter.transform.localToWorldMatrix));
            }
        }

        private static float Surface(Mesh mesh, Matrix4x4 matrix, Vector2 p, float ceiling)
        {
            // Independent world-space triangle interpolation, not the clipping helper under test.
            var bounds = mesh.bounds; var local = matrix.inverse.MultiplyPoint3x4(new Vector3(p.x, 0, p.y));
            if (local.x < bounds.min.x - .01f || local.x > bounds.max.x + .01f || local.z < bounds.min.z - .01f || local.z > bounds.max.z + .01f) return float.NaN;
            Assert.IsTrue(mesh.isReadable, "Relevant floor mesh must be readable: " + mesh.name);
            var vertices = mesh.vertices; var triangles = mesh.triangles; float best = float.NaN;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = matrix.MultiplyPoint3x4(vertices[triangles[i]]); var b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]); var c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < 1e-7f) continue;
                float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.y - c.z)) / denominator;
                float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.y - c.z)) / denominator;
                if (u < -1e-5f || v < -1e-5f || u + v > 1.00001f) continue;
                float y = u * a.y + v * b.y + (1 - u - v) * c.y;
                if (y <= ceiling && (float.IsNaN(best) || y > best)) best = y;
            }
            return best;
        }

        private static Transform CopyTransform(Transform source, Transform target, Dictionary<Transform, Transform> copies)
        {
            if (copies.TryGetValue(source, out var found)) return found;
            var go = new GameObject(source.name);
            go.transform.SetParent(source.parent != null ? CopyTransform(source.parent, target, copies) : target, false);
            go.transform.localPosition = source.localPosition; go.transform.localRotation = source.localRotation; go.transform.localScale = source.localScale;
            go.SetActive(source.gameObject.activeSelf); copies[source] = go.transform; return go.transform;
        }

        private static void CopyPhysics(Transform source, Transform target, Dictionary<Transform, Transform> copies, bool interior)
        {
            foreach (var original in source.GetComponentsInChildren<Collider>(true))
            {
                if (!original.enabled || original.isTrigger || !original.gameObject.activeSelf || Backdrop(original.transform)) continue;
                var go = CopyTransform(original.transform, target, copies).gameObject;
                go.layer = interior && original.gameObject.layer == 0 ? Layers.Interior : original.gameObject.layer;
                EditorUtility.CopySerialized(original, go.AddComponent(original.GetType()));
            }
        }
    }
}
