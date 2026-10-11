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
    /// <summary>Staged outside Assets. Root may import after stability gates; these never publish a venue.</summary>
    public static class SpacesStudioCandidateE
    {
        public const string PrefabPath = "Assets/Art/Candidates/SpacesStudio20261006E/SpacesStudioE.prefab";
        public static void BuildCandidate() => VenueExpansionCandidateE.Build("spaces_studio");
        public static void BakeCandidate() => VenueExpansionCandidateE.Bake("spaces_studio");
    }
    internal static class VenueExpansionCandidateE
    {
        private static string VenueId;
        private static bool IsStudio => VenueId == "spaces_studio";
        private static float HalfWidth => IsStudio ? 12 : 14;
        private static string Output => "Assets/Art/Candidates/"+(IsStudio ? "SpacesStudio20261006E" : "PerpPit20261006C");
        private static string PrefabPath => SpacesStudioCandidateE.PrefabPath;
        private static string ProposalRoot => Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/venue-expansion"));
        private const string Canonical = "Assets/Art/Generated/Venues/TimelineSquare.prefab";
        private const string Snapshot = "Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest = "Assets/Art/environment/manifest.json";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly List<Bounds> Solids = new List<Bounds>();
        private static readonly JArray Placements = new JArray();
        private static readonly JArray ChairBackChecks = new JArray();
        private static readonly JArray CollisionWitnesses = new JArray();
        private static readonly Dictionary<string, SpotAnchor> SpotAnchors = new Dictionary<string, SpotAnchor>();
        private static readonly Dictionary<string,Mesh> UpholsteryMeshes = new Dictionary<string,Mesh>();
        private static JObject _manifest;
        private static Transform _root;
        private static ProtectedFiles _protection;
        private static int _bakedLights;
        private static int _detailTriangles;
        private const string Baseline = "Assets/Art/Candidates/SpacesStudio20261006D";
        private const string BaselinePrefabHash = "2b6f84d80ce7df1977675d9911c5dbe3c6bdb4ad12012892f5548ee1f0f8e288";
        private const string BaselineNavigationHash = "767cc92c6623316a7ab49544f0943c9440fb824f1d091c16ab260c10e40a1c48";
        private static readonly JArray Exterior = new JArray();

        private static void Select(string id)
        {
            if (_protection != null) throw new InvalidOperationException("Candidate transaction already active.");
            if (id != "spaces_studio") throw new ArgumentOutOfRangeException(nameof(id));
            VenueId = id;
        }
        public static void Build(string id)
        {
            Select(id);
            if(File.Exists(Output+"/StageReceipt.json"))throw new InvalidOperationException("Preserve this candidate receipt; use a reviewed new revision for regeneration.");
            Protected("stage",Stage);
        }
        public static void Bake(string id)
        {
            Select(id);
            Protected("bake",() =>
            {
                var receipt=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));
                if ((string)receipt["status"]!="staged_unbaked_not_published" || (string)receipt["snapshot_sha256"]!=Hash(Snapshot))
                    throw new InvalidDataException("Stage receipt absent or snapshot changed; restage deliberately.");
                VenueLightingBuilder.BakeVenue(PrefabPath,Output+"/Lighting");
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var maps=prefab!=null?prefab.GetComponent<VenueLightmaps>():null;
                if (maps==null || maps.colors.Length==0 || maps.bindings.Length==0) throw new InvalidDataException("No bound candidate lightmaps.");
                File.WriteAllText(Output+"/BakeReceipt.json",new JObject
                {
                    ["status"]="baked_not_published_requires_visual_and_device_review",["prefab"]=PrefabPath,
                    ["prefab_sha256"]=Hash(PrefabPath),["lightmaps"]=maps.colors.Length,["bound_renderers"]=maps.bindings.Length,
                    ["limitations"]="Art candidate only. No Space roles, live financial board or trading UI is enabled."
                }.ToString());
            });
        }
        private static VenueSpots ReadImmutableSpots()
        {
            var proof=JObject.Parse(File.ReadAllText(Path.Combine(ProposalRoot,"anchor-contract.json")));
            var data=JObject.Parse(File.ReadAllText(Snapshot));
            if ((string)proof["snapshot_sha256"]!=Hash(Snapshot) || !JToken.DeepEquals(proof["venues"]?[VenueId],data["ids"]?["spots"]?["venues"]?[VenueId]))
                throw new InvalidDataException("Frozen anchor proposal differs from current snapshot. Review contract before regeneration.");
            var spots=Registries.Parse(data["ids"],data["config"]).SpotsFor(VenueId);
            if (spots==null || spots.Entrance==null || spots.walkable.Count!=1) throw new InvalidDataException("Positioned venue contract required.");
            var rect=spots.walkable[0];
            if (rect.x0!=-HalfWidth || rect.x1!=HalfWidth || rect.z0!=-10 || rect.z1!=10)
                throw new InvalidDataException("Candidate bounds changed.");
            if (spots.spots.Values.Distinct().Count(s=>s.hasPosition)!=(IsStudio?34:13)) throw new InvalidDataException("Unexpected anchor count.");
            return spots;
        }
        private static void Stage()
        {
            if(Hash(Baseline+"/SpacesStudioD.prefab")!=BaselinePrefabHash || Hash(Baseline+"/Meshes/Navigation.asset")!=BaselineNavigationHash ||
                Hash(Baseline+"/BakeReceipt.json")!="36650a5d8356f3191b509fec0be8eaba4e168368bc2e53ec3353cdb3d136f368")
                throw new InvalidDataException("Preserve the reviewed D art/navigation comparison; baseline changed.");
            Exterior.Clear();
            var spots=ReadImmutableSpots();
            foreach (string sub in new[]{"Materials","Meshes","Textures","GeometryEvidence"}) Directory.CreateDirectory(Output+"/"+sub);
            AssetDatabase.Refresh();
            _manifest=JObject.Parse(File.ReadAllText(Manifest)); Materials.Clear(); Solids.Clear(); Placements.Clear();ChairBackChecks.Clear();UpholsteryMeshes.Clear();CollisionWitnesses.Clear();SpotAnchors.Clear();
            _bakedLights=0; _detailTriangles=0;
            var preview=EditorSceneManager.NewPreviewScene();
            try
            {
                var go=new GameObject(IsStudio?"Spaces Studio - isolated broadcast salon":"Perp Pit - isolated trading floor");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,preview); _root=go.transform;
                var venue=go.AddComponent<VenueSceneRoot>(); venue.venueId=VenueId; var catalog=JObject.Parse(File.ReadAllText(Snapshot));
                venue.districtId=Registries.Parse(catalog["ids"],catalog["config"]).Venues[VenueId].districtId;
                venue.cameraBounds=new Rect(-HalfWidth,-10,HalfWidth*2,20);
                venue.cameraDistance=IsStudio?11.6f:12.2f; venue.cameraPitch=29; venue.cameraYaw=-12;
                venue.cameraFieldOfView=45; venue.cameraTargetViewportY=.30f; venue.cameraFocusHeight=1;
                var lighting=go.AddComponent<VenueContentLighting>();
                lighting.sunColor=IsStudio?new Color(1,.92f,.82f):new Color(.91f,.96f,1);
                lighting.sunIntensity=IsStudio?.78f:.8f; lighting.sunAngles=new Vector3(36,-65,0);
                lighting.sky=new Color(.43f,.57f,.65f); lighting.equator=new Color(.42f,.44f,.43f); lighting.ground=new Color(.19f,.21f,.21f);
                lighting.haze=new Color(.36f,.45f,.47f);
                var anchors=Group("Exact immutable anchors");
                foreach(var spot in spots.spots.Values.Distinct().Where(s=>s.hasPosition))
                {
                    var anchor=new GameObject("spot_"+spot.id); anchor.transform.SetParent(anchors,false);
                    anchor.transform.localPosition=new Vector3(spot.x,0,spot.z);
                    anchor.transform.localRotation=Quaternion.Euler(0,spot.rotY*Mathf.Rad2Deg,0);
                    var a=anchor.AddComponent<SpotAnchor>();a.spotId=spot.id;a.kind=spot.kind;a.anim=spot.anim??"";
                    SpotAnchors.Add(spot.id,a);
                    if (!string.IsNullOrEmpty(a.anim))
                    {
                        // All points are frontend-only, beside the physical furniture. Wire pose is unchanged.
                        var approach=new GameObject("Approach");approach.transform.SetParent(anchor.transform,false);
                        approach.transform.position=anchor.transform.position+new Vector3(spot.id.EndsWith("stage_host",StringComparison.Ordinal)?1.05f:.95f,0,0);
                        a.approach=approach.transform;
                    }
                }
                BuildShell();
                BuildStudio(spots);
                RequireBaselinePhysicalLayout();
                int tagged=SpotAnchors.Values.Count(a=>!string.IsNullOrEmpty(a.anim)&&a.GetComponentsInChildren<MeshCollider>(true).Length>0);
                if(tagged!=31)throw new InvalidDataException("Expected 30 source chair targets and one physical host podium target.");
                venue.navigation=Navigation(spots); venue.Init(spots);
                var nav=CheckNavigation(venue,spots); CheckMaterials(go);
                foreach(var spot in spots.spots.Values.Distinct().Where(s=>s.hasPosition))
                {
                    if(!venue.TryGetSpot(spot.id,out var point,out var angle) || Vector3.Distance(point,new Vector3(spot.x,0,spot.z))>.0001f || Mathf.Abs(Mathf.DeltaAngle(angle*Mathf.Rad2Deg,spot.rotY*Mathf.Rad2Deg))>.001f)
                        throw new InvalidDataException("Moved immutable anchor "+spot.id);
                }
                if(PrefabUtility.SaveAsPrefabAsset(go,PrefabPath)==null)throw new IOException("Candidate save failed.");
                SaveCandidateAssets();
                File.WriteAllText(Output+"/StageReceipt.json",new JObject
                {
                    ["status"]="staged_unbaked_not_published",["venue_id"]=VenueId,["prefab"]=PrefabPath,["snapshot_sha256"]=Hash(Snapshot),
                    ["visual_revision"]="E: unobstructed room identity, flush woven audience field, sourced city outlook",
                    ["baseline_prefab_sha256"]=BaselinePrefabHash,["navigation_reused_sha256"]=BaselineNavigationHash,["physical_layout"]="exact D source furniture/collider/anchor/approach equality",
                    ["exterior_visual_bounds"]=Exterior.DeepClone(),["audience_floor_extent_xz"]=new JArray(-8,-10,8,0),["floor_geometry_and_height"]="unchanged source tiles; material only; no raised rug",
                    ["positioned_anchors"]=Anchors(go).Count,["authored_approaches"]=SpotAnchors.Values.Count(a=>a.approach!=null),["tagged_geometry_spots"]=tagged,["truthful_collision_geometry"]=CollisionWitnesses.DeepClone(),["mesh_colliders"]=go.GetComponentsInChildren<MeshCollider>(true).Length,["native_chair_back_checks"]=ChairBackChecks.DeepClone(),["navigation"]=nav,["placements"]=Placements.DeepClone(),
                    ["source_lod0_triangles_manifest_estimate"]=Placements.Sum(p=>(int)p["lod0_triangles"]),["additional_trim_triangles"]=_detailTriangles,
                    ["static_mesh_renderers"]=go.GetComponentsInChildren<MeshRenderer>(true).Length,["baked_only_lights"]=_bakedLights,
                    ["limitations"]="Unpublished art. Native source mesh collision/side approach is staged only. Adult chair/body contact, actual tap and walk, landscape framing, real room occupancy, host/speaker authority, voice permission and device performance require validation. No financial or host-role behavior enabled."
                }.ToString());
                Debug.Log("CTLIFE [venue-expansion] Staged "+VenueId+" with "+Anchors(go).Count+" exact anchors; bake and review required: "+PrefabPath);
            }
            finally{_root=null;EditorSceneManager.ClosePreviewScene(preview);Materials.Clear();}
        }
        private static void BuildShell()
        {
            var floor=Group("Continuous floor with deliberate activity insets");
            for(int x=(int)-HalfWidth+1;x<HalfWidth;x+=2)for(int z=-9;z<10;z+=2)
            {
                var tile=Add(IsStudio?"floor_wood_2m":"floor_concrete_2m",x,z,y:-.101f,parent:floor);
                if(!IsStudio)SolidSurface(tile,"PitSlate",new Color(.27f,.30f,.31f));
                else if(Mathf.Abs(x)<=7 && z<=-1)WovenSurface(tile,"AudienceFloorWool",new Color(.30f,.36f,.34f));
            }
            CombineStatic(floor,"Floor");
            var ground=new GameObject("Contract floor collision");ground.transform.SetParent(_root,false);
            ground.transform.localPosition=new Vector3(0,-.1f,0);ground.AddComponent<BoxCollider>().size=new Vector3(HalfWidth*2,.2f,20);
            var walls=Group("Full rear wall - glazed left wall - cutaway right and front");
            for(int x=(int)-HalfWidth+1;x<HalfWidth;x+=2)Add("wall_plaster_solid_2m",x,10.2f,parent:walls,heightScale:IsStudio?1.5f:1.65f);
            for(int z=-9;z<10;z+=2)
            {
                var window=Add("wall_plaster_window_2m",-HalfWidth-.14f,z,yaw:-90,parent:walls,heightScale:1.5f);OpenWindowGlass(window);
                Add("wall_plaster_solid_2m",HalfWidth+.14f,z,yaw:90,parent:walls,heightScale:z>=3?1.35f:.18f);
            }
            for(int x=(int)-HalfWidth+1;x<HalfWidth;x+=2)if(Mathf.Abs(x)>=3)
                Add("wall_plaster_solid_2m",x,-10.15f,parent:walls,heightScale:.14f);
            CombineStatic(walls,"Walls");
            // Non-walkable envelope only; contract-boundary walls do not consume anchors.
            Solid(new Bounds(new Vector3(-HalfWidth-.3f,1.2f,0),new Vector3(.3f,2.4f,20)));
            Solid(new Bounds(new Vector3(HalfWidth+.3f,1.2f,0),new Vector3(.3f,2.4f,20)));
            // Recessed garden outside open glass gives the side wall a destination and depth.
            var outside=Group("Continuous city threshold and window recess");
            for(int x=(int)-HalfWidth-5;x<=HalfWidth+5;x+=2)for(int z=-17;z<=13;z+=2)
                if(x < -HalfWidth || x > HalfWidth || z < -10 || z > 10)
                    Add("plaza_hexpaving_2m",x,z,y:-.101f,parent:outside);
            CombineStatic(outside,"CityThreshold");
            foreach(float x in new[]{-6f,6f})
            {
                Add("planter_sidewalk",x,-12.4f,yaw:90);
                Add("bush_large_flowers",x,-12.4f,y:.35f,scale:.6f);
            }
            foreach(float z in new[]{-5f,2f,8f})
            {
                Add("planter_sidewalk",-HalfWidth-2.3f,z,yaw:90);
                Add("bush_large_flowers",-HalfWidth-2.3f,z,y:.35f,scale:.8f);
            }
            var trim=Group("Architectural timber and brass reveals");
            foreach(float x in new[]{-HalfWidth+.16f,HalfWidth-.16f})
                Block("Side skirting",new Vector3(x,.12f,0),new Vector3(.09f,.24f,20),"DarkTimber",new Color(.18f,.16f,.13f),trim);
            Block("Rear picture rail",new Vector3(0,3.65f,9.98f),new Vector3(HalfWidth*2,.07f,.08f),"Brass",new Color(.57f,.43f,.24f),trim);
            CombineStatic(trim,"RoomTrim");
            // Licensed context beyond the unchanged walkable/collision envelope, never claimable destinations.
            ExteriorBuilding("building_low_a",-21.3f,6.8f,90);
            ExteriorBuilding("building_low_d",-21.3f,-5.2f,90);
            ExteriorBuilding("building_low_e",4.0f,18.0f,180);
        }
        private static void BuildStudio(VenueSpots spots)
        {
            var stage=Group("Flush stage field and acoustic backdrop");
            Block("Flush stage field",new Vector3(0,.0035f,7.2f),new Vector3(21,.005f,4.8f),"StageWalnut",new Color(.26f,.16f,.09f),stage);
            Block("Stage brass edge",new Vector3(0,.001f,4.8f),new Vector3(21,.004f,.055f),"Brass",new Color(.57f,.43f,.24f),stage);
            for(int i=0;i<36;i++)
                Block("Acoustic timber fin",new Vector3(-10.5f+i*.6f,2.15f,10.03f),new Vector3(.065f,4.1f,.17f),"WalnutFins",new Color(.28f,.18f,.12f),stage);
            // D's suspended beam crossed the identity at host/stage10 camera views. Remove only that non-colliding trim.
            CombineStatic(stage,"StudioStageAndAcoustics");
            foreach(var spot in spots.spots.Values.Distinct().Where(s=>!string.IsNullOrEmpty(s.anim)))
            {
                if(spot.id.EndsWith("stage_host",StringComparison.Ordinal))continue;
                bool audience=spot.kind=="seat";
                var chair=Add("lounge_chair",spot.x,spot.z,yaw:spot.rotY*Mathf.Rad2Deg+180);
                Upholstery(chair,audience?"AudiencePetrol":"SpeakerLinen",audience?new Color(.20f,.32f,.36f):new Color(.72f,.66f,.55f));
                CheckNativeChairBack(chair,spot);
                chair.transform.SetParent(SpotAnchors[spot.id].transform,true);
                FurnitureCollision(chair,true,spot.id);
                if(spot.id.EndsWith("seat_1",StringComparison.Ordinal)||spot.id.EndsWith("stage_1",StringComparison.Ordinal))
                    ExportSeatSource(chair,SpotAnchors[spot.id],spot.id.EndsWith("seat_1",StringComparison.Ordinal)?"audience-chair":"speaker-chair");
            }
            var podium=Add("podium",0,7.30f,yaw:180,solid:true,screen:"StudioMark");
            var host=SpotAnchors.Values.Single(a=>a.spotId.EndsWith("stage_host",StringComparison.Ordinal));
            podium.transform.SetParent(host.transform,true); // Physical claim target only; never grants host/voice permissions.
            Add("mic_stand",.48f,7.4f,yaw:180);
            Panel("StudioHeader",new Vector3(0,1.7f,9.98f),8.4f,1.4f,"StudioHeader");
            for(int i=0;i<2;i++)
            {
                float x=i==0?-10.75f:10.75f;
                Add("studio_speaker",x,4.35f,solid:true);
                foreach(float z in new[]{4.9f,8.8f})
                {
                    Add("studio_light",x,z,yaw:i==0?45:-45,solid:true);
                    BakedSpot("Studio practical",new Vector3(x,2.8f,z),new Vector3(0,1.1f,6.5f),new Color(1,.80f,.60f),3.4f,13,72);
                }
            }
            // The seat at (0,-8) is immutable: entry forks through ±1.5 m, side aisles remain clear.
            var wayfinding=Group("Flush entry and aisle reveals");
            foreach(float x in new[]{-1.5f,1.5f})Block("Entry fork",new Vector3(x,.002f,-6.7f),new Vector3(.04f,.004f,4.1f),"Brass",new Color(.57f,.43f,.24f),wayfinding);
            CombineStatic(wayfinding,"StudioAisleReveals");
            SideLounge(-9.4f,-5.1f,90); SideLounge(9.4f,-5.1f,-90);
            foreach(float x in new[]{-10.4f,10.4f})Add("home_houseplant_1",x,-1.3f,scale:1.0f,solid:true);
            foreach(float x in new[]{-10.3f,10.3f})
            {
                Add("home_light_floor1",x,-6.8f,solid:true);
                BakedPoint("Audience floor lamp",new Vector3(x,1.7f,-6.8f),new Color(1,.77f,.52f),1.8f,6);
            }
            BakedSpot("Cool window fill",new Vector3(-10.8f,3.7f,-.5f),new Vector3(0,.5f,0),new Color(.58f,.77f,1),2.4f,18,95);
        }
        private static void BuildPit(VenueSpots spots)
        {
            var joinery=Group("Paired terminal benches and overhead service frames");
            foreach(float z in new[]{4.82f,8.82f})
            {
                Block("Bench service ribbon",new Vector3(0,.24f,z+.48f),new Vector3(22,.48f,.12f),"DarkMetal",new Color(.08f,.12f,.13f),joinery);
                Block("Suspended amber frame",new Vector3(0,4.0f,z+.3f),new Vector3(22,.12f,.12f),"Brass",new Color(.57f,.43f,.24f),joinery);
            }
            CombineStatic(joinery,"PitBenchRibbons");
            foreach(var spot in spots.spots.Values.Distinct().Where(s=>s.kind=="terminal"))
            {
                if(spot.anim!="act_type"||Mathf.Abs(spot.rotY)>.001f)throw new InvalidDataException("Unexpected terminal pose "+spot.id);
                Add("desk_medium_scifi",spot.x,spot.z+.82f,solid:true);
                var chair=Add("office_chair_scifi",spot.x,spot.z-.1f,yaw:180);Surface(chair,"PitChair",new Color(.23f,.29f,.30f));
                Add("monitor_triple",spot.x,spot.z+.98f,y:.76f,screen:"IdlePanel");
                Add("cup_coffee",spot.x+.75f,spot.z+.61f,y:.76f);
            }
            foreach(float x in new[]{-12.65f,12.65f})
            {
                foreach(float z in new[]{3f,6f,9f})Add("server_rack",x,z,yaw:x<0?90:-90,solid:true);
                Add("locker",x,-1,yaw:x<0?90:-90,solid:true);
            }
            // Wall display lives beyond z=10; the leverage_board standing anchor is (0,9.5).
            foreach(float x in new[]{-8.4f,-4.2f,0,4.2f,8.4f})
                Add("wall_screen_large",x,10.05f,y:2.25f,screen:x==0?"PitIdentity":"IdlePanel");
            var graphic=Group("Flush liquidation witness circle");
            const int segments=48;
            for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments;
                var segment=Block("Witness circle",new Vector3(Mathf.Cos(a)*2.3f,.002f,-3+Mathf.Sin(a)*2.3f),new Vector3(.305f,.004f,.04f),"ClaySignal",new Color(.54f,.27f,.20f),graphic);
                segment.transform.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg+90,0);
            }
            CombineStatic(graphic,"PitWitnessCircle");
            SideLounge(-7.4f,-5.7f,90);SideLounge(7.4f,-5.7f,-90);
            foreach(float x in new[]{-10.2f,10.2f})Add("houseplant_tall",x,-5.7f,solid:true);
            foreach(float x in new[]{-9f,0f,9f})
            {
                Add("home_light_chandelier",x,1.2f,y:3.8f,scale:.65f);
                BakedPoint("Trading floor pendant",new Vector3(x,3.65f,1.2f),new Color(1,.83f,.57f),2.0f,8);
            }
            BakedSpot("Cool window fill",new Vector3(-12.5f,3.7f,-3),new Vector3(0,.3f,0),new Color(.58f,.77f,1),2.0f,20,100);
            BakedPoint("Board wall wash",new Vector3(0,4.3f,8.7f),new Color(.61f,.82f,.88f),1.5f,9);
        }
        private static void SideLounge(float x,float z,float yaw)
        {
            Add("home_couch_medium1",x,z,yaw:yaw,solid:true);
            float tableX=x+(x<0?1.5f:-1.5f);
            Add("home_table_roundsmall",tableX,z,scale:.7f,solid:true);
            Add("cup_coffee",tableX,z,y:.525f);
        }
        private static GameObject Block(string name,Vector3 position,Vector3 size,string materialKey,Color color,Transform parent)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=size;go.isStatic=true;Object.DestroyImmediate(go.GetComponent<Collider>());
            if(!Materials.TryGetValue(materialKey,out var material))
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit")){name=materialKey,enableInstancing=true};
                material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.1f);
                Materials[materialKey]=material=Save(material,Output+"/Materials/"+materialKey+".mat");
            }
            go.GetComponent<MeshRenderer>().sharedMaterial=material;_detailTriangles+=12;return go;
        }
        private static void ExteriorBuilding(string id,float x,float z,float yaw)
        {
            var item=Add(id,x,z,yaw:yaw);var renderers=item.GetComponentsInChildren<Renderer>(true);
            var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
            if(!(bounds.max.x<=-13f || bounds.min.z>=12.5f))throw new InvalidDataException("Background model enters held room envelope: "+id);
            if(item.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidDataException("Visual background must not change navigation/hits.");
            Exterior.Add(new JObject{["source"]=id,["min"]=Vector(bounds.min),["max"]=Vector(bounds.max),["colliders"]=0,["role"]="non-interactive licensed city outlook"});
        }
        private static Material WovenMaterial(string key,Color color)
        {
            var material=PlainMaterial(key,color);if(material.GetTexture("_BaseMap")!=null)return material;
            const string textureName="OriginalQuietWeave";string path=Output+"/Textures/"+textureName+".png";
            if(!File.Exists(path))
            {
                var texture=new Texture2D(128,128,TextureFormat.RGB24,false);
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                {
                    // Original deterministic two-thread weave. Restrained variation, no source atlas tinting.
                    float value=.93f+(((x/2+y/2)&1)==0?.035f:-.035f)+(x%2==0?.012f:-.012f);
                    texture.SetPixel(x,y,new Color(value,value,value));
                }
                texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.maxTextureSize=128;
                var android=importer.GetPlatformTextureSettings("Android");android.overridden=true;android.maxTextureSize=128;android.format=TextureImporterFormat.ASTC_8x8;importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
            }
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));material.SetFloat("_Smoothness",.02f);EditorUtility.SetDirty(material);return material;
        }
        private static void WovenSurface(GameObject item,string key,Color color)
        {
            var material=WovenMaterial(key,color);foreach(var renderer in item.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>material).ToArray();
        }
        private static void RequireBaselinePhysicalLayout()
        {
            var baseline=JObject.Parse(File.ReadAllText(Baseline+"/StageReceipt.json"));
            if(!JToken.DeepEquals(baseline["truthful_collision_geometry"],CollisionWitnesses))throw new InvalidDataException("E changed D furniture collision surfaces or placement.");
            var oldRoot=AssetDatabase.LoadAssetAtPath<GameObject>(Baseline+"/SpacesStudioD.prefab");
            var old=oldRoot.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a=>a.spotId);
            foreach(var pair in SpotAnchors)
            {
                if(!old.TryGetValue(pair.Key,out var prior))throw new InvalidDataException("Added wire spot.");
                var current=pair.Value;
                if(Vector3.Distance(current.transform.position,prior.transform.position)>1e-6f||Quaternion.Angle(current.transform.rotation,prior.transform.rotation)>1e-4f||current.kind!=prior.kind||current.anim!=prior.anim||
                    (current.approach==null)!=(prior.approach==null)||current.approach!=null&&Vector3.Distance(current.approach.position,prior.approach.position)>1e-6f)
                    throw new InvalidDataException("Changed held D spot or approach: "+pair.Key);
            }
            if(old.Count!=SpotAnchors.Count)throw new InvalidDataException("Removed wire spot.");
            File.WriteAllText(Output+"/VisualRevisionEvidence.json",new JObject{["baseline"]=Baseline,["baseline_prefab_sha256"]=BaselinePrefabHash,["unchanged_spots"]=old.Count,["unchanged_collision_witnesses"]=CollisionWitnesses.Count,["removed_trim"]="Suspended studio frame, non-colliding 12 triangles",["added_background"]=Exterior.DeepClone(),["lighting"]="same seven authored D lights, rebake E because exterior and materials changed"}.ToString());
        }
        private static Material PlainMaterial(string key,Color color)
        {
            if(Materials.TryGetValue(key,out var cached))return cached;
            var material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit")){name=key,enableInstancing=true};
            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.08f);
            return Materials[key]=Save(material,Output+"/Materials/"+key+".mat");
        }
        private static void SolidSurface(GameObject item,string key,Color color)
        {
            var material=PlainMaterial(key,color);
            foreach(var renderer in item.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>material).ToArray();
        }
        private static void Upholstery(GameObject item,string key,Color color)
        {
            // Split source triangles only by native height. Geometry/UVs remain unchanged;
            // a pink source atlas is not multiplied and mislabeled as petrol fabric.
            var fabric=WovenMaterial(key,color);
            var feet=PlainMaterial("ChairWalnutFeet",new Color(.28f,.20f,.13f));
            foreach(var filter in item.GetComponentsInChildren<MeshFilter>(true))
            {
                var original=filter.sharedMesh;
                string meshKey=key+"_"+original.name;
                if(!UpholsteryMeshes.TryGetValue(meshKey,out var mesh))
                {
                    var vertices=original.vertices.Select(filter.transform.TransformPoint).ToArray();
                    float low=vertices.Min(v=>v.y),cut=low+.14f;
                    var upper=new List<int>();var lower=new List<int>();var triangles=original.triangles;
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        var target=vertices[triangles[i]].y<=cut && vertices[triangles[i+1]].y<=cut && vertices[triangles[i+2]].y<=cut?lower:upper;
                        target.Add(triangles[i]);target.Add(triangles[i+1]);target.Add(triangles[i+2]);
                    }
                    if(upper.Count==0 || lower.Count==0)throw new InvalidDataException("Source chair no longer has the reviewed upholstery/foot split: "+original.name);
                    mesh=Readable(original);mesh.name=meshKey;mesh.subMeshCount=2;
                    mesh.SetTriangles(upper,0);mesh.SetTriangles(lower,1);
                    UpholsteryMeshes[meshKey]=mesh=Save(mesh,Output+"/Meshes/"+meshKey+".asset");
                }
                filter.sharedMesh=mesh;filter.GetComponent<MeshRenderer>().sharedMaterials=new[]{fabric,feet};
            }
        }
        private static void CheckNativeChairBack(GameObject chair,SpotDef spot)
        {
            var vertices=chair.GetComponentsInChildren<MeshFilter>(true).Where(f=>!f.name.Contains("_LOD")||f.name.Contains("_LOD0"))
                .SelectMany(f=>f.sharedMesh.vertices.Select(f.transform.TransformPoint)).ToArray();
            float low=vertices.Min(v=>v.y),high=vertices.Max(v=>v.y);
            var back=vertices.Where(v=>v.y>low+(high-low)*.8f).ToArray();
            Vector3 facing=Quaternion.Euler(0,spot.rotY*Mathf.Rad2Deg,0)*Vector3.forward;
            float distance=back.Average(v=>Vector3.Dot(v-chair.transform.position,facing));
            ChairBackChecks.Add(new JObject { ["spot"]=spot.id,["chair_yaw_degrees"]=chair.transform.eulerAngles.y,
                ["high_back_mean_along_avatar_forward_m"]=distance,["status"]=distance<-.05f?"back_behind_avatar_requires_pose_review":"orientation_rejected" });
            if(distance>=-.05f)throw new InvalidDataException("Native studio chair back is not behind its actor forward: "+spot.id);
        }
        private static void Panel(string name,Vector3 position,float width,float height,string texture)
        {
            var group=Group("Identity_"+name);group.localPosition=position;
            var frame=Block("Identity frame",new Vector3(0,height*.5f,0),new Vector3(width+.12f,height+.12f,.14f),"IdentityFrame",new Color(.09f,.13f,.14f),group);
            frame.GetComponent<MeshFilter>().sharedMesh=Save(Object.Instantiate(frame.GetComponent<MeshFilter>().sharedMesh),Output+"/Meshes/"+name+"_frame.asset");
            var mesh=new Mesh{name=name+"_face"};mesh.vertices=new[]{new Vector3(-width*.5f,0,-.081f),new Vector3(-width*.5f,height,-.081f),new Vector3(width*.5f,height,-.081f),new Vector3(width*.5f,0,-.081f)};
            mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
            var face=new GameObject("Identity face");face.transform.SetParent(group,false);face.isStatic=true;
            face.AddComponent<MeshFilter>().sharedMesh=Save(mesh,Output+"/Meshes/"+name+"_face.asset");
            var renderer=face.AddComponent<MeshRenderer>();renderer.sharedMaterial=Resolve("KEEP_screen",_manifest["wall_screen_large"],texture);renderer.shadowCastingMode=ShadowCastingMode.Off;
            _detailTriangles+=2;
            Placements.Add(new JObject { ["id"]="authored_panel_"+name,["x"]=position.x,["y"]=position.y,["z"]=position.z,
                ["width"]=width,["height"]=height,["screen"]=texture,["source"]="project-authored frame and original texture; no live activity claim",["lod0_triangles"]=0 });
        }
        private static void Surface(GameObject item,string key,Color tint)
        {
            foreach(var renderer in item.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>
            {
                string id=key+"_"+original.name;if(Materials.TryGetValue(id,out var cached))return cached;
                var copy=new Material(original){name=id};copy.SetColor("_BaseColor",tint);copy.SetFloat("_Smoothness",.05f);
                Materials[id]=Save(copy,Output+"/Materials/"+id+".mat");return Materials[id];
            }).ToArray();
        }
        private static void BakedPoint(string name,Vector3 position,Color color,float intensity,float range)
        {
            var go=new GameObject(name);go.transform.SetParent(_root,false);go.transform.localPosition=position;
            var light=go.AddComponent<Light>();light.type=LightType.Point;light.lightmapBakeType=LightmapBakeType.Baked;
            light.color=color;light.intensity=intensity;light.range=range;light.shadows=LightShadows.Soft;_bakedLights++;
        }
        private static void BakedSpot(string name,Vector3 position,Vector3 target,Color color,float intensity,float range,float angle)
        {
            BakedPoint(name,position,color,intensity,range);
            var go=_root.GetChild(_root.childCount-1);var light=go.GetComponent<Light>();light.type=LightType.Spot;light.spotAngle=angle;
            go.localRotation=Quaternion.LookRotation(target-position);
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
                var mesh=Readable(original); mesh.name="Venue candidate open window";
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
                // Bounds are bake-only exclusions; runtime hits use the source LOD0 triangles.
                FurnitureCollision(item,true,null);
            }
            Placements.Add(new JObject { ["id"] = id, ["x"] = x, ["y"] = y, ["z"] = z, ["yaw"] = yaw, ["scale"] = scale,
                ["screen"] = screen, ["solid"] = solid, ["height_scale"] = heightScale, ["source"] = (string)row["source"], ["lod0_triangles"] = (int)row["tris"][0] });
            return item;
        }
        private static void FurnitureCollision(GameObject item,bool excludeNavigation,string spot)
        {
            var filters=item.GetComponentsInChildren<MeshFilter>(true).Where(f=>Lod(f.name)==0&&f.sharedMesh!=null).ToArray();
            if(filters.Length==0)throw new InvalidDataException("No source collision geometry: "+item.name);
            var vertices=new List<Vector3>();int tris=0;
            foreach(var filter in filters)
            {
                var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;collider.convex=false;collider.isTrigger=false;
                vertices.AddRange(filter.sharedMesh.vertices.Select(filter.transform.TransformPoint));tris+=filter.sharedMesh.triangles.Length/3;
            }
            var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);
            if(excludeNavigation)Solids.Add(new Bounds(new Vector3(bounds.center.x,1.2f,bounds.center.z),new Vector3(bounds.size.x,2.4f,bounds.size.z)));
            CollisionWitnesses.Add(new JObject { ["item"]=item.name,["spot"]=spot,["triangles"]=tris,["bounds_min"]=Vector(bounds.min),["bounds_max"]=Vector(bounds.max),["nav_exclusion_only"]=excludeNavigation,["runtime_shape"]="source_lod0_mesh_nonconvex" });
        }
        private static JArray Vector(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static void ExportSeatSource(GameObject chair,SpotAnchor anchor,string name)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            foreach(var f in chair.GetComponentsInChildren<MeshFilter>(true).Where(f=>Lod(f.name)==0&&f.sharedMesh!=null))
            {
                int start=vertices.Count;vertices.AddRange(f.sharedMesh.vertices.Select(v=>anchor.transform.InverseTransformPoint(f.transform.TransformPoint(v))));
                triangles.AddRange(f.sharedMesh.triangles.Select(i=>start+i));
            }
            string N(float n)=>n.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            using(var writer=new StreamWriter(Output+"/GeometryEvidence/"+name+".obj"))
            {
                writer.WriteLine("# Exact native imported chair in immutable actor-anchor coordinates. Not body-contact acceptance.");
                foreach(var v in vertices)writer.WriteLine("v "+N(v.x)+" "+N(v.y)+" "+N(v.z));
                for(int i=0;i<triangles.Count;i+=3)writer.WriteLine("f "+(triangles[i]+1)+" "+(triangles[i+1]+1)+" "+(triangles[i+2]+1));
            }
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
            string key = display ? "Screen_" + (screen ?? "IdlePanel") : name;
            if (Materials.TryGetValue(key, out var cached)) return cached;
            var shader = Shader.Find(display ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidDataException("Required URP shader missing.");
            var material = new Material(shader) { name = key, enableInstancing = true };
            material.SetColor("_BaseColor", Color.white);
            Texture2D texture = null;
            if (display) texture = Screen(screen ?? "IdlePanel");
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
            string source = Path.GetFullPath(Path.Combine(ProposalRoot, "revision-b/Textures", id + ".png"));
            if(!File.Exists(source))source=Path.GetFullPath(Path.Combine(ProposalRoot,"Textures",id+".png"));
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
            // Exact saved D navigation is reused. E modifies only render composition outside held collider surfaces.
            string source=Baseline+"/Meshes/Navigation.asset",target=Output+"/Meshes/Navigation.asset";
            if(Hash(source)!=BaselineNavigationHash||File.Exists(target)||!AssetDatabase.CopyAsset(source,target)||Hash(target)!=BaselineNavigationHash)
                throw new InvalidDataException("Could not preserve exact D navigation bytes.");
            return AssetDatabase.LoadAssetAtPath<NavMeshData>(target);
        }
        private static JArray CheckNavigation(VenueSceneRoot venue, VenueSpots spots)
        {
            const string previous="Assets/Art/Candidates/SpacesStudio20261006D/Meshes/Navigation.asset";
            var baseline=AssetDatabase.LoadAssetAtPath<NavMeshData>(previous);
            if(baseline==null)throw new InvalidDataException("Preserve D's accepted .3m navigation for the paired visual revision.");
            var oldRows=ReadPathSet(baseline,spots);var rows=ReadPathSet(venue.navigation,spots);
            if(!JToken.DeepEquals(oldRows,rows))throw new InvalidDataException("E changed any D sampled navigation path.");
            File.WriteAllText(Output+"/NavigationDiagnostics.json",new JObject
            {
                ["status"]="native_same_anchor_path_comparison_not_body_collision_acceptance",
                ["baseline_source"]=previous,["baseline_sha256"]=Hash(previous),["baseline_bake_radius_m"]=.3f,
                ["candidate_bake_radius_m"]=.3f,["runtime_agent_radius_m"]=.3f,
                ["baseline"]=oldRows,["candidate"]=rows,
                ["nav_exclusion_bounds"]=new JArray(Solids.Select(v=>new JObject{["min"]=Vector(v.min),["max"]=Vector(v.max)}))
            }.ToString());
            foreach(var row in rows)
                if((string)row["path"]!="PathComplete")throw new InvalidDataException("No complete path to "+(string)row["spot"]+"; paired diagnostics preserved.");
            return rows;
        }
        private static JArray ReadPathSet(NavMeshData data,VenueSpots spots)
        {
            var offset=new Vector3(10000,0,10000);var instance=NavMesh.AddNavMeshData(data,offset,Quaternion.identity);var rows=new JArray();
            try
            {
                var p=new Vector3(spots.Entrance.x,0,spots.Entrance.z)+offset;
                if(!NavMesh.SamplePosition(p,out var start,.75f,NavMesh.AllAreas))throw new InvalidDataException("Entrance blocked.");
                foreach(var spot in spots.spots.Values.Distinct().Where(s=>s.hasPosition))
                {
                    var anchor=SpotAnchors[spot.id];var point=(anchor.approach!=null?anchor.approach.position:new Vector3(spot.x,0,spot.z))+offset;
                    float sampleRadius=anchor.approach!=null?.15f:.75f;
                    bool sampled=NavMesh.SamplePosition(point,out var hit,sampleRadius,NavMesh.AllAreas);var path=new NavMeshPath();
                    bool calculated=sampled&&NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path);
                    rows.Add(new JObject { ["spot"]=spot.id,["target_kind"]=anchor.approach!=null?"frontend_approach":"wire_anchor",
                        ["target"]=Vector(point-offset),["wire_anchor"]=Vector(new Vector3(spot.x,0,spot.z)),
                        ["sampled"]=sampled,["sample_distance_m"]=sampled?Vector3.Distance(point,hit.position):-1,
                        ["calculated"]=calculated,["path"]=calculated?path.status.ToString():"NoPath",
                        ["corners"]=new JArray(path.corners.Select(v=>Vector(v-offset))) });
                }
            }
            finally{if(instance.valid)NavMesh.RemoveNavMeshData(instance);}
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
                _backup = Path.Combine(Path.GetTempPath(),"ctlife-"+VenueId+"-" + phase + "-" + Guid.NewGuid().ToString("N"));
                var sourceModels = JObject.Parse(File.ReadAllText(Manifest)).Properties().Select(p => "Assets/Art/"+(string)p.Value["fbx"]).ToArray();
                var paths = AssetDatabase.GetDependencies(sourceModels,true).Concat(AssetDatabase.GetDependencies(Canonical,true)).Concat(new[] { Canonical,Snapshot,Manifest,"Assets/Resources/World/VenueContentRegistry.asset","Packages/manifest.json","Packages/packages-lock.json" })
                    .Concat(Directory.GetFiles("Assets/Art/Candidates","*",SearchOption.AllDirectories).Where(p=>!p.StartsWith(Output+"/",StringComparison.Ordinal))).Concat(Directory.GetFiles("ProjectSettings","*",SearchOption.AllDirectories)).Where(File.Exists).Distinct().ToList();
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
                Debug.Log("CTLIFE [venue-expansion] protection receipt: " + Path.Combine(_backup,"transaction.json"));
            }
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
