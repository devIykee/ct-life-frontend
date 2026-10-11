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
    /// <summary>
    /// Isolated, sourced Terminal Cafe interior. Does not publish a venue, edit its contract,
    /// overwrite original assets or imply that the illustrated activity pockets are live services.
    /// Stage and bake in separate coordinator-owned batch processes; verify external hashes after exit.
    /// </summary>
    public static class TerminalCafeCandidate
    {
        public const string Output = "Assets/Art/Candidates/TerminalCafe20261006";
        public const string PrefabPath = Output + "/TerminalCafe.prefab";
        private const string Canonical = "Assets/Art/Generated/Venues/TimelineSquare.prefab";
        private const string Snapshot = "Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest = "Assets/Art/environment/manifest.json";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly List<Bounds> Solids = new List<Bounds>();
        private static readonly JArray Placements = new JArray();
        private static JObject _manifest;
        private static Transform _root;
        private static ProtectedFiles _protection;

        [MenuItem("CT Life/Candidates/Stage Terminal Cafe")]
        public static void BuildCandidate() => Protected("stage", Stage);

        [MenuItem("CT Life/Candidates/Bake Terminal Cafe")]
        public static void BakeCandidate() => Protected("bake", () =>
        {
            var receipt = JObject.Parse(File.ReadAllText(Output + "/StageReceipt.json"));
            if ((string)receipt["status"] != "staged_unbaked_not_published" || (string)receipt["snapshot_sha256"] != Hash(Snapshot))
                throw new InvalidDataException("Stage receipt missing or contract snapshot changed.");
            VenueLightingBuilder.BakeVenue(PrefabPath, Output + "/Lighting");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var maps = prefab != null ? prefab.GetComponent<VenueLightmaps>() : null;
            if (maps == null || maps.colors.Length == 0 || maps.bindings.Length == 0) throw new InvalidDataException("Cafe bake has no bound lightmaps.");
            File.WriteAllText(Output + "/BakeReceipt.json", new JObject
            {
                ["status"] = "baked_not_published_requires_visual_and_device_review", ["prefab"] = PrefabPath,
                ["prefab_sha256"] = Hash(PrefabPath), ["lightmaps"] = maps.colors.Length, ["bound_renderers"] = maps.bindings.Length
            }.ToString());
        });

        private static void Stage()
        {
            foreach (string sub in new[] { "Materials", "Meshes", "Textures" }) Directory.CreateDirectory(Output + "/" + sub);
            AssetDatabase.Refresh();
            _manifest = JObject.Parse(File.ReadAllText(Manifest)); Materials.Clear(); Solids.Clear(); Placements.Clear();
            var data = JObject.Parse(File.ReadAllText(Snapshot));
            var spots = Registries.Parse(data["ids"],data["config"]).SpotsFor("terminal_cafe");
            if (spots == null || spots.Entrance == null || spots.walkable.Count != 1) throw new InvalidDataException("Positioned cafe contract required.");
            var room = spots.walkable[0];
            if (room.x0 != -12 || room.x1 != 12 || room.z0 != -9 || room.z1 != 9)
                throw new InvalidDataException("Cafe geometry is authored for current 24 x 18 m contract; review changed bounds before regenerating.");
            var terminals = spots.spots.Values.Distinct().Where(s => s.kind == "terminal").ToArray();
            var seats = spots.spots.Values.Distinct().Where(s => s.kind == "seat").ToArray();
            if (terminals.Length != 8 || seats.Length != 4) throw new InvalidDataException("Expected eight terminal seats and four social seats.");
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("Terminal Cafe - isolated authored interior");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,preview);
                _root = go.transform;
                var venue = go.AddComponent<VenueSceneRoot>();
                venue.venueId = "terminal_cafe"; venue.districtId = "timeline";
                venue.cameraBounds = new Rect(-12,-9,24,18);
                venue.cameraDistance = 15.8f; venue.cameraPitch = 39f; venue.cameraYaw = -12f;
                venue.cameraFieldOfView = 45f; venue.cameraTargetViewportY = .38f; venue.cameraFocusHeight = 1;
                go.AddComponent<VenueContentLighting>();
                var floor = Group("Wood floor and tiled coffee apron");
                for (int x = -11; x < 12; x += 2) for (int z = -8; z < 9; z += 2)
                    Add(x <= -9 && z < 0 ? "floor_tiles_2m" : "floor_wood_2m",x,z,y:-.101f,parent:floor);
                var ground = new GameObject("Contract floor collision"); ground.transform.SetParent(_root,false);
                ground.transform.localPosition = new Vector3(0,-.1f,0); ground.AddComponent<BoxCollider>().size = new Vector3(24,.2f,18);

                var walls = Group("Brick display wall and open window wall");
                for (int x = -11; x < 12; x += 2) Add("wall_brick_solid_2m",x,9.1f,parent:walls,heightScale:1.5f);
                for (int z = -8; z < 9; z += 2)
                {
                    var window = Add(z % 4 == 0 ? "wall_brick_window_2m" : "wall_brick_solid_2m",-12.1f,z,yaw:-90,parent:walls,heightScale:1.5f);
                    if (z % 4 == 0) OpenWindowGlass(window);
                    Add("wall_brick_solid_2m",12.1f,z,yaw:90,parent:walls,heightScale:.21f);
                }
                // Cutaway front keeps the actor readable and leaves a four-metre entrance passage.
                for (int x = -11; x < 12; x += 2) if (Mathf.Abs(x) >= 3)
                    Add("wall_brick_solid_2m",x,-9.1f,parent:walls,heightScale:.14f);
                CombineStatic(floor,"CafeFloor"); CombineStatic(walls,"CafeWalls");

                // All eight claimed terminal anchors are seated typing stations, not standing kiosk art.
                var terminalRows = new JArray();
                string[] charts = { "cafe_chart_pump","cafe_chart_dump","cafe_chart_crab","kiosk_chart" };
                int index = 0;
                foreach (var spot in terminals.OrderBy(s => s.id))
                {
                    if (!spot.hasPosition || Mathf.Abs(spot.rotY) > .001f || spot.anim != "act_type")
                        throw new InvalidDataException("Cafe station layout expects positioned north-facing act_type anchors: " + spot.id);
                    Add("desk_medium_scifi",spot.x,spot.z+.86f,solid:true);
                    Add("office_chair",spot.x,spot.z-.10f,yaw:180);
                    Add("monitor_triple",spot.x,spot.z+1.03f,y:.76f,screen:charts[index%charts.Length]);
                    Add("laptop",spot.x-.56f,spot.z+.69f,y:.76f,yaw:14);
                    Add("cup_coffee",spot.x+.82f,spot.z+.68f,y:.76f);
                    Add("desk_lamp",spot.x-.91f,spot.z+1.0f,y:.76f);
                    terminalRows.Add(new JObject { ["spot"]=spot.id,["x"]=spot.x,["z"]=spot.z,["yaw_radians"]=spot.rotY,["animation"]=spot.anim });
                    index++;
                }
                // Four server-owned social seats with table settings; this leaves a continuous center aisle.
                foreach (var spot in seats)
                {
                    if (!spot.hasPosition || Mathf.Abs(spot.rotY) > .001f) throw new InvalidDataException("Unexpected social seat orientation " + spot.id);
                    Add("cafe_chair_a",spot.x,spot.z-.10f,yaw:180);
                    Add("cafe_table_round",spot.x,spot.z+.86f,solid:true);
                    Add("cup_coffee",spot.x-.23f,spot.z+.78f,y:.76f);
                    Add("croissant",spot.x+.15f,spot.z+.85f,y:.76f);
                    Add("plant_small",spot.x,spot.z+1.10f,y:.76f,scale:.6f);
                }

                // Bar runs along the left foreground wall, beside the existing barista/counter anchor.
                for (int z = -6; z <= -2; z++) Add("bar_counter",-8.9f,z,yaw:90,solid:true);
                Add("coffee_machine",-8.9f,-5.9f,y:.95f,yaw:90);
                Add("cash_register",-8.9f,-2.2f,y:.95f,yaw:90);
                Add("display_bread",-8.9f,-4.2f,y:.95f,yaw:90);
                Add("cafe_fridge",-11.25f,-2.2f,yaw:90,solid:true);
                Add("cafe_shelf",-11.7f,-5.4f,y:1.7f,yaw:90);
                Add("cup_coffee",-11.62f,-5.3f,y:2.4f);
                Add("billboard_wall",-11.91f,-5.1f,y:2.75f,yaw:90,scale:.65f,screen:"sign_gm_coffee");

                // Graphic hierarchy: a large Pulse wall, one venue sign, then workstation-scale charts.
                Add("wall_screen_large",0,8.99f,y:.83f,scale:1.40f,screen:"cafe_pulse");
                Add("billboard_wall",8.0f,8.98f,y:2.8f,scale:.93f,screen:"neon_terminal_cafe");
                Add("billboard_wall",-8.0f,8.98f,y:2.8f,scale:.67f,screen:"neon_wagmi");
                foreach (float x in new[] { -7f, 0f, 7f })
                {
                    Add("home_light_chandelier",x,-.9f,y:3.7f,scale:.85f);
                    var lamp = new GameObject("Baked cafe pendant"); lamp.transform.SetParent(_root,false);
                    lamp.transform.localPosition = new Vector3(x,3.65f,-.9f);
                    var light = lamp.AddComponent<Light>(); light.type = LightType.Point;
                    light.lightmapBakeType = LightmapBakeType.Baked;
                    light.color = new Color(1f,.88f,.73f); light.intensity = 2.2f; light.range = 7f;
                    light.shadows = LightShadows.Soft;
                }
                Add("houseplant_tall",-10.6f,7.4f,scale:1.05f,solid:true);
                Add("houseplant_tall",10.6f,7.4f,scale:1.05f,solid:true);
                Add("houseplant_tall",-11.15f,-7.6f,scale:.9f,solid:true);
                Add("houseplant_tall",10.8f,-7.4f,scale:.9f,solid:true);
                Add("home_rug_rectangle_stripes_a",8.6f,-5.9f,scale:1.45f);
                Add("lounge_sofa",9.8f,-5.7f,yaw:-90,solid:true);
                Add("home_table_roundsmall",8.2f,-5.7f,scale:.8f,solid:true);
                Add("cup_coffee",8.2f,-5.7f,y:.6f);
                Add("floor_lamp",10.7f,-3.6f,solid:true);
                // Outside the left windows: a framed garden, not an unexplained clear-color void.
                for (int z = -6; z <= 6; z += 6)
                {
                    Add("planter_sidewalk",-14.2f,z,solid:false);
                    Add("tree_blossom_4",-14.2f,z,y:.28f,scale:.72f);
                }
                var outside = Group("Window garden paving");
                for (int x = -17; x <= -13; x += 2) for (int z = -10; z <= 10; z += 2)
                    Add("plaza_hexpaving_2m",x,z,y:-.101f,parent:outside);
                CombineStatic(outside,"CafeWindowGarden");

                var anchors = Group("Exact contract anchors");
                foreach (var spot in spots.spots.Values.Distinct().Where(s=>s.hasPosition))
                {
                    var anchor = new GameObject("spot_"+spot.id); anchor.transform.SetParent(anchors,false);
                    anchor.transform.localPosition = new Vector3(spot.x,0,spot.z);
                    anchor.transform.localRotation = Quaternion.Euler(0,spot.rotY*Mathf.Rad2Deg,0);
                    var a = anchor.AddComponent<SpotAnchor>(); a.spotId=spot.id; a.kind=spot.kind; a.anim=spot.anim??"";
                }
                venue.navigation=Navigation(spots); venue.Init(spots);
                var navigation=CheckNavigation(venue,spots); CheckMaterials(go);
                foreach (var spot in spots.spots.Values.Distinct().Where(s=>s.hasPosition))
                    if (!venue.TryGetSpot(spot.id,out var p,out var angle) || Vector3.Distance(p,new Vector3(spot.x,0,spot.z))>.0001f || Mathf.Abs(Mathf.DeltaAngle(angle*Mathf.Rad2Deg,spot.rotY*Mathf.Rad2Deg))>.001f)
                        throw new InvalidDataException("Candidate changed positioned spot "+spot.id);
                if (PrefabUtility.SaveAsPrefabAsset(go,PrefabPath)==null) throw new IOException("Cafe candidate save failed.");
                SaveCandidateAssets();
                File.WriteAllText(Output+"/StageReceipt.json",new JObject
                {
                    ["status"]="staged_unbaked_not_published",["prefab"]=PrefabPath,["snapshot_sha256"]=Hash(Snapshot),
                    ["positioned_anchors"]=Anchors(go).Count,["navigation"]=navigation,["terminal_stations"]=terminalRows,
                    ["placements"]=Placements.DeepClone(),["source_lod0_triangles_manifest_estimate"]=Placements.Sum(p=>(int)p["lod0_triangles"]),
                    ["static_mesh_renderers"]=go.GetComponentsInChildren<MeshRenderer>(true).Length,["baked_only_pendant_lights"]=3,
                    ["limitations"]="Seats require native animation/clearance checks. Charts are artwork, not live market data. No registry promotion, server change or performance acceptance."
                }.ToString());
                Debug.Log("CTLIFE [terminal-cafe] 8 terminal stations and 4 social seats staged at exact contract anchors; bake required: "+PrefabPath);
            }
            finally { _root=null; EditorSceneManager.ClosePreviewScene(preview); Materials.Clear(); }
        }
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
                var mesh=Readable(original); mesh.name="Cafe open window";
                var triangles=keep.Select(s=>original.GetTriangles(s)).ToArray();
                mesh.subMeshCount=triangles.Length;
                for (int s=0;s<triangles.Length;s++) mesh.SetTriangles(triangles[s],s);
                filter.sharedMesh=Save(mesh,Output+"/Meshes/OpenWindow.asset");
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
            item.name = "Direction_" + id;
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
            if (solid)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                // Renderer bounds include every LOD; y is deliberately tall for nav exclusion, while XZ retain real source size.
                Solid(new Bounds(new Vector3(bounds.center.x, 1.2f, bounds.center.z), new Vector3(bounds.size.x, 2.4f, bounds.size.z)));
            }
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
            if (display) texture = Screen(screen ?? "timeline_screen");
            else if (name == "M_ctl_palette") texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/environment/_shared/ctl_palette.png");
            else if (name.StartsWith("M_"))
            {
                string stem = name.Substring(2);
                string tex = (row["textures"] as JArray)?.Values<string>().FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == stem);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/" + (tex ?? "environment/_shared/textures/" + stem + ".png"));
            }
            if (texture != null) material.SetTexture("_BaseMap", texture);
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
        private static Texture2D Screen(string id)
        {
            string path = Output + "/Textures/" + id + ".png";
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/lookdev/tex", id + ".png"));
            File.Copy(source, path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidDataException("Missing candidate texture importer.");
            importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 1024;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.ASTC_8x8;
            importer.SetPlatformTextureSettings(android); importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        private static void Solid(Bounds bounds)
        {
            Solids.Add(bounds);
            var go = new GameObject("Direction solid"); go.transform.SetParent(_root, false); go.transform.localPosition = bounds.center;
            go.AddComponent<BoxCollider>().size = bounds.size;
        }
        private static NavMeshData Navigation(VenueSpots spots)
        {
            var sources = new List<NavMeshBuildSource>();
            foreach (var r in spots.walkable) sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 0,
                transform = Matrix4x4.TRS(new Vector3((r.x0+r.x1)*.5f,-.1f,(r.z0+r.z1)*.5f), Quaternion.identity,Vector3.one),
                size = new Vector3(Mathf.Abs(r.x1-r.x0),.2f,Mathf.Abs(r.z1-r.z0)) });
            foreach (var b in Solids) sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox, area = 1,
                transform = Matrix4x4.TRS(b.center,Quaternion.identity,Vector3.one), size = b.size });
            var nav = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),sources,new Bounds(new Vector3(0,1,0),new Vector3(27,8,21)),Vector3.zero,Quaternion.identity);
            if (nav == null) throw new InvalidDataException("Candidate navigation generation failed.");
            nav.name = "Terminal Cafe navigation";
            return Save(nav,Output + "/Meshes/Navigation.asset");
        }
        private static JArray CheckNavigation(VenueSceneRoot venue, VenueSpots spots)
        {
            var offset = new Vector3(10000,0,10000);
            var instance = NavMesh.AddNavMeshData(venue.navigation,offset,Quaternion.identity);
            var rows = new JArray();
            try
            {
                var p = new Vector3(spots.Entrance.x,0,spots.Entrance.z) + offset;
                if (!NavMesh.SamplePosition(p,out var start,.75f,NavMesh.AllAreas)) throw new InvalidDataException("Entrance blocked.");
                foreach (var spot in spots.spots.Values.Distinct().Where(s => s.hasPosition))
                {
                    var point = new Vector3(spot.x,0,spot.z) + offset;
                    if (!NavMesh.SamplePosition(point,out var hit,.75f,NavMesh.AllAreas)) throw new InvalidDataException("Blocked spot " + spot.id);
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidDataException("No complete path to " + spot.id);
                    rows.Add(new JObject { ["spot"] = spot.id, ["sample_distance_m"] = Vector3.Distance(point,hit.position), ["path"] = "PathComplete" });
                }
            }
            finally { if (instance.valid) NavMesh.RemoveNavMeshData(instance); }
            return rows;
        }
        private static Dictionary<string,Vector3> Anchors(GameObject root) => root.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a => a.spotId,a => a.transform.position);
        private static void CheckMaterials(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials)
                if (m == null || m.shader == null || m.shader.name.Contains("InternalError") || !AssetDatabase.GetAssetPath(m).StartsWith(Output + "/",StringComparison.Ordinal))
                    throw new InvalidDataException("Missing or non-isolated material on " + r.name);
        }
        private static T Save<T>(T value, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value,path); return value; }
            EditorUtility.CopySerialized(value,existing); Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); return existing;
        }
        private static void SaveCandidateAssets()
        {
            _protection.ClearDirty();
            foreach (string guid in AssetDatabase.FindAssets("",new[] { Output }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".mat",StringComparison.Ordinal) || path.EndsWith(".asset",StringComparison.Ordinal))
                    AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));
            }
        }
        private static void Protected(string phase, Action action)
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Use an idle, coordinator-owned Unity batch process.");
            if (_protection != null) throw new InvalidOperationException("Another candidate transaction is active.");
            _protection = new ProtectedFiles(phase);
            try { action(); _protection.ClearDirty(); _protection.Verify(); _protection.Record("passed_protected_bytes_unchanged",null); }
            catch (Exception error)
            {
                _protection.Restore(); _protection.Record("failed_original_bytes_restored",error.ToString());
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/" + (phase == "stage" ? "StageReceipt.json" : "BakeReceipt.json"),new JObject { ["status"] = "failed", ["error"] = error.ToString() }.ToString());
                throw;
            }
            finally { _protection = null; }
        }
        private sealed class ProtectedFiles
        {
            private readonly Dictionary<string,string> _hashes = new Dictionary<string,string>();
            private readonly string _backup;
            public ProtectedFiles(string phase)
            {
                _backup = Path.Combine(Path.GetTempPath(),"ctlife-terminal-cafe-" + phase + "-" + Guid.NewGuid().ToString("N"));
                var paths = AssetDatabase.GetDependencies(Canonical,true).Concat(new[] { Canonical,Snapshot,Manifest,"Assets/Resources/World/VenueContentRegistry.asset" })
                    .Concat(Directory.GetFiles("ProjectSettings","*",SearchOption.AllDirectories)).Where(File.Exists).Distinct().ToList();
                paths.AddRange(paths.Where(p => File.Exists(p + ".meta")).Select(p => p + ".meta").ToArray());
                foreach (string path in paths.Distinct())
                {
                    string copy = Path.Combine(_backup,"before",path); Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    File.Copy(path,copy); _hashes[path] = Hash(path);
                    if (Hash(copy) != _hashes[path]) throw new IOException("Cannot snapshot " + path);
                }
                Record("captured_after_editor_startup_before_candidate",null);
            }
            public void ClearDirty()
            {
                foreach (string path in _hashes.Keys.Where(p => p.StartsWith("Assets/",StringComparison.Ordinal) && !p.EndsWith(".meta",StringComparison.Ordinal)))
                    foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path)) if (obj != null && EditorUtility.IsDirty(obj)) EditorUtility.ClearDirty(obj);
            }
            public void Verify()
            {
                var changed = _hashes.Where(p => !File.Exists(p.Key) || Hash(p.Key) != p.Value).Select(p => p.Key).ToArray();
                if (changed.Length > 0) throw new InvalidDataException("Protected files changed: " + string.Join(", ",changed));
            }
            public void Restore()
            {
                ClearDirty();
                foreach (var p in _hashes.Where(p => !File.Exists(p.Key) || Hash(p.Key) != p.Value))
                {
                    if (File.Exists(p.Key)) { string failed = Path.Combine(_backup,"after-failure",p.Key); Directory.CreateDirectory(Path.GetDirectoryName(failed)); File.Copy(p.Key,failed); }
                    string source = Path.Combine(_backup,"before",p.Key);
                    if (Hash(source) != p.Value) throw new IOException("Snapshot corrupted: " + p.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(p.Key)); File.Copy(source,p.Key,true);
                }
                Verify(); // No AssetDatabase refresh on recovery: editor must exit, then coordinator rechecks bytes.
            }
            public void Record(string status,string error)
            {
                Directory.CreateDirectory(_backup);
                File.WriteAllText(Path.Combine(_backup,"transaction.json"),new JObject
                {
                    ["status"] = status, ["error"] = error, ["post_editor_exit_hash_check_required"] = true,
                    ["files"] = new JArray(_hashes.Select(p => new JObject { ["path"] = p.Key,["sha256"] = p.Value }))
                }.ToString());
                Debug.Log("CTLIFE [terminal-cafe] protection receipt: " + Path.Combine(_backup,"transaction.json"));
            }
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
