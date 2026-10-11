using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// Per-venue look (venue builder, round 3): baked light probes for characters, the camera's clear colour (the
    /// painted sky's horizon), post-processing on the gameplay camera for the venue's global Volume (colour grade,
    /// neutral tonemapping, threshold bloom on emissive lights, light vignette) and a scale for the realtime sun on
    /// characters (the probes already hold the baked sun). Everything is restored when the venue unloads.
    /// <see cref="PostProcessingEnabled"/> is the Settings switch (Build Spec: each post effect can be turned off).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VenueLook : MonoBehaviour
    {
        /// <summary>Settings hook: off = no post-processing on the gameplay camera (grade, bloom, vignette).</summary>
        public static bool PostProcessingEnabled = true;

        public LightProbes lightProbes;
        public Color skyColor = new Color(0.62f, 0.78f, 0.92f);
        [Range(0f, 1.5f)] public float characterSunScale = 0.65f;

        private LightProbes _previousProbes;
        private Camera _camera;
        private Color _previousBackground;
        private bool _previousPost, _applied;
        private Light _sun;
        private float _previousSun;

        private void Start()
        {
            if (Application.isPlaying) Apply(Camera.main);
        }

        /// <summary>Apply to a camera (also used by editor captures).</summary>
        public void Apply(Camera camera)
        {
            if (_applied) return;
            _applied = true;
            if (lightProbes != null)
            {
                _previousProbes = LightmapSettings.lightProbes;
                LightmapSettings.lightProbes = lightProbes;
            }
            _camera = camera;
            if (_camera != null)
            {
                _previousBackground = _camera.backgroundColor;
                _camera.backgroundColor = skyColor;
                var data = _camera.GetUniversalAdditionalCameraData();
                _previousPost = data.renderPostProcessing;
                data.renderPostProcessing = PostProcessingEnabled;
            }
            _sun = RenderSettings.sun;
            if (_sun != null && lightProbes != null)
            {
                _previousSun = _sun.intensity;
                _sun.intensity *= characterSunScale;
            }
        }

        public void Restore()
        {
            if (!_applied) return;
            _applied = false;
            if (lightProbes != null && LightmapSettings.lightProbes == lightProbes) LightmapSettings.lightProbes = _previousProbes;
            // Only undo what is still ours: VenueContentLighting or the next venue may already have reset these.
            if (_camera != null)
            {
                if (_camera.backgroundColor == skyColor) _camera.backgroundColor = _previousBackground;
                _camera.GetUniversalAdditionalCameraData().renderPostProcessing = _previousPost;
            }
            if (_sun != null && lightProbes != null && Mathf.Approximately(_sun.intensity, _previousSun * characterSunScale))
                _sun.intensity = _previousSun;
            _camera = null;
            _sun = null;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
