using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CTLife.World
{
    /// <summary>Venue-scoped golden-hour lighting. One sun, no realtime shadows or extra per-pixel lights.</summary>
    public sealed class VenueContentLighting : MonoBehaviour
    {
        public Color sunColor = new Color(1f, 0.88f, 0.73f);
        public float sunIntensity = 1.15f;
        public Vector3 sunAngles = new Vector3(48f, -32f, 0f);
        public Color sky = new Color(0.64f, 0.75f, 0.86f);
        public Color equator = new Color(0.71f, 0.68f, 0.61f);
        public Color ground = new Color(0.38f, 0.36f, 0.32f);
        public Color haze = new Color(0.81f, 0.83f, 0.80f);

        private bool _applied, _createdSun, _oldFog;
        private Light _sun;
        private Light _oldRenderSun;
        private Color _oldSunColor, _oldSky, _oldEquator, _oldGround, _oldFogColor;
        private float _oldSunIntensity, _oldFogStart, _oldFogEnd;
        private Quaternion _oldSunRotation;
        private LightShadows _oldShadows;
        private AmbientMode _oldAmbientMode;
        private FogMode _oldFogMode;
        private Scene _appliedScene;

        public void Apply()
        {
            if (_applied) return;
            _applied = true;
            _appliedScene = SceneManager.GetActiveScene();
            _oldRenderSun = RenderSettings.sun;
            _oldAmbientMode = RenderSettings.ambientMode;
            _oldSky = RenderSettings.ambientSkyColor;
            _oldEquator = RenderSettings.ambientEquatorColor;
            _oldGround = RenderSettings.ambientGroundColor;
            _oldFog = RenderSettings.fog;
            _oldFogMode = RenderSettings.fogMode;
            _oldFogColor = RenderSettings.fogColor;
            _oldFogStart = RenderSettings.fogStartDistance;
            _oldFogEnd = RenderSettings.fogEndDistance;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional && light.enabled) { _sun = light; break; }
            if (_sun == null)
            {
                var sunObject = new GameObject("Golden hour sun");
                sunObject.transform.SetParent(transform, false);
                _sun = sunObject.AddComponent<Light>();
                _sun.type = LightType.Directional;
                _createdSun = true;
            }
            _oldSunColor = _sun.color;
            _oldSunIntensity = _sun.intensity;
            _oldSunRotation = _sun.transform.rotation;
            _oldShadows = _sun.shadows;
            _sun.color = sunColor;
            _sun.intensity = sunIntensity;
            _sun.shadows = LightShadows.None;
            _sun.transform.rotation = Quaternion.Euler(sunAngles);
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = haze;
            // At the shipped portrait camera, storefront bounds are 57–75 m in view Z and the
            // garden crowns 42–64 m. Keep those colors clear; reserve haze for the marina backdrop.
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 165f;
        }

        public void Restore()
        {
            if (!_applied) return;
            _applied = false;
            if (_sun != null && !_createdSun)
            {
                _sun.color = _oldSunColor;
                _sun.intensity = _oldSunIntensity;
                _sun.shadows = _oldShadows;
                _sun.transform.rotation = _oldSunRotation;
            }
            // OnDestroy may run after another scene has become active. Never overwrite that scene's
            // ambient/fog state; VenueLoader explicitly restores before switching active scenes.
            if (SceneManager.GetActiveScene() != _appliedScene) return;
            RenderSettings.sun = _oldRenderSun;
            RenderSettings.ambientMode = _oldAmbientMode;
            RenderSettings.ambientSkyColor = _oldSky;
            RenderSettings.ambientEquatorColor = _oldEquator;
            RenderSettings.ambientGroundColor = _oldGround;
            RenderSettings.fog = _oldFog;
            RenderSettings.fogMode = _oldFogMode;
            RenderSettings.fogColor = _oldFogColor;
            RenderSettings.fogStartDistance = _oldFogStart;
            RenderSettings.fogEndDistance = _oldFogEnd;
        }

        private void OnDestroy() => Restore();
    }
}
