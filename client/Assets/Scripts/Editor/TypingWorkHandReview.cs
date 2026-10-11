// Isolated hand/keyboard comparison. No prefab/controller/clip/scene asset writes.
// A passed result is bounded pose evidence, not chair-fit or runtime integration acceptance.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    public static class TypingWorkHandReview
    {
        private const int Keys = 120, Dense = 240;
        private const string KeyboardSha = "9b7c02535527e6e7e5e1e327715fea950d383c9954ba27797002f81857920eaf";
        private const string KeybedSha = "79363c3ba37d7f07e3f019cd0b27f63d7eee72a50a7b70b83403d184c38e7caf";
        private static readonly HumanBodyBones[] Bones = {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftLittleProximal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightLittleProximal };
        private static Vector3[][] keybed;
        private sealed class Frame
        {
            public float phase, nativeTime, leftSole, rightSole, handDeskGap, handKeybedGap;
            public int callbacks;
            public Vector3[] joints, tipLowest, targets;
            public Quaternion[] rotations, targetRotations;
            public float[] tipGap;
        }
        private sealed class Skin
        {
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
            public int[][] feet, hands, tips;
            public readonly List<Vector3> local = new List<Vector3>();
            public Vector3[] vertices;
        }
        private sealed class Rig : IDisposable
        {
            public GameObject root;
            public Animator animator;
            public AnimatorController controller;
            public AnimatorStateMachine machine;
            public AnimatorState state;
            public SeatedTypingHandTargets driver;
            public readonly List<Skin> skins = new List<Skin>();
            public AnimationClip clip;
            public Rig(GameObject prefab, Scene scene, AnimationClip source, Vector3[][] positions, Quaternion[][] rotations, float weight, bool ikPass)
            {
                try
                {
                    clip = source;
                    root = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root, scene);
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    animator = root.GetComponentsInChildren<Animator>(true).Single(); animator.enabled = false;
                    Require(animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid, "Own Humanoid Avatar missing.");
                    controller = new AnimatorController { name = "UNSAVED_TYPING_HAND_REVIEW" };
                    machine = new AnimatorStateMachine(); state = machine.AddState("act_type"); state.motion = source;
                    state.speed = 1f; state.speedParameterActive = false; state.iKOnFeet = true; machine.defaultState = state;
                    controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine, iKPass = ikPass } };
                    animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false; animator.speed = 1; animator.enabled = true; animator.Rebind();
                    driver = animator.gameObject.AddComponent<SeatedTypingHandTargets>();
                    Require(driver != null, "Unity could not attach SeatedTypingHandTargets. Keep its UNITY_EDITOR-guarded source in Scripts/World/Review, outside Editor; inspect Unity\'s preceding attachment error.");
                    driver.animator = animator; driver.root = root.transform; driver.positions = positions; driver.rotations = rotations; driver.weight = weight;
                    foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var weights = renderer.sharedMesh.boneWeights;
                        int[] Select(Transform bone, bool descendants) => Enumerable.Range(0, weights.Length).Where(i =>
                        {
                            var w = weights[i];
                            float W(int b, float v) => v > 0 && (renderer.bones[b] == bone || descendants && renderer.bones[b].IsChildOf(bone)) ? v : 0;
                            return W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3) > .5f;
                        }).ToArray();
                        var skin = new Skin { renderer = renderer, mesh = new Mesh(),
                            feet = new[] { Select(animator.GetBoneTransform(HumanBodyBones.LeftFoot),true),Select(animator.GetBoneTransform(HumanBodyBones.RightFoot),true) },
                            hands = new[] { Select(animator.GetBoneTransform(HumanBodyBones.LeftHand),true),Select(animator.GetBoneTransform(HumanBodyBones.RightHand),true) },
                            tips = new[] { Select(animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal),true),Select(animator.GetBoneTransform(HumanBodyBones.RightIndexDistal),true) } };
                        Require(skin.feet.Concat(skin.hands).Concat(skin.tips).All(v => v.Length >= 3), "Missing weighted shoe/hand/index-tip vertices in " + renderer.name);
                        skins.Add(skin);
                    }
                    Require(skins.Count == 3, "Expected actual three world skin tiers.");
                }
                catch { Dispose(); throw; }
            }
            public void Tick(float dt, bool expectCallback)
            {
                int before = driver.callbackCount; animator.Update(dt);
                if (expectCallback) Require(driver.callbackCount > before, "OnAnimatorIK did not execute in the actual sampler; do not accept a static/unsolved pose.");
            }
            public void Warm(int steps, bool callback)
            {
                animator.Play("act_type",0,0); Tick(0,callback);
                for (int i=0;i<2*steps;i++) Tick(clip.length/steps,callback);
                Require(Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime-2f)<.001f,"Warm-up did not advance two loops.");
            }
            public void Step(int steps, bool callback)
            {
                float before=animator.GetCurrentAnimatorStateInfo(0).normalizedTime; Tick(clip.length/steps,callback);
                Require(Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime-before-1f/steps)<.0001f,"Native state time failed to advance.");
            }
            public Frame Read(float phase)
            {
                var info=animator.GetCurrentAnimatorStateInfo(0);
                Require(Mathf.Abs(info.normalizedTime-(2f+phase))<.001f && Mathf.Abs(info.speed*info.speedMultiplier*animator.speed-1f)<.00001f,"Native sample phase/rate mismatch.");
                Require(root.transform.position.sqrMagnitude<1e-10f && Quaternion.Angle(root.transform.rotation,Quaternion.identity)<.001f,"Root changed.");
                var f=new Frame { phase=phase,nativeTime=info.normalizedTime,callbacks=driver.callbackCount,leftSole=float.PositiveInfinity,rightSole=float.PositiveInfinity,
                    handDeskGap=float.PositiveInfinity,handKeybedGap=float.PositiveInfinity,
                    joints=Bones.Select(b=>root.transform.InverseTransformPoint(animator.GetBoneTransform(b).position)).ToArray(),
                    rotations=Bones.Select(b=>Quaternion.Inverse(root.transform.rotation)*animator.GetBoneTransform(b).rotation).ToArray(),
                    tipLowest=new Vector3[2],tipGap=new[]{float.PositiveInfinity,float.PositiveInfinity},targets=new Vector3[2],targetRotations=new Quaternion[2] };
                float[] lowest={float.PositiveInfinity,float.PositiveInfinity};
                foreach(var skin in skins)
                {
                    skin.renderer.BakeMesh(skin.mesh,false);skin.mesh.GetVertices(skin.local);
                    var m=root.transform.worldToLocalMatrix*skin.renderer.localToWorldMatrix;
                    skin.vertices=skin.local.Select(m.MultiplyPoint3x4).ToArray();
                    f.leftSole=Mathf.Min(f.leftSole,skin.feet[0].Min(i=>skin.vertices[i].y));f.rightSole=Mathf.Min(f.rightSole,skin.feet[1].Min(i=>skin.vertices[i].y));
                    for(int side=0;side<2;side++)
                    {
                        foreach(int i in skin.tips[side])
                        {
                            var v=skin.vertices[i];
                            // The actual keybed is a single sloped plane. Its extension is only
                            // a translation guide before the fingertip reaches the bounded keys;
                            // acceptance below uses actual triangle coverage and skin clearance.
                            float guideGap=v.y-KeyPlane(v.x,v.z);
                            if(guideGap<lowest[side]){lowest[side]=guideGap;f.tipLowest[side]=v;}
                            if(Surface(v.x,v.z,out float y)) f.tipGap[side]=Mathf.Min(f.tipGap[side],v.y-y);
                        }
                        foreach(int i in skin.hands[side])
                        {
                            var v=skin.vertices[i];
                            if(Mathf.Abs(v.x)<=.6f && v.z>=.20f && v.z<=.8f)f.handDeskGap=Mathf.Min(f.handDeskGap,v.y-.74f);
                            if(Surface(v.x,v.z,out float y))f.handKeybedGap=Mathf.Min(f.handKeybedGap,v.y-y);
                        }
                    }
                }
                float at=phase*(driver.positions.Length-1);int a=Mathf.Min(Mathf.FloorToInt(at),driver.positions.Length-1),b=Mathf.Min(a+1,driver.positions.Length-1);
                for(int s=0;s<2;s++){f.targets[s]=Vector3.Lerp(driver.positions[a][s],driver.positions[b][s],at-a);f.targetRotations[s]=Quaternion.Slerp(driver.rotations[a][s],driver.rotations[b][s],at-a);}
                Require(f.joints.All(v=>Finite(v.sqrMagnitude))&&Finite(f.leftSole)&&Finite(f.rightSole),"Nonfinite native pose.");
                return f;
            }
            public void Dispose()
            {
                foreach(var skin in skins) if(skin.mesh!=null)Object.DestroyImmediate(skin.mesh);
                if(root!=null)Object.DestroyImmediate(root);if(controller!=null)Object.DestroyImmediate(controller);
                if(state!=null)Object.DestroyImmediate(state);if(machine!=null)Object.DestroyImmediate(machine);
            }
        }

        public static void Capture()
        {
            string baseDir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/character-candidates/motion-review-20261006"));
            string output=Path.Combine(baseDir,"native-typing-work-hands-v2");Directory.CreateDirectory(output);
            string keyboardDir=Path.GetFullPath(Path.Combine(baseDir,"../../direction-20261006/terminal-cafe-b3-fit/keyboard"));
            string keyboardObj=Path.Combine(keyboardDir,"shared-typing-keyboard-target.obj.gz"),keyboardJson=Path.Combine(keyboardDir,"keybed-triangles.json");
            var protectedBefore=Snapshot();var receipt=new JObject{["state"]="sampling",["all_pass"]=false,["cases"]=new JArray(),
                ["scope"]="Editor-only hand target comparison against accepted closed-source typing feet and measured curved-chair body fit. Combined chair/body/keyboard visual review, imported keyboard facing/material, runtime baking/transition/feel remain pending. No production asset writes."};
            bool pass=true;
            try
            {
                Require(Hash(keyboardObj)==KeyboardSha && Hash(keyboardJson)==KeybedSha,"Frozen sourced keyboard target changed.");
                keybed=JObject.Parse(File.ReadAllText(keyboardJson))["keybed_top_triangles"].Select(t=>t["vertices"].Select(V).ToArray()).ToArray();
                Require(keybed.Length>0,"No actual keybed triangles.");
                Require(keybed.SelectMany(t=>t).All(v=>Mathf.Abs(v.y-KeyPlane(v.x,v.z))<.000002f),"Translation guide requires the verified planar keybed; do not approximate a curved surface.");
                receipt["keyboard_geometry_sha256"]=KeyboardSha;receipt["keyboard_triangles_sha256"]=Hash(keyboardJson);
                var feetReceipt=JObject.Parse(File.ReadAllText(Path.Combine(baseDir,"native-typing-work-body-fit-v1/candidate-receipt.json")));
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                foreach(string character in new[]{"snow","rain"})
                {
                    var item=new JObject{["character"]=character,["all_pass"]=false};((JArray)receipt["cases"]).Add(item);
                    try
                    {
                        var feetCase=feetReceipt["cases"].Single(c=>(string)c["character"]==character && (string)c["action"]=="act_type");
                        Require((bool)feetCase["all_pass"] && !feetReceipt["protected_drift"].Any(),"Closed-source typing body/feet candidate did not pass native gates.");
                        string sourcePath=(string)feetCase["candidate_asset"];Require(Hash(sourcePath)==(string)feetCase["candidate_sha256"],"Fitted typing source hash changed.");Require(sourcePath.StartsWith("Assets/Art/characters/authored/MotionReview20261006/TypingWorkBodyFitV1/",StringComparison.Ordinal),"Unexpected source scope.");
                        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);Require(source!=null&&source.humanMotion&&source.isLooping,"Fitted closed-source typing clip invalid.");
                        item["source_asset"]=sourcePath;item["source_sha256"]=Hash(sourcePath);
                        var prefab=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+character)).prefab;
                        Review(character,prefab,source,baseDir,output,item);item["all_pass"]=true;
                    }
                    catch(Exception e){pass=false;item["failure"]=e.ToString();Debug.LogError("CTLIFE [typing-hands] "+character+": "+e);}
                }
            }
            catch(Exception e){pass=false;receipt["failure"]=e.ToString();throw;}
            finally
            {
                var after=Snapshot();var drift=protectedBefore.Where(p=>!after.TryGetValue(p.Key,out string v)||v!=p.Value).Select(p=>p.Key).Concat(after.Keys.Where(p=>!protectedBefore.ContainsKey(p))).ToArray();
                receipt["protected_drift"]=new JArray(drift);receipt["all_pass"]=pass&&drift.Length==0;receipt["state"]=pass&&drift.Length==0?"native-numeric-pass-not-promoted":"failed";
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());Require(drift.Length==0,"Protected assets changed.");
            }
            Require(pass,"Typing hand comparison failed; inspect retained geometry and receipt.");
        }
        private static void Review(string character,GameObject prefab,AnimationClip source,string baseDir,string output,JObject receipt)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var positions=Enumerable.Range(0,Keys+1).Select(_=>new[]{new Vector3(-.15f,.80f,.13f),new Vector3(.10f,.80f,.13f)}).ToArray();
                var rotations=Enumerable.Range(0,Keys+1).Select(_=>new[]{Quaternion.identity,Quaternion.identity}).ToArray();
                var original=Sample(prefab,scene,source,positions,rotations,0,false,Dense,Path.Combine(output,character+"-original"));
                WriteFrames(Path.Combine(output,character+"-original.json"),original,source.length);
                var archived=JObject.Parse(File.ReadAllText(Path.Combine(baseDir,"native-typing-work-body-fit-v1/"+(character=="snow"?"Snow":"Rain")+"_TypingWorkBodyFit-dense-candidate.json")));
                float archivedError=0;
                foreach(var bone in Bones.Take(17))
                {
                    int a=Array.IndexOf(Bones,bone),b=archived["joints"].Values<string>().ToList().IndexOf(bone.ToString());Require(b>=0,"Missing independent source joint.");
                    for(int i=0;i<=Dense;i++)archivedError=Mathf.Max(archivedError,Vector3.Distance(original[i].joints[a],V(archived["frames"][i]["joints"][b])));
                }
                receipt["source_independent_joint_error_m"]=archivedError;Require(archivedError<=.002f,"Source does not reproduce accepted typing body/foot poses.");
                for(int side=0;side<2;side++)
                {
                    int wrist=side==0?13:16,index=side==0?18:22,middle=side==0?19:23,prox=side==0?17:21,little=side==0?20:24;
                    var frame=original[0];Vector3 forward=(frame.joints[index]+frame.joints[middle])*.5f-frame.joints[wrist];
                    Vector3 normal=Vector3.Cross(forward,frame.joints[prox]-frame.joints[little]).normalized;if(normal.y<0)normal=-normal;
                    Require(forward.magnitude>.035f&&normal.sqrMagnitude>.5f,"Cannot establish actual hand frame.");
                    var target=Quaternion.LookRotation(new Vector3(0,-.20f,1).normalized,Vector3.up)*Quaternion.Inverse(Quaternion.LookRotation(forward,normal))*frame.rotations[wrist];
                    for(int i=0;i<=Keys;i++)rotations[i][side]=target;
                }
                var zero=Sample(prefab,scene,source,positions,rotations,0,true,Dense,null);
                float zeroError=original.Zip(zero,(a,b)=>Enumerable.Range(0,Bones.Length).Max(j=>Vector3.Distance(a.joints[j],b.joints[j]))).Max();
                receipt["zero_weight_ik_pass_joint_error_m"]=zeroError;Require(zeroError<=.0001f,"IK-pass/callback alone changes source pose.");
                var initial=Sample(prefab,scene,source,positions,rotations,1,true,Keys,Path.Combine(output,character+"-initial"));
                WriteFrames(Path.Combine(output,character+"-initial.json"),initial,source.length);
                // Animator hand IK goals use an Avatar-specific orientation frame. They are not wrist bone quaternions.
                var desiredWristRotations=rotations.Select(pair=>(Quaternion[])pair.Clone()).ToArray();
                var goalOffsets=new JArray();receipt["goal_to_wrist_orientation_calibration"]=goalOffsets;
                for(int side=0;side<2;side++)
                {
                    int wrist=side==0?13:16;Quaternion offset=Quaternion.Inverse(initial[0].targetRotations[side])*initial[0].rotations[wrist];
                    float variation=initial.Max(f=>Quaternion.Angle(offset,Quaternion.Inverse(f.targetRotations[side])*f.rotations[wrist]));
                    goalOffsets.Add(new JObject{["side"]=side==0?"left":"right",["bone_vs_requested_goal_degrees"]=Quaternion.Angle(initial[0].rotations[wrist],initial[0].targetRotations[side]),["local_offset"]=new JArray(offset.x,offset.y,offset.z,offset.w),["full_cycle_offset_variation_degrees"]=variation});
                    Require(variation<=.2f,"Hand goal-to-bone orientation offset is not constant through the measured source cycle.");
                    for(int i=0;i<=Keys;i++)rotations[i][side]=desiredWristRotations[i][side]*Quaternion.Inverse(offset);
                }
                var oriented=Sample(prefab,scene,source,positions,rotations,1,true,Keys,Path.Combine(output,character+"-orientation-calibrated"));
                WriteFrames(Path.Combine(output,character+"-orientation-calibrated.json"),oriented,source.length);
                float calibratedRotation=oriented.Select((f,i)=>new[]{0,1}.Max(side=>Quaternion.Angle(f.rotations[side==0?13:16],desiredWristRotations[i][side]))).Max();
                float calibratedWrist=oriented.Max(f=>new[]{0,1}.Max(side=>Vector3.Distance(f.joints[side==0?13:16],f.targets[side])));
                receipt["calibrated_wrist_rotation_error_degrees"]=calibratedRotation;receipt["calibrated_wrist_position_error_m"]=calibratedWrist;
                Require(calibratedRotation<=1f&&calibratedWrist<=.002f,"Measured orientation calibration does not reproduce physical wrist orientation/position.");
                var iterations=new JArray();receipt["hand_surface_iterations"]=iterations;
                for(int iteration=0;iteration<3;iteration++)
                {
                    var measured=iteration==0?oriented:Sample(prefab,scene,source,positions,rotations,1,true,Keys,null);float correction=0;
                    for(int i=0;i<=Keys;i++)for(int side=0;side<2;side++)
                    {
                        float x=side==0?-.115f:.035f,z=.260f;
                        Require(Surface(x,z,out float y),"Selected real main-keybed point has no triangle.");
                        float gap=DesiredGap(i/(float)Keys,side);
                        Vector3 error=new Vector3(x,y+gap,z)-measured[i].tipLowest[side];correction=Mathf.Max(correction,error.magnitude);
                        Require(error.magnitude<.12f,"Hand correction leaves the measured reach neighborhood.");positions[i][side]+=error;
                    }
                    iterations.Add(new JObject{["iteration"]=iteration,["max_target_correction_m"]=correction});
                    for(int side=0;side<2;side++)positions[0][side]=positions[Keys][side]=(positions[0][side]+positions[Keys][side])*.5f;
                    if(correction<.0002f)break;
                }
                var final=Sample(prefab,scene,source,positions,rotations,1,true,Dense,Path.Combine(output,character+"-candidate"));
                WriteFrames(Path.Combine(output,character+"-candidate.json"),final,source.length);
                receipt["desired_physical_wrist_rotations"]=JArray.FromObject(desiredWristRotations[0].Select(Q));
                receipt["targets"]=JArray.FromObject(positions.Select((p,i)=>new{phase=i/(float)Keys,positions=p.Select(A),rotations=rotations[i].Select(Q)}));
                float fixedBody=original.Zip(final,(a,b)=>Enumerable.Range(0,11).Concat(new[]{11,14}).Max(j=>Vector3.Distance(a.joints[j],b.joints[j]))).Max();
                float targetError=final.Max(f=>new[]{0,1}.Max(s=>Vector3.Distance(f.joints[s==0?13:16],f.targets[s])));
                float rotationError=final.Max(f=>new[]{0,1}.Max(side=>Quaternion.Angle(f.rotations[side==0?13:16],desiredWristRotations[0][side])));
                float tipError=final.Max(f=>new[]{0,1}.Max(s=>Mathf.Abs(f.tipGap[s]-DesiredGap(f.phase,s))));
                float soleLow=final.Min(f=>Mathf.Min(f.leftSole,f.rightSole)),soleHigh=final.Max(f=>Mathf.Max(f.leftSole,f.rightSole));
                float reach=0,elbowMin=180,elbowMax=0;
                foreach(var f in final)for(int side=0;side<2;side++)
                {
                    int shoulder=side==0?11:14,elbow=side==0?12:15,wrist=side==0?13:16;
                    float upper=Vector3.Distance(original[0].joints[shoulder],original[0].joints[elbow]),lower=Vector3.Distance(original[0].joints[elbow],original[0].joints[wrist]);
                    reach=Mathf.Max(reach,Vector3.Distance(f.joints[shoulder],f.joints[wrist])/(upper+lower));
                    float angle=Vector3.Angle(f.joints[shoulder]-f.joints[elbow],f.joints[wrist]-f.joints[elbow]);elbowMin=Mathf.Min(elbowMin,angle);elbowMax=Mathf.Max(elbowMax,angle);
                }
                receipt["fixed_body_feet_shoulders_delta_m"]=fixedBody;receipt["wrist_target_error_m"]=targetError;receipt["wrist_rotation_error_degrees"]=rotationError;
                receipt["index_contact_gap_error_m"]=tipError;receipt["hand_keybed_min_gap_m"]=final.Min(f=>f.handKeybedGap);receipt["hand_desktop_min_gap_m"]=final.Min(f=>f.handDeskGap);
                receipt["sole_range_m"]=new JArray(soleLow,soleHigh);receipt["max_reach_fraction"]=reach;receipt["elbow_included_angle_degrees"]=new JArray(elbowMin,elbowMax);
                Require(fixedBody<=.002f&&targetError<=.002f&&rotationError<=1f,"Hand solve moved fixed body/feet/shoulders or missed wrist targets.");
                Require(soleLow>=-.002f&&soleHigh<=.008f,"Hand solving lost measured shoe contact.");
                Require(final.All(f=>f.tipGap.All(Finite))&&tipError<=.0015f,"Index contact witness does not follow actual keybed within1.5mm.");
                Require(final.Min(f=>f.handKeybedGap)>=-.0015f&&final.Min(f=>f.handDeskGap)>=-.0015f,"Hand skin penetrates measured keybed/desktop.");
                Require(reach<.98f&&elbowMin>45f&&elbowMax<160f,"Typing arms are overfolded/near-full stretch.");
                float loop=Enumerable.Range(0,Bones.Length).Max(j=>Vector3.Distance(final[0].joints[j],final[Dense].joints[j]));receipt["loop_joint_seam_m"]=loop;Require(loop<.002f,"Typing hand loop adds a visible seam.");
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static Frame[] Sample(GameObject prefab,Scene scene,AnimationClip source,Vector3[][] positions,Quaternion[][] rotations,float weight,bool pass,int steps,string stem)
        {
            using var rig=new Rig(prefab,scene,source,positions,rotations,weight,pass);rig.Warm(steps,pass);var frames=new Frame[steps+1];
            for(int i=0;i<=steps;i++)
            {
                frames[i]=rig.Read(i/(float)steps);
                if(stem!=null&&i<steps&&i%(steps/8)==0)for(int tier=0;tier<rig.skins.Count;tier++)WriteObj(stem+"-tier"+tier+"-phase"+i.ToString("D3")+".obj.gz",rig.skins[tier]);
                if(i<steps)rig.Step(steps,pass);
            }
            return frames;
        }
        private static float DesiredGap(float phase,int side)=>.001f+.004f*(.5f+.5f*Mathf.Cos(4f*Mathf.PI*phase+side*Mathf.PI));
        private static float KeyPlane(float x,float z)
        {
            var t=keybed[0];var normal=Vector3.Cross(t[1]-t[0],t[2]-t[0]);
            Require(Mathf.Abs(normal.y)>1e-6f,"Keybed plane has no vertical projection.");
            return t[0].y-(normal.x*(x-t[0].x)+normal.z*(z-t[0].z))/normal.y;
        }
        private static bool Surface(float x,float z,out float height)
        {
            height=float.NegativeInfinity;
            foreach(var t in keybed)
            {
                var a=t[0];var b=t[1];var c=t[2];float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<1e-10f)continue;
                float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/d,v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/d,w=1-u-v;
                if(u>=-.00001f&&v>=-.00001f&&w>=-.00001f)height=Mathf.Max(height,u*a.y+v*b.y+w*c.y);
            }
            return Finite(height);
        }
        private static void WriteFrames(string path,Frame[] frames,float length)=>File.WriteAllText(path,JsonConvert.SerializeObject(new{clip_seconds=length,source_rate=1,joints=Bones.Select(b=>b.ToString()),coordinates="root-local metres, actual weighted skin vertices across all3world tiers",frames=frames.Select(f=>new{f.phase,f.nativeTime,f.callbacks,f.leftSole,f.rightSole,f.handDeskGap,f.handKeybedGap,joints=f.joints.Select(A),rotations=f.rotations.Select(Q),tipLowest=f.tipLowest.Select(A),f.tipGap,targets=f.targets.Select(A),targetRotations=f.targetRotations.Select(Q)})},Formatting.Indented));
        private static void WriteObj(string path,Skin skin)
        {
            using var file=File.Create(path);using var zip=new GZipStream(file,System.IO.Compression.CompressionLevel.Fastest);using var writer=new StreamWriter(zip);
            string N(float v)=>v.ToString("R",CultureInfo.InvariantCulture);foreach(var v in skin.vertices)writer.WriteLine($"v {N(v.x)} {N(v.y)} {N(v.z)}");
            var triangles=skin.renderer.sharedMesh.triangles;for(int i=0;i<triangles.Length;i+=3)writer.WriteLine($"f {triangles[i]+1} {triangles[i+1]+1} {triangles[i+2]+1}");
        }
        private static Dictionary<string,string> Snapshot()=>new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
        private static string Hash(string p){using var sha=SHA256.Create();using var f=File.OpenRead(p);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        private static Vector3 V(JToken a){var x=a.Values<float>().ToArray();return new Vector3(x[0],x[1],x[2]);}
        private static float[] A(Vector3 v)=>new[]{v.x,v.y,v.z};private static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
        private static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        private static void Require(bool okay,string reason){if(!okay)throw new InvalidOperationException("Typing hand review: "+reason);}
    }
}
