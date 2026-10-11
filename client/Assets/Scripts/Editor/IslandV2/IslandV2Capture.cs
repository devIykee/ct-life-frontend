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
            new View("bay", new Vector3(-30f, 60f, -520f), new Vector3(10f, 0f, -120f), 52f),
            new View("downtown_street", new Vector3(-150f, 1.9f, 30f), new Vector3(120f, 6f, 44f), 58f, true),
            new View("plaza", new Vector3(22f, 34f, -84f), new Vector3(0f, 0f, 8f), 55f),
            new View("ocean_drive", new Vector3(-170f, 2.2f, -226f), new Vector3(60f, 4f, -190f), 58f, true),
            new View("harbour_bridge", new Vector3(-400f, 16f, -486f), new Vector3(-510f, 2f, -400f), 55f),
            new View("trenches", new Vector3(-250f, 30f, -330f), new Vector3(-360f, 0f, -250f), 55f),
            new View("summit_view", new Vector3(-221f, 92f, 455f), new Vector3(0f, 0f, -120f), 55f),
            new View("switchbacks", new Vector3(-40f, 120f, 300f), new Vector3(-180f, 50f, 430f), 55f),
            new View("tunnel", new Vector3(752f, 22f, 196f), new Vector3(782f, 16f, 262f), 55f),
            new View("marina", new Vector3(470f, 46f, -500f), new Vector3(490f, 0f, -320f), 52f),
            new View("farm", new Vector3(520f, 40f, -60f), new Vector3(640f, 8f, 110f), 55f),
            new View("builder_hills", new Vector3(240f, 60f, 140f), new Vector3(380f, 20f, 320f), 55f),
            new View("breakpoint", new Vector3(-150f, 40f, 170f), new Vector3(-300f, 10f, 290f), 55f),
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
            foreach (var v in world.venues)
            {
                var e = entries.FirstOrDefault(x => x.contentId == v.venueId);
                if (e == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.SetPositionAndRotation(v.Position, v.Rotation);
                StripVenueLook(go);
                IslandVenueTrim.Trim(go.transform, v.localFrame);
                venues++;
            }
            receipt["venues"] = venues;
            var atm = root.GetComponent<IslandAtmosphere>();
            atm.Apply();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l != atm.Sun) l.enabled = false;
            var camGo = new GameObject("CaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 5200f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            int W = 1780, H = 800;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            var h = IslandV2Height.Load(IslandV2Paths.Out);
            var shots = new JArray();
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

        private static void Render(Camera cam, RenderTexture rt, string path)
        {
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
