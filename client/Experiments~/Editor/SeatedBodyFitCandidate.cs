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
    public static class SeatedBodyFitCandidate
    {
        private const int Keys = 120, Dense = 240;
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/SeatedBodyFitV1/";
        private static string SourcePath;
        private static string Action;
        private static readonly string[] PatchNames = { "Foot.L", "Toes.L", "Foot.R", "Toes.R" };
        private static Dictionary<string, int[][]> patchIndices;
        private static readonly HumanBodyBones[] Tracked = {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm };

        private sealed class Frame
        {
            public float phase, leftSole, rightSole, nativeNormalizedTime, animatorSpeed, stateSpeed, speedMultiplier;
            public float[] tierLeft, tierRight, tierBearing;
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
            public int[] left, right, bearing;
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
                controller = new AnimatorController { name = "UNSAVED_SEATED_GOALS_V1" };
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
                    var bearingBones=new[]{HumanBodyBones.Hips,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg}.Select(animator.GetBoneTransform).ToHashSet();
                    var bearing=Enumerable.Range(0,weights.Length).Where(i=>{var w=weights[i];float W(int b,float v)=>v>0&&bearingBones.Contains(renderer.bones[b])?v:0;
                        return W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3)>.5f;}).ToArray();
                    var skin = new Skin { renderer = renderer, baked = new Mesh(), left = Select("L"), right = Select("R"), patches = patches,bearing=bearing };
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
            public Frame Read(float phase, bool muscles = false)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                Require(Mathf.Abs(info.normalizedTime - (2f + phase)) < .001f, "Sample label differs from actual native state time.");
                Require(Mathf.Abs(info.speed * info.speedMultiplier * animator.speed - 1f) < .00001f, "Contact sampler native rate differs from 1x.");
                Require(root.transform.position.sqrMagnitude < 1e-10f, "Disabled root motion still moved the root.");
                var frame = new Frame { phase = phase, nativeNormalizedTime = info.normalizedTime, animatorSpeed = animator.speed,
                    stateSpeed = info.speed, speedMultiplier = info.speedMultiplier, leftSole = float.PositiveInfinity, rightSole = float.PositiveInfinity,
                    tierLeft = new float[skins.Count], tierRight = new float[skins.Count], tierBearing = new float[skins.Count],
                    tierNames = skins.Select(s => s.renderer.name).ToArray(), patchMeans = new Vector3[skins.Count][],
                    joints = Tracked.Select(b => root.transform.InverseTransformPoint(animator.GetBoneTransform(b).position)).ToArray(),
                    rotations = Tracked.Select(b => Quaternion.Inverse(root.transform.rotation) * animator.GetBoneTransform(b).rotation).ToArray() };
                for (int tier = 0; tier < skins.Count; tier++)
                {
                    var skin = skins[tier]; skin.renderer.BakeMesh(skin.baked, false); skin.baked.GetVertices(skin.vertices);
                    var matrix = root.transform.worldToLocalMatrix * skin.renderer.localToWorldMatrix;
                    frame.tierLeft[tier] = skin.left.Min(i => matrix.MultiplyPoint3x4(skin.vertices[i]).y);
                    frame.tierRight[tier] = skin.right.Min(i => matrix.MultiplyPoint3x4(skin.vertices[i]).y);
                    var bearing=skin.bearing.Select(i=>matrix.MultiplyPoint3x4(skin.vertices[i])).Where(v=>Mathf.Abs(v.x)<=.145f&&v.z>=-.42f&&v.z<=-.15f&&v.y>.25f&&v.y<.7f).ToArray();
                    Require(bearing.Length>5,"No geometric support witness inside declared chair bearing patch.");frame.tierBearing[tier]=bearing.Min(v=>v.y);
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

        public static void BuildCandidates()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../ct-life-evidence/art/character-candidates/motion-review-20261006/native-seated-body-fit-v1"));
            Directory.CreateDirectory(output);
            var roots = new[] { "Assets/Resources/Characters", "Assets/Art/animations", "Assets/Art/characters/authored" };
            var hashes = roots.SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                .Where(p => !p.Replace('\\', '/').StartsWith(Stage, StringComparison.Ordinal) && p != Stage.TrimEnd('/') + ".meta").ToDictionary(p => p, Hash);
            var receipt = new JObject { ["state"] = "sampling-seated-v1", ["all_pass"] = false, ["purpose"] = "Per-look seated foot goals: restore original ankle XZ, place all-LOD soles on floor, preserve root/hips/hands. Explicit social seated body fit to shared0.450m chair/0.452m underside witness; GameObject roots and grounded feet stay fixed. Typing remains separate.", ["cases"] = new JArray() };
            bool passed = true;
            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                patchIndices = new Dictionary<string, int[][]>();
                foreach (string character in new[] { "snow", "rain" })
                {
                    string matrixPath = Path.Combine(output, "../native-rest-contact-matrix/" + character + "-original-y-footik.json");
                    var matrix = JObject.Parse(File.ReadAllText(matrixPath));
                    foreach (var skin in matrix["rest_surfaces"])
                        patchIndices.Add((string)skin["skin"], PatchNames.Select(name => skin["patches"].Single(p => (string)p["name"] == name)["fixed_vertex_indices"].Values<int>().ToArray()).ToArray());
                    foreach (string action in new[] { "act_sit" })
                    {
                        Action=action;SourcePath="Assets/Art/characters/authored/MotionReview20261006/SeatedContactGoalsV1/"+(character=="snow"?"Snow":"Rain")+"_act_sit_SeatedContact_Goals_Candidate.anim";
                        Require(Hash(SourcePath)==(character=="snow"?"c1c9a1a09fa67e057917d65a1d995b6e87feba9a5c1c76399f40859f9afaf79a":"79c6172bfd52f759160f33d2de7b3368e10838b04fbc5e7a11742e4abe80da39"),"Accepted social foot candidate changed.");
                        var result = new JObject { ["character"] = character, ["action"] = action, ["all_pass"] = false };
                        ((JArray)receipt["cases"]).Add(result);
                        try
                        {
                            var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + character));
                            Build(character, entry.prefab, output, result); result["all_pass"] = true;
                        }
                        catch (Exception error) { passed = false; result["failure"] = error.ToString(); Debug.LogError("CTLIFE [seated-goals] " + character + "/" + action + ": " + error); }
                    }
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
                receipt["state"] = passed && drift.Length == 0 ? "native-numeric-pass-not-promoted" : "failed";
                File.WriteAllText(Path.Combine(output, "candidate-receipt.json"), receipt.ToString());
                Require(drift.Length == 0, "Protected production/candidate files changed; inspect receipt.");
            }
            Require(passed, "At least one isolated candidate failed. No production motion/controller/registry was assigned; inspect candidate-receipt.json.");
            Debug.Log("CTLIFE [rest-jog-goals] Native contact/loop gates passed for isolated clips only; rendered/device review still required.");
        }

        private static void Build(string character,GameObject prefab,string output,JObject receipt)
        {
            var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath);var scene=EditorSceneManager.NewPreviewScene();AnimationClip candidate=null;
            string stem=character=="snow"?"Snow_SocialBodyFit":"Rain_SocialBodyFit";
            try
            {
                var baseline=AuthorSamples(prefab,scene,source);WriteFrames(Path.Combine(output,stem+"-author-source.json"),baseline,source.length);
                var evidence=JObject.Parse(File.ReadAllText(Path.Combine(output,"../native-seated-goals-v1/"+(character=="snow"?"Snow":"Rain")+"_act_sit_SeatedContact_Goals-dense-candidate.json")));
                float sourceError=0;
                for(int i=0;i<=Keys;i++)for(int j=0;j<Tracked.Length;j++)
                { var q=evidence["frames"][i*2]["joints"][j].Values<float>().ToArray();sourceError=Mathf.Max(sourceError,Vector3.Distance(baseline[i].joints[j],new Vector3(q[0],q[1],q[2]))); }
                receipt["accepted_source_joint_error_m"]=sourceError;Require(sourceError<=.002f,"Accepted source native poses changed.");
                var responses=new JArray();receipt["body_translation_response"]=responses;
                foreach(float delta in new[]{-.001f,.001f})
                {
                    var probe=BodyShift(source,baseline[0].humanScale,delta);
                    try
                    {
                        using var rig=new Rig(prefab,scene,Action,probe,4.5f,4.5f,true);rig.Pose(.25f);var actual=rig.Read(.25f);
                        var difference=actual.joints[0]-baseline[Keys/4].joints[0];float error=Vector3.Distance(difference,Vector3.up*delta);
                        responses.Add(new JObject{["requested_world_y_m"]=delta,["hip_delta_xyz_m"]=new JArray(difference.x,difference.y,difference.z),["error_m"]=error});
                        Require(error<=.00025f,"RootT units/body response are not the assumed world-Y translation.");
                    }
                    finally { Object.DestroyImmediate(probe); }
                }
                const float target=.452f;float appliedBodyDelta=0;float bodyDelta=target-baseline.Min(f=>f.tierBearing.Min());var iterations=new JArray();receipt["fit_iterations"]=iterations;
                Require(bodyDelta>=-.08f&&bodyDelta<=.02f,"Body fit differs substantially from measured shared-chair witnesses.");
                for(int iteration=0;iteration<3;iteration++)
                {
                    if(candidate!=null)Object.DestroyImmediate(candidate);candidate=BodyShift(source,baseline[0].humanScale,bodyDelta);appliedBodyDelta=bodyDelta;
                    for(int refinement=0;refinement<3;refinement++)
                    {
                        var shifted=AuthorSamples(prefab,scene,candidate);var offsets=new Vector3[Keys+1][];float error=0;
                        for(int i=0;i<=Keys;i++)
                        { offsets[i]=new[]{baseline[i].joints[7]-shifted[i].joints[7],baseline[i].joints[11]-shifted[i].joints[11]};error=Mathf.Max(error,offsets[i].Max(v=>v.magnitude)); }
                        if(error<.00025f)break;
                        Require(error<.09f,"Planted foot response too large for measured seated body fit.");
                        var revised=GoalClone(candidate,shifted,offsets,stem+"_Candidate");Object.DestroyImmediate(candidate);candidate=revised;
                    }
                    var fitted=AuthorSamples(prefab,scene,candidate);float low=fitted.Min(f=>f.tierBearing.Min()),high=fitted.Max(f=>f.tierBearing.Max());
                    iterations.Add(new JObject{["iteration"]=iteration,["body_y_delta_m"]=bodyDelta,["bearing_min_m"]=low,["bearing_max_m"]=high});
                    if(Mathf.Abs(low-target)<.001f)break;
                    bodyDelta+=target-low;Require(bodyDelta>=-.08f&&bodyDelta<=.02f,"Measured body fit left declared envelope.");
                }
                bodyDelta=appliedBodyDelta;
                var original=Sample(prefab,scene,Action,source,4.5f,4.5f,true,Path.Combine(output,stem+"-original"));
                var final=Sample(prefab,scene,Action,candidate,4.5f,4.5f,true,Path.Combine(output,stem+"-candidate"));
                WriteFrames(Path.Combine(output,stem+"-dense-original.json"),original,source.length);WriteFrames(Path.Combine(output,stem+"-dense-candidate.json"),final,source.length);
                float feet=MaxJoints(original,final,new[]{7,8,11,12});float body=original.Zip(final,(a,b)=>Enumerable.Range(0,5).Max(i=>Vector3.Distance(a.joints[i]+Vector3.up*bodyDelta,b.joints[i]))).Max();
                float footRotation=original.Zip(final,(a,b)=>new[]{7,8,11,12}.Max(i=>Quaternion.Angle(a.rotations[i],b.rotations[i]))).Max();
                float minSole=final.Min(f=>Mathf.Min(f.leftSole,f.rightSole)),maxSole=final.Max(f=>f.tierLeft.Concat(f.tierRight).Max());
                float bearingLow=final.Min(f=>f.tierBearing.Min()),bearingHigh=final.Max(f=>f.tierBearing.Max());
                receipt["explicit_seated_body_y_delta_m"]=bodyDelta;receipt["body_translation_error_m"]=body;receipt["fixed_feet_max_delta_m"]=feet;receipt["foot_rotation_delta_degrees"]=footRotation;
                receipt["sole_range_m"]=new JArray(minSole,maxSole);receipt["bearing_range_m"]=new JArray(bearingLow,bearingHigh);receipt["chair_seat_plane_m"]=.450f;
                Require(feet<=.002f&&body<=.002f&&footRotation<=1f,"Body fit moved fixed feet or changed original translated body pose.");
                Require(minSole>=-.002f&&maxSole<=.008f,"Body fit lost ground contact.");
                Require(Mathf.Abs(bearingLow-target)<=.0015f&&bearingLow>=.450f&&bearingHigh<=.460f,"Candidate support witness penetrates/floats above declared shared chair plane.");
                float Knee(Frame f,int h,int k,int a)=>Vector3.Angle(f.joints[h]-f.joints[k],f.joints[a]-f.joints[k]);
                var angles=final.SelectMany(f=>new[]{Knee(f,5,6,7),Knee(f,9,10,11)}).ToArray();receipt["knee_angle_range_degrees"]=new JArray(angles.Min(),angles.Max());
                Require(angles.Min()>45f&&angles.Max()<160f,"Body fit produces overfolded/near-straight seated knee.");
                float dt=source.length/Dense;
                float Loop(Frame[] fs)=>new[]{7,8,11,12}.Max(i=>Vector3.Distance((fs[1].joints[i]-fs[0].joints[i])/dt,(fs[Dense].joints[i]-fs[Dense-1].joints[i])/dt));
                Require(new[]{7,8,11,12}.Max(i=>Vector3.Distance(final[0].joints[i],final[Dense].joints[i]))<.001f&&Loop(final)<=Loop(original)+.1f,"Body fit degrades foot loop.");
                candidate.name=stem+"_Candidate";Directory.CreateDirectory(Stage);SaveClip(candidate,Stage+candidate.name+".anim");candidate=null;
                receipt["scope"]="Explicit per-look seated body translation authored into clip, fixed root/feet. Bearing vertex witnesses are not full chair collision/pressure proof; world triangle/render review required.";
            }
            finally { if(candidate!=null)Object.DestroyImmediate(candidate);EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static Frame[] AuthorSamples(GameObject prefab,Scene scene,AnimationClip clip)
        {
            using var rig=new Rig(prefab,scene,Action,clip,4.5f,4.5f,true);var frames=new Frame[Keys+1];rig.Warm(Keys);
            for(int i=0;i<=Keys;i++){frames[i]=rig.Read(i/(float)Keys,true);if(i<Keys)rig.Step(Keys);}return frames;
        }
        private static AnimationClip BodyShift(AnimationClip source,float humanScale,float worldY)
        {
            var clone=Object.Instantiate(source);clone.name="UNSAVED_SOCIAL_BODY_FIT";
            var bindings=AnimationUtility.GetCurveBindings(source).Where(b=>b.type==typeof(Animator)&&b.propertyName=="RootT.y").ToArray();Require(bindings.Length==1,"Expected unique Humanoid body RootT.y curve.");
            var curve=AnimationUtility.GetEditorCurve(source,bindings[0]);var keys=curve.keys;for(int i=0;i<keys.Length;i++)keys[i].value+=worldY/humanScale;
            curve.keys=keys;AnimationUtility.SetEditorCurve(clone,bindings[0],curve);return clone;
        }

        private static AnimationClip GoalClone(AnimationClip source, Frame[] frames, Vector3[][] lifts, string name)
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
                        Vector3 delta = Quaternion.Inverse(frames[i].bodyRotation) * lifts[i][side] / frames[i].humanScale;
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
            for (int axis = 0; axis < 3; axis++)
            foreach (float requested in new[] { -.001f, .001f })
            {
                int phaseIndex = Enumerable.Range(0, Keys).OrderBy(i => side == 0 ? baseline[i].leftSole : baseline[i].rightSole).First();
                float phase = phaseIndex / (float)Keys;
                var requestedVector = axis == 0 ? Vector3.right * requested : axis == 1 ? Vector3.up * requested : Vector3.forward * requested;
                var offsets = Enumerable.Range(0, Keys + 1).Select(i => side == 0 ? new[] { requestedVector, Vector3.zero } : new[] { Vector3.zero, requestedVector }).ToArray();
                var probe = GoalClone(source, baseline, offsets, "DIAGNOSTIC_GOAL_RESPONSE");
                try
                {
                    using var before = new Rig(prefab, scene, state, source, speed, reference, ik);
                    using var after = new Rig(prefab, scene, state, probe, speed, reference, ik);
                    before.Pose(phase); after.Pose(phase);
                    var a = before.Read(phase); var b = after.Read(phase);
                    int ankle = side == 0 ? 7 : 11;
                    Vector3 delta = b.joints[ankle] - a.joints[ankle];
                    float responseError = Vector3.Distance(delta, requestedVector);
                    float upper = Enumerable.Range(0, 5).Max(i => Vector3.Distance(a.joints[i], b.joints[i]));
                    bool pass = responseError <= .00025f && upper <= .00025f;
                    facts.Add(new JObject { ["foot_ik"] = ik, ["selected_for_candidate"] = ik == selectedIk, ["side"] = side == 0 ? "left" : "right",
                        ["phase"] = phase, ["requested_world_xyz_m"] = new JArray(requestedVector.x,requestedVector.y,requestedVector.z), ["ankle_delta_xyz_m"] = new JArray(delta.x, delta.y, delta.z),
                        ["upper_body_max_delta_m"] = upper, ["response_pass"] = pass });
                    if (ik == selectedIk && !pass) selectedPassed = false;
                }
                finally { Object.DestroyImmediate(probe); }
            }
            Require(selectedPassed, "Pinned foot-goal translation does not follow a 1mm XYZ request within 0.25mm, or moves upper body; do not assume goal units/semantics.");
        }
        private static Frame[] Sample(GameObject prefab, Scene scene, string state, AnimationClip clip, float speed, float reference, bool ik, string proofStem = null)
        {
            using var rig = new Rig(prefab, scene, state, clip, speed, reference, ik);
            var frames = new Frame[Dense + 1]; rig.Warm(Dense);
            for (int i = 0; i <= Dense; i++)
            {
                float phase = i / (float)Dense; frames[i] = rig.Read(phase);
                // Same eight equal phases for both variants, all authored tiers. Also retained on a numeric failure.
                if (proofStem != null && i < Dense && i % (Dense / 8) == 0)
                    for (int tier = 0; tier < rig.skins.Count; tier++)
                        WriteObj(proofStem + "-tier" + tier + "-phase" + i.ToString("D3") + ".obj.gz", rig.root.transform, rig.skins[tier]);
                if (i < Dense) rig.Step(Dense);
            }
            return frames;
        }
        private static void Validate(Frame[] original, Frame[] candidate, Frame[] production, float period, JObject receipt)
        {
            ValidateTemporal(candidate, "dense candidate", receipt);
            float upper=MaxJoints(production,candidate,Enumerable.Range(0,5));
            float ankleXz=production.Zip(candidate,(a,b)=>new[]{7,11}.Max(i=>Vector2.Distance(new Vector2(a.joints[i].x,a.joints[i].z),new Vector2(b.joints[i].x,b.joints[i].z)))).Max();
            float rotation=original.Zip(candidate,(a,b)=>new[]{7,8,11,12}.Max(i=>Quaternion.Angle(a.rotations[i],b.rotations[i]))).Max();
            float minimum=candidate.Min(f=>Mathf.Min(f.leftSole,f.rightSole));
            float maximum=candidate.Max(f=>f.tierLeft.Concat(f.tierRight).Max());
            float Knee(Frame f,int hip,int knee,int ankle)=>Vector3.Angle(f.joints[hip]-f.joints[knee],f.joints[ankle]-f.joints[knee]);
            var angles=candidate.SelectMany(f=>new[]{Knee(f,5,6,7),Knee(f,9,10,11)}).ToArray();
            var oldAngles=production.SelectMany(f=>new[]{Knee(f,5,6,7),Knee(f,9,10,11)}).ToArray();
            float kneeDisplacement=MaxJoints(production,candidate,new[]{6,10});
            float dt=period/Dense;
            float Mismatch(Frame[] fs)=>new[]{7,8,11,12}.Max(i=>Vector3.Distance((fs[1].joints[i]-fs[0].joints[i])/dt,(fs[Dense].joints[i]-fs[Dense-1].joints[i])/dt));
            float loop=new[]{7,8,11,12}.Max(i=>Vector3.Distance(candidate[0].joints[i],candidate[Dense].joints[i]));
            receipt["upper_body_vs_production_max_m"]=upper;receipt["ankle_xz_vs_production_max_m"]=ankleXz;
            receipt["foot_rotation_vs_ik_control_max_degrees"]=rotation;receipt["minimum_sole_m"]=minimum;receipt["maximum_per_foot_per_lod_sole_m"]=maximum;
            receipt["knee_angle_candidate_range_degrees"]=new JArray(angles.Min(),angles.Max());receipt["knee_angle_production_range_degrees"]=new JArray(oldAngles.Min(),oldAngles.Max());
            receipt["knee_vs_production_max_m"]=kneeDisplacement;receipt["loop_foot_position_m"]=loop;
            receipt["loop_foot_velocity_candidate_mps"]=Mismatch(candidate);receipt["loop_foot_velocity_ik_control_mps"]=Mismatch(original);
            Require(upper<=.002f&&ankleXz<=.002f,"Seated goal changed original upper body or ankle XZ by>2mm.");
            Require(rotation<=1f,"Seated target changed IK control foot orientation.");
            Require(minimum>=-.002f&&maximum<=.008f,"A foot/LOD misses floor contact during the seated cycle.");
            Require(angles.Min()>45f&&angles.Max()<160f,"Seated candidate knee is overfolded/near-straight; review anatomy.");
            Require(loop<=.001f&&Mismatch(candidate)<=Mismatch(original)+.1f,"Seated goal loop is worse than source.");
            receipt["scope"]= "Feet numeric gates only: exact actor root/hips and original ankle XZ; geometry review of knees/seat and separate hand/typing fit required.";
        }
        private static void ValidateTemporal(Frame[] frames,string label,JObject receipt)
        {
            int n=frames.Length-1;float span=frames[n].nativeNormalizedTime-frames[0].nativeNormalizedTime;
            Require(Mathf.Abs(span-1)<.001f,label+" did not advance one native loop.");
            float range=Tracked.Select((_,j)=>frames.Max(f=>Vector3.Distance(f.joints[j],frames[0].joints[j]))).Max();
            Require(range>.0001f,label+" has no changing joints.");
            receipt[label+"_temporal"]=new JObject{["native_start"]=frames[0].nativeNormalizedTime,["native_end"]=frames[n].nativeNormalizedTime,["max_joint_excursion_m"]=range};
        }
        private static void ValidateMatrix(string character,Frame[] frames,string variant,string output,JObject receipt)
        {
            string file=character+(Action=="act_type"?"-work-":"-social-")+variant+".json";
            var reference=JObject.Parse(File.ReadAllText(Path.Combine(output,"../native-seated-footik/"+file)));
            Require((string)reference["effective_clip"]==SourcePath,"Seated matrix source changed.");
            var expected=reference["frames"].ToArray();Require(expected.Length==25&&frames.Length==121,"Seated matrix phase count differs.");
            var root=reference["root_world"].Values<float>().ToArray();var offset=new Vector3(root[0],root[1],root[2]);float joints=0,soles=0;
            for(int i=0;i<25;i++)
            {
                var frame=frames[i*5];var points=(JObject)expected[i]["joints_world"];
                for(int j=0;j<Tracked.Length;j++)if(points.TryGetValue(Tracked[j].ToString(),out var token))
                { var a=token.Values<float>().ToArray();joints=Mathf.Max(joints,Vector3.Distance(frame.joints[j],new Vector3(a[0],a[1],a[2])-offset)); }
                foreach(int side in new[]{0,1})
                { float y=expected[i]["skins"].Min(k=>(float)k[side==0?"left_foot_min_y_m":"right_foot_min_y_m"]);soles=Mathf.Max(soles,Mathf.Abs(y-(side==0?frame.leftSole:frame.rightSole))); }
            }
            receipt[variant+"_matrix_joint_error_m"]=joints;receipt[variant+"_matrix_sole_error_m"]=soles;
            Require(joints<=.002f&&soles<=.002f,"Source controls do not reproduce independent seated matrix within2mm.");
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
                f.leftSole, f.rightSole, f.tierLeft, f.tierRight, f.tierBearing,
                surfaces = f.tierNames.Select((name, tier) => new { skin = name, left_min_y = f.tierLeft[tier], right_min_y = f.tierRight[tier],
                    patches = PatchNames.Select((patch, index) => new { patch, mean = f.patchMeans[tier][index] })
                        .ToDictionary(p => p.patch, p => new { mean = new[] { p.mean.x, p.mean.y, p.mean.z } }) }),
                joints = f.joints.Select(v => new[] { v.x, v.y, v.z }).ToArray(),
                rotations = f.rotations.Select(q => new[] { q.x, q.y, q.z, q.w }).ToArray(),
                bodyRotation = new[] { f.bodyRotation.x, f.bodyRotation.y, f.bodyRotation.z, f.bodyRotation.w }, f.humanScale })
        }, Formatting.Indented));
        private static void SaveClip(AnimationClip clip, string path)
        {
            var previous = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (previous == null) AssetDatabase.CreateAsset(clip, path);
            else { EditorUtility.CopySerialized(clip, previous); EditorUtility.SetDirty(previous); AssetDatabase.SaveAssetIfDirty(previous); Object.DestroyImmediate(clip); }
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
