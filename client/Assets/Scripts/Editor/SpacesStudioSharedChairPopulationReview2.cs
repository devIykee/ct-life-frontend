using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Search;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Native Play-mode component review. No network channel, asset save, or fabricated server traffic.</summary>
    [InitializeOnLoad]
    public static class SpacesStudioSharedChairPopulationReview2
    {
        private const string Key = "CTLife.SpacesStudioSharedChairPopulationReview2.";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator _steps;
        private static CameraRig _rig;
        private static GameObject _root;
        private static LocalPlayerController _player;
        private static AvatarView _view;
        private static NavMeshDataInstance _navigation;
        private static int _moveIntents;
        private static readonly List<object> Walks = new List<object>();
        private static readonly List<object> Captures = new List<object>();
        private static readonly List<string> Checks = new List<string>();
        private static readonly List<string> Commands = new List<string>();
        private const string Candidate = "Assets/Art/Candidates/SpacesStudio20261007F2/SpacesStudioF2.prefab";
        private static readonly List<CastMember> Cast = new List<CastMember>();
        private static VenueSceneRoot _venue;
        private sealed class CastMember { public string spot, look, action, evaluatedState; public AvatarView view; public float timeBefore; }
        private const string ControllerReceipt="../art/character-candidates/motion-review-20261006/native-cafe-seat-controller-v1/receipt.json";
        private static readonly string[] ControllerHashes={"74d5ebc9f6f56b0c8de142e2b76c7eabcb8693ce02fb71adf5c46a1983b3527e","a9f4db0539c13b14e4cfccf5fb4ec11eea93fc27b39e26b6c2355e71fff0a1db"};
        private static readonly string[] SocialHashes={"9dfc92040a30404bf85422003879996f8e8f4361e7589bbd9750db699306e043","4e95bbcfa94abbcd3fddc86813e8add1663aa66c8ccc0df95c5614d7eec756d0"};
        private static JObject _controllerEvidence;
        private static int _boundMaps,_boundRenderers;
        [Serializable] private sealed class SavedScene { public string path; public bool isLoaded, isActive; }
        static SpacesStudioSharedChairPopulationReview2()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (Active && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                    SessionState.SetString(Key + "runtimeError", message);
            };
        }
        private static bool Active => SessionState.GetBool(Key + "active", false);
        private static string Phase => SessionState.GetString(Key + "phase", "");
        private static void PhaseIs(string value) => SessionState.SetString(Key + "phase", value);

        public static void Launch()
        {
            if (Active || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Seat review requires idle Edit mode.");
            RequireCleanScenes();
            if(!File.Exists(Candidate))throw new FileNotFoundException("Stage the exact revision F2 Studio prefab first.",Candidate);
            var stage=JObject.Parse(File.ReadAllText("Assets/Art/Candidates/SpacesStudio20261007F2/StageReceipt.json"));
            if((string)stage["status"]!="staged_unbaked_not_published"||(int)stage["positioned_anchors"]!=34||(int)stage["authored_approaches"]!=31||(int)stage["tagged_geometry_spots"]!=31||(string)stage["snapshot_sha256"]!=Hash("Assets/Resources/Contract/config_snapshot.json"))
                throw new InvalidDataException("Studio stage/anchor/interaction receipt no longer matches.");
            if((int)stage["shared_fitted_chairs"]!=30||((JArray)stage["chairs"]).Count!=30||stage["chairs"].Any(c=>(float)c["max_anchor_local_vertex_error_m"]>.00001f))throw new InvalidDataException("All thirty prototype chair identities are required.");
            if(string.IsNullOrEmpty(Argument("studioPrefabHash"))||Hash(Candidate)!=Argument("studioPrefabHash"))throw new InvalidDataException("Pass the exact reviewed F baked prefab hash explicitly.");
            RequireMotionSources();
            SessionState.SetString(Key+"candidateHash",Hash(Candidate));
            string bakePath="Assets/Art/Candidates/SpacesStudio20261007F2/BakeReceipt.json";
            SessionState.SetString(Key+"bakeReceiptHash",File.Exists(bakePath)?Hash(bakePath):"");
            if(!File.Exists(bakePath)||string.IsNullOrEmpty(Argument("studioBakeHash"))||Hash(bakePath)!=Argument("studioBakeHash"))throw new InvalidDataException("Pass the exact reviewed F bake receipt hash explicitly.");
            if(File.Exists(bakePath))
            {
                var bake=JObject.Parse(File.ReadAllText(bakePath));
                if((string)bake["status"]!="baked_not_published_requires_visual_and_device_review"||(string)bake["prefab_sha256"]!=Hash(Candidate))
                    throw new InvalidDataException("F bake receipt does not match the exact current prefab.");
            }
            var setup = EditorSceneManager.GetSceneManagerSetup();
            SessionState.SetString(Key + "scenes", JsonConvert.SerializeObject(setup.Select(s => new SavedScene
                { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive })));
            SessionState.SetString(Key + "protection", JsonConvert.SerializeObject(ProtectedHashes()));
            string output=Argument("studioPopulationOutput") ?? "/tmp/ct-studio-shared-chair-f2-population-native-20261007";
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Population review output already contains evidence; select a new output directory.");
            SessionState.SetString(Key + "output", output);
            SessionState.SetBool(Key + "exit", Argument("studioPopulationExit") == "1");
            SessionState.SetInt(Key + "result", 1);
            SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 360d).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "active", true);
            PhaseIs("startup");
            Debug.Log("CTLIFE [studio-shared-chair-population] Waiting for imports and Search database initialization.");
        }

        private static void Tick()
        {
            if (!Active) return;
            try
            {
                if (Phase == "stopping")
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
                    return;
                }
                if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture))
                    throw new TimeoutException("Seat review timed out in " + Phase);
                var runtimeError = SessionState.GetString(Key + "runtimeError", "");
                if (!string.IsNullOrEmpty(runtimeError)) throw new InvalidOperationException("Native runtime error: " + runtimeError);
                if (Phase == "startup")
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating || !SearchService.EnumerateDatabases().Any()) return;
                    RequireCleanScenes();
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    PhaseIs("entering");
                    EditorApplication.EnterPlaymode();
                    return;
                }
                if (!EditorApplication.isPlaying) return;
                if (Phase == "entering")
                {
                    // This preference is runtime-only and is discarded when the isolated Play session exits.
                    SessionState.SetBool(Key + "originalRunInBackground", Application.runInBackground);
                    Application.runInBackground = true;
                    if (EditorApplication.isPaused) throw new InvalidOperationException("Play mode is paused; review did not unpause it.");
                    Checks.Clear(); Commands.Clear(); Walks.Clear(); Captures.Clear(); Cast.Clear(); _moveIntents=0;
                    _steps = Review();
                    PhaseIs("running");
                }
                if (Phase == "running" && !_steps.MoveNext()) Stop(null);
            }
            catch (Exception e) { Stop(e); }
        }

        private static void RequireMotionSources()
        {
            _controllerEvidence=JObject.Parse(File.ReadAllText(ControllerReceipt));
            if((bool?)_controllerEvidence["all_pass"]!=true||((JArray)_controllerEvidence["protected_drift"]).Count!=0)throw new InvalidDataException("Existing saved-controller pose evidence is not passing.");
            foreach(var pair in new[]{("snow",0),("rain",1)})
            {
                var c=((JArray)_controllerEvidence["cases"]).Single(row=>(string)row["character"]==pair.Item1);
                if((bool?)c["all_pass"]!=true||(string)c["controller_sha256"]!=ControllerHashes[pair.Item2]||Hash((string)c["controller_asset"])!=ControllerHashes[pair.Item2]||(string)c["social_sha256"]!=SocialHashes[pair.Item2]||Hash((string)c["social_clip"])!=SocialHashes[pair.Item2])throw new InvalidDataException("Frozen saved social controller/clip changed: "+pair.Item1);
            }
        }
        private static IEnumerator Review()
        {
            _root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidate));
            _venue=_root.GetComponent<VenueSceneRoot>();
            var data=JObject.Parse(File.ReadAllText("Assets/Resources/Contract/config_snapshot.json"));
            var registries=Registries.Parse(data["ids"],data["config"]);_venue.Init(registries.SpotsFor("spaces_studio"));
            _navigation=NavMesh.AddNavMeshData(_venue.navigation,_root.transform.position,_root.transform.rotation);
            Require(_navigation.valid,"existing F saved navigation installed");_root.GetComponent<VenueContentLighting>()?.Apply();
            RequireMotionSources();
            var bake=JObject.Parse(File.ReadAllText("Assets/Art/Candidates/SpacesStudio20261007F2/BakeReceipt.json"));
            var maps=_root.GetComponent<VenueLightmaps>();Require(maps!=null&&maps.colors.Length==(int)bake["lightmaps"]&&maps.bindings.Length==(int)bake["bound_renderers"]&&maps.colors.Length>0&&maps.bindings.Length>0,"actual F bake maps and bindings match receipt");
            _boundMaps=maps.colors.Length;_boundRenderers=maps.bindings.Length;
            foreach(var b in maps.bindings)Require(b.renderer!=null&&b.renderer.lightmapIndex>=0&&b.renderer.lightmapIndex<LightmapSettings.lightmaps.Length&&
                LightmapSettings.lightmaps[b.renderer.lightmapIndex].lightmapColor==maps.colors[b.lightmap]&&Vector4.Distance(b.renderer.lightmapScaleOffset,b.scaleOffset)<.00001f,"installed baked renderer "+b.renderer.name);
            _rig=CameraRig.Ensure();_rig.ConfigureVenue(_venue);_rig.Camera.aspect=16f/9f;
            if(_rig.Camera.GetComponent<UniversalAdditionalCameraData>()==null)_rig.Camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            string[] spots={"seat_1","seat_3","seat_5","seat_8","seat_11","seat_13","seat_16","seat_20","stage_1","stage_5","stage_10","stage_host"};
            for(int i=0;i<spots.Length;i++)
            {
                string look=i%2==0?"snow":"rain";
                var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+look));
                var anchor=_venue.Anchors.Single(a=>a.spotId==spots[i]);
                var actor=AvatarFactory.Create("Diagnostic cast / "+spots[i]+" / "+look,entry.key.Split('|'),false,parent:_root.transform);
                actor.transform.SetPositionAndRotation(anchor.transform.position,anchor.transform.rotation);
                Require(actor.Animator!=null&&actor.Animator.isHuman&&actor.Animator.runtimeAnimatorController==(entry.controllerOverride??registry.controller),"actual world Humanoid and effective controller "+spots[i]);
                actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;actor.Animator.applyRootMotion=false;actor.Animator.speed=1;
                actor.SetBase(anchor.anim,anchor.spotId);actor.SetTalking(false);
                string evaluatedState=anchor.anim;
                if(anchor.anim=="act_sit")
                {
                    var evidence=((JArray)_controllerEvidence["cases"]).Single(c=>(string)c["character"]==look);
                    Require((string)evidence["look_key"]==entry.key,"exact previously fitted look "+look);
                    string avatarAsset=AssetDatabase.GetAssetPath(actor.Animator.avatar);
                    Require(avatarAsset==(string)evidence["own_avatar_asset"]&&actor.Animator.avatar.name==(string)evidence["own_avatar_name"],"same own Humanoid Avatar "+look);
                    actor.Animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>((string)evidence["controller_asset"]);
                    evaluatedState=CafeSeatMotionProfile.SocialState;
                    Require(actor.Animator.HasState(0,AnimationLibrary.Hash(evaluatedState)),"saved fitted social state exists "+look);
                    actor.Animator.Play(evaluatedState,0,0f);
                }
                Cast.Add(new CastMember{spot=spots[i],look=look,action=anchor.anim,evaluatedState=evaluatedState,view=actor});
            }
            var local=registry.looks.Single(e=>e.key.Split('|').Contains("head_snow"));
            _view=AvatarFactory.Create("Diagnostic standing observer",local.key.Split('|'),false,parent:_root.transform);
            _view.transform.position=_venue.EntrancePosition;_view.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            _player=_view.gameObject.AddComponent<LocalPlayerController>();typeof(LocalPlayerController).GetMethod("Init").Invoke(_player,new object[]{_venue,null});
            _player.ReviewCommandObserved+=Observe;_rig.Target=_view.transform;
            var warm=Wait(.6f,4);while(warm.MoveNext())yield return warm.Current;
            foreach(var member in Cast){Require(!member.view.Animator.IsInTransition(0)&&member.view.Animator.GetCurrentAnimatorStateInfo(0).IsName(member.evaluatedState),"explicit preview pose "+member.spot);member.timeBefore=member.view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;}
            warm=Wait(.35f,3);while(warm.MoveNext())yield return warm.Current;
            foreach(var member in Cast)Require(member.view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime>member.timeBefore+.005f,"real Play animation advances "+member.spot);
            Capture("populated-arrival",_venue);
            // Reuse same previous E2 viewpoints after the scoped F ray/walk review; this records real walking through the diagnostic cast, not new path acceptance.
            foreach(var item in new[]{("audience",new Vector3(-5.05f,0,-3.8f)),("speaker",new Vector3(-8.05f,0,4.2f)),("host",new Vector3(2.05f,0,6.5f))})
            {
                Require(_player.MoveTo(item.Item2),"standing observer move "+item.Item1);
                var walk=WalkToArrival(item.Item2,"populated-"+item.Item1,-1,_venue);while(walk.MoveNext())yield return walk.Current;
                Capture("populated-"+item.Item1,_venue);
            }
            var host=Cast.Single(c=>c.spot=="stage_host");
            // Explicit presentation audition, not the authoritative action (which remains loco_idle at this anchor).
            Require(host.view.Animator.HasState(0,AnimationLibrary.Hash("act_host_mic")),"existing host gesture is available for a labeled visual audition");
            host.view.SetTalking(true);host.view.SetBase("act_host_mic");host.action="act_host_mic";host.evaluatedState="act_host_mic";
            warm=Wait(.45f,4);while(warm.MoveNext())yield return warm.Current;
            Require(host.view.Animator.GetCurrentAnimatorStateInfo(0).IsName("act_host_mic"),"host gesture actually evaluated");
            Capture("host-gesture-audition",_venue);host.view.SetTalking(false);host.view.SetBase("loco_idle",host.spot);host.action="loco_idle";host.evaluatedState="loco_idle";
            foreach(var id in new[]{"seat_1","seat_3","stage_1","stage_5"})
            {
                var member=Cast.Single(c=>c.spot==id);
                foreach(var angle in new[]{"front","side"})CaptureDetail(member,angle);
            }
            Require(_player.MoveTo(_venue.EntrancePosition),"observer return requested");
            var returning=WalkToArrival(_venue.EntrancePosition,"populated-return",-1,_venue);while(returning.MoveNext())yield return returning.Current;
            Capture("populated-return",_venue);
            Require(SitCount()==0,"visual cast does not dispatch seat claims or fabricate confirmations");
        }
        private static IEnumerator Wait(float seconds,int minimumFrames)
        {
            float start=Time.time;int frame=Time.frameCount;double end=EditorApplication.timeSinceStartup+20;
            while(Time.time-start<seconds||Time.frameCount-frame<minimumFrames){if(EditorApplication.timeSinceStartup>end)throw new TimeoutException("Actual Play frames did not advance.");yield return null;}
        }
        private static void Observe(string command)
        { if(command=="move")_moveIntents++;else Commands.Add(command); }
        private static int SitCount()=>Commands.Count(c=>c.StartsWith("sit:",StringComparison.Ordinal));
        private static float FlatDistance(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
        private static IEnumerator WalkToArrival(Vector3 target,string label,int expectedSits,VenueSceneRoot venue)
        {
            var agent=_player.GetComponent<NavMeshAgent>();var start=_player.transform.position;var last=start;
            int startFrame=Time.frameCount,lastFrame=-1;float distance=0,maxSpeed=0;int samples=0;bool midCaptured=false;
            var blockers=new HashSet<string>();double deadline=EditorApplication.timeSinceStartup+25d;
            while(EditorApplication.timeSinceStartup<deadline)
            {
                if(Time.frameCount==lastFrame){yield return null;continue;}lastFrame=Time.frameCount;
                var p=_player.transform.position;distance+=FlatDistance(p,last);last=p;maxSpeed=Mathf.Max(maxSpeed,_player.ActualVelocity.magnitude);samples++;
                foreach(var collider in Physics.OverlapCapsule(p+Vector3.up*.32f,p+Vector3.up*1.43f,agent.radius,~0,QueryTriggerInteraction.Ignore))
                    if(!collider.transform.IsChildOf(_player.transform))blockers.Add(collider.name);
                bool hasDestination=(bool)typeof(LocalPlayerController).GetField("_hasDestination",Private).GetValue(_player);
                if(Time.frameCount>=startFrame+2&&!hasDestination&&!agent.pathPending&&FlatDistance(p,target)<.15f&&_player.ActualVelocity.magnitude<.2f)break;
                if(!midCaptured&&label=="viewpoint-stage_1"&&distance>1f)
                { _rig.SnapToTarget();Capture("room-mid-walk",venue);midCaptured=true; }
                yield return null;
            }
            var end=_player.transform.position;bool arrived=FlatDistance(end,target)<.15f&&!agent.pathPending&&!(bool)typeof(LocalPlayerController).GetField("_hasDestination",Private).GetValue(_player);
            Walks.Add(new{label,start=new[]{start.x,start.y,start.z},target=new[]{target.x,target.y,target.z},end=new[]{end.x,end.y,end.z},arrived,
                elapsedFrames=Time.frameCount-startFrame,samples,travelledMetres=distance,maxActualSpeed=maxSpeed,proxyOverlapNames=blockers.ToArray(),expectedSitIntents=expectedSits,observedSitIntents=SitCount(),
                proxyScope="Actual agent radius0.30m capsule at rootY+0.32..1.43m (2cm floor margin); sampled standing navigation proxy, not full skinned animation contact."});
            Require(arrived,"real agent arrives "+label);Require(blockers.Count==0,"walking body proxy clears solids "+label);
            if(expectedSits>=0)Require(SitCount()==expectedSits,"exactly one arrival intent "+label);
        }
        private static void Capture(string label,VenueSceneRoot venue)
        {
            _rig.SnapToTarget();CaptureCamera(label,_rig.Camera,"actual authored landscape CameraRig following the real standing observer");
        }
        private static void CaptureDetail(CastMember member,string angle)
        {
            var go=new GameObject("Diagnostic chair camera");var camera=go.AddComponent<Camera>();camera.enabled=false;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();camera.fieldOfView=42;camera.nearClipPlane=.03f;camera.farClipPlane=80;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=_rig.Camera.backgroundColor;
            Vector3 target=member.view.transform.position+Vector3.up*.85f;
            Vector3 offset=member.view.transform.TransformDirection(angle=="front"?new Vector3(.6f,.30f,2.6f):new Vector3(2.65f,.25f,.45f));
            camera.transform.position=target+offset;camera.transform.LookAt(target);
            try{CaptureCamera("chair-"+member.spot+"-"+member.look+"-"+angle,camera,"diagnostic contact angle, not gameplay camera");}
            finally{Object.DestroyImmediate(go);}
        }
        private static void CaptureCamera(string label,Camera camera,string scope)
        {
            string output=SessionState.GetString(Key+"output","");Directory.CreateDirectory(output);string path=Path.Combine(output,label+".png");camera.aspect=16f/9f;
            var snapshots=new List<GameObject>();var meshes=new List<Mesh>();var changed=new List<(Renderer renderer,bool enabled)>();var groups=new List<(LODGroup group,bool enabled)>();var poses=new List<object>();
            RenderTexture rt=null;Texture2D tex=null;var previous=RenderTexture.active;
            try
            {
                rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);rt.Create();tex=new Texture2D(1600,900,TextureFormat.RGB24,false);
                foreach(var member in Cast.Concat(new[]{new CastMember{spot="standing_observer",look="snow",action="loco_idle",view=_view}}))
                {
                    var actor=member.view;var lodGroups=actor.GetComponentsInChildren<LODGroup>(true);
                    var skins=lodGroups.SelectMany(g=>g.GetLODs().Take(1)).SelectMany(l=>l.renderers).OfType<SkinnedMeshRenderer>().Distinct().ToArray();
                    Require(skins.Length==1,"one actual authored LOD0 skin "+member.spot);var skin=skins[0];var mesh=new Mesh();meshes.Add(mesh);skin.BakeMesh(mesh);
                    var measured=mesh.vertices.Select(v=>skin.transform.TransformPoint(v)).ToArray();
                    var go=new GameObject("Exact native CPU pose / "+member.spot);snapshots.Add(go);go.transform.SetParent(skin.transform,false);go.layer=skin.gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=skin.sharedMaterials;renderer.shadowCastingMode=skin.shadowCastingMode;renderer.receiveShadows=skin.receiveShadows;renderer.renderingLayerMask=skin.renderingLayerMask;renderer.lightProbeUsage=skin.lightProbeUsage;renderer.reflectionProbeUsage=skin.reflectionProbeUsage;renderer.probeAnchor=skin.probeAnchor;
                    var block=new MaterialPropertyBlock();skin.GetPropertyBlock(block);renderer.SetPropertyBlock(block);
                    for(int i=0;i<renderer.sharedMaterials.Length;i++){block.Clear();skin.GetPropertyBlock(block,i);if(!block.isEmpty)renderer.SetPropertyBlock(block,i);}
                    float error=0;var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++)error=Mathf.Max(error,Vector3.Distance(go.transform.TransformPoint(vertices[i]),measured[i]));
                    Require(error<=.00001f,"snapshot matches measured native world vertices "+member.spot);
                    foreach(var group in lodGroups){groups.Add((group,group.enabled));group.enabled=false;}
                    foreach(var original in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true)){changed.Add((original,original.enabled));original.enabled=false;}
                    var state=actor.Animator.GetCurrentAnimatorStateInfo(0);
                    string meshFile=null;
                    if(label.StartsWith("chair-"+member.spot+"-",StringComparison.Ordinal)&&label.EndsWith("-side",StringComparison.Ordinal))
                    {meshFile=label+"-actual.obj.gz";WriteObj(Path.Combine(output,meshFile),measured,mesh.triangles);}
                    poses.Add(new{spot=member.spot,look=member.look,requestedAction=member.action,evaluatedPreviewState=member.evaluatedState,actualStateHash=state.shortNameHash,normalizedTime=state.normalizedTime,talking=actor.Talking,
                        root=V(actor.transform.position),yaw=actor.transform.eulerAngles.y,hip=V(actor.Animator.GetBoneTransform(HumanBodyBones.Hips).position),
                        worldFloorMinimum=measured.Min(v=>v.y),vertices=measured.Length,snapshotErrorMetres=error,meshSha256=VertexHash(measured),meshFile,
                        controller=AssetDatabase.GetAssetPath(actor.Animator.runtimeAnimatorController)});
                }
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};Require(RenderPipeline.SupportsRenderRequest(camera,request),"native graphics capture available");
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
                Captures.Add(new{label,file=Path.GetFileName(path),sha256=Hash(path),nativeFrame=Time.frameCount,scope,
                    source="same-native-frame CPU BakeMesh snapshots with exact original materials; no stale GPU skin buffer",
                    camera=V(camera.transform.position),rotation=V(camera.transform.eulerAngles),fov=camera.fieldOfView,aspect=camera.aspect,
                    castCount=Cast.Count,observerCount=1,synthetic=true,boundLightmapCount=_boundMaps,boundRendererCount=_boundRenderers,poses});
            }
            finally
            {
                RenderTexture.active=previous;foreach(var go in snapshots)if(go!=null)Object.DestroyImmediate(go);foreach(var m in meshes)Object.DestroyImmediate(m);
                foreach(var item in groups)if(item.group!=null)item.group.enabled=item.enabled;foreach(var item in changed)if(item.renderer!=null)item.renderer.enabled=item.enabled;
                if(tex!=null)Object.DestroyImmediate(tex);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}
            }
        }
        private static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
        private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static string VertexHash(Vector3[] vertices){using var memory=new MemoryStream();using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true))foreach(var v in vertices){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(memory.ToArray())).Replace("-","").ToLowerInvariant();}
        private static void WriteObj(string path,Vector3[] vertices,int[] triangles){using var file=File.Create(path);using var zip=new GZipStream(file,System.IO.Compression.CompressionLevel.Fastest);using var w=new StreamWriter(zip);foreach(var v in vertices)w.WriteLine("v "+v.x.ToString("R",CultureInfo.InvariantCulture)+" "+v.y.ToString("R",CultureInfo.InvariantCulture)+" "+v.z.ToString("R",CultureInfo.InvariantCulture));for(int i=0;i<triangles.Length;i+=3)w.WriteLine($"f {triangles[i]+1} {triangles[i+1]+1} {triangles[i+2]+1}");}
        private static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + check);
            Checks.Add(check); Debug.Log("CTLIFE [studio-shared-chair-population] PASS " + check);
        }
        private static void Stop(Exception failure)
        {
            if(Phase=="stopping")return;
            // Set the boundary first: one failing cleanup must not re-enter Stop indefinitely.
            PhaseIs("stopping");SessionState.SetInt(Key+"result",failure==null?0:1);
            var errors=new List<string>();
            void Try(string name,Action action){try{action();}catch(Exception e){errors.Add(name+": "+e);SessionState.SetInt(Key+"result",1);}}
            string output=SessionState.GetString(Key+"output","/tmp/ct-studio-shared-chair-f2-population-native-20261007");
            try
            {
                Try("unsubscribe observer",()=>{if(_player!=null)_player.ReviewCommandObserved-=Observe;});
                Try("remove owned navigation",()=>{if(_navigation.valid)_navigation.Remove();});
                Try("restore owned lighting",()=>{if(_root!=null)_root.GetComponent<VenueContentLighting>()?.Restore();});
                Try("restore owned lightmaps",()=>{if(_root!=null)_root.GetComponent<VenueLightmaps>()?.Restore();});
                Try("destroy review root",()=>{if(_root!=null)Object.Destroy(_root);});
                Try("destroy review camera",()=>{if(_rig!=null)Object.Destroy(_rig.gameObject);});
                Try("write capture receipt",()=>
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output,"receipt.json"),JsonConvert.SerializeObject(new
                    {
                        status=failure==null&&errors.Count==0?"shared_chair_fitted_population_captured_not_runtime_or_voice_accepted":"failed",
                        candidatePrefab=Candidate,candidatePrefabSha256=SessionState.GetString(Key+"candidateHash",""),bakeReceiptSha256=SessionState.GetString(Key+"bakeReceiptHash",""),
                        checks=Checks,diagnostics=Commands,walks=Walks,captures=Captures,observedMoveIntents=_moveIntents,
                        socialPoseReceipt=ControllerReceipt,socialPoseReceiptSha256=Hash(ControllerReceipt),savedControllerHashes=ControllerHashes,savedSocialClipHashes=SocialHashes,
                        networkChannel="none; synthetic diagnostic casting only, no seat claims or server confirmations",
                        limitations="Synthetic 12-person cast plus real standing observer in exact caller-hashed baked F; 11 seated previews directly evaluate the saved accepted Café fitted social state with exact saved controller/clip identity and unchanged own Avatar, not canonical Studio act_sit. No profile is attached or runtime action changed. Native Play time and full-detail LOD0 CPU snapshots; four real observer walks. Host act_host_mic is a labeled audition, not authoritative loco_idle or RTC. This capture supports shared-chair visual/contact review only, not connected seat ownership, authoritative speaker actions, entry/exit traversal, mobile performance, production crowd LOD or venue promotion. Existing feedback item 13 remains the single backend stage-action report.",
                        failure=failure?.ToString(),cleanupErrors=errors.ToArray()
                    },Formatting.Indented));
                });
                if(failure!=null)Debug.LogException(failure);
            }
            finally
            {
                Try("restore runInBackground",()=>{if(EditorApplication.isPlaying)Application.runInBackground=SessionState.GetBool(Key+"originalRunInBackground",false);});
                SessionState.SetString(Key+"cleanupErrors",JsonConvert.SerializeObject(errors));
                try
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.ExitPlaymode();else Finish();
                }
                catch(Exception e)
                {
                    SessionState.SetInt(Key+"result",1);SessionState.SetBool(Key+"active",false);
                    Debug.LogError("CTLIFE [studio-shared-chair-population] Play-mode exit failed: "+e);
                    if(SessionState.GetBool(Key+"exit",false))EditorApplication.Exit(1);
                }
            }
        }
        private static void Finish()
        {
            var errors=JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key+"cleanupErrors","[]"))??new List<string>();
            void Try(string name,Action action){try{action();}catch(Exception e){errors.Add(name+": "+e);SessionState.SetInt(Key+"result",1);}}
            string output=SessionState.GetString(Key+"output","/tmp/ct-studio-shared-chair-f2-population-native-20261007");
            try
            {
                Try("restore saved scene setup",()=>
                {
                    var scenes=JsonConvert.DeserializeObject<SavedScene[]>(SessionState.GetString(Key+"scenes","[]"));
                    if(scenes.Any(v=>!string.IsNullOrEmpty(v.path)))EditorSceneManager.RestoreSceneManagerSetup(scenes.Select(v=>new SceneSetup{path=v.path,isLoaded=v.isLoaded,isActive=v.isActive}).ToArray());
                    else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                });
                Try("verify protected bytes",()=>
                {
                    var before=JsonConvert.DeserializeObject<Dictionary<string,string>>(SessionState.GetString(Key+"protection","{}"));var after=ProtectedHashes();
                    var drift=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var oldHash)||!after.TryGetValue(k,out var newHash)||oldHash!=newHash).ToArray();
                    Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"protected-drift.json"),JsonConvert.SerializeObject(new{count=before.Count,drift},Formatting.Indented));
                    if(drift.Length!=0)SessionState.SetInt(Key+"result",1);
                });
                Try("write cleanup result",()=>
                {
                    Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"cleanup-result.json"),JsonConvert.SerializeObject(new{exit=SessionState.GetInt(Key+"result",1),errors},Formatting.Indented));
                });
            }
            finally
            {
                int result=SessionState.GetInt(Key+"result",1);SessionState.SetBool(Key+"active",false);
                if(errors.Count>0)Debug.LogError("CTLIFE [studio-shared-chair-population] Cleanup failures: "+string.Join("\n",errors));
                Debug.Log("CTLIFE [studio-shared-chair-population] END exit="+result);
                if(SessionState.GetBool(Key+"exit",false))EditorApplication.Exit(result);
            }
        }
        private static Dictionary<string, string> ProtectedHashes()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var files = new[] { "ProjectSettings", "Packages", "Assets/Scripts/World", "Assets/Resources", "Assets/Art/Candidates", "Assets/Art/characters/authored" }
                .SelectMany(p => Directory.GetFiles(Path.Combine(project, p), "*", SearchOption.AllDirectories));
            using var sha = SHA256.Create();
            return files.ToDictionary(p => p, p => BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-", "").ToLowerInvariant());
        }
        private static void RequireCleanScenes()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && scene.rootCount != 0))
                    throw new InvalidOperationException("Existing unsaved scene must be preserved; review did not replace it.");
            }
        }
        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-ctlife-" + name) return args[i + 1];
            return null;
        }
    }
}
