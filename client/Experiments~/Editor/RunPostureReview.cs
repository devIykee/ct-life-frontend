using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Candidate-only root-orientation experiment. Never assigns production controllers or Avatars.</summary>
    public static class RunPostureReview
    {
        private const string CandidateRoot = "Assets/Art/characters/authored/MotionReview20261006/";
        private const string RunPath = "Assets/Art/animations/loco_run.fbx";
        private const string JogPath = "Assets/Art/characters/authored/LocomotionDiagnostic/loco_jog.fbx";
        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        private static string Output => Path.Combine(Repo, "../ct-life-evidence/art/character-candidates/motion-review-20261006/native-orientation");

        public static void PromoteRunOrientation()
        {
            const string expectedRunHash = "8c4f53caa05a528ba7554378617efa32b318d5ecd11cb2b7c0872fafe9ad4a48";
            if (Hash(RunPath) != expectedRunHash) throw new InvalidDataException("Run FBX changed since the measured candidate.");
            var candidate = (ModelImporter)AssetImporter.GetAtPath(CandidateRoot + "sprint-original-180.fbx");
            var source = (ModelImporter)AssetImporter.GetAtPath(RunPath);
            if (candidate == null || candidate.sourceAvatar != source.sourceAvatar || !candidate.clipAnimations.All(c =>
                    c.keepOriginalOrientation && Mathf.Abs(c.rotationOffset - 180f) < .001f))
                throw new InvalidDataException("Pinned candidate import settings are absent or changed.");
            foreach (string name in new[] { "snow", "rain" }) ValidateFrozenCandidate(name);
            string root = Path.GetDirectoryName(Output);
            string receipt = Path.Combine(root, "promotion-receipt.json");
            string backup = Path.Combine(Path.GetTempPath(), "ctlife-run-orientation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            byte[] metadata = File.ReadAllBytes(RunPath + ".meta");
            File.WriteAllBytes(Path.Combine(backup, "loco_run.fbx.meta"), metadata);
            var paths = new[] { RunPath, "Assets/Art/characters/male/body/ctl_male_body.fbx",
                "Assets/Resources/Characters/AvatarController.controller", "Assets/Resources/Characters/CharacterRegistry.asset",
                "Assets/Resources/Characters/SnowController.overrideController" };
            var protectedFiles = paths.SelectMany(p => new[] { p, p + ".meta" }).Where(p => p != RunPath + ".meta" && File.Exists(p))
                .ToDictionary(p => p, Hash);
            try
            {
                DirectMovementImport.ConfigureAuthoredRunOrientation();
                CapturePair("promoted-sprint", Clip(RunPath));
                foreach (string name in new[] { "snow", "rain" })
                {
                    var target = ReadCapture(name, "sprint-original-180");
                    var actual = ReadCapture(name, "promoted-sprint");
                    for (int i = 0; i <= 60; i++)
                    {
                        var a = actual["frames"][i]; var b = target["frames"][i];
                        foreach (var joint in ((JObject)b["joints"]).Properties())
                            if (Vector3.Distance(V(a["joints"][joint.Name]), V(joint.Value)) > .00001f)
                                throw new InvalidDataException($"Promoted native joint differs: {name} phase {i} {joint.Name}");
                        foreach (string shoe in new[] { "leftShoe", "rightShoe" })
                            if (Math.Abs((float)a[shoe]["minY"] - (float)b[shoe]["minY"]) > .00001f)
                                throw new InvalidDataException("Promoted native sole differs: " + name);
                    }
                }
                foreach (var pair in protectedFiles)
                    if (Hash(pair.Key) != pair.Value) throw new IOException("Protected source changed: " + pair.Key);
                File.WriteAllText(receipt, JsonConvert.SerializeObject(new {
                    state = "native-promoted; device acceptance pending", unity = Application.unityVersion, utc = DateTime.UtcNow,
                    sourceSha256 = Hash(RunPath), metadataSha256 = Hash(RunPath + ".meta"), backup,
                    keepOriginalOrientation = true, rotationOffset = 180f,
                    nativeComparisonToleranceMetres = .00001f, uniquePhasesPerCharacter = 60,
                    aliases = new[] { "loco_run", "react_run_away" }, protectedFiles,
                    limitation = "Orientation correction only. Existing run contact/sliding/cadence issues are unchanged. No controller state or muscle curve modification."
                }, Formatting.Indented));
                Debug.Log("CTLIFE [run-posture] Authored run direction promoted, 122 native phases match the frozen candidate. " + receipt);
            }
            catch (Exception ex)
            {
                File.WriteAllBytes(RunPath + ".meta", metadata);
                AssetDatabase.ImportAsset(RunPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                // Retain exact pre-run bytes even if Unity reserializes equivalent metadata during rollback.
                File.WriteAllBytes(RunPath + ".meta", metadata);
                File.WriteAllText(receipt, JsonConvert.SerializeObject(new { state = "failed; original run metadata restored", backup, error = ex.ToString() }, Formatting.Indented));
                throw;
            }
        }

        private static JObject ReadCapture(string name, string variant) =>
            JObject.Parse(File.ReadAllText(Path.Combine(Output, name + "-loco_run-" + variant + ".json")));
        private static Vector3 V(JToken value) => new Vector3((float)value[0], (float)value[1], (float)value[2]);
        private static void ValidateFrozenCandidate(string name)
        {
            var baseline = ReadCapture(name, "sprint-body-control"); var target = ReadCapture(name, "sprint-original-180");
            if ((string)baseline["sourceFileSha256"] != Hash(RunPath) || (string)target["sourceFileSha256"] != Hash(RunPath) ||
                baseline["frames"].Count() != 61 || target["frames"].Count() != 61)
                throw new InvalidDataException("Missing/changed full-cycle frozen candidate: " + name);
            float lateralMean = 0, facingMean = 0; bool crossesLeft = false, crossesRight = false;
            var firstA = baseline["frames"][0]["joints"]; var firstB = target["frames"][0]["joints"];
            var thighA = V(firstA["RightUpperLeg"]) - V(firstA["LeftUpperLeg"]);
            var thighB = V(firstB["RightUpperLeg"]) - V(firstB["LeftUpperLeg"]);
            thighA.y = thighB.y = 0;
            var rotation = Quaternion.AngleAxis(Vector3.SignedAngle(thighA, thighB, Vector3.up), Vector3.up);
            for (int i = 0; i <= 60; i++)
            {
                var a = baseline["frames"][i]; var b = target["frames"][i];
                foreach (var joint in ((JObject)b["joints"]).Properties())
                    if (Vector3.Distance(rotation * V(a["joints"][joint.Name]), V(joint.Value)) > .00001f)
                        throw new InvalidDataException("Candidate is not the measured pure heading correction: " + name);
                foreach (string shoe in new[] { "leftShoe", "rightShoe" })
                    if (Math.Abs((float)a[shoe]["minY"] - (float)b[shoe]["minY"]) > .00001f)
                        throw new InvalidDataException("Candidate changed sole height: " + name);
                if (i == 60) continue;
                var j = b["joints"]; var torso = V(j["Head"]) - V(j["Hips"]);
                float lateral = Mathf.Atan2(torso.x, torso.y) * Mathf.Rad2Deg;
                lateralMean += lateral / 60f; crossesLeft |= lateral < 0; crossesRight |= lateral > 0;
                var facing = (V(j["LeftEye"]) + V(j["RightEye"])) * .5f - V(j["Head"]);
                facingMean += Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg / 60f;
            }
            if (!crossesLeft || !crossesRight || Mathf.Abs(lateralMean) > 5f || Mathf.Abs(facingMean) > 2f)
                throw new InvalidDataException("Candidate heading/center-crossing check failed: " + name);
        }

        public static void CaptureOrientation()
        {
            Directory.CreateDirectory(Output);
            var paths = new[] { RunPath, JogPath, "Assets/Art/characters/male/body/ctl_male_body.fbx",
                "Assets/Resources/Characters/AvatarController.controller", "Assets/Resources/Characters/CharacterRegistry.asset",
                "Assets/Resources/Characters/SnowController.overrideController" };
            var protectedFiles = paths.SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists)
                .ToDictionary(p => p, File.ReadAllBytes);
            string receipt = Path.Combine(Output, "receipt.json");
            try
            {
                var captures = new List<object>();
                foreach (var source in new[] { ("sprint", RunPath), ("jog", JogPath) })
                {
                    var sourceClip = Clip(source.Item2);
                    CapturePair(source.Item1 + "-body-control", sourceClip);
                    captures.Add(new { variant = source.Item1 + "-body-control", path = source.Item2, sha256 = Hash(source.Item2) });
                    foreach (float offset in new[] { 0f, 180f })
                    {
                        string variant = source.Item1 + "-original-" + offset;
                        string path = CandidateRoot + variant + ".fbx";
                        ImportOrientation(source.Item2, path, offset);
                        var clip = Clip(path);
                        if (!clip.humanMotion || !clip.isLooping || Math.Abs(clip.length - sourceClip.length) > .001f)
                            throw new InvalidDataException("Candidate lost the source Humanoid loop: " + path);
                        CapturePair(variant, clip);
                        captures.Add(new { variant, path, sha256 = Hash(path) });
                    }
                }
                foreach (var pair in protectedFiles)
                    if (!File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) throw new IOException("Protected bytes changed: " + pair.Key);
                File.WriteAllText(receipt, JsonConvert.SerializeObject(new {
                    state = "candidate-only, native pose captured; no artistic or device acceptance",
                    unity = Application.unityVersion, utc = DateTime.UtcNow, captures,
                    protectedFiles = protectedFiles.Keys.ToDictionary(p => p, Hash),
                    rationale = "Compare Body-derived root forward against unchanged authored orientation at 0 and 180 degrees. Body Orientation averages upper/lower body relative to Avatar T-pose (Unity 6000.3 Manual/RootMotion)."
                }, Formatting.Indented));
                Debug.Log("CTLIFE [run-posture] 12 candidate/control cycles captured. Production files unchanged. " + receipt);
            }
            catch (Exception ex)
            {
                // Preserve failure evidence. No stale success receipt and no broad SaveAssets call.
                File.WriteAllText(receipt, JsonConvert.SerializeObject(new { state = "failed", error = ex.ToString() }, Formatting.Indented));
                throw;
            }
        }

        private static void ImportOrientation(string sourcePath, string destination, float offset)
        {
            Directory.CreateDirectory(CandidateRoot);
            byte[] bytes = File.ReadAllBytes(sourcePath);
            if (File.Exists(destination) && !File.ReadAllBytes(destination).SequenceEqual(bytes))
                throw new IOException("Candidate path contains different FBX bytes; preserve it before rerun: " + destination);
            if (!File.Exists(destination)) File.WriteAllBytes(destination, bytes);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var source = (ModelImporter)AssetImporter.GetAtPath(sourcePath);
            var mi = (ModelImporter)AssetImporter.GetAtPath(destination);
            mi.globalScale = source.globalScale; mi.useFileScale = source.useFileScale; mi.bakeAxisConversion = source.bakeAxisConversion;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            mi.sourceAvatar = source.sourceAvatar; mi.humanDescription = source.humanDescription;
            mi.animationCompression = source.animationCompression;
            mi.animationRotationError = source.animationRotationError; mi.animationPositionError = source.animationPositionError;
            mi.animationScaleError = source.animationScaleError;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = true; c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true; c.rotationOffset = offset;
                c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
            if (Hash(destination) != Hash(sourcePath)) throw new IOException("Candidate FBX content changed.");
        }

        private static void CapturePair(string variant, AnimationClip clip)
        {
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            var capture = typeof(LocomotionQualityProbe).GetMethod("CaptureCycle", BindingFlags.NonPublic | BindingFlags.Static);
            if (capture == null) throw new MissingMethodException("Native cycle capture method not found.");
            foreach (string name in new[] { "snow", "rain" })
            {
                var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + name));
                var controller = new AnimatorController { name = "UNSAVED_POSTURE_" + variant };
                var machine = new AnimatorStateMachine();
                AnimatorState state = null;
                try
                {
                    controller.AddParameter("speed", AnimatorControllerParameterType.Float);
                    state = machine.AddState("loco_run"); state.motion = clip; state.speed = 1f / 4.5f;
                    state.speedParameter = "speed"; state.speedParameterActive = true; state.iKOnFeet = false;
                    machine.defaultState = state;
                    controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine } };
                    capture.Invoke(null, new object[] { Output, name, "loco_run", 4.5f,
                        entry.previewPrefab != null ? entry.previewPrefab : entry.prefab,
                        controller, variant, (float?)4.5f, (bool?)false });
                }
                finally
                {
                    Object.DestroyImmediate(controller);
                    if (state != null) Object.DestroyImmediate(state);
                    Object.DestroyImmediate(machine);
                }
            }
        }
        private static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        private static string Hash(string path)
        {
            using var hash = SHA256.Create(); using var file = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
    }
}
