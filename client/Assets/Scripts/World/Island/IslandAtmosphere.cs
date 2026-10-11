using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// CT Island v2 light and air (World lane): one golden-hour sun with realtime shadows near the camera, trilight
    /// ambient, the sky material, long-range fog and the world shaders' warm distance haze (_CTWorldHaze* globals).
    /// Applied while the island is loaded and fully restored afterwards, so venues, the lobby and captures keep their
    /// own lighting. The URP asset only enables main-light shadows; the island sets the shadow distance itself.
    /// </summary>
    public sealed class IslandAtmosphere : MonoBehaviour
    {
        [Header("Sun")]
        public Color sunColor = new Color(1f, 0.86f, 0.68f);
        public float sunIntensity = 1.15f;
        [Tooltip("x = elevation (degrees), y = heading the light travels towards (degrees clockwise from north).")]
        public Vector2 sunAngles = new Vector2(30f, 38f);
        public float shadowDistance = 70f;
        [Range(0f, 1f)] public float shadowStrength = 0.72f;

        [Header("Ambient")]
        public Color skyAmbient = new Color(0.55f, 0.62f, 0.78f);
        public Color equatorAmbient = new Color(0.70f, 0.62f, 0.55f);
        public Color groundAmbient = new Color(0.34f, 0.31f, 0.27f);
        public Material skybox;

        [Header("Fog and haze")]
        public Color fogColor = new Color(0.92f, 0.84f, 0.74f);
        public float fogStart = 900f, fogEnd = 6000f;
        public Color hazeColor = new Color(0.90f, 0.83f, 0.75f);
        public Color sunGlowColor = new Color(1f, 0.78f, 0.55f);
        [Range(0f, 1f)] public float hazeStrength = 0.42f;
        public float hazeStart = 220f, hazeEnd = 2400f, hazeHeightFalloff = 0.006f, sunGlow = 0.9f;

        private static readonly int HazeColorId = Shader.PropertyToID("_CTWorldHazeColor");
        private static readonly int HazeParamsId = Shader.PropertyToID("_CTWorldHazeParams");
        private static readonly int SunGlowId = Shader.PropertyToID("_CTWorldSunGlow");

        private bool _applied;
        private Light _sun;
        private Light _oldSun;
        private AmbientMode _oldMode;
        private Color _oldSky, _oldEq, _oldGround, _oldFogColor;
        private bool _oldFog;
        private FogMode _oldFogMode;
        private float _oldFogStart, _oldFogEnd, _oldShadowDistance;
        private Material _oldSkybox;

        public Light Sun => _sun;

        private void OnEnable() => Apply();
        private void OnDisable() => Restore();

        public void Apply()
        {
            if (_applied) return;
            _applied = true;
            _oldSun = RenderSettings.sun;
            _oldMode = RenderSettings.ambientMode;
            _oldSky = RenderSettings.ambientSkyColor; _oldEq = RenderSettings.ambientEquatorColor; _oldGround = RenderSettings.ambientGroundColor;
            _oldFog = RenderSettings.fog; _oldFogMode = RenderSettings.fogMode; _oldFogColor = RenderSettings.fogColor;
            _oldFogStart = RenderSettings.fogStartDistance; _oldFogEnd = RenderSettings.fogEndDistance;
            _oldSkybox = RenderSettings.skybox;
            if (_sun == null)
            {
                var go = new GameObject("Island sun");
                go.transform.SetParent(transform, false);
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
            }
            _sun.color = sunColor;
            _sun.intensity = sunIntensity;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = shadowStrength;
            _sun.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y, 0f);
            _sun.enabled = true;
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = skyAmbient;
            RenderSettings.ambientEquatorColor = equatorAmbient;
            RenderSettings.ambientGroundColor = groundAmbient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            if (skybox != null)
            {
                if (skybox.HasProperty("_Horizon")) skybox.SetColor("_Horizon", fogColor);   // the far sea melts into the sky
                RenderSettings.skybox = skybox;
            }
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                _oldShadowDistance = urp.shadowDistance;
                urp.shadowDistance = shadowDistance;
            }
            PushHaze();
        }

        public void PushHaze()
        {
            Shader.SetGlobalVector(HazeColorId, new Vector4(hazeColor.r, hazeColor.g, hazeColor.b, hazeStrength));
            Shader.SetGlobalVector(HazeParamsId, new Vector4(hazeStart, hazeEnd, hazeHeightFalloff, sunGlow));
            Shader.SetGlobalVector(SunGlowId, new Vector4(sunGlowColor.r, sunGlowColor.g, sunGlowColor.b, 1f));
        }

        public void Restore()
        {
            if (!_applied) return;
            _applied = false;
            RenderSettings.sun = _oldSun;
            RenderSettings.ambientMode = _oldMode;
            RenderSettings.ambientSkyColor = _oldSky; RenderSettings.ambientEquatorColor = _oldEq; RenderSettings.ambientGroundColor = _oldGround;
            RenderSettings.fog = _oldFog; RenderSettings.fogMode = _oldFogMode; RenderSettings.fogColor = _oldFogColor;
            RenderSettings.fogStartDistance = _oldFogStart; RenderSettings.fogEndDistance = _oldFogEnd;
            RenderSettings.skybox = _oldSkybox;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.shadowDistance = _oldShadowDistance;
            Shader.SetGlobalVector(HazeColorId, Vector4.zero);
            if (_sun != null) _sun.enabled = false;
        }
    }
}
