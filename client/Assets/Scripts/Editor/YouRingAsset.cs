using System.IO;
using CTLife.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.EditorTools
{
    /// <summary>Writes Resources/World/YouRing.mat + .png: the "you" ring as a real URP Unlit transparent material asset, so
    /// the transparent shader variant ships in builds (it is a shader_feature and is stripped when only runtime code uses it).</summary>
    public static class YouRingAsset
    {
        public const string Folder = "Assets/Resources/World";

        [MenuItem("CT Life/Map/Write you-ring material")]
        public static void Write()
        {
            var tex = LocalPlayerMarker.RingTexture(readable: true);
            File.WriteAllBytes(Folder + "/YouRing.png", tex.EncodeToPNG());
            AssetDatabase.ImportAsset(Folder + "/YouRing.png");
            var ti = (TextureImporter)AssetImporter.GetAtPath(Folder + "/YouRing.png");
            ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 128;
            ti.SaveAndReimport();
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "YouRing" };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/YouRing.png"));
            m.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(m, Folder + "/YouRing.mat");
            AssetDatabase.SaveAssets();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
