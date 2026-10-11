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
    public static class TerminalCafeCandidateB3Hits
    {
        public const string Output = "Assets/Art/Candidates/TerminalCafe20261006B3Hits";
        public const string PrefabPath = Output + "/TerminalCafeB3Hits.prefab";
        private const string Canonical = "Assets/Art/Generated/Venues/TimelineSquare.prefab";
        private const string Snapshot = "Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest = "Assets/Art/environment/manifest.json";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly List<Bounds> Solids = new List<Bounds>();
        private static readonly Dictionary<string,List<GameObject>> SeatFurniture = new Dictionary<string,List<GameObject>>();
        private static readonly JArray Placements = new JArray();
        private static JObject _manifest;
        private static Transform _root;
        private static ProtectedFiles _protection;
        private static int _joineryTriangles;

        [MenuItem("CT Life/Candidates/Stage Terminal Cafe B3 Hits")]
        public static void BuildCandidate() => Protected("stage", Stage);

        [MenuItem("CT Life/Candidates/Bake Terminal Cafe B3 Hits")]
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
            _manifest = JObject.Parse(File.ReadAllText(Manifest)); Materials.Clear(); Solids.Clear(); SeatFurniture.Clear(); Placements.Clear(); _joineryTriangles=0;
            var data = JObject.Parse(File.ReadAllText(Snapshot));
            var registries = Registries.Parse(data["ids"],data["config"]);
            var spots = registries.SpotsFor("terminal_cafe");
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
                venue.venueId = "terminal_cafe";
                if (!registries.Venues.TryGetValue(venue.venueId, out var definition)) throw new InvalidDataException("Cafe catalog identity missing.");
                venue.districtId = definition.districtId;
                venue.cameraBounds = new Rect(-12,-9,24,18);
                venue.cameraDistance = 10.8f; venue.cameraPitch = 30f; venue.cameraYaw = -12f;
                venue.cameraFieldOfView = 45f; venue.cameraTargetViewportY = .30f; venue.cameraFocusHeight = 1;
                var lighting = go.AddComponent<VenueContentLighting>();
                lighting.sunIntensity=.72f;lighting.sunColor=new Color(1,.91f,.81f);lighting.sunAngles=new Vector3(42,-25,0);
                lighting.sky=new Color(.54f,.63f,.68f);lighting.equator=new Color(.55f,.53f,.47f);lighting.ground=new Color(.28f,.24f,.20f);
                var floor = Group("Warm wood floor and stone coffee apron");
                for (int x = -11; x < 12; x += 2) for (int z = -8; z < 9; z += 2)
                    Add("floor_wood_2m",x,z,y:-.101f,parent:floor);
                var ground = new GameObject("Contract floor collision"); ground.transform.SetParent(_root,false);
                ground.transform.localPosition = new Vector3(0,-.1f,0); ground.AddComponent<BoxCollider>().size = new Vector3(24,.2f,18);

                var walls = Group("Brick display wall and open window wall");
                for (int x = -11; x < 12; x += 2) Add("wall_plaster_solid_2m",x,9.1f,parent:walls,heightScale:1.35f);
                for (int z = -8; z < 9; z += 2)
                {
                    var window = Add(z % 4 == 0 ? "wall_plaster_window_2m" : "wall_plaster_solid_2m",-12.1f,z,yaw:-90,parent:walls,heightScale:1.35f);
                    if (z % 4 == 0) OpenWindowGlass(window);
                    Add("wall_plaster_solid_2m",12.1f,z,yaw:90,parent:walls,heightScale:z>=0?1.35f:.21f);
                }
                // Cutaway front keeps the actor readable and leaves a four-metre entrance passage.
                for (int x = -11; x < 12; x += 2) if (Mathf.Abs(x) >= 3)
                    Add("wall_plaster_solid_2m",x,-9.1f,parent:walls,heightScale:.14f);
                CombineStatic(floor,"CafeFloor"); CombineStatic(walls,"CafeWalls");

                // All eight claimed terminal anchors are seated typing stations, not standing kiosk art.
                var terminalRows = new JArray();
                string[] charts = { "CafeDesktop" };
                int index = 0;
                foreach (var spot in terminals.OrderBy(s => s.id))
                {
                    if (!spot.hasPosition || Mathf.Abs(spot.rotY) > .001f || spot.anim != "act_type")
                        throw new InvalidDataException("Cafe station layout expects positioned north-facing act_type anchors: " + spot.id);
                    SeatFurniture[spot.id] = new List<GameObject> {
                        Add("home_desk",spot.x,spot.z+.86f,solid:true),
                        Add("office_chair",spot.x,spot.z-.10f,yaw:180,solid:true) };
                    Add("monitor_triple",spot.x,spot.z+1.03f,y:.76f,screen:charts[index%charts.Length]);
                    Add("laptop",spot.x-.40f,spot.z+.69f,y:.76f,yaw:14);
                    Add("cup_coffee",spot.x+.60f,spot.z+.68f,y:.76f);
                    Add("desk_lamp",spot.x-.60f,spot.z+1.0f,y:.76f);
                    terminalRows.Add(new JObject { ["spot"]=spot.id,["x"]=spot.x,["z"]=spot.z,["yaw_radians"]=spot.rotY,["animation"]=spot.anim });
                    index++;
                }
                // Compact adult tables retain all four fixed social seats without a classroom-sized bridge.
                var joinery=Group("Cafe architectural joinery and quiet floor zones");
                foreach(var spot in seats)
                {
                    if(!spot.hasPosition||Mathf.Abs(spot.rotY)>.001f)throw new InvalidDataException("Unexpected social seat orientation "+spot.id);
                    // One shared physical chair for both looks. Native support target=.450m, fixed floor contact.
                    // Pose contact remains a separate joint motion/art gate; no actor/wire-anchor offset.
                    SeatFurniture[spot.id] = new List<GameObject> {
                        Add("home_chair_1",spot.x,spot.z-.30f,yaw:180,scale:1.08f,heightScale:.450f/.428800762f,solid:true),
                        Add("home_table_roundsmall",spot.x,spot.z+1.20f,solid:true) };
                    Add("cup_coffee",spot.x-.22f,spot.z+.99f,y:.75f);
                    Add("croissant",spot.x+.17f,spot.z+1.17f,y:.75f);
                    Add("plant_small",spot.x,spot.z+1.48f,y:.75f,scale:.60f);
                }
                // Workstations are individual furniture; tall greenery breaks up the rigid back rows.
                foreach(float z in new[]{4.0f,8.0f})foreach(float x in new[]{-5f,5f})
                    Add("houseplant_tall",x,z,scale:.85f,solid:true);
                // Sage cabinetry and stone worktop replace the oversized orange restaurant display.
                for(int i=0;i<9;i++)Add("home_kitchen_cabinet1",-8.9f,-7.15f+i*.61f,yaw:90,solid:true);
                Block("Coffee counter worktop",new Vector3(-8.9f,.98f,-4.71f),new Vector3(.78f,.06f,5.55f),"WarmStone",new Color(.76f,.74f,.66f),joinery);
                Add("coffee_machine",-8.9f,-5.9f,y:1.01f,yaw:90);
                Add("cash_register",-8.9f,-2.3f,y:1.01f,yaw:90);
                foreach(float z in new[]{-4.6f,-4.3f,-4.0f})Add("croissant",-8.85f,z,y:1.01f,yaw:90);
                Add("home_kitchen_fridge",-11.35f,-2.2f,yaw:90,solid:true);
                // A shallow backbar gives the counter a readable backdrop; the existing NPC foot anchor stays clear.
                Block("Backbar return",new Vector3(-10.35f,1.45f,-1.34f),new Vector3(3.20f,2.90f,.14f),"BackbarSage",new Color(.25f,.33f,.29f),joinery,true);
                Block("Counter stone apron",new Vector3(-9.85f,.004f,-4.7f),new Vector3(3.60f,.008f,6.65f),"WarmStone",new Color(.76f,.74f,.66f),joinery);
                Panel("CounterMenu",-9.63f,1.10f,-1.44f,0,1.36f,1.904f,"CafeMenu");
                foreach(float y in new[]{.68f,1.34f})
                    Add("home_shelf_b_large_decorated",-11.20f,-1.46f,y:y,yaw:180,scale:.82f);

                // Lower wall joinery and repeated window reveals give this large contract room a clear interior envelope.
                foreach(float x in new[]{-11.92f,11.92f})
                    Block("Side skirting",new Vector3(x,.14f,0),new Vector3(.08f,.28f,18),"Charcoal",new Color(.12f,.16f,.16f),joinery);
                Block("Rear picture rail",new Vector3(0,3.7f,8.98f),new Vector3(24,.08f,.08f),"Brass",new Color(.58f,.44f,.25f),joinery);
                for(int i=0;i<5;i++)Block("Window timber mullion",new Vector3(-11.93f,2.025f,-8+i*4),new Vector3(.08f,4.05f,.06f),"Walnut",new Color(.39f,.29f,.20f),joinery);
                foreach(float x in new[]{-5f,5f})
                {
                    foreach(float z in new[]{3.5f,7.5f})
                        Block("Terminal carpet field",new Vector3(x,.003f,z),new Vector3(7,.006f,2.30f),"PetrolCarpet",new Color(.24f,.32f,.31f),joinery);
                    Add("home_light_chandelier",x,-2.85f,y:3.20f,scale:.80f);
                    BakedPoint("Cafe pendant",new Vector3(x,3.14f,-2.85f),new Color(1,.83f,.63f),2.5f,8);
                }
                // One primary identity, intentionally low enough to remain in the arrival frame.
                Panel("CoffeeIdentity",0,.48f,8.98f,0,5.50f,2.40625f,"CafeIdentity");
                foreach(float x in new[]{-11.83f,11.83f})
                {
                    Block("Reading shelf backing",new Vector3(x,1.35f,.7f),new Vector3(.10f,2.70f,1.65f),"Walnut",new Color(.39f,.29f,.20f),joinery,true);
                    foreach(float y in new[]{.40f,1.10f,1.80f})
                        Add("home_shelf_b_large_decorated",x+(x<0?.13f:-.13f),.7f,y:y,yaw:x<0?90:-90,scale:1.10f);
                    Add("houseplant_tall",x<0?-10.65f:10.65f,7.6f,scale:.95f,solid:true);
                }
                // These reading sofas are decorative furniture, not additional claimed multiplayer seats.
                foreach(float side in new[]{-1f,1f})
                {
                    Block("Reading rug",new Vector3(side*4.95f,.006f,-7.0f),new Vector3(3.30f,.012f,3.30f),"ReadingPetrol",new Color(.28f,.34f,.33f),joinery);
                    Add("home_couch_medium1",side*5.6f,-7.0f,yaw:side<0?90:-90,solid:true);
                    Add("home_table_roundsmall",side*4.12f,-7.0f,scale:.70f,solid:true);
                    Add("cup_coffee",side*4.12f,-7.0f,y:.525f);
                    Add("home_light_floor1",side*6.5f,-8.15f,solid:true);
                    Add("home_houseplant_1",side*6.65f,-5.2f,scale:.80f,solid:true);
                    BakedPoint("Reading lamp",new Vector3(side*6.5f,1.7f,-8.15f),new Color(1,.79f,.56f),1.7f,5.5f);
                }
                // Walnut lower walls soften the room without obstructing its windows or the central route.
                foreach(float x in new[]{-11.99f,11.99f})
                    Block("Timber wainscot",new Vector3(x,.42f,0),new Vector3(.06f,.84f,18),"Walnut",new Color(.39f,.29f,.20f),joinery);
                Block("Rear timber wainscot",new Vector3(0,.22f,8.99f),new Vector3(24,.44f,.06f),"Walnut",new Color(.39f,.29f,.20f),joinery);
                CombineStatic(joinery,"CafeJoinery");
                BakedPoint("Counter practical",new Vector3(-9.0f,3.4f,-4.8f),new Color(1,.83f,.62f),2.4f,7);
                BakedSpot("Window fill",new Vector3(-11.5f,3.4f,-1),new Vector3(0,.5f,0),new Color(.65f,.78f,1),2.1f,22,95);

                // A grounded street threshold/window garden surrounds the cutaway; only the unchanged contract floor is navigable.
                var outside=Group("Grounded cafe threshold and window garden");
                for(int x=-17;x<=17;x+=2)for(int z=-16;z<=10;z+=2)
                    if(x < -12 || x > 12 || z < -9 || z > 9)Add("plaza_hexpaving_2m",x,z,y:-.105f,parent:outside);
                CombineStatic(outside,"CafeSurroundings");
                foreach(float z in new[]{-6f,1f,7f})
                {
                    Add("planter_sidewalk",-14.2f,z);
                    Add("tree_blossom_4",-14.2f,z,y:.28f,scale:.72f);
                }
                Add("planter_sidewalk",-5.5f,-11.0f,yaw:90);
                Add("planter_sidewalk",5.5f,-11.0f,yaw:90);
                Add("bush_large_flowers",-5.5f,-11.0f,y:.35f,scale:.55f);
                Add("bush_large_flowers",5.5f,-11.0f,y:.35f,scale:.55f);

                var anchors = Group("Exact contract anchors");
                foreach (var spot in spots.spots.Values.Distinct().Where(s=>s.hasPosition))
                {
                    var anchor = new GameObject("spot_"+spot.id); anchor.transform.SetParent(anchors,false);
                    anchor.transform.localPosition = new Vector3(spot.x,0,spot.z);
                    anchor.transform.localRotation = Quaternion.Euler(0,spot.rotY*Mathf.Rad2Deg,0);
                    var a = anchor.AddComponent<SpotAnchor>(); a.spotId=spot.id; a.kind=spot.kind; a.anim=spot.anim??"";
                    if (spot.Claimable)
                    {
                        var approach = new GameObject("Approach_" + spot.id);
                        approach.transform.SetParent(_root, false);
                        approach.transform.localPosition = new Vector3(spot.x + (spot.kind == "terminal" ? 1.35f : .95f), 0,
                            spot.z + (spot.kind == "terminal" ? -.10f : -.30f));
                        a.approach = approach.transform;
                        if (!SeatFurniture.TryGetValue(spot.id, out var furniture) || furniture.Count != 2)
                            throw new InvalidDataException("Claimable furniture missing for " + spot.id);
                        foreach (var item in furniture) item.transform.SetParent(anchor.transform, true);
                    }
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
                    ["interaction_tagged_spots"]=SeatFurniture.Count,["interaction_mesh_colliders"]=go.GetComponentsInChildren<MeshCollider>(true).Length,
                    ["additional_joinery_triangles"]=_joineryTriangles,["static_mesh_renderers"]=go.GetComponentsInChildren<MeshRenderer>(true).Length,["baked_only_lights"]=6,
                    ["limitations"]="B3 SOCIAL + HIT GEOMETRY CANDIDATE ONLY. Tall navigation bounds never become invisible furniture colliders. Existing SpotAnchor ancestry tags real chair/table geometry; requires explicit-target WorldInput patch and native camera-ray review.  Shared social chair top .450m and center z=anchor-.30m require corrected per-look clips and native contact review. Workstation furniture remains B2 pending hand/foot solve. Chairs now exclude walking navigation; 12 authored approaches require native runtime seat review. Identity, menu and coffee screensavers are decorative artwork. Reading sofas add no claimed seats. No registry promotion, server change or performance acceptance."
                }.ToString());
                Debug.Log("CTLIFE [terminal-cafe-b3-hits] 8 terminal stations and 4 social seats staged at exact contract anchors; bake required: "+PrefabPath);
            }
            finally { _root=null; EditorSceneManager.ClosePreviewScene(preview); Materials.Clear(); }
        }
        private static void Panel(string name, float x, float y, float z, float yaw, float width, float height, string screen)
        {
            var group = new GameObject("Fascia_" + name);
            group.transform.SetParent(_root, false);
            group.transform.localPosition = new Vector3(x,y,z);
            group.transform.localRotation = Quaternion.Euler(0,yaw,0);
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame"; frame.transform.SetParent(group.transform, false);
            frame.transform.localPosition = new Vector3(0,height*.5f,0);
            frame.transform.localScale = new Vector3(width+.12f,height+.12f,.14f);
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.GetComponent<MeshFilter>().sharedMesh = Save(Object.Instantiate(frame.GetComponent<MeshFilter>().sharedMesh), Output + "/Meshes/" + name + "_FasciaFrame.asset");
            const string materialKey = "FasciaCharcoal";
            if (!Materials.TryGetValue(materialKey, out var trim))
            {
                var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null) throw new InvalidDataException("Required fascia shader missing.");
                trim = new Material(shader) { name = materialKey, enableInstancing = true };
                trim.SetColor("_BaseColor",new Color(.085f,.11f,.13f)); trim.SetFloat("_Smoothness",.12f);
                Materials[materialKey] = trim = Save(trim,Output + "/Materials/" + materialKey + ".mat");
            }
            frame.GetComponent<MeshRenderer>().sharedMaterial = trim;
            var mesh = new Mesh { name = name + "_FasciaFace" };
            mesh.vertices = new[] { new Vector3(-width*.5f,0,-.081f), new Vector3(-width*.5f,height,-.081f),
                new Vector3(width*.5f,height,-.081f), new Vector3(width*.5f,0,-.081f) };
            mesh.triangles = new[] {0,1,2,0,2,3}; mesh.uv = new[] {Vector2.zero,Vector2.up,Vector2.one,Vector2.right};
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var face = new GameObject("Identity face"); face.transform.SetParent(group.transform,false);
            face.AddComponent<MeshFilter>().sharedMesh = Save(mesh,Output + "/Meshes/" + mesh.name + ".asset");
            face.AddComponent<MeshRenderer>().sharedMaterial = Resolve("KEEP_screen",_manifest["billboard_wall"],screen);
            foreach (var renderer in group.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.gameObject.isStatic = true; renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false; renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Placements.Add(new JObject { ["id"] = "authored_fascia_" + name, ["x"] = x, ["y"] = y, ["z"] = z,
                ["yaw"] = yaw, ["width"] = width, ["height"] = height, ["screen"] = screen, ["solid"] = false,
                ["source"] = "project-authored shallow frame and original Cafe B2 artwork at source aspect ratio", ["lod0_triangles"] = 14 });
        }

        private static GameObject Block(string name,Vector3 position,Vector3 size,string key,Color color,Transform parent,bool solid=false)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=size;go.isStatic=true;Object.DestroyImmediate(go.GetComponent<Collider>());
            if(!Materials.TryGetValue(key,out var material))
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit")){name=key,enableInstancing=true};
                material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.08f);Materials[key]=material=Save(material,Output+"/Materials/"+key+".mat");
            }
            go.GetComponent<MeshRenderer>().sharedMaterial=material; _joineryTriangles+=12;
            if(solid)
            {
                Solids.Add(new Bounds(new Vector3(position.x,1.2f,position.z),new Vector3(size.x,2.4f,size.z)));
                // Keep authored block hit geometry at its real height after visual meshes combine.
                SolidHit(new Bounds(position,size));
            }
            return go;
        }
        private static void NeutralSurface(GameObject item,string key,Color color)
        {
            if(!Materials.TryGetValue(key,out var material))
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit")){name=key,enableInstancing=true};
                material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.10f);Materials[key]=material=Save(material,Output+"/Materials/"+key+".mat");
            }
            foreach(var renderer in item.GetComponentsInChildren<MeshRenderer>())renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>material).ToArray();
        }
        private static void BakedPoint(string name,Vector3 position,Color color,float intensity,float range)
        {
            var go=new GameObject(name);go.transform.SetParent(_root,false);go.transform.localPosition=position;
            var light=go.AddComponent<Light>();light.type=LightType.Point;light.lightmapBakeType=LightmapBakeType.Baked;
            light.color=color;light.intensity=intensity;light.range=range;light.shadows=LightShadows.Soft;
        }
        private static void BakedSpot(string name,Vector3 position,Vector3 target,Color color,float intensity,float range,float angle)
        {
            var go=new GameObject(name);go.transform.SetParent(_root,false);go.transform.localPosition=position;
            go.transform.rotation=Quaternion.LookRotation(target-position,Vector3.up);
            var light=go.AddComponent<Light>();light.type=LightType.Spot;light.lightmapBakeType=LightmapBakeType.Baked;
            light.color=color;light.intensity=intensity;light.range=range;light.spotAngle=angle;light.shadows=LightShadows.Soft;
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
                // Tall bounds affect navigation only. Click geometry follows actual source LOD0 triangles.
                Solids.Add(new Bounds(new Vector3(bounds.center.x, 1.2f, bounds.center.z), new Vector3(bounds.size.x, 2.4f, bounds.size.z)));
                foreach (var renderer in renderers.Where(r => Lod(r.name) == 0))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) throw new InvalidDataException("Missing hit mesh " + renderer.name);
                    var collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh; collider.convex = false;
                }
            }
            Placements.Add(new JObject { ["id"] = id, ["x"] = x, ["y"] = y, ["z"] = z, ["yaw"] = yaw, ["scale"] = scale, ["height_scale"] = heightScale,
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
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/direction-20261006/terminal-cafe-b2/Textures", id + ".png"));
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
        private static void SolidHit(Bounds bounds)
        {
            var go = new GameObject("Direction actual block hit"); go.transform.SetParent(_root, false); go.transform.localPosition = bounds.center;
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
                    if (!venue.TryGetSpotApproach(spot.id, out var approach)) throw new InvalidDataException("No approach for " + spot.id);
                    var point = approach + offset;
                    if (!NavMesh.SamplePosition(point,out var hit, spot.Claimable ? .35f : .75f,NavMesh.AllAreas))
                        throw new InvalidDataException("Blocked approach " + spot.id);
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidDataException("No complete path to " + spot.id);
                    rows.Add(new JObject { ["spot"] = spot.id, ["sample_distance_m"] = Vector3.Distance(point,hit.position), ["path"] = "PathComplete",
                        ["target"] = spot.Claimable ? "authored_frontend_approach" : "original_anchor",
                        ["approach_x"] = approach.x, ["approach_z"] = approach.z });
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
                _backup = Path.Combine(Path.GetTempPath(),"ctlife-terminal-cafe-b2-" + phase + "-" + Guid.NewGuid().ToString("N"));
                var sourceModels=JObject.Parse(File.ReadAllText(Manifest)).Properties().Select(p=>"Assets/Art/"+(string)p.Value["fbx"]).Where(File.Exists).ToArray();
                var paths=AssetDatabase.GetDependencies(sourceModels,true).Concat(AssetDatabase.GetDependencies(Canonical,true))
                    .Concat(Directory.GetFiles("Assets/Art/Candidates/TerminalCafe20261006B3Social","*",SearchOption.AllDirectories))
                    .Concat(new[] { Canonical,Snapshot,Manifest,"Assets/Resources/World/VenueContentRegistry.asset","Packages/manifest.json","Packages/packages-lock.json" })
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
                Debug.Log("CTLIFE [terminal-cafe-b3-hits] protection receipt: " + Path.Combine(_backup,"transaction.json"));
            }
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
