// Staged fallback after rest-Jog matrix: per-foot stance goals, no body/root shift.
// Root coordinator only. Generated clips are unreferenced candidates; no global SaveAssets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class SprintSourceDiagnostic
    {
        private const int Keys = 120, Dense = 240;
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/SprintSourceDiagnosticNoAssets/";
        private const string SourcePath = "Assets/Art/animations/loco_run.fbx";
        private static readonly string[] PatchNames = { "Foot.L", "Toes.L", "Foot.R", "Toes.R" };
        private static Dictionary<string, int[][]> patchIndices;
        private static readonly HumanBodyBones[] Tracked = {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes };

        private sealed class Frame
        {
            public float phase, leftSole, rightSole, nativeNormalizedTime, animatorSpeed, stateSpeed, speedMultiplier;
            public float[] tierLeft, tierRight;
            public string[] tierNames;
            public Vector3[][] patchMeans;
            public Quaternion bodyRotation;
            public float humanScale;
            public Vector3[] joints;
            public Quaternion[] rotations;
        }
        private sealed class Skin
        {
            public SkinnedMeshRenderer renderer;
            public Mesh baked;
            public int[] left, right;
            public int[][] patches;
            public readonly List<Vector3> vertices = new List<Vector3>();
        }
        private sealed class Rig : IDisposable
        {
            public GameObject root;
            public Animator animator;
            public AnimatorController controller;
            public AnimatorStateMachine machine;
            public AnimatorState state;
            public HumanPoseHandler human;
            public readonly List<Skin> skins = new List<Skin>();
            public Rig(GameObject prefab, Scene scene, string stateName, AnimationClip clip, float speed, float reference, bool footIk)
            {
                try
                {
                root = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                animator = root.GetComponentsInChildren<Animator>(true).Single(); animator.enabled = false;
                if (animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid) throw new InvalidDataException("Own Avatar is invalid.");
                Require(Mathf.Abs(speed / reference - 1f) < .000001f, "Contact comparison must use the proven matrix 1x time path.");
                controller = new AnimatorController { name = "UNSAVED_SPRINT_GOALS_V1" };
                machine = new AnimatorStateMachine(); state = machine.AddState(stateName); state.motion = clip;
                state.speed = 1f; state.speedParameterActive = false; state.iKOnFeet = footIk;
                machine.defaultState = state;
                controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine } };
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false; animator.speed = 1f; animator.enabled = true; animator.Rebind();
                human = new HumanPoseHandler(animator.avatar, animator.transform);
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var weights = renderer.sharedMesh.boneWeights;
                    var names = renderer.bones.Select(b => b.name).ToArray();
                    int[] Select(string suffix) => Enumerable.Range(0, weights.Length).Where(i =>
                    {
                        var w = weights[i];
                        float W(int bone, float value) => value > 0 && (names[bone] == "Foot." + suffix || names[bone] == "Toes." + suffix) ? value : 0f;
                        return W(w.boneIndex0, w.weight0) + W(w.boneIndex1, w.weight1) + W(w.boneIndex2, w.weight2) + W(w.boneIndex3, w.weight3) > .5f;
                    }).ToArray();
                    Require(patchIndices.TryGetValue(renderer.name, out var patches), "Missing fixed material patches for " + renderer.name);
                    Require(patches.All(p => p.Length > 0 && p.All(i => i >= 0 && i < renderer.sharedMesh.vertexCount)), "Fixed patch vertex bounds differ.");
                    var skin = new Skin { renderer = renderer, baked = new Mesh(), left = Select("L"), right = Select("R"), patches = patches };
                    skins.Add(skin); Require(skin.left.Length > 0 && skin.right.Length > 0, "Missing footwear weights.");
                    skin.vertices.Capacity = renderer.sharedMesh.vertexCount;
                }
                Require(skins.Count > 0, "No skinned geometry.");
                }
                catch { Dispose(); throw; }
            }
            public void Warm(int steps)
            {
                animator.Play(state.name, 0, 0); animator.Update(0);
                float dt = ((AnimationClip)state.motion).length / steps;
                for (int i = 0; i < 2 * steps; i++) animator.Update(dt);
                Require(Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime - 2f) < .001f, "Native warm-up did not advance exactly two loops.");
            }
            public void Step(int steps)
            {
                float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                animator.Update(((AnimationClip)state.motion).length / steps);
                float after = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Require(Mathf.Abs(after - before - 1f / steps) < .0001f, "Native state time failed to advance one requested sample.");
            }
            public void Pose(float phase)
            {
                Warm(Keys);
                for (int i = 0; i < Mathf.RoundToInt(phase * Keys); i++) Step(Keys);
            }
            public Frame Read(float phase, bool muscles = false, float expectedCycle = 2f)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                Require(Mathf.Abs(info.normalizedTime - (expectedCycle + phase)) < .001f, "Sample label differs from actual native state time.");
                Require(Mathf.Abs(info.speed * info.speedMultiplier * animator.speed - 1f) < .00001f, "Contact sampler native rate differs from 1x.");
                Require(root.transform.position.sqrMagnitude < 1e-10f, "Disabled root motion still moved the root.");
                var frame = new Frame { phase = phase, nativeNormalizedTime = info.normalizedTime, animatorSpeed = animator.speed,
                    stateSpeed = info.speed, speedMultiplier = info.speedMultiplier, leftSole = float.PositiveInfinity, rightSole = float.PositiveInfinity,
                    tierLeft = new float[skins.Count], tierRight = new float[skins.Count],
                    tierNames = skins.Select(s => s.renderer.name).ToArray(), patchMeans = new Vector3[skins.Count][],
                    joints = Tracked.Select(b => root.transform.InverseTransformPoint(animator.GetBoneTransform(b).position)).ToArray(),
                    rotations = Tracked.Select(b => Quaternion.Inverse(root.transform.rotation) * animator.GetBoneTransform(b).rotation).ToArray() };
                for (int tier = 0; tier < skins.Count; tier++)
                {
                    var skin = skins[tier]; skin.renderer.BakeMesh(skin.baked, false); skin.baked.GetVertices(skin.vertices);
                    var matrix = root.transform.worldToLocalMatrix * skin.renderer.localToWorldMatrix;
                    frame.tierLeft[tier] = skin.left.Min(i => matrix.MultiplyPoint3x4(skin.vertices[i]).y);
                    frame.tierRight[tier] = skin.right.Min(i => matrix.MultiplyPoint3x4(skin.vertices[i]).y);
                    frame.patchMeans[tier] = skin.patches.Select(p => p.Aggregate(Vector3.zero, (sum, i) => sum + matrix.MultiplyPoint3x4(skin.vertices[i])) / p.Length).ToArray();
                    frame.leftSole = Mathf.Min(frame.leftSole, frame.tierLeft[tier]); frame.rightSole = Mathf.Min(frame.rightSole, frame.tierRight[tier]);
                }
                if (muscles)
                {
                    var pose = new HumanPose(); human.GetHumanPose(ref pose);
                    frame.bodyRotation = pose.bodyRotation; frame.humanScale = animator.humanScale;
                }
                Require(frame.joints.All(v => Finite(v.sqrMagnitude)) && Finite(frame.leftSole) && Finite(frame.rightSole), "Nonfinite pose.");
                return frame;
            }
            public void Dispose()
            {
                human?.Dispose();
                foreach (var skin in skins) if (skin.baked != null) Object.DestroyImmediate(skin.baked);
                if (root != null) Object.DestroyImmediate(root);
                if (controller != null) Object.DestroyImmediate(controller);
                if (state != null) Object.DestroyImmediate(state);
                if (machine != null) Object.DestroyImmediate(machine);
            }
        }

        public static void CaptureBaselines()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/character-candidates/motion-review-20261006/native-sprint-source-diagnostic"));
            Directory.CreateDirectory(output);
            var roots = new[] { "Assets/Resources/Characters", "Assets/Art/animations", "Assets/Art/characters/authored" };
            var hashes = roots.SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                .Where(p => !p.Replace('\\', '/').StartsWith(Stage, StringComparison.Ordinal) && p != Stage.TrimEnd('/') + ".meta").ToDictionary(p => p, Hash);
            var receipt = new JObject { ["state"] = "sampling-source-diagnostic", ["all_pass"] = false, ["purpose"] = "Read-only existing Sprint source diagnostic: compare direct normalized-time sampling with continuous warm-loop sampling and capture FootIK baseline. No animation/controller assets created.", ["cases"] = new JArray() };
            bool passed = true;
            try
            {
                Require(Hash(SourcePath) == "8c4f53caa05a528ba7554378617efa32b318d5ecd11cb2b7c0872fafe9ad4a48", "Production Sprint source changed.");
                Require(Hash(SourcePath + ".meta") == "669360664ad82c37615b75bb32ce8852e34794b1c6ce74b379333ed66ea89613", "Production Sprint importer changed.");
                var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath);
                var settings = importer.clipAnimations.Single();
                Require(settings.keepOriginalOrientation && settings.rotationOffset == 180 && settings.keepOriginalPositionY &&
                    !settings.heightFromFeet && settings.lockRootHeightY && settings.heightOffset == 0,
                    "Expected frozen Original-Y, Original180 orientation and zero height offset.");
                receipt["source_avatar"] = AssetDatabase.GetAssetPath(importer.sourceAvatar);
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                patchIndices = new Dictionary<string, int[][]>();
                foreach (string character in new[] { "snow", "rain" })
                {
                    string matrixPath = Path.Combine(output, "../native-rest-contact-matrix/" + character + "-original-y-footik.json");
                    var matrix = JObject.Parse(File.ReadAllText(matrixPath));
                    foreach (var skin in matrix["rest_surfaces"])
                        patchIndices.Add((string)skin["skin"], PatchNames.Select(name => skin["patches"].Single(p => (string)p["name"] == name)["fixed_vertex_indices"].Values<int>().ToArray()).ToArray());
                    var result = new JObject { ["character"] = character, ["all_pass"] = false };
                    ((JArray)receipt["cases"]).Add(result);
                    try
                    {
                        var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + character));
                        Build(character, entry.prefab, output, result);
                        result["all_pass"] = true;
                    }
                    catch (Exception error) { passed = false; result["failure"] = error.ToString(); Debug.LogError("CTLIFE [sprint-goals] " + character + ": " + error); }
                }
            }
            catch (Exception error) { passed = false; receipt["failure"] = error.ToString(); throw; }
            finally
            {
                var after = roots.SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                    .Where(p => !p.Replace('\\', '/').StartsWith(Stage, StringComparison.Ordinal) && p != Stage.TrimEnd('/') + ".meta").ToDictionary(p => p, Hash);
                var drift = hashes.Where(p => !after.TryGetValue(p.Key, out string value) || value != p.Value).Select(p => p.Key)
                    .Concat(after.Keys.Where(p => !hashes.ContainsKey(p))).ToArray();
                receipt["protected_drift"] = new JArray(drift); receipt["protected_hashes"] = JObject.FromObject(hashes);
                receipt["all_pass"] = passed && drift.Length == 0;
                receipt["state"] = passed && drift.Length == 0 ? "captured-source-comparison-no-candidate" : "failed";
                File.WriteAllText(Path.Combine(output, "candidate-receipt.json"), receipt.ToString());
                Require(drift.Length == 0, "Protected production/candidate files changed; inspect receipt.");
            }
            Require(passed, "At least one isolated candidate failed. No production motion/controller/registry was assigned; inspect candidate-receipt.json.");
            Debug.Log("CTLIFE [sprint-goals] Source direct/continuous diagnostic captured; contact, loop quality and cadence remain unaccepted.");
        }

        private static void Build(string character, GameObject prefab, string output, JObject receipt)
        {
            var source=AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var direct=new Frame[Keys+1];
                using(var rig=new Rig(prefab,scene,"loco_run",source,4.5f,4.5f,false))
                    for(int i=0;i<=Keys;i++)
                    { float phase=i/(float)Keys;rig.animator.Play("loco_run",0,phase);rig.animator.Update(0);direct[i]=rig.Read(phase,false,0); }
                WriteFrames(Path.Combine(output,character+"-direct-noik.json"),direct,source.length);
                var controls=new List<(bool ik,Frame[] frames)>();
                foreach(bool ik in new[]{false,true})
                {
                    using var rig=new Rig(prefab,scene,"loco_run",source,4.5f,4.5f,ik);
                    var frames=new Frame[Keys+1];rig.Warm(Keys);
                    for(int i=0;i<=Keys;i++)
                    {
                        frames[i]=rig.Read(i/(float)Keys);
                        if(i<Keys&&i%(Keys/8)==0)for(int tier=0;tier<rig.skins.Count;tier++)
                            WriteObj(Path.Combine(output,character+(ik?"-ik":"-noik")+"-tier"+tier+"-phase"+i.ToString("D3")+".obj.gz"),rig.root.transform,rig.skins[tier]);
                        if(i<Keys)rig.Step(Keys);
                    }
                    WriteFrames(Path.Combine(output,character+(ik?"-continuous-ik":"-continuous-noik")+".json"),frames,source.length);
                    controls.Add((ik,frames));
                    receipt[ik?"ik_summary":"noik_summary"]=new JObject{["native_start"]=frames[0].nativeNormalizedTime,["native_end"]=frames[Keys].nativeNormalizedTime,
                        ["left_min_m"]=frames.Min(f=>f.leftSole),["right_min_m"]=frames.Min(f=>f.rightSole),
                        ["both_above3cm_samples"]=frames.Take(Keys).Count(f=>Mathf.Min(f.leftSole,f.rightSole)>.03f),["sample_count"]=Keys};
                }
                var moving=controls.Single(c=>!c.ik).frames;var errors=new JArray();
                for(int i=0;i<=Keys;i++)for(int j=0;j<Tracked.Length;j++)
                {
                    float error=Vector3.Distance(moving[i].joints[j],direct[i].joints[j]);
                    if(error>.0001f)errors.Add(new JObject{["phase"]=i/(float)Keys,["joint"]=Tracked[j].ToString(),["error_m"]=error,["continuous_native_time"]=moving[i].nativeNormalizedTime,
                        ["direct_native_time"]=direct[i].nativeNormalizedTime,["direct_xyz"]=new JArray(direct[i].joints[j].x,direct[i].joints[j].y,direct[i].joints[j].z),
                        ["continuous_xyz"]=new JArray(moving[i].joints[j].x,moving[i].joints[j].y,moving[i].joints[j].z)});
                }
                receipt["continuous_vs_direct_errors_above0_1mm"]=errors;
                // Reproduce the historical direct sampling first. Continuous seam differences are retained, not tuned away.
                ValidateMatrix(character,direct,"noik",output,receipt);
                Require(Mathf.Abs(moving[Keys].nativeNormalizedTime-moving[0].nativeNormalizedTime-1)<.001f,"Continuous native timeline did not traverse source loop.");
                receipt["scope"]="Read-only source sampling diagnostic; no candidate curve, controller assignment or acceptance of contact/cadence.";
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void ValidateMatrix(string character, Frame[] frames, string variant, string output, JObject receipt)
        {
            Require(variant == "noik", "Only the independently captured current no-IK Sprint has a source reference.");
            string path = Path.Combine(output, "../native-cadence-current/" + character + "-cadence.json");
            var reference = JObject.Parse(File.ReadAllText(path));
            Require((string)reference["clip_sha256"] == Hash(SourcePath) && !(bool)reference["foot_ik"], "Independent Sprint reference source changed.");
            var expected = reference["frames"].ToArray();
            Require(expected.Length == 2 * (frames.Length - 1) + 1, "Current reference phase count differs.");
            float joints = 0f, soles = 0f;
            for (int i = 0; i < frames.Length; i++)
            {
                var sample = expected[2 * i];
                Require(Mathf.Abs((float)sample["phase"] - frames[i].phase) < .000001f, "Source comparison phase labels differ.");
                for (int j = 0; j < Tracked.Length; j++)
                {
                    var point = sample["joints"][Tracked[j].ToString()].Values<float>().ToArray();
                    joints = Mathf.Max(joints, Vector3.Distance(frames[i].joints[j], new Vector3(point[0], point[1], point[2])));
                }
                foreach (int side in new[] { 0, 1 })
                {
                    float value = sample["surfaces"].Min(surface => (float)surface[side == 0 ? "left_min_y" : "right_min_y"]);
                    soles = Mathf.Max(soles, Mathf.Abs(value - (side == 0 ? frames[i].leftSole : frames[i].rightSole)));
                }
            }
            receipt[variant + "_matrix_max_joint_error_m"] = joints; receipt[variant + "_matrix_max_sole_error_m"] = soles;
            Require(joints <= .002f && soles <= .002f, "Unmodified temporal sample does not reproduce the independently captured matrix within2mm.");
        }
        private static void WriteFrames(string path, Frame[] frames, float period) => File.WriteAllText(path, JsonConvert.SerializeObject(new
        {
            clip_seconds = period, source_rate = 1, joints = Tracked.Select(b => b.ToString()).ToArray(), coordinates = "root-local metres; min sole across all authored world tiers",
            frames = frames.Select(f => new { f.phase, f.nativeNormalizedTime, f.animatorSpeed, f.stateSpeed, f.speedMultiplier,
                f.leftSole, f.rightSole, f.tierLeft, f.tierRight,
                surfaces = f.tierNames.Select((name, tier) => new { skin = name, left_min_y = f.tierLeft[tier], right_min_y = f.tierRight[tier],
                    patches = PatchNames.Select((patch, index) => new { patch, mean = f.patchMeans[tier][index] })
                        .ToDictionary(p => p.patch, p => new { mean = new[] { p.mean.x, p.mean.y, p.mean.z } }) }),
                joints = f.joints.Select(v => new[] { v.x, v.y, v.z }).ToArray(),
                rotations = f.rotations.Select(q => new[] { q.x, q.y, q.z, q.w }).ToArray(),
                bodyRotation = new[] { f.bodyRotation.x, f.bodyRotation.y, f.bodyRotation.z, f.bodyRotation.w }, f.humanScale })
        }, Formatting.Indented));
        private static void WriteObj(string path, Transform root, Skin skin)
        {
            var matrix = root.worldToLocalMatrix * skin.renderer.localToWorldMatrix;
            using var file = File.Create(path);
            using var zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
            using var writer = new StreamWriter(zip);
            string N(float x) => x.ToString("R", CultureInfo.InvariantCulture);
            foreach (var raw in skin.vertices)
            {
                Vector3 v = matrix.MultiplyPoint3x4(raw); writer.WriteLine($"v {N(v.x)} {N(v.y)} {N(v.z)}");
            }
            foreach (var uv in skin.renderer.sharedMesh.uv) writer.WriteLine($"vt {N(uv.x)} {N(uv.y)}");
            foreach (var raw in skin.baked.normals)
            {
                Vector3 n = matrix.MultiplyVector(raw).normalized; writer.WriteLine($"vn {N(n.x)} {N(n.y)} {N(n.z)}");
            }
            int[] triangles = skin.renderer.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i] + 1, b = triangles[i + 1] + 1, c = triangles[i + 2] + 1;
                writer.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
            }
        }
        private static string Hash(string path) { using var hash = SHA256.Create(); using var file = File.OpenRead(path); return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Contact candidate: " + message); }
    }
}
