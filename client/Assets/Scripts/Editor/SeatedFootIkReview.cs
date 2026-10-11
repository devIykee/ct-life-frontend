// Native read-only pose evidence. Coordinator imports/runs only when Unity is idle.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class SeatedFootIkReview
    {
        private const string VenuePath = "Assets/Art/Candidates/TerminalCafe20261006B2/TerminalCafeB2.prefab";
        private const string RegistryPath = "Assets/Resources/Characters/CharacterRegistry.asset";
        private const int Samples = 24;
        public static void Capture() => Run(true);
        public static void CaptureGeometry() => Run(false);

        private static void Run(bool render)
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode && !Lightmapping.isRunning, "Use idle Edit mode.");
            var venue = AssetDatabase.LoadAssetAtPath<GameObject>(VenuePath);
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath);
            Need(venue != null && registry != null, "Stage B2 and keep the real current character registry available.");
            string output = Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/character-candidates/motion-review-20261006/native-seated-footik"));
            Directory.CreateDirectory(output);
            var dependencies = AssetDatabase.GetDependencies(new[] { VenuePath, RegistryPath },true).Where(File.Exists).ToArray();
            var frozen = dependencies.Concat(dependencies.Where(p=>File.Exists(p+".meta")).Select(p=>p+".meta")).Distinct().ToDictionary(p=>p,Hash);
            var receipt = new JObject { ["status"]="running",["venue"]=VenuePath,["render_requested"]=render,
                ["scope"]="Exact immutable roots; actual current world prefabs/effective clips in isolated unsaved controllers; FootIK off/on; 0.2 s state crossfade then two warm loops and 24 continuous native samples; no root offsets or source asset edits.",
                ["limitations"]="Pose evidence is not multiplayer, runtime grounding, NavMesh seat approach or phone acceptance. Weighted-region/plane deltas are diagnostic witnesses, not a complete mesh collision solver." };
            try
            {
                var cases = new JArray(); receipt["cases"]=cases;
                foreach(string character in new[]{"snow","rain"})
                {
                    var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+character));
                    var controller=entry.controllerOverride!=null?entry.controllerOverride:registry.controller;
                    Need(entry.prefab!=null && controller!=null,"Current world look/controller missing: "+character);
                    foreach(bool work in new[]{false,true})
                        foreach(bool footIk in new[]{false,true})
                            cases.Add(CaptureCase(venue,entry.prefab,controller,character,work,footIk,output,render));
                }
                receipt["status"]="captured_not_seating_accepted";
            }
            catch(Exception error) { receipt["status"]="failed";receipt["error"]=error.ToString();throw; }
            finally
            {
                var changed=frozen.Where(p=>!File.Exists(p.Key)||Hash(p.Key)!=p.Value).Select(p=>p.Key).ToArray();
                receipt["protected_drift"]=new JArray(changed);receipt["protected_sha256"]=JObject.FromObject(frozen);
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());
                Need(changed.Length==0,"Read-only pose review found protected asset drift: "+string.Join(",",changed));
            }
        }

        private static JObject CaptureCase(GameObject venue,GameObject prefab,RuntimeAnimatorController controller,string character,bool work,bool footIk,string output,bool render)
        {
            string spotId=work?"terminal_1":"table_1";
            string action=work?"act_type":"act_sit";
            string baselineId=character+(work?"-work":"-social");
            string caseId=baselineId+(footIk?"-footik":"-noik");
            var anchor=venue.GetComponentsInChildren<SpotAnchor>(true).Single(a=>a.spotId==spotId);
            Vector3 foot=anchor.transform.position;
            Transform Furniture(string id) => venue.GetComponentsInChildren<Transform>(true)
                .Where(t=>t.name=="Direction_"+id).OrderBy(t=>(t.position-foot).sqrMagnitude).First();
            var sourceChair=Furniture(work?"office_chair":"home_chair_1");
            var sourceTable=Furniture(work?"home_desk":"home_table_roundsmall");
            var scene=EditorSceneManager.NewPreviewScene();
            var temporary=new List<Object>();
            try
            {
                GameObject Copy(GameObject source)
                {
                    var copy=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(copy,scene);temporary.Add(copy);return copy;
                }
                var chair=Copy(sourceChair.gameObject);var table=Copy(sourceTable.gameObject);
                var root=Copy(prefab);root.transform.SetPositionAndRotation(foot,anchor.transform.rotation);
                var animator=root.GetComponentsInChildren<Animator>(true).Single();
                var shared=(controller is AnimatorOverrideController ownController?ownController.runtimeAnimatorController:controller) as AnimatorController;
                Need(shared!=null,"Expected real AnimatorController lineage.");
                var sourceState=States(shared.layers[0].stateMachine).Single(state=>state.name==action);
                var sourceClip=(AnimationClip)sourceState.motion;
                if(controller is AnimatorOverrideController own && own[sourceClip]!=null)sourceClip=own[sourceClip];
                Need(sourceClip!=null && !sourceState.iKOnFeet,"Expected existing seated source with Foot IK off.");
                var reviewController=new AnimatorController{name="UNSAVED_SEATED_CONTACT_REVIEW"};
                var machine=new AnimatorStateMachine();var idle=machine.AddState("loco_idle");var seated=machine.AddState(action);
                idle.motion=sourceClip;seated.motion=sourceClip;idle.speed=seated.speed=1;seated.iKOnFeet=footIk;machine.defaultState=idle;
                reviewController.layers=new[]{new AnimatorControllerLayer{name="Base Layer",defaultWeight=1,stateMachine=machine}};
                temporary.Add(machine);temporary.Add(idle);temporary.Add(seated);temporary.Add(reviewController);
                animator.enabled=false;animator.runtimeAnimatorController=reviewController;animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.speed=1;animator.enabled=true;animator.Rebind();
                foreach(var group in root.GetComponentsInChildren<LODGroup>(true))group.ForceLOD(0);
                Need(animator.HasState(0,Animator.StringToHash(action)),"Effective controller lacks "+action);
                animator.Play("loco_idle",0,0);animator.Update(0);
                animator.CrossFadeInFixedTime(action,.2f,0);
                for(int i=0;i<24;i++)animator.Update(1f/60f);
                Need(animator.GetCurrentAnimatorStateInfo(0).IsName(action)&&!animator.IsInTransition(0),"Actual crossfade did not settle in "+action);
                animator.Play(action,0,0);animator.Update(0);
                var clips=animator.GetCurrentAnimatorClipInfo(0);
                Need(clips.Length==1 && clips[0].clip!=null,"Expected one effective seated clip.");
                var clip=clips[0].clip;var initial=animator.GetCurrentAnimatorStateInfo(0);
                Need(initial.loop && clip.isLooping,"Current seated state/clip does not loop.");
                Need(Mathf.Abs(initial.speed-1)<.0001f && Mathf.Abs(initial.speedMultiplier-1)<.0001f,"Review requires the current 1x seated state.");
                float period=initial.length,dt=period/Samples;
                Need(period>.01f && float.IsFinite(period),"Invalid native seated period.");
                for(int i=0;i<2*Samples;i++)animator.Update(dt);
                float start=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Need(Mathf.Abs(start-2)<.005f,"Native seated warm-up did not advance two cycles.");
                var furniture=Geometry(chair).Concat(Geometry(table)).ToArray();
                float seatY=SeatPlane(chair);
                var frames=new JArray();
                var baselinePath=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/terminal-cafe-b2/seating-amendment/native-b2-pose-review/"+baselineId+".json"));
                var prior=JObject.Parse(File.ReadAllText(baselinePath));
                Need((string)prior["effective_clip"]==AssetDatabase.GetAssetPath(sourceClip),"Independent seated source changed.");
                float baselineSoleError=0,baselineHipError=0;
                Camera camera=null;
                if(render)camera=PrepareCamera(scene,foot,temporary);
                for(int sample=0;sample<=Samples;sample++)
                {
                    var info=animator.GetCurrentAnimatorStateInfo(0);
                    Need(info.IsName(action) && Mathf.Abs(info.normalizedTime-start-sample/(float)Samples)<.005f,"Native seated phase froze or changed state.");
                    Need((root.transform.position-foot).sqrMagnitude<1e-8f,"Unexpected avatar root translation.");
                    var skins=new JArray();
                    foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var baked=new Mesh();
                        try
                        {
                            skin.BakeMesh(baked);
                            var vertices=baked.vertices.Select(skin.transform.TransformPoint).ToArray();
                            Need(vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Nonfinite posed skin.");
                            int[] Region(params HumanBodyBones[] wanted)
                            {
                                var bones=wanted.Select(animator.GetBoneTransform).Where(b=>b!=null).ToHashSet();
                                var weights=skin.sharedMesh.boneWeights;
                                return Enumerable.Range(0,weights.Length).Where(i=> {
                                    var w=weights[i];float Weight(int b,float value)=>value>0&&bones.Contains(skin.bones[b])?value:0;
                                    return Weight(w.boneIndex0,w.weight0)+Weight(w.boneIndex1,w.weight1)+Weight(w.boneIndex2,w.weight2)+Weight(w.boneIndex3,w.weight3)>=.5f;
                                }).ToArray();
                            }
                            var feet=Region(HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightFoot,HumanBodyBones.RightToes);
                            var leftFoot=Region(HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes);
                            var rightFoot=Region(HumanBodyBones.RightFoot,HumanBodyBones.RightToes);
                            var leftHand=Region(HumanBodyBones.LeftHand,HumanBodyBones.LeftIndexProximal,HumanBodyBones.LeftIndexIntermediate,HumanBodyBones.LeftIndexDistal);
                            var rightHand=Region(HumanBodyBones.RightHand,HumanBodyBones.RightIndexProximal,HumanBodyBones.RightIndexIntermediate,HumanBodyBones.RightIndexDistal);
                            var priorSkin=prior["frames"][sample]["skins"].Single(k=>(string)k["skin"]==skin.name);
                            baselineSoleError=Mathf.Max(baselineSoleError,Mathf.Abs(feet.Min(i=>vertices[i].y)-(float)priorSkin["foot_surface_min_y_m"]));
                            var hips=Region(HumanBodyBones.Hips,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg);
                            var nearSeat=hips.Where(i=>Mathf.Abs(vertices[i].x-foot.x)<.3f && vertices[i].z>foot.z-.30f && vertices[i].z<foot.z+.30f).ToArray();
                            var torso=Region(HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest);
                            skins.Add(new JObject { ["skin"]=skin.name,["vertices"]=vertices.Length,
                                ["foot_surface_min_y_m"]=feet.Length>0?(JToken)feet.Min(i=>vertices[i].y):JValue.CreateNull(),
                                ["left_foot_min_y_m"]=leftFoot.Min(i=>vertices[i].y),["right_foot_min_y_m"]=rightFoot.Min(i=>vertices[i].y),
                                ["left_hand_min_y_m"]=leftHand.Min(i=>vertices[i].y),["right_hand_min_y_m"]=rightHand.Min(i=>vertices[i].y),
                                ["left_hand_front_world_z_m"]=leftHand.Max(i=>vertices[i].z),["right_hand_front_world_z_m"]=rightHand.Max(i=>vertices[i].z),
                                ["butt_thigh_near_seat_min_y_m"]=nearSeat.Length>0?(JToken)nearSeat.Min(i=>vertices[i].y):JValue.CreateNull(),
                                ["butt_thigh_seat_plane_delta_m"]=nearSeat.Length>0?(JToken)(nearSeat.Min(i=>vertices[i].y)-seatY):JValue.CreateNull(),
                                ["torso_back_min_world_z_m"]=torso.Length>0?(JToken)torso.Min(i=>vertices[i].z):JValue.CreateNull() });
                            if(sample%8==0 && sample<Samples)
                                WriteObj(Path.Combine(output,caseId+"-"+skin.name+"-"+sample.ToString("00")+".obj.gz"),new[]{(vertices,baked.triangles)}.Concat(furniture));
                        }
                        finally { Object.DestroyImmediate(baked); }
                    }
                    var priorHip=prior["frames"][sample]["hips_world"].Values<float>().ToArray();
                    baselineHipError=Mathf.Max(baselineHipError,Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.Hips).position,new Vector3(priorHip[0],priorHip[1],priorHip[2])));
                    frames.Add(new JObject { ["phase"]=sample/(float)Samples,["native_time"]=info.normalizedTime,["skins"]=skins,
                        ["hips_world"]=V(animator.GetBoneTransform(HumanBodyBones.Hips).position),["root_world"]=V(root.transform.position),
                        ["joints_world"]=JObject.FromObject(new[]{HumanBodyBones.Hips,HumanBodyBones.Chest,HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes}.ToDictionary(b=>b.ToString(),b=>V(animator.GetBoneTransform(b).position))) });
                    if(render && sample%8==0 && sample<Samples)
                        foreach(string view in new[]{"front","side"})Render(camera,foot,view,Path.Combine(output,caseId+"-"+sample.ToString("00")+"-"+view+".png"));
                    if(sample<Samples)animator.Update(dt);
                }
                var result=new JObject { ["case"]=caseId,["spot"]=spotId,["animation"]=action,["prefab"]=AssetDatabase.GetAssetPath(prefab),
                    ["effective_controller"]=AssetDatabase.GetAssetPath(controller),["effective_clip"]=AssetDatabase.GetAssetPath(clip),["effective_clip_name"]=clip.name,
                    ["root_world"]=V(foot),["root_yaw_degrees"]=anchor.transform.eulerAngles.y,["chair_root_world"]=V(chair.transform.position),
                    ["chair_yaw_degrees"]=chair.transform.eulerAngles.y,["native_seat_plane_y_m"]=seatY,["period_seconds"]=period,["frames"]=frames,
                    ["foot_ik"]=footIk,["baseline_sole_max_error_m"]=baselineSoleError,["baseline_hips_max_error_m"]=baselineHipError,
                    ["left_feet_bottom_height_m"]=animator.leftFeetBottomHeight,["right_feet_bottom_height_m"]=animator.rightFeetBottomHeight,
                    ["acceptance"]="not_accepted_requires_human_mesh_contact_review_and_runtime_approach" };
                File.WriteAllText(Path.Combine(output,caseId+".json"),result.ToString());
                Need(baselineHipError<.002f,"Seated contact state trial changed hips relative to independently captured real controller.");
                if(!footIk)Need(baselineSoleError<.002f,"Control does not reproduce independent real-controller shoes within2mm.");
                return new JObject { ["case"]=caseId,["file"]=caseId+".json",["samples"]=frames.Count,["images"]=render?6:0 };
            }
            finally
            {
                foreach(var obj in temporary.AsEnumerable().Reverse())if(obj!=null)Object.DestroyImmediate(obj);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) => machine.states.Select(s=>s.state).Concat(machine.stateMachines.SelectMany(s=>States(s.stateMachine)));
        private static IEnumerable<(Vector3[] vertices,int[] triangles)> Geometry(GameObject go)
        {
            foreach(var filter in go.GetComponentsInChildren<MeshFilter>(true).Where(f=>!f.name.Contains("_LOD")||f.name.Contains("_LOD0")))
                yield return (filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray(),filter.sharedMesh.triangles);
        }
        private static float SeatPlane(GameObject chair)
        {
            var surfaces=new List<(float y,float area)>();
            foreach(var (v,t) in Geometry(chair))for(int i=0;i<t.Length;i+=3)
            {
                var a=v[t[i]];var b=v[t[i+1]];var c=v[t[i+2]];var normal=Vector3.Cross(b-a,c-a);
                float y=(a.y+b.y+c.y)/3;
                if(normal.magnitude>.0001f && normal.normalized.y>.95f && y>.25f && y<.75f)
                    surfaces.Add((Mathf.Round(y*1000)/1000,normal.magnitude*.5f));
            }
            Need(surfaces.Count>0,"No native upward chair seat surface.");
            return surfaces.GroupBy(p=>p.y).OrderByDescending(g=>g.Sum(p=>p.area)).First().Key;
        }
        private static Camera PrepareCamera(Scene scene,Vector3 foot,List<Object> temporary)
        {
            GameObject InScene(string name) { var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);temporary.Add(go);return go; }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);temporary.Add(floor);
            floor.transform.position=foot+new Vector3(0,-.035f,.3f);floor.transform.localScale=new Vector3(5,.07f,5);
            var material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));temporary.Add(material);material.SetColor("_BaseColor",new Color(.48f,.46f,.41f));floor.GetComponent<Renderer>().sharedMaterial=material;
            var key=InScene("Read-only pose key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.5f;key.color=new Color(1,.93f,.84f);key.transform.rotation=Quaternion.Euler(35,-35,0);
            var fill=InScene("Read-only pose fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.6f;fill.transform.rotation=Quaternion.Euler(30,140,0);
            var camera=InScene("Read-only pose camera").AddComponent<Camera>();camera.scene=scene;camera.enabled=false;
            camera.orthographic=true;camera.orthographicSize=1.35f;camera.aspect=1;camera.nearClipPlane=.01f;camera.farClipPlane=20;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.20f,.23f,.24f);
            return camera;
        }
        private static void Render(Camera camera,Vector3 foot,string view,string path)
        {
            Vector3 target=foot+new Vector3(0,.75f,.35f);
            camera.transform.position=target+(view=="front"?new Vector3(0,.1f,4):new Vector3(4,.1f,0));camera.transform.LookAt(target);
            var rt=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32);rt.Create();
            var image=new Texture2D(1024,1024,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                var request=new RenderPipeline.StandardRequest{destination=rt};
                Need(RenderPipeline.SupportsRenderRequest(camera,request),"URP render requests unavailable; run with graphics, or use CaptureGeometry explicitly.");
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,1024,1024),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally { RenderTexture.active=previous;Object.DestroyImmediate(image);rt.Release();Object.DestroyImmediate(rt); }
        }
        private static void WriteObj(string path,IEnumerable<(Vector3[] vertices,int[] triangles)> pieces)
        {
            using var file=File.Create(path);using var gzip=new GZipStream(file,System.IO.Compression.CompressionLevel.Optimal);using var writer=new StreamWriter(gzip);
            int offset=1;foreach(var (v,t) in pieces)
            {
                foreach(var p in v)writer.WriteLine(string.Format(CultureInfo.InvariantCulture,"v {0:R} {1:R} {2:R}",p.x,p.y,p.z));
                for(int i=0;i<t.Length;i+=3)writer.WriteLine($"f {t[i]+offset} {t[i+1]+offset} {t[i+2]+offset}");offset+=v.Length;
            }
        }
        private static JArray V(Vector3 value)=>new JArray(value.x,value.y,value.z);
        private static void Need(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
        private static string Hash(string path)
        {
            using var sha=SHA256.Create();using var stream=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
