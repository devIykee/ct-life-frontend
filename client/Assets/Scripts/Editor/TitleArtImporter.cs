using UnityEditor;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Import settings for the title screen art in Resources/UI/Title (see art/LICENSES.md "Title screen"): the key art
    /// (title-keyart-b.png, 3840×1726) is a UI image (no mipmaps, ASTC 6x6, max 4096 so it is never halved; about 2.9 MB on the
    /// GPU and in the APK, the same as the painting it replaced); the lockup (title-logo.png, 900×360 with alpha) and the
    /// painted button bodies keep sharp alpha edges (ASTC 4x4). Applied on every import, so the settings do not depend on
    /// hand-edited .meta files.
    /// </summary>
    public sealed class TitleArtImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/UI/Title/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder) || !(assetImporter is TextureImporter ti)) return;
            bool button = assetPath.Contains("/btn-");
            bool logo = assetPath.Contains("/title-logo");
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.alphaIsTransparency = button || logo;
            ti.alphaSource = button || logo ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            int max = button ? 2048 : logo ? 1024 : 4096;
            ti.maxTextureSize = max;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = max;
            android.format = button || logo ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }
    }
}
