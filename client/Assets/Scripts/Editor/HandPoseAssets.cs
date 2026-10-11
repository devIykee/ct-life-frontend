using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Emote hand poses (Phase A feel pass, 2026-10-17). FeelProbe measured every emote, locomotion and seat clip in the project
    /// with all 40 finger muscles at exactly 0: a thumbs-up had no thumb, a point no finger, a fist pump an open hand. This
    /// writes humanoid finger-muscle clips (rig-independent, so every cast family retargets them), a fingers-only AvatarMask and
    /// a "Hands" override layer on the shared AvatarController; HandPoses drives it from AvatarView.PlayOneShot.
    /// Batch: Unity -batchmode -executeMethod CTLife.EditorTools.HandPoseAssets.Build
    /// </summary>
    internal static class HandPoseAssets
    {
        public const string Folder = "Assets/Art/animations/hands";
        public const string ControllerPath = "Assets/Resources/Characters/AvatarController.controller";

        // Muscle values per joint: 1 Stretched, 2 Stretched, 3 Stretched, Spread (Unity humanoid: -1 = curled, +1 = straight).
        private static readonly Dictionary<string, (float[] thumb, float[] finger, float[] index)> Poses = new Dictionary<string, (float[], float[], float[])>
        {
            ["relaxed"] = (new[] { -0.15f, -0.2f, -0.2f, 0.1f }, new[] { -0.25f, -0.35f, -0.3f, 0f }, new[] { -0.15f, -0.25f, -0.25f, 0f }),
            ["open"] = (new[] { 0.4f, 0.5f, 0.5f, 0.6f }, new[] { 0.55f, 0.6f, 0.55f, 0.45f }, new[] { 0.6f, 0.6f, 0.55f, 0.35f }),
            ["fist"] = (new[] { -0.5f, -0.7f, -0.9f, -0.4f }, new[] { -1f, -1f, -1f, 0f }, new[] { -1f, -1f, -1f, 0f }),
            ["point"] = (new[] { -0.5f, -0.7f, -0.9f, -0.4f }, new[] { -1f, -1f, -1f, 0f }, new[] { 0.65f, 0.75f, 0.75f, 0f }),
            ["thumbs"] = (new[] { 0.7f, 0.85f, 0.85f, 0.7f }, new[] { -1f, -1f, -1f, 0f }, new[] { -1f, -1f, -1f, 0f }),
            ["grip"] = (new[] { -0.2f, -0.4f, -0.5f, 0.5f }, new[] { -0.6f, -0.75f, -0.6f, 0f }, new[] { -0.55f, -0.7f, -0.55f, 0f }),
            ["curve"] = (new[] { 0.1f, -0.2f, -0.3f, 0.6f }, new[] { -0.35f, -0.45f, -0.35f, -0.1f }, new[] { -0.3f, -0.4f, -0.3f, -0.1f }),
        };

        [MenuItem("CT Life/Characters/Build emote hand poses")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            var mask = Mask();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new FileNotFoundException(ControllerPath);
            // Rebuild the layer idempotently.
            var layers = controller.layers.Where(l => l.name != HandPoses.Layer).ToArray();
            controller.layers = layers;
            var sm = new AnimatorStateMachine { name = HandPoses.Layer, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            var empty = sm.AddState(HandPoses.None);
            sm.defaultState = empty;
            foreach (var combo in HandPoses.Map.Values.Distinct())
            {
                var clip = Clip(combo.left, combo.right);
                var state = sm.AddState(HandPoses.StateName(combo.left, combo.right));
                state.motion = clip;
                state.writeDefaultValues = false;
            }
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = HandPoses.Layer, stateMachine = sm, avatarMask = mask, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f,
            });
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"CTLIFE [hands] layer '{HandPoses.Layer}' with {sm.states.Length} states on {ControllerPath}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static AvatarMask Mask()
        {
            string path = Folder + "/fingers.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, path); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, i == (int)AvatarMaskBodyPart.LeftFingers || i == (int)AvatarMaskBodyPart.RightFingers);
            mask.transformCount = 0;
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static AnimationClip Clip(string left, string right)
        {
            string path = $"{Folder}/hand_{left}_{right}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves();
            clip.name = $"hand_{left}_{right}";
            foreach (var (side, pose) in new[] { ("LeftHand", left), ("RightHand", right) })
            {
                var p = Poses[pose];
                foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    var v = finger == "Thumb" ? p.thumb : finger == "Index" ? p.index : p.finger;
                    Set(clip, $"{side}.{finger}.1 Stretched", v[0]);
                    Set(clip, $"{side}.{finger}.2 Stretched", v[1]);
                    Set(clip, $"{side}.{finger}.3 Stretched", v[2]);
                    Set(clip, $"{side}.{finger}.Spread", v[3]);
                }
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void Set(AnimationClip clip, string muscle, float value) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), AnimationCurve.Constant(0f, 1f, value));
    }
}
