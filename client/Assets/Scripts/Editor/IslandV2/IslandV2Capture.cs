using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using CTLife.World.Vehicles;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Light verification captures of the built island (edit mode, no play): loads the base prefab, every cell and every
    /// venue diorama at its placement, applies the island atmosphere, and renders named views to PNG (landscape phone
    /// aspect), plus a top-down orthographic overview.
    /// Batch: Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.IslandV2.IslandV2Capture.Run
    ///        -ctlife-captureOut &lt;dir&gt; [-ctlife-views name,name]
    /// </summary>
    public static class IslandV2Capture
    {
        public struct View
        {
            public string Name; public Vector3 Eye, Target; public float Fov; public bool Ortho; public float OrthoSize; public bool Ground;
            public View(string n, Vector3 eye, Vector3 target, float fov = 50f, bool ground = false) { Name = n; Eye = eye; Target = target; Fov = fov; Ortho = false; OrthoSize = 0; Ground = ground; }
        }

        public static readonly View[] Views =
        {
            new View("overview", new Vector3(80f, 470f, -1050f), new Vector3(0f, 20f, 120f), 46f),
            new View("range_from_downtown", new Vector3(30f, 22f, -70f), new Vector3(-140f, 80f, 480f), 55f),
            new View("downtown_aerial", new Vector3(230f, 170f, -300f), new Vector3(0f, 0f, 10f), 50f),
            new View("bay", new Vector3(-30f, 60f, -520f), new Vector3(10f, 0f, -120f), 52f),
            new View("downtown_street", new Vector3(-150f, 1.9f, 30f), new Vector3(120f, 6f, 44f), 58f, true),
            new View("plaza", new Vector3(22f, 34f, -84f), new Vector3(0f, 0f, 8f), 55f),
            new View("ocean_drive", new Vector3(-170f, 2.2f, -226f), new Vector3(60f, 4f, -190f), 58f, true),
            new View("harbour_bridge", new Vector3(-400f, 16f, -486f), new Vector3(-510f, 2f, -400f), 55f),
            new View("trenches", new Vector3(-250f, 30f, -330f), new Vector3(-360f, 0f, -250f), 55f),
            new View("summit_view", new Vector3(-250f, 96f, 414f), new Vector3(0f, 0f, -120f), 55f),
            new View("mtndao_lodge", new Vector3(-170f, 96f, 360f), new Vector3(-255f, 74f, 412f), 55f),
            new View("switchbacks", new Vector3(-40f, 120f, 250f), new Vector3(-210f, 45f, 380f), 55f),
            new View("summit_aerial", new Vector3(-60f, 260f, 200f), new Vector3(-190f, 100f, 470f), 52f),
            new View("tunnel", new Vector3(752f, 22f, 196f), new Vector3(782f, 16f, 262f), 55f),
            new View("marina", new Vector3(470f, 46f, -500f), new Vector3(490f, 0f, -320f), 52f),
            new View("farm", new Vector3(520f, 40f, -60f), new Vector3(640f, 8f, 110f), 55f),
            new View("builder_hills", new Vector3(240f, 60f, 140f), new Vector3(380f, 20f, 320f), 55f),
            new View("breakpoint", new Vector3(-150f, 40f, 170f), new Vector3(-300f, 10f, 290f), 55f),
            new View("timeline_screens", new Vector3(46f, 9f, 22f), new Vector3(-6f, 7f, 50f), 55f),
            new View("boardwalk", new Vector3(-60f, 3.2f, -230f), new Vector3(60f, 0f, -222f), 58f, true),
            new View("marina_quay", new Vector3(520f, 8f, -420f), new Vector3(470f, -3f, -360f), 55f),
            new View("lighthouse", new Vector3(720f, 14f, -480f), new Vector3(676f, 12f, -532f), 50f),
            // world v3 (2026-10-22): Timeline Downtown re-dressed from the v3 kit
            new View("v3_boulevard", new Vector3(-112f, 1.8f, -99f), new Vector3(-50f, 7f, -43f), 58f, true),
            new View("v3_tower", new Vector3(40f, 28f, -150f), new Vector3(-96f, 45f, -9f), 55f),
            new View("v3_skyline", new Vector3(160f, 70f, -260f), new Vector3(-40f, 30f, 0f), 52f),
            new View("v3_side_street", new Vector3(-180f, 1.8f, -60f), new Vector3(-150f, 5f, 40f), 58f, true),
            // perf lane (2026-10-22): the hovercar's view crossing downtown at 80 m
            new View("perf_fly80", new Vector3(140f, 80f, -140f), new Vector3(-40f, 15f, 30f), 60f),
            // world v3 districts (2026-10-23): The Trenches, Residences, Dubai Marina at eye level and from the air
            new View("d_trenches", new Vector3(-332.5f, 1.7f, -279f), new Vector3(-283f, 6f, -288f), 60f, true),
            new View("d_trenches_air", new Vector3(-250f, 70f, -380f), new Vector3(-360f, 0f, -250f), 55f),
            new View("d_residences", new Vector3(-493.6f, 1.7f, 50.7f), new Vector3(-491f, 10f, 100f), 60f, true),
            new View("d_residences_air", new Vector3(-420f, 80f, -120f), new Vector3(-560f, 0f, 60f), 55f),
            new View("d_marina", new Vector3(560f, 1.7f, -262f), new Vector3(640f, 30f, -235f), 60f, true),
            // world polish (2026-10-23): the marina's east bank (the device route's spot), a park lawn and the park from the air
            new View("p_marina_east", new Vector3(690f, 1.7f, -335f), new Vector3(680f, 3f, -290f), 60f, true),
            new View("p_park", new Vector3(-30f, 1.7f, 70f), new Vector3(0f, 1f, 110f), 60f, true),
            new View("p_park_air", new Vector3(60f, 70f, 10f), new Vector3(-10f, 0f, 95f), 55f),
            new View("d_marina_air", new Vector3(470f, 90f, -470f), new Vector3(560f, 10f, -260f), 52f),
            // world districts 2 (2026-10-23): the semi-real forest at eye level and from the hovercar
            new View("f_summit_road", new Vector3(-305f, 40f, 380f), new Vector3(-285f, 44f, 432f), 60f),
            new View("f_north_highway", new Vector3(-147f, 25f, 660f), new Vector3(-80f, 28f, 640f), 60f),
            new View("f_slope_eye", new Vector3(-90f, 58f, 389f), new Vector3(-150f, 62f, 420f), 60f),
            new View("f_air80", new Vector3(-40f, 130f, 260f), new Vector3(-210f, 60f, 470f), 58f),
            new View("f_forest_close", new Vector3(-300f, 41f, 385f), new Vector3(-330f, 44f, 395f), 60f),
            new View("d2_hills_eye", new Vector3(330f, 1.7f, 300f), new Vector3(400f, 8f, 340f), 60f, true),
            new View("d2_farm_eye", new Vector3(560f, 1.7f, 60f), new Vector3(620f, 4f, 120f), 60f, true),
            new View("d2_lodge_close", new Vector3(-200f, 84f, 452f), new Vector3(-221f, 80f, 479f), 60f),
            new View("d2_helipad", new Vector3(-226f, 110f, 400f), new Vector3(-226f, 78f, 428f), 55f),
            new View("d2_perp_glass", new Vector3(-330f, 3f, -270f), new Vector3(-345f, 8f, -250f), 60f),
            new View("d2_islet", new Vector3(720f, 25f, -640f), new Vector3(800f, 2f, -650f), 55f),
            // world fix-ups (2026-10-23): the venue architecture, the mountain, the islet path, the poles, the Trenches lot
            new View("fx_cc", new Vector3(-268f, 3.5f, 277f), new Vector3(-262f, 24f, 316f), 60f, true),
            new View("fx_pp", new Vector3(-384f, 3.0f, -249f), new Vector3(-359f, 1f, -275f), 60f, true),
            new View("fx_lodge", new Vector3(-228f, 4.5f, 419f), new Vector3(-260f, 78f, 412f), 60f, true),
            new View("fx_hut", new Vector3(104f, 3.0f, -226f), new Vector3(96f, 1f, -258f), 60f, true),
            new View("fx_mtn_air", new Vector3(-60f, 170f, 330f), new Vector3(-182f, 100f, 513f), 55f),
            new View("fx_mtn_slope", new Vector3(-120f, 6f, 420f), new Vector3(-182f, 120f, 513f), 60f, true),
            new View("fx_islet_eye", new Vector3(802f, 1.8f, -598f), new Vector3(797f, 3f, -644f), 60f, true),
            new View("fx_poles", new Vector3(262f, 1.8f, 47f), new Vector3(340f, 9f, 66f), 60f, true),
            // district fix-ups 2: the device route's mountain and pole shots, the camera ~5 m behind and above the player
            new View("fx2_mtn_view", new Vector3(753.3f, 3.2f, -26.5f), new Vector3(489.2f, 60f, 126.0f), 60f, true),
            new View("fx2_poles", new Vector3(250.6f, 3.2f, 42.7f), new Vector3(308.0f, 6f, 73.2f), 60f, true),
            new View("fx2_ct_jail", new Vector3(91.9f, 3.0f, -80.5f), new Vector3(102.7f, 3.6f, -115.6f), 60f, true),
            new View("fx2_rekt_clinic", new Vector3(-368.1f, 3.0f, -340.2f), new Vector3(-391.6f, 3.1f, -312.0f), 60f, true),
            new View("fx2_audit_row", new Vector3(306.6f, 3.0f, 231.1f), new Vector3(298.1f, 21.6f, 266.8f), 60f, true),
            new View("fx2_spaces_studio", new Vector3(-87.9f, 3.0f, 22.8f), new Vector3(-98.6f, 5.3f, 57.9f), 60f, true),
            new View("fx2_terminal_cafe", new Vector3(-279.3f, 3.0f, -217.4f), new Vector3(-302.9f, 3.4f, -189.3f), 60f, true),
            new View("fx2_launchpad_tower", new Vector3(-354.8f, 3.0f, -258.5f), new Vector3(-306.6f, 5.9f, -252.6f), 60f, true),
            new View("fx2_discord_heights", new Vector3(98.7f, 3.0f, 71.7f), new Vector3(145.8f, 8.3f, 83.7f), 60f, true),
            new View("fx2_vc_row", new Vector3(337.0f, 3.0f, 287.9f), new Vector3(332.4f, 35.2f, 324.3f), 60f, true),
            new View("fx2_cabal_lounge", new Vector3(475.8f, 3.0f, -328.4f), new Vector3(469.6f, 1.0f, -292.3f), 60f, true),
            new View("fx2_sybil_barn", new Vector3(596.3f, 3.0f, -51.9f), new Vector3(594.2f, 16.1f, -88.5f), 60f, true),
            new View("fx2_ore_refinery", new Vector3(128.1f, 3.0f, 285.0f), new Vector3(139.0f, 20.1f, 320.0f), 60f, true),
            new View("fx2_ratio_arena", new Vector3(-360.4f, 3.0f, 304.8f), new Vector3(-348.3f, 22.6f, 270.2f), 60f, true),
            new View("fx2_superteam_hq", new Vector3(401.7f, 3.0f, 413.2f), new Vector3(392.0f, 40.9f, 372.0f), 60f, true),
            new View("fx2_jupiter_observatory", new Vector3(873.4f, 3.0f, 401.3f), new Vector3(846.0f, 59.5f, 386.0f), 60f, true),
            new View("fx2_seeker_shop", new Vector3(-103.8f, 3.0f, -31.7f), new Vector3(-96.0f, 2.3f, -58.0f), 60f, true),
            new View("fx2_helius_depot", new Vector3(-266.8f, 3.0f, -201.4f), new Vector3(-252.0f, 7.6f, -176.0f), 60f, true),
            // the first walk-in door (room space: +Z out to the street, the room at -Z)
            new View("room_door", new Vector3(0.6f, 1.6f, 7f), new Vector3(0f, 1.4f, 0f), 60f),
            new View("room_hall", new Vector3(0.2f, 1.6f, 0.8f), new Vector3(0.2f, 1.4f, -4f), 70f),
            new View("room_living", new Vector3(-0.3f, 1.6f, -3.6f), new Vector3(-3f, 1.0f, -7f), 75f),
            new View("room_back", new Vector3(-2.5f, 1.6f, -8.5f), new Vector3(0f, 1.2f, -2f), 75f),
            // R3 (2026-10-24): The Rug Room at the arcade pier, outside, and its floor (rr_room_*: room space of the rug_room
            // door, +Z out to the beach, the floor at -Z, the slot bank at +x; -ctlife-room rug_room places that room)
            new View("rr_ext", new Vector3(116f, 1.7f, -231f), new Vector3(126f, 5.5f, -256f), 60f, true),
            new View("rr_ext_wide", new Vector3(92f, 2.0f, -214f), new Vector3(126f, 4f, -258f), 58f, true),
            new View("rr_ext_3q", new Vector3(106f, 4.5f, -234f), new Vector3(126f, 4.5f, -255f), 58f, true),
            new View("rr_ext_air", new Vector3(160f, 38f, -205f), new Vector3(122f, 0f, -262f), 55f),
            new View("rr_room_entry", new Vector3(0.6f, 1.7f, -1.0f), new Vector3(-1.2f, 1.3f, -10f), 70f),
            new View("rr_room_slots", new Vector3(1.6f, 1.7f, -4.2f), new Vector3(6.4f, 1.1f, -7.6f), 68f),
            new View("rr_room_crash", new Vector3(-1.4f, 1.7f, -5.2f), new Vector3(-6.4f, 1.8f, -7.4f), 68f),
            new View("rr_room_coinflip", new Vector3(0.3f, 2.2f, -5.8f), new Vector3(0f, 0.9f, -9.4f), 68f),
            new View("rr_room_back", new Vector3(0.5f, 1.8f, -6.0f), new Vector3(-0.5f, 1.6f, -16f), 72f),
            // Rekt Burger (2026-10-25) on the Coast Highway in The Trenches, outside, and its restaurant (rb_room_*: room space of
            // the rekt_burger door, +Z out to the highway, the room at -Z, the booths at +x; -ctlife-room rekt_burger places it)
            new View("rb_room_ext", new Vector3(3.0f, 1.7f, 13f), new Vector3(0f, 3.4f, -6f), 60f),
            new View("rb_room_ext_wide", new Vector3(-9f, 2.6f, 25f), new Vector3(0f, 2.8f, -6f), 56f),
            new View("rb_ext_air", new Vector3(-330f, 30f, -270f), new Vector3(-301.8f, -4f, -306.7f), 55f),
            new View("rb_room_entry", new Vector3(-0.3f, 1.6f, -0.8f), new Vector3(0.5f, 1.4f, -8f), 72f),
            new View("rb_room_counter", new Vector3(-2.6f, 1.7f, -1.4f), new Vector3(1.0f, 1.6f, -7f), 70f),
            new View("rb_room_booths", new Vector3(-1.5f, 1.6f, -5.2f), new Vector3(4.4f, 0.9f, -2.6f), 70f),
            new View("rb_room_logo", new Vector3(2.4f, 1.6f, -3.3f), new Vector3(-5.2f, 2.1f, -3.3f), 70f),
            new View("rb_room_kitchen", new Vector3(0.2f, 2.4f, -7.0f), new Vector3(0.6f, 1.1f, -11.6f), 72f),
        };

        public static void Run()
        {
            string fatal = null;
            var outDir = Arg("-ctlife-captureOut") ?? Path.Combine(IslandV2Paths.Repo, "..", "ct-life-evidence", "world-v2-20261017", "capture");
            Directory.CreateDirectory(outDir);
            var only = Arg("-ctlife-views")?.Split(',').ToHashSet();
            var receipt = new JObject();
            try { Capture(outDir, only, receipt); }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            receipt["fatal"] = fatal;
            File.WriteAllText(Path.Combine(outDir, "island-v2-capture-receipt.json"), receipt.ToString());
            Debug.Log($"CTLIFE [islandv2-capture] {(fatal == null ? "PASS" : "FAIL " + fatal)} -> {outDir}");
            if (Application.isBatchMode) EditorApplication.Exit(fatal == null ? 0 : 1);
        }

        public static void Capture(string outDir, HashSet<string> only, JObject receipt)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.BasePrefab) ?? throw new InvalidOperationException("island not built");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var world = root.GetComponent<IslandWorld>();
            root.GetComponent<IslandTerrainFar>()?.Build(world.LoadHeightfield());
            foreach (var c in world.cells)
            {
                var cp = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.CellsDir + "/" + c.id + ".prefab");
                if (cp == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(cp);
                go.transform.SetParent(root.transform, false);
                go.GetComponentInChildren<IslandTerrainCell>(true)?.BuildNow(world.LoadHeightfield());
                if (c.far != null) root.transform.Find("Far/" + c.id + "_far")?.gameObject.SetActive(false);
            }
            int venues = 0, reskinned = 0;
            var entries = AssetDatabase.FindAssets("t:VenueContentEntry").Select(g => AssetDatabase.LoadAssetAtPath<VenueContentEntry>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null && !e.isHome && e.prefab != null).ToList();
            bool noVenues = Environment.GetCommandLineArgs().Contains("-ctlife-novenues");
            foreach (var v in world.venues)
            {
                if (noVenues) break;
                var e = entries.FirstOrDefault(x => x.contentId == v.venueId);
                if (e == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.SetPositionAndRotation(v.Position, v.Rotation);
                StripVenueLook(go);
                IslandVenueTrim.Trim(go.transform, v.localFrame, v.shellHeight > 0f ? 0.5f : 4f, v.shellHeight > 0f ? v.shellHeight + 0.4f : 400f);
                if (v.shellHeight <= 0f && world.venueReskin != null) reskinned += IslandVenueReskin.Apply(go.transform, world.venueReskin);
                venues++;
            }
            receipt["venues"] = venues;
            // the first walk-in room where IslandInteriors would stream it (static, for the room_* views)
            Vector3 doorPos = Vector3.zero; Quaternion doorRot = Quaternion.identity;
            var doorsTa = AssetDatabase.LoadAssetAtPath<TextAsset>(IslandV2Interiors.DoorsAsset);
            if (doorsTa != null && JObject.Parse(doorsTa.text)["doors"] is JArray dl && dl.Count > 0)
            {
                var want = Arg("-ctlife-room");
                var d0 = (JObject)(dl.FirstOrDefault(d => want != null && (string)d["room"] == want) ?? dl[0]);
                doorPos = new Vector3((float)d0["p"][0], (float)d0["floor"], (float)d0["p"][2]);
                doorRot = Quaternion.Euler(0f, (float)d0["yaw"], 0f);
                var rp = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Interiors.PrefabDir + "/" + (string)d0["room"] + ".prefab");
                if (rp != null)
                {
                    var rg = (GameObject)PrefabUtility.InstantiatePrefab(rp);
                    rg.transform.SetPositionAndRotation(doorPos + doorRot * new Vector3(0f, 0f, -0.04f), doorRot);
                    receipt["room"] = rp.name + " at " + doorPos;
                }
            }
            receipt["venueRenderersReskinned"] = reskinned;
            receipt["venueReskinMaterial"] = world.venueReskin != null ? world.venueReskin.name : "none";
            // kit instances: edit mode has no OnEnable/LateUpdate, so decode and draw them for the capture camera here
            var sets = Object.FindObjectsByType<IslandInstances>(FindObjectsSortMode.None);
            foreach (var set in sets) { set.Decode(); IslandInstanceRenderer.Register(set); }
            receipt["instanceSets"] = sets.Length;
            receipt["instances"] = sets.Sum(x => x.Count);
            IslandInstanceRenderer.CollectStats = true;
            if (Environment.GetCommandLineArgs().Contains("-ctlife-noocclusion")) root.GetComponent<IslandInstanceRenderer>().occlusion = false;
            _instanceRenderer = Arg("-ctlife-noinstances") == null && !Environment.GetCommandLineArgs().Contains("-ctlife-noinstances") ? root.GetComponent<IslandInstanceRenderer>() : null;
            var atm = root.GetComponent<IslandAtmosphere>();
            if (Environment.GetCommandLineArgs().Contains("-ctlife-nohaze")) atm.hazeStrength = 0f;
            atm.Apply();
            if (Environment.GetCommandLineArgs().Contains("-ctlife-nofog")) RenderSettings.fog = false;
            if (Environment.GetCommandLineArgs().Contains("-ctlife-dusk") && atm.Sun != null)
            {
                // a look-dev dusk (the game has no day cycle yet): a low warm sun, dim ambient, to judge the neon
                atm.Sun.intensity *= 0.18f; atm.Sun.color = new Color(1f, 0.55f, 0.35f);
                atm.Sun.transform.rotation = Quaternion.Euler(6f, 250f, 0f);
                RenderSettings.ambientIntensity *= 0.3f;
                RenderSettings.ambientSkyColor *= 0.3f; RenderSettings.ambientEquatorColor *= 0.3f; RenderSettings.ambientGroundColor *= 0.3f;
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-noshadows") && atm.Sun != null) atm.Sun.shadows = LightShadows.None;
            if (Environment.GetCommandLineArgs().Contains("-ctlife-tagmats"))
            {
                void Tag(string m, Color c) { var mm = AssetDatabase.LoadAssetAtPath<Material>(IslandV2Paths.Materials + "/" + m + ".mat"); if (mm != null) { mm.SetColor("_Emission", c); mm.SetColor("_BaseColor", c); } }
                Tag("sidewalk", new Color(1f, 0f, 0f)); Tag("kerb", new Color(0f, 0f, 1f)); Tag("concrete", new Color(1f, 1f, 0f));
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-whitesidewalk"))
            {
                var sw = AssetDatabase.LoadAssetAtPath<Material>(IslandV2Paths.Materials + "/sidewalk.mat");
                var tx = sw.GetTexture("_BaseMap") as Texture2D;
                Debug.Log($"CTLIFE [islandv2-capture] sidewalk texture {tx?.name} {tx?.format} {tx?.width}x{tx?.height} mips {tx?.mipmapCount} filter {tx?.filterMode}");
                sw.SetTexture("_BaseMap", Texture2D.whiteTexture);
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-noterrain"))
                foreach (var tc in Object.FindObjectsByType<IslandTerrainCell>(FindObjectsSortMode.None))
                    foreach (var r in tc.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l != atm.Sun) l.enabled = false;
            var camGo = new GameObject("CaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 5200f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = !Environment.GetCommandLineArgs().Contains("-ctlife-nopost");
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            int W = 1780, H = 800;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            var h = IslandV2Height.Load(IslandV2Paths.Out);
            var shots = new JArray();
            var stats = new JObject();
            receipt["renderStats"] = stats;
            foreach (var v in Views)
            {
                if (only != null && !only.Contains(v.Name)) continue;
                var eye = v.Eye;
                if (v.Ground) eye.y = h.Sample(eye.x, eye.z) + v.Eye.y;
                var lookAt = v.Target;
                if (v.Name.StartsWith("room_") || v.Name.StartsWith("rr_room_") || v.Name.StartsWith("rb_room_")) { eye = doorPos + doorRot * v.Eye; lookAt = doorPos + doorRot * v.Target; }
                cam.orthographic = false;
                cam.fieldOfView = v.Fov;
                cam.transform.position = eye;
                cam.transform.LookAt(lookAt);
                cam.nearClipPlane = v.Name.StartsWith("room_") || v.Name.StartsWith("rr_room_") || v.Name.StartsWith("rb_room_") ? 0.05f : 0.3f;
                Render(cam, rt, Path.Combine(outDir, "v2-" + v.Name + ".png"));
                shots.Add(v.Name);
                stats[v.Name] = new JObject
                {
                    ["batches"] = UnityEditor.UnityStats.batches, ["setPass"] = UnityEditor.UnityStats.setPassCalls,
                    ["triangles"] = UnityEditor.UnityStats.triangles, ["shadowCasters"] = UnityEditor.UnityStats.shadowCasters, ["kitDraws"] = IslandInstanceRenderer.LastDraws,
                    ["kitInstances"] = IslandInstanceRenderer.LastInstances, ["kit"] = KitStats(),
                };
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-venueviews"))
            {
                // every venue from the street, 35 m out from its door and 22 m up (shell and diorama checks)
                foreach (var v in world.venues)
                {
                    var c = v.ToIsland(v.localFrame.center);
                    var door = v.Rotation * Vector3.back;                     // the door side (local -Z)
                    var eye = new Vector3(c.x, 0f, c.y) + door * 34f + Quaternion.Euler(0f, 35f, 0f) * door * 8f;
                    eye.y = h.Sample(eye.x, eye.z) + 22f;
                    cam.orthographic = false; cam.fieldOfView = 55f;
                    cam.transform.position = eye;
                    cam.transform.LookAt(new Vector3(c.x, v.elevation + 3f, c.y));
                    Render(cam, rt, Path.Combine(outDir, "venue-" + v.venueId + ".png"));
                    shots.Add("venue-" + v.venueId);
                }
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-heroviews"))
            {
                // W3: every hero statue and landmark building, from the street side
                var targets = new List<(string name, Vector3 at, float yaw, float dist, float lift)>();
                var dress = IslandV2Paths.Json("dress.json");
                if (dress["heroes"] is JArray hs) foreach (var hj in hs) targets.Add(("hero-" + (string)hj["id"], IslandV2Paths.V3(hj["p"]), (float)hj["yaw"], 9f, 3.2f));
                var planJ = IslandV2Paths.Json("plan.json");
                if (planJ["landmarkBuildings"] is JArray lbs)
                    foreach (var lb in lbs)
                    {
                        var at2 = IslandV2Paths.V2(lb["at"]);
                        targets.Add(("landmark-" + (string)lb["id"], new Vector3(at2.x, (float)lb["y"], at2.y), (float)lb["yaw"], (float)lb["h"] > 10f ? 70f : 34f, (float)lb["h"] > 10f ? (float)lb["h"] * 0.6f + 4f : (float)lb["h"] + 12f));   // small ones from above the street
                    }
                foreach (var (name, at, yaw, dist, lift) in targets)
                {
                    var front = Quaternion.Euler(0f, yaw + 25f, 0f) * Vector3.forward;   // statues face their road; buildings' signs face local -Z
                    if (name.StartsWith("landmark-")) front = Quaternion.Euler(0f, yaw + 25f, 0f) * Vector3.back;
                    var eye = at + front * dist;
                    eye.y = Mathf.Max(h.Sample(eye.x, eye.z) + 1.8f, at.y + lift);
                    cam.orthographic = false; cam.fieldOfView = 50f;
                    cam.transform.position = eye;
                    cam.transform.LookAt(at + Vector3.up * Mathf.Min(lift, 12f) * 0.6f);
                    Render(cam, rt, Path.Combine(outDir, name + ".png"));
                    shots.Add(name);
                }
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-shots"))
            {
                // W4: the authored demo shots (Resources/World/Island/demo_route.json), with a 1.8 m stand-in for the player
                var stand = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(stand.GetComponent<Collider>());
                stand.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
                var standMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.85f, 0.35f, 0.25f) };
                stand.GetComponent<MeshRenderer>().sharedMaterial = standMat;
                Physics.SyncTransforms();
                foreach (var w in IslandWaypoints.Load())
                {
                    float gy = h.Sample(w.x, w.z);
                    if (Physics.Raycast(new Vector3(w.x, 209f, w.z), Vector3.down, out var hit, 400f,   // below the 214 m flight ceiling collider
                         (1 << Layers.Ground) | (1 << Layers.Road))) gy = hit.point.y;
                    var feet = new Vector3(w.x, w.y ?? gy, w.z);
                    stand.transform.position = feet + Vector3.up * 0.9f;
                    var (pos, rot, fov) = IslandWaypoints.CameraPose(w, feet);
                    cam.orthographic = false; cam.fieldOfView = fov;
                    cam.transform.SetPositionAndRotation(pos, rot);
                    Render(cam, rt, Path.Combine(outDir, $"shot-{w.shot}-{w.id}.png"));
                    shots.Add("shot-" + w.id);
                    stats["shot-" + w.id] = new JObject
                    {
                        ["batches"] = UnityEditor.UnityStats.batches, ["setPass"] = UnityEditor.UnityStats.setPassCalls,
                        ["triangles"] = UnityEditor.UnityStats.triangles, ["shadowCasters"] = UnityEditor.UnityStats.shadowCasters,
                        ["kitDraws"] = IslandInstanceRenderer.LastDraws, ["kitInstances"] = IslandInstanceRenderer.LastInstances, ["kit"] = KitStats(),
                    };
                }
                Object.DestroyImmediate(stand);
            }
            if (Environment.GetCommandLineArgs().Contains("-ctlife-kitlods")) KitLods(cam, rt, outDir, shots, Arg("-ctlife-kitlods-ids"));
            if (Environment.GetCommandLineArgs().Contains("-ctlife-lodviews")) LodViews(cam, rt, outDir, h, shots, receipt);
            if (only == null || only.Contains("topdown"))
            {
                // top-down: the whole island, orthographic, sun overhead-ish for an even read
                cam.orthographic = true;
                cam.transform.position = new Vector3(10f, 900f, 10f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var rt2 = new RenderTexture(2048, 1640, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt2;
                cam.aspect = 2048f / 1640f;
                cam.orthographicSize = 1780f / cam.aspect / 2f;
                Render(cam, rt2, Path.Combine(outDir, "v2-topdown.png"));
                shots.Add("topdown");
                rt2.Release();
            }
            receipt["shots"] = shots;
            atm.Restore();
            rt.Release();
        }

        /// <summary>
        /// LOD continuity evidence (2026-10-22): one parked car on Consensus Boulevard and one downtown building, seen from
        /// eye height at a ladder of distances on the same bearing, the way the device picks its LOD (Android lodBias 0.7,
        /// the instance renderer's distance tiers). The car is rendered twice per distance: as the instanced decor and as
        /// the drivable prefab ParkedCars swaps in (same model pick, same paint), so a morph on approach or on the swap shows.
        /// </summary>
        private static void LodViews(Camera cam, RenderTexture rt, string outDir, IslandHeightfield h, JArray shots, JObject receipt)
        {
            QualitySettings.lodBias = 0.7f;
            var anchor = new Vector3(82f, 0f, 86f);
            // a parked car near the boulevard with a clear line of sight out to 100 m on one of four diagonal bearings
            Physics.SyncTransforms();
            ParkedCar decor = null; var bearing = Vector3.forward;
            float EyeY(float d) => 2.6f + Mathf.Max(0f, d - 20f) * 0.12f;
            foreach (var cand in Object.FindObjectsByType<ParkedCar>(FindObjectsSortMode.None).Where(c => c.set != null)
                         .OrderBy(c => (c.transform.position - anchor).sqrMagnitude).Take(60))
            {
                var cp = cand.transform.position;
                foreach (var (fw, sd) in new[] { (0.6f, 0.8f), (0.6f, -0.8f), (-0.6f, 0.8f), (-0.6f, -0.8f) })
                {
                    var bdir = (cand.transform.forward * fw + cand.transform.right * sd).normalized;
                    bool clear = true;
                    foreach (float d in new[] { 12f, 36f, 100f })
                    {
                        var from = cp + bdir * d + Vector3.up * EyeY(d); var to = cp + bdir * 3f + Vector3.up * 0.8f;
                        if (Physics.Linecast(from, to, (1 << Layers.Building) | (1 << Layers.Ground) | (1 << Layers.Prop))) { clear = false; break; }
                    }
                    if (clear) { decor = cand; bearing = bdir; break; }
                }
                if (decor != null) break;
            }
            var lod = new JObject();
            receipt["lod"] = lod;
            cam.fieldOfView = 52f;
            if (decor != null)
            {
                var p = decor.transform.position;
                if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var gh, 20f, (1 << Layers.Road) | (1 << Layers.Ground))) lod["carGroundOffset"] = Math.Round(gh.point.y - p.y, 2);
                var target = (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out gh, 20f, (1 << Layers.Road) | (1 << Layers.Ground)) ? gh.point : p) + Vector3.up * 0.75f;
                int pick = unchecked(Mathf.RoundToInt(p.x * 10) * 73856093 ^ Mathf.RoundToInt(p.z * 10) * 19349663);
                var id = ParkedCars.SpecFor(decor.vehicleClass, decor.model, pick);
                lod["car"] = $"{decor.model} at ({p.x:0.0}, {p.z:0.0}) -> drivable {id}";
                var prefab = Resources.Load<GameObject>("Vehicles/" + id);
                foreach (float d in new[] { 6f, 12f, 20f, 28f, 36f, 60f, 100f, 180f })
                {
                    var eye = p + bearing * d; eye.y = target.y + EyeY(d) - 0.75f;
                    cam.transform.position = eye; cam.transform.LookAt(target);
                    Render(cam, rt, Path.Combine(outDir, $"lod-car-decor-{d:000}m.png"));
                    if (prefab != null)
                    {
                        decor.Hide(true);
                        var car = Object.Instantiate(prefab, p + Vector3.up * 0.05f, decor.transform.rotation);
                        var v = car.GetComponent<Vehicle>();
                        if (v != null)
                        {
                            v.Bind();
                            if (v.Spec != null && v.Spec.kind == VehicleKind.Car) VehiclePaint.Apply(v, VehiclePaint.Pick(((pick >> 8) & 0xFFFF) / 65535.0));
                        }
                        Render(cam, rt, Path.Combine(outDir, $"lod-car-drivable-{d:000}m.png"));
                        Object.DestroyImmediate(car);
                        decor.Hide(false);
                    }
                    shots.Add($"lod-car-{d:000}m");
                }
            }
            // a downtown building: the nearest Neighbourhood City / 99.Miles box to the boulevard, from its street front
            var dress = IslandV2Paths.Json("dress.json");
            JArray best = null; float bestD = float.MaxValue;
            foreach (JArray b in (JArray)dress["buildingBoxes"])
            {
                float bx = (float)b[0], bz = (float)b[2], bw = (float)b[3], bh = (float)b[4];
                if (bw < 9f || bh < 9f || bh > 40f) continue;
                float dd = (new Vector3(bx, 0f, bz) - anchor).sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = b; }
            }
            if (best != null)
            {
                var c = new Vector3((float)best[0], (float)best[1], (float)best[2]);
                float bh = (float)best[4], bd = (float)best[5];
                var front = Quaternion.Euler(0f, (float)best[6], 0f) * Vector3.forward;
                var dirB = (front * 0.9f + Quaternion.Euler(0f, (float)best[6], 0f) * Vector3.right * 0.3f).normalized;
                lod["building"] = $"box at ({c.x:0.0}, {c.z:0.0}) {best[3]}x{best[5]}x{bh} m yaw {best[6]}";
                foreach (float d in new[] { 15f, 30f, 45f, 80f, 120f, 200f, 350f })
                {
                    var eye = c + dirB * (bd * 0.5f + d);
                    eye.y = h.Sample(eye.x, eye.z) + 1.7f + Mathf.Max(0f, d - 20f) * 0.4f;     // above the street wall beyond 20 m (the hovercar's view)
                    cam.transform.position = eye; cam.transform.LookAt(new Vector3(c.x, c.y, c.z));
                    Render(cam, rt, Path.Combine(outDir, $"lod-building-{d:000}m.png"));
                    shots.Add($"lod-building-{d:000}m");
                }
            }
        }

        /// <summary>
        /// Every LOD of chosen kit prototypes side by side (LOD0, LOD1, LOD2 left to right) with their island materials, high
        /// over the sea in the island light, from the front-left and the back-right: holes, see-through walls, card edges
        /// and LOD mismatches show without the street in the way.
        /// </summary>
        private static void KitLods(Camera cam, RenderTexture rt, string outDir, JArray shots, string idsArg)
        {
            var kit = AssetDatabase.LoadAssetAtPath<IslandKit>(IslandV2Kit.KitAsset);
            var ids = (idsArg ?? "nc_building_01,nc_building_02,nc_building_03,nc_building_04,nc_building_05,nc_building_06,m99_chicago_a,m99_apt_complex,m99_office_tower,v3car_sedan,v3car_taxi").Split(',');
            var origin = new Vector3(0f, 420f, -2600f);
            foreach (var id in ids)
            {
                int pi = kit.IndexOf(id);
                if (pi < 0) continue;
                var pr = kit.protos[pi];
                float w = Mathf.Max(pr.bounds.size.x, pr.bounds.size.z), hh = pr.bounds.size.y;
                float gap = w * 1.25f;
                var made = new List<GameObject>();
                for (int l = 0; l < pr.lods.Length; l++)
                {
                    var go = new GameObject($"{id}_LOD{l}");
                    go.transform.position = origin + Vector3.right * (l - (pr.lods.Length - 1) * 0.5f) * gap;
                    go.AddComponent<MeshFilter>().sharedMesh = pr.lods[l];
                    var mr = go.AddComponent<MeshRenderer>();
                    var mats = new Material[pr.lods[l].subMeshCount];
                    for (int sm = 0; sm < mats.Length; sm++)
                        mats[sm] = l < pr.lodMaterials.Length && pr.lodMaterials[l] != null ? pr.lodMaterials[l] : sm < pr.materials.Length && pr.materials[sm] != null ? pr.materials[sm] : pr.material;
                    mr.sharedMaterials = mats;
                    made.Add(go);
                }
                // a grey ground plate under them
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.transform.position = origin + Vector3.down * 0.05f; plate.transform.localScale = new Vector3(gap * 4f, 0.1f, w * 3f);
                plate.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(IslandV2Paths.Materials + "/concrete.mat");
                float span = gap * (pr.lods.Length - 1) + w;
                float dist = Mathf.Max(span, hh * 2.2f) * 1.05f;
                cam.fieldOfView = 50f;
                foreach (var (name, dir) in new[] { ("front", new Vector3(-0.25f, 0.18f, 1f)), ("back", new Vector3(0.3f, 0.25f, -1f)), ("top", new Vector3(0f, 1f, -0.02f)) })
                {
                    var target = origin + Vector3.up * hh * 0.45f;
                    cam.transform.position = target + dir.normalized * dist;
                    cam.transform.LookAt(target);
                    Render(cam, rt, Path.Combine(outDir, $"kit-{id}-{name}.png"));
                    shots.Add($"kit-{id}-{name}");
                }
                foreach (var go in made) Object.DestroyImmediate(go);
                Object.DestroyImmediate(plate);
            }
        }

        public static void StripVenueLook(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<VenueLook>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<VenueContentLighting>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Volume>(true)) Object.DestroyImmediate(c.gameObject);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) if (l.type == LightType.Directional) l.enabled = false;
            var b = go.transform.Find("Backdrop");
            if (b != null) b.gameObject.SetActive(false);
        }

        private static IslandInstanceRenderer _instanceRenderer;

        private static void Render(Camera cam, RenderTexture rt, string path)
        {
            if (_instanceRenderer != null)
            {
                IslandInstanceRenderer.TargetCamera = cam;
                _instanceRenderer.Render(cam);
            }
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>Kit renderer counters of the last view (per kind: main draws, main tris, caster draws, caster tris).</summary>
        private static JObject KitStats()
        {
            var o = new JObject { ["tris"] = IslandInstanceRenderer.LastTris, ["casterTris"] = IslandInstanceRenderer.LastCasterTris, ["casters"] = IslandInstanceRenderer.LastCasters,
                ["occluded"] = IslandInstanceRenderer.LastOccluded, ["occluders"] = IslandInstanceRenderer.LastOccluders };
            foreach (var kv in IslandInstanceRenderer.Stats) o[kv.Key] = string.Join("/", kv.Value);
            return o;
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
