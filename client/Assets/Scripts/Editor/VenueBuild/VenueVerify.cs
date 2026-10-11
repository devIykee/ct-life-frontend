using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Post-bake verification in a clean scene: prefab loads, lightmaps bind, NavMesh present and every spot on it and
    /// reachable, then gameplay-camera budget estimates (visible draws/triangles with LOD selection at the Android
    /// LOD bias) and render captures. Writes {id}.png (small preview) and receipts/{id}-verify.json to the evidence dir.
    /// Draw-call figures are editor estimates: one draw per visible renderer submesh (venue prefabs are instantiated at
    /// runtime, so no static batching applies). Device profiling remains the acceptance gate.
    /// </summary>
    public static class VenueVerify
    {
        private const int PreviewW = 960, PreviewH = 540;

        [MenuItem("CT Life/Venues/Verify built venues")]
        public static void Verify()
        {
            int failures = 0;
            var table = new JArray();
            foreach (var spec in VenueBuildPipeline.Selected())
            {
                try { table.Add(VerifyOne(spec)); }
                catch (Exception e)
                {
                    failures++;
                    Debug.LogError($"CTLIFE [venues] verify {spec.id} FAILED: {e}");
                    table.Add(new JObject { ["id"] = spec.id, ["ok"] = false, ["error"] = e.Message });
                }
            }
            Directory.CreateDirectory(VenueBuildPipeline.EvidenceDir);
            File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "last-verify.json"), table.ToString());
            Debug.Log($"CTLIFE [venues] verify finished: {table.Count - failures} ok, {failures} failed.");
            if (Application.isBatchMode && failures > 0) EditorApplication.Exit(1);
        }

        public static JObject VerifyOne(VenueSpec spec)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) ?? throw new FileNotFoundException(spec.PrefabPath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Read (never switch) the Android quality level: switching levels from batchmode re-serialises
            // ProjectSettings/QualitySettings.asset.
            int android = AndroidQualityLevel(out float lodBias);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            NavMeshDataInstance nav = default;
            try
            {
                var venue = go.GetComponent<VenueSceneRoot>() ?? throw new InvalidDataException("no VenueSceneRoot");
                var spots = VenueSpotsFile.Load(spec.spotsKey);
                venue.Init(spots);
                var maps = go.GetComponent<VenueLightmaps>();
                bool baked = maps != null && maps.colors != null && maps.colors.Length > 0 && maps.colors.All(c => c != null);
                int lights = go.GetComponentsInChildren<Light>(true).Length;
                if (baked) maps.Apply();
                go.GetComponent<VenueContentLighting>()?.Apply();
                var look = go.GetComponent<VenueLook>();

                // Navigation from the saved prefab asset (not the build-time copy).
                // -ctNoGate: capture-only run (e.g. a "before" shot of a prefab whose spots the proposal has since extended).
                var navReport = Environment.GetCommandLineArgs().Contains("-ctNoGate") ? new JArray() : VenueBuildPipeline.ValidateBuild(spec, venue, spots);

                var cam = NewCamera();
                if (look != null) look.Apply(cam); // grade, clear colour and probes as the game camera gets them
                // Entrance gameplay captures are opt-in (-ctEntranceCaptures): the default is one preview per venue.
                bool entranceCapture = Environment.GetCommandLineArgs().Contains("-ctEntranceCaptures");
                var (views, maxDraws, maxTris) = MeasureViews(go, venue, spots, lodBias, cam,
                    entranceCapture ? Path.Combine(VenueBuildPipeline.EvidenceDir, "captures", spec.id + "-entrance.png") : null);
                // Overview preview: high three-quarter shot of the whole diorama.
                PlaceOverview(cam, venue);
                var overview = Estimate(go, cam, lodBias);
                Render(cam, Path.Combine(VenueBuildPipeline.EvidenceDir, spec.id + ".png"), PreviewW, PreviewH);

                int allTris = 0, allDraws = 0;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r.transform.parent != null && r.transform.parent.GetComponent<LODGroup>() != null && r.name.EndsWith("_LOD1")) continue;
                    allTris += Tris(mesh); allDraws += mesh.subMeshCount;
                }
                var result = new JObject
                {
                    ["id"] = spec.id, ["ok"] = true, ["venueId"] = spec.venueId, ["baked"] = baked,
                    ["lightmaps"] = baked ? maps.colors.Length : 0,
                    ["lightmapTexels"] = baked ? maps.colors.Sum(t => (long)t.width * t.height) : 0,
                    ["boundRenderers"] = baked ? maps.bindings.Length : 0, ["prefabLights"] = lights,
                    ["navigation"] = navReport, ["spots"] = navReport.Count,
                    ["views"] = views, ["maxEstimatedDraws"] = maxDraws, ["maxEstimatedTriangles"] = maxTris,
                    ["overviewDraws"] = overview.draws, ["overviewTriangles"] = overview.tris,
                    ["wholeVenueLod0Draws"] = allDraws, ["wholeVenueLod0Triangles"] = allTris,
                    ["androidQualityLevel"] = android, ["lodBias"] = lodBias,
                    ["drawBudgetOk"] = maxDraws < 150, ["verifiedUtc"] = DateTime.UtcNow.ToString("o"),
                };
                if (!baked) result["warning"] = "not baked";
                Directory.CreateDirectory(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts"));
                File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts", spec.id + "-verify.json"), result.ToString());
                Debug.Log($"CTLIFE [venues] verified {spec.id}: baked={baked}, gameplay max draws≈{maxDraws}, tris≈{maxTris:N0}; " +
                          $"overview draws≈{overview.draws}; whole-venue LOD0 {allDraws} draws / {allTris:N0} tris; spots={navReport.Count} reachable.");
                return result;
            }
            finally
            {
                if (go != null) go.GetComponent<VenueLook>()?.Restore();
                if (go != null) go.GetComponent<VenueLightmaps>()?.Restore();
                if (go != null) go.GetComponent<VenueContentLighting>()?.Restore();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        internal static Camera NewCamera()
        {
            var cam = new GameObject("Verify camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.81f, 0.83f, 0.80f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.aspect = 2670f / 1200f;
            return cam;
        }

        /// <summary>Gameplay-camera draw/triangle estimates at the entrance, centre and farthest spot.</summary>
        internal static (JArray views, int maxDraws, int maxTris) MeasureViews(GameObject go, VenueSceneRoot venue, CTLife.Core.VenueSpots spots,
            float lodBias, Camera cam, string entranceCapturePath)
        {
            var views = new JArray();
            int maxDraws = 0, maxTris = 0;
            var hero = spots.spots.Values.Distinct().Where(s => s.hasPosition && s.kind != "exit").OrderByDescending(s => s.z).FirstOrDefault();
            var targets = new List<(string label, Vector3 p)>
            {
                ("entrance", new Vector3(spots.Entrance.x, 0, spots.Entrance.z)),
                ("center", spots.spots.TryGetValue("center", out var c) ? new Vector3(c.x, 0, c.z) : Vector3.zero),
            };
            if (hero != null) targets.Add(("far_" + hero.id, new Vector3(hero.x, 0, hero.z)));
            // -ctCaptureSpots a,b: extra gameplay-camera captures at named spots (evidence for details such as the eggs).
            var args = Environment.GetCommandLineArgs();
            int ci = Array.IndexOf(args, "-ctCaptureSpots");
            if (ci >= 0 && ci + 1 < args.Length && entranceCapturePath != null)
                foreach (var id in args[ci + 1].Split(','))
                    if (spots.spots.TryGetValue(id, out var sp))
                    {
                        PlaceGameplayCamera(cam, venue, new Vector3(sp.x, 0, sp.z));
                        Render(cam, entranceCapturePath.Replace("-entrance.png", "-spot-" + id + ".png"), 1870, 841);
                    }
            foreach (var (label, p) in targets)
            {
                PlaceGameplayCamera(cam, venue, p);
                var est = Estimate(go, cam, lodBias);
                maxDraws = Mathf.Max(maxDraws, est.draws);
                maxTris = Mathf.Max(maxTris, est.tris);
                // Gameplay captures at the in-game concepts' size and aspect (side-by-side evidence): entrance and centre views.
                if (label == "entrance" && entranceCapturePath != null) Render(cam, entranceCapturePath, 1870, 841);
                if (label == "center" && entranceCapturePath != null) Render(cam, entranceCapturePath.Replace("-entrance.png", "-center.png"), 1870, 841);
                views.Add(new JObject
                {
                    ["view"] = label, ["estimatedDraws"] = est.draws, ["estimatedTriangles"] = est.tris, ["materials"] = est.materials,
                    ["visibleRenderers"] = est.renderers,
                });
            }
            return (views, maxDraws, maxTris);
        }

        internal static int AndroidQualityLevel(out float lodBias)
        {
            lodBias = QualitySettings.lodBias;
            try
            {
                string quality = File.ReadAllText("ProjectSettings/QualitySettings.asset");
                var m = Regex.Match(quality, @"(?m)^\s+Android: (\d+)");
                if (!m.Success) return -1;
                int level = int.Parse(m.Groups[1].Value);
                var biases = Regex.Matches(quality, @"(?m)^\s+lodBias: ([0-9.]+)");
                if (level < biases.Count) lodBias = float.Parse(biases[level].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return level;
            }
            catch { return -1; }
        }

        /// <summary>Same framing math as CameraRig.Follow with the venue's authored settings.</summary>
        private static void PlaceGameplayCamera(Camera cam, VenueSceneRoot venue, Vector3 target)
        {
            cam.fieldOfView = venue.cameraFieldOfView;
            float ahead = CameraRig.LookAheadForViewport(venue.cameraDistance, venue.cameraPitch, venue.cameraFieldOfView,
                venue.cameraTargetViewportY, venue.cameraFocusHeight);
            var focus = target + Quaternion.Euler(0, venue.cameraYaw, 0) * Vector3.forward * ahead;
            var rot = Quaternion.Euler(venue.cameraPitch, venue.cameraYaw, 0);
            cam.transform.SetPositionAndRotation(focus + Vector3.up * venue.cameraFocusHeight - rot * Vector3.forward * venue.cameraDistance, rot);
        }

        private static void PlaceOverview(Camera cam, VenueSceneRoot venue)
        {
            var b = venue.cameraBounds;
            cam.fieldOfView = 40f;
            cam.aspect = PreviewW / (float)PreviewH;
            float size = Mathf.Max(b.width, b.height * 1.6f);
            float distance = size * 0.5f / Mathf.Tan(20f * Mathf.Deg2Rad) * 1.05f;
            var rot = Quaternion.Euler(44f, -18f, 0);
            var focus = new Vector3(b.center.x, 0.5f, b.center.y + b.height * 0.06f);
            cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }

        private struct Est { public int draws, tris, materials, renderers; }

        private static Est Estimate(GameObject root, Camera cam, float bias)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var counted = new HashSet<Renderer>();
            var mats = new HashSet<Material>();
            var e = new Est();
            var inGroup = new HashSet<Renderer>();
            foreach (var g in root.GetComponentsInChildren<LODGroup>())
            {
                var lods = g.GetLODs();
                foreach (var l in lods) foreach (var r in l.renderers) if (r != null) inGroup.Add(r);
                // Screen-relative height as Unity computes it (perspective): size / (distance * 2 tan(fov/2)).
                var world = g.transform.TransformPoint(g.localReferencePoint);
                float size = g.size * Mathf.Max(g.transform.lossyScale.x, g.transform.lossyScale.y, g.transform.lossyScale.z);
                float rel = size / (Vector3.Distance(cam.transform.position, world) * 2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f)) * bias;
                int active = -1;
                for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) { active = i; break; }
                if (active < 0) continue;
                foreach (var r in lods[active].renderers) Count(r);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (!inGroup.Contains(r)) Count(r);
            e.materials = mats.Count;
            return e;

            void Count(Renderer r)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || !counted.Add(r)) return;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) return;
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) return;
                e.renderers++;
                e.draws += mesh.subMeshCount;
                e.tris += Tris(mesh);
                foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
            }
        }

        private static int Tris(Mesh mesh)
        {
            long n = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) n += mesh.GetIndexCount(i);
            return (int)(n / 3);
        }

        private struct Stats { public int batches, tris; }

        private static Stats Render(Camera cam, string path, int w, int h)
        {
            var stats = new Stats { batches = -1, tris = -1 };
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("CTLIFE [venues] no graphics device (-nographics): skipping render capture.");
                return stats;
            }
            float aspect = cam.aspect;
            cam.aspect = w / (float)h;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var previous = RenderTexture.active;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (!RenderPipeline.SupportsRenderRequest(cam, request)) throw new InvalidOperationException("URP single-camera capture unsupported.");
                RenderPipeline.SubmitRenderRequest(cam, request);
                stats.batches = UnityStats.batches;
                stats.tris = UnityStats.triangles;
                if (path != null)
                {
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previous;
                cam.aspect = aspect;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            return stats;
        }
    }
}
