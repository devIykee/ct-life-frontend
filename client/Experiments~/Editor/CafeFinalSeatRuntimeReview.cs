using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Native Play-mode component review. No network channel, asset save, or fabricated server traffic.</summary>
    [InitializeOnLoad]
    public static class CafeFinalSeatRuntimeReview
    {
        private const string Key = "CTLife.CafeFinalSeatRuntimeReview.";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator _steps;
        private static NavMeshDataInstance _navigation;
        private static GameObject _root;
        private static readonly List<string> Checks = new List<string>();
        private static readonly List<string> Commands = new List<string>();
        [Serializable] private sealed class SavedScene { public string path; public bool isLoaded, isActive; }
        static CafeFinalSeatRuntimeReview()
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
            var setup = EditorSceneManager.GetSceneManagerSetup();
            SessionState.SetString(Key + "scenes", JsonConvert.SerializeObject(setup.Select(s => new SavedScene
                { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive })));
            SessionState.SetString(Key + "protection", JsonConvert.SerializeObject(ProtectedHashes()));
            SessionState.SetString(Key + "output", Argument("cafeRuntimeOutput") ?? "/tmp/ct-cafe-final-runtime-20261006");
            SessionState.SetBool(Key + "exit", Argument("cafeRuntimeExit") == "1");
            SessionState.SetInt(Key + "result", 1);
            SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 300d).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "active", true);
            PhaseIs("startup");
            Debug.Log("CTLIFE [cafe-final-runtime] Waiting for imports and Search database initialization.");
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
                    Checks.Clear(); Commands.Clear();
                    _steps = Review();
                    PhaseIs("running");
                }
                if (Phase == "running" && !_steps.MoveNext()) Stop(null);
            }
            catch (Exception e) { Stop(e); }
        }

        private const string FinalPrefab="Assets/Art/Candidates/TerminalCafe20261006Final/TerminalCafeFinal.prefab";
        private static readonly List<object> Cases=new List<object>();
        private static ConfigStore _previousConfig;
        private static bool _hadConfig, _configured;
        private static readonly List<string> ContactFailures = new List<string>();
        private static int _contactCasesCompleted;
        private static bool _reviewCompleted;
        private static CameraRig _cameraRig;
        private static string Evidence=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006"));
        private static IEnumerator Review()
        {
            Cases.Clear(); ContactFailures.Clear(); _configured = false; _contactCasesCompleted = 0; _reviewCompleted = false;
            var source=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(Evidence,"cafe-seat-integration/bridge-runtime/source-baseline.json")));
            Require(FileHash("Assets/Scripts/World/Avatars/AvatarView.cs")== (string)source["candidate_sha256"],"exact staged bridge runtime is imported");
            var strategy=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(Evidence,"native-cafe-typing-strategies-v1/receipt.json")));
            Require((bool?)strategy["capture_pass"]==true&&(bool?)strategy["both_looks_bridge_numeric_pass"]==true&&!strategy["protected_drift"].Any(),"both-look isolated bridge geometry passed");
            string controllerReceipt=Path.Combine(Evidence,"native-cafe-seat-controller-v1/receipt.json");
            var reference=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(controllerReceipt));
            Require((bool?)reference["all_pass"]==true&&FileHash(controllerReceipt)==(string)strategy["controller_reference_sha256"],"exact full controller reference retained");
            var characterManifest=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(Evidence,"cafe-seat-integration/baseline-sha256.json")));
            foreach(var item in ((Newtonsoft.Json.Linq.JObject)characterManifest["files"]).Properties().Where(p=>p.Name.StartsWith("client/Assets/Resources/Characters/",StringComparison.Ordinal)||p.Name.StartsWith("client/Assets/Art/characters/",StringComparison.Ordinal)))
                Require(FileHash(item.Name.Substring("client/".Length))==(string)item.Value,"unchanged accepted character dependency "+item.Name);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FinalPrefab);Require(prefab!=null,"final geometry prefab exists");
            _root=Object.Instantiate(prefab);_root.name="FinalCafeRuntimeReview_CloneOnly";
            var venue=_root.GetComponent<VenueSceneRoot>();Require(venue!=null&&venue.venueId=="terminal_cafe"&&venue.navigation!=null&&!venue.IsGreybox,"actual final Cafe root/navigation");
            var config=new ConfigStore();config.Apply(Resources.Load<TextAsset>("Contract/config_snapshot").text,null,persist:false);
            _hadConfig=Services.TryGet<ConfigStore>(out _previousConfig);Services.Register(config);_configured=true;venue.Init(config.Registries.SpotsFor("terminal_cafe"));
            var registry=CharacterRegistry.Instance;Require(registry!=null,"actual character registry loaded");
            var profile=_root.GetComponent<CafeSeatMotionProfile>();if(profile==null)profile=_root.AddComponent<CafeSeatMotionProfile>();
            // This profile is filled on the runtime clone only, to break the deliberate attachment-review cycle.
            var serialized=new SerializedObject(profile);var bindings=serialized.FindProperty("bindings");bindings.arraySize=2;
            for(int i=0;i<2;i++)
            {
                string look=i==0?"snow":"rain";var proof=reference["cases"].Single(c=>(string)c["character"]==look);var entry=registry.looks.Single(e=>e.key==(string)proof["look_key"]);
                foreach(string key in new[]{"controller","baseline_controller"})Require(FileHash((string)proof[key+"_asset"])==(string)proof[key+"_sha256"],"unchanged "+look+" "+key);
                foreach(string key in new[]{"social","typing"})Require(FileHash((string)proof[key+"_clip"])==(string)proof[key+"_sha256"],"unchanged accepted "+look+" "+key+" clip");
                Require((bool?)proof["all_pass"]==true,"accepted per-look controller reference");
                Require(AssetDatabase.GetAssetPath(entry.prefab)==(string)proof["world_prefab_asset"],"exact "+look+" world prefab");
                var avatar=entry.prefab.GetComponentsInChildren<Animator>(true).Single().avatar;Require(AssetDatabase.GetAssetPath(avatar)==(string)proof["own_avatar_asset"]&&avatar.name==(string)proof["own_avatar_name"],"exact "+look+" Humanoid Avatar");
                var row=bindings.GetArrayElementAtIndex(i);row.FindPropertyRelative("baselineController").objectReferenceValue=entry.controllerOverride;row.FindPropertyRelative("ownAvatar").objectReferenceValue=avatar;row.FindPropertyRelative("cafeController").objectReferenceValue=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>((string)proof["controller_asset"]);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var anchors=venue.Anchors.Where(a=>!string.IsNullOrEmpty(a.anim)).ToArray();Require(anchors.Length==12,"exact twelve final claimable anchors");
            Require(anchors.Count(a=>a.spotId.StartsWith("table_")&&a.anim=="act_sit")==4&&anchors.Count(a=>a.spotId.StartsWith("terminal_")&&a.anim=="act_type")==8,"final action/spot whitelist is intact");
            _navigation=NavMesh.AddNavMeshData(venue.navigation,venue.transform.position,venue.transform.rotation);Require(_navigation.valid,"actual final baked navigation added");Physics.SyncTransforms();
            foreach(var anchor in anchors)
            {
                Require(anchor.approach!=null,"actual approach "+anchor.spotId);var path=new NavMeshPath();Require(NavMesh.SamplePosition(venue.EntrancePosition,out var start,.75f,NavMesh.AllAreas)&&NavMesh.SamplePosition(anchor.approach.position,out var finish,.35f,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,finish.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete,"final entrance reaches "+anchor.spotId);
            }
            _cameraRig=CameraRig.Ensure();_cameraRig.ConfigureVenue(venue);_cameraRig.Camera.aspect=16f/9f;
            foreach(string look in new[]{"snow","rain"})
            {
                var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+look));
                foreach(string spot in new[]{"table_1","terminal_1"})
                {
                    var view=Create(entry,venue.EntrancePosition);var local=view.gameObject.AddComponent<LocalPlayerController>();typeof(LocalPlayerController).GetMethod("Init").Invoke(local,new object[]{venue,null});local.ReviewCommandObserved+=Commands.Add;
                    view.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;_cameraRig.Target=view.transform;_cameraRig.SnapToTarget();
                    var agent=view.GetComponent<NavMeshAgent>();int ready=Time.frameCount+3;while(Time.frameCount<ready)yield return null;Require(agent.isOnNavMesh,"actual final-room agent is ready");int before=SitCount();Require(local.GoSit(spot),look+" real GoSit accepts "+spot);
                    double deadline=EditorApplication.timeSinceStartup+18;while(SitCount()==before&&EditorApplication.timeSinceStartup<deadline)yield return null;
                    Require(SitCount()==before+1&&string.IsNullOrEmpty(local.SeatedSpot),look+" actual arrival dispatches intent but no offline seat acknowledgement");
                    Require(venue.TryGetSpot(spot,out var anchor,out var yaw),"authoritative anchor resolves");string action=spot=="table_1"?"act_sit":"act_type";string final=spot=="table_1"?CafeSeatMotionProfile.SocialState:CafeSeatMotionProfile.TypingState;
                    local.OnServerSeat("terminal_cafe."+spot,action);Require(!agent.enabled&&Vector3.Distance(view.transform.position,anchor)<.00001f,"injected state applies exact root; agent disabled");
                    var observe=Observe(view,look+"-local-"+spot+"-entry",.65f,null,true);while(observe.MoveNext())yield return null;
                    Require(view.Animator.GetCurrentAnimatorStateInfo(0).IsName(final)&&!view.Animator.IsInTransition(0),look+" actual local settled fitted "+action);Require(Bridge(view)==null,"no stale entry queue");
                    Vector3 seatedFocus=view.transform.position;CaptureBoundary(view,look+"-local-"+spot+"-before-stand",seatedFocus);
                    int stands=Commands.Count(x=>x=="stand");local.OnServerSeat(null,"loco_idle");Require(agent.enabled&&agent.isOnNavMesh&&local.SeatedSpot==null,"actual authoritative stand restores agent/approach");Require(Commands.Count(x=>x=="stand")==stands,"authoritative stand sends no stand echo");
                    observe=Observe(view,look+"-local-"+spot+"-stand",.65f,null,true,seatedFocus);while(observe.MoveNext())yield return null;
                    Require(view.Animator.GetCurrentAnimatorStateInfo(0).IsName("loco_idle")&&Bridge(view)==null,"actual local stand settles idle without pending bridge");
                    if(spot=="terminal_1")
                    {
                        local.OnServerSeat(spot,action);observe=Observe(view,look+"-local-retype",.65f,null,false);while(observe.MoveNext())yield return null;
                        Vector3 seatedRoot=view.transform.position;CaptureBoundary(view,look+"-local-before-direct-move",seatedRoot);local.SetMoveInput(Vector2.up);Vector3 moveStart=view.transform.position;Cases.Add(new{ name=look+"-direct-stand-root-restoration", from=new[]{seatedRoot.x,seatedRoot.y,seatedRoot.z}, to=new[]{moveStart.x,moveStart.y,moveStart.z}, displacement_m=Vector3.Distance(seatedRoot,moveStart), scope="existing local authoritative-anchor to NavMesh-approach restoration; not locomotion distance" });Require(local.SeatedSpot==null&&agent.enabled&&agent.isOnNavMesh&&local.DirectInputActive,"direct movement starts immediately after restoring navigation");
                        observe=Observe(view,look+"-local-direct-move",.65f,()=>local.SetMoveInput(Vector2.up),false,seatedRoot);while(observe.MoveNext())yield return null;
                        Require(Vector3.Distance(view.transform.position,moveStart)>.1f,"real direct input moved the root; no added control lock");local.StopDirectInput();local.CancelMovement();
                        observe=Observe(view,look+"-local-stop",.65f,null,false);while(observe.MoveNext())yield return null;Require(Bridge(view)==null,"latest walking/stop intent clears bridge");
                    }
                    Object.Destroy(view.gameObject);while(view!=null)yield return null;
                }
                Require(venue.TryGetSpotApproach("terminal_1",out var remoteWalkingPosition),"remote standing approach resolves");var remoteView=Create(entry,remoteWalkingPosition);var remote=remoteView.gameObject.AddComponent<RemotePlayer>();var stateType=typeof(RemotePlayer).GetMethod("Init").GetParameters()[1].ParameterType;
                object State(string spot,string anim){if(!string.IsNullOrEmpty(spot)&&venue.TryGetSpotApproach(spot,out var nextApproach))remoteWalkingPosition=nextApproach;var state=Activator.CreateInstance(stateType);stateType.GetField("name").SetValue(state,"review remote");stateType.GetField("spotId").SetValue(state,spot);stateType.GetField("anim").SetValue(state,anim);var p=venue.ToLocal(remoteWalkingPosition);stateType.GetField("x").SetValue(state,(double)p.x);stateType.GetField("z").SetValue(state,(double)p.y);return state;}
                void Apply(string spot,string anim)=>typeof(RemotePlayer).GetMethod("Apply").Invoke(remote,new object[]{State(spot,anim),false});
                typeof(RemotePlayer).GetMethod("Init").Invoke(remote,new object[]{venue,State("terminal_cafe.terminal_1","act_type")});remoteView.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;_cameraRig.Target=remoteView.transform;_cameraRig.SnapToTarget();
                var trace=Observe(remoteView,look+"-remote-entry",.65f,null,true);while(trace.MoveNext())yield return null;
                Require(remoteView.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.TypingState),"actual RemotePlayer selects fitted typing");Require(venue.TryGetSpot("terminal_1",out var remoteAnchor,out _)&&Vector3.Distance(remoteView.transform.position,remoteAnchor)<.00001f,"remote root uses exact authoritative anchor");
                Apply(null,"loco_idle");trace=Observe(remoteView,look+"-remote-stand",.65f,null,true);while(trace.MoveNext())yield return null;Require(Bridge(remoteView)==null,"actual remote locomotion clears completed queue");
                Apply("table_1","act_sit");trace=Observe(remoteView,look+"-remote-social",.65f,null,true);while(trace.MoveNext())yield return null;
                Require(remoteView.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.SocialState)&&Bridge(remoteView)==null,"actual remote social action selects fitted social state");
                Apply(null,"loco_idle");trace=Observe(remoteView,look+"-remote-social-stand",.65f,null,true);while(trace.MoveNext())yield return null;
                // Two interruption phases: before and after the first social leg completes.
                foreach(float delay in new[]{.07f,.27f})
                {
                    Apply("terminal_1","act_type");trace=Observe(remoteView,look+"-remote-interrupt-prepare-"+delay,.65f,null,false);while(trace.MoveNext())yield return null;
                    Apply(null,"loco_idle");float until=Time.time+delay;while(Time.time<until)yield return null;Apply("terminal_1","act_type");
                    trace=Observe(remoteView,look+"-remote-reseat-"+delay,.65f,null,true);while(trace.MoveNext())yield return null;
                    Require(remoteView.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.TypingState)&&Bridge(remoteView)==null,"reseat replaces stand at "+delay+" seconds");
                }
                remoteView.ApplyLod(0,true,1f);Apply(null,"loco_idle");trace=Observe(remoteView,look+"-remote-culled-state",.15f,null,false);while(trace.MoveNext())yield return null;remoteView.ApplyLod(0,false,1f);remoteView.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                trace=Observe(remoteView,look+"-remote-resume",.65f,null,false);while(trace.MoveNext())yield return null;Require(Bridge(remoteView)==null&&!remoteView.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.TypingState),"resumed remote uses latest locomotion intent");
                Apply(null,"act_type");trace=Observe(remoteView,look+"-remote-empty-context",.3f,null,false);while(trace.MoveNext())yield return null;Require(remoteView.Animator.GetCurrentAnimatorStateInfo(0).IsName("act_type"),"empty spot cannot select fitted private state");
                Apply(null,null);trace=Observe(remoteView,look+"-remote-null-action",.65f,null,false);while(trace.MoveNext())yield return null;Require(PrivateValue<string>(remoteView,"_baseSpot")==null&&Bridge(remoteView)==null,"remote null action clears fitted context");
                Apply("terminal_1","act_type");float overlayStart=Time.time+.07f;while(Time.time<overlayStart)yield return null;
                remoteView.PlayOneShot("emote_wave");Require(Bridge(remoteView)==null,"started one-shot clears a pending entry bridge");
                Apply("terminal_1","act_type");Require(Bridge(remoteView)==null&&PrivateValue<string>(remoteView,"_overlayAnim")=="emote_wave","repeated same seated state does not restart the cancelled bridge during an overlay");
                trace=Observe(remoteView,look+"-remote-overlay",.25f,null,false);while(trace.MoveNext())yield return null;
                Apply(null,"loco_idle");trace=Observe(remoteView,look+"-remote-overlay-base-interrupt",.65f,null,false);while(trace.MoveNext())yield return null;
                Require(PrivateValue<string>(remoteView,"_baseAnim")=="loco_idle"&&Bridge(remoteView)==null,"changed locomotion retains existing base-interrupt ownership during overlay");
                float overlayReturn=PrivateValue<float>(remoteView,"_overlayEndsAt")+.5f;while(Time.time<overlayReturn)yield return null;
                Require(PrivateValue<string>(remoteView,"_overlayAnim")==null&&remoteView.Animator.GetCurrentAnimatorStateInfo(0).IsName("loco_idle")&&Bridge(remoteView)==null,"actual timed overlay completion returns to latest base without stale seat queue");
                Object.Destroy(remoteView.gameObject);while(remoteView!=null)yield return null;
            }
            _reviewCompleted = true;
            Require(_contactCasesCompleted==20,"all twenty both-look fitted transition observations completed");
            Require(ContactFailures.Count==0,"actual fitted entry/exit contacts stay above minus 2 mm for both looks");
        }
        private static AvatarView Create(CharacterRegistry.Entry entry,Vector3 position)
        {
            var view=AvatarFactory.Create("Final Cafe runtime review",entry.key.Split('|'),false,null,_root.transform);view.transform.SetPositionAndRotation(position,Quaternion.identity);view.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;foreach(var group in view.GetComponentsInChildren<LODGroup>(true))group.ForceLOD(0);return view;
        }
        private static string Bridge(AvatarView view)=>PrivateValue<string>(view,"_seatBridgeTarget");
        private static IEnumerator Observe(AvatarView view,string name,float duration,Action input,bool contactScope,Vector3? fixedFocus=null)
        {
            float begin=Time.time;int last=-1;var frames=new List<object>();var images=new List<object>();float[] pictureTimes={0f,.1f,.2f,.3f,.4f,.6f};int nextPicture=0;float minimum=float.PositiveInfinity;var start=view.transform.position;double wallBegin=EditorApplication.timeSinceStartup;double wallDeadline=wallBegin+15d;Vector3 focus=fixedFocus??start;
            var skins=view.GetComponentsInChildren<SkinnedMeshRenderer>(true);var feet=skins.Select(s=>{
                var bones=new HashSet<Transform>(new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes}.Select(view.Animator.GetBoneTransform));var weights=s.sharedMesh.boneWeights;
                return Enumerable.Range(0,weights.Length).Where(i=>{var w=weights[i];float W(int b,float v)=>v>0&&bones.Contains(s.bones[b])?v:0;return W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3)>.5f;}).ToArray();}).ToArray();
            Require(skins.Length==3&&feet.All(indices=>indices.Length>0),"all three authored LOD skins have measured foot geometry for "+name);
            var mesh=new Mesh();try
            {
                while(Time.time-begin<duration || frames.Count<3)
                {
                    if(EditorApplication.timeSinceStartup>wallDeadline)throw new TimeoutException("Actual Play observation exceeded 15 seconds: "+name);
                    if(Time.frameCount==last){yield return null;continue;}last=Time.frameCount;input?.Invoke();float sole=float.PositiveInfinity;var tiers=new List<float>();
                    for(int t=0;t<skins.Length;t++){skins[t].BakeMesh(mesh,false);var vertices=mesh.vertices;var matrix=view.transform.worldToLocalMatrix*skins[t].localToWorldMatrix;float y=feet[t].Min(i=>matrix.MultiplyPoint3x4(vertices[i]).y);tiers.Add(y);sole=Mathf.Min(sole,y);}
                    minimum=Mathf.Min(minimum,sole);var info=view.Animator.GetCurrentAnimatorStateInfo(0);var pos=view.transform.position;var hips=view.transform.InverseTransformPoint(view.Animator.GetBoneTransform(HumanBodyBones.Hips).position);
                    frames.Add(new{frame=Time.frameCount,seconds=Time.time-begin,wall_seconds=EditorApplication.timeSinceStartup-wallBegin,root=new[]{pos.x,pos.y,pos.z},hips_local=new[]{hips.x,hips.y,hips.z},soles_local=tiers,animator_enabled=view.Animator.enabled,state_hash=info.shortNameHash,native_phase=info.normalizedTime,transition=view.Animator.IsInTransition(0),queued=Bridge(view)});
                    if((contactScope||name.EndsWith("direct-move",StringComparison.Ordinal))&&!view.Culled&&view.Animator.enabled&&nextPicture<pictureTimes.Length&&Time.time-begin>=pictureTimes[nextPicture])
                    {
                        string pictureOutput=SessionState.GetString(Key+"output","");Directory.CreateDirectory(pictureOutput);string file=name+"-"+nextPicture+".png";CaptureCpuPose(view,focus,Path.Combine(pictureOutput,file));string oblique=Path.GetFileNameWithoutExtension(file)+"-oblique.png";images.Add(new{file,sha256=FileHash(Path.Combine(pictureOutput,file)),oblique_file=oblique,oblique_sha256=FileHash(Path.Combine(pictureOutput,oblique)),actual_seconds=Time.time-begin,native_frame=Time.frameCount,native_phase=info.normalizedTime});nextPicture++;
                    }
                    yield return null;
                }
            }
            finally{Object.Destroy(mesh);}
            string output=SessionState.GetString(Key+"output","");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,name+".json"),JsonConvert.SerializeObject(frames,Formatting.Indented));
            Cases.Add(new{name,frames=frames.Count,requested_seconds=duration,actual_seconds=Time.time-begin,wall_seconds=EditorApplication.timeSinceStartup-wallBegin,minimum_sole_local_y_m=minimum,root_displacement_m=Vector3.Distance(start,view.transform.position),contact_scope=contactScope,contact_pass=minimum>=-.002f,images,visual_acceptance=false});
            Require(frames.Count>=3,"real Play frames observed for "+name);
            if(contactScope) { _contactCasesCompleted++; if(minimum<-.002f) ContactFailures.Add(name); }
            // Capture all cases before the aggregate contact gate; injected state is not connected authority.
        }
        private static void CaptureBoundary(AvatarView view,string name,Vector3 focus)
        {
            string output=SessionState.GetString(Key+"output","");Directory.CreateDirectory(output);string path=Path.Combine(output,name+".png");CaptureCpuPose(view,focus,path);
            var position=view.transform.position;Cases.Add(new{name,scope="actual pose immediately before existing root restoration; oblique camera remains fixed to this seated focus for subsequent sequence",root=new[]{position.x,position.y,position.z},side_file=Path.GetFileName(path),side_sha256=FileHash(path),oblique_file=name+"-oblique.png",oblique_sha256=FileHash(Path.Combine(output,name+"-oblique.png")),visual_acceptance=false});
        }
        private static void CaptureCpuPose(AvatarView view,Vector3 focus,string path)
        {
            var groups=view.GetComponentsInChildren<LODGroup>(true);var groupEnabled=groups.Select(g=>g.enabled).ToArray();
            var source=groups.SelectMany(g=>g.GetLODs().Take(1)).SelectMany(l=>l.renderers).OfType<SkinnedMeshRenderer>().Distinct().Single();
            var skins=view.GetComponentsInChildren<SkinnedMeshRenderer>(true);var enabled=skins.Select(r=>r.enabled).ToArray();var mesh=new Mesh();source.BakeMesh(mesh,false);GameObject snapshot=null,cameraObject=null,lightObject=null;RenderTexture rt=null;Texture2D image=null;var previous=RenderTexture.active;
            try
            {
                snapshot=new GameObject("Exact measured Play pose");snapshot.transform.SetParent(source.transform,false);snapshot.layer=source.gameObject.layer;snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=snapshot.AddComponent<MeshRenderer>();renderer.sharedMaterials=source.sharedMaterials;
                var block=new MaterialPropertyBlock();source.GetPropertyBlock(block);renderer.SetPropertyBlock(block);for(int i=0;i<renderer.sharedMaterials.Length;i++){block.Clear();source.GetPropertyBlock(block,i);if(!block.isEmpty)renderer.SetPropertyBlock(block,i);}
                foreach(var group in groups)group.enabled=false;foreach(var skin in skins)skin.enabled=false;
                cameraObject=new GameObject("Final runtime side review camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.15f;camera.aspect=1;camera.nearClipPlane=.01f;camera.farClipPlane=8;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.2f,.23f,.24f);cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
                Vector3 target=focus+Vector3.up*.86f;camera.transform.position=target+new Vector3(1.8f,.1f,0);camera.transform.LookAt(target);
                lightObject=new GameObject("Review fill only - not saved lighting");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.transform.rotation=Quaternion.Euler(35,-35,0);
                rt=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32);rt.Create();image=new Texture2D(1024,1024,TextureFormat.RGB24,false);
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};Require(RenderPipeline.SupportsRenderRequest(camera,request),"graphics runtime capture supported");
                void Save(string destination){RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1024,1024),0,0);image.Apply();File.WriteAllBytes(destination,image.EncodeToPNG());}
                Save(path);
                // Fixed seated-world focus exposes lateral root restoration that the side view projects away.
                // This is an oblique diagnostic, not a claim to be the final gameplay camera or lighting.
                camera.orthographicSize=2.2f;camera.farClipPlane=20f;camera.transform.position=target+new Vector3(3.5f,2.3f,3.5f);camera.transform.LookAt(target);
                Save(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"-oblique.png"));
            }
            finally
            {
                RenderTexture.active=previous;if(image!=null)Object.DestroyImmediate(image);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(snapshot!=null)Object.DestroyImmediate(snapshot);if(cameraObject!=null)Object.DestroyImmediate(cameraObject);if(lightObject!=null)Object.DestroyImmediate(lightObject);Object.DestroyImmediate(mesh);
                for(int i=0;i<groups.Length;i++)groups[i].enabled=groupEnabled[i];for(int i=0;i<skins.Length;i++)skins[i].enabled=enabled[i];
            }
        }
        private static string FileHash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}

        private static int SitCount() => Commands.Count(c => c.StartsWith("sit:", StringComparison.Ordinal));
        private static T PrivateValue<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
        private static void SetPrivate(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
        private static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + check);
            Checks.Add(check); Debug.Log("CTLIFE [cafe-final-runtime] PASS " + check);
        }
        private static void Stop(Exception failure)
        {
            if (Phase == "stopping") return;
            if (_navigation.valid) _navigation.Remove();
            if (_root != null) Object.Destroy(_root);
            if (_cameraRig != null) Object.Destroy(_cameraRig.gameObject);
            if (_configured) { if (_hadConfig) Services.Register(_previousConfig); else Services.Unregister<ConfigStore>(); _configured=false; }
            string output = SessionState.GetString(Key + "output", "/tmp/ct-cafe-final-runtime-20261006");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "receipt.json"), JsonConvert.SerializeObject(new
            {
                status = failure == null ? "final_room_components_captured_contact_visual_and_connected_review_pending" : "failed",
                revision = "actual-final-room-seat-components-v2",
                component_checks_pass = failure == null,
                review_completed = _reviewCompleted,
                fitted_contact_cases_completed = _contactCasesCompleted,
                fitted_contact_cases_required = 20,
                observed_fitted_contacts_pass = _contactCasesCompleted > 0 && ContactFailures.Count == 0,
                fitted_transition_contact_all_pass = _reviewCompleted && _contactCasesCompleted == 20 && ContactFailures.Count == 0,
                contact_failures = ContactFailures,
                bridge_runtime_sha256 = FileHash("Assets/Scripts/World/Avatars/AvatarView.cs"),
                helper_sha256 = FileHash("Assets/Scripts/Editor/CafeFinalSeatRuntimeReview.cs"),
                cases = Cases, prefab = FinalPrefab, prefab_sha256 = File.Exists(FinalPrefab) ? FileHash(FinalPrefab) : null,
                profile_scope = "bindings filled on runtime clone only; no prefab/registry assignment",
                frame_scope = "actual Play frames with actual LocalPlayerController/RemotePlayer; Animator AlwaysAnimate except explicit culled test",
                checks = Checks, observedEditorIntents = Commands, networkChannel = "null; no server calls",
                limitations = "No deployed server or phone-device validation. OnServerSeat/RemotePlayer state inputs are explicitly injected, never packets or claimed backend authority. All-tier pose contact and moving-root traces require independent visual/whole-furniture review. No added movement delay; direct controls remain live. CPU sampling time is not a frame-performance benchmark.",
                failure = failure?.ToString()
            }, Formatting.Indented));
            SessionState.SetInt(Key + "result", failure == null ? 0 : 1);
            if (failure != null) Debug.LogException(failure);
            PhaseIs("stopping");
            if (EditorApplication.isPlaying)
                Application.runInBackground = SessionState.GetBool(Key + "originalRunInBackground", false);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else Finish();
        }
        private static void Finish()
        {
            var scenes = JsonConvert.DeserializeObject<SavedScene[]>(SessionState.GetString(Key + "scenes", "[]"));
            if (scenes.Any(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(scenes.Select(s => new SceneSetup { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive }).ToArray());
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var before = JsonConvert.DeserializeObject<Dictionary<string, string>>(SessionState.GetString(Key + "protection", "{}"));
            var after = ProtectedHashes();
            var drift = before.Keys.Union(after.Keys).Where(k => !before.TryGetValue(k, out var b) || !after.TryGetValue(k, out var a) || a != b).ToArray();
            string output = SessionState.GetString(Key + "output", "/tmp/ct-cafe-final-runtime-20261006");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "protected-drift.json"), JsonConvert.SerializeObject(new { count = before.Count, drift }, Formatting.Indented));
            if (drift.Length != 0) SessionState.SetInt(Key + "result", 1);
            int result = SessionState.GetInt(Key + "result", 1);
            SessionState.SetBool(Key + "active", false);
            Debug.Log("CTLIFE [cafe-final-runtime] END exit=" + result + " protectedDrift=" + drift.Length);
            if (SessionState.GetBool(Key + "exit", false)) EditorApplication.Exit(result);
        }
        private static Dictionary<string, string> ProtectedHashes()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var files = new[] { "ProjectSettings", "Packages", "Assets/Scripts/World", "Assets/Resources", "Assets/Art/characters/authored", "Assets/Art/Candidates/TerminalCafe20261006Final" }
                .Where(p => Directory.Exists(Path.Combine(project, p))).SelectMany(p => Directory.GetFiles(Path.Combine(project, p), "*", SearchOption.AllDirectories));
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
