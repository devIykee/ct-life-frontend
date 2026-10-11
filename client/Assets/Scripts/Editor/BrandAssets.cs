using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Generates the CT Life brand assets from the single source art/branding/logo (README there) and applies them to
    /// the Android player: adaptive icon (foreground = transparent mark inside the 66 dp safe circle, background = the
    /// purple sampled from ct-mark-on-purple), legacy square + round icons, the Android static splash image and the
    /// splash settings ("Made with Unity" off: optional for Unity Personal since Unity 6). Runs before every build
    /// (BuildScript) so icons always match the source; also menu CT Life → Build → Apply brand assets.
    /// </summary>
    public static class BrandAssets
    {
        private const string OutDir = "Assets/Art/branding/generated";
        public const string UiMark = "Assets/Resources/UI/Brand/ct-mark.png";
        private static string Source(string file) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/branding/logo", file));

        [MenuItem("CT Life/Build/Apply brand assets")]
        public static void Apply()
        {
            if (!File.Exists(Source("ct-mark-transparent-1254.png")))
            {
                // A checkout without art/ (e.g. a partial clone) keeps the committed generated assets and settings.
                Debug.LogWarning($"CTLIFE [brand] source {Source("")} missing; keeping committed icons/splash");
                return;
            }
            var mark = Load(Source("ct-mark-transparent-1254.png"));
            var onPurple = Load(Source("ct-mark-on-purple-1254.png"));
            var purple = (Color)onPurple.GetPixel(8, 8);
            purple.a = 1f;
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(Path.GetDirectoryName(UiMark));

            // In-app mark: the master, capped for UI use by its importer.
            WriteIfChanged(UiMark, File.ReadAllBytes(Source("ct-mark-transparent-1254.png")));

            float radius = OpaqueRadius(mark, out var centre);
            var fg = Compose(432, 432, Color.clear, mark, centre, 0.61f * 432f / 2f * 0.96f / radius, false);
            var bg = Compose(432, 432, purple, null, centre, 0f, false);
            var legacy = Compose(192, 192, purple, mark, centre, 0.78f * 96f / radius, false);
            var round = Compose(192, 192, purple, mark, centre, 0.82f * 96f / radius, true);
            var splash = Compose(2670, 1200, purple, mark, centre, 0.22f * 1200f / radius, false);
            Write(fg, "icon_adaptive_foreground_432.png");
            Write(bg, "icon_adaptive_background_432.png");
            Write(legacy, "icon_legacy_192.png");
            Write(round, "icon_round_192.png");
            Write(splash, "splash_landscape_2670x1200.png");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporters();

            Texture2D T(string f) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{OutDir}/{f}");
            var target = NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(target))
            {
                var name = kind.ToString();
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (var icon in icons)
                {
                    if (name.IndexOf("Adaptive", StringComparison.OrdinalIgnoreCase) >= 0)
                        icon.SetTextures(T("icon_adaptive_background_432.png"), T("icon_adaptive_foreground_432.png"));
                    else if (name.IndexOf("Round", StringComparison.OrdinalIgnoreCase) >= 0)
                        icon.SetTexture(T("icon_round_192.png"));
                    else
                        icon.SetTexture(T("icon_legacy_192.png"));
                }
                PlayerSettings.SetPlatformIcons(target, kind, icons);
            }

            // Splash: no "Made with Unity" (optional on Unity Personal from Unity 6; see docs/progress/integration.md).
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.backgroundColor = purple;
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var image = so.FindProperty("androidSplashScreen");
                var scale = so.FindProperty("AndroidSplashScreenScale");
                if (image != null) image.objectReferenceValue = T("splash_landscape_2670x1200.png");
                if (scale != null) scale.intValue = 2; // scale to fill (the image is the Seeker's landscape size)
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"CTLIFE [brand] icons, splash and UI mark applied; brand purple #{ColorUtility.ToHtmlStringRGB(purple)}");
        }

        public static void ApplyBatch()
        {
            int code = 0;
            try { Apply(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static void ConfigureImporters()
        {
            foreach (var path in Directory.GetFiles(OutDir, "*.png").Select(p => p.Replace('\\', '/')).Append(UiMark))
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                var before = EditorJsonUtility.ToJson(ti) + JsonUtility.ToJson(ti.GetPlatformTextureSettings("Android"));
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = path == UiMark;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = path == UiMark ? 512 : 4096;
                ti.textureCompression = path == UiMark ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
                if (path == UiMark)
                {
                    var android = ti.GetPlatformTextureSettings("Android");
                    android.overridden = true;
                    android.maxTextureSize = 512;
                    android.format = TextureImporterFormat.ASTC_4x4;
                    ti.SetPlatformTextureSettings(android);
                }
                if (EditorJsonUtility.ToJson(ti) + JsonUtility.ToJson(ti.GetPlatformTextureSettings("Android")) != before)
                    ti.SaveAndReimport();
            }
        }

        private static Texture2D Load(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(path))) throw new InvalidDataException(path);
            return t;
        }

        /// <summary>Largest distance of an opaque pixel from the opaque bounding-box centre (source pixels).</summary>
        private static float OpaqueRadius(Texture2D t, out Vector2 centre)
        {
            var px = t.GetPixels32();
            int w = t.width, h = t.height, minX = w, minY = h, maxX = 0, maxY = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 16) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            centre = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
            float r2 = 0;
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                if (px[y * w + x].a > 16) r2 = Math.Max(r2, (x - centre.x) * (x - centre.x) + (y - centre.y) * (y - centre.y));
            return Mathf.Sqrt(r2);
        }

        /// <summary>
        /// Canvas filled with <paramref name="fill"/>, then the mark (scaled about its centre to the canvas centre,
        /// area-averaged so downscaling stays clean), composited with straight alpha. Optional circular mask.
        /// </summary>
        private static Texture2D Compose(int w, int h, Color fill, Texture2D mark, Vector2 markCentre, float scale, bool circle)
        {
            var dst = new Color[w * h];
            for (int i = 0; i < dst.Length; i++) dst[i] = fill;
            if (mark != null && scale > 0)
            {
                var src = mark.GetPixels();
                int sw = mark.width, sh = mark.height;
                float inv = 1f / scale;
                int r = Math.Max(1, Mathf.CeilToInt(inv / 2f));
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sx = (x + 0.5f - w / 2f) * inv + markCentre.x, sy = (y + 0.5f - h / 2f) * inv + markCentre.y;
                    if (sx < -r || sy < -r || sx >= sw + r || sy >= sh + r) continue;
                    Color acc = Color.clear; int n = 0;
                    int cx = (int)sx, cy = (int)sy;
                    for (int dy = -r + 1; dy <= r; dy++)
                    for (int dx = -r + 1; dx <= r; dx++)
                    {
                        int ux = cx + dx, uy = cy + dy;
                        n++;
                        if (ux < 0 || uy < 0 || ux >= sw || uy >= sh) continue;
                        var c = src[uy * sw + ux];
                        acc.r += c.r * c.a; acc.g += c.g * c.a; acc.b += c.b * c.a; acc.a += c.a;
                    }
                    if (acc.a <= 0f) continue;
                    var m = new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a / n);
                    var d = dst[y * w + x];
                    float a = m.a + d.a * (1f - m.a);
                    dst[y * w + x] = a <= 0f ? Color.clear :
                        new Color((m.r * m.a + d.r * d.a * (1f - m.a)) / a, (m.g * m.a + d.g * d.a * (1f - m.a)) / a, (m.b * m.a + d.b * d.a * (1f - m.a)) / a, a);
                }
            }
            if (circle)
            {
                float rr = w / 2f;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - rr) * (x + 0.5f - rr) + (y + 0.5f - rr) * (y + 0.5f - rr));
                    float cov = Mathf.Clamp01(rr - d + 0.5f);
                    var c = dst[y * w + x]; c.a *= cov; dst[y * w + x] = c;
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(dst);
            tex.Apply();
            return tex;
        }

        private static void Write(Texture2D t, string file) => WriteIfChanged($"{OutDir}/{file}", t.EncodeToPNG());

        // Unchanged bytes are not rewritten, so a build only reimports brand textures when the source art changed.
        private static void WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
            File.WriteAllBytes(path, bytes);
        }
    }
}
