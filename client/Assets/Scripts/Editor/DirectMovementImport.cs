using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Reapply after ContentImport.SyncAll; does not rebuild the controller or replace any motion.</summary>
    public static class DirectMovementImport
    {
        private const string ControllerPath = "Assets/Resources/Characters/AvatarController.controller";
        internal const string RunClipPath = "Assets/Art/animations/loco_run.fbx";
        internal const string AuthoredRunOrientationMarker = "ct-life:authored-run-forward-v1";

        // The native original-orientation comparison measured a 23.8139-degree
        // sideways heading from Body-derived forward. Preserve the source's own
        // axis and rotate its -Z forward to navigation +Z. The promotion helper
        // pins the complete comparison before setting this opt-in marker.
        internal static bool UsesAuthoredRunOrientation(string path, ModelImporter importer) =>
            path == RunClipPath && importer.userData == AuthoredRunOrientationMarker;

        internal static void ConfigureAuthoredRunOrientation()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(RunClipPath);
            Require(importer != null, "Missing run clip importer.");
            Require(string.IsNullOrEmpty(importer.userData) || importer.userData == AuthoredRunOrientationMarker,
                "Run clip has unrelated userData; do not overwrite it.");
            importer.userData = AuthoredRunOrientationMarker;
            importer.SaveAndReimport();
            Require(importer.clipAnimations.Length > 0 && importer.clipAnimations.All(c =>
                c.keepOriginalOrientation && Mathf.Abs(c.rotationOffset - 180f) < .001f),
                "Run orientation was not retained by the import pipeline.");
        }
        // Native foot-support trajectories with Humanoid Foot IK fit 0.9845m/s (Rain)
        // and 1.0157m/s (Snow). Gameplay's walk ceiling is not this clip's authored speed.
        private const float WalkClipReferenceSpeed = 1f;

        [MenuItem("CT Life/Content/Configure Direct Locomotion Rate")]
        public static void ConfigureLocomotion()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Require(controller != null, "Missing AvatarController.");
            Require(controller.parameters.Any(p => p.name == "speed" && p.type == AnimatorControllerParameterType.Float),
                "The existing float speed parameter is missing.");
            Require(!AssetDatabase.LoadAllAssetsAtPath(ControllerPath).Any(EditorUtility.IsDirty), "Save pending controller edits first.");
            var all = controller.layers.SelectMany(l => States(l.stateMachine)).ToArray();
            var walk = all.Single(s => s.name == "loco_walk");
            var run = all.Single(s => s.name == "loco_run");
            var untouched = all.Where(s => s != walk && s != run).ToDictionary(s => s, EditorJsonUtility.ToJson);
            var motions = all.ToDictionary(s => s, s => s.motion);
            string guid = AssetDatabase.AssetPathToGUID(ControllerPath);
            byte[] previous = File.ReadAllBytes(ControllerPath);
            try
            {
                Configure(walk, WalkClipReferenceSpeed);
                walk.iKOnFeet = true;
                Configure(run, DirectMovementInput.RunSpeed);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                Require(untouched.All(pair => EditorJsonUtility.ToJson(pair.Key) == pair.Value), "An unrelated state changed.");
                Require(motions.All(pair => pair.Key.motion == pair.Value), "A motion changed.");
                Require(AssetDatabase.AssetPathToGUID(ControllerPath) == guid, "Controller GUID changed.");
                ValidateRates(controller);
                Require(walk.iKOnFeet, "Walk Foot IK did not persist.");
                Debug.Log($"CTLIFE [direct-locomotion] PASS: only walk/run rate settings and walk Foot IK changed; {untouched.Count} other states and " +
                    $"all {motions.Count} motions preserved. Walk 1x at {WalkClipReferenceSpeed}m/s; run 1x at {DirectMovementInput.RunSpeed}m/s.");
            }
            catch
            {
                File.WriteAllBytes(ControllerPath, previous);
                AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Debug.LogError("CTLIFE [direct-locomotion] Rejected; restored the previous controller.");
                throw;
            }
        }

        private static void Configure(AnimatorState state, float nominalSpeed)
        {
            state.speed = 1f / nominalSpeed;
            state.speedParameter = "speed";
            state.speedParameterActive = true;
            EditorUtility.SetDirty(state);
        }

        private static void ValidateRates(AnimatorController controller)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AuthoredCharacterImport.WorldPrefabPath);
            Require(prefab != null, "Snow world prefab is missing.");
            var instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                Require(animator != null && animator.avatar != null && animator.avatar.isValid, "Snow Animator is invalid.");
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.Rebind();
                foreach (var sample in new[]
                {
                    ("loco_walk", DirectMovementInput.WalkSpeed, DirectMovementInput.WalkSpeed / WalkClipReferenceSpeed),
                    ("loco_walk", DirectMovementInput.WalkSpeed * 0.5f, DirectMovementInput.WalkSpeed * 0.5f / WalkClipReferenceSpeed),
                    ("loco_walk", 1.75f, 1.75f / WalkClipReferenceSpeed),
                    ("loco_run", DirectMovementInput.RunSpeed, 1f),
                    ("loco_run", DirectMovementInput.RunSpeed * 0.5f, 0.5f),
                    ("loco_idle", 0f, 1f),
                })
                {
                    animator.SetFloat("speed", sample.Item2);
                    animator.Play(sample.Item1, 0, 0.2f);
                    animator.Update(0f);
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    float actual = state.speed * state.speedMultiplier;
                    Require(Mathf.Abs(actual - sample.Item3) < 0.001f,
                        $"{sample.Item1} actual playback rate {actual:F4} does not match {sample.Item3:F4} at {sample.Item2:F3}m/s.");
                    Debug.Log($"CTLIFE [direct-locomotion] state={sample.Item1}; measuredSpeedParameter={sample.Item2:F3}; rate={actual:F3}.");
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) => machine.states.Select(s => s.state)
            .Concat(machine.stateMachines.SelectMany(s => States(s.stateMachine)));
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Direct locomotion: " + message);
        }
    }
}
