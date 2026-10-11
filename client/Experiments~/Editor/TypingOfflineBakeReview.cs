// Offline combined typing bake. Only unassigned isolated candidate clips may be written.
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
    public static class TypingOfflineBakeReview
    {
        private const int Keys = 120, Dense = 240;
        private const string Stage="Assets/Art/characters/authored/MotionReview20261006/TypingOfflineBakeV1/";
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
            public HumanPose pose;
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
            public HumanPoseHandler human;
            public Rig(GameObject prefab, Scene scene, AnimationClip source, Vector3[][] positions, Quaternion[][] rotations, float weight, bool ikPass, bool footIk=true)
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
                    state.speed = 1f; state.speedParameterActive = false; state.iKOnFeet = footIk; machine.defaultState = state;
                    controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine, iKPass = ikPass } };
                    animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false; animator.speed = 1; animator.enabled = true; animator.Rebind();
                    human=new HumanPoseHandler(animator.avatar,animator.transform);
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
                    Require(skin.vertices.All(v=>Finite(v.sqrMagnitude)),"Nonfinite native skin vertex in "+skin.renderer.name);
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
                Require(f.joints.All(v=>Finite(v.sqrMagnitude))&&f.rotations.All(q=>Q(q).All(Finite))&&Finite(f.leftSole)&&Finite(f.rightSole),"Nonfinite native pose.");
                var pose=new HumanPose();human.GetHumanPose(ref pose);
                Require(Finite(pose.bodyPosition.sqrMagnitude)&&Q(pose.bodyRotation).All(Finite)&&pose.muscles.All(Finite),"Nonfinite HumanPose representation.");f.pose=pose;
                return f;
            }
            public void Dispose()
            {
                human?.Dispose();
                foreach(var skin in skins) if(skin.mesh!=null)Object.DestroyImmediate(skin.mesh);
                if(root!=null)Object.DestroyImmediate(root);if(controller!=null)Object.DestroyImmediate(controller);
                if(state!=null)Object.DestroyImmediate(state);if(machine!=null)Object.DestroyImmediate(machine);
            }
        }

        public static void Capture()
        {
            string baseDir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006"));
            string output=Path.Combine(baseDir,"native-typing-offline-bake-v1");Directory.CreateDirectory(output);
            string keyboardDir=Path.GetFullPath(Path.Combine(baseDir,"../../direction-20261006/terminal-cafe-b3-fit/keyboard"));
            string keyboardObj=Path.Combine(keyboardDir,"shared-typing-keyboard-target.obj.gz"),keyboardJson=Path.Combine(keyboardDir,"keybed-triangles.json");
            var protectedBefore=Snapshot();var receipt=new JObject{["state"]="sampling",["all_pass"]=false,["cases"]=new JArray(),["review_revision"]="persistent-replay-v2",
                ["scope"]="Offline Humanoid bake of the accepted combined typing body/feet/hands. First human-pose representation, then exact source-relative muscle curves, measured root-translation compensation and FootIK-off/hand-pass-off full-skin replay. Candidate clips only; no controller assignment. Furniture render/transitions/device acceptance separate."};
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
            var approved=JObject.Parse(File.ReadAllText(Path.Combine(baseDir,"native-typing-work-hands-v2/receipt.json")));
            Require((bool)approved["all_pass"]&&!approved["protected_drift"].Any(),"Combined hand/body/feet reference did not pass.");
            var accepted=approved["cases"].Single(c=>(string)c["character"]==character);Require((string)accepted["source_sha256"]==Hash(AssetDatabase.GetAssetPath(source)),"Approved combined source changed.");
            var positions=accepted["targets"].Select(t=>t["positions"].Select(V).ToArray()).ToArray();
            var rotations=accepted["targets"].Select(t=>t["rotations"].Select(tq=>{var q=tq.Values<float>().ToArray();return new Quaternion(q[0],q[1],q[2],q[3]);}).ToArray()).ToArray();
            Require(positions.Length==Keys+1&&rotations.Length==Keys+1,"Frozen target sample count changed.");
            var scene=EditorSceneManager.NewPreviewScene();AnimationClip candidate=null;
            try
            {
                var target=Sample(prefab,scene,source,positions,rotations,1,true,Keys,null);
                var baseline=Sample(prefab,scene,source,positions,rotations,0,false,Keys,null,false);
                var targetDense=Sample(prefab,scene,source,positions,rotations,1,true,Dense,Path.Combine(output,character+"-reference"));
                WriteFrames(Path.Combine(output,character+"-reference.json"),targetDense,source.length);
                var archived=JObject.Parse(File.ReadAllText(Path.Combine(baseDir,"native-typing-work-hands-v2/"+character+"-candidate.json")));
                float independent=0;for(int i=0;i<=Dense;i++)for(int j=0;j<Bones.Length;j++)independent=Mathf.Max(independent,Vector3.Distance(targetDense[i].joints[j],V(archived["frames"][i]["joints"][j])));
                receipt["independent_combined_source_joint_error_m"]=independent;Require(independent<=.0001f,"Frozen combined reference did not reproduce.");
                HumanRepresentation(prefab,scene,source,positions,rotations,receipt,Path.Combine(output,character+"-humanpose-roundtrip"));
                var selected=Enumerable.Range(0,HumanTrait.MuscleCount).Where(i=>{string n=HumanTrait.MuscleName[i];return (n.StartsWith("Left ")||n.StartsWith("Right "))&&(n.Contains(" Leg ")||n.Contains(" Foot ")||n.Contains(" Toes ")||n.Contains(" Shoulder ")||n.Contains(" Arm ")||n.Contains(" Forearm ")||n.Contains(" Hand "));}).ToArray();
                Require(selected.Length==34,"Expected16leg and18arm muscle channels.");receipt["baked_muscles"]=new JArray(selected.Select(i=>HumanTrait.MuscleName[i]));
                candidate=Object.Instantiate(source);candidate.name=(character=="snow"?"Snow":"Rain")+"_TypingCombined_FK_Candidate";
                foreach(int m in selected)
                {
                    var binding=Binding(source,HumanTrait.MuscleName[m]);var curve=AnimationUtility.GetEditorCurve(source,binding);
                    var delta=Enumerable.Range(0,Keys+1).Select(i=>target[i].pose.muscles[m]-baseline[i].pose.muscles[m]).ToArray();
                    AnimationUtility.SetEditorCurve(candidate,binding,AddDelta(curve,PeriodicCurve(delta,source.length)));
                }
                var replay=Sample(prefab,scene,candidate,positions,rotations,0,false,Keys,null,false);
                WriteFrames(Path.Combine(output,character+"-muscle-only.json"),replay,source.length);
                float relative=0;var offsets=new Vector3[Keys+1];
                for(int i=0;i<=Keys;i++)
                {
                    offsets[i]=target[i].joints[0]-replay[i].joints[0];
                    for(int j=0;j<Bones.Length;j++)relative=Mathf.Max(relative,Vector3.Distance(target[i].joints[j],replay[i].joints[j]+offsets[i]));
                }
                receipt["muscle_replay_hip_relative_joint_error_m"]=relative;receipt["needed_body_translation_max_m"]=offsets.Max(v=>v.magnitude);
                Require(relative<=.002f&&offsets.Max(v=>v.magnitude)<=.05f,"Muscle replay cannot reproduce relative articulation; do not mask a limb error with body translation.");
                var response=RootResponse(prefab,scene,candidate,positions,rotations,receipt);var inverse=response.inverse;
                for(int axis=0;axis<3;axis++)
                {
                    var binding=Binding(candidate,"RootT."+"xyz"[axis]);var curve=AnimationUtility.GetEditorCurve(candidate,binding);
                    var delta=offsets.Select(v=>inverse.MultiplyVector(v)[axis]).ToArray();
                    AnimationUtility.SetEditorCurve(candidate,binding,AddDelta(curve,PeriodicCurve(delta,source.length)));
                }
                var beforeSave=new JObject();receipt["pre_save_replay"]=beforeSave;
                ValidateReplay(prefab,scene,source,candidate,positions,rotations,targetDense,beforeSave,Path.Combine(output,character+"-pre-save"));
                ValidateCurveScope(source,candidate,selected,beforeSave);Require(candidate.humanMotion&&candidate.isLooping&&candidate.length==source.length,"Baked Humanoid loop settings changed.");
                Directory.CreateDirectory(Stage);string path=Stage+candidate.name+".anim";
                receipt["candidate_asset"]=path;receipt["persisted_replay_verified"]=false;
                var saved=SaveReloadCandidate(ref candidate,path,receipt);
                // Every acceptance field and candidate geometry below comes from the reloaded
                // on-disk asset, not the in-memory curve authoring object. Both IK systems stay off.
                ValidateCurveScope(source,saved,selected,receipt);
                Require(saved.humanMotion&&saved.isLooping&&saved.length==source.length,"Persisted Humanoid loop settings changed.");
                ValidateReplay(prefab,scene,source,saved,positions,rotations,targetDense,receipt,Path.Combine(output,character+"-candidate"));
                Require(Hash(path)==(string)receipt["candidate_sha256"]&&!EditorUtility.IsDirty(saved),"Persisted candidate changed during replay.");
                receipt["persisted_replay_verified"]=true;
                receipt["scope"]="Unassigned per-look offline Humanoid clip. Saved, unloaded, synchronously reimported and reloaded; native steady-loop FootIK-off/no-hand-pass full-skin replay only. Real controller transitions, shared furniture triangles and phone cost still pending.";
            }
            finally{if(candidate!=null)Object.DestroyImmediate(candidate);EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static AnimationClip SaveReloadCandidate(ref AnimationClip candidate,string path,JObject receipt)
        {
            Require(path.StartsWith(Stage,StringComparison.Ordinal)&&path.EndsWith(".anim",StringComparison.Ordinal),"Candidate save escaped the isolated stage.");
            Require(candidate!=null&&!EditorUtility.IsPersistent(candidate),"Expected a transient authored candidate.");
            AnimationClip saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(saved==null){AssetDatabase.CreateAsset(candidate,path);saved=candidate;candidate=null;}
            else{EditorUtility.CopySerialized(candidate,saved);EditorUtility.SetDirty(saved);Object.DestroyImmediate(candidate);candidate=null;}
            AssetDatabase.SaveAssetIfDirty(saved);
            Require(EditorUtility.IsPersistent(saved)&&!EditorUtility.IsDirty(saved)&&File.Exists(path),"Candidate did not persist cleanly.");
            string hash=Hash(path);receipt["candidate_sha256"]=hash;
            // All sampling rigs/controllers have already been disposed. Unload only this isolated
            // disk asset, then synchronously import that exact path before obtaining a new object.
            Resources.UnloadAsset(saved);Require(saved==null,"Saved candidate stayed loaded; disk replay is not established.");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var reloaded=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Require(reloaded!=null&&EditorUtility.IsPersistent(reloaded)&&AssetDatabase.GetAssetPath(reloaded)==path,"Saved candidate could not reload from its exact path.");
            Require(Hash(path)==hash&&!EditorUtility.IsDirty(reloaded),"Saved candidate bytes changed during reload.");
            receipt["replay_source"]="saved-unloaded-synchronously-reimported-asset";
            return reloaded;
        }
        private static void ValidateReplay(GameObject prefab,Scene scene,AnimationClip source,AnimationClip candidate,Vector3[][] positions,Quaternion[][] rotations,Frame[] targetDense,JObject receipt,string stem)
        {
            var final=Sample(prefab,scene,candidate,positions,rotations,0,false,Dense,stem,false);
            WriteFrames(stem+".json",final,source.length);
            float joint=targetDense.Zip(final,(a,c)=>Enumerable.Range(0,Bones.Length).Max(j=>Vector3.Distance(a.joints[j],c.joints[j]))).Max();
            float rotation=targetDense.Zip(final,(a,c)=>new[]{5,6,9,10,13,16}.Max(j=>Quaternion.Angle(a.rotations[j],c.rotations[j]))).Max();
            float skin=CompareSkins(prefab,scene,source,candidate,positions,rotations);
            receipt["replayed_joint_error_m"]=joint;receipt["replayed_feet_wrist_rotation_error_degrees"]=rotation;receipt["all_skin_replay_error_m"]=skin;
            receipt["playback_foot_ik"]=false;receipt["playback_hand_ik_pass"]=false;receipt["playback_ik_callback_count"]=final.Max(f=>f.callbacks);
            Require(final.All(f=>f.callbacks==0),"IK callback ran during authored FK replay.");
            receipt["sole_range_m"]=new JArray(final.Min(f=>Mathf.Min(f.leftSole,f.rightSole)),final.Max(f=>Mathf.Max(f.leftSole,f.rightSole)));
            receipt["keybed_min_gap_m"]=final.Min(f=>f.handKeybedGap);receipt["desktop_min_gap_m"]=final.Min(f=>f.handDeskGap);
            float tip=final.Max(f=>new[]{0,1}.Max(side=>Mathf.Abs(f.tipGap[side]-DesiredGap(f.phase,side))));receipt["index_contact_gap_error_m"]=tip;
            float loop=Enumerable.Range(0,Bones.Length).Max(j=>Vector3.Distance(final[0].joints[j],final[Dense].joints[j]));receipt["loop_joint_seam_m"]=loop;
            Require(joint<=.002f&&rotation<=1f&&skin<=.002f,"Authored FK replay differs from the approved full-body geometry.");
            Require(final.All(f=>f.tipGap.All(Finite))&&tip<=.0015f&&final.Min(f=>f.handKeybedGap)>=-.0015f&&final.Min(f=>f.handDeskGap)>=-.0015f,"FK replay lost actual keybed/desktop hand contact.");
            Require(final.Min(f=>Mathf.Min(f.leftSole,f.rightSole))>=-.002f&&final.Max(f=>Mathf.Max(f.leftSole,f.rightSole))<=.008f&&loop<.002f,"FK replay lost floor contact or loop closure.");
        }
        private static void HumanRepresentation(GameObject prefab,Scene scene,AnimationClip clip,Vector3[][] p,Quaternion[][] q,JObject receipt,string proof)
        {
            using var rig=new Rig(prefab,scene,clip,p,q,1,true);rig.Warm(Dense,true);float joint=0,skin=0,rotation=0;var frames=new Frame[Dense+1];var jointErrors=new float[Bones.Length];var skinErrors=new float[rig.skins.Count];
            for(int i=0;i<=Dense;i++)
            {
                var before=rig.Read(i/(float)Dense);var vertices=rig.skins.Select(s=>(Vector3[])s.vertices.Clone()).ToArray();var pose=before.pose;
                rig.human.SetHumanPose(ref pose);var after=rig.Read(i/(float)Dense);frames[i]=after;
                for(int j=0;j<Bones.Length;j++)jointErrors[j]=Mathf.Max(jointErrors[j],Vector3.Distance(before.joints[j],after.joints[j]));
                joint=Mathf.Max(joint,Enumerable.Range(0,Bones.Length).Max(j=>Vector3.Distance(before.joints[j],after.joints[j])));
                rotation=Mathf.Max(rotation,new[]{5,6,9,10,13,16}.Max(j=>Quaternion.Angle(before.rotations[j],after.rotations[j])));
                for(int tier=0;tier<vertices.Length;tier++)
                {
                    for(int v=0;v<vertices[tier].Length;v++){float error=Vector3.Distance(vertices[tier][v],rig.skins[tier].vertices[v]);skin=Mathf.Max(skin,error);skinErrors[tier]=Mathf.Max(skinErrors[tier],error);}
                    if(i<Dense&&i%(Dense/8)==0)WriteObj(proof+"-tier"+tier+"-phase"+i.ToString("D3")+".obj.gz",rig.skins[tier]);
                }
                if(i<Dense)rig.Step(Dense,true);
            }
            WriteFrames(proof+".json",frames,clip.length);
            receipt["human_pose_get_set_roundtrip"]=new JObject{["joint_error_m"]=joint,["all_skin_error_m"]=skin,["feet_wrist_rotation_error_degrees"]=rotation,["per_joint_error_m"]=JObject.FromObject(Bones.Select((bone,i)=>new{bone=bone.ToString(),error=jointErrors[i]}).ToDictionary(x=>x.bone,x=>x.error)),["per_tier_skin_error_m"]=new JArray(skinErrors)};
            Require(joint<=.002f&&skin<=.002f&&rotation<=1f,"Solved skeleton cannot roundtrip through HumanPose within existing bounds; muscle-only baking is not established.");
        }
        private static Matrix4x4 RootResponse(GameObject prefab,Scene scene,AnimationClip clip,Vector3[][] p,Quaternion[][] q,JObject receipt)
        {
            Frame One(AnimationClip c){using var rig=new Rig(prefab,scene,c,p,q,0,false,false);rig.Warm(Keys,false);return rig.Read(0);}
            var baseline=One(clip);var matrix=Matrix4x4.identity;var rows=new JArray();receipt["root_translation_response"]=rows;
            for(int axis=0;axis<3;axis++)
            {
                var deltas=new List<Vector3>();
                foreach(float sign in new[]{-1f,1f})
                {
                    var altered=Object.Instantiate(clip);
                    try
                    {
                        var binding=Binding(clip,"RootT."+"xyz"[axis]);var original=AnimationUtility.GetEditorCurve(clip,binding);var keys=original.keys;
                        for(int i=0;i<keys.Length;i++){var key=keys[i];key.value+=sign*.001f;keys[i]=key;}var curve=new AnimationCurve(keys){preWrapMode=original.preWrapMode,postWrapMode=original.postWrapMode};AnimationUtility.SetEditorCurve(altered,binding,curve);
                        var after=One(altered);var d=after.joints[0]-baseline.joints[0];float residual=Enumerable.Range(0,Bones.Length).Max(j=>Vector3.Distance(after.joints[j]-baseline.joints[j],d));
                        rows.Add(new JObject{["axis"]=axis,["normalized_delta"]=sign*.001f,["hip_delta_m"]=new JArray(d.x,d.y,d.z),["uniform_body_translation_error_m"]=residual});Require(residual<.00001f,"Root translation probe changed articulation.");deltas.Add(d);
                    }
                    finally{Object.DestroyImmediate(altered);}
                }
                Require((deltas[0]+deltas[1]).magnitude<.00001f,"Signed root translation response is not symmetric.");Vector3 gain=(deltas[1]-deltas[0])/.002f;matrix.SetColumn(axis,new Vector4(gain.x,gain.y,gain.z,0));
            }
            Require(Mathf.Abs(matrix.determinant)>.01f,"Root translation response is singular.");return matrix;
        }
        private static float CompareSkins(GameObject prefab,Scene scene,AnimationClip source,AnimationClip candidate,Vector3[][] p,Quaternion[][] q)
        {
            using var a=new Rig(prefab,scene,source,p,q,1,true);using var b=new Rig(prefab,scene,candidate,p,q,0,false,false);a.Warm(Dense,true);b.Warm(Dense,false);float error=0;
            for(int i=0;i<=Dense;i++){a.Read(i/(float)Dense);b.Read(i/(float)Dense);for(int t=0;t<a.skins.Count;t++)for(int v=0;v<a.skins[t].vertices.Length;v++)error=Mathf.Max(error,Vector3.Distance(a.skins[t].vertices[v],b.skins[t].vertices[v]));if(i<Dense){a.Step(Dense,true);b.Step(Dense,false);}}return error;
        }
        private static EditorCurveBinding Binding(AnimationClip clip,string name){var bindings=AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Animator)&&b.propertyName==name).ToArray();Require(bindings.Length==1,"Expected unique Humanoid curve "+name);return bindings[0];}
        private static void ValidateCurveScope(AnimationClip source,AnimationClip candidate,int[] muscles,JObject receipt)
        {
            var a=AnimationUtility.GetCurveBindings(source);var b=AnimationUtility.GetCurveBindings(candidate);Require(a.Length==b.Length,"Binding count changed.");var allowed=muscles.Select(i=>HumanTrait.MuscleName[i]).Concat(new[]{"RootT.x","RootT.y","RootT.z"}).ToArray();var changed=new JArray();
            foreach(var binding in a){var old=AnimationUtility.GetEditorCurve(source,binding);var next=AnimationUtility.GetEditorCurve(candidate,binding);Require(next!=null,"Curve missing.");if(!old.keys.SequenceEqual(next.keys)||old.preWrapMode!=next.preWrapMode||old.postWrapMode!=next.postWrapMode){Require(binding.type==typeof(Animator)&&allowed.Contains(binding.propertyName),"Unexpected curve change "+binding.propertyName);changed.Add(binding.propertyName);}}receipt["changed_curves"]=changed;
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
        private static Frame[] Sample(GameObject prefab,Scene scene,AnimationClip source,Vector3[][] positions,Quaternion[][] rotations,float weight,bool pass,int steps,string stem,bool footIk=true)
        {
            using var rig=new Rig(prefab,scene,source,positions,rotations,weight,pass,footIk);rig.Warm(steps,pass);var frames=new Frame[steps+1];
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
        private static void WriteFrames(string path,Frame[] frames,float length)=>File.WriteAllText(path,JsonConvert.SerializeObject(new{clip_seconds=length,source_rate=1,joints=Bones.Select(b=>b.ToString()),coordinates="root-local metres, actual weighted skin vertices across all3world tiers",frames=frames.Select(f=>new{f.phase,f.nativeTime,f.callbacks,f.leftSole,f.rightSole,f.handDeskGap,f.handKeybedGap,joints=f.joints.Select(A),rotations=f.rotations.Select(Q),tipLowest=f.tipLowest.Select(A),f.tipGap,targets=f.targets.Select(A),targetRotations=f.targetRotations.Select(Q),human_body_position=A(f.pose.bodyPosition),human_body_rotation=Q(f.pose.bodyRotation),muscles=f.pose.muscles})},Formatting.Indented));
        private static void WriteObj(string path,Skin skin)
        {
            using var file=File.Create(path);using var zip=new GZipStream(file,System.IO.Compression.CompressionLevel.Fastest);using var writer=new StreamWriter(zip);
            string N(float v)=>v.ToString("R",CultureInfo.InvariantCulture);foreach(var v in skin.vertices)writer.WriteLine($"v {N(v.x)} {N(v.y)} {N(v.z)}");
            var triangles=skin.renderer.sharedMesh.triangles;for(int i=0;i<triangles.Length;i+=3)writer.WriteLine($"f {triangles[i]+1} {triangles[i+1]+1} {triangles[i+2]+1}");
        }
        private static Dictionary<string,string> Snapshot()=>new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Where(p=>!p.Replace('\\','/').StartsWith(Stage,StringComparison.Ordinal)&&p!=Stage.TrimEnd('/')+".meta").ToDictionary(p=>p,Hash);
        private static string Hash(string p){using var sha=SHA256.Create();using var f=File.OpenRead(p);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        private static Vector3 V(JToken a){var x=a.Values<float>().ToArray();return new Vector3(x[0],x[1],x[2]);}
        private static float[] A(Vector3 v)=>new[]{v.x,v.y,v.z};private static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
        private static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        private static void Require(bool okay,string reason){if(!okay)throw new InvalidOperationException("Typing hand review: "+reason);}
    }
}
