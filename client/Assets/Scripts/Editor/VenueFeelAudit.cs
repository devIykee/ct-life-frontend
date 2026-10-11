using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Read-only physicality audit of every shipped venue/home prefab, for the venue lane:
    /// 1) solid props a character can walk through: free-standing furniture/props (chairs, tables, benches, plants,
    ///    planters, bins, lamps, counters...) whose footprint has no solid (non-trigger) Collider, and whether the venue
    ///    NavMesh still covers the prop's centre (then even NavMesh-driven characters pass through it);
    /// 2) interaction anchors (SpotAnchor id, kind, anim, approach) and whether the furniture under a seat has a collider.
    /// Batch: Unity -batchmode -nographics -projectPath client -executeMethod CTLife.EditorTools.VenueFeelAudit.RunBatch
    ///        [-ctlife-auditOut /abs/report.json]   (exit 0 always; the report lists the gaps)
    /// Menu: CT Life > Venues > Audit physicality (colliders, anchors)
    /// </summary>
    public static class VenueFeelAudit
    {
        [MenuItem("CT Life/Venues/Audit physicality (colliders, anchors)")]
        public static void RunMenu() => Debug.Log(Run(null).ToString());

        public static void RunBatch()
        {
            var outPath = Arg("-ctlife-auditOut");
            try
            {
                var report = Run(outPath);
                Debug.Log("CTLIFE [venue-audit] " + report["summary"]);
            }
            catch (Exception e) { Debug.LogException(e); }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static JObject Run(string outPath)
        {
            var report = new JObject();
            var venues = new JObject();
            report["venues"] = venues;
            int totalGaps = 0, totalWalkThrough = 0;
            var ids = VenueContent.VenueIds.Select(v => (v, false)).Concat(VenueContent.BuiltHomes.Select(h => (h, true))).ToList();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var (id, home) in ids)
            {
                var prefab = home ? VenueContent.FindHome(id) : VenueContent.Find(id);
                if (prefab == null) continue;
                var root = (VenueSceneRoot)PrefabUtility.InstantiatePrefab(prefab, scene);
                NavMeshDataInstance nav = default;
                try
                {
                    if (root.navigation != null) nav = NavMesh.AddNavMeshData(root.navigation, root.transform.position, root.transform.rotation);
                    var v = AuditVenue(root, prefab);
                    v["prefab"] = AssetDatabase.GetAssetPath(prefab);
                    venues[(home ? "home:" : "") + id] = v;
                    totalGaps += ((JArray)v["props_without_collider"]).Count;
                    totalWalkThrough += ((JArray)v["walkthrough_regions"]).Sum(p => (int)p["samples"]);
                }
                finally
                {
                    if (nav.valid) NavMesh.RemoveNavMeshData(nav);
                    Object.DestroyImmediate(root.gameObject);
                }
            }
            int navThrough = venues.Properties().Sum(v => ((JArray)v.Value["navmesh_through_collider_regions"]).Count);
            report["summary"] = $"{venues.Count} venues/homes; {navThrough} collider-but-uncarved-NavMesh regions; {totalGaps} walk-through regions (body-height geometry, no solid collider, NavMesh under it; {totalWalkThrough} samples of 0.2 m)";
            if (!string.IsNullOrEmpty(outPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, report.ToString());
            }
            return report;
        }

        private const int ScratchLayer = 31;
        private const float Cell = 0.2f, BodyMin = 0.25f, BodyMax = 1.9f;

        private static JObject AuditVenue(VenueSceneRoot root, VenueSceneRoot prefab)
        {
            var result = new JObject();
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.enabled).ToArray();
            var floorY = root.transform.position.y;
            // 1) Body-height geometry with no solid collider over it, where the NavMesh still lets characters walk.
            var scratch = new List<GameObject>();
            try
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                    var r = mf.GetComponent<MeshRenderer>();
                    if (r == null || !r.enabled) continue;
                    var go = new GameObject("scratch_" + mf.name) { layer = ScratchLayer, hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                    go.transform.localScale = mf.transform.lossyScale;
                    go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    scratch.Add(go);
                }
                Physics.SyncTransforms();
                int solidMask = ~(1 << ScratchLayer);
                var b = root.cameraBounds; // venue-local x0,z0,w,h
                var gaps = new HashSet<Vector2Int>();
                var navOnly = new HashSet<Vector2Int>(); // has a collider, but the NavMesh is not carved: NPCs pass through
                var heights = new Dictionary<Vector2Int, float>();
                for (float x = b.xMin; x <= b.xMax; x += Cell)
                    for (float z = b.yMin; z <= b.yMax; z += Cell)
                    {
                        var top = root.transform.TransformPoint(new Vector3(x, floorY + BodyMax + 0.3f, z));
                        if (!Physics.Raycast(top, Vector3.down, out var geo, BodyMax + 0.3f - BodyMin, 1 << ScratchLayer, QueryTriggerInteraction.Ignore)) continue;
                        float h = geo.point.y - floorY;
                        if (h < BodyMin || h > BodyMax) continue;
                        var feet = new Vector3(geo.point.x, floorY, geo.point.z);
                        // Walkable here = the NavMesh is under this very sample (carved props have none).
                        if (!NavMesh.SamplePosition(feet, out var nav, 0.5f, NavMesh.AllAreas) ||
                            new Vector2(nav.position.x - feet.x, nav.position.z - feet.z).magnitude > 0.08f) continue;
                        // A real solid collider at that body height (or a solid within 0.1 m) blocks a physics body.
                        bool solid = Physics.CheckSphere(geo.point + Vector3.down * 0.05f, 0.1f, solidMask, QueryTriggerInteraction.Ignore) ||
                            (Physics.Raycast(top, Vector3.down, out var real, BodyMax + 0.3f - 0.05f, solidMask, QueryTriggerInteraction.Ignore) &&
                             real.point.y >= geo.point.y - 0.25f);
                        var key = new Vector2Int(Mathf.RoundToInt(x / Cell), Mathf.RoundToInt(z / Cell));
                        (solid ? navOnly : gaps).Add(key);
                        heights[key] = h;
                    }
                JArray Regions(HashSet<Vector2Int> set)
                {
                    var regions = new JArray();
                    var visited = new HashSet<Vector2Int>();
                    foreach (var g in set.OrderBy(k => k.x).ThenBy(k => k.y))
                    {
                        if (!visited.Add(g)) continue;
                        var stack = new Stack<Vector2Int>(); stack.Push(g);
                        var cells = new List<Vector2Int>();
                        while (stack.Count > 0)
                        {
                            var c = stack.Pop(); cells.Add(c);
                            foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                                if (set.Contains(c + d) && visited.Add(c + d)) stack.Push(c + d);
                        }
                        if (cells.Count < 2) continue; // a single 0.2 m sample is noise (thin trim, a cable)
                        float x0 = cells.Min(c => c.x) * Cell, x1 = cells.Max(c => c.x) * Cell, z0 = cells.Min(c => c.y) * Cell, z1 = cells.Max(c => c.y) * Cell;
                        regions.Add(new JObject
                        {
                            ["centre_local"] = $"{(x0 + x1) / 2:F2},{(z0 + z1) / 2:F2}", ["extent_xz"] = $"{x1 - x0 + Cell:F2}x{z1 - z0 + Cell:F2}",
                            ["max_height"] = Math.Round(cells.Max(c => heights[c]), 2), ["samples"] = cells.Count,
                            ["nearest_anchor"] = NearestAnchor(root, new Vector3((x0 + x1) / 2, 0, (z0 + z1) / 2)),
                        });
                    }
                    return regions;
                }
                var regions = Regions(gaps);
                result["navmesh_through_collider_regions"] = Regions(navOnly);
                result["walkthrough_regions"] = regions;
            }
            finally { foreach (var go in scratch) Object.DestroyImmediate(go); }
            result["props_without_collider"] = result["walkthrough_regions"];
            var anchors = new JArray();
            foreach (var a in root.GetComponentsInChildren<SpotAnchor>(true))
            {
                var p = a.transform.position;
                // Furniture under or right beside the anchor (seat pan, bench, sofa): any solid collider within 0.6 m.
                bool furnitureSolid = colliders.Any(c =>
                {
                    var cp = c.ClosestPointOnBounds(p + Vector3.up * 0.4f);
                    return c.bounds.size.y >= 0.3f && c.bounds.size.y < 2.5f && Vector3.Distance(cp, p + Vector3.up * 0.4f) < 0.6f;
                });
                anchors.Add(new JObject
                {
                    ["spotId"] = a.spotId, ["kind"] = a.kind, ["anim"] = a.anim, ["approach"] = a.approach != null,
                    ["local"] = Fmt(root.transform.InverseTransformPoint(p)), ["yaw"] = Math.Round(a.transform.eulerAngles.y, 1),
                    ["seat_has_collider"] = furnitureSolid,
                });
            }
            result["anchors"] = anchors;
            var cols = new JArray();
            foreach (var c in colliders)
                cols.Add(new JObject
                {
                    ["path"] = PathOf(c.transform, root.transform), ["type"] = c.GetType().Name,
                    ["centre_local"] = Fmt(root.transform.InverseTransformPoint(c.bounds.center)),
                    ["size"] = $"{c.bounds.size.x:F2}x{c.bounds.size.y:F2}x{c.bounds.size.z:F2}", ["layer"] = LayerMask.LayerToName(c.gameObject.layer),
                });
            result["colliders"] = cols;
            result["navmesh"] = root.navigation != null;
            result["solid_colliders"] = colliders.Length;
            return result;
        }

        private static string NearestAnchor(VenueSceneRoot root, Vector3 local)
        {
            SpotAnchor best = null; float bd = float.MaxValue;
            foreach (var a in root.GetComponentsInChildren<SpotAnchor>(true))
            {
                float d = (root.transform.InverseTransformPoint(a.transform.position) - local).sqrMagnitude;
                if (d < bd) { bd = d; best = a; }
            }
            return best == null ? null : $"{best.spotId} ({Mathf.Sqrt(bd):F1} m)";
        }

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var p = t; p != null && p != root; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Fmt(Vector3 v) => $"{v.x:F2},{v.y:F2},{v.z:F2}";

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
