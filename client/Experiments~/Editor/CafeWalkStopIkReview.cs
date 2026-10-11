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
    public static class CafeWalkStopIkReview
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


        private static string Output;
        public static void Capture()
        {
            Need(Application.isBatchMode,"Dedicated native batch required.");
            string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006"));
            Output=Path.Combine(evidence,"native-cafe-walk-stop-ik-v1");Directory.CreateDirectory(Output);
            var before=Snapshot();var cases=new JArray();var report=new JObject{["revision"]="actual-snow-stop-phases-idle-footik-v1",["capture_pass"]=false,["accepted"]=false,["cases"]=cases,
                ["scope"]="Snow's actual room walk-to-idle contact failed at phase.674, while local phase.700 stayed grounded. Compare exact source/effective walk and idle at those phases with only destination idle Foot IK off/on in transient controllers. No profile, motion, importer or runtime mutation. Ground/hover/posture all reported; no candidate accepted automatically."};
            try
            {
                string proofPath=Path.Combine(evidence,"native-cafe-seat-controller-v1/receipt.json");Need(Hash(proofPath)=="d120c156e3073298561b242c0f90c365119220e766c829f44e8c9f6a29c7c65a","Controller reference changed.");
                var proof=JObject.Parse(File.ReadAllText(proofPath));var accepted=proof["cases"].Single(c=>(string)c["character"]=="snow");Need((bool?)accepted["all_pass"]==true,"Snow controller reference failed.");
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");var entry=registry.looks.Single(e=>e.key==(string)accepted["look_key"]);
                Need(AssetDatabase.GetAssetPath(entry.prefab)==(string)accepted["world_prefab_asset"],"Exact Snow world body changed.");
                var own=entry.prefab.GetComponentInChildren<Animator>(true).avatar;Need(AssetDatabase.GetAssetPath(own)==(string)accepted["own_avatar_asset"]&&own.name==(string)accepted["own_avatar_name"],"Own Avatar changed.");
                Need(Hash((string)accepted["controller_asset"])==(string)accepted["controller_sha256"],"Saved per-look controller changed.");
                var source=AssetDatabase.LoadAssetAtPath<AnimatorController>((string)accepted["controller_asset"]);
                var walk=source.layers.Single().stateMachine.states.Single(s=>s.state.name=="loco_walk").state;var idle=source.layers.Single().stateMachine.states.Single(s=>s.state.name=="loco_idle").state;
                Need(walk.iKOnFeet&&!idle.iKOnFeet&&walk.speedParameterActive&&walk.speedParameter=="speed","Expected installed walk/idle settings differ.");
                report["effective_walk_asset"]=AssetDatabase.GetAssetPath(walk.motion);report["effective_walk_sha256"]=Hash(AssetDatabase.GetAssetPath(walk.motion));report["effective_idle_asset"]=AssetDatabase.GetAssetPath(idle.motion);report["effective_idle_sha256"]=Hash(AssetDatabase.GetAssetPath(idle.motion));
                var scene=EditorSceneManager.NewPreviewScene();
                try
                {
                    foreach(float stopPhase in new[]{.674f,.700f})
                    {
                        Pose[] control=null;
                        foreach(bool idleIk in new[]{false,true})
                        {
                            string stem="snow-stop-"+(stopPhase<.69f?"674":"700")+"-idle-ik-"+(idleIk?"on":"off");
                            var controller=new AnimatorController();var machine=new AnimatorStateMachine();
                            try
                            {
                                controller.parameters=source.parameters;
                                foreach(var original in new[]{walk,idle})
                                {
                                    var state=machine.AddState(original.name);state.motion=original.motion;state.speed=original.speed;state.speedParameter=original.speedParameter;state.speedParameterActive=original.speedParameterActive;state.writeDefaultValues=original.writeDefaultValues;state.iKOnFeet=original==idle?idleIk:original.iKOnFeet;
                                    if(original==walk)machine.defaultState=state;
                                }
                                controller.layers=new[]{new AnimatorControllerLayer{name="Base Layer",defaultWeight=1,iKPass=false,stateMachine=machine}};
                                using var rig=new Rig(entry,controller,scene);using var visual=new TransitionVisual(rig);
                                rig.Play("loco_walk",2.2f);float step=((AnimationClip)walk.motion).length/(2.2f*240);
                                for(int i=0;i<480;i++)rig.Step(step);rig.Step(((AnimationClip)walk.motion).length*stopPhase/2.2f);
                                var initial=rig.animator.GetCurrentAnimatorStateInfo(0);Need(initial.IsName("loco_walk")&&Mathf.Abs(initial.normalizedTime-(2+stopPhase))<.001f,"Actual native stop phase was not established.");
                                var frames=new Pose[31];
                                void SavePose(int sample)
                                {
                                    frames[sample]=rig.Read();
                                    if(sample==0||sample==6||sample==12||sample==30)
                                    {
                                        visual.Render(frames[sample],Path.Combine(Output,stem+"-"+sample.ToString("D2")));
                                        for(int tier=0;tier<3;tier++)Obj(Path.Combine(Output,stem+"-"+sample.ToString("D2")+"-tier"+tier+".obj.gz"),frames[sample].vertices[tier],rig.skins[tier].renderer.sharedMesh.triangles);
                                    }
                                    if(sample<30)frames[sample].vertices=null;
                                }
                                SavePose(0);
                                rig.view.SetSpeed(0);rig.animator.CrossFadeInFixedTime("loco_idle",.1f,0,0f);
                                for(int i=1;i<frames.Length;i++){rig.Step(1f/120);SavePose(i);}
                                float minimum=frames.Min(f=>f.soles.SelectMany(x=>x).Min()),settled=frames.Last().soles.SelectMany(x=>x).Max();
                                int low=Enumerable.Range(0,frames.Length).OrderBy(i=>frames[i].soles.SelectMany(x=>x).Min()).First();
                                var row=new JObject{["name"]=stem,["stop_native_phase"]=initial.normalizedTime,["idle_foot_ik"]=idleIk,["minimum_sole_m"]=minimum,["lowest_sample"]=low,["settled_worst_foot_minimum_m"]=settled,["contact_gate_pass"]=minimum>=-.002f,["settled_support_gate_pass"]=settled<=.008f};
                                if(!idleIk&&stopPhase<.69f)Need(Mathf.Abs(minimum-(-.007964f))<=.002f,"Stop baseline does not reproduce observed remote Snow sink within2mm.");
                                if(control!=null)
                                {
                                    float initialDelta=frames[0].joints.Select((v,j)=>Vector3.Distance(v,control[0].joints[j])).Max();row["unchanged_start_joint_error_m"]=initialDelta;Need(initialDelta<=.0001f,"Unused destination setting changed the initial walk pose.");
                                    row["destination_ik_max_joint_change_m"]=frames.Select((f,i)=>f.joints.Select((v,j)=>Vector3.Distance(v,control[i].joints[j])).Max()).Max();
                                    row["settled_hip_change_m"]=Vector3.Distance(frames.Last().joints[0],control.Last().joints[0]);
                                    row["settled_full_skin_change_m"]=Enumerable.Range(0,3).Max(t=>frames.Last().vertices[t].Select((v,j)=>Vector3.Distance(v,control.Last().vertices[t][j])).Max());
                                }
                                cases.Add(row);SaveFrames(stem,frames);
                                if(!idleIk)control=frames;
                            }
                            finally{var states=machine.states.Select(s=>s.state).ToArray();Object.DestroyImmediate(controller);foreach(var state in states)Object.DestroyImmediate(state);Object.DestroyImmediate(machine);}
                        }
                    }
                }
                finally{EditorSceneManager.ClosePreviewScene(scene);}
                report["capture_pass"]=true;
            }
            catch(Exception e){report["failure"]=e.ToString();Debug.LogError("CTLIFE stop IK: "+e);}
            finally{var after=Snapshot();var drift=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var a)||!after.TryGetValue(k,out var b)||a!=b).ToArray();report["protected_drift"]=new JArray(drift);if(drift.Length>0)report["capture_pass"]=false;File.WriteAllText(Path.Combine(Output,"receipt.json"),report.ToString());}
            Need((bool)report["capture_pass"],"Stop comparison capture failed.");
        }
        private static void SaveFrames(string stem,Pose[] poses)=>File.WriteAllText(Path.Combine(Output,stem+".json"),JsonConvert.SerializeObject(poses.Select((p,i)=>new{seconds=i/120f,p.time,p.state,p.transition,p.soles,joints=p.joints.Select(v=>new[]{v.x,v.y,v.z}),rotations=p.rotations.Select(q=>new[]{q.x,q.y,q.z,q.w})}),Formatting.Indented));
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
