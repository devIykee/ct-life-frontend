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
    /// <summary>Imports only preserved, hash-pinned UBC tint masks. Does not rewrite the original character materials.</summary>
    public static class CharacterPaletteImport
    {
        private const string Root = "Assets/Art/characters/palettes";
        private const string CatalogPath = "Assets/Resources/Characters/CharacterPalettes.asset";
        private static readonly string[] Regions = { "skin", "hair", "top", "bottom", "shoes", "brows" };

        [MenuItem("CT Life/Cast/Import reviewed character palettes")]
        public static void Run()
        {
            var source = JObject.Parse(File.ReadAllText(Root + "/manifest.json"));
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/CharacterMaskedLit.shader");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidDataException("Masked character shader did not compile.");
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            if (registry == null) throw new InvalidDataException("Import the distinct character roster first.");
            var previousCatalog = AssetDatabase.LoadAssetAtPath<CharacterPaletteCatalog>(CatalogPath);
            var entries = new List<CharacterPaletteCatalog.Base>();
            foreach (var record in source["models"])
            {
                string id = (string)record["id"];
                if (id != "ubc_m" && id != "ubc_f") throw new InvalidDataException("Unreviewed palette body: " + id);
                var look = DistinctCharacterImport.Look(id);
                var entry = registry.looks.SingleOrDefault(e => e.key == string.Join("|", look));
                if (entry?.prefab == null || entry.previewPrefab == null) throw new InvalidDataException("Missing source body " + id);
                var original = AssetDatabase.LoadAssetAtPath<Material>(DistinctCharacterImport.Root + "/materials/" + id + ".mat");
                if (original == null || new[] { entry.prefab, entry.previewPrefab }.Any(p =>
                    p.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMaterials.Length != 1 || r.sharedMaterial != original)))
                    throw new InvalidDataException("Source material/UV binding changed for " + id);
                Texture2D Mask(string role) => ImportMask(Root + "/" + (string)record["files"][role], (string)record["source_hashes"][role]);
                var mask0 = Mask("mask0"); var mask1 = Mask("mask1");
                string materialPath = Root + "/" + id + "_Masked.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
                material.shader = shader;
                material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                material.SetTexture("_TintMask0", mask0); material.SetTexture("_TintMask1", mask1);
                material.SetFloat("_Smoothness", .22f);
                material.SetFloat("_Cull", original.GetFloat("_Cull"));
                material.SetFloat("_UseNormalMap", 0f); material.SetFloat("_UseMetallicMap", 0f);
                material.SetFloat("_Metallic", 0f); material.SetColor("_EmissionColor", Color.black);
                material.SetFloat("_ReferenceFloor", .03f);
                foreach (string region in Regions)
                {
                    if (!ColorUtility.TryParseHtmlString((string)record["tint_reference"][region], out var color))
                        throw new InvalidDataException("Missing reference for " + id + ":" + region);
                    string property = char.ToUpperInvariant(region[0]) + region.Substring(1);
                    var linear = color.linear;
                    material.SetVector("_" + property + "Reference", new Vector4(linear.r, linear.g, linear.b, 1f));
                    material.SetVector("_" + property + "Tint", Vector4.zero);
                }
                EditorUtility.SetDirty(material);
                entries.Add(new CharacterPaletteCatalog.Base { id = look[1], sourceLook = look, sourceMaterial = original,
                    maskedMaterial = material, mask0 = mask0, mask1 = mask1,
                    hairStyles = previousCatalog?.FindBase(look[1])?.hairStyles ?? Array.Empty<CharacterPaletteCatalog.HairStyle>() });
            }
            var palettes = new List<CharacterPaletteCatalog.Palette>();
            foreach (string type in new[] { "skin", "hair", "cloth" })
            {
                var swatches = new List<CharacterPaletteCatalog.Swatch>();
                void Add(string id, string hex)
                {
                    if (!ColorUtility.TryParseHtmlString(hex, out var color)) throw new InvalidDataException("Invalid palette color " + id);
                    swatches.Add(new CharacterPaletteCatalog.Swatch { id = id, color = color });
                }
                if (type == "skin") foreach (var value in source["palettes"][type]) Add((string)value["id"], (string)value["hex"]);
                else foreach (var property in ((JObject)source["palettes"][type]).Properties()) Add(property.Name, (string)property.Value);
                palettes.Add(new CharacterPaletteCatalog.Palette { id = type, swatches = swatches.ToArray() });
            }
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterPaletteCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<CharacterPaletteCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            // A palette refresh must preserve separately imported, reviewed human outfit bindings.
            entries.AddRange(catalog.bases.Where(b => b != null && b.id != "head_ubc_m" && b.id != "head_ubc_f"));
            catalog.bases = entries.ToArray(); catalog.palettes = palettes.ToArray();
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("CTLIFE character palettes: two reviewed identities, ten skin tones, twelve hair colors, sixteen cloth colors.");
        }

        internal static Texture2D ImportMask(string path, string expectedHash, int resolution = 1024)
        {
            using (var hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                if (actual != expectedHash) throw new InvalidDataException("Mask hash mismatch: " + path);
            }
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidDataException("Mask is not imported: " + path);
            // Validate the reviewed UV source independently of platform import settings.
            // Keep imported dimensions in diagnostics to distinguish asset/load failures.
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            if (sourceWidth != resolution || sourceHeight != resolution)
                throw new InvalidDataException($"Unexpected source mask size: {path} is {sourceWidth}x{sourceHeight}.");
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = false; importer.alphaIsTransparency = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = true; importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = resolution; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            { name = "Android", overridden = true, maxTextureSize = resolution, format = TextureImporterFormat.ASTC_4x4 });
            importer.SaveAndReimport();
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (mask == null) throw new InvalidDataException("Mask could not be loaded: " + path);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var android = importer.GetPlatformTextureSettings("Android");
            if (importer.textureShape != TextureImporterShape.Texture2D ||
                importer.sRGBTexture || importer.isReadable || importer.alphaIsTransparency ||
                importer.alphaSource != TextureImporterAlphaSource.FromInput ||
                importer.maxTextureSize != resolution || !android.overridden || android.maxTextureSize != resolution ||
                android.format != TextureImporterFormat.ASTC_4x4)
                throw new InvalidDataException("Another importer changed the reviewed mask settings: " + path);
            Debug.Log($"CTLIFE mask {Path.GetFileName(path)}: source={sourceWidth}x{sourceHeight}, " +
                $"imported={mask.width}x{mask.height}, quality mip limit={QualitySettings.globalTextureMipmapLimit}.");
            return mask;
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
