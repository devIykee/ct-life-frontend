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
    public static class UbcSurfaceVisualDiagnostic
    {
        static readonly string[] Protected = {
            "Assets/Resources/Characters/CharacterRegistry.asset", "Assets/Resources/UI/CreatorLookCatalog.asset",
            "Assets/Resources/Characters/AvatarController.controller" };
        static readonly List<GameObject> Owned = new List<GameObject>();
        static JObject Report;
        static string Out;
        static void Need(bool value, string message) => UbcImportProbe.Require(value, message);

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
            Report = new JObject { ["scope"] = "Read-only existing-D imported merged/assembled visual comparison; no art/player/backend acceptance", ["pass"] = false };
            try
            {
                Out = Path.GetFullPath(UbcImportProbe.Argument("-ctUbcComparisonOut"));
                Need(!Directory.Exists(Out), "Evidence directory already exists; preserve earlier output."); Directory.CreateDirectory(Out); ownsEvidence = true;
                string sourceRoot = UbcImportProbe.Argument("-ctUbcComparisonRoot");
                Need(sourceRoot == "Assets/CTLifeCandidates/UBCImportProbe20261009D", "Only the existing native04/D candidate is authorized.");
                string acceptedPath = UbcImportProbe.Argument("-ctUbcComparisonAccepted");
                var accepted = JObject.Parse(File.ReadAllText(acceptedPath));
                Need((bool)accepted["pass"] && (string)accepted["assetRoot"] == sourceRoot && (bool)accepted["sceneRestored"] &&
                    !accepted["cleanupErrors"].HasValues && !accepted["sourceDrift"].HasValues && !accepted["protectedDrift"].HasValues,
                    "The matching structural prerequisite did not pass.");
                Report["structuralReceiptSHA256"] = UbcImportProbe.Sha(acceptedPath);
                Report["sourceRoot"] = accepted["sourceRoot"].DeepClone(); Report["sourceHashes"] = accepted["sourceHashes"].DeepClone();
                Report["candidateRoot"] = sourceRoot;
                var protectedAssets = AssetDatabase.GetDependencies(Protected.Concat(new[] { sourceRoot + "/PreviewOnlyCatalog.asset" }).ToArray(), true)
                    .Concat(Protected).Concat(Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories).Select(f => f.Replace("\\", "/")))
                    .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(p)).Distinct().ToArray();
                foreach (var f in protectedAssets.SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists))
                    protectedHashes[f] = UbcImportProbe.Sha(f);
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
                var catalog = AssetDatabase.LoadAssetAtPath<ExactModularCatalog>(sourceRoot + "/PreviewOnlyCatalog.asset");
                Need(catalog != null, "Existing catalogue missing.");
                var request = Request(false);
                Need(ExactModularAvatar.TryResolve(catalog, request, out var plan, out var resolutionFailure), "Existing default resolution: " + resolutionFailure);
                Need(plan.RendererCount == 7 && plan.TriangleCount == 5331, "Existing default geometry changed.");
                var assembled = Create("ASSEMBLED DEFAULT visual diagnostic", catalog, request);
                var originalModel = AssetDatabase.LoadAssetAtPath<GameObject>(sourceRoot + "/source/bases/ubc_m/ubc_m.fbx");
                var originalMaterial = AssetDatabase.LoadAssetAtPath<Material>(sourceRoot + "/OriginalAlbedo.mat");
                Need(originalModel != null && originalMaterial != null, "Existing merged model or material missing.");
                var merged = Object.Instantiate(originalModel); Owned.Add(merged);
                merged.name = "MERGED LOD0 visual diagnostic";
                foreach (var lod in merged.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
                var allMerged = merged.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var mergedSkin = allMerged.Single(r => r.name == "ubc_m_LOD0");
                foreach (var skin in allMerged) skin.enabled = skin == mergedSkin;
                mergedSkin.sharedMaterial = originalMaterial;
                var mergedAnimator = merged.GetComponent<Animator>();
                Need(mergedAnimator != null && mergedAnimator.avatar == catalog.avatar && mergedAnimator.avatar.isValid && mergedAnimator.avatar.isHuman,
                    "Merged source must retain the exact own Humanoid Avatar.");
                mergedAnimator.runtimeAnimatorController = catalog.controller;
                mergedAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate; mergedAnimator.applyRootMotion = false;
                var assembledSkins = assembled.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                generatedMaterials.AddRange(assembledSkins.SelectMany(r => r.sharedMaterials));
                var eyeSource = AssetDatabase.LoadAssetAtPath<GameObject>(sourceRoot + "/part-eyes.prefab").GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                var headSource = AssetDatabase.LoadAssetAtPath<GameObject>(sourceRoot + "/part-head.prefab").GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                var eyes = assembledSkins.Single(r => r.sharedMesh == eyeSource); var head = assembledSkins.Single(r => r.sharedMesh == headSource);
                var camera = CameraForReview();
                var shots = new JArray(); var comparisons = new JArray(); Report["shots"] = shots; Report["comparisons"] = comparisons;
                foreach (string pose in new[] { "rest", "idle" })
                {
                    assembled.SetActive(true); merged.SetActive(true);
                    if (pose == "idle") foreach (var actor in new[] { assembled, merged })
                    {
                        var animator = actor.GetComponent<Animator>(); animator.Rebind();
                        animator.Play("loco_idle", 0, .13f); animator.Update(0);
                    }
                    var bodyBounds = GeometryBounds(assembledSkins, assembled.transform);
                    var headBounds = GeometryBounds(new[] { head }, assembled.transform);
                    var eyesBounds = GeometryBounds(new[] { eyes }, assembled.transform);
                    var rawFacing = eyesBounds.center - headBounds.center;
                    var horizontal = new Vector3(rawFacing.x, 0, rawFacing.z);
                    Need(horizontal.magnitude > .015f, "Eye/head geometry does not define a stable horizontal front.");
                    var front = horizontal.normalized;
                    bool same = Surface(new[] { mergedSkin }, merged.transform).SequenceEqual(Surface(assembledSkins, assembled.transform));
                    if (pose == "rest") Need(same, "Existing rest surface no longer equals the accepted merged surface.");
                    comparisons.Add(new JObject { ["pose"] = pose, ["sameTrianglePositionMultisetAt0_1mm"] = same,
                        ["eyesCentre"] = Vector(eyesBounds.center), ["headBoundsCentre"] = Vector(headBounds.center),
                        ["frontFromHeadToEyes"] = Vector(front), ["horizontalSeparation"] = horizontal.magnitude });
                    foreach (bool close in new[] { false, true }) foreach (bool back in new[] { false, true })
                    {
                        var target = close ? headBounds.center + Vector3.up * .035f : bodyBounds.center;
                        float distance = close ? .85f : Mathf.Max(bodyBounds.size.y, bodyBounds.size.x) * 2.25f;
                        var direction = ((back ? -front : front) + Vector3.up * .04f).normalized;
                        foreach (var pair in new[] { Tuple.Create("merged", merged), Tuple.Create("assembled", assembled) })
                        {
                            merged.SetActive(pair.Item2 == merged); assembled.SetActive(pair.Item2 == assembled);
                            Capture(camera, pair.Item2, pair.Item1 + "-" + pose + (close ? "-head" : "-body") + (back ? "-back" : "-front"), close, shots, target, direction, distance);
                        }
                    }
                }
                Report["assetImportCalled"] = false; Report["assetSaveCalled"] = false;
                Report["visibleArtAccepted"] = false;
                Report["acceptanceLimit"] = "Matched original imported LOD0 and assembled default with identical material, pose and camera. Diagnoses source versus assembly appearance; no source repair, contact or human approval.";
                code = 0;
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
                try { if (ownsEvidence) File.WriteAllText(Path.Combine(Out, "ubc-surface-comparison.json"), Report.ToString()); }
                catch (Exception e) { code = 1; Debug.LogError("[UBC surface comparison] Could not retain receipt: " + e.Message); }
                finally
                {
                    Debug.Log("[UBC surface comparison] " + (code == 0 ? "PASS" : "FAIL") + "; local art only; evidence=" + Out);
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
                try { var path = Path.Combine(root, pair.Key); if (!File.Exists(path) || UbcImportProbe.Sha(path) != pair.Value) drift.Add(pair.Key); }
                catch (Exception e) { drift.Add(pair.Key); errors.Add("verify " + pair.Key + ": " + e.Message); }
            return drift;
        }
        static ModularAvatarRequest Request(bool buzzed)
        {
            var r = new ModularAvatarRequest { baseId = "local-probe:ubc_m" };
            r.parts.Add(ModularSlot.Head, "head"); r.parts.Add(ModularSlot.Hair, buzzed ? "hair-buzzed" : "hair-default");
            r.parts.Add(ModularSlot.Top, "top"); r.parts.Add(ModularSlot.Bottom, "bottom"); r.parts.Add(ModularSlot.Shoes, "feet"); return r;
        }
        static GameObject Create(string name, ExactModularCatalog c, ModularAvatarRequest r)
        {
            Need(ExactModularAvatar.TryCreate(name, c, r, null, true, out var view, out var error), "Runtime creation: " + error);
            Owned.Add(view.gameObject); return view.gameObject;
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
        static void Capture(Camera c, GameObject actor, string label, bool head, JArray shots, Vector3 target, Vector3 direction, float distance)
        {
            foreach (var t in actor.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            c.transform.position = target + direction * distance;
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
                        float error = UbcImportProbe.MatrixDelta(snapshot.transform.localToWorldMatrix, skin.transform.localToWorldMatrix);
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
                            var point = toActor.MultiplyPoint3x4(vertex); Need(UbcImportProbe.Finite(point), "Nonfinite captured sample.");
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
                    ["cameraTarget"] = Vector(target), ["cameraDirection"] = Vector(direction), ["cameraDistance"] = distance,
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
            try { foreach(var r in skins) { r.BakeMesh(mesh,false); var mat=root.worldToLocalMatrix*r.transform.localToWorldMatrix; foreach(var v in mesh.vertices) { var p=mat.MultiplyPoint3x4(v); Need(UbcImportProbe.Finite(p),"Nonfinite baked vertex."); if(first){result=new Bounds(p,Vector3.zero);first=false;} else result.Encapsulate(p); } } }
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
