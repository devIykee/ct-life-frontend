// Editor-only geometry comparison. No source, clip, controller, runtime or profile writes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CTLife.EditorTools
{
    public static class RainRestSprintFootRollReview
    {
        private const string Source="Assets/Art/characters/authored/MotionReview20261006/RainRestSprintCompareV1/UAL_SourceSprint_Original0.fbx";
        private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const int Samples=480;
        private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException("Rain rest foot-roll: "+message);}
        private static T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,Fields).GetValue(obj);
        private static object Call(object obj,string name,params object[] args)
        {try{return obj.GetType().GetMethod(name,Fields).Invoke(obj,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}}
        private static float Knee(Vector3 hip,Vector3 knee,Vector3 ankle)=>Vector3.Angle(hip-knee,ankle-knee);
        private static float TargetKnee(Vector3 hip,Vector3 knee,Vector3 ankle,float lift)
        {
            double a=Vector3.Distance(hip,knee),b=Vector3.Distance(knee,ankle),c=Vector3.Distance(hip,ankle+Vector3.up*lift);
            Need(c<a+b+.00001&&c>Math.Abs(a-b)-.00001,"Unreachable fixed-length analytical target.");
            return (float)(Math.Acos(Math.Max(-1,Math.Min(1,(a*a+b*b-c*c)/(2*a*b))))*180/Math.PI);
        }
        public static void Capture()
        {
            string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/character-candidates/motion-review-20261006"));
            string output=Path.Combine(evidence,"native-rain-rest-foot-roll-v1");Directory.CreateDirectory(output);
            var paths=new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).ToArray();
            var before=paths.ToDictionary(p=>p,Hash);var receipt=new JObject{["revision"]="rain-exact-rest-sprint-bounded-heel-roll-geometry-v1",["captured"]=false,["accepted"]=false,
                ["source_selection"]="Previously inspected licensed rest-Avatar Sprint Original0,1x at4.5m/s. This is a distinct source candidate, not a claim to repair old copied-Avatar Sprint.",
                ["purpose"]="Measure actual all-LOD sole response to at most4degree foot pitch before any muscle-curve authoring. Test whether it can retain the existing15degree knee adjustment gate without root/body/rig translation. No clip saved.",
                ["scope"]="Geometry response and analytical fixed-length feasibility only; leg solve, muscle roundtrip, dense persistent clip, support skating, source-preserving flight and continuous view remain required.",
                ["rows"]=new JArray()};
            var scene=EditorSceneManager.NewPreviewScene();object rig=null;
            try
            {
                Need(Hash(Source)=="aa7fd9448fb1694823ecad4d4d1c168c74b90be8dd4d30ca3064d4adf94f9161"&&Hash(Source+".meta")=="80e06ea73b480100cd23f2c229ceabd3e373ee42bcd91887b3d240763d46b760","Inspected exact rest source/import settings changed.");
                Need(Hash("Assets/Scripts/Editor/RainRestSprintComparison.cs")=="2142bd24b824302e12c69568a6e6d9a71b526dac190d46104f3b96faedfa23b5","Frozen proven native sampler changed.");
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_rain"));
                Need(AssetDatabase.GetAssetPath(entry.prefab)=="Assets/Art/characters/authored/Rain/BreezeWorld.prefab"&&Hash(AssetDatabase.GetAssetPath(entry.prefab))=="56682e42635ccc83b52d0e7f3817bbb6473dc34f80266774a23d9df38fe94105","Exact Rain world prefab changed.");
                var avatar=entry.prefab.GetComponentsInChildren<Animator>(true).Single().avatar;
                Need(AssetDatabase.GetAssetPath(avatar)=="Assets/Art/characters/authored/Rain/RainAdult_Creator.fbx"&&avatar.name=="RainAdult_CreatorAvatar","Own Rain Avatar changed.");
                var matrix=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"native-rest-contact-matrix/rain-original-y-footik.json")));
                var patches=new Dictionary<string,int[][]>();string[] names={"Foot.L","Toes.L","Foot.R","Toes.R"};
                foreach(var skin in matrix["rest_surfaces"])patches.Add((string)skin["skin"],names.Select(n=>skin["patches"].Single(p=>(string)p["name"]==n)["fixed_vertex_indices"].Values<int>().ToArray()).ToArray());
                var sampler=typeof(RainRestSprintComparison);sampler.GetField("patchIndices",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,patches);
                var clip=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));var type=sampler.GetNestedType("Rig",BindingFlags.NonPublic);
                rig=Activator.CreateInstance(type,Fields,null,new object[]{entry.prefab,scene,"loco_run",clip,4.5f,4.5f,false},null);Call(rig,"Warm",Samples);
                var animator=Field<Animator>(rig,"animator");var archive=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"native-rain-rest-sprint-compare-v1/rain-rest-source-original0.json")));float sourceError=0,sourceSole=0,sourceRestore=0;
                for(int i=0;i<=Samples;i++)
                {
                    float phase=i/(float)Samples;var frame=Call(rig,"Read",phase,false);var joints=Field<Vector3[]>(frame,"joints");var expected=archive["frames"][i];
                    for(int j=0;j<joints.Length;j++){var p=expected["joints"][j].Values<float>().ToArray();sourceError=Mathf.Max(sourceError,Vector3.Distance(joints[j],new Vector3(p[0],p[1],p[2])));}
                    sourceSole=Mathf.Max(sourceSole,Mathf.Abs(Field<float>(frame,"leftSole")-(float)expected["leftSole"]),Mathf.Abs(Field<float>(frame,"rightSole")-(float)expected["rightSole"]));
                    for(int side=0;side<2;side++)
                    {
                        string sideName=side==0?"left":"right";float sole=Field<float>(frame,sideName+"Sole");if(sole>.008f)continue;
                        int h=side==0?5:9,k=h+1,a=h+2;var foot=animator.GetBoneTransform(side==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                        var toe=animator.GetBoneTransform(side==0?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);var rotation=foot.rotation;
                        Vector3 forward=Vector3.ProjectOnPlane(toe.position-foot.position,Vector3.up);Need(forward.magnitude>.005f,"Pitch axis is poorly determined near stance.");var axis=Vector3.Cross(Vector3.up,forward).normalized;
                        var options=new JArray();
                        try
                        {
                            for(int half=-8;half<=8;half++)
                            {
                                float angle=half*.5f;foot.rotation=Quaternion.AngleAxis(angle,axis)*rotation;var tilted=Call(rig,"Read",phase,false);float changed=Field<float>(tilted,sideName+"Sole"),lift=Mathf.Max(0,.0005f-changed);
                                float kneeChange=Mathf.Abs(TargetKnee(joints[h],joints[k],joints[a],lift)-Knee(joints[h],joints[k],joints[a]));var posed=Field<Vector3[]>(tilted,"joints");
                                float toeShift=Vector2.Distance(new Vector2(posed[a+1].x,posed[a+1].z),new Vector2(joints[a+1].x,joints[a+1].z));
                                options.Add(new JObject{["pitch_degrees"]=angle,["measured_all_tier_sole_before_leg_lift_m"]=changed,["required_ankle_lift_m"]=lift,["analytic_knee_change_degrees"]=kneeChange,["toe_xz_shift_m"]=toeShift,["analytically_within15degree_knee_gate"]=kneeChange<=15f});
                            }
                        }
                        finally{foot.rotation=rotation;}
                        var restored=Call(rig,"Read",phase,false);sourceRestore=Mathf.Max(sourceRestore,Mathf.Abs(Field<float>(restored,sideName+"Sole")-sole));
                        ((JArray)receipt["rows"]).Add(new JObject{["phase"]=phase,["side"]=sideName,["source_sole_m"]=sole,["original_knee_degrees"]=Knee(joints[h],joints[k],joints[a]),["pitch_axis"]=new JArray(axis.x,axis.y,axis.z),["options"]=options});
                    }
                    if(i<Samples)Call(rig,"Step",Samples);
                }
                receipt["source_to_independent_native_joint_error_m"]=sourceError;receipt["source_to_independent_native_sole_error_m"]=sourceSole;receipt["restored_rotation_sole_error_m"]=sourceRestore;
                Need(sourceError<=.002f&&sourceSole<=.002f&&sourceRestore<=.00001f,"Independent source or restoration did not reproduce the inspected native source.");
                receipt["captured"]=true;
            }
            catch(Exception e){receipt["failure"]=e.ToString();throw;}
            finally
            {
                (rig as IDisposable)?.Dispose();EditorSceneManager.ClosePreviewScene(scene);
                var after=new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
                var drift=before.Where(p=>!after.TryGetValue(p.Key,out var h)||h!=p.Value).Select(p=>p.Key).Concat(after.Keys.Where(p=>!before.ContainsKey(p))).ToArray();receipt["protected_drift"]=new JArray(drift);
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());Need(drift.Length==0,"Protected assets changed.");
            }
        }
    }
}
