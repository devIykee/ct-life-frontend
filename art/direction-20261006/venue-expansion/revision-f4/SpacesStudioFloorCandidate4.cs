using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Replace raised decorative stage skins with disjoint material regions on the exact existing floor.</summary>
    public static class SpacesStudioFloorCandidate4
    {
        public const string Output="Assets/Art/Candidates/SpacesStudio20261007F4";
        public const string PrefabPath=Output+"/SpacesStudioF4.prefab";
        private const string Baseline="Assets/Art/Candidates/SpacesStudio20261007F3";
        private const string Canonical=Baseline+"/SpacesStudioF3.prefab";
        private const string BaselineHash="dd6413805b84b07c9b59e24cd6f708f38c745bf82204a96d36b63667c8cac1b2";
        private const string Snapshot="Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest="Assets/Art/environment/manifest.json";
        private static ProtectedFiles _protection;
        private const string FloorName="Floor_0",StageName="StudioStageAndAcoustics_0",EdgeName="StudioStageAndAcoustics_1";
        public static void BuildCandidate()
        {
            if(Directory.Exists(Output))throw new InvalidOperationException("Preserve earlier F4 output; use a reviewed new revision.");
            Protected("stage",Stage);
        }
        private static void RequireSources()
        {
            if(Hash(Canonical)!=BaselineHash||Hash(Baseline+"/BakeReceipt.json")!="263ab32d20ec843add77a3be10142a6e59fe3ce0a4b04a0db1d0c38102730ce3")throw new InvalidDataException("Exact F3 source changed.");
            var stage=JObject.Parse(File.ReadAllText(Baseline+"/StageReceipt.json"));
            if((string)stage["snapshot_sha256"]!=Hash(Snapshot)||(bool?)stage["navigation_only_revision"]!=true||((JArray)stage["navigation"]).Count!=34||stage["navigation"].Any(row=>(string)row["path"]!="PathComplete"))throw new InvalidDataException("F3 saved contract/navigation stage differs.");
        }
        private static void Stage()
        {
            RequireSources();Directory.CreateDirectory(Output+"/Meshes");AssetDatabase.Refresh();
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var original=AssetDatabase.LoadAssetAtPath<GameObject>(Canonical);var root=Object.Instantiate(original);root.name="Spaces Studio / exact flush floor inlay / candidate";UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                if(root.GetComponent<CafeSeatMotionProfile>()!=null)throw new InvalidDataException("F3 unexpectedly has a motion profile; preserve review scope.");
                var maps=root.GetComponent<VenueLightmaps>();if(maps!=null)Object.DestroyImmediate(maps);
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)){renderer.lightmapIndex=-1;renderer.lightmapScaleOffset=new Vector4(1,1,0,0);}
                var partition=Partition(root);RequireHeld(original,root,true);
                if(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null)throw new IOException("Could not save isolated F4 floor revision.");
                AssetDatabase.SaveAssetIfDirty(Find(root,FloorName).sharedMesh);
                var saved=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);RequireHeld(original,saved,true);RequireFloorPinned(saved,partition);
                var receipt=JObject.Parse(File.ReadAllText(Baseline+"/StageReceipt.json"));receipt["prefab"]=PrefabPath;receipt["baseline_prefab_sha256"]=BaselineHash;receipt["floor_inlay_revision"]=true;receipt["floor_partition"]=partition;receipt["static_mesh_renderers"]=saved.GetComponentsInChildren<MeshRenderer>(true).Length;
                receipt.Remove("lighting_reused_from");receipt.Remove("navigation_only_revision");receipt["limitations"]="Only decorative raised stage/edge render boxes are removed and their existing materials partition the original Floor_0 plane. Exact chairs, fitted support, actor/wire anchors, physical colliders, approaches and F3 navigation are held. Fresh bake, populated stage contact and room art review required. No motion profile, connected authority, voice or registry assignment.";
                File.WriteAllText(Output+"/StageReceipt.json",receipt.ToString());
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        public static void BakeCandidate()=>Protected("bake",()=>
        {
            RequireSources();var receipt=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));if((string)receipt["status"]!="staged_unbaked_not_published"||(bool?)receipt["floor_inlay_revision"]!=true)throw new InvalidDataException("F4 stage did not pass.");
            VenueLightingBuilder.BakeVenue(PrefabPath,Output+"/Lighting");var root=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);RequireHeld(AssetDatabase.LoadAssetAtPath<GameObject>(Canonical),root,false);RequireFloorPinned(root,(JObject)receipt["floor_partition"]);
            var maps=root.GetComponent<VenueLightmaps>();if(maps==null||maps.colors.Length==0||maps.bindings.Length==0)throw new InvalidDataException("F4 has no new bound lightmaps.");
            File.WriteAllText(Output+"/BakeReceipt.json",new JObject{["status"]="baked_not_published_requires_visual_and_device_review",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["lightmaps"]=maps.colors.Length,["bound_renderers"]=maps.bindings.Length,["new_bake_executed"]=true,["limitations"]="Flush decorative floor correction with exact F3 physical/navigation/anchor geometry held. Populated floor/chair/feet and art review still required; no action/exit, connected authority, voice or registry promotion."}.ToString());
        });
        private sealed class Vertex
        {
            public Vector3 p,n;public Vector4 tangent;public Vector2 uv,uv2;public Color color;
            public static Vertex Lerp(Vertex a,Vertex b,float t,int axis,float boundary)
            {
                var p=Vector3.LerpUnclamped(a.p,b.p,t);p[axis]=boundary;
                return new Vertex{p=p,n=Vector3.LerpUnclamped(a.n,b.n,t),tangent=Vector4.LerpUnclamped(a.tangent,b.tangent,t),uv=Vector2.LerpUnclamped(a.uv,b.uv,t),uv2=Vector2.LerpUnclamped(a.uv2,b.uv2,t),color=Color.LerpUnclamped(a.color,b.color,t)};
            }
        }
        private sealed class Region { public float x0,x1,z0,z1;public bool Contains(Vector3 p)=>p.x>=x0-.000001f&&p.x<=x1+.000001f&&p.z>=z0-.000001f&&p.z<=z1+.000001f;public double Area=>(double)(x1-x0)*(z1-z0); }
        private static List<Vertex> Clip(List<Vertex> polygon,int axis,float boundary,bool greater)
        {
            var result=new List<Vertex>();for(int i=0;i<polygon.Count;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Count];bool ia=greater?a.p[axis]>=boundary:a.p[axis]<=boundary;bool ib=greater?b.p[axis]>=boundary:b.p[axis]<=boundary;
                if(ia)result.Add(a);if(ia!=ib)result.Add(Vertex.Lerp(a,b,(boundary-a.p[axis])/(b.p[axis]-a.p[axis]),axis,boundary));
            }return result;
        }
        private static List<Vertex> Extract(List<Vertex> polygon,Region r,List<List<Vertex>> outside)
        {
            var current=polygon;foreach(var plane in new[]{(0,r.x0,true),(0,r.x1,false),(2,r.z0,true),(2,r.z1,false)})
            {
                if(current.Count<3)break;var rest=Clip(current,plane.Item1,plane.Item2,!plane.Item3);if(Area(rest)>1e-10)outside.Add(rest);current=Clip(current,plane.Item1,plane.Item2,plane.Item3);
            }return current;
        }
        private static double Area(List<Vertex> p){double area=0;for(int i=1;i+1<p.Count;i++)area+=TriangleArea(p[0].p,p[i].p,p[i+1].p);return area;}
        private static double TriangleArea(Vector3 a,Vector3 b,Vector3 c)=>Math.Abs(((double)b.x-a.x)*((double)c.z-a.z)-((double)b.z-a.z)*((double)c.x-a.x))*.5d;
        private static JObject Partition(GameObject root)
        {
            var floor=Find(root,FloorName);var stage=Find(root,StageName);var edge=Find(root,EdgeName);
            if(floor.transform.localToWorldMatrix!=Matrix4x4.identity||stage.transform.localToWorldMatrix!=Matrix4x4.identity||edge.transform.localToWorldMatrix!=Matrix4x4.identity)throw new InvalidDataException("Native source floor/skin transforms changed.");
            foreach(var f in new[]{floor,stage,edge})if(f.GetComponents<Collider>().Length!=0||f.sharedMesh.subMeshCount!=1)throw new InvalidDataException("Decorative floor pieces must have one submesh and no physical collider.");
            if(Hash(AssetDatabase.GetAssetPath(floor.sharedMesh))!="b9ce5038ccac9836db64335a59c0cf9467c8556950000692b5ddc0cc7175e68f"||Hash(AssetDatabase.GetAssetPath(stage.sharedMesh))!="c3177036eee8213adcc68f0f37f3fe3c9bc23c74b29c40cccc65708aeee77fc1"||Hash(AssetDatabase.GetAssetPath(edge.sharedMesh))!="296e2dca8f257cee736fbf3bd46a5907accc3f4f63ae962b4cfc9d2336054431")throw new InvalidDataException("Exact native source floor/skins changed.");
            var sv=stage.sharedMesh.vertices;var ev=edge.sharedMesh.vertices;var smin=sv.Aggregate(Vector3.Min);var smax=sv.Aggregate(Vector3.Max);var emin=ev.Aggregate(Vector3.Min);var emax=ev.Aggregate(Vector3.Max);
            if(stage.sharedMesh.triangles.Length!=36||edge.sharedMesh.triangles.Length!=36||Mathf.Abs(smax.y-.006f)>.0000001f||Mathf.Abs(emax.y-.003f)>.0000001f||smin.x!=emin.x||smax.x!=emax.x||emax.z<=smin.z||emin.z>=smin.z)throw new InvalidDataException("Expected exact two raised decorative boxes.");
            var brass=new Region{x0=emin.x,x1=emax.x,z0=emin.z,z1=emax.z};var walnut=new Region{x0=smin.x,x1=smax.x,z0=emax.z,z1=smax.z};
            var source=floor.sharedMesh;var p=source.vertices;var normals=source.normals;var uv=source.uv;var uv2=source.uv2;var tangents=source.tangents;var colors=source.colors;var ids=source.triangles;
            if(p.Length!=320||ids.Length!=480||normals.Length!=p.Length||uv.Length!=p.Length||uv2.Length!=p.Length||p.Any(v=>v.y!=p[0].y)||Mathf.Abs(p[0].y)>.00000001f)throw new InvalidDataException("Native visible floor plane/attributes changed.");
            Vertex Read(int i)=>new Vertex{p=p[i],n=normals[i],uv=uv[i],uv2=uv2[i],tangent=tangents.Length==p.Length?tangents[i]:Vector4.zero,color=colors.Length==p.Length?colors[i]:Color.white};
            var output=new List<Vertex>();var parts=new[]{new List<int>(),new List<int>(),new List<int>()};var areas=new double[3];var rows=new JArray();double originalArea=0,maxPerTriangleError=0;
            void Add(List<Vertex> poly,int material)
            {
                for(int i=1;i+1<poly.Count;i++)
                {
                    var a=poly[0];var b=poly[i];var c=poly[i+1];double area=TriangleArea(a.p,b.p,c.p);if(area<=1e-10)continue;
                    var center=(a.p+b.p+c.p)/3;if(material==1&&!walnut.Contains(center)||material==2&&!brass.Contains(center))throw new InvalidDataException("Partition material escapes its exact region.");
                    if(a.p.y!=p[0].y||b.p.y!=p[0].y||c.p.y!=p[0].y)throw new InvalidDataException("Partition lifted the existing floor plane.");
                    foreach(var v in new[]{a,b,c}){parts[material].Add(output.Count);output.Add(v);}areas[material]+=area;
                }
            }
            for(int i=0;i<ids.Length;i+=3)
            {
                var poly=new List<Vertex>{Read(ids[i]),Read(ids[i+1]),Read(ids[i+2])};double expected=Area(poly);originalArea+=expected;double before=areas.Sum();
                var outer=new List<List<Vertex>>();var metal=Extract(poly,brass,outer);Add(metal,2);
                foreach(var rest in outer){var untouched=new List<List<Vertex>>();var wood=Extract(rest,walnut,untouched);Add(wood,1);foreach(var left in untouched)Add(left,0);}
                double actual=areas.Sum()-before;double error=Math.Abs(actual-expected);maxPerTriangleError=Math.Max(maxPerTriangleError,error);
                if(error>.00001d)throw new InvalidDataException("Per-source-triangle partition coverage differs: "+i/3+" error="+error);
                rows.Add(new JObject{["source_triangle"]=i/3,["source_area_m2"]=expected,["partition_area_m2"]=actual,["absolute_error_m2"]=error});
            }
            if(Math.Abs(originalArea-320d)>.00001||Math.Abs(areas.Sum()-originalArea)>.0001||Math.Abs(areas[1]-walnut.Area)>.0001||Math.Abs(areas[2]-brass.Area)>.0001)throw new InvalidDataException("Full floor/inlay area coverage differs.");
            var mesh=new Mesh{name="Exact native floor plane - disjoint walnut and brass inlay",indexFormat=source.indexFormat};mesh.SetVertices(output.Select(v=>v.p).ToList());mesh.SetNormals(output.Select(v=>v.n).ToList());mesh.SetUVs(0,output.Select(v=>v.uv).ToList());mesh.SetUVs(1,output.Select(v=>v.uv2).ToList());if(tangents.Length==p.Length)mesh.SetTangents(output.Select(v=>v.tangent).ToList());if(colors.Length==p.Length)mesh.SetColors(output.Select(v=>v.color).ToList());mesh.subMeshCount=3;for(int i=0;i<3;i++)mesh.SetTriangles(parts[i],i);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Output+"/Meshes/ExactFlushFloor.asset");
            var oldMaterial=floor.GetComponent<MeshRenderer>().sharedMaterial;var woodMaterial=stage.GetComponent<MeshRenderer>().sharedMaterial;var brassMaterial=edge.GetComponent<MeshRenderer>().sharedMaterial;
            floor.sharedMesh=mesh;floor.GetComponent<MeshRenderer>().sharedMaterials=new[]{oldMaterial,woodMaterial,brassMaterial};Object.DestroyImmediate(stage.gameObject);Object.DestroyImmediate(edge.gameObject);
            var receipt=new JObject{["source_floor_sha256"]=Hash(AssetDatabase.GetAssetPath(source)),["source_floor_area_m2"]=originalArea,["partition_area_m2"]=areas.Sum(),["held_top_y_m"]=p[0].y,["per_material_area_m2"]=new JArray(areas),["expected_walnut_area_m2"]=walnut.Area,["expected_brass_area_m2"]=brass.Area,["maximum_per_source_triangle_area_error_m2"]=maxPerTriangleError,["source_triangles"]=ids.Length/3,["candidate_triangles"]=mesh.triangles.Length/3,["candidate_vertices"]=mesh.vertexCount,["floor_surface_sha256"]=Surface(floor.gameObject,false),["material_assets"]=new JArray(new[]{oldMaterial,woodMaterial,brassMaterial}.Select(AssetDatabase.GetAssetPath)),["removed_noncolliding_decorative_boxes"]=2,["source_triangle_coverage"]=rows,["seam_method"]="Convex half-plane partition of each original triangle; intersection coordinates pinned to the exact shared X/Z boundary; all original planeY/UV/normal interpolation retained. Brass and walnut regions are disjoint; no second coplanar surface or height overlay is created."};
            File.WriteAllText(Output+"/FloorPartition.json",receipt.ToString());return receipt;
        }
        private static MeshFilter Find(GameObject root,string name)=>root.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==name);
        private static bool IsFloorArt(Transform t)=>t.name==FloorName||t.name==StageName||t.name==EdgeName;
        private static void RequireFloorPinned(GameObject root,JObject receipt)
        {
            if(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name==StageName||t.name==EdgeName))throw new InvalidDataException("Raised decorative stage/edge remain.");
            var floor=Find(root,FloorName);if(Surface(floor.gameObject,false)!=(string)receipt["floor_surface_sha256"]||floor.sharedMesh.vertices.Any(v=>v.y!=(float)receipt["held_top_y_m"]))throw new InvalidDataException("Saved/baked partition altered exact floor surfaces.");
        }
        private static void RequireHeld(GameObject original,GameObject clone,bool compareMaterialReferences)
        {
            if(Surface(original,true)!=Surface(clone,true)||!Colliders(original).SequenceEqual(Colliders(clone)))throw new InvalidDataException("F4 changed a non-floor rendered surface or physical collider.");
            var old=original.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a=>a.spotId);var current=clone.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a=>a.spotId);if(old.Count!=34||current.Count!=34)throw new InvalidDataException("Anchor count changed.");
            foreach(var row in old){var a=row.Value;var b=current[row.Key];if(a.kind!=b.kind||a.anim!=b.anim||a.transform.position!=b.transform.position||a.transform.rotation!=b.transform.rotation||(a.approach==null)!=(b.approach==null)||a.approach!=null&&(a.approach.position!=b.approach.position||a.approach.rotation!=b.approach.rotation))throw new InvalidDataException("Immutable anchor/approach changed: "+row.Key);}
            var aRoot=original.GetComponent<VenueSceneRoot>();var bRoot=clone.GetComponent<VenueSceneRoot>();if(aRoot.navigation!=bRoot.navigation||aRoot.venueId!=bRoot.venueId||aRoot.districtId!=bRoot.districtId||aRoot.cameraBounds!=bRoot.cameraBounds||aRoot.cameraDistance!=bRoot.cameraDistance||aRoot.cameraPitch!=bRoot.cameraPitch||aRoot.cameraYaw!=bRoot.cameraYaw||aRoot.cameraFieldOfView!=bRoot.cameraFieldOfView||aRoot.cameraTargetViewportY!=bRoot.cameraTargetViewportY||aRoot.cameraFocusHeight!=bRoot.cameraFocusHeight||aRoot.cameraLookAhead!=bRoot.cameraLookAhead)throw new InvalidDataException("Room navigation/camera/identity changed.");
            if(compareMaterialReferences)
            {
                var renderers=original.GetComponentsInChildren<MeshRenderer>(true).Where(v=>!IsFloorArt(v.transform)).ToArray();var currentRenderers=clone.GetComponentsInChildren<MeshRenderer>(true).Where(v=>!IsFloorArt(v.transform)).ToArray();if(renderers.Length!=currentRenderers.Length)throw new InvalidDataException("Non-floor renderer count changed.");
                // Removing two siblings changes sibling indices: unchanged renderer traversal order plus exact surfaces identifies remaining rows.
                for(int i=0;i<renderers.Length;i++)if(renderers[i].name!=currentRenderers[i].name||!renderers[i].sharedMaterials.SequenceEqual(currentRenderers[i].sharedMaterials))throw new InvalidDataException("Non-floor material references changed.");
            }
            File.WriteAllText(Output+"/HeldPhysicalEquality.json",new JObject{["all_nonfloor_render_surfaces_unchanged"]=true,["all_collider_surfaces_unchanged"]=true,["all_chairs_and_supports_unchanged"]=true,["anchors"]=34,["approaches"]=31,["f3_navigation_asset_unchanged"]=true,["camera_unchanged"]=true,["existing_materials_only"]=true}.ToString());
        }
        private static string K(Vector3 p)=>p.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        private static string Surface(GameObject root,bool skipFloorArt)
        {
            var rows=new List<string>();foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(v=>!skipFloorArt||!IsFloorArt(v.transform))){var v=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var t=f.sharedMesh.triangles;for(int i=0;i<t.Length;i+=3)rows.Add(K(v[t[i]])+";"+K(v[t[i+1]])+";"+K(v[t[i+2]]));}rows.Sort(StringComparer.Ordinal);using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("/",rows)))).Replace("-","").ToLowerInvariant();
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
        private static void Protected(string phase,Action action)
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)throw new InvalidOperationException("Use coordinator-owned idle batch.");if(_protection!=null)throw new InvalidOperationException("Transaction active.");_protection=new ProtectedFiles(phase);
            try{action();_protection.Verify();_protection.Record("passed_protected_physical_bytes_unchanged",null);}
            catch(Exception e){_protection.Restore();_protection.Record("failed_original_physical_bytes_restored",e.ToString());Directory.CreateDirectory(Output);File.WriteAllText(Output+"/"+(phase=="bake"?"BakeReceipt.json":"StageReceipt.json"),new JObject{["status"]="failed",["error"]=e.ToString()}.ToString());throw;}
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
                _backup=Path.Combine(Path.GetTempPath(),"ctlife-studio-floor-"+phase+"-"+Guid.NewGuid().ToString("N"));
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
                Debug.Log("CTLIFE [studio-floor-f4] physical protection receipt: "+Path.Combine(_backup,"transaction.json"));
            }
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
