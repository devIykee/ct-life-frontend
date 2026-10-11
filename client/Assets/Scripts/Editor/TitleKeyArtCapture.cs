using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.EditorTools.VenueBuild;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Title-screen key-art plates rendered in the real engine: the shipped Timeline Square diorama, real cast looks from the
    /// CharacterRegistry, real AvatarController poses (sampled at a chosen phase), a golden-hour sun and URP post (depth of
    /// field, bloom, vignette, warm grade). No UI, logo or text. Edit mode, no play mode; nothing in the scene is saved.
    /// Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.TitleKeyArtCapture.Run
    ///       [-ctlife-keyart /abs/plates.json] [-ctlife-captureOut /abs/dir] [-ctlife-keyartOnly A,B]
    /// (default plates: client/Tools~/keyart/title-plates-20261016.json, the three chosen compositions)
    /// (no -nographics). The JSON holds the light, the three compositions and their cast, so framing iterates without
    /// recompiling. Writes plate-{name}.png at 2670x1200 and keyart-receipt.json (per-actor look, state, foot/ground gap).
    /// </summary>
    public static class TitleKeyArtCapture
    {
        private const int Width = 2670, Height = 1200;
        private static readonly List<string> Failures = new List<string>();

        /// <summary>
        /// The shipped URP asset is tuned for phones (render scale 0.7, no HDR, no depth texture, no realtime shadows).
        /// Key art raises those in memory for the capture only and puts every value back before exit (never saved). MSAA is
        /// left alone: its setter writes QualitySettings (a ProjectSettings file); the render target carries its own 4x MSAA.
        /// </summary>
        private sealed class OfflineQuality : IDisposable
        {
            private readonly UniversalRenderPipelineAsset _urp;
            private readonly float _scale, _shadow;
            private readonly bool _hdr, _depth, _shadows;
            private static readonly System.Reflection.FieldInfo ShadowsField = typeof(UniversalRenderPipelineAsset)
                .GetField("m_MainLightShadowsSupported", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            public OfflineQuality(float shadowDistance)
            {
                _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (_urp == null) return;
                _scale = _urp.renderScale; _hdr = _urp.supportsHDR; _depth = _urp.supportsCameraDepthTexture;
                _shadow = _urp.shadowDistance; _shadows = ShadowsField != null && (bool)ShadowsField.GetValue(_urp);
                _urp.renderScale = 1f; _urp.supportsHDR = true; _urp.supportsCameraDepthTexture = true;
                _urp.shadowDistance = shadowDistance;
                ShadowsField?.SetValue(_urp, true);
            }

            public void Dispose()
            {
                if (_urp == null) return;
                _urp.renderScale = _scale; _urp.supportsHDR = _hdr; _urp.supportsCameraDepthTexture = _depth;
                _urp.shadowDistance = _shadow;
                ShadowsField?.SetValue(_urp, _shadows);
            }
        }

        public static void Run()
        {
            string fatal = null;
            var receipt = new JObject();
            string outDir = Arg("-ctlife-captureOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../ct-life-evidence/title-keyart-20261016"));
            try
            {
                var paramsPath = Arg("-ctlife-keyart") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools~/keyart/title-plates-20261016.json"));
                var doc = JObject.Parse(File.ReadAllText(paramsPath));
                Directory.CreateDirectory(outDir);
                if (!Services.TryGet<ConfigStore>(out var config))
                {
                    config = Services.Register(new ConfigStore());
                    config.LoadLocal();
                }
                var only = (Arg("-ctlife-keyartOnly") ?? "").Split(',').Where(s => s.Length > 0).ToHashSet();
                using (new OfflineQuality((float?)doc["light"]?["shadowDistance"] ?? 30f))
                {
                    foreach (JObject plate in (JArray)doc["plates"])
                    {
                        var name = (string)plate["name"];
                        if (only.Count > 0 && !only.Contains(name)) continue;
                        receipt[name] = RenderPlate(doc, plate, Path.Combine(outDir, (string)plate["file"] ?? "plate-" + name + ".png"));
                    }
                    if (doc["survey"] is JArray survey) receipt["survey"] = Survey(doc, survey, outDir);
                }
            }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            receipt["fatal"] = fatal;
            receipt["failures"] = new JArray(Failures);
            try { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "keyart-receipt.json"), receipt.ToString()); }
            catch (Exception e) { Debug.LogException(e); }
            bool pass = fatal == null && Failures.Count == 0;
            Debug.Log($"CTLIFE [keyart] {(pass ? "PASS" : "FAIL")} fatal={fatal} failures={Failures.Count}");
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
        }

        // ------------------------------------------------------------------ scene

        private sealed class Stage : IDisposable
        {
            public GameObject Venue;
            public Camera Camera;
            public Light Sun;
            public Volume Volume;
            public readonly List<GameObject> Owned = new List<GameObject>();
            public void Dispose()
            {
                if (Venue != null)
                {
                    Venue.GetComponent<VenueLook>()?.Restore();
                    Venue.GetComponent<VenueLightmaps>()?.Restore();
                    Venue.GetComponent<VenueContentLighting>()?.Restore();
                }
                if (Volume != null && Volume.sharedProfile != null) Object.DestroyImmediate(Volume.sharedProfile);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static Stage Build(JObject doc)
        {
            var spec = VenueCatalog.All.First(s => s.id == ((string)doc["venue"] ?? "timeline_square"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) ?? throw new FileNotFoundException(spec.PrefabPath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var st = new Stage { Venue = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene) };
            var root = st.Venue.GetComponent<VenueSceneRoot>();
            root.Init(VenueSpotsFile.Load(spec.spotsKey));
            var light = (JObject)doc["light"] ?? new JObject();
            var maps = st.Venue.GetComponent<VenueLightmaps>();
            if ((bool?)light["lightmaps"] == true && maps != null && maps.colors != null && maps.colors.Length > 0) maps.Apply();
            st.Venue.GetComponent<VenueContentLighting>()?.Apply();

            // Golden-hour key light: low, warm, long soft shadows.
            st.Sun = RenderSettings.sun;
            if (st.Sun == null)
            {
                st.Sun = new GameObject("Key sun").AddComponent<Light>();
                st.Sun.type = LightType.Directional;
                RenderSettings.sun = st.Sun;
            }
            st.Sun.transform.rotation = Quaternion.Euler(V3(light["sunEuler"], new Vector3(16f, -55f, 0f)));
            st.Sun.color = C(light["sunColor"], new Color(1f, 0.74f, 0.48f));
            st.Sun.intensity = (float?)light["sunIntensity"] ?? 2.1f;
            st.Sun.shadows = LightShadows.Soft;
            st.Sun.shadowStrength = (float?)light["shadowStrength"] ?? 0.8f;
            // Soft warm fill from the camera side so faces read against the low back-light (key-art convention).
            if (light["fill"] is JObject fill)
            {
                var f = new GameObject("Key fill").AddComponent<Light>();
                f.type = LightType.Directional;
                f.transform.rotation = Quaternion.Euler(V3(fill["euler"], new Vector3(12f, 20f, 0f)));
                f.color = C(fill["color"], new Color(1f, 0.86f, 0.74f));
                f.intensity = (float?)fill["intensity"] ?? 0.35f;
                f.shadows = LightShadows.None;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = C(light["sky"], new Color(0.62f, 0.66f, 0.80f));
            RenderSettings.ambientEquatorColor = C(light["equator"], new Color(0.78f, 0.62f, 0.50f));
            RenderSettings.ambientGroundColor = C(light["ground"], new Color(0.40f, 0.30f, 0.24f));
            RenderSettings.fog = (bool?)light["fog"] ?? true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = C(light["fogColor"], new Color(0.98f, 0.80f, 0.62f));
            RenderSettings.fogStartDistance = (float?)light["fogStart"] ?? 18f;
            RenderSettings.fogEndDistance = (float?)light["fogEnd"] ?? 90f;

            st.Camera = new GameObject("Key art camera").AddComponent<Camera>();
            st.Camera.clearFlags = CameraClearFlags.SolidColor;
            st.Camera.backgroundColor = C(light["skyClear"], new Color(0.98f, 0.78f, 0.58f));
            st.Camera.nearClipPlane = 0.08f;
            st.Camera.farClipPlane = 250f;
            st.Camera.aspect = Width / (float)Height;
            st.Venue.GetComponent<VenueLook>()?.Apply(st.Camera);
            var data = st.Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            if (light["skyColor"] != null) st.Camera.backgroundColor = C(light["skyColor"], st.Camera.backgroundColor);

            var vol = new GameObject("Key art volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1000;
            vol.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            st.Volume = vol;
            return st;
        }

        private static void Post(Stage st, JObject plate, JObject doc)
        {
            var p = st.Volume.sharedProfile;
            foreach (var c in p.components.ToList()) p.Remove(c.GetType());
            var post = (JObject)plate["post"] ?? new JObject();
            var dof = p.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override((float?)post["focus"] ?? 4f);
            dof.aperture.Override((float?)post["aperture"] ?? 2.2f);
            dof.focalLength.Override((float?)post["focal"] ?? 60f);
            dof.bladeCount.Override(6);
            var bloom = p.Add<Bloom>(true);
            bloom.intensity.Override((float?)post["bloom"] ?? 0.55f);
            bloom.threshold.Override((float?)post["bloomThreshold"] ?? 0.85f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.72f));
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override((float?)post["vignette"] ?? 0.26f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(new Color(0.16f, 0.07f, 0.04f));
            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var wb = p.Add<WhiteBalance>(true);
            wb.temperature.Override((float?)post["temperature"] ?? 14f);
            wb.tint.Override((float?)post["tint"] ?? 4f);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override((float?)post["exposure"] ?? 0.1f);
            ca.contrast.Override((float?)post["contrast"] ?? 8f);
            ca.saturation.Override((float?)post["saturation"] ?? 8f);
            var split = p.Add<SplitToning>(true);
            split.shadows.Override(new Color(0.40f, 0.46f, 0.62f));
            split.highlights.Override(new Color(1f, 0.82f, 0.62f));
            split.balance.Override(-12f);
        }

        // ------------------------------------------------------------------ plate

        private static JObject RenderPlate(JObject doc, JObject plate, string path)
        {
            var name = (string)plate["name"];
            var result = new JObject { ["file"] = path };
            using (var st = Build(doc))
            {
                Post(st, plate, doc);
                var actors = new JArray();
                foreach (JObject a in (JArray)plate["actors"] ?? new JArray()) actors.Add(Spawn(st, a, name));
                result["actors"] = actors;
                if (plate["props"] is JArray props) foreach (JObject pr in props) Prop(st, pr);
                var cam = (JObject)plate["camera"];
                Place(st.Camera, cam);
                Physics.SyncTransforms();
                Render(st.Camera, path, Width, Height, (int?)plate["supersample"] ?? 2);
                result["camera"] = cam;
                Debug.Log("CTLIFE [keyart] wrote " + path);
            }
            return result;
        }

        private static void Place(Camera cam, JObject c)
        {
            var pos = V3(c["pos"], new Vector3(0, 1.2f, -6));
            var target = V3(c["target"], Vector3.up);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up) * Quaternion.Euler(0, 0, (float?)c["roll"] ?? 0f);
            // Lens: fov is vertical. A 35 mm full-frame lens on a 2.225:1 frame ≈ vertical fov from the sensor crop.
            if (c["mm"] != null)
            {
                float mm = (float)c["mm"];
                float sensorH = 36f / (Width / (float)Height); // 36 mm wide sensor cropped to the frame's aspect
                cam.fieldOfView = 2f * Mathf.Atan(sensorH / (2f * mm)) * Mathf.Rad2Deg;
            }
            else cam.fieldOfView = (float?)c["fov"] ?? 30f;
            if (c["shift"] != null) { cam.usePhysicalProperties = false; var s = V3(c["shift"], Vector3.zero); cam.lensShift = new Vector2(s.x, s.y); }
        }

        private static float Ground(Vector3 at)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(at.x, at.y + 6f, at.z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            var ground = hits.Where(h => h.collider.GetComponentInParent<AvatarView>() == null).OrderBy(h => h.distance).ToList();
            return ground.Count > 0 ? ground[0].point.y : at.y;
        }

        private static string[] Look(string head, string full)
        {
            var reg = CharacterRegistry.Instance;
            if (!string.IsNullOrEmpty(full)) return full.Split('|');
            var e = reg.looks.FirstOrDefault(x => x != null && x.key != null && x.key.Contains("|" + head + "|"));
            if (e == null) throw new InvalidDataException("no registry look with " + head);
            return e.key.Split('|');
        }

        private static JObject Spawn(Stage st, JObject a, string plate)
        {
            var look = Look((string)a["head"], (string)a["look"]);
            var view = AvatarFactory.Create("keyart:" + (string)a["head"], look, false, null, null);
            st.Owned.Add(view.gameObject);
            var p = V3(a["pos"], Vector3.zero);
            float yaw = (float?)a["yaw"] ?? 180f;
            view.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
            var animator = view.Animator;
            var state = (string)a["state"] ?? "loco_idle";
            float t = (float?)a["t"] ?? 0.3f;
            // Real AvatarController states, sampled at a fixed phase: no procedural layer, no T-pose.
            if (animator != null)
            {
                // Shipped prefabs strip bone transforms (Optimize Game Objects); restore them so feet and hands can be
                // measured and a prop can ride the hand. Pose and skinning are unchanged.
                if (animator.isHuman && animator.GetBoneTransform(HumanBodyBones.Hips) == null)
                    AnimatorUtility.DeoptimizeTransformHierarchy(animator.gameObject);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.Rebind();
                animator.Update(0f);
                int hash = Animator.StringToHash(state);
                if (!animator.HasState(0, hash)) Failures.Add($"{plate}/{a["head"]}: no animator state {state}");
                animator.Play(hash, 0, t);
                animator.Update(0f);
                animator.Update(0.0001f);
            }
            var procedural = view.GetComponentInChildren<ProceduralAnimator>();
            if (procedural != null) procedural.enabled = false;
            int doubled = 0;
            foreach (var smr in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Opt-in ("doubleSided": true): draw single-sided opaque shells from both sides on capture-only copies.
                // Off by default: on Snow-family looks it also turns the glasses lens solid; the shipped materials are untouched.
                if ((bool?)a["doubleSided"] == true)
                {
                    var mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        // Opaque cloth only: doubling a transparent lens (glasses) makes it read as tinted.
                        if (mats[i] != null && mats[i].HasProperty("_Cull") && mats[i].GetFloat("_Cull") != 0f && mats[i].renderQueue < 2450
                            && !(mats[i].HasProperty("_Surface") && mats[i].GetFloat("_Surface") > 0.5f))
                        {
                            mats[i] = new Material(mats[i]) { name = mats[i].name + "_keyart_2sided" };
                            mats[i].SetFloat("_Cull", 0f);
                            doubled++;
                        }
                    smr.sharedMaterials = mats;
                }
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.On;
                smr.receiveShadows = true;
            }
            // Keep the hero LOD for close shots.
            var lod = view.GetComponentInChildren<LODGroup>();
            if (lod != null) lod.ForceLOD(0);
            // Ground the feet: the lowest foot/toe sits on the paving (sitting poses keep their authored height).
            float ground = (bool?)a["seated"] == true ? p.y : Ground(p);
            float gap = float.NaN;
            if (animator != null && animator.isHuman)
            {
                var bones = new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }
                    .Select(b => animator.GetBoneTransform(b)).Where(b => b != null).ToList();
                if (bones.Count > 0 && (bool?)a["seated"] != true)
                {
                    float lowest = bones.Min(b => b.position.y) - ((float?)a["footSole"] ?? 0.035f);
                    gap = lowest - ground;
                    if ((bool?)a["ground"] != false) view.transform.position += Vector3.up * (ground - lowest);
                }
            }
            if (a["phone"] != null && animator != null) AttachPhone(st, animator, (JObject)a["phone"]);
            if (animator != null) { animator.Update(0f); animator.enabled = false; } // freeze the sampled pose
            // Blob shadows (gameplay readability) are off for key art: the sun casts real shadows.
            foreach (var sr in view.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = false;
            return new JObject
            {
                ["head"] = (string)a["head"], ["look"] = string.Join("|", look), ["state"] = state, ["t"] = t,
                ["pos"] = new JArray(view.transform.position.x, view.transform.position.y, view.transform.position.z),
                ["footGapBeforeGrounding"] = float.IsNaN(gap) ? null : (JToken)Math.Round(gap, 3),
                ["human"] = animator != null && animator.isHuman,
                ["doubleSidedMaterials"] = doubled,
                ["shaders"] = new JArray(view.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name).Distinct()),
            };
        }

        private static void AttachPhone(Stage st, Animator animator, JObject p)
        {
            var hand = animator.GetBoneTransform((string)p["hand"] == "left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>((string)p["asset"] ?? "Assets/Art/characters/male/parts/phone.fbx");
            if (hand == null || src == null) { Failures.Add("phone prop: missing hand or asset"); return; }
            var phone = Object.Instantiate(src, hand, false);
            phone.name = "keyart_phone";
            phone.transform.localPosition = V3(p["offset"], new Vector3(0f, 0.08f, 0.03f));
            phone.transform.localRotation = Quaternion.Euler(V3(p["euler"], Vector3.zero));
            float s = (float?)p["scale"] ?? 1f;
            phone.transform.localScale = Vector3.one * s;
            // The part's mesh is authored off its pivot (it sits where the hand is in the source rig): move its visual centre
            // to the palm offset instead of trusting the pivot.
            var rs = phone.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                phone.transform.position += hand.TransformPoint(V3(p["offset"], new Vector3(0f, 0.08f, 0.03f))) - b.center;
                Debug.Log($"CTLIFE [keyart] phone bounds size={b.size} moved to {hand.TransformPoint(V3(p["offset"], Vector3.zero))}");
            }
        }

        private static void Prop(Stage st, JObject pr)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>((string)pr["asset"]);
            if (src == null) { Failures.Add("prop missing: " + pr["asset"]); return; }
            var go = Object.Instantiate(src);
            go.transform.SetPositionAndRotation(V3(pr["pos"], Vector3.zero), Quaternion.Euler(V3(pr["euler"], Vector3.zero)));
            go.transform.localScale = Vector3.one * ((float?)pr["scale"] ?? 1f);
        }

        // ------------------------------------------------------------------ survey (location scouting)

        private static JArray Survey(JObject doc, JArray views, string outDir)
        {
            var list = new JArray();
            using (var st = Build(doc))
            {
                Post(st, new JObject { ["post"] = new JObject { ["aperture"] = 22f } }, doc);
                var spots = st.Venue.GetComponentsInChildren<SpotAnchor>(true)
                    .Select(s => new JObject { ["id"] = s.spotId, ["kind"] = s.kind, ["anim"] = s.anim,
                        ["pos"] = new JArray(Math.Round(s.transform.position.x, 2), Math.Round(s.transform.position.y, 2), Math.Round(s.transform.position.z, 2)),
                        ["yaw"] = Math.Round(s.transform.eulerAngles.y, 1) });
                list.Add(new JObject { ["spots"] = new JArray(spots) });
                foreach (JObject v in views)
                {
                    Place(st.Camera, v);
                    var file = Path.Combine(outDir, "survey-" + (string)v["name"] + ".png");
                    Render(st.Camera, file, 1335, 600, 1);
                    list.Add(new JObject { ["file"] = file, ["view"] = v });
                }
            }
            return list;
        }

        // ------------------------------------------------------------------ render

        private static void Render(Camera cam, string path, int w, int h, int supersample)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("no graphics device (-nographics)");
            int sw = w * Mathf.Max(1, supersample), sh = h * Mathf.Max(1, supersample);
            cam.aspect = w / (float)h;
            var rt = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var down = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            down.Create();
            var previous = RenderTexture.active;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                // Two warm-up renders: the editor streams textures and compiles shader variants on first use.
                // camera.Render() into a target runs URP's full camera stack (post included); the single-camera
                // request path skipped the volume post here.
                cam.targetTexture = rt;
                for (int i = 0; i < 3; i++) cam.Render();
                cam.targetTexture = null;
                Graphics.Blit(rt, down);
                RenderTexture.active = down;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                rt.Release(); down.Release();
                Object.DestroyImmediate(rt); Object.DestroyImmediate(down); Object.DestroyImmediate(tex);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 V3(JToken t, Vector3 fallback) =>
            t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : t is JArray b && b.Count == 2 ? new Vector3((float)b[0], (float)b[1], 0) : fallback;

        private static Color C(JToken t, Color fallback) =>
            t is JArray a && a.Count >= 3 ? new Color((float)a[0], (float)a[1], (float)a[2], a.Count > 3 ? (float)a[3] : 1f) : fallback;

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
