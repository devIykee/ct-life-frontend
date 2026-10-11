// Coordinator-only native experiment, staged outside Assets. Does not import or save assets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public static class RunCadenceCandidateReview
    {
        private const int PhaseCount = 240;
        private static readonly float[] References = { 4.5f, 6.5f, 8.25f };
        private static readonly float[] Speeds = { 3.6f, 3.96f, 4.5f };
        private sealed class Patch { public string name; public int[] ids; }
        private sealed class Skin { public SkinnedMeshRenderer renderer; public int[] left, right; public Patch[] patches; public Mesh baked; }
        public static void Capture()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string output = Path.Combine(repo, "../ct-life-evidence/art/character-candidates/motion-review-20261006/native-cadence-current");
            Directory.CreateDirectory(output);
            var roots = new[] { "Assets/Resources/Characters", "Assets/Art/animations", "Assets/Art/characters/authored/Snow", "Assets/Art/characters/authored/Rain" };
            var frozen = roots.SelectMany(r => Directory.GetFiles(r, "*", SearchOption.AllDirectories)).ToDictionary(p => p, Hash);
            var receipt = new JObject { ["state"] = "sampling", ["production_changed"] = false,
                ["scope"] = "Current promoted run only; cadence-only in-memory comparisons, no source replacement or production promotion.",
                ["limitations"] = "Static normalized-phase samples and native rate checks. No control, NavMesh, continuous transition, visible-LOD device, Foley or artistic acceptance." };
            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                var baseController = registry.controller as AnimatorController; Require(baseController != null, "Shared base controller missing.");
                var states = baseController.layers.SelectMany(l => States(l.stateMachine)).ToArray();
                var run = states.Single(s => s.name == "loco_run");
                Require(run.motion is AnimationClip && run.speedParameterActive && run.speedParameter == "speed" &&
                    Mathf.Abs(run.speed - 1f / 4.5f) < .000001f && !run.iKOnFeet, "Production run configuration changed; review the new baseline.");
                var cases = new JArray(); receipt["characters"] = cases;
                foreach (string character in new[] { "snow", "rain" })
                {
                    var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + character));
                    RuntimeAnimatorController bound = entry.controllerOverride != null ? entry.controllerOverride : registry.controller;
                    var effective = (AnimationClip)run.motion;
                    if (bound is AnimatorOverrideController own)
                    {
                        Require(own.runtimeAnimatorController == registry.controller, "Override lineage changed.");
                        var replacement = own[effective]; if (replacement != null) effective = replacement;
                    }
                    Require(AssetDatabase.GetAssetPath(effective) == "Assets/Art/animations/loco_run.fbx", "Current run is no longer the measured source.");
                    cases.Add(CaptureCharacter(character, entry.prefab, bound, effective, output));
                }
                receipt["state"] = "captured-not-accepted";
            }
            catch (Exception e) { receipt["state"] = "failed"; receipt["failure"] = e.ToString(); throw; }
            finally
            {
                var drift = frozen.Where(p => !File.Exists(p.Key) || Hash(p.Key) != p.Value).Select(p => p.Key).ToArray();
                receipt["protected_drift"] = new JArray(drift); receipt["protected_sha256"] = JObject.FromObject(frozen);
                File.WriteAllText(Path.Combine(output, "receipt.json"), receipt.ToString());
                Require(drift.Length == 0, "Production file drift: " + string.Join(",", drift));
            }
        }
        private static JObject CaptureCharacter(string name, GameObject prefab, RuntimeAnimatorController bound, AnimationClip clip, string output)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject go = null; var skins = new List<Skin>();
            var controller = new AnimatorController { name = "UNSAVED_RUN_CADENCE" }; var machine = new AnimatorStateMachine();
            AnimatorState state = null;
            try
            {
                go = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = go.GetComponentsInChildren<Animator>(true).Single(); animator.enabled = false;
                Require(animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid, "Own Avatar is invalid.");
                foreach (var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var weights = renderer.sharedMesh.boneWeights; var bones = renderer.bones.Select(b => b.name).ToArray();
                    int[] Select(string[] names, float minimum) => Enumerable.Range(0, weights.Length).Where(i => {
                        var w = weights[i]; float Part(int b, float v) => v > 0 && names.Contains(bones[b]) ? v : 0;
                        return Part(w.boneIndex0, w.weight0) + Part(w.boneIndex1, w.weight1) + Part(w.boneIndex2, w.weight2) + Part(w.boneIndex3, w.weight3) >= minimum;
                    }).ToArray();
                    var skin = new Skin { renderer = renderer, baked = new Mesh(), left = Select(new[] { "Foot.L", "Toes.L" }, .5f), right = Select(new[] { "Foot.R", "Toes.R" }, .5f) };
                    Require(skin.left.Length > 0 && skin.right.Length > 0, "Missing weighted shoes.");
                    renderer.BakeMesh(skin.baked);
                    var rest = skin.baked.vertices.Select(v => go.transform.InverseTransformPoint(renderer.transform.TransformPoint(v))).ToArray();
                    var patches = new List<Patch>();
                    foreach (string side in new[] { "L", "R" }) foreach (string part in new[] { "Foot", "Toes" })
                    {
                        var ids = Select(new[] { part + "." + side }, .75f); Require(ids.Length > 0, "No fixed sole patch candidate.");
                        float lowest = ids.Min(i => rest[i].y);
                        patches.Add(new Patch { name = part + "." + side, ids = ids.Where(i => rest[i].y <= lowest + .002f).ToArray() });
                    }
                    skin.patches = patches.ToArray(); skins.Add(skin);
                }
                controller.AddParameter("speed", AnimatorControllerParameterType.Float);
                state = machine.AddState("loco_run"); state.motion = clip; state.speedParameter = "speed";
                state.speedParameterActive = true; state.iKOnFeet = false; state.speed = 1f / References[0];
                machine.defaultState = state;
                controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine } };
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false; animator.speed = 1; animator.enabled = true; animator.Rebind();
                var metadata = new JArray();
                foreach (var skin in skins) metadata.Add(new JObject {
                    ["name"] = skin.renderer.name, ["mesh"] = skin.renderer.sharedMesh.name, ["vertex_count"] = skin.renderer.sharedMesh.vertexCount,
                    ["triangles"] = skin.renderer.sharedMesh.triangles.Length / 3, ["patches"] = new JArray(skin.patches.Select(p => new JObject { ["name"] = p.name, ["fixed_vertex_indices"] = new JArray(p.ids) })) });
                var frames = new JArray(); var controls = new Dictionary<int, Vector3[]>(); var rates = new JArray(); float maxResidual = 0;
                HumanBodyBones[] tracked = Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>().Where(b => b != HumanBodyBones.LastBone && animator.GetBoneTransform(b) != null).ToArray();
                Vector3 Point(HumanBodyBones b) => go.transform.InverseTransformPoint(animator.GetBoneTransform(b).position);
                for (int referenceIndex = 0; referenceIndex < References.Length; referenceIndex++)
                {
                    float reference = References[referenceIndex]; state.speed = 1f / reference;
                    animator.Rebind();
                    foreach (float speed in Speeds)
                    {
                        animator.SetFloat("speed", speed); animator.Play("loco_run", 0, .25f); animator.Update(0);
                        var info = animator.GetCurrentAnimatorStateInfo(0); float rate = info.speed * info.speedMultiplier * animator.speed;
                        Require(Mathf.Abs(rate - speed / reference) < .00001f, "Native rate differs from expected speed/reference.");
                        rates.Add(new JObject { ["reference_mps"] = reference, ["speed_mps"] = speed, ["native_rate"] = rate,
                            ["cycle_seconds"] = clip.length / rate, ["two_plants_per_second_assumption"] = 2 * rate / clip.length });
                    }
                    for (int phase = 0; phase <= PhaseCount; phase++)
                    {
                        animator.SetFloat("speed", 4.5f); animator.Play("loco_run", 0, (float)phase / PhaseCount); animator.Update(0);
                        var joints = tracked.Select(Point).ToArray();
                        if (referenceIndex == 0) controls[phase] = joints;
                        else
                        {
                            for (int j = 0; j < joints.Length; j++) maxResidual = Mathf.Max(maxResidual, Vector3.Distance(joints[j], controls[phase][j]));
                            continue;
                        }
                        var surfaces = new JArray();
                        foreach (var skin in skins)
                        {
                            skin.renderer.BakeMesh(skin.baked);
                            var v = skin.baked.vertices.Select(p => go.transform.InverseTransformPoint(skin.renderer.transform.TransformPoint(p))).ToArray();
                            Require(v.All(p => !float.IsNaN(p.sqrMagnitude) && !float.IsInfinity(p.sqrMagnitude)), "Invalid native surface.");
                            var patches = new JObject();
                            foreach (var patch in skin.patches)
                            {
                                Vector3 mean = patch.ids.Aggregate(Vector3.zero, (a, i) => a + v[i]) / patch.ids.Length;
                                patches[patch.name] = new JObject { ["mean"] = V(mean), ["min_y"] = patch.ids.Min(i => v[i].y), ["max_y"] = patch.ids.Max(i => v[i].y) };
                            }
                            surfaces.Add(new JObject { ["skin"] = skin.renderer.name, ["left_min_y"] = skin.left.Min(i => v[i].y), ["right_min_y"] = skin.right.Min(i => v[i].y), ["patches"] = patches });
                            if (phase % 30 == 0 && phase < PhaseCount)
                            {
                                var method = typeof(LocomotionQualityProbe).GetMethod("WriteObj", BindingFlags.Static | BindingFlags.NonPublic);
                                Require(method != null, "OBJ diagnostic writer missing.");
                                method.Invoke(null, new object[] { Path.Combine(output, name + "-" + skin.renderer.name + "-phase" + phase.ToString("000") + ".obj.gz"), v,
                                    skin.baked.normals.Select(n => go.transform.InverseTransformDirection(skin.renderer.transform.TransformDirection(n))).ToArray(), skin.renderer.sharedMesh.uv, skin.renderer.sharedMesh.triangles });
                            }
                        }
                        frames.Add(new JObject { ["phase"] = (float)phase / PhaseCount, ["joints"] = JObject.FromObject(tracked.Select((b, i) => new { b, i }).ToDictionary(p => p.b.ToString(), p => V(joints[p.i]))), ["surfaces"] = surfaces });
                    }
                }
                Require(maxResidual < .000002f, "Cadence changed normalized-phase joint pose by more than2 micrometres.");
                var result = new JObject { ["character"] = name, ["prefab"] = AssetDatabase.GetAssetPath(prefab), ["bound_controller"] = AssetDatabase.GetAssetPath(bound),
                    ["own_avatar"] = AssetDatabase.GetAssetPath(animator.avatar), ["clip"] = AssetDatabase.GetAssetPath(clip), ["clip_sha256"] = Hash(AssetDatabase.GetAssetPath(clip)),
                    ["clip_seconds"] = clip.length, ["foot_ik"] = false, ["root_motion"] = false, ["skins"] = metadata,
                    ["rates"] = rates, ["maximum_equal_phase_joint_residual_m"] = maxResidual, ["frames"] = frames,
                    ["patch_method"] = "Fixed material vertex sets from each bone's >=75% influenced rest-bottom2mm region. Indices stay fixed across phase; this is not a changing lowest-vertex centroid or guaranteed planted foot." };
                File.WriteAllText(Path.Combine(output, name + "-cadence.json"), result.ToString());
                return new JObject { ["character"] = name, ["file"] = name + "-cadence.json", ["maximum_equal_phase_joint_residual_m"] = maxResidual, ["rates"] = rates.DeepClone() };
            }
            finally
            {
                foreach (var skin in skins) Object.DestroyImmediate(skin.baked);
                if (go != null) Object.DestroyImmediate(go); Object.DestroyImmediate(controller); if (state != null) Object.DestroyImmediate(state); Object.DestroyImmediate(machine);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static JArray V(Vector3 p) => new JArray(p.x, p.y, p.z);
        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) => machine.states.Select(s => s.state).Concat(machine.stateMachines.SelectMany(s => States(s.stateMachine)));
        private static string Hash(string path) { using var sha = SHA256.Create(); using var input = File.OpenRead(path); return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
