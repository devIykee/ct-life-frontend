// Isolated authored-motion candidate. Coordinator copies into Editor and executes BuildCandidates.
// No production controller/registry or source FBX is changed. Generated clips are unreferenced staging assets.
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
    public static class FootGoalCandidate
    {
        private const int Keys = 120, Dense = 240;
        private const float BlendBand = .004f; // C1 smooth positive part: 1mm clearance at exactly-zero baseline sole.
        private const string Stage = "Assets/Art/characters/authored/LocomotionCandidate/";
        private static readonly HumanBodyBones[] Tracked = {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes };

        private sealed class Frame
        {
            public float phase, leftSole, rightSole;
            public float[] tierLeft, tierRight;
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
            public Rig(GameObject prefab, string stateName, AnimationClip clip, float speed, float reference, bool footIk)
            {
                try
                {
                root = Object.Instantiate(prefab);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                animator = root.GetComponentsInChildren<Animator>(true).Single();
                if (animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid) throw new InvalidDataException("Own Avatar is invalid.");
                controller = new AnimatorController(); controller.AddParameter("speed", AnimatorControllerParameterType.Float);
                machine = new AnimatorStateMachine(); state = machine.AddState(stateName); state.motion = clip;
                state.speed = 1f / reference; state.speedParameter = "speed"; state.speedParameterActive = true; state.iKOnFeet = footIk;
                machine.defaultState = state;
                controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine } };
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false; animator.enabled = true; animator.Rebind(); animator.SetFloat("speed", speed);
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
                    var skin = new Skin { renderer = renderer, baked = new Mesh(), left = Select("L"), right = Select("R") };
                    skins.Add(skin); Require(skin.left.Length > 0 && skin.right.Length > 0, "Missing footwear weights.");
                    skin.vertices.Capacity = renderer.sharedMesh.vertexCount;
                }
                Require(skins.Count > 0, "No skinned geometry.");
                }
                catch { Dispose(); throw; }
            }
            public void Pose(float phase) { animator.Play(state.name, 0, phase); animator.Update(0f); }
            public Frame Read(float phase, bool muscles = false)
            {
                var frame = new Frame { phase = phase, leftSole = float.PositiveInfinity, rightSole = float.PositiveInfinity,
                    tierLeft = new float[skins.Count], tierRight = new float[skins.Count],
                    joints = Tracked.Select(b => root.transform.InverseTransformPoint(animator.GetBoneTransform(b).position)).ToArray(),
                    rotations = Tracked.Select(b => Quaternion.Inverse(root.transform.rotation) * animator.GetBoneTransform(b).rotation).ToArray() };
                for (int tier = 0; tier < skins.Count; tier++)
                {
                    var skin = skins[tier]; skin.renderer.BakeMesh(skin.baked, false); skin.baked.GetVertices(skin.vertices);
                    var matrix = root.transform.worldToLocalMatrix * skin.renderer.localToWorldMatrix;
                    frame.tierLeft[tier] = skin.left.Min(i => matrix.MultiplyPoint3x4(skin.vertices[i]).y);
                    frame.tierRight[tier] = skin.right.Min(i => matrix.MultiplyPoint3x4(skin.vertices[i]).y);
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

        public static void BuildGoalCandidates()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/character-candidates/locomotion-quality-20261006/native-contact-goal-candidate"));
            Directory.CreateDirectory(output);
            var paths = new[] { "Assets/Resources/Characters/CharacterRegistry.asset", "Assets/Resources/Characters/AvatarController.controller", "Assets/Art/animations/loco_walk.fbx", "Assets/Art/animations/loco_run.fbx" };
            var hashes = paths.ToDictionary(p => p, Hash);
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(paths[0]);
            var receipt = new JObject { ["all_pass"] = false, ["purpose"] = "Isolated vertical foot-contact candidate; run cadence/sliding remains a separate unresolved motion decision.", ["cases"] = new JArray() };
            bool passed = true;
            foreach (string character in new[] { "snow", "rain" })
            {
                var result = new JObject { ["character"] = character, ["all_pass"] = false };
                ((JArray)receipt["cases"]).Add(result);
                try
                {
                    var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + character));
                    Build(character, entry.prefab, output, result);
                    result["all_pass"] = true;
                }
                catch (Exception error) { passed = false; result["failure"] = error.ToString(); Debug.LogError("CTLIFE [contact-goal-candidate] " + character + ": " + error); }
            }
            foreach (var pair in hashes) Require(Hash(pair.Key) == pair.Value, "Changed protected production file: " + pair.Key);
            receipt["all_pass"] = passed; receipt["protected_hashes"] = JObject.FromObject(hashes);
            File.WriteAllText(Path.Combine(output, "candidate-receipt.json"), receipt.ToString());
            Require(passed, "At least one isolated candidate failed. No production motion/controller/registry was assigned; inspect candidate-receipt.json.");
            Debug.Log("CTLIFE [contact-goal-candidate] Native contact/loop gates passed for isolated clips only; rendered/device review still required.");
        }

        private static void Build(string character, GameObject prefab, string output, JObject receipt)
        {
            bool walk = character == "snow"; string state = walk ? "loco_walk" : "loco_run";
            float speed = walk ? 1.75f : 4.5f, reference = walk ? 1f : 4.5f, maximumLift = walk ? .04f : .05f;
            string path = "Assets/Art/animations/" + state + ".fbx";
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            string stem = walk ? "Snow_WalkContact_Goals" : "Rain_RunContact_Goals";
            var scene = EditorSceneManager.NewPreviewScene();
            AnimationClip roundtrip = null, candidate = null;
            try
            {
                var baseline = new Frame[Keys + 1]; var lifts = new float[Keys + 1][];
                using (var rig = new Rig(prefab, state, source, speed, reference, walk))
                {
                    SceneManager.MoveGameObjectToScene(rig.root, scene);
                    for (int i = 0; i <= Keys; i++)
                    {
                        float phase = i / (float)Keys; rig.Pose(phase); baseline[i] = rig.Read(phase, true);
                        lifts[i] = new[] { Lift(baseline[i].leftSole), Lift(baseline[i].rightSole) };
                    }
                }
                receipt["source"] = path; receipt["clip_length"] = source.length; receipt["actual_speed"] = speed;
                receipt["reference_speed"] = reference; receipt["source_foot_ik"] = walk;
                receipt["max_requested_ankle_y_correction_m"] = lifts.SelectMany(v => v).Max();
                receipt["support_correction_phase_windows"] = new JObject { ["left"] = Windows(lifts.Select(v => v[0] > .0002f).ToArray()), ["right"] = Windows(lifts.Select(v => v[1] > .0002f).ToArray()) };
                receipt["animator_bindings"] = new JArray(AnimationUtility.GetCurveBindings(source).Where(b => b.type == typeof(Animator)).Select(b => b.propertyName));
                WriteFrames(Path.Combine(output, stem + "-sampled-original.json"), baseline);
                Require(lifts.SelectMany(v => v).Max() <= maximumLift, "Observed defect exceeds the targeted correction bound.");
                CheckGoalResponse(prefab, scene, state, source, speed, reference, walk, baseline, receipt);
                roundtrip = Object.Instantiate(source); roundtrip.name = stem + "_Roundtrip";
                candidate = GoalClone(source, baseline, lifts, stem + "_Candidate");
                var originalDense = Sample(prefab, scene, state, source, speed, reference, walk, Path.Combine(output, stem + "-original"));
                var reconstructedDense = Sample(prefab, scene, state, roundtrip, speed, reference, walk);
                var candidateDense = Sample(prefab, scene, state, candidate, speed, reference, walk, Path.Combine(output, stem + "-candidate"));
                WriteFrames(Path.Combine(output, stem + "-dense-original.json"), originalDense);
                WriteFrames(Path.Combine(output, stem + "-dense-roundtrip.json"), reconstructedDense);
                WriteFrames(Path.Combine(output, stem + "-dense-candidate.json"), candidateDense);
                float reconstructionJointError = MaxJoints(originalDense, reconstructedDense, Enumerable.Range(0, Tracked.Length));
                float reconstructionSoleError = originalDense.Zip(reconstructedDense, (a, b) => Mathf.Max(Mathf.Abs(a.leftSole - b.leftSole), Mathf.Abs(a.rightSole - b.rightSole))).Max();
                receipt["roundtrip_max_joint_error_m"] = reconstructionJointError; receipt["roundtrip_max_sole_error_m"] = reconstructionSoleError;
                Require(reconstructionJointError <= .002f && reconstructionSoleError <= .002f, "Unmodified clip clone does not reproduce original native pose within 2mm; candidate is not yet trustworthy.");
                Validate(originalDense, candidateDense, source.length / (speed / reference), receipt);
                Directory.CreateDirectory(Stage);
                SaveClip(roundtrip, Stage + stem + "_Roundtrip.anim"); roundtrip = null;
                SaveClip(candidate, Stage + stem + "_Candidate.anim"); candidate = null;
                AssetDatabase.SaveAssets();
                receipt["candidate_asset"] = Stage + stem + "_Candidate.anim";
                Debug.Log("CTLIFE [contact-goal-candidate] " + stem + " passed numeric gates, unreferenced staging clip saved.");
            }
            finally
            {
                if (roundtrip != null) Object.DestroyImmediate(roundtrip);
                if (candidate != null) Object.DestroyImmediate(candidate);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static float Lift(float sole)
        {
            if (sole >= BlendBand) return 0f;
            if (sole <= -BlendBand) return -sole;
            float x = BlendBand - sole;
            return x * x / (4f * BlendBand); // Value and first derivative join continuously at both ends.
        }
        private static AnimationClip GoalClone(AnimationClip source, Frame[] frames, float[][] lifts, string name)
        {
            var clip = Object.Instantiate(source); clip.name = name;
            try
            {
                var bindings = AnimationUtility.GetCurveBindings(source).Where(b => b.type == typeof(Animator)).ToArray();
                for (int side = 0; side < 2; side++)
                for (int axis = 0; axis < 3; axis++)
                {
                    string property = (side == 0 ? "LeftFootT." : "RightFootT.") + "xyz"[axis];
                    var matches = bindings.Where(b => b.propertyName == property).ToArray();
                    Require(matches.Length == 1, "Expected exactly one Humanoid foot-goal translation binding: " + property);
                    var binding = matches[0]; var original = AnimationUtility.GetEditorCurve(source, binding);
                    float[] values = new float[Keys + 1];
                    for (int i = 0; i <= Keys; i++)
                    {
                        Require(frames[i].humanScale > 0f, "Invalid own Avatar humanScale.");
                        // Unity stores Humanoid IK goals relative to the body. The small native response
                        // probe below checks this conversion on both actual Avatars with Foot IK off/on.
                        Vector3 delta = Quaternion.Inverse(frames[i].bodyRotation) * (Vector3.up * lifts[i][side]) / frames[i].humanScale;
                        values[i] = original.Evaluate(i * source.length / Keys) + delta[axis];
                    }
                    AnimationUtility.SetEditorCurve(clip, binding, PeriodicCurve(values, source.length));
                }
                Require(clip.humanMotion && clip.isLooping, "Goal clone lost its Humanoid loop settings.");
                return clip;
            }
            catch { Object.DestroyImmediate(clip); throw; }
        }
        private static AnimationCurve PeriodicCurve(float[] values, float length)
        {
            Require(values.All(Finite), "Nonfinite goal translation.");
            values[0] = values[Keys] = (values[0] + values[Keys]) * .5f;
            float dt = length / Keys; var keys = new Keyframe[Keys + 1];
            for (int i = 0; i <= Keys; i++)
            {
                int at = i == Keys ? 0 : i;
                float tangent = (values[(at + 1) % Keys] - values[(at + Keys - 1) % Keys]) / (2f * dt);
                keys[i] = new Keyframe(i * dt, values[i], tangent, tangent);
            }
            return new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
        }
        private static void CheckGoalResponse(GameObject prefab, Scene scene, string state, AnimationClip source,
            float speed, float reference, bool selectedIk, Frame[] baseline, JObject receipt)
        {
            var facts = new JArray(); receipt["one_mm_goal_response"] = facts;
            bool selectedPassed = true;
            foreach (bool ik in new[] { false, true })
            for (int side = 0; side < 2; side++)
            {
                int phaseIndex = Enumerable.Range(0, Keys).OrderBy(i => side == 0 ? baseline[i].leftSole : baseline[i].rightSole).First();
                float phase = phaseIndex / (float)Keys;
                var offsets = Enumerable.Range(0, Keys + 1).Select(i => side == 0 ? new[] { .001f, 0f } : new[] { 0f, .001f }).ToArray();
                var probe = GoalClone(source, baseline, offsets, "DIAGNOSTIC_GOAL_RESPONSE");
                try
                {
                    using var before = new Rig(prefab, state, source, speed, reference, ik);
                    using var after = new Rig(prefab, state, probe, speed, reference, ik);
                    SceneManager.MoveGameObjectToScene(before.root, scene); SceneManager.MoveGameObjectToScene(after.root, scene);
                    before.Pose(phase); after.Pose(phase);
                    var a = before.Read(phase); var b = after.Read(phase);
                    int ankle = side == 0 ? 7 : 11;
                    Vector3 delta = b.joints[ankle] - a.joints[ankle];
                    float horizontal = new Vector2(delta.x, delta.z).magnitude;
                    float upper = Enumerable.Range(0, 5).Max(i => Vector3.Distance(a.joints[i], b.joints[i]));
                    bool pass = Mathf.Abs(delta.y - .001f) <= .00025f && horizontal <= .00025f && upper <= .00025f;
                    facts.Add(new JObject { ["foot_ik"] = ik, ["selected_for_candidate"] = ik == selectedIk, ["side"] = side == 0 ? "left" : "right",
                        ["phase"] = phase, ["requested_world_y_m"] = .001f, ["ankle_delta_xyz_m"] = new JArray(delta.x, delta.y, delta.z),
                        ["upper_body_max_delta_m"] = upper, ["response_pass"] = pass });
                    if (ik == selectedIk && !pass) selectedPassed = false;
                }
                finally { Object.DestroyImmediate(probe); }
            }
            Require(selectedPassed, "Pinned foot-goal translation does not follow a 1mm Y request within 0.25mm, or moves upper body; do not assume goal units/semantics.");
        }
        private static Frame[] Sample(GameObject prefab, Scene scene, string state, AnimationClip clip, float speed, float reference, bool ik, string proofStem = null)
        {
            using var rig = new Rig(prefab, state, clip, speed, reference, ik);
            SceneManager.MoveGameObjectToScene(rig.root, scene);
            var frames = new Frame[Dense + 1];
            for (int i = 0; i <= Dense; i++)
            {
                float phase = i / (float)Dense; rig.Pose(phase); frames[i] = rig.Read(phase);
                // Same eight equal phases for both variants, all authored tiers. Also retained on a numeric failure.
                if (proofStem != null && i < Dense && i % (Dense / 8) == 0)
                    for (int tier = 0; tier < rig.skins.Count; tier++)
                        WriteObj(proofStem + "-tier" + tier + "-phase" + i.ToString("D3") + ".obj.gz", rig.root.transform, rig.skins[tier]);
            }
            return frames;
        }
        private static void Validate(Frame[] original, Frame[] candidate, float period, JObject receipt)
        {
            float upperDelta = MaxJoints(original, candidate, Enumerable.Range(0, 5));
            int[] footIndices = { 7, 8, 11, 12 };
            float xz = original.Zip(candidate, (a, b) => footIndices.Max(i => Vector2.Distance(new Vector2(a.joints[i].x, a.joints[i].z), new Vector2(b.joints[i].x, b.joints[i].z)))).Max();
            float footRotation = original.Zip(candidate, (a, b) => footIndices.Max(i => Quaternion.Angle(a.rotations[i], b.rotations[i]))).Max();
            float minimum = candidate.Min(f => Mathf.Min(f.leftSole, f.rightSole));
            float supportGap = 0f, flightDelta = 0f, slipDelta = 0f;
            float dt = period / Dense;
            for (int f = 0; f < Dense; f++)
            for (int side = 0; side < 2; side++)
            {
                float before = side == 0 ? original[f].leftSole : original[f].rightSole;
                float after = side == 0 ? candidate[f].leftSole : candidate[f].rightSole;
                if (before <= BlendBand) supportGap = Mathf.Max(supportGap, after);
                if (before >= .03f) flightDelta = Mathf.Max(flightDelta, Mathf.Abs(after - before));
                if (before < .03f)
                {
                    int i = side == 0 ? 7 : 11, previous = (f + Dense - 1) % Dense, next = (f + 1) % Dense;
                    float a = (original[next].joints[i].z - original[previous].joints[i].z) / (2f * dt);
                    float b = (candidate[next].joints[i].z - candidate[previous].joints[i].z) / (2f * dt);
                    slipDelta = Mathf.Max(slipDelta, Mathf.Abs(a - b));
                }
            }
            float loopPosition = footIndices.Max(i => Vector3.Distance(candidate[0].joints[i], candidate[Dense].joints[i]));
            float Mismatch(Frame[] frames) => footIndices.Max(i => Vector3.Distance((frames[1].joints[i] - frames[0].joints[i]) / dt, (frames[Dense].joints[i] - frames[Dense - 1].joints[i]) / dt));
            float loopVelocity = Mismatch(candidate), originalLoopVelocity = Mismatch(original);
            int Flight(Frame[] frames) => frames.Take(Dense).Count(f => Mathf.Min(f.leftSole, f.rightSole) > .03f);
            receipt["dense_phases"] = Dense + 1; receipt["upper_body_max_delta_m"] = upperDelta;
            receipt["foot_toe_xz_max_delta_m"] = xz; receipt["foot_toe_rotation_max_delta_degrees"] = footRotation; receipt["minimum_sole_m"] = minimum;
            receipt["maximum_corrected_support_gap_m"] = supportGap; receipt["unmodified_flight_sole_max_delta_m"] = flightDelta;
            receipt["support_ankle_sliding_max_change_mps"] = slipDelta;
            receipt["loop_position_delta_m"] = loopPosition; receipt["loop_velocity_mismatch_mps"] = loopVelocity;
            receipt["original_loop_velocity_mismatch_mps"] = originalLoopVelocity;
            receipt["both_feet_above3cm_original"] = Flight(original); receipt["both_feet_above3cm_candidate"] = Flight(candidate);
            Require(upperDelta <= .002f, "Correction changed hip/upper body more than 2mm.");
            Require(xz <= .002f, "Foot/toe horizontal target changed more than 2mm.");
            Require(footRotation <= 1f, "Correction changed original foot/toe articulation by more than one degree.");
            Require(minimum >= -.002f, "Corrected full-cycle sole still penetrates more than 2mm.");
            Require(supportGap <= .008f, "Correction replaces support penetration with >8mm hover.");
            Require(flightDelta <= .002f && Math.Abs(Flight(original) - Flight(candidate)) <= 2, "Correction changed flight instead of only contact.");
            Require(slipDelta <= .4f, "Correction worsens support ankle horizontal velocity by more than 0.4m/s.");
            Require(loopPosition <= .001f && loopVelocity <= originalLoopVelocity + .1f, "Loop closure/velocity is worse than the original.");
        }
        private static float MaxJoints(Frame[] a, Frame[] b, IEnumerable<int> indices) => a.Zip(b, (x, y) => indices.Max(i => Vector3.Distance(x.joints[i], y.joints[i]))).Max();
        private static JArray Windows(bool[] mask)
        {
            var result = new JArray(); int start = -1;
            for (int i = 0; i <= Keys; i++)
            {
                bool active = i < Keys && mask[i];
                if (active && start < 0) start = i;
                if (!active && start >= 0) { result.Add(new JArray(start / (float)Keys, (i - 1) / (float)Keys)); start = -1; }
            }
            return result;
        }
        private static void WriteFrames(string path, Frame[] frames) => File.WriteAllText(path, JsonConvert.SerializeObject(new
        {
            joints = Tracked.Select(b => b.ToString()).ToArray(), coordinates = "root-local metres; min sole across all authored world tiers",
            frames = frames.Select(f => new { f.phase, f.leftSole, f.rightSole, f.tierLeft, f.tierRight,
                joints = f.joints.Select(v => new[] { v.x, v.y, v.z }).ToArray(),
                rotations = f.rotations.Select(q => new[] { q.x, q.y, q.z, q.w }).ToArray(),
                bodyRotation = new[] { f.bodyRotation.x, f.bodyRotation.y, f.bodyRotation.z, f.bodyRotation.w }, f.humanScale })
        }, Formatting.Indented));
        private static void SaveClip(AnimationClip clip, string path)
        {
            var previous = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (previous == null) AssetDatabase.CreateAsset(clip, path);
            else { EditorUtility.CopySerialized(clip, previous); EditorUtility.SetDirty(previous); Object.DestroyImmediate(clip); }
        }
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
