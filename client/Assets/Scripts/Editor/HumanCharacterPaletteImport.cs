using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>Source-part-derived masks for the existing Snow/Rain outfits. All authored maps/materials stay intact.</summary>
    public static class HumanCharacterPaletteImport
    {
        private const string Root = "Assets/Art/characters/palettes/humans";
        private const string CatalogPath = "Assets/Resources/Characters/CharacterPalettes.asset";
        [MenuItem("CT Life/Cast/Import Snow and Rain color masks")]
        public static void Run()
        {
            var manifest = JObject.Parse(File.ReadAllText(Root + "/manifest.json"));
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterPaletteCatalog>(CatalogPath)
                ?? throw new InvalidDataException("Import the shared character palettes first.");
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            var shader = Shader.Find(CharacterPaletteResolver.ShaderName);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidDataException("Masked character shader has errors.");
            var entries = new List<CharacterPaletteCatalog.Base>();
            var importedMasks = new Dictionary<string, Texture2D>();
            foreach (var record in manifest["models"])
            {
                string id = (string)record["id"];
                var look = record["look"].ToObject<string[]>();
                if (look.Length != 7 || look[1] != (string)record["headId"] ||
                    !(id == "snow_everyday" || id == "rain_breeze" || id.StartsWith("player_m_", StringComparison.Ordinal) || id.StartsWith("player_f_", StringComparison.Ordinal)))
                    throw new InvalidDataException("Unreviewed human palette binding " + id);
                var entry = registry?.looks.SingleOrDefault(e => e.key == string.Join("|", look));
                if (entry?.prefab == null || entry.previewPrefab == null) throw new InvalidDataException("Missing exact human source " + id);
                var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/characters/cast/materials/" + id + ".mat");
                if (original == null || new[] { entry.prefab, entry.previewPrefab }.Any(p =>
                    p.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMaterials.Length != 1 || r.sharedMaterial != original)))
                    throw new InvalidDataException("Human source material does not match " + id);
                string albedoPath = AssetDatabase.GetAssetPath(original.GetTexture("_BaseMap"));
                using (var sha = SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(albedoPath))).Replace("-", "").ToLowerInvariant() != (string)record["sourceAlbedoSHA256"])
                        throw new InvalidDataException("Human mask/albedo UV source changed " + id);
                Texture2D Mask(string role)
                {
                    string path = Root + "/" + (string)record["files"][role];
                    if (!importedMasks.TryGetValue(path, out var mask))
                    {
                        mask = CharacterPaletteImport.ImportMask(path, (string)record["sha256"][role], (int)record["resolution"]);
                        importedMasks.Add(path, mask);
                    }
                    return mask;
                }
                var mask0 = Mask("mask0"); var mask1 = Mask("mask1");
                string materialPath = Root + "/" + id + "_Masked.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
                material.shader = shader;
                material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                material.SetTexture("_TintMask0", mask0); material.SetTexture("_TintMask1", mask1);
                var normal = original.GetTexture("_BumpMap"); var metal = original.GetTexture("_MetallicGlossMap");
                material.SetTexture("_BumpMap", normal); material.SetFloat("_UseNormalMap", normal != null ? 1f : 0f);
                material.SetFloat("_BumpScale", original.GetFloat("_BumpScale"));
                material.SetTexture("_MetallicGlossMap", metal); material.SetFloat("_UseMetallicMap", metal != null ? 1f : 0f);
                material.SetFloat("_Metallic", original.GetFloat("_Metallic"));
                material.SetFloat("_Smoothness", original.GetFloat("_Smoothness"));
                material.SetTexture("_EmissionMap", original.GetTexture("_EmissionMap"));
                material.SetColor("_EmissionColor", original.IsKeywordEnabled("_EMISSION") ? original.GetColor("_EmissionColor") : Color.black);
                material.SetFloat("_ReferenceFloor", .0001f);
                foreach (string region in new[] { "skin", "hair", "top", "bottom", "shoes", "brows" })
                {
                    if (!ColorUtility.TryParseHtmlString((string)record["tint_reference"][region], out var color))
                        throw new InvalidDataException("Missing human tint reference " + id + ":" + region);
                    string property = char.ToUpperInvariant(region[0]) + region.Substring(1);
                    var linear = color.linear;
                    material.SetVector("_" + property + "Reference", new Vector4(linear.r, linear.g, linear.b, 1f));
                    material.SetVector("_" + property + "Tint", Vector4.zero);
                }
                EditorUtility.SetDirty(material);
                entries.Add(new CharacterPaletteCatalog.Base { id = look[1], personId = (string)record["family"] == "m" ? "head_snow" : "head_rain", sourceLook = look, sourceMaterial = original,
                    maskedMaterial = material, mask0 = mask0, mask1 = mask1, regions = record["regions"].ToObject<string[]>(),
                    hairStyles = catalog.FindBase(look[1])?.hairStyles ?? Array.Empty<CharacterPaletteCatalog.HairStyle>() });
            }
            if (entries.Count != 24 || entries.Select(e => e.id).Distinct().Count() != 24)
                throw new InvalidDataException("Expected the 24 original human outfit bindings.");
            var ids = new HashSet<string>(entries.Select(e => e.id));
            catalog.bases = catalog.bases.Where(b => b != null && !ids.Contains(b.id)).Concat(entries).ToArray();
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("CTLIFE human palettes: all 24 Snow/Rain outfits have source-part masks and preserved normal/metallic/emission maps.");
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
