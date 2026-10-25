using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.Events
{
    /// <summary>
    /// The Breakpoint 2026 London skin (`london_2026`) for the Conference Center (lane B, 2026-10-24). Writes
    /// Resources/World/Events/bp26_london.prefab in the hall's local frame (x right seen from the street, z into the hall, the
    /// front glass at z = -10.7, the hall floor at y = 0, the plaza lawn at about -0.12):
    /// - live: six expo booths under the canopy (proof ids bp26.booth_1..6, west to east), the fascia print, four vertical
    ///   banners and a header over the doors, a red entrance arch, a carpet runner, eight pole flags along the kerb, the bus
    ///   and phone-box slots, and inside the keynote screen (live countdown), the trivia kiosk and the stream totem;
    /// - upcoming: the countdown totem on the plaza;
    /// - nods: eight event nods around Breakpoint Park (bp26.nod_1..8), snapped to the ground at runtime.
    /// Prints come from art/source/events/bp26 (bp26_atlas.png + layout.json). Static parts are merged per material.
    /// Batch: -executeMethod CTLife.EditorTools.Events.BreakpointSkinBuilder.Build
    /// </summary>
    public static class BreakpointSkinBuilder
    {
        private const string Dir = "Assets/World/Events", Out = "Assets/Resources/World/Events";
        private static string Src => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art", "source", "events", "bp26"));

        public const float GlassZ = -10.7f, Paving = 0.06f, Lawn = -0.12f;
        /// <summary>Booth centres (x) along the canopy, west to east; their fronts at z = -13.45.</summary>
        public static readonly float[] BoothX = { -10.8f, -7.6f, -4.4f, 4.4f, 7.6f, 10.8f };
        public const float BoothFrontZ = -13.45f;
        /// <summary>Nods, hall-local (x, z); y comes from the ground at runtime.</summary>
        public static readonly Vector2[] NodXZ =
        {
            new Vector2(-17.5f, -16f), new Vector2(-21f, 2f), new Vector2(16.5f, 6f), new Vector2(3f, 14.5f),
            new Vector2(25f, -14f), new Vector2(-4f, -45f), new Vector2(16f, -47f), new Vector2(-27f, -21.5f),
        };

        private static JObject _layout;
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        [MenuItem("CT Life/Events/Build Breakpoint London Skin")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir + "/Textures"); Directory.CreateDirectory(Dir + "/Materials"); Directory.CreateDirectory(Dir + "/Meshes");
            Directory.CreateDirectory(Out);
            _layout = JObject.Parse(File.ReadAllText(Path.Combine(Src, "layout.json")));
            var atlasPath = Dir + "/Textures/bp26_atlas.png";
            File.Copy(Path.Combine(Src, "bp26_atlas.png"), atlasPath, true);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.alphaIsTransparency = false;
            ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4; ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 4096;
            ti.npotScale = TextureImporterNPOTScale.None;
            var a = ti.GetPlatformTextureSettings("Android");
            a.overridden = true; a.maxTextureSize = 4096; a.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(a);
            ti.SaveAndReimport();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            MakeMaterials(atlas);
            BuildModels();

            var root = new GameObject("bp26_london");
            var skin = root.AddComponent<EventSkin>();
            skin.live = new GameObject("live"); skin.live.transform.SetParent(root.transform, false);
            skin.upcoming = new GameObject("upcoming"); skin.upcoming.transform.SetParent(root.transform, false);
            var grounded = new List<Transform>();

            // ---------------------------------------------------------------- live: booths and the facade (one merged mesh)
            var front = new MeshBuild();
            var booths = new List<Transform>();
            for (int i = 0; i < 6; i++)
            {
                front.M = Matrix4x4.TRS(new Vector3(BoothX[i], Paving, BoothFrontZ), Quaternion.identity, Vector3.one);
                Booth(front, i + 1);
                var anchor = new GameObject("booth_" + (i + 1)).transform;
                anchor.SetParent(skin.live.transform, false);
                anchor.localPosition = new Vector3(BoothX[i], Paving, BoothFrontZ - 0.55f);
                booths.Add(anchor);
            }
            skin.booths = booths.ToArray();
            front.M = Matrix4x4.identity;
            // the fascia above the canopy (visible band y 13.2..14.85), four vertical banners on the glass (the old name sign is hidden)
            float fz = GlassZ - 0.2f;
            front.Quad(new Vector3(0f, 14.02f, fz - 0.005f), new Vector2(16.9f, 1.65f), Vector3.back, "print", R("fascia"));
            float[] vx = { -8.8f, -5.6f, 5.6f, 8.8f };
            for (int i = 0; i < 4; i++)
            {
                front.Box(new Vector3(vx[i], 11.75f, GlassZ - 0.08f), new Vector3(1.3f, 0.06f, 0.06f), "steel");          // the hanging rail
                front.Quad(new Vector3(vx[i], 9.3f, GlassZ - 0.12f), new Vector2(1.2f, 4.8f), Vector3.back, "print", R("vban_" + (i + 1)));
            }
            // the carpet runner from the kerb to the doors (lawn, then the paving apron)
            front.Box(new Vector3(0f, Lawn + 0.02f, -18.9f), new Vector3(3.2f, 0.1f, 8.4f), "carpet");
            front.Box(new Vector3(-1.66f, Lawn + 0.025f, -18.9f), new Vector3(0.12f, 0.11f, 8.4f), "purple");
            front.Box(new Vector3(1.66f, Lawn + 0.025f, -18.9f), new Vector3(0.12f, 0.11f, 8.4f), "purple");
            front.Box(new Vector3(0f, Paving + 0.012f, -12.7f), new Vector3(3.2f, 0.024f, 4.0f), "carpet");
            Merge(front, skin.live.transform, "front", true);

            // furniture dressing from the Duo Shop set (CC0 Poly Haven, Resources/World/Furniture)
            for (int i = 0; i < 6; i++)
            {
                var b = new Vector3(BoothX[i], Paving + 0.08f, BoothFrontZ);
                Furniture("fu_plant_aloe", skin.live.transform, b + new Vector3(i % 2 == 0 ? 1.12f : -1.12f, 0f, 1.85f), 0f, 1.0f);
                if (i == 1 || i == 4) Furniture("fu_ottoman", skin.live.transform, b + new Vector3(i == 1 ? -1.0f : 1.0f, 0f, 1.25f), 15f, 1.0f);
                if (i == 2 || i == 3) Furniture("fu_side_table", skin.live.transform, b + new Vector3(i == 2 ? -1.05f : 1.05f, 0f, 1.5f), 0f, 1.0f);
            }

            // ---------------------------------------------------------------- live: the arch, the flags (grounded pieces)
            var arch = new MeshBuild();
            arch.Box(new Vector3(-3.55f, 1.95f, 0f), new Vector3(0.4f, 4.5f, 0.4f), "ink");
            arch.Box(new Vector3(3.55f, 1.95f, 0f), new Vector3(0.4f, 4.5f, 0.4f), "ink");
            arch.Box(new Vector3(-3.55f, 0.1f, 0f), new Vector3(0.7f, 0.8f, 0.7f), "steel");
            arch.Box(new Vector3(3.55f, 0.1f, 0f), new Vector3(0.7f, 0.8f, 0.7f), "steel");
            arch.Box(new Vector3(0f, 4.0f, 0f), new Vector3(7.5f, 1.25f, 0.5f), "purple");
            arch.Quad(new Vector3(0f, 4.0f, -0.255f), new Vector2(7.3f, 1.2f), Vector3.back, "print", R("arch"));
            arch.Quad(new Vector3(0f, 4.0f, 0.255f), new Vector2(7.3f, 1.2f), Vector3.forward, "print", R("arch"));
            grounded.Add(Place(Merge(arch, skin.live.transform, "arch", true), new Vector3(0f, Lawn, -17.6f), 0f));
            foreach (int side in new[] { -1, 1 })
            {
                var flags = new MeshBuild();
                float[] fx = { 6.5f, 10.5f, 14.5f, 18.5f };
                for (int k = 0; k < fx.Length; k++)
                {
                    float x = side * fx[k];
                    flags.Cyl(new Vector3(x, -0.3f, 0f), 0.045f, 5.2f, 10, "steel");
                    flags.Box(new Vector3(x + side * 0.47f, 4.62f, 0f), new Vector3(0.94f, 0.04f, 0.04f), "steel");
                    flags.Box(new Vector3(x + side * 0.47f, 2.1f, 0f), new Vector3(0.94f, 0.03f, 0.03f), "steel");
                    var r = R(k % 2 == 0 ? "flag_a" : "flag_b");
                    flags.Quad(new Vector3(x + side * 0.5f, 3.36f, -0.012f), new Vector2(0.8f, 2.4f), Vector3.back, "print", r);
                    flags.Quad(new Vector3(x + side * 0.5f, 3.36f, 0.012f), new Vector2(0.8f, 2.4f), Vector3.forward, "print", r);
                }
                grounded.Add(Place(Merge(flags, skin.live.transform, side < 0 ? "flags_w" : "flags_e", true), new Vector3(0f, Lawn, -21.9f), 0f));
            }
            // the London pieces: Sketchfab models (CC-BY) dropped in by BreakpointModels when processed; empty slots until then
            grounded.Add(Slot(skin.live.transform, "bus_slot", new Vector3(19.6f, Lawn, -18.2f), 90f, "Assets/World/Events/Models/bp26_bus.prefab"));
            grounded.Add(Slot(skin.live.transform, "phonebox_slot", new Vector3(-9.8f, Lawn, -20.4f), 160f, "Assets/World/Events/Models/bp26_phonebox.prefab"));

            // ---------------------------------------------------------------- live: inside, the keynote stage
            var hall = new MeshBuild();
            hall.Box(new Vector3(0f, 3.7f, 9.15f), new Vector3(8.4f, 4.85f, 0.25f), "ink");
            hall.Quad(new Vector3(0f, 3.7f, 9.02f), new Vector2(8.0f, 4.5f), Vector3.back, "led", R("screen"));
            hall.Box(new Vector3(0f, 0.6f, 9.1f), new Vector3(1.2f, 1.2f, 0.4f), "ink");                                          // screen foot
            foreach (int side in new[] { -1, 1 })
            {
                hall.Box(new Vector3(side * 5.0f, 3.0f, 9.16f), new Vector3(1.3f, 4.95f, 0.06f), "ink");
                hall.Quad(new Vector3(side * 5.0f, 3.0f, 9.125f), new Vector2(1.2f, 4.8f), Vector3.back, "print", R(side < 0 ? "stage_l" : "stage_r"));
            }
            Merge(hall, skin.live.transform, "keynote", false);
            skin.kiosk = Totem(skin.live.transform, "kiosk", new Vector3(-9.7f, 0f, 4.0f), -90f, R("kiosk"));     // faces the room (+x)
            skin.streamPost = Totem(skin.live.transform, "stream", new Vector3(9.7f, 0f, 4.0f), 90f, R("stream"));
            var screenAnchor = new GameObject("stage_screen").transform;
            screenAnchor.SetParent(skin.live.transform, false);
            screenAnchor.localPosition = new Vector3(0f, 0f, 6.5f);
            skin.stageScreen = screenAnchor;
            skin.countdownCaption = Text(skin.live.transform, "countdown_caption", new Vector3(0f, 4.02f, 8.99f), 0.18f, new Color(0.667f, 0.404f, 0.984f), "LIVE · DAY 1 OF 3 · ENDS IN", true);
            skin.countdown = Text(skin.live.transform, "countdown", new Vector3(0f, 3.25f, 8.99f), 0.62f, Color.white, "00:00:00");

            // ---------------------------------------------------------------- live: the IRL attendee board on the plaza
            var irl = new MeshBuild();
            irl.Box(new Vector3(0f, 1.2f, 0.18f), new Vector3(1.75f, 3.0f, 0.36f), "ink");
            irl.Box(new Vector3(0f, -0.12f, 0.18f), new Vector3(1.95f, 0.5f, 0.56f), "gold");
            irl.Quad(new Vector3(0f, 1.45f, -0.005f), new Vector2(1.6f, 2.4f), Vector3.back, "led", R("irl_board"));
            var irlGo = Merge(irl, skin.live.transform, "irl_board_totem", true);
            grounded.Add(Place(irlGo, new Vector3(6.2f, Lawn, -19.6f), -18f));
            skin.irlLines = Text(irlGo.transform, "irl_lines", new Vector3(0f, 1.16f, -0.012f), 0.11f, Color.white, "anon_whale\nsolana_sam\n—");
            skin.irlLines.anchor = TextAnchor.MiddleCenter; skin.irlLines.lineSpacing = 1.25f;
            var irlAnchor = new GameObject("irl_board").transform;
            irlAnchor.SetParent(irlGo.transform, false);
            irlAnchor.localPosition = new Vector3(0f, 0f, -0.9f);
            skin.irlBoard = irlAnchor;

            // ---------------------------------------------------------------- upcoming: the countdown totem
            var tot = new MeshBuild();
            tot.Box(new Vector3(0f, 1.2f, 0.18f), new Vector3(1.75f, 3.0f, 0.36f), "ink");
            tot.Box(new Vector3(0f, -0.12f, 0.18f), new Vector3(1.95f, 0.5f, 0.56f), "steel");
            tot.Quad(new Vector3(0f, 1.45f, -0.005f), new Vector2(1.6f, 2.4f), Vector3.back, "led", R("upcoming"));
            var totem = Merge(tot, skin.upcoming.transform, "countdown_totem", true);
            grounded.Add(Place(totem, new Vector3(-5.6f, Lawn, -19.4f), 18f));
            // the window on the print sits 44%..64% down the face
            skin.upcomingCaption = Text(totem.transform, "upcoming_caption", new Vector3(0f, 1.52f, -0.012f), 0.07f, new Color(0.667f, 0.404f, 0.984f), "STARTS IN", true);
            skin.upcomingCountdown = Text(totem.transform, "upcoming_countdown", new Vector3(0f, 1.27f, -0.012f), 0.15f, Color.white, "21d 00:00:00");

            // ---------------------------------------------------------------- nods: eight around Breakpoint Park
            var nods = new List<Transform>();
            var nodMesh = new MeshBuild();
            nodMesh.Disc(Vector3.zero, 0.3f, 0.045f, 28, "gold", R("nod"), R("nod_back"));
            var nodAsset = Save(nodMesh, "bp26_nod", out var nodMats);
            for (int i = 0; i < NodXZ.Length; i++)
            {
                var n = new GameObject("nod_" + (i + 1));
                n.transform.SetParent(skin.live.transform, false);
                n.transform.localPosition = new Vector3(NodXZ[i].x, 0f, NodXZ[i].y);
                var disc = new GameObject("disc");
                disc.transform.SetParent(n.transform, false);
                disc.transform.localPosition = new Vector3(0f, 1.15f, 0f);
                disc.transform.localRotation = Quaternion.Euler(0f, i * 47f, 0f);
                disc.AddComponent<MeshFilter>().sharedMesh = nodAsset;
                var mr = disc.AddComponent<MeshRenderer>();
                mr.sharedMaterials = nodMats;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                nods.Add(n.transform);
                grounded.Add(n.transform);
            }
            skin.nods = nods.ToArray();
            skin.grounded = grounded.ToArray();
            skin.snapped = new bool[grounded.Count];
            skin.SetLive(true);

            var path = EventSkin.PrefabAssetPath;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("CTLIFE [bp26] skin prefab -> " + path);
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>One expo stand in its own frame: the front edge at z = 0, the back wall at z = 2.3, facing -z.</summary>
        private static void Booth(MeshBuild m, int n)
        {
            m.Box(new Vector3(0f, 0.04f, 1.15f), new Vector3(3.0f, 0.08f, 2.3f), "floor");
            m.Box(new Vector3(0f, 0.045f, 0.03f), new Vector3(3.0f, 0.09f, 0.06f), "white");
            m.Box(new Vector3(0f, 1.38f, 2.25f), new Vector3(3.0f, 2.6f, 0.1f), "white");
            m.Quad(new Vector3(0f, 1.38f, 2.195f), new Vector2(3.0f, 2.6f), Vector3.back, "print", R("booth_" + n));
            m.Box(new Vector3(0f, 2.87f, 2.22f), new Vector3(3.06f, 0.4f, 0.16f), "ink");
            m.Quad(new Vector3(0f, 2.87f, 2.135f), new Vector2(3.0f, 0.375f), Vector3.back, "print", R("head_" + n));
            m.Box(new Vector3(-1.47f, 1.38f, 1.75f), new Vector3(0.06f, 2.6f, 1.1f), "white");
            m.Box(new Vector3(1.47f, 1.38f, 1.75f), new Vector3(0.06f, 2.6f, 1.1f), "white");
            m.Box(new Vector3(0f, 0.55f, 0.85f), new Vector3(1.6f, 0.94f, 0.55f), "white");
            m.Quad(new Vector3(0f, 0.78f, 0.57f), new Vector2(1.56f, 0.317f), Vector3.back, "print", R("counter_" + n));
            m.Box(new Vector3(0f, 1.04f, 0.85f), new Vector3(1.7f, 0.04f, 0.65f), "ink");
            // a small screen on a post at the counter (dark glass)
            m.Box(new Vector3(0.45f, 1.08f, 0.95f), new Vector3(0.14f, 0.04f, 0.12f), "steel");
            m.Box(new Vector3(0.45f, 1.26f, 0.97f), new Vector3(0.03f, 0.34f, 0.03f), "steel");
            m.Box(new Vector3(0.45f, 1.45f, 0.95f), new Vector3(0.46f, 0.28f, 0.025f), "ink");
        }

        /// <summary>A free-standing touch totem facing local -z; returns its prompt anchor in front of the face.</summary>
        private static Transform Totem(Transform parent, string name, Vector3 pos, float yaw, Rect face)
        {
            var t = new MeshBuild();
            t.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.9f, 0.08f, 0.6f), "steel");
            t.Box(new Vector3(0f, 1.0f, 0.06f), new Vector3(0.72f, 1.95f, 0.3f), "ink");
            t.Quad(new Vector3(0f, 1.36f, -0.095f), new Vector2(0.6f, 0.96f), Vector3.back, "led", face);
            var go = Merge(t, parent, name + "_totem", true);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, -0.9f);
            anchor.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return anchor;
        }

        private static Transform Place(GameObject go, Vector3 pos, float yaw)
        {
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        private static Transform Slot(Transform parent, string name, Vector3 pos, float yaw, string modelPrefab)
        {
            var s = new GameObject(name).transform;
            s.SetParent(parent, false);
            s.localPosition = pos; s.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(modelPrefab);
            if (p != null) { var m = (GameObject)PrefabUtility.InstantiatePrefab(p); m.transform.SetParent(s, false); }
            else Debug.Log("CTLIFE [bp26] slot " + name + " empty (no " + modelPrefab + ")");
            return s;
        }

        private static void Furniture(string key, Transform parent, Vector3 pos, float yaw, float scale)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/Furniture/" + key + ".prefab");
            if (p == null) { Debug.LogWarning("CTLIFE [bp26] no furniture " + key); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); go.transform.localScale = Vector3.one * scale;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public const string DisplayFont = "Assets/Resources/UI/Fonts/Events/Geist-SemiBold.ttf", MonoFont = "Assets/Resources/UI/Fonts/Events/GeistMono-Medium.ttf";

        private static TextMesh Text(Transform parent, string name, Vector3 pos, float capHeight, Color color, string sample, bool mono = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(mono ? MonoFont : DisplayFont);
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            var tm = g.AddComponent<TextMesh>();
            tm.font = font; tm.fontSize = 96; tm.text = sample;
            // TextMesh em in metres = characterSize * fontSize / 10; Geist's cap height is about 0.71 em
            tm.characterSize = capHeight / 0.71f / (tm.fontSize * 0.1f);
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = color;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = mono ? Mats["text_mono"] : Mats["text"];
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            // an unrotated TextMesh reads from the -z side, the side every print here faces
            return tm;
        }

        // ------------------------------------------------------------------ the London models (Sketchfab, CC-BY)

        /// <summary>
        /// The bus and the phone box from art/processed/world_v3/v3_events (world_v3_process.py, family v3_events: our own
        /// signage over the bus's real ads and fleet name, the phone box's crowns painted out) as prefabs under
        /// Assets/World/Events/Models: origin on the ground under the centre, LOD0/LOD1, Kit PBR materials.
        /// </summary>
        private static void BuildModels()
        {
            var src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art", "processed", "world_v3", "v3_events"));
            var manPath = Path.Combine(src, "manifest.json");
            if (!File.Exists(manPath)) { Debug.Log("CTLIFE [bp26] no processed models yet"); return; }
            var man = JObject.Parse(File.ReadAllText(manPath));
            var md = Dir + "/Models";
            Directory.CreateDirectory(md);
            foreach (var p in man.Properties())
            {
                File.Copy(Path.Combine(src, p.Name + ".fbx"), $"{md}/{p.Name}.fbx", true);
                foreach (var t in (JArray)p.Value["textures"]) File.Copy(Path.Combine(src, "textures", (string)t), $"{Dir}/Textures/{(string)t}", true);
            }
            AssetDatabase.Refresh();
            foreach (var p in man.Properties())
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath($"{md}/{p.Name}.fbx");
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.isReadable = false; mi.addCollider = false;
                mi.importTangents = ModelImporterTangents.CalculateMikk; mi.meshCompression = ModelImporterMeshCompression.Low;
                mi.SaveAndReimport();
                foreach (var t in (JArray)p.Value["textures"])
                {
                    var path = $"{Dir}/Textures/{(string)t}";
                    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                    bool normal = path.EndsWith("_Normal.png"), mask = path.EndsWith("_MaskMap.png");
                    ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = !normal && !mask; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 2;
                    ti.wrapMode = TextureWrapMode.Clamp; ti.alphaIsTransparency = !normal && !mask; ti.maxTextureSize = 2048;
                    var a = ti.GetPlatformTextureSettings("Android");
                    a.overridden = true; a.maxTextureSize = mask ? 1024 : 2048; a.format = TextureImporterFormat.ASTC_6x6;
                    ti.SetPlatformTextureSettings(a);
                    ti.SaveAndReimport();
                }
            }
            var kit = Shader.Find("CT Life/World/Kit PBR");
            foreach (var p in man.Properties())
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{md}/{p.Name}.fbx");
                var root = new GameObject(p.Name);
                var model = Object.Instantiate(fbx);
                model.name = "model";
                model.transform.SetParent(root.transform, false);
                var lod0 = new List<Renderer>(); var lod1 = new List<Renderer>();
                var names = ((JArray)p.Value["materials"]).Select(t => (string)t).ToList();
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    string n = r.gameObject.name + (mf != null && mf.sharedMesh != null ? "|" + mf.sharedMesh.name : "");
                    (n.Contains("_LOD1") || n.Contains("_lod1") ? lod1 : lod0).Add(r);
                    var mats = r.sharedMaterials;
                    // the FBX's slot order is not the manifest's: the glass is the smaller submesh
                    var mesh = mf != null ? mf.sharedMesh : null;
                    int glassSlot = -1;
                    if (mesh != null && mats.Length == 2 && names.Count == 2 && names.Any(x => x.EndsWith("_glass")))
                        glassSlot = mesh.GetSubMesh(0).indexCount < mesh.GetSubMesh(1).indexCount ? 0 : 1;
                    string body = names.First(x => !x.EndsWith("_glass")), glassName = names.FirstOrDefault(x => x.EndsWith("_glass"));
                    for (int i = 0; i < mats.Length; i++) mats[i] = ModelMat(kit, glassSlot < 0 ? (i < names.Count ? names[i] : names[0]) : i == glassSlot ? glassName : body);
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    Debug.Log($"CTLIFE [bp26] {p.Name} {r.name}: " + string.Join(", ", Enumerable.Range(0, mats.Length).Select(i =>
                        (mesh != null && i < mesh.subMeshCount ? mesh.GetSubMesh(i).indexCount / 3 : -1) + " tris -> " + mats[i].name)));
                }
                var b = new Bounds(); bool any = false;
                foreach (var r in lod0) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
                if (lod1.Count > 0)
                {
                    var g = root.AddComponent<LODGroup>();
                    g.SetLODs(new[] { new LOD(0.18f, lod0.ToArray()), new LOD(0.015f, lod1.ToArray()) });
                    g.RecalculateBounds();
                }
                PrefabUtility.SaveAsPrefabAsset(root, $"{md}/{p.Name}.prefab");
                Object.DestroyImmediate(root);
                Debug.Log($"CTLIFE [bp26] model {p.Name}: {lod0.Count}+{lod1.Count} renderers, {b.size.x:0.00} x {b.size.z:0.00} x {b.size.y:0.00} m");
            }
        }

        private static Material ModelMat(Shader kit, string fbxMaterial)
        {
            string stem = fbxMaterial.StartsWith("M_") ? fbxMaterial.Substring(2) : fbxMaterial;
            bool glass = stem.EndsWith("_glass");
            string tex = glass ? stem.Substring(0, stem.Length - "_glass".Length) : stem;
            var path = $"{Dir}/Materials/{stem}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(kit); AssetDatabase.CreateAsset(m, path); }
            m.shader = kit;
            Texture2D T(string k) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/{tex}_{k}.png");
            void Kw(string k, bool on) { if (on) m.EnableKeyword(k); else m.DisableKeyword(k); }
            var nrm = T("Normal"); var msk = T("MaskMap");
            m.SetTexture("_BaseMap", T("BaseColor"));
            m.SetColor("_BaseColor", glass ? new Color(0.85f, 0.88f, 0.9f, 0.35f) : Color.white);
            m.SetTexture("_BumpMap", nrm); Kw("_NORMALMAP", nrm != null); m.SetFloat("_UseNormal", nrm != null ? 1f : 0f);
            m.SetTexture("_MaskMap", msk); Kw("_MASKMAP", msk != null); m.SetFloat("_UseMask", msk != null ? 1f : 0f);
            Kw("_GLASS", glass); m.SetFloat("_Glass", glass ? 1f : 0f);
            m.SetFloat("_Metallic", msk != null ? 1f : 0f); m.SetFloat("_Smoothness", msk != null ? 1f : 0.3f);
            m.SetFloat("_OcclusionStrength", 1f); m.SetFloat("_Reflection", 1f); m.SetFloat("_TintStrength", 0f);
            // the bus's paint bakes at 0.89 smoothness: under the island's sky reflection it read as pink chrome
            if (tex == "bp26_bus" && !glass) { m.SetFloat("_Smoothness", 0.6f); m.SetFloat("_Reflection", 0.4f); }
            m.SetVector("_GroundAO", new Vector4(0.35f, 0.18f, 0f, 0f));
            m.SetFloat("_Cutoff", glass ? 0.08f : 0f); Kw("_ALPHATEST_ON", glass);
            m.SetFloat("_Cull", glass ? 0f : 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ materials

        private static void MakeMaterials(Texture2D atlas)
        {
            Mats.Clear();
            var kit = Shader.Find("CT Life/World/Kit PBR") ?? Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material M(string name, Shader sh, Color c, float smooth, float metal = 0f, Texture tex = null)
            {
                var path = $"{Dir}/Materials/bp26_{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh;
                m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
                m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
                if (m.HasProperty("_TintStrength")) m.SetFloat("_TintStrength", 0f);
                if (m.HasProperty("_Reflection")) m.SetFloat("_Reflection", metal > 0.5f ? 1f : 0.4f);
                if (m.HasProperty("_GroundAO")) m.SetVector("_GroundAO", new Vector4(0.4f, 0.2f, 0f, 0f));
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                Mats[name] = m;
                return m;
            }
            M("print", kit, Color.white, 0.22f, 0f, atlas);
            M("led", unlit, new Color(0.92f, 0.92f, 0.92f), 0f, 0f, atlas);
            M("ink", kit, new Color(0.055f, 0.055f, 0.06f), 0.4f, 0.2f);
            M("white", kit, new Color(0.9f, 0.89f, 0.86f), 0.3f);
            M("floor", kit, new Color(0.17f, 0.18f, 0.2f), 0.12f);
            M("steel", kit, new Color(0.62f, 0.64f, 0.67f), 0.6f, 0.85f);
            M("purple", kit, new Color(0.667f, 0.404f, 0.984f), 0.35f);     // Breakpoint purple #AA67FB
            M("carpet", kit, new Color(0.03f, 0.03f, 0.035f), 0.05f);
            M("gold", kit, new Color(0.95f, 0.7f, 0.25f), 0.65f, 0.9f);
            var textPath = $"{Dir}/Materials/bp26_text.mat";
            var text = AssetDatabase.LoadAssetAtPath<Material>(textPath);
            var sh = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/World/Furniture/Shaders/PaintedText.shader");
            if (text == null) { text = new Material(sh); AssetDatabase.CreateAsset(text, textPath); }
            var font = AssetDatabase.LoadAssetAtPath<Font>(DisplayFont);
            text.shader = sh;
            text.mainTexture = font != null ? font.material.mainTexture : null;
            EditorUtility.SetDirty(text);
            Mats["text"] = text;
            var monoPath = $"{Dir}/Materials/bp26_text_mono.mat";
            var tmono = AssetDatabase.LoadAssetAtPath<Material>(monoPath);
            if (tmono == null) { tmono = new Material(sh); AssetDatabase.CreateAsset(tmono, monoPath); }
            var mfont = AssetDatabase.LoadAssetAtPath<Font>(MonoFont);
            tmono.shader = sh;
            tmono.mainTexture = mfont != null ? mfont.material.mainTexture : null;
            EditorUtility.SetDirty(tmono);
            Mats["text_mono"] = tmono;
        }

        private static Rect R(string name)
        {
            var a = (JArray)_layout["rects"][name] ?? throw new InvalidOperationException("no atlas cell " + name);
            float W = (float)_layout["width"], H = (float)_layout["height"];
            float x = (float)a[0], y = (float)a[1], w = (float)a[2], h = (float)a[3];
            // half-texel inset against bleeding at mip levels
            return Rect.MinMaxRect((x + 1.5f) / W, 1f - (y + h - 1.5f) / H, (x + w - 1.5f) / W, 1f - (y + 1.5f) / H);
        }

        // ------------------------------------------------------------------ meshes

        private static int _saved;

        private static Mesh Save(MeshBuild b, string name, out Material[] mats)
        {
            var keys = b.Subs.Keys.ToList();
            var mesh = new Mesh { name = name, indexFormat = b.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(b.V); mesh.SetNormals(b.N); mesh.SetUVs(0, b.UV);
            mesh.subMeshCount = keys.Count;
            for (int i = 0; i < keys.Count; i++) mesh.SetTriangles(b.Subs[keys[i]], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            var path = $"{Dir}/Meshes/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            mats = keys.Select(k => Mats[k]).ToArray();
            _saved++;
            return mesh;
        }

        private static GameObject Merge(MeshBuild b, Transform parent, string name, bool shadows)
        {
            var mesh = Save(b, "bp26_" + name, out var mats);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Triangles per material in one frame (<see cref="M"/> transforms what is added).</summary>
        private sealed class MeshBuild
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly Dictionary<string, List<int>> Subs = new Dictionary<string, List<int>>();
            public Matrix4x4 M = Matrix4x4.identity;

            private List<int> Sub(string m) { if (!Subs.TryGetValue(m, out var l)) Subs[m] = l = new List<int>(); return l; }

            private void Face(string mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = V.Count;
                V.Add(M.MultiplyPoint3x4(a)); V.Add(M.MultiplyPoint3x4(b)); V.Add(M.MultiplyPoint3x4(c)); V.Add(M.MultiplyPoint3x4(d));
                var wn = M.MultiplyVector(n).normalized;
                N.Add(wn); N.Add(wn); N.Add(wn); N.Add(wn);
                UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
                var t = Sub(mat);
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            /// <summary>An axis-aligned box (all faces one material, UVs in metres).</summary>
            public void Box(Vector3 c, Vector3 s, string mat)
            {
                var h = s * 0.5f;
                Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
                Vector2 U(float a, float b) => new Vector2(a, b);
                Face(mat, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), Vector3.back, U(0, 0), U(0, s.y), U(s.x, s.y), U(s.x, 0));
                Face(mat, P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), Vector3.forward, U(0, 0), U(0, s.y), U(s.x, s.y), U(s.x, 0));
                Face(mat, P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), Vector3.left, U(0, 0), U(0, s.y), U(s.z, s.y), U(s.z, 0));
                Face(mat, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right, U(0, 0), U(0, s.y), U(s.z, s.y), U(s.z, 0));
                Face(mat, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.up, U(0, 0), U(0, s.z), U(s.x, s.z), U(s.x, 0));
                Face(mat, P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), Vector3.down, U(0, 0), U(0, s.z), U(s.x, s.z), U(s.x, 0));
            }

            /// <summary>A print facing <paramref name="normal"/> (back or forward), reading left to right from that side.</summary>
            public void Quad(Vector3 c, Vector2 size, Vector3 normal, string mat, Rect uv)
            {
                float hx = size.x * 0.5f, hy = size.y * 0.5f;
                var right = normal.z < 0 ? Vector3.right : Vector3.left;      // seen from -z, +x is the viewer's right
                var up = Vector3.up;
                Face(mat, c - right * hx - up * hy, c - right * hx + up * hy, c + right * hx + up * hy, c + right * hx - up * hy, normal,
                    new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin));
            }

            /// <summary>A vertical cylinder (a pole) from <paramref name="b"/> up.</summary>
            public void Cyl(Vector3 b, float r, float h, int seg, string mat)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r; var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                    var n = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                    Face(mat, b + d0, b + d0 + Vector3.up * h, b + d1 + Vector3.up * h, b + d1, n, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                }
                Face(mat, b + new Vector3(-r, h, -r), b + new Vector3(-r, h, r), b + new Vector3(r, h, r), b + new Vector3(r, h, -r), Vector3.up, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            }

            /// <summary>A vertical disc (a medal) facing -z with <paramref name="frontUv"/>, +z with <paramref name="backUv"/>,
            /// the rim in <paramref name="rim"/>.</summary>
            public void Disc(Vector3 c, float r, float t, int seg, string rim, Rect frontUv, Rect backUv)
            {
                float hz = t * 0.5f;
                var sf = Sub("print"); var sr = Sub(rim);
                for (int side = 0; side < 2; side++)
                {
                    var uv = side == 0 ? frontUv : backUv;
                    float z = side == 0 ? -hz : hz;
                    var n = side == 0 ? Vector3.back : Vector3.forward;
                    int ci = V.Count;
                    V.Add(M.MultiplyPoint3x4(c + new Vector3(0f, 0f, z))); N.Add(M.MultiplyVector(n)); UV.Add(uv.center);
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = i * Mathf.PI * 2f / seg;
                        float x = Mathf.Cos(a), y = Mathf.Sin(a);
                        V.Add(M.MultiplyPoint3x4(c + new Vector3(x * r, y * r, z))); N.Add(M.MultiplyVector(n));
                        // the print is circular: map the unit circle into the cell (mirrored on the back so it reads)
                        float u = side == 0 ? x : -x;
                        UV.Add(new Vector2(uv.center.x + u * uv.width * 0.5f, uv.center.y + y * uv.height * 0.5f));
                    }
                    for (int i = 0; i < seg; i++)
                    {
                        if (side == 0) { sf.Add(ci); sf.Add(ci + 2 + i); sf.Add(ci + 1 + i); }
                        else { sf.Add(ci); sf.Add(ci + 1 + i); sf.Add(ci + 2 + i); }
                    }
                }
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var p0 = new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, 0f); var p1 = new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, 0f);
                    var n = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), Mathf.Sin((a0 + a1) * 0.5f), 0f);
                    Face(rim, c + p0 + new Vector3(0, 0, -hz), c + p1 + new Vector3(0, 0, -hz), c + p1 + new Vector3(0, 0, hz), c + p0 + new Vector3(0, 0, hz), n,
                        Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                }
            }
        }
    }
}
