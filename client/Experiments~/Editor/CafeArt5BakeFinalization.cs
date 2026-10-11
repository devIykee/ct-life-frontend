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
    /// <summary>Read-only geometry/lightmap finalization of the already-baked Art5 prefab. Never rebuilds or rebakes.</summary>
    public static class CafeArt5BakeFinalization
    {
        private const string Output="Assets/Art/Candidates/TerminalCafe20261007Art5";
        private const string PrefabPath=Output+"/TerminalCafeArt5.prefab";
        private const string Canonical="Assets/Art/Candidates/TerminalCafe20261007Art3/TerminalCafeArt3.prefab";
        private const string Receipt=Output+"/BakeFinalizationReceipt.json";
        private const string CandidateHash="728bc17e662a48f95cd0c1a6f6ad1d4d6bfcb968a2b0501607c544586e296673";
        private const string FailedHash="68deddcfa643adca13ee2e82ff8ce2c2a15107d4e453b117b9343000cd0e68cf";
        public static void FinalizeExistingBake()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)throw new InvalidOperationException("Coordinator-owned idle batch only.");
            if(File.Exists(Receipt))throw new IOException("Preserve existing finalization evidence; do not overwrite it.");
            if(Hash(PrefabPath)!=CandidateHash||Hash(Canonical)!="80ee1475262cbbbf69cfaaddcf5d0705d93a65f27446b6c843d2d2bfee7a2065"||Hash(Output+"/StageReceipt.json")!="41d7728bf5508a35c24b9f5c1aeb24208790f7711efbc5d6d6668e00059edbb1"||Hash(Output+"/BakeReceipt.json")!=FailedHash||Hash(Output+"/Lighting/LastBake.txt")!="42435e476cc769e62f2454272397252e2f36953a3641874988e9445a9efd0d83")throw new InvalidDataException("Exact failed-finalization inputs changed.");
            var failed=JObject.Parse(File.ReadAllText(Output+"/BakeReceipt.json"));if((string)failed["status"]!="failed"||!((string)failed["error"]).Contains("Key: Direction_monitor_triple"))throw new InvalidDataException("This helper only recovers the diagnosed duplicate-name finalization failure.");
            var before=ProtectedHashes();string transaction=Path.Combine(Path.GetTempPath(),"ctlife-cafe-art5-finalization-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(transaction);WriteProtection(transaction,"captured",before,null);bool published=false;
            try
            {
                var stage=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));if((string)stage["status"]!="staged_unbaked_not_published"||(int)stage["monitor_supports_repaired"]!=8)throw new InvalidDataException("Art5 stage geometry evidence is incomplete.");
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);var source=AssetDatabase.LoadAssetAtPath<GameObject>(Canonical);RequireHeldGeometry(source,prefab);
                var expected=((JArray)stage["monitor_geometry"]).ToArray();var monitors=FindMonitors(prefab);var sourceMonitors=FindMonitors(source);if(monitors.Length!=8||sourceMonitors.Length!=8||expected.Length!=8)throw new InvalidDataException("Expected eight actual monitor assemblies.");
                var unique=new HashSet<string>();var monitorRows=new JArray();
                foreach(var monitor in monitors)
                {
                    var position=monitor.position;var matches=expected.Where(row=>ReadV(row["position"]).Equals(position)).ToArray();if(matches.Length!=1||!unique.Add(VectorKey(position)))throw new InvalidDataException("Monitor native position must match exactly one saved station witness.");
                    var row=matches[0];string actual=Surface(monitor.gameObject,false);if(actual!=(string)row["surface_sha256"])throw new InvalidDataException("Baked monitor surface differs from its pinned stage geometry at "+position);
                    var edits=((JArray)row["mesh_edits"]).ToArray();if(edits.Length!=1||(string)edits[0]["path"]!="")throw new InvalidDataException("Expected one root support source mesh.");
                    var support=AssetDatabase.LoadAssetAtPath<Mesh>((string)edits[0]["candidate_mesh"]);var actualSupport=monitor.GetComponent<MeshFilter>().sharedMesh;if(CornerDigest(support)!=CornerDigest(actualSupport))throw new InvalidDataException("Bake changed support triangles, original normals or UV0.");
                    string texturePath=(string)row["screen_texture"];var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);if(texture==null)throw new InvalidDataException("Saved station texture missing.");
                    var old=sourceMonitors.Single(t=>t.position.Equals(position));var oldScreen=Screen(old,texture);var actualScreen=Screen(monitor,texture);
                    if(CornerDigest(oldScreen.sharedMesh)!=CornerDigest(actualScreen.sharedMesh)||oldScreen.transform.localPosition!=actualScreen.transform.localPosition||oldScreen.transform.localRotation!=actualScreen.transform.localRotation||oldScreen.transform.localScale!=actualScreen.transform.localScale)throw new InvalidDataException("Original screen position, normals or UV0 changed.");
                    var brackets=monitor.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("Rear monitor mounting bracket ",StringComparison.Ordinal)).ToArray();if(brackets.Length!=3)throw new InvalidDataException("Expected three authored rear brackets per monitor.");
                    var originalBracket=AssetDatabase.LoadAssetAtPath<Mesh>(Output+"/Meshes/RearMountingBracket.asset");foreach(var bracket in brackets)if(CornerDigest(bracket.sharedMesh)!=CornerDigest(originalBracket))throw new InvalidDataException("Bake changed a bracket's original normals/UV0/surface.");
                    monitorRows.Add(new JObject{["position"]=V(position),["saved_ambiguous_name"]=(string)row["path"],["unique_position_match"]=true,["surface_sha256"]=actual,["support_corner_sha256"]=CornerDigest(actualSupport),["screen_corner_sha256"]=CornerDigest(actualScreen.sharedMesh),["screen_texture"]=texturePath,["screen_texture_sha256"]=Hash(texturePath),["original_screen_uv0_normals_held"]=true,["rear_brackets"]=3});
                }
                var mapEvidence=ValidateInstalledMaps(prefab);
                Verify(before);if(Hash(PrefabPath)!=CandidateHash||Hash(Output+"/BakeReceipt.json")!=FailedHash)throw new InvalidDataException("Finalization altered its protected inputs.");
                var result=new JObject{["status"]="baked_not_published_requires_visual_and_device_review",["review_revision"]="art5-unique-position-read-only-finalization-v1",["prefab"]=PrefabPath,["prefab_sha256"]=CandidateHash,["art_only"]=true,["lightmaps"]=3,["bound_renderers"]=240,["existing_completed_bake_revalidated"]=true,["new_bake_executed"]=false,["failed_bake_receipt_preserved"]=Output+"/BakeReceipt.json",["failed_bake_receipt_sha256"]=FailedHash,["monitor_matches"]=monitorRows,["installed_lightmaps"]=mapEvidence,["protected_files"]=before.Count,["protected_drift"]=new JArray(),["protection_transaction"]=Path.Combine(transaction,"transaction.json"),["limitations"]="Original Art5 wrapper failed after the light bake because same-named monitor siblings were keyed by nonunique paths. This separate read-only finalization validates the already-saved geometry/maps/UV0 using unique exact positions and preserves the failed receipt. Front/rear/gameplay beauty remains required; no seated exit, authority, voice, registry or device acceptance."};
                string temp=Path.Combine(transaction,"finalization.json");File.WriteAllText(temp,result.ToString());File.Copy(temp,Receipt,false);published=true;Verify(before);WriteProtection(transaction,"passed_existing_assets_unchanged",before,null);Debug.Log("CTLIFE [cafe-art5-finalize] PASS existing bake validated; failed receipt preserved. "+Receipt);
            }
            catch(Exception e){if(published&&File.Exists(Receipt))File.Move(Receipt,Path.Combine(transaction,"failed-finalization-receipt.json"));WriteProtection(transaction,"failed_no_asset_restore_or_save_attempted",before,e.ToString());throw;}
        }
        private static MeshFilter Screen(Transform monitor,Texture texture)=>monitor.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.GetComponent<MeshRenderer>().sharedMaterials.Any(m=>m.HasProperty("_BaseMap")&&m.GetTexture("_BaseMap")==texture));
        private static JObject ValidateInstalledMaps(GameObject prefab)
        {
            var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;VenueLightmaps maps=null;var oldMaps=LightmapSettings.lightmaps??Array.Empty<LightmapData>();var oldMode=LightmapSettings.lightmapsMode;var rows=new JArray();
            try
            {
                root=Object.Instantiate(prefab);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);maps=root.GetComponent<VenueLightmaps>();if(maps==null||maps.colors.Length!=3||maps.bindings.Length!=240||maps.bindings.Select(b=>b.renderer).Distinct().Count()!=240)throw new InvalidDataException("Actual native maps/bindings do not match the completed bake.");
                foreach(var texture in maps.colors)if(texture==null||!File.Exists(AssetDatabase.GetAssetPath(texture)))throw new InvalidDataException("Missing persistent baked texture.");
                maps.Apply();
                foreach(var binding in maps.bindings)
                {
                    var renderer=binding.renderer;if(renderer==null||!renderer.transform.IsChildOf(root.transform)||binding.lightmap<0||binding.lightmap>=maps.colors.Length||renderer.lightmapIndex!=oldMaps.Length+binding.lightmap||LightmapSettings.lightmaps[renderer.lightmapIndex].lightmapColor!=maps.colors[binding.lightmap]||renderer.lightmapScaleOffset!=binding.scaleOffset)throw new InvalidDataException("Actual native lightmap installation differs.");
                }
                foreach(var texture in maps.colors){string path=AssetDatabase.GetAssetPath(texture);rows.Add(new JObject{["path"]=path,["sha256"]=Hash(path),["width"]=texture.width,["height"]=texture.height});}
            }
            finally
            {
                try{if(maps!=null)maps.Restore();}finally{EditorSceneManager.ClosePreviewScene(scene);}
                var current=LightmapSettings.lightmaps??Array.Empty<LightmapData>();if(current.Length!=oldMaps.Length||LightmapSettings.lightmapsMode!=oldMode||current.Where((m,i)=>m.lightmapColor!=oldMaps[i].lightmapColor||m.lightmapDir!=oldMaps[i].lightmapDir||m.shadowMask!=oldMaps[i].shadowMask).Any())throw new InvalidDataException("Preview lightmap state was not restored exactly.");
            }
            return new JObject{["textures"]=rows,["native_bound_renderers_verified"]=240,["previous_global_lightmaps_restored"]=true};
        }
        private static string CornerDigest(Mesh mesh)
        {
            var p=mesh.vertices;var n=mesh.normals;var uv=mesh.uv;if(n.Length!=p.Length||uv.Length!=p.Length)throw new InvalidDataException("Required source normals/UV0 absent.");var rows=new List<string>();var ids=mesh.triangles;for(int i=0;i<ids.Length;i+=3){var corners=new List<string>();for(int j=0;j<3;j++){int k=ids[i+j];corners.Add(VectorKey(p[k])+"|"+VectorKey(n[k])+"|"+uv[k].x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+uv[k].y.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}rows.Add(string.Join(";",corners));}rows.Sort(StringComparer.Ordinal);using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("/",rows)))).Replace("-","").ToLowerInvariant();
        }
        private static Transform[] FindMonitors(GameObject root)=>root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Direction_monitor_triple").OrderBy(t=>t.position.z).ThenBy(t=>t.position.x).ToArray();
        private static Vector3 ReadV(JToken t)=>new Vector3((float)t[0],(float)t[1],(float)t[2]);
        private static Dictionary<string,string> ProtectedHashes()
        {
            var logical=Directory.GetFiles("Assets/Art/Candidates","*",SearchOption.AllDirectories).Where(p=>p!=Receipt).Concat(Directory.GetFiles("ProjectSettings","*",SearchOption.AllDirectories)).Concat(new[]{"Packages/manifest.json","Packages/packages-lock.json","Assets/Resources/World/VenueContentRegistry.asset","Assets/Resources/Contract/config_snapshot.json"}).Concat(AssetDatabase.GetDependencies(new[]{PrefabPath,Canonical},true));
            var files=new HashSet<string>();foreach(var path in logical)
            {
                string physical=path;if(path.StartsWith("Packages/com.",StringComparison.Ordinal)){var info=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);if(info==null)throw new InvalidDataException("Cannot resolve installed package path.");physical=Path.Combine(info.resolvedPath,path.Substring(info.assetPath.Length+1));}
                if(File.Exists(physical))files.Add(Path.GetFullPath(physical));if(File.Exists(physical+".meta"))files.Add(Path.GetFullPath(physical+".meta"));
            }return files.ToDictionary(p=>p,Hash);
        }
        private static void Verify(Dictionary<string,string> before){var changed=before.Where(p=>!File.Exists(p.Key)||Hash(p.Key)!=p.Value).Select(p=>p.Key).ToArray();if(changed.Length!=0)throw new InvalidDataException("Protected bytes changed; no automatic overwrite: "+string.Join(", ",changed));}
        private static void WriteProtection(string folder,string status,Dictionary<string,string> before,string error)=>File.WriteAllText(Path.Combine(folder,"transaction.json"),new JObject{["status"]=status,["error"]=error,["post_editor_exit_hash_check_required"]=true,["files"]=new JArray(before.Select(p=>new JObject{["path"]=p.Key,["sha256"]=p.Value}))}.ToString());
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
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
