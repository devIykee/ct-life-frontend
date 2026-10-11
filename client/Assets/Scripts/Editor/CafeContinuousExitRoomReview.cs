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
using UnityEditor.Animations;
using Newtonsoft.Json.Linq;
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
    public static class CafeContinuousExitRoomReview
    {
        private const string Key = "CTLife.CafeContinuousExitRoomReview.";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator _steps;
        private static readonly List<Object> TemporaryControllers=new List<Object>();
        private static NavMeshDataInstance _navigation;
        private static GameObject _root;
        private static readonly List<string> Checks = new List<string>();
        private static readonly List<string> Commands = new List<string>();
        [Serializable] private sealed class SavedScene { public string path; public bool isLoaded, isActive; }
        static CafeContinuousExitRoomReview()
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
            SessionState.SetBool(Key+"pictures",Argument("cafeRuntimePictures")=="1");
            SessionState.SetInt(Key + "result", 1);
            SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetString(Key+"foregroundStartup","not-observed");
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
                    else if(EditorApplication.timeSinceStartup>double.Parse(SessionState.GetString(Key+"stopDeadline","0"),System.Globalization.CultureInfo.InvariantCulture))
                    {
                        SessionState.SetInt(Key+"result",1);SessionState.SetBool(Key+"active",false);
                        Debug.LogError("Cafe exit review failed to leave its owned Play session within15 seconds; scene restoration is incomplete.");
                        if(SessionState.GetBool(Key+"exit",false))EditorApplication.Exit(1);
                    }
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
                    SessionState.SetInt(Key+"originalTargetFrameRate",Application.targetFrameRate);
                    SessionState.SetInt(Key+"originalVSyncCount",QualitySettings.vSyncCount);
                    SessionState.SetBool(Key+"cadenceOwned",true);
                    QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
                    if (EditorApplication.isPaused) throw new InvalidOperationException("Play mode is paused; review did not unpause it.");
                    // Observe genuine foreground Play cadence. Do not fabricate player-loop updates,
                    // repeatedly steal focus, or modify saved Editor preferences.
                    Checks.Clear(); Commands.Clear();
                    SessionState.SetString(Key+"focusDeadline",(EditorApplication.timeSinceStartup+60d).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    SessionState.SetString(Key+"focusSince","-1");SessionState.SetInt(Key+"focusFirstFrame",-1);
                    if(CapturePictures)
                    {
                        //Synchronous CPU snapshots deliberately run independently of foreground cadence.
                        //Every actual frame, contact and source/goal-clock gate remains; this cannot pass timing.
                        SessionState.SetString(Key+"foregroundStartup",JsonConvert.SerializeObject(new{scope="explicit diagnostic-pictures background allowed; not timing eligible",application_focused=Application.isFocused,game_view_focused=GameViewFocused,focused_window=FocusedWindowType}));
                        _steps=Review();PhaseIs("running");
                        Debug.Log("CTLIFE [cafe-final-runtime] Diagnostic pictures: background allowed, no desktop cadence acceptance.");
                    }
                    else
                    {
                        var game = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w=>w.GetType().FullName=="UnityEditor.GameView");
                        if(game!=null)game.Focus(); //Existing view only; root can activate the app via actual desktop control.
                        PhaseIs("awaitingForeground");
                        Debug.Log("CTLIFE [cafe-final-runtime] Awaiting actual foreground Game view (60-second bound); no timed case has started.");
                    }
                }
                if(Phase=="awaitingForeground")
                {
                    if(EditorApplication.timeSinceStartup>double.Parse(SessionState.GetString(Key+"focusDeadline","0"),System.Globalization.CultureInfo.InvariantCulture))
                        throw new TimeoutException("Foreground Game view was not observed within60 seconds; no timed cases started.");
                    if(!Application.isFocused||!GameViewFocused)
                    {SessionState.SetString(Key+"focusSince","-1");SessionState.SetInt(Key+"focusFirstFrame",-1);return;}
                    double since=double.Parse(SessionState.GetString(Key+"focusSince","-1"),System.Globalization.CultureInfo.InvariantCulture);
                    if(since<0){SessionState.SetString(Key+"focusSince",EditorApplication.timeSinceStartup.ToString("R",System.Globalization.CultureInfo.InvariantCulture));SessionState.SetInt(Key+"focusFirstFrame",Time.frameCount);return;}
                    if(EditorApplication.timeSinceStartup-since<.3d||Time.frameCount-SessionState.GetInt(Key+"focusFirstFrame",Time.frameCount)<3)return;
                    SessionState.SetString(Key+"foregroundStartup",JsonConvert.SerializeObject(new{application_focused=Application.isFocused,game_view_focused=GameViewFocused,focused_window=FocusedWindowType,stable_wall_seconds=EditorApplication.timeSinceStartup-since,observed_frame_advance=Time.frameCount-SessionState.GetInt(Key+"focusFirstFrame",Time.frameCount)}));
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
        private static readonly List<string> CadenceFailures = new List<string>();
        private static int _contactCasesCompleted;
        private static bool _reviewCompleted;
        private static CameraRig _cameraRig;
        private static bool GameViewFocused => EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView";
        private static string FocusedWindowType => EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().FullName : "none";
        private static bool CapturePictures=>SessionState.GetBool(Key+"pictures",false);
        private static string Evidence=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/character-candidates/motion-review-20261006"));
        private static IEnumerator Review()
        {
            Cases.Clear(); ContactFailures.Clear(); CadenceFailures.Clear(); _configured = false; _contactCasesCompleted = 0; _reviewCompleted = false;
            Require(FileHash("Assets/Scripts/World/Avatars/CafeWorkExitGoals.cs")=="ae8336d1fa9de5889f47e1393957e005c954672741e6aaadce49edb683a0ea82","exact isolated first-callback time-origin correction");
            Require(FileHash(Path.Combine(Evidence,"cafe-seat-integration/exit-traversal/work-clearance-v2/runtime-manifest.json"))=="1a803eccf7ca7ca7da5bdcd580a159a4609daf716b6a631f802a5fd618d7e2f8","exact coupled clearance revision manifest");
            var source=JObject.Parse(File.ReadAllText(Path.Combine(Evidence,"cafe-seat-integration/exit-traversal/work-clearance-v2/runtime-manifest.json")));
            string clockPinPath=Path.Combine(Evidence,"cafe-seat-integration/exit-traversal/first-callback-clock-v4/source-manifest.json");
            Require(FileHash(clockPinPath)=="af84f2745323aa1031542563b4c7f0ddca9031b62ac2244f825075ef9b840e0b","exact frozen first-callback correction manifest");
            var clockPin=JObject.Parse(File.ReadAllText(clockPinPath));
            Require((string)clockPin["source"]=="client/Assets/Scripts/World/Avatars/CafeWorkExitGoals.cs"&&(string)clockPin["candidate_sha256"]=="ae8336d1fa9de5889f47e1393957e005c954672741e6aaadce49edb683a0ea82","first-callback correction changes only expected runtime source");
            foreach(var file in ((JObject)source["runtime_files"]).Properties())
            {
                string expected=(string)file.Value;
                if(file.Name=="Assets/Scripts/World/Avatars/CafeWorkExitGoals.cs")
                {Require((string)clockPin["source_baseline_sha256"]==expected,"clock correction derives from frozen v3 runtime");expected=(string)clockPin["candidate_sha256"];}
                Require(FileHash(file.Name)==expected,"exact work-exit runtime imported "+file.Name);
            }
            foreach(var file in ((JObject)source["data_sources"]).Properties())Require(FileHash(Path.Combine(Evidence,file.Name))==(string)file.Value,"exact measured exit data "+file.Name);
            var rainContact=JObject.Parse(File.ReadAllText(Path.Combine(Evidence,"native-rain-walk-contact-v2/receipt.json")));
            Require((string)rainContact["revision"]=="rain-contact-source-relative-loop-v2"&&(bool?)rainContact["all_pass"]==true&&!rainContact["protected_drift"].Any(),"Rain contact-only candidate passed with inherited loop explicitly retained");
            Require(FileHash((string)rainContact["candidate_asset"])==(string)rainContact["candidate_sha256"],"exact saved Rain contact candidate");
            const string persistedReceipt="native-rain-walk-persistent-replay-v1/process-99210/receipt.json";
            string persistedPath=Path.Combine(Evidence,persistedReceipt);
            Require(FileHash(persistedPath)=="64e94dc0d5e0047595635f2d9e4f4b0ab8fa427e58291328502bd85521492bc1","exact preserved separate-process Rain replay receipt");
            var persistent=JObject.Parse(File.ReadAllText(persistedPath));
            Require((string)persistent["revision"]=="rain-saved-contact-fresh-process-v1"&&(bool?)persistent["all_pass"]==true&&(bool?)persistent["persistent_replay_verified"]==true&&!persistent["protected_drift"].Any(),"Rain saved replay passed independently of this room process");
            Require((int)persistent["process_id"]==99210&&DateTime.Parse((string)persistent["process_started_utc"]).ToUniversalTime()>DateTime.Parse((string)persistent["author_receipt_written_utc"]).ToUniversalTime(),"saved replay process followed authoring");
            Require((string)persistent["saved_asset"]==(string)rainContact["candidate_asset"]&&(string)persistent["saved_sha256"]==(string)rainContact["candidate_sha256"]&&FileHash((string)persistent["saved_asset"])==(string)persistent["saved_sha256"],"current Rain bytes match independently replayed saved clip");
            Require((float)persistent["author_to_saved_joint_error_m"]<=.0001f&&(float)persistent["author_to_saved_sole_error_m"]<=.0001f&&(float)persistent["author_to_saved_rotation_error_degrees"]<=.1f&&(int)persistent["native_contact_gates"]["dense_phases"]==241,"persistent full-cycle correspondence and all-skin contact evidence retained");
            var settle=JObject.Parse(File.ReadAllText(Path.Combine(Evidence,"native-cafe-walk-stop-contact-v1/receipt.json")));
            Require((string)settle["revision"]=="exit-only-native-fk-contact-settle-v1"&&(bool?)settle["capture_pass"]==true&&(bool?)settle["all_contact_cases_pass"]==true&&!settle["protected_drift"].Any(),"Snow exit-only settle all compared phases passed");
            foreach(var sample in settle["cases"].Where(c=>(bool)c["exit_settle_correction"]))
            {
                var frames=JArray.Parse(File.ReadAllText(Path.Combine(Evidence,"native-cafe-walk-stop-contact-v1/"+(string)sample["name"]+".json")));
                var last=frames.Last;Require((int)last["state"]==Animator.StringToHash("loco_idle")&&!(bool)last["transition"],"settle native final pose is actual idle outside transition");
            }
            var choreography=JObject.Parse(File.ReadAllText(Path.Combine(Evidence,"native-cafe-exit-second-step-v2/receipt.json")));
            Require((bool?)choreography["all_pass"]==true&&!choreography["protected_drift"].Any(),"inspected dense native choreography passed");
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
                var row=bindings.GetArrayElementAtIndex(i);row.FindPropertyRelative("baselineController").objectReferenceValue=entry.controllerOverride;row.FindPropertyRelative("ownAvatar").objectReferenceValue=avatar;var motion=MakeMotion(look,choreography);row.FindPropertyRelative("workExitMotion").objectReferenceValue=motion;
                row.FindPropertyRelative("cafeController").objectReferenceValue=MakeController(AssetDatabase.LoadAssetAtPath<AnimatorController>((string)proof["controller_asset"]),look,rainContact);
            }
            var routes=serialized.FindProperty("seatExits");routes.arraySize=1;var route=routes.GetArrayElementAtIndex(0);route.FindPropertyRelative("spotId").stringValue="terminal_1";
            Require(venue.TryGetSpotApproach("terminal_1",out var workApproach),"work approach exists");var points=route.FindPropertyRelative("points");points.arraySize=1;points.GetArrayElementAtIndex(0).vector3Value=venue.transform.InverseTransformPoint(workApproach);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var anchors=venue.Anchors.Where(a=>!string.IsNullOrEmpty(a.anim)).ToArray();Require(anchors.Length==12,"exact twelve final claimable anchors");
            Require(anchors.Count(a=>a.spotId.StartsWith("table_")&&a.anim=="act_sit")==4&&anchors.Count(a=>a.spotId.StartsWith("terminal_")&&a.anim=="act_type")==8,"final action/spot whitelist is intact");
            _navigation=NavMesh.AddNavMeshData(venue.navigation,venue.transform.position,venue.transform.rotation);Require(_navigation.valid,"actual final baked navigation added");Physics.SyncTransforms();
            foreach(var anchor in anchors)
            {
                Require(anchor.approach!=null,"actual approach "+anchor.spotId);var path=new NavMeshPath();Require(NavMesh.SamplePosition(venue.EntrancePosition,out var start,.75f,NavMesh.AllAreas)&&NavMesh.SamplePosition(anchor.approach.position,out var finish,.35f,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,finish.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete,"final entrance reaches "+anchor.spotId);
            }
            _cameraRig=CameraRig.Ensure();_cameraRig.ConfigureVenue(venue);_cameraRig.Camera.aspect=16f/9f;
            Require(_cameraRig.Camera.isActiveAndEnabled,"actual gameplay camera remains enabled for ordinary foreground rendering");
            foreach(string look in new[]{"snow","rain"})
            {
                var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+look));
                var view=Create(entry,venue.EntrancePosition);var local=view.gameObject.AddComponent<LocalPlayerController>();typeof(LocalPlayerController).GetMethod("Init").Invoke(local,new object[]{venue,null});local.ReviewCommandObserved+=Commands.Add;
                view.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;_cameraRig.Target=view.transform;_cameraRig.SnapToTarget();
                var agent=view.GetComponent<NavMeshAgent>();int ready=Time.frameCount+3;while(Time.frameCount<ready)yield return null;Require(agent.isOnNavMesh,"actual final-room agent ready");
                int before=SitCount();Require(local.GoSit("terminal_1"),"real approach path accepted");double deadline=EditorApplication.timeSinceStartup+18;while(SitCount()==before&&EditorApplication.timeSinceStartup<deadline)yield return null;
                Require(SitCount()==before+1&&string.IsNullOrEmpty(local.SeatedSpot),"real arrival dispatches intent without fabricating server acknowledgement");
                Require(venue.TryGetSpot("terminal_1",out var seat,out var yaw),"work anchor resolves");Require(Mathf.Abs(yaw)<.00001f,"terminal_1 furniture evidence frame faces positiveZ");
                string wireSpot=venue.WireSpotId("terminal_1");var configuredSpot=venue.SpotDef(wireSpot);
                Require(configuredSpot!=null&&configuredSpot.wireId==wireSpot&&wireSpot=="terminal_cafe.terminal_1"&&configuredSpot.anim=="act_type","exact configured global wire spot and typing presentation");
                local.OnServerSeat(wireSpot,"act_sit");Require(PrivateValue<string>(view,"_baseAnim")=="act_type","generic confirmed act_sit selects configured typing presentation without changing occupancy");
                var observe=Observe(view,look+"-local-instant-entry",.7f,null,false,seat);while(observe.MoveNext())yield return null;
                Require(view.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.TypingState),"actual fitted work loop active");
                CaptureBoundary(view,look+"-before-exit",seat);local.StandIfSeated();Require(view.SeatExitActive&&agent.enabled&&agent.isOnNavMesh,"actual stand restores logical root and starts independent presentation");
                var logical=view.transform.position;Require(Vector3.Distance(view.PresentationPosition,seat)<.00001f,"visible body remains at chair in same stand call");
                float began=Time.time;observe=Observe(view,look+"-local-exit-stand",3f,null,true,seat);while(observe.MoveNext())yield return null;
                Require(!view.SeatExitActive&&Vector3.Distance(view.PresentationPosition,view.transform.position)<.00001f,"actual complete work exit reconciles presentation");
                Require(Vector3.Distance(logical,view.transform.position)<.001f,"stand-only preserved logical approach");
                //Release while rising must cancel queued movement; no stale held stick after completion.
                var prepare=PrepareFreshSeat(local,view,venue,look+"-release-prepare",seat);while(prepare.MoveNext())yield return null;
                local.SetMoveInput(Vector2.up);Require(view.SeatExitActive&&local.DirectInputActive,"latest stick accepted during rise");
                Require(local.GoSit("terminal_2")&&local.GoSit("terminal_3")&&PrivateValue<string>(local,"_exitThenSit")=="terminal_3","newest path replaces prior path while exiting");
                local.SetMoveInput(Vector2.up);Require(!PrivateValue<bool>(local,"_exitPathQueued"),"new direct intent replaces queued path");local.SetMoveInput(Vector2.zero);Require(!local.DirectInputActive,"stick release clears pending input");
                logical=view.transform.position;observe=Observe(view,look+"-released-exit",3f,null,false,seat);while(observe.MoveNext())yield return null;
                Require(!view.SeatExitActive&&Vector3.Distance(logical,view.transform.position)<.001f,"released input does not walk after exit");
                //Held input is accepted immediately, but actual movement latency is measured honestly.
                prepare=PrepareFreshSeat(local,view,venue,look+"-direct-prepare",seat);while(prepare.MoveNext())yield return null;
                local.SetMoveInput(Vector2.down);logical=view.transform.position;began=Time.time;float movementAt=-1;float presentationAt=-1;
                observe=Observe(view,look+"-held-input-exit",3.6f,()=>{local.SetMoveInput(Vector2.down);if(presentationAt<0&&!view.SeatExitActive)presentationAt=Time.time-began;if(movementAt<0&&Vector3.Distance(logical,view.transform.position)>.02f)movementAt=Time.time-began;},false,seat);while(observe.MoveNext())yield return null;
                Cases.Add(new{name=look+"-input-latency",presentation_complete_seconds=presentationAt,logical_movement_seconds=movementAt,scope=CapturePictures?"synchronous screenshots disturb timing; not feel or device latency evidence":"actual Play time without screenshot/mesh exports; CPU foot sampling remains, not device performance acceptance"});
                Require(movementAt>=0&&presentationAt>=0,"held latest input resumes actual NavMesh movement after presentation");local.StopDirectInput();local.CancelMovement();
                Object.Destroy(view.gameObject);while(view!=null)yield return null;
                //Colyseus mutates the same object. Reuse one instance in every update below.
                Require(venue.TryGetSpotApproach("terminal_1",out var remotePoint),"remote approach resolves");var remoteView=Create(entry,remotePoint);var remote=remoteView.gameObject.AddComponent<RemotePlayer>();var stateType=typeof(RemotePlayer).GetMethod("Init").GetParameters()[1].ParameterType;object packet=Activator.CreateInstance(stateType);
                void Mutate(string spot,string anim){stateType.GetField("name").SetValue(packet,"review remote");stateType.GetField("spotId").SetValue(packet,spot);stateType.GetField("anim").SetValue(packet,anim);var p=venue.ToLocal(remotePoint);stateType.GetField("x").SetValue(packet,(double)p.x);stateType.GetField("z").SetValue(packet,(double)p.y);}
                void Apply(string spot,string anim){Mutate(spot,anim);typeof(RemotePlayer).GetMethod("Apply").Invoke(remote,new[]{packet,(object)false});}
                Mutate("terminal_1","act_type");typeof(RemotePlayer).GetMethod("Init").Invoke(remote,new[]{(object)venue,packet});remoteView.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;_cameraRig.Target=remoteView.transform;_cameraRig.SnapToTarget();
                observe=Observe(remoteView,look+"-remote-prepare",.7f,null,false,seat);while(observe.MoveNext())yield return null;
                Apply(null,"act_type");Require(remoteView.SeatExitActive,"same-instance remote release with lingering old action starts exit");Apply(null,"act_type");Require(remoteView.SeatExitActive,"repeated stale seated action does not cancel exit");
                observe=Observe(remoteView,look+"-remote-exit",3f,null,true,seat);while(observe.MoveNext())yield return null;
                Require(!remoteView.SeatExitActive&&Vector3.Distance(remoteView.PresentationPosition,remoteView.transform.position)<.015f,"actual remote exit reconciles logical root");Apply(null,"loco_idle");
                foreach(float delay in new[]{.2f,.85f})
                {
                    Apply("terminal_1","act_type");observe=Observe(remoteView,look+"-reseat-prepare-"+delay,.6f,null,false,seat);while(observe.MoveNext())yield return null;
                    Apply(null,"loco_idle");float until=Time.time+delay;while(Time.time<until)yield return null;Apply("terminal_1","act_type");
                    Require(!remoteView.SeatExitActive,"new confirmed seat cancels authored exit");observe=Observe(remoteView,look+"-reseat-"+delay,.6f,null,false,seat);while(observe.MoveNext())yield return null;
                    Require(remoteView.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.TypingState),"new seat returns to exact fitted loop");
                }
                Apply(null,"loco_idle");remoteView.ApplyLod(0,true,1f);observe=Observe(remoteView,look+"-culled",.15f,null,false,seat);while(observe.MoveNext())yield return null;Require(!remoteView.SeatExitActive,"culled exit cancels stale presentation");remoteView.ApplyLod(0,false,1f);
                observe=Observe(remoteView,look+"-resumed",.6f,null,false,seat);while(observe.MoveNext())yield return null;Require(!remoteView.SeatExitActive,"visible resume uses latest state");
                Apply("terminal_1","act_type");observe=Observe(remoteView,look+"-focus-prepare",.6f,null,false,seat);while(observe.MoveNext())yield return null;Apply(null,"loco_idle");typeof(AvatarView).GetMethod("OnApplicationFocus",Private).Invoke(remoteView,new object[]{false});Require(!remoteView.SeatExitActive,"focus loss cancels presentation");
                Object.Destroy(remoteView.gameObject);while(remoteView!=null)yield return null;
            }
            _reviewCompleted=true;Require(_contactCasesCompleted==4,"both looks actual local+remote work exit contact observations completed");Require(ContactFailures.Count==0,"actual work exit observed contacts stay above minus2mm");Require(CapturePictures||CadenceFailures.Count==0,"desktop cadence observed near requested60Hz in all no-image cases");
        }
        private static IEnumerator PrepareFreshSeat(LocalPlayerController local,AvatarView view,VenueSceneRoot venue,string label,Vector3 seat)
        {
            Require(!view.SeatExitActive&&string.IsNullOrEmpty(local.SeatedSpot),"repeat setup starts from completed free state");
            string wire=venue.WireSpotId("terminal_1");int before=SitCount();Require(local.GoSit(wire),"fresh actual GoSit request accepted for "+label);
            double deadline=EditorApplication.timeSinceStartup+18d;while(SitCount()==before&&EditorApplication.timeSinceStartup<deadline)yield return null;
            Require(SitCount()==before+1&&string.IsNullOrEmpty(local.SeatedSpot),"fresh actual arrival sends one intent before injected confirmation for "+label);
            local.OnServerSeat(wire,"act_sit");
            Require(local.SeatedSpot==wire&&Vector3.Distance(view.transform.position,seat)<.00001f,"fresh confirmed seat accepted at actual anchor for "+label);
            var observe=Observe(view,label,.6f,null,false,seat);while(observe.MoveNext())yield return null;
            Require(view.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.TypingState)&&!view.Animator.IsInTransition(0),"repeat setup reaches actual fitted typing for "+label);
        }
        private static CafeWorkExitMotion MakeMotion(string look,JObject choreography)
        {
            var row=choreography["cases"].Single(c=>(string)c["character"]==look);Require((bool?)row["all_pass"]==true,"per-look native choreography passed");
            var original=JArray.Parse(File.ReadAllText(Path.Combine(Evidence,"native-cafe-exit-second-step-v2/"+look+"-original.json")));
            Vector3 V(JToken t){var a=t.Values<float>().ToArray();return new Vector3(a[0],a[1],a[2]);}
            Quaternion Q(JToken t){var a=t.Values<float>().ToArray();return new Quaternion(a[0],a[1],a[2],a[3]);}
            var motion=ScriptableObject.CreateInstance<CafeWorkExitMotion>();TemporaryControllers.Add(motion);motion.hands=look=="rain";motion.settleContact=look=="snow";motion.frames=new CafeWorkExitMotion.Frame[CafeWorkExitMotion.SampleCount];
            Require(original.Count==CafeWorkExitMotion.SampleCount&&row["full_body_offset_samples"].Count()==CafeWorkExitMotion.SampleCount&&Mathf.Abs((float)row["length_seconds"]-CafeWorkExitMotion.Length)<.00001f,"exact measured motion sample count and source duration");
            Quaternion[] handOffset=null;if(motion.hands){var hand=JObject.Parse(File.ReadAllText(Path.Combine(Evidence,"native-typing-work-hands-v2/receipt.json")));Require((bool?)hand["all_pass"]==true&&!hand["protected_drift"].Any(),"native hand-goal coordinate frame passed");handOffset=hand["cases"].Single(c=>(string)c["character"]==look)["goal_to_wrist_orientation_calibration"].Select(c=>Q(c["local_offset"])).ToArray();}
            float Ease(float u){u=Mathf.Clamp01(u);return u*u*(3-2*u);}
            for(int i=0;i<CafeWorkExitMotion.SampleCount;i++)
            {
                var f=new CafeWorkExitMotion.Frame{body=V(row["full_body_offset_samples"][i]),leftFoot=V(row["goal_positions"][0])+V(row["foot_offset_samples"][i][0]),rightFoot=V(row["goal_positions"][1])+V(row["foot_offset_samples"][i][1]),leftFootRotation=Q(row["goal_rotations"][0]),rightFootRotation=Q(row["goal_rotations"][1])};motion.frames[i]=f;
                if(motion.hands)
                {
                    float seconds=i/120f,envelope=Ease(seconds/.20f)*(1-Ease((seconds-.60f)/.26f));var left=new Vector3(-.08f,.04f,.08f)*envelope;var right=new Vector3(.08f,.04f,.08f)*envelope;
                    f.leftHand=V(original[i]["joints"][13])+f.body+left;f.rightHand=V(original[i]["joints"][16])+f.body+right;
                    f.leftElbow=V(original[i]["joints"][12])+f.body+left*.5f;f.rightElbow=V(original[i]["joints"][15])+f.body+right*.5f;
                    f.leftHandRotation=Q(original[i]["rotations"][13])*Quaternion.Inverse(handOffset[0]);f.rightHandRotation=Q(original[i]["rotations"][16])*Quaternion.Inverse(handOffset[1]);
                }
            }
            return motion;
        }
        private static AnimatorController MakeController(AnimatorController baseline,string look,JObject rainContact)
        {
            Require(baseline.layers.Length==1,"flat accepted controller layer");var original=baseline.layers[0];var source=original.stateMachine;
            Require(source.stateMachines.Length==0&&source.anyStateTransitions.Length==0&&source.entryTransitions.Length==0&&source.behaviours.Length==0&&source.states.All(s=>s.state.transitions.Length==0&&s.state.behaviours.Length==0),"flat source has no hidden behaviour/transition ownership");
            var copy=new AnimatorController();var machine=new AnimatorStateMachine();TemporaryControllers.Add(copy);TemporaryControllers.Add(machine);copy.parameters=baseline.parameters;
            foreach(var child in source.states){var state=machine.AddState(child.state.name);TemporaryControllers.Add(state);EditorUtility.CopySerialized(child.state,state);if(child.state==source.defaultState)machine.defaultState=state;}
            if(look=="rain")
            {
                var walk=machine.states.Single(s=>s.state.name=="loco_walk").state;
                Require(AssetDatabase.GetAssetPath(walk.motion)==(string)rainContact["source_asset"]&&walk.iKOnFeet,"Rain comparison replaces only the expected raw walking clip with FootIK retained");
                walk.motion=AssetDatabase.LoadAssetAtPath<AnimationClip>((string)rainContact["candidate_asset"]);Require(walk.motion!=null,"saved Rain contact clip loaded in this fresh process");
            }
            var exit=Object.Instantiate(AssetDatabase.LoadAllAssetsAtPath("Assets/Art/animations/act_stand_up.fbx").OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__")));TemporaryControllers.Add(exit);var settings=AnimationUtility.GetAnimationClipSettings(exit);settings.loopTime=false;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(exit,settings);
            var authored=machine.AddState(CafeSeatMotionProfile.WorkExitState);TemporaryControllers.Add(authored);authored.motion=exit;authored.speed=CafeWorkExitMotion.Rate;authored.speedParameterActive=false;authored.iKOnFeet=false;authored.writeDefaultValues=true;
            var sourceIdle=source.states.Single(s=>s.state.name=="loco_idle").state;var idle=machine.AddState(CafeSeatMotionProfile.WorkExitIdleState);TemporaryControllers.Add(idle);EditorUtility.CopySerialized(sourceIdle,idle);idle.name=CafeSeatMotionProfile.WorkExitIdleState;idle.speed=CafeWorkExitMotion.Rate;idle.speedParameterActive=false;
            copy.layers=new[]{new AnimatorControllerLayer{name=original.name,defaultWeight=original.defaultWeight,iKPass=true,stateMachine=machine,blendingMode=original.blendingMode,avatarMask=original.avatarMask}};
            return copy; //Entirely transient. No persistent shared state machine is modified.
        }
        private static AvatarView Create(CharacterRegistry.Entry entry,Vector3 position)
        {
            var view=AvatarFactory.Create("Final Cafe runtime review",entry.key.Split('|'),false,null,_root.transform);view.transform.SetPositionAndRotation(position,Quaternion.identity);view.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;foreach(var group in view.GetComponentsInChildren<LODGroup>(true))group.ForceLOD(0);return view;
        }
        private static string Bridge(AvatarView view)=>PrivateValue<string>(view,"_seatBridgeTarget");
        private static IEnumerator Observe(AvatarView view,string name,float duration,Action input,bool contactScope,Vector3? fixedFocus=null)
        {
            float begin=Time.time;int last=-1;var frames=new List<object>();var images=new List<object>();float[] pictureTimes={0f,.25f,.55f,.85f,1.15f,1.5f,1.9f,2.4f};int nextPicture=0;float minimum=float.PositiveInfinity;var start=view.transform.position;double wallBegin=EditorApplication.timeSinceStartup;double wallDeadline=wallBegin+15d;Vector3 focus=fixedFocus??start;
            Require(Application.targetFrameRate==60&&QualitySettings.vSyncCount==0,"explicit desktop target60/vSync0 settings for "+name);
            var observedDeltas=new List<float>();var observedFrames=new HashSet<int>();
            var frozenStandClip=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/animations/act_stand_up.fbx").OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
            float nativeGoalClockError=0;int nativeGoalClockSamples=0;bool firstGoalCallbackObserved=false;
            int unfocusedFrames=0;double contactSampleCpuTotal=0,contactSampleCpuMaximum=0;
            var phaseCounts=new Dictionary<string,int>();float largestFrameDelta=0f;var goals=view.Animator.GetComponent<CafeWorkExitGoals>();
            var skins=view.GetComponentsInChildren<SkinnedMeshRenderer>(true);var feet=skins.Select(s=>{
                var bones=new HashSet<Transform>(new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes}.Select(view.Animator.GetBoneTransform));var weights=s.sharedMesh.boneWeights;
                return Enumerable.Range(0,weights.Length).Where(i=>{var w=weights[i];float W(int b,float v)=>v>0&&bones.Contains(s.bones[b])?v:0;return W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3)>.5f;}).ToArray();}).ToArray();
            Require(skins.Length==3&&feet.All(indices=>indices.Length>0),"all three authored LOD skins have measured foot geometry for "+name);
            var mesh=new Mesh();try
            {
                // Synchronous PNG/export work can consume several real frames. Diagnostic pictures
                // observe an exit through completion with a bounded extra window; normal
                // no-image timing retains its original duration and completion assertion.
                float diagnosticCompletedAt=-1f;
                while(Time.time-begin<duration || frames.Count<3 ||
                    (CapturePictures && duration>=3f && Time.time-begin<duration+3f &&
                        (view.SeatExitActive || diagnosticCompletedAt<0f || Time.time-diagnosticCompletedAt<.35f)))
                {
                    if(EditorApplication.timeSinceStartup>wallDeadline)throw new TimeoutException("Actual Play observation exceeded 15 seconds: "+name);
                    if(Time.frameCount==last){yield return null;continue;}last=Time.frameCount;if(!observedFrames.Add(Time.frameCount))throw new InvalidOperationException("Duplicate game frame in room observation.");
                    if(CapturePictures && duration>=3f && !view.SeatExitActive && diagnosticCompletedAt<0f) diagnosticCompletedAt=Time.time;
                    long sampleCpuStart=System.Diagnostics.Stopwatch.GetTimestamp();bool appFocused=Application.isFocused,gameFocused=GameViewFocused;if(!appFocused||!gameFocused)unfocusedFrames++;
                    var settleOwner=PrivateValue<CafeExitSettleContact>(view,"_exitSettleContact");Require(settleOwner==null||settleOwner.Failure==null,"exit settle owner has no bounded-solve failure in "+name);
                    observedDeltas.Add(Time.deltaTime);input?.Invoke();float sole=float.PositiveInfinity;var tiers=new List<float>();
                    for(int t=0;t<skins.Length;t++){skins[t].BakeMesh(mesh,false);var vertices=mesh.vertices;var matrix=view.transform.worldToLocalMatrix*skins[t].localToWorldMatrix;float y=feet[t].Min(i=>matrix.MultiplyPoint3x4(vertices[i]).y);tiers.Add(y);sole=Mathf.Min(sole,y);}
                    minimum=Mathf.Min(minimum,sole);string stage=view.SeatExitStage;phaseCounts[stage]=phaseCounts.TryGetValue(stage,out int count)?count+1:1;largestFrameDelta=Mathf.Max(largestFrameDelta,Time.deltaTime);var info=view.Animator.GetCurrentAnimatorStateInfo(0);var pos=view.transform.position;var hips=view.transform.InverseTransformPoint(view.Animator.GetBoneTransform(HumanBodyBones.Hips).position);
                    var workInfo=info;bool hasWorkClock=goals!=null&&goals.Running&&info.IsName(CafeSeatMotionProfile.WorkExitState);
                    if(!hasWorkClock&&goals!=null&&goals.Running&&view.Animator.IsInTransition(0))
                    {var next=view.Animator.GetNextAnimatorStateInfo(0);if(next.IsName(CafeSeatMotionProfile.WorkExitState)){hasWorkClock=true;workInfo=next;}}
                    float workSourceSeconds=hasWorkClock?workInfo.normalizedTime*frozenStandClip.length:-1f;
                    if(hasWorkClock&&goals.CallbackCount>0)
                    {
                        nativeGoalClockSamples++;nativeGoalClockError=Mathf.Max(nativeGoalClockError,Mathf.Abs(goals.Seconds-workSourceSeconds));
                        if(goals.CallbackCount==1){firstGoalCallbackObserved=true;Require(goals.Seconds<=.00001f&&Mathf.Abs(workSourceSeconds)<=.002f,"first actual goal callback shares native source time0 in "+name);}
                    }
                    double sampleCpuMs=(System.Diagnostics.Stopwatch.GetTimestamp()-sampleCpuStart)*1000d/System.Diagnostics.Stopwatch.Frequency;contactSampleCpuTotal+=sampleCpuMs;contactSampleCpuMaximum=Math.Max(contactSampleCpuMaximum,sampleCpuMs);
                    frames.Add(new{frame=Time.frameCount,actual_frame_delta=Time.deltaTime,application_focused=appFocused,game_view_focused=gameFocused,focused_window=FocusedWindowType,gameplay_camera_enabled=_cameraRig.Camera.isActiveAndEnabled,contact_sampling_cpu_ms=sampleCpuMs,work_source_seconds=workSourceSeconds,work_goal_clock_error=hasWorkClock?Mathf.Abs(goals.Seconds-workSourceSeconds):-1f,goal_callbacks=goals!=null?goals.CallbackCount:0,goal_source_seconds=goals!=null?goals.Seconds:0f,seconds=Time.time-begin,wall_seconds=EditorApplication.timeSinceStartup-wallBegin,root=new[]{pos.x,pos.y,pos.z},hips_local=new[]{hips.x,hips.y,hips.z},soles_local=tiers,animator_enabled=view.Animator.enabled,state_hash=info.shortNameHash,native_phase=info.normalizedTime,transition=view.Animator.IsInTransition(0),queued=Bridge(view),canonical_base=PrivateValue<string>(view,"_baseAnim"),base_spot=PrivateValue<string>(view,"_baseSpot"),presentation_position=new[]{view.PresentationPosition.x,view.PresentationPosition.y,view.PresentationPosition.z},exit_stage=view.SeatExitStage,exit_active=view.SeatExitActive,animator_speed_parameter=view.Animator.GetFloat(AnimationLibrary.SpeedParam)});
                    if(CapturePictures&&(contactScope||name.Contains("exit"))&&!view.Culled&&view.Animator.enabled&&nextPicture<pictureTimes.Length&&Time.time-begin>=pictureTimes[nextPicture])
                    {
                        string pictureOutput=SessionState.GetString(Key+"output","");Directory.CreateDirectory(pictureOutput);string file=name+"-"+nextPicture+".png";CaptureCpuPose(view,focus,Path.Combine(pictureOutput,file));string oblique=Path.GetFileNameWithoutExtension(file)+"-oblique.png";images.Add(new{file,sha256=FileHash(Path.Combine(pictureOutput,file)),oblique_file=oblique,oblique_sha256=FileHash(Path.Combine(pictureOutput,oblique)),actual_seconds=Time.time-begin,native_frame=Time.frameCount,native_phase=info.normalizedTime});nextPicture++;
                    }
                    yield return null;
                }
            }
            finally{Object.Destroy(mesh);}
            string output=SessionState.GetString(Key+"output","");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,name+".json"),JsonConvert.SerializeObject(frames,Formatting.Indented));
            var sortedDeltas=observedDeltas.OrderBy(x=>x).ToArray();float medianDelta=sortedDeltas[sortedDeltas.Length/2];
            bool desktopCadence=!CapturePictures&&medianDelta>=.0125f&&medianDelta<=.025f&&unfocusedFrames==0;if(!CapturePictures&&!desktopCadence)CadenceFailures.Add(name);
            var settleMetrics=PrivateValue<CafeExitSettleContact>(view,"_exitSettleContact");
            Cases.Add(new{name,unfocused_frames=unfocusedFrames,foreground_observed=unfocusedFrames==0,contact_sampling_cpu_total_ms=contactSampleCpuTotal,contact_sampling_cpu_maximum_ms=contactSampleCpuMaximum,contact_sampling_timing_scope="Helper all-LOD CPU pose witness only, including per-frame assertions; excludes game update/rendering and PNG output. Not player performance.",native_goal_clock_samples=nativeGoalClockSamples,first_goal_callback_observed=firstGoalCallbackObserved,maximum_native_goal_clock_error_seconds=nativeGoalClockError,settle_apply_calls=PrivateValue<int>(view,"_exitSettleApplyCalls"),settle_cpu_total_ms=PrivateValue<double>(view,"_exitSettleTotalMilliseconds"),settle_cpu_maximum_ms=PrivateValue<double>(view,"_exitSettleMaximumMilliseconds"),settle_cached_shoe_vertices=settleMetrics!=null?settleMetrics.ShoeVertexCount:0,settle_cached_matrices=settleMetrics!=null?settleMetrics.MatrixCount:0,settle_timing_scope="Per-current-exit cumulative measured desktop CPU callback time; no Seeker or simultaneous-crowd performance claim",frames=frames.Count,unique_game_frames=observedFrames.Count,target_frame_rate=60,v_sync_count=0,median_observed_frame_delta_seconds=medianDelta,desktop_cadence_observed=desktopCadence,capture_mode=CapturePictures?"diagnostic-synchronous-images":"no-images-timing-contact",phase_frame_counts=phaseCounts,largest_observed_frame_delta_seconds=largestFrameDelta,requested_seconds=duration,actual_seconds=Time.time-begin,wall_seconds=EditorApplication.timeSinceStartup-wallBegin,minimum_sole_local_y_m=minimum,root_displacement_m=Vector3.Distance(start,view.transform.position),contact_scope=contactScope,contact_pass=minimum>=-.002f,images,visual_acceptance=false});
            Require(frames.Count>=3,"real Play frames observed for "+name);
            if(nativeGoalClockSamples>0)Require(nativeGoalClockError<=.002f,"actual native stand and goal clocks agree within2ms in "+name);
            if(contactScope)Require(firstGoalCallbackObserved,"actual first goal callback was observed in "+name);
            if(contactScope) { _contactCasesCompleted++; if(minimum<-.002f) ContactFailures.Add(name); }
            // Capture all cases before the aggregate contact gate; injected state is not connected authority.
        }
        private static void CaptureBoundary(AvatarView view,string name,Vector3 focus)
        {
            if(!CapturePictures)return;
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
                light.enabled=false; //Actual game camera uses the room lighting, not the diagnostic fill.
                var gameRt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);gameRt.Create();var gameImage=new Texture2D(1280,720,TextureFormat.RGB24,false);
                try{var gameRequest=new UniversalRenderPipeline.SingleCameraRequest{destination=gameRt};Require(RenderPipeline.SupportsRenderRequest(_cameraRig.Camera,gameRequest),"actual game-camera capture supported");RenderPipeline.SubmitRenderRequest(_cameraRig.Camera,gameRequest);RenderTexture.active=gameRt;gameImage.ReadPixels(new Rect(0,0,1280,720),0,0);gameImage.Apply();File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"-gameplay.png"),gameImage.EncodeToPNG());}
                finally{Object.DestroyImmediate(gameImage);gameRt.Release();Object.DestroyImmediate(gameRt);}
                //Exact all-LOD body witnesses in terminal_1's frozen furniture frame, for a
                //bounded moving-pose intersection audit after the actual room run.
                using(var writerMesh=new MeshScope())foreach(var skin in skins)
                {
                    skin.BakeMesh(writerMesh.mesh,false);var verts=writerMesh.mesh.vertices.Select(v=>skin.transform.TransformPoint(v)-focus).ToArray();
                    string meshPath=Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"-tier"+Array.IndexOf(skins,skin)+".obj.gz");
                    using var file=File.Create(meshPath);using var zip=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionLevel.Fastest);using var writer=new StreamWriter(zip);
                    foreach(var v in verts)writer.WriteLine("v "+v.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" "+v.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" "+v.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture));var triangles=skin.sharedMesh.triangles;for(int t=0;t<triangles.Length;t+=3)writer.WriteLine($"f {triangles[t]+1} {triangles[t+1]+1} {triangles[t+2]+1}");
                }
            }
            finally
            {
                RenderTexture.active=previous;if(image!=null)Object.DestroyImmediate(image);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(snapshot!=null)Object.DestroyImmediate(snapshot);if(cameraObject!=null)Object.DestroyImmediate(cameraObject);if(lightObject!=null)Object.DestroyImmediate(lightObject);Object.DestroyImmediate(mesh);
                for(int i=0;i<groups.Length;i++)groups[i].enabled=groupEnabled[i];for(int i=0;i<skins.Length;i++)skins[i].enabled=enabled[i];
            }
        }
        private sealed class MeshScope:IDisposable { public readonly Mesh mesh=new Mesh();public void Dispose()=>Object.DestroyImmediate(mesh); }
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
            PhaseIs("stopping"); //Own the stop before any cleanup/serialization can throw.
            SessionState.SetString(Key+"stopDeadline",(EditorApplication.timeSinceStartup+15d).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            SessionState.SetInt(Key+"result",failure==null?0:1);
            var cleanup=new List<string>();
            void Attempt(Action action)
            {
                try{action();}catch(Exception error){cleanup.Add(error.ToString());SessionState.SetInt(Key+"result",1);Debug.LogError("Cafe exit review cleanup: "+error);}
            }
            try
            {
                Attempt(()=>{(_steps as IDisposable)?.Dispose();_steps=null;});
                Attempt(()=>{if(_navigation.valid)_navigation.Remove();_navigation=default;});
                Attempt(()=>{if(_root!=null)Object.Destroy(_root);_root=null;});
                foreach(var temporary in TemporaryControllers)Attempt(()=>{if(temporary!=null)Object.Destroy(temporary);});
                TemporaryControllers.Clear();
                Attempt(()=>{if(_cameraRig!=null)Object.Destroy(_cameraRig.gameObject);_cameraRig=null;});
                Attempt(()=>{if(_configured){try{if(_hadConfig)Services.Register(_previousConfig);else Services.Unregister<ConfigStore>();}finally{_configured=false;}}});
                string output=SessionState.GetString(Key+"output","/tmp/ct-cafe-continuous-exit-v1-20261007");
                Attempt(()=>
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output,"receipt.json"),JsonConvert.SerializeObject(new
            {
                status = failure == null && cleanup.Count==0 ? "work_exit_room_components_captured_visual_and_connected_review_pending" : "failed",
                revision = "actual-work-exit-room-clock-v4-foreground-or-diagnostic-v5d",
                timing_eligible = !CapturePictures,
                foreground_startup = SessionState.GetString(Key+"foregroundStartup","not-observed"),
                requested_desktop_target_frame_rate = 60, requested_v_sync_count = 0,
                rain_gait_scope = "Transient review controller uses the saved contact-only Rain candidate; original loop seam remains explicitly retained. No registry/controller asset is saved.",
                desktop_cadence_scope = "Game-view target, not fixed simulation or phone performance. Actual frame deltas/unique frames and app/Game-view focus recorded per case; any observed focus loss fails cadence. No artificial player-loop advancement. Diagnostic PNG mode excluded from timing acceptance.",
                capture_mode = CapturePictures?"diagnostic-synchronous-images":"no-images-timing-contact",
                component_checks_pass = failure == null && cleanup.Count==0,
                review_completed = _reviewCompleted,
                fitted_contact_cases_completed = _contactCasesCompleted,
                fitted_contact_cases_required = 4,
                observed_fitted_contacts_pass = _contactCasesCompleted > 0 && ContactFailures.Count == 0,
                fitted_transition_contact_all_pass = _reviewCompleted && _contactCasesCompleted == 4 && ContactFailures.Count == 0,
                contact_failures = ContactFailures, desktop_cadence_failures = CadenceFailures, desktop_cadence_all_pass = _reviewCompleted&&!CapturePictures&&CadenceFailures.Count==0,
                bridge_runtime_sha256 = FileHash("Assets/Scripts/World/Avatars/AvatarView.cs"),
                helper_sha256 = FileHash("Assets/Scripts/Editor/CafeContinuousExitRoomReview.cs"),
                cases = Cases, prefab = FinalPrefab, prefab_sha256 = File.Exists(FinalPrefab) ? FileHash(FinalPrefab) : null,
                profile_scope = "bindings filled on runtime clone only; no prefab/registry assignment",
                frame_scope = "actual Play frames with actual LocalPlayerController/RemotePlayer; Animator AlwaysAnimate except explicit culled test",
                checks = Checks, observedEditorIntents = Commands, networkChannel = "null; no server calls",
                limitations = "No deployed server or phone-device validation. OnServerSeat/RemotePlayer state inputs are explicitly injected, never packets or claimed backend authority. All-tier pose contact and moving-root traces require independent visual/whole-furniture review. Input is accepted/replaced immediately; logical translation waits for authored presentation. This delay is recorded and needs interactive review. Instant seat entry remains unchanged. Social-chair exits are excluded. The bounded two-foot/Rain-hand solver is enabled only during each1.47s authored exit; its controller IK pass still needs mobile cost measurement. CPU sampling time is not a frame-performance benchmark.",
                cleanup_failures = cleanup,
                failure = failure?.ToString()

                    },Formatting.Indented));
                });
                if(failure!=null)Debug.LogException(failure);
            }
            finally
            {
                Attempt(RestoreCadence);
                Attempt(()=>{if(EditorApplication.isPlaying)Application.runInBackground=SessionState.GetBool(Key+"originalRunInBackground",false);});
                SessionState.SetString(Key+"stopCleanup",JsonConvert.SerializeObject(cleanup));
                //Even a failed receipt/owner cleanup must release Play mode and restore the saved scene.
                if(EditorApplication.isPlayingOrWillChangePlaymode)Attempt(()=>EditorApplication.ExitPlaymode());
                else Finish();
            }
        }
        private static void RestoreCadence()
        {
            if(!SessionState.GetBool(Key+"cadenceOwned",false))return;
            Application.targetFrameRate=SessionState.GetInt(Key+"originalTargetFrameRate",-1);
            QualitySettings.vSyncCount=SessionState.GetInt(Key+"originalVSyncCount",0);
            if(Application.targetFrameRate!=SessionState.GetInt(Key+"originalTargetFrameRate",-1)||QualitySettings.vSyncCount!=SessionState.GetInt(Key+"originalVSyncCount",0))
                throw new InvalidOperationException("Room review cadence settings did not restore.");
            SessionState.SetBool(Key+"cadenceOwned",false);
        }
        private static void Finish()
        {
            var failures=new List<string>();
            void Attempt(Action action)
            {
                try{action();}catch(Exception error){failures.Add(error.ToString());SessionState.SetInt(Key+"result",1);Debug.LogError("Cafe exit review finalization: "+error);}
            }
            string output=SessionState.GetString(Key+"output","/tmp/ct-cafe-continuous-exit-v1-20261007");int driftCount=-1;
            Attempt(RestoreCadence);
            try
            {
                Attempt(()=>
                {
                    var scenes=JsonConvert.DeserializeObject<SavedScene[]>(SessionState.GetString(Key+"scenes","[]"));
                    if(scenes.Any(scene=>!string.IsNullOrEmpty(scene.path)))EditorSceneManager.RestoreSceneManagerSetup(scenes.Select(scene=>new SceneSetup{path=scene.path,isLoaded=scene.isLoaded,isActive=scene.isActive}).ToArray());
                    else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                });
                Attempt(()=>
                {
                    var before=JsonConvert.DeserializeObject<Dictionary<string,string>>(SessionState.GetString(Key+"protection","{}"));var after=ProtectedHashes();
                    var drift=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var oldHash)||!after.TryGetValue(k,out var nextHash)||oldHash!=nextHash).ToArray();driftCount=drift.Length;
                    if(drift.Length!=0)SessionState.SetInt(Key+"result",1);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"protected-drift.json"),JsonConvert.SerializeObject(new{count=before.Count,drift},Formatting.Indented));
                });
                Attempt(()=>{Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"finish-cleanup.json"),JsonConvert.SerializeObject(new{failures,stop_cleanup=SessionState.GetString(Key+"stopCleanup","[]"),result=SessionState.GetInt(Key+"result",1)},Formatting.Indented));});
            }
            finally
            {
                int result=SessionState.GetInt(Key+"result",1);SessionState.SetBool(Key+"active",false);_steps=null;
                Debug.Log("CTLIFE [cafe-continuous-exit] END exit="+result+" protectedDrift="+driftCount);
                if(SessionState.GetBool(Key+"exit",false))EditorApplication.Exit(result);
            }
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
