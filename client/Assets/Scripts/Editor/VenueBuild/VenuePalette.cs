using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// One shared swatch atlas for every procedurally built venue block (walls, trim, counters, signage frames).
    /// Every block samples the centre of a 16 px swatch, so a whole venue's procedural geometry shares one lit
    /// material and one unlit "glow" material (two draw calls before lightmapping). The table order is the atlas
    /// layout: append new colours at the end only, never reorder (that would recolour already built venues).
    /// </summary>
    public static class VenuePalette
    {
        public const string SharedRoot = "Assets/World/Venues/_Shared";
        public const string TexturePath = SharedRoot + "/Textures/VenueBlockPalette.png";
        private const int Grid = 16, Cell = 16;

        private static readonly (string name, string hex)[] Table =
        {
            // Neutrals
            ("white", "f4f1ea"), ("offwhite", "e8e2d6"), ("cream", "efe3c8"), ("sand", "d8c3a0"), ("lightgrey", "c9ccd1"),
            ("grey", "9aa0a8"), ("midgrey", "6b717a"), ("darkgrey", "3b3f46"), ("charcoal", "24272c"), ("black", "111214"),
            // Woods
            ("oak", "b07a46"), ("walnut", "6b4429"), ("darkwood", "3d2a1c"), ("plywood", "d6b07a"), ("barnred", "8e2f23"),
            // Metals
            ("steel", "8d969e"), ("darksteel", "4a5259"), ("brass", "b8913f"), ("gold", "e3b33a"), ("chrome", "cfd6dc"),
            ("copper", "b46a3c"), ("rust", "8a4b2a"), ("concrete", "a7a39b"), ("darkconcrete", "6d6a64"), ("asphalt", "3a3a3d"),
            // CT signal colours
            ("ctgreen", "26de81"), ("ctred", "ff4757"), ("ctgold", "ffc43d"), ("ctpurple", "8a5cf6"), ("ctblue", "3d7eff"),
            ("ctteal", "1fbfb8"), ("ctpink", "ff5fa2"), ("ctorange", "ff8a3d"), ("cyan", "39d5ff"), ("solpurple", "9945ff"),
            // Dark UI
            ("screenbg", "0f141c"), ("navy", "1b2a44"), ("midnight", "101a2c"), ("slate", "2c3645"), ("ink", "1a1f2b"),
            // Fabrics
            ("velvetred", "7a1626"), ("velvetpurple", "4b2163"), ("leather", "5a3423"), ("petrol", "2f6f73"), ("mustard", "c9962f"),
            ("linen", "d9cfbd"), ("denim", "3e5a7e"), ("blush", "e8b4b0"), ("forest", "2e5a3c"), ("plum", "5e2b4f"),
            // Nature
            ("grass", "6fae4a"), ("darkgrass", "4b7f35"), ("soil", "6b4a2f"), ("hay", "d9b65a"), ("water", "2b8fb3"),
            ("deepwater", "1d5f86"), ("beach", "e5d2a4"), ("leaf", "5f9b3d"), ("bark", "5b4130"), ("pumpkin", "e07a2b"),
            // Clinic / jail
            ("mint", "bfe7d9"), ("hospitalblue", "8fc3e0"), ("clinicwhite", "eef3f5"), ("jailorange", "ff7f27"), ("barsteel", "5c646c"),
            // Glows (used with the unlit material)
            ("glowgreen", "3dff9a"), ("glowred", "ff3b4e"), ("glowgold", "ffd34d"), ("glowpurple", "b26bff"), ("glowblue", "4da3ff"),
            ("glowpink", "ff6bd6"), ("glowcyan", "56f0ff"), ("glowwhite", "fff6e0"), ("glowamber", "ffb03b"), ("oreglow", "ffae33"),
            // Extras
            ("terracotta", "c8673f"), ("olive", "7b7a3a"), ("lavender", "b9a7e8"), ("skyblue", "9fd3f0"), ("pool", "3fc1d9"),
            ("marble", "e9e6e1"), ("darkmarble", "2b2b30"), ("rosegold", "d9a08c"), ("emerald", "1f8a5b"), ("crimson", "b0172b"),
            ("discord", "7b68ee"), ("teal2", "2aa198"), ("paper", "f7f3e8"), ("cardboard", "b98d5a"), ("pizza", "e8a33d"),
            // Look-dev (round 3): warm fabrics and woods matched to the painted concepts.
            ("rose", "c25560"), ("dustpink", "d98a92"), ("tan", "c8a27c"), ("honey", "c98a4a"), ("sage", "8fa58a"),
            ("limestone", "e2d3b2"), ("stoneshade", "b9a888"), ("water2", "4fb3c8"), ("waterlight", "a6e3ea"), ("awningblue", "3b6fb6"),
            ("awningred", "c8453b"), ("awninggreen", "2f8a4e"), ("leafdark", "3d6b35"), ("ivy", "5a9441"), ("wisteria", "9a7fd1"),
            ("wisterialight", "c4aef0"), ("shutterblue", "4a78a8"), ("shuttergreen", "5b8a5a"), ("heliusorange", "e84125"), ("bonkyellow", "ffc93c"),
            ("bonkorange", "fc8e03"), ("penguinblue", "6fa8dc"), ("ghostpurple", "9b84e8"), ("clay", "7cbf6a"), ("iron", "2b2d31"),
            ("petalred", "e0455a"), ("petalpink", "f08cb4"), ("petalwhite", "f7f2ea"), ("petalyellow", "f5cf4a"), ("cypress", "2f5232"),
            ("stuccocream", "ecdab8"), ("stuccoochre", "e2b670"), ("stuccopeach", "e8b08c"), ("stuccoblue", "96a8d2"), ("stuccostone", "cec0a6"),
            ("stuccosage", "b2c0a0"), ("rooftile", "c0603a"),
        };

        private static Dictionary<string, int> _index;
        private static Material _lit, _glow;

        public static int IndexOf(string name)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < Table.Length; i++) _index[Table[i].name] = i;
            }
            if (!_index.TryGetValue(name, out int index)) throw new ArgumentException("Unknown venue palette colour: " + name);
            return index;
        }

        public static Color ColorOf(string name)
        {
            ColorUtility.TryParseHtmlString("#" + Table[IndexOf(name)].hex, out var c);
            return c;
        }

        /// <summary>UV (atlas space, origin bottom-left) at the centre of the colour's swatch.</summary>
        public static Vector2 Uv(string name)
        {
            int i = IndexOf(name);
            return new Vector2(((i % Grid) + 0.5f) / Grid, ((i / Grid) + 0.5f) / Grid);
        }

        public static void Reset() { _lit = _glow = null; }

        public static Texture2D EnsureTexture()
        {
            if (Table.Length > Grid * Grid) throw new InvalidOperationException("Venue palette overflow.");
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            var tex = new Texture2D(Grid * Cell, Grid * Cell, TextureFormat.RGBA32, false);
            var fill = new Color32[Grid * Cell * Grid * Cell];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 0, 255, 255);
            for (int i = 0; i < Table.Length; i++)
            {
                ColorUtility.TryParseHtmlString("#" + Table[i].hex, out var c);
                Color32 c32 = c;
                int ox = (i % Grid) * Cell, oy = (i / Grid) * Cell;
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++) fill[(oy + y) * Grid * Cell + ox + x] = c32;
            }
            tex.SetPixels32(fill);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (!File.Exists(TexturePath) || !BytesEqual(File.ReadAllBytes(TexturePath), png))
            {
                File.WriteAllBytes(TexturePath, png);
                AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            bool dirty = importer.filterMode != FilterMode.Point || importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp;
            var android = importer.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.format != TextureImporterFormat.ASTC_4x4) dirty = true;
            if (dirty)
            {
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                android.overridden = true;
                android.maxTextureSize = 256;
                android.format = TextureImporterFormat.ASTC_4x4;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Shared lit material for procedural blocks.</summary>
        public static Material Lit => _lit != null ? _lit : _lit = SharedMaterial("VB_Palette", false);

        /// <summary>Shared unlit material for neon trims and glowing blocks (never darkened by the bake).</summary>
        public static Material Glow => _glow != null ? _glow : _glow = SharedMaterial("VB_Glow", true);

        private static Material SharedMaterial(string name, bool unlit)
        {
            string path = SharedRoot + "/Materials/" + name + ".mat";
            Directory.CreateDirectory(SharedRoot + "/Materials");
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidOperationException("URP shader missing for " + name);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", EnsureTexture());
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
