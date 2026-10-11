// Candidate-only controller copies and actual AvatarView routing/crossfade probe.
// No CharacterRegistry, ordinary controller, venue prefab, server state or clip assignment is saved.
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
    public static class CafeSeatControllerReview
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
        public static void Capture() => Run(true,false);
        public static void CaptureGeometry() => Run(false,false);
        public static void CaptureTransitions() => Run(true,true);
        private static void Run(bool render,bool transitionsOnly)
        {
            string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006"));
            string output=Path.Combine(evidence,transitionsOnly?"native-cafe-seat-transition-v2":"native-cafe-seat-controller-v1");Directory.CreateDirectory(output);
            var before=Snapshot();var report=new JObject{["state"]="running",["all_pass"]=false,["cases"]=new JArray(),["render_requested"]=render,["render_revision"]="cpu-native-pose-snapshots-v2",["transitions_only"]=transitionsOnly,
                ["scope"]="Two unassigned Cafe controller copies; exact original-state preservation, actual AvatarView routing and 0.2s crossfade samples. Isolated root, no furniture/navigation/server/device acceptance. Overlay expiry is deterministically injected."};bool pass=true;
            try
            {
                Need(Application.isBatchMode,"Dedicated batch process required.");
                var manifest=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"cafe-seat-integration/baseline-sha256.json")));
                foreach(var property in ((JObject)manifest["files"]).Properties().Where(p=>p.Name.StartsWith("client/Assets/Resources/Characters/",StringComparison.Ordinal)||p.Name.StartsWith("client/Assets/Art/characters/",StringComparison.Ordinal)))
                    Need(Hash(property.Name.Substring("client/".Length))==(string)property.Value,"Frozen controller/character dependency changed: "+property.Name);
                var typed=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"native-typing-persistent-replay-v1/receipt.json")));
                Need((bool)typed["all_pass"]&&!typed["protected_drift"].Any()&&(string)typed["review_revision"]=="fresh-process-persistent-replay-v3","Persistent typing replay missing.");
                var social=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"native-seated-body-fit-v1/candidate-receipt.json")));
                Need((bool)social["all_pass"]&&!social["protected_drift"].Any(),"Social FootIK body-fit source gate missing.");
                JObject previous=null;
                if(transitionsOnly)
                {
                    string priorPath=Path.Combine(evidence,"native-cafe-seat-controller-v1/receipt.json");previous=JObject.Parse(File.ReadAllText(priorPath));
                    Need((bool?)previous["all_pass"]==true&&!previous["protected_drift"].Any()&&(string)previous["render_revision"]=="cpu-native-pose-snapshots-v2","Full controller comparison must pass before a transition-only follow-up.");
                    report["full_controller_reference_receipt"]=priorPath;report["full_controller_reference_sha256"]=Hash(priorPath);
                }
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");var shared=AssetDatabase.LoadAssetAtPath<AnimatorController>(Shared);
                Need(shared.layers.Length==1&&shared.layers[0].stateMachine.states.Length==46&&shared.layers[0].stateMachine.stateMachines.Length==0,"Baseline graph changed; do not approximate it.");
                foreach(string character in new[]{"snow","rain"})
                {
                    var item=new JObject{["character"]=character,["all_pass"]=false};((JArray)report["cases"]).Add(item);var scene=EditorSceneManager.NewPreviewScene();
                    try
                    {
                        var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+character));var baseline=entry.controllerOverride as AnimatorOverrideController;
                        Need(baseline!=null&&baseline.runtimeAnimatorController==shared,"Expected existing authored look override.");
                        var t=typed["cases"].Single(c=>(string)c["character"]==character);
                        Need((bool)t["all_pass"]&&(bool)t["persisted_replay_verified"]&&!(bool)t["playback_foot_ik"]&&!(bool)t["playback_hand_ik_pass"]&&(int)t["playback_ik_callback_count"]==0,"Typing replay settings changed.");
                        string typePath=(string)t["candidate_asset"],sitPath="Assets/Art/characters/authored/MotionReview20261006/SeatedBodyFitV1/"+(character=="snow"?"Snow":"Rain")+"_SocialBodyFit_Candidate.anim";
                        Need(Hash(typePath)==(string)t["candidate_sha256"],"Saved typing bytes changed.");
                        var sit=AssetDatabase.LoadAssetAtPath<AnimationClip>(sitPath);var type=AssetDatabase.LoadAssetAtPath<AnimationClip>(typePath);Need(sit!=null&&type!=null,"Candidate clips missing.");
                        string controllerPath=Stage+(character=="snow"?"Snow":"Rain")+"_CafeSeats_Candidate.controller";
                        var controller=MakeCopy(shared,baseline,sit,type,controllerPath);item["controller_asset"]=controllerPath;item["controller_sha256"]=Hash(controllerPath);
                        item["look_key"]=entry.key;item["world_prefab_asset"]=AssetDatabase.GetAssetPath(entry.prefab);item["own_avatar_asset"]=AssetDatabase.GetAssetPath(entry.prefab.GetComponentsInChildren<Animator>(true).Single().avatar);
                        item["baseline_controller_asset"]=AssetDatabase.GetAssetPath(baseline);item["baseline_controller_sha256"]=Hash(AssetDatabase.GetAssetPath(baseline));item["own_avatar_name"]=entry.prefab.GetComponentsInChildren<Animator>(true).Single().avatar.name;
                        item["social_clip"]=sitPath;item["social_sha256"]=Hash(sitPath);item["typing_clip"]=typePath;item["typing_sha256"]=Hash(typePath);
                        var originals=new JArray();item["original_state_comparisons"]=originals;
                        if(transitionsOnly)
                        {
                            var prior=previous["cases"].Single(c=>(string)c["character"]==character);
                            Need((bool?)prior["all_pass"]==true&&(string)prior["controller_sha256"]==Hash(controllerPath)&&
                                (string)prior["baseline_controller_sha256"]==Hash(AssetDatabase.GetAssetPath(baseline))&&
                                (string)prior["social_sha256"]==Hash(sitPath)&&(string)prior["typing_sha256"]==Hash(typePath),"Transition-only inputs differ from full comparison.");
                            item["original_state_scope"]="Previously captured full native comparisons; exact controller and clip bytes guarded, not repeated.";
                        }
                        foreach(var state in shared.layers[0].stateMachine.states.Where(_=>!transitionsOnly).Select(s=>s.state))
                        {
                            var clip=Effective(baseline,state.motion as AnimationClip);float speed=state.name=="loco_run"?4.5f:1f;
                            using var a=new Rig(entry,baseline,scene);using var b=new Rig(entry,controller,scene);originals.Add(Compare(a,b,state.name,state.name,clip.length,speed,60,clip.isLooping));
                        }
                        if(!transitionsOnly)item["private_state_comparisons"]=new JArray(ComparePrivate(entry,controller,sit,true,CafeSeatMotionProfile.SocialState,scene),ComparePrivate(entry,controller,type,false,CafeSeatMotionProfile.TypingState,scene));
                        using(var rig=new Rig(entry,baseline,scene))
                        {
                            var venue=ProfileVenue(scene,baseline,controller,rig.animator.avatar);var profile=venue.GetComponent<CafeSeatMotionProfile>();rig.view.BindSeatMotionProfile(profile);rig.animator.Update(0);
                            Need(rig.animator.runtimeAnimatorController==controller,"Actual AvatarView profile binding failed.");
                            var routes=Routes(rig,output,character,render);item["routing_transitions"]=routes;
                            var fitted=new[]{"social","typing","social-return","stand-social","typing-from-idle","stand-typing"};
                            item["fitted_transition_contact_pass"]=routes.Where(r=>fitted.Contains((string)r["case"])).All(r=>(float)r["minimum_sole_y_m"]>=-.002f);
                            item["fitted_transition_contact_scope"]="All three current skins, native 120Hz samples over each first400ms; tolerance2mm; canonical fallback and ordinary locomotion reported separately.";
                            var noProfile=new GameObject("Other venue").AddComponent<VenueSceneRoot>();SceneManager.MoveGameObjectToScene(noProfile.gameObject,scene);noProfile.venueId="timeline_square";
                            var wrong=noProfile.gameObject.AddComponent<CafeSeatMotionProfile>();FillBinding(wrong,baseline,controller,rig.animator.avatar);
                            using var other=new Rig(entry,baseline,scene);other.view.BindSeatMotionProfile(wrong);Need(other.animator.runtimeAnimatorController==baseline,"Non-Cafe venue accepted profile.");
                            other.view.SetBase("act_sit","timeline_square.table_1");for(int i=0;i<48;i++)other.Step(1f/120);Need(other.animator.GetCurrentAnimatorStateInfo(0).IsName("act_sit"),"Ordinary venue seating changed.");
                        }
                        Need(Hash(controllerPath)==(string)item["controller_sha256"]&&!EditorUtility.IsDirty(controller),"Candidate controller changed while sampled.");
                        Need(!EditorUtility.IsDirty(registry)&&!EditorUtility.IsDirty(baseline)&&!AssetDatabase.LoadAllAssetsAtPath(Shared).Any(EditorUtility.IsDirty),"Protected source became dirty in memory.");item["all_pass"]=true;
                    }
                    catch(Exception ex){pass=false;item["failure"]=ex.ToString();Debug.LogError("CTLIFE [cafe-seat-controller] "+character+": "+ex);}
                    finally{EditorSceneManager.ClosePreviewScene(scene);}
                }
            }
            catch(Exception ex){pass=false;report["failure"]=ex.ToString();throw;}
            finally
            {
                var after=Snapshot();var drift=before.Where(p=>!after.TryGetValue(p.Key,out string hash)||hash!=p.Value).Select(p=>p.Key).Concat(after.Keys.Where(p=>!before.ContainsKey(p))).ToArray();
                bool contact=report["cases"].Count()==2&&report["cases"].All(c=>(bool?)c["fitted_transition_contact_pass"]==true);
                report["protected_drift"]=new JArray(drift);report["routing_pose_gates_pass"]=pass&&drift.Length==0;report["fitted_transition_contact_all_pass"]=contact;
                report["all_pass"]=pass&&drift.Length==0&&(!transitionsOnly||contact);
                report["state"]=pass&&drift.Length==0?(transitionsOnly?(contact?"fitted-transition-numeric-pass-visual-pending":"fitted-transition-contact-failed"):"routing-and-native-pose-pass-visual-pending"):"failed";
                File.WriteAllText(Path.Combine(output,"receipt.json"),report.ToString());Need(drift.Length==0,"Protected source drift.");
            }
            Need((bool)report["all_pass"],"Cafe candidate controller/contact gate failed; no production assignment.");
        }
        private static AnimatorController MakeCopy(AnimatorController source,AnimatorOverrideController baseline,AnimationClip sit,AnimationClip type,string path)
        {
            Directory.CreateDirectory(Stage);var copy=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if(copy==null)
            {
                Need(AssetDatabase.CopyAsset(Shared,path),"Could not copy isolated controller.");copy=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                Need(AssetDatabase.GetAssetPath(copy.layers[0].stateMachine)==path&&copy.layers[0].stateMachine.states.All(s=>AssetDatabase.GetAssetPath(s.state)==path),"Copied graph still references external state objects.");
                foreach(var state in copy.layers[0].stateMachine.states.Select(s=>s.state)){state.motion=Effective(baseline,state.motion as AnimationClip);EditorUtility.SetDirty(state);}
                Add(copy,CafeSeatMotionProfile.SocialState,sit,true);Add(copy,CafeSeatMotionProfile.TypingState,type,false);
                EditorUtility.SetDirty(copy.layers[0].stateMachine);EditorUtility.SetDirty(copy);AssetDatabase.SaveAssetIfDirty(copy);
            }
            Need(copy.layers.Length==1&&copy.layers[0].stateMachine.states.Length==48,"Candidate must preserve46 states and append2 private states.");
            Need(Shape(source)==Shape(copy),"Controller layers/parameters/default graph changed.");
            foreach(var state in source.layers[0].stateMachine.states.Select(s=>s.state))
            {
                var next=copy.layers[0].stateMachine.states.Single(s=>s.state.name==state.name).state;
                Need(StateShape(state)==StateShape(next)&&next.motion==Effective(baseline,state.motion as AnimationClip),"Original state semantics changed: "+state.name);
            }
            foreach(var v in new[]{(CafeSeatMotionProfile.SocialState,sit,true),(CafeSeatMotionProfile.TypingState,type,false)})
            {
                var state=copy.layers[0].stateMachine.states.Single(s=>s.state.name==v.Item1).state;
                Need(state.motion==v.Item2&&state.iKOnFeet==v.Item3&&state.speed==1&&!state.speedParameterActive&&state.transitions.Length==0,"Private candidate state incorrect.");
            }
            Need(!copy.layers[0].iKPass,"No hand IK pass may run.");return copy;
        }
        private static void Add(AnimatorController c,string id,AnimationClip clip,bool footIk)
        {
            var state=c.layers[0].stateMachine.AddState(id);state.motion=clip;state.speed=1;state.speedParameterActive=false;state.iKOnFeet=footIk;state.writeDefaultValues=true;EditorUtility.SetDirty(state);
        }
        private static AnimationClip Effective(AnimatorOverrideController o,AnimationClip clip)
        {
            Need(clip!=null,"Baseline motion is no longer a simple clip.");var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();o.GetOverrides(pairs);var pair=pairs.Single(p=>p.Key==clip);return pair.Value!=null?pair.Value:clip;
        }
        private static string Shape(AnimatorController c)
        {
            var l=c.layers.Single();var m=l.stateMachine;Need(m.stateMachines.Length==0&&m.anyStateTransitions.Length==0&&m.entryTransitions.Length==0&&m.behaviours.Length==0,"Unexpected graph callbacks/transitions.");
            return JsonConvert.SerializeObject(new{l.name,l.defaultWeight,l.iKPass,l.blendingMode,l.syncedLayerIndex,l.syncedLayerAffectsTiming,mask=l.avatarMask!=null?AssetDatabase.GetAssetPath(l.avatarMask):null,defaultState=m.defaultState.name,parameters=c.parameters.Select(p=>new{p.name,p.type,p.defaultBool,p.defaultFloat,p.defaultInt})});
        }
        private static string StateShape(AnimatorState s)
        {
            Need(s.transitions.Length==0&&s.behaviours.Length==0,"Unexpected original state behavior.");
            return JsonConvert.SerializeObject(new{s.name,s.speed,s.cycleOffset,s.iKOnFeet,s.writeDefaultValues,s.mirror,s.tag,s.speedParameter,s.speedParameterActive,s.mirrorParameter,s.mirrorParameterActive,s.cycleOffsetParameter,s.cycleOffsetParameterActive,s.timeParameter,s.timeParameterActive});
        }
        private static JObject Compare(Rig a,Rig b,string stateA,string stateB,float length,float speed,int steps,bool looping)
        {
            a.Play(stateA,speed);b.Play(stateB,speed);float skin=0,joint=0,rotation=0;
            for(int warm=0;warm<(looping?2*steps:0);warm++){a.Step(length/steps);b.Step(length/steps);}
            float first=a.animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            for(int i=0;i<=steps;i++)
            {
                var pa=a.Read();var pb=b.Read();Need(Mathf.Abs(pa.time-pb.time)<.0001f,"Native clocks diverged.");
                for(int j=0;j<Bones.Length;j++){joint=Mathf.Max(joint,Vector3.Distance(pa.joints[j],pb.joints[j]));rotation=Mathf.Max(rotation,Quaternion.Angle(pa.rotations[j],pb.rotations[j]));}
                for(int t=0;t<pa.vertices.Length;t++){Need(pa.vertices[t].Length==pb.vertices[t].Length,"Mesh topology differs.");for(int v=0;v<pa.vertices[t].Length;v++)skin=Mathf.Max(skin,Vector3.Distance(pa.vertices[t][v],pb.vertices[t][v]));}
                if(i<steps){a.Step(length/steps);b.Step(length/steps);}
            }
            Need(a.animator.GetCurrentAnimatorStateInfo(0).normalizedTime>first+.99f,"Native comparison did not advance a cycle.");
            Need(joint<=.00001f&&skin<=.00001f&&rotation<=.1f,"Controller copy changed a native pose.");
            return new JObject{["baseline_state"]=stateA,["candidate_state"]=stateB,["samples"]=steps+1,["looping"]=looping,["sample_start_normalized_time"]=first,["joint_error_m"]=joint,["all_tier_skin_error_m"]=skin,["rotation_error_degrees"]=rotation};
        }
        private static JObject ComparePrivate(CharacterRegistry.Entry entry,AnimatorController candidate,AnimationClip clip,bool footIk,string id,Scene scene)
        {
            var control=new AnimatorController();control.AddParameter("speed",AnimatorControllerParameterType.Float);var machine=new AnimatorStateMachine();var state=machine.AddState("reference");state.motion=clip;state.iKOnFeet=footIk;state.speed=1;machine.defaultState=state;control.layers=new[]{new AnimatorControllerLayer{name="Base Layer",stateMachine=machine,defaultWeight=1,iKPass=false}};
            try{using var a=new Rig(entry,control,scene);using var b=new Rig(entry,candidate,scene);return Compare(a,b,"reference",id,clip.length,1,240,clip.isLooping);}
            finally{Object.DestroyImmediate(control);Object.DestroyImmediate(state);Object.DestroyImmediate(machine);}
        }
        private static VenueSceneRoot ProfileVenue(Scene scene,RuntimeAnimatorController baseline,RuntimeAnimatorController candidate,Avatar avatar)
        {
            var root=new GameObject("Isolated Cafe profile");SceneManager.MoveGameObjectToScene(root,scene);var venue=root.AddComponent<VenueSceneRoot>();venue.venueId="terminal_cafe";
            for(int i=1;i<=12;i++){bool social=i<=4;var go=new GameObject("Profile anchor");go.transform.SetParent(root.transform,false);var spot=go.AddComponent<SpotAnchor>();spot.spotId=(social?"table_":"terminal_")+(social?i:i-4);spot.anim=social?"act_sit":"act_type";}
            venue.Init(null);FillBinding(root.AddComponent<CafeSeatMotionProfile>(),baseline,candidate,avatar);return venue;
        }
        private static void FillBinding(CafeSeatMotionProfile profile,RuntimeAnimatorController baseline,RuntimeAnimatorController candidate,Avatar avatar)
        {
            var serialized=new SerializedObject(profile);var bindings=serialized.FindProperty("bindings");bindings.arraySize=1;var row=bindings.GetArrayElementAtIndex(0);
            row.FindPropertyRelative("baselineController").objectReferenceValue=baseline;row.FindPropertyRelative("ownAvatar").objectReferenceValue=avatar;row.FindPropertyRelative("cafeController").objectReferenceValue=candidate;serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static JArray Routes(Rig rig,string output,string character,bool render)
        {
            using var visual=render?new TransitionVisual(rig):null;
            var rows=new JArray();var cases=new[]{
                ("social","act_sit","terminal_cafe.table_1",CafeSeatMotionProfile.SocialState),
                ("typing","act_type","terminal_cafe.terminal_1",CafeSeatMotionProfile.TypingState),
                ("social-return","act_sit","table_2",CafeSeatMotionProfile.SocialState),
                ("stand-social","loco_idle",(string)null,"loco_idle"),
                ("typing-from-idle","act_type","terminal_1",CafeSeatMotionProfile.TypingState),
                ("stand-typing","loco_idle",(string)null,"loco_idle"),
                ("empty-spot","act_type",(string)null,"act_type"),
                ("wrong-action","act_sit","terminal_1","act_sit"),
                ("null-action",(string)null,(string)null,"loco_idle"),
                ("wrong-seat","act_type","table_1","act_type"),
                ("foreign-prefix","act_sit","timeline_square.table_1","act_sit"),
                ("stand","loco_idle",(string)null,"loco_idle"),
                ("walk","loco_walk",(string)null,"loco_walk"),
                ("run","loco_run",(string)null,"loco_run"),
                ("stop","loco_idle",(string)null,"loco_idle")};
            foreach(var c in cases)
            {
                string previousState=(string)typeof(AvatarView).GetField("_baseState",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(rig.view);
                rig.view.SetSpeed(c.Item2=="loco_run"?4.5f:c.Item2=="loco_walk"?1f:0f);rig.view.SetBase(c.Item2,c.Item3);var frames=new JArray();var images=new JArray();JObject lastImage=null;Vector3[] lastVisible=null;float minimum=float.PositiveInfinity,maximumStep=0;Pose last=null;
                for(int i=0;i<=48;i++)
                {
                    var pose=rig.Read();minimum=Mathf.Min(minimum,pose.soles.Min(s=>s.Min()));if(last!=null)maximumStep=Mathf.Max(maximumStep,pose.joints.Zip(last.joints,(a,b)=>Vector3.Distance(a,b)).Max());
                    frames.Add(new JObject{["frame"]=i,["seconds"]=i/120f,["normalized_time"]=pose.time,["state_hash"]=pose.state,["transition"]=pose.transition,["joints"]=new JArray(pose.joints.Select(v=>new JArray(v.x,v.y,v.z))),["sole_y_per_tier"]=new JArray(pose.soles.Select(x=>new JArray(x)))});
                    if((c.Item1=="social"||c.Item1=="typing"||c.Item1=="social-return"||c.Item1=="stand"||c.Item1=="stand-social"||c.Item1=="typing-from-idle"||c.Item1=="stand-typing")&&(i==0||i==12||i==24||i==48))
                    {
                        string prefix=Path.Combine(output,character+"-"+c.Item1+"-"+i.ToString("D2"));
                        for(int t=0;t<rig.skins.Count;t++)Obj(prefix+"-tier"+t+".obj.gz",pose.vertices[t],rig.skins[t].renderer.sharedMesh.triangles);
                        if(visual!=null)
                        {
                            var image=visual.Render(pose,prefix);image["frame"]=i;image["seconds"]=i/120f;image["normalized_time"]=pose.time;image["state_hash"]=pose.state;image["transition"]=pose.transition;
                            var visible=pose.vertices[visual.tier];float delta=lastVisible==null?0:visible.Zip(lastVisible,(a,b)=>Vector3.Distance(a,b)).Max();image["maximum_skin_delta_from_previous_capture_m"]=delta;
                            // A >=5mm visible mesh change must alter at least one of the two PNGs. This is a capture gate, not a posture gate.
                            Need(lastImage==null||delta<.005f||(string)image["front_sha256"]!=(string)lastImage["front_sha256"]||(string)image["side_sha256"]!=(string)lastImage["side_sha256"],"Materially different measured transition poses produced identical front and side PNGs.");
                            images.Add(image);lastImage=image;lastVisible=visible;
                        }
                    }
                    last=pose;if(i<48)rig.Step(1f/120);
                }
                Need(rig.animator.GetCurrentAnimatorStateInfo(0).IsName(c.Item4)&&!rig.animator.IsInTransition(0),"Actual AvatarView route/transition failed: "+c.Item1);
                string file=character+"-"+c.Item1+"-transition.json";File.WriteAllText(Path.Combine(output,file),frames.ToString());rows.Add(new JObject{["case"]=c.Item1,["previous_resolved_state"]=previousState,["canonical_action"]=c.Item2,["spot"]=c.Item3,["expected_state"]=c.Item4,["minimum_sole_y_m"]=minimum,["max_joint_step_m"]=maximumStep,["frames"]=file,["images"]=images,["visual_acceptance"]=false});
            }
            rig.view.SetBase("act_sit","table_1");for(int i=0;i<48;i++)rig.Step(1f/120);float before=rig.animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            rig.view.SetBase("act_sit","terminal_cafe.table_2");rig.animator.Update(0);Need(Mathf.Abs(rig.animator.GetCurrentAnimatorStateInfo(0).normalizedTime-before)<.00001f&&!rig.animator.IsInTransition(0),"Repeated seat update restarted the loop.");
            Need((string)typeof(AvatarView).GetField("_baseSpot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(rig.view)=="terminal_cafe.table_2","Repeated update did not retain latest canonical seat context.");
            rig.view.PlayOneShot("emote_wave");for(int i=0;i<24;i++)rig.Step(1f/120);
            typeof(AvatarView).GetField("_overlayEndsAt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(rig.view,float.NegativeInfinity);
            typeof(AvatarView).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(rig.view,null);for(int i=0;i<48;i++)rig.Step(1f/120);
            Need(rig.animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.SocialState),"Overlay did not return to latest seated context.");
            rows.Add(new JObject{["case"]="repeated-seat-and-overlay-return",["all_pass"]=true,["clock_scope"]="Fixture injects overlay expiry then invokes actual AvatarView.Update; wall-clock/connected behavior not established."});return rows;
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
        private static Dictionary<string,string> Snapshot()=>new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Where(p=>!p.Replace('\\','/').StartsWith(Stage,StringComparison.Ordinal)&&p!=Stage.TrimEnd('/')+".meta").ToDictionary(p=>p,Hash);
        private static string Hash(string path){using var sha=SHA256.Create();using var f=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException("Cafe seat review: "+message);}
    }
}
