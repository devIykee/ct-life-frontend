// Candidate-only authored-source experiment. Coordinator may copy this helper into Editor.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
namespace CTLife.EditorTools
{
    public static class RunSourceRestReview
    {
        private const string Root = "Assets/Art/characters/authored/MotionReview20261006/SourceLibraryRest/";
        public static void Capture()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string source = Path.Combine(repo, "../ct-life-evidence/art/character-candidates/motion-review-20261006");
            string output = Path.Combine(source, "native-source-rest-library");
            Directory.CreateDirectory(output); Directory.CreateDirectory(Root);
            var paths = new[] { "Assets/Art/animations/loco_run.fbx", "Assets/Resources/Characters/AvatarController.controller",
                "Assets/Resources/Characters/CharacterRegistry.asset", "Assets/Resources/Characters/SnowController.overrideController" };
            var before = paths.SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists).ToDictionary(p => p, Hash);
            var facts = new List<object>();
            string restPath = Root + "UAL_SourceRest.fbx";
            byte[] restBytes = File.ReadAllBytes(Path.Combine(source, "UAL_SourceRest.fbx"));
            if (File.Exists(restPath) && !File.ReadAllBytes(restPath).SequenceEqual(restBytes)) throw new IOException("Different rest candidate exists.");
            if (!File.Exists(restPath)) File.WriteAllBytes(restPath, restBytes);
            AssetDatabase.ImportAsset(restPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var restImporter = (ModelImporter)AssetImporter.GetAtPath(restPath);
            restImporter.globalScale = 1; restImporter.useFileScale = true; restImporter.bakeAxisConversion = true;
            restImporter.importCameras = false; restImporter.importLights = false; restImporter.importAnimation = false;
            restImporter.materialImportMode = ModelImporterMaterialImportMode.None;
            restImporter.animationType = ModelImporterAnimationType.Generic;
            restImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            restImporter.sourceAvatar = null; restImporter.humanDescription = new HumanDescription();
            restImporter.SaveAndReimport();
            ConfigureOwnAvatar(AssetDatabase.LoadAssetAtPath<GameObject>(restPath), restImporter);
            var sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(restPath).OfType<Avatar>().Single();
            if (!sourceAvatar.isHuman || !sourceAvatar.isValid) throw new InvalidDataException("Rest source Avatar invalid.");
            foreach (string label in new[] { "Sprint", "Jog" })
            {
                string file = "UAL_Source" + label + ".fbx", path = Root + file;
                byte[] bytes = File.ReadAllBytes(Path.Combine(source, file));
                if (File.Exists(path) && !File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException("Candidate contains different source bytes: " + path);
                if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                mi.globalScale = 1; mi.useFileScale = true; mi.bakeAxisConversion = true;
                mi.importCameras = false; mi.importLights = false; mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.sourceAvatar = null; mi.humanDescription = new HumanDescription(); mi.optimizeGameObjects = false;
                mi.SaveAndReimport();
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                mi.sourceAvatar = sourceAvatar; mi.humanDescription = restImporter.humanDescription;
                mi.importAnimation = true; mi.animationCompression = ModelImporterAnimationCompression.Off;
                mi.SaveAndReimport();
                foreach (float offset in new[] { 0f, 180f })
                {
                    var clips = mi.defaultClipAnimations;
                    foreach (var clip in clips)
                    {
                        clip.loopTime = true; clip.lockRootRotation = true; clip.lockRootHeightY = true; clip.lockRootPositionXZ = true;
                        clip.keepOriginalOrientation = true; clip.rotationOffset = offset;
                        clip.keepOriginalPositionY = true; clip.keepOriginalPositionXZ = true;
                    }
                    mi.clipAnimations = clips; mi.SaveAndReimport();
                    var motion = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                    if (!motion.isLooping || !motion.humanMotion) throw new InvalidDataException("Source motion is not a Humanoid loop.");
                    var avatar = sourceAvatar;
                    if (!avatar.isHuman || !avatar.isValid) throw new InvalidDataException("Source Avatar invalid.");
                    string variant = "source-" + label.ToLowerInvariant() + "-original-" + offset;
                    CapturePair(output, variant, motion, false);
                    facts.Add(new { variant, source = path, sourceSha256 = Hash(path), avatar = avatar.name, ownSourceAvatar = true, footIk = false });
                }
            }
            foreach (var pair in before) if (Hash(pair.Key) != pair.Value) throw new IOException("Production source changed: " + pair.Key);
            File.WriteAllText(Path.Combine(output, "receipt.json"), JsonConvert.SerializeObject(new {
                state = "candidate-only; no promotion", unity = Application.unityVersion, captures = facts, protectedFiles = before,
                purpose = "Compare direct original CC0 UAL skeletal motion with a separately exported, genuinely unanimated rest Avatar. Tests whether animated-default reference contaminated the first source experiment. Runtime character Avatars remain their own."
            }, Formatting.Indented));
        }
        private static void ConfigureOwnAvatar(GameObject model, ModelImporter mi)
        {
            var template = (ModelImporter)AssetImporter.GetAtPath("Assets/Art/animations/loco_run.fbx");
            var reference = Object.Instantiate(model); reference.name = model.name;
            var human = new HumanDescription();
            try
            {
                var transforms = reference.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                human.human = template.humanDescription.human.Where(b => transforms.ContainsKey(b.boneName)).ToArray();
                var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
                var wrapper = setup.GetNestedType("BoneWrapper", BindingFlags.Public | BindingFlags.NonPublic);
                var enforce = setup.GetMethod("MakePoseValid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (wrapper == null || enforce == null) throw new MissingMethodException("Avatar T-pose tool changed.");
                var wrappers = Array.CreateInstance(wrapper, HumanTrait.BoneCount);
                for (int i = 0; i < HumanTrait.BoneCount; i++)
                {
                    var mapped = human.human.FirstOrDefault(b => b.humanName == HumanTrait.BoneName[i]);
                    Transform bone = !string.IsNullOrEmpty(mapped.boneName) ? transforms[mapped.boneName] : null;
                    wrappers.SetValue(Activator.CreateInstance(wrapper, new object[] { HumanTrait.BoneName[i], bone }), i);
                }
                enforce.Invoke(null, new object[] { wrappers });
                human.skeleton = reference.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
                }).ToArray();
            }
            finally { Object.DestroyImmediate(reference); }
            human.upperArmTwist = human.lowerArmTwist = human.upperLegTwist = human.lowerLegTwist = .5f;
            human.armStretch = human.legStretch = .05f;
            mi.humanDescription = human; mi.sourceAvatar = null;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; mi.animationType = ModelImporterAnimationType.Human;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.importAnimation = true; mi.SaveAndReimport();
        }
        private static void CapturePair(string output, string variant, AnimationClip clip, bool footIk)
        {
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            var capture = typeof(LocomotionQualityProbe).GetMethod("CaptureCycle", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (string name in new[] { "snow", "rain" })
            {
                var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + name));
                var controller = new AnimatorController { name = "UNSAVED_ORIGINAL_UAL_" + variant };
                var machine = new AnimatorStateMachine(); AnimatorState state = null;
                try
                {
                    controller.AddParameter("speed", AnimatorControllerParameterType.Float);
                    state = machine.AddState("loco_run"); state.motion = clip; state.speed = 1f / 4.5f;
                    state.speedParameter = "speed"; state.speedParameterActive = true; state.iKOnFeet = footIk;
                    machine.defaultState = state;
                    controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1, stateMachine = machine } };
                    capture.Invoke(null, new object[] { output, name, "loco_run", 4.5f, entry.previewPrefab != null ? entry.previewPrefab : entry.prefab,
                        controller, variant, (float?)4.5f, (bool?)footIk });
                }
                finally { Object.DestroyImmediate(controller); if (state != null) Object.DestroyImmediate(state); Object.DestroyImmediate(machine); }
            }
        }
        private static string Hash(string path)
        {
            using var hash = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
