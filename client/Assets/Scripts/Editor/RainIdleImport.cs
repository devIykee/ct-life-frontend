using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CTLife.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Imports a Rain-only native idle override; the shared controller and Snow remain unchanged.
    /// CLI: -executeMethod CTLife.EditorTools.RainIdleImport.ImportIdle
    /// Run after Rain import and the shared Snow idle/locomotion setup. Reapply after rebuilding the shared controller.
    /// </summary>
    public sealed class RainIdleImport : AssetPostprocessor
    {
        public const string ClipPath = "Assets/Art/characters/authored/RainIdle/RainAdult_RelaxedIdle.fbx";
        private const string ClipName = "RainAdult_RelaxedIdle";
        private const string ControllerPath = "Assets/Resources/Characters/AvatarController.controller";
        public const string OverridePath = "Assets/Resources/Characters/RainController.overrideController";
        private const string RegistryPath = "Assets/Resources/Characters/CharacterRegistry.asset";
        private const string ReadyTag = "ct-life:rain-native-idle-humanoid-v1";
        private const string ProbeTag = "ct-life:rain-native-idle-source-probe-v1";
        private const ImportAssetOptions ImportNow = ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport;

        private void OnPreprocessModel()
        {
            if (assetPath != ClipPath) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = true;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.optimizeGameObjects = false;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            bool human = importer.userData == ReadyTag;
            importer.animationType = human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            importer.avatarSetup = human ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = human ? OwnRainAvatar() : null;
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != ClipPath) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            Require(clips.Length == 1, "Native idle must contain exactly one take; found " + clips.Length);
            var clip = clips[0];
            // Preserve the actual FBX take and frame range, including the authored final loop key.
            clip.name = ClipName;
            clip.loopTime = true;
            clip.loopPose = true;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = false; // Body Orientation, matching the corrected Rain locomotion clips.
            clip.rotationOffset = 0f;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            importer.clipAnimations = clips;
        }

        [MenuItem("CT Life/Content/Import Rain Native Idle")]
        public static void ImportIdle()
        {
            var source = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../art/character-candidates/rain-adult-production/animations/RainAdult_RelaxedIdle.fbx"));
            Require(File.Exists(source), "Missing authored idle: " + source);
            string sha = Hash(source);
            ValidateSourceReport(Path.Combine(Path.GetDirectoryName(source), "idle-export-validation.json"), sha);
            var avatar = OwnRainAvatar();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath);
            Require(controller != null && registry != null, "Shared controller/character registry is missing.");
            Require(!EditorUtility.IsDirty(registry) && !AssetDatabase.LoadAllAssetsAtPath(ControllerPath).Any(EditorUtility.IsDirty),
                "Save pending registry/controller edits before native idle import.");
            string look = string.Join("|", RainCharacterImport.Look);
            var entry = registry.looks.SingleOrDefault(e => e.key == look);
            Require(entry != null && entry.prefab != null && entry.previewPrefab != null, "Import the exact complete Rain look first.");
            var sharedIdle = FindIdle(controller).motion as AnimationClip;
            Require(sharedIdle != null && controller.layers.SelectMany(l => States(l.stateMachine)).Count(s => s.motion == sharedIdle) == 1,
                "The loco_idle clip must be unique to that state; a clip override must not alter another state.");
            var previousOverride = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
            Require(previousOverride == null || !EditorUtility.IsDirty(previousOverride), "Save pending Rain override edits first.");
            var oldOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            if (previousOverride != null)
            {
                previousOverride.GetOverrides(oldOverrides);
                Require(previousOverride.runtimeAnimatorController == controller && oldOverrides.All(p =>
                    p.Key == sharedIdle || p.Value == null || p.Value == p.Key),
                    "Existing Rain controller has unrelated overrides; refusing to overwrite them.");
            }
            string sharedHash = Hash(ControllerPath), registryOthers = RegistryFingerprint(registry, entry);
            var preserved = new[] { ClipPath, ClipPath + ".meta", OverridePath, OverridePath + ".meta", RegistryPath }
                .ToDictionary(path => path, ReadIfPresent);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClipPath));
                if (AssetImporter.GetAtPath(ClipPath) is ModelImporter existing)
                {
                    existing.userData = ProbeTag;
                    AssetDatabase.WriteImportSettingsIfDirty(ClipPath);
                }
                File.Copy(source, ClipPath, true);
                AssetDatabase.ImportAsset(ClipPath, ImportNow);
                var importer = (ModelImporter)AssetImporter.GetAtPath(ClipPath);
                Require(importer != null && importer.animationType == ModelImporterAnimationType.Generic, "Generic source probe was bypassed.");
                ValidateRawDigits(SingleClip());
                importer.userData = ReadyTag;
                importer.SaveAndReimport();
                importer = (ModelImporter)AssetImporter.GetAtPath(ClipPath);
                Require(importer.animationType == ModelImporterAnimationType.Human &&
                    importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar == avatar,
                    "Rain idle must use Rain's own Avatar.");
                var clip = SingleClip();
                ValidateHumanClip(clip, importer);
                var overrides = previousOverride != null ? previousOverride : new AnimatorOverrideController(controller);
                overrides.name = "Rain Native Idle";
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrides.GetOverrides(pairs);
                Require(pairs.Count(p => p.Key == sharedIdle) == 1, "Shared idle was not found in override table.");
                overrides.ApplyOverrides(pairs.Select(p => new KeyValuePair<AnimationClip, AnimationClip>(p.Key,
                    p.Key == sharedIdle ? clip : null)).ToList());
                if (previousOverride == null) AssetDatabase.CreateAsset(overrides, OverridePath);
                else EditorUtility.SetDirty(overrides);

                // Explicitly use the override in isolated scenes; the general validator uses the shared controller.
                ValidatePreview(entry.previewPrefab, overrides, avatar);
                ValidatePreview(entry.prefab, overrides, avatar);
                var applied = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrides.GetOverrides(applied);
                Require(applied.All(p => p.Key == sharedIdle ? p.Value == clip : p.Value == null || p.Value == p.Key),
                    "A non-idle clip was overridden.");
                Require(Hash(ControllerPath) == sharedHash, "Shared controller bytes changed during Rain import.");
                entry.controllerOverride = overrides;
                Require(RegistryFingerprint(registry, entry) == registryOthers, "Another registry field changed.");
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssetIfDirty(overrides);
                AssetDatabase.SaveAssetIfDirty(registry);
                Debug.Log($"CTLIFE [rain-native-idle] PASS: {clip.length:F3}s; SHA256={sha}; " +
                    $"exact Rain entry override={OverridePath}; shared controller unchanged; creator/world hand/foot samples passed. " +
                    "Device creator, idle-to-walk and gesture-return review remain required.");
            }
            catch (Exception failure)
            {
                try
                {
                    foreach (string path in new[] { ClipPath, OverridePath })
                    {
                        if (preserved[path] == null) AssetDatabase.DeleteAsset(path);
                        else
                        {
                            RestoreFile(path, preserved[path]);
                            RestoreFile(path + ".meta", preserved[path + ".meta"]);
                            AssetDatabase.ImportAsset(path, ImportNow);
                        }
                    }
                    RestoreFile(RegistryPath, preserved[RegistryPath]);
                    AssetDatabase.ImportAsset(RegistryPath, ImportNow);
                    Require(Hash(ControllerPath) == sharedHash, "Unexpected shared-controller mutation; Rain importer never writes it.");
                    Debug.LogError("CTLIFE [rain-native-idle] Import rejected; prior Rain clip/override/registry restored. " + failure.Message);
                }
                catch (Exception rollback) { throw new AggregateException("Rain idle import and rollback both failed.", failure, rollback); }
                throw;
            }
        }

        private static string RegistryFingerprint(CharacterRegistry registry, CharacterRegistry.Entry target)
        {
            string Entry(CharacterRegistry.Entry e) => e == null ? "<null>" :
                $"{e.key}:{AssetId(e.prefab)}:{AssetId(e.previewPrefab)}:{e.height:R}:{e.variant}:" +
                (e == target ? "<target-override>" : AssetId(e.controllerOverride));
            return $"{AssetId(registry.controller)}|{AssetId(registry.defaultPlayer)}|{AssetId(registry.defaultNpc)}|" +
                string.Join("|", registry.looks.Select(Entry)) + "::" + string.Join("|", registry.archetypes.Select(Entry));
        }

        private sealed class SkinProbe
        {
            public SkinnedMeshRenderer skin;
            public Mesh baked;
            public int[] leftHand, rightHand, lowerBody;
        }

        private static void ValidatePreview(GameObject prefab, RuntimeAnimatorController controller, Avatar avatar)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            var probes = new List<SkinProbe>();
            try
            {
                root = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.SetActive(true);
                var animator = root.GetComponentsInChildren<Animator>(true).Single();
                Require(animator.avatar == avatar && avatar.isHuman && avatar.isValid, "Preview does not use Rain's valid own Avatar.");
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.enabled = true;
                animator.Rebind();
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var weights = skin.sharedMesh.boneWeights;
                    var names = skin.bones.Select(b => b.name).ToArray();
                    bool Hand(string name, string side) => name == "Hand." + side ||
                        (name.EndsWith("." + side, StringComparison.Ordinal) &&
                         new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }.Any(d => name.StartsWith(d, StringComparison.Ordinal)));
                    int[] Select(Func<string, bool> predicate)
                    {
                        var selected = new List<int>();
                        for (int i = 0; i < weights.Length; i++)
                        {
                            var w = weights[i];
                            float Weight(int bone, float weight) => weight > 0f && predicate(names[bone]) ? weight : 0f;
                            if (Weight(w.boneIndex0, w.weight0) + Weight(w.boneIndex1, w.weight1) +
                                Weight(w.boneIndex2, w.weight2) + Weight(w.boneIndex3, w.weight3) > 0.5f) selected.Add(i);
                        }
                        return selected.ToArray();
                    }
                    var probe = new SkinProbe { skin = skin, baked = new Mesh(), leftHand = Select(n => Hand(n, "L")),
                        rightHand = Select(n => Hand(n, "R")), lowerBody = Select(n => n == "Hips" || n.StartsWith("Thigh.", StringComparison.Ordinal)) };
                    probes.Add(probe);
                    Require(probe.leftHand.Length > 0 && probe.rightHand.Length > 0 && probe.lowerBody.Length > 0,
                        "Cannot identify weighted hand/thigh geometry in " + skin.name);
                }
                Require(probes.Count > 0, "Rain prefab has no skinned geometry.");
                var firstFeet = new Vector3[2];
                float minimumClearance = float.PositiveInfinity, maximumFootDrift = 0f, maximumSoleOffset = 0f;
                for (int sample = 0; sample <= 12; sample++)
                {
                    animator.Play("loco_idle", 0, sample / 12f);
                    animator.Update(0f);
                    var head = root.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Head).position);
                    Require(head.y > 0.9f, "Rain idle collapsed its head below standing height.");
                    Vector3 LocalBone(HumanBodyBones bone) => root.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
                    var leftHip = LocalBone(HumanBodyBones.LeftUpperLeg);
                    var rightHip = LocalBone(HumanBodyBones.RightUpperLeg);
                    // Body Orientation can reverse the source FBX X sign. Derive anatomical left from the hips;
                    // never assume Blender .L is Unity root-space +X. Facing is checked independently below.
                    var lateral = Vector3.ProjectOnPlane(leftHip - rightHip, Vector3.up).normalized;
                    var face = (LocalBone(HumanBodyBones.LeftEye) + LocalBone(HumanBodyBones.RightEye)) * 0.5f - head;
                    Require(lateral.sqrMagnitude > 0.9f, "Cannot determine Rain's anatomical lateral axis.");
                    if (sample == 0)
                        Debug.Log($"CTLIFE [rain-idle-basis] {prefab.name}: leftHip={leftHip:F5}, rightHip={rightHip:F5}, " +
                            $"leftHand={LocalBone(HumanBodyBones.LeftHand):F5}, rightHand={LocalBone(HumanBodyBones.RightHand):F5}, " +
                            $"face={face:F5}, leftAxis={lateral:F5}, visualYaw={animator.transform.localEulerAngles:F3}.");
                    Require(face.z > 0.02f, "Rain idle face points away from navigation forward: " + face);
                    int footIndex = 0;
                    foreach (var foot in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    {
                        var p = root.transform.InverseTransformPoint(animator.GetBoneTransform(foot).position);
                        if (sample == 0) firstFeet[footIndex] = p;
                        maximumFootDrift = Mathf.Max(maximumFootDrift, Vector3.Distance(p, firstFeet[footIndex++]));
                    }
                    foreach (var probe in probes)
                    {
                        probe.skin.BakeMesh(probe.baked);
                        var vertices = probe.baked.vertices.Select(v => root.transform.InverseTransformPoint(probe.skin.transform.TransformPoint(v))).ToArray();
                        Require(vertices.Length > 0 && vertices.All(v => !float.IsNaN(v.sqrMagnitude) && !float.IsInfinity(v.sqrMagnitude)), "Non-finite skinned idle vertices.");
                        float sole = vertices.Min(v => v.y), height = vertices.Max(v => v.y) - sole;
                        maximumSoleOffset = Mathf.Max(maximumSoleOffset, Mathf.Abs(sole));
                        Require(height > 1.4f && height < 2.2f && Mathf.Abs(sole) < 0.03f,
                            $"Rain idle height/sole failed {probe.skin.name}@{sample}/12: height={height:F4}, sole={sole:F4}");
                        foreach (var side in new[] { 1f, -1f })
                        {
                            var hand = side > 0 ? probe.leftHand : probe.rightHand;
                            float low = hand.Min(i => vertices[i].y) - 0.04f, high = hand.Max(i => vertices[i].y) + 0.04f;
                            var thigh = probe.lowerBody.Where(i => vertices[i].y >= low && vertices[i].y <= high).ToArray();
                            Require(thigh.Length > 0, "No thigh geometry at hand height.");
                            float gap = hand.Min(i => side * Vector3.Dot(vertices[i], lateral)) -
                                thigh.Max(i => side * Vector3.Dot(vertices[i], lateral));
                            if (sample == 0)
                            {
                                float oldXGap = hand.Min(i => side * vertices[i].x) - thigh.Max(i => side * vertices[i].x);
                                Debug.Log($"CTLIFE [rain-idle-gap] {probe.skin.name}: namedSide={(side > 0 ? "L" : "R")}, " +
                                    $"anatomicalGap={gap:F5}m, obsoleteAssumedXGap={oldXGap:F5}m, " +
                                    $"handVertices={hand.Length}, thighVertices={thigh.Length}, handHeight={low:F4}..{high:F4}m.");
                            }
                            minimumClearance = Mathf.Min(minimumClearance, gap);
                            Require(gap > 0.01f, $"Rain hand intersects/approaches thigh {probe.skin.name}@{sample}/12 side={side}: gap={gap:F4}m");
                        }
                    }
                }
                Require(maximumFootDrift < 0.02f, "Rain idle feet drift: " + maximumFootDrift);
                Debug.Log($"CTLIFE [rain-native-idle] Preview {prefab.name}: 13 phases × {probes.Count} skins; " +
                    $"minHandThighGap={minimumClearance:F4}m, maxFootDrift={maximumFootDrift:F4}m, maxSoleOffset={maximumSoleOffset:F4}m.");
            }
            finally
            {
                foreach (var probe in probes) Object.DestroyImmediate(probe.baked);
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Avatar OwnRainAvatar()
        {
            var importer = AssetImporter.GetAtPath(RainCharacterImport.ModelPath) as ModelImporter;
            var avatar = AssetDatabase.LoadAllAssetsAtPath(RainCharacterImport.ModelPath).OfType<Avatar>().SingleOrDefault();
            Require(importer != null && importer.animationType == ModelImporterAnimationType.Human &&
                importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel &&
                avatar != null && avatar.isHuman && avatar.isValid,
                "Import and validate Rain's own Humanoid Avatar before importing the idle.");
            return avatar;
        }

        private static void ValidateSourceReport(string path, string sha)
        {
            Require(File.Exists(path), "Missing native idle FBX roundtrip report: " + path);
            var report = JObject.Parse(File.ReadAllText(path));
            Require((bool?)report["all_pass"] == true,
                "Native idle FBX roundtrip report has not passed; resolve its failing checks before import.");
            Require(string.Equals((string)report["sha256"], sha, StringComparison.OrdinalIgnoreCase),
                "Native idle FBX changed since its roundtrip report; revalidate this exact source file.");
        }

        private static AnimationClip SingleClip()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            Require(clips.Length == 1 && clips[0].name == ClipName, "Expected one named native idle clip.");
            return clips[0];
        }

        private static KeyValuePair<HumanBodyBones, string>[] Digits() => CharacterValidation.SnowBoneMap()
            .Where(pair => pair.Value.StartsWith("Thumb", StringComparison.Ordinal) ||
                pair.Value.StartsWith("Index", StringComparison.Ordinal) || pair.Value.StartsWith("Middle", StringComparison.Ordinal) ||
                pair.Value.StartsWith("Ring", StringComparison.Ordinal) || pair.Value.StartsWith("Pinky", StringComparison.Ordinal)).ToArray();

        private static void ValidateRawDigits(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var digits = Digits();
            Require(digits.Length == 30, "Rain mapping no longer contains 30 digit joints.");
            foreach (var digit in digits)
            {
                var rotations = bindings.Where(b => b.type == typeof(Transform) &&
                    b.path.Split('/').Last() == digit.Value &&
                    (b.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal) ||
                     b.propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal) ||
                     b.propertyName.StartsWith("m_LocalEulerAngles", StringComparison.Ordinal))).ToArray();
                Require(rotations.Length >= 3, "Missing explicit source rotation channels for " + digit.Value);
                foreach (var binding in rotations)
                    Require(ValidCurve(AnimationUtility.GetEditorCurve(clip, binding), 2), "Invalid source digit curve: " + binding.path + "/" + binding.propertyName);
            }
            Debug.Log($"CTLIFE [native-idle] Source probe: {digits.Length}/30 digit joints have explicit finite rotation keys; " +
                $"{bindings.Length} imported bindings, {clip.length:F3}s. Humanoid conversion follows.");
        }

        private static void ValidateHumanClip(AnimationClip clip, ModelImporter importer)
        {
            Require(clip.humanMotion && !clip.legacy, "Native idle did not import as a Humanoid clip.");
            Require(Mathf.Abs(clip.length - 6f) <= 1f / 30f, "Expected the authored six-second idle; got " + clip.length);
            var settings = importer.clipAnimations;
            Require(settings.Length == 1 && settings[0].loopTime && settings[0].loopPose &&
                settings[0].lockRootRotation && settings[0].lockRootHeightY && settings[0].lockRootPositionXZ &&
                !settings[0].keepOriginalOrientation, "Native idle is not looping/in-place with Body Orientation.");
            var curves = AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.type == typeof(Animator))
                .Select(b => new { Binding = b, Curve = AnimationUtility.GetEditorCurve(clip, b) }).ToArray();
            Require(curves.Length > 0 && curves.All(c => ValidCurve(c.Curve, 1)), "Missing or invalid Humanoid curves.");
            Require(curves.Any(c => c.Curve.keys.Max(k => k.value) - c.Curve.keys.Min(k => k.value) > 0.00001f),
                "Native idle has no changing Humanoid curves.");
            var fingerBindings = new HashSet<string>();
            foreach (var digit in Digits())
            {
                var muscles = Enumerable.Range(0, 3).Select(axis => HumanTrait.MuscleFromBone((int)digit.Key, axis))
                    .Where(index => index >= 0).Select(index => NormalizeMuscle(HumanTrait.MuscleName[index])).ToArray();
                var matches = curves.Where(c => muscles.Contains(NormalizeMuscle(c.Binding.propertyName))).ToArray();
                Require(matches.Length > 0, "No converted finger-muscle curve for " + digit.Key +
                    "; imported Animator properties: " + string.Join(", ", curves.Select(c => c.Binding.propertyName)));
                foreach (var match in matches) fingerBindings.Add(match.Binding.propertyName);
            }
            Debug.Log($"CTLIFE [native-idle] Humanoid probe: 30/30 digit joints represented by {fingerBindings.Count} muscle curves; " +
                $"take={settings[0].takeName}; frames={settings[0].firstFrame}..{settings[0].lastFrame}; " +
                $"avatar={AssetDatabase.GetAssetPath(importer.sourceAvatar)}; curves={string.Join(", ", fingerBindings.OrderBy(s => s))}.");
        }

        // Unity animation bindings use e.g. LeftHand.Index.1 Stretched; HumanTrait names omit Hand/punctuation.
        private static string NormalizeMuscle(string name) => new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()).Replace("hand", "");
        private static bool ValidCurve(AnimationCurve curve, int minimumKeys) => curve != null && curve.length >= minimumKeys &&
            curve.keys.All(k => !float.IsNaN(k.time) && !float.IsInfinity(k.time) && !float.IsNaN(k.value) && !float.IsInfinity(k.value));
        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) =>
            machine.states.Select(s => s.state).Concat(machine.stateMachines.SelectMany(s => States(s.stateMachine)));
        private static AnimatorState FindIdle(AnimatorController controller)
        {
            var states = controller.layers.SelectMany(layer => States(layer.stateMachine)).Where(state => state.name == "loco_idle").ToArray();
            Require(states.Length == 1, "Expected one loco_idle state; found " + states.Length);
            return states[0];
        }
        private static Dictionary<string, string> StateMotions(AnimatorController controller) =>
            controller.layers.SelectMany(layer => States(layer.stateMachine)).ToDictionary(AssetId, state => AssetId(state.motion));
        private static string AssetId(Object value)
        {
            if (value == null) return "<null>";
            Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId),
                "Expected a saved controller/clip asset: " + value.name);
            return guid + ":" + localId;
        }
        private static byte[] ReadIfPresent(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
        private static void RestoreFile(string path, byte[] bytes)
        {
            if (bytes == null) { if (File.Exists(path)) File.Delete(path); }
            else File.WriteAllBytes(path, bytes);
        }
        private static string Hash(string path)
        {
            using var hash = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Rain native idle: " + message);
        }
    }
}
