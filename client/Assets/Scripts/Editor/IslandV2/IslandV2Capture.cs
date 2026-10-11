using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Light verification captures of the built island (edit mode, no play): loads the base prefab, every cell and every
    /// venue diorama at its placement, applies the island atmosphere, and renders named views to PNG (landscape phone
    /// aspect), plus a top-down orthographic overview.
    /// Batch: Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.IslandV2.IslandV2Capture.Run
    ///        -ctlife-captureOut &lt;dir&gt; [-ctlife-views name,name]
    /// </summary>
    public static class IslandV2Capture
    {
        public struct View
        {
            public string Name; public Vector3 Eye, Target; public float Fov; public bool Ortho; public float OrthoSize; public bool Ground;
            public View(string n, Vector3 eye, Vector3 target, float fov = 50f, bool ground = false) { Name = n; Eye = eye; Target = target; Fov = fov; Ortho = false; OrthoSize = 0; Ground = ground; }
        }

        public static readonly View[] Views =
        {
            new View("overview", new Vector3(80f, 470f, -1050f), new Vector3(0f, 20f, 120f), 46f),
            new View("range_from_downtown", new Vector3(30f, 22f, -70f), new Vector3(-140f, 80f, 480f), 55f),
            new View("downtown_aerial", new Vector3(230f, 170f, -300f), new Vector3(0f, 0f, 10f), 50f),
            new View("bay", new Vector3(-30f, 60f, -520f), new Vector3(10f, 0f, -120f), 52f),
            new View("downtown_street", new Vector3(-150f, 1.9f, 30f), new Vector3(120f, 6f, 44f), 58f, true),
            new View("plaza", new Vector3(22f, 34f, -84f), new Vector3(0f, 0f, 8f), 55f),
            new View("ocean_drive", new Vector3(-170f, 2.2f, -226f), new Vector3(60f, 4f, -190f), 58f, true),
            new View("harbour_bridge", new Vector3(-400f, 16f, -486f), new Vector3(-510f, 2f, -400f), 55f),
            new View("trenches", new Vector3(-250f, 30f, -330f), new Vector3(-360f, 0f, -250f), 55f),
            new View("summit_view", new Vector3(-250f, 96f, 414f), new Vector3(0f, 0f, -120f), 55f),
            new View("mtndao_lodge", new Vector3(-170f, 96f, 360f), new Vector3(-255f, 74f, 412f), 55f),
            new View("switchbacks", new Vector3(-40f, 120f, 250f), new Vector3(-210f, 45f, 380f), 55f),
            new View("summit_aerial", new Vector3(-60f, 260f, 200f), new Vector3(-190f, 100f, 470f), 52f),
            new View("tunnel", new Vector3(752f, 22f, 196f), new Vector3(782f, 16f, 262f), 55f),
            new View("marina", new Vector3(470f, 46f, -500f), new Vector3(490f, 0f, -320f), 52f),
            new View("farm", new Vector3(520f, 40f, -60f), new Vector3(640f, 8f, 110f), 55f),
            new View("builder_hills", new Vector3(240f, 60f, 140f), new Vector3(380f, 20f, 320f), 55f),
            new View("breakpoint", new Vector3(-150f, 40f, 170f), new Vector3(-300f, 10f, 290f), 55f),
            new View("timeline_screens", new Vector3(46f, 9f, 22f), new Vector3(-6f, 7f, 50f), 55f),
            new View("boardwalk", new Vector3(-60f, 3.2f, -230f), new Vector3(60f, 0f, -222f), 58f, true),
            new View("marina_quay", new Vector3(520f, 8f, -420f), new Vector3(470f, -3f, -360f), 55f),
            new View("lighthouse", new Vector3(720f, 14f, -480f), new Vector3(676f, 12f, -532f), 50f),
        };

        public static void Run()
        {
            string fatal = null;
            var outDir = Arg("-ctlife-captureOut") ?? Path.Combine(IslandV2Paths.Repo, "..", "ct-life-evidence", "world-v2-20261017", "capture");
            Directory.CreateDirectory(outDir);
            var only = Arg("-ctlife-views")?.Split(',').ToHashSet();
            var receipt = new JObject();
            try { Capture(outDir, only, receipt); }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            receipt["fatal"] = fatal;
            File.WriteAllText(Path.Combine(outDir, "island-v2-capture-receipt.json"), receipt.ToString());
            Debug.Log($"CTLIFE [islandv2-capture] {(fatal == null ? "PASS" : "FAIL " + fatal)} -> {outDir}");
            if (Application.isBatchMode) EditorApplication.Exit(fatal == null ? 0 : 1);
        }

        public static void Capture(string outDir, HashSet<string> only, JObject receipt)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.BasePrefab) ?? throw new InvalidOperationException("island not built");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var world = root.GetComponent<IslandWorld>();
            root.GetComponent<IslandTerrainFar>()?.Build(world.LoadHeightfield());
            foreach (var c in world.cells)
            {
                var cp = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.CellsDir + "/" + c.id + ".prefab");
                if (cp == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(cp);
                go.transform.SetParent(root.transform, false);
                go.GetComponentInChildren<IslandTerrainCell>(true)?.BuildNow(world.LoadHeightfield());
                if (c.far != null) root.transform.Find("Far/" + c.id + "_far")?.gameObject.SetActive(false);
            }
            int venues = 0;
            var entries = AssetDatabase.FindAssets("t:VenueContentEntry").Select(g => AssetDatabase.LoadAssetAtPath<VenueContentEntry>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null && !e.isHome && e.prefab != null).ToList();
            bool noVenues = Environment.GetCommandLineArgs().Contains("-ctlife-novenues");
            foreach (var v in world.venues)
            {
                if (noVenues) break;
                var e = entries.FirstOrDefault(x => x.contentId == v.venueId);
                if (e == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.SetPositionAndRotation(v.Position, v.Rotation);
                StripVenueLook(go);
                IslandVenueTrim.Trim(go.transform, v.localFrame, v.shellHeight > 0f ? 0.5f : 4f, v.shellHeight > 0f ? v.shellHeight + 0.4f : 400f);
                venues++;
            }
            receipt["venues"] = venues;
            // kit instances: edit mode has no OnEnable/LateUpdate, so decode and draw them for the capture camera here
            var sets = Object.FindObjectsByType<IslandInstances>(FindObjectsSortMode.None);
            foreach (var set in sets) { set.Decode(); IslandInstanceRenderer.Register(set); }
            receipt["instanceSets"] = sets.Length;
            receipt["instances"] = sets.Sum(x => x.Count);
            _instanceRenderer = Arg("-ctlife-noinstances") == null && !Environment.GetCommandLineArgs().Contains("-ctlife-noinstances") ? root.GetComponent<IslandInstanceRenderer>() : null;
            var atm = root.GetComponent<IslandAtmosphere>();
            if (Environment.GetCommandLineArgs().Contains("-ctlife-nohaze")) atm.hazeStrength = 0f;
            atm.Apply();
            if (Environment.GetCommandLineArgs().Contains("-ctlife-nofog")) RenderSettings.fog = false;
            if (Environment.GetCommandLineArgs().Contains("-ctlife-noshadows") && atm.Sun != null) atm.Sun.shadows = LightShadows.None;
            if (Environment.GetCommandLineArgs().Contains("-ctlife-tagmats"))
            {
                void Tag(string m, Color c) { var mm = AssetDatabase.LoadAssetAtPath<Material>(IslandV2Paths.Materials + "/" + m + ".mat"); if (mm != null) { mm.SetColor("_Emission", c); mm.SetColor("_BaseColor", c); } }
                Tag("sidewalk", new Color(1f, 0f, 0f)); Tag("kerb", new Color(0f, 0f, 1f)); Tag("concrete", new Color(1f, 1f, 0f));
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-whitesidewalk"))
            {
                var sw = AssetDatabase.LoadAssetAtPath<Material>(IslandV2Paths.Materials + "/sidewalk.mat");
                var tx = sw.GetTexture("_BaseMap") as Texture2D;
                Debug.Log($"CTLIFE [islandv2-capture] sidewalk texture {tx?.name} {tx?.format} {tx?.width}x{tx?.height} mips {tx?.mipmapCount} filter {tx?.filterMode}");
                sw.SetTexture("_BaseMap", Texture2D.whiteTexture);
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-noterrain"))
                foreach (var tc in Object.FindObjectsByType<IslandTerrainCell>(FindObjectsSortMode.None))
                    foreach (var r in tc.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l != atm.Sun) l.enabled = false;
            var camGo = new GameObject("CaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 5200f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = !Environment.GetCommandLineArgs().Contains("-ctlife-nopost");
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            int W = 1780, H = 800;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            var h = IslandV2Height.Load(IslandV2Paths.Out);
            var shots = new JArray();
            var stats = new JObject();
            receipt["renderStats"] = stats;
            foreach (var v in Views)
            {
                if (only != null && !only.Contains(v.Name)) continue;
                var eye = v.Eye;
                if (v.Ground) eye.y = h.Sample(eye.x, eye.z) + v.Eye.y;
                cam.orthographic = false;
                cam.fieldOfView = v.Fov;
                cam.transform.position = eye;
                cam.transform.LookAt(v.Target);
                Render(cam, rt, Path.Combine(outDir, "v2-" + v.Name + ".png"));
                shots.Add(v.Name);
                stats[v.Name] = new JObject
                {
                    ["batches"] = UnityEditor.UnityStats.batches, ["setPass"] = UnityEditor.UnityStats.setPassCalls,
                    ["triangles"] = UnityEditor.UnityStats.triangles, ["kitDraws"] = IslandInstanceRenderer.LastDraws,
                    ["kitInstances"] = IslandInstanceRenderer.LastInstances,
                };
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-venueviews"))
            {
                // every venue from the street, 35 m out from its door and 22 m up (shell and diorama checks)
                foreach (var v in world.venues)
                {
                    var c = v.ToIsland(v.localFrame.center);
                    var door = v.Rotation * Vector3.back;                     // the door side (local -Z)
                    var eye = new Vector3(c.x, 0f, c.y) + door * 34f + Quaternion.Euler(0f, 35f, 0f) * door * 8f;
                    eye.y = h.Sample(eye.x, eye.z) + 22f;
                    cam.orthographic = false; cam.fieldOfView = 55f;
                    cam.transform.position = eye;
                    cam.transform.LookAt(new Vector3(c.x, v.elevation + 3f, c.y));
                    Render(cam, rt, Path.Combine(outDir, "venue-" + v.venueId + ".png"));
                    shots.Add("venue-" + v.venueId);
                }
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-heroviews"))
            {
                // W3: every hero statue and landmark building, from the street side
                var targets = new List<(string name, Vector3 at, float yaw, float dist, float lift)>();
                var dress = IslandV2Paths.Json("dress.json");
                if (dress["heroes"] is JArray hs) foreach (var hj in hs) targets.Add(("hero-" + (string)hj["id"], IslandV2Paths.V3(hj["p"]), (float)hj["yaw"], 9f, 3.2f));
                var planJ = IslandV2Paths.Json("plan.json");
                if (planJ["landmarkBuildings"] is JArray lbs)
                    foreach (var lb in lbs)
                    {
                        var at2 = IslandV2Paths.V2(lb["at"]);
                        targets.Add(("landmark-" + (string)lb["id"], new Vector3(at2.x, (float)lb["y"], at2.y), (float)lb["yaw"], (float)lb["h"] > 10f ? 70f : 34f, (float)lb["h"] > 10f ? (float)lb["h"] * 0.6f + 4f : (float)lb["h"] + 12f));   // small ones from above the street
                    }
                foreach (var (name, at, yaw, dist, lift) in targets)
                {
                    var front = Quaternion.Euler(0f, yaw + 25f, 0f) * Vector3.forward;   // statues face their road; buildings' signs face local -Z
                    if (name.StartsWith("landmark-")) front = Quaternion.Euler(0f, yaw + 25f, 0f) * Vector3.back;
                    var eye = at + front * dist;
                    eye.y = Mathf.Max(h.Sample(eye.x, eye.z) + 1.8f, at.y + lift);
                    cam.orthographic = false; cam.fieldOfView = 50f;
                    cam.transform.position = eye;
                    cam.transform.LookAt(at + Vector3.up * Mathf.Min(lift, 12f) * 0.6f);
                    Render(cam, rt, Path.Combine(outDir, name + ".png"));
                    shots.Add(name);
                }
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-shots"))
            {
                // W4: the authored demo shots (Resources/World/Island/demo_route.json), with a 1.8 m stand-in for the player
                var stand = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(stand.GetComponent<Collider>());
                stand.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
                var standMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.85f, 0.35f, 0.25f) };
                stand.GetComponent<MeshRenderer>().sharedMaterial = standMat;
                Physics.SyncTransforms();
                foreach (var w in IslandWaypoints.Load())
                {
                    float gy = h.Sample(w.x, w.z);
                    if (Physics.Raycast(new Vector3(w.x, 209f, w.z), Vector3.down, out var hit, 400f,   // below the 214 m flight ceiling collider
                         (1 << Layers.Ground) | (1 << Layers.Road))) gy = hit.point.y;
                    var feet = new Vector3(w.x, w.y ?? gy, w.z);
                    stand.transform.position = feet + Vector3.up * 0.9f;
                    var (pos, rot, fov) = IslandWaypoints.CameraPose(w, feet);
                    cam.orthographic = false; cam.fieldOfView = fov;
                    cam.transform.SetPositionAndRotation(pos, rot);
                    Render(cam, rt, Path.Combine(outDir, $"shot-{w.shot}-{w.id}.png"));
                    shots.Add("shot-" + w.id);
                }
                Object.DestroyImmediate(stand);
            }
            if (only == null || only.Contains("topdown"))
            {
                // top-down: the whole island, orthographic, sun overhead-ish for an even read
                cam.orthographic = true;
                cam.transform.position = new Vector3(10f, 900f, 10f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var rt2 = new RenderTexture(2048, 1640, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt2;
                cam.aspect = 2048f / 1640f;
                cam.orthographicSize = 1780f / cam.aspect / 2f;
                Render(cam, rt2, Path.Combine(outDir, "v2-topdown.png"));
                shots.Add("topdown");
                rt2.Release();
            }
            receipt["shots"] = shots;
            atm.Restore();
            rt.Release();
        }

        public static void StripVenueLook(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<VenueLook>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<VenueContentLighting>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Volume>(true)) Object.DestroyImmediate(c.gameObject);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) if (l.type == LightType.Directional) l.enabled = false;
            var b = go.transform.Find("Backdrop");
            if (b != null) b.gameObject.SetActive(false);
        }

        private static IslandInstanceRenderer _instanceRenderer;

        private static void Render(Camera cam, RenderTexture rt, string path)
        {
            if (_instanceRenderer != null)
            {
                IslandInstanceRenderer.TargetCamera = cam;
                _instanceRenderer.Render(cam);
            }
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
