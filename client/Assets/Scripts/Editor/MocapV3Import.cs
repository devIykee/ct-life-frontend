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
    /// Motion-capture locomotion v3 (2026-10-22): 100STYLE (CC BY 4.0) and CMU clips retargeted per body family by
    /// art/pipeline (art/processed/animations/mocap_v3.json), in Assets/Art/animations/mocap_v3/{snow,rain,hazmat}/.
    /// - Import: Humanoid, each clip its own Avatar ("Create From This Model": the files' node defaults are the rest pose);
    ///   snow and rain map the shared 55-bone names (<see cref="CharacterValidation.SnowBoneMap"/>), hazmat the CTS
    ///   mannequin names of the distinct looks (read from hero_hazmat's own Humanoid map). Loops bake root rotation (based upon body orientation), Y and
    ///   XZ into the pose; starts and stops leave XZ to root motion (our actors move themselves, so they play in place
    ///   while the body accelerates or stops); turns leave the rotation to root motion (body orientation based).
    /// - Shared AvatarController: start states (exit at normalised time 1 into the Locomotion tree, which lands on walk
    ///   phase 0), walk stop, turns, three idle breaks, all with Foot IK; the snow clips are their shared motions.
    /// - Family overrides: Snow and Rain point loco_idle/walk/jog/run and the run stop at their own mocap clips (the old
    ///   SnowWalkContact and Rain RelaxedIdle overrides go); a new CTS override controller does the same for the distinct
    ///   cast (hazmat, assassin, gynoid, balaclava, UBC, mascots), which were on the shared cartoon set.
    /// - LocomotionTuning: the clips' measured native ground speeds per family (no sprint clamp, a real jog band).
    /// Menu: CT Life > Characters > Import mocap v3 locomotion. Batch: -executeMethod CTLife.EditorTools.MocapV3Import.RunBatch
    /// </summary>
    public static class MocapV3Import
    {
        public const string Root = "Assets/Art/animations/mocap_v3";
        public const string AvatarTag = "ct-life:mocap-v3-avatar-v1";
        public const string CtsControllerPath = "Assets/Resources/Characters/CtsController.overrideController";
        private const string Anim = "Assets/Art/animations/";
        private const string HazmatModel = "Assets/Art/characters/distinct/hero_hazmat/hero_hazmat_Hero.fbx";
        internal static readonly HashSet<string> RestProbe = new HashSet<string>(StringComparer.Ordinal);
        public static readonly string[] Families = { "snow", "rain", "hazmat" };

        public enum Kind { Loop, StartStop, Turn }

        public static Kind KindOf(string file)
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (id.StartsWith("turn_", StringComparison.Ordinal)) return Kind.Turn;
            if (id.EndsWith("_start", StringComparison.Ordinal) || id.EndsWith("_stop", StringComparison.Ordinal)) return Kind.StartStop;
            return Kind.Loop;
        }

        /// <summary>Shared Animator states the v3 set adds (state id → clip id) and where each one exits to.</summary>
        public static readonly (string state, string clip, string exitTo, float speed)[] NewStates =
        {
            ("loco_walk_start", "loco_walk_start", "loco_walk", 1.15f),
            ("loco_run_start", "loco_run_start", "loco_walk", 1.0f),
            ("loco_walk_stop", "loco_walk_stop", "loco_idle", 1.3f),
            ("turn_90_l", "turn_90_l", "loco_idle", 1.25f),
            ("turn_90_r", "turn_90_r", "loco_idle", 1.25f),
            ("turn_180_walk", "turn_180_walk", "loco_walk", 1.1f),
            ("idle_hands_hips", "idle_hands_hips", null, 1f),
            ("idle_heavy", "idle_heavy", null, 1f),
            ("idle_phone_call", "idle_phone_call", null, 1f),
        };

        /// <summary>Shared clip file (Assets/Art/animations/; "state:x" = whatever clip state x plays, the idle being Snow's
        /// authored RelaxedIdle) → the v3 clip that overrides it in every family.</summary>
        public static readonly (string shared, string v3)[] Replaces =
        {
            ("state:loco_idle", "loco_idle"), ("loco_walk.fbx", "loco_walk"), ("loco_jog.fbx", "loco_jog"), ("loco_run.fbx", "loco_sprint"),
            ("mocap/jog_stop.fbx", "loco_jog_stop"),
        };

        public static string PathOf(string family, string clip) => $"{Root}/{family}/{clip}.fbx";

        [MenuItem("CT Life/Characters/Import mocap v3 locomotion")]
        public static JObject Run()
        {
            var report = new JObject();
            var data = JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "..", "..", "art", "processed", "animations", "mocap_v3.json")));
            var clips = ((JArray)data["clips"]).Cast<JObject>().ToList();
            // 1) Avatars and clip settings
            var cts = CtsMap();
            int configured = 0;
            foreach (var family in Families)
                foreach (var c in clips)
                {
                    string path = PathOf(family, (string)c["id"]);
                    var mi = AssetImporter.GetAtPath(path) as ModelImporter ?? throw new FileNotFoundException("mocap v3 clip not imported", path);
                    if (mi.userData != AvatarTag || !AvatarValid(path))
                    {
                        Configure(path, mi, family == "hazmat" ? cts : CharacterValidation.SnowBoneMap());
                        configured++;
                    }
                    if (Clip(path) == null) throw new InvalidDataException("no clip in " + path);
                }
            report["avatarsConfigured"] = configured;
            // 2) the shared controller's new states
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath) ?? throw new FileNotFoundException(CharacterFeelController.ControllerPath);
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState State(string n) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n);
            int added = 0;
            foreach (var (state, clip, exitTo, speed) in NewStates)
            {
                var st = State(state);
                if (st == null) { st = sm.AddState(state); added++; }
                st.motion = Clip(PathOf("snow", clip));
                st.writeDefaultValues = true;
                st.iKOnFeet = true;
                st.speed = speed;
                foreach (var t in st.transitions.ToArray()) st.RemoveTransition(t);
                var target = exitTo != null ? State(exitTo) : null;
                if (target != null)
                {
                    var t = st.AddTransition(target);
                    t.hasExitTime = true; t.exitTime = 1f; t.hasFixedDuration = true; t.duration = exitTo == "loco_idle" ? 0.2f : 0.05f;
                    t.offset = 0f; t.interruptionSource = TransitionInterruptionSource.None;
                }
            }
            foreach (var standing in new[] { "loco_idle", "loco_walk", "loco_run", "loco_run_stop" })
            {
                var st = State(standing);
                if (st != null) st.iKOnFeet = true;
            }
            var stop = State("loco_run_stop");
            if (stop != null) stop.speed = 1.15f;
            EditorUtility.SetDirty(ctrl);
            report["statesAdded"] = added;
            // 3) family overrides: Snow, Rain, and the new CTS (distinct cast) controller
            var snow = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.SnowControllerPath);
            var rain = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.RainControllerPath);
            var ctsCtrl = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CtsControllerPath);
            if (ctsCtrl == null)
            {
                ctsCtrl = new AnimatorOverrideController(ctrl) { name = "CtsController" };
                AssetDatabase.CreateAsset(ctsCtrl, CtsControllerPath);
            }
            ctsCtrl.runtimeAnimatorController = ctrl;
            report["snowOverrides"] = Override(snow, "snow", ctrl);
            report["rainOverrides"] = Override(rain, "rain", ctrl);
            report["ctsOverrides"] = Override(ctsCtrl, "hazmat", ctrl);
            // 4) the distinct cast onto the CTS controller (looks that had no family controller)
            report["ctsLooks"] = AssignCts(ctsCtrl);
            // 5) native ground speeds per family (the clips' own, measured by the retarget) and the planted-sole lifts
            var tuning = AssetDatabase.LoadAssetAtPath<LocomotionTuning>(CharacterFeelController.TuningPath) ?? throw new FileNotFoundException(CharacterFeelController.TuningPath);
            float Native(string id, string family) => (float)clips.First(c => (string)c["id"] == id)["per_target"][family]["native_speed_mps"];
            LocomotionTuning.Family Fam(RuntimeAnimatorController rc)
            {
                var f = tuning.families.FirstOrDefault(x => x != null && x.controller == rc);
                if (f == null) { f = new LocomotionTuning.Family { controller = rc }; tuning.families.Add(f); }
                return f;
            }
            var lifts = new JObject();
            foreach (var (rc, family, prefabPath) in new (RuntimeAnimatorController, string, string)[]
                     {
                         (snow, "snow", "Assets/Art/characters/cast/prefabs/player_m_kol.prefab"),
                         (rain, "rain", "Assets/Art/characters/cast/prefabs/player_f_builder.prefab"),
                         (ctsCtrl, "hazmat", "Assets/Art/characters/distinct/prefabs/hero_hazmat_Hero.prefab"),
                     })
            {
                if (rc == null) continue;
                var f = Fam(rc);
                f.walk = Native("loco_walk", family); f.run = Native("loco_jog", family); f.sprint = Native("loco_sprint", family);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    f.walkSoleLift = Mathf.Max(0f, ClipMeasure.PlantedSoleLift(prefab, Clip(PathOf(family, "loco_walk"))));
                    f.runSoleLift = Mathf.Max(0f, ClipMeasure.PlantedSoleLift(prefab, Clip(PathOf(family, "loco_jog"))));
                }
                lifts[family] = $"walk {f.walk:F3} jog {f.run:F3} sprint {f.sprint:F3} m/s; sole lift walk {f.walkSoleLift:F3} jog {f.runSoleLift:F3} m";
            }
            tuning.defaultWalk = Native("loco_walk", "snow"); tuning.defaultRun = Native("loco_jog", "snow"); tuning.defaultSprint = Native("loco_sprint", "snow");
            foreach (var (state, clip, _, speed) in NewStates)
            {
                var c = Clip(PathOf("snow", clip));
                tuning.lengths.RemoveAll(l => l != null && l.state == state);
                tuning.lengths.Add(new LocomotionTuning.StateLength { state = state, seconds = c.length / Mathf.Max(0.1f, speed) });
            }
            var runStop = Clip(PathOf("snow", "loco_jog_stop"));
            tuning.lengths.RemoveAll(l => l != null && l.state == "loco_run_stop");
            tuning.lengths.Add(new LocomotionTuning.StateLength { state = "loco_run_stop", seconds = runStop.length / 1.15f });
            EditorUtility.SetDirty(tuning);
            report["natives"] = lifts;
            AssetDatabase.SaveAssets();
            report["summary"] = $"{configured} avatars configured, +{added} states, overrides snow {report["snowOverrides"]} rain {report["rainOverrides"]} cts {report["ctsOverrides"]}, {report["ctsLooks"]} distinct looks on CTS";
            return report;
        }

        public static void RunBatch()
        {
            int code = 0;
            try { Debug.Log("CTLIFE [mocap-v3] " + Run().ToString(Newtonsoft.Json.Formatting.None)); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>Points every shared locomotion clip at the family's v3 clip and every new state at its own version.</summary>
        private static int Override(AnimatorOverrideController o, string family, AnimatorController ctrl)
        {
            if (o == null) return 0;
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(o.overridesCount);
            o.GetOverrides(pairs);
            var want = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var (shared, v3) in Replaces)
            {
                var original = shared.StartsWith("state:", StringComparison.Ordinal)
                    ? ctrl.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == shared.Substring(6))?.motion as AnimationClip
                    : Clip(Anim + shared);
                if (original != null) want[original] = Clip(PathOf(family, v3));
            }
            foreach (var (_, clip, _, _) in NewStates)
                want[Clip(PathOf("snow", clip))] = family == "snow" ? null : Clip(PathOf(family, clip));
            int n = 0;
            for (int i = 0; i < pairs.Count; i++)
                if (pairs[i].Key != null && want.TryGetValue(pairs[i].Key, out var to))
                {
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, to);
                    n++;
                }
            o.ApplyOverrides(pairs);
            EditorUtility.SetDirty(o);
            return n;
        }

        /// <summary>Registry looks without a family controller (the distinct CTS cast) get the CTS override controller.</summary>
        private static int AssignCts(AnimatorOverrideController cts)
        {
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            if (registry == null) return 0;
            int n = 0;
            foreach (var e in registry.looks)
            {
                if (e == null || e.prefab == null) continue;
                bool distinct = AssetDatabase.GetAssetPath(e.prefab).StartsWith(DistinctCharacterImport.Root + "/", StringComparison.Ordinal);
                if (!distinct || (e.controllerOverride != null && e.controllerOverride != cts)) continue;
                e.controllerOverride = cts;
                n++;
            }
            EditorUtility.SetDirty(registry);
            return n;
        }

        /// <summary>The CTS mannequin Humanoid map, as hero_hazmat's own import has it.</summary>
        private static Dictionary<HumanBodyBones, string> CtsMap()
        {
            var mi = AssetImporter.GetAtPath(HazmatModel) as ModelImporter ?? throw new FileNotFoundException(HazmatModel);
            var map = new Dictionary<HumanBodyBones, string>();
            foreach (var hb in mi.humanDescription.human)
                if (Enum.TryParse<HumanBodyBones>(hb.humanName.Replace(" ", ""), out var b)) map[b] = hb.boneName;
            if (!map.ContainsKey(HumanBodyBones.Hips)) throw new InvalidDataException("hero_hazmat has no Humanoid map");
            return map;
        }

        public static AnimationClip Clip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));

        private static bool AvatarValid(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = model != null ? model.GetComponent<Animator>() : null;
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        /// <summary>The clip's own Avatar from the map and this file's rest transforms after Enforce T-Pose (as MocapImport).</summary>
        private static void Configure(string path, ModelImporter mi, Dictionary<HumanBodyBones, string> map)
        {
            RestProbe.Add(path);
            try { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport); }
            finally { RestProbe.Remove(path); }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidDataException("clip did not import: " + path);
            var human = new HumanDescription();
            var reference = Object.Instantiate(model);
            try
            {
                reference.name = model.name;
                var transforms = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var t in reference.GetComponentsInChildren<Transform>(true)) if (!transforms.ContainsKey(t.name)) transforms[t.name] = t;
                var used = map.Where(kv => transforms.ContainsKey(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
                foreach (var need in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                    if (!used.ContainsKey(need)) throw new InvalidDataException($"{path}: {need} bone missing");
                var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
                var wrapper = setup.GetNestedType("BoneWrapper", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var enforce = setup.GetMethod("MakePoseValid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (wrapper == null || enforce == null) throw new MissingMethodException("Unity Avatar T-pose tooling changed.");
                var bones = Array.CreateInstance(wrapper, HumanTrait.BoneCount);
                for (int i = 0; i < HumanTrait.BoneCount; i++)
                {
                    Transform bone = used.TryGetValue((HumanBodyBones)i, out var boneName) ? transforms[boneName] : null;
                    bones.SetValue(Activator.CreateInstance(wrapper, new object[] { HumanTrait.BoneName[i], bone }), i);
                }
                enforce.Invoke(null, new object[] { bones });
                human.skeleton = reference.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
                }).ToArray();
                human.human = used.Select(pair => new HumanBone
                {
                    boneName = pair.Value, humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true },
                }).ToArray();
            }
            finally { Object.DestroyImmediate(reference); }
            human.upperArmTwist = 0.5f; human.lowerArmTwist = 0.5f; human.upperLegTwist = 0.5f; human.lowerLegTwist = 0.5f;
            human.armStretch = 0.05f; human.legStretch = 0.05f; human.feetSpacing = 0; human.hasTranslationDoF = false;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.sourceAvatar = null;
            mi.humanDescription = human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.userData = AvatarTag;
            mi.SaveAndReimport();
            if (!AvatarValid(path)) throw new InvalidDataException("mocap v3 clip did not build a valid Humanoid Avatar: " + path);
        }
    }

    /// <summary>Import settings for Assets/Art/animations/mocap_v3 (CTLifeImportPostprocessor skips this folder).</summary>
    public sealed class MocapV3Postprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 2;   // 2: loops and starts/stops based on body orientation (were reversed)

        private bool Ours => assetPath.StartsWith(MocapV3Import.Root + "/", StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            if (!Ours) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;   // Blender export (-Z forward, Y up): the character faces +Z
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.optimizeGameObjects = false;
            bool probe = MocapV3Import.RestProbe.Contains(assetPath);
            mi.importAnimation = !probe;
            if (probe) { mi.animationType = ModelImporterAnimationType.None; return; }
            mi.animationType = ModelImporterAnimationType.Human;
            if (mi.userData != MocapV3Import.AvatarTag)
                Debug.LogWarning("CTLIFE [mocap-v3] Avatar not configured yet (CT Life > Characters > Import mocap v3 locomotion): " + assetPath);
        }

        private void OnPreprocessAnimation()
        {
            if (!Ours || MocapV3Import.RestProbe.Contains(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            var kind = MocapV3Import.KindOf(assetPath);
            foreach (var c in clips)
            {
                c.rotationOffset = 0f;
                c.heightFromFeet = false;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                switch (kind)
                {
                    case MocapV3Import.Kind.Loop:
                        // in place, last frame == first: loop, everything baked into the pose
                        c.loopTime = true;
                        // Based Upon Body Orientation: the clips' original root faces -Z here (Original played every
                        // loop, start and stop backwards in game, 2026-10-22; LocoFacingProbe checks it)
                        c.lockRootRotation = true; c.keepOriginalOrientation = false;
                        c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                        break;
                    case MocapV3Import.Kind.StartStop:
                        // travel to root motion (unused: the actor's own body speed moves it), rotation baked
                        c.loopTime = false;
                        c.lockRootRotation = true; c.keepOriginalOrientation = false;
                        c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = true;
                        break;
                    default:
                        // turns: the yaw is root motion based on body orientation (the actor turns itself); steps stay
                        c.loopTime = false;
                        c.lockRootRotation = false; c.keepOriginalOrientation = false;
                        c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = true;
                        break;
                }
            }
            mi.clipAnimations = clips;
        }

        /// <summary>Clip FBX carry a 1-triangle `_bind_proxy` mesh (bind pose carrier): strip it.</summary>
        private void OnPostprocessModel(GameObject root)
        {
            if (!Ours) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("_bind_proxy")).ToList())
                if (t != null && t != root.transform) Object.DestroyImmediate(t.gameObject);
        }
    }
}
