using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>Street plaza paving surrounds a venue's own floor instead of painting over it.</summary>
    public static class IslandV2Plazas
    {
        public const float ApproachWidth = 1.5f;

        public sealed class Geometry
        {
            public string Id;
            public Vector2[] Boundary;
            public Vector2 Center;
            public float Height;
            public IslandVenue Venue;
            public readonly MeshBuilder Visual = new MeshBuilder(), Collision = new MeshBuilder();
            public Vector3[] OriginalQuad => Boundary.Select(p => new Vector3(p.x, Height, p.y)).ToArray();
        }

        public static Geometry Describe(JToken plaza, IslandHeightfield heightfield, IReadOnlyList<IslandVenue> venues)
        {
            var polygon = ((JArray)plaza["polygon"]).Select(p => new Vector2((float)p[0], (float)p[1])).ToArray();
            var boundary = IslandV2Builder.Inset(polygon, 9.2f);
            var center = boundary.Aggregate(Vector2.zero, (a, b) => a + b) / boundary.Length;
            float y = boundary.Concat(new[] { center }).Max(p => heightfield.Sample(p.x, p.y)) + 0.05f;
            var contained = venues.Where(v => Contains(boundary, v.origin)).ToArray();
            if (contained.Length > 1) throw new InvalidOperationException("Plaza has multiple venue frames: " + plaza["id"]);
            var result = Create(boundary, y, contained.SingleOrDefault());
            result.Id = (string)plaza["id"]; result.Center = center;
            return result;
        }

        public static Geometry Create(Vector2[] boundary, float height, IslandVenue venue)
        {
            var g = new Geometry { Boundary = boundary, Height = height, Venue = venue };
            if (venue == null) { Polygon(g.Visual, boundary, height); g.Collision.Append(g.Visual); return g; }
            var frame = Corners(venue, 0f);
            var approach = Corners(venue, ApproachWidth);
            if (approach.Any(p => !Contains(boundary, p)))
                throw new InvalidOperationException("Venue approach does not fit its plaza: " + venue.venueId);

            // Successively emit the outside of each half-plane, retaining the intersection for the next edge.
            // Each emitted piece is convex and disjoint; the final retained polygon is precisely the hole.
            var remaining = new List<Vector2>(boundary);
            for (int i = 0; i < approach.Length; i++)
            {
                var a = approach[i]; var b = approach[(i + 1) % approach.Length];
                Polygon(g.Visual, Clip(remaining, a, b, false), height);
                remaining = Clip(remaining, a, b, true);
            }
            for (int i = 0; i < frame.Length; i++)
            {
                int j = (i + 1) % frame.Length;
                Triangle(g.Visual, At(approach[i], height), At(approach[j], height), At(frame[j], venue.elevation));
                Triangle(g.Visual, At(approach[i], height), At(frame[j], venue.elevation), At(frame[i], venue.elevation));
            }
            g.Collision.Append(g.Visual);
            // The venue has a contract walk rectangle smaller than its visible paving. Support that existing
            // paving at its authored elevation too; the raised fountain/deck colliders remain above this floor.
            Polygon(g.Collision, frame, venue.elevation);
            return g;
        }

        public static void Build(JObject plan, IslandHeightfield h, IReadOnlyList<IslandVenue> venues, CellSet cells, JObject receipt)
        {
            var entries = new JArray();
            if (plan["plazas"] is JArray plazas)
                foreach (var p in plazas)
                {
                    var g = Describe(p, h, venues);
                    var cell = cells.At(At(g.Center, 0f));
                    cell.V(Mat.Plaza).Append(g.Visual); cell.Col(Layers.Road).Append(g.Collision);
                    entries.Add(new JObject { ["id"] = g.Id, ["venue"] = g.Venue?.venueId, ["height"] = g.Height,
                        ["visualTriangles"] = g.Visual.T.Count / 3, ["collisionTriangles"] = g.Collision.T.Count / 3 });
                }
            receipt["plazas"] = entries;
        }

        private static Vector2[] Corners(IslandVenue v, float margin)
        {
            var r = v.localFrame;
            return new[] { new Vector2(r.xMin - margin, r.yMin - margin), new Vector2(r.xMax + margin, r.yMin - margin),
                new Vector2(r.xMax + margin, r.yMax + margin), new Vector2(r.xMin - margin, r.yMax + margin) }.Select(v.ToIsland).ToArray();
        }

        private static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        private static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 p)
        {
            for (int i = 0; i < polygon.Count; i++) if (Side(polygon[i], polygon[(i + 1) % polygon.Count], p) < -0.001f) return false;
            return true;
        }

        private static List<Vector2> Clip(IReadOnlyList<Vector2> polygon, Vector2 a, Vector2 b, bool inside)
        {
            var result = new List<Vector2>();
            if (polygon.Count == 0) return result;
            var previous = polygon[polygon.Count - 1]; float previousSide = Side(a, b, previous);
            foreach (var p in polygon)
            {
                float side = Side(a, b, p);
                bool wasKept = inside ? previousSide >= 0f : previousSide <= 0f;
                bool keep = inside ? side >= 0f : side <= 0f;
                if (keep != wasKept) result.Add(Vector2.LerpUnclamped(previous, p, previousSide / (previousSide - side)));
                if (keep) result.Add(p);
                previous = p; previousSide = side;
            }
            return result;
        }

        private static Vector3 At(Vector2 p, float y) => new Vector3(p.x, y, p.y);
        private static void Polygon(MeshBuilder mesh, IReadOnlyList<Vector2> points, float y)
        {
            for (int i = 1; i + 1 < points.Count; i++) Triangle(mesh, At(points[0], y), At(points[i], y), At(points[i + 1], y));
        }

        private static void Triangle(MeshBuilder mesh, Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-10f) return;
            if (normal.y < 0f) { (b, c) = (c, b); normal = -normal; }
            normal.Normalize();
            int x = mesh.Add(a, normal, new Vector2(a.x, a.z));
            int y = mesh.Add(b, normal, new Vector2(b.x, b.z));
            int z = mesh.Add(c, normal, new Vector2(c.x, c.z));
            mesh.Tri(x, y, z);
        }

        /// <summary>Remove only triangles whose three vertices belong to the exact old quad. No streams or other indices change.</summary>
        public static int[] WithoutOriginalQuad(Mesh source, Vector3[] quad, out int removed)
        {
            if (source.subMeshCount != 1) throw new InvalidOperationException("Expected one plaza material submesh");
            var vertices = source.vertices; var triangles = source.triangles; var kept = new List<int>(); removed = 0;
            bool Corner(int index) => quad.Any(p => (vertices[index] - p).sqrMagnitude < 0.000004f);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (Corner(triangles[i]) && Corner(triangles[i + 1]) && Corner(triangles[i + 2])) { removed++; continue; }
                kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]);
            }
            return kept.ToArray();
        }

        /// <summary>Incremental asset repair, never a full island rebuild. Repeating verifies the existing patch.</summary>
        [MenuItem("CT Life/World/Repair Timeline plaza ground")]
        public static void RepairTimelinePlaza()
        {
            var receipt = new JObject { ["utc"] = DateTime.UtcNow.ToString("O") };
            string fatal = null;
            try { Repair(receipt); }
            catch (Exception e) { fatal = e.ToString(); Debug.LogException(e); }
            receipt["fatal"] = fatal;
            string path = Path.GetFullPath(Path.Combine(IslandV2Paths.Repo, "..", "ct-life-evidence", "audit-20261019", "world-plaza-receipt.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, receipt.ToString());
            Debug.Log("CTLIFE [plaza] " + receipt.ToString(Newtonsoft.Json.Formatting.None));
            if (Application.isBatchMode) EditorApplication.Exit(fatal == null ? 0 : 1);
        }

        private static void Repair(JObject receipt)
        {
            var plan = IslandV2Paths.Json("plan.json"); var h = IslandV2Height.Load(IslandV2Paths.Out);
            var g = Describe(((JArray)plan["plazas"]).Single(p => (string)p["id"] == "timeline_plaza"), h, IslandV2Builder.Venues(plan, out _));
            string cellId = IslandV2Paths.CellId(IslandV2Paths.CellOf(g.Center.x, g.Center.y, h));
            string prefabPath = IslandV2Paths.CellsDir + "/" + cellId + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var filter = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == Mat.Plaza);
                if (filter.transform.localToWorldMatrix != Matrix4x4.identity) throw new InvalidOperationException("Plaza vertices no longer use world coordinates");
                var kept = WithoutOriginalQuad(filter.sharedMesh, g.OriginalQuad, out int removed);
                string childName = "timeline_plaza_exterior";
                var prior = root.GetComponentsInChildren<MeshFilter>(true).SingleOrDefault(f => f.name == childName);
                receipt["cell"] = cellId; receipt["oldPlazaHeight"] = g.Height; receipt["venueFloor"] = g.Venue.elevation;
                receipt["approachWidth"] = ApproachWidth; receipt["removedTriangles"] = removed;
                receipt["preservedTriangles"] = kept.Length / 3;
                receipt["visualTriangles"] = g.Visual.T.Count / 3; receipt["collisionTriangles"] = g.Collision.T.Count / 3;
                if (removed == 0 && prior != null)
                {
                    var collider = prior.GetComponent<MeshCollider>();
                    Verify(prior.sharedMesh, g.Visual); Verify(collider != null ? collider.sharedMesh : null, g.Collision);
                    if (prior.gameObject.layer != Layers.Road || !collider.enabled || collider.isTrigger || collider.convex)
                        throw new InvalidOperationException("Existing plaza support settings changed");
                    receipt["alreadyRepaired"] = true; return;
                }
                if (removed != 2 || prior != null) throw new InvalidOperationException("Expected exactly the two original plaza triangles and no partial prior repair");
                string visualPath = IslandV2Paths.Meshes + "/" + cellId + "_timeline_plaza_exterior.asset";
                string collisionPath = IslandV2Paths.Meshes + "/" + cellId + "_timeline_plaza_support.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(visualPath) != null || AssetDatabase.LoadAssetAtPath<Mesh>(collisionPath) != null)
                    throw new InvalidOperationException("Unexpected pre-existing plaza repair assets");
                var visual = g.Visual.ToMesh(childName); var support = g.Collision.ToMesh("timeline_plaza_support");
                AssetDatabase.CreateAsset(visual, visualPath); AssetDatabase.CreateAsset(support, collisionPath);
                var child = new GameObject(childName) { layer = Layers.Road };
                child.transform.SetParent(filter.transform.parent, false);
                child.AddComponent<MeshFilter>().sharedMesh = visual;
                var renderer = child.AddComponent<MeshRenderer>(); var oldRenderer = filter.GetComponent<MeshRenderer>();
                EditorUtility.CopySerialized(oldRenderer, renderer);
                child.AddComponent<MeshCollider>().sharedMesh = support;
                filter.sharedMesh.SetTriangles(kept, 0, false); // preserve every other material triangle and all vertex streams
                EditorUtility.SetDirty(filter.sharedMesh);
                AssetDatabase.SaveAssets(); PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                receipt["alreadyRepaired"] = false;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void Verify(Mesh actual, MeshBuilder expected)
        {
            if (actual == null || !actual.vertices.SequenceEqual(expected.V) || !actual.triangles.SequenceEqual(expected.T))
                throw new InvalidOperationException("Existing plaza patch does not match current authored geometry");
        }
    }
}
