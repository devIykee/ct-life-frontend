using UnityEditor;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Import settings for the baked maps in Resources/UI/Map (MapBaker / AtlasBaker): ASTC with mipmaps (the minimap and the
    /// zoomed-out full map minify them), clamp, trilinear. Area maps cap at 1024, the CT Island atlas at 4096.
    /// </summary>
    public sealed class MapArtImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/UI/Map/", System.StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            bool atlas = assetPath.Contains("/Atlas/");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.filterMode = UnityEngine.FilterMode.Trilinear;
            ti.anisoLevel = 2;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = atlas ? 4096 : 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = atlas ? 4096 : 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }
    }
}
