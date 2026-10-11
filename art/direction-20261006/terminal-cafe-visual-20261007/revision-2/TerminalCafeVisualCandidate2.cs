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
    /// <summary>Unregistered art-only clone. No seat-profile, runtime, contract or fitted surface edits.</summary>
    public static class TerminalCafeVisualCandidate2
    {
        public const string Output="Assets/Art/Candidates/TerminalCafe20261007Art2";
        public const string PrefabPath=Output+"/TerminalCafeArt2.prefab";
        private const string Baseline="Assets/Art/Candidates/TerminalCafe20261006Final";
        private const string Canonical=Baseline+"/TerminalCafeFinal.prefab";
        private const string BaselineHash="a7be079bdad8883b0fbeff0dada062d37387f71f3c4b5d4343e939478bb1fa32";
        private const string NavHash="b9033beff4817f674d75775fc9c961cd957bcca68548fa748fb33cd15c8d0318";
        private const string Snapshot="Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest="Assets/Art/environment/manifest.json";
        private static string ArtRoot=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/terminal-cafe-visual-20261007"));
        private static ProtectedFiles _protection;
        private static int _materialIndex;
        private static readonly JArray Screens=new JArray();
        private static readonly JArray Lamps=new JArray();
        private static readonly Dictionary<Material,Material> MaterialCopies=new Dictionary<Material,Material>();

        public static void BuildCandidate()
        {
            if(File.Exists(Output+"/StageReceipt.json"))throw new InvalidOperationException("Preserve this art candidate; use a new reviewed revision.");
            Protected("stage",Stage);
        }
        public static void BakeCandidate()=>Protected("bake",()=>
        {
            RequireBaseline();var stage=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));
            if((string)stage["status"]!="staged_unbaked_not_published"||(bool?)stage["exact_fitted_surfaces_held"]!=true||(string)stage["snapshot_sha256"]!=Hash(Snapshot))throw new InvalidDataException("Art stage identity differs.");
            // This separate ART prefab deliberately has no accepted seat-motion profile and cannot be promoted by this helper.
            VenueLightingBuilder.BakeVenue(PrefabPath,Output+"/Lighting");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);RequirePhysicalEquality(AssetDatabase.LoadAssetAtPath<GameObject>(Canonical),prefab);
            var maps=prefab.GetComponent<VenueLightmaps>();if(maps==null||maps.colors.Length==0||maps.bindings.Length==0)throw new InvalidDataException("Art bake has no lightmap bindings.");
            File.WriteAllText(Output+"/BakeReceipt.json",new JObject{["status"]="baked_not_published_requires_visual_and_device_review",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["lightmaps"]=maps.colors.Length,["bound_renderers"]=maps.bindings.Length,["art_only"]=true,["limitations"]="Lighting and material review only. No seat profile, exit acceptance, connected authority, registration or device approval."}.ToString());
        });
        private static void RequireBaseline()
        {
            if(Hash(Canonical)!=BaselineHash||Hash(Baseline+"/StageReceipt.json")!="beec93f7bbb06a3cbbd68a9f30cbd77596f1346e7814fc5900f21bb428e70414"||Hash(Baseline+"/Meshes/Navigation.asset")!=NavHash)throw new InvalidDataException("Held final Café geometry changed; review this art patch against the new baseline deliberately.");
        }
        private static void Stage()
        {
            RequireBaseline();foreach(var name in new[]{"Meshes","Materials","Textures"})Directory.CreateDirectory(Output+"/"+name);AssetDatabase.Refresh();
            var provenance=JObject.Parse(File.ReadAllText(Path.Combine(ArtRoot,"artwork-provenance.json")));
            foreach(var row in ((JObject)provenance["files"]).Properties())if(Hash(Path.Combine(ArtRoot,"Textures",row.Name))!=(string)row.Value)throw new InvalidDataException("Original desktop artwork hash differs: "+row.Name);
            Screens.Clear();Lamps.Clear();MaterialCopies.Clear();_materialIndex=0;
            var preview=EditorSceneManager.NewPreviewScene();
            try
            {
                var original=AssetDatabase.LoadAssetAtPath<GameObject>(Canonical);var root=Object.Instantiate(original);root.name="Terminal Cafe - art and lighting comparison only";UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,preview);
                if(root.GetComponents<Component>().Any(c=>c!=null&&c.GetType().FullName=="CTLife.World.CafeSeatMotionProfile"))throw new InvalidDataException("Art baseline unexpectedly acquired a seat-motion profile; explicit integration review required.");
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(CopyMaterial).ToArray();
                int floorTriangles=SocialFloor(root);
                var monitors=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Direction_monitor_triple").OrderBy(t=>t.position.z).ThenBy(t=>t.position.x).ToArray();
                if(monitors.Length!=8)throw new InvalidDataException("Expected eight held monitor objects.");
                for(int i=0;i<monitors.Length;i++)Triptych(monitors[i],i+1);
                var lamps=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Direction_desk_lamp").OrderBy(t=>t.position.z).ThenBy(t=>t.position.x).ToArray();
                if(lamps.Length!=8)throw new InvalidDataException("Expected eight existing desk lamp meshes.");
                foreach(var lamp in lamps)
                {
                    var lightObject=new GameObject("Authored light from existing desk lamp");lightObject.transform.SetParent(root.transform,false);lightObject.transform.position=lamp.position+new Vector3(0,.40f,-.06f);
                    Vector3 target=lamp.position+new Vector3(.42f,-.02f,-.52f);lightObject.transform.rotation=Quaternion.LookRotation(target-lightObject.transform.position,Vector3.up);
                    var light=lightObject.AddComponent<Light>();light.type=LightType.Spot;light.lightmapBakeType=LightmapBakeType.Baked;light.color=new Color(1,.84f,.64f);light.intensity=.85f;light.range=3;light.spotAngle=105;light.shadows=LightShadows.Soft;
                    Lamps.Add(new JObject{["existing_fixture"]=Vector(lamp.position),["light"]=Vector(lightObject.transform.position),["target"]=Vector(target),["baked_only"]=true});
                }
                var lighting=root.GetComponent<VenueContentLighting>();lighting.sunIntensity=.63f;lighting.sky=new Color(.45f,.52f,.56f);lighting.equator=new Color(.48f,.46f,.40f);lighting.ground=new Color(.20f,.18f,.16f);
                foreach(var light in root.GetComponentsInChildren<Light>(true))
                {
                    if(light.name=="Counter practical")light.intensity=3.1f;
                    if(light.name=="Window fill")light.intensity=2.5f;
                }
                RequirePhysicalEquality(original,root);
                string navPath=Output+"/Meshes/Navigation.asset";if(File.Exists(navPath)||!AssetDatabase.CopyAsset(Baseline+"/Meshes/Navigation.asset",navPath)||Hash(navPath)!=NavHash)throw new InvalidDataException("Could not preserve exact final navigation bytes.");
                root.GetComponent<VenueSceneRoot>().navigation=AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
                if(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)==null)throw new IOException("Could not save isolated Café art.");
                SaveCandidateAssets();
                var receipt=JObject.Parse(File.ReadAllText(Baseline+"/StageReceipt.json"));receipt["prefab"]=PrefabPath;receipt["baseline_prefab_sha256"]=BaselineHash;receipt["art_only"]=true;receipt["exact_fitted_surfaces_held"]=true;receipt["navigation_reused_sha256"]=NavHash;
                receipt["screens"]=Screens.DeepClone();receipt["desk_lights"]=Lamps.DeepClone();receipt["baked_only_lights"]=root.GetComponentsInChildren<Light>(true).Count(l=>l.lightmapBakeType==LightmapBakeType.Baked);receipt["floor_finish_changed_triangles"]=floorTriangles;
                receipt["motion_profile_status"]="absent: isolated art comparison, no runtime promotion";receipt["artwork_provenance_sha256"]=Hash(Path.Combine(ArtRoot,"artwork-provenance.json"));
                receipt["limitations"]="ART-ONLY CLONE. Same exact native geometry/colliders/anchors/camera/NavMesh as Final; monitor UVs/materials, a flush social floor finish and baked lights differ. No registered venue, fitted exit or connected behavior is claimed.";
                File.WriteAllText(Output+"/StageReceipt.json",receipt.ToString());File.Copy(Path.Combine(ArtRoot,"artwork-provenance.json"),Output+"/OriginalDesktopArtwork.json");
            }
            finally{EditorSceneManager.ClosePreviewScene(preview);MaterialCopies.Clear();}
        }
        private static Material CopyMaterial(Material original)
        {
            if(original==null)throw new InvalidDataException("Baseline material missing.");if(MaterialCopies.TryGetValue(original,out var existing))return existing;
            var copy=new Material(original){name=original.name};AssetDatabase.CreateAsset(copy,Output+"/Materials/"+(_materialIndex++).ToString("D3")+"_"+original.name+".mat");copy.name=original.name;EditorUtility.SetDirty(copy);MaterialCopies.Add(original,copy);return copy;
        }
        private static int SocialFloor(GameObject root)
        {
            var material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit")){name="SocialStoneInlay",enableInstancing=true};material.SetColor("_BaseColor",new Color(.76f,.73f,.65f));material.SetFloat("_Smoothness",.08f);
            var stone=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/environment/_shared/textures/concrete_floor_02_diff_1k.png");if(stone==null)throw new InvalidDataException("Existing licensed floor texture missing.");material.SetTexture("_BaseMap",stone);AssetDatabase.CreateAsset(material,Output+"/Materials/SocialStoneInlay.mat");
            int count=0;float area=0;int meshIndex=0;var selection=new JArray();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("CafeFloor",StringComparison.Ordinal)))
            {
                var currentRenderer=filter.GetComponent<MeshRenderer>();
                string texturePath=AssetDatabase.GetAssetPath(currentRenderer.sharedMaterial.GetTexture("_BaseMap"));
                // Native failed Art evidence identified two distinct surfaces: visible wood quad atY≈0 and slab top atY−.001.
                // Select the exact textured finish, not every horizontal triangle within a broad height tolerance.
                if(texturePath!="Assets/Art/environment/_shared/textures/wood_floor_diff_1k.png")
                {selection.Add(new JObject{["mesh"]=filter.name,["texture"]=texturePath,["selected"]=false,["reason"]="underlying slab material retained"});continue;}
                float meshArea=0;
                var source=filter.sharedMesh;if(source.subMeshCount!=1)throw new InvalidDataException("Held combined floor material grouping changed.");var vertices=source.vertices.Select(filter.transform.TransformPoint).ToArray();var kept=new List<int>();var inlay=new List<int>();var indices=source.triangles;
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];var cross=Vector3.Cross(b-a,c-a);var center=(a+b+c)/3;
                    bool inside=Mathf.Abs(center.x)<8&&center.z>-5&&center.z<-1&&Mathf.Abs(center.y)<.005f&&Mathf.Abs(cross.normalized.y)>.99f;
                    var to=inside?inlay:kept;to.Add(indices[i]);to.Add(indices[i+1]);to.Add(indices[i+2]);if(inside){count++;area+=cross.magnitude*.5f;meshArea+=cross.magnitude*.5f;}
                }
                selection.Add(new JObject{["mesh"]=filter.name,["texture"]=texturePath,["selected"]=true,["triangles"]=inlay.Count/3,["area_m2"]=meshArea,["surface_y_min"]=inlay.Count>0?inlay.Min(v=>vertices[v].y):0,["surface_y_max"]=inlay.Count>0?inlay.Max(v=>vertices[v].y):0});
                if(inlay.Count==0)continue;var mesh=Object.Instantiate(source);mesh.name="Exact floor with material-only social inlay";mesh.subMeshCount=2;mesh.SetTriangles(kept,0);mesh.SetTriangles(inlay,1);AssetDatabase.CreateAsset(mesh,Output+"/Meshes/SocialFloor"+(meshIndex++)+".asset");filter.sharedMesh=mesh;
                var renderer=filter.GetComponent<MeshRenderer>();renderer.sharedMaterials=new[]{renderer.sharedMaterial,material};
            }
            File.WriteAllText(Output+"/FloorFinishSelection.json",new JObject{["rows"]=selection,["selected_area_m2"]=area,["expected_area_m2"]=64,["geometry_removed_or_added"]=false}.ToString());
            if(count!=32||Mathf.Abs(area-64f)>.01f)throw new InvalidDataException("Material inlay must cover exact16×4m existing top surface; area="+area);
            return count;
        }
        private static void Triptych(Transform monitor,int station)
        {
            string filename="Desktop"+station.ToString("D2")+".png",path=Output+"/Textures/"+filename;File.Copy(Path.Combine(ArtRoot,"Textures",filename),path);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;var android=importer.GetPlatformTextureSettings("Android");android.overridden=true;android.maxTextureSize=2048;android.format=TextureImporterFormat.ASTC_8x8;importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);var panels=new HashSet<int>();int screenTriangles=0,meshIndex=0;
            foreach(var filter in monitor.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();var materials=renderer.sharedMaterials;var mesh=Object.Instantiate(filter.sharedMesh);var uv=mesh.uv;var vertices=mesh.vertices;var assigned=new Dictionary<int,int>();bool changed=false;
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    if(!materials[sub].name.StartsWith("Screen_",StringComparison.Ordinal))continue;
                    var material=new Material(materials[sub]){name="OriginalDesktop"+station.ToString("D2")};material.SetTexture("_BaseMap",texture);AssetDatabase.CreateAsset(material,Output+"/Materials/Desktop"+station.ToString("D2")+"_"+meshIndex+"_"+sub+".mat");materials[sub]=material;
                    var triangles=mesh.GetTriangles(sub);
                    for(int k=0;k<triangles.Length;k+=3)
                    {
                        float x=0;for(int j=0;j<3;j++)x+=monitor.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[k+j]])).x/3f;
                        int panel=x<-.25f?0:x>.25f?2:1;panels.Add(panel);screenTriangles++;
                        for(int j=0;j<3;j++)
                        {
                            int v=triangles[k+j];if(assigned.TryGetValue(v,out int prior)){if(prior!=panel)throw new InvalidDataException("Screen vertex shared across atlas panels.");continue;}
                            if(uv[v].x<-.0001f||uv[v].x>1.0001f||uv[v].y<-.0001f||uv[v].y>1.0001f)throw new InvalidDataException("Source panel UVs no longer0..1.");assigned.Add(v,panel);uv[v]=new Vector2((uv[v].x+panel)/3f,uv[v].y);
                        }
                    }
                    changed=true;
                }
                if(changed){mesh.uv=uv;AssetDatabase.CreateAsset(mesh,Output+"/Meshes/Monitor"+station.ToString("D2")+"_"+(meshIndex++)+".asset");filter.sharedMesh=mesh;renderer.sharedMaterials=materials;}else Object.DestroyImmediate(mesh);
            }
            if(panels.Count!=3||screenTriangles!=6)throw new InvalidDataException("Expected exact three existing quads, six screen triangles; no screen geometry was added.");
            Screens.Add(new JObject{["station"]=station,["monitor_position"]=Vector(monitor.position),["texture_sha256"]=Hash(path),["panels"]=3,["unchanged_screen_triangles"]=screenTriangles,["meaning"]="original static desktop artwork; no live service or market data"});
        }
        private static JArray Vector(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static string N(float v)=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        private static string V(Vector3 v)=>N(v.x)+","+N(v.y)+","+N(v.z);
        private static string Digest(string value){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        private static string RenderSurface(GameObject root)
        {
            var faces=new List<string>();foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var vertices=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();var indices=filter.sharedMesh.triangles;
                for(int i=0;i<indices.Length;i+=3)faces.Add(V(vertices[indices[i]])+";"+V(vertices[indices[i+1]])+";"+V(vertices[indices[i+2]]));
            }
            faces.Sort(StringComparer.Ordinal);return Digest(string.Join("/",faces));
        }
        private static void RequirePhysicalEquality(GameObject before,GameObject after)
        {
            var a=RenderSurface(before);var b=RenderSurface(after);var ca=ColliderSurfaces(before);var cb=ColliderSurfaces(after);
            var old=before.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(s=>s.spotId);var current=after.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(s=>s.spotId);
            if(a!=b||!ca.SequenceEqual(cb)||old.Count!=17||current.Count!=17)throw new InvalidDataException("Art clone changed exact rendered or collided physical surfaces.");
            foreach(var pair in old)
            {
                if(!current.TryGetValue(pair.Key,out var spot)||spot.kind!=pair.Value.kind||spot.anim!=pair.Value.anim||spot.transform.position!=pair.Value.transform.position||spot.transform.rotation!=pair.Value.transform.rotation||(spot.approach==null)!=(pair.Value.approach==null)||spot.approach!=null&&spot.approach.position!=pair.Value.approach.position)throw new InvalidDataException("Art clone changed held anchor/approach.");
            }
            File.WriteAllText(Output+"/ExactPhysicalEquality.json",new JObject{["render_surface_sha256"]=a,["collider_signatures"]=new JArray(ca),["anchors"]=17,["geometry_changed"]=false,["note"]="Native world-space triangle identities; UVs/material grouping and lighting deliberately excluded."}.ToString());
        }
        private static void SaveCandidateAssets()
        {
            foreach(string guid in AssetDatabase.FindAssets("",new[]{Output}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);if(path.EndsWith(".mat",StringComparison.Ordinal)||path.EndsWith(".asset",StringComparison.Ordinal))AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));
            }
        }
        private static void Protected(string phase,Action action)
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)throw new InvalidOperationException("Use coordinator-owned idle Unity batch.");
            if(_protection!=null)throw new InvalidOperationException("Art transaction active.");_protection=new ProtectedFiles(phase);
            try{action();_protection.Verify();_protection.Record("passed_protected_physical_bytes_unchanged",null);}
            catch(Exception error){_protection.Restore();_protection.Record("failed_original_physical_bytes_restored",error.ToString());Directory.CreateDirectory(Output);File.WriteAllText(Output+"/"+(phase=="stage"?"StageReceipt.json":"BakeReceipt.json"),new JObject{["status"]="failed",["error"]=error.ToString()}.ToString());throw;}
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
