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
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Isolated monitor-support correction; no fitted furniture, collider, anchor, motion or registry change.</summary>
    public static class TerminalCafeVisualCandidate5
    {
        public const string Output="Assets/Art/Candidates/TerminalCafe20261007Art5";
        public const string PrefabPath=Output+"/TerminalCafeArt5.prefab";
        private const string Baseline="Assets/Art/Candidates/TerminalCafe20261007Art3";
        private const string Canonical=Baseline+"/TerminalCafeArt3.prefab";
        private const string BaselineHash="80ee1475262cbbbf69cfaaddcf5d0705d93a65f27446b6c843d2d2bfee7a2065";
        private const string NavHash="b9033beff4817f674d75775fc9c961cd957bcca68548fa748fb33cd15c8d0318";
        private const string Snapshot="Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest="Assets/Art/environment/manifest.json";
        private static ProtectedFiles _protection;
        private static readonly JArray Monitors=new JArray();
        private static Mesh _bracket;
        private static Vector2 _supportUv;
        private static readonly Bounds[] Parts={
            new Bounds(new Vector3(0,.015f,.009510707f),new Vector3(.30f,.03f,.22f)),
            new Bounds(new Vector3(0,.220000006f,.039510705f),new Vector3(.05f,.42f,.05f)),
            new Bounds(new Vector3(0,.449999988f,.039510705f),new Vector3(1.50f,.05f,.05f))};
        public static void BuildCandidate()
        {
            if(Directory.Exists(Output))throw new InvalidOperationException("Preserve existing Art5 output; use a reviewed new revision.");
            Protected("stage",Stage);
        }
        public static void BakeCandidate()=>Protected("bake",()=>
        {
            RequireBaseline();var stage=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));
            if((string)stage["status"]!="staged_unbaked_not_published"||(bool?)stage["exact_fitted_surfaces_held"]!=true||(string)stage["snapshot_sha256"]!=Hash(Snapshot)||(int)stage["monitor_supports_repaired"]!=8)throw new InvalidDataException("Art5 stage identity differs.");
            VenueLightingBuilder.BakeVenue(PrefabPath,Output+"/Lighting");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);RequireHeldGeometry(AssetDatabase.LoadAssetAtPath<GameObject>(Canonical),prefab);
            var pinned=((JArray)stage["monitor_geometry"]).ToDictionary(r=>(string)r["path"]);
            foreach(var monitor in FindMonitors(prefab))if(Surface(monitor.gameObject,false)!=(string)pinned[Relative(monitor,prefab.transform)]["surface_sha256"])throw new InvalidDataException("Art5 bake changed pinned monitor surfaces.");
            var maps=prefab.GetComponent<VenueLightmaps>();if(maps==null||maps.colors.Length==0||maps.bindings.Length==0)throw new InvalidDataException("Art5 bake has no bound lightmaps.");
            File.WriteAllText(Output+"/BakeReceipt.json",new JObject{["status"]="baked_not_published_requires_visual_and_device_review",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["lightmaps"]=maps.colors.Length,["bound_renderers"]=maps.bindings.Length,["art_only"]=true,["limitations"]="Monitor-support art correction. No seat profile, exit acceptance, connected authority, registration or phone approval."}.ToString());
        });
        private static void RequireBaseline()
        {
            if(Hash(Canonical)!=BaselineHash||Hash(Baseline+"/BakeReceipt.json")!="4985c199eb696b19927b0651ee1e865b0d4c3d87b997bb4335eaf663813e8bfb"||Hash(Baseline+"/Meshes/Navigation.asset")!=NavHash)throw new InvalidDataException("Exact accepted Art3 baseline changed.");
        }
        private static void Stage()
        {
            RequireBaseline();Directory.CreateDirectory(Output+"/Meshes");AssetDatabase.Refresh();Monitors.Clear();_bracket=null;
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var original=AssetDatabase.LoadAssetAtPath<GameObject>(Canonical);var root=Object.Instantiate(original);root.name="Terminal Cafe / repaired rear monitor supports / candidate";UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                if(root.GetComponent<CafeSeatMotionProfile>()!=null)throw new InvalidDataException("Art3 unexpectedly contains an assigned seat profile.");
                var maps=root.GetComponent<VenueLightmaps>();if(maps!=null)Object.DestroyImmediate(maps);
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)){renderer.lightmapIndex=-1;renderer.lightmapScaleOffset=new Vector4(1,1,0,0);}
                var monitors=FindMonitors(root);if(monitors.Length!=8)throw new InvalidDataException("Expected eight exact monitor objects.");
                for(int i=0;i<monitors.Length;i++)Repair(monitors[i],root.transform,i);
                RequireHeldGeometry(original,root);
                if(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null)throw new IOException("Could not save isolated Art5.");SaveOwned();
                var receipt=JObject.Parse(File.ReadAllText(Baseline+"/StageReceipt.json"));receipt["prefab"]=PrefabPath;receipt["baseline_prefab_sha256"]=BaselineHash;receipt["exact_fitted_surfaces_held"]=true;receipt["monitor_supports_repaired"]=8;receipt["monitor_geometry"]=Monitors.DeepClone();receipt["static_mesh_renderers"]=root.GetComponentsInChildren<MeshRenderer>(true).Length;receipt["additional_monitor_bracket_triangles"]=288;receipt["actual_mesh_triangles_all_lods"]=root.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh.triangles.Length/3);
                receipt["monitor_source_limitation_resolved_geometry_only"]="Source stand crossbar and vertical post translated rear0.10m; base rear0.05m; three rear mounting brackets per station. Existing screen/bezel triangles, UVs, normals and materials unchanged. Native beauty still required.";
                receipt.Remove("source_monitor_limitation");receipt["limitations"]="Unregistered art-only clone of accepted Art3. Only monitor support geometry changes; all fitted chair/desk/keyboard surfaces, colliders, anchors, approaches, navigation, lights and artwork remain held. No seat profile or interaction promotion.";
                File.WriteAllText(Output+"/StageReceipt.json",receipt.ToString());
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);_bracket=null;}
        }
        private static Transform[] FindMonitors(GameObject root)=>root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Direction_monitor_triple").OrderBy(t=>t.position.z).ThenBy(t=>t.position.x).ToArray();
        private static bool IsBoxCorner(Bounds b,Vector3 p){var q=p-b.center;var e=b.extents;return Mathf.Abs(Mathf.Abs(q.x)-e.x)<.00001f&&Mathf.Abs(Mathf.Abs(q.y)-e.y)<.00001f&&Mathf.Abs(Mathf.Abs(q.z)-e.z)<.00001f;}
        private static void Repair(Transform monitor,Transform room,int station)
        {
            if(monitor.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidDataException("Monitor is not a non-collision detail; do not silently change interaction geometry.");
            string expectedTexturePath=Baseline+"/Textures/Desktop"+(station+1).ToString("D2")+".png";
            var expectedTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(expectedTexturePath);
            var screenReceipt=JObject.Parse(File.ReadAllText(Baseline+"/StageReceipt.json"))["screens"].Single(row=>(int)row["station"]==station+1);
            if(expectedTexture==null||Hash(expectedTexturePath)!=(string)screenReceipt["texture_sha256"])throw new InvalidDataException("Exact original desktop texture changed.");
            var materialRows=new JArray();int sourceScreenTriangles=0;
            var count=new int[3];var vertexCount=new int[3];var planes=new Dictionary<int,(Vector3 point,Vector3 normal)>();Material supportMaterial=null;GameObject sourceFlags=null;int meshNumber=0;var changedRows=new JArray();
            foreach(var filter in monitor.GetComponentsInChildren<MeshFilter>(true))
            {
                var source=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();var vertices=source.vertices;var local=vertices.Select(v=>monitor.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();var claims=new Dictionary<int,int>();var normals=source.normals;var uv=source.uv;
                for(int sub=0;sub<source.subMeshCount;sub++)
                {
                    var ids=source.GetTriangles(sub);var subMaterial=renderer.sharedMaterials[sub];
                    bool screen=subMaterial.HasProperty("_BaseMap")&&subMaterial.GetTexture("_BaseMap")==expectedTexture;
                    materialRows.Add(new JObject{["mesh"]=Relative(filter.transform,monitor),["submesh"]=sub,["material_name"]=subMaterial.name,["material_asset"]=AssetDatabase.GetAssetPath(subMaterial),["base_map"]=subMaterial.HasProperty("_BaseMap")?AssetDatabase.GetAssetPath(subMaterial.GetTexture("_BaseMap")):null,["exact_desktop_texture"]=screen,["triangles"]=ids.Length/3});
                    for(int k=0;k<ids.Length;k+=3)
                    {
                        int part=-1;for(int p=0;p<3;p++)if(IsBoxCorner(Parts[p],local[ids[k]])&&IsBoxCorner(Parts[p],local[ids[k+1]])&&IsBoxCorner(Parts[p],local[ids[k+2]])){if(part>=0)throw new InvalidDataException("Ambiguous stand triangle.");part=p;}
                        if(part>=0)
                        {
                            count[part]++;supportMaterial=renderer.sharedMaterials[sub];sourceFlags=filter.gameObject;
                            for(int j=0;j<3;j++){int id=ids[k+j];if(claims.TryGetValue(id,out int old)&&old!=part)throw new InvalidDataException("Stand parts share a moved vertex.");claims[id]=part;}
                        }
                        else if(screen)
                        {
                            sourceScreenTriangles++;
                            var a=local[ids[k]];var b=local[ids[k+1]];var c=local[ids[k+2]];var center=(a+b+c)/3;int panel=center.x<-.25f?0:center.x>.25f?2:1;var n=Vector3.Cross(b-a,c-a).normalized;if(n.z>0)n=-n;
                            if(planes.TryGetValue(panel,out var previous)&&Vector3.Dot(n,previous.normal)<.9999f)throw new InvalidDataException("Panel normals disagree.");planes[panel]=(a,n);
                        }
                    }
                }
                if(claims.Count==0)continue;
                // A moved support vertex must not also belong to an unchanged screen/bezel triangle.
                foreach(int sub in Enumerable.Range(0,source.subMeshCount))
                {
                    var ids=source.GetTriangles(sub);for(int i=0;i<ids.Length;i+=3)if(Enumerable.Range(0,3).Any(j=>claims.ContainsKey(ids[i+j]))&&!Enumerable.Range(0,3).All(j=>claims.ContainsKey(ids[i+j])&&claims[ids[i+j]]==claims[ids[i]]))throw new InvalidDataException("Support selection would move a non-support face.");
                }
                var mesh=Object.Instantiate(source);foreach(var item in claims)
                {
                    float dz=item.Value==0?.05f:.10f;var p=local[item.Key]+Vector3.forward*dz;vertices[item.Key]=filter.transform.InverseTransformPoint(monitor.TransformPoint(p));vertexCount[item.Value]++;
                }
                if(meshNumber==0)_supportUv=uv[claims.First().Key];
                mesh.vertices=vertices;mesh.RecalculateBounds();
                if(!mesh.uv.SequenceEqual(uv)||!mesh.normals.SequenceEqual(normals)||!mesh.triangles.SequenceEqual(source.triangles))throw new InvalidDataException("Support edit changed original UVs/normals/topology.");
                string path=Output+"/Meshes/MonitorSupport"+station.ToString("D2")+"_"+meshNumber+".asset";AssetDatabase.CreateAsset(mesh,path);filter.sharedMesh=mesh;
                changedRows.Add(new JObject{["path"]=Relative(filter.transform,monitor),["source_mesh"]=AssetDatabase.GetAssetPath(source),["candidate_mesh"]=path,["moved_vertex_count"]=claims.Count,["original_topology_uv_normals_held"]=true});meshNumber++;
            }
            File.WriteAllText(Output+"/ScreenClassification"+(station+1).ToString("D2")+".json",new JObject{["expected_texture"]=expectedTexturePath,["expected_texture_sha256"]=Hash(expectedTexturePath),["materials"]=materialRows,["source_box_triangles"]=new JArray(count),["source_screen_triangles"]=sourceScreenTriangles,["screen_plane_count"]=planes.Count}.ToString());
            if(count.Any(n=>n!=12)||planes.Count!=3||sourceScreenTriangles!=6||supportMaterial==null)throw new InvalidDataException("Expected three source stand boxes and three exact-texture screen planes; boxes="+string.Join(",",count)+" screenTriangles="+sourceScreenTriangles+" planes="+planes.Count+". Classification receipt preserved.");
            if(_bracket==null)
            {
                var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);_bracket=Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(primitive);_bracket.name="Original rear monitor mounting bracket";_bracket.uv=Enumerable.Repeat(_supportUv,_bracket.vertexCount).ToArray();AssetDatabase.CreateAsset(_bracket,Output+"/Meshes/RearMountingBracket.asset");
            }
            var bracketRows=new JArray();
            foreach(var pair in new[]{(-.56f,0),(0f,1),(.56f,2)})
            {
                var go=new GameObject("Rear monitor mounting bracket "+pair.Item2);go.transform.SetParent(monitor,false);go.transform.localPosition=new Vector3(pair.Item1,.45f,.0625f);go.transform.localScale=new Vector3(.05f,.05f,.135f);
                GameObjectUtility.SetStaticEditorFlags(go,GameObjectUtility.GetStaticEditorFlags(sourceFlags));go.AddComponent<MeshFilter>().sharedMesh=_bracket;go.AddComponent<MeshRenderer>().sharedMaterial=supportMaterial;
                var actual=_bracket.vertices.Select(v=>monitor.InverseTransformPoint(go.transform.TransformPoint(v))).ToArray();var plane=planes[pair.Item2];float maxFront=actual.Max(p=>Vector3.Dot(plane.normal,p-plane.point));
                if(maxFront>-.01f)throw new InvalidDataException("Rear bracket reaches within10mm of visible screen plane.");
                bracketRows.Add(new JObject{["panel"]=pair.Item2,["max_signed_screen_plane_distance_m"]=maxFront,["bounds_min"]=V(actual.Aggregate(Vector3.Min)),["bounds_max"]=V(actual.Aggregate(Vector3.Max))});
            }
            // Exact native desk footprint remains [0.2,0.89749283] relative to each fixed actor anchor.
            float baseRearWorldRelative=.7187464f+Parts[0].max.z+.05f;float rearDeskMargin=.89749283f-baseRearWorldRelative;
            if(rearDeskMargin<.009f)throw new InvalidDataException("Shifted base is not fully supported by the unchanged desk.");
            var row=new JObject{["path"]=Relative(monitor,room),["position"]=V(monitor.position),["source_box_triangles"]=new JArray(count),["moved_vertices_per_part"]=new JArray(vertexCount),["base_rear_shift_m"]=.05f,["post_and_bar_rear_shift_m"]=.10f,["desk_rear_margin_m"]=rearDeskMargin,["new_brackets"]=bracketRows,["mesh_edits"]=changedRows,["source_screen_triangles"]=sourceScreenTriangles,["screen_texture"]=expectedTexturePath,["surface_sha256"]=Surface(monitor.gameObject,false)};Monitors.Add(row);
        }
        private static bool UnderMonitor(Transform t){for(var p=t;p!=null;p=p.parent)if(p.name=="Direction_monitor_triple")return true;return false;}
        private static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static string Relative(Transform t,Transform root){var names=new List<string>();for(var p=t;p!=null&&p!=root;p=p.parent)names.Add(p.name);names.Reverse();return string.Join("/",names);}
        private static string VectorKey(Vector3 v)=>v.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+v.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+v.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        private static string Surface(GameObject root,bool omitMonitors)
        {
            var faces=new List<string>();foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>!omitMonitors||!UnderMonitor(f.transform)))
            {
                var v=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var ids=f.sharedMesh.triangles;for(int i=0;i<ids.Length;i+=3)faces.Add(VectorKey(v[ids[i]])+";"+VectorKey(v[ids[i+1]])+";"+VectorKey(v[ids[i+2]]));
            }faces.Sort(StringComparer.Ordinal);using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("/",faces)))).Replace("-","").ToLowerInvariant();
        }
        private static void RequireHeldGeometry(GameObject original,GameObject candidate)
        {
            string a=Surface(original,true),b=Surface(candidate,true);if(a!=b||!ColliderSurfaces(original).SequenceEqual(ColliderSurfaces(candidate)))throw new InvalidDataException("A non-monitor render surface or any collider changed.");
            var old=original.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(v=>v.spotId);var current=candidate.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(v=>v.spotId);if(old.Count!=17||current.Count!=17)throw new InvalidDataException("Anchor count changed.");
            foreach(var row in old){var x=row.Value;var y=current[row.Key];if(x.transform.position!=y.transform.position||x.transform.rotation!=y.transform.rotation||x.kind!=y.kind||x.anim!=y.anim||(x.approach==null)!=(y.approach==null)||x.approach!=null&&x.approach.position!=y.approach.position)throw new InvalidDataException("Immutable seat/approach changed.");}
            var va=original.GetComponent<VenueSceneRoot>();var vb=candidate.GetComponent<VenueSceneRoot>();if(va.navigation!=vb.navigation||va.venueId!=vb.venueId||va.districtId!=vb.districtId||va.cameraBounds!=vb.cameraBounds||va.cameraDistance!=vb.cameraDistance||va.cameraPitch!=vb.cameraPitch||va.cameraYaw!=vb.cameraYaw||va.cameraFieldOfView!=vb.cameraFieldOfView||va.cameraTargetViewportY!=vb.cameraTargetViewportY||va.cameraFocusHeight!=vb.cameraFocusHeight||va.cameraLookAhead!=vb.cameraLookAhead)throw new InvalidDataException("Room/navigation/camera changed.");
            File.WriteAllText(Output+"/HeldPhysicalEquality.json",new JObject{["nonmonitor_render_sha256"]=a,["all_colliders_held"]=true,["exact_chair_desk_keyboard_held"]=true,["anchors"]=17,["navigation_asset_unchanged"]=true,["intentional_difference"]="Only existing monitor stand support vertices plus24 rear bracket boxes across8 stations"}.ToString());
        }
        private static void SaveOwned(){foreach(string guid in AssetDatabase.FindAssets("",new[]{Output})){string path=AssetDatabase.GUIDToAssetPath(guid);if(path.EndsWith(".asset")||path.EndsWith(".mat"))AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));}}
        private static void Protected(string phase,Action action)
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)throw new InvalidOperationException("Use root-owned idle Unity batch.");if(_protection!=null)throw new InvalidOperationException("Art transaction active.");_protection=new ProtectedFiles(phase);
            try{action();_protection.Verify();_protection.Record("passed_protected_physical_bytes_unchanged",null);}
            catch(Exception e){_protection.Restore();_protection.Record("failed_original_physical_bytes_restored",e.ToString());Directory.CreateDirectory(Output);File.WriteAllText(Output+"/"+(phase=="stage"?"StageReceipt.json":"BakeReceipt.json"),new JObject{["status"]="failed",["error"]=e.ToString()}.ToString());throw;}
            finally{_protection=null;}
        }
        private static string[] ColliderSurfaces(GameObject root)
        {
            string N(float x)=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            string V(Vector3 v)=>N(v.x)+","+N(v.y)+","+N(v.z);
            var rows=new List<string>();
            foreach(var collider in root.GetComponentsInChildren<Collider>(true))
            {
                string header=collider.GetType().Name+"|"+collider.enabled+"|"+collider.isTrigger+"|"+collider.gameObject.layer+"|";
                if(collider is MeshCollider mesh)
                {
                    var verts=mesh.sharedMesh.vertices.Select(mesh.transform.TransformPoint).ToArray();var indices=mesh.sharedMesh.triangles;var faces=new List<string>();
                    for(int i=0;i<indices.Length;i+=3)faces.Add(V(verts[indices[i]])+";"+V(verts[indices[i+1]])+";"+V(verts[indices[i+2]]));
                    faces.Sort(StringComparer.Ordinal);header+=mesh.convex+"|"+string.Join("/",faces);
                }
                else if(collider is BoxCollider box)
                {
                    var corners=new List<string>();foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
                        corners.Add(V(box.transform.TransformPoint(box.center+Vector3.Scale(box.size*.5f,new Vector3(x,y,z)))));
                    corners.Sort(StringComparer.Ordinal);header+=string.Join("/",corners);
                }
                else throw new InvalidDataException("Unreviewed collider kind "+collider.GetType().Name);
                using var sha=SHA256.Create();rows.Add(collider.GetType().Name+":"+BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(header))).Replace("-","").ToLowerInvariant());
            }
            rows.Sort(StringComparer.Ordinal);return rows.ToArray();
        }
        private sealed class ProtectedFiles
        {
            private readonly Dictionary<string,string> _hashes=new Dictionary<string,string>();
            private readonly Dictionary<string,string> _logical=new Dictionary<string,string>();
            private readonly Dictionary<string,string> _copies=new Dictionary<string,string>();
            private readonly string _backup;
            public ProtectedFiles(string phase)
            {
                _backup=Path.Combine(Path.GetTempPath(),"ctlife-cafe-art-"+phase+"-"+Guid.NewGuid().ToString("N"));
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
