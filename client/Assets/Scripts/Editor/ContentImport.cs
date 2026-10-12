using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Brings the art and audio deliverables into the Unity project (copy-if-changed, never deletes):
    ///   art/processed/characters/{male,female,npc} → Assets/Resources/Characters/…   (runtime assembly by item id)
    ///   art/processed/{animations,environment,props,nature} → Assets/Art/…            (scenes, controller)
    ///   audio/processed/** + audio/sfx-map.json → Assets/Resources/Audio/…          (AudioManager)
    /// Import settings live in <see cref="CTLifeImportPostprocessor"/> (docs/progress/assets.md "READY FOR UNITY IMPORT").
    /// Batch: Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.ContentImport.SyncAll
    /// </summary>
    public static class ContentImport
    {
        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        [MenuItem("CT Life/Content/Sync + Import All")]
        public static void SyncAll()
        {
            int n = 0;
            // Characters are placeholders (MakeHuman, rejected; ready-made humanoids are being sourced). They live outside
            // Resources: CharacterRegistry references prefabs, so only what is mapped ships.
            n += Sync("art/processed/characters", "Assets/Art/characters");
            n += Sync("art/processed/animations", "Assets/Art/animations");
            n += Sync("art/processed/environment", "Assets/Art/environment");
            n += Sync("art/processed/props", "Assets/Art/props");
            n += Sync("art/processed/nature", "Assets/Art/nature");
            // Audio ships through `python3 audio/tools/ship_audio.py --sync` (only map-referenced files); import
            // settings come from CTLife.Audio.Editor/AudioImportPostprocessor (one policy, unit-tested).
            Debug.Log($"CTLIFE [content] synced {n} changed files; importing");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BuildAvatarController();
            FillCharacterRegistry();
            AssetDatabase.SaveAssets();
            Debug.Log("CTLIFE [content] done");
        }

        private static int Sync(string srcRel, string dstRel)
        {
            var src = Path.Combine(Repo, srcRel);
            if (!Directory.Exists(src))
            {
                Debug.LogWarning("CTLIFE [content] missing " + srcRel);
                return 0;
            }
            int n = 0;
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, f);
                if (rel.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith(".") || p == "__pycache__")) continue;
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".blend" || ext == ".blend1" || ext == ".py" || ext == ".md") continue;
                n += CopyIfChanged(f, Path.Combine(dstRel, rel));
            }
            return n;
        }

        private static int CopyFile(string srcRel, string dst) => CopyIfChanged(Path.Combine(Repo, srcRel), dst);

        private static int CopyIfChanged(string src, string dst)
        {
            if (!File.Exists(src)) return 0;
            var si = new FileInfo(src);
            var di = new FileInfo(dst);
            if (di.Exists && di.Length == si.Length && di.LastWriteTimeUtc >= si.LastWriteTimeUtc) return 0;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
            return 1;
        }

        /// <summary>
        /// Assets/Resources/Characters/AvatarController.controller: one state per anims.json id, named exactly like the id
        /// (AnimationLibrary convention), from art/processed/animations/clip_map.json; float parameter "speed".
        /// </summary>
        [MenuItem("CT Life/Content/Build Avatar Controller")]
        public static void BuildAvatarController()
        {
            const string mapPath = "Assets/Art/animations/clip_map.json";
            if (!File.Exists(mapPath))
            {
                Debug.LogWarning("CTLIFE [content] no clip_map.json; controller not built");
                return;
            }
            var map = JObject.Parse(File.ReadAllText(mapPath))["map"] as JObject;
            if (map == null) return;
            const string ctrlPath = "Assets/Resources/Characters/AvatarController.controller";
            Directory.CreateDirectory(Path.GetDirectoryName(ctrlPath));
            // Upsert, never recreate: the shipped controller carries hand-tuned states (Codex's native idle, walk/run
            // rates, Foot IK) and the character-feel locomotion tree; rebuilding it from clip_map.json silently lost them.
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) ?? AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            if (!ctrl.parameters.Any(x => x.name == "speed")) ctrl.AddParameter("speed", AnimatorControllerParameterType.Float);
            var sm = ctrl.layers[0].stateMachine;
            int added = 0, kept = 0, missing = 0;
            foreach (var p in map.Properties())
            {
                if (sm.states.Any(st => st.state.name == p.Name)) { kept++; continue; }
                var file = p.Value["clip"]?.ToString();
                var clip = file == null ? null : LoadClip("Assets/Art/animations/" + file);
                if (clip == null) { missing++; continue; }
                var state = sm.AddState(p.Name);
                state.motion = clip;
                if (p.Name == "loco_idle") sm.defaultState = state;
                added++;
            }
            EditorUtility.SetDirty(ctrl);
            Debug.Log($"CTLIFE [content] AvatarController: +{added} states, {kept} kept, {missing} clips missing");
            CharacterFeelController.Apply();
        }

        /// <summary>
        /// Creates/updates Resources/Characters/CharacterRegistry.asset: controller = AvatarController; every model or
        /// prefab under Assets/Art/characters whose file name is an archetype id becomes that archetype's NPC prefab
        /// (prefabs win over raw models); files named default_player / default_npc become the defaults. Existing manual
        /// entries are kept. Works for the placeholder NPCs today and for sourced humanoids dropped in later.
        /// </summary>
        [MenuItem("CT Life/Content/Fill Character Registry")]
        public static void FillCharacterRegistry()
        {
            const string regPath = "Assets/Resources/Characters/CharacterRegistry.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(regPath));
            var reg = AssetDatabase.LoadAssetAtPath<CTLife.World.CharacterRegistry>(regPath);
            if (reg == null)
            {
                reg = ScriptableObject.CreateInstance<CTLife.World.CharacterRegistry>();
                AssetDatabase.CreateAsset(reg, regPath);
            }
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/Characters/AvatarController.controller");
            if (ctrl != null) reg.controller = ctrl;

            var archetypeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var snap = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/Contract/config_snapshot.json");
            if (snap != null && JObject.Parse(snap.text)["ids"]?["archetypes"] is JObject archReg)
            {
                var list = (archReg["archetypes"] ?? archReg["items"]) as JArray;
                if (list != null) foreach (var a in list) archetypeIds.Add(a["id"]?.ToString() ?? a.ToString());
            }

            var candidates = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Art/characters" })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .OrderBy(p => p.EndsWith(".prefab") ? 0 : 1).ToList();
            int mapped = 0;
            foreach (var path in candidates)
            {
                var key = Path.GetFileNameWithoutExtension(path);
                if (key.Contains("_LOD") || path.Contains("/parts/") || path.Contains("/body/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponentInChildren<Animator>() == null && !path.EndsWith(".fbx")) continue;
                if (key.Equals("default_player", StringComparison.OrdinalIgnoreCase)) { if (reg.defaultPlayer == null) reg.defaultPlayer = go; continue; }
                if (key.Equals("default_npc", StringComparison.OrdinalIgnoreCase)) { if (reg.defaultNpc == null) reg.defaultNpc = go; continue; }
                var id = key == "perp_degen" && archetypeIds.Contains("perp_degen_npc") ? "perp_degen_npc" : key;
                if (!archetypeIds.Contains(id) || reg.archetypes.Any(e => e.key == id && e.prefab != null)) continue;
                reg.archetypes.Add(new CTLife.World.CharacterRegistry.Entry { key = id, prefab = go });
                mapped++;
            }
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            Debug.Log($"CTLIFE [content] CharacterRegistry: +{mapped} archetypes ({reg.archetypes.Count} total), controller={(reg.controller != null)}");
        }

        private static AnimationClip LoadClip(string fbxPath) =>
            AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
    }

    /// <summary>Import settings for CT Life content folders.</summary>
    public sealed class CTLifeImportPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 2; // 1: one-shot transition clips (sit down, stand up, get up); 2: skips the mocap folder
        private const string CharRoot = "Assets/Art/characters/";
        private const string AnimRoot = "Assets/Art/animations/";
        private const string EnvRoots = "Assets/Art/";

        private static readonly Dictionary<string, bool> _loopCache = new Dictionary<string, bool>();

        private void OnPreprocessModel()
        {
            if (assetPath.StartsWith(AuthoredCharacterImport.Root)) return;
            // The cast has its own importer (CastPostprocessor) and its own per-look Avatars; CTL settings must not leak in.
            if (assetPath.StartsWith(CastImport.Root + "/", StringComparison.Ordinal)) return;
            if (assetPath.StartsWith(DistinctCharacterImport.Root + "/", StringComparison.Ordinal)) return;
            if (assetPath.StartsWith(MocapImport.Root + "/", StringComparison.Ordinal)) return; // own Avatars (MocapPostprocessor)
            var mi = (ModelImporter)assetImporter;
            if (assetPath.StartsWith(CharRoot) || assetPath.StartsWith(AnimRoot))
            {
                mi.globalScale = 1f;
                mi.useFileScale = true;
                mi.bakeAxisConversion = true;
                mi.importCameras = false;
                mi.importLights = false;
                mi.importBlendShapes = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.optimizeGameObjects = false; // runtime part merge needs the bone transforms
                mi.skinWeights = ModelImporterSkinWeights.Standard;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                // Parts are merged into one SkinnedMeshRenderer at runtime (CharacterAssembler reads vertices).
                mi.isReadable = assetPath.StartsWith(CharRoot) && !assetPath.Contains("/npc/");
                bool isBody = assetPath.Contains("/body/");
                if (isBody) mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                else
                {
                    // Declared dependency: this asset is reimported after the body Avatar is (re)imported. Before then
                    // (fresh Library, arbitrary import order) the body may not be loadable; keep the stored setup rather
                    // than silently switching clips/parts to an Avatar generated from their own skeleton.
                    var bodyPath = BodyPath();
                    if (bodyPath != null) context.DependsOnArtifact(bodyPath);
                    var source = SourceAvatar();
                    if (source != null)
                    {
                        mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                        mi.sourceAvatar = source;
                    }
                    else if (mi.avatarSetup == ModelImporterAvatarSetup.NoAvatar)
                        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }
                mi.importAnimation = assetPath.StartsWith(AnimRoot);
            }
            else if (assetPath.StartsWith(EnvRoots))
            {
                mi.globalScale = 1f;
                mi.useFileScale = true;
                mi.bakeAxisConversion = true;
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.generateSecondaryUV = true;
                mi.isReadable = false;
            }
        }

        /// <summary>Body avatar of the matching sex (male clips drive both through Humanoid retargeting).</summary>
        private Avatar SourceAvatar()
        {
            var body = BodyPath();
            return body == null ? null : AssetDatabase.LoadAllAssetsAtPath(body).OfType<Avatar>().FirstOrDefault();
        }

        private string BodyPath()
        {
            var sex = assetPath.Contains("/female/") ? "female" : "male";
            var body = $"{CharRoot}{sex}/body/ctl_{sex}_body.fbx";
            return body == assetPath || !File.Exists(body) ? null : body;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(AnimRoot) || assetPath.StartsWith(MocapImport.Root + "/", StringComparison.Ordinal)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            bool loop = ClipLoops(Path.GetFileName(assetPath));
            foreach (var c in clips)
            {
                c.loopTime = loop;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                // Normalize imported in-place clips to Humanoid forward. Preserving
                // the Blender authored offset made correctly mapped avatars face -Z.
                bool authoredRun = DirectMovementImport.UsesAuthoredRunOrientation(assetPath, mi);
                c.keepOriginalOrientation = authoredRun || mi.userData == "ct-life:original-orientation-probe";
                c.rotationOffset = authoredRun ? 180f : 0f;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }

        private static bool ClipLoops(string fbxName)
        {
            if (_loopCache.Count == 0)
            {
                const string mapPath = AnimRoot + "clip_map.json";
                if (File.Exists(mapPath) && JObject.Parse(File.ReadAllText(mapPath))["map"] is JObject map)
                    foreach (var p in map.Properties())
                        if (p.Value["clip"] != null) _loopCache[p.Value["clip"].ToString()] = p.Value["loop"]?.Value<bool>() ?? false;
            }
            if (_loopCache.TryGetValue(fbxName, out var l)) return l;
            // Transitions are one-shots: a looping sit-down/stand-up/get-up wraps to its first frame while the next state
            // cross-fades in (the body drops back towards the seat or floor for a moment).
            if (fbxName.Contains("sit_down") || fbxName.Contains("stand_up") || fbxName.Contains("get_up")) return false;
            return fbxName.StartsWith("loco_") || fbxName.StartsWith("act_");
        }

        /// <summary>Clip FBX carry a 1-triangle `_bind_proxy` mesh (bind pose carrier): strip it.</summary>
        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(AnimRoot) || assetPath.StartsWith(MocapImport.Root + "/", StringComparison.Ordinal)) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("_bind_proxy")).ToList())
                if (t != null && t != root.transform) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(AuthoredCharacterImport.Root)) return;
            if (assetPath.StartsWith(DistinctCharacterImport.Root + "/", StringComparison.Ordinal)) return;
            if (assetPath.StartsWith("Assets/Art/characters/palettes/", StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.StartsWith(CharRoot))
            {
                ti.isReadable = !assetPath.Contains("/npc/"); // per-avatar atlas packing at runtime
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 1024;
                SetAndroid(ti, TextureImporterFormat.ASTC_6x6, 1024);
            }
            else if (assetPath.StartsWith(EnvRoots))
            {
                if (Path.GetFileName(assetPath).StartsWith("ctl_palette"))
                {
                    ti.filterMode = FilterMode.Point;
                    ti.mipmapEnabled = false;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                }
                else SetAndroid(ti, TextureImporterFormat.ASTC_8x8, 1024);
            }
        }

        private static void SetAndroid(TextureImporter ti, TextureImporterFormat fmt, int max)
        {
            var s = ti.GetPlatformTextureSettings("Android");
            s.overridden = true;
            s.format = fmt;
            s.maxTextureSize = max;
            ti.SetPlatformTextureSettings(s);
        }

        /// <summary>`ctl_alpha_clip` materials (leaves, grass, flowers) need Alpha Clipping on URP Lit.</summary>
        private void OnPostprocessMaterial(Material m)
        {
            if (!assetPath.StartsWith(EnvRoots) || m == null || !m.name.Contains("alpha_clip")) return;
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
    }
}
