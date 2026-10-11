// Preserve the current Sprint FK gait before correcting measured foot penetration.
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
    public static class SprintDirectLegCandidate
    {
        private const int Keys = 480, Dense = 960;
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/SprintDirectLegV1/";
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
            public float[] muscleValues;
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
                controller = new AnimatorController { name = "UNSAVED_SPRINT_DIRECT_LEG_V1" };
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
                    frame.bodyRotation = pose.bodyRotation; frame.humanScale = animator.humanScale; frame.muscleValues=(float[])pose.muscles.Clone();
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
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/character-candidates/motion-review-20261006/native-sprint-direct-leg-v1"));
            Directory.CreateDirectory(output);
            var roots = new[] { "Assets/Resources/Characters", "Assets/Art/animations", "Assets/Art/characters/authored" };
            var hashes = roots.SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                .Where(p => !p.Replace('\\', '/').StartsWith(Stage, StringComparison.Ordinal) && p != Stage.TrimEnd('/') + ".meta").ToDictionary(p => p, Hash);
            var receipt = new JObject { ["state"] = "sampling-sprint-v1", ["all_pass"] = false, ["purpose"] = "Existing Sprint Original180 and1x cadence. Offline direct two-bone correction preserves source segment lengths, bend direction, ankle XZ and foot rotation. Author only measured leg-muscle deltas; native replay must reproduce the direct geometry with Foot IK off. Root/body curves and upper-body muscles remain unchanged. Existing15-degree anatomy gate retained; no production assignment.", ["cases"] = new JArray() };
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
                receipt["state"] = passed && drift.Length == 0 ? "native-numeric-pass-not-promoted" : "failed";
                File.WriteAllText(Path.Combine(output, "candidate-receipt.json"), receipt.ToString());
                Require(drift.Length == 0, "Protected production/candidate files changed; inspect receipt.");
            }
            Require(passed, "At least one isolated candidate failed. No production motion/controller/registry was assigned; inspect candidate-receipt.json.");
            Debug.Log("CTLIFE [sprint-goals] Native contact/loop gates passed for isolated clips only; rendered/device review still required.");
        }

        private static int[] LegMuscles() => Enumerable.Range(0, HumanTrait.MuscleCount).Where(i =>
        {
            string n=HumanTrait.MuscleName[i]; return (n.StartsWith("Left ")||n.StartsWith("Right ")) &&
                (n.Contains(" Leg ")||n.Contains(" Foot ")||n.Contains(" Toes "));
        }).ToArray();
        private static void Build(string character, GameObject prefab, string output, JObject receipt)
        {
            var source=AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
            string stem=(character=="snow"?"Snow":"Rain")+"_SprintDirectLeg";
            var scene=EditorSceneManager.NewPreviewScene(); AnimationClip control=null,candidate=null;
            try
            {
                ValidateDirectSource(character,prefab,scene,source,output,receipt);
                int[] muscles=LegMuscles(); Require(muscles.Length==16,"Expected eight measured leg muscles per side.");
                receipt["leg_muscles"]=new JArray(muscles.Select(i=>HumanTrait.MuscleName[i]));
                receipt["source_rate"]=1; receipt["world_speed_mps"]=4.5; receipt["foot_ik"]=false;
                receipt["source_sha256"]=Hash(SourcePath); receipt["source_orientation"]="Original180 unchanged";
                var original=AuthorDirect(prefab,scene,source,false,null,receipt,"original");
                var target=AuthorDirect(prefab,scene,source,true,Path.Combine(output,stem+"-direct-target"),receipt,"direct");
                WriteFrames(Path.Combine(output,stem+"-author-original.json"),original,source.length);
                WriteFrames(Path.Combine(output,stem+"-author-target.json"),target,source.length);
                var anatomy=new JObject();receipt["intended_direct_geometry_anatomy"]=anatomy;ValidateLegAnatomy(original,target,anatomy,false);
                // Explicit zero-delta curve composition/native playback control precedes the correction.
                control=MuscleClone(source,original,original,muscles,stem+"_ZeroDeltaControl");
                var before=Sample(prefab,scene,"loco_run",source,4.5f,4.5f,false,Path.Combine(output,stem+"-production"));
                var zero=Sample(prefab,scene,"loco_run",control,4.5f,4.5f,false);
                float zeroError=MaxJoints(before,zero,Enumerable.Range(0,Tracked.Length));
                receipt["zero_delta_native_joint_error_m"]=zeroError; Require(zeroError<=.0001f,"Zero-delta original curve replay changed source.");
                candidate=MuscleClone(source,original,target,muscles,stem+"_Candidate");
                var replayAuthor=AuthorDirect(prefab,scene,candidate,false,null,receipt,"replay");
                WriteFrames(Path.Combine(output,stem+"-author-replay.json"),replayAuthor,source.length);
                var after=Sample(prefab,scene,"loco_run",candidate,4.5f,4.5f,false,Path.Combine(output,stem+"-candidate"));
                WriteFrames(Path.Combine(output,stem+"-dense-production.json"),before,source.length);
                WriteFrames(Path.Combine(output,stem+"-dense-candidate.json"),after,source.length);
                float replay=MaxJoints(target,replayAuthor,Enumerable.Range(0,Tracked.Length));
                float replayRotation=target.Zip(replayAuthor,(a,b)=>new[]{7,8,11,12}.Max(j=>Quaternion.Angle(a.rotations[j],b.rotations[j]))).Max();
                float replaySole=target.Zip(replayAuthor,(a,b)=>Mathf.Max(Mathf.Abs(a.leftSole-b.leftSole),Mathf.Abs(a.rightSole-b.rightSole))).Max();
                receipt["target_to_native_replay_joint_error_m"]=replay; receipt["target_to_native_replay_sole_error_m"]=replaySole;
                receipt["target_to_native_replay_foot_rotation_error_degrees"]=replayRotation;
                Require(replay<=.002f&&replaySole<=.002f&&replayRotation<=1f,"Measured muscle deltas do not reproduce direct FK target; no acceptance from target geometry alone.");
                Validate(before,after,before,source.length,receipt); ValidateLegAnatomy(before,after,receipt); ValidateCurves(source,candidate,receipt);
                Directory.CreateDirectory(Stage); string path=Stage+stem+"_Candidate.anim"; SaveClip(candidate,path); candidate=null;
                receipt["asset"]=path; receipt["sha256"]=Hash(path);
                receipt["scope"]="Unassigned offline authored leg-muscle correction, Foot IK off, original rate/root/heading. Continuous game-camera/side feel and stance cadence remain unaccepted.";
            }
            finally { if(control!=null)Object.DestroyImmediate(control); if(candidate!=null)Object.DestroyImmediate(candidate); EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static Frame[] AuthorDirect(GameObject prefab,Scene scene,AnimationClip clip,bool correct,string proof,JObject receipt,string label)
        {
            using var rig=new Rig(prefab,scene,"loco_run",clip,4.5f,4.5f,false);rig.Warm(Keys);var frames=new Frame[Keys+1];
            float targetError=0,lengthError=0,rotationError=0,maximumLift=0,nonlegDelta=0;
            int[] leg=LegMuscles();
            for(int i=0;i<=Keys;i++)
            {
                float phase=i/(float)Keys; var before=rig.Read(phase,true);
                if(correct)
                {
                    for(int side=0;side<2;side++)
                    {
                        int index=side==0?5:9; float lift=Mathf.Max(0,.0005f-(side==0?before.leftSole:before.rightSole));
                        maximumLift=Mathf.Max(maximumLift,lift); Require(lift<=.05f,"Actual source correction exceeds50mm.");
                        var hip=rig.animator.GetBoneTransform(Tracked[index]);var knee=rig.animator.GetBoneTransform(Tracked[index+1]);var foot=rig.animator.GetBoneTransform(Tracked[index+2]);
                        Vector3 goal=foot.position+Vector3.up*lift; Quaternion rotation=foot.rotation;
                        float upper=Vector3.Distance(hip.position,knee.position),lower=Vector3.Distance(knee.position,foot.position);
                        if(lift>0) Solve(hip,knee,foot,goal);
                        targetError=Mathf.Max(targetError,Vector3.Distance(foot.position,goal));rotationError=Mathf.Max(rotationError,Quaternion.Angle(foot.rotation,rotation));
                        lengthError=Mathf.Max(lengthError,Mathf.Abs(upper-Vector3.Distance(hip.position,knee.position)),Mathf.Abs(lower-Vector3.Distance(knee.position,foot.position)));
                    }
                }
                frames[i]=rig.Read(phase,true);
                if(correct)for(int m=0;m<HumanTrait.MuscleCount;m++)if(!leg.Contains(m))nonlegDelta=Mathf.Max(nonlegDelta,Mathf.Abs(frames[i].muscleValues[m]-before.muscleValues[m]));
                if(proof!=null&&i<Keys&&i%(Keys/8)==0)for(int tier=0;tier<rig.skins.Count;tier++)WriteObj(proof+"-tier"+tier+"-phase"+i.ToString("D3")+".obj.gz",rig.root.transform,rig.skins[tier]);
                if(i<Keys)rig.Step(Keys);
            }
            receipt[label+"_direct_geometry"]=new JObject{["ankle_target_error_m"]=targetError,["segment_length_error_m"]=lengthError,["foot_rotation_error_degrees"]=rotationError,["maximum_lift_m"]=maximumLift,["unwritten_nonleg_muscle_delta"]=nonlegDelta};
            Require(targetError<=.00001f&&lengthError<=.00001f&&rotationError<=.1f,"Direct geometric solve changed foot goal, segment lengths or foot rotation.");
            return frames;
        }
        private static void Solve(Transform hip,Transform knee,Transform foot,Vector3 target)
        {
            Vector3 h=hip.position,k=knee.position,a=foot.position;Quaternion footRotation=foot.rotation;
            float upper=Vector3.Distance(h,k),lower=Vector3.Distance(k,a),distance=Vector3.Distance(h,target);
            Require(distance>Mathf.Abs(upper-lower)+.000001f && distance<upper+lower+.000001f,"Fixed-length target is unreachable; no bone stretching allowed.");
            Vector3 oldAxis=(a-h).normalized,axis=(target-h).normalized;
            Vector3 bend=k-h-oldAxis*Vector3.Dot(k-h,oldAxis);
            Require(bend.sqrMagnitude>1e-12f,"Source leg is exactly straight and has no measured bend direction; do not invent a knee pole.");
            bend=(Quaternion.FromToRotation(oldAxis,axis)*bend).normalized;
            float along=(upper*upper-lower*lower+distance*distance)/(2f*distance);
            float height=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            Vector3 desiredKnee=h+axis*along+bend*height;
            hip.rotation=Quaternion.FromToRotation(k-h,desiredKnee-h)*hip.rotation;
            knee.rotation=Quaternion.FromToRotation(foot.position-knee.position,target-knee.position)*knee.rotation;
            foot.rotation=footRotation;
        }
        private static AnimationClip MuscleClone(AnimationClip source,Frame[] original,Frame[] target,int[] muscles,string name)
        {
            var clip=Object.Instantiate(source);clip.name=name;
            try
            {
                foreach(int m in muscles)
                {
                    var binding=Binding(source,HumanTrait.MuscleName[m]);var curve=AnimationUtility.GetEditorCurve(source,binding);
                    var delta=Enumerable.Range(0,Keys+1).Select(i=>target[i].muscleValues[m]-original[i].muscleValues[m]).ToArray();
                    AnimationUtility.SetEditorCurve(clip,binding,AddDelta(curve,PeriodicCurve(delta,source.length)));
                }
                Require(clip.humanMotion&&clip.isLooping,"Muscle correction lost Humanoid loop settings.");return clip;
            }
            catch { Object.DestroyImmediate(clip);throw; }
        }
        private static EditorCurveBinding Binding(AnimationClip clip,string property)
        {
            var bindings=AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Animator)&&b.propertyName==property).ToArray();Require(bindings.Length==1,"Expected unique muscle binding "+property);return bindings[0];
        }
        private static void ValidateDirectSource(string character,GameObject prefab,Scene scene,AnimationClip source,string output,JObject receipt)
        {
            var archived=JObject.Parse(File.ReadAllText(Path.Combine(output,"../native-sprint-source-diagnostic/"+character+"-direct-noik.json")));
            using var rig=new Rig(prefab,scene,"loco_run",source,4.5f,4.5f,false);float joints=0,soles=0;
            for(int i=0;i<=120;i++)
            {
                float phase=i/120f;rig.animator.Play("loco_run",0,2f+phase);rig.animator.Update(0);var actual=rig.Read(phase);var expected=archived["frames"][i];
                for(int j=0;j<Tracked.Length;j++){var p=expected["joints"][j].Values<float>().ToArray();joints=Mathf.Max(joints,Vector3.Distance(actual.joints[j],new Vector3(p[0],p[1],p[2])));}
                soles=Mathf.Max(soles,Mathf.Abs(actual.leftSole-(float)expected["leftSole"]),Mathf.Abs(actual.rightSole-(float)expected["rightSole"]));
            }
            receipt["independent_direct_source_joint_error_m"]=joints;receipt["independent_direct_source_sole_error_m"]=soles;
            receipt["source_seam_note"]="Independent source uses exact direct phase. Candidate/full-FK controls use identical continuous native clocks, so the known original LeftHand endpoint discontinuity is never exempted from candidate-vs-control body gates.";
            Require(joints<=.002f&&soles<=.002f,"Exact direct source changed relative to independently diagnosed original.");
        }
        private static void ValidateLegAnatomy(Frame[] original,Frame[] final,JObject receipt,bool enforce=true)
        {
            float kneeChange=0,kneePosition=0,lengthChange=0,planeChange=0,maxY=0;
            for(int i=0;i<final.Length;i++)for(int side=0;side<2;side++)
            {
                int hip=side==0?5:9,knee=hip+1,ankle=hip+2;var a=original[i];var b=final[i];
                float Angle(Frame f)=>Vector3.Angle(f.joints[hip]-f.joints[knee],f.joints[ankle]-f.joints[knee]);
                float aa=Angle(a),bb=Angle(b);kneeChange=Mathf.Max(kneeChange,Mathf.Abs(aa-bb));kneePosition=Mathf.Max(kneePosition,Vector3.Distance(a.joints[knee],b.joints[knee]));
                lengthChange=Mathf.Max(lengthChange,Mathf.Abs(Vector3.Distance(a.joints[hip],a.joints[knee])-Vector3.Distance(b.joints[hip],b.joints[knee])),Mathf.Abs(Vector3.Distance(a.joints[knee],a.joints[ankle])-Vector3.Distance(b.joints[knee],b.joints[ankle])));
                if(aa<165f&&bb<165f)planeChange=Mathf.Max(planeChange,Vector3.Angle(Vector3.Cross(a.joints[hip]-a.joints[knee],a.joints[ankle]-a.joints[knee]),Vector3.Cross(b.joints[hip]-b.joints[knee],b.joints[ankle]-b.joints[knee])));
                maxY=Mathf.Max(maxY,Mathf.Abs(a.joints[ankle].y-b.joints[ankle].y));
            }
            receipt["knee_angle_change_max_degrees"]=kneeChange;receipt["knee_position_change_max_m"]=kneePosition;receipt["leg_segment_length_change_max_m"]=lengthChange;
            receipt["bent_knee_plane_change_max_degrees"]=planeChange;receipt["actual_ankle_y_change_from_production_max_m"]=maxY;
            receipt["existing_anatomy_gate_pass"]=lengthChange<=.002f&&maxY<=.05f&&kneeChange<=15f&&planeChange<=15f;
            if(enforce) Require(lengthChange<=.002f&&maxY<=.05f&&kneeChange<=15f&&planeChange<=15f,"Contact correction changed limb length/stance anatomy beyond bounded original-FK adjustment.");
        }
        private static void ValidateCurves(AnimationClip source,AnimationClip candidate,JObject receipt)
        {
            var original=AnimationUtility.GetCurveBindings(source);var altered=AnimationUtility.GetCurveBindings(candidate);Require(original.Length==altered.Length,"Binding count changed.");
            var changed=new JArray();
            foreach(var b in original)
            {
                var a=AnimationUtility.GetEditorCurve(source,b);var c=AnimationUtility.GetEditorCurve(candidate,b);
                Require(c!=null,"Candidate lost original binding.");bool same=a.keys.SequenceEqual(c.keys)&&a.preWrapMode==c.preWrapMode&&a.postWrapMode==c.postWrapMode;
                if(!same){changed.Add(b.propertyName);Require(b.type==typeof(Animator)&&LegMuscles().Select(i=>HumanTrait.MuscleName[i]).Contains(b.propertyName),"Changed source non-leg muscle/body/goal curve: "+b.propertyName);}
            }
            receipt["changed_curves"]=changed;Require(source.length==candidate.length,"Source period changed.");
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
        private static void Validate(Frame[] original, Frame[] candidate, Frame[] stanceControl, float period, JObject receipt)
        {
            ValidateTemporal(original, "dense original", receipt); ValidateTemporal(candidate, "dense candidate", receipt);
            ValidateTemporal(stanceControl, "dense no-IK control", receipt);
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
                float uncorrected = side == 0 ? stanceControl[f].leftSole : stanceControl[f].rightSole;
                if (uncorrected <= 0) supportGap = Mathf.Max(supportGap, after);
                if (uncorrected >= .008f) flightDelta = Mathf.Max(flightDelta, Mathf.Abs(after - before));
                if (uncorrected < .03f)
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
            foreach (int side in new[] { 0, 1 })
            {
                int expected = 0, close = 0;
                for (int f = 0; f < Dense; f++)
                    if ((side == 0 ? stanceControl[f].leftSole : stanceControl[f].rightSole) <= 0f)
                    {
                        expected++; float sole = side == 0 ? candidate[f].leftSole : candidate[f].rightSole;
                        if (sole >= -.002f && sole <= .008f) close++;
                    }
                Require(expected >= 4 && close == expected, "Measured source stance must exist and each corrected stance sample must contact within -2/+8mm.");
                receipt[(side == 0 ? "left" : "right") + "_expected_stance_phases"] = expected;
                receipt[(side == 0 ? "left" : "right") + "_corrected_contact_phases"] = close;
            }
            int clearFlight = 0, lostFlight = 0;
            for (int f = 0; f < Dense; f++)
                if (Mathf.Min(stanceControl[f].leftSole, stanceControl[f].rightSole) >= .03f)
                {
                    clearFlight++;
                    if (Mathf.Min(candidate[f].leftSole, candidate[f].rightSole) < Mathf.Min(original[f].leftSole, original[f].rightSole) - .002f) lostFlight++;
                }
            receipt["uncorrected_clear_flight_phases"] = clearFlight; receipt["lost_clear_flight_phases"] = lostFlight;
            Require(flightDelta <= .002f && lostFlight == 0, "Correction changed no-IK-defined swing/flight instead of only contact.");
            Require(slipDelta <= .4f, "Correction worsens support ankle horizontal velocity by more than 0.4m/s.");
            Require(loopPosition <= .001f && loopVelocity <= originalLoopVelocity + .1f, "Loop closure/velocity is worse than the original.");
        }
        private static void ValidateTemporal(Frame[] frames, string label, JObject receipt)
        {
            int count = frames.Length - 1;
            float temporalSpan = frames[count].nativeNormalizedTime - frames[0].nativeNormalizedTime;
            Require(Mathf.Abs(temporalSpan - 1f) < .001f, label + " did not traverse a full native source loop.");
            foreach (int foot in new[] { 7, 11 })
            {
                float range = frames.Max(f => f.joints[foot].z) - frames.Min(f => f.joints[foot].z);
                Require(range > 1f, label + " ankle does not cover the matrix-proven >1m fore/aft range.");
            }
            foreach (int side in new[] { 0, 1 })
            {
                float low = frames.Min(f => side == 0 ? f.leftSole : f.rightSole), high = frames.Max(f => side == 0 ? f.leftSole : f.rightSole);
                Require(low < .1f && high > .25f && high - low > .25f, label + " must contain measured Sprint ground-near/swing range (IK may hover up to 0.1m before correction; final contact gate remains strict).");
            }
            int flight = frames.Take(count).Count(f => Mathf.Min(f.leftSole, f.rightSole) > .03f);
            Require(flight > count / 3 && flight < count * 9 / 10, label + " flight/support distribution is degenerate.");
            receipt[label + "_temporal"] = new JObject { ["native_start"] = frames[0].nativeNormalizedTime,
                ["native_end"] = frames[count].nativeNormalizedTime, ["both_feet_above3cm"] = flight, ["samples"] = frames.Length };
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
                bodyRotation = new[] { f.bodyRotation.x, f.bodyRotation.y, f.bodyRotation.z, f.bodyRotation.w }, f.humanScale, f.muscleValues })
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
