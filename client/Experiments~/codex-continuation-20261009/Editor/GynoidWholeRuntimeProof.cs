#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class GynoidWholeRuntimeProof
    {
        static readonly string[] Protected = {
            "Assets/Resources/Characters/CharacterRegistry.asset", "Assets/Resources/UI/CreatorLookCatalog.asset",
            "Assets/Resources/Characters/AvatarController.controller" };
        static readonly string[] States = { "loco_idle", "loco_walk", "loco_run", "act_sit" };
        static readonly List<GameObject> Owned = new List<GameObject>();
        static JObject Report;
        static string Out;
        static void Need(bool value, string message) => GynoidWholeImportProbe.Require(value, message);

        // Root-only CLI entry. New candidate assets persist; original scenes/catalogues and sources do not change.
        public static void Run()
        {
            int code = 1; bool ownsEvidence = false, ownsReviewScene = false, reusedStartupScene = false;
            Scene review = default; var originalScene = SceneManager.GetActiveScene();
            var startupRoots = new List<KeyValuePair<GameObject, bool>>();
            JObject originalSceneSnapshot = null;
            var originalRandom = UnityEngine.Random.state; var originalRt = RenderTexture.active;
            var protectedHashes = new Dictionary<string, string>();
            var originalViews = AvatarView.All.ToArray();
            var generatedMaterials = new List<Material>();
            GynoidWholeImportProbe.Imported imported = null;
            Report = new JObject { ["scope"] = "Native Editor import, exact local assembly and manual Animator evaluation; not player/Seeker/backend acceptance", ["pass"] = false };
            try
            {
                Out = Path.GetFullPath(GynoidWholeImportProbe.Argument("-ctUbcOut"));
                Need(!Directory.Exists(Out), "Evidence directory already exists; preserve earlier output."); Directory.CreateDirectory(Out); ownsEvidence = true;
                var protectedAssets = AssetDatabase.GetDependencies(Protected, true).Concat(Protected)
                    .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(p)).Distinct().ToArray();
                foreach (var f in protectedAssets.SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists))
                    protectedHashes[f] = GynoidWholeImportProbe.Sha(f);
                Report["protectedBefore"] = JObject.FromObject(protectedHashes);
                Report["protectedAssetCount"] = protectedAssets.Length;
                originalSceneSnapshot = SceneSnapshot(originalScene);
                Report["originalSceneBefore"] = originalSceneSnapshot;
                if (string.IsNullOrEmpty(originalScene.path))
                {
                    // Unity rejects additive creation beside an untitled unsaved startup scene. Reuse only the
                    // strictly bounded fresh batch default; never save/discard an existing scene to get past it.
                    Need(Application.isBatchMode && SceneManager.sceneCount == 1 && originalScene.IsValid() && originalScene.isLoaded,
                        "Untitled scene reuse requires one loaded fresh batch startup scene.");
                    var roots = originalScene.GetRootGameObjects();
                    foreach (var go in roots) ValidateStartupRoot(go);
                    foreach (var go in roots) startupRoots.Add(new KeyValuePair<GameObject, bool>(go, go.activeSelf));
                    reusedStartupScene = true; review = originalScene;
                    foreach (var pair in startupRoots) pair.Key.SetActive(false);
                    Report["sceneMode"] = "reused bounded untitled batch startup; default Camera/Light roots temporarily disabled";
                }
                else
                {
                    review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); ownsReviewScene = true;
                    Need(SceneManager.SetActiveScene(review), "Cannot activate isolated review scene.");
                    Report["sceneMode"] = "owned additive review scene beside saved original";
                }
                Report["ownsReviewScene"] = ownsReviewScene;
                imported = GynoidWholeImportProbe.Import(GynoidWholeImportProbe.Argument("-ctUbcPlan"),
                    GynoidWholeImportProbe.Argument("-ctUbcAssetRoot", GynoidWholeImportProbe.DefaultRoot), Report);
                var catalog = Catalog(imported);
                AssetDatabase.CreateAsset(catalog, imported.Root + "/PreviewOnlyCatalog.asset");
                var controller = AssetDatabase.GetAssetPath(catalog.controller);
                Report["controller"] = controller; Report["controllerSHA256"] = GynoidWholeImportProbe.Sha(controller);
                var normalRequest = Request();
                Need(ExactModularAvatar.TryResolve(catalog, normalRequest, out var normalPlan, out var normalError), "Complete base resolution: " + normalError);
                Need(normalPlan.RendererCount == 1 && normalPlan.TriangleCount == 5634, "Incomplete original Gynoid LOD0.");
                NegativeControls(catalog);
                var normal = Create("LOCAL ART PROBE complete Gynoid", catalog, normalRequest);
                generatedMaterials.AddRange(normal.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.sharedMaterials));
                var normalSkins = normal.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Need(normalSkins.Length == 1 && normalSkins[0].sharedMesh == imported.Parts["whole"].GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh, "Whole-avatar source mesh was not retained.");
                Need(generatedMaterials.All(m => m != imported.Material), "Runtime material was not cloned.");
                var merged = Object.Instantiate(imported.Merged); Owned.Add(merged);
                foreach (var r in merged.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.enabled = r.name.EndsWith("_LOD0", StringComparison.Ordinal);
                var mergedSkin = merged.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.enabled);
                // Compare position/triangle multisets, not vertex indices (FBX can split vertices at UV/material seams).
                var baseline = Surface(new[] { mergedSkin }, merged.transform);
                var assembled = Surface(normalSkins, normal.transform);
                Need(baseline.SequenceEqual(assembled), "Complete default surface differs from merged LOD0 at rest (0.1mm quantization).");
                Report["completeDefaultRestSurfaceMatchesMerged"] = true;
                merged.SetActive(false);
                var camera = CameraForReview();
                var shots = new JArray(); var motions = new JArray(); Report["shots"] = shots; Report["motion"] = motions;
                Capture(camera, normal, "whole-rest-body", false, shots);
                Capture(camera, normal, "whole-rest-head", true, shots);
                EvaluateMotion(normal, "whole", camera, shots, motions);
                Report["wholeAvatar"] = new JObject { ["triangles"] = normalPlan.TriangleCount, ["renderers"] = normalPlan.RendererCount,
                    ["selectedSourceRenderer"] = "hero_gynoid_LOD0", ["modularReplacementTested"] = false };
                Report["notAccepted"] = new JArray("wire IDs", "UI options", "modular wardrobe or hair", "tints", "accessories", "seat or hand contact", "human art approval", "Seeker", "connected multiplayer");
                Report["nativeImportOnly"] = false; code = 0;
            }
            catch (Exception e) { Report["failure"] = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            finally
            {
                var cleanup = new JArray();
                foreach (var go in Owned.Where(g => g != null))
                    try { generatedMaterials.AddRange(go.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null && !AssetDatabase.Contains(m))); }
                    catch (Exception e) { cleanup.Add("collect owned materials: " + e.Message); }
                foreach (var go in Owned.ToArray()) try { if (go != null) Object.DestroyImmediate(go); } catch (Exception e) { cleanup.Add(e.GetType().Name + ": " + e.Message); }
                Owned.Clear();
                // Probe teardown must not strand material instances if Edit-mode lifecycle differs from Play.
                int editorFallbackMaterials = 0;
                foreach (var m in generatedMaterials.Distinct())
                    try { if (m != null && !AssetDatabase.Contains(m)) { Object.DestroyImmediate(m); editorFallbackMaterials++; } }
                    catch (Exception e) { cleanup.Add("owned material cleanup: " + e.Message); }
                Report["editModeMaterialCleanupFallbackCount"] = editorFallbackMaterials;
                foreach (var pair in startupRoots)
                    try { if (pair.Key != null) pair.Key.SetActive(pair.Value); else cleanup.Add("original startup root was destroyed"); }
                    catch (Exception e) { cleanup.Add("startup root active state restore: " + e.Message); }
                try
                {
                    bool attempted = SceneManager.GetActiveScene() != originalScene;
                    Report["originalSceneActivationAttempted"] = attempted;
                    if (!originalScene.IsValid() || !originalScene.isLoaded)
                        cleanup.Add("original scene is invalid or no longer loaded");
                    else if (attempted)
                        Report["originalSceneActivationReturn"] = SceneManager.SetActiveScene(originalScene);
                    // Require the actual final state. In native02 the already-active untitled scene's
                    // redundant SetActiveScene call returned false despite exact loaded/active readback.
                    if (SceneManager.GetActiveScene() != originalScene)
                        cleanup.Add("original scene did not reactivate");
                }
                catch (Exception e) { cleanup.Add("active scene restore: " + e.Message); }
                try { if (ownsReviewScene && review.IsValid() && !EditorSceneManager.CloseScene(review, true)) cleanup.Add("owned scene did not close"); }
                catch (Exception e) { cleanup.Add("owned scene close: " + e.Message); }
                if (originalSceneSnapshot != null)
                    try
                    {
                        var after = SceneSnapshot(originalScene); Report["originalSceneAfter"] = after;
                        bool rootsMatch = JToken.DeepEquals(originalSceneSnapshot["roots"], after["roots"]);
                        Report["originalSceneRootsRestored"] = rootsMatch;
                        Report["originalSceneDirtyChanged"] = (bool)originalSceneSnapshot["isDirty"] != (bool)after["isDirty"];
                        Report["startupSceneReused"] = reusedStartupScene;
                        // Reused untitled startup may become dirty. Record it; never clear/save its dirty state.
                        if (!rootsMatch) cleanup.Add("original root identities/active states/transforms/names/layers/components changed");
                    }
                    catch (Exception e) { cleanup.Add("original scene snapshot readback: " + e.Message); }
                try { RenderTexture.active = originalRt; UnityEngine.Random.state = originalRandom; }
                catch (Exception e) { cleanup.Add("render/random restore: " + e.Message); }
                var drift = CheckHashes(protectedHashes, "", cleanup);
                Report["protectedDrift"] = drift; Report["cleanupErrors"] = cleanup;
                bool viewsRestored = originalViews.Length == AvatarView.All.Count && originalViews.All(AvatarView.All.Contains);
                Report["avatarRegistryRestored"] = viewsRestored;
                Report["sceneRestored"] = SceneManager.GetActiveScene() == originalScene;
                if (Report["sourceHashes"] is JObject sourceHashes)
                {
                    string source = (string)Report["sourceRoot"];
                    var sourceDrift = CheckHashes(sourceHashes.Properties().ToDictionary(p => p.Name, p => (string)p.Value), source, cleanup);
                    Report["sourceDrift"] = sourceDrift; if (sourceDrift.Count != 0) code = 1;
                }
                if (drift.Count != 0 || cleanup.Count != 0 || !viewsRestored || SceneManager.GetActiveScene() != originalScene) code = 1;
                Report["pass"] = code == 0;
                try { if (ownsEvidence) File.WriteAllText(Path.Combine(Out, "gynoid-whole-import-review.json"), Report.ToString()); }
                catch (Exception e) { code = 1; Debug.LogError("[Gynoid whole import proof] Could not retain receipt: " + e.Message); }
                finally
                {
                    Debug.Log("[Gynoid whole import proof] " + (code == 0 ? "PASS" : "FAIL") + "; local art only; evidence=" + Out);
                    if (Application.isBatchMode) EditorApplication.Exit(code);
                }
            }
        }
        static void ValidateStartupRoot(GameObject go)
        {
            Need(go.transform.childCount == 0, "Startup scene root has unexpected children: " + go.name);
            bool camera = go.GetComponent<Camera>() != null, light = go.GetComponent<Light>() != null;
            Need(camera != light, "Startup scene root is not a single default Camera or Light: " + go.name);
            foreach (var component in go.GetComponents<Component>())
            {
                Need(component != null, "Startup scene contains a missing script.");
                var type = component.GetType();
                bool urpData = type.Assembly.GetName().Name == "Unity.RenderPipelines.Universal.Runtime" &&
                    ((camera && type.FullName == "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData") ||
                     (light && type.FullName == "UnityEngine.Rendering.Universal.UniversalAdditionalLightData"));
                Need(type == typeof(Transform) || type == typeof(Camera) || type == typeof(Light) ||
                     (camera && type == typeof(AudioListener)) || urpData,
                     "Unexpected startup scene component: " + type.FullName);
            }
        }
        static JObject SceneSnapshot(Scene scene)
        {
            Need(scene.IsValid() && scene.isLoaded, "Original scene is not available for metadata capture.");
            var roots = new JArray();
            foreach (var go in scene.GetRootGameObjects().OrderBy(g => g.GetInstanceID()))
            {
                var t = go.transform; var q = t.localRotation;
                roots.Add(new JObject { ["instanceId"] = go.GetInstanceID(), ["name"] = go.name, ["activeSelf"] = go.activeSelf,
                    ["layer"] = go.layer, ["childCount"] = t.childCount, ["localPosition"] = Vector(t.localPosition),
                    ["localScale"] = Vector(t.localScale), ["localRotation"] = new JArray(q.x,q.y,q.z,q.w),
                    ["components"] = new JArray(go.GetComponentsInChildren<Component>(true).Select(c => c == null ? (JToken)JValue.CreateNull() :
                        new JObject { ["instanceId"] = c.GetInstanceID(), ["type"] = c.GetType().AssemblyQualifiedName,
                            ["path"] = AnimationUtility.CalculateTransformPath(c.transform,t),
                            ["enabled"] = c is Behaviour behaviour ? (JToken)behaviour.enabled : JValue.CreateNull() })) });
            }
            return new JObject { ["name"] = scene.name, ["path"] = scene.path, ["sceneHash"] = scene.GetHashCode(),
                ["isDirty"] = scene.isDirty, ["isLoaded"] = scene.isLoaded, ["rootCount"] = roots.Count, ["roots"] = roots };
        }
        static JArray CheckHashes(IEnumerable<KeyValuePair<string, string>> hashes, string root, JArray errors)
        {
            var drift = new JArray();
            foreach (var pair in hashes)
                try { var path = Path.Combine(root, pair.Key); if (!File.Exists(path) || GynoidWholeImportProbe.Sha(path) != pair.Value) drift.Add(pair.Key); }
                catch (Exception e) { drift.Add(pair.Key); errors.Add("verify " + pair.Key + ": " + e.Message); }
            return drift;
        }
        static ExactModularCatalog Catalog(GynoidWholeImportProbe.Imported a)
        {
            var c = ScriptableObject.CreateInstance<ExactModularCatalog>(); c.name = "Gynoid whole-avatar local proof, unregistered";
            c.baseId = "local-probe:hero_gynoid"; c.rigPrefab = a.Rig; c.avatar = a.Avatar;
            c.controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/Characters/AvatarController.controller");
            Need(c.controller != null, "Existing shared controller is missing.");
            c.height = GeometryBounds(a.Merged.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name.EndsWith("_LOD0", StringComparison.Ordinal)), a.Merged.transform).size.y;
            c.requiredSlots = new[] { ModularSlot.Body }; c.requiredRoles = ModularRole.Body | ModularRole.Head;
            c.parts = new[] { new ModularPart { id = "complete-original", baseId = c.baseId, slot = ModularSlot.Body,
                renderers = new[] { new ModularRenderer { prefab = a.Parts["whole"], roles = ModularRole.Body | ModularRole.Head } } } };
            return c;
        }
        static ModularAvatarRequest Request()
        {
            var r = new ModularAvatarRequest { baseId = "local-probe:hero_gynoid" };
            r.parts.Add(ModularSlot.Body, "complete-original"); return r;
        }
        static void NegativeControls(ExactModularCatalog c)
        {
            var rows = new JArray(); Report["negativeControls"] = rows;
            void Reject(string name, ModularAvatarRequest r, ModularAvatarError expected)
            {
                bool ok = ExactModularAvatar.TryResolve(c, r, out var plan, out var failure);
                Need(!ok && plan == null && failure.code == expected, "Negative control did not fail as expected: " + name + ":" + failure);
                rows.Add(new JObject { ["case"] = name, ["error"] = failure.code.ToString() });
            }
            var missing = Request(); missing.parts.Clear(); Reject("missing complete base", missing, ModularAvatarError.MissingPart);
            var tint = Request(); tint.tints["skin"] = "skin_01"; Reject("unimplemented tint", tint, ModularAvatarError.UnsupportedTint);
            var acc = Request(); acc.accessories["face"] = "visor"; Reject("unimplemented accessory", acc, ModularAvatarError.UnsupportedAccessory);
            var wrong = Request(); wrong.baseId = "ubc_f"; Reject("other base", wrong, ModularAvatarError.IncompatibleBase);
            var hair = Request(); hair.parts.Add(ModularSlot.Hair, "invented-hair"); Reject("unavailable hair slot", hair, ModularAvatarError.UnexpectedPart);
            var whole = c.parts.Single(); var oldRoles = whole.renderers[0].roles;
            try { whole.renderers[0].roles = ModularRole.Body; Reject("missing declared head coverage", Request(), ModularAvatarError.MissingGeometry); }
            finally { whole.renderers[0].roles = oldRoles; }
        }
        static GameObject Create(string name, ExactModularCatalog c, ModularAvatarRequest r)
        {
            Need(ExactModularAvatar.TryCreate(name, c, r, null, true, out var view, out var error), "Runtime creation: " + error);
            Owned.Add(view.gameObject); return view.gameObject;
        }
        static void EvaluateMotion(GameObject root, string variant, Camera camera, JArray shots, JArray rows)
        {
            var a = root.GetComponentInChildren<Animator>(); var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Need(a != null && a.avatar.isHuman && a.avatar.isValid, "Animation lacks own Humanoid Avatar.");
            foreach (var state in States)
            {
                Need(a.HasState(0, Animator.StringToHash(state)), "Controller state missing: " + state);
                a.Rebind();
                // Both locomotion states share a speed-driven BlendTree in the actual controller.
                // Its authored thresholds are walk 2.2 and run 3.6; a state name alone would replay walk twice.
                float speed = state == "loco_walk" ? 2.2f : state == "loco_run" ? 3.6f : 0f;
                foreach (var parameter in new[] { "speed", "locoRate", "seatHeight" })
                    Need(a.parameters.Any(p => p.name == parameter && p.type == AnimatorControllerParameterType.Float), "Missing controller parameter: " + parameter);
                a.SetFloat("speed", speed); a.SetFloat("locoRate", 1); a.SetFloat("seatHeight", .44f);
                a.Play(state, 0, 0.07f); a.Update(0);
                var transforms = a.GetComponentsInChildren<Transform>(true); var before = transforms.Select(t => t.position).ToArray();
                float start = a.GetCurrentAnimatorStateInfo(0).normalizedTime; float maxBoneDelta = 0;
                for (int frame = 0; frame < 60; frame++)
                {
                    a.Update(1f / 60f);
                    for (int i = 0; i < transforms.Length; i++) maxBoneDelta = Mathf.Max(maxBoneDelta, Vector3.Distance(before[i], transforms[i].position));
                }
                var final = a.GetCurrentAnimatorStateInfo(0);
                var clips = a.GetCurrentAnimatorClipInfo(0);
                Need(clips.Length > 0 && clips.All(c => c.clip != null && c.clip.humanMotion), "Expected actual Humanoid clip playback: " + state);
                string expectedGuid = state == "loco_walk" ? "5d68c912782f844899a2bd888c1c3952" : state == "loco_run" ? "910ffd7dd71e743948a2daa7c675f851" : null;
                if (expectedGuid != null) Need(clips.Any(c => c.weight > .99f && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.clip)) == expectedGuid), "Wrong locomotion clip selected: " + state);
                Need(final.shortNameHash == Animator.StringToHash(state) && final.normalizedTime > start, "Requested state did not advance: " + state);
                Need(maxBoneDelta > .000001f, "Animation produced no measurable bone movement: " + state);
                var bounds = GeometryBounds(skins, root.transform);
                Need(GynoidWholeImportProbe.Finite(bounds.min) && GynoidWholeImportProbe.Finite(bounds.max) && bounds.size.y > .3f && bounds.size.y < 3f && bounds.size.x < 3f && bounds.size.z < 3f, "Invalid animated surface bounds: " + state);
                rows.Add(new JObject { ["variant"] = variant, ["state"] = state, ["evaluatedSteps"] = 60, ["stepSeconds"] = 1f/60f,
                    ["normalizedStart"] = start, ["normalizedEnd"] = final.normalizedTime, ["maxBoneDeltaMeters"] = maxBoneDelta,
                    ["speedParameter"] = speed, ["locoRate"] = 1, ["seatHeight"] = .44f,
                    ["surfaceMin"] = Vector(bounds.min), ["surfaceMax"] = Vector(bounds.max), ["rendererCount"] = skins.Length, ["clips"] = new JArray(clips.Select(c => new JObject { ["asset"] = AssetDatabase.GetAssetPath(c.clip), ["clip"] = c.clip.name, ["weight"] = c.weight, ["length"] = c.clip.length })) });
                Capture(camera, root, variant + "-" + state, false, shots);
            }
        }
        static Camera CameraForReview()
        {
            var go = new GameObject("Local art proof camera"); Owned.Add(go); var c = go.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(.065f,.075f,.09f); c.fieldOfView = 30;
            c.nearClipPlane = .03f; c.farClipPlane = 50; c.allowHDR = false; c.allowMSAA = false; c.cullingMask = 1 << 30;
            var light = new GameObject("Local art key"); Owned.Add(light); var l = light.AddComponent<Light>(); l.type = LightType.Directional;
            l.intensity = 1.6f; l.color = new Color(1,.96f,.9f); l.cullingMask = 1 << 30; light.transform.rotation = Quaternion.Euler(28,-35,0);
            return c;
        }
        static void Capture(Camera c, GameObject actor, string label, bool head, JArray shots)
        {
            foreach (var t in actor.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true); var bounds = GeometryBounds(skins, actor.transform);
            var target = head ? actor.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head).position + Vector3.up*.09f : actor.transform.TransformPoint(bounds.center);
            float distance = head ? .78f : Mathf.Max(bounds.size.y, bounds.size.x)*2.25f;
            c.transform.position = target + new Vector3(.18f,.08f,1).normalized*distance;
            c.transform.LookAt(target); int size = head ? 640 : 1024;
            var rt = new RenderTexture(size,size,24,RenderTextureFormat.ARGB32); var old = RenderTexture.active; var previous = c.targetTexture;
            var image = new Texture2D(size,size,TextureFormat.RGB24,false);
            var snapshotObjects = new List<GameObject>(); var bakedMeshes = new List<Mesh>();
            var previousEnabled = skins.Select(r => r.enabled).ToArray();
            int sampledVertices = 0; float maximumWorldMatrixError = 0; string poseHash;
            try
            {
                // Manual Animator.Update advances the CPU pose, but Edit-mode Camera.Render can reuse cached GPU
                // skinning. Render newly baked meshes at the exact skin transforms instead of claiming that cache
                // represents this sample. The live Animator and numeric motion checks are unchanged.
                using (var bytes = new MemoryStream())
                using (var writer = new BinaryWriter(bytes))
                {
                    for (int index = 0; index < skins.Length; index++)
                    {
                        var skin = skins[index];
                        var baked = new Mesh { name = label + "_sample_" + index }; bakedMeshes.Add(baked);
                        skin.BakeMesh(baked, false);
                        Need(baked.vertexCount == skin.sharedMesh.vertexCount, "Baked sample lost source vertices: " + label);
                        var snapshot = new GameObject("CPU pose snapshot " + index) { layer = skin.gameObject.layer };
                        snapshotObjects.Add(snapshot);
                        // Identity child preserves the complete original parent chain, including nonuniform scale.
                        snapshot.transform.SetParent(skin.transform, false);
                        float error = GynoidWholeImportProbe.MatrixDelta(snapshot.transform.localToWorldMatrix, skin.transform.localToWorldMatrix);
                        maximumWorldMatrixError = Mathf.Max(maximumWorldMatrixError, error);
                        Need(error <= .000001f, "Snapshot world transform differs from sampled skin: " + label);
                        snapshot.AddComponent<MeshFilter>().sharedMesh = baked;
                        var renderer = snapshot.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = skin.sharedMaterials; renderer.enabled = previousEnabled[index];
                        renderer.shadowCastingMode = skin.shadowCastingMode; renderer.receiveShadows = skin.receiveShadows;
                        renderer.lightProbeUsage = skin.lightProbeUsage; renderer.reflectionProbeUsage = skin.reflectionProbeUsage;
                        renderer.probeAnchor = skin.probeAnchor;
                        var block = new MaterialPropertyBlock(); skin.GetPropertyBlock(block); renderer.SetPropertyBlock(block);
                        for (int slot = 0; slot < skin.sharedMaterials.Length; slot++)
                        {
                            block.Clear(); skin.GetPropertyBlock(block, slot);
                            if (!block.isEmpty) renderer.SetPropertyBlock(block, slot);
                        }
                        writer.Write(index); writer.Write(baked.vertexCount);
                        var toActor = actor.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                        foreach (var vertex in baked.vertices)
                        {
                            var point = toActor.MultiplyPoint3x4(vertex); Need(GynoidWholeImportProbe.Finite(point), "Nonfinite captured sample.");
                            writer.Write(point.x); writer.Write(point.y); writer.Write(point.z);
                        }
                        sampledVertices += baked.vertexCount;
                        skin.enabled = false;
                    }
                    writer.Flush();
                    using (var hash = SHA256.Create())
                        poseHash = BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
                }
                rt.Create(); c.targetTexture = rt; c.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,size,size),0,0); image.Apply();
                var pixels = image.GetPixels32(); var corner = pixels[0];
                int foreground = pixels.Count(p => Math.Abs(p.r-corner.r)+Math.Abs(p.g-corner.g)+Math.Abs(p.b-corner.b) > 20);
                Need(foreground > size*size/50, "Native capture has too little visible content: " + label);
                string path = Path.Combine(Out, "local-preview-" + label + ".png"); File.WriteAllBytes(path,image.EncodeToPNG());
                var animatorState = actor.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0);
                shots.Add(new JObject { ["file"] = Path.GetFileName(path), ["width"] = size, ["height"] = size,
                    ["foregroundPixels"] = foreground, ["captureMethod"] = "CPU BakeMesh(false) snapshot with original materials and full transform chain",
                    ["sampledRendererCount"] = skins.Length, ["sampledVertexCount"] = sampledVertices,
                    ["sampledPoseSHA256"] = poseHash, ["maxSnapshotWorldMatrixError"] = maximumWorldMatrixError,
                    ["animatorStateHash"] = animatorState.shortNameHash, ["animatorNormalizedTime"] = animatorState.normalizedTime,
                    ["meaning"] = "Baked sample of live native Animator pose; not realtime Play mode, wire identity or gameplay acceptance" });
            }
            finally
            {
                for (int index = 0; index < skins.Length; index++) if (skins[index] != null) skins[index].enabled = previousEnabled[index];
                foreach (var go in snapshotObjects) if (go != null) Object.DestroyImmediate(go);
                foreach (var mesh in bakedMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
                c.targetTexture = previous; RenderTexture.active = old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
            }
        }
        static JObject Vector(Vector3 v) => new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
        static Bounds GeometryBounds(IEnumerable<SkinnedMeshRenderer> skins, Transform root)
        {
            bool first=true; var result=new Bounds(); var mesh=new Mesh();
            try { foreach(var r in skins) { r.BakeMesh(mesh,false); var mat=root.worldToLocalMatrix*r.transform.localToWorldMatrix; foreach(var v in mesh.vertices) { var p=mat.MultiplyPoint3x4(v); Need(GynoidWholeImportProbe.Finite(p),"Nonfinite baked vertex."); if(first){result=new Bounds(p,Vector3.zero);first=false;} else result.Encapsulate(p); } } }
            finally { Object.DestroyImmediate(mesh); }
            Need(!first,"No skinned surface."); return result;
        }
        static string[] Surface(IEnumerable<SkinnedMeshRenderer> skins, Transform root)
        {
            var triangles=new List<string>(); var mesh=new Mesh();
            string Key(Vector3 p) => Math.Round(p.x*10000)+","+Math.Round(p.y*10000)+","+Math.Round(p.z*10000);
            try { foreach(var r in skins) { r.BakeMesh(mesh,false);var mat=root.worldToLocalMatrix*r.transform.localToWorldMatrix;var verts=mesh.vertices;var ids=mesh.triangles;
                for(int i=0;i<ids.Length;i+=3){var points=new[]{Key(mat.MultiplyPoint3x4(verts[ids[i]])),Key(mat.MultiplyPoint3x4(verts[ids[i+1]])),Key(mat.MultiplyPoint3x4(verts[ids[i+2]]))};Array.Sort(points,StringComparer.Ordinal);triangles.Add(string.Join("|",points));} } }
            finally { Object.DestroyImmediate(mesh); }
            return triangles.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        }
    }
}
#endif
