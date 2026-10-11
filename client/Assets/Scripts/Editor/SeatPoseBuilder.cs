using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Seated pose variants made from the CC0 Quaternius sit loop (act_sit) by editing Humanoid muscles, with each edit
    /// found by a measured search on the real family reference looks (not hand-tuned angles):
    /// - act_sit_lounge (sofa, armchair): torso reclined against a tilted back, legs a little longer;
    /// - act_sit_beanbag (sink-in): deep recline, knees above the hips, feet forward on the floor;
    /// - act_sit_stool (bar-stool perch): upright, knees bent more so the feet tuck onto the footrest.
    /// The breathing/idle motion of the source loop is kept (the same offsets on every key). Feet are then placed
    /// exactly (floor or footrest) by CharacterIK at LOD0. Also measures every seated state's hip/back/feet contact per
    /// family into SeatFits.asset (SeatFitCatalog.poses) for seat placement.
    /// Batch: -executeMethod CTLife.EditorTools.SeatPoseBuilder.BuildBatch [-ctlife-poseOut /abs.json] [-ctlife-poseProbe 1]
    /// </summary>
    public static class SeatPoseBuilder
    {
        public const string OutDir = "Assets/Art/animations/seated/";
        private const string Anim = "Assets/Art/animations/";
        private const string MalePrefab = "Assets/Art/characters/cast/prefabs/player_m_kol.prefab";
        // f reference: a trousered look. Every f look shares one skeleton (LooksProbe), but the dress looks' skirts are
        // skinned to the pelvis, so their lowest "pelvis" vertices are the hem, not the seat contact.
        private const string FemalePrefab = "Assets/Art/characters/cast/prefabs/player_f_builder.prefab";
        private static readonly string[] Torso = { "Spine Front-Back", "Chest Front-Back", "UpperChest Front-Back" };
        private static readonly string[] Nod = { "Neck Nod Down-Up", "Head Nod Down-Up" };
        private static readonly string[] Thigh = { "Left Upper Leg Front-Back", "Right Upper Leg Front-Back" };
        private static readonly string[] Knee = { "Left Lower Leg Stretch", "Right Lower Leg Stretch" };
        private static readonly string[] Ankle = { "Left Foot Up-Down", "Right Foot Up-Down" };
        private static readonly string[] Spread = { "Left Upper Leg In-Out", "Right Upper Leg In-Out" };

        public static readonly string[] SeatedStates =
            { "act_sit", "act_type", "act_sit_lounge", "act_sit_beanbag", "act_sit_stool", CafeSeatMotionProfile.SocialState, CafeSeatMotionProfile.TypingState };
        // (act_sit/lounge/beanbag/stool are seatHeight blend trees; the rest single clips.)

        public static void BuildBatch()
        {
            int code = 0;
            try
            {
                var report = Arg("-ctlife-poseProbe") == "1" ? RunProbe() : Arg("-ctlife-poseProbe") == "goal" ? GoalProbe() : Arg("-ctlife-poseProbe") == "ctrl" ? ControllerProbe() : Arg("-ctlife-poseProbe") == "looks" ? LooksProbe() : Arg("-ctlife-poseProbe") == "weights" ? WeightScan() : Build();
                var outPath = Arg("-ctlife-poseOut");
                if (!string.IsNullOrEmpty(outPath)) { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); File.WriteAllText(outPath, report.ToString()); }
                Debug.Log("CTLIFE [seat-pose] " + report["summary"]);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // ------------------------------------------------------------------ probe (a pose with muscle deltas)

        internal sealed class Probe : IDisposable
        {
            private readonly ClipMeasure.Rig _rig;
            private readonly HumanPoseHandler _handler;
            private HumanPose _base;
            private readonly Dictionary<string, int> _index = new Dictionary<string, int>();
            public Transform Hips, Neck, LKnee, RKnee, LFoot, RFoot, LToes, RToes;

            public Probe(GameObject prefab, AnimationClip clip, float phase)
            {
                _rig = new ClipMeasure.Rig(prefab, clip);
                _rig.At(clip.length * phase);
                var a = _rig.animator;
                _handler = new HumanPoseHandler(a.avatar, a.transform);
                _handler.GetHumanPose(ref _base);
                for (int i = 0; i < HumanTrait.MuscleCount; i++) _index[HumanTrait.MuscleName[i]] = i;
                Hips = a.GetBoneTransform(HumanBodyBones.Hips); Neck = a.GetBoneTransform(HumanBodyBones.Neck);
                LKnee = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); RKnee = a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                LFoot = a.GetBoneTransform(HumanBodyBones.LeftFoot); RFoot = a.GetBoneTransform(HumanBodyBones.RightFoot);
                LToes = a.GetBoneTransform(HumanBodyBones.LeftToes); RToes = a.GetBoneTransform(HumanBodyBones.RightToes);
            }

            public float Base(string muscle) => _base.muscles[_index[muscle]];

            /// <summary>Muscle deltas plus a body pitch (degrees, positive = recline backwards about the hips' right axis).</summary>
            public void Pose(IDictionary<string, float> deltas, float pitch = 0f)
            {
                var p = _base;
                p.muscles = (float[])_base.muscles.Clone();
                if (deltas != null)
                    foreach (var kv in deltas) p.muscles[_index[kv.Key]] = Mathf.Clamp(p.muscles[_index[kv.Key]] + kv.Value, -1f, 1f);
                p.bodyRotation = Recliner(pitch) * _base.bodyRotation;
                _handler.SetHumanPose(ref p);
            }

            /// <summary>Torso recline from vertical, degrees (positive = leaning back; the character faces +z).</summary>
            public float Recline { get { var v = Neck.position - Hips.position; return Mathf.Atan2(-v.z, v.y) * Mathf.Rad2Deg; } }
            public float KneeRise => (LKnee.position.y + RKnee.position.y) * 0.5f - Hips.position.y;
            public float FootDrop => Hips.position.y - (LFoot.position.y + RFoot.position.y) * 0.5f;
            public float FootForward => (LFoot.position.z + RFoot.position.z) * 0.5f - Hips.position.z;
            public float ToeForward => (LToes.position.z + RToes.position.z) * 0.5f - Hips.position.z;
            private SkinnedMeshRenderer _smr;
            private Mesh _baked;
            private int[] _butt, _feet, _spine, _calf;

            private void Bake(out Vector3[] world)
            {
                if (_smr == null)
                {
                    var go = Hips.root.gameObject;
                    _smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).First();
                    _baked = new Mesh();
                    var a = _rig.animator;
                    int Idx(HumanBodyBones b) { var t = a.GetBoneTransform(b); return t == null ? -2 : Array.IndexOf(_smr.bones, t); }
                    // Pelvis only: thigh vertices would swap in and out of the contact as the legs rotate.
                    var hips = new HashSet<int> { Idx(HumanBodyBones.Hips) };
                    var feet = new HashSet<int> { Idx(HumanBodyBones.LeftFoot), Idx(HumanBodyBones.LeftToes), Idx(HumanBodyBones.RightFoot), Idx(HumanBodyBones.RightToes) };
                    var w = _smr.sharedMesh.boneWeights;
                    int Dom(BoneWeight x) => x.weight0 >= x.weight1 && x.weight0 >= x.weight2 && x.weight0 >= x.weight3 ? x.boneIndex0 : x.weight1 >= x.weight2 && x.weight1 >= x.weight3 ? x.boneIndex1 : x.weight2 >= x.weight3 ? x.boneIndex2 : x.boneIndex3;
                    _butt = Enumerable.Range(0, w.Length).Where(i => hips.Contains(Dom(w[i]))).ToArray();
                    _feet = Enumerable.Range(0, w.Length).Where(i => feet.Contains(Dom(w[i]))).ToArray();
                    var spine = new HashSet<int> { Idx(HumanBodyBones.Spine), Idx(HumanBodyBones.Chest), Idx(HumanBodyBones.UpperChest), Idx(HumanBodyBones.Hips) };
                    _spine = Enumerable.Range(0, w.Length).Where(i => spine.Contains(Dom(w[i]))).ToArray();
                    var calf = new HashSet<int> { Idx(HumanBodyBones.LeftLowerLeg), Idx(HumanBodyBones.RightLowerLeg) };
                    _calf = Enumerable.Range(0, w.Length).Where(i => calf.Contains(Dom(w[i]))).ToArray();
                }
                _smr.BakeMesh(_baked, true);
                var v = _baked.vertices; var m = _smr.transform.localToWorldMatrix;
                world = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) world[i] = m.MultiplyPoint3x4(v[i]);
            }

            /// <summary>Lowest buttock point (within 15 cm of the rearmost buttock) and lowest sole point.</summary>
            public (Vector3 hip, Vector3 sole) Contacts()
            {
                Bake(out var w);
                var low = _butt.OrderBy(i => w[i].y).Take(12).ToList();
                var hip = new Vector3(0f, low.Average(i => w[i].y), low.Average(i => w[i].z));
                var sl = _feet.OrderBy(i => w[i].y).Take(16).ToList();
                var sole = new Vector3(0f, sl.Average(i => w[i].y), sl.Average(i => w[i].z));
                return (hip, sole);
            }
            /// <summary>Hip contact, rearmost lumbar point (12–30 cm above it) and both soles, root space (scale 1).</summary>
            public SeatFitCatalog.PoseMetric Metric(string state, string family, float height)
            {
                Bake(out var w);
                var low = _butt.OrderBy(i => w[i].y).Take(12).ToList();
                var hip = new Vector3(0f, low.Average(i => w[i].y), low.Average(i => w[i].z));
                var lumbar = _spine.Where(i => w[i].y > hip.y + 0.12f && w[i].y < hip.y + 0.3f).ToList();
                var back = lumbar.Count == 0 ? hip : new Vector3(0f, hip.y + 0.2f, lumbar.OrderBy(i => w[i].z).Take(8).Average(i => w[i].z));
                Vector3 Sole(bool left)
                {
                    var v = _feet.Where(i => left ? w[i].x < 0 : w[i].x >= 0).OrderBy(i => w[i].y).Take(10).ToList();
                    return v.Count == 0 ? Vector3.zero : new Vector3(v.Average(i => w[i].x), v.Average(i => w[i].y), v.Average(i => w[i].z));
                }
                // Rearmost calf point 4+ cm below the seat line: what would cut into an upholstered seat's front.
                var under = _calf.Where(i => w[i].y < hip.y - 0.04f).ToList();
                var calfBack = under.Count == 0 ? Vector3.zero : new Vector3(0f, under.Min(i => w[i].y), under.OrderBy(i => w[i].z).Take(6).Average(i => w[i].z));
                return new SeatFitCatalog.PoseMetric { state = state, family = family, height = height, hipContact = hip, backContact = back, leftFoot = Sole(true), rightFoot = Sole(false), calfBack = calfBack,
                    leftAnkle = LFoot.position, rightAnkle = RFoot.position,
                    leftHand = Hips.root.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftHand).position,
                    rightHand = Hips.root.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand).position };
            }

            public string DebugSoles()
            {
                Bake(out var w);
                var bw = _smr.sharedMesh.boneWeights; var names = _smr.bones.Select(b => b ? b.name : "null").ToArray();
                return string.Join(" | ", _feet.Where(i => w[i].x < 0).OrderBy(i => w[i].y).Take(6).Select(i =>
                    $"{i} {w[i]:F3} {names[bw[i].boneIndex0]}:{bw[i].weight0:F2} {names[bw[i].boneIndex1]}:{bw[i].weight1:F2}"));
            }

            /// <summary>Joint-space (continuous) leg metrics: hips bone → ankle midpoint.</summary>
            public Vector3 Ankles => (LFoot.position + RFoot.position) * 0.5f;
            public float JointDrop => Hips.position.y - Ankles.y;
            public float JointForward => Ankles.z - Hips.position.z;

            public float SoleDrop() { var (h, s) = Contacts(); return h.y - s.y; }
            public float SoleForward() { var (h, s) = Contacts(); return s.z - h.z; }

            public JObject Snapshot() => new JObject
            {
                ["recline"] = Math.Round(Recline, 1), ["kneeRise"] = Math.Round(KneeRise, 3), ["footDrop"] = Math.Round(FootDrop, 3),
                ["footForward"] = Math.Round(FootForward, 3), ["toeForward"] = Math.Round(ToeForward, 3),
            };

            public void Dispose() { if (_baked != null) Object.DestroyImmediate(_baked); _handler?.Dispose(); _rig?.Dispose(); }
        }

        private static JObject RunProbe()
        {
            var report = new JObject();
            var sit = CharacterFeelController.Clip(Anim + "act_sit.fbx");
            var male = AssetDatabase.LoadAssetAtPath<GameObject>(MalePrefab);
            using var probe = new Probe(male, sit, 0.3f);
            probe.Pose(null);
            report["base"] = probe.Snapshot();
            foreach (var group in new (string name, string[] muscles)[] { ("torso", Torso), ("thigh", Thigh), ("knee", Knee), ("ankle", Ankle), ("nod", Nod) })
                foreach (float d in new[] { -0.3f, 0.3f })
                {
                    var deltas = group.muscles.ToDictionary(m => m, m => d);
                    probe.Pose(deltas);
                    report[$"{group.name}{d:+0.0;-0.0}"] = probe.Snapshot();
                }
            var bindings = AnimationUtility.GetCurveBindings(sit).Select(b => b.propertyName).ToArray();
            report["curve_count"] = bindings.Length;
            report["has_torso_curves"] = Torso.All(m => bindings.Contains(m));
            report["sample_bindings"] = new JArray(bindings.Take(12));
            report["summary"] = "probe: " + report["base"];
            return report;
        }

        /// <summary>
        /// Diagnostic: how a Humanoid clip's stored foot IK goal (LeftFootT/Q) relates to the FK ankle. For a source clip,
        /// compares the curve values with inverse(bodyRotation)·(ankle − bodyPosition)/humanScale and the rotation offset.
        /// </summary>
        /// <summary>Diagnostic: the fitted child clip alone vs the shared controller + family override at a seat height.</summary>
        public static JObject ControllerProbe()
        {
            var report = new JObject();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath);
            var rain = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.RainControllerPath);
            var snow = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.SnowControllerPath);
            string Measure(Animator a)
            {
                var h = a.GetBoneTransform(HumanBodyBones.Hips); var t = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var k = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var f = a.GetBoneTransform(HumanBodyBones.LeftFoot);
                return $"hips {h.position.y - a.transform.position.y:F3} knee {Vector3.Angle(t.position - k.position, f.position - k.position):F1} foot {f.position.y - a.transform.position.y:F3} bodyPos {a.bodyPosition:F3} humanScale {a.humanScale:F3}";
            }
            foreach (var (family, prefabPath, over) in new[] { ("m", MalePrefab, snow), ("f", FemalePrefab, rain) })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var st = ctrl.layers[0].stateMachine.states.Select(x => x.state).First(x => x.name == "act_sit");
                var child = ((BlendTree)st.motion).children[1].motion as AnimationClip;
                var clip = family == "f" && rain[child] != null ? rain[child] : child;
                using (var rig = new ClipMeasure.Rig(prefab, clip)) { rig.At(clip.length * 0.3f); report[$"{family}.clip {clip.name}"] = Measure(rig.animator); }
                string[] legs = { "Left Upper Leg Front-Back", "Left Upper Leg In-Out", "Left Lower Leg Stretch", "Left Foot Up-Down", "Spine Front-Back" };
                var bindings = AnimationUtility.GetCurveBindings(clip).ToDictionary(b => b.propertyName, b => b);
                report[$"{family}.curves"] = string.Join(" ", legs.Select(n => bindings.TryGetValue(n, out var b) ? $"{n}={AnimationUtility.GetEditorCurve(clip, b).Evaluate(clip.length * 0.3f):F3}" : $"{n}=missing"))
                    + " RootT.y=" + (bindings.TryGetValue("RootT.y", out var ry) ? AnimationUtility.GetEditorCurve(clip, ry).Evaluate(clip.length * 0.3f).ToString("F3") : "-");
                using (var probe = new Probe(prefab, clip, 0.3f))
                {
                    var hp = new HumanPose(); var h0 = new HumanPoseHandler(probe.Hips.root.GetComponent<Animator>().avatar, probe.Hips.root);
                    h0.GetHumanPose(ref hp);
                    int I(string n) => Array.IndexOf(HumanTrait.MuscleName, n);
                    report[$"{family}.animator muscles"] = string.Join(" ", legs.Select(n => $"{n}={hp.muscles[I(n)]:F3}")) + $" bodyPos {hp.bodyPosition:F3}";
                    probe.Pose(null);
                    h0.GetHumanPose(ref hp);
                    report[$"{family}.after SetHumanPose"] = Measure(probe.Hips.root.GetComponent<Animator>()) + " | " + string.Join(" ", legs.Select(n => $"{n}={hp.muscles[I(n)]:F3}")) + $" bodyPos {hp.bodyPosition:F3}";
                    var mm = probe.Metric("act_sit", family, 0.44f);
                    report[$"{family}.metric"] = $"hip {mm.hipContact:F3} back {mm.backContact:F3} soles {mm.leftFoot:F3}";
                    h0.Dispose();
                }
                var go = Object.Instantiate(prefab);
                try
                {
                    var a = go.GetComponent<Animator>();
                    AnimatorUtility.DeoptimizeTransformHierarchy(go);
                    a.applyRootMotion = false; a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    a.runtimeAnimatorController = over != null ? (RuntimeAnimatorController)over : ctrl;
                    a.Rebind();
                    a.SetFloat("seatHeight", ((BlendTree)st.motion).children[1].threshold);
                    a.Play("act_sit", 0, 0.3f);
                    a.Update(0f);
                    report[$"{family}.controller {a.runtimeAnimatorController.name} t0"] = Measure(a) + " clips " + string.Join(",", a.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name + ":" + c.weight.ToString("F2")));
                    for (int i = 0; i < 10; i++) a.Update(0.1f);
                    report[$"{family}.controller {a.runtimeAnimatorController.name} t1"] = Measure(a) + " clips " + string.Join(",", a.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name + ":" + c.weight.ToString("F2")));
                }
                finally { Object.DestroyImmediate(go); }
            }
            report["summary"] = "controller probe";
            return report;
        }

        /// <summary>Diagnostic: bones and contacts of each family's fitted act_sit child (probe pose, root space).</summary>
        public static JObject LooksProbe()
        {
            var report = new JObject();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath);
            var rain = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.RainControllerPath);
            var st = ctrl.layers[0].stateMachine.states.Select(x => x.state).First(x => x.name == "act_sit");
            var child = ((BlendTree)st.motion).children[1].motion as AnimationClip;
            foreach (var path in new[] { MalePrefab, FemalePrefab, "Assets/Art/characters/cast/prefabs/player_f_kol.prefab" })
            {
                string fam = Path.GetFileName(path).StartsWith("player_f_") ? "f" : "m";
                var clip = fam == "f" && rain[child] != null ? rain[child] : child;
                using var probe = new Probe(AssetDatabase.LoadAssetAtPath<GameObject>(path), clip, 0.3f);
                probe.Pose(null);
                var a = probe.Hips.root.GetComponent<Animator>();
                var m = probe.Metric("act_sit", fam, 0.44f);
                string B(HumanBodyBones b) { var t = a.GetBoneTransform(b); return t == null ? "-" : t.position.ToString("F3"); }
                foreach (var r in probe.Hips.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bones = r.bones;
                    int li = Array.IndexOf(bones, a.GetBoneTransform(HumanBodyBones.LeftFoot));
                    var baked = new Mesh(); r.BakeMesh(baked, true);
                    var vv = baked.vertices; var ww = r.sharedMesh.boneWeights; var mm = r.transform.localToWorldMatrix;
                    var footVerts = Enumerable.Range(0, vv.Length).Where(i => ww[i].boneIndex0 == li && ww[i].weight0 > 0.5f).Select(i => mm.MultiplyPoint3x4(vv[i])).ToList();
                    Object.DestroyImmediate(baked);
                    report[Path.GetFileNameWithoutExtension(path) + " smr " + r.name] = $"verts {vv.Length} bones {bones.Length} nullBones {bones.Count(b => b == null)} leftFootIdx {li} footVerts {footVerts.Count} centroid {(footVerts.Count > 0 ? new Vector3(footVerts.Average(q => q.x), footVerts.Average(q => q.y), footVerts.Average(q => q.z)) : Vector3.zero):F3} root {(r.rootBone ? r.rootBone.name : "-")} enabled {r.enabled} active {r.gameObject.activeInHierarchy}";
                }
                report[Path.GetFileNameWithoutExtension(path) + " lowest feet"] = probe.DebugSoles();
                report[Path.GetFileNameWithoutExtension(path) + " " + clip.name] =
                    $"hipJoint {B(HumanBodyBones.LeftUpperLeg)} knee {B(HumanBodyBones.LeftLowerLeg)} ankle {B(HumanBodyBones.LeftFoot)} toes {B(HumanBodyBones.LeftToes)} | contact {m.hipContact:F3} soleL {m.leftFoot:F3} calf {m.calfBack:F3}";
            }
            report["summary"] = "looks probe";
            return report;
        }

        /// <summary>
        /// Diagnostic: per cast mesh, vertices of the rest pose below 0.25 m (feet/shoes) that carry weight on a bone
        /// far from the feet (Head, Neck, Spine, Chest, hands, arms): skinning that smears shoes once the legs move.
        /// </summary>
        public static JObject WeightScan()
        {
            var report = new JObject();
            var far = new[] { "Head", "Neck", "Spine", "Chest", "UpperChest", "Hand", "Arm", "Shoulder", "Eye", "Jaw" };
            int bad = 0, meshes = 0;
            var roots = new[] { "Assets/Art/characters/cast/looks", "Assets/Art/characters/authored" };
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", roots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                if (go.GetComponent<Animator>() != null) AnimatorUtility.DeoptimizeTransformHierarchy(go);
                try
                {
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = r.sharedMesh; if (mesh == null) continue;
                    meshes++;
                    var names = r.bones.Select(b => b ? b.name : "").ToArray();
                    if (names.Length == 0) { report[path + "#" + r.name] = "optimized (no bone names)"; continue; }
                    var bakedMesh = new Mesh(); r.BakeMesh(bakedMesh, true);
                    var mtx = r.transform.localToWorldMatrix;
                    var v = bakedMesh.vertices.Select(x => mtx.MultiplyPoint3x4(x) - go.transform.position).ToArray(); var w = mesh.boneWeights;
                    Object.DestroyImmediate(bakedMesh);
                    var hits = new Dictionary<string, int>(); int n = 0;
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (v[i].y > 0.25f) continue;
                        void Check(int bi, float wt) { if (wt < 0.05f || bi >= names.Length) return; var nm = names[bi]; if (far.Any(f => nm.Contains(f))) { hits[nm] = hits.GetValueOrDefault(nm) + 1; n++; } }
                        Check(w[i].boneIndex0, w[i].weight0); Check(w[i].boneIndex1, w[i].weight1); Check(w[i].boneIndex2, w[i].weight2); Check(w[i].boneIndex3, w[i].weight3);
                    }
                    if (n > 0) { bad++; report[path + "#" + r.name] = string.Join(", ", hits.Select(kv => $"{kv.Key}:{kv.Value}")); }
                }
                }
                finally { Object.DestroyImmediate(go); }
            }
            // The bone itself: where is the "Head" these vertices follow, and is it the Humanoid head?
            foreach (var probePath in new[] { FemalePrefab, "Assets/Art/characters/authored/RainIdle/RainAdult_RelaxedIdle.fbx" })
            {
                var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(probePath));
                try
                {
                    var an = inst.GetComponent<Animator>();
                    if (an != null) AnimatorUtility.DeoptimizeTransformHierarchy(inst);
                    var smr = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh.vertexCount).First();
                    var heads = smr.bones.Where(b => b != null && b.name == "Head").ToList();
                    var human = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Head) : null;
                    report["head bones " + Path.GetFileName(probePath)] = string.Join("; ", heads.Select(h => $"{AnimationUtility.CalculateTransformPath(h, inst.transform)} at {h.position:F3} humanoidHead={(h == human)}"));
                    // Bind pose of that bone (where the mesh was skinned against it).
                    int hi = Array.FindIndex(smr.bones, b => b != null && b.name == "Head");
                    if (hi >= 0) report["head bindpose " + Path.GetFileName(probePath)] = (smr.transform.localToWorldMatrix * smr.sharedMesh.bindposes[hi].inverse).GetColumn(3).ToString("F3");
                }
                finally { Object.DestroyImmediate(inst); }
            }
            report["summary"] = $"{bad} of {meshes} skinned meshes have foot-level vertices weighted to far bones";
            return report;
        }

        public static JObject GoalProbe()
        {
            var report = new JObject();
            var sit = CharacterFeelController.Clip(Anim + "act_sit.fbx");
            var walk = CharacterFeelController.Clip(Anim + "loco_walk.fbx");
            var male = AssetDatabase.LoadAssetAtPath<GameObject>(MalePrefab);
            foreach (var clip in new[] { sit, walk })
            {
                var b = AnimationUtility.GetCurveBindings(clip).ToDictionary(x => x.propertyName, x => x);
                AnimationCurve C(string n) => AnimationUtility.GetEditorCurve(clip, b[n]);
                var tx = C("LeftFootT.x"); var ty = C("LeftFootT.y"); var tz = C("LeftFootT.z");
                var qx = C("LeftFootQ.x"); var qy = C("LeftFootQ.y"); var qz = C("LeftFootQ.z"); var qw = C("LeftFootQ.w");
                var rows = new JArray();
                using var rig = new ClipMeasure.Rig(male, clip, footIk: false);
                foreach (float ph in new[] { 0.1f, 0.4f, 0.7f })
                {
                    float t = clip.length * ph;
                    rig.At(t);
                    var a = rig.animator;
                    var foot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var inv = Quaternion.Inverse(a.bodyRotation);
                    var derived = inv * (foot.position - a.bodyPosition) / a.humanScale;
                    var stored = new Vector3(tx.Evaluate(t), ty.Evaluate(t), tz.Evaluate(t));
                    var storedQ = new Quaternion(qx.Evaluate(t), qy.Evaluate(t), qz.Evaluate(t), qw.Evaluate(t));
                    var offset = Quaternion.Inverse(inv * foot.rotation) * storedQ;
                    rows.Add($"t {ph:F1}: stored {stored:F4} derived {derived:F4} diff {(stored - derived).magnitude:F4}; rotOffset {offset.eulerAngles:F1}; humanScale {a.humanScale:F4}");
                }
                report[clip.name + "@" + Path.GetFileName(AssetDatabase.GetAssetPath(clip))] = rows;
            }
            report["summary"] = "goal probe";
            return report;
        }

        // ------------------------------------------------------------------ build

        /// <summary>World-space rotation that reclines a +z-facing body by `pitch` degrees (top towards -z).</summary>
        internal static Quaternion Recliner(float pitch) => Quaternion.AngleAxis(-pitch, Vector3.right);

        /// <summary>A seated loop per seat kind: a 1D blend over "seatHeight" (hip contact → sole drop, scale-1 metres).</summary>
        public sealed class KindSpec
        {
            public string state;          // controller state (blend tree)
            public float recline;         // degrees added to the source sit
            public float footForward;     // sole forward of the hip contact (m), scale 1
            public float[] heights;       // seatHeight children
            public float stance;          // ankle half-width (m) when the feet must come together (a stool's bar); 0 = source
        }

        public static readonly KindSpec[] Kinds =
        {
            new KindSpec { state = "act_sit", recline = 0f, footForward = 0.47f, heights = new[] { 0.36f, 0.52f } },
            new KindSpec { state = "act_sit_lounge", recline = 16f, footForward = 0.58f, heights = new[] { 0.24f, 0.50f } },
            new KindSpec { state = "act_sit_beanbag", recline = 28f, footForward = 0.66f, heights = new[] { 0.46f, 0.66f } },
            new KindSpec { state = "act_sit_stool", recline = 2f, footForward = 0.12f, heights = new[] { 0.44f, 0.68f }, stance = 0.055f },
        };

        public const string SeatHeightParam = "seatHeight";

        [MenuItem("CT Life/Characters/Build seated pose variants")]
        public static JObject Build()
        {
            var report = new JObject();
            var sit = CharacterFeelController.Clip(Anim + "act_sit.fbx") ?? throw new FileNotFoundException("act_sit.fbx");
            var male = AssetDatabase.LoadAssetAtPath<GameObject>(MalePrefab) ?? throw new FileNotFoundException(MalePrefab);
            var female = AssetDatabase.LoadAssetAtPath<GameObject>(FemalePrefab) ?? throw new FileNotFoundException(FemalePrefab);
            Directory.CreateDirectory(OutDir);
            float r0;
            using (var p = new Probe(male, sit, 0.3f)) { p.Pose(null); r0 = p.Recline; report["source"] = p.Snapshot(); }
            // Flat-foot reference: the foot pitch of the standing idle (ankle above the toe joint when the sole is flat).
            var idle = CharacterFeelController.Clip(Anim + "loco_idle.fbx");
            using (var p = new Probe(male, idle, 0.3f)) { p.Pose(null); _standingFootPitch = FootPitch(p); }
            report["standing_foot_pitch"] = Math.Round(_standingFootPitch, 1);
            // Calibrate the clip-space body-rotation frame (RootQ is not HumanPose.bodyRotation's frame): write +10° and see.
            _clipPitchSign = 1f;
            var test = WriteClip(sit, "_calibration", new Dictionary<string, float>(), 10f, save: false);
            float measured;
            using (var pc = new Probe(male, test, 0.3f)) { pc.Pose(null); measured = pc.Recline - r0; }
            Object.DestroyImmediate(test);
            _clipPitchSign = Mathf.Sign(measured);
            report["clip_pitch_calibration"] = $"+10° written → {measured:F1}° measured; sign {_clipPitchSign}";

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath);
            var rain = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.RainControllerPath);
            if (!ctrl.parameters.Any(x => x.name == SeatHeightParam))
                ctrl.AddParameter(new AnimatorControllerParameter { name = SeatHeightParam, type = AnimatorControllerParameterType.Float, defaultFloat = 0.44f });
            var sm = ctrl.layers[0].stateMachine;
            var rainPairs = new Dictionary<AnimationClip, AnimationClip>();
            var kinds = new JObject();
            var catalogSeats = AssetDatabase.LoadAssetAtPath<SeatFitCatalog>(SeatFitBuilder.CatalogPath)?.seats ?? new List<SeatFitCatalog.Seat>();
            foreach (var k in Kinds)
            {
                var children = new List<(float h, AnimationClip m)>();
                var o = new JObject();
                // Children at the seat heights the shipped seats actually need (exact there; blended between).
                var needed = catalogSeats.Where(x => SeatFitCatalog.PoseFor(x.kind, "act_sit") == k.state)
                    .Select(x => Mathf.Round(SeatFitCatalog.SeatHeight(x, 1f) * 50f) / 50f).Distinct().OrderBy(x => x).ToList();
                var heights = needed.Count == 0 ? k.heights.ToList()
                    : needed.Count <= 4 ? needed
                    : new List<float> { needed[0], needed[needed.Count / 3], needed[2 * needed.Count / 3], needed[needed.Count - 1] };
                o["needed"] = string.Join(", ", needed.Select(x => x.ToString("F2")));
                foreach (float h in heights)
                {
                    AnimationClip mClip = null;
                    foreach (var (fam, prefab) in new[] { ("m", male), ("f", female) })
                    {
                        var deltas = Solve(prefab, sit, k, h, r0, out float pitch, out var achieved);
                        var name = $"{k.state}_{fam}_{Mathf.RoundToInt(h * 100)}";
                        var clip = WriteClip(sit, name, deltas, pitch);
                        o[name] = achieved;
                        if (fam == "m") mClip = clip; else rainPairs[mClip] = clip;
                    }
                    children.Add((h, mClip));
                }
                kinds[k.state] = o;
                // Blend tree state (Foot IK off: the children's FK feet are the fitted contact).
                var st = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == k.state) ?? sm.AddState(k.state);
                var tree = st.motion as BlendTree;
                if (tree == null || tree.name != k.state + "_seat")
                {
                    tree = new BlendTree { name = k.state + "_seat", hideFlags = HideFlags.HideInHierarchy };
                    AssetDatabase.AddObjectToAsset(tree, ctrl);
                }
                tree.blendType = BlendTreeType.Simple1D;
                tree.blendParameter = SeatHeightParam;
                tree.useAutomaticThresholds = false;
                tree.children = Array.Empty<ChildMotion>();
                foreach (var (h, clip) in children) tree.AddChild(clip, h);
                st.motion = tree;
                st.iKOnFeet = false;
                st.writeDefaultValues = true;
                EditorUtility.SetDirty(tree);
            }
            EditorUtility.SetDirty(ctrl);
            if (rain != null)
            {
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(rain.overridesCount);
                rain.GetOverrides(pairs);
                for (int i = 0; i < pairs.Count; i++)
                    if (pairs[i].Key != null && rainPairs.TryGetValue(pairs[i].Key, out var f)) pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, f);
                rain.ApplyOverrides(pairs);
                EditorUtility.SetDirty(rain);
            }
            AssetDatabase.SaveAssets();
            // Children of an earlier build that this one no longer uses (the needed seat heights changed): drop the clips
            // and their stale Rain override rows.
            var written = new HashSet<string>(rainPairs.SelectMany(kv => new[] { kv.Key, kv.Value }).Where(c => c != null).Select(AssetDatabase.GetAssetPath));
            var removed = new JArray();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { OutDir.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (written.Contains(path) || Path.GetFileName(path).StartsWith("_")) continue;
                removed.Add(Path.GetFileName(path));
                AssetDatabase.DeleteAsset(path);
            }
            if (rain != null)
            {
                var so = new SerializedObject(rain);
                var list = so.FindProperty("m_Clips");
                for (int i = list.arraySize - 1; i >= 0; i--)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("m_OriginalClip").objectReferenceValue == null || e.FindPropertyRelative("m_OverrideClip").objectReferenceValue == null)
                        list.DeleteArrayElementAtIndex(i);
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rain);
                AssetDatabase.SaveAssets();
            }
            report["removed_stale_children"] = removed;
            report["kinds"] = kinds;
            report["summary"] = $"{Kinds.Length} seated blend states, {Kinds.Sum(k => k.heights.Length) * 2} fitted children in {OutDir}";
            return report;
        }

        /// <summary>
        /// On the family reference look (baked skin, scale 1): body pitch to the recline, then alternate thigh flexion (sole
        /// height: the sole lands `height` below the hip contact) and knee extension (sole reach `footForward`), then flat
        /// feet. Returns the achieved numbers.
        /// </summary>
        private static Dictionary<string, float> Solve(GameObject prefab, AnimationClip clip, KindSpec k, float height, float sourceRecline,
            out float pitch, out JObject achieved)
        {
            var deltas = new Dictionary<string, float>();
            float bodyPitch = 0f;
            float standingPitch = StandingFootPitch(prefab);
            using var probe = new Probe(prefab, clip, 0.3f);
            void Set(string[] muscles, float d) { foreach (var m in muscles) deltas[m] = d; }
            float Measure(Func<float> metric) { probe.Pose(deltas, bodyPitch); return metric(); }
            void Bisect(Action<float> apply, Func<float> metric, float target, float lo, float hi)
            {
                apply(lo); float mLo = Measure(metric);
                apply(hi); float mHi = Measure(metric);
                bool increasing = mHi > mLo;
                for (int i = 0; i < 22; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    apply(mid);
                    float m = Measure(metric);
                    if ((m < target) == increasing) lo = mid; else hi = mid;
                }
                apply((lo + hi) * 0.5f);
            }
            Set(Torso, -k.recline / 160f);
            foreach (var n in Nod) deltas[n] = Mathf.Clamp(k.recline / 120f, -0.4f, 0.4f);
            Bisect(d => bodyPitch = d, () => probe.Recline, sourceRecline + k.recline, -40f, 60f);
            // Legs: thigh × knee grid search in JOINT space (hips bone → ankles: continuous), with the mesh offsets
            // (pelvis bone → buttock contact, ankle → sole) measured on this pose and corrected once after the solve.
            float bestT = 0f, bestK = 0f;
            float wantDrop = height, wantFwd = k.footForward;
            for (int pass = 0; pass < 3; pass++)
            {
                Set(Thigh, bestT); Set(Knee, bestK);
                // Feet together on a stool's bar: thigh adduction to the stance half-width (ankles).
                if (k.stance > 0f) Bisect(d => Set(Spread, d), () => (probe.RFoot.position.x - probe.LFoot.position.x) * 0.5f, k.stance, -1f, 1f);
                // Feet flat like standing (the sole height depends on the foot pitch) before measuring the offsets.
                Bisect(d => Set(Ankle, d), () => FootPitch(probe), standingPitch, -0.9f, 0.9f);
                probe.Pose(deltas, bodyPitch);
                var (hipC, soleC) = probe.Contacts();
                float contactOff = probe.Hips.position.y - hipC.y, soleOff = probe.Ankles.y - soleC.y;
                float contactZ = probe.Hips.position.z - hipC.z, soleZ = probe.Ankles.z - soleC.z;
                // drop(contact→sole) = jointDrop − contactOff + soleOff; forward(contact→sole) = jointFwd + contactZ − soleZ
                float jd = wantDrop + contactOff - soleOff, jf = wantFwd - contactZ + soleZ;
                float Error(float th, float kn)
                {
                    Set(Thigh, th); Set(Knee, kn);
                    probe.Pose(deltas, bodyPitch);
                    float d = probe.JointDrop - jd, f = probe.JointForward - jf;
                    return d * d * 100f + f * f;
                }
                float bestE = float.MaxValue;
                for (float th = -1f; th <= 1f + 1e-4f; th += 0.05f)
                    for (float kn = -1f; kn <= 1f + 1e-4f; kn += 0.05f)
                    {
                        float e = Error(th, kn);
                        if (e < bestE) { bestE = e; bestT = th; bestK = kn; }
                    }
                foreach (float step in new[] { 0.0125f, 0.004f, 0.0012f })
                {
                    float ct = bestT, ck = bestK;
                    for (int i = -4; i <= 4; i++)
                        for (int j = -4; j <= 4; j++)
                        {
                            float th = Mathf.Clamp(ct + i * step, -1f, 1f), kn = Mathf.Clamp(ck + j * step, -1f, 1f);
                            float e = Error(th, kn);
                            if (e < bestE) { bestE = e; bestT = th; bestK = kn; }
                        }
                }
            }
            Set(Thigh, bestT); Set(Knee, bestK);
            Bisect(d => Set(Ankle, d), () => FootPitch(probe), standingPitch, -0.9f, 0.9f);
            probe.Pose(deltas, bodyPitch);
            achieved = new JObject
            {
                ["pitch"] = Math.Round(bodyPitch, 2), ["recline"] = Math.Round(probe.Recline, 1), ["soleDrop"] = Math.Round(probe.SoleDrop(), 3),
                ["soleForward"] = Math.Round(probe.SoleForward(), 3), ["target"] = $"drop {height:F2} forward {k.footForward:F2}",
                ["stance"] = Math.Round((probe.RFoot.position.x - probe.LFoot.position.x) * 0.5f, 3),
            };
            pitch = bodyPitch;
            return deltas;
        }

        /// <summary>Foot pitch (deg): heel→toe vector vs horizontal (0 = flat).</summary>
        private static float FootPitch(Probe p)
        {
            var v = ((p.LToes.position - p.LFoot.position) + (p.RToes.position - p.RFoot.position)) * 0.5f;
            return Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
        }

        private static float _clipPitchSign = 1f;
        private static float _standingFootPitch;
        private static readonly Dictionary<GameObject, float> _standing = new Dictionary<GameObject, float>();

        /// <summary>Foot pitch of the family's own standing idle (a flat sole), measured once per reference look.</summary>
        private static float StandingFootPitch(GameObject prefab)
        {
            if (_standing.TryGetValue(prefab, out var v)) return v;
            var idle = CharacterFeelController.Clip(Anim + "loco_idle.fbx");
            using (var p = new Probe(prefab, idle, 0.3f)) { p.Pose(null); v = FootPitch(p); }
            _standing[prefab] = v;
            return v;
        }

        private static AnimationClip WriteClip(AnimationClip source, string state, Dictionary<string, float> deltas, float pitch, bool save = true)
        {
            var path = OutDir + state + ".anim";
            var clip = Object.Instantiate(source);
            clip.name = state;
            var bindings = AnimationUtility.GetCurveBindings(clip).ToDictionary(b => b.propertyName, b => b);
            foreach (var kv in deltas)
            {
                if (bindings.TryGetValue(kv.Key, out var b))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    var keys = curve.keys;
                    for (int i = 0; i < keys.Length; i++) keys[i].value = Mathf.Clamp(keys[i].value + kv.Value, -1f, 1f);
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, b, curve);
                }
                else
                {
                    var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key);
                    AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, source.length, Mathf.Clamp(kv.Value, -1f, 1f)));
                }
            }
            // Body rotation: resample RootQ at 30 Hz and pre-multiply the recline.
            if (Mathf.Abs(pitch) > 0.01f)
            {
                var q = new[] { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" }.Select(n => bindings[n]).ToArray();
                var curves = q.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
                int n = Mathf.Max(2, Mathf.RoundToInt(source.length * 30f) + 1);
                var outKeys = new Keyframe[4][];
                for (int c = 0; c < 4; c++) outKeys[c] = new Keyframe[n];
                var tilt = Recliner(pitch * _clipPitchSign);
                for (int i = 0; i < n; i++)
                {
                    float time = source.length * i / (n - 1);
                    var rq = new Quaternion(curves[0].Evaluate(time), curves[1].Evaluate(time), curves[2].Evaluate(time), curves[3].Evaluate(time));
                    var r = Quaternion.Normalize(tilt * Quaternion.Normalize(rq));
                    outKeys[0][i] = new Keyframe(time, r.x); outKeys[1][i] = new Keyframe(time, r.y);
                    outKeys[2][i] = new Keyframe(time, r.z); outKeys[3][i] = new Keyframe(time, r.w);
                }
                for (int c = 0; c < 4; c++)
                {
                    var curve = new AnimationCurve(outKeys[c]);
                    for (int i = 0; i < n; i++) curve.SmoothTangents(i, 0f);
                    AnimationUtility.SetEditorCurve(clip, q[c], curve);
                }
            }
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (!save) return clip;
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) { EditorUtility.CopySerialized(clip, existing); Object.DestroyImmediate(clip); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ------------------------------------------------------------------ pose metrics (per blend child, per family)

        public static void MeasureBatch()
        {
            int code = 0;
            try
            {
                var report = MeasurePoses();
                var outPath = Arg("-ctlife-poseOut");
                if (!string.IsNullOrEmpty(outPath)) { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); File.WriteAllText(outPath, report.ToString()); }
                Debug.Log("CTLIFE [seat-pose] " + report["summary"]);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>
        /// Every seated state (blend children at their seat heights; single-clip states at height 0) per family (m: the
        /// shared controller's clip, f: RainController's), measured on the baked LOD0 skin of the family reference look at
        /// scale 1, into SeatFits.asset.
        /// </summary>
        [MenuItem("CT Life/Characters/Measure seated poses (SeatFits)")]
        public static JObject MeasurePoses()
        {
            var report = new JObject();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath);
            var rain = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.RainControllerPath);
            var catalog = AssetDatabase.LoadAssetAtPath<SeatFitCatalog>(SeatFitBuilder.CatalogPath) ?? throw new FileNotFoundException(SeatFitBuilder.CatalogPath);
            var poses = new List<SeatFitCatalog.PoseMetric>();
            foreach (var state in SeatedStates)
            {
                var st = ctrl.layers[0].stateMachine.states.Select(x => x.state).FirstOrDefault(x => x.name == state);
                if (st == null) { report[state] = "missing state"; continue; }
                var children = st.motion is BlendTree t ? t.children.Select(c => (c.threshold, c.motion as AnimationClip)).ToList()
                    : new List<(float, AnimationClip)> { (0f, st.motion as AnimationClip) };
                foreach (var (h, baseClip) in children)
                {
                    if (baseClip == null) continue;
                    foreach (var (family, prefabPath) in new[] { ("m", MalePrefab), ("f", FemalePrefab) })
                    {
                        var clip = family == "f" && rain != null && rain[baseClip] != null ? rain[baseClip] : baseClip;
                        using var probe = new Probe(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), clip, 0.3f);
                        probe.Pose(null);
                        var m = probe.Metric(state, family, h);
                        poses.Add(m);
                        report[$"{state}.{family}.{h:F2}"] = $"{clip.name}: hip {m.hipContact:F3} back {m.backContact:F3} soles {m.leftFoot.y:F3}/{m.rightFoot.y:F3} z {m.leftFoot.z:F2}";
                    }
                }
            }
            catalog.poses = poses;
            catalog.femaleControllers = rain != null ? new RuntimeAnimatorController[] { rain } : Array.Empty<RuntimeAnimatorController>();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            report["summary"] = $"{poses.Count} seated pose metrics";
            return report;
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
