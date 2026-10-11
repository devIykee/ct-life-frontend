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
    public static class CafeFinalRoomReview
    {
        private const string Key = "CTLife.CafeFinalRoomReview.";
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
        private const string Candidate = "Assets/Art/Candidates/TerminalCafe20261006Final/TerminalCafeFinal.prefab";
        private const string Baseline = "Assets/Art/Candidates/TerminalCafe20261006B3Social/TerminalCafeB3Social.prefab";
        private static readonly List<object> Rays = new List<object>();
        [Serializable] private sealed class SavedScene { public string path; public bool isLoaded, isActive; }
        static CafeFinalRoomReview()
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
            if(!File.Exists(Candidate))throw new FileNotFoundException("Stage the exact final Café prefab first.",Candidate);
            SessionState.SetString(Key+"candidateHash",Hash(Candidate));
            var setup = EditorSceneManager.GetSceneManagerSetup();
            SessionState.SetString(Key + "scenes", JsonConvert.SerializeObject(setup.Select(s => new SavedScene
                { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive })));
            SessionState.SetString(Key + "protection", JsonConvert.SerializeObject(ProtectedHashes()));
            SessionState.SetString(Key + "output", Argument("cafeFinalOutput") ?? "/tmp/ct-cafe-final-room-native-20261006");
            SessionState.SetBool(Key + "exit", Argument("cafeFinalExit") == "1");
            SessionState.SetInt(Key + "result", 1);
            SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 360d).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "active", true);
            PhaseIs("startup");
            Debug.Log("CTLIFE [cafe-final-room] Waiting for imports and Search database initialization.");
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
                    Checks.Clear(); Commands.Clear(); Rays.Clear(); Walks.Clear(); Captures.Clear(); _moveIntents=0;
                    _steps = Review();
                    PhaseIs("running");
                }
                if (Phase == "running" && !_steps.MoveNext()) Stop(null);
            }
            catch (Exception e) { Stop(e); }
        }

        private sealed class Probe
        {
            public string spot, part;
            public Vector3 world, approach;
        }
        private static IEnumerator Review()
        {
            _rig = CameraRig.Ensure();
            _rig.Camera.enabled = false; // Real projection/input code; this fixture performs no image rendering.
            _rig.Camera.aspect = 16f / 9f;
            var probes = new List<Probe>();
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidate));
            Require(_root != null, "candidate prefab staged");
            var venue = _root.GetComponent<VenueSceneRoot>();
            _rig.ConfigureVenue(venue);
            var focus = new GameObject("Read-only player approach").transform;
            focus.SetParent(_root.transform, false); _rig.Target = focus;
            Physics.SyncTransforms();
            var anchors = _root.GetComponentsInChildren<SpotAnchor>().Where(a => !string.IsNullOrEmpty(a.anim)).OrderBy(a => a.spotId).ToArray();
            Require(anchors.Length == 12, "all twelve claimable immutable anchors retained");
            foreach (var anchor in anchors)
            {
                Require(anchor.approach != null, "authored approach " + anchor.spotId);
                focus.position = anchor.approach.position; _rig.SnapToTarget(); Physics.SyncTransforms();
                var furniture = anchor.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Direction_", StringComparison.Ordinal)).ToArray();
                var chair = furniture.Single(t => t.name == "Direction_office_chair" || t.name == "Direction_home_chair_1");
                var table = furniture.Single(t => t.name == "Direction_home_desk" || t.name == "Direction_home_table_roundsmall");
                Require(chair.GetComponentsInChildren<MeshCollider>().Length > 0 && table.GetComponentsInChildren<MeshCollider>().Length > 0,
                    "actual mesh collider ownership " + anchor.spotId);
                foreach (string part in new[] { "back", "table" })
                {
                    var selectedFurniture = part == "back" ? chair : table;
                    var point = VisibleSurface(selectedFurniture, anchor, part, _rig.Camera);
                    Require(point.HasValue, "visible " + part + " source triangle from gameplay camera " + anchor.spotId);
                    var probe = new Probe { spot = anchor.spotId, part = part, world = point.Value, approach = focus.position };
                    probes.Add(probe);
                    Require(Tap(probe, "candidate", out var hit) == anchor.spotId, "WorldInput.Tap selects " + anchor.spotId + " via " + part);
                    if (part == "back") Require(Vector3.Distance(hit.point, anchor.transform.position) > .9f,
                        "back probe actually exceeds legacy three-dimensional radius " + anchor.spotId);
                }
                yield return null;
            }
            Object.Destroy(_root);
            while (_root != null) yield return null;
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Baseline));
            Require(_root != null, "original B3 preserved for negative control");
            venue = _root.GetComponent<VenueSceneRoot>(); _rig.ConfigureVenue(venue);
            focus = new GameObject("Read-only baseline approach").transform; focus.SetParent(_root.transform, false); _rig.Target = focus;
            Physics.SyncTransforms();
            int missedBacks = 0;
            foreach (var probe in probes)
            {
                focus.position = probe.approach; _rig.SnapToTarget(); Physics.SyncTransforms();
                string selected = Tap(probe, "original_b3", out _);
                if (probe.part == "back" && selected != probe.spot) missedBacks++;
                yield return null;
            }
            Require(missedBacks > 0, "original B3 negative control reproduces unselectable visible chair backs");
            Commands.Add("original_b3_back_misses=" + missedBacks + "/12");
            Object.Destroy(_root);while(_root!=null)yield return null;
            var walks=ActualWalking();while(walks.MoveNext())yield return walks.Current;
        }
        private static IEnumerator ActualWalking()
        {
            _root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidate));
            var venue=_root.GetComponent<VenueSceneRoot>();
            var data=JObject.Parse(File.ReadAllText("Assets/Resources/Contract/config_snapshot.json"));
            var registries=Registries.Parse(data["ids"],data["config"]);var spots=registries.SpotsFor("terminal_cafe");
            Require(spots!=null&&venue.navigation!=null,"final saved contract and NavMesh are available");venue.Init(spots);
            _navigation=NavMesh.AddNavMeshData(venue.navigation,_root.transform.position,_root.transform.rotation);
            Require(_navigation.valid,"actual final saved navigation is registered");
            _root.GetComponent<VenueContentLighting>()?.Apply();
            _rig.ConfigureVenue(venue);_rig.Camera.enabled=true;_rig.Camera.aspect=16f/9f;
            if(_rig.Camera.GetComponent<UniversalAdditionalCameraData>()==null)_rig.Camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            var look=registry.looks.Single(e=>e.key.Split('|').Contains("head_snow"));
            _view=AvatarFactory.Create("Actual final-room walking review",look.key.Split('|'),false,parent:_root.transform);
            Require(_view.Animator!=null,"actual Snow world Humanoid created");
            _view.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; // Diagnostic timing only, not a production culling change.
            _view.transform.position=venue.EntrancePosition;_player=_view.gameObject.AddComponent<LocalPlayerController>();
            typeof(LocalPlayerController).GetMethod("Init").Invoke(_player,new object[]{venue,null});
            _player.ReviewCommandObserved+=Observe;_rig.Target=_view.transform;
            int ready=Time.frameCount+3;while(Time.frameCount<ready)yield return null;
            var agent=_view.GetComponent<NavMeshAgent>();Require(agent.enabled&&agent.isOnNavMesh,"actual final-room local player on native nav");
            _rig.SnapToTarget();Physics.SyncTransforms();Capture("room-arrival",venue);
            var focus=new GameObject("Viewpoint search only").transform;focus.SetParent(_root.transform,false);
            foreach(var anchor in venue.Anchors.Where(a=>!string.IsNullOrEmpty(a.anim)).OrderBy(a=>a.spotId))
            {
                var chair=anchor.GetComponentsInChildren<Transform>().Single(t=>t.name=="Direction_office_chair"||t.name=="Direction_home_chair_1");
                Vector3? viewpoint=null;
                foreach(var offset in new[]{new Vector3(0,0,-1.8f),new Vector3(1,0,-1.5f),new Vector3(-1,0,-1.5f),new Vector3(1.8f,0,0)})
                {
                    if(!NavMesh.SamplePosition(anchor.approach.position+offset,out var hit,.35f,agent.areaMask)||!venue.IsWalkable(hit.position))continue;
                    var path=new NavMeshPath();if(!agent.CalculatePath(hit.position,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
                    focus.position=hit.position;_rig.Target=focus;_rig.SnapToTarget();Physics.SyncTransforms();
                    if(VisibleSurface(chair,anchor,"back",_rig.Camera).HasValue){viewpoint=hit.position;break;}
                }
                _rig.Target=_view.transform;_rig.SnapToTarget();
                Require(viewpoint.HasValue,"reachable visible interaction viewpoint "+anchor.spotId);
                Require(_player.MoveTo(viewpoint.Value),"actual MoveTo accepts viewpoint "+anchor.spotId);
                var before=WalkToArrival(viewpoint.Value,"viewpoint-"+anchor.spotId,-1,venue);while(before.MoveNext())yield return before.Current;
                _rig.SnapToTarget();Physics.SyncTransforms();
                var point=VisibleSurface(chair,anchor,"back",_rig.Camera);Require(point.HasValue,"chair back visible from actual standing player "+anchor.spotId);
                int sits=SitCount();bool accepted=false;
                var probe=new Probe{spot=anchor.spotId,part="back",world=point.Value,approach=_view.transform.position};
                Require(Tap(probe,"actual_walk",out _,a=>accepted=_player.GoSit(a.spotId))==anchor.spotId&&accepted,"actual camera tap routes to local approach "+anchor.spotId);
                var arrival=WalkToArrival(anchor.approach.position,"claim-"+anchor.spotId,sits+1,venue);while(arrival.MoveNext())yield return arrival.Current;
                Require(Commands.Last(c=>c.StartsWith("sit:",StringComparison.Ordinal))=="sit:"+venue.WireSpotId(anchor.spotId),"arrival retains exact wire intent "+anchor.spotId);
                Require(string.IsNullOrEmpty(_player.SeatedSpot)&&agent.enabled&&agent.isOnNavMesh,"null-channel arrival remains standing "+anchor.spotId);
                if(anchor.spotId=="table_1"||anchor.spotId=="terminal_1"||anchor.spotId=="terminal_8")Capture("room-"+anchor.spotId+"-approach",venue);
            }
            Require(_player.MoveTo(venue.EntrancePosition),"return approach accepted");
            var returning=WalkToArrival(venue.EntrancePosition,"return-entrance",-1,venue);while(returning.MoveNext())yield return returning.Current;
            Capture("room-return",venue);
            Require(SitCount()==12,"twelve real tap-walk arrivals emit exactly twelve seat intents without confirmation");
            _player.ReviewCommandObserved-=Observe;
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
                foreach(var collider in Physics.OverlapCapsule(p+Vector3.up*.4f,p+Vector3.up*1.35f,.28f,~0,QueryTriggerInteraction.Ignore))
                    if(!collider.transform.IsChildOf(_player.transform))blockers.Add(collider.name);
                bool hasDestination=(bool)typeof(LocalPlayerController).GetField("_hasDestination",Private).GetValue(_player);
                if(Time.frameCount>=startFrame+2&&!hasDestination&&!agent.pathPending&&FlatDistance(p,target)<.15f&&_player.ActualVelocity.magnitude<.2f)break;
                if(!midCaptured&&label=="viewpoint-terminal_1"&&distance>1f)
                { _rig.SnapToTarget();Capture("room-mid-walk",venue);midCaptured=true; }
                yield return null;
            }
            var end=_player.transform.position;bool arrived=FlatDistance(end,target)<.15f&&!agent.pathPending&&!(bool)typeof(LocalPlayerController).GetField("_hasDestination",Private).GetValue(_player);
            Walks.Add(new{label,start=new[]{start.x,start.y,start.z},target=new[]{target.x,target.y,target.z},end=new[]{end.x,end.y,end.z},arrived,
                elapsedFrames=Time.frameCount-startFrame,samples,travelledMetres=distance,maxActualSpeed=maxSpeed,proxyOverlapNames=blockers.ToArray(),expectedSitIntents=expectedSits,observedSitIntents=SitCount(),
                proxyScope="Radius0.28m capsule at rootY+0.4..1.35m; navigation body clearance, not full skinned animation contact."});
            Require(arrived,"real agent arrives "+label);Require(blockers.Count==0,"walking body proxy clears solids "+label);
            if(expectedSits>=0)Require(SitCount()==expectedSits,"exactly one arrival intent "+label);
        }
        private static void Capture(string label,VenueSceneRoot venue)
        {
            _rig.SnapToTarget();var camera=_rig.Camera;camera.aspect=16f/9f;
            string output=SessionState.GetString(Key+"output","/tmp/ct-cafe-final-room-native-20261006");Directory.CreateDirectory(output);
            string path=Path.Combine(output,label+".png");var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);rt.Create();var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};Require(RenderPipeline.SupportsRenderRequest(camera,request),"graphics capture available "+label);
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
                var maps=venue.GetComponent<VenueLightmaps>();
                Captures.Add(new{label,file=Path.GetFileName(path),sha256=Hash(path),nativeFrame=Time.frameCount,
                    player=new[]{_player.transform.position.x,_player.transform.position.y,_player.transform.position.z},camera=new[]{camera.transform.position.x,camera.transform.position.y,camera.transform.position.z},
                    pitch=_rig.pitch,yaw=_rig.yaw,distance=_rig.distance,fov=camera.fieldOfView,aspect=camera.aspect,
                    boundLightmapCount=maps?.colors?.Length??0,scope="Actual final-room Play camera and original materials; unbaked output is spatial evidence only. No UI, confirmed seated pose, animated contact or final lighting approval."});
            }
            finally{RenderTexture.active=previous;Object.Destroy(tex);rt.Release();Object.Destroy(rt);}
        }
        private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static Vector3? VisibleSurface(Transform furniture, SpotAnchor anchor, string part, Camera camera)
        {
            var points = new List<Vector3>();
            foreach (var collider in furniture.GetComponentsInChildren<MeshCollider>())
            {
                // Imported source meshes deliberately have Read/Write disabled in players.
                // Editor MeshUtility reads a snapshot without mutating their importer or source bytes.
                using var data = MeshUtility.AcquireReadOnlyMeshData(collider.sharedMesh);
                var mesh = data[0];
                using var vertexData = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
                mesh.GetVertices(vertexData);
                var vertices = vertexData.ToArray();
                var allTriangles = new List<int>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var descriptor = mesh.GetSubMesh(sub);
                    Require(descriptor.topology == MeshTopology.Triangles, "triangle source mesh topology");
                    using var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp);
                    mesh.GetIndices(indices, sub, true);
                    allTriangles.AddRange(indices.ToArray());
                }
                var triangles = allTriangles.ToArray();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = collider.transform.TransformPoint(vertices[triangles[i]]);
                    var b = collider.transform.TransformPoint(vertices[triangles[i + 1]]);
                    var c = collider.transform.TransformPoint(vertices[triangles[i + 2]]);
                    var point = (a + b + c) / 3f;
                    var n = Vector3.Cross(b-a,c-a).normalized;
                    float y = point.y - anchor.transform.position.y;
                    if (part == "back" ? y < .82f || Vector3.Distance(point,anchor.transform.position) <= .95f : y < .65f || y > .85f || Mathf.Abs(n.y) < .9f) continue;
                    var v = camera.WorldToViewportPoint(point);
                    if (v.z <= camera.nearClipPlane || v.x <= .01f || v.x >= .99f || v.y <= .01f || v.y >= .99f) continue;
                    points.Add(point);
                }
            }
            var ideal = anchor.transform.position + (part == "back" ? new Vector3(0,.95f,-.4f) : new Vector3(0,.75f,.8f));
            foreach (var point in points.OrderBy(p => (p - ideal).sqrMagnitude))
            {
                var screen = camera.WorldToScreenPoint(point); var ray = camera.ScreenPointToRay(screen);
                var first = Physics.RaycastAll(ray,200f,~0,QueryTriggerInteraction.Collide).Where(h => !h.collider.isTrigger).OrderBy(h=>h.distance).FirstOrDefault();
                if (first.collider != null && first.collider.GetComponentInParent<SpotAnchor>() == anchor &&
                    first.collider.transform.IsChildOf(furniture) && Vector3.Distance(first.point,point) < .02f) return point;
            }
            return null;
        }
        private static string Tap(Probe probe, string variant, out RaycastHit first, Action<SpotAnchor> selectedAction = null)
        {
            var screen = _rig.Camera.WorldToScreenPoint(probe.world);
            var ray = _rig.Camera.ScreenPointToRay(screen);
            first = Physics.RaycastAll(ray,200f,~0,QueryTriggerInteraction.Collide).Where(h=>!h.collider.isTrigger).OrderBy(h=>h.distance).FirstOrDefault();
            string selected = null; int count = 0;
            void Pick(SpotAnchor spot) { selected = spot.spotId; count++; selectedAction?.Invoke(spot); }
            WorldInput.SpotTapped += Pick;
            try { WorldInput.Tap(new Vector2(screen.x,screen.y)); }
            finally { WorldInput.SpotTapped -= Pick; }
            Require(count <= 1, "one-or-zero selection event per actual tap");
            Rays.Add(new { variant, expected=probe.spot, part=probe.part, selected, eventCount=count,
                world=new[]{probe.world.x,probe.world.y,probe.world.z}, screen=new[]{screen.x,screen.y},
                rayHit=first.collider != null ? first.collider.name : null,
                hitPoint=new[]{first.point.x,first.point.y,first.point.z},
                cameraPosition=new[]{_rig.transform.position.x,_rig.transform.position.y,_rig.transform.position.z},
                pitch=_rig.pitch,yaw=_rig.yaw,distance=_rig.distance,fov=_rig.Camera.fieldOfView,aspect=_rig.Camera.aspect });
            return selected;
        }
        private static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + check);
            Checks.Add(check); Debug.Log("CTLIFE [cafe-final-room] PASS " + check);
        }
        private static void Stop(Exception failure)
        {
            if (Phase == "stopping") return;
            if(_player!=null)_player.ReviewCommandObserved-=Observe;
            if(_navigation.valid)_navigation.Remove();
            if(_root!=null)_root.GetComponent<VenueContentLighting>()?.Restore();
            if (_root != null) Object.Destroy(_root);
            if (_rig != null) Object.Destroy(_rig.gameObject);
            string output = SessionState.GetString(Key + "output", "/tmp/ct-cafe-final-room-native-20261006");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "receipt.json"), JsonConvert.SerializeObject(new
            {
                status = failure == null ? "final_room_camera_rays_and_actual_walking_passed_connected_seating_unverified" : "failed",
                candidatePrefab = Candidate, candidatePrefabSha256 = SessionState.GetString(Key+"candidateHash",""),
                checks = Checks, diagnostics = Commands, rays = Rays, walks = Walks, captures = Captures, observedMoveIntents = _moveIntents, networkChannel = "none; actual public GoSit through WorldInput event, no server confirmations",
                limitations = "Actual final CameraRig16:9, Physics, WorldInput.Tap and LocalPlayer walking/arrival on saved NavMesh. Null-channel intents are not network calls or ownership. No UI touch, server seat/stand, motion bridge, full skinned contact, final unbaked lighting or phone acceptance. Moving seated exit belongs to the separate motion fixture.",
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
            string output = SessionState.GetString(Key + "output", "/tmp/ct-cafe-final-room-native-20261006");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "protected-drift.json"), JsonConvert.SerializeObject(new { count = before.Count, drift }, Formatting.Indented));
            if (drift.Length != 0) SessionState.SetInt(Key + "result", 1);
            int result = SessionState.GetInt(Key + "result", 1);
            SessionState.SetBool(Key + "active", false);
            Debug.Log("CTLIFE [cafe-final-room] END exit=" + result + " protectedDrift=" + drift.Length);
            if (SessionState.GetBool(Key + "exit", false)) EditorApplication.Exit(result);
        }
        private static Dictionary<string, string> ProtectedHashes()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var files = new[] { "ProjectSettings", "Packages", "Assets/Scripts/World", "Assets/Resources", "Assets/Art/Candidates/TerminalCafe20261006B3Social", "Assets/Art/Candidates/TerminalCafe20261006B3Hits", "Assets/Art/Candidates/TerminalCafe20261006Final", "Assets/Art/characters/authored" }
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
