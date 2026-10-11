using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Prop collision for every venue. A prop is a floor-standing object (sourced model or registered procedural piece)
    /// whose footprint touches the walkable area. Each prop gets a BoxCollider fitted to its oriented bounds (tree:
    /// trunk only) and a not-walkable NavMesh volume so NPC agents path around it. Seat furniture is carved too (round 4,
    /// the player has a physics body and the interaction system walks to the approach point), but the carve is clipped
    /// so every spot anchor and approach point keeps <see cref="ClearRadius"/> of walkable NavMesh round it.
    /// <see cref="GeometryRegions"/> then scans the merged render meshes (body height, 0.2 m grid, like the character-feel
    /// lane's VenueFeelAudit) for anything still walkable or without a solid collider and the builders cover it.
    /// <see cref="Validate"/> fails the build when a prop has no collider, a prop is still on the NavMesh, or the
    /// geometry scan finds walk-through or uncarved geometry.
    /// </summary>
    public static class VenueCollision
    {
        public sealed class PropBox
        {
            public string name;
            public Vector3 center;      // venue-local, centre of the collision box
            public Vector3 size;
            public float yaw;
            public bool seat, tree;
        }

        private const float MinHeight = 0.15f, MaxBottom = 0.45f, MaxFootprint = 6f, ColliderTop = 2.2f;
        private static readonly string[] TreeWords = { "tree", "palm" };
        // Thin poles with overhead parts (canopy, lamp head, softbox): collide with the pole, not the head.
        private static readonly string[] PoleWords = { "parasol", "lamp", "light_stand", "mic_stand", "iv_stand", "studio_light", "light_floor" };

        /// <summary>Oriented bounds of a model's LOD0 renderers under <paramref name="m"/> (venue-local).</summary>
        public static PropBox FromRenderers(string name, IEnumerable<Renderer> renderers, Matrix4x4 rootToVenue, Matrix4x4 venueFromRoot)
        {
            var list = renderers.Where(r => r != null && VB.LodIndex(r.name) == 0).ToList();
            if (list.Count == 0) return null;
            bool any = false;
            var lb = new Bounds();
            foreach (var r in list)
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var toRoot = rootToVenue.inverse * r.localToWorldMatrix; // renderer -> prop root space
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toRoot.MultiplyPoint3x4(c);
                    if (!any) { lb = new Bounds(p, Vector3.zero); any = true; } else lb.Encapsulate(p);
                }
            }
            if (!any) return null;
            return FromLocalBounds(name, lb, venueFromRoot);
        }

        /// <summary>Box from bounds in a prop's own space and the prop-to-venue matrix (yaw + scale only).</summary>
        public static PropBox FromLocalBounds(string name, Bounds lb, Matrix4x4 m)
        {
            var scale = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
            float yaw = Mathf.Atan2(m.m02, m.m22) * Mathf.Rad2Deg;
            return new PropBox { name = name, center = m.MultiplyPoint3x4(lb.center), size = Vector3.Scale(lb.size, scale), yaw = yaw };
        }

        /// <summary>Filter raw boxes to floor props near the walkable area; mark seats and trees; clamp heights.</summary>
        public static List<PropBox> Classify(IEnumerable<PropBox> raw, VenueSpots spots, Rect walk)
        {
            var claimable = spots.spots.Values.Distinct().Where(s => s.hasPosition && s.Claimable).ToList();
            var outList = new List<PropBox>();
            var near = new Rect(walk.xMin - 0.3f, walk.yMin - 0.3f, walk.width + 0.6f, walk.height + 0.6f);
            foreach (var p in raw)
            {
                if (p == null) continue;
                float bottom = p.center.y - p.size.y * .5f, top = p.center.y + p.size.y * .5f;
                if (top - Mathf.Max(bottom, 0f) < MinHeight || bottom > MaxBottom) continue;
                if (Mathf.Max(p.size.x, p.size.z) > MaxFootprint) continue;
                string lower = p.name.ToLowerInvariant();
                p.tree = TreeWords.Any(lower.Contains) && p.size.y > 2.5f;
                bool pole = !p.tree && PoleWords.Any(lower.Contains) && p.size.y > 1.2f;
                if (p.tree || pole)
                {
                    // Trunk / pole only; the canopy or head is above head height.
                    float w = p.tree ? 0.5f : 0.35f, h = Mathf.Min(ColliderTop, top);
                    p.size = new Vector3(w, h, w);
                    p.center = new Vector3(p.center.x, h * .5f, p.center.z);
                    p.yaw = 0f;
                }
                else
                {
                    float h = Mathf.Min(top, ColliderTop) - Mathf.Max(bottom, 0f);
                    p.size = new Vector3(Mathf.Max(p.size.x, 0.12f), h, Mathf.Max(p.size.z, 0.12f));
                    p.center = new Vector3(p.center.x, Mathf.Max(bottom, 0f) + h * .5f, p.center.z);
                }
                if (!Intersects(p, near)) continue;
                p.seat = !p.tree && claimable.Any(s => Contains(p, new Vector3(s.x, 0, s.z), 0.35f));
                outList.Add(p);
            }
            return outList;
        }

        public static bool Contains(PropBox p, Vector3 point, float margin)
        {
            var local = Quaternion.Euler(0, -p.yaw, 0) * (new Vector3(point.x, 0, point.z) - new Vector3(p.center.x, 0, p.center.z));
            return Mathf.Abs(local.x) <= p.size.x * .5f + margin && Mathf.Abs(local.z) <= p.size.z * .5f + margin;
        }

        private static bool Intersects(PropBox p, Rect r)
        {
            // Conservative: the box's axis-aligned footprint against the rect.
            var q = Quaternion.Euler(0, p.yaw, 0);
            float ex = Mathf.Abs((q * new Vector3(p.size.x * .5f, 0, 0)).x) + Mathf.Abs((q * new Vector3(0, 0, p.size.z * .5f)).x);
            float ez = Mathf.Abs((q * new Vector3(p.size.x * .5f, 0, 0)).z) + Mathf.Abs((q * new Vector3(0, 0, p.size.z * .5f)).z);
            return p.center.x + ex >= r.xMin && p.center.x - ex <= r.xMax && p.center.z + ez >= r.yMin && p.center.z - ez <= r.yMax;
        }

        /// <summary>One BoxCollider per prop under <paramref name="parent"/>, skipping props existing colliders already cover.</summary>
        public static int AddColliders(Transform parent, GameObject root, List<PropBox> props)
        {
            int added = 0;
            foreach (var p in props)
            {
                if (Coverage(root, p) >= 0.6f) continue;
                var go = new GameObject("Prop collider " + p.name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = p.center;
                go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                go.AddComponent<BoxCollider>().size = p.size;
                added++;
            }
            return added;
        }

        /// <summary>Walkable clearance (agent radius 0.5 m + margin) kept round approach points and approach-less spots.</summary>
        public const float ClearRadius = 0.6f;

        /// <summary>
        /// Not-walkable volumes for the props. Seat furniture is carved too, clipped away from <paramref name="keepClear"/>
        /// (approach points and spots without one); a seat whose own anchor has no approach is not carved.
        /// </summary>
        public static IEnumerable<NavMeshBuildSource> Obstacles(List<PropBox> props, IReadOnlyList<Vector3> keepClear)
        {
            foreach (var p in props)
            {
                var box = p.seat ? ClipClear(p, keepClear) : p;
                if (box == null) continue;
                yield return new NavMeshBuildSource
                {
                    // Reaches 0.5 m below the floor so the walkable surface voxels are always inside the modifier.
                    shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.TRS(new Vector3(box.center.x, (Mathf.Max(box.size.y + box.center.y - box.size.y * .5f, 1f) - 0.5f) * .5f, box.center.z),
                        Quaternion.Euler(0, box.yaw, 0), Vector3.one),
                    size = new Vector3(box.size.x, Mathf.Max(box.size.y + box.center.y - box.size.y * .5f, 1f) + 0.5f, box.size.z), area = 1,
                };
            }
        }

        /// <summary>
        /// The prop's footprint with every side that faces a keep-clear point pulled back so the point stays
        /// <see cref="ClearRadius"/> outside it; null if too little remains (or a keep-clear point is inside the core).
        /// </summary>
        public static PropBox ClipClear(PropBox p, IReadOnlyList<Vector3> keepClear)
        {
            float x0 = -p.size.x * .5f, x1 = p.size.x * .5f, z0 = -p.size.z * .5f, z1 = p.size.z * .5f;
            var inv = Quaternion.Euler(0, -p.yaw, 0);
            foreach (var k in keepClear)
            {
                var l = inv * new Vector3(k.x - p.center.x, 0, k.z - p.center.z);
                if (l.x < x0 - ClearRadius || l.x > x1 + ClearRadius || l.z < z0 - ClearRadius || l.z > z1 + ClearRadius) continue;
                // Candidate cuts: move one side so the point is ClearRadius outside; keep the cut with the largest area left.
                var options = new List<(float x0, float x1, float z0, float z1)>
                {
                    (x0, Mathf.Min(x1, l.x - ClearRadius), z0, z1), (Mathf.Max(x0, l.x + ClearRadius), x1, z0, z1),
                    (x0, x1, z0, Mathf.Min(z1, l.z - ClearRadius)), (x0, x1, Mathf.Max(z0, l.z + ClearRadius), z1),
                };
                // At least 0.35 m both ways, or the NavMesh voxelisation (0.17 m cells) may not carve it at all.
                var best = options.Where(o => o.x1 - o.x0 >= 0.35f && o.z1 - o.z0 >= 0.35f)
                    .OrderByDescending(o => (o.x1 - o.x0) * (o.z1 - o.z0)).Cast<(float, float, float, float)?>().FirstOrDefault();
                if (best == null) return null;
                (x0, x1, z0, z1) = best.Value;
            }
            var q = Quaternion.Euler(0, p.yaw, 0);
            var c = q * new Vector3((x0 + x1) * .5f, 0, (z0 + z1) * .5f);
            return new PropBox
            {
                name = p.name, seat = p.seat, tree = p.tree, yaw = p.yaw,
                center = new Vector3(p.center.x + c.x, p.center.y, p.center.z + c.z), size = new Vector3(x1 - x0, p.size.y, z1 - z0),
            };
        }

        /// <summary>
        /// Every spot anchor and every approach point (venue-local): the walkable keep-clear set. Furniture is carved, never
        /// the seat anchor or its approach (character-feel lane, docs/progress/requests.md), so NPCs and the player's
        /// approach / align / sit path still reach both.
        /// </summary>
        public static List<Vector3> KeepClear(GameObject root)
        {
            var list = new List<Vector3>();
            foreach (var a in root.GetComponentsInChildren<SpotAnchor>(true))
            {
                list.Add(root.transform.InverseTransformPoint(a.transform.position));
                if (a.approach != null) list.Add(root.transform.InverseTransformPoint(a.approach.position));
            }
            return list;
        }

        // ------------------------------------------------------------------ geometry scan (body-height render geometry)

        public const float ScanCell = 0.2f, BodyMin = 0.25f, BodyMax = 1.9f, ScanTop = 2.2f, ScanKeepClear = 0.75f;

        /// <summary>
        /// Scans the venue's LOD0 render meshes on a 0.2 m grid over the walkable bounds: where the topmost surface below
        /// 2.2 m stands 0.25–1.9 m above the floor and the NavMesh is still under it, or no solid collider holds it, the
        /// sample is a gap. Samples within 0.75 m of an approach point or approach-less spot are left alone. Gaps cluster
        /// into regions (2+ samples); each region comes back as an axis-aligned prop box (collider + carve).
        /// Same criteria as the character-feel lane's VenueFeelAudit, computed analytically (no physics scene needed).
        /// </summary>
        public static List<PropBox> GeometryRegions(GameObject root, NavMeshData navigation, Rect walk, out int walkThrough, out int uncarved)
        {
            walkThrough = uncarved = 0;
            int nx = Mathf.FloorToInt(walk.width / ScanCell) + 1, nz = Mathf.FloorToInt(walk.height / ScanCell) + 1;
            var top = new float[nx, nz];
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) top[i, j] = float.NegativeInfinity;
            var toVenue = root.transform.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                var r = mf.GetComponent<MeshRenderer>();
                if (mesh == null || r == null || !r.enabled || VB.LodIndex(mf.name) > 0 || !mesh.isReadable) continue;
                if (mf.GetComponentsInParent<Transform>(true).Any(t => t.name == "Backdrop")) continue;
                var m = toVenue * mf.transform.localToWorldMatrix;
                var v = mesh.vertices;
                for (int k = 0; k < v.Length; k++) v[k] = m.MultiplyPoint3x4(v[k]);
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var tris = mesh.GetTriangles(sm);
                    for (int t = 0; t < tris.Length; t += 3)
                        Raster(v[tris[t]], v[tris[t + 1]], v[tris[t + 2]], walk, top, nx, nz);
                }
            }
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.enabled).ToList();
            var keep = KeepClear(root);
            NavMeshDataInstance instance = default;
            if (navigation != null) instance = NavMesh.AddNavMeshData(navigation, root.transform.position, root.transform.rotation);
            var gaps = new Dictionary<Vector2Int, float>();
            try
            {
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        float h = top[i, j];
                        if (h < BodyMin || h > BodyMax) continue;
                        float x = walk.xMin + i * ScanCell, z = walk.yMin + j * ScanCell;
                        if (keep.Any(k => new Vector2(k.x - x, k.z - z).sqrMagnitude < ScanKeepClear * ScanKeepClear)) continue;
                        var feet = root.transform.TransformPoint(new Vector3(x, 0f, z));
                        bool onNav = NavMesh.SamplePosition(feet, out var hit, 0.5f, NavMesh.AllAreas) &&
                                     new Vector2(hit.position.x - feet.x, hit.position.z - feet.z).magnitude <= 0.08f;
                        bool solid = colliders.Any(c => Inside(c, root.transform.TransformPoint(new Vector3(x, h - 0.05f, z))) ||
                                                        Inside(c, root.transform.TransformPoint(new Vector3(x, h - 0.2f, z))));
                        // Characters stay on the NavMesh (the player's CharacterController rides on it), so geometry
                        // matters only where the NavMesh is under it: then it needs a solid collider and a carve.
                        if (!onNav) continue;
                        if (solid) uncarved++; else walkThrough++;
                        gaps[new Vector2Int(i, j)] = h;
                    }
            }
            finally { if (instance.valid) NavMesh.RemoveNavMeshData(instance); }
            var regions = new List<PropBox>();
            var visited = new HashSet<Vector2Int>();
            foreach (var g in gaps.Keys.OrderBy(k => k.x).ThenBy(k => k.y))
            {
                if (!visited.Add(g)) continue;
                var cells = new List<Vector2Int>();
                var stack = new Stack<Vector2Int>(); stack.Push(g);
                while (stack.Count > 0)
                {
                    var c = stack.Pop(); cells.Add(c);
                    foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                        if (gaps.ContainsKey(c + d) && visited.Add(c + d)) stack.Push(c + d);
                }
                if (cells.Count < 2) continue;
                float x0 = walk.xMin + cells.Min(c => c.x) * ScanCell - ScanCell * .5f, x1 = walk.xMin + cells.Max(c => c.x) * ScanCell + ScanCell * .5f;
                float z0 = walk.yMin + cells.Min(c => c.y) * ScanCell - ScanCell * .5f, z1 = walk.yMin + cells.Max(c => c.y) * ScanCell + ScanCell * .5f;
                float hmax = cells.Max(c => gaps[c]);
                var box = new PropBox
                {
                    name = $"geometry@{(x0 + x1) * .5f:F1},{(z0 + z1) * .5f:F1}", center = new Vector3((x0 + x1) * .5f, hmax * .5f, (z0 + z1) * .5f),
                    size = new Vector3(x1 - x0, hmax, z1 - z0), seat = true, // clipped against keep-clear points like seats
                };
                regions.Add(box);
            }
            return regions;
        }

        private static void Raster(Vector3 a, Vector3 b, Vector3 c, Rect walk, float[,] top, int nx, int nz)
        {
            if (a.y < BodyMin - 0.05f && b.y < BodyMin - 0.05f && c.y < BodyMin - 0.05f) return;
            if (a.y > ScanTop && b.y > ScanTop && c.y > ScanTop) return;
            float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
            if (Mathf.Abs(area) < 1e-6f) return; // vertical face: its edges belong to neighbouring faces
            int i0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - walk.xMin) / ScanCell));
            int i1 = Mathf.Min(nx - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - walk.xMin) / ScanCell));
            int j0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - walk.yMin) / ScanCell));
            int j1 = Mathf.Min(nz - 1, Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - walk.yMin) / ScanCell));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    float x = walk.xMin + i * ScanCell, z = walk.yMin + j * ScanCell;
                    float w0 = ((b.x - x) * (c.z - z) - (c.x - x) * (b.z - z)) / area;
                    float w1 = ((c.x - x) * (a.z - z) - (a.x - x) * (c.z - z)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    float y = w0 * a.y + w1 * b.y + w2 * c.y;
                    if (y <= ScanTop && y > top[i, j]) top[i, j] = y;
                }
        }

        /// <summary>Fraction of the prop's footprint (3×3 samples at knee height) inside some non-trigger collider.</summary>
        public static float Coverage(GameObject root, PropBox p)
        {
            // Seat tap targets (colliders under a SpotAnchor) do not count: the furniture itself must be solid (round 4).
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.GetComponentInParent<SpotAnchor>() == null).ToList();
            float y = Mathf.Min(p.center.y - p.size.y * .5f + 0.3f, p.center.y);
            int inside = 0, total = 0;
            var q = Quaternion.Euler(0, p.yaw, 0);
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var local = new Vector3(i * p.size.x * .35f, 0, j * p.size.z * .35f);
                    var pt = root.transform.TransformPoint(new Vector3(p.center.x, y, p.center.z) + q * local);
                    total++;
                    if (colliders.Any(c => Inside(c, pt))) inside++;
                }
            return inside / (float)total;
        }

        private static bool Inside(Collider c, Vector3 worldPoint)
        {
            if (c is BoxCollider b)
            {
                var l = b.transform.InverseTransformPoint(worldPoint) - b.center;
                var e = b.size * .5f + Vector3.one * 0.02f;
                return Mathf.Abs(l.x) <= e.x && Mathf.Abs(l.y) <= e.y && Mathf.Abs(l.z) <= e.z;
            }
            if (c is MeshCollider mc && mc.sharedMesh != null)
            {
                var l = mc.transform.InverseTransformPoint(worldPoint);
                var bb = mc.sharedMesh.bounds;
                bb.Expand(0.04f);
                return bb.Contains(l);
            }
            return c.bounds.Contains(worldPoint);
        }

        /// <summary>
        /// Build gate: every prop must be covered by a collider (≥ 60% of its footprint samples) and every non-seat prop
        /// must be off the NavMesh. Throws with the full list of offenders.
        /// </summary>
        public static JObject Validate(string id, GameObject root, NavMeshData navigation, List<PropBox> props)
        {
            var missing = new List<string>();
            var walkThrough = new List<string>();
            var keep = KeepClear(root);
            NavMeshDataInstance instance = default;
            if (navigation != null) instance = NavMesh.AddNavMeshData(navigation, root.transform.position, root.transform.rotation);
            try
            {
                foreach (var p in props)
                {
                    if (Coverage(root, p) < 0.6f) missing.Add(p.name + "@" + p.center.ToString("F1"));
                    // Every prop is carved (seats clipped away from approach points): the centre of what remains of the
                    // carve must be off the NavMesh.
                    var box = p.seat ? ClipClear(p, keep) : p;
                    if (box != null && navigation != null)
                    {
                        var c = root.transform.TransformPoint(new Vector3(box.center.x, 0, box.center.z));
                        if (NavMesh.SamplePosition(c, out var hit, 0.2f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - c.y) < 0.5f)
                            walkThrough.Add(p.name + "@" + p.center.ToString("F1"));
                    }
                }
            }
            finally
            {
                if (instance.valid) NavMesh.RemoveNavMeshData(instance);
            }
            var walk = root.GetComponent<VenueSceneRoot>().cameraBounds;
            var regions = GeometryRegions(root, navigation, walk, out int gapSamples, out int uncarvedSamples);
            if (missing.Count > 0 || walkThrough.Count > 0 || regions.Count > 0)
                throw new InvalidDataException($"{id}: prop collision gate failed. Without collider: [{string.Join(", ", missing)}]. " +
                                               $"Walk-through (on NavMesh): [{string.Join(", ", walkThrough)}]. " +
                                               $"Geometry scan ({gapSamples} walk-through / {uncarvedSamples} uncarved samples): " +
                                               $"[{string.Join(", ", regions.Select(r => r.name + " " + r.size.x.ToString("F1") + "x" + r.size.z.ToString("F1") + " h" + r.size.y.ToString("F2")))}].");
            return new JObject
            {
                ["props"] = props.Count, ["seatProps"] = props.Count(p => p.seat), ["trees"] = props.Count(p => p.tree),
                ["obstacles"] = props.Count, ["geometryScan"] = "clean (0 walk-through, 0 uncarved regions)",
            };
        }

        /// <summary>
        /// Builder step after the first NavMesh: geometry the prop list missed (procedural details, authored joinery, wall-hung
        /// pieces) gets a collider and a carve. Returns the extra props (empty when the scan is clean).
        /// </summary>
        public static List<PropBox> CoverGeometry(GameObject root, NavMeshData navigation, Transform colliderParent)
        {
            var walk = root.GetComponent<VenueSceneRoot>().cameraBounds;
            var extra = GeometryRegions(root, navigation, walk, out _, out _);
            if (extra.Count > 0) AddColliders(colliderParent, root, extra);
            return extra;
        }

        // ------------------------------------------------------------------ props from an authored (repack source) prefab

        private static HashSet<string> _manifestIds;

        /// <summary>Environment manifest model ids (props are instances of these; merged shells/joinery are not).</summary>
        public static bool IsManifestModel(string objectName)
        {
            if (_manifestIds == null)
                _manifestIds = new HashSet<string>(Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Art/environment/manifest.json"))
                    .Properties().Select(p => p.Name));
            string n = objectName.StartsWith("Direction_", StringComparison.Ordinal) ? objectName.Substring(10) : objectName;
            int lod = n.LastIndexOf("_LOD", StringComparison.Ordinal);
            if (lod > 0) n = n.Substring(0, lod);
            return _manifestIds.Contains(n);
        }

        /// <summary>
        /// Prop roots of an instantiated source prefab: LODGroup owners, else renderer objects (LOD suffix stripped),
        /// limited to instances of manifest models (merged walls, floors and joinery keep their own colliders).
        /// </summary>
        public static List<PropBox> FromSourceInstance(GameObject root)
        {
            var groups = new Dictionary<Transform, List<Renderer>>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var owner = r.GetComponentInParent<LODGroup>(true)?.transform;
                if (owner == null) owner = r.name.Contains("_LOD") && r.transform.parent != null && r.transform.parent != root.transform ? r.transform.parent : r.transform;
                if (!groups.TryGetValue(owner, out var list)) groups[owner] = list = new List<Renderer>();
                list.Add(r);
            }
            var toVenue = root.transform.worldToLocalMatrix;
            return groups.Where(g => IsManifestModel(g.Key.name)).Select(g => FromRenderers(g.Key.name, g.Value, g.Key.localToWorldMatrix, toVenue * g.Key.localToWorldMatrix))
                .Where(p => p != null).ToList();
        }
    }
}
