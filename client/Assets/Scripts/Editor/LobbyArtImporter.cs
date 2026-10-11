using UnityEditor;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Import settings for the Arena lobby art in Resources/UI/Lobby (art/LICENSES.md "Lobby (Arena)"): the plaza render
    /// (3840×1726, opaque UI image, ASTC 6x6, max 4096, no mips: ~2.9 MB), the Invite fren silhouette (alpha, ASTC 4x4, max
    /// 1024) and the Icons8 Windows 11 Color icons (256 px with alpha, ASTC 4x4, max 256: ~4 KB each). Applied on every import.
    /// </summary>
    public sealed class LobbyArtImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/UI/Lobby/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder) || !(assetImporter is TextureImporter ti)) return;
            bool plaza = assetPath.EndsWith("/lobby-plaza.png");
            bool icon = assetPath.StartsWith(Folder + "Icons/");
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.alphaIsTransparency = !plaza;
            ti.alphaSource = plaza ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            int max = plaza ? 4096 : icon ? 256 : 1024;
            ti.maxTextureSize = max;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = max;
            android.format = plaza ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;
            ti.SetPlatformTextureSettings(android);
        }
    }
}
