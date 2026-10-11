using System;
using System.Collections.Generic;
using System.Globalization;
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
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Bounded dressing of the inspected private Home A. No scene registration or contact assignment.</summary>
    public static class ParentsBasementCandidateB2
    {
        public const string Output="Assets/Art/Candidates/ParentsBasement20261007B2";
        public const string PrefabPath=Output+"/ParentsBasementB2.prefab";
        private const string Baseline="Assets/Art/Candidates/ParentsBasement20261007A/ParentsBasementA.prefab";
        private const string BaselineHash="81d42473f3a3f6eae82a38b17e7791e4a332190880a9b364ec32fb121a165357";
        private const string BaselineBake="Assets/Art/Candidates/ParentsBasement20261007A/BakeReceipt.json";
        private const string BaselineBakeHash="06e3e20ffe9c5380926907f8d312a7d4d5fb04c57b6445e0341071ecd91098c1";
        private const string Decoration="Home B / added domestic detail";
        private static string StageFolder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/home-20261007/revision-b"));
        private static readonly JArray Placements=new JArray(),Planes=new JArray(),PlaneClassifications=new JArray();
        private static Transform _detail;
        private static JObject _manifest,_art;
        private static Material _display,_print;
        private static readonly Dictionary<string,Material> SourceMaterials=new Dictionary<string,Material>();
        private static int _serial;
        private static string _frozenDetails;

        public static void BuildCandidate()
        {
            if(Directory.Exists(Output))throw new InvalidOperationException("Preserve earlier output, including failure; use a new reviewed revision.");
            Protected("stage",Stage);
        }
        public static void BakeCandidate()=>Protected("bake",()=>
        {
            RequireSources();var receipt=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));
            if((string)receipt["status"]!="staged_unbaked_private_home_not_published"||Hash(PrefabPath)!=(string)receipt["prefab_sha256"])throw new InvalidDataException("Only the exact staged B prefab can be baked.");
            var before=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);string details=Surface(before,true);
            VenueLightingBuilder.BakeVenue(PrefabPath,Output+"/Lighting");
            var after=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);var baseline=AssetDatabase.LoadAssetAtPath<GameObject>(Baseline);
            RequireHeld(baseline,after);if(Surface(after,true)!=details)throw new InvalidDataException("Home B bake changed added detail surfaces.");
            var maps=after.GetComponent<VenueLightmaps>();
            if(maps==null||maps.colors.Length==0||maps.bindings.Length==0||maps.bindings.Select(b=>b.renderer).Distinct().Count()!=maps.bindings.Length||maps.colors.Any(c=>c==null))throw new InvalidDataException("Missing or repeated Home B lightmap bindings.");
            File.WriteAllText(Output+"/BakeReceipt.json",new JObject{["status"]="baked_private_home_art_not_registered_or_contact_accepted",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["baseline_prefab_sha256"]=BaselineHash,["lightmaps"]=maps.colors.Length,["bound_renderers"]=maps.bindings.Length,["added_surface_sha256"]=details,["limitations"]="Fresh native beauty review, actual Home lifecycle, bed/sofa/desk contact and device acceptance remain separate. Original A geometry, anchors, standing approaches, camera, navigation and lights are held."}.ToString());
        });
        private static void RequireSources()
        {
            if(Hash(Baseline)!=BaselineHash||Hash(BaselineBake)!=BaselineBakeHash)throw new InvalidDataException("Inspected Home A prefab or bake receipt changed.");
            var pins=JObject.Parse(File.ReadAllText(Path.Combine(StageFolder,"source-manifest.json")));
            foreach(var pin in ((JObject)pins["source_files"]).Properties())if(Hash(pin.Name)!=(string)pin.Value)throw new InvalidDataException("Pinned Home B source changed: "+pin.Name);
            _art=JObject.Parse(File.ReadAllText(Path.Combine(StageFolder,"artwork-provenance.json")));
            foreach(var pin in ((JObject)_art["files"]).Properties())if(Hash(Path.Combine(StageFolder,"Textures",pin.Name))!=(string)pin.Value)throw new InvalidDataException("Original Home artwork changed: "+pin.Name);
        }
        private static void Stage()
        {
            RequireSources();foreach(string folder in new[]{"Meshes","Materials","Textures"})Directory.CreateDirectory(Output+"/"+folder);
            AssetDatabase.Refresh();_manifest=JObject.Parse(File.ReadAllText("Assets/Art/environment/manifest.json"));Placements.Clear();Planes.Clear();PlaneClassifications.Clear();_serial=0;
            var baseline=AssetDatabase.LoadAssetAtPath<GameObject>(Baseline);var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var room=Object.Instantiate(baseline);room.name="Parent's Basement B / original domestic detail";UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(room,scene);
                foreach(var old in room.GetComponents<VenueLightmaps>())Object.DestroyImmediate(old);
                foreach(var r in room.GetComponentsInChildren<Renderer>(true)){r.lightmapIndex=-1;r.lightmapScaleOffset=new Vector4(1,1,0,0);}
                _detail=new GameObject(Decoration).transform;_detail.SetParent(room.transform,false);
                SourceMaterials.Clear();foreach(var material in room.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct())SourceMaterials[MaterialKey(material.name)]=material;
                var atlas=ImportTexture("HomeDetailAtlas.png",2048);var weave=ImportTexture("HomeWeave.png",512);
                _display=Material("Original Home screens","Universal Render Pipeline/Unlit",atlas);
                _print=Material("Original Home paper","Universal Render Pipeline/Simple Lit",atlas);
                var woven=Material("Quiet woven floor","Universal Render Pipeline/Simple Lit",weave);
                var floor=PartitionFloor(room,woven);
                AddShelfContents(room);
                var desk=Find(room,"Home_055_home_desk");var db=BoundsOf(desk);
                AddOn("desk_lamp",new Vector3(4.55f,db.max.y,3.83f),0,1);
                FlatArt("Personal notebook",new Vector3(3.50f,db.max.y+.0006f,3.42f),Vector3.right,Vector3.forward,new Vector2(.20f,.25f),"notebook",_print,Vector3.up);
                var coffeeCabinet=Find(room,"Home_067_home_kitchen_cabinet1");var cb=BoundsOf(coffeeCabinet);
                AddOn("coffee_machine",new Vector3(cb.center.x+.04f,cb.max.y,cb.center.z),90,.85f);
                var mugCabinet=Find(room,"Home_066_home_kitchen_cabinet1");var mb=BoundsOf(mugCabinet);
                AddOn("mug",new Vector3(mb.center.x+.13f,mb.max.y,mb.center.z+.08f),30,1);
                AddOn("mug",new Vector3(mb.center.x-.06f,mb.max.y,mb.center.z-.10f),-20,1);
                var plantCabinet=Find(room,"Home_068_home_kitchen_cabinet1");var pb=BoundsOf(plantCabinet);
                AddOn("plant_small",new Vector3(pb.center.x,pb.max.y,pb.center.z),0,.80f);
                DisplayOn(Find(room,"Home_061_home_tv_modern"),"Television / original offline artwork","tv",Vector3.back,.035f,.15f,2.0f);
                DisplayOn(Find(room,"Home_057_laptop"),"Laptop / original personal notes","laptop",Vector3.back,.008f,.009f,.14f);
                AddWallPrint("Grass print",-3.45f,1.55f,"print_grass");
                AddWallPrint("Tabs print",2.55f,1.55f,"print_tabs");
                var fridge=BoundsOf(Find(room,"Home_064_home_kitchen_fridge"));
                FlatArt("Mum's fridge note",new Vector3(fridge.max.x+.0006f,1.28f,fridge.center.z-.08f),Vector3.back,Vector3.up,new Vector2(.16f,.20f),"fridge_note",_print,Vector3.right);
                if(_detail.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidDataException("Home B decoration must not add hidden movement obstacles.");
                RequireHeld(baseline,room);
                int newTriangles=_detail.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh.triangles.Length/3);
                int newRenderers=_detail.GetComponentsInChildren<MeshRenderer>(true).Length;
                int newMaterials=Directory.GetFiles(Output+"/Materials","*.mat").Length;
                if(newTriangles>3000||newRenderers>24||newMaterials>6)throw new InvalidDataException("Home B detail budget exceeded: "+newTriangles+" triangles / "+newRenderers+" renderers / "+newMaterials+" materials.");
                var venue=room.GetComponent<VenueSceneRoot>();var snapshot=JObject.Parse(File.ReadAllText("Assets/Resources/Contract/config_snapshot.json"));var spots=Registries.Parse(snapshot["ids"],snapshot["config"]).SpotsFor("home");venue.Init(spots);
                var paths=CheckNavigation(venue,spots);
                _frozenDetails=Surface(room,true);
                if(PrefabUtility.SaveAsPrefabAsset(room,PrefabPath)==null)throw new IOException("Home B save failed.");SaveOwned();
                File.WriteAllText(Output+"/StageReceipt.json",new JObject{["status"]="staged_unbaked_private_home_not_published",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["baseline_prefab_sha256"]=BaselineHash,["home_id"]="parents_basement",["logical_spot_group"]="home",["positioned_anchors"]=6,["authored_approaches"]=4,["navigation"]=paths,["placements"]=Placements.DeepClone(),["display_native_planes"]=Planes.DeepClone(),["floor_finish"]=floor,["added_triangles"]=newTriangles,["added_mesh_renderers"]=newRenderers,["new_prebake_materials"]=newMaterials,["budget_triangles"]=3000,["budget_renderers"]=24,["budget_materials"]=6,["added_surface_sha256"]=_frozenDetails,["added_colliders"]=0,["lights_held"]=5,["limitations"]="Private Home dressing only. Source bed/sofa/desk contact remains unaccepted. New art is static original decoration, no live data or needs/claim effect. No registry, backend, camera, actor motion, wire anchors, navigation or collider change. Native material/readability/full-room and device review pending."}.ToString());
            }
            finally{_detail=null;SourceMaterials.Clear();_print=null;_display=null;EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static JObject PartitionFloor(GameObject root,Material weave)
        {
            double area=0;int count=0;var rows=new JArray();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("HomeFloor_",StringComparison.Ordinal)))
            {
                var source=filter.sharedMesh;var vertices=source.vertices;var selected=new List<int[]>();var kept=new List<int[]>();
                for(int sub=0;sub<source.subMeshCount;sub++)
                {
                    var stay=new List<int>();var take=new List<int>();var indices=source.GetTriangles(sub);
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        var a=filter.transform.TransformPoint(vertices[indices[i]]);var b=filter.transform.TransformPoint(vertices[indices[i+1]]);var c=filter.transform.TransformPoint(vertices[indices[i+2]]);var center=(a+b+c)/3;
                        bool top=Mathf.Abs(a.y)<.00001f&&Mathf.Abs(b.y)<.00001f&&Mathf.Abs(c.y)<.00001f;
                        bool inside=center.x> -2&&center.x<2&&center.z> -3&&center.z<1;
                        if(top&&inside)
                        {
                            if(new[]{a,b,c}.Any(v=>v.x< -2.00001f||v.x>2.00001f||v.z< -3.00001f||v.z>1.00001f))throw new InvalidDataException("Woven floor crosses a source triangle; do not silently split physical geometry.");
                            take.AddRange(new[]{indices[i],indices[i+1],indices[i+2]});area+=Vector3.Cross(b-a,c-a).magnitude*.5;count++;
                        }
                        else stay.AddRange(new[]{indices[i],indices[i+1],indices[i+2]});
                    }
                    kept.Add(stay.ToArray());selected.Add(take.ToArray());
                }
                if(selected.Sum(v=>v.Length)==0)continue;
                // Duplicate indexed vertices only for a new UV seam. Every old triangle keeps exact positions/normals.
                var mesh=new Mesh{name=source.name+"_woven_finish",indexFormat=IndexFormat.UInt32};var vs=vertices.ToList();var ns=source.normals.ToList();var uv=source.uv.ToList();var uv2=source.uv2.ToList();var insert=new List<int>();
                if(ns.Count!=vertices.Length||uv.Count!=vertices.Length||uv2.Count!=vertices.Length)throw new InvalidDataException("Home A floor needs complete native normal/UV channels.");
                foreach(int idx in selected.SelectMany(v=>v))
                {var p=filter.transform.TransformPoint(vertices[idx]);insert.Add(vs.Count);vs.Add(vertices[idx]);ns.Add(source.normals[idx]);uv.Add(new Vector2((p.x+2)/4,(p.z+3)/4));uv2.Add(source.uv2[idx]);}
                mesh.SetVertices(vs);mesh.SetNormals(ns);mesh.SetUVs(0,uv);mesh.SetUVs(1,uv2);mesh.subMeshCount=source.subMeshCount+1;
                for(int i=0;i<kept.Count;i++)mesh.SetTriangles(kept[i],i);mesh.SetTriangles(insert,kept.Count);mesh.RecalculateBounds();
                filter.sharedMesh=Save(mesh,Output+"/Meshes/"+filter.name+"_woven.asset");var renderer=filter.GetComponent<MeshRenderer>();renderer.sharedMaterials=renderer.sharedMaterials.Concat(new[]{weave}).ToArray();
                rows.Add(new JObject{["mesh"]=filter.name,["triangles"]=insert.Count/3,["position_changes"]=0});
            }
            if(count!=8||Math.Abs(area-16)>.00001)throw new InvalidDataException("Woven finish must cover eight exact existing top triangles /16 m², observed "+count+" / "+area);
            return new JObject{["area_m2"]=area,["triangles"]=count,["bounds_xz"]=new JArray(-2,-3,2,1),["top_y"]=0,["new_surface_geometry"]=false,["meshes"]=rows};
        }
        private static void AddShelfContents(GameObject room)
        {
            var shelf=Find(room,"Home_052_home_bookshelf");var bounds=BoundsOf(shelf);var levels=new Dictionary<int,double>();
            foreach(var f in shelf.GetComponentsInChildren<MeshFilter>(true).Where(f=>Lod(f.name)==0))
            {
                var vs=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var ids=f.sharedMesh.triangles;
                for(int i=0;i<ids.Length;i+=3){var a=vs[ids[i]];var b=vs[ids[i+1]];var c=vs[ids[i+2]];var cross=Vector3.Cross(b-a,c-a);float y=(a.y+b.y+c.y)/3;
                    if(cross.sqrMagnitude<1e-12f||cross.normalized.y<.90f||y<bounds.min.y+.15f||y>bounds.max.y-.24f)continue;
                    int key=Mathf.RoundToInt(y*10000);levels[key]=levels.TryGetValue(key,out var area)?area+cross.magnitude*.5:cross.magnitude*.5;}
            }
            var use=levels.Where(p=>p.Value>.10).Select(p=>p.Key/10000f).OrderBy(y=>y).ToArray();
            if(use.Length<5)throw new InvalidDataException("Native bookcase needs five native interior shelf levels; found "+string.Join(",",use));
            foreach(int shelfIndex in new[]{0,2,4})
            {
                float y=use[shelfIndex];var item=AddOn("home_shelf_b_large_decorated",new Vector3(bounds.center.x+.025f,y+.001f,bounds.center.z),90,.45f);var b=BoundsOf(item);
                float ceiling=shelfIndex+1<use.Length?use[shelfIndex+1]-.020f:bounds.max.y-.020f;
                if(b.max.y>ceiling)throw new InvalidDataException("Decorative books cross the next physical shelf.");
                if(b.min.x<bounds.min.x-.002f||b.max.x>bounds.max.x+.002f||b.min.z<bounds.min.z-.002f||b.max.z>bounds.max.z+.002f||b.max.y>bounds.max.y)throw new InvalidDataException("Native decorative shelf exceeds bookcase interior envelope.");
            }
        }
        private static void AddWallPrint(string name,float x,float y,string region)
        {
            var frame=AddOn("home_pictureframe_large_a",new Vector3(x,y,4.975f),0,.62f);frame.name=name+" / source frame";
            DisplayOn(frame,name+" / original print",region,Vector3.back,.022f,.03f,.50f,_print);
        }
        private static GameObject AddOn(string id,Vector3 bottom,float yaw,float scale)
        {
            var row=_manifest[id]??throw new InvalidDataException("Missing licensed prop "+id);string path="Assets/Art/"+(string)row["fbx"];
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(source==null)throw new FileNotFoundException("Licensed prop missing",path);
            var go=Object.Instantiate(source,_detail);go.name="HomeB_"+(++_serial).ToString("D2")+"_"+id;go.transform.localPosition=bottom;go.transform.localRotation=Quaternion.Euler(0,yaw,0);go.transform.localScale=Vector3.one*scale;
            foreach(var c in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);foreach(var l in go.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(l);
            foreach(var filter in go.GetComponentsInChildren<MeshFilter>(true).Where(f=>Lod(f.name)>0).ToArray())Object.DestroyImmediate(filter.gameObject);
            foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>
                {string key=MaterialKey(m!=null?m.name:"M_ctl_palette");if(!((JArray)row["materials"]).Values<string>().Contains(key)||!SourceMaterials.TryGetValue(key,out var material))throw new InvalidDataException("No inspected Home A source material for "+id+": "+key);return material;}).ToArray();Configure(renderer);
            }
            var bounds=BoundsOf(go);go.transform.position+=Vector3.up*(bottom.y-bounds.min.y);
            Placements.Add(new JObject{["id"]=id,["source"]=path,["source_sha256"]=Hash(path),["licensed_family"]=(string)row["source"],["bottom_position"]=V(bottom),["yaw"]=yaw,["scale"]=scale,["actual_native_triangles"]=go.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh.triangles.Length/3),["colliders"]=0});
            return go;
        }
        private sealed class Plane
        {
            public Vector3 normal;public float distance;public float area;public readonly List<Vector3> points=new List<Vector3>();
        }
        private static void DisplayOn(GameObject source,string name,string region,Vector3 facing,float inset,float minArea,float maxArea,Material material=null)
        {
            var groups=new List<Plane>();
            foreach(var f in source.GetComponentsInChildren<MeshFilter>(true).Where(f=>Lod(f.name)==0))
            {
                var verts=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var ids=f.sharedMesh.triangles;
                for(int i=0;i<ids.Length;i+=3)
                {
                    var a=verts[ids[i]];var b=verts[ids[i+1]];var c=verts[ids[i+2]];var cross=Vector3.Cross(b-a,c-a);float area=cross.magnitude*.5f;if(area<.000001f)continue;var normal=cross.normalized;
                    if(Vector3.Dot(normal,facing)<.60f||Mathf.Abs(normal.y)>.75f)continue;float d=Vector3.Dot(normal,a);
                    var group=groups.FirstOrDefault(g=>Vector3.Dot(g.normal,normal)>.99999f&&new[]{a,b,c}.All(p=>Mathf.Abs(Vector3.Dot(g.normal,p-g.points[0]))<.00002f));
                    if(group==null){group=new Plane{normal=normal,distance=d};groups.Add(group);}group.area+=area;group.points.AddRange(new[]{a,b,c});
                }
            }
            var ordered=groups.Where(g=>g.area>=minArea&&g.area<=maxArea).OrderByDescending(g=>g.area).ToArray();
            if(ordered.Length==0)throw new InvalidDataException("No measured display plane for "+name+"; native groups "+string.Join(",",groups.Select(g=>g.area.ToString("R",CultureInfo.InvariantCulture))));
            var plane=ordered[0];var right=Vector3.ProjectOnPlane(Vector3.right,plane.normal).normalized;if(right.sqrMagnitude<.5f)throw new InvalidDataException("Unexpected display horizontal basis.");var up=Vector3.ProjectOnPlane(Vector3.up,plane.normal).normalized;up=(up-right*Vector3.Dot(up,right)).normalized;if(up.y<0)up=-up;
            float u0=plane.points.Min(v=>Vector3.Dot(v,right)),u1=plane.points.Max(v=>Vector3.Dot(v,right)),v0=plane.points.Min(v=>Vector3.Dot(v,up)),v1=plane.points.Max(v=>Vector3.Dot(v,up));
            float rectArea=(u1-u0)*(v1-v0);
            PlaneClassifications.Add(new JObject{["name"]=name,["normal"]=V(plane.normal),["measured_area_m2"]=plane.area,["projected_rectangle_area_m2"]=rectArea,["rectangle_error_fraction"]=Mathf.Abs(rectArea-plane.area)/rectArea,["coplanarity_tolerance_m"]=.00002f,["max_vertex_plane_residual_m"]=plane.points.Max(p=>Mathf.Abs(Vector3.Dot(plane.normal,p-plane.points[0]))),["all_candidate_areas"]=new JArray(ordered.Select(p=>p.area)),["method"]="Relative vertex-to-plane residual; no world-origin plane-distance subtraction"});
            File.WriteAllText(Output+"/DisplayPlaneClassification.json",PlaneClassifications.ToString());
            if(Mathf.Abs(rectArea-plane.area)>rectArea*.035f)throw new InvalidDataException("Display plane is not a filled source rectangle: "+name);
            var center=right*(u0+u1)*.5f+up*(v0+v1)*.5f+plane.normal*(plane.distance+.0006f);var size=new Vector2(u1-u0-2*inset,v1-v0-2*inset);
            if(size.x<.05f||size.y<.04f)throw new InvalidDataException("Inset display collapsed: "+name);
            // Artwork U follows world-right and V follows world-up; winding is selected independently for this measured face.
            FlatArt(name,center,right,up,size,region,material??_display,plane.normal);
            Planes.Add(new JObject{["source"]=source.name,["name"]=name,["source_plane_normal"]=V(plane.normal),["source_plane_area"]=plane.area,["center"]=V(center),["size"]=new JArray(size.x,size.y),["surface_offset_m"]=.0006f,["chosen_from_candidates"]=ordered.Length,["visual_orientation_acceptance"]=false});
        }
        private static void FlatArt(string name,Vector3 center,Vector3 right,Vector3 up,Vector2 size,string region,Material material,Vector3 facing)
        {
            var go=new GameObject(name);go.transform.SetParent(_detail,false);var x=right.normalized*size.x*.5f;var y=up.normalized*size.y*.5f;
            var r=(JArray)_art["top_origin_pixel_regions"][region];float u0=(float)r[0]/2048,u1=(float)r[2]/2048,v0=1-(float)r[3]/1024,v1=1-(float)r[1]/1024;
            var mesh=new Mesh{name=name,vertices=new[]{center-x-y,center+x-y,center+x+y,center-x+y},uv=new[]{new Vector2(u0,v0),new Vector2(u1,v0),new Vector2(u1,v1),new Vector2(u0,v1)},triangles=Vector3.Dot(Vector3.Cross(right,up),facing)>0?new[]{0,1,2,0,2,3}:new[]{0,2,1,0,3,2}};mesh.RecalculateNormals();mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh=Save(mesh,Output+"/Meshes/Artwork_"+(++_serial).ToString("D2")+".asset");var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;Configure(renderer);
        }
        private static Texture2D ImportTexture(string name,int size)
        {
            string target=Output+"/Textures/"+name;File.Copy(Path.Combine(StageFolder,"Textures",name),target,false);AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(target);importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.mipmapEnabled=true;importer.alphaSource=TextureImporterAlphaSource.None;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=size;importer.textureCompression=TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=size,format=TextureImporterFormat.ASTC_8x8});importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(target);
        }
        private static Material Material(string name,string shaderName,Texture2D texture)
        {var shader=Shader.Find(shaderName);if(shader==null)throw new InvalidDataException("Missing shader "+shaderName);var m=new Material(shader){name=name,enableInstancing=true};m.SetColor("_BaseColor",Color.white);m.SetTexture("_BaseMap",texture);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.10f);return Save(m,Output+"/Materials/"+name.Replace(' ','_')+".mat");}
        private static void Configure(MeshRenderer r){r.gameObject.isStatic=true;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;}
        private static GameObject Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name).gameObject;
        private static Bounds BoundsOf(GameObject item){var rs=item.GetComponentsInChildren<MeshRenderer>(true).Where(r=>Lod(r.name)==0).ToArray();if(rs.Length==0)throw new InvalidDataException("No native prop surfaces "+item.name);var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        private static string MaterialKey(string name){name=name.Replace(" (Instance)","");if(name.StartsWith("Baked_",StringComparison.Ordinal))name=name.Substring(6);if(name.StartsWith("Display_",StringComparison.Ordinal))name=name.Substring(8);int i=name.LastIndexOf('.');return i>=0&&int.TryParse(name.Substring(i+1),out _)?name.Substring(0,i):name;}
        private static int Lod(string name){int i=name.LastIndexOf("_LOD",StringComparison.Ordinal);return i>=0&&i+4<name.Length&&char.IsDigit(name[i+4])?name[i+4]-'0':0;}
        private static bool IsDetail(Transform t){for(var p=t;p!=null;p=p.parent)if(p.name==Decoration)return true;return false;}
        private static string Surface(GameObject root,bool detail)
        {
            var faces=new List<string>();foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>IsDetail(f.transform)==detail)){var vs=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var ids=f.sharedMesh.triangles;for(int i=0;i<ids.Length;i+=3)faces.Add(K(vs[ids[i]])+";"+K(vs[ids[i+1]])+";"+K(vs[ids[i+2]]));}faces.Sort(StringComparer.Ordinal);return TextHash(string.Join("/",faces));
        }
        private static void RequireHeld(GameObject original,GameObject candidate)
        {
            string a=Surface(original,false),b=Surface(candidate,false);if(a!=b||!Colliders(original).SequenceEqual(Colliders(candidate)))throw new InvalidDataException("Home A render positions or physical colliders changed.");
            var old=original.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(s=>s.spotId);var current=candidate.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(s=>s.spotId);if(old.Count!=6||current.Count!=6)throw new InvalidDataException("Home anchor count changed.");
            foreach(var p in old){var x=p.Value;var y=current[p.Key];if(x.transform.position!=y.transform.position||x.transform.rotation!=y.transform.rotation||x.kind!=y.kind||x.anim!=y.anim||(x.approach==null)!=(y.approach==null)||x.approach!=null&&x.approach.position!=y.approach.position)throw new InvalidDataException("Home anchor/approach changed: "+p.Key);}
            var va=original.GetComponent<VenueSceneRoot>();var vb=candidate.GetComponent<VenueSceneRoot>();if(va.navigation!=vb.navigation||va.venueId!=vb.venueId||va.districtId!=vb.districtId||va.cameraBounds!=vb.cameraBounds||va.cameraDistance!=vb.cameraDistance||va.cameraPitch!=vb.cameraPitch||va.cameraYaw!=vb.cameraYaw||va.cameraFieldOfView!=vb.cameraFieldOfView||va.cameraTargetViewportY!=vb.cameraTargetViewportY||va.cameraFocusHeight!=vb.cameraFocusHeight||va.cameraLookAhead!=vb.cameraLookAhead)throw new InvalidDataException("Home room/navigation/camera changed.");
            var oldLights=original.GetComponentsInChildren<Light>(true);var newLights=candidate.GetComponentsInChildren<Light>(true);if(oldLights.Length!=5||newLights.Length!=5)throw new InvalidDataException("Home light count changed.");
            foreach(var light in oldLights){var l=newLights.Single(n=>n.name==light.name);if(light.transform.position!=l.transform.position||light.transform.rotation!=l.transform.rotation||light.intensity!=l.intensity||light.range!=l.range||light.color!=l.color||light.type!=l.type||light.lightmapBakeType!=l.lightmapBakeType)throw new InvalidDataException("Existing Home light changed.");}
            if(candidate.GetComponent<CafeSeatMotionProfile>()!=null)throw new InvalidDataException("Home dressing must not attach an unaccepted seat profile.");
            File.WriteAllText(Output+"/HeldPhysicalEquality.json",new JObject{["all_original_render_surface_sha256"]=a,["all_colliders_held"]=true,["six_anchors_and_four_approaches_held"]=true,["navigation_reference_held"]=true,["camera_and_five_lights_held"]=true,["intentional_change"]="Original floor texture/UV finish only, plus separate no-collider decorative prop/render hierarchy."}.ToString());
        }
        private static string[] Colliders(GameObject root)
        {
            var rows=new List<string>();foreach(var c in root.GetComponentsInChildren<Collider>(true))
            {
                string row=c.GetType().Name+"|"+c.enabled+"|"+c.isTrigger+"|"+c.gameObject.layer+"|";
                if(c is MeshCollider m){var vs=m.sharedMesh.vertices.Select(m.transform.TransformPoint).ToArray();var ids=m.sharedMesh.triangles;var faces=new List<string>();for(int i=0;i<ids.Length;i+=3)faces.Add(K(vs[ids[i]])+";"+K(vs[ids[i+1]])+";"+K(vs[ids[i+2]]));faces.Sort(StringComparer.Ordinal);row+=m.convex+"|"+string.Join("/",faces);}
                else if(c is BoxCollider box){var points=new List<string>();foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})points.Add(K(box.transform.TransformPoint(box.center+Vector3.Scale(box.size*.5f,new Vector3(x,y,z)))));points.Sort(StringComparer.Ordinal);row+=string.Join("/",points);}
                else throw new InvalidDataException("Unreviewed Home collider "+c.GetType().Name);rows.Add(TextHash(row));
            }rows.Sort(StringComparer.Ordinal);return rows.ToArray();
        }
        private static JArray CheckNavigation(VenueSceneRoot venue,VenueSpots spots)
        {
            var offset=new Vector3(12000,0,12000);var handle=NavMesh.AddNavMeshData(venue.navigation,offset,Quaternion.identity);var result=new JArray();
            try{if(!NavMesh.SamplePosition(venue.EntrancePosition+offset,out var start,.20f,NavMesh.AllAreas))throw new InvalidDataException("Home entrance unavailable.");foreach(var spot in spots.spots.Values.Distinct().Where(s=>s.hasPosition).OrderBy(s=>s.id,StringComparer.Ordinal)){if(!venue.TryGetSpotApproach(spot.id,out var target)||!NavMesh.SamplePosition(target+offset,out var hit,.20f,NavMesh.AllAreas)||Vector2.Distance(new Vector2(target.x+offset.x,target.z+offset.z),new Vector2(hit.position.x,hit.position.z))>.08f)throw new InvalidDataException("Home approach unavailable: "+spot.id);var path=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new InvalidDataException("Home route incomplete: "+spot.id);result.Add(new JObject{["spot"]=spot.id,["approach"]=V(target),["path"]="PathComplete",["corners"]=new JArray(path.corners.Select(p=>V(p-offset))),["scope"]="Existing standing approach only; not contact acceptance"});}}
            finally{if(handle.valid)NavMesh.RemoveNavMeshData(handle);}return result;
        }
        private static T Save<T>(T obj,string path)where T:Object{if(File.Exists(path)||AssetDatabase.LoadMainAssetAtPath(path)!=null)throw new IOException("Refuse overwriting "+path);AssetDatabase.CreateAsset(obj,path);return obj;}
        private static void SaveOwned(){foreach(string guid in AssetDatabase.FindAssets("",new[]{Output})){string path=AssetDatabase.GUIDToAssetPath(guid);if(path.EndsWith(".mat",StringComparison.Ordinal)||path.EndsWith(".asset",StringComparison.Ordinal))AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));}}
        private static void Protected(string phase,Action action)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)throw new InvalidOperationException("Run Home B only in an idle root-owned Editor.");for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Preserve existing unsaved scene.");
            var before=ProtectedHashes();string receipt=Path.Combine(Path.GetTempPath(),"ctlife-home-detail2-"+phase+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(receipt);
            Action<string,string> record=(status,error)=>File.WriteAllText(Path.Combine(receipt,"transaction.json"),new JObject{["status"]=status,["error"]=error,["post_editor_exit_hash_check_required"]=true,["files"]=new JArray(before.Select(p=>new JObject{["path"]=p.Key,["sha256"]=p.Value}))}.ToString());record("captured_before_candidate",null);
            try{action();var after=ProtectedHashes();var delta=before.Keys.Union(after.Keys).Where(p=>!before.TryGetValue(p,out var a)||!after.TryGetValue(p,out var b)||a!=b).ToArray();if(delta.Length!=0)throw new InvalidDataException("Protected path/bytes changed; no automatic restoration: "+string.Join(",",delta));record("passed_protected_path_set_and_bytes_unchanged",null);}
            catch(Exception e){record("failed_no_unrelated_restore_or_dirty_clear",e.ToString());if(Directory.Exists(Output))File.WriteAllText(Output+"/"+(phase=="stage"?"StageReceipt":"BakeReceipt")+".json",new JObject{["status"]="failed",["error"]=e.ToString(),["protection_transaction"]=Path.Combine(receipt,"transaction.json")}.ToString());throw;}
            finally{Debug.Log("CTLIFE [home-detail] post-exit protection receipt: "+Path.Combine(receipt,"transaction.json"));}
        }
        private static Dictionary<string,string> ProtectedHashes(){var result=new Dictionary<string,string>();foreach(string dir in new[]{"Assets","ProjectSettings","Packages"})foreach(string path in Directory.GetFiles(dir,"*",SearchOption.AllDirectories))if(!path.StartsWith(Output+"/",StringComparison.Ordinal)&&path!=Output+".meta")result[Path.GetFullPath(path)]=Hash(path);return result;}
        private static string K(Vector3 v)=>v.x.ToString("R",CultureInfo.InvariantCulture)+","+v.y.ToString("R",CultureInfo.InvariantCulture)+","+v.z.ToString("R",CultureInfo.InvariantCulture);
        private static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static string TextHash(string text){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        private static string Hash(string path){using var sha=SHA256.Create();using var stream=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    }
}
