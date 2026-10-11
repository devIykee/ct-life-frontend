// Isolated transition choreography comparison. Existing clips and controllers stay byte-identical.
// This is not a runtime routing implementation or a final-room/mobile acceptance test.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class CafeExitSecondStepCandidate
    {
        private const string Shared="Assets/Resources/Characters/AvatarController.controller";
        private const string Stage="Assets/Art/characters/authored/MotionReview20261006/CafeSeatControllerV1/";
        private static readonly HumanBodyBones[] Bones={HumanBodyBones.Hips,HumanBodyBones.Chest,HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,HumanBodyBones.RightToes,
            HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,
            HumanBodyBones.LeftIndexDistal,HumanBodyBones.RightIndexDistal};
        private sealed class Pose
        {
            public float time;public int state;public bool transition;public Vector3[] joints;public Quaternion[] rotations;
            public Vector3[][] vertices;public float[][] soles;public int callbacks;
        }
        private sealed class Skin
        {
            public SkinnedMeshRenderer renderer;public Mesh baked;public int[][] feet;
        }
        private sealed class Rig:IDisposable
        {
            public AvatarView view;public Animator animator;public CafeExitChoreographyTargets driver;public readonly List<Skin> skins=new List<Skin>();
            public Rig(CharacterRegistry.Entry entry,RuntimeAnimatorController controller,Scene scene,bool withDriver=false,Vector3[] goals=null,Quaternion[] goalRotations=null,float goalWeight=0)
            {
                try
                {
                    view=AvatarFactory.Create("Cafe controller review",entry.key.Split('|'),false);
                    SceneManager.MoveGameObjectToScene(view.gameObject,scene);view.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    animator=view.Animator;Need(animator!=null&&animator.avatar!=null&&animator.avatar.isHuman,"Expected actual world Humanoid.");
                    animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.speed=1;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;animator.Rebind();
                    if(withDriver){driver=animator.gameObject.AddComponent<CafeExitChoreographyTargets>();Need(driver!=null,"Foot authoring driver must be in World/Review outside Editor under UNITY_EDITOR.");driver.animator=animator;driver.root=view.transform;driver.positions=goals;driver.rotations=goalRotations;driver.weight=goalWeight;}
                    animator.Update(0);
                    foreach(var renderer in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var weights=renderer.sharedMesh.boneWeights;
                        int[] Select(HumanBodyBones id){var bone=animator.GetBoneTransform(id);return Enumerable.Range(0,weights.Length).Where(i=>{
                            var w=weights[i];float W(int b,float v)=>v>0&&(renderer.bones[b]==bone||renderer.bones[b].IsChildOf(bone))?v:0;
                            return W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3)>.5f;}).ToArray();}
                        var skin=new Skin{renderer=renderer,baked=new Mesh(),feet=new[]{Select(HumanBodyBones.LeftFoot),Select(HumanBodyBones.RightFoot)}};
                        Need(skin.feet.All(f=>f.Length>=3),"Missing shoe vertices.");skins.Add(skin);
                    }
                    Need(skins.Count==3,"Expected three actual world skins.");
                }
                catch{Dispose();throw;}
            }
            public void Play(string name,float speed=1){view.SetSpeed(speed);animator.Play(name,0,0);animator.Update(0);Need(animator.GetCurrentAnimatorStateInfo(0).IsName(name),"Native state was not selected: "+name);}
            public void Step(float dt){animator.Update(dt);Need(float.IsFinite(animator.GetCurrentAnimatorStateInfo(0).normalizedTime),"Nonfinite native state time.");}
            public Pose Read()
            {
                var info=animator.GetCurrentAnimatorStateInfo(0);var p=new Pose{callbacks=driver!=null?driver.callbackCount:0,time=info.normalizedTime,state=info.shortNameHash,transition=animator.IsInTransition(0),
                    joints=Bones.Select(b=>view.transform.InverseTransformPoint(animator.GetBoneTransform(b).position)).ToArray(),
                    rotations=Bones.Select(b=>Quaternion.Inverse(view.transform.rotation)*animator.GetBoneTransform(b).rotation).ToArray(),vertices=new Vector3[skins.Count][],soles=new float[skins.Count][]};
                Need(view.transform.position.sqrMagnitude<1e-10f&&Quaternion.Angle(view.transform.rotation,Quaternion.identity)<.001f,"Fixture actor root moved.");
                Need(p.joints.All(v=>float.IsFinite(v.sqrMagnitude))&&p.rotations.All(q=>float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)),"Nonfinite native joint/rotation.");
                for(int tier=0;tier<skins.Count;tier++)
                {
                    var skin=skins[tier];skin.renderer.BakeMesh(skin.baked,false);var matrix=view.transform.worldToLocalMatrix*skin.renderer.localToWorldMatrix;
                    p.vertices[tier]=skin.baked.vertices.Select(matrix.MultiplyPoint3x4).ToArray();Need(p.vertices[tier].All(v=>float.IsFinite(v.sqrMagnitude)),"Nonfinite skin vertex.");
                    p.soles[tier]=skin.feet.Select(indices=>indices.Min(i=>p.vertices[tier][i].y)).ToArray();
                }
                return p;
            }
            public void Dispose(){foreach(var s in skins)if(s.baked!=null)Object.DestroyImmediate(s.baked);if(view!=null)Object.DestroyImmediate(view.gameObject);}
        }


        private const string Source="Assets/Art/animations/act_stand_up.fbx";
        private const float Dt=1f/120f,PrefixEnd=.70f,Total=2.2f;
        private static string Output;
        public static void Capture()
        {
            Need(Application.isBatchMode,"Dedicated native batch required.");
            string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006"));
            Output=Path.Combine(evidence,"native-cafe-exit-second-step-v1");Directory.CreateDirectory(Output);
            var before=Snapshot();var rows=new JArray();bool passed=true;
            var report=new JObject{["revision"]="typing-preserved-rise-second-planted-step-v1",["all_pass"]=false,["cases"]=rows,
                ["source_asset"]=Source,["source_sha256"]=Hash(Source),["source_importer_sha256"]=Hash(Source+".meta"),
                ["scope"]="Isolated authoring candidate. Actual accepted fitted-typing grounded ankles/rotations remain fixed through existing licensed Sitting_Exit forward-rise prefix then idle; source backwards step excluded. Measured backward hips drift is removed during rise. Rain arm goals clear the visibly intersecting thigh, with source start/end preserved. The inspected first right/left pair moves0.45m right/0.05m back. A second shorter right/left pair adds0.30m right with fixed support feet. Final endpoint0.75m right/0.05m back is only a discrete Snow gait-envelope candidate, not room acceptance. Bounded authored pelvis dip uses measured leg reach. Root remains fixed; all motion is an editor-only authoring comparison. No room/traversal/mobile acceptance. Calibrated editor-only foot goals, actor roots unchanged. No runtime controller/clip/profile writes. Contact/upper-body numbers do not establish chair clearance, continuous feel, persistent bake, traversal or production acceptance."};
            try
            {
                Need(Hash(Source)=="df052678d2604d9ca3760185be343b0654a12f7a1253673e16453099135059e7"&&Hash(Source+".meta")=="3fd2323ac67e0d6d368a0f7e9aeb2d323ef82602d4f20768548a618287624f84","Licensed source changed.");
                var proof=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"native-cafe-seat-controller-v1/receipt.json")));
                Need((bool?)proof["all_pass"]==true&&!proof["protected_drift"].Any(),"Accepted controller reference required.");
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                foreach(string look in new[]{"snow","rain"})
                {
                    var row=new JObject{["character"]=look,["all_pass"]=false};rows.Add(row);
                    try
                    {
                        var accepted=proof["cases"].Single(c=>(string)c["character"]==look);
                        Need((bool?)accepted["all_pass"]==true,"Per-look reference failed.");
                        var entry=registry.looks.Single(e=>e.key==(string)accepted["look_key"]);
                        Need(AssetDatabase.GetAssetPath(entry.prefab)==(string)accepted["world_prefab_asset"],"Exact world prefab changed.");
                        Need(Hash((string)accepted["controller_asset"])==(string)accepted["controller_sha256"],"Controller changed.");
                        foreach(string name in new[]{"social","typing"})Need(Hash((string)accepted[name+"_clip"])==(string)accepted[name+"_sha256"],"Steady clip changed.");
                        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>((string)accepted["controller_asset"]);
                        var scene=EditorSceneManager.NewPreviewScene();AnimationClip clip=null;
                        try
                        {
                            var source=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
                            clip=Object.Instantiate(source);clip.name="Sitting_Exit_nonloop_prefix_authoring_only";
                            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
                            var zeros=new[]{Vector3.zero,Vector3.zero};var identities=new[]{Quaternion.identity,Quaternion.identity};
                            var original=Sample(entry,controller,clip,scene,false,zeros,identities,0,null);
                            var zero=Sample(entry,controller,clip,scene,true,zeros,identities,0,null);
                            float zeroError=JointError(original,zero);row["noop_callback_joint_error_m"]=zeroError;Need(zeroError<=.0001f,"IK pass/no-op callback changes original source.");
                            var desired=new[]{original[0].joints[5],original[0].joints[9]};var desiredQ=new[]{original[0].rotations[5],original[0].rotations[9]};
                            var positions=(Vector3[])desired.Clone();var rotations=(Quaternion[])desiredQ.Clone();
                            var bodyOffsets=original.Select(f=>new Vector3(0,0,SmoothPositive(original[0].joints[0].z-f.joints[0].z,.003f))).ToArray();
                            row["maximum_authored_forward_body_offset_m"]=bodyOffsets.Max(v=>v.z);
                            row["body_offset_samples"]=JArray.FromObject(bodyOffsets.Select(v=>new[]{v.x,v.y,v.z}));
                            Need(bodyOffsets.Max(v=>v.magnitude)<=.06f,"Measured backward anticipation exceeds bounded6cm body correction.");
                            var initial=Sample(entry,controller,clip,scene,true,positions,rotations,1,null,bodyOffsets);
                            var calibration=new JArray();row["goal_calibration"]=calibration;
                            for(int side=0;side<2;side++)
                            {
                                int bone=side==0?5:9;var offset=Quaternion.Inverse(rotations[side])*initial[0].rotations[bone];
                                float variation=initial.Max(f=>Quaternion.Angle(offset,Quaternion.Inverse(rotations[side])*f.rotations[bone]));
                                calibration.Add(new JObject{["side"]=side,["rotation_offset_degrees"]=Quaternion.Angle(rotations[side],initial[0].rotations[bone]),["offset_variation_degrees"]=variation});
                                Need(variation<=.2f,"Foot goal-to-bone orientation frame is not stable through prefix.");
                                rotations[side]=desiredQ[side]*Quaternion.Inverse(offset);
                            }
                            var oriented=Sample(entry,controller,clip,scene,true,positions,rotations,1,null,bodyOffsets);
                            for(int side=0;side<2;side++)
                            {
                                int bone=side==0?5:9;var correction=desired[side]-oriented[0].joints[bone];
                                positions[side]+=correction;calibration[side]["position_offset_m"]=new JArray(correction.x,correction.y,correction.z);
                                Need(correction.magnitude<.2f,"Unexpected goal-to-ankle position frame displacement.");
                            }
                            var footOffsets=Enumerable.Range(0,original.Length).Select(_=>new[]{Vector3.zero,Vector3.zero}).ToArray();
                            for(int i=0;i<original.Length;i++)
                            {
                                float u=Mathf.Clamp01((i*Dt-1.05f)/.56f);
                                float v=Mathf.Clamp01((i*Dt-1.61f)/.42f);
                                bodyOffsets[i]+=new Vector3(.45f*Ease(u)+.30f*Ease(v),0,-.05f*Ease(u));
                                for(int side=0;side<2;side++)
                                {
                                    float swing=Mathf.Clamp01(side==1?u*2f:u*2f-1f);
                                    float second=Mathf.Clamp01(side==1?v*2f:v*2f-1f);
                                    footOffsets[i][side]=new Vector3(.45f*Ease(swing)+.30f*Ease(second),
                                        .05f*Mathf.Pow(Mathf.Sin(Mathf.PI*swing),2)+.04f*Mathf.Pow(Mathf.Sin(Mathf.PI*second),2),
                                        -.05f*Ease(swing));
                                }
                            }
                            var neededDrop=new float[original.Length];
                            for(int i=0;i<original.Length;i++)
                            {
                                if(i*Dt<1.05f)continue;
                                for(int side=0;side<2;side++)
                                {
                                    int h=side==0?3:7,k=h+1,a=h+2;
                                    float reach=(Vector3.Distance(original[i].joints[h],original[i].joints[k])+Vector3.Distance(original[i].joints[k],original[i].joints[a]))*.99f;
                                    var delta=original[i].joints[h]+bodyOffsets[i]-(desired[side]+footOffsets[i][side]);
                                    float horizontal=delta.x*delta.x+delta.z*delta.z;
                                    Need(horizontal<reach*reach,"Authored side-step exceeds horizontal leg reach.");
                                    neededDrop[i]=Mathf.Max(neededDrop[i],delta.y-Mathf.Sqrt(reach*reach-horizontal));
                                }
                            }
                            // Conservative smooth upper envelope of measured reach demand, never a floor Y offset.
                            var dilated=Enumerable.Range(0,original.Length).Select(i=>Enumerable.Range(Mathf.Max(0,i-2),Mathf.Min(original.Length-1,i+2)-Mathf.Max(0,i-2)+1).Max(j=>neededDrop[j])).ToArray();
                            for(int i=0;i<original.Length;i++)
                            {
                                float drop=Enumerable.Range(Mathf.Max(0,i-2),Mathf.Min(original.Length-1,i+2)-Mathf.Max(0,i-2)+1).Average(j=>dilated[j]);
                                bodyOffsets[i].y-=drop;
                            }
                            float maxDrop=bodyOffsets.Max(v=>-v.y);Need(maxDrop<=.08f,"Side-step needs more than8cm authored pelvis drop; choose a shorter step.");
                            Vector3[][] handPositions=null;Quaternion[][] handRotations=null;Vector3[][] elbowHints=null;
                            if(look=="rain")
                            {
                                var handProof=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"native-typing-work-hands-v2/receipt.json")));
                                Need((bool?)handProof["all_pass"]==true&&!handProof["protected_drift"].Any(),"Accepted same-Avatar hand frame calibration required.");
                                var handCase=handProof["cases"].Single(c=>(string)c["character"]==look);
                                var offsets=handCase["goal_to_wrist_orientation_calibration"].Select(c=>{var q=c["local_offset"].Values<float>().ToArray();return new Quaternion(q[0],q[1],q[2],q[3]);}).ToArray();
                                handPositions=new Vector3[original.Length][];handRotations=new Quaternion[original.Length][];elbowHints=new Vector3[original.Length][];
                                for(int i=0;i<original.Length;i++)
                                {
                                    float time=i*Dt;float envelope=Ease(Mathf.Clamp01(time/.20f))*(1f-Ease(Mathf.Clamp01((time-.60f)/.26f)));
                                    handPositions[i]=new Vector3[2];handRotations[i]=new Quaternion[2];elbowHints[i]=new Vector3[2];
                                    for(int side=0;side<2;side++)
                                    {
                                        int wrist=side==0?13:16,elbow=side==0?12:15;
                                        var shift=new Vector3(side==0?-.08f:.08f,.04f,.08f)*envelope;
                                        handPositions[i][side]=original[i].joints[wrist]+bodyOffsets[i]+shift;
                                        handRotations[i][side]=original[i].rotations[wrist]*Quaternion.Inverse(offsets[side]);
                                        elbowHints[i][side]=original[i].joints[elbow]+bodyOffsets[i]+shift*.5f;
                                    }
                                }
                            }
                            var candidate=Sample(entry,controller,clip,scene,true,positions,rotations,1,look+"-candidate",bodyOffsets,footOffsets,handPositions,handRotations,elbowHints);
                            SaveFrames(look+"-original",original);SaveFrames(look+"-candidate",candidate);
                            var priorRoot=Path.Combine(evidence,"native-cafe-exit-choreography-v1");
                            Need(Hash(Path.Combine(priorRoot,"receipt.json"))=="d6d55e71f1b25dbba205ba5fb8dcd4b310f489ce69ad6834f700ec875e6327e6","Prior inspected rise evidence changed.");
                            var prior=JArray.Parse(File.ReadAllText(Path.Combine(priorRoot,look+"-candidate.json")));
                            float prefixDrift=Enumerable.Range(0,181).Max(i=>Enumerable.Range(0,Bones.Length).Max(j=>{
                                var a=prior[i]["joints"][j].Values<float>().ToArray();return Vector3.Distance(candidate[i].joints[j],new Vector3(a[0],a[1],a[2]));}));
                            row["preserved_first_1_5_seconds_joint_error_m"]=prefixDrift;
                            Need(prefixDrift<=.0001f,"The additional step changed the inspected rise/first step prefix.");
                            row["full_body_offset_samples"]=JArray.FromObject(bodyOffsets.Select(v=>new[]{v.x,v.y,v.z}));
                            row["foot_offset_samples"]=JArray.FromObject(footOffsets.Select(pair=>pair.Select(v=>new[]{v.x,v.y,v.z})));
                            row["side_step_start_seconds"]=1.05f;row["side_step_duration_seconds"]=.56f;row["second_pair_start_seconds"]=1.61f;row["second_pair_duration_seconds"]=.42f;row["final_horizontal_displacement_m"]=new JArray(.75f,0f,-.05f);row["authored_pelvis_dip_m"]=maxDrop;
                            row["rain_hand_clearance_offset_m"]=new JArray(.08f,.04f,.08f);
                            if(handPositions!=null)
                            {
                                float handError=candidate.Select((f,i)=>new[]{0,1}.Max(side=>Vector3.Distance(f.joints[side==0?13:16],handPositions[i][side]))).Max();
                                float handRotation=candidate.Select((f,i)=>new[]{0,1}.Max(side=>Quaternion.Angle(f.rotations[side==0?13:16],original[i].rotations[side==0?13:16]))).Max();
                                row["hand_goal_error_m"]=handError;row["source_wrist_rotation_error_degrees"]=handRotation;
                                Need(handError<=.002f&&handRotation<=1f,"Bounded arm target missed position/rotation; do not accept a visual guess.");
                            }
                            float target=candidate.Select((f,i)=>new[]{0,1}.Max(side=>Vector3.Distance(f.joints[side==0?5:9],desired[side]+footOffsets[i][side]))).Max();
                            float rotation=candidate.Max(f=>new[]{0,1}.Max(side=>Quaternion.Angle(f.rotations[side==0?5:9],desiredQ[side])));
                            int[] body=look=="rain"?new[]{0,1,2,11,14}:new[]{0,1,2,11,12,13,14,15,16,17,18};
                            float bodyError=candidate.Select((f,i)=>body.Max(j=>Vector3.Distance(f.joints[j],original[i].joints[j]+bodyOffsets[i]))).Max();
                            float minimum=candidate.Min(f=>f.soles.SelectMany(v=>v).Min()), maximum=candidate.Max(f=>f.soles.SelectMany(v=>v).Max());
                            var kneeRanges=new JArray();
                            foreach(int[] ids in new[]{new[]{3,4,5},new[]{7,8,9}})
                            {
                                var angles=candidate.Select(f=>Vector3.Angle(f.joints[ids[0]]-f.joints[ids[1]],f.joints[ids[2]]-f.joints[ids[1]])).ToArray();
                                kneeRanges.Add(new JObject{["minimum_degrees"]=angles.Min(),["maximum_degrees"]=angles.Max(),["max_step_degrees"]=angles.Zip(angles.Skip(1),(a,b)=>Mathf.Abs(a-b)).Max()});
                            }
                            row["samples"]=candidate.Length;row["prefix_end_seconds"]=PrefixEnd;row["exit_to_idle_crossfade_seconds"]=.16f;row["length_seconds"]=Total;
                            row["ankle_target_error_m"]=target;row["ankle_rotation_error_degrees"]=rotation;row["authored_upper_body_translation_error_m"]=bodyError;row["minimum_sole_m"]=minimum;row["maximum_sole_m"]=maximum;
                            row["knees"]=kneeRanges;row["desired_ankle_positions"]=JArray.FromObject(desired.Select(v=>new[]{v.x,v.y,v.z}));row["goal_positions"]=JArray.FromObject(positions.Select(v=>new[]{v.x,v.y,v.z}));row["goal_rotations"]=JArray.FromObject(rotations.Select(q=>new[]{q.x,q.y,q.z,q.w}));
                            row["chair_or_visual_acceptance"]=false;
                            Need(target<=.002f&&rotation<=1f&&bodyError<=.002f,"Planted solve misses ankle/rotation or deviates from the measured authored upper-body translation.");
                            float soleTrajectoryError=candidate.Select((f,i)=>Enumerable.Range(0,f.soles.Length).Max(t=>new[]{0,1}.Max(side=>Mathf.Abs(f.soles[t][side]-original[0].soles[t][side]-footOffsets[i][side].y)))).Max();
                            row["expected_support_or_swing_sole_error_m"]=soleTrajectoryError;
                            Need(minimum>=-.002f&&soleTrajectoryError<=.002f,"Authored support/swing trajectory misses all-tier shoe geometry.");
                            Need(candidate.Last().state==Animator.StringToHash("loco_idle")&&!candidate.Last().transition,"Prefix did not finish at actual idle state.");
                            row["all_pass"]=true;
                        }
                        finally{if(clip!=null)Object.DestroyImmediate(clip);EditorSceneManager.ClosePreviewScene(scene);}
                    }
                    catch(Exception e){passed=false;row["failure"]=e.ToString();Debug.LogError("CTLIFE planted-emergence "+look+": "+e);}
                }
            }
            catch(Exception e){passed=false;report["failure"]=e.ToString();throw;}
            finally
            {
                var after=Snapshot();var drift=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var a)||!after.TryGetValue(k,out var b)||a!=b).ToArray();
                report["protected_drift"]=new JArray(drift);report["all_pass"]=passed&&rows.Count==2&&drift.Length==0;
                report["status"]=(bool)report["all_pass"]?"numeric-planted-prefix-pass-furniture-visual-pending":"failed";
                File.WriteAllText(Path.Combine(Output,"receipt.json"),report.ToString());Need(drift.Length==0,"Protected source drift.");
            }
            Need(passed&&rows.Count==2,"Planted emergence comparison failed; no production assignment.");
        }
        private static Pose[] Sample(CharacterRegistry.Entry entry,AnimatorController controller,AnimationClip clip,Scene scene,bool pass,Vector3[] positions,Quaternion[] rotations,float weight,string stem,Vector3[] bodyOffsets=null,Vector3[][] footOffsets=null,Vector3[][] hands=null,Quaternion[][] handQ=null,Vector3[][] hints=null)
        {
            var temporary=new AnimatorController();var machine=new AnimatorStateMachine();
            try
            {
                temporary.parameters=controller.parameters;
                foreach(string id in new[]{"loco_idle",CafeSeatMotionProfile.TypingState})
                {
                    var source=controller.layers.Single().stateMachine.states.Single(s=>s.state.name==id).state;var state=machine.AddState(id);
                    state.motion=source.motion;state.iKOnFeet=source.iKOnFeet;state.writeDefaultValues=source.writeDefaultValues;state.speed=source.speed;state.speedParameter=source.speedParameter;state.speedParameterActive=source.speedParameterActive;
                    if(id=="loco_idle")machine.defaultState=state;
                }
                var exit=machine.AddState("review_stand_exit");exit.motion=clip;exit.iKOnFeet=false;exit.writeDefaultValues=true;
                temporary.layers=new[]{new AnimatorControllerLayer{name="Base Layer",defaultWeight=1,iKPass=pass,stateMachine=machine}};
                using var rig=new Rig(entry,temporary,scene,pass,positions,rotations,weight);using var visual=stem!=null?new TransitionVisual(rig):null;
                if(rig.driver!=null&&hands!=null){rig.driver.handPositions=hands[0];rig.driver.handRotations=handQ[0];rig.driver.elbowHints=hints[0];}
                rig.Play(CafeSeatMotionProfile.TypingState,0);float length=rig.animator.GetCurrentAnimatorStateInfo(0).length;
                for(int i=0;i<120;i++)rig.Step(length/60f);
                var frames=new Pose[Mathf.RoundToInt(Total/Dt)+1];
                for(int i=0;i<frames.Length;i++)
                {
                    if(i==1)rig.animator.CrossFadeInFixedTime("review_stand_exit",.12f,0,0f);
                    if(i==Mathf.RoundToInt(PrefixEnd/Dt)+1)rig.animator.CrossFadeInFixedTime("loco_idle",.16f,0,0f);
                    if(rig.driver!=null)
                    {
                        rig.driver.bodyOffset=bodyOffsets!=null?bodyOffsets[i]:Vector3.zero;
                        rig.driver.positions=footOffsets!=null?new[]{positions[0]+footOffsets[i][0],positions[1]+footOffsets[i][1]}:positions;
                        if(hands!=null){rig.driver.handPositions=hands[i];rig.driver.handRotations=handQ[i];rig.driver.elbowHints=hints[i];}
                    }
                    if(i>0){int calls=rig.driver!=null?rig.driver.callbackCount:0;rig.Step(Dt);if(pass)Need(rig.driver.callbackCount>calls,"Actual authoring IK callback did not execute.");}
                    var pose=rig.Read();frames[i]=pose;
                    if(stem!=null&&(i%12==0||i==frames.Length-1))
                        for(int t=0;t<rig.skins.Count;t++)Obj(Path.Combine(Output,stem+"-"+i.ToString("D3")+"-tier"+t+".obj.gz"),pose.vertices[t],rig.skins[t].renderer.sharedMesh.triangles);
                    if(stem!=null&&(i==0||i==24||i==60||i==84||i==108||i==144||i==162||i==180||i==192||i==216||i==228||i==240||i==252||i==frames.Length-1))visual.Render(pose,Path.Combine(Output,stem+"-"+i.ToString("D3")));
                    pose.vertices=null; // Geometry already exported/rendered; retain only dense pose/contact evidence.
                }
                Need(Vector3.Distance(frames[0].joints[0],frames.Last().joints[0])>.25f,"No actual advancing rise.");return frames;
            }
            finally{var states=machine.states.Select(s=>s.state).ToArray();Object.DestroyImmediate(temporary);foreach(var s in states)Object.DestroyImmediate(s);Object.DestroyImmediate(machine);}
        }
        private static float Ease(float u)=>u*u*(3f-2f*u);
        private static float SmoothPositive(float value,float edge)
        {
            if(value<=0)return 0;if(value>=edge)return value;
            float u=value/edge;return edge*u*u*(2-u); // C1 at0 and edge; no negative/backward compensation.
        }
        private static float JointError(Pose[] a,Pose[] b)=>a.Select((p,i)=>p.joints.Select((v,j)=>Vector3.Distance(v,b[i].joints[j])).Max()).Max();
        private static void SaveFrames(string stem,Pose[] poses)=>File.WriteAllText(Path.Combine(Output,stem+".json"),JsonConvert.SerializeObject(poses.Select((p,i)=>new{seconds=i*Dt,p.time,p.state,p.transition,p.callbacks,p.soles,joints=p.joints.Select(v=>new[]{v.x,v.y,v.z}),rotations=p.rotations.Select(q=>new[]{q.x,q.y,q.z,q.w})}),Formatting.Indented));
        // Render exact CPU-measured meshes, never the frame-cached GPU skin state.
        private sealed class TransitionVisual:IDisposable
        {
            private readonly Rig rig;private readonly List<Object> temporary=new List<Object>();private readonly Camera camera;
            public readonly int tier;
            public TransitionVisual(Rig source)
            {
                rig=source;
                try
                {
                    var groups=rig.view.GetComponentsInChildren<LODGroup>(true);
                    var lod0=groups.SelectMany(g=>g.GetLODs().Take(1)).SelectMany(l=>l.renderers).OfType<SkinnedMeshRenderer>().Distinct().ToArray();
                    Need(lod0.Length==1,"Transition visual expects one authored LOD0 skin.");tier=rig.skins.FindIndex(s=>s.renderer==lod0[0]);Need(tier>=0&&lod0[0].gameObject.activeInHierarchy,"LOD0 skin missing/inactive.");
                    GameObject InScene(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,rig.view.gameObject.scene);temporary.Add(go);return go;}
                    var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,rig.view.gameObject.scene);temporary.Add(floor);floor.name="Transition review floor";floor.transform.position=new Vector3(0,-.02f,0);floor.transform.localScale=new Vector3(3,.04f,3);
                    var material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));temporary.Add(material);material.SetColor("_BaseColor",new Color(.48f,.46f,.41f));floor.GetComponent<Renderer>().sharedMaterial=material;
                    var key=InScene("Transition key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.5f;key.color=new Color(1,.93f,.84f);key.transform.rotation=Quaternion.Euler(35,-35,0);
                    var fill=InScene("Transition fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.6f;fill.transform.rotation=Quaternion.Euler(30,140,0);
                    camera=InScene("CPU pose transition camera").AddComponent<Camera>();camera.scene=rig.view.gameObject.scene;camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.1f;camera.aspect=1;camera.nearClipPlane=.01f;camera.farClipPlane=20;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.20f,.23f,.24f);camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
                }
                catch{Dispose();throw;}
            }
            public JObject Render(Pose pose,string prefix)
            {
                var skin=rig.skins[tier];var mesh=Object.Instantiate(skin.baked);mesh.name="Exact native CPU pose snapshot";GameObject go=null;
                var renderers=rig.view.GetComponentsInChildren<SkinnedMeshRenderer>(true);var enabled=renderers.Select(s=>s.enabled).ToArray();
                var groups=rig.view.GetComponentsInChildren<LODGroup>(true);var groupEnabled=groups.Select(g=>g.enabled).ToArray();
                try
                {
                    // Parenting with local identity preserves the entire original transform chain, including scale.
                    go=new GameObject("CPU measured pose only");go.transform.SetParent(skin.renderer.transform,false);go.layer=skin.renderer.gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=skin.renderer.sharedMaterials;
                    renderer.shadowCastingMode=skin.renderer.shadowCastingMode;renderer.receiveShadows=skin.renderer.receiveShadows;renderer.renderingLayerMask=skin.renderer.renderingLayerMask;
                    var block=new MaterialPropertyBlock();skin.renderer.GetPropertyBlock(block);renderer.SetPropertyBlock(block);
                    for(int i=0;i<renderer.sharedMaterials.Length;i++){block.Clear();skin.renderer.GetPropertyBlock(block,i);if(!block.isEmpty)renderer.SetPropertyBlock(block,i);}
                    var matrix=rig.view.transform.worldToLocalMatrix*go.transform.localToWorldMatrix;var local=mesh.vertices;Need(local.Length==pose.vertices[tier].Length,"Snapshot topology changed.");float error=0;
                    for(int i=0;i<local.Length;i++)error=Mathf.Max(error,Vector3.Distance(matrix.MultiplyPoint3x4(local[i]),pose.vertices[tier][i]));Need(error<=.00001f,"Rendered snapshot differs from measured native pose.");
                    foreach(var g in groups)g.enabled=false;foreach(var r in renderers)r.enabled=false;
                    Need(go.activeInHierarchy&&renderer.enabled&&renderers.All(r=>!r.enabled),"Original GPU skin remained visible.");
                    var row=new JObject{["render_source"]="CPU BakeMesh from same native Read, original materials and full transform chain",["tier"]=tier,["skin"]=skin.renderer.name,["snapshot_vertex_error_m"]=error,["measured_vertices_sha256"]=VertexHash(pose.vertices[tier])};
                    foreach(string view in new[]{"front","side"})
                    {
                        Vector3 target=new Vector3(0,.88f,0);camera.transform.position=target+(view=="front"?new Vector3(0,.1f,4):new Vector3(4,.1f,0));camera.transform.LookAt(target);
                        string path=prefix+"-"+view+".png";RenderCamera(camera,path);row[view+"_file"]=Path.GetFileName(path);row[view+"_sha256"]=Hash(path);
                    }
                    return row;
                }
                finally
                {
                    if(go!=null)Object.DestroyImmediate(go);Object.DestroyImmediate(mesh);
                    for(int i=0;i<groups.Length;i++)groups[i].enabled=groupEnabled[i];for(int i=0;i<renderers.Length;i++)renderers[i].enabled=enabled[i];
                }
            }
            public void Dispose(){foreach(var obj in temporary.AsEnumerable().Reverse())if(obj!=null)Object.DestroyImmediate(obj);temporary.Clear();}
        }
        private static void RenderCamera(Camera camera,string path)
        {
            var rt=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32);rt.Create();var image=new Texture2D(1024,1024,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};Need(RenderPipeline.SupportsRenderRequest(camera,request),"Graphics render request unavailable; CaptureGeometry explicitly omits images.");RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1024,1024),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);rt.Release();Object.DestroyImmediate(rt);}
        }
        private static string VertexHash(Vector3[] vertices)
        {
            using var memory=new MemoryStream();using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true))foreach(var v in vertices){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}
            using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(memory.ToArray())).Replace("-","").ToLowerInvariant();
        }
        private static void Obj(string path,Vector3[] vertices,int[] triangles){using var file=File.Create(path);using var zip=new GZipStream(file,System.IO.Compression.CompressionLevel.Fastest);using var w=new StreamWriter(zip);foreach(var v in vertices)w.WriteLine("v "+v.x.ToString("R",CultureInfo.InvariantCulture)+" "+v.y.ToString("R",CultureInfo.InvariantCulture)+" "+v.z.ToString("R",CultureInfo.InvariantCulture));for(int i=0;i<triangles.Length;i+=3)w.WriteLine($"f {triangles[i]+1} {triangles[i+1]+1} {triangles[i+2]+1}");}
        private static Dictionary<string,string> Snapshot()=>new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
        private static string Hash(string path){using var sha=SHA256.Create();using var f=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException("Cafe seat review: "+message);}
    }
}
