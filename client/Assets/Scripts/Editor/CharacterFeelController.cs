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
    /// Character-feel additions to the shared AvatarController, applied IN PLACE and idempotently (never recreates the
    /// controller, so Codex's tuned states/overrides survive):
    /// - IK pass on the base layer (foot/look-at/hand IK in CharacterIK);
    /// - one speed-matched locomotion blend tree for loco_walk/loco_run (thresholds = measured clip ground speeds,
    ///   playback rate parameter "locoRate" set at runtime, so feet do not slide);
    /// - states for clips already imported but not wired: sit-down/stand-up, stagger/fall/get-up, jump/land, idle
    ///   variations, counter lean, interact;
    /// - Resources/Characters/LocomotionTuning.asset with the measured speeds for the runtime.
    /// Batch: Unity -batchmode -nographics -projectPath client -executeMethod CTLife.EditorTools.CharacterFeelController.ApplyBatch
    ///        [-ctlife-measureOut /abs/measure.json]
    /// </summary>
    public static class CharacterFeelController
    {
        public const string ControllerPath = "Assets/Resources/Characters/AvatarController.controller";
        public const string TuningPath = "Assets/Resources/Characters/LocomotionTuning.asset";
        private const string Anim = "Assets/Art/animations/";
        private const string ReferencePrefab = "Assets/Art/characters/cast/prefabs/player_m_kol.prefab";

        /// <summary>State id → clip file (all CC0, already imported; see art/processed/animations/clips.json).</summary>
        public static readonly (string state, string file)[] ExtraStates =
        {
            ("act_sit_down", "act_sit_down.fbx"), ("act_stand_up", "act_stand_up.fbx"), ("act_sit_talk", "act_sit_talk.fbx"),
            ("act_get_up", "act_get_up.fbx"), ("react_stagger", "react_stagger.fbx"), ("react_fall", "react_rug_pull_fall.fbx"),
            ("react_hit_chest", "react_hit_chest.fbx"), ("react_roll", "react_roll.fbx"), ("react_kneel_despair", "react_kneel_despair.fbx"),
            ("jump_start", "jump_start.fbx"), ("jump_air", "jump_air.fbx"), ("jump_land", "jump_land.fbx"),
            ("idle_fidget_lookaround", "idle_fidget_lookaround.fbx"), ("idle_fidget_stretch", "idle_fidget_stretch.fbx"),
            ("idle_fidget_foldarms", "idle_fidget_foldarms.fbx"), ("idle_fidget_talk", "idle_fidget_talk.fbx"),
            ("idle_lean_rail", "idle_lean_rail.fbx"), ("act_interact", "act_interact.fbx"), ("act_phone_hold", "act_phone_hold.fbx"),
            ("talk_host_with_mic", "talk_host_with_mic.fbx"), ("loco_jog", "loco_jog.fbx"), ("idle_rekt", "idle_rekt.fbx"),
            // (Seated variants act_sit / act_sit_lounge / _beanbag / _stool are seatHeight blend trees owned by SeatPoseBuilder.)
            // Codex's fitted café states (Snow clips here; RainController overrides them with Rain's own fits).
            (CafeSeatMotionProfile.SocialState, SnowSocial), (CafeSeatMotionProfile.TypingState, SnowTyping),
            (CafeSeatMotionProfile.WorkExitState, StandPrefix), (CafeSeatMotionProfile.WorkExitIdleState, SnowIdle),
            // CMU mocap one-shots (MocapImport: free use, NSF acknowledgement in art/LICENSES.md).
            ("act_get_up_front", "mocap/getup_face_down.fbx"), ("react_trip", "mocap/trip.fbx"), ("loco_run_stop", "mocap/jog_stop.fbx"),
        };

        private const string Fits = "Assets/Art/characters/authored/MotionReview20261006/";
        public const string SnowSocial = Fits + "SeatedBodyFitV1/Snow_SocialBodyFit_Candidate.anim";
        public const string RainSocial = Fits + "SeatedBodyFitV1/Rain_SocialBodyFit_Candidate.anim";
        public const string SnowTyping = Fits + "TypingOfflineBakeV1/Snow_TypingCombined_FK_Candidate.anim";
        public const string RainTyping = Fits + "TypingOfflineBakeV1/Rain_TypingCombined_FK_Candidate.anim";
        public const string StandPrefix = "Assets/Art/Candidates/PlayableVenues20261007/Motion/StandPrefix.anim";
        public const string SnowIdle = "Assets/Art/characters/authored/SnowIdle/SnowAdult_RelaxedIdle.fbx";
        /// <summary>Seated states whose feet CharacterIK plants (Humanoid Foot IK gives it goals that follow the feet).</summary>
        /// (The seated blend trees and Codex's fitted states keep Foot IK off: their FK feet are the fitted contact.)
        public static readonly string[] SeatedFootIk = { "act_type", "act_frantic_type", "act_sit_talk" };

        public static void ApplyBatch()
        {
            int code = 0;
            try
            {
                var report = Apply();
                var outPath = Arg("-ctlife-measureOut");
                if (!string.IsNullOrEmpty(outPath)) { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); File.WriteAllText(outPath, report.ToString()); }
                Debug.Log("CTLIFE [feel-controller] " + report["summary"]);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>
        /// Clip for the run band (3.6–4.5 m/s). The UAL Sprint's stance speed (~5.5–5.8 m/s) would have to play at
        /// 0.62–0.77× there ("slow bounding", ct_motion.md); the UAL Jog's (~4.5–4.9 m/s) plays at 0.8–1.0×. Its known
        /// sole depth is handled by CharacterIK's ground clamp. Switch back to "loco_run.fbx" to restore the sprint.
        /// </summary>
        public const string RunBandClip = "loco_jog.fbx";

        /// <summary>Codex's accepted shared walk reference (docs/progress/locomotion-quality-20261006.md): speed / 1.0 m/s.</summary>
        public const float WalkReference = 1.0f;
        private const string FemaleReferencePrefab = "Assets/Art/characters/cast/prefabs/player_f_builder.prefab"; // trousered: soles, not a hem

        [MenuItem("CT Life/Characters/Apply character-feel controller")]
        public static JObject Apply()
        {
            var report = new JObject();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) ?? throw new FileNotFoundException(ControllerPath);
            var male = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefab) ?? throw new FileNotFoundException(ReferencePrefab);
            var female = AssetDatabase.LoadAssetAtPath<GameObject>(FemaleReferencePrefab) ?? throw new FileNotFoundException(FemaleReferencePrefab);
            var snowCtrl = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.SnowControllerPath);
            var rainCtrl = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.RainControllerPath);

            // 1) The clips the game plays for walk/run (the shared ones; family overrides resolved per controller).
            var walk = Clip(Anim + "loco_walk.fbx");
            var run = Clip(Anim + "loco_run.fbx");
            AnimationClip Played(AnimatorOverrideController o, AnimationClip shared) => o != null && o[shared] != null ? o[shared] : shared;
            var measures = new JObject();
            float Measure(GameObject prefab, AnimationClip c, string label)
            {
                var v = ClipMeasure.GroundSpeed(prefab, c);
                measures[label] = Math.Round(v, 3);
                return v;
            }
            float mWalk = Measure(male, Played(snowCtrl, walk), "m.walk:" + Played(snowCtrl, walk).name);
            Measure(male, Played(snowCtrl, run), "m.sprint:" + Played(snowCtrl, run).name);
            var band = Clip(Anim + RunBandClip) ?? run;
            float mRun = Measure(male, band, "m.runBand:" + RunBandClip);
            float fWalk = Measure(female, Played(rainCtrl, walk), "f.walk:" + Played(rainCtrl, walk).name);
            Measure(female, Played(rainCtrl, run), "f.sprint:" + Played(rainCtrl, run).name);
            float fRun = Measure(female, band, "f.runBand:" + RunBandClip);
            foreach (var (state, file) in new[] { ("act_sit_down", "act_sit_down.fbx"), ("act_stand_up", "act_stand_up.fbx"), ("act_sit", "act_sit.fbx"),
                         ("act_get_up", "act_get_up.fbx"), ("react_fall", "react_rug_pull_fall.fbx"), ("react_stagger", "react_stagger.fbx"), ("jump_land", "jump_land.fbx") })
            {
                var c = Clip(Anim + file);
                if (c != null) measures[state + ".hips"] = ClipMeasure.HipsTrack(male, c);
            }
            report["measures"] = measures;
            if (Arg("-ctlife-dump") == "1")
            {
                report["dump_walk"] = ClipMeasure.Track(male, walk);
                report["dump_run"] = ClipMeasure.Track(male, run, 40);
            }
            if (Arg("-ctlife-measureOnly") == "1")
            {
                report["summary"] = $"measured only: m walk {mWalk:F2} run {mRun:F2}; f walk {fWalk:F2} run {fRun:F2}";
                return report;
            }
            if (mRun < 2f || fRun < 2f || mRun > 7f || fRun > 7f) throw new InvalidDataException($"implausible run-band ground speed m={mRun:F2} f={fRun:F2}");

            // 2) Parameters and IK pass.
            if (!ctrl.parameters.Any(p => p.name == "locoRate"))
                ctrl.AddParameter(new AnimatorControllerParameter { name = "locoRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var layers = ctrl.layers;
            layers[0].iKPass = true;
            ctrl.layers = layers;
            var sm = ctrl.layers[0].stateMachine;

            // 3) One walk↔run tree by "speed" (thresholds = the control's walk max / run min), playback "locoRate".
            var tree = sm.states.Select(st => st.state.motion).OfType<BlendTree>().FirstOrDefault(t => t.name == "Locomotion");
            if (tree == null)
            {
                tree = new BlendTree { name = "Locomotion", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, ctrl);
            }
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "speed";
            tree.useAutomaticThresholds = false;
            tree.children = Array.Empty<ChildMotion>();
            tree.AddChild(walk, LocomotionTuning.BlendWalk);
            tree.AddChild(band, LocomotionTuning.BlendRun);
            foreach (var name in new[] { "loco_walk", "loco_run" })
            {
                var st = State(sm, name) ?? sm.AddState(name);
                st.motion = tree;
                st.speed = 1f;
                st.speedParameterActive = true;
                st.speedParameter = "locoRate";
                st.writeDefaultValues = true;
                // Humanoid Foot IK on both: they play one tree (walk ↔ jog), and only with Foot IK does the IK pass get goals
                // that follow the animated feet (verified: probe_ik_goals); CharacterIK's ground placement needs them.
                st.iKOnFeet = true;
            }
            foreach (var standing in new[] { "loco_idle", "idle_fidget_lookaround", "idle_fidget_stretch", "idle_fidget_foldarms", "idle_fidget_talk", "idle_lean_rail" })
            {
                var st = State(sm, standing);
                if (st != null) st.iKOnFeet = true;
            }

            // 4) Wire imported-but-unused clips as states (names = anim ids; AnimationLibrary convention).
            int added = 0;
            var missing = new JArray();
            foreach (var (state, file) in ExtraStates)
            {
                var clip = Clip(Anim + file);
                if (clip == null) { missing.Add(file); continue; }
                var st = State(sm, state);
                if (st == null) { st = sm.AddState(state); added++; }
                st.motion = clip;
                st.writeDefaultValues = true;
            }
            foreach (var seated in SeatedFootIk)
            {
                var st = State(sm, seated);
                if (st != null) st.iKOnFeet = true;
            }
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(ctrl);
            // Rain family: its own fitted café clips over Snow's.
            var rainOverride = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.RainControllerPath);
            if (rainOverride != null)
            {
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(rainOverride.overridesCount);
                rainOverride.GetOverrides(pairs);
                var wanted = new Dictionary<AnimationClip, AnimationClip>
                {
                    [Clip(SnowSocial)] = Clip(RainSocial),
                    [Clip(SnowTyping)] = Clip(RainTyping),
                };
                for (int i = 0; i < pairs.Count; i++)
                    if (pairs[i].Key != null && wanted.TryGetValue(pairs[i].Key, out var rain) && rain != null)
                        pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, rain);
                rainOverride.ApplyOverrides(pairs);
                EditorUtility.SetDirty(rainOverride);
            }

            // 5) Runtime tuning: natives per family controller (shared walk = Codex's reference).
            var tuning = AssetDatabase.LoadAssetAtPath<LocomotionTuning>(TuningPath);
            if (tuning == null) { tuning = ScriptableObject.CreateInstance<LocomotionTuning>(); AssetDatabase.CreateAsset(tuning, TuningPath); }
            // Planted soles of the walk and the run band on each family's reference look (the generalized run correction).
            float Lift(GameObject prefab, AnimationClip c, string label) { var v = ClipMeasure.PlantedSoleLift(prefab, c); measures[label] = Math.Round(v, 4); return v; }
            float mWalkLift = Lift(male, Played(snowCtrl, walk), "m.walk.soleLift"), mRunLift = Lift(male, band, "m.runBand.soleLift");
            float fWalkLift = Lift(female, Played(rainCtrl, walk), "f.walk.soleLift"), fRunLift = Lift(female, band, "f.runBand.soleLift");
            tuning.families = new List<LocomotionTuning.Family>
            {
                // Walk: Codex's 1.0 m/s reference (fitted with the walk's Foot IK on). In motion it slides less than the
                // static fit (0.895 m/s m-family): stance slide 0.23 vs 0.29 m/s at 2 m/s (gait_idle_turn capture).
                new LocomotionTuning.Family { controller = snowCtrl, walk = WalkReference, run = mRun, walkSoleLift = Mathf.Max(0f, mWalkLift), runSoleLift = Mathf.Max(0f, mRunLift) },
                new LocomotionTuning.Family { controller = rainCtrl, walk = WalkReference, run = fRun, walkSoleLift = Mathf.Max(0f, fWalkLift), runSoleLift = Mathf.Max(0f, fRunLift) },
            };
            tuning.lengths = new List<LocomotionTuning.StateLength>();
            foreach (var (state, file) in ExtraStates)
            {
                var c = Clip(Anim + file);
                if (c != null) tuning.lengths.Add(new LocomotionTuning.StateLength { state = state, seconds = c.length });
            }
            tuning.poses = new List<LocomotionTuning.StatePose>();
            foreach (var (state, file) in new[] { ("act_get_up", "act_get_up.fbx"), ("act_get_up_front", "mocap/getup_face_down.fbx"),
                         ("react_fall", "react_rug_pull_fall.fbx"), ("react_stagger", "react_stagger.fbx"), ("react_trip", "mocap/trip.fbx"),
                         ("loco_run_stop", "mocap/jog_stop.fbx") })
            {
                var c = Clip(Anim + file);
                if (c == null) continue;
                var (h0, h1, hd) = ClipMeasure.Pose(male, c);
                tuning.poses.Add(new LocomotionTuning.StatePose { state = state, hipsStart = h0, hipsEnd = h1, headStart = hd });
                measures[state + ".pose"] = $"hips0 {h0:F2} hips1 {h1:F2} head0 {hd:F2}";
            }
            tuning.defaultWalk = WalkReference;
            tuning.defaultRun = (mRun + fRun) * 0.5f;
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssets();

            report["natives"] = new JObject { ["m.run"] = Math.Round(mRun, 3), ["f.run"] = Math.Round(fRun, 3), ["walk_reference"] = WalkReference,
                ["m.walk_measured"] = Math.Round(mWalk, 3), ["f.walk_measured"] = Math.Round(fWalk, 3) };
            report["states_added"] = added;
            report["missing_clips"] = missing;
            report["summary"] = $"walk ref {WalkReference:F2} (measured m {mWalk:F2} / f {fWalk:F2}); run band {RunBandClip} m {mRun:F2} / f {fRun:F2} m/s; +{added} states; IK pass on; {missing.Count} clips missing";
            return report;
        }

        private static AnimatorState State(AnimatorStateMachine sm, string name) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

        public static AnimationClip Clip(string path)
        {
            // Entries may be absolute ("Assets/...") or relative to Assets/Art/animations/ (Anim + file; an absolute entry arrives as Anim + "Assets/...").
            int abs = path.IndexOf("Assets/", 1, StringComparison.Ordinal);
            if (abs > 0) path = path.Substring(abs);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }

    /// <summary>Samples Humanoid clips on a real character (de-optimized instance, PlayableGraph, edit mode).</summary>
    public static class ClipMeasure
    {
        /// <summary>A de-optimized instance of a look at the origin (scale 1) playing one clip; sampled in edit mode.</summary>
        internal sealed class Rig : IDisposable
        {
            public GameObject go; public Animator animator; private AnimatorController _ctrl; private readonly float _length;
            public Transform hips, lFoot, rFoot;
            public Rig(GameObject prefab, AnimationClip c, bool footIk = false)
            {
                _length = Mathf.Max(0.0001f, c.length);
                go = Object.Instantiate(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                animator = go.GetComponent<Animator>();
                AnimatorUtility.DeoptimizeTransformHierarchy(go);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _ctrl = new AnimatorController { name = "measure", hideFlags = HideFlags.HideAndDontSave };
                _ctrl.AddLayer("Base");
                var st = _ctrl.layers[0].stateMachine.AddState("m");
                st.motion = c;
                st.iKOnFeet = footIk;
                animator.runtimeAnimatorController = _ctrl;
                animator.Rebind();
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            public void At(float t)
            {
                animator.Play("m", 0, Mathf.Clamp01(t / _length) * 0.9999f);
                animator.Update(0f);
            }
            public void Dispose() { if (go != null) Object.DestroyImmediate(go); if (_ctrl != null) Object.DestroyImmediate(_ctrl); }
        }

        /// <summary>
        /// Ground speed of an in-place loop (m/s): for each foot, the stance window is where its toe is within 2 cm of
        /// that toe's lowest height; a least-squares fit of ankle and toe Z against clip time over the central 60% of the
        /// window (no heel-strike / toe-off) gives the backward speed. Mean of both feet and both landmarks (the same
        /// method as Codex's locomotion-quality fit, docs/progress/locomotion-quality-20261006.md).
        /// </summary>
        public static float GroundSpeed(GameObject prefab, AnimationClip c)
        {
            if (c == null) return 0f;
            using var rig = new Rig(prefab, c);
            var lt = rig.animator.GetBoneTransform(HumanBodyBones.LeftToes);
            var rt = rig.animator.GetBoneTransform(HumanBodyBones.RightToes);
            if (rig.lFoot == null || rig.rFoot == null || lt == null || rt == null) return 0f;
            int n = Mathf.Max(60, Mathf.RoundToInt(c.length * 240f));
            var t = new float[n + 1];
            var la = new Vector3[n + 1]; var ltp = new Vector3[n + 1]; var ra = new Vector3[n + 1]; var rtp = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) { t[i] = c.length * i / n; rig.At(t[i]); la[i] = rig.lFoot.position; ltp[i] = lt.position; ra[i] = rig.rFoot.position; rtp[i] = rt.position; }
            var speeds = new List<float>();
            void Foot(Vector3[] ankle, Vector3[] toe)
            {
                float min = toe.Min(p => p.y);
                // Longest contiguous stance run (the loop may wrap: unroll twice).
                int bestStart = -1, bestLen = 0;
                for (int s0 = 0; s0 <= n; s0++)
                {
                    if (toe[s0].y - min > 0.02f || (s0 > 0 && toe[s0 - 1].y - min <= 0.02f)) continue;
                    int len = 0;
                    while (len <= n && toe[(s0 + len) % (n + 1)].y - min <= 0.02f) len++;
                    if (len > bestLen) { bestLen = len; bestStart = s0; }
                }
                if (bestLen < 4) return;
                int from = bestStart + (int)(bestLen * 0.2f), to = bestStart + (int)(bestLen * 0.8f);
                foreach (var track in new[] { ankle, toe })
                {
                    double st = 0, sz = 0, stt = 0, stz = 0; int m = 0; float offset = 0f; float prevT = -1f;
                    for (int k = from; k <= to; k++)
                    {
                        int i = k % (n + 1);
                        float ti = t[i] + offset;
                        if (prevT >= 0 && ti < prevT) { offset += c.length; ti = t[i] + offset; }
                        prevT = ti;
                        st += ti; sz += track[i].z; stt += ti * ti; stz += ti * track[i].z; m++;
                    }
                    double denom = m * stt - st * st;
                    if (m >= 3 && Math.Abs(denom) > 1e-9) speeds.Add((float)(-(m * stz - st * sz) / denom));
                }
            }
            Foot(la, ltp); Foot(ra, rtp);
            return speeds.Count > 0 ? speeds.Average() : 0f;
        }

        /// <summary>Per-frame foot/toe positions (diagnostic dump for the ground-speed estimator).</summary>
        public static JArray Track(GameObject prefab, AnimationClip c, int frames = 60)
        {
            using var rig = new Rig(prefab, c);
            var lt = rig.animator.GetBoneTransform(HumanBodyBones.LeftToes);
            var rt = rig.animator.GetBoneTransform(HumanBodyBones.RightToes);
            var arr = new JArray();
            for (int i = 0; i <= frames; i++)
            {
                float t = c.length * i / frames;
                rig.At(t);
                string P(Transform x) => x == null ? "-" : $"{x.position.y:F3},{x.position.z:F3}";
                arr.Add($"{t:F3} L {P(rig.lFoot)} Lt {P(lt)} R {P(rig.rFoot)} Rt {P(rt)}");
            }
            return arr;
        }

        /// <summary>Root-space hips at the first/last frame and head at the first frame (aligning get-ups with bodies).</summary>
        /// <summary>
        /// Planted-sole lift of a locomotion loop on this look (m): over 60 phases with Foot IK (as it plays), the lowest
        /// skinned vertex above the root plane; its 10th percentile is the stance contact (0 = the soles touch the ground).
        /// </summary>
        public static float PlantedSoleLift(GameObject prefab, AnimationClip c)
        {
            using var rig = new Rig(prefab, c, footIk: true);
            var smr = rig.go.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).First();
            var mesh = new Mesh();
            var lows = new List<float>();
            try
            {
                for (int i = 0; i < 60; i++)
                {
                    rig.At(c.length * i / 60f);
                    smr.BakeMesh(mesh, true);
                    var m = smr.transform.localToWorldMatrix;
                    float low = float.MaxValue;
                    foreach (var v in mesh.vertices) low = Mathf.Min(low, m.MultiplyPoint3x4(v).y);
                    lows.Add(low - rig.go.transform.position.y);
                }
            }
            finally { Object.DestroyImmediate(mesh); }
            lows.Sort();
            return lows[lows.Count / 10];
        }

        public static (Vector3 hipsStart, Vector3 hipsEnd, Vector3 headStart) Pose(GameObject prefab, AnimationClip c)
        {
            using var rig = new Rig(prefab, c);
            var head = rig.animator.GetBoneTransform(HumanBodyBones.Head);
            rig.At(0f);
            var h0 = rig.hips.position; var hd = head != null ? head.position : h0 + Vector3.up;
            rig.At(c.length);
            return (h0, rig.hips.position, hd);
        }

        /// <summary>Hips position (root space) at start/middle/end, plus the clip length.</summary>
        public static JObject HipsTrack(GameObject prefab, AnimationClip c)
        {
            using var rig = new Rig(prefab, c);
            var o = new JObject { ["length"] = Math.Round(c.length, 3) };
            foreach (var (k, f) in new[] { ("start", 0f), ("mid", 0.5f), ("end", 1f) })
            {
                rig.At(c.length * f);
                var p = rig.hips != null ? rig.hips.position : Vector3.zero;
                o[k] = $"{p.x:F2},{p.y:F2},{p.z:F2}";
            }
            return o;
        }
    }
}
