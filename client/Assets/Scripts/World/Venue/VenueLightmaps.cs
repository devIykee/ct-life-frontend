using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Lightmaps captured from the venue's editor bake scene. Prefabs do not carry scene lightmap tables,
    /// so a venue registers its own textures and restores the previous table when it leaves.
    /// </summary>
    public sealed class VenueLightmaps : MonoBehaviour
    {
        [Serializable]
        public sealed class Binding
        {
            public MeshRenderer renderer;
            public int lightmap;
            public Vector4 scaleOffset;
        }

        public Texture2D[] colors = Array.Empty<Texture2D>();
        public Binding[] bindings = Array.Empty<Binding>();
        public LightmapsMode mode = LightmapsMode.NonDirectional;

        private LightmapData[] _previous, _installed;
        private LightmapsMode _previousMode;
        private int[] _oldIndices;
        private Vector4[] _oldOffsets;
        private bool _applied;

        private void OnEnable()
        {
            if (Application.isPlaying) Apply();
        }

        public void Apply()
        {
            if (_applied || colors == null || colors.Length == 0) return;
            foreach (var color in colors)
                if (color == null) throw new InvalidOperationException("Venue has a missing baked lightmap texture.");
            foreach (var binding in bindings)
                if (binding.renderer == null || binding.lightmap < 0 || binding.lightmap >= colors.Length)
                    throw new InvalidOperationException("Venue has an invalid renderer lightmap binding.");

            _previous = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            _previousMode = LightmapSettings.lightmapsMode;
            _installed = new LightmapData[_previous.Length + colors.Length];
            Array.Copy(_previous, _installed, _previous.Length);
            for (int i = 0; i < colors.Length; i++)
                _installed[_previous.Length + i] = new LightmapData { lightmapColor = colors[i] };
            LightmapSettings.lightmapsMode = mode;
            LightmapSettings.lightmaps = _installed;
            _oldIndices = new int[bindings.Length];
            _oldOffsets = new Vector4[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                _oldIndices[i] = binding.renderer.lightmapIndex;
                _oldOffsets[i] = binding.renderer.lightmapScaleOffset;
                binding.renderer.lightmapIndex = _previous.Length + binding.lightmap;
                binding.renderer.lightmapScaleOffset = binding.scaleOffset;
            }
            _applied = true;
        }

        public void Restore()
        {
            if (!_applied) return;
            _applied = false;
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].renderer == null) continue;
                bindings[i].renderer.lightmapIndex = _oldIndices[i];
                bindings[i].renderer.lightmapScaleOffset = _oldOffsets[i];
            }
            // Normal travel unloads one venue before loading the next. If an unrelated scene has since
            // changed the global table, do not restore stale indices over that scene's lightmaps.
            var current = LightmapSettings.lightmaps;
            bool ours = current.Length == _installed.Length;
            for (int i = 0; ours && i < current.Length; i++)
                ours = current[i].lightmapColor == _installed[i].lightmapColor &&
                       current[i].lightmapDir == _installed[i].lightmapDir &&
                       current[i].shadowMask == _installed[i].shadowMask;
            if (ours)
            {
                LightmapSettings.lightmaps = _previous;
                LightmapSettings.lightmapsMode = _previousMode;
            }
            _previous = _installed = null;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
