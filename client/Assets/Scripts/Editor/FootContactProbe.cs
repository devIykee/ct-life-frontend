// Read-only production audit. Only a separate diagnostic Jog FBX is imported.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Generic;
using CTLife.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class FootContactProbe
    {
        // Mechanism test only: neutralize the two imported toe muscle curves in an UNSAVED copy.
        // This does not assume neutral toes are the right final gait or edit the shared source clip.
        public static void CaptureToeNeutral()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../art/character-candidates/locomotion-quality-20261006/native-toe-neutral"));
            Directory.CreateDirectory(output);
            var original = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/animations/loco_walk.fbx").OfType<AnimationClip>()
                .Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            string originalBytes = Convert.ToBase64String(File.ReadAllBytes(AssetDatabase.GetAssetPath(original)));
            var clone = Object.Instantiate(original); clone.name = "DIAGNOSTIC_WALK_TOES_NEUTRAL";
            var bindings = AnimationUtility.GetCurveBindings(clone).Where(b => b.propertyName == "Left Toes Up-Down" || b.propertyName == "Right Toes Up-Down").ToArray();
            if (bindings.Length != 2) throw new InvalidDataException("Expected exactly two imported toe muscle bindings.");
            try
            {
                foreach (var binding in bindings) AnimationUtility.SetEditorCurve(clone, binding,
                    AnimationCurve.Linear(0f, 0f, clone.length, 0f));
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                var capture = typeof(LocomotionQualityProbe).GetMethod("CaptureCycle", BindingFlags.NonPublic | BindingFlags.Static);
                foreach (string name in new[] { "snow", "rain" })
                foreach (var pair in new[] { ("toe-original", original), ("toe-neutral", clone) })
                {
                    var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + name));
                    var controller = new AnimatorController(); var machine = new AnimatorStateMachine(); AnimatorState state = null;
                    try
                    {
                        controller.AddParameter("speed", AnimatorControllerParameterType.Float);
                        state = machine.AddState("loco_walk"); state.motion = pair.Item2; state.speed = 1f;
                        state.speedParameter = "speed"; state.speedParameterActive = true; state.iKOnFeet = true;
                        machine.defaultState = state;
                        controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1, stateMachine = machine } };
                        capture.Invoke(null, new object[] { output, name, "loco_walk", 1.75f, entry.previewPrefab != null ? entry.previewPrefab : entry.prefab,
                            controller, pair.Item1, (float?)1f, (bool?)true });
                    }
                    finally { Object.DestroyImmediate(controller); if (state != null) Object.DestroyImmediate(state); Object.DestroyImmediate(machine); }
                }
            }
            finally { Object.DestroyImmediate(clone); }
            if (Convert.ToBase64String(File.ReadAllBytes(AssetDatabase.GetAssetPath(original))) != originalBytes)
                throw new InvalidOperationException("Diagnostic modified the original walk FBX.");
            Debug.Log("CTLIFE [toe-neutral-probe] Four unsaved original/neutral toe cycles captured; no source clip or controller saved.");
        }

        public static void Capture()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string output = Path.Combine(repo, "art/character-candidates/locomotion-quality-20261006/native-run-contact");
            Directory.CreateDirectory(output);
            const string runPath = "Assets/Art/animations/loco_run.fbx";
            const string walkPath = "Assets/Art/animations/loco_walk.fbx";
            const string jogPath = "Assets/Art/characters/authored/LocomotionDiagnostic/loco_jog.fbx";
            string Hash(string path) { using var hash = SHA256.Create(); using var stream = File.OpenRead(path); return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""); }
            var protectedHashes = new[] { runPath, walkPath, "Assets/Resources/Characters/AvatarController.controller", "Assets/Resources/Characters/CharacterRegistry.asset" }.ToDictionary(p => p, Hash);
            Directory.CreateDirectory(Path.GetDirectoryName(jogPath));
            File.Copy(Path.Combine(repo, "art/processed/animations/loco_jog.fbx"), jogPath, true);
            AssetDatabase.ImportAsset(jogPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var original = (ModelImporter)AssetImporter.GetAtPath(runPath);
            var mi = (ModelImporter)AssetImporter.GetAtPath(jogPath);
            mi.globalScale = original.globalScale; mi.useFileScale = original.useFileScale; mi.bakeAxisConversion = original.bakeAxisConversion;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            // This FBX was already authored against the same CTL animation skeleton as existing run.
            // Only its clip uses that import Avatar; modern models keep their own Snow/Rain Avatar.
            mi.sourceAvatar = original.sourceAvatar; mi.humanDescription = original.humanDescription;
            mi.animationCompression = original.animationCompression;
            mi.animationRotationError = original.animationRotationError; mi.animationPositionError = original.animationPositionError;
            mi.animationScaleError = original.animationScaleError;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = true; c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = false; c.rotationOffset = 0; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips; mi.SaveAndReimport();
            AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            var run = Clip(runPath); var jog = Clip(jogPath); var walk = Clip(walkPath);
            if (!jog.humanMotion || !jog.isLooping || jog.length < .5f) throw new InvalidDataException("Diagnostic jog is not a valid Humanoid loop.");
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            var facts = new List<object>();
            var capture = typeof(LocomotionQualityProbe).GetMethod("CaptureCycle", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (string name in new[] { "snow", "rain" })
            {
                var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + name));
                var prefab = entry.previewPrefab != null ? entry.previewPrefab : entry.prefab;
                var instance = Object.Instantiate(prefab);
                try
                {
                    var animator = instance.GetComponentInChildren<Animator>(); animator.Rebind();
                    var hd = animator.avatar.humanDescription;
                    facts.Add(new { character = name, avatar = AssetDatabase.GetAssetPath(animator.avatar),
                        animator.humanScale, animator.leftFeetBottomHeight, animator.rightFeetBottomHeight,
                        hd.feetSpacing, hd.legStretch, hd.hasTranslationDoF,
                        feetReference = hd.skeleton.Where(b => b.name.StartsWith("Foot.") || b.name.StartsWith("Toes.")).Select(b => new {
                            b.name, position = new[] { b.position.x, b.position.y, b.position.z },
                            rotation = new[] { b.rotation.x, b.rotation.y, b.rotation.z, b.rotation.w }, scale = new[] { b.scale.x, b.scale.y, b.scale.z } }) });
                }
                finally { Object.DestroyImmediate(instance); }
                foreach (var pair in new[] { ("sprint", run), ("jog", jog) })
                foreach (bool ik in new[] { false, true })
                {
                    var controller = new AnimatorController(); var machine = new AnimatorStateMachine(); AnimatorState state = null;
                    try
                    {
                        controller.AddParameter("speed", AnimatorControllerParameterType.Float);
                        state = machine.AddState("loco_run"); state.motion = pair.Item2;
                        state.speed = 1f / 4.5f; state.speedParameter = "speed"; state.speedParameterActive = true; state.iKOnFeet = ik;
                        machine.defaultState = state;
                        controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1, stateMachine = machine } };
                        capture.Invoke(null, new object[] { output, name, "loco_run", 4.5f, prefab, controller, pair.Item1 + (ik ? "-footik" : "-original"), (float?)4.5f, (bool?)ik });
                    }
                    finally { Object.DestroyImmediate(controller); if (state != null) Object.DestroyImmediate(state); Object.DestroyImmediate(machine); }
                }
            }
            var toeCurves = new[] { walk, run, jog }.Select(clip => new { clip = AssetDatabase.GetAssetPath(clip),
                bindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName.IndexOf("toe", StringComparison.OrdinalIgnoreCase) >= 0).Select(b => {
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    return new { b.path, b.propertyName, values = Enumerable.Range(0, 61).Select(i => curve.Evaluate(clip.length * i / 60f)).ToArray() }; }) });
            foreach (var pair in protectedHashes) if (Hash(pair.Key) != pair.Value) throw new InvalidOperationException("Diagnostic changed production asset " + pair.Key);
            File.WriteAllText(Path.Combine(output, "foot-reference-facts.json"), JsonConvert.SerializeObject(new { facts, toeCurves, protectedHashes }, Formatting.Indented));
            Debug.Log("CTLIFE [foot-contact-probe] Captured eight isolated run/jog cycles and own-Avatar foot/toe facts; production clips/controllers unchanged.");
        }
    }
}
