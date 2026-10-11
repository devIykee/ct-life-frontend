using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>Targeted lobby-art import; never rebuilds character or venue content.</summary>
    public static class LobbyArtworkImport
    {
        public static void ConfigurePresentation()
        {
            const string path = "Assets/Resources/UI/Art/CityDiorama.png";
            // The home redesign (2026-10-09) no longer ships the diorama (copy kept in website/assets/city-diorama.png).
            if (!System.IO.File.Exists(path))
            {
                DirectMovementImport.ConfigureLocomotion();
                Debug.Log("CTLIFE [presentation] Lobby diorama not bundled any more; direct locomotion configured.");
                return;
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = TextureImporterFormat.ASTC_4x4;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            DirectMovementImport.ConfigureLocomotion();
            Debug.Log("CTLIFE [presentation] Lobby RGBA artwork: no mipmaps, original NPOT size, Android ASTC 4x4. Direct locomotion configured.");
        }
    }
}
