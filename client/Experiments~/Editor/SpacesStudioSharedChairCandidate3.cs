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
    /// <summary>Navigation-only clone of F2; same physical chairs, rendering, anchors and baked lighting.</summary>
    public static class SpacesStudioSharedChairCandidate3
    {
        public const string Output="Assets/Art/Candidates/SpacesStudio20261007F3";
        public const string PrefabPath=Output+"/SpacesStudioF3.prefab";
        private const string Baseline="Assets/Art/Candidates/SpacesStudio20261007F2";
        private const string Canonical=Baseline+"/SpacesStudioF2.prefab";
        private const string BaselineHash="b51823fb2c9f05ad39cb00b8f1a8906524d73898c88967e44a46b0dad7149f75";
        private const string Snapshot="Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest="Assets/Art/environment/manifest.json";
        private static ProtectedFiles _protection;
        private const float Padding=.06f;
        public static void BuildCandidate()
        {
            if(Directory.Exists(Output))throw new InvalidOperationException("Preserve existing F3 output; select a new reviewed revision.");
            Protected("navigation-stage",Stage);
        }
        private static void Stage()
        {
            if(Hash(Canonical)!=BaselineHash||Hash(Baseline+"/BakeReceipt.json")!="d02dede7cceb5ae15801fc38707fcef717ef19caf68f4161be772cc1fae69f4d"||Hash(Baseline+"/StageReceipt.json")!="b9cffae1879c0b28cd67964bedaabbf2fa41fb75a1f0ae7efc19eaac49542b7f"||Hash(Baseline+"/NavigationDiagnostics.json")!="f7e0d9e366defcf5efdbef2eebcaff250e007882f8d279ed9fa2890bb3570fb1")throw new InvalidDataException("Exact baked F2 baseline changed.");
            Directory.CreateDirectory(Output+"/Meshes");AssetDatabase.Refresh();
            var stage=JObject.Parse(File.ReadAllText(Baseline+"/StageReceipt.json"));if((string)stage["snapshot_sha256"]!=Hash(Snapshot)||(int)stage["shared_fitted_chairs"]!=30)throw new InvalidDataException("F2 stage/contract differs.");
            var snapshot=JObject.Parse(File.ReadAllText(Snapshot));var spots=Registries.Parse(snapshot["ids"],snapshot["config"]).SpotsFor("spaces_studio");
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(Canonical);var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var root=Object.Instantiate(original);root.name="Spaces Studio / physical chair clearance navigation / candidate";UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                var bounds=((JArray)JObject.Parse(File.ReadAllText(Baseline+"/NavigationDiagnostics.json"))["nav_exclusion_bounds"]).Select(v=>BoundsFrom(ReadV(v["min"]),ReadV(v["max"]))).ToList();
                var changes=new JArray();var changedIndices=new HashSet<int>();
                foreach(var row in (JArray)stage["chairs"])
                {
                    var actual=BoundsFrom(ReadV(row["min"]),ReadV(row["max"]));var matching=bounds.Select((b,i)=>new{b,i}).Where(v=>CloseXZ(v.b,actual)).ToArray();
                    if(matching.Length!=1||!changedIndices.Add(matching[0].i))throw new InvalidDataException("Physical chair must identify one distinct F2 exclusion: "+row["spot"]);
                    var before=matching[0].b;var after=before;after.Expand(new Vector3(Padding*2,0,Padding*2));bounds[matching[0].i]=after;
                    changes.Add(new JObject{["spot"]=row["spot"].DeepClone(),["padding_each_horizontal_side_m"]=Padding,["before_min"]=V(before.min),["before_max"]=V(before.max),["after_min"]=V(after.min),["after_max"]=V(after.max)});
                }
                if(changes.Count!=30||bounds.Count!=47)throw new InvalidDataException("Expected only thirty chair exclusions among the existing47 bounds.");
                var venue=root.GetComponent<VenueSceneRoot>();venue.Init(spots);var oldPaths=Paths(venue.navigation,venue,spots);venue.navigation=BuildNavigation(spots,bounds);var paths=Paths(venue.navigation,venue,spots);
                File.WriteAllText(Output+"/NavigationDiagnostics.json",new JObject{["status"]="candidate_requires_actual_full_room_walk",["baseline"]=oldPaths,["candidate"]=paths,["changed_exclusions"]=changes,["candidate_bake_radius_m"]=.3f,["runtime_agent_radius_m"]=.3f,["nav_exclusion_bounds"]=new JArray(bounds.Select(b=>new JObject{["min"]=V(b.min),["max"]=V(b.max)})),["scope"]="Only thirty chair exclusions expanded by0.06m per X/Z side; other17 held. Physical furniture and wire anchors unchanged. Complete paths alone do not establish capsule clearance."}.ToString());
                if(paths.Count!=34||paths.Any(p=>(string)p["path"]!="PathComplete"))throw new InvalidDataException("All34 actual fixed destinations must remain complete.");
                RequirePhysicalAndLighting(original,root);
                if(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null)throw new IOException("Could not save isolated F3.");AssetDatabase.SaveAssetIfDirty(venue.navigation);
                var saved=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);RequirePhysicalAndLighting(original,saved);var maps=saved.GetComponent<VenueLightmaps>();
                stage["prefab"]=PrefabPath;stage["baseline_prefab_sha256"]=BaselineHash;stage["navigation"]=paths;stage["navigation_only_revision"]=true;stage["chair_exclusion_padding_m"]=Padding;stage["lighting_reused_from"]=Canonical;stage["limitations"]="Only navigation clearance changed. All geometry, materials, fitted prototype chairs, anchors/approaches and baked lightmaps identical to F2. Actual room/capsule and populated review still pending; no profile, voice or registry assignment.";
                File.WriteAllText(Output+"/StageReceipt.json",stage.ToString());
                File.WriteAllText(Output+"/BakeReceipt.json",new JObject{["status"]="baked_not_published_requires_visual_and_device_review",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["lightmaps"]=maps.colors.Length,["bound_renderers"]=maps.bindings.Length,["lighting_source"]=Canonical,["lighting_source_prefab_sha256"]=BaselineHash,["lighting_source_bake_sha256"]=Hash(Baseline+"/BakeReceipt.json"),["lighting_reused_unchanged"]=true,["new_bake_executed"]=false,["limitations"]="F2 lighting reused with exact texture/binding/renderer/material identity; geometry unchanged. New navigation still requires real walk and ray review."}.ToString());
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static void RequirePhysicalAndLighting(GameObject original,GameObject clone)
        {
            if(Surface(original)!=Surface(clone)||!Colliders(original).SequenceEqual(Colliders(clone)))throw new InvalidDataException("F3 changed physical geometry.");
            var old=original.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a=>a.spotId);var current=clone.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a=>a.spotId);if(old.Count!=34||current.Count!=34)throw new InvalidDataException("Anchor count changed.");
            foreach(var row in old){var a=row.Value;var b=current[row.Key];if(a.kind!=b.kind||a.anim!=b.anim||a.transform.position!=b.transform.position||a.transform.rotation!=b.transform.rotation||(a.approach==null)!=(b.approach==null)||a.approach!=null&&a.approach.position!=b.approach.position)throw new InvalidDataException("Immutable anchor/approach changed: "+row.Key);}
            var aRoot=original.GetComponent<VenueSceneRoot>();var bRoot=clone.GetComponent<VenueSceneRoot>();if(aRoot.venueId!=bRoot.venueId||aRoot.districtId!=bRoot.districtId||aRoot.cameraBounds!=bRoot.cameraBounds||aRoot.cameraDistance!=bRoot.cameraDistance||aRoot.cameraPitch!=bRoot.cameraPitch||aRoot.cameraYaw!=bRoot.cameraYaw||aRoot.cameraFieldOfView!=bRoot.cameraFieldOfView||aRoot.cameraTargetViewportY!=bRoot.cameraTargetViewportY||aRoot.cameraFocusHeight!=bRoot.cameraFocusHeight||aRoot.cameraLookAhead!=bRoot.cameraLookAhead)throw new InvalidDataException("Room camera/identity changed.");
            var renderers=original.GetComponentsInChildren<MeshRenderer>(true).ToDictionary(r=>Relative(r.transform,original.transform));var currentRenderers=clone.GetComponentsInChildren<MeshRenderer>(true).ToDictionary(r=>Relative(r.transform,clone.transform));
            if(renderers.Count!=currentRenderers.Count)throw new InvalidDataException("Renderer count changed.");foreach(var row in renderers)if(!row.Value.sharedMaterials.SequenceEqual(currentRenderers[row.Key].sharedMaterials))throw new InvalidDataException("Source material references changed.");
            var am=original.GetComponent<VenueLightmaps>();var bm=clone.GetComponent<VenueLightmaps>();if(am==null||bm==null||!am.colors.SequenceEqual(bm.colors)||am.mode!=bm.mode||am.bindings.Length!=bm.bindings.Length)throw new InvalidDataException("Held baked lighting changed.");
            for(int i=0;i<am.bindings.Length;i++){var a=am.bindings[i];var b=bm.bindings[i];if(a.renderer==null||b.renderer==null||!b.renderer.transform.IsChildOf(clone.transform)||Relative(a.renderer.transform,original.transform)!=Relative(b.renderer.transform,clone.transform)||a.lightmap!=b.lightmap||a.scaleOffset!=b.scaleOffset)throw new InvalidDataException("Copied native lightmap binding changed.");}
            File.WriteAllText(Output+"/PhysicalAndLightingEquality.json",new JObject{["all_render_surfaces_unchanged"]=true,["all_collider_surfaces_unchanged"]=true,["materials_unchanged"]=true,["anchors"]=34,["approaches"]=31,["lightmap_textures_unchanged"]=am.colors.Length,["native_renderer_bindings_unchanged"]=am.bindings.Length,["new_bake_needed"]=false}.ToString());
        }
        private static string Relative(Transform t,Transform root){var names=new List<string>();for(var p=t;p!=null&&p!=root;p=p.parent)names.Add(p.name+"["+p.GetSiblingIndex()+"]");names.Reverse();return string.Join("/",names);}
        private static string K(Vector3 p)=>p.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        private static string Surface(GameObject root)
        {
            var rows=new List<string>();foreach(var f in root.GetComponentsInChildren<MeshFilter>(true)){var v=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var t=f.sharedMesh.triangles;for(int i=0;i<t.Length;i+=3)rows.Add(K(v[t[i]])+";"+K(v[t[i+1]])+";"+K(v[t[i+2]]));}rows.Sort(StringComparer.Ordinal);using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("/",rows)))).Replace("-","").ToLowerInvariant();
        }
        private static string[] Colliders(GameObject root)
        {
            var rows=new List<string>();foreach(var c in root.GetComponentsInChildren<Collider>(true))
            {
                string prefix=c.GetType().Name+"|"+c.enabled+"|"+c.isTrigger+"|"+c.gameObject.layer+"|";
                if(c is MeshCollider m){var v=m.sharedMesh.vertices.Select(m.transform.TransformPoint).ToArray();var t=m.sharedMesh.triangles;for(int i=0;i<t.Length;i+=3)rows.Add(prefix+K(v[t[i]])+";"+K(v[t[i+1]])+";"+K(v[t[i+2]]));}
                else if(c is BoxCollider b)foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})rows.Add(prefix+K(b.transform.TransformPoint(b.center+Vector3.Scale(b.size*.5f,new Vector3(x,y,z)))));
                else throw new InvalidDataException("Unreviewed collider shape.");
            }rows.Sort(StringComparer.Ordinal);return rows.ToArray();
        }
        private static bool CloseXZ(Bounds a,Bounds b)=>Mathf.Abs(a.min.x-b.min.x)<.00002f&&Mathf.Abs(a.max.x-b.max.x)<.00002f&&Mathf.Abs(a.min.z-b.min.z)<.00002f&&Mathf.Abs(a.max.z-b.max.z)<.00002f;
        private static Bounds BoundsFrom(Vector3 lo,Vector3 hi)=>new Bounds((lo+hi)*.5f,hi-lo);
        private static Vector3 ReadV(JToken t)=>new Vector3((float)t[0],(float)t[1],(float)t[2]);
        private static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static void Protected(string phase,Action action)
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)throw new InvalidOperationException("Use coordinator-owned idle batch.");if(_protection!=null)throw new InvalidOperationException("Transaction active.");_protection=new ProtectedFiles(phase);
            try{action();_protection.Verify();_protection.Record("passed_protected_physical_bytes_unchanged",null);}
            catch(Exception e){_protection.Restore();_protection.Record("failed_original_physical_bytes_restored",e.ToString());Directory.CreateDirectory(Output);File.WriteAllText(Output+"/StageReceipt.json",new JObject{["status"]="failed",["error"]=e.ToString()}.ToString());throw;}
            finally{_protection=null;}
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
                Debug.Log("CTLIFE [studio-shared-chair-f3] physical protection receipt: "+Path.Combine(_backup,"transaction.json"));
            }
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
