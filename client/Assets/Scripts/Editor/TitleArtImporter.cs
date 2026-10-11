using UnityEditor;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Import settings for the title screen art in Resources/UI/Title (art direction 2026-10-08, see art/LICENSES.md "Title screen"):
    /// the background is a UI image (no mipmaps, Android max 2048, ASTC 6x6); the painted button bodies keep sharp alpha edges
    /// (ASTC 4x4). Applied on every import, so the settings do not depend on hand-edited .meta files.
    /// </summary>
    public sealed class TitleArtImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/UI/Title/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder) || !(assetImporter is TextureImporter ti)) return;
            bool button = assetPath.Contains("/btn-");
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.alphaIsTransparency = button;
            ti.alphaSource = button ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.maxTextureSize = 2048;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = button ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }
    }
}
