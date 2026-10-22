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
    /// <summary>Refresh collision metadata and existing cell prefabs without regenerating layout, art, navmesh or lighting.</summary>
    public static class IslandV2Collision
    {
        public const string WaterAsset = IslandV2Paths.Data + "/WaterBodies.asset";
        private const string GroupName = "AuditColliders";

        [MenuItem("Tools/CT Life/Audit/Refresh World Safety")]
        public static void RefreshWorldSafety()
        {
            var kit = AssetDatabase.LoadAssetAtPath<IslandKit>(IslandV2Kit.KitAsset);
            if (kit == null) throw new InvalidOperationException("Island kit is missing");
            BakeKit(kit);
            var receipt = new JObject { ["kind"] = "world-safety", ["waterSource"] = "existing rendered inland triangles", ["cells"] = new JArray() };
            int total = 0;
            foreach (var path in Directory.GetFiles(IslandV2Paths.CellsDir, "*.prefab").OrderBy(p => p))
            {
                var cell = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int count = ReplaceCellColliders(cell);
                    total += count;
                    var audit = AuditCell(cell);
                    audit["id"] = cell.name; audit["newColliders"] = count;
                    audit["totalColliders"] = cell.GetComponentsInChildren<Collider>(true).Length;
                    ((JArray)receipt["cells"]).Add(audit);
                    PrefabUtility.SaveAsPrefabAsset(cell, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(cell); }
            }
            var root = PrefabUtility.LoadPrefabContents(IslandV2Paths.BasePrefab);
            try
            {
                var bodies = AssetDatabase.LoadAssetAtPath<IslandWaterBodies>(WaterAsset);
                if (bodies == null) { bodies = ScriptableObject.CreateInstance<IslandWaterBodies>(); AssetDatabase.CreateAsset(bodies, WaterAsset); }
                bodies.triangles = IslandWaterBodies.Capture(root.transform.Find("Water"));
                if (bodies.triangles.Length == 0) throw new InvalidOperationException("No inland water triangles captured");
                bodies.Invalidate(); EditorUtility.SetDirty(bodies);
                root.GetComponent<IslandWorld>().waterBodies = bodies;
                PrefabUtility.SaveAsPrefabAsset(root, IslandV2Paths.BasePrefab);
                receipt["waterTriangles"] = bodies.triangles.Length;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            receipt["newColliders"] = total;
            receipt["auditScope"] = "Static Building/Prop pairs in each cell, depth >0.15m; same-object compounds excluded. Roads/support contacts excluded. Cross-cell seams and visual art still require device review.";
            receipt["penetratingPairs"] = ((JArray)receipt["cells"]).Sum(c => (int)c["penetratingPairs"]);
            receipt["penetrationsRequireReview"] = (int)receipt["penetratingPairs"] > 0;
            receipt["buildingPrototypeBoxes"] = new JArray(kit.protos.Where(p => p.kind == "building")
                .Select(p => new JObject { ["id"] = p.id, ["boxes"] = p.collision.boxes.Length }));
            AssetDatabase.SaveAssets();
            string output = Path.Combine(IslandV2Paths.Repo, "..", "ct-life-evidence", "audit-20261019", "world-safety-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, receipt.ToString());
            Debug.Log($"CTLIFE [world-safety] refreshed {((JArray)receipt["cells"]).Count} cells; {total} metadata colliders; {receipt["waterTriangles"]} water triangles. {output}");
            if ((int)receipt["penetratingPairs"] > 0)
                Debug.LogWarning($"CTLIFE [world-safety] {receipt["penetratingPairs"]} static overlaps >0.15m need classification; details in receipt (not a no-interpenetration pass).");
        }

        public static void BakeKit(IslandKit kit)
        {
            foreach (var p in kit.protos)
            {
                p.collision = IslandCollisionRules.Simple(p);
                if (p.collision.shape != IslandCollisionSpec.Shape.Compound) continue;
                var mesh = p.lods.Length > 0 ? p.lods[0] : null;
                if (mesh == null) throw new InvalidOperationException("Building has no LOD0: " + p.id);
                p.collision.boxes = FootprintBoxes(mesh.vertices, mesh.triangles, p.bounds);
                p.collision.source = "LOD0 lower-wall slices; nonzero winding union; 0.25m footprint cells";
                if (p.collision.boxes.Length == 0) throw new InvalidOperationException("No closed building footprint: " + p.id);
                string dir = IslandV2Kit.KitDir + "/Collision"; Directory.CreateDirectory(dir);
                string path = dir + "/" + p.id + ".asset";
                var footprint = FootprintMesh(p.collision.boxes); footprint.name = p.id + "_collision";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved == null) { saved = footprint; AssetDatabase.CreateAsset(saved, path); }
                else { EditorUtility.CopySerialized(footprint, saved); Object.DestroyImmediate(footprint); EditorUtility.SetDirty(saved); }
                p.collision.footprint = saved;
            }
            EditorUtility.SetDirty(kit); AssetDatabase.SaveAssetIfDirty(kit);
            AssetDatabase.SaveAssets();
        }

        /// <summary>One static mesh for the occupied footprint. Caps use packed rectangles; interior box faces are removed.</summary>
        public static Mesh FootprintMesh(Bounds[] boxes)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            foreach (var box in boxes)
            {
                var lo = box.min; var hi = box.max;
                Quad(new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, hi.y, lo.z));
                Quad(new Vector3(lo.x, lo.y, lo.z), new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, lo.y, hi.z), new Vector3(lo.x, lo.y, hi.z));
                for (int axis = 0; axis < 2; axis++) for (int side = 0; side < 2; side++)
                {
                    bool xAxis = axis == 0; float edge = xAxis ? (side == 0 ? lo.x : hi.x) : (side == 0 ? lo.z : hi.z);
                    var spans = new List<(float lo, float hi)> { (xAxis ? lo.z : lo.x, xAxis ? hi.z : hi.x) };
                    foreach (var other in boxes)
                    {
                        float touch = xAxis ? (side == 0 ? other.max.x : other.min.x) : (side == 0 ? other.max.z : other.min.z);
                        if (Mathf.Abs(touch - edge) > 0.001f) continue;
                        float cutLo = xAxis ? other.min.z : other.min.x, cutHi = xAxis ? other.max.z : other.max.x;
                        for (int k = spans.Count - 1; k >= 0; k--)
                        {
                            var s = spans[k]; if (cutHi <= s.lo + 0.001f || cutLo >= s.hi - 0.001f) continue;
                            spans.RemoveAt(k);
                            if (cutLo > s.lo + 0.001f) spans.Add((s.lo, cutLo));
                            if (cutHi < s.hi - 0.001f) spans.Add((cutHi, s.hi));
                        }
                    }
                    foreach (var s in spans)
                    {
                        var a = xAxis ? new Vector3(edge, lo.y, s.lo) : new Vector3(s.lo, lo.y, edge);
                        var b = xAxis ? new Vector3(edge, hi.y, s.lo) : new Vector3(s.lo, hi.y, edge);
                        var c = xAxis ? new Vector3(edge, hi.y, s.hi) : new Vector3(s.hi, hi.y, edge);
                        var d = xAxis ? new Vector3(edge, lo.y, s.hi) : new Vector3(s.hi, lo.y, edge);
                        if ((xAxis && side == 0) || (!xAxis && side == 1)) Quad(a, d, c, b); else Quad(a, b, c, d);
                    }
                }
            }
            var mesh = new Mesh { name = "building_footprint" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>Extrudes occupied lower-wall footprints. Recesses/courtyards stay empty instead of using a lot-wide box.</summary>
        public static Bounds[] FootprintBoxes(Vector3[] vertices, int[] triangles, Bounds bounds)
        {
            float pitch = Mathf.Max(0.25f, Mathf.Max(bounds.size.x, bounds.size.z) / 128f);
            int nx = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / pitch)), nz = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / pitch));
            float dx = bounds.size.x / nx, dz = bounds.size.z / nz;
            var filled = new bool[nx, nz];
            foreach (float above in new[] { 0.7f, 1.7f })
            {
                float plane = bounds.min.y + Mathf.Min(above, bounds.size.y * 0.4f);
                var edges = Slice(vertices, triangles, plane);
                for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
                {
                    var q = new Vector2(bounds.min.x + (x + 0.5f) * dx, bounds.min.z + (z + 0.5f) * dz);
                    int winding = 0;
                    foreach (var e in edges)
                        if ((e.a.y > q.y) != (e.b.y > q.y) && q.x < (e.b.x - e.a.x) * (q.y - e.a.y) / (e.b.y - e.a.y) + e.a.x)
                            winding += e.b.y > e.a.y ? 1 : -1;
                    // Separate facade/window volumes overlap. Parity incorrectly punches holes in their union.
                    filled[x, z] |= winding != 0;
                }
            }
            var result = new List<Bounds>();
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                if (!filled[x, z]) continue;
                int width = 1; while (x + width < nx && filled[x + width, z]) width++;
                int depth = 1;
                while (z + depth < nz)
                {
                    bool row = true;
                    for (int k = 0; k < width; k++) if (!filled[x + k, z + depth]) { row = false; break; }
                    if (!row) break;
                    depth++;
                }
                for (int zz = z; zz < z + depth; zz++) for (int xx = x; xx < x + width; xx++) filled[xx, zz] = false;
                result.Add(new Bounds(new Vector3(bounds.min.x + (x + width * 0.5f) * dx, bounds.center.y,
                    bounds.min.z + (z + depth * 0.5f) * dz), new Vector3(width * dx, bounds.size.y, depth * dz)));
            }
            return result.ToArray();
        }

        private static List<(Vector2 a, Vector2 b)> Slice(Vector3[] v, int[] tris, float y)
        {
            var segments = new List<(Vector2, Vector2)>();
            var seen = new HashSet<string>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                var points = new List<Vector2>(3);
                for (int k = 0; k < 3; k++)
                {
                    var a = v[tris[i + k]]; var b = v[tris[i + (k + 1) % 3]];
                    if ((a.y > y) == (b.y > y)) continue;
                    var p = Vector3.Lerp(a, b, (y - a.y) / (b.y - a.y));
                    points.Add(new Vector2(p.x, p.z));
                }
                if (points.Count != 2 || (points[0] - points[1]).sqrMagnitude < 1e-10f) continue;
                var normal = Vector3.Cross(v[tris[i + 1]] - v[tris[i]], v[tris[i + 2]] - v[tris[i]]);
                var direction = Vector3.Cross(normal, Vector3.up);
                if (Vector2.Dot(points[1] - points[0], new Vector2(direction.x, direction.z)) < 0)
                { var tmp = points[0]; points[0] = points[1]; points[1] = tmp; }
                string Key(Vector2 p) => Mathf.RoundToInt(p.x * 10000) + "," + Mathf.RoundToInt(p.y * 10000);
                string ka = Key(points[0]), kb = Key(points[1]);
                string key = ka + ":" + kb;
                if (seen.Add(key)) segments.Add((points[0], points[1]));
            }
            return segments;
        }

        public static int ReplaceCellColliders(GameObject cell)
        {
            var set = cell.GetComponentInChildren<IslandInstances>(true);
            if (set == null || set.kit == null || set.data == null) return 0;
            IslandV2PlacementRepair.ApplySavedOverrides(cell, set);
            // Keep existing trunks, posts, road, venue-shell and parked-car collision.
            var old = cell.transform.Find(GroupName); if (old != null) Object.DestroyImmediate(old.gameObject);
            var group = new GameObject(GroupName); group.transform.SetParent(cell.transform, false);
            int count = 0, buildings = 0;
            using (var stream = new MemoryStream(set.data.bytes)) using (var r = new BinaryReader(stream))
            {
                if (r.ReadInt32() != IslandInstances.Magic) throw new InvalidDataException("Bad island instance data: " + cell.name);
                int groups = r.ReadInt32();
                for (int g = 0; g < groups; g++)
                {
                    int proto = r.ReadInt32(), n = r.ReadInt32();
                    var p = set.kit.protos[proto]; var spec = p.collision;
                    if (spec == null || spec.shape == IslandCollisionSpec.Shape.Unbaked) throw new InvalidDataException("Unbaked collider: " + p.id);
                    for (int i = 0; i < n; i++)
                    {
                        var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        float yaw = r.ReadSingle(), sxz = r.ReadSingle(), sy = r.ReadSingle(); r.ReadUInt32();
                        if (!IslandCollisionRules.SolidAtScale(p, sxz, sy)) continue;
                        if (p.kind == "building") buildings++;
                        var go = new GameObject(p.id) { layer = p.kind == "building" ? Layers.Building : Layers.Prop };
                        go.transform.SetParent(group.transform, false);
                        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
                        go.transform.localScale = new Vector3(sxz, sy, sxz);
                        if (spec.shape == IslandCollisionSpec.Shape.Compound && spec.footprint != null)
                        {
                            go.AddComponent<MeshCollider>().sharedMesh = spec.footprint; count++;
                        }
                        else if (spec.shape == IslandCollisionSpec.Shape.Capsule)
                        {
                            var c = go.AddComponent<CapsuleCollider>(); c.center = spec.center; c.radius = spec.radius;
                            c.height = Mathf.Max(spec.height, spec.radius * 2); count++;
                        }
                        else foreach (var box in spec.boxes)
                        {
                            var c = go.AddComponent<BoxCollider>(); c.center = box.center; c.size = box.size; count++;
                        }
                    }
                }
            }
            if (buildings > 0)
            {
                var col = cell.transform.Find("Colliders");
                if (col != null) foreach (var b in col.GetComponentsInChildren<BoxCollider>(true))
                    if (b.gameObject.layer == Layers.Building && b.name == "building") Object.DestroyImmediate(b.gameObject);
            }
            return count;
        }

        public static JObject AuditCell(GameObject cell)
        {
            var proxies = AddAuditProxies(cell);
            try { return AuditCellWithProxies(cell); }
            finally { foreach (var proxy in proxies) Object.DestroyImmediate(proxy); }
        }

        // Non-convex mesh/mesh ComputePenetration is unsupported. Use the identical packed footprint solely for editor checks.
        public static List<BoxCollider> AddAuditProxies(GameObject cell)
        {
            var added = new List<BoxCollider>(); var set = cell.GetComponentInChildren<IslandInstances>(true);
            var group = cell.transform.Find(GroupName); if (set == null || group == null) return added;
            foreach (Transform t in group)
            {
                if (t.GetComponent<MeshCollider>() == null) continue;
                var p = set.kit.protos[set.kit.IndexOf(t.name)];
                foreach (var box in p.collision.boxes)
                { var c = t.gameObject.AddComponent<BoxCollider>(); c.center = box.center; c.size = box.size; added.Add(c); }
            }
            return added;
        }

        public static bool IsAuditShape(Collider c) => !(c is MeshCollider && c.transform.parent != null && c.transform.parent.name == GroupName);

        private static JObject AuditCellWithProxies(GameObject cell)
        {
            Physics.SyncTransforms();
            var cols = cell.GetComponentsInChildren<Collider>(true).Where(c => IsAuditShape(c) && c.enabled && !c.isTrigger &&
                (c.gameObject.layer == Layers.Building || c.gameObject.layer == Layers.Prop)).ToArray();
            var buckets = new Dictionary<Vector2Int, List<int>>();
            var pairs = new HashSet<long>(); var hits = new List<(int a, int b, float depth)>();
            int checkedPairs = 0;
            for (int i = 0; i < cols.Length; i++)
            {
                var bounds = cols[i].bounds;
                int x0 = Mathf.FloorToInt(bounds.min.x / 16), x1 = Mathf.FloorToInt(bounds.max.x / 16);
                int z0 = Mathf.FloorToInt(bounds.min.z / 16), z1 = Mathf.FloorToInt(bounds.max.z / 16);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                    foreach (int j in list)
                    {
                        long pair = ((long)j << 32) | (uint)i;
                        if (!pairs.Add(pair) || cols[i].gameObject == cols[j].gameObject || !bounds.Intersects(cols[j].bounds)) continue;
                        checkedPairs++;
                        var a = cols[i]; var b = cols[j];
                        if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                            b, b.transform.position, b.transform.rotation, out _, out float depth) && depth > 0.15f)
                            hits.Add((i, j, depth));
                    }
                    list.Add(i);
                }
            }
            string PathOf(Transform t)
            {
                var names = new List<string>();
                while (t != null && t != cell.transform) { names.Add(t.name); t = t.parent; }
                names.Reverse(); return string.Join("/", names);
            }
            var details = new JArray();
            foreach (var h in hits.OrderByDescending(h => h.depth).Take(100))
            {
                var a = cols[h.a]; var b = cols[h.b]; var p = (a.bounds.center + b.bounds.center) * 0.5f;
                details.Add(new JObject { ["a"] = PathOf(a.transform), ["b"] = PathOf(b.transform), ["depth"] = h.depth,
                    ["position"] = new JArray(p.x, p.y, p.z) });
            }
            return new JObject { ["checkedPairs"] = checkedPairs, ["penetratingPairs"] = hits.Count,
                ["reportedPairs"] = details, ["truncatedPairs"] = Mathf.Max(0, hits.Count - details.Count) };
        }
    }
}
