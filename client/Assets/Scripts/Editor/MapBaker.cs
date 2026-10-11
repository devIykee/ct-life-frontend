using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using CTLife.EditorTools.VenueBuild;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Bakes the in-game map of every shipped venue diorama FROM the level itself (map/HUD job, 2026-10-17):
    /// the venue prefab is rendered straight down by an orthographic camera (venue-local +Z up, +X right), a raycast
    /// heightfield of the same geometry gives edges and heights, and a short CPU pass stylises the render toward the
    /// approved painterly references (soft toon bands, low top-left sun with long soft shadows, warm saturated palette,
    /// lighter paving, richer greenery, blue water, thin dark building outlines, paper grain). Building shapes are the
    /// real geometry, so a marker placed through the recorded transform lands where the thing really is.
    ///
    /// Output: Assets/Resources/UI/Map/Areas/&lt;area&gt;.png and Assets/Resources/UI/Map/areas.json (world-to-map transform
    /// per area: venue-local min corner, size in metres, rotation, pixel size). Batch entry:
    ///   Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.MapBaker.Run
    ///         [-ctlife-mapOnly id,id] [-ctlife-captureOut dir]
    /// </summary>
    public static class MapBaker
    {
        public const string Folder = "Assets/Resources/UI/Map";
        public const string AreasFolder = Folder + "/Areas";
        public const string AreasJson = Folder + "/areas.json";
        public const float PxPerMeter = 20f;
        private const int MaxSide = 1024, Supersample = 2;
        private const float Margin = 5f;
        // Low warm sun from the map's top-left (north-west), 36 degrees up: long soft shadows to the south-east.
        private const float SunElevation = 36f;
        private static readonly List<string> Failures = new List<string>();

        /// <summary>Diagnostics: lists renderers outside the walkable area (name, path, bounds) for one venue.</summary>
        public static void Probe()
        {
            var id = Arg("-ctlife-mapOnly") ?? "terminal_cafe";
            var spec = VenueCatalog.All.First(s => s.id == id);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var venue = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath), scene);
            var walk = WalkBounds(VenueSpotsFile.Load(spec.spotsKey), venue.GetComponent<VenueSceneRoot>());
            foreach (var r in Visible(venue))
            {
                var b = r.bounds;
                string path = r.name; for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                Debug.Log($"CTLIFE [mapprobe] {path} center={b.center} size={b.size} inWalk={walk.Contains(new Vector2(b.center.x, b.center.z))} mesh={(r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh?.name : "-")}");
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void Run()
        {
            string fatal = null;
            var receipt = new JObject();
            string evidence = Arg("-ctlife-captureOut");
            try
            {
                if (!Services.TryGet<ConfigStore>(out var config)) { config = Services.Register(new ConfigStore()); config.LoadLocal(); }
                var only = (Arg("-ctlife-mapOnly") ?? "").Split(',').Where(s => s.Length > 0).ToHashSet();
                Directory.CreateDirectory(AreasFolder);
                var doc = File.Exists(AreasJson) ? JObject.Parse(File.ReadAllText(AreasJson)) : new JObject();
                var areas = doc["areas"] as JArray ?? new JArray();
                using (new BakeQuality())
                {
                    foreach (var spec in VenueCatalog.All)
                    {
                        if (only.Count > 0 && !only.Contains(spec.id)) continue;
                        if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) == null) { receipt[spec.id] = "no prefab"; continue; }
                        try
                        {
                            var area = Bake(spec, evidence);
                            for (int i = areas.Count - 1; i >= 0; i--) if ((string)areas[i]["id"] == spec.id) areas.RemoveAt(i);
                            areas.Add(area);
                            receipt[spec.id] = area;
                        }
                        catch (Exception e) { Failures.Add(spec.id + ": " + e.Message); Debug.LogException(e); }
                    }
                }
                doc["version"] = 1;
                doc["note"] = "Baked by MapBaker from the venue prefabs. Coordinates are venue-local metres (x right/east, z up/north). " +
                              "uv = (local - min) / size; rotation (degrees, counter-clockwise) is applied about the area centre.";
                doc["pxPerMeter"] = PxPerMeter;
                doc["areas"] = new JArray(areas.OrderBy(a => (string)a["id"], StringComparer.Ordinal));
                File.WriteAllText(AreasJson, doc.ToString());
                AssetDatabase.Refresh();
                AtlasBaker.Bake(); // the atlas places every area map, so it follows each bake
                if (evidence != null) File.Copy(AtlasBaker.AtlasPng, Path.Combine(evidence, "atlas-ct-island.png"), true);
            }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            receipt["fatal"] = fatal;
            receipt["failures"] = new JArray(Failures);
            if (evidence != null) { Directory.CreateDirectory(evidence); File.WriteAllText(Path.Combine(evidence, "map-bake-receipt.json"), receipt.ToString()); }
            bool pass = fatal == null && Failures.Count == 0;
            Debug.Log($"CTLIFE [mapbake] {(pass ? "PASS" : "FAIL")} fatal={fatal} failures={Failures.Count}");
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
        }

        /// <summary>Phone URP settings are raised in memory for the bake only (render scale 1, soft main-light shadows); restored after.</summary>
        private sealed class BakeQuality : IDisposable
        {
            private readonly UniversalRenderPipelineAsset _urp;
            private readonly float _scale, _shadow;
            private readonly bool _shadows;
            private static readonly System.Reflection.FieldInfo ShadowsField = typeof(UniversalRenderPipelineAsset)
                .GetField("m_MainLightShadowsSupported", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            public BakeQuality()
            {
                _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (_urp == null) return;
                _scale = _urp.renderScale; _shadow = _urp.shadowDistance;
                _shadows = ShadowsField != null && (bool)ShadowsField.GetValue(_urp);
                _urp.renderScale = 1f; _urp.shadowDistance = 140f;
                ShadowsField?.SetValue(_urp, true);
            }

            public void Dispose()
            {
                if (_urp == null) return;
                _urp.renderScale = _scale; _urp.shadowDistance = _shadow;
                ShadowsField?.SetValue(_urp, _shadows);
            }
        }

        // ------------------------------------------------------------------ one area

        private sealed class Field
        {
            public int W, H;
            public float[] Height;     // world Y of the first hit, NaN where nothing
            public int[] Id;           // collider instance id (object identity for edges)
            public float Floor;
        }

        private static JObject Bake(VenueSpec spec, string evidence)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var venue = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            venue.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var root = venue.GetComponent<VenueSceneRoot>();
            var spots = VenueSpotsFile.Load(spec.spotsKey);
            root?.Init(spots);
            var lighting = venue.GetComponent<VenueContentLighting>();
            try
            {
                lighting?.Apply();
                var renderers = Visible(venue);
                if (renderers.Count == 0) throw new InvalidOperationException("no visible renderers");
                var all = renderers.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });

                // The map frame: the walkable area (what the player can reach) plus a margin of surroundings, kept inside the art.
                Rect walk = WalkBounds(spots, root);
                var frame = Rect.MinMaxRect(walk.xMin - Margin, walk.yMin - Margin, walk.xMax + Margin, walk.yMax + Margin);
                frame = Rect.MinMaxRect(Mathf.Max(frame.xMin, all.min.x), Mathf.Max(frame.yMin, all.min.z),
                    Mathf.Min(frame.xMax, all.max.x), Mathf.Min(frame.yMax, all.max.z));
                frame = Rect.MinMaxRect(Mathf.Min(frame.xMin, walk.xMin - 1f), Mathf.Min(frame.yMin, walk.yMin - 1f),
                    Mathf.Max(frame.xMax, walk.xMax + 1f), Mathf.Max(frame.yMax, walk.yMax + 1f));
                float ppm = Mathf.Min(PxPerMeter, MaxSide / Mathf.Max(frame.width, frame.height));
                int W = Mathf.Max(64, Mathf.RoundToInt(frame.width * ppm / 4f) * 4), H = Mathf.Max(64, Mathf.RoundToInt(frame.height * ppm / 4f) * 4);
                // Snap the frame to the pixel grid so 1 px is exactly 1/ppm metres both ways.
                frame = new Rect(frame.center.x - W / ppm / 2f, frame.center.y - H / ppm / 2f, W / ppm, H / ppm);

                // Colliders that match what is drawn: every visible mesh, nothing invisible.
                foreach (var c in venue.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                var added = new List<Collider>();
                foreach (var r in renderers)
                    if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                    {
                        var mc = r.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        added.Add(mc);
                    }
                Physics.SyncTransforms();

                float top = all.max.y + 2f, bottom = all.min.y - 2f;
                var probe = HeightField(frame, Mathf.Max(16, W / 8), Mathf.Max(16, H / 8), top, bottom);
                float floor = Floor(probe, frame, walk);

                // Interiors: anything that roofs the walkable floor (ceilings, canopies covering it) is lifted off the bake.
                bool indoor = false;
                var cutRenderers = new List<string>();
                foreach (var r in renderers)
                {
                    var b = r.bounds;
                    float cover = Overlap(walk, Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z)) / Mathf.Max(1f, walk.width * walk.height);
                    if (b.min.y > floor + 2.4f && cover > 0.22f)
                    {
                        r.enabled = false; indoor = true; cutRenderers.Add(r.name);
                        foreach (var mc in r.GetComponents<MeshCollider>()) mc.enabled = false;
                    }
                }
                Physics.SyncTransforms();
                var field = HeightField(frame, W, H, top, bottom);
                field.Floor = floor;

                var color = RenderTop(frame, W, H, top, bottom);
                if (evidence != null)
                {
                    Directory.CreateDirectory(Path.Combine(evidence, "bake"));
                    SavePng(color, W, H, Path.Combine(evidence, "bake", spec.id + "-raw.png"));
                }
                var styled = Stylise(color, field, ppm);
                string png = AreasFolder + "/" + spec.id + ".png";
                SavePng(styled, W, H, png);
                if (evidence != null) File.Copy(png, Path.Combine(evidence, "bake", spec.id + "-map.png"), true);
                foreach (var c in added) Object.DestroyImmediate(c);

                return new JObject
                {
                    ["id"] = spec.id, ["venueId"] = spec.venueId, ["homeId"] = spec.homeId, ["district"] = spec.district, ["title"] = spec.title,
                    ["texture"] = "UI/Map/Areas/" + spec.id,
                    ["min"] = new JArray(R(frame.xMin), R(frame.yMin)), ["size"] = new JArray(R(frame.width), R(frame.height)),
                    ["rotation"] = 0, ["pixels"] = new JArray(W, H), ["pxPerMeter"] = R(ppm),
                    ["walkable"] = new JArray(R(walk.xMin), R(walk.yMin), R(walk.xMax), R(walk.yMax)),
                    ["floorY"] = R(floor), ["indoor"] = indoor, ["roofsLifted"] = new JArray(cutRenderers.Take(12)),
                };
            }
            finally
            {
                lighting?.Restore();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static float R(float v) => (float)Math.Round(v, 3);

        /// <summary>Renderers that draw in the game: enabled, not shadow-only, LOD 0 only.</summary>
        private static List<Renderer> Visible(GameObject venue)
        {
            var skip = new HashSet<Renderer>();
            foreach (var lod in venue.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lod.GetLODs();
                for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) skip.Add(r);
            }
            return venue.GetComponentsInChildren<Renderer>(false)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !skip.Contains(r) && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly
                    && (r is MeshRenderer) && !(r is ParticleSystemRenderer)
                    && !r.name.StartsWith("Glow", StringComparison.Ordinal)) // additive fake-glow cards are lighting, not things
                // Small isolated backdrop props (trees/houses seen through windows) read as floating blobs from above.
                .Where(r => !(InBackdrop(r.transform) && r.bounds.size.x * r.bounds.size.z < 8f))
                .ToList();
        }

        private static bool InBackdrop(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name == "Backdrop") return true;
            return false;
        }

        private static Rect WalkBounds(VenueSpots spots, VenueSceneRoot root)
        {
            if (spots != null && spots.walkable.Count > 0)
                return Rect.MinMaxRect(spots.walkable.Min(w => Mathf.Min(w.x0, w.x1)), spots.walkable.Min(w => Mathf.Min(w.z0, w.z1)),
                    spots.walkable.Max(w => Mathf.Max(w.x0, w.x1)), spots.walkable.Max(w => Mathf.Max(w.z0, w.z1)));
            return root != null ? root.cameraBounds : new Rect(-12, -10, 24, 20);
        }

        private static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0 && h > 0 ? w * h : 0f;
        }

        private static float Floor(Field probe, Rect frame, Rect walk)
        {
            var hs = new List<float>();
            for (int y = 0; y < probe.H; y++)
                for (int x = 0; x < probe.W; x++)
                {
                    float wx = frame.xMin + (x + 0.5f) / probe.W * frame.width, wz = frame.yMin + (y + 0.5f) / probe.H * frame.height;
                    float h = probe.Height[y * probe.W + x];
                    if (walk.Contains(new Vector2(wx, wz)) && !float.IsNaN(h)) hs.Add(h);
                }
            if (hs.Count == 0) return 0f;
            hs.Sort();
            return hs[hs.Count / 4]; // the lower quartile: the floor, not the furniture on it
        }

        private static Field HeightField(Rect frame, int W, int H, float top, float bottom)
        {
            var f = new Field { W = W, H = H, Height = new float[W * H], Id = new int[W * H] };
            var cmds = new NativeArray<RaycastCommand>(W * H, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(W * H, Allocator.TempJob);
            try
            {
                var q = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, false);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float wx = frame.xMin + (x + 0.5f) / W * frame.width, wz = frame.yMin + (y + 0.5f) / H * frame.height;
                        cmds[y * W + x] = new RaycastCommand(new Vector3(wx, top, wz), Vector3.down, q, top - bottom);
                    }
                RaycastCommand.ScheduleBatch(cmds, hits, 512, 1).Complete();
                for (int i = 0; i < W * H; i++)
                {
                    var h = hits[i];
                    bool hit = h.colliderInstanceID != 0;
                    f.Height[i] = hit ? h.point.y : float.NaN;
                    f.Id[i] = hit ? h.colliderInstanceID : 0;
                }
            }
            finally { cmds.Dispose(); hits.Dispose(); }
            return f;
        }

        // ------------------------------------------------------------------ the top-down render

        private static Color[] RenderTop(Rect frame, int W, int H, float top, float bottom)
        {
            var sunGo = new GameObject("Map sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            var flat = new Vector3(1f, 0f, -1f).normalized; // from the top-left (north-west) toward the bottom-right
            float e = SunElevation * Mathf.Deg2Rad;
            sun.transform.rotation = Quaternion.LookRotation(flat * Mathf.Cos(e) + Vector3.down * Mathf.Sin(e), Vector3.up);
            sun.color = new Color(1f, 0.9f, 0.76f);
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.None; // shadows come from the heightfield (CPU), softer and exactly aligned
            sun.shadowStrength = 0.62f;
            sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.80f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.80f, 0.70f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.36f, 0.30f);
            RenderSettings.fog = false;
            // Disable venue point/spot/extra lights' realtime contribution so the bake reads as daylight.
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l != sun) l.enabled = false;

            var camGo = new GameObject("Map camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = frame.height / 2f;
            cam.aspect = frame.width / frame.height;
            cam.transform.SetPositionAndRotation(new Vector3(frame.center.x, top + 1f, frame.center.y), Quaternion.Euler(90f, 0f, 0f));
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = top - bottom + 4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;

            int sw = W * Supersample, sh = H * Supersample;
            var rt = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var down = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32);
            rt.Create(); down.Create();
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                for (int i = 0; i < 3; i++) cam.Render();
                cam.targetTexture = null;
                Graphics.Blit(rt, down);
                RenderTexture.active = down;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                return tex.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                rt.Release(); down.Release();
                Object.DestroyImmediate(rt); Object.DestroyImmediate(down); Object.DestroyImmediate(tex);
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(sunGo);
            }
        }

        // ------------------------------------------------------------------ stylise

        /// <summary>The painterly pass (CPU, deterministic). Exposed for tests through <see cref="StyleColor"/>.</summary>
        private static Color[] Stylise(Color[] src, Field f, float ppm)
        {
            int W = f.W, H = f.H;
            var o = new Color[W * H];
            float edgeStep = 0.32f;           // metres of height change that reads as a building/object edge
            src = FlattenFloors(src, f, Mathf.Max(4, Mathf.RoundToInt(ppm * 1.1f)));
            var shadow = Shadows(f, ppm);
            var shade = HillShade(f, ppm);
            int ao = Mathf.Max(2, Mathf.RoundToInt(ppm * 0.45f));
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float h = f.Height[i];
                    var c = src[i];
                    if (float.IsNaN(h) || c.a < 0.02f) { o[i] = new Color(0, 0, 0, 0); continue; }
                    float rel = h - f.Floor;
                    c = StyleColor(c, rel);
                    // Gentle top-left light on slopes and tops, then the long soft cast shadow (cool, the painted-map way).
                    c = new Color(Mathf.Clamp01(c.r * shade[i]), Mathf.Clamp01(c.g * shade[i]), Mathf.Clamp01(c.b * shade[i]), 1f);
                    c = Color.Lerp(c, new Color(c.r * 0.62f, c.g * 0.60f, c.b * 0.74f, 1f), shadow[i] * 0.62f);

                    // Thin dark outline on the higher side of a height step (buildings, furniture tops, walls).
                    float maxDrop = 0f; bool idEdge = false;
                    for (int k = 0; k < 8; k++)
                    {
                        int r = k < 4 ? 1 : 2;
                        int nx = x + (k % 4 == 0 ? r : k % 4 == 1 ? -r : 0), ny = y + (k % 4 == 2 ? r : k % 4 == 3 ? -r : 0);
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        float nh = f.Height[ny * W + nx];
                        if (float.IsNaN(nh)) { maxDrop = Mathf.Max(maxDrop, 1f); continue; }
                        maxDrop = Mathf.Max(maxDrop, h - nh);
                        if (r == 1 && f.Id[ny * W + nx] != f.Id[i] && Mathf.Abs(h - nh) > 0.12f) idEdge = true;
                    }
                    if (maxDrop > edgeStep) c = Color.Lerp(c, Ink, rel > 1.2f ? 0.78f : 0.6f);
                    else if (idEdge) c = Color.Lerp(c, Ink, 0.32f);

                    // Soft contact shading at the foot of tall things (cheap AO from the heightfield).
                    if (rel < 0.35f)
                    {
                        float occ = 0f;
                        for (int k = 1; k <= ao; k += Mathf.Max(1, ao / 4))
                            for (int d = 0; d < 4; d++)
                            {
                                int nx = x + (d == 0 ? k : d == 1 ? -k : 0), ny = y + (d == 2 ? k : d == 3 ? -k : 0);
                                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                                float nh = f.Height[ny * W + nx];
                                if (!float.IsNaN(nh) && nh - h > 0.6f) occ += 1f / (1f + k);
                            }
                        c = Color.Lerp(c, c * new Color(0.78f, 0.72f, 0.80f, 1f), Mathf.Clamp01(occ * 0.35f));
                    }

                    // Paper grain: fine fibre noise plus a large soft wash.
                    float n = Hash(x, y) * 0.035f - 0.0175f + (Noise(x / 37f, y / 37f) - 0.5f) * 0.05f;
                    c = new Color(Mathf.Clamp01(c.r + n), Mathf.Clamp01(c.g + n), Mathf.Clamp01(c.b + n * 0.8f), 1f);
                    o[i] = c;
                }
            Feather(o, W, H, Mathf.RoundToInt(ppm * 0.9f));
            return o;
        }

        public static readonly Color Ink = new Color(0.19f, 0.10f, 0.16f, 1f);

        /// <summary>
        /// Cast shadow from the heightfield: march toward the low north-west sun; a pixel is shadowed when the terrain rises
        /// above the sun ray. Soft: graded by how deep the occluder reaches above the ray, then box-blurred.
        /// </summary>
        private static float[] Shadows(Field f, float ppm)
        {
            int W = f.W, H = f.H;
            var s = new float[W * H];
            float tan = Mathf.Tan(SunElevation * Mathf.Deg2Rad);
            float stepM = 1.41421f / ppm;      // one diagonal pixel in metres
            int steps = Mathf.RoundToInt(9f * ppm / 1.41421f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float h = f.Height[y * W + x];
                    if (float.IsNaN(h)) continue;
                    float best = 0f;
                    for (int k = 1; k <= steps; k++)
                    {
                        int nx = x - k, ny = y + k;     // toward the top-left (north-west) in image space (rows grow north)
                        if (nx < 0 || ny >= H) break;
                        float nh = f.Height[ny * W + nx];
                        if (float.IsNaN(nh)) continue;
                        float above = nh - (h + k * stepM * tan);
                        if (above > 0f) { best = Mathf.Max(best, Mathf.Clamp01(above / 0.35f)); if (best >= 1f) break; }
                    }
                    s[y * W + x] = best;
                }
            return Blur(s, W, H, Mathf.Max(1, Mathf.RoundToInt(ppm * 0.18f)));
        }

        /// <summary>Lambert-ish shade from heightfield normals under the same sun: 0.86..1.08, flat ground is 1.</summary>
        private static float[] HillShade(Field f, float ppm)
        {
            int W = f.W, H = f.H;
            var o = new float[W * H];
            var sun = new Vector3(-1f, 0f, 1f).normalized * Mathf.Cos(SunElevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(SunElevation * Mathf.Deg2Rad);
            float flat = Vector3.Dot(Vector3.up, sun);
            float px = 1f / ppm;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float h = f.Height[i];
                    if (float.IsNaN(h)) { o[i] = 1f; continue; }
                    float hx0 = Sample(f, x - 1, y, h), hx1 = Sample(f, x + 1, y, h), hy0 = Sample(f, x, y - 1, h), hy1 = Sample(f, x, y + 1, h);
                    // Clamp steps so walls read as edges (outline) rather than black slopes.
                    float dx = Mathf.Clamp((hx1 - hx0) / (2f * px), -1.5f, 1.5f), dz = Mathf.Clamp((hy1 - hy0) / (2f * px), -1.5f, 1.5f);
                    var n = new Vector3(-dx, 1f, -dz).normalized;
                    o[i] = Mathf.Clamp(1f + (Vector3.Dot(n, sun) - flat) * 0.55f, 0.84f, 1.1f);
                }
            return o;
        }

        /// <summary>
        /// Painterly floors: texture detail on floor-level surfaces (wood grain, paving noise) is averaged within the same object
        /// (id-aware and colour-aware, so rugs, floor and painted zones keep their edges), leaving flat fills with a trace (14%) of the texture.
        /// </summary>
        private static Color[] FlattenFloors(Color[] src, Field f, int r)
        {
            int W = f.W, H = f.H;
            bool Floorish(int i) => !float.IsNaN(f.Height[i]); // every surface: roofs and floors alike lose tiling noise; objects keep detail via the colour gate
            var tmp = new Color[W * H];
            var o = (Color[])src.Clone();
            for (int pass = 0; pass < 2; pass++)
            {
                var from = pass == 0 ? src : tmp;
                var to = pass == 0 ? tmp : o;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (!Floorish(i)) { to[i] = from[i]; continue; }
                        Color sum = Color.clear; int n = 0;
                        for (int k = -r; k <= r; k++)
                        {
                            int nx = pass == 0 ? x + k : x, ny = pass == 0 ? y : y + k;
                            if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                            int j = ny * W + nx;
                            if (f.Id[j] != f.Id[i] || !Floorish(j)) continue;
                            var dc = src[j] - src[i];
                            if (dc.r * dc.r + dc.g * dc.g + dc.b * dc.b > 0.045f) continue; // bilateral: keep painted zones apart
                            sum += from[j]; n++;
                        }
                        to[i] = n > 0 ? sum / n : from[i];
                    }
            }
            for (int i = 0; i < W * H; i++) if (Floorish(i)) o[i] = Color.Lerp(o[i], src[i], 0.14f);
            return o;
        }

        private static float Sample(Field f, int x, int y, float fallback)
        {
            if (x < 0 || y < 0 || x >= f.W || y >= f.H) return fallback;
            float v = f.Height[y * f.W + x];
            return float.IsNaN(v) ? fallback : v;
        }

        private static float[] Blur(float[] a, int W, int H, int r)
        {
            var t = new float[W * H];
            var o = new float[W * H];
            for (int y = 0; y < H; y++)
            {
                float sum = 0; int n = 0;
                for (int x = -r; x < W + r; x++)
                {
                    if (x + r < W) { sum += a[y * W + x + r]; n++; }
                    if (x - r - 1 >= 0) { sum -= a[y * W + x - r - 1]; n--; }
                    if (x >= 0 && x < W) t[y * W + x] = sum / Mathf.Max(1, n);
                }
            }
            for (int x = 0; x < W; x++)
            {
                float sum = 0; int n = 0;
                for (int y = -r; y < H + r; y++)
                {
                    if (y + r < H) { sum += t[(y + r) * W + x]; n++; }
                    if (y - r - 1 >= 0) { sum -= t[(y - r - 1) * W + x]; n--; }
                    if (y >= 0 && y < H) o[y * W + x] = sum / Mathf.Max(1, n);
                }
            }
            return o;
        }

        /// <summary>
        /// Per-pixel palette shaping: warm grade and saturation, soft toon banding, lighter paving, richer greenery, bluer water.
        /// <paramref name="relHeight"/> is metres above the venue floor.
        /// </summary>
        public static Color StyleColor(Color c, float relHeight)
        {
            Color.RGBToHSV(c, out float hue, out float s, out float v);
            float deg = hue * 360f;
            bool water = deg >= 182f && deg <= 212f && s > 0.32f && relHeight < 1.4f;
            bool green = deg >= 62f && deg <= 165f && s > 0.18f;
            bool paving = relHeight < 0.3f && s < 0.32f && v > 0.35f && !green && !water;
            s = Mathf.Clamp01(s * 1.18f + 0.02f);
            if (green) { s = Mathf.Clamp01(s * 1.22f + 0.04f); v = Mathf.Clamp01(v * 0.97f + 0.02f); hue = Mathf.Repeat((deg - 4f) / 360f, 1f); }
            if (water) { hue = 0.548f; s = Mathf.Clamp(s * 1.1f, 0.45f, 0.8f); v = Mathf.Clamp(v * 1.08f, 0.55f, 0.95f); }
            if (paving) { v = Mathf.Lerp(v, 1f, 0.28f); s *= 0.8f; }
            v = Mathf.Clamp01(v * 1.07f + 0.035f); // daylight lift: interiors read bright like the game, not like a cellar
            // Soft toon: pull value toward 6 bands (35%), keeping gradients readable.
            float band = Mathf.Round(v * 6f) / 6f;
            v = Mathf.Lerp(v, band, 0.35f);
            var o = Color.HSVToRGB(hue, s, v);
            // Warm the whole palette slightly (the plaza's golden hour).
            o = new Color(Mathf.Clamp01(o.r * 1.035f + 0.01f), Mathf.Clamp01(o.g * 1.005f), Mathf.Clamp01(o.b * 0.95f), 1f);
            return o;
        }

        private static void Feather(Color[] o, int W, int H, int px)
        {
            if (px <= 0) return;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                    if (d >= px) continue;
                    var c = o[y * W + x];
                    c.a *= Mathf.SmoothStep(0f, 1f, d / (float)px);
                    o[y * W + x] = c;
                }
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        private static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float tx = x - xi, ty = y - yi;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        internal static void SavePng(Color[] px, int W, int H, string path)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels(px);
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(tex); }
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
