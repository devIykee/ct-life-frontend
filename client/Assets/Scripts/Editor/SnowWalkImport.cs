using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Promotes the inspected native foot-goal correction to the exact Snow look only.</summary>
    public static class SnowWalkImport
    {
        private const string Shared = "Assets/Resources/Characters/AvatarController.controller";
        private const string Registry = "Assets/Resources/Characters/CharacterRegistry.asset";
        private const string Candidate = "Assets/Art/characters/authored/LocomotionCandidate/Snow_WalkContact_Goals_Candidate.anim";
        private const string ClipPath = AuthoredCharacterImport.SnowRoot + "SnowWalkContact.anim";
        private const string OverridePath = "Assets/Resources/Characters/SnowController.overrideController";
        private const string CandidateSha = "5e469ab0eb724bab72e105c422c80a2e561f412d996ef57ad3123ee1a6606aa7";
        private const string ReceiptSha = "b75e514d2a4426de69772a7de7217c06cf7cafc26c51a558e23529ed434f911e";

        [MenuItem("CT Life/Content/Apply Snow Walk Contact")]
        public static void ImportWalk()
        {
            Require(Application.isBatchMode, "Run in a dedicated batch editor to protect unsaved interactive work.");
            string evidence = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../art/character-candidates/locomotion-quality-20261006/native-contact-goal-candidate/candidate-receipt.json"));
            Require(Hash(evidence) == ReceiptSha && Hash(Candidate) == CandidateSha, "Reviewed candidate/evidence bytes changed.");
            var report = JObject.Parse(File.ReadAllText(evidence));
            var snow = report["cases"].Single(c => (string)c["character"] == "snow");
            Require((bool)snow["all_pass"] && (int)snow["dense_phases"] == 241, "Snow native contact gate did not pass.");
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(Registry);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Shared);
            Require(registry != null && controller != null, "Registry/controller missing.");
            var entry = registry.looks.Single(e => e.key == string.Join("|", AuthoredCharacterImport.Look));
            var states = controller.layers.SelectMany(l => States(l.stateMachine)).ToArray();
            var walk = states.Single(s => s.name == "loco_walk");
            var original = walk.motion as AnimationClip;
            Require(original != null && states.Count(s => s.motion == original) == 1 && walk.iKOnFeet,
                "The walk clip must belong only to loco_walk with Foot IK enabled.");
            Require(Hash(Shared) == (string)report["protected_hashes"][Shared] &&
                Hash(AssetDatabase.GetAssetPath(original)) == (string)report["protected_hashes"][AssetDatabase.GetAssetPath(original)],
                "Source walk/controller no longer match the inspected evidence.");
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Candidate);
            Require(source != null && source.humanMotion && Mathf.Abs(source.length - original.length) < .00001f, "Invalid native candidate.");
            ValidateCurves(original, source);
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
            Require(entry.controllerOverride == null || entry.controllerOverride == existing, "Snow has a different authored controller; refusing to replace it.");
            Require(!EditorUtility.IsDirty(registry) && (existing == null || !EditorUtility.IsDirty(existing)), "Save pending Snow edits first.");
            if (existing != null)
            {
                var old = new List<KeyValuePair<AnimationClip, AnimationClip>>(); existing.GetOverrides(old);
                Require(existing.runtimeAnimatorController == controller && old.All(p => p.Key == original || p.Value == null || p.Value == p.Key),
                    "Existing Snow override contains another motion.");
            }
            string others = Fingerprint(registry, entry), sharedHash = Hash(Shared);
            var previousController = entry.controllerOverride;
            var preserved = new[] { ClipPath, ClipPath + ".meta", OverridePath, OverridePath + ".meta", Registry }
                .ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
            try
            {
                File.Copy(Candidate, ClipPath, true);
                AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
                var overrides = existing != null ? existing : new AnimatorOverrideController(controller);
                overrides.name = "Snow Walk Contact";
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); overrides.GetOverrides(pairs);
                Require(pairs.Count(p => p.Key == original) == 1, "Walk clip not uniquely present in override table.");
                overrides.ApplyOverrides(pairs.Select(p => new KeyValuePair<AnimationClip, AnimationClip>(p.Key, p.Key == original ? clip : null)).ToList());
                if (existing == null) AssetDatabase.CreateAsset(overrides, OverridePath);
                else EditorUtility.SetDirty(overrides);
                string posesPath = Path.Combine(Path.GetDirectoryName(evidence), "Snow_WalkContact_Goals-dense-candidate.json");
                Require(Hash(posesPath) == "84d00cc38555c4c2af9f2c29da0f9fb970f71b435ff67db6b1fcc3c904bb2cd1", "Inspected dense pose evidence changed.");
                float minimumSole = ValidateWorld(entry.prefab, overrides, posesPath);
                entry.controllerOverride = overrides;
                Require(Fingerprint(registry, entry) == others && Hash(Shared) == sharedHash, "Unrelated character/controller state changed.");
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssetIfDirty(overrides); AssetDatabase.SaveAssetIfDirty(registry);
                var result = new JObject { ["status"] = "native_pass_device_pending", ["look"] = entry.key,
                    ["candidate_sha256"] = CandidateSha, ["evidence_sha256"] = ReceiptSha, ["shared_controller_sha256"] = sharedHash,
                    ["override"] = OverridePath, ["world_phases"] = 241, ["minimum_world_tier_sole_m"] = minimumSole,
                    ["other_registry_fields_preserved"] = true, ["other_motion_overrides"] = 0 };
                File.WriteAllText("Logs/snow-walk-import.json", result.ToString());
                Debug.Log("CTLIFE [snow-walk] PASS: exact Snow look only; 241 phases across world tiers; minimum sole=" + minimumSole.ToString("F6") +
                    "m. Shared controller and other look entries preserved. Continuous device review required.");
            }
            catch
            {
                entry.controllerOverride = previousController; EditorUtility.ClearDirty(registry);
                foreach (string path in new[] { ClipPath, OverridePath })
                    if (preserved[path] == null) AssetDatabase.DeleteAsset(path);
                foreach (var pair in preserved.Where(p => p.Value != null)) File.WriteAllBytes(pair.Key, pair.Value);
                Debug.LogError("CTLIFE [snow-walk] Rejected; restored prior Snow clip/override/registry bytes.");
                throw;
            }
        }

        private static void ValidateCurves(AnimationClip original, AnimationClip candidate)
        {
            var before = AnimationUtility.GetCurveBindings(original); var after = AnimationUtility.GetCurveBindings(candidate);
            Require(before.Length == after.Length && before.All(after.Contains), "Curve bindings changed.");
            var allowed = new HashSet<string>(new[] { "LeftFootT.x", "LeftFootT.y", "LeftFootT.z", "RightFootT.x", "RightFootT.y", "RightFootT.z" });
            foreach (var binding in before)
            {
                if (binding.type == typeof(Animator) && allowed.Contains(binding.propertyName)) continue;
                var a = AnimationUtility.GetEditorCurve(original, binding); var b = AnimationUtility.GetEditorCurve(candidate, binding);
                Require(a.preWrapMode == b.preWrapMode && a.postWrapMode == b.postWrapMode && a.keys.SequenceEqual(b.keys),
                    "Non-foot-goal curve changed: " + binding.propertyName);
            }
        }

        private static float ValidateWorld(GameObject prefab, RuntimeAnimatorController controller, string posesPath)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null;
            var probes = new List<(SkinnedMeshRenderer skin, Mesh baked, int[] feet)>();
            try
            {
                root = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root, scene);
                var animator = root.GetComponentsInChildren<Animator>(true).Single();
                Require(animator.avatar != null && animator.avatar.isValid && animator.isHuman, "Invalid Snow Avatar.");
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false; animator.enabled = true; animator.Rebind(); animator.SetFloat("speed", 1.75f);
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bones = skin.bones; var weights = skin.sharedMesh.boneWeights;
                    var feet = Enumerable.Range(0, weights.Length).Where(i =>
                    {
                        var w = weights[i];
                        float W(int bone, float weight) => weight > 0 && (bones[bone].name.StartsWith("Foot.") || bones[bone].name.StartsWith("Toes.")) ? weight : 0;
                        return W(w.boneIndex0,w.weight0) + W(w.boneIndex1,w.weight1) + W(w.boneIndex2,w.weight2) + W(w.boneIndex3,w.weight3) > .5f;
                    }).ToArray();
                    Require(feet.Length > 0, "No weighted footwear on " + skin.name);
                    probes.Add((skin, new Mesh(), feet));
                }
                Require(probes.Count >= 3, "Expected all three Snow world tiers.");
                var frames = (JArray)JObject.Parse(File.ReadAllText(posesPath))["frames"];
                Require(frames.Count == 241, "Dense candidate phase evidence is incomplete.");
                float minimum = float.PositiveInfinity;
                for (int i = 0; i <= 240; i++)
                {
                    animator.Play("loco_walk", 0, i / 240f); animator.Update(0f);
                    for (int tier = 0; tier < probes.Count; tier++)
                    {
                        var p = probes[tier];
                        p.skin.BakeMesh(p.baked, false); var vertices = p.baked.vertices;
                        var matrix = root.transform.worldToLocalMatrix * p.skin.localToWorldMatrix;
                        float sole = p.feet.Min(v => matrix.MultiplyPoint3x4(vertices[v]).y);
                        float expected = Mathf.Min((float)frames[i]["tierLeft"][tier], (float)frames[i]["tierRight"][tier]);
                        // Preserve the inspected full cycle, including its original ~10mm
                        // uncorrected flight. The 4mm candidate gate applied only to corrected support.
                        Require(!float.IsNaN(sole) && sole >= -.003f && Mathf.Abs(sole - expected) <= .0002f,
                            "World walk contact failed " + p.skin.name + " phase=" + i + " sole=" + sole);
                        minimum = Mathf.Min(minimum, sole);
                    }
                }
                return minimum;
            }
            finally
            {
                foreach (var p in probes) Object.DestroyImmediate(p.baked);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static string Fingerprint(CharacterRegistry registry, CharacterRegistry.Entry target)
        {
            string Id(Object value) => value == null ? "null" : AssetDatabase.GetAssetPath(value) + ":" + value.GetInstanceID();
            string Row(CharacterRegistry.Entry e) => $"{e.key}|{Id(e.prefab)}|{Id(e.previewPrefab)}|{e.height:R}|{e.variant}|" + (e == target ? "<override>" : Id(e.controllerOverride));
            return $"{Id(registry.controller)}|{Id(registry.defaultPlayer)}|{Id(registry.defaultNpc)}|" + string.Join(";", registry.looks.Select(Row)) + "::" + string.Join(";", registry.archetypes.Select(Row));
        }
        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) => machine.states.Select(s => s.state)
            .Concat(machine.stateMachines.SelectMany(s => States(s.stateMachine)));
        private static string Hash(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Snow walk: " + message); }
    }
}
