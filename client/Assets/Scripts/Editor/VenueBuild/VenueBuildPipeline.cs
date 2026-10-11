using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Reproducible venue pipeline (batchmode entry points):
    ///   Build:  -executeMethod CTLife.EditorTools.VenueBuild.VenueBuildPipeline.Build  [-ctVenues perp_pit,rekt_clinic]
    ///   Bake:   -executeMethod CTLife.EditorTools.VenueBuild.VenueBuildPipeline.Bake   (needs graphics: omit -nographics)
    ///   Verify: -executeMethod CTLife.EditorTools.VenueBuild.VenueVerify.Verify        (needs graphics for captures)
    ///   All:    -executeMethod CTLife.EditorTools.VenueBuild.VenueBuildPipeline.All
    /// Outputs live only under Assets/World/Venues/{id}/. Receipts and previews go to -ctEvidence (outside the repo).
    /// </summary>
    public static class VenueBuildPipeline
    {
        public static string EvidenceDir => Arg("-ctEvidence") ?? "/Users/mac/Documents/codes/projects/ct-life-evidence/venues";

        public static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static List<VenueSpec> Selected()
        {
            var filter = Arg("-ctVenues");
            var all = VenueCatalog.All;
            if (string.IsNullOrEmpty(filter) || filter == "all") return all;
            var ids = filter.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var unknown = ids.Where(i => all.All(v => v.id != i)).ToList();
            if (unknown.Count > 0) throw new ArgumentException("Unknown venue ids: " + string.Join(",", unknown));
            return all.Where(v => ids.Contains(v.id)).ToList();
        }

        [MenuItem("CT Life/Venues/Build all venue dioramas")]
        public static void Build() => Run("build", spec => BuildOne(spec));

        [MenuItem("CT Life/Venues/Bake all venue dioramas")]
        public static void Bake() => Run("bake", BakeOne);

        public static void All() => Run("all", spec => { BuildOne(spec); BakeOne(spec); VenueVerify.VerifyOne(spec); });

        private static void Run(string phase, Action<VenueSpec> step)
        {
            int failures = 0;
            var summary = new JArray();
            foreach (var spec in Selected())
            {
                var started = DateTime.UtcNow;
                try
                {
                    step(spec);
                    summary.Add(new JObject { ["id"] = spec.id, ["phase"] = phase, ["ok"] = true, ["seconds"] = (DateTime.UtcNow - started).TotalSeconds });
                }
                catch (Exception e)
                {
                    failures++;
                    Debug.LogError($"CTLIFE [venues] {phase} {spec.id} FAILED: {e}");
                    summary.Add(new JObject { ["id"] = spec.id, ["phase"] = phase, ["ok"] = false, ["error"] = e.Message });
                }
            }
            Directory.CreateDirectory(EvidenceDir);
            File.WriteAllText(Path.Combine(EvidenceDir, "last-" + phase + ".json"), summary.ToString());
            Debug.Log($"CTLIFE [venues] {phase} finished: {summary.Count - failures} ok, {failures} failed.");
            if (Application.isBatchMode && failures > 0) EditorApplication.Exit(1);
        }

        public static VB.Result BuildOne(VenueSpec spec)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build venues outside Play mode.");
            if (!string.IsNullOrEmpty(spec.repackSource)) return VenueRepack.Repack(spec);
            VenuePalette.Reset();
            VenueSpotsFile.Reset();
            VenuePalette.EnsureTexture();
            var spots = VenueSpotsFile.Load(spec.spotsKey);
            var scene = EditorSceneManager.NewPreviewScene();
            VB.Result result;
            var vb = new VB(spec);
            try
            {
                vb.Begin(scene, spots);
                spec.build(vb);
                result = vb.Finish();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            SaveFolder(spec.Folder);
            SaveFolder(VenuePalette.SharedRoot);
            var nav = ValidateBuild(spec, result.prefab.GetComponent<VenueSceneRoot>(), spots);
            var collision = VenueCollision.Validate(spec.id, result.prefab, result.prefab.GetComponent<VenueSceneRoot>().navigation, vb.Props);
            var receipt = new JObject
            {
                ["id"] = spec.id, ["venueId"] = spec.venueId, ["spotsKey"] = spec.spotsKey, ["homeId"] = spec.homeId, ["title"] = spec.title,
                ["district"] = spec.district, ["identity"] = spec.identity, ["prefab"] = spec.PrefabPath,
                ["prefabGuid"] = AssetDatabase.AssetPathToGUID(spec.PrefabPath),
                ["sourcedInstances"] = result.sourcedInstances, ["staticRenderers"] = result.staticRenderers,
                ["lod0Triangles"] = result.lod0Triangles, ["materials"] = result.materials, ["hooks"] = new JArray(vb.Hooks),
                ["sources"] = JObject.FromObject(result.sources), ["notes"] = new JArray(vb.Notes.Distinct()), ["navigation"] = nav,
                ["collision"] = collision,
                ["builtUtc"] = DateTime.UtcNow.ToString("o"),
            };
            Directory.CreateDirectory(Path.Combine(EvidenceDir, "receipts"));
            File.WriteAllText(Path.Combine(EvidenceDir, "receipts", spec.id + "-build.json"), receipt.ToString());
            Debug.Log($"CTLIFE [venues] built {spec.id}: {result.sourcedInstances} sourced instances, {result.staticRenderers} static renderers, " +
                      $"{result.lod0Triangles:N0} LOD0 tris, {result.materials} materials, {vb.Hooks.Count} hooks. Notes: {string.Join("; ", vb.Notes.Distinct())}");
            return result;
        }

        internal static void SaveFolder(string folder)
        {
            foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
            }
        }

        /// <summary>Anchors unchanged, every positioned spot on the NavMesh and reachable from the entrance.</summary>
        internal static JArray ValidateBuild(VenueSpec spec, VenueSceneRoot venue, VenueSpots spots)
        {
            var report = new JArray();
            var anchors = venue.GetComponentsInChildren<SpotAnchor>(true);
            foreach (var s in spots.spots.Values.Distinct().Where(s => s.hasPosition))
            {
                var a = anchors.FirstOrDefault(x => x.spotId == s.id);
                // The contract is 2D: anchors may sit on raised floors (stages, risers), so compare x/z only.
                if (a == null || Vector2.Distance(new Vector2(a.transform.position.x, a.transform.position.z), new Vector2(s.x, s.z)) > 0.001f ||
                    Mathf.Abs(Mathf.DeltaAngle(a.transform.eulerAngles.y, s.rotY * Mathf.Rad2Deg)) > 0.05f)
                    throw new InvalidDataException(spec.id + ": anchor moved or missing for " + s.id);
            }
            foreach (var r in venue.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || m.shader == null || m.shader.name.Contains("InternalError"))
                        throw new InvalidDataException(spec.id + ": missing material on " + r.name);
            if (venue.navigation == null) throw new InvalidDataException(spec.id + ": no navigation.");
            var instance = NavMesh.AddNavMeshData(venue.navigation);
            try
            {
                var entrance = spots.Entrance ?? throw new InvalidDataException(spec.id + ": no entrance spot.");
                if (!NavMesh.SamplePosition(new Vector3(entrance.x, 0, entrance.z), out var start, 0.75f, NavMesh.AllAreas))
                    throw new InvalidDataException(spec.id + ": entrance is off the NavMesh.");
                foreach (var s in spots.spots.Values.Distinct().Where(s => s.hasPosition).OrderBy(s => s.id, StringComparer.Ordinal))
                {
                    var p = new Vector3(s.x, 0, s.z);
                    bool on = NavMesh.SamplePosition(p, out var hit, 0.75f, NavMesh.AllAreas);
                    var path = new NavMeshPath();
                    bool reach = on && NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    var approach = anchors.First(x => x.spotId == s.id).approach;
                    bool approachOk = true;
                    if (approach != null)
                    {
                        var ap = new NavMeshPath();
                        approachOk = NavMesh.SamplePosition(approach.position, out var ah, 0.3f, NavMesh.AllAreas) &&
                                     NavMesh.CalculatePath(start.position, ah.position, NavMesh.AllAreas, ap) && ap.status == NavMeshPathStatus.PathComplete;
                        // Seats tucked into furniture (Terminal Café, Spaces Studio) are reached through their approach point;
                        // the player walks there and the server snaps the seated pose to the anchor.
                        if (!reach && approachOk) reach = true;
                    }
                    report.Add(new JObject
                    {
                        ["spot"] = s.id, ["kind"] = s.kind, ["onNavMesh"] = on, ["offset"] = on ? Math.Round(Vector3.Distance(new Vector3(hit.position.x, 0, hit.position.z), p), 3) : -1,
                        ["reachable"] = reach, ["approach"] = approach != null ? (approachOk ? "reachable" : "UNREACHABLE") : "none",
                    });
                    if (!reach || !approachOk)
                    {
                        // ASCII NavMesh map round the spot (0.2 m cells): '.' walkable, '#' not, 'S' spot, 'A' approach.
                        var sb = new System.Text.StringBuilder();
                        var centre = approach != null ? approach.position : p;
                        for (int j = 12; j >= -12; j--)
                        {
                            sb.Append('\n');
                            for (int i = -15; i <= 15; i++)
                            {
                                var q = centre + new Vector3(i * 0.2f, 0, j * 0.2f);
                                char ch = NavMesh.SamplePosition(q, out var qh, 0.12f, NavMesh.AllAreas) ? '.' : '#';
                                if (Mathf.Abs(q.x - p.x) < 0.1f && Mathf.Abs(q.z - p.z) < 0.1f) ch = 'S';
                                if (approach != null && i == 0 && j == 0) ch = 'A';
                                sb.Append(ch);
                            }
                        }
                        throw new InvalidDataException($"{spec.id}: {(reach ? "approach for " : "spot ")}{s.id} unreachable from the entrance (onNavMesh={on}, approach {(approach != null ? approach.position.ToString("F2") : "none")}).{sb}");
                    }
                    if (!approachOk) throw new InvalidDataException($"{spec.id}: approach for {s.id} unreachable.");
                }
            }
            finally
            {
                if (instance.valid) NavMesh.RemoveNavMeshData(instance);
            }
            return report;
        }

        /// <summary>
        /// Static lighting via the project's existing VenueLightingBuilder (ProgressiveGPU on Apple silicon; graphics
        /// required). Baked-only lights are removed from the prefab afterwards: their light is in the lightmaps, and an
        /// unbaked prefab light would otherwise render as a realtime light at runtime.
        /// </summary>
        public static void BakeOne(VenueSpec spec)
        {
            VenueBake.Bake(spec);
            Debug.Log("CTLIFE [venues] baked " + spec.id);
        }
    }
}
