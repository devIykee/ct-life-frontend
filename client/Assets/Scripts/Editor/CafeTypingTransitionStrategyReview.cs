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
    public static class CafeTypingTransitionStrategyReview
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
            public Vector3[][] vertices;public float[][] soles;
        }
        private sealed class Skin
        {
            public SkinnedMeshRenderer renderer;public Mesh baked;public int[][] feet;
        }
        private sealed class Rig:IDisposable
        {
            public AvatarView view;public Animator animator;public readonly List<Skin> skins=new List<Skin>();
            public Rig(CharacterRegistry.Entry entry,RuntimeAnimatorController controller,Scene scene)
            {
                try
                {
                    view=AvatarFactory.Create("Cafe controller review",entry.key.Split('|'),false);
                    SceneManager.MoveGameObjectToScene(view.gameObject,scene);view.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    animator=view.Animator;Need(animator!=null&&animator.avatar!=null&&animator.avatar.isHuman,"Expected actual world Humanoid.");
                    animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.speed=1;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;animator.Rebind();animator.Update(0);
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
                var info=animator.GetCurrentAnimatorStateInfo(0);var p=new Pose{time=info.normalizedTime,state=info.shortNameHash,transition=animator.IsInTransition(0),
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

        private const string TypingContact="review_typing_contact_ik";
        public static void Capture()
        {
            Need(Application.isBatchMode,"Dedicated graphics batch required.");
            string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/character-candidates/motion-review-20261006"));
            string output=Path.Combine(evidence,"native-cafe-typing-strategies-v1");Directory.CreateDirectory(output);
            var before=Snapshot();var cases=new JArray();bool captured=true;
            var report=new JObject{["revision"]="fitted-typing-transition-strategies-v1",["capture_pass"]=false,["cases"]=cases,
                ["scope"]="Isolated fixed-root native Animator comparison: existing social bridge versus exact persisted typing clip with FootIK ON only in a transient contact state. No saved controller/clip/prefab/runtime changes.",
                ["limitations"]="Choreography probe only. No LocalPlayer/RemotePlayer queue implementation, moving actor, furniture, culling/resume, navigation or device latency acceptance. A social bridge adds a second fixed0.2s crossfade; immediate local NavMesh movement would need separate review for sitting-slide. Contact-state phase handoffs require full-skin continuity."};
            try
            {
                string referencePath=Path.Combine(evidence,"native-cafe-seat-controller-v1/receipt.json");var reference=JObject.Parse(File.ReadAllText(referencePath));
                Need((bool?)reference["all_pass"]==true&&!reference["protected_drift"].Any(),"Full native controller comparison required.");report["controller_reference_sha256"]=Hash(referencePath);
                var manifest=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"cafe-seat-integration/baseline-sha256.json")));
                foreach(var property in ((JObject)manifest["files"]).Properties().Where(p=>p.Name.StartsWith("client/Assets/Resources/Characters/",StringComparison.Ordinal)||p.Name.StartsWith("client/Assets/Art/characters/",StringComparison.Ordinal)))
                    Need(Hash(property.Name.Substring("client/".Length))==(string)property.Value,"Frozen character dependency changed: "+property.Name);
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");Need(registry!=null,"Character registry missing.");
                foreach(string character in new[]{"snow","rain"})
                {
                    var row=new JObject{["character"]=character,["capture_pass"]=false};cases.Add(row);var scene=EditorSceneManager.NewPreviewScene();AnimatorController temporary=null;
                    try
                    {
                        var accepted=reference["cases"].Single(c=>(string)c["character"]==character);Need((bool?)accepted["all_pass"]==true,"Per-look full native reference did not pass.");
                        foreach(string key in new[]{"controller","baseline_controller","social","typing"})
                        {
                            string path=(string)accepted[key+(key=="social"||key=="typing"?"_clip":"_asset")];Need(Hash(path)==(string)accepted[key+"_sha256"],"Guarded source bytes changed: "+key);row[key+"_asset"]=path;row[key+"_sha256"]=Hash(path);
                        }
                        var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+character));Need(AssetDatabase.GetAssetPath(entry.controllerOverride)==(string)accepted["baseline_controller_asset"],"Registry binding changed.");
                        Need(entry.key==(string)accepted["look_key"]&&AssetDatabase.GetAssetPath(entry.prefab)==(string)accepted["world_prefab_asset"],"Exact accepted look/world prefab changed.");
                        var ownAvatar=entry.prefab.GetComponentsInChildren<Animator>(true).Single().avatar;
                        Need(ownAvatar!=null&&AssetDatabase.GetAssetPath(ownAvatar)==(string)accepted["own_avatar_asset"]&&ownAvatar.name==(string)accepted["own_avatar_name"],"Accepted per-look Humanoid Avatar changed.");
                        row["look_key"]=entry.key;row["world_prefab_asset"]=AssetDatabase.GetAssetPath(entry.prefab);row["own_avatar_asset"]=AssetDatabase.GetAssetPath(ownAvatar);row["own_avatar_name"]=ownAvatar.name;
                        var full=AssetDatabase.LoadAssetAtPath<AnimatorController>((string)accepted["controller_asset"]);temporary=TemporaryController(full);
                        row["temporary_baseline_replay"]=Baseline(entry,full,temporary,scene);
                        row["typing_foot_ik_on_off"]=ContactDifference(entry,temporary,scene);
                        var sequences=new JArray();row["sequences"]=sequences;
                        foreach(string id in new[]{"bridge-entry","bridge-exit","bridge-entry-cancel","bridge-exit-reseat","bridge-exit-run","contact-entry","contact-exit","contact-entry-cancel","contact-exit-reseat","contact-exit-run"})
                            sequences.Add(Sequence(entry,temporary,scene,output,character,id));
                        bool bridge=sequences.Where(x=>((string)x["id"]).StartsWith("bridge-")&&!((string)x["id"]).EndsWith("-run")).All(x=>(bool)x["contact_pass"]&&(bool)x["final_state_pass"]);
                        bool contact=(bool)row["typing_foot_ik_on_off"]["pose_continuity_pass"]&&sequences.Where(x=>((string)x["id"]).StartsWith("contact-")&&!((string)x["id"]).EndsWith("-run")).All(x=>(bool)x["contact_pass"]&&(bool)x["final_state_pass"]&&(bool)x["handoff_continuity_pass"]);
                        row["bridge_numeric_candidate_pass"]=bridge;row["contact_numeric_candidate_pass"]=contact;row["capture_pass"]=true;
                    }
                    catch(Exception ex){captured=false;row["failure"]=ex.ToString();Debug.LogError("CTLIFE [typing-transition-strategy] "+character+": "+ex);}
                    finally
                    {
                        if(temporary!=null){var machine=temporary.layers[0].stateMachine;var states=machine.states.Select(s=>s.state).ToArray();Object.DestroyImmediate(temporary);foreach(var state in states)Object.DestroyImmediate(state);Object.DestroyImmediate(machine);}
                        EditorSceneManager.ClosePreviewScene(scene);
                    }
                }
            }
            catch(Exception ex){captured=false;report["failure"]=ex.ToString();throw;}
            finally
            {
                var after=Snapshot();var drift=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out string a)||!after.TryGetValue(k,out string b)||a!=b).ToArray();report["protected_drift"]=new JArray(drift);
                report["capture_pass"]=captured&&drift.Length==0;report["state"]=captured&&drift.Length==0?"strategies-captured-not-runtime-or-visual-accepted":"failed";
                report["both_looks_bridge_numeric_pass"]=captured&&cases.Count==2&&cases.All(c=>(bool?)c["bridge_numeric_candidate_pass"]==true);
                report["both_looks_contact_numeric_pass"]=captured&&cases.Count==2&&cases.All(c=>(bool?)c["contact_numeric_candidate_pass"]==true);
                File.WriteAllText(Path.Combine(output,"receipt.json"),report.ToString());Need(drift.Length==0,"Protected source drift.");
            }
            Need(captured,"Strategy capture incomplete. No runtime candidate was assigned.");
        }
        private static AnimatorController TemporaryController(AnimatorController source)
        {
            var controller=new AnimatorController();controller.parameters=source.parameters;var machine=new AnimatorStateMachine();
            foreach(string id in new[]{"loco_idle","loco_walk","loco_run",CafeSeatMotionProfile.SocialState,CafeSeatMotionProfile.TypingState,TypingContact})
            {
                string original=id==TypingContact?CafeSeatMotionProfile.TypingState:id;var a=source.layers.Single().stateMachine.states.Single(s=>s.state.name==original).state;var b=machine.AddState(id);
                b.motion=a.motion;b.speed=a.speed;b.speedParameter=a.speedParameter;b.speedParameterActive=a.speedParameterActive;b.cycleOffset=a.cycleOffset;b.cycleOffsetParameter=a.cycleOffsetParameter;b.cycleOffsetParameterActive=a.cycleOffsetParameterActive;b.iKOnFeet=id==TypingContact||a.iKOnFeet;b.writeDefaultValues=a.writeDefaultValues;b.mirror=a.mirror;b.mirrorParameter=a.mirrorParameter;b.mirrorParameterActive=a.mirrorParameterActive;b.timeParameter=a.timeParameter;b.timeParameterActive=a.timeParameterActive;
                if(id=="loco_idle")machine.defaultState=b;
            }
            controller.layers=new[]{new AnimatorControllerLayer{name="Base Layer",stateMachine=machine,defaultWeight=1,iKPass=false}};return controller;
        }
        private static float SkinDifference(Pose a,Pose b)
        {
            Need(a.vertices.Length==b.vertices.Length,"Skin count differs.");float result=0;for(int t=0;t<a.vertices.Length;t++){Need(a.vertices[t].Length==b.vertices[t].Length,"Topology differs.");for(int i=0;i<a.vertices[t].Length;i++)result=Mathf.Max(result,Vector3.Distance(a.vertices[t][i],b.vertices[t][i]));}return result;
        }
        private static JArray Baseline(CharacterRegistry.Entry entry,AnimatorController full,AnimatorController temporary,Scene scene)
        {
            var rows=new JArray();foreach(string state in new[]{"loco_idle",CafeSeatMotionProfile.SocialState,CafeSeatMotionProfile.TypingState})
            {
                using var a=new Rig(entry,full,scene);using var b=new Rig(entry,temporary,scene);a.Play(state);b.Play(state);float length=a.animator.GetCurrentAnimatorStateInfo(0).length;
                for(int i=0;i<120;i++){a.Step(length/60);b.Step(length/60);}float difference=0;float start=a.Read().time;
                for(int i=0;i<=60;i++){var pa=a.Read();var pb=b.Read();Need(Mathf.Abs(pa.time-pb.time)<.0001f,"Reference times differ.");difference=Mathf.Max(difference,SkinDifference(pa,pb));if(i<60){a.Step(length/60);b.Step(length/60);}}
                Need(a.Read().time>start+.99f&&difference<=.00001f,"Temporary subset changed original native pose.");rows.Add(new JObject{["state"]=state,["samples"]=61,["maximum_skin_error_m"]=difference});
            }return rows;
        }
        private static JObject ContactDifference(CharacterRegistry.Entry entry,AnimatorController temporary,Scene scene)
        {
            using var a=new Rig(entry,temporary,scene);using var b=new Rig(entry,temporary,scene);a.Play(CafeSeatMotionProfile.TypingState);b.Play(TypingContact);float length=a.animator.GetCurrentAnimatorStateInfo(0).length;
            for(int i=0;i<480;i++){a.Step(length/240);b.Step(length/240);}float skin=0,joint=0,rotation=0,min=float.PositiveInfinity,max=float.NegativeInfinity,start=a.Read().time;
            for(int i=0;i<=240;i++)
            {
                var pa=a.Read();var pb=b.Read();Need(Mathf.Abs(pa.time-pb.time)<.0001f,"ON/OFF native clocks diverged.");skin=Mathf.Max(skin,SkinDifference(pa,pb));
                for(int j=0;j<Bones.Length;j++){joint=Mathf.Max(joint,Vector3.Distance(pa.joints[j],pb.joints[j]));rotation=Mathf.Max(rotation,Quaternion.Angle(pa.rotations[j],pb.rotations[j]));}
                min=Mathf.Min(min,pb.soles.Min(s=>s.Min()));max=Mathf.Max(max,pb.soles.Max(s=>s.Max()));if(i<240){a.Step(length/240);b.Step(length/240);}
            }
            Need(a.Read().time>start+.99f,"ON/OFF comparison did not advance.");return new JObject{["samples"]=241,["skin_error_m"]=skin,["joint_error_m"]=joint,["maximum_joint_rotation_delta_degrees"]=rotation,["contact_min_sole_y_m"]=min,["contact_max_sole_y_m"]=max,["pose_continuity_pass"]=skin<=.002f&&joint<=.002f&&min>=-.002f};
        }
        private static JObject Sequence(CharacterRegistry.Entry entry,AnimatorController controller,Scene scene,string output,string character,string id)
        {
            bool contact=id.StartsWith("contact-"),entrySequence=id.Contains("-entry"),cancel=id.EndsWith("-cancel"),reseat=id.EndsWith("-reseat"),run=id.EndsWith("-run");
            string expected=cancel?"loco_idle":reseat?CafeSeatMotionProfile.TypingState:entrySequence?CafeSeatMotionProfile.TypingState:run?"loco_run":"loco_idle";
            using var rig=new Rig(entry,controller,scene);using var visual=new TransitionVisual(rig);rig.Play(entrySequence?"loco_idle":CafeSeatMotionProfile.TypingState);
            float period=rig.animator.GetCurrentAnimatorStateInfo(0).length;for(int i=0;i<240;i++)rig.Step(period/120);
            var frames=new JArray();var images=new JArray();var events=new JArray();float handoff=0,minimum=float.PositiveInfinity,firstFinal=-1,maximumStep=0;Pose previous=null;JObject previousImage=null;Vector3[] previousVertices=null;
            void Fade(string state,int frame){Need(AnimationLibrary.TryPlay(rig.animator,state,.2f),"Missing choreography state.");events.Add(new JObject{["frame"]=frame,["event"]="fixed crossfade",["state"]=state,["duration_seconds"]=.2f});}
            void Handoff(string state,int frame)
            {
                var before=rig.Read();float phase=rig.animator.GetCurrentAnimatorStateInfo(0).normalizedTime;rig.animator.Play(state,0,phase);rig.animator.Update(0);var after=rig.Read();float difference=SkinDifference(before,after);handoff=Mathf.Max(handoff,difference);
                events.Add(new JObject{["frame"]=frame,["event"]="phase-matched state handoff",["state"]=state,["native_phase"]=phase,["skin_displacement_m"]=difference});
            }
            string queued=null;
            if(contact)
            {
                if(entrySequence){Fade(TypingContact,0);queued=CafeSeatMotionProfile.TypingState;}
                else{Handoff(TypingContact,0);rig.view.SetSpeed(run?4.5f:0);Fade(run?"loco_run":"loco_idle",0);}
            }
            else{Fade(CafeSeatMotionProfile.SocialState,0);queued=entrySequence?CafeSeatMotionProfile.TypingState:run?"loco_run":"loco_idle";}
            for(int i=0;i<=96;i++)
            {
                if(i==8&&cancel){queued=null;Fade("loco_idle",i);events.Add(new JObject{["frame"]=i,["event"]="latest intent: cancel entry"});}
                if(i==8&&reseat){queued=contact?CafeSeatMotionProfile.TypingState:null;Fade(contact?TypingContact:CafeSeatMotionProfile.TypingState,i);events.Add(new JObject{["frame"]=i,["event"]="latest intent: reseat replaces stand"});}
                string bridgeState=contact?TypingContact:CafeSeatMotionProfile.SocialState;
                if(i>0&&queued!=null&&!rig.animator.IsInTransition(0)&&rig.animator.GetCurrentAnimatorStateInfo(0).IsName(bridgeState))
                {
                    if(contact)Handoff(queued,i);else{rig.view.SetSpeed(queued=="loco_run"?4.5f:0);Fade(queued,i);}queued=null;
                }
                var pose=rig.Read();minimum=Mathf.Min(minimum,pose.soles.Min(s=>s.Min()));if(previous!=null)maximumStep=Mathf.Max(maximumStep,pose.joints.Zip(previous.joints,(a,b)=>Vector3.Distance(a,b)).Max());
                if(firstFinal<0&&i>(cancel||reseat?8:0)&&rig.animator.GetCurrentAnimatorStateInfo(0).IsName(expected)&&!rig.animator.IsInTransition(0))firstFinal=i/120f;
                frames.Add(new JObject{["frame"]=i,["seconds"]=i/120f,["native_time"]=pose.time,["state_hash"]=pose.state,["transition"]=pose.transition,["soles"]=new JArray(pose.soles.Select(s=>new JArray(s))),["joints"]=new JArray(pose.joints.Select(v=>new JArray(v.x,v.y,v.z)))});
                bool main=!cancel&&!reseat&&!run;
                if(main&&(i==0||i==12||i==24||i==36||i==48||i==72))
                {
                    string prefix=Path.Combine(output,character+"-"+id+"-"+i.ToString("D2"));for(int t=0;t<rig.skins.Count;t++)Obj(prefix+"-tier"+t+".obj.gz",pose.vertices[t],rig.skins[t].renderer.sharedMesh.triangles);
                    var image=visual.Render(pose,prefix);image["frame"]=i;image["native_time"]=pose.time;image["state_hash"]=pose.state;image["transition"]=pose.transition;
                    var verts=pose.vertices[visual.tier];float delta=previousVertices==null?0:verts.Zip(previousVertices,(a,b)=>Vector3.Distance(a,b)).Max();Need(previousImage==null||delta<.005f||(string)previousImage["front_sha256"]!=(string)image["front_sha256"]||(string)previousImage["side_sha256"]!=(string)image["side_sha256"],"Measured pose advanced but rendered images did not.");images.Add(image);previousImage=image;previousVertices=verts;
                }
                previous=pose;if(i<96)rig.Step(1f/120);
            }
            string file=character+"-"+id+"-frames.json";File.WriteAllText(Path.Combine(output,file),frames.ToString());
            return new JObject{["id"]=id,["frames_file"]=file,["events"]=events,["images"]=images,["expected_final_state"]=expected,["first_settled_final_seconds"]=firstFinal,["minimum_sole_y_m"]=minimum,["maximum_joint_step_m"]=maximumStep,["handoff_maximum_skin_displacement_m"]=handoff,
                ["contact_pass"]=minimum>=-.002f,["handoff_continuity_pass"]=handoff<=.002f,["final_state_pass"]=rig.animator.GetCurrentAnimatorStateInfo(0).IsName(expected)&&!rig.animator.IsInTransition(0)&&queued==null,
                ["run_scope"]=run?"Ordinary run clip contact also included; not a gait correction. Fixed actor root means movement responsiveness/sitting-slide not established.":null};
        }

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
