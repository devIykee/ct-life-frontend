#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace CTLife.EditorTools
{
    // Read-only source import boundary: no import, save, Avatar build, axis flip or target actor change.
    public static class KayKitSourcePoseDiagnostic
    {
        const string Base="Assets/Art/characters/authored/MotionReview20261006/";
        const string Generic=Base+"KayKitRawRunV2/Knight.fbx",Human=Base+"KayKitRawRunV3/Knight.fbx";
        const string External="../ct-life-evidence/art/character-candidates/motion-review-20261006/regular-run-contact-20261007/kaykit-source-diagnostic-v1";
        static readonly Dictionary<string,string> Pins=new Dictionary<string,string>{
            {Generic,"b25e35cc7eaaa3c7c103f10a4f41fde1cbd81936fede9aa9ae4efbd29d40ba1b"},
            {Generic+".meta","28af5729e5c016758255d1415293556e950d8696c7673acb055f3a32a6c77949"},
            {Human,"b25e35cc7eaaa3c7c103f10a4f41fde1cbd81936fede9aa9ae4efbd29d40ba1b"},
            {Human+".meta","ca775f4ce66fe7612a053e148e4bce9fca591952c6d99a680f6bd2db4546ce89"},
            {Base+"KayKitRawRunV3/StageReceipt.json","7c5ef3ce28102a2731f777e04ff678ba021645a7245063e0df2185493573ae44"},
            {"Assets/Scripts/Editor/LocomotionQualityProbe.cs","c9c9f1d1629ccd7c1f8511a1c3398a27d768aaa2aa5ebb4a77c9f8555302fa3a"}};
        static readonly string[] Names={"hips","spine","chest","head","upperarm.l","lowerarm.l","wrist.l","hand.l","upperarm.r","lowerarm.r","wrist.r","hand.r","upperleg.l","lowerleg.l","foot.l","toes.l","upperleg.r","lowerleg.r","foot.r","toes.r"};
        public static void Capture()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode,"Must run outside Play mode.");
            foreach(var p in Pins)Need(Hash(p.Key)==p.Value,"Source dependency changed: "+p.Key);
            var gi=(ModelImporter)AssetImporter.GetAtPath(Generic);var hi=(ModelImporter)AssetImporter.GetAtPath(Human);
            Need(gi.animationType==ModelImporterAnimationType.Generic&&gi.importAnimation&&gi.isReadable,"Generic persisted source settings differ.");
            Need(hi.animationType==ModelImporterAnimationType.Human&&hi.importAnimation&&hi.isReadable,"Human persisted source settings differ.");
            string output=Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath,"../..")),External,"native-source-pose");
            Need(!Directory.Exists(output),"Preserve prior evidence; output already exists.");Directory.CreateDirectory(output);
            var before=Snapshot();var rows=new JArray();var receipt=new JObject{["status"]="failed_source_diagnostic",["input_hashes"]=JObject.FromObject(Pins),["helper_sha256"]=Hash("Assets/Scripts/Editor/KayKitSourcePoseDiagnostic.cs")};
            try
            {
                receipt["generic_authored_tpose"]=ReadReference();
                receipt["v3_stage_reference"]=JObject.Parse(File.ReadAllText(Base+"KayKitRawRunV3/StageReceipt.json"))["source_reference_transforms"].DeepClone();
                foreach(string mode in new[]{"generic_sample","generic_animator","human_own_avatar"})
                foreach(string clip in new[]{"Running_A","Running_B"})rows.Add(CaptureCase(mode,clip,output));
                Need(rows.Count==6,"Six source playback cases required.");receipt["status"]="captured_source_boundary_not_motion_accepted";
            }
            catch(Exception e){receipt["failure"]=e.ToString();throw;}
            finally
            {
                var after=Snapshot();var drift=before.Where(p=>!after.TryGetValue(p.Key,out var h)||h!=p.Value).Select(p=>p.Key).ToArray();var added=after.Keys.Except(before.Keys).ToArray();
                receipt["cases"]=rows;receipt["protected_count"]=before.Count;receipt["protected_drift"]=new JArray(drift);receipt["unexpected_added"]=new JArray(added);
                receipt["limits"]="Source own rig only. Generic SampleAnimation is explicit keyed-pose sampling; Generic Animator plays first nonloop cycle; Human own-Avatar Animator plays same imported source for two warm cycles then one measured cycle. All IK/root motion off. No imports/saves, reference changes, production assignment, adult retarget repair or movement/naturalness acceptance.";
                if(drift.Length>0||added.Length>0)receipt["status"]="failed_protected_tree_changed";
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());Need(drift.Length==0&&added.Length==0,"Protected source tree changed; no restore attempted.");
            }
        }
        static JObject ReadReference()
        {
            var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Generic);root=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(root,scene);root.name=prefab.name;
                foreach(var a in root.GetComponentsInChildren<Animator>(true))a.enabled=false;
                var clip=AssetDatabase.LoadAllAssetsAtPath(Generic).OfType<AnimationClip>().Single(c=>c.name=="T-Pose");clip.SampleAnimation(root,0);
                return new JObject{["clip"]=clip.name,["seconds"]=clip.length,["transforms"]=JArray.FromObject(root.GetComponentsInChildren<Transform>(true).Select(t=>new{name=t.name,parent=t.parent!=null?t.parent.name:null,localPosition=V(t.localPosition),localRotation=Q(t.localRotation),localScale=V(t.localScale),worldPosition=V(t.position),worldRotation=Q(t.rotation)}))};
            }
            finally{if(root!=null)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static JObject CaptureCase(string mode,string name,string output)
        {
            bool sample=mode=="generic_sample",human=mode=="human_own_avatar";string modelPath=human?Human:Generic;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);var clip=AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Single(c=>c.name==name);
            Need(clip.humanMotion==human,"Clip import category differs.");Need(Mathf.Abs(clip.length-(name=="Running_A"?.8f:32f/30))<.002f,"Source duration differs.");
            var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;AnimatorController controller=null;AnimatorStateMachine machine=null;AnimatorState state=null;HumanPoseHandler poseHandler=null;var meshes=new List<Mesh>();
            try
            {
                root=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(root,scene);root.name=prefab.name;
                var animator=root.GetComponentsInChildren<Animator>(true).Single();animator.enabled=false;
                Need(animator.avatar!=null&&animator.avatar.isValid&&animator.avatar.isHuman==human,"Source own Avatar mismatch.");
                var all=root.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.ToArray());foreach(string n in Names)Need(all.TryGetValue(n,out var a)&&a.Length==1,"Source joint missing/ambiguous: "+n);
                Vector3 P(string n)=>root.transform.InverseTransformPoint(all[n][0].position);
                var before=JArray.FromObject(root.GetComponentsInChildren<Transform>(true).Select(t=>new{name=t.name,parent=t.parent!=null?t.parent.name:null,local_position=V(t.localPosition),local_rotation=Q(t.localRotation),local_scale=V(t.localScale)}));
                var restJoints=JObject.FromObject(Names.ToDictionary(n=>n,n=>V(P(n))));
                var frames=new JArray();var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);foreach(var skin in skins){Need(skin.sharedMesh.isReadable,"Source mesh must be readable.");meshes.Add(new Mesh());}
                int warm=human?240:0;float dt=clip.length/120;float start=0;
                if(!sample)
                {
                    controller=new AnimatorController{name="UNSAVED_SOURCE_BOUNDARY_"+mode};machine=new AnimatorStateMachine();state=machine.AddState("source_run");state.motion=clip;state.speed=1;state.iKOnFeet=false;machine.defaultState=state;
                    controller.layers=new[]{new AnimatorControllerLayer{name="Base Layer",defaultWeight=1,stateMachine=machine,iKPass=false}};
                    animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.speed=1;animator.enabled=true;animator.Rebind();animator.Play("source_run",0,0);animator.Update(0);
                    for(int i=0;i<warm;i++)animator.Update(dt);start=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;Need(Mathf.Abs(start-warm/120f)<.001f,"Native warm time differs.");
                    if(human)poseHandler=new HumanPoseHandler(animator.avatar,animator.transform);
                }
                var humanPose=new HumanPose();
                for(int i=0;i<=120;i++)
                {
                    if(sample)clip.SampleAnimation(root,clip.length*i/120f);
                    float normalized=sample?i/120f:animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    if(!sample)Need(Mathf.Abs(normalized-start-i/120f)<.001f,"Native Animator time does not advance.");
                    var joints=Names.ToDictionary(n=>n,n=>P(n));Need(joints.Values.All(p=>!float.IsNaN(p.sqrMagnitude)&&!float.IsInfinity(p.sqrMagnitude)),"Nonfinite joint.");
                    var toe=(P("toes.l")+P("toes.r")-P("foot.l")-P("foot.r"))*.5f;var axis=P("head")-P("hips");
                    var frame=new JObject{["phase"]=i/120f,["native_normalized_time"]=sample?JValue.CreateNull():new JValue(normalized),["joints"]=JObject.FromObject(joints.ToDictionary(p=>p.Key,p=>V(p.Value))),["toe_forward"]=JArray.FromObject(V(toe)),["hips_to_head"]=JArray.FromObject(V(axis)),["root_position"]=JArray.FromObject(V(root.transform.position)),["root_rotation"]=JArray.FromObject(Q(root.transform.rotation)),["left_minus_right_upperleg"]=JArray.FromObject(V(P("upperleg.l")-P("upperleg.r"))),["left_wrist_minus_upperarm_y"]=P("wrist.l").y-P("upperarm.l").y,["right_wrist_minus_upperarm_y"]=P("wrist.r").y-P("upperarm.r").y};
                    frame["joint_rotations"]=JObject.FromObject(Names.ToDictionary(n=>n,n=>new{local=Q(all[n][0].localRotation),world=Q(all[n][0].rotation)}));
                    if(human){poseHandler.GetHumanPose(ref humanPose);frame["human_body_position"]=JArray.FromObject(V(humanPose.bodyPosition));frame["human_body_rotation"]=JArray.FromObject(Q(humanPose.bodyRotation));frame["human_muscles"]=JArray.FromObject(humanPose.muscles);}
                    if(i<120&&i%30==0)
                    {
                        var objs=new JArray();for(int s=0;s<skins.Length;s++)
                        {
                            var skin=skins[s];var mesh=meshes[s];skin.BakeMesh(mesh);var m=root.transform.worldToLocalMatrix*skin.localToWorldMatrix;var vs=mesh.vertices.Select(v=>m.MultiplyPoint3x4(v)).ToArray();var ns=mesh.normals.Select(n=>root.transform.InverseTransformDirection(skin.transform.TransformDirection(n))).ToArray();
                            string file=mode+"-"+name+"-skin"+s+"-phase"+i.ToString("000")+".obj.gz";var method=typeof(LocomotionQualityProbe).GetMethod("WriteObj",BindingFlags.Static|BindingFlags.NonPublic);Need(method!=null,"Pinned native OBJ writer missing.");method.Invoke(null,new object[]{Path.Combine(output,file),vs,ns,skin.sharedMesh.uv,skin.sharedMesh.triangles});objs.Add(new JObject{["file"]=file,["sha256"]=Hash(Path.Combine(output,file)),["vertices"]=vs.Length});
                        }frame["native_geometry"]=objs;
                    }
                    frames.Add(frame);if(!sample&&i<120)animator.Update(dt);
                }
                string filename=mode+"-"+name+".json";var result=new JObject{["mode"]=mode,["clip"]=name,["path"]=modelPath,["seconds"]=clip.length,["clip_is_looping"]=clip.isLooping,["clip_human_motion"]=clip.humanMotion,["own_avatar_path"]=AssetDatabase.GetAssetPath(animator.avatar),["warm_cycles"]=warm/120,["foot_ik"]=false,["hand_ik"]=false,["root_motion"]=false,["rest_transforms"]=before,["rest_joints"]=restJoints,["rest_scope"]="Persisted imported prefab before playback, not a newly sampled Human T-pose",["frames"]=frames};
                File.WriteAllText(Path.Combine(output,filename),result.ToString());return new JObject{["mode"]=mode,["clip"]=name,["frames"]=frames.Count,["file"]=filename,["sha256"]=Hash(Path.Combine(output,filename))};
            }
            finally
            {
                poseHandler?.Dispose();foreach(var m in meshes)if(m!=null)Object.DestroyImmediate(m);if(root!=null)Object.DestroyImmediate(root);if(controller!=null)Object.DestroyImmediate(controller);if(state!=null)Object.DestroyImmediate(state);if(machine!=null)Object.DestroyImmediate(machine);EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        static Dictionary<string,string> Snapshot()=>new[]{"Assets","ProjectSettings","Packages"}.SelectMany(r=>Directory.GetFiles(r,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
        static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
        static string Hash(string p){using var sha=SHA256.Create();using var f=File.OpenRead(p);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        static void Need(bool value,string message){if(!value)throw new InvalidDataException("KayKit source diagnostic: "+message);}
    }
}
#endif
