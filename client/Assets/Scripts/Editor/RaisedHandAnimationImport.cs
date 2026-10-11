using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Completes the legacy mitten-rig stretch using the already authored, relaxed finger pose of each family.
    /// The source FBX and all non-finger curves remain unchanged. No runtime bone/physics correction is added.
    /// </summary>
    public static class RaisedHandAnimationImport
    {
        public const string SourcePath = "Assets/Art/animations/idle_fidget_stretch.fbx";
        public const string OutputRoot = "Assets/Art/animations/ArticulatedHands";
        public const string SharedControllerPath = "Assets/Resources/Characters/AvatarController.controller";

        public static bool IsDigitCurve(EditorCurveBinding binding) => binding.type == typeof(Animator) &&
            (binding.propertyName.StartsWith("LeftHand.", StringComparison.Ordinal) ||
             binding.propertyName.StartsWith("RightHand.", StringComparison.Ordinal));

        [MenuItem("CT Life/Content/Complete Snow and Rain stretch fingers")]
        public static void Run()
        {
            Directory.CreateDirectory(OutputRoot); AssetDatabase.Refresh();
            var source = Clip(SourcePath);
            var snow = Build(source, Clip(AuthoredIdleImport.ClipPath), "Snow");
            var rain = Build(source, Clip(RainIdleImport.ClipPath), "Rain");
            var shared = AssetDatabase.LoadAssetAtPath<AnimatorController>(SharedControllerPath);
            var stretch = shared.layers.SelectMany(layer => States(layer.stateMachine)).Single(state => state.name == "idle_fidget_stretch");
            if (stretch.motion != source && stretch.motion != snow)
                throw new InvalidDataException("Stretch was edited outside this importer; review the existing motion before replacing it.");
            stretch.motion = snow;
            EditorUtility.SetDirty(stretch); EditorUtility.SetDirty(shared);
            // Other CTS humans use the shared controller. Rain retains its own reference hand shape.
            ApplyOverride(CastImport.SnowControllerPath, snow, snow);
            ApplyOverride(CastImport.RainControllerPath, snow, rain);
            AssetDatabase.SaveAssets();
        }

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (Exception exception) { Debug.LogException(exception); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static AnimationClip Build(AnimationClip source, AnimationClip idle, string family)
        {
            var digits = AnimationUtility.GetCurveBindings(idle).Where(IsDigitCurve).ToArray();
            if (!source.humanMotion || !idle.humanMotion || digits.Length != 40)
                throw new InvalidDataException("Expected Humanoid source and all 40 authored finger muscle channels: " + family);
            string path = OutputRoot + "/" + family + "_RelaxedHandsStretch.anim";
            var result = Object.Instantiate(source); result.name = family + "_RelaxedHandsStretch";
            try
            {
                foreach (var binding in digits)
                {
                    // Use the authored idle's relaxed hand pose, not zero/default Humanoid muscles.
                    // Constant keys avoid an unrelated six-second breathing period driving a three-second gesture.
                    float value = AnimationUtility.GetEditorCurve(idle, binding).Evaluate(idle.length * .3f);
                    if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite digit curve: " + binding.propertyName);
                    AnimationUtility.SetEditorCurve(result, binding, AnimationCurve.Constant(0f, source.length, value));
                }
                var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (saved == null) { AssetDatabase.CreateAsset(result, path); saved = result; result = null; }
                else EditorUtility.CopySerialized(result, saved);
                EditorUtility.SetDirty(saved);
                Debug.Log($"CTLIFE [stretch-hands] {family}: 40 native finger channels; non-finger source curves preserved; {path}");
                return saved;
            }
            finally { if (result != null) Object.DestroyImmediate(result); }
        }

        private static void ApplyOverride(string path, AnimationClip sharedClip, AnimationClip replacement)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (controller == null) throw new InvalidDataException("Missing family controller: " + path);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); controller.GetOverrides(pairs);
            if (!pairs.Any(pair => pair.Key == sharedClip)) throw new InvalidDataException("Shared stretch clip is absent from " + path);
            controller.ApplyOverrides(pairs.Select(pair => new KeyValuePair<AnimationClip, AnimationClip>(pair.Key,
                pair.Key == sharedClip ? (replacement == sharedClip ? null : replacement) : pair.Value)).ToList());
            EditorUtility.SetDirty(controller);
        }

        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) => machine.states.Select(child => child.state)
            .Concat(machine.stateMachines.SelectMany(child => States(child.stateMachine)));

        private static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Single(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
    }
}
