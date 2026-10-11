using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>Actual fitted Hermes hair parts on their unchanged UBC bodies, never additional people.</summary>
    public static class CharacterHairStyleImport
    {
        private const string Root = DistinctCharacterImport.Root + "/hairstyles";
        private const string Portraits = "Assets/Resources/UI/Customizer/Portraits/Hairstyles/";

        [MenuItem("CT Life/Cast/Import reviewed hairstyles")]
        public static void Run()
        {
            var manifest = JObject.Parse(File.ReadAllText(Root + "/manifest.json"));
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset")
                ?? throw new InvalidDataException("Import distinct characters before hairstyles.");
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterPaletteCatalog>("Assets/Resources/Characters/CharacterPalettes.asset")
                ?? throw new InvalidDataException("Import palettes before hairstyles.");
            var rig = (JObject)JObject.Parse(File.ReadAllText(DistinctCharacterImport.Root + "/cts_humanoid_map.json"))["unity_humanoid"];
            var pending = new List<CharacterRegistry.Entry>();
            var styles = new Dictionary<string, List<CharacterPaletteCatalog.HairStyle>>();
            var choices = new List<CreatorLookCatalog.Entry>();
            foreach (string body in new[] { "ubc_m", "ubc_f" })
            {
                var binding = catalog.FindBase("head_" + body) ?? throw new InvalidDataException("Missing UBC palette " + body);
                var original = registry.looks.Single(e => e.key == string.Join("|", binding.sourceLook));
                if (original.prefab == null || original.previewPrefab == null) throw new InvalidDataException("Missing original UBC meshes.");
                styles.Add(body, new List<CharacterPaletteCatalog.HairStyle> { new CharacterPaletteCatalog.HairStyle {
                    id = "original", hairId = binding.sourceLook[2], labelKey = body == "ubc_m" ? "creator.hair_style.parted" : "creator.hair_style.long" } });
                choices.Add(new CreatorLookCatalog.Entry { identityId = binding.sourceLook[2], look = binding.sourceLook, previewYaw = 0f });
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var record in manifest["models"])
            {
                string body = (string)record["identity"], style = (string)record["style"], hair = (string)record["hairId"];
                var allowed = body == "ubc_m" ? new[] { "buzzed", "long", "buns" } :
                    body == "ubc_f" ? new[] { "buns", "buzzed", "parted" } : Array.Empty<string>();
                if (!allowed.Contains(style) || hair != "hair_" + body + "_" + style || styles[body].Any(s => s.id == style))
                    throw new InvalidDataException("Unknown or duplicate hairstyle " + body + ":" + style);
                var binding = catalog.FindBase("head_" + body);
                var source = registry.looks.Single(e => e.key == string.Join("|", binding.sourceLook));
                var files = new Dictionary<string, string>();
                foreach (string role in new[] { "world", "hero" })
                {
                    string path = Root + "/" + (string)record["files"][role];
                    using (var sha = SHA256.Create())
                    {
                        string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                        if (actual != (string)record["sha256"][role]) throw new InvalidDataException("Hairstyle source hash mismatch: " + path);
                    }
                    DistinctCharacterImport.ConfigureAvatar(path, rig);
                    files.Add(role, path);
                }
                var world = DistinctCharacterImport.BuildPrefab(body + "_" + style, files["world"], binding.sourceMaterial, false);
                var hero = DistinctCharacterImport.BuildPrefab(body + "_" + style, files["hero"], binding.sourceMaterial, true);
                var problems = CastValidation.Check(world, registry.controller, new JObject());
                problems.AddRange(CastValidation.Check(hero, registry.controller, new JObject()));
                var spans = CastValidation.PoseSpans(hero, registry.controller, new[] { "loco_idle", "loco_walk", "loco_run", "act_sit" });
                if (problems.Count > 0 || spans.Count != 4 || spans.Values.Any(v => float.IsNaN(v) || float.IsInfinity(v)))
                    throw new InvalidDataException("Hairstyle animation gate failed " + hair + ": " + string.Join("; ", problems));
                var look = (string[])binding.sourceLook.Clone(); look[2] = hair;
                pending.Add(new CharacterRegistry.Entry { key = string.Join("|", look), prefab = world, previewPrefab = hero,
                    height = source.height, controllerOverride = source.controllerOverride, variant = source.variant });
                styles[body].Add(new CharacterPaletteCatalog.HairStyle { id = style, hairId = hair, labelKey = "creator.hair_style." + style });
                choices.Add(new CreatorLookCatalog.Entry { identityId = hair, look = look, previewYaw = 0f });
            }
            if (pending.Count != 6 || styles.Any(s => s.Value.Count != 4)) throw new InvalidDataException("Expected six alternate UBC hairstyles.");
            // Publish only after every exact source passed its own rig and shared locomotion gate.
            foreach (var entry in pending)
            {
                int index = registry.looks.FindIndex(e => e.key == entry.key);
                if (index < 0) registry.looks.Add(entry); else registry.looks[index] = entry;
            }
            EditorUtility.SetDirty(registry);
            typeof(CharacterRegistry).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(registry, null);
            AssetDatabase.SaveAssets();
            DistinctCharacterImport.BakePortraits(choices, registry, Portraits);
            foreach (var pair in styles)
            {
                foreach (var style in pair.Value)
                    style.thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(Portraits + style.hairId + ".png")
                        ?? throw new InvalidDataException("Missing hairstyle portrait: " + style.hairId);
                catalog.FindBase("head_" + pair.Key).hairStyles = pair.Value.ToArray();
            }
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("CTLIFE hairstyles: four fitted styles per UBC identity; eight actual-mesh portraits, six new world/Hero pairs.");
        }

        public static void BakePortraitsBatch()
        {
            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                var catalog = AssetDatabase.LoadAssetAtPath<CharacterPaletteCatalog>("Assets/Resources/Characters/CharacterPalettes.asset");
                var choices = new List<CreatorLookCatalog.Entry>();
                foreach (string id in new[] { "head_ubc_m", "head_ubc_f" })
                {
                    var binding = catalog.FindBase(id);
                    foreach (var style in binding.hairStyles)
                    {
                        var look = (string[])binding.sourceLook.Clone(); look[2] = style.hairId;
                        choices.Add(new CreatorLookCatalog.Entry { identityId = style.hairId, look = look, previewYaw = 0f });
                    }
                }
                DistinctCharacterImport.BakePortraits(choices, registry, Portraits);
                AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
