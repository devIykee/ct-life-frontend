// Isolated Rain walking contact correction after actual room source-phase evidence; no root/body shift.
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
    public static class RainWalkContactCandidateV2
    {
        private const int Keys = 120, Dense = 240;
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/RainWalkContactV2/";
        private const string SourcePath = "Assets/Art/animations/loco_walk.fbx";
        private static readonly string[] PatchNames = { "Foot.L", "Toes.L", "Foot.R", "Toes.R" };
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
                controller = new AnimatorController { name = "UNSAVED_RAIN_WALK_CONTACT_V2" };
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
                    var patches=PatchNames.Select(name=>Enumerable.Range(0,weights.Length).Where(i=>{
                        var w=weights[i];float W(int b,float v)=>v>0&&names[b]==name?v:0;
                        return W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3)>.5f;
                    }).ToArray()).ToArray();
                    Require(patches.All(x=>x.Length>0),"Missing own-rig weighted foot/toe surfaces.");
                    var skin = new Skin { renderer = renderer, baked = new Mesh(), left = Select("L"), right = Select("R"), patches = patches };
                    skins.Add(skin); Require(skin.left.Length > 0 && skin.right.Length > 0, "Missing footwear weights.");
                    skin.vertices.Capacity = renderer.sharedMesh.vertexCount;
                }
                Require(skins.Count == 3, "Expected all three actual Rain world skins.");
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
            public Frame Read(float phase, bool muscles = false)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                Require(Mathf.Abs(info.normalizedTime - (2f + phase)) < .001f, "Sample label differs from actual native state time.");
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

        public static void Capture()
        {
            Require(Application.isBatchMode,"Dedicated native batch required.");
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006/native-rain-walk-contact-v2"));
            Directory.CreateDirectory(output);var before=Snapshot();var receipt=new JObject{["revision"]="rain-contact-source-relative-loop-v2",["all_pass"]=false,
                ["scope"]="Rain raw walk contact only: preserves original root/body/muscles/foot orientation curves and cadence. The original approximately 2.166mm foot loop seam is retained and reported as an inherited limitation, not accepted as seamless gait. Source-relative seam/sliding nonregression replaces the rejected v1 absolute seam gate by explicit scope decision. Absolute floor, pose and anatomy gates are unchanged. No registry/controller/profile assignment or global Y offset; actual room/device acceptance remains pending."};
            AnimationClip source=null,copy=null,candidate=null;Scene scene=default;bool opened=false;
            try
            {
                Require(Hash(SourcePath)=="23eb4b68326d47798d210f43a70c0f1654967b0a568e746652659910d16b12c3"&&Hash(SourcePath+".meta")=="5d162fdd8276f86609c621e3ea927cb3617ce1c43cff46fe7d635a0fbd007617","Raw walk source/importer changed.");
                var importer=(ModelImporter)AssetImporter.GetAtPath(SourcePath);var setting=importer.clipAnimations.Single();
                Require(setting.keepOriginalPositionY&&!setting.heightFromFeet&&Mathf.Abs(setting.heightOffset)<.00001f,"Expected raw walk OriginalY, no Feet projection/height offset.");
                var proof=JObject.Parse(File.ReadAllText(Path.Combine(output,"../native-cafe-seat-controller-v1/receipt.json")));
                var accepted=proof["cases"].Single(c=>(string)c["character"]=="rain");Require((bool?)accepted["all_pass"]==true,"Accepted same-Rain identity required.");
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");var entry=registry.looks.Single(e=>e.key==(string)accepted["look_key"]);
                Require(AssetDatabase.GetAssetPath(entry.prefab)==(string)accepted["world_prefab_asset"],"World Rain prefab changed.");
                var own=entry.prefab.GetComponentInChildren<Animator>(true).avatar;
                Require(AssetDatabase.GetAssetPath(own)==(string)accepted["own_avatar_asset"]&&own.name==(string)accepted["own_avatar_name"],"Actual own Avatar differs from fitted controller evidence.");
                source=AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
                scene=EditorSceneManager.NewPreviewScene();opened=true;
                var baseline=new Frame[Keys+1];var lifts=new float[Keys+1][];
                using(var rig=new Rig(entry.prefab,scene,"loco_walk",source,1,1,true))
                {
                    rig.Warm(Keys);
                    for(int i=0;i<=Keys;i++){baseline[i]=rig.Read(i/(float)Keys,true);lifts[i]=new[]{Lift(baseline[i].leftSole),Lift(baseline[i].rightSole)};if(i<Keys)rig.Step(Keys);}
                }
                receipt["source_asset"]=SourcePath;receipt["source_sha256"]=Hash(SourcePath);receipt["source_importer_sha256"]=Hash(SourcePath+".meta");receipt["look_key"]=entry.key;receipt["world_prefab_asset"]=AssetDatabase.GetAssetPath(entry.prefab);receipt["own_avatar_asset"]=AssetDatabase.GetAssetPath(own);
                receipt["maximum_requested_ankle_y_m"]=lifts.SelectMany(x=>x).Max();
                Require(lifts.SelectMany(x=>x).Max()<=.015f,"Raw walk defect exceeds this measured15mm maximum foot-only correction.");
                CheckGoalResponse(entry.prefab,scene,"loco_walk",source,1,1,true,baseline,receipt);
                copy=Object.Instantiate(source);candidate=GoalClone(source,baseline,lifts,"Rain_WalkContact_CafeCandidate");
                ValidateUnchangedCurves(source,candidate);
                var a=Sample(entry.prefab,scene,"loco_walk",source,1,1,true,Path.Combine(output,"rain-original"));
                var b=Sample(entry.prefab,scene,"loco_walk",copy,1,1,true);
                var c=Sample(entry.prefab,scene,"loco_walk",candidate,1,1,true,Path.Combine(output,"rain-candidate"));
                WriteFrames(Path.Combine(output,"rain-original.json"),a,source.length);WriteFrames(Path.Combine(output,"rain-candidate.json"),c,source.length);
                float roundtrip=MaxJoints(a,b,Enumerable.Range(0,Tracked.Length));receipt["unmodified_clone_joint_error_m"]=roundtrip;Require(roundtrip<.0001f,"Unmodified clip cloning changed native pose.");
                // Independently observed actual-room raw walk failure: phase .54, about -5.53mm.
                float roomPhase=a[130].leftSole<a[130].rightSole?a[130].leftSole:a[130].rightSole;
                receipt["source_phase_0_5417_minimum_m"]=roomPhase;Require(Mathf.Abs(roomPhase-(-.00553f))<.002f,"Raw source sampling does not reproduce actual room phase.54 within2mm.");
                Validate(a,c,source.length,receipt);
                Directory.CreateDirectory(Stage);string path=Stage+"Rain_WalkContact_CafeCandidate.anim";Require(!File.Exists(path),"Preserve prior candidate artifact; use a new revision instead of overwrite.");
                AssetDatabase.CreateAsset(candidate,path);candidate=null;var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);AssetDatabase.SaveAssetIfDirty(saved);
                receipt["candidate_asset"]=path;receipt["candidate_sha256"]=Hash(path);receipt["persistent_replay_verified"]=false;receipt["all_pass"]=true;
            }
            catch(Exception e){receipt["failure"]=e.ToString();Debug.LogError("CTLIFE Rain walk: "+e);}
            finally
            {
                if(copy!=null)Object.DestroyImmediate(copy);if(candidate!=null)Object.DestroyImmediate(candidate);if(opened)EditorSceneManager.ClosePreviewScene(scene);
                var after=Snapshot();var drift=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var a)||!after.TryGetValue(k,out var b)||a!=b).ToArray();receipt["protected_drift"]=new JArray(drift);
                if(drift.Length>0)receipt["all_pass"]=false;receipt["status"]=(bool)receipt["all_pass"]?"contact-improved-inherited-loop-retained-not-promoted":"failed";
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());
            }
            Require((bool)receipt["all_pass"],"Rain walk candidate failed; original motion remains assigned.");
        }
        private static Dictionary<string,string> Snapshot()=>new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Where(p=>!p.Replace('\\','/').StartsWith(Stage,StringComparison.Ordinal)&&p!=Stage.TrimEnd('/')+".meta").ToDictionary(p=>p,Hash);
        private static float Lift(float sole){const float band=.002f;if(sole>=band)return 0;if(sole<=-band)return -sole;float x=band-sole;return x*x/(4*band);}
        private static void ValidateUnchangedCurves(AnimationClip a,AnimationClip b)
        {
            var allowed=new HashSet<string>(new[]{"LeftFootT.x","LeftFootT.y","LeftFootT.z","RightFootT.x","RightFootT.y","RightFootT.z"});
            var aa=AnimationUtility.GetCurveBindings(a);var bb=AnimationUtility.GetCurveBindings(b);Require(aa.Length==bb.Length&&aa.All(bb.Contains),"Curve bindings changed.");
            foreach(var binding in aa){if(binding.type==typeof(Animator)&&allowed.Contains(binding.propertyName))continue;var x=AnimationUtility.GetEditorCurve(a,binding);var y=AnimationUtility.GetEditorCurve(b,binding);Require(x.keys.SequenceEqual(y.keys)&&x.preWrapMode==y.preWrapMode&&x.postWrapMode==y.postWrapMode,"Non-foot-goal curve changed: "+binding.propertyName);}
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
                        values[i] = delta[axis];
                    }
                    AnimationUtility.SetEditorCurve(clip, binding, AddDelta(original, PeriodicCurve(values, source.length)));
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
                // A zero correction sample lies outside this phase's repair window. No overshoot into flight.
                if (Mathf.Abs(values[i]) < 1e-12f) tangent = 0f;
                keys[i] = new Keyframe(i * dt, values[i], tangent, tangent);
            }
            return new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
        }
        private static AnimationCurve AddDelta(AnimationCurve original, AnimationCurve delta)
        {
            var a = original.keys; var b = delta.keys;
            Require(a.Length >= 2 && b.Length >= 2 && a.Concat(b).All(k => k.weightedMode == WeightedMode.None && Finite(k.inTangent) && Finite(k.outTangent)),
                "Expected measured unweighted finite Humanoid goal curves; do not approximate weighted/step curves.");
            Require(Mathf.Abs(a[0].time - b[0].time) < .000001f && Mathf.Abs(a[a.Length - 1].time - b[b.Length - 1].time) < .000001f,
                "Source and correction domains differ.");
            // The sum of piecewise unweighted cubics is exact at the union of their knots when
            // values AND left/right derivatives are retained. Do not periodize the original curve:
            // its authored endpoint tangents differ and resampling them caused the V2 XZ artifact.
            var times = a.Select(k => k.time).ToList();
            foreach (var key in b)
                if (times.All(t => Mathf.Abs(t - key.time) > .000001f)) times.Add(key.time);
            times.Sort();
            var keys = times.Select(t => new Keyframe(t, original.Evaluate(t) + delta.Evaluate(t),
                CurveSlope(a, t, true) + CurveSlope(b, t, true), CurveSlope(a, t, false) + CurveSlope(b, t, false))).ToArray();
            var result = new AnimationCurve(keys) { preWrapMode = original.preWrapMode, postWrapMode = original.postWrapMode };
            float error = 0f;
            for (int i = 0; i <= 1000; i++)
            {
                float t = Mathf.Lerp(a[0].time, a[a.Length - 1].time, i / 1000f);
                error = Mathf.Max(error, Mathf.Abs(result.Evaluate(t) - original.Evaluate(t) - delta.Evaluate(t)));
            }
            Require(error < .000005f, "Curve composition differs from source+delta by more than5 microunits.");
            return result;
        }
        private static float CurveSlope(Keyframe[] keys, float time, bool incoming)
        {
            foreach (var key in keys)
                if (Mathf.Abs(time - key.time) < .000001f) return incoming ? key.inTangent : key.outTangent;
            int i = 0; while (i + 1 < keys.Length && keys[i + 1].time < time) i++;
            Require(i + 1 < keys.Length, "Curve derivative requested outside its domain.");
            var a = keys[i]; var b = keys[i + 1]; float h = b.time - a.time, t = (time - a.time) / h;
            return ((6f * t * t - 6f * t) * a.value + (-6f * t * t + 6f * t) * b.value) / h
                + (3f * t * t - 4f * t + 1f) * a.outTangent + (3f * t * t - 2f * t) * b.inTangent;
        }
        private static void CheckGoalResponse(GameObject prefab, Scene scene, string state, AnimationClip source,
            float speed, float reference, bool selectedIk, Frame[] baseline, JObject receipt)
        {
            var facts = new JArray(); receipt["one_mm_goal_response"] = facts;
            bool selectedPassed = true;
            foreach (bool ik in new[] { false, true })
            for (int side = 0; side < 2; side++)
            foreach (float requested in new[] { -.001f, .001f })
            {
                int phaseIndex = Enumerable.Range(0, Keys).OrderBy(i => side == 0 ? baseline[i].leftSole : baseline[i].rightSole).First();
                float phase = phaseIndex / (float)Keys;
                var offsets = Enumerable.Range(0, Keys + 1).Select(i => side == 0 ? new[] { requested, 0f } : new[] { 0f, requested }).ToArray();
                var probe = GoalClone(source, baseline, offsets, "DIAGNOSTIC_GOAL_RESPONSE");
                try
                {
                    using var before = new Rig(prefab, scene, state, source, speed, reference, ik);
                    using var after = new Rig(prefab, scene, state, probe, speed, reference, ik);
                    before.Pose(phase); after.Pose(phase);
                    var a = before.Read(phase); var b = after.Read(phase);
                    int ankle = side == 0 ? 7 : 11;
                    Vector3 delta = b.joints[ankle] - a.joints[ankle];
                    float horizontal = new Vector2(delta.x, delta.z).magnitude;
                    float upper = Enumerable.Range(0, 5).Max(i => Vector3.Distance(a.joints[i], b.joints[i]));
                    bool pass = Mathf.Abs(delta.y - requested) <= .00025f && horizontal <= .00025f && upper <= .00025f;
                    facts.Add(new JObject { ["foot_ik"] = ik, ["selected_for_candidate"] = ik == selectedIk, ["side"] = side == 0 ? "left" : "right",
                        ["phase"] = phase, ["requested_world_y_m"] = requested, ["ankle_delta_xyz_m"] = new JArray(delta.x, delta.y, delta.z),
                        ["upper_body_max_delta_m"] = upper, ["response_pass"] = pass });
                    if (ik == selectedIk && !pass) selectedPassed = false;
                }
                finally { Object.DestroyImmediate(probe); }
            }
            Require(selectedPassed, "Pinned foot-goal translation does not follow a 1mm Y request within 0.25mm, or moves upper body; do not assume goal units/semantics.");
        }
        private static Frame[] Sample(GameObject prefab, Scene scene, string state, AnimationClip clip, float speed, float reference, bool ik, string proofStem = null)
        {
            using var rig = new Rig(prefab, scene, state, clip, speed, reference, ik);
            var frames = new Frame[Dense + 1]; rig.Warm(Dense);
            for (int i = 0; i <= Dense; i++)
            {
                float phase = i / (float)Dense; frames[i] = rig.Read(phase);
                // Same eight equal phases for both variants, all authored tiers. Also retained on a numeric failure.
                if (proofStem != null && i < Dense && (i % (Dense / 8) == 0 || i == 130))
                    for (int tier = 0; tier < rig.skins.Count; tier++)
                        WriteObj(proofStem + "-tier" + tier + "-phase" + i.ToString("D3") + ".obj.gz", rig.root.transform, rig.skins[tier]);
                if (i < Dense) rig.Step(Dense);
            }
            return frames;
        }
        private static void Validate(Frame[] a,Frame[] b,float seconds,JObject report)
        {
            Require(Mathf.Abs(a.Last().nativeNormalizedTime-a[0].nativeNormalizedTime-1)<.001f&&Mathf.Abs(b.Last().nativeNormalizedTime-b[0].nativeNormalizedTime-1)<.001f,"Native sampler did not advance one full loop.");
            foreach(int j in new[]{7,11})Require(a.Max(f=>f.joints[j].z)-a.Min(f=>f.joints[j].z)>.3f,"Expected advancing walking ankles.");
            var feet=new[]{7,8,11,12};float upper=MaxJoints(a,b,Enumerable.Range(0,5));float xz=a.Zip(b,(x,y)=>feet.Max(j=>Vector2.Distance(new Vector2(x.joints[j].x,x.joints[j].z),new Vector2(y.joints[j].x,y.joints[j].z)))).Max();
            float rotation=a.Zip(b,(x,y)=>feet.Max(j=>Quaternion.Angle(x.rotations[j],y.rotations[j]))).Max();float min=b.Min(f=>Mathf.Min(f.leftSole,f.rightSole));
            float hover=0,flight=0,slip=0;float dt=seconds/Dense;
            for(int i=0;i<Dense;i++)for(int side=0;side<2;side++)
            {
                float original=side==0?a[i].leftSole:a[i].rightSole,now=side==0?b[i].leftSole:b[i].rightSole;
                if(original<=0)hover=Mathf.Max(hover,now);if(original>=.03f)flight=Mathf.Max(flight,Mathf.Abs(now-original));
                if(original<.03f){int j=side==0?7:11,prev=(i+Dense-1)%Dense,next=(i+1)%Dense;slip=Mathf.Max(slip,Mathf.Abs((b[next].joints[j].z-b[prev].joints[j].z-a[next].joints[j].z+a[prev].joints[j].z)/(2*dt)));}
            }
            float loop=feet.Max(j=>Vector3.Distance(b[0].joints[j],b.Last().joints[j]));
            float Mismatch(Frame[] f)=>feet.Max(j=>Vector3.Distance((f[1].joints[j]-f[0].joints[j])/dt,(f.Last().joints[j]-f[Dense-1].joints[j])/dt));
            float loopV=Mismatch(b),baseLoopV=Mismatch(a);float knee=MaxJoints(a,b,new[]{6,10});
            report["dense_phases"]=b.Length;report["minimum_sole_m"]=min;report["upper_body_delta_m"]=upper;report["foot_toe_xz_delta_m"]=xz;report["foot_rotation_delta_degrees"]=rotation;report["knee_delta_m"]=knee;report["corrected_support_max_gap_m"]=hover;report["unchanged_swing_max_error_m"]=flight;report["stance_ankle_z_velocity_change_mps"]=slip;report["loop_position_delta_m"]=loop;report["loop_velocity_mismatch_mps"]=loopV;report["source_loop_velocity_mismatch_mps"]=baseLoopV;
            Require(upper<=.0001f&&xz<=.002f&&rotation<=1f,"Foot repair changed body/horizontal articulation/rotation beyond scope.");
            Require(knee<=.015f,"Small foot repair requires more than15mm knee displacement; inspect anatomy.");
            Require(min>=-.002f&&hover<=.004f&&flight<=.002f,"Full-cycle contact/hover/swing gate failed.");
            float sourceLoop=feet.Max(j=>Vector3.Distance(a[0].joints[j],a.Last().joints[j]));
            float seamVectorRegression=Enumerable.Range(0,Tracked.Length).Max(j=>((b.Last().joints[j]-b[0].joints[j])-(a.Last().joints[j]-a[0].joints[j])).magnitude);
            report["source_loop_position_delta_m"]=sourceLoop;
            report["loop_seam_vector_regression_m"]=seamVectorRegression;
            report["absolute_one_mm_loop_gate_pass"]=loop<=.001f;
            report["inherited_loop_limitation"]="The source foot seam remains. This acceptance only establishes floor-contact improvement without source-loop/sliding regression; it does not accept seamless gait.";
            Require(slip<=.2f,"Contact correction worsened source stance velocity by more than0.2m/s.");
            Require(loop<=sourceLoop+.0001f&&seamVectorRegression<=.0001f,"Contact correction changed the inherited seam vector by more than0.1mm.");
            Require(loopV<=baseLoopV+.1f,"Contact correction worsened the original endpoint velocity mismatch beyond0.1m/s.");
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
