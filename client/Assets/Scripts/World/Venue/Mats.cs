using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Runtime materials for greybox geometry and placeholder avatars. Prefers the URP Simple Lit shader
    /// (cheap on Mali), falls back to URP Lit/Unlit. ProjectSetup adds these shaders to "Always Included Shaders"
    /// so Shader.Find works in player builds.
    /// </summary>
    public static class Mats
    {
        private static readonly Dictionary<int, Material> _byColor = new Dictionary<int, Material>();
        private static Shader _lit;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public static Shader LitShader
        {
            get
            {
                if (_lit != null) return _lit;
                // Resources/Materials/GreyboxLit.mat references URP Simple Lit by GUID, which also guarantees the
                // shader ships in the build; Shader.Find is only the fallback.
                var mat = Resources.Load<Material>("Materials/GreyboxLit");
                if (mat != null && mat.shader != null && mat.shader.isSupported) return _lit = mat.shader;
                _lit = Shader.Find("Universal Render Pipeline/Simple Lit")
                       ?? Shader.Find("Universal Render Pipeline/Lit")
                       ?? Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Standard");
                return _lit;
            }
        }

        public static Material Color(Color c)
        {
            int key = ((int)(c.r * 255) << 24) | ((int)(c.g * 255) << 16) | ((int)(c.b * 255) << 8) | (int)(c.a * 255);
            if (_byColor.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(LitShader) { name = "greybox_" + key.ToString("X8"), enableInstancing = true };
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, c);
            else m.color = c;
            _byColor[key] = m;
            return m;
        }

        public static Material Emissive(Color c, float intensity = 2f)
        {
            var m = new Material(Color(c));
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColor, c * intensity);
            return m;
        }

        public static Material Textured(Texture2D tex)
        {
            var m = new Material(LitShader) { name = "tex_" + tex.name, enableInstancing = true };
            if (m.HasProperty(BaseMap)) m.SetTexture(BaseMap, tex);
            else m.mainTexture = tex;
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, UnityEngine.Color.white);
            return m;
        }
    }
}
