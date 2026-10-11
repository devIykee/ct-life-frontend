using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// CMU motion-capture clips (free use, mocap.cs.cmu.edu; converted by tools/anim/build_mocap_clips.py: ASF/AMC → BVH
    /// → Blender 5.1.2 FBX, metres, 30 fps; art/processed/animations/mocap/mocap.json) imported as Humanoid clips:
    /// - every clip FBX carries its subject's ASF rest pose as the static node transforms, so each gets its OWN Avatar
    ///   (CreateFromThisModel) from the explicit CMU → Humanoid map and that rest pose after Unity's Enforce T-Pose (the
    ///   ASF rest has the legs splayed ~20°; Enforce T-Pose straightens them so muscle zero matches every other rig);
    /// - one-shots, orientation and height baked into the pose; the get-up keeps its travel too (Knockdown places the root
    ///   under the lying body at the start and under the standing body at the end), the stumble and run stop play on
    ///   the spot (their travel would otherwise pull the body away from the collider and the root).
    /// Menu: CT Life > Characters > Import CMU mocap clips (copies the processed FBX, then configures the Avatars).
    /// </summary>
    public static class MocapImport
    {
        public const string Root = "Assets/Art/animations/mocap";
        public const string AvatarTag = "ct-life:mocap-avatar-v2";
        private const string Source = "../art/processed/animations/mocap";
        internal static readonly HashSet<string> RestProbe = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Processed clip → controller state (CharacterFeelController adds the states).</summary>
        public static readonly (string file, string state)[] Clips =
        {
            ("getup_face_down.fbx", "act_get_up_front"), // CMU 140_02 "Get Up Face Down"
            ("trip.fbx", "react_trip"),                  // CMU 104_13 "StumbleWalk", trimmed to one stumble and recovery
            ("jog_stop.fbx", "loco_run_stop"),           // CMU 104_09 "JogStop"
        };

        /// <summary>
        /// CMU ASF bones → Humanoid. Every animated spine bone is mapped (an unmapped "in-between" bone's animation is
        /// dropped by Humanoid retargeting: Unity's Rig Error for lowerback 8–17°); upperneck stays in between Neck and Head.
        /// </summary>
        public static Dictionary<HumanBodyBones, string> BoneMap() => new Dictionary<HumanBodyBones, string>
        {
            [HumanBodyBones.Hips] = "root",
            [HumanBodyBones.LeftUpperLeg] = "lfemur", [HumanBodyBones.LeftLowerLeg] = "ltibia",
            [HumanBodyBones.LeftFoot] = "lfoot", [HumanBodyBones.LeftToes] = "ltoes",
            [HumanBodyBones.RightUpperLeg] = "rfemur", [HumanBodyBones.RightLowerLeg] = "rtibia",
            [HumanBodyBones.RightFoot] = "rfoot", [HumanBodyBones.RightToes] = "rtoes",
            [HumanBodyBones.Spine] = "lowerback", [HumanBodyBones.Chest] = "upperback", [HumanBodyBones.UpperChest] = "thorax",
            [HumanBodyBones.Neck] = "lowerneck", [HumanBodyBones.Head] = "head",
            [HumanBodyBones.LeftShoulder] = "lclavicle", [HumanBodyBones.LeftUpperArm] = "lhumerus",
            [HumanBodyBones.LeftLowerArm] = "lradius", [HumanBodyBones.LeftHand] = "lwrist",
            [HumanBodyBones.RightShoulder] = "rclavicle", [HumanBodyBones.RightUpperArm] = "rhumerus",
            [HumanBodyBones.RightLowerArm] = "rradius", [HumanBodyBones.RightHand] = "rwrist",
        };

        public static string PathOf(string file) => Root + "/" + file;

        [MenuItem("CT Life/Characters/Import CMU mocap clips")]
        public static void Run()
        {
            Directory.CreateDirectory(Root);
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", Source));
            foreach (var (file, _) in Clips)
            {
                string from = Path.Combine(src, file), to = PathOf(file);
                if (!File.Exists(from)) throw new FileNotFoundException("Processed mocap clip missing (run tools/anim/build_mocap_clips.py)", from);
                if (!File.Exists(to) || !File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(from))) File.Copy(from, to, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var (file, _) in Clips)
            {
                var path = PathOf(file);
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                if (mi.userData != AvatarTag || !AvatarValid(path)) Configure(path, mi);
                Debug.Log($"CTLIFE [mocap] {path}: avatar {(AvatarValid(path) ? "valid" : "INVALID")}, clip {Clip(path)?.length:F2} s");
            }
        }

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static AnimationClip Clip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));

        private static bool AvatarValid(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = model != null ? model.GetComponent<Animator>() : null;
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        /// <summary>The clip's own Avatar: the CMU map + this file's rest transforms after Enforce T-Pose (as CastImport).</summary>
        internal static void Configure(string path, ModelImporter mi)
        {
            RestProbe.Add(path);
            try { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport); }
            finally { RestProbe.Remove(path); }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidDataException("Mocap clip did not import: " + path);
            var human = new HumanDescription();
            var reference = UnityEngine.Object.Instantiate(model);
            try
            {
                reference.name = model.name;
                var map = BoneMap();
                var transforms = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var t in reference.GetComponentsInChildren<Transform>(true)) if (!transforms.ContainsKey(t.name)) transforms[t.name] = t;
                var missing = map.Values.Where(b => !transforms.ContainsKey(b)).ToList();
                if (missing.Count > 0) throw new InvalidDataException($"{path}: CMU bones missing: {string.Join(", ", missing)}");
                // Unity's Enforce T-Pose (AvatarSetupTool.MakePoseValid), as CastImport / AuthoredCharacterImport (6000.3).
                var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
                var wrapper = setup.GetNestedType("BoneWrapper", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var enforce = setup.GetMethod("MakePoseValid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (wrapper == null || enforce == null) throw new MissingMethodException("Unity Avatar T-pose tooling changed.");
                var bones = Array.CreateInstance(wrapper, HumanTrait.BoneCount);
                for (int i = 0; i < HumanTrait.BoneCount; i++)
                {
                    Transform bone = map.TryGetValue((HumanBodyBones)i, out var boneName) ? transforms[boneName] : null;
                    bones.SetValue(Activator.CreateInstance(wrapper, new object[] { HumanTrait.BoneName[i], bone }), i);
                }
                enforce.Invoke(null, new object[] { bones });
                human.skeleton = reference.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
                }).ToArray();
                human.human = map.Select(pair => new HumanBone
                {
                    boneName = pair.Value, humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true },
                }).ToArray();
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
            human.upperArmTwist = 0.5f; human.lowerArmTwist = 0.5f; human.upperLegTwist = 0.5f; human.lowerLegTwist = 0.5f;
            human.armStretch = 0.05f; human.legStretch = 0.05f; human.feetSpacing = 0; human.hasTranslationDoF = false;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.sourceAvatar = null;
            mi.humanDescription = human;
            mi.userData = AvatarTag;
            mi.SaveAndReimport();
            if (!AvatarValid(path)) throw new InvalidDataException("Mocap clip did not build a valid Humanoid Avatar: " + path);
        }
    }

    /// <summary>Import settings for Assets/Art/animations/mocap (CTLifeImportPostprocessor skips this folder).</summary>
    public sealed class MocapPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        private bool Ours => assetPath.StartsWith(MocapImport.Root + "/", StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            if (!Ours) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true; // Blender export (-Z forward, Y up), as the CC0 clip set
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.optimizeGameObjects = false;
            mi.importAnimation = !MocapImport.RestProbe.Contains(assetPath);
            if (MocapImport.RestProbe.Contains(assetPath)) { mi.animationType = ModelImporterAnimationType.None; return; }
            // The Avatar is the clip's own (MocapImport.Configure stores its HumanDescription in this .meta).
            mi.animationType = ModelImporterAnimationType.Human;
            if (mi.userData != MocapImport.AvatarTag)
                Debug.LogWarning("CTLIFE [mocap] Avatar not configured yet (CT Life > Characters > Import CMU mocap clips): " + assetPath);
        }

        private void OnPreprocessAnimation()
        {
            if (!Ours || MocapImport.RestProbe.Contains(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            string file = System.IO.Path.GetFileName(assetPath);
            foreach (var c in clips)
            {
                c.loopTime = false;
                c.lockRootRotation = true; c.keepOriginalOrientation = true; c.rotationOffset = 0f;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                // The get-up keeps its travel in the pose (Knockdown re-places the root at both ends). The stumble and
                // the stop lose theirs (it goes to root motion, which our actors do not apply): they play on the spot
                // while the actor's own root decelerates or is blocked.
                bool inPlace = file != "getup_face_down.fbx";
                c.lockRootPositionXZ = !inPlace; c.keepOriginalPositionXZ = !inPlace;
                // JogStop: the decelerating steps only (0.8–2.4 s: 1.9 m/s → standing), not the jog before them.
                if (file == "jog_stop.fbx") { c.firstFrame = 24f; c.lastFrame = 72f; }
            }
            mi.clipAnimations = clips;
        }
    }
}
