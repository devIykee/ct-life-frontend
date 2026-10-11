using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>One shared existing contact assembly for thirty Studio seats. Never registers or assigns a motion profile.</summary>
    public static class SpacesStudioSharedChairCandidate
    {
        public const string Output="Assets/Art/Candidates/SpacesStudio20261007F";
        public const string PrefabPath=Output+"/SpacesStudioF.prefab";
        private const string Baseline="Assets/Art/Candidates/SpacesStudio20261006E2";
        private const string Canonical=Baseline+"/SpacesStudioE2.prefab";
        private const string BaselineHash="860dfe5fedec81123427f9d057faae4ea03ab010a71711836be01f8115695a5a";
        private const string Cafe="Assets/Art/Candidates/TerminalCafe20261006Final/TerminalCafeFinal.prefab";
        private const string CafeHash="a7be079bdad8883b0fbeff0dada062d37387f71f3c4b5d4343e939478bb1fa32";
        private const string Snapshot="Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest="Assets/Art/environment/manifest.json";
        private static ProtectedFiles _protection;
        private static readonly JArray ChairRows=new JArray();
        private static readonly Dictionary<Mesh,Mesh> FinishMeshes=new Dictionary<Mesh,Mesh>();
        private static Material _audience,_speaker;
        public static void BuildCandidate()
        {
            if(Directory.Exists(Output))throw new InvalidOperationException("Preserve existing F candidate; use a separately reviewed revision.");
            Protected("stage",Stage);
        }
        public static void BakeCandidate()=>Protected("bake",()=>
        {
            RequireSources();var stage=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));
            if((string)stage["status"]!="staged_unbaked_not_published"||(int)stage["shared_fitted_chairs"]!=30)throw new InvalidDataException("F stage receipt is not complete.");
            ChairRows.Clear();foreach(var row in (JArray)stage["chairs"])ChairRows.Add(row.DeepClone());
            VenueLightingBuilder.BakeVenue(PrefabPath,Output+"/Lighting");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);RequireLayout(AssetDatabase.LoadAssetAtPath<GameObject>(Canonical),prefab);
            var maps=prefab.GetComponent<VenueLightmaps>();if(maps==null||maps.colors.Length==0||maps.bindings.Length==0)throw new InvalidDataException("F bake has no bound maps.");
            File.WriteAllText(Output+"/BakeReceipt.json",new JObject{["status"]="baked_not_published_requires_visual_and_device_review",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["lightmaps"]=maps.colors.Length,["bound_renderers"]=maps.bindings.Length,["limitations"]="Shared furniture art only; no Studio profile, authority, voice, seated exit or registry assignment."}.ToString());
        });
        private static void RequireSources()
        {
            if(Hash(Canonical)!=BaselineHash||Hash(Cafe)!=CafeHash||Hash(Baseline+"/BakeReceipt.json")!="c0b60527e739a28d1e5f93ebe40b6e5df114d9902722085c8a5fc4c349e377e6")throw new InvalidDataException("Reviewed Studio/Café source changed.");
        }
        private static void Stage()
        {
            RequireSources();foreach(string folder in new[]{"Meshes","Materials","GeometryEvidence"})Directory.CreateDirectory(Output+"/"+folder);AssetDatabase.Refresh();ChairRows.Clear();FinishMeshes.Clear();
            var snapshot=JObject.Parse(File.ReadAllText(Snapshot));var spots=Registries.Parse(snapshot["ids"],snapshot["config"]).SpotsFor("spaces_studio");
            if((string)JObject.Parse(File.ReadAllText(Baseline+"/StageReceipt.json"))["snapshot_sha256"]!=Hash(Snapshot))throw new InvalidDataException("Studio contract changed.");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var original=AssetDatabase.LoadAssetAtPath<GameObject>(Canonical);var root=Object.Instantiate(original);root.name="Spaces Studio / shared fitted chairs / candidate";UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                var maps=root.GetComponent<VenueLightmaps>();if(maps!=null)Object.DestroyImmediate(maps);
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)){renderer.lightmapIndex=-1;renderer.lightmapScaleOffset=new Vector4(1,1,0,0);}
                var sourceRoot=AssetDatabase.LoadAssetAtPath<GameObject>(Cafe);var sourceAnchor=sourceRoot.GetComponentsInChildren<SpotAnchor>(true).Single(a=>a.spotId=="table_1");
                var sourceChair=sourceAnchor.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Direction_home_chair_1");
                if(sourceAnchor.transform.lossyScale!=Vector3.one||root.transform.lossyScale!=Vector3.one)throw new InvalidDataException("Reviewed chair/root unit transform changed.");
                _audience=Finish("AudienceDeepTeal",new Color(.20f,.32f,.36f));_speaker=Finish("SpeakerWarmNeutral",new Color(.72f,.66f,.55f));
                var exclusions=(JArray)JObject.Parse(File.ReadAllText(Baseline+"/NavigationDiagnostics.json"))["nav_exclusion_bounds"];
                var solids=exclusions.Select(row=>BoundsFrom(ReadV(row["min"]),ReadV(row["max"]))).ToList();
                var anchors=root.GetComponentsInChildren<SpotAnchor>(true).Where(a=>a.anim=="act_sit").OrderBy(a=>a.spotId).ToArray();
                if(anchors.Length!=30)throw new InvalidDataException("Expected exactly thirty Studio canonical social seats.");
                foreach(var anchor in anchors)
                {
                    var old=anchor.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Direction_lounge_chair");
                    var oldBounds=CollisionBounds(old.gameObject);var matches=solids.Select((b,i)=>new{b,i}).Where(v=>CloseXZ(v.b,oldBounds)).ToArray();
                    if(matches.Length!=1)throw new InvalidDataException("Old chair must match exactly one saved bake exclusion: "+anchor.spotId);
                    Object.DestroyImmediate(old.gameObject);
                    var chair=Object.Instantiate(sourceChair.gameObject,anchor.transform,false);chair.name="Direction_home_chair_1";
                    chair.transform.localPosition=sourceAnchor.transform.InverseTransformPoint(sourceChair.position);chair.transform.localRotation=Quaternion.Inverse(sourceAnchor.transform.rotation)*sourceChair.rotation;chair.transform.localScale=sourceChair.lossyScale;
                    ApplyFinish(chair,anchor,anchor.kind=="seat"?_audience:_speaker);
                    float error=RequireChairIdentity(sourceChair,sourceAnchor.transform,chair.transform,anchor.transform);
                    var support=RequireSupport(chair,anchor);
                    var bounds=CollisionBounds(chair);solids[matches[0].i]=new Bounds(new Vector3(bounds.center.x,1.2f,bounds.center.z),new Vector3(bounds.size.x,2.4f,bounds.size.z));
                    ChairRows.Add(new JObject{["spot"]=anchor.spotId,["kind"]=anchor.kind,["animation"]=anchor.anim,["world_anchor"]=V(anchor.transform.position),["yaw_radians"]=anchor.RotY,["source"]=Cafe+"#table_1/Direction_home_chair_1",["max_anchor_local_vertex_error_m"]=error,["native_support_rays"]=support,["min"]=V(bounds.min),["max"]=V(bounds.max),["unchanged_approach"]=V(anchor.approach.position)});
                    if(anchor.spotId=="seat_1"||anchor.spotId=="stage_1")Export(chair,anchor,anchor.spotId=="seat_1"?"audience-chair":"speaker-chair");
                }
                RequireLayout(original,root);
                var venue=root.GetComponent<VenueSceneRoot>();venue.Init(spots);venue.navigation=BuildNavigation(spots,solids);
                var oldPaths=Paths(AssetDatabase.LoadAssetAtPath<NavMeshData>(Baseline+"/Meshes/Navigation.asset"),venue,spots);var paths=Paths(venue.navigation,venue,spots);
                File.WriteAllText(Output+"/NavigationDiagnostics.json",new JObject{["status"]="scoped_chair_change_same34_targets",["baseline"]=oldPaths,["candidate"]=paths,["candidate_bake_radius_m"]=.3f,["runtime_agent_radius_m"]=.3f,["nav_exclusion_bounds"]=new JArray(solids.Select(b=>new JObject{["min"]=V(b.min),["max"]=V(b.max)}))}.ToString());
                if(paths.Count!=34||paths.Any(row=>(string)row["path"]!="PathComplete"))throw new InvalidDataException("Shared-chair candidate must retain all34 complete approach/anchor paths.");
                int tagged=root.GetComponentsInChildren<SpotAnchor>(true).Count(a=>!string.IsNullOrEmpty(a.anim)&&a.GetComponentsInChildren<MeshCollider>(true).Length>0);if(tagged!=31)throw new InvalidDataException("Shared chair targets plus host must remain31.");
                if(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null)throw new IOException("Could not save isolated Studio F.");SaveOwned();
                File.WriteAllText(Output+"/StageReceipt.json",new JObject{["status"]="staged_unbaked_not_published",["prefab"]=PrefabPath,["snapshot_sha256"]=Hash(Snapshot),["positioned_anchors"]=34,["authored_approaches"]=31,["tagged_geometry_spots"]=31,["shared_fitted_chairs"]=30,["chairs"]=ChairRows.DeepClone(),["navigation"]=paths,["baseline_prefab_sha256"]=BaselineHash,["cafe_prototype_sha256"]=CafeHash,["static_mesh_renderers"]=root.GetComponentsInChildren<MeshRenderer>(true).Length,["mesh_colliders"]=root.GetComponentsInChildren<MeshCollider>(true).Length,["limitations"]="Only30chair render/collision surfaces and candidate NavMesh change. Same shared Café physical contact assembly, distinct material treatment. No fitted Studio action/profile, exit, roles, voice or registry is enabled."}.ToString());
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);FinishMeshes.Clear();}
        }
        private static Material Finish(string name,Color color)
        {
            var mat=new Material(Shader.Find("Universal Render Pipeline/Simple Lit")){name=name,enableInstancing=true};mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",.02f);
            string texture=Baseline+"/Textures/OriginalQuietWeave.png";if(Hash(texture)!="8abf62a46d2db993e39188dafb451887c82a7ac31bb8ed2b77d4c278bc080935")throw new InvalidDataException("Existing original weave changed.");mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));AssetDatabase.CreateAsset(mat,Output+"/Materials/"+name+".mat");return mat;
        }
        private static void ApplyFinish(GameObject chair,SpotAnchor anchor,Material finish)
        {
            foreach(var filter in chair.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();var source=filter.sharedMesh;var oldMaterials=renderer.sharedMaterials;
                if(!FinishMeshes.TryGetValue(source,out var mesh))
                {
                    mesh=Object.Instantiate(source);var originalSubmeshes=source.subMeshCount;var cloth=new List<int>();var vertices=source.vertices.Select(v=>anchor.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
                    mesh.subMeshCount=originalSubmeshes+1;
                    for(int sub=0;sub<originalSubmeshes;sub++)
                    {
                        var keep=new List<int>();var ids=source.GetTriangles(sub);
                        for(int i=0;i<ids.Length;i+=3){var target=vertices[ids[i]].y>.40f&&vertices[ids[i+1]].y>.40f&&vertices[ids[i+2]].y>.40f?cloth:keep;target.Add(ids[i]);target.Add(ids[i+1]);target.Add(ids[i+2]);}
                        mesh.SetTriangles(keep,sub);
                    }
                    if(cloth.Count==0)throw new InvalidDataException("Shared chair has no native upper finish surfaces.");mesh.SetTriangles(cloth,originalSubmeshes);AssetDatabase.CreateAsset(mesh,Output+"/Meshes/ChairFinish"+FinishMeshes.Count+".asset");FinishMeshes.Add(source,mesh);
                }
                filter.sharedMesh=mesh;renderer.sharedMaterials=oldMaterials.Concat(new[]{finish}).ToArray();renderer.lightmapIndex=-1;renderer.lightmapScaleOffset=new Vector4(1,1,0,0);
                // The original source collision shape remains exactly the same surface after material regrouping.
            }
        }
        private static float RequireChairIdentity(Transform source,Transform sourceAnchor,Transform copy,Transform targetAnchor)
        {
            var before=source.GetComponentsInChildren<MeshFilter>(true);var after=copy.GetComponentsInChildren<MeshFilter>(true);if(before.Length!=after.Length)throw new InvalidDataException("Shared chair mesh count changed.");float max=0;
            for(int n=0;n<before.Length;n++)
            {
                var a=before[n].sharedMesh;var b=after[n].sharedMesh;if(a.vertexCount!=b.vertexCount||!TriangleKeys(a).SequenceEqual(TriangleKeys(b)))throw new InvalidDataException("Shared chair topology changed.");var av=a.vertices;var bv=b.vertices;
                for(int i=0;i<av.Length;i++)max=Mathf.Max(max,Vector3.Distance(sourceAnchor.InverseTransformPoint(before[n].transform.TransformPoint(av[i])),targetAnchor.InverseTransformPoint(after[n].transform.TransformPoint(bv[i]))));
            }
            if(max>.00001f)throw new InvalidDataException("Shared chair differs from fitted prototype by "+max+"m.");
            var ac=source.GetComponentsInChildren<MeshCollider>(true);var bc=copy.GetComponentsInChildren<MeshCollider>(true);if(ac.Length!=bc.Length||ac.Length==0)throw new InvalidDataException("Shared truthful chair collision changed.");
            for(int i=0;i<ac.Length;i++)if(ac[i].sharedMesh!=bc[i].sharedMesh||bc[i].isTrigger||bc[i].convex)throw new InvalidDataException("Shared source collider identity changed.");return max;
        }
        private static JArray RequireSupport(GameObject chair,SpotAnchor anchor)
        {
            var rows=new JArray();foreach(float x in new[]{-.12f,0f,.12f})foreach(float z in new[]{-.36f,-.30f,-.24f})
            {
                float highest=float.NegativeInfinity;
                foreach(var collider in chair.GetComponentsInChildren<MeshCollider>(true))
                {
                    var vertices=collider.sharedMesh.vertices.Select(v=>anchor.transform.InverseTransformPoint(collider.transform.TransformPoint(v))).ToArray();var ids=collider.sharedMesh.triangles;
                    for(int i=0;i<ids.Length;i+=3)
                    {
                        var a=vertices[ids[i]];var b=vertices[ids[i+1]];var c=vertices[ids[i+2]];var normal=Vector3.Cross(b-a,c-a).normalized;if(Mathf.Abs(normal.y)<.95f)continue;
                        float denominator=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(denominator)<1e-10f)continue;
                        float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/denominator,v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/denominator,w=1-u-v;
                        if(u<-.00001f||v<-.00001f||w<-.00001f)continue;float y=u*a.y+v*b.y+w*c.y;if(y>.40f&&y<.50f)highest=Mathf.Max(highest,y);
                    }
                }
                if(float.IsInfinity(highest)||Mathf.Abs(highest-.45f)>.00002f)throw new InvalidDataException("Shared native support differs at "+anchor.spotId+" / "+x+","+z+": "+highest);
                rows.Add(new JObject{["x"]=x,["z"]=z,["support_y"]=highest});
            }return rows;
        }
        private static string[] TriangleKeys(Mesh m){var t=m.triangles;var rows=new List<string>();for(int i=0;i<t.Length;i+=3)rows.Add(t[i]+","+t[i+1]+","+t[i+2]);rows.Sort(StringComparer.Ordinal);return rows.ToArray();}
        private static Bounds CollisionBounds(GameObject go){var v=go.GetComponentsInChildren<MeshCollider>(true).SelectMany(c=>c.sharedMesh.vertices.Select(c.transform.TransformPoint)).ToArray();if(v.Length==0)throw new InvalidDataException("Source collision missing.");var b=new Bounds(v[0],Vector3.zero);foreach(var p in v.Skip(1))b.Encapsulate(p);return b;}
        private static bool CloseXZ(Bounds a,Bounds b)=>Mathf.Abs(a.min.x-b.min.x)<.00002f&&Mathf.Abs(a.max.x-b.max.x)<.00002f&&Mathf.Abs(a.min.z-b.min.z)<.00002f&&Mathf.Abs(a.max.z-b.max.z)<.00002f;
        private static Bounds BoundsFrom(Vector3 lo,Vector3 hi)=>new Bounds((lo+hi)*.5f,hi-lo);
        private static Vector3 ReadV(JToken t)=>new Vector3((float)t[0],(float)t[1],(float)t[2]);
        private static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static bool ChairPart(Transform t)
        {
            for(var p=t;p!=null;p=p.parent)if((p.name=="Direction_lounge_chair"||p.name=="Direction_home_chair_1")&&p.GetComponentInParent<SpotAnchor>()?.anim=="act_sit")return true;return false;
        }
        private static string VectorKey(Vector3 p)=>p.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        private static string[] NonChairRender(GameObject root)
        {
            var rows=new List<string>();foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>!ChairPart(f.transform))){var v=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var t=f.sharedMesh.triangles;for(int i=0;i<t.Length;i+=3)rows.Add(VectorKey(v[t[i]])+";"+VectorKey(v[t[i+1]])+";"+VectorKey(v[t[i+2]]));}rows.Sort(StringComparer.Ordinal);return rows.ToArray();
        }
        private static string[] NonChairColliders(GameObject root)
        {
            var rows=new List<string>();foreach(var c in root.GetComponentsInChildren<Collider>(true).Where(c=>!ChairPart(c.transform)))
            {
                string prefix=c.GetType().Name+"|"+c.enabled+"|"+c.isTrigger+"|"+c.gameObject.layer+"|";
                if(c is MeshCollider m){var v=m.sharedMesh.vertices.Select(m.transform.TransformPoint).ToArray();var t=m.sharedMesh.triangles;for(int i=0;i<t.Length;i+=3)rows.Add(prefix+VectorKey(v[t[i]])+";"+VectorKey(v[t[i+1]])+";"+VectorKey(v[t[i+2]]));}
                else if(c is BoxCollider b){foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})rows.Add(prefix+VectorKey(b.transform.TransformPoint(b.center+Vector3.Scale(b.size*.5f,new Vector3(x,y,z)))));}
                else throw new InvalidDataException("Unreviewed collider kind.");
            }rows.Sort(StringComparer.Ordinal);return rows.ToArray();
        }
        private static void RequireLayout(GameObject original,GameObject candidate)
        {
            if(!NonChairRender(original).SequenceEqual(NonChairRender(candidate))||!NonChairColliders(original).SequenceEqual(NonChairColliders(candidate)))throw new InvalidDataException("A non-chair rendered or collided surface changed.");
            var old=original.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a=>a.spotId);var current=candidate.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a=>a.spotId);if(old.Count!=34||current.Count!=34)throw new InvalidDataException("Anchor count changed.");
            foreach(var row in old){var a=row.Value;var b=current[row.Key];if(a.kind!=b.kind||a.anim!=b.anim||a.transform.position!=b.transform.position||a.transform.rotation!=b.transform.rotation||(a.approach==null)!=(b.approach==null)||(a.approach!=null&&a.approach.position!=b.approach.position))throw new InvalidDataException("Anchor/approach changed: "+row.Key);}
            var ca=original.GetComponent<VenueSceneRoot>();var cb=candidate.GetComponent<VenueSceneRoot>();if(ca.cameraBounds!=cb.cameraBounds||ca.cameraDistance!=cb.cameraDistance||ca.cameraPitch!=cb.cameraPitch||ca.cameraYaw!=cb.cameraYaw||ca.cameraFieldOfView!=cb.cameraFieldOfView||ca.cameraTargetViewportY!=cb.cameraTargetViewportY||ca.cameraFocusHeight!=cb.cameraFocusHeight||ca.cameraLookAhead!=cb.cameraLookAhead||ca.venueId!=cb.venueId||ca.districtId!=cb.districtId)throw new InvalidDataException("Room identity/camera changed.");
            File.WriteAllText(Output+"/LayoutEvidence.json",new JObject{["nonchair_render_and_collision_surfaces_unchanged"]=true,["immutable_anchors"]=34,["approaches"]=31,["changed_furniture"]=30,["chairs"]=ChairRows.DeepClone(),["profile_assigned"]=false}.ToString());
        }
        private static NavMeshData BuildNavigation(VenueSpots spots,List<Bounds> solids)
        {
            var sources=new List<NavMeshBuildSource>();foreach(var r in spots.walkable)sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Box,area=0,transform=Matrix4x4.TRS(new Vector3((r.x0+r.x1)*.5f,-.1f,(r.z0+r.z1)*.5f),Quaternion.identity,Vector3.one),size=new Vector3(Mathf.Abs(r.x1-r.x0),.2f,Mathf.Abs(r.z1-r.z0))});
            foreach(var b in solids)sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,area=1,transform=Matrix4x4.TRS(b.center,Quaternion.identity,Vector3.one),size=b.size});var settings=NavMesh.GetSettingsByID(0);settings.agentRadius=.3f;
            var nav=NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(0,1,0),new Vector3(28,8,24)),Vector3.zero,Quaternion.identity);if(nav==null)throw new InvalidDataException("Shared chair navigation build failed.");AssetDatabase.CreateAsset(nav,Output+"/Meshes/Navigation.asset");return nav;
        }
        private static JArray Paths(NavMeshData nav,VenueSceneRoot venue,VenueSpots spots)
        {
            var offset=new Vector3(10000,0,10000);var instance=NavMesh.AddNavMeshData(nav,offset,Quaternion.identity);var rows=new JArray();
            try
            {
                if(!NavMesh.SamplePosition(venue.EntrancePosition+offset,out var start,.75f,NavMesh.AllAreas))throw new InvalidDataException("Entrance unavailable on sampled navigation.");
                foreach(var anchor in venue.Anchors.OrderBy(a=>a.spotId)){var p=(anchor.approach!=null?anchor.approach.position:anchor.transform.position)+offset;bool sampled=NavMesh.SamplePosition(p,out var hit,anchor.approach!=null?.15f:.75f,NavMesh.AllAreas);var path=new NavMeshPath();bool calculated=sampled&&NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path);rows.Add(new JObject{["spot"]=anchor.spotId,["sampled"]=sampled,["target"]=V(p-offset),["sample_distance_m"]=sampled?Vector3.Distance(p,hit.position):-1,["path"]=calculated?path.status.ToString():"NoPath",["corners"]=new JArray(path.corners.Select(v=>V(v-offset)))});}
            }
            finally{if(instance.valid)instance.Remove();}return rows;
        }
        private static void Export(GameObject chair,SpotAnchor anchor,string name)
        {
            using var w=new StreamWriter(Output+"/GeometryEvidence/"+name+".obj");int offset=0;foreach(var c in chair.GetComponentsInChildren<MeshCollider>(true)){var v=c.sharedMesh.vertices.Select(p=>anchor.transform.InverseTransformPoint(c.transform.TransformPoint(p))).ToArray();foreach(var p in v)w.WriteLine("v "+p.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" "+p.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" "+p.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture));var t=c.sharedMesh.triangles;for(int i=0;i<t.Length;i+=3)w.WriteLine($"f {t[i]+1+offset} {t[i+1]+1+offset} {t[i+2]+1+offset}");offset+=v.Length;}
        }
        private static void SaveOwned(){foreach(string guid in AssetDatabase.FindAssets("",new[]{Output})){var path=AssetDatabase.GUIDToAssetPath(guid);if(path.EndsWith(".mat")||path.EndsWith(".asset"))AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));}}
        private static void Protected(string phase,Action action)
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)throw new InvalidOperationException("Use coordinator-owned idle batch.");if(_protection!=null)throw new InvalidOperationException("Transaction active.");_protection=new ProtectedFiles(phase);
            try{action();_protection.Verify();_protection.Record("passed_protected_physical_bytes_unchanged",null);}
            catch(Exception e){_protection.Restore();_protection.Record("failed_original_physical_bytes_restored",e.ToString());Directory.CreateDirectory(Output);File.WriteAllText(Output+"/"+(phase=="stage"?"StageReceipt.json":"BakeReceipt.json"),new JObject{["status"]="failed",["error"]=e.ToString()}.ToString());throw;}
            finally{_protection=null;}
        }
        private sealed class ProtectedFiles
        {
            private readonly Dictionary<string,string> _hashes=new Dictionary<string,string>();
            private readonly Dictionary<string,string> _logical=new Dictionary<string,string>();
            private readonly Dictionary<string,string> _copies=new Dictionary<string,string>();
            private readonly string _backup;
            public ProtectedFiles(string phase)
            {
                _backup=Path.Combine(Path.GetTempPath(),"ctlife-studio-shared-chair-"+phase+"-"+Guid.NewGuid().ToString("N"));
                var models=JObject.Parse(File.ReadAllText(Manifest)).Properties().Select(p=>"Assets/Art/"+(string)p.Value["fbx"]).ToArray();
                var logical=AssetDatabase.GetDependencies(models,true).Concat(AssetDatabase.GetDependencies(Canonical,true))
                    .Concat(new[]{Canonical,Snapshot,Manifest,"Assets/Resources/World/VenueContentRegistry.asset","Packages/manifest.json","Packages/packages-lock.json"})
                    .Concat(Directory.GetFiles("Assets/Art/Candidates","*",SearchOption.AllDirectories).Where(p=>!p.StartsWith(Output+"/",StringComparison.Ordinal)))
                    .Concat(Directory.GetFiles("ProjectSettings","*",SearchOption.AllDirectories)).Distinct().ToArray();
                foreach(string assetPath in logical)
                {
                    string physical=Physical(assetPath);if(!File.Exists(physical))continue;
                    Capture(assetPath,physical);if(File.Exists(physical+".meta"))Capture(assetPath+".meta",physical+".meta");
                }
                Record("captured_physical_bytes_without_loading_assets",null);
            }
            private static string Physical(string assetPath)
            {
                if(assetPath.StartsWith("Packages/com.",StringComparison.Ordinal))
                {
                    var info=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                    if(info==null||string.IsNullOrEmpty(info.resolvedPath)||!assetPath.StartsWith(info.assetPath+"/",StringComparison.Ordinal))throw new InvalidDataException("Cannot resolve installed package dependency "+assetPath);
                    return Path.GetFullPath(Path.Combine(info.resolvedPath,assetPath.Substring(info.assetPath.Length+1)));
                }
                return Path.GetFullPath(assetPath);
            }
            private void Capture(string logical,string physical)
            {
                if(_hashes.ContainsKey(physical))return;
                string copy=Path.Combine(_backup,"before",_hashes.Count.ToString("D6"));Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(physical,copy);
                _hashes[physical]=Hash(physical);_logical[physical]=logical;_copies[physical]=copy;
                if(Hash(copy)!=_hashes[physical])throw new IOException("Protection copy differs: "+logical);
            }
            public void ClearDirty(){ /* Intentionally no-op: never load/clear unrelated assets or scenes. Saves are candidate-only. */ }
            public void Verify()
            {
                var changed=_hashes.Where(p=>!File.Exists(p.Key)||Hash(p.Key)!=p.Value).Select(p=>p.Key).ToArray();
                if(changed.Length!=0)throw new InvalidDataException("Protected physical bytes changed: "+string.Join(", ",changed));
            }
            public void Restore()
            {
                foreach(var pair in _hashes.Where(p=>!File.Exists(p.Key)||Hash(p.Key)!=p.Value))
                {
                    // Package caches are read-only evidence: report any change rather than recreate aliases or overwrite a cache.
                    if(_logical[pair.Key].StartsWith("Packages/com.",StringComparison.Ordinal))throw new InvalidDataException("Installed package dependency changed; no automatic cache restore: "+pair.Key);
                    string before=_copies[pair.Key];if(Hash(before)!=pair.Value)throw new IOException("Protection snapshot corrupt.");
                    if(File.Exists(pair.Key)){string failed=Path.Combine(_backup,"after-failure",Path.GetFileName(before));Directory.CreateDirectory(Path.GetDirectoryName(failed));File.Copy(pair.Key,failed);}
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.Key));File.Copy(before,pair.Key,true);
                }
                Verify();
            }
            public void Record(string status,string error)
            {
                Directory.CreateDirectory(_backup);File.WriteAllText(Path.Combine(_backup,"transaction.json"),new JObject{["status"]=status,["error"]=error,["post_editor_exit_hash_check_required"]=true,
                    ["files"]=new JArray(_hashes.Select(p=>new JObject{["path"]=p.Key,["logical_path"]=_logical[p.Key],["sha256"]=p.Value}))}.ToString());
                Debug.Log("CTLIFE [cafe-art] physical protection receipt: "+Path.Combine(_backup,"transaction.json"));
            }
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
