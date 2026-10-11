using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    /// <summary>Private local home art candidate. Not a public venue, published home, or accepted seated interaction.</summary>
    public static class ParentsBasementCandidateA
    {
        public const string Output = "Assets/Art/Candidates/ParentsBasement20261007A";
        public const string PrefabPath = Output + "/ParentsBasementA.prefab";
        private const string Snapshot = "Assets/Resources/Contract/config_snapshot.json";
        private const string SnapshotHash = "3a54ec2117e03fd3e456355e8fe407e5889d10790def5f4863d2163e970a9b56";
        private const string Manifest = "Assets/Art/environment/manifest.json";
        private static readonly Dictionary<string,Material> Materials = new Dictionary<string,Material>();
        private static readonly List<Bounds> Solids = new List<Bounds>();
        private static readonly JArray Placements = new JArray();
        private static JObject _manifest;
        private static Transform _root;
        private static int _serial, _windowMesh;
        private static readonly Dictionary<string,Vector3> Approaches = new Dictionary<string,Vector3>
        {
            ["bed"]=new Vector3(-3.05f,0,3), ["desk"]=new Vector3(2.85f,0,2.65f),
            ["sofa"]=new Vector3(1.8f,0,2), ["exit"]=new Vector3(0,0,-4.45f)
        };

        public static void BuildCandidate()
        {
            if(Directory.Exists(Output))throw new InvalidOperationException("Preserve the previous candidate, including failed output; use a reviewed new revision.");
            Protected("stage",Stage);
        }
        public static void BakeCandidate()=>Protected("bake",()=>
        {
            RequireSources();var stage=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));
            if((string)stage["status"]!="staged_unbaked_private_home_not_published"||stage["navigation"].Count()!=6||stage["navigation"].Any(p=>(string)p["path"]!="PathComplete"))throw new InvalidDataException("Home stage/navigation receipt is not complete.");
            VenueLightingBuilder.BakeVenue(PrefabPath,Output+"/Lighting");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);var maps=prefab.GetComponent<VenueLightmaps>();
            if(maps==null||maps.colors.Length==0||maps.bindings.Length==0||maps.bindings.Select(b=>b.renderer).Distinct().Count()!=maps.bindings.Length)throw new InvalidDataException("Home needs complete unique lightmap bindings.");
            File.WriteAllText(Output+"/BakeReceipt.json",new JObject{["status"]="baked_private_home_art_not_registered_or_contact_accepted",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["home_id"]="parents_basement",["logical_spot_group"]="home",["lightmaps"]=maps.colors.Length,["bound_renderers"]=maps.bindings.Length,["limitations"]="No public venue, local Home lifecycle, needs receipt, seated pose/exit, room registration or device acceptance is established by an art bake."}.ToString());
        });
        private static void RequireSources()
        {
            if(Hash(Snapshot)!=SnapshotHash)throw new InvalidDataException("Review changed Home contract before regenerating.");
            var pins=JObject.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/home-20261007/build-source-pins.json"))));
            foreach(var pin in ((JObject)pins["source_files"]).Properties())if(Hash(pin.Name)!=(string)pin.Value)throw new InvalidDataException("Pinned Home source or texture changed: "+pin.Name);
            var inventory=JObject.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/home-20261007/read-only-inventory.json"))));
            foreach(var source in inventory["source_models"])
            {
                var path=((string)source["fbx"]).Substring("client/".Length);
                if(Hash(path)!=(string)source["sha256"])throw new InvalidDataException("Pinned Home source changed: "+path);
            }
        }
        private static void Stage()
        {
            RequireSources();foreach(var dir in new[]{"Materials","Meshes","Textures"})Directory.CreateDirectory(Output+"/"+dir);AssetDatabase.Refresh();
            _manifest=JObject.Parse(File.ReadAllText(Manifest));Materials.Clear();Solids.Clear();Placements.Clear();_serial=0;_windowMesh=0;
            var data=JObject.Parse(File.ReadAllText(Snapshot));var spots=Registries.Parse(data["ids"],data["config"]).SpotsFor("home");
            var all=spots?.spots.Values.Distinct().Where(s=>s.hasPosition).OrderBy(s=>s.id,StringComparer.Ordinal).ToArray();
            if(all==null||all.Length!=6||spots.walkable.Count!=1)throw new InvalidDataException("Exactly six positioned Home anchors required.");
            var bounds=spots.walkable[0];if(bounds.x0!=-6||bounds.x1!=6||bounds.z0!=-5||bounds.z1!=5)throw new InvalidDataException("Home requires exact 12 x 10 m contract bounds.");
            var preview=EditorSceneManager.NewPreviewScene();
            try
            {
                var go=new GameObject("Parent's Basement / private local art candidate");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,preview);_root=go.transform;
                var venue=go.AddComponent<VenueSceneRoot>();venue.venueId="home";venue.districtId="";venue.cameraBounds=new Rect(-6,-5,12,10);
                venue.cameraDistance=8.8f;venue.cameraPitch=30;venue.cameraYaw=-12;venue.cameraFieldOfView=42;venue.cameraTargetViewportY=.30f;venue.cameraFocusHeight=1;
                var lighting=go.AddComponent<VenueContentLighting>();lighting.sunIntensity=.9f;lighting.sunAngles=new Vector3(48,-32,0);
                var floor=Group("Continuous domestic floor");
                for(int x=-5;x<=5;x+=2)for(int z=-4;z<=4;z+=2)
                {
                    var tile=Add((x==5&&z<=-2)||(x==-5&&z<=0)?"floor_tiles_2m":"floor_wood_2m",x,z,parent:floor);
                    var b=RenderBounds(tile);tile.transform.position-=Vector3.up*b.max.y;
                    if(Mathf.Abs(RenderBounds(tile).max.y)>.00001f)throw new InvalidDataException("Visible floor does not meet physical floor0.");
                }
                CombineStatic(floor,"HomeFloor");
                var ground=Group("Exact contract physical floor");ground.localPosition=new Vector3(0,-.1f,0);ground.gameObject.AddComponent<BoxCollider>().size=new Vector3(12,.2f,10);
                var walls=Group("Warm plaster shell / cutaway front");
                for(int x=-5;x<=5;x+=2)
                {
                    var panel=Add(x==-5||x==1?"wall_plaster_window_2m":"wall_plaster_solid_2m",x,5.10f,parent:walls);
                    if(x==-5||x==1)OpenWindowGlass(panel);
                }
                for(int z=-4;z<=4;z+=2)
                {
                    Add("wall_plaster_solid_2m",-6.1f,z,yaw:-90,parent:walls);
                    Add("wall_plaster_solid_2m",6.1f,z,yaw:90,parent:walls,heightScale:.35f);
                }
                foreach(int x in new[]{-5,-3,3,5})Add("wall_plaster_solid_2m",x,-5.1f,parent:walls,heightScale:.10f);
                CombineStatic(walls,"HomeShell");
                // Tall perimeter navigation stays outside exact contract bounds; small physical colliders match rendered walls.
                PhysicalBox("West wall",new Vector3(-6.1f,1.5f,0),new Vector3(.17f,3,10),false);
                PhysicalBox("North wall",new Vector3(0,1.5f,5.1f),new Vector3(12,3,.17f),false);
                PhysicalBox("East low cutaway",new Vector3(6.1f,.525f,0),new Vector3(.17f,1.05f,10),false);
                foreach(float x in new[]{-4f,4f})PhysicalBox("South cutaway",new Vector3(x,.15f,-5.1f),new Vector3(4,.30f,.17f),false);

                // Bed is domestic art pending edge/pose acceptance. Wire anchor remains at (-4,3), floor0.
                Furnish("home_bed_single",-4.65f,3.28f,yaw:0);
                Furnish("home_bookshelf",-5.55f,1.35f,yaw:90);
                Furnish("home_light_floor1",-3.3f,4.42f);
                Furnish("home_houseplant_1",-2.7f,4.43f,scale:.65f);
                // North-facing work nook; these unmodified source desk/chair surfaces are not yet fitted motions.
                Furnish("home_desk",4,3.65f,yaw:0);
                Furnish("office_chair",4,2.72f,yaw:180);
                Add("laptop",3.95f,3.65f,y:.76f,yaw:0);
                Add("cup_coffee",4.47f,3.40f,y:.76f);
                Add("home_shelf_b_large_decorated",4.0f,4.83f,y:1.62f);
                // Sofa and TV remain clear of center0; claim semantics are explicitly not enabled by this builder.
                Furnish("home_couch_medium1",0,1.72f,yaw:180);
                Add("home_tv_modern",0,4.82f,y:1.08f);
                Furnish("home_light_floor1",1.9f,4.10f);
                Furnish("home_houseplant_1",2.60f,4.45f,scale:.65f);
                // Source kitchen row: clear separation from the central entrance path.
                Furnish("home_kitchen_fridge",-5.4f,-3.45f,yaw:90);
                Furnish("home_kitchen_sink",-5.47f,-2.47f,yaw:90);
                foreach(float z in new[]{-1.80f,-1.10f,-.40f})Furnish("home_kitchen_cabinet1",-5.42f,z,yaw:90);
                // Bathroom cutaway: short wall gives identity without hiding the player from the arrival camera.
                Furnish("home_bathroom_shower1",5.13f,-3.46f,yaw:0);
                Furnish("home_bathroom_toilet",5.35f,-1.50f,yaw:90);
                Furnish("home_bathroom_sink",3.76f,-3.82f,yaw:0);
                var partition=Add("wall_plaster_solid_2m",3.1f,-3.45f,yaw:90,heightScale:.46f);AddSolid(partition);
                // Small rear lightwell: source planter silhouettes frame high windows instead of an unexplained void.
                var lightwell=Group("Rear lightwell");
                for(int x=-5;x<=5;x+=2)Add("floor_tiles_2m",x,6.1f,y:-.101f,parent:lightwell);
                for(int x=-5;x<=5;x+=2)Add("wall_plaster_solid_2m",x,7.1f,parent:lightwell,heightScale:.50f);
                CombineStatic(lightwell,"HomeLightwell");
                Add("home_houseplant_1",-5,6.1f,scale:.75f);Add("home_houseplant_1",1,6.1f,scale:.75f);
                Lamp("Bedside pool",new Vector3(-3.30f,1.52f,4.42f),1.20f,4,new Color(1,.88f,.73f));
                Lamp("Desk pool",new Vector3(4,2.4f,3.3f),1.3f,4,new Color(1,.90f,.78f));
                Lamp("Living pool",new Vector3(1.9f,1.53f,4.1f),1.1f,4,new Color(1,.88f,.73f));
                Lamp("Kitchen bounce",new Vector3(-4.8f,2.35f,-2),.8f,4,new Color(1,.93f,.84f));
                Lamp("Window fill",new Vector3(0,2.7f,4.35f),.65f,5,new Color(.78f,.86f,1));

                var anchors=Group("Six immutable Home anchors");
                foreach(var spot in all)
                {
                    var a=new GameObject("spot_"+spot.id);a.transform.SetParent(anchors,false);a.transform.localPosition=new Vector3(spot.x,0,spot.z);a.transform.localRotation=Quaternion.Euler(0,spot.rotY*Mathf.Rad2Deg,0);
                    var anchor=a.AddComponent<SpotAnchor>();anchor.spotId=spot.id;anchor.kind=spot.kind;anchor.anim=spot.anim??"";
                    if(Approaches.TryGetValue(spot.id,out var approach))
                    {var ap=new GameObject("Frontend standing approach");ap.transform.SetParent(a.transform,false);ap.transform.position=approach;anchor.approach=ap.transform;}
                }
                venue.Init(spots);venue.navigation=Navigation(spots);var paths=CheckNavigation(venue,spots);
                var witnesses=new JArray();foreach(var spot in all)
                {
                    if(!venue.TryGetSpot(spot.id,out var p,out var yaw)||Vector3.Distance(p,new Vector3(spot.x,0,spot.z))>.00001f||Mathf.Abs(Mathf.DeltaAngle(yaw*Mathf.Rad2Deg,spot.rotY*Mathf.Rad2Deg))>.001f)throw new InvalidDataException("Home anchor changed: "+spot.id);
                    witnesses.Add(new JObject{["spot"]=spot.id,["wire_id"]=spot.wireId,["position"]=V(p),["rotation_radians"]=spot.rotY,["declared_action"]=spot.anim,["contact_accepted"]=false});
                }
                CheckMaterials(go);if(go.GetComponent<CafeSeatMotionProfile>()!=null)throw new InvalidDataException("Unreviewed Home must not attach a Cafe motion profile.");
                if(PrefabUtility.SaveAsPrefabAsset(go,PrefabPath)==null)throw new IOException("Home candidate save failed.");SaveCandidateAssets();
                File.WriteAllText(Output+"/StageReceipt.json",new JObject{["status"]="staged_unbaked_private_home_not_published",["prefab"]=PrefabPath,["prefab_sha256"]=Hash(PrefabPath),["home_id"]="parents_basement",["logical_spot_group"]="home",["snapshot_sha256"]=Hash(Snapshot),["anchors"]=witnesses,["positioned_anchors"]=all.Length,["authored_approaches"]=Approaches.Count,["navigation"]=paths,["placements"]=Placements.DeepClone(),["source_lod0_triangles_manifest_sum"]=Placements.Sum(p=>(int)p["lod0_triangles"]),["static_mesh_renderers"]=go.GetComponentsInChildren<MeshRenderer>(true).Length,["mesh_colliders"]=go.GetComponentsInChildren<MeshCollider>(true).Length,["baked_only_lights"]=5,["limitations"]="Private local home art only. No public registry or Home entry callback, needs effects, server claim, accepted bed/couch/desk posture, sleep animation, motion profile, voice or device performance. Exactly six declared anchors held; navigation validates frontend approaches rather than claiming furniture interiors are walkable."}.ToString());
                Debug.Log("CTLIFE [home-art] Parent's Basement candidate staged; local lifecycle and native art/contact remain separate: "+PrefabPath);
            }
            finally{_root=null;EditorSceneManager.ClosePreviewScene(preview);Materials.Clear();}
        }
        private static GameObject Furnish(string id,float x,float z,float yaw=0,float scale=1)
        {
            var item=Add(id,x,z,yaw:yaw,scale:scale);var b=RenderBounds(item);item.transform.position-=Vector3.up*b.min.y;AddSolid(item);return item;
        }
        private static Bounds RenderBounds(GameObject item)
        {
            var rs=item.GetComponentsInChildren<MeshRenderer>(true).Where(r=>Lod(r.name)==0).ToArray();if(rs.Length==0)throw new InvalidDataException("Missing LOD0: "+item.name);
            var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
        }
        private static void AddSolid(GameObject item)
        {
            foreach(var filter in item.GetComponentsInChildren<MeshFilter>(true).Where(f=>Lod(f.name)==0))
            {var c=filter.gameObject.AddComponent<MeshCollider>();c.sharedMesh=filter.sharedMesh;}
            var b=RenderBounds(item);Solids.Add(new Bounds(new Vector3(b.center.x,1.2f,b.center.z),new Vector3(b.size.x+.12f,2.4f,b.size.z+.12f)));
        }
        private static void PhysicalBox(string name,Vector3 center,Vector3 size,bool nav)
        {var go=Group(name);go.localPosition=center;go.gameObject.AddComponent<BoxCollider>().size=size;if(nav)Solids.Add(new Bounds(center,size));}
        private static void Lamp(string name,Vector3 p,float intensity,float range,Color color)
        {var go=Group(name);go.localPosition=p;var l=go.gameObject.AddComponent<Light>();l.type=LightType.Point;l.lightmapBakeType=LightmapBakeType.Baked;l.intensity=intensity;l.range=range;l.color=color;l.shadows=LightShadows.Soft;}
        private static JArray V(Vector3 p)=>new JArray(p.x,p.y,p.z);
        private static Transform Group(string name)
        {
            var go=new GameObject(name);go.transform.SetParent(_root,false);return go.transform;
        }
        private static void OpenWindowGlass(GameObject item)
        {
            foreach (var filter in item.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if (renderer==null) continue;
                var original=filter.sharedMesh;
                var keep=new List<int>();
                for (int s=0;s<original.subMeshCount;s++) if (!renderer.sharedMaterials[s].name.Contains("KEEP_glass")) keep.Add(s);
                if (keep.Count==original.subMeshCount) continue;
                var mesh=Readable(original); mesh.name="Home open window";
                var triangles=keep.Select(s=>original.GetTriangles(s)).ToArray();
                mesh.subMeshCount=triangles.Length;
                for (int s=0;s<triangles.Length;s++) mesh.SetTriangles(triangles[s],s);
                filter.sharedMesh=Save(mesh,Output+"/Meshes/OpenWindow_"+(++_windowMesh).ToString("D2")+".asset");
                renderer.sharedMaterials=keep.Select(s=>renderer.sharedMaterials[s]).ToArray();
            }
        }
        private static Mesh Readable(Mesh original)
        {
            var mesh=new Mesh { name=original.name+"_candidate",indexFormat=original.indexFormat,
                vertices=original.vertices,normals=original.normals,uv=original.uv,uv2=original.uv2,subMeshCount=original.subMeshCount };
            for(int s=0;s<original.subMeshCount;s++) mesh.SetTriangles(original.GetTriangles(s),s);
            mesh.RecalculateBounds();return mesh;
        }
        private static void CombineStatic(Transform group,string name)
        {
            var byMaterial=new Dictionary<Material,List<CombineInstance>>();
            var copies=new Dictionary<Mesh,Mesh>();
            foreach(var filter in group.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();var original=filter.sharedMesh;
                if(renderer==null||original==null)continue;
                if(!copies.TryGetValue(original,out var copy))copies[original]=copy=Readable(original);
                for(int s=0;s<copy.subMeshCount;s++)
                {
                    var material=renderer.sharedMaterials[s];
                    if(!byMaterial.TryGetValue(material,out var list))byMaterial[material]=list=new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh=copy,subMeshIndex=s,transform=_root.worldToLocalMatrix*filter.transform.localToWorldMatrix });
                }
            }
            int part=0;
            foreach(var pair in byMaterial)
            {
                var mesh=new Mesh { name=name+"_"+part,indexFormat=IndexFormat.UInt32 };
                mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh.RecalculateBounds();
                var go=new GameObject(mesh.name);go.transform.SetParent(_root,false);go.isStatic=true;
                go.AddComponent<MeshFilter>().sharedMesh=Save(mesh,Output+"/Meshes/"+name+"_"+part+".asset");
                part++;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=pair.Key;renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            }
            foreach(var copy in copies.Values)Object.DestroyImmediate(copy);
            Object.DestroyImmediate(group.gameObject);
        }
        private static GameObject Add(string id, float x, float z, float yaw = 0, float y = 0, float scale = 1, string screen = null, bool solid = false, Transform parent = null, float heightScale = 1)
        {
            var row = _manifest[id] ?? throw new InvalidDataException("Unknown sourced asset " + id);
            string path = "Assets/Art/" + (string)row["fbx"];
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (original == null) throw new FileNotFoundException("Sourced model missing", path);
            var item = Object.Instantiate(original, parent != null ? parent : _root);
            item.name = "Home_" + (++_serial).ToString("D3") + "_" + id;
            item.transform.localPosition = new Vector3(x,y,z);
            item.transform.localRotation = Quaternion.Euler(0,yaw,0);
            item.transform.localScale = new Vector3(scale, scale * heightScale, scale);
            foreach (var c in item.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var group in item.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(group);
            var renderers = item.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => Resolve(m != null ? m.name : "M_ctl_palette", row, screen)).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.gameObject.isStatic = true;
            }
            var levels = renderers.GroupBy(r => Lod(r.name)).OrderBy(g => g.Key).ToArray();
            if (levels.Length > 1)
            {
                var group = item.AddComponent<LODGroup>();
                group.SetLODs(levels.Select((g, i) => new LOD(i == levels.Length - 1 ? .002f : i == 0 ? .065f : .025f, g.Cast<Renderer>().ToArray())).ToArray());
                group.fadeMode = LODFadeMode.None; group.RecalculateBounds();
            }
            if (solid) AddSolid(item);
            Placements.Add(new JObject { ["id"] = id, ["x"] = x, ["y"] = y, ["z"] = z, ["yaw"] = yaw, ["scale"] = scale,
                ["screen"] = screen, ["solid"] = solid, ["source"] = (string)row["source"], ["lod0_triangles"] = (int)row["tris"][0] });
            return item;
        }
        private static int Lod(string name)
        {
            int i = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            return i >= 0 && i + 4 < name.Length && char.IsDigit(name[i + 4]) ? name[i + 4] - '0' : 0;
        }
        private static Material Resolve(string name, JToken row, string screen)
        {
            name = name.Replace(" (Instance)", "");
            int suffix = name.LastIndexOf('.');
            if (suffix >= 0 && int.TryParse(name.Substring(suffix + 1), out _)) name = name.Substring(0,suffix);
            bool display = name.StartsWith("KEEP_ad_slot") || name.StartsWith("KEEP_screen");
            string key = display ? "Screen_" + (screen ?? "timeline_screen") : name;
            if (Materials.TryGetValue(key, out var cached)) return cached;
            var shader = Shader.Find(display ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidDataException("Required URP shader missing.");
            var material = new Material(shader) { name = key, enableInstancing = true };
            material.SetColor("_BaseColor", Color.white);
            Texture2D texture = null;
            if (display) { material.SetColor("_BaseColor",new Color(.10f,.18f,.18f)); }
            else if (name == "M_ctl_palette") texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/environment/_shared/ctl_palette.png");
            else if (name.StartsWith("M_"))
            {
                string stem = name.Substring(2);
                string tex = (row["textures"] as JArray)?.Values<string>().FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == stem);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/" + (tex ?? "environment/_shared/textures/" + stem + ".png"));
            }
            if (name.Contains("painted_plaster")) { texture=null; material.SetColor("_BaseColor",new Color(.76f,.73f,.66f)); }
            else if (texture != null) material.SetTexture("_BaseMap", texture);
            else if (name.StartsWith("M_")) throw new InvalidDataException("Missing sourced texture " + name);
            material.SetFloat("_Smoothness", .12f);
            if (name.Contains("glass")) material.SetColor("_BaseColor", new Color(.39f,.64f,.72f));
            if (name.Contains("neon")) material.SetColor("_BaseColor", new Color(.26f,.9f,.74f));
            string lower = name.ToLowerInvariant();
            if (new[] { "leaves", "leaf_", "flowers", "grass", "bush" }.Any(lower.Contains))
            {
                material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .45f); material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_ALPHATEST_ON"); material.SetOverrideTag("RenderType", "TransparentCutout"); material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            Materials[key] = Save(material, Output + "/Materials/" + key + ".mat");
            return Materials[key];
        }
        private static NavMeshData Navigation(VenueSpots spots)
        {
            var sources=new List<NavMeshBuildSource>();foreach(var r in spots.walkable)sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Box,area=0,transform=Matrix4x4.TRS(new Vector3((r.x0+r.x1)*.5f,-.1f,(r.z0+r.z1)*.5f),Quaternion.identity,Vector3.one),size=new Vector3(r.x1-r.x0,.2f,r.z1-r.z0)});
            foreach(var b in Solids)sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,area=1,transform=Matrix4x4.TRS(b.center,Quaternion.identity,Vector3.one),size=b.size});
            var settings=NavMesh.GetSettingsByID(0);settings.agentRadius=.30f;settings.agentHeight=1.75f;settings.overrideVoxelSize=true;settings.voxelSize=.04f;
            var nav=NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(0,1,0),new Vector3(15,8,13)),Vector3.zero,Quaternion.identity);if(nav==null)throw new InvalidDataException("Home navigation generation failed.");
            nav.name="Private Home navigation";return Save(nav,Output+"/Meshes/Navigation.asset");
        }
        private static JArray CheckNavigation(VenueSceneRoot venue,VenueSpots spots)
        {
            var offset=new Vector3(12000,0,12000);var instance=NavMesh.AddNavMeshData(venue.navigation,offset,Quaternion.identity);var rows=new JArray();
            try
            {
                if(!NavMesh.SamplePosition(venue.EntrancePosition+offset,out var start,.20f,NavMesh.AllAreas))throw new InvalidDataException("Home entrance blocked.");
                foreach(var s in spots.spots.Values.Distinct().Where(s=>s.hasPosition).OrderBy(s=>s.id,StringComparer.Ordinal))
                {
                    if(!venue.TryGetSpotApproach(s.id,out var p))throw new InvalidDataException("No Home approach: "+s.id);
                    if(!NavMesh.SamplePosition(p+offset,out var hit,.20f,NavMesh.AllAreas)||Vector2.Distance(new Vector2(p.x+offset.x,p.z+offset.z),new Vector2(hit.position.x,hit.position.z))>.08f)throw new InvalidDataException("Home approach is not on navigation: "+s.id);
                    var path=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new InvalidDataException("No complete Home approach path: "+s.id);
                    rows.Add(new JObject{["spot"]=s.id,["approach"]=V(p),["sampled"]=V(hit.position-offset),["path"]="PathComplete",["corners"]=new JArray(path.corners.Select(c=>V(c-offset))),["scope"]="Standing approach only, not furniture support or seated action acceptance"});
                }
            }
            finally{if(instance.valid)NavMesh.RemoveNavMeshData(instance);}return rows;
        }
        private static void CheckMaterials(GameObject root)
        {
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials)
                if(m==null||m.shader==null||m.shader.name.Contains("InternalError")||!AssetDatabase.GetAssetPath(m).StartsWith(Output+"/",StringComparison.Ordinal))throw new InvalidDataException("Missing/non-isolated Home material: "+r.name);
        }
        private static T Save<T>(T obj,string path) where T:Object
        {
            if(File.Exists(path)||AssetDatabase.LoadMainAssetAtPath(path)!=null)throw new IOException("Refuse overwriting candidate asset: "+path);
            AssetDatabase.CreateAsset(obj,path);return obj;
        }
        private static void SaveCandidateAssets()
        {
            foreach(var guid in AssetDatabase.FindAssets("",new[]{Output}))
            {var p=AssetDatabase.GUIDToAssetPath(guid);if(p.EndsWith(".mat",StringComparison.Ordinal)||p.EndsWith(".asset",StringComparison.Ordinal))AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(p));}
        }
        private static void Protected(string phase,Action body)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)throw new InvalidOperationException("Run an isolated Home stage/bake only while idle.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Preserve existing unsaved scene.");
            var protectedFiles=ProtectedHashes();
            var receipt=Path.Combine(Path.GetTempPath(),"ctlife-home-art-"+phase+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(receipt);
            Action<string,string> record=(status,error)=>File.WriteAllText(Path.Combine(receipt,"transaction.json"),new JObject{["status"]=status,["error"]=error,["post_editor_exit_hash_check_required"]=true,["files"]=new JArray(protectedFiles.Select(p=>new JObject{["path"]=p.Key,["sha256"]=p.Value}))}.ToString());
            record("captured_before_candidate",null);
            try
            {
                body();var after=ProtectedHashes();var drift=protectedFiles.Keys.Union(after.Keys).Where(p=>!protectedFiles.TryGetValue(p,out var beforeHash)||!after.TryGetValue(p,out var afterHash)||beforeHash!=afterHash).ToArray();if(drift.Length!=0)throw new InvalidDataException("Protected Home source path or bytes changed; no automatic restoration: "+string.Join(", ",drift));record("passed_protected_path_set_and_bytes_unchanged",null);
            }
            catch(Exception e)
            {
                record("failed_no_unrelated_asset_restore_or_clear_dirty",e.ToString());
                if(Directory.Exists(Output))File.WriteAllText(Output+"/"+(phase=="stage"?"StageReceipt":"BakeReceipt")+".json",new JObject{["status"]="failed",["error"]=e.ToString(),["protection_transaction"]=Path.Combine(receipt,"transaction.json")}.ToString());throw;
            }
            finally{Debug.Log("CTLIFE [home-art] post-exit protection receipt: "+Path.Combine(receipt,"transaction.json"));}
        }
        private static Dictionary<string,string> ProtectedHashes()
        {
            var result=new Dictionary<string,string>();foreach(string folder in new[]{"Assets","ProjectSettings","Packages"})
                foreach(string path in Directory.GetFiles(folder,"*",SearchOption.AllDirectories))if(!path.StartsWith(Output+"/",StringComparison.Ordinal)&&path!=Output+".meta")result[Path.GetFullPath(path)]=Hash(path);
            return result;
        }
        private static string Hash(string path){using var sha=SHA256.Create();using var stream=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    }
}
