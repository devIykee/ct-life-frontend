using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Persistent copies of inspected rooms and explicit seat profiles. No art recut or global asset save.</summary>
    public static class PlayableVenueIntegration
    {
        public const string Output = "Assets/Art/Candidates/PlayableVenues20261007";
        private const string Catalog = "Assets/Resources/World/VenueContentRegistry.asset";
        private const string Cafe = "Assets/Art/Candidates/TerminalCafe20261007Art5/TerminalCafeArt5.prefab";
        private const string Studio = "Assets/Art/Candidates/SpacesStudio20261007F4/SpacesStudioF4.prefab";
        private const string Home = "Assets/Art/Candidates/ParentsBasement20261007B2/ParentsBasementB2.prefab";
        private static string Evidence => Resolve("art/character-candidates/motion-review-20261006");
        private static JObject _proof;
        private static readonly List<Object> OwnedAssets = new List<Object>();
        private static readonly Dictionary<string, AnimatorController> Controllers = new Dictionary<string, AnimatorController>();
        private static readonly Dictionary<string, CafeWorkExitMotion> Motions = new Dictionary<string, CafeWorkExitMotion>();

        // First stage persists isolated content only. Registry publication is a separate scoped operation.
        public static void StageCandidates() => Run(false, Stage);
        public static void PublishReviewed() => Run(true, Publish);

        private static void Run(bool publishing, Action action)
        {
            int code = 0; Protection protection = null;
            try
            {
                Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Run in idle Edit mode.");
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    Need(!scene.isDirty, "Existing dirty scene must be preserved: " + scene.name);
                }
                _proof = ReadProofs();
                protection = new Protection(publishing ? new[] { Catalog } : Array.Empty<string>());
                action();
            }
            catch (Exception e) { code = 1; Debug.LogException(e); }
            finally
            {
                if (protection != null)
                {
                    try { protection.Finish(); } catch (Exception e) { code = 1; Debug.LogException(e); }
                    Debug.Log("[playable-venues] protection transaction: " + protection.Receipt);
                }
                // Do not clear unrelated dirty objects or restore/rewrite protected files.
                if (Application.isBatchMode) EditorApplication.Exit(code);
            }
        }

        private static JObject ReadProofs()
        {
            string path = Argument("integrationProofs");
            Need(!string.IsNullOrEmpty(path), "Supply -ctlife-integrationProofs with a completed, pinned proof manifest. The template is deliberately blocked.");
            var proof = JObject.Parse(File.ReadAllText(Resolve(path)));
            Need((string)proof["revision"] == "playable-venue-integration-v1" && (string)proof["output"] == Output, "Wrong integration schema/output.");
            foreach (var file in ((JObject)proof["pinned_files"]).Properties()) RequireHash(file.Name, (string)file.Value);
            var runtime = PinnedJson(proof["runtime_manifest"]); var clock = PinnedJson(proof["clock_manifest"]);
            foreach (var file in ((JObject)runtime["runtime_files"]).Properties())
            {
                string expected = (string)file.Value;
                if (file.Name == "Assets/Scripts/World/Avatars/CafeWorkExitGoals.cs")
                { Need(expected == (string)clock["source_baseline_sha256"], "Clock proof does not derive from pinned source."); expected = (string)clock["candidate_sha256"]; }
                RequireHash(file.Name, expected);
            }
            foreach (var file in ((JObject)runtime["data_sources"]).Properties()) RequireHash(Path.Combine(Evidence, file.Name), (string)file.Value);
            var rows = (JArray)proof["runtime_variants"];
            Need(rows.Count == 4, "Four explicit runtime families required.");
            foreach (string variant in new[] { "work", "cafe", "studio-audience", "studio-speaker" })
            {
                var row = rows.Single(x => (string)x["variant"] == variant);
                Need((bool?)row["visual_and_furniture_accepted"] == true, "Actual visual/furniture review is pending for " + variant);
                foreach (bool timing in new[] { true, false })
                {
                    var receipt = PinnedJson(row[timing ? "timing_receipt" : "visual_receipt"]);
                    Need((string)receipt["revision"] == "explicit-social-work-venue-runtime-v1" && (string)receipt["room_variant"] == variant,
                        "Wrong runtime receipt family: " + variant);
                    Need((bool?)receipt["component_checks_pass"] == true && (bool?)receipt["review_completed"] == true &&
                        (bool?)receipt["fitted_transition_contact_all_pass"] == true && (int?)receipt["fitted_contact_cases_completed"] == 4 &&
                        receipt["contact_failures"] is JArray contacts && contacts.Count == 0 && receipt["cleanup_failures"] is JArray cleanup && cleanup.Count == 0 &&
                        receipt["failure"]?.Type == JTokenType.Null, "Incomplete or failed native runtime proof: " + variant);
                    Need((bool?)receipt["timing_eligible"] == timing && (!timing || (bool?)receipt["desktop_cadence_all_pass"] == true), "Wrong timing evidence scope.");
                    RequireHash((string)receipt["prefab"], (string)receipt["prefab_sha256"]);
                    RequireHash("Assets/Scripts/World/Avatars/AvatarView.cs", (string)receipt["bridge_runtime_sha256"]);
                    var helper = row[timing ? "timing_helper_source" : "visual_helper_source"];
                    RequireHash((string)helper?["path"], (string)helper?["sha256"]);
                    Need((string)receipt["helper_sha256"] == (string)helper["sha256"], "Receipt differs from its exact frozen helper source.");
                    Need((string)receipt["prefab"] == (variant.StartsWith("studio-", StringComparison.Ordinal) ? Studio : Cafe), "Wrong selected art in runtime receipt.");
                }
            }
            return proof;
        }

        private static void Stage()
        {
            Need(!Directory.Exists(Output) && !File.Exists(Output + ".meta"), "Preserve an existing/failed integration candidate; choose a reviewed new revision.");
            OwnedAssets.Clear(); Controllers.Clear(); Motions.Clear();
            Directory.CreateDirectory(Output + "/Motion"); Directory.CreateDirectory(Output + "/Controllers");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var controllerProof = JObject.Parse(File.ReadAllText(Path.Combine(Evidence, "native-cafe-seat-controller-v1/receipt.json")));
            var rainContact = JObject.Parse(File.ReadAllText(Path.Combine(Evidence, "native-rain-walk-contact-v2/receipt.json")));
            Need((bool?)controllerProof["all_pass"] == true && ((JArray)controllerProof["protected_drift"]).Count == 0 &&
                (bool?)rainContact["all_pass"] == true && ((JArray)rainContact["protected_drift"]).Count == 0, "Original controller/Rain gait evidence failed.");
            RequireHash((string)rainContact["candidate_asset"], (string)rainContact["candidate_sha256"]);
            var sourceExit = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/animations/act_stand_up.fbx").OfType<AnimationClip>().Single(x => !x.name.StartsWith("__preview__", StringComparison.Ordinal));
            var exit = Object.Instantiate(sourceExit); exit.name = "Shared inspected stand prefix";
            var settings = AnimationUtility.GetAnimationClipSettings(exit); settings.loopTime = false; settings.loopBlend = false; AnimationUtility.SetAnimationClipSettings(exit, settings);
            string exitCurves = ClipCurves(exit);
            AssetDatabase.CreateAsset(exit, Output + "/Motion/StandPrefix.anim"); OwnedAssets.Add(exit);
            foreach (string look in new[] { "snow", "rain" })
            {
                var row = controllerProof["cases"].Single(x => (string)x["character"] == look);
                Need((bool?)row["all_pass"] == true, "Unaccepted source controller: " + look);
                foreach (string key in new[] { "controller", "baseline_controller" }) RequireHash((string)row[key + "_asset"], (string)row[key + "_sha256"]);
                foreach (string key in new[] { "social", "typing" }) RequireHash((string)row[key + "_clip"], (string)row[key + "_sha256"]);
                var baseline = AssetDatabase.LoadAssetAtPath<AnimatorController>((string)row["controller_asset"]);
                Controllers[look] = BuildController(baseline, look, rainContact, exit);
                foreach (string kind in new[] { "work", "social-right", "social-left" })
                {
                    var motion = BuildMotion(look, kind); string path = Output + "/Motion/" + look + "-" + kind + ".asset";
                    AssetDatabase.CreateAsset(motion, path); OwnedAssets.Add(motion); Motions[look + "/" + kind] = motion;
                }
            }
            foreach (var asset in OwnedAssets) AssetDatabase.SaveAssetIfDirty(asset);
            // Synchronous reload validates persisted references/data, not only transient authoring objects.
            foreach (var path in OwnedAssets.Select(AssetDatabase.GetAssetPath).Distinct()) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            exit = AssetDatabase.LoadAssetAtPath<AnimationClip>(Output + "/Motion/StandPrefix.anim");
            Need(exit != null && ClipCurves(exit) == exitCurves && exit.isHumanMotion == sourceExit.isHumanMotion &&
                !AnimationUtility.GetAnimationClipSettings(exit).loopTime && !AnimationUtility.GetAnimationClipSettings(exit).loopBlend, "Saved stand prefix curves/settings changed.");
            foreach (string look in new[] { "snow", "rain" })
            {
                var row = controllerProof["cases"].Single(x => (string)x["character"] == look);
                Controllers[look] = AssetDatabase.LoadAssetAtPath<AnimatorController>(Output + "/Controllers/" + look + ".controller");
                CheckController(AssetDatabase.LoadAssetAtPath<AnimatorController>((string)row["controller_asset"]), Controllers[look], look, rainContact, exit);
                foreach (string kind in new[] { "work", "social-right", "social-left" })
                {
                    var saved = AssetDatabase.LoadAssetAtPath<CafeWorkExitMotion>(Output + "/Motion/" + look + "-" + kind + ".asset");
                    var reference = BuildMotion(look, kind);
                    try { Need(saved != null && saved.Valid && JsonUtility.ToJson(saved) == JsonUtility.ToJson(reference), "Saved motion frames differ: " + look + "/" + kind); }
                    finally { Object.DestroyImmediate(reference); }
                    Motions[look + "/" + kind] = saved;
                }
            }
            var rooms = new JArray { CopyRoom(Cafe, "terminal_cafe", 12, controllerProof), CopyRoom(Studio, "spaces_studio", 30, controllerProof) };
            var assets = new JObject();
            foreach (string file in Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))) assets[file.Replace('\\', '/')] = Hash(file);
            File.WriteAllText(Output + "/StageReceipt.json", new JObject
            {
                ["revision"] = "persistent-playable-venues-v1", ["status"] = "persisted_profiles_unregistered_requires_actual_saved_runtime_review",
                ["rooms"] = rooms, ["route_count"] = 42, ["motion_assets"] = 6, ["derived_controllers"] = 2,
                ["art_changed"] = false, ["new_bake"] = false, ["assets"] = assets,
                ["proof_manifest_sha256"] = Hash(Resolve(Argument("integrationProofs"))),
                ["limitations"] = "Four representative room families were inspected before staging. Persistent controller/profile routing must be reviewed before catalog publication. No new connected/device or every-anchor exit claim. Existing art, nav, lightmaps and immutable anchors are held."
            }.ToString());
        }

        private static JObject CopyRoom(string source, string id, int routeCount, JObject controllerProof)
        {
            var baseline = AssetDatabase.LoadAssetAtPath<GameObject>(source); Need(baseline != null && baseline.GetComponent<CafeSeatMotionProfile>() == null, "Expected unbound selected art source.");
            string held = RoomSignature(baseline); string destination = Output + "/" + id + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(source);
            try
            {
                var venue = root.GetComponent<VenueSceneRoot>(); var config = new ConfigStore(); config.Apply(Resources.Load<TextAsset>("Contract/config_snapshot").text, null, persist: false); venue.Init(config.Registries.SpotsFor(id));
                Need(venue.venueId == id && venue.navigation != null && root.GetComponent<VenueLightmaps>() != null, "Selected root is incomplete.");
                var profile = root.AddComponent<CafeSeatMotionProfile>(); var serialized = new SerializedObject(profile);
                var bindings = serialized.FindProperty("bindings"); bindings.arraySize = 2;
                for (int i = 0; i < 2; i++)
                {
                    string look = i == 0 ? "snow" : "rain"; var proof = controllerProof["cases"].Single(x => (string)x["character"] == look);
                    var world = AssetDatabase.LoadAssetAtPath<GameObject>((string)proof["world_prefab_asset"]); var avatar = world.GetComponentsInChildren<Animator>(true).Single().avatar;
                    Need(AssetDatabase.GetAssetPath(avatar) == (string)proof["own_avatar_asset"] && avatar.name == (string)proof["own_avatar_name"], "Wrong own Avatar.");
                    var row = bindings.GetArrayElementAtIndex(i);
                    row.FindPropertyRelative("baselineController").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>((string)proof["baseline_controller_asset"]);
                    row.FindPropertyRelative("ownAvatar").objectReferenceValue = avatar; row.FindPropertyRelative("cafeController").objectReferenceValue = Controllers[look];
                    row.FindPropertyRelative("workExitMotion").objectReferenceValue = id == "terminal_cafe" ? Motions[look + "/work"] : null;
                    row.FindPropertyRelative("socialRightExitMotion").objectReferenceValue = Motions[look + "/social-right"];
                    row.FindPropertyRelative("socialLeftExitMotion").objectReferenceValue = id == "spaces_studio" ? Motions[look + "/social-left"] : null;
                }
                var anchors = venue.Anchors.Where(a => a.anim == "act_sit" || a.anim == "act_type").OrderBy(a => a.spotId, StringComparer.Ordinal).ToArray();
                Need(anchors.Length == routeCount && anchors.Select(a => a.spotId).Distinct().Count() == routeCount, "Exact unique seat count changed.");
                var routes = serialized.FindProperty("seatExits"); routes.arraySize = routeCount; var witnesses = new JArray();
                for (int i = 0; i < anchors.Length; i++)
                {
                    var anchor = anchors[i]; string spot = anchor.spotId;
                    bool typing = id == "terminal_cafe" && Numbered(spot, "terminal_", 8); bool social = id == "terminal_cafe" ? Numbered(spot, "table_", 4) : Numbered(spot, "seat_", 20) || Numbered(spot, "stage_", 10);
                    Need((typing && anchor.anim == "act_type") || (social && anchor.anim == "act_sit"), "Unapproved spot/action: " + spot);
                    Need(anchor.approach != null, "Missing authored standing approach: " + spot);
                    Need(venue.TryGetSpotApproach(spot, out var approach), "Unresolved authored standing approach: " + spot);
                    float length = Vector3.Distance(anchor.transform.position, approach);
                    Need(length > .02f && length <= 2.5f && Mathf.Abs(anchor.transform.position.y - approach.y) < .02f, "Route outside runtime bounds: " + spot);
                    var row = routes.GetArrayElementAtIndex(i); row.FindPropertyRelative("spotId").stringValue = spot; var points = row.FindPropertyRelative("points"); points.arraySize = 1; points.GetArrayElementAtIndex(0).vector3Value = venue.transform.InverseTransformPoint(approach);
                    witnesses.Add(new JObject { ["spot"] = spot, ["action"] = anchor.anim, ["yaw_radians"] = anchor.RotY,
                        ["seat"] = Vec(anchor.transform.position), ["approach"] = Vec(approach),
                        ["motion"] = typing ? "work" : id == "spaces_studio" && Numbered(spot, "stage_", 10) ? "social-left" : "social-right" });
                }
                serialized.ApplyModifiedPropertiesWithoutUndo(); Need(RoomSignature(root) == held, "Profile attachment changed selected art/nav/colliders/lightmaps/anchors.");
                PrefabUtility.SaveAsPrefabAsset(root, destination);
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(destination); Need(saved != null && RoomSignature(saved) == held && saved.GetComponent<CafeSeatMotionProfile>() != null, "Saved room copy lost held content/profile.");
                return new JObject { ["venue"] = id, ["source"] = source, ["source_sha256"] = Hash(source), ["prefab"] = destination, ["prefab_sha256"] = Hash(destination), ["routes"] = witnesses, ["held_content_signature"] = held };
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static CafeWorkExitMotion BuildMotion(string look, string kind)
        {
            bool work = kind == "work", left = kind == "social-left";
            string folder = work ? "native-cafe-exit-second-step-v2" : "native-shared-social-exit-v4";
            string label = work ? look : look + (left ? "-left" : "-right");
            var proof = JObject.Parse(File.ReadAllText(Path.Combine(Evidence, folder + "/receipt.json")));
            Need((bool?)proof["all_pass"] == true && ((JArray)proof["protected_drift"]).Count == 0, "Source motion proof failed.");
            var row = proof["cases"].Single(c => (string)c["character"] == look && (work || (string)c["label"] == label));
            Need((bool?)row["all_pass"] == true, "Source motion case failed.");
            var original = JArray.Parse(File.ReadAllText(Path.Combine(Evidence, folder + "/" + label + "-original.json")));
            Vector3 V(JToken t) { var a = t.Values<float>().ToArray(); return new Vector3(a[0], a[1], a[2]); }
            Quaternion Q(JToken t) { var a = t.Values<float>().ToArray(); return new Quaternion(a[0], a[1], a[2], a[3]); }
            var motion = ScriptableObject.CreateInstance<CafeWorkExitMotion>(); motion.name = look + "-" + kind;
            motion.hands = look == "rain"; motion.settleContact = look == "snow"; motion.frames = new CafeWorkExitMotion.Frame[CafeWorkExitMotion.SampleCount];
            if (!work) { float sign = left ? -1 : 1; motion.firstStep = new Vector3(sign * .375f, 0, -.05f); motion.secondStep = new Vector3(sign * .375f, 0, 0); }
            Need(original.Count == CafeWorkExitMotion.SampleCount && row["full_body_offset_samples"].Count() == CafeWorkExitMotion.SampleCount &&
                Mathf.Abs((float)row["length_seconds"] - CafeWorkExitMotion.Length) < .00001f, "Motion sample count/duration differs.");
            Quaternion[] offsets = null;
            if (motion.hands)
            {
                var hand = JObject.Parse(File.ReadAllText(Path.Combine(Evidence, "native-typing-work-hands-v2/receipt.json")));
                Need((bool?)hand["all_pass"] == true && ((JArray)hand["protected_drift"]).Count == 0, "Hand coordinate calibration failed.");
                offsets = hand["cases"].Single(c => (string)c["character"] == look)["goal_to_wrist_orientation_calibration"].Select(c => Q(c["local_offset"])).ToArray();
            }
            float Ease(float u) { u = Mathf.Clamp01(u); return u * u * (3 - 2 * u); }
            for (int i = 0; i < motion.frames.Length; i++)
            {
                var frame = new CafeWorkExitMotion.Frame
                {
                    body = V(row["full_body_offset_samples"][i]),
                    leftFoot = V(row["goal_positions"][0]) + V(row["foot_offset_samples"][i][0]), rightFoot = V(row["goal_positions"][1]) + V(row["foot_offset_samples"][i][1]),
                    leftFootRotation = Q(row["goal_rotations"][0]), rightFootRotation = Q(row["goal_rotations"][1])
                };
                motion.frames[i] = frame;
                if (motion.hands)
                {
                    float seconds = i / 120f, envelope = Ease(seconds / .20f) * (1 - Ease((seconds - .60f) / .26f));
                    var l = new Vector3(-.08f, .04f, .08f) * envelope; var r = new Vector3(.08f, .04f, .08f) * envelope;
                    frame.leftHand = V(original[i]["joints"][13]) + frame.body + l; frame.rightHand = V(original[i]["joints"][16]) + frame.body + r;
                    frame.leftElbow = V(original[i]["joints"][12]) + frame.body + l * .5f; frame.rightElbow = V(original[i]["joints"][15]) + frame.body + r * .5f;
                    frame.leftHandRotation = Q(original[i]["rotations"][13]) * Quaternion.Inverse(offsets[0]); frame.rightHandRotation = Q(original[i]["rotations"][16]) * Quaternion.Inverse(offsets[1]);
                }
            }
            Need(motion.Valid, "Invalid explicit pair data."); return motion;
        }

        private static AnimatorController BuildController(AnimatorController baseline, string look, JObject rain, AnimationClip exitClip)
        {
            Need(baseline != null && baseline.layers.Length == 1, "Expected single-layer inspected controller.");
            var layer = baseline.layers[0]; var source = layer.stateMachine;
            Need(source.states.Length == 48 && source.stateMachines.Length == 0 && source.anyStateTransitions.Length == 0 && source.entryTransitions.Length == 0 && source.behaviours.Length == 0 &&
                source.states.All(s => s.state.transitions.Length == 0 && s.state.behaviours.Length == 0), "Unexpected controller topology/behaviours.");
            string path = Output + "/Controllers/" + look + ".controller";
            var controller = new AnimatorController { name = look + " fitted venue seats" }; AssetDatabase.CreateAsset(controller, path); OwnedAssets.Add(controller);
            var machine = new AnimatorStateMachine { name = layer.name }; AssetDatabase.AddObjectToAsset(machine, controller); controller.parameters = baseline.parameters;
            foreach (var child in source.states)
            {
                var state = machine.AddState(child.state.name, child.position); EditorUtility.CopySerialized(child.state, state);
                if (child.state == source.defaultState) machine.defaultState = state;
                if (look == "rain" && state.name == "loco_walk")
                { Need(AssetDatabase.GetAssetPath(state.motion) == (string)rain["source_asset"] && state.iKOnFeet, "Unexpected Rain walking source."); state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>((string)rain["candidate_asset"]); }
            }
            var exit = machine.AddState(CafeSeatMotionProfile.WorkExitState);
            exit.motion = exitClip; exit.speed = CafeWorkExitMotion.Rate; exit.speedParameterActive = false; exit.iKOnFeet = false; exit.writeDefaultValues = true;
            var idle = machine.AddState(CafeSeatMotionProfile.WorkExitIdleState); EditorUtility.CopySerialized(source.states.Single(s => s.state.name == "loco_idle").state, idle);
            idle.name = CafeSeatMotionProfile.WorkExitIdleState; idle.speed = CafeWorkExitMotion.Rate; idle.speedParameterActive = false;
            controller.layers = new[] { new AnimatorControllerLayer { name = layer.name, defaultWeight = layer.defaultWeight, iKPass = true, stateMachine = machine, blendingMode = layer.blendingMode, avatarMask = layer.avatarMask } };
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller); return controller;
        }
        private static void CheckController(AnimatorController original, AnimatorController saved, string look, JObject rain, AnimationClip exitClip)
        {
            Need(saved != null && saved.layers.Length == 1 && saved.layers[0].iKPass, "Saved controller missing IK dispatch layer.");
            var sourceLayer = original.layers[0]; var savedLayer = saved.layers[0];
            Need(savedLayer.name == sourceLayer.name && savedLayer.defaultWeight == sourceLayer.defaultWeight && savedLayer.blendingMode == sourceLayer.blendingMode && savedLayer.avatarMask == sourceLayer.avatarMask, "Saved controller layer differs.");
            string Parameters(AnimatorController c) => JsonConvert.SerializeObject(c.parameters.Select(p => new { p.name, p.type, p.defaultBool, p.defaultFloat, p.defaultInt }));
            Need(Parameters(original) == Parameters(saved), "Saved controller parameters differ.");
            var source = sourceLayer.stateMachine; var actual = savedLayer.stateMachine;
            Need(actual.states.Length == source.states.Length + 2 && actual.defaultState.name == source.defaultState.name && actual.stateMachines.Length == 0 &&
                actual.anyStateTransitions.Length == 0 && actual.entryTransitions.Length == 0 && actual.behaviours.Length == 0, "Saved controller topology changed.");
            foreach (var child in source.states)
            {
                var target = actual.states.Single(x => x.state.name == child.state.name).state;
                var expectedMotion = look == "rain" && target.name == "loco_walk" ? AssetDatabase.LoadAssetAtPath<AnimationClip>((string)rain["candidate_asset"]) : child.state.motion;
                Need(StateSignature(child.state, expectedMotion) == StateSignature(target, target.motion), "Saved original state semantics changed: " + target.name);
            }
            var exit = actual.states.Single(x => x.state.name == CafeSeatMotionProfile.WorkExitState).state;
            Need(exit.motion == exitClip && exit.speed == CafeWorkExitMotion.Rate && !exit.iKOnFeet && !exit.speedParameterActive && exit.writeDefaultValues, "Saved exit state differs.");
            var idle = actual.states.Single(x => x.state.name == CafeSeatMotionProfile.WorkExitIdleState).state;
            var sourceIdle = source.states.Single(x => x.state.name == "loco_idle").state;
            Need(StateSignature(sourceIdle, sourceIdle.motion, CafeSeatMotionProfile.WorkExitIdleState, CafeWorkExitMotion.Rate, false) == StateSignature(idle, idle.motion), "Saved exit idle differs.");
            Need(actual.states.All(x => x.state.transitions.Length == 0 && x.state.behaviours.Length == 0), "Unexpected saved transition/behaviour.");
        }
        private static string StateSignature(AnimatorState s, Motion motion, string expectedName = null, float? expectedSpeed = null, bool? expectedSpeedActive = null) => JsonConvert.SerializeObject(new
        {
            name = expectedName ?? s.name, motion = Identity(motion), speed = expectedSpeed ?? s.speed, s.speedParameter, speedParameterActive = expectedSpeedActive ?? s.speedParameterActive, s.iKOnFeet, s.writeDefaultValues,
            s.mirror, s.mirrorParameter, s.mirrorParameterActive, s.cycleOffset, s.cycleOffsetParameter, s.cycleOffsetParameterActive,
            s.timeParameter, s.timeParameterActive, s.tag
        });
        private static string ClipCurves(AnimationClip clip)
        {
            var rows = new JArray();
            foreach (var b in AnimationUtility.GetCurveBindings(clip).OrderBy(x => x.path + "/" + x.propertyName, StringComparer.Ordinal))
            {
                var c = AnimationUtility.GetEditorCurve(clip, b);
                rows.Add(new JObject { ["path"] = b.path, ["type"] = b.type.FullName, ["property"] = b.propertyName, ["before"] = (int)c.preWrapMode, ["after"] = (int)c.postWrapMode,
                    ["keys"] = JArray.FromObject(c.keys.Select(k => new { k.time, k.value, k.inTangent, k.outTangent, k.inWeight, k.outWeight, mode = (int)k.weightedMode })) });
            }
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip).OrderBy(x => x.path + "/" + x.propertyName, StringComparer.Ordinal))
                rows.Add(new JObject { ["path"] = b.path, ["type"] = b.type.FullName, ["property"] = b.propertyName,
                    ["objects"] = JArray.FromObject(AnimationUtility.GetObjectReferenceCurve(clip, b).Select(k => new { k.time, value = Identity(k.value) })) });
            return rows.ToString(Formatting.None);
        }

        private static void Publish()
        {
            var stage = JObject.Parse(File.ReadAllText(Output + "/StageReceipt.json"));
            Need((string)stage["revision"] == "persistent-playable-venues-v1" && (int?)stage["route_count"] == 42, "Persistent stage receipt missing.");
            foreach (var asset in ((JObject)stage["assets"]).Properties()) RequireHash(asset.Name, (string)asset.Value);
            var acceptance = PinnedJson(_proof["integration_acceptance"]);
            Need((bool?)acceptance["approved"] == true && (string)acceptance["scope"] == "actual_saved_cafe_studio_profiles_and_travel" &&
                (string)acceptance["stage_receipt_sha256"] == Hash(Output + "/StageReceipt.json"), "Actual saved-profile integration is not accepted.");
            foreach (var room in stage["rooms"]) Need((string)acceptance["prefabs"][(string)room["venue"]] == (string)room["prefab_sha256"], "Acceptance names different persisted room bytes.");
            RequireHash(Catalog, (string)_proof["catalog_baseline_sha256"]);
            var catalog = AssetDatabase.LoadAssetAtPath<VenueContentRegistry>(Catalog); Need(catalog != null && !EditorUtility.IsDirty(catalog), "Preserve pending catalog changes.");
            Need(catalog.venues.Select(x => x.venueId).Distinct().Count() == catalog.venues.Count && catalog.homes.Select(x => x.homeId).Distinct().Count() == catalog.homes.Count, "Ambiguous existing catalog.");
            string existing = JsonConvert.SerializeObject(catalog.venues.Where(x => x.venueId != "terminal_cafe" && x.venueId != "spaces_studio").Select(x => new { x.venueId, prefab = Identity(x.prefab) }));
            string homes = JsonConvert.SerializeObject(catalog.homes.Select(x => new { x.homeId, prefab = Identity(x.prefab) }));
            Need(catalog.Find("timeline_square") != null && catalog.FindHome("parents_basement") == AssetDatabase.LoadAssetAtPath<GameObject>(Home).GetComponent<VenueSceneRoot>(), "Preserve accepted Timeline/private Home before publication.");
            var backup = Path.Combine(Path.GetTempPath(), "ct-playable-venue-catalog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup); File.Copy(Catalog, Path.Combine(backup, "VenueContentRegistry.asset"));
            foreach (var room in stage["rooms"])
            {
                string id = (string)room["venue"]; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)room["prefab"]).GetComponent<VenueSceneRoot>();
                var row = catalog.venues.SingleOrDefault(x => x.venueId == id);
                if (row == null) catalog.venues.Add(new VenueContentRegistry.Entry { venueId = id, prefab = prefab }); else row.prefab = prefab;
            }
            Need(JsonConvert.SerializeObject(catalog.venues.Where(x => x.venueId != "terminal_cafe" && x.venueId != "spaces_studio").Select(x => new { x.venueId, prefab = Identity(x.prefab) })) == existing &&
                JsonConvert.SerializeObject(catalog.homes.Select(x => new { x.homeId, prefab = Identity(x.prefab) })) == homes, "Unrelated catalog entry changed.");
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            File.WriteAllText(Path.Combine(backup, "PublicationReceipt.json"), new JObject { ["status"] = "selected_reviewed_room_prefabs", ["catalog_sha256"] = Hash(Catalog), ["stage_receipt_sha256"] = Hash(Output + "/StageReceipt.json"), ["preserved_private_homes"] = true, ["preserved_other_public_entries"] = true }.ToString());
            Debug.Log("[playable-venues] exact catalog backup/receipt: " + backup);
        }

        private static string RoomSignature(GameObject root)
        {
            string PathOf(Transform item)
            {
                var parts = new List<string>(); while (item != root.transform) { Need(item != null, "Reference escaped room."); parts.Add(item.GetSiblingIndex() + ":" + item.name); item = item.parent; } parts.Reverse(); return string.Join("/", parts);
            }
            JArray Matrix(Transform t) { var m = root.transform.worldToLocalMatrix * t.localToWorldMatrix; var a = new JArray(); for (int i = 0; i < 16; i++) a.Add(m[i]); return a; }
            var meshes = new JArray(root.GetComponentsInChildren<MeshFilter>(true).Select(x => new JObject { ["path"] = PathOf(x.transform), ["matrix"] = Matrix(x.transform), ["mesh"] = Identity(x.sharedMesh), ["active"] = x.gameObject.activeSelf }));
            var renderers = new JArray(root.GetComponentsInChildren<MeshRenderer>(true).Select(x => new JObject { ["path"] = PathOf(x.transform), ["enabled"] = x.enabled, ["materials"] = new JArray(x.sharedMaterials.Select(Identity)) }));
            var colliders = new JArray(root.GetComponentsInChildren<Collider>(true).Select(x => new JObject { ["path"] = PathOf(x.transform), ["type"] = x.GetType().Name, ["matrix"] = Matrix(x.transform), ["enabled"] = x.enabled, ["trigger"] = x.isTrigger,
                ["mesh"] = x is MeshCollider mc ? Identity(mc.sharedMesh) : null, ["convex"] = x is MeshCollider mesh && mesh.convex,
                ["center"] = x is BoxCollider box ? Vec(box.center) : x is CapsuleCollider capsule ? Vec(capsule.center) : null,
                ["size"] = x is BoxCollider b ? Vec(b.size) : x is CapsuleCollider cap ? new JArray(cap.radius, cap.height, cap.direction) : null }));
            var anchors = new JArray(root.GetComponentsInChildren<SpotAnchor>(true).Select(x => new JObject { ["path"] = PathOf(x.transform), ["spot"] = x.spotId, ["anim"] = x.anim, ["matrix"] = Matrix(x.transform), ["approach"] = x.approach != null ? PathOf(x.approach) : null, ["approach_matrix"] = x.approach != null ? Matrix(x.approach) : null }));
            var maps = root.GetComponent<VenueLightmaps>(); var venue = root.GetComponent<VenueSceneRoot>();
            Need(maps != null && maps.colors.Length > 0 && maps.bindings.Length > 0, "Source maps missing.");
            var lightmaps = new JObject { ["mode"] = (int)maps.mode, ["colors"] = new JArray(maps.colors.Select(Identity)), ["bindings"] = new JArray(maps.bindings.Select(x => new JObject { ["renderer"] = PathOf(x.renderer.transform), ["map"] = x.lightmap, ["uv"] = new JArray(x.scaleOffset.x, x.scaleOffset.y, x.scaleOffset.z, x.scaleOffset.w) })) };
            var lights = new JArray(root.GetComponentsInChildren<Light>(true).Select(x => new JObject { ["path"] = PathOf(x.transform), ["matrix"] = Matrix(x.transform), ["type"] = (int)x.type, ["enabled"] = x.enabled, ["intensity"] = x.intensity, ["range"] = x.range, ["color"] = new JArray(x.color.r, x.color.g, x.color.b, x.color.a), ["shadows"] = (int)x.shadows }));
            return new JObject { ["meshes"] = meshes, ["renderers"] = renderers, ["colliders"] = colliders, ["anchors"] = anchors, ["lightmaps"] = lightmaps, ["lights"] = lights,
                ["nav"] = Identity(venue.navigation), ["venue"] = venue.venueId, ["district"] = venue.districtId, ["camera"] = new JArray(venue.cameraDistance, venue.cameraPitch, venue.cameraYaw, venue.cameraFieldOfView, venue.cameraTargetViewportY, venue.cameraFocusHeight), ["lookahead"] = Vec(venue.cameraLookAhead), ["bounds"] = new JArray(venue.cameraBounds.x, venue.cameraBounds.y, venue.cameraBounds.width, venue.cameraBounds.height) }.ToString(Formatting.None);
        }

        private sealed class Protection
        {
            public readonly string Receipt; private readonly Dictionary<string, string> before; private readonly HashSet<string> allowed; private readonly bool excludeOutput;
            public Protection(IEnumerable<string> writes)
            {
                allowed = new HashSet<string>(writes, StringComparer.Ordinal); excludeOutput = allowed.Count == 0; string folder = Path.Combine(Path.GetTempPath(), "ct-playable-venue-integration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); Receipt = Path.Combine(folder, "transaction.json"); before = Scan();
                File.WriteAllText(Receipt, new JObject { ["status"] = "captured", ["protected_files"] = JObject.FromObject(before) }.ToString());
            }
            private Dictionary<string, string> Scan()
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string tree in new[] { "Assets", "ProjectSettings", "Packages" })
                    foreach (string file in Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories))
                    { string p = file.Replace('\\', '/'); if ((excludeOutput && (p.StartsWith(Output + "/", StringComparison.Ordinal) || p == Output + ".meta")) || allowed.Contains(p)) continue; result.Add(p, Hash(p)); }
                return result;
            }
            public void Finish()
            {
                var after = Scan(); var changed = before.Where(x => !after.TryGetValue(x.Key, out var hash) || hash != x.Value).Select(x => x.Key).ToArray(); var added = after.Keys.Where(x => !before.ContainsKey(x)).ToArray();
                File.WriteAllText(Receipt, new JObject { ["status"] = changed.Length == 0 && added.Length == 0 ? "unchanged_at_method_end_post_editor_audit_required" : "drift", ["protected_files"] = JObject.FromObject(before), ["changed_or_missing"] = new JArray(changed), ["new_outside_output"] = new JArray(added), ["allowed_files"] = new JArray(allowed) }.ToString());
                Need(changed.Length == 0 && added.Length == 0, "Protected tree drift; retained transaction, no automatic restoration.");
            }
        }
        private static JArray Vec(Vector3 v) => new JArray(v.x, v.y, v.z);
        private static bool Numbered(string id, string prefix, int maximum) => id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.Substring(prefix.Length), out int n) && n >= 1 && n <= maximum && id == prefix + n;
        private static string Identity(Object value) { if (value == null) return null; Need(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long local), "Expected persistent asset: " + value.name); return guid + ":" + local; }
        private static JObject PinnedJson(JToken pin) { string path = (string)pin?["path"], hash = (string)pin?["sha256"]; RequireHash(path, hash); return JObject.Parse(File.ReadAllText(Resolve(path))); }
        private static string Resolve(string path) { Need(!string.IsNullOrWhiteSpace(path), "Required proof path remains unset."); return Path.IsPathRooted(path) ? path : Path.GetFullPath(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("ProjectSettings/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal) ? path : Path.Combine("..", path)); }
        private static void RequireHash(string path, string expected) { Need(expected != null && expected.Length == 64 && Hash(Resolve(path)) == expected, "Missing or changed pinned input: " + path); }
        private static string Hash(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        private static string Argument(string key) { var args = Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-ctlife-" + key) return args[i + 1]; return null; }
        private static void Need(bool yes, string message) { if (!yes) throw new InvalidDataException(message); }
    }
}
