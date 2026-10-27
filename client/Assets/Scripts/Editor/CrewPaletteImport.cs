using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Masked materials for the Rekt Burger crew variants (palettes/humans/crew_manifest.json from
    /// art/cast/scripts/export_character_palette_masks.py), bound in Resources/Characters/UniformPalettes.asset. Same material
    /// recipe as HumanCharacterPaletteImport; the creator's CharacterPalettes catalog is not touched.
    /// Batch: -executeMethod CTLife.EditorTools.CrewPaletteImport.RunBatch
    /// </summary>
    public static class CrewPaletteImport
    {
        private const string Root = "Assets/Art/characters/palettes/humans";
        private const string AssetPath = "Assets/Resources/Characters/UniformPalettes.asset";

        public static void Run()
        {
            var manifest = JObject.Parse(File.ReadAllText(Root + "/crew_manifest.json"));
            var shader = Shader.Find(CharacterPaletteResolver.ShaderName) ?? throw new InvalidDataException("Masked character shader missing");
            var entries = new List<UniformPalettes.Entry>();
            foreach (var record in manifest["models"])
            {
                string id = (string)record["id"];
                var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/characters/cast/materials/" + id + ".mat")
                    ?? throw new InvalidDataException("Crew material missing " + id);
                var mask0 = CharacterPaletteImport.ImportMask(Root + "/" + (string)record["files"]["mask0"], (string)record["sha256"]["mask0"], (int)record["resolution"]);
                var mask1 = CharacterPaletteImport.ImportMask(Root + "/" + (string)record["files"]["mask1"], (string)record["sha256"]["mask1"], (int)record["resolution"]);
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
                material.SetFloat("_Cull", original.GetFloat("_Cull"));
                material.SetTexture("_EmissionMap", original.GetTexture("_EmissionMap"));
                material.SetColor("_EmissionColor", original.IsKeywordEnabled("_EMISSION") ? original.GetColor("_EmissionColor") : Color.black);
                material.SetFloat("_ReferenceFloor", .0001f);
                foreach (string region in new[] { "skin", "hair", "top", "bottom", "shoes", "brows" })
                {
                    if (!ColorUtility.TryParseHtmlString((string)record["tint_reference"][region], out var color))
                        throw new InvalidDataException("Missing crew tint reference " + id + ":" + region);
                    string property = char.ToUpperInvariant(region[0]) + region.Substring(1);
                    var linear = color.linear;
                    material.SetVector("_" + property + "Reference", new Vector4(linear.r, linear.g, linear.b, 1f));
                    material.SetVector("_" + property + "Tint", Vector4.zero);
                }
                EditorUtility.SetDirty(material);
                entries.Add(new UniformPalettes.Entry { lookId = id, sourceMaterial = original, maskedMaterial = material });
            }
            var asset = AssetDatabase.LoadAssetAtPath<UniformPalettes>(AssetPath);
            if (asset == null) { asset = ScriptableObject.CreateInstance<UniformPalettes>(); AssetDatabase.CreateAsset(asset, AssetPath); }
            asset.entries = entries.ToArray();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"CTLIFE [cast] {entries.Count} crew palette bindings");
        }

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
