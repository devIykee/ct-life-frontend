using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Imports the separately authored Snow idle and changes only loco_idle.motion.
    /// CLI: -executeMethod CTLife.EditorTools.AuthoredIdleImport.ImportIdle
    /// Run after ContentImport.SyncAll, which rebuilds the shared controller from the legacy registry.
    /// </summary>
    public sealed class AuthoredIdleImport : AssetPostprocessor
    {
        public const string ClipPath = "Assets/Art/characters/authored/SnowIdle/SnowAdult_RelaxedIdle.fbx";
        private const string ClipName = "SnowAdult_RelaxedIdle";
        private const string ControllerPath = "Assets/Resources/Characters/AvatarController.controller";
        private const string ReadyTag = "ct-life:snow-native-idle-humanoid-v1";
        private const string ProbeTag = "ct-life:snow-native-idle-source-probe-v1";
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
            importer.sourceAvatar = human ? OwnSnowAvatar() : null;
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
            clip.keepOriginalOrientation = false; // Body Orientation, matching the corrected Snow locomotion clips.
            clip.rotationOffset = 0f;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            importer.clipAnimations = clips;
        }

        [MenuItem("CT Life/Content/Import Snow Native Idle")]
        public static void ImportIdle()
        {
            var source = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../art/character-candidates/unity-snow/animations/SnowAdult_RelaxedIdle.fbx"));
            Require(File.Exists(source), "Missing authored idle: " + source);
            string sha = Hash(source);
            ValidateSourceReport(Path.Combine(Path.GetDirectoryName(source), "idle-export-validation.json"), sha);
            var avatar = OwnSnowAvatar();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Require(controller != null, "Shared AvatarController is missing.");
            Require(!AssetDatabase.LoadAllAssetsAtPath(ControllerPath).Any(EditorUtility.IsDirty),
                "Save pending AvatarController edits before importing the idle; this operation preserves the saved controller.");
            var before = StateMotions(controller);
            var idle = FindIdle(controller);
            string idleId = AssetId(idle);
            string controllerGuid = AssetDatabase.AssetPathToGUID(ControllerPath);
            var priorController = File.ReadAllBytes(ControllerPath);
            var priorFbx = ReadIfPresent(ClipPath);
            var priorMeta = ReadIfPresent(ClipPath + ".meta");
            bool imported = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClipPath));
                imported = true; // Include importer metadata in rollback even if the file copy fails.
                if (AssetImporter.GetAtPath(ClipPath) is ModelImporter existing)
                {
                    existing.userData = ProbeTag;
                    AssetDatabase.WriteImportSettingsIfDirty(ClipPath);
                }
                File.Copy(source, ClipPath, true);
                AssetDatabase.ImportAsset(ClipPath, ImportNow);
                var importer = (ModelImporter)AssetImporter.GetAtPath(ClipPath);
                Require(importer != null && importer.animationType == ModelImporterAnimationType.Generic,
                    "The source binding probe did not import as Generic.");
                var raw = SingleClip();
                ValidateRawDigits(raw);

                importer.userData = ReadyTag;
                importer.SaveAndReimport();
                importer = (ModelImporter)AssetImporter.GetAtPath(ClipPath);
                Require(importer.animationType == ModelImporterAnimationType.Human &&
                    importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar == avatar,
                    "Native idle must copy Snow's own Avatar.");
                var clip = SingleClip();
                ValidateHumanClip(clip, importer);

                // Reacquire objects after FBX imports; a prior idle may itself reference this FBX on reruns.
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                idle = FindIdle(controller);
                Require(AssetId(idle) == idleId, "Idle state identity changed during import.");
                idle.motion = clip;
                EditorUtility.SetDirty(idle);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                var after = StateMotions(controller);
                Require(before.Count == after.Count && before.All(pair =>
                    after.TryGetValue(pair.Key, out var value) && (pair.Key == idleId || value == pair.Value)),
                    "A controller state other than loco_idle changed.");
                Require(AssetDatabase.AssetPathToGUID(ControllerPath) == controllerGuid, "Controller GUID changed.");
                CharacterValidation.ValidateSnowAsset(AuthoredCharacterImport.PrefabPath);
                CharacterValidation.ValidateSnowAsset(AuthoredCharacterImport.WorldPrefabPath);
                Debug.Log($"CTLIFE [native-idle] PASS: {ClipPath}; length={clip.length:F3}s; sourceSHA256={sha}; " +
                    $"only loco_idle.motion replaced; {before.Count - 1} other states preserved; creator/world numeric checks passed. " +
                    "Device review of hands, stance and the loop is still required.");
            }
            catch (Exception failure)
            {
                try
                {
                    if (imported)
                    {
                        if (priorFbx == null) AssetDatabase.DeleteAsset(ClipPath);
                        else
                        {
                            File.WriteAllBytes(ClipPath, priorFbx);
                            RestoreFile(ClipPath + ".meta", priorMeta);
                            AssetDatabase.ImportAsset(ClipPath, ImportNow);
                        }
                    }
                    File.WriteAllBytes(ControllerPath, priorController);
                    AssetDatabase.ImportAsset(ControllerPath, ImportNow);
                    Require(File.ReadAllBytes(ControllerPath).SequenceEqual(priorController), "Controller rollback bytes differ.");
                    Debug.LogError("CTLIFE [native-idle] Import rejected; previous idle asset and saved controller restored. " + failure.Message);
                }
                catch (Exception rollback)
                {
                    throw new AggregateException("Native idle import failed, and rollback also failed.", failure, rollback);
                }
                throw;
            }
        }

        private static Avatar OwnSnowAvatar()
        {
            var importer = AssetImporter.GetAtPath(AuthoredCharacterImport.ModelPath) as ModelImporter;
            var avatar = AssetDatabase.LoadAllAssetsAtPath(AuthoredCharacterImport.ModelPath).OfType<Avatar>().SingleOrDefault();
            Require(importer != null && importer.animationType == ModelImporterAnimationType.Human &&
                importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel &&
                avatar != null && avatar.isHuman && avatar.isValid,
                "Import and validate Snow's own Humanoid Avatar before importing the idle.");
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
            Require(digits.Length == 30, "Snow mapping no longer contains 30 digit joints.");
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
            if (!condition) throw new InvalidOperationException("Snow native idle: " + message);
        }
    }
}
