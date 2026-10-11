// Diagnostic only: root copies into Editor, runs, then may remove the copied helper.
// No imports, SaveAssets, controller edits, prefab writes or production mutations.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class LocomotionQualityProbe
    {
        public static void Capture()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../art/character-candidates/locomotion-quality-20261006/native-current"));
            string[] args = Environment.GetCommandLineArgs();
            int arg = Array.IndexOf(args, "-ctLocomotionProbeOut");
            if (arg >= 0 && arg + 1 < args.Length) output = Path.GetFullPath(args[arg + 1]);
            Directory.CreateDirectory(output);
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            if (registry == null) throw new InvalidOperationException("Missing character registry");
            foreach (string name in new[] { "snow", "rain" })
            {
                var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + name));
                foreach (string state in new[] { "loco_walk", "loco_run" })
                    CaptureCycle(output, name, state, state == "loco_walk" ? 1.75f : 4.5f,
                        entry.previewPrefab != null ? entry.previewPrefab : entry.prefab,
                        entry.controllerOverride != null ? entry.controllerOverride : registry.controller);
            }
            Debug.Log("CTLIFE [locomotion-probe] Captured four complete native cycles, without changing production assets: " + output);
        }

        // An isolated one-state controller reproduces only the walk state: original clip, own Avatar,
        // no transitions. It never saves or edits the shared controller or character overrides.
        public static void CaptureCadenceCandidates()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../art/character-candidates/locomotion-quality-20261006/native-cadence"));
            Directory.CreateDirectory(output);
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            foreach (string name in new[] { "snow", "rain" })
            {
                var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + name));
                var original = entry.controllerOverride != null ? entry.controllerOverride : registry.controller;
                var baseController = original is AnimatorOverrideController over ? over.runtimeAnimatorController : original;
                var walk = ((AnimatorController)baseController).layers[0].stateMachine.states.Single(x => x.state.name == "loco_walk").state;
                var clip = (AnimationClip)walk.motion;
                if (original is AnimatorOverrideController overrides && overrides[clip] != null) clip = overrides[clip];
                string[] paths = { AssetDatabase.GetAssetPath(original), AssetDatabase.GetAssetPath(baseController), AssetDatabase.GetAssetPath(clip) };
                var before = paths.Distinct().ToDictionary(p => p, Hash);
                foreach (string variant in new[] { "control", "calibrated", "calibrated-footik" })
                {
                    float reference = variant == "control" ? 2.2f : name == "rain" ? 1.0464f : .9392f;
                    var controller = new AnimatorController { name = "DIAGNOSTIC_UNSAVED_" + variant };
                    var machine = new AnimatorStateMachine { name = "Diagnostic" };
                    AnimatorState state = null;
                    try
                    {
                        controller.AddParameter("speed", AnimatorControllerParameterType.Float);
                        state = machine.AddState("loco_walk"); state.motion = clip;
                        state.speed = 1f / reference; state.speedParameter = "speed"; state.speedParameterActive = true;
                        state.iKOnFeet = variant == "calibrated-footik";
                        machine.defaultState = state;
                        controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine } };
                        CaptureCycle(output, name, "loco_walk", 1.75f,
                            entry.previewPrefab != null ? entry.previewPrefab : entry.prefab, controller, variant, reference, state.iKOnFeet);
                    }
                    finally
                    {
                        Object.DestroyImmediate(controller);
                        if (state != null) Object.DestroyImmediate(state);
                        Object.DestroyImmediate(machine);
                    }
                }
                foreach (var pair in before) if (Hash(pair.Key) != pair.Value)
                    throw new InvalidOperationException("Diagnostic changed source asset " + pair.Key);
            }
            Debug.Log("CTLIFE [locomotion-cadence] Six isolated walk cycles captured; source controller/clip hashes unchanged.");
        }

        private static void CaptureCycle(string output, string character, string stateName, float speed,
            GameObject prefab, RuntimeAnimatorController controller, string variant = null, float? referenceSpeed = null, bool? footIK = null)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            Mesh baked = new Mesh();
            string stem = character + "-" + stateName + (variant != null ? "-" + variant : "");
            try
            {
                root = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = root.GetComponentsInChildren<Animator>(true).Single();
                var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh.vertexCount).First();
                if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException("Invalid own Humanoid Avatar for " + character);
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.enabled = true;
                animator.Rebind();
                var weights = skin.sharedMesh.boneWeights;
                string[] boneNames = skin.bones.Select(b => b.name).ToArray();
                int[] Select(params string[] wanted)
                {
                    var names = new HashSet<string>(wanted);
                    return Enumerable.Range(0, weights.Length).Where(i =>
                    {
                        var w = weights[i];
                        float W(int b, float v) => v > 0f && names.Contains(boneNames[b]) ? v : 0f;
                        return W(w.boneIndex0, w.weight0) + W(w.boneIndex1, w.weight1) +
                            W(w.boneIndex2, w.weight2) + W(w.boneIndex3, w.weight3) > 0.5f;
                    }).ToArray();
                }
                var leftShoe = Select("Foot.L", "Toes.L");
                var rightShoe = Select("Foot.R", "Toes.R");
                if (leftShoe.Length == 0 || rightShoe.Length == 0) throw new InvalidOperationException("Cannot identify weighted footwear");
                Vector3 LocalBone(HumanBodyBones b) => root.transform.InverseTransformPoint(animator.GetBoneTransform(b).position);
                var jointNames = new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftEye, HumanBodyBones.RightEye,
                    HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
                    HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
                    HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                    HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
                var restJoints = jointNames.ToDictionary(b => b.ToString(), b => V(LocalBone(b)));
                var frames = new List<object>();
                var renderedPhases = new HashSet<int>(Enumerable.Range(0, 8).Select(i => Mathf.RoundToInt(i * 60f / 8)));
                AnimationClip clip = null;
                float period = 0f, rate = 0f;
                for (int frame = 0; frame <= 60; frame++)
                {
                    animator.SetFloat("speed", speed);
                    animator.Play(stateName, 0, frame / 60f);
                    animator.Update(0f);
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    var clips = animator.GetCurrentAnimatorClipInfo(0);
                    if (clips.Length != 1) throw new InvalidOperationException("Expected a single current clip");
                    clip = clips[0].clip;
                    rate = state.speed * state.speedMultiplier;
                    if (rate <= 0f) throw new InvalidOperationException("Nonpositive locomotion playback rate");
                    period = clip.length / rate;
                    skin.BakeMesh(baked);
                    Vector3[] vertices = baked.vertices.Select(v => root.transform.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                    if (vertices.Any(v => float.IsNaN(v.sqrMagnitude) || float.IsInfinity(v.sqrMagnitude)))
                        throw new InvalidOperationException("Non-finite skinned mesh");
                    object Shoe(int[] indices)
                    {
                        float minY = indices.Min(i => vertices[i].y);
                        var low = indices.Where(i => vertices[i].y < minY + .015f).ToArray();
                        return new { minY, maxY = indices.Max(i => vertices[i].y),
                            minZ = indices.Min(i => vertices[i].z), maxZ = indices.Max(i => vertices[i].z),
                            lowestSurfaceMean = V(low.Select(i => vertices[i]).Aggregate(Vector3.zero, (a, b) => a + b) / low.Length),
                            lowestSurfaceCount = low.Length };
                    }
                    frames.Add(new { phase = frame / 60f, secondsAtSpeed = period * frame / 60f,
                        rootForwardDistanceAtSpeed = speed * period * frame / 60f,
                        bodyMinY = vertices.Min(v => v.y), bodyMaxY = vertices.Max(v => v.y),
                        joints = jointNames.ToDictionary(b => b.ToString(), b => V(LocalBone(b))),
                        leftShoe = Shoe(leftShoe), rightShoe = Shoe(rightShoe) });
                    if (renderedPhases.Contains(frame))
                        WriteObj(Path.Combine(output, stem + "-phase" + frame.ToString("D2") + ".obj.gz"),
                            vertices, baked.normals.Select(n => root.transform.InverseTransformDirection(skin.transform.TransformDirection(n))).ToArray(),
                            skin.sharedMesh.uv, skin.sharedMesh.triangles);
                }
                string clipPath = AssetDatabase.GetAssetPath(clip);
                var record = new { character, state = stateName, variant, referenceSpeed, footIK, requestedSpeedMetresPerSecond = speed,
                    clipName = clip.name, clipPath, sourceFileSha256 = string.IsNullOrEmpty(clipPath) ? null : Hash(clipPath), clipLength = clip.length,
                    playbackRate = rate, physicalCycleSeconds = period, forwardDistancePerCycle = speed * period,
                    prefab = AssetDatabase.GetAssetPath(prefab), avatar = AssetDatabase.GetAssetPath(animator.avatar),
                    controller = AssetDatabase.GetAssetPath(controller), rootMotion = animator.applyRootMotion,
                    vertexCount = baked.vertexCount, triangleCount = skin.sharedMesh.triangles.Length / 3,
                    leftShoeVertices = leftShoe.Length, rightShoeVertices = rightShoe.Length, restJoints,
                    coordinates = "Unity root-local metres: Y up, Z navigation forward; anatomical left is normally negative X",
                    purpose = "Native pose measurements, not an automatic artistic/biomechanical pass; excludes runtime transitions/NavMesh/grounding",
                    frames };
                File.WriteAllText(Path.Combine(output, stem + ".json"), JsonConvert.SerializeObject(record, Formatting.Indented));
                Debug.Log($"CTLIFE [locomotion-probe] {stem}: {clip.name} length={clip.length:F4}s, rate={rate:F4}, " +
                    $"actualSpeed={speed:F3}m/s, cycle={period:F4}s, travelPerCycle={speed * period:F4}m; 61 phases/8 meshes.");
            }
            finally
            {
                Object.DestroyImmediate(baked);
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        private static string Hash(string path)
        {
            using var h = SHA256.Create();
            using var file = File.OpenRead(path);
            return BitConverter.ToString(h.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        private static void WriteObj(string path, Vector3[] vertices, Vector3[] normals, Vector2[] uv, int[] triangles)
        {
            using var file = File.Create(path);
            using var zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal);
            using var writer = new StreamWriter(zip);
            string N(float x) => x.ToString("R", CultureInfo.InvariantCulture);
            foreach (var v in vertices) writer.WriteLine($"v {N(v.x)} {N(v.y)} {N(v.z)}");
            foreach (var t in uv) writer.WriteLine($"vt {N(t.x)} {N(t.y)}");
            foreach (var n in normals) writer.WriteLine($"vn {N(n.x)} {N(n.y)} {N(n.z)}");
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i] + 1, b = triangles[i+1] + 1, c = triangles[i+2] + 1;
                writer.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
            }
        }
    }
}
