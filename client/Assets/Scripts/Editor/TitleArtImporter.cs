using UnityEditor;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Import settings for the title screen art in Resources/UI/Title (art direction 2026-10-08, see art/LICENSES.md "Title screen"):
    /// the background is a UI image (no mipmaps, ASTC 6x6, max 4096 so the 3840×1727 high-res art is never halved: the user asked
    /// for its crispness; about 2.9 MB on the GPU and in the APK); the painted button bodies keep sharp alpha edges (ASTC 4x4,
    /// max 2048). Applied on every import, so the settings do not depend on hand-edited .meta files.
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
            int max = button ? 2048 : 4096;
            ti.maxTextureSize = max;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = max;
            android.format = button ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }
    }
}
