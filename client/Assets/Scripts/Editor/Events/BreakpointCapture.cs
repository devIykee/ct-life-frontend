using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.EditorTools.IslandV2;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.Events
{
    /// <summary>
    /// Look-dev captures of Breakpoint Park and the Conference Center (lane B, 2026-10-24), edit mode: the island base, the
    /// cells within 320 m of the hall, the venue dioramas there and, unless -ctlife-noskin, the London skin prefab
    /// (Resources/World/Events/bp26_london) at the hall's pose with its outdoor props snapped to the ground. Views are named in
    /// the hall's local frame (x right seen from the street, z into the building, y up from the hall floor).
    /// Batch: Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.Events.BreakpointCapture.Run
    ///        -ctlife-captureOut &lt;dir&gt; [-ctlife-views a,b] [-ctlife-noskin] [-ctlife-upcoming]
    /// </summary>
    public static class BreakpointCapture
    {
        private struct View
        {
            public string Name; public Vector3 Eye, Target; public float Fov; public bool Ortho; public float Size;
            public View(string n, Vector3 eye, Vector3 target, float fov = 55f) { Name = n; Eye = eye; Target = target; Fov = fov; Ortho = false; Size = 0f; }
        }

        // hall-local: the door is at z = -10, the street in front at about z = -18, the stage at z = +8
        private static readonly View[] Views =
        {
            new View("top", new Vector3(0f, 160f, -10f), new Vector3(0f, 0f, -9.99f), 60f),
            new View("park_aerial", new Vector3(-40f, 45f, -75f), new Vector3(0f, 0f, -10f), 55f),
            new View("street", new Vector3(-9f, 1.7f, -30f), new Vector3(2f, 3.5f, -10f), 60f),
            new View("plaza", new Vector3(7f, 2.2f, -24f), new Vector3(-2f, 2.5f, -10f), 62f),
            new View("plaza_wide", new Vector3(18f, 8f, -40f), new Vector3(-2f, 2f, -10f), 55f),
            new View("hall_in", new Vector3(0f, 2.6f, -9.2f), new Vector3(0f, 2.0f, 5f), 72f),
            new View("hall_west", new Vector3(4f, 2.2f, -8.5f), new Vector3(-10f, 1.4f, 1f), 70f),
            new View("hall_east", new Vector3(-4f, 2.2f, -8.5f), new Vector3(10f, 1.4f, 1f), 70f),
            new View("stage", new Vector3(0f, 1.8f, -2f), new Vector3(0f, 3.4f, 9.5f), 62f),
            new View("bus", new Vector3(12f, 2.2f, -27f), new Vector3(19.6f, 2.0f, -18.2f), 55f),
            new View("phonebox", new Vector3(-6.5f, 1.7f, -24.5f), new Vector3(-9.8f, 1.4f, -20.4f), 55f),
            new View("booths", new Vector3(-4f, 1.8f, -19f), new Vector3(-8f, 1.5f, -12f), 62f),
            new View("irl", new Vector3(3.5f, 1.8f, -24.5f), new Vector3(6.2f, 1.5f, -19.6f), 55f),
            new View("nod", new Vector3(-15.5f, 1.6f, -18.5f), new Vector3(-17.5f, 1.1f, -16f), 50f),
        };

        public static void Run()
        {
            string fatal = null;
            var outDir = Arg("-ctlife-captureOut") ?? Path.Combine(IslandV2Paths.Repo, "..", "ct-life-evidence", "breakpoint-r2-20261024", "editor");
            Directory.CreateDirectory(outDir);
            var only = Arg("-ctlife-views")?.Split(',').ToHashSet();
            var receipt = new JObject();
            try { Capture(outDir, only, receipt); }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            receipt["fatal"] = fatal;
            File.WriteAllText(Path.Combine(outDir, "bp-capture-receipt.json"), receipt.ToString());
            Debug.Log($"CTLIFE [bp-capture] {(fatal == null ? "PASS" : "FAIL " + fatal)} -> {outDir}");
            if (Application.isBatchMode) EditorApplication.Exit(fatal == null ? 0 : 1);
        }

        private static void Capture(string outDir, HashSet<string> only, JObject receipt)
        {
            var args = Environment.GetCommandLineArgs();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.BasePrefab) ?? throw new InvalidOperationException("island not built");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var world = root.GetComponent<IslandWorld>();
            var hf = world.LoadHeightfield();
            root.GetComponent<IslandTerrainFar>()?.Build(hf);
            var hall = world.Find("conference_hall") ?? throw new InvalidOperationException("no conference_hall on the island");
            var centre = new Vector2(hall.origin.x, hall.origin.y);
            int cells = 0;
            foreach (var c in world.cells)
            {
                var near = new Vector2(Mathf.Clamp(centre.x, c.rect.xMin, c.rect.xMax), Mathf.Clamp(centre.y, c.rect.yMin, c.rect.yMax));
                if (Vector2.Distance(near, centre) > 320f) continue;
                var cp = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.CellsDir + "/" + c.id + ".prefab");
                if (cp == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(cp);
                go.transform.SetParent(root.transform, false);
                go.GetComponentInChildren<IslandTerrainCell>(true)?.BuildNow(hf);
                if (c.far != null) root.transform.Find("Far/" + c.id + "_far")?.gameObject.SetActive(false);
                cells++;
            }
            receipt["cells"] = cells;
            var entries = AssetDatabase.FindAssets("t:VenueContentEntry").Select(g => AssetDatabase.LoadAssetAtPath<VenueContentEntry>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null && !e.isHome && e.prefab != null).ToList();
            foreach (var v in world.venues)
            {
                if (Vector2.Distance(v.origin, centre) > 320f) continue;
                var e = entries.FirstOrDefault(x => x.contentId == v.venueId);
                if (e == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.SetPositionAndRotation(v.Position, v.Rotation);
                IslandV2Capture.StripVenueLook(go);
                IslandVenueTrim.Trim(go.transform, v.localFrame, v.shellHeight > 0f ? 0.5f : 4f, v.shellHeight > 0f ? v.shellHeight + 0.4f : 400f);
                if (v.shellHeight <= 0f && world.venueReskin != null) IslandVenueReskin.Apply(go.transform, world.venueReskin);
            }
            Physics.SyncTransforms();
            var pose = Matrix4x4.TRS(hall.Position, hall.Rotation, Vector3.one);
            receipt["hall"] = $"{hall.Position} yaw {hall.yaw}";
            if (args.Contains("-ctlife-probe"))
            {
                // what stands on the ground in front of and beside the hall (hall-local x, z -> hit y and collider)
                var probe = new JArray();
                for (float z = -8f; z >= -48f; z -= 2f)
                    foreach (float x in new[] { -30f, -20f, -12f, 0f, 12f, 20f, 30f })
                    {
                        var p = pose.MultiplyPoint3x4(new Vector3(x, 30f, z));
                        var hits = Physics.RaycastAll(p, Vector3.down, 80f).OrderBy(hh => hh.distance).ToArray();
                        var top = hits.Length > 0 ? hits[0] : default;
                        probe.Add($"x {x} z {z}: y {(hits.Length > 0 ? (top.point.y - hall.elevation).ToString("F2") : "-")} {(hits.Length > 0 ? top.collider.name : "none")} hf {(hf.Sample(p.x, p.z) - hall.elevation):F2}");
                    }
                receipt["probe"] = probe;
                var near = new JArray();
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    var c = r.bounds.center;
                    var l = pose.inverse.MultiplyPoint3x4(c);
                    if (Mathf.Abs(l.x) > 40f || l.z < -50f || l.z > 25f || r.GetComponentInParent<VenueSceneRoot>() != null && r.bounds.size.magnitude < 0.5f) continue;
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r.GetComponentInParent<EventSkin>() != null) continue;
                    if (r.bounds.size.magnitude > 120f) continue;
                    near.Add($"{r.transform.parent?.name}/{r.name} local {l.x:F1},{l.y:F1},{l.z:F1} size {r.bounds.size.x:F1}x{r.bounds.size.y:F1}x{r.bounds.size.z:F1} mats {string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.name))}");
                }
                receipt["near"] = near;
            }
            if (!args.Contains("-ctlife-noskin"))
            {
                var skin = AssetDatabase.LoadAssetAtPath<GameObject>(EventSkin.PrefabAssetPath);
                if (skin != null)
                {
                    var s = (GameObject)PrefabUtility.InstantiatePrefab(skin);
                    s.transform.SetPositionAndRotation(hall.Position, hall.Rotation);
                    var anchors = s.GetComponent<EventSkin>();
                    anchors.SetLive(!args.Contains("-ctlife-upcoming"));
                    if (anchors.IsLive) receipt["hiddenEveryday"] = EventSkin.HideEveryday(root.transform, hall.Position, hall.venueId, new List<GameObject>());
                    int snapped = anchors.SnapAll(hf != null ? (x, z) => hf.Sample(x, z) : (Func<float, float, float>)null);
                    receipt["skin"] = $"{skin.name}, snapped {snapped}/{anchors.grounded.Length}";
                    receipt["skinRenderers"] = s.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled && r.gameObject.activeInHierarchy);
                    foreach (var tm in s.GetComponentsInChildren<TextMesh>(true)) if (tm.name == "countdown") tm.text = "21d 04:12:09";
                }
                else receipt["skin"] = "not built";
            }
            // the CC's own lights are in the diorama; the island gives the sun and sky
            var atm = root.GetComponent<IslandAtmosphere>();
            atm.Apply();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l != atm.Sun) l.enabled = false;
            var sets = Object.FindObjectsByType<IslandInstances>(FindObjectsSortMode.None);
            foreach (var set in sets) { set.Decode(); IslandInstanceRenderer.Register(set); }
            var kit = root.GetComponent<IslandInstanceRenderer>();
            var cam = new GameObject("CaptureCamera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.farClipPlane = 3000f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var rt = new RenderTexture(1780, 800, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var shots = new JArray();
            // warm-up: the first frame renders before the editor's shaders are ready (black, blown out)
            cam.transform.position = pose.MultiplyPoint3x4(new Vector3(0f, 6f, -40f));
            cam.transform.LookAt(pose.MultiplyPoint3x4(new Vector3(0f, 2f, -10f)));
            for (int i = 0; i < 3; i++) { if (kit != null) { IslandInstanceRenderer.TargetCamera = cam; kit.Render(cam); } cam.Render(); }
            foreach (var v in Views)
            {
                if (only != null && !only.Contains(v.Name)) continue;
                cam.fieldOfView = v.Fov;
                cam.nearClipPlane = v.Name.StartsWith("hall") || v.Name == "stage" ? 0.05f : 0.3f;
                var eye = pose.MultiplyPoint3x4(v.Eye);
                var ground = hf != null ? hf.Sample(eye.x, eye.z) : eye.y;
                if (!v.Name.StartsWith("hall") && v.Name != "stage" && v.Name != "top") eye.y = Mathf.Max(eye.y, ground + v.Eye.y);
                cam.transform.position = eye;
                cam.transform.LookAt(pose.MultiplyPoint3x4(v.Target));
                if (kit != null) { IslandInstanceRenderer.TargetCamera = cam; kit.Render(cam); }
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(outDir, "bp-" + v.Name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                shots.Add(v.Name + " batches " + UnityStats.batches + " tris " + UnityStats.triangles);
            }
            receipt["shots"] = shots;
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
