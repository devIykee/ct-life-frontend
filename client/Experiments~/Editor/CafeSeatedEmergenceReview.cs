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
    public static class CafeSeatedEmergenceReview
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


        private const string Source="Assets/Art/animations/act_stand_up.fbx";
        public static void Capture()
        {
            Need(Application.isBatchMode,"Dedicated native batch required.");
            string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006"));
            string output=Path.Combine(evidence,"native-cafe-emergence-source-v1");Directory.CreateDirectory(output);
            var before=Snapshot();var rows=new JArray();bool complete=false;
            var report=new JObject{["revision"]="licensed-seated-emergence-source-v1",["capture_pass"]=false,["cases"]=rows,
                ["source_asset"]=Source,["source_sha256"]=Hash(Source),["source_importer_sha256"]=Hash(Source+".meta"),
                ["scope"]="Current registered adult world looks, existing licensed Quaternius Sitting_Exit source, original fixed actor root, raw source and fitted-social handoff with FootIK OFF/ON. LoopTime is disabled on a temporary in-memory clone only. Full pose/skin outputs require furniture/contact/visual review; no runtime or source assignment."};
            try
            {
                var proof=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"native-cafe-seat-controller-v1/receipt.json")));
                Need((bool?)proof["all_pass"]==true&&!proof["protected_drift"].Any(),"Accepted controller reference required.");
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                foreach(string look in new[]{"snow","rain"})
                {
                    var accepted=proof["cases"].Single(c=>(string)c["character"]==look);
                    var entry=registry.looks.Single(e=>e.key==(string)accepted["look_key"]);
                    Need(AssetDatabase.GetAssetPath(entry.prefab)==(string)accepted["world_prefab_asset"],"Exact world prefab changed.");
                    Need(Hash((string)accepted["controller_asset"])==(string)accepted["controller_sha256"],"Controller changed.");
                    foreach(string name in new[]{"social","typing"})Need(Hash((string)accepted[name+"_clip"])==(string)accepted[name+"_sha256"],"Steady clip changed.");
                    var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>((string)accepted["controller_asset"]);
                    foreach(bool footIk in new[]{false,true})
                    {
                        var scene=EditorSceneManager.NewPreviewScene();AnimationClip clip=null;AnimatorController temporary=null;
                        try
                        {
                            var source=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
                            clip=Object.Instantiate(source);clip.name="Sitting_Exit_nonloop_review_only";
                            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
                            temporary=new AnimatorController();temporary.parameters=controller.parameters;var machine=new AnimatorStateMachine();
                            foreach(string id in new[]{"loco_idle",CafeSeatMotionProfile.SocialState})
                            {
                                var a=controller.layers.Single().stateMachine.states.Single(s=>s.state.name==id).state;var state=machine.AddState(id);
                                state.motion=a.motion;state.iKOnFeet=a.iKOnFeet;state.writeDefaultValues=a.writeDefaultValues;state.speed=a.speed;state.speedParameter=a.speedParameter;state.speedParameterActive=a.speedParameterActive;
                                if(id=="loco_idle")machine.defaultState=state;
                            }
                            var exit=machine.AddState("review_stand_exit");exit.motion=clip;exit.iKOnFeet=footIk;exit.writeDefaultValues=true;
                            temporary.layers=new[]{new AnimatorControllerLayer{name="Base Layer",defaultWeight=1,iKPass=false,stateMachine=machine}};
                            foreach(bool blended in new[]{false,true})
                            {
                                string id=look+"-"+(footIk?"ik":"fk")+"-"+(blended?"from-social":"source");
                                using var rig=new Rig(entry,temporary,scene);using var visual=new TransitionVisual(rig);
                                if(blended)
                                {
                                    rig.Play(CafeSeatMotionProfile.SocialState,0);float length=rig.animator.GetCurrentAnimatorStateInfo(0).length;
                                    for(int i=0;i<120;i++)rig.Step(length/60f);
                                    rig.animator.CrossFadeInFixedTime("review_stand_exit",.12f,0,0f);
                                }
                                else rig.Play("review_stand_exit",0);
                                var samples=new JArray();var images=new JArray();float min=float.PositiveInfinity;var first=rig.Read();float advance=0;
                                int count=Mathf.CeilToInt((clip.length+.3f)*120f);
                                for(int i=0;i<=count;i++)
                                {
                                    if(i>0)rig.Step(1f/120f);var pose=rig.Read();min=Mathf.Min(min,pose.soles.SelectMany(v=>v).Min());advance=Mathf.Max(advance,Vector3.Distance(first.joints[0],pose.joints[0]));
                                    samples.Add(new JObject{["seconds"]=i/120f,["native_phase"]=pose.time,["state"]=pose.state,["transition"]=pose.transition,["soles"]=JArray.FromObject(pose.soles),
                                        ["joints"]=new JArray(pose.joints.Select(v=>new JArray(v.x,v.y,v.z))),["rotations"]=new JArray(pose.rotations.Select(q=>new JArray(q.x,q.y,q.z,q.w)))});
                                    if(i%12==0 || i==count)
                                        for(int t=0;t<rig.skins.Count;t++)Obj(Path.Combine(output,id+"-"+i.ToString("D3")+"-tier"+t+".obj.gz"),pose.vertices[t],rig.skins[t].renderer.sharedMesh.triangles);
                                    if(i==0||i==24||i==60||i==count)images.Add(visual.Render(pose,Path.Combine(output,id+"-"+i.ToString("D3"))));
                                }
                                Need(advance>.1f,"Source did not show an advancing seated-to-standing hips range.");
                                Need(rig.animator.GetCurrentAnimatorStateInfo(0).IsName("review_stand_exit")&&rig.Read().time>=1f,"Authored source temporal range incomplete.");
                                File.WriteAllText(Path.Combine(output,id+".json"),samples.ToString());
                                rows.Add(new JObject{["id"]=id,["source_length"]=clip.length,["foot_ik"]=footIk,["from_fitted_social"]=blended,["samples"]=samples.Count,["hips_range_m"]=advance,["minimum_sole_m"]=min,["images"]=images,["capture_pass"]=true,["contact_or_furniture_acceptance"]=false});
                            }
                        }
                        finally
                        {
                            if(temporary!=null){var machine=temporary.layers[0].stateMachine;var states=machine.states.Select(s=>s.state).ToArray();Object.DestroyImmediate(temporary);foreach(var state in states)Object.DestroyImmediate(state);Object.DestroyImmediate(machine);}
                            if(clip!=null)Object.DestroyImmediate(clip);EditorSceneManager.ClosePreviewScene(scene);
                        }
                    }
                }
                complete=rows.Count==8;
            }
            catch(Exception e){report["failure"]=e.ToString();throw;}
            finally
            {
                var after=Snapshot();var drift=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var a)||!after.TryGetValue(k,out var b)||a!=b).ToArray();
                report["protected_drift"]=new JArray(drift);report["capture_pass"]=complete&&drift.Length==0;
                report["status"]=complete&&drift.Length==0?"source-comparison-captured-not-accepted":"failed";
                File.WriteAllText(Path.Combine(output,"receipt.json"),report.ToString());Need(drift.Length==0,"Protected source drift.");
            }
            Need(complete,"Emergence comparison incomplete.");
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
