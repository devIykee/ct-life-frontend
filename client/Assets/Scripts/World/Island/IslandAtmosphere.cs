using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// CT Island v2 light and air (World lane): one warm golden-hour sun with realtime shadows near the camera, trilight
    /// ambient (also pushed to the world shaders, which shade with it directly), soft deep shadows, the sky material, long-range fog and a light, cool aerial perspective (_CTWorldHaze* globals): distant
    /// hills turn slightly blue, the foreground keeps its warmth and saturation, and only the sun side glows warm.
    /// Applied while the island is loaded and fully restored afterwards, so venues, the lobby and captures keep their
    /// own lighting. The URP asset only enables main-light shadows; the island sets the shadow distance itself.
    /// </summary>
    public sealed class IslandAtmosphere : MonoBehaviour
    {
        [Header("Sun")]
        public Color sunColor = new Color(1f, 0.86f, 0.68f);
        public float sunIntensity = 1.35f;
        [Tooltip("x = elevation (degrees), y = heading the light travels towards (degrees clockwise from north).")]
        public Vector2 sunAngles = new Vector2(30f, 38f);
        // 2026-10-22 ("the light feels like lines"): one 2048 cascade over 70 m stair-stepped thin reveals and frames into
        // dotted lines. Two cascades put a fine map on the first 15 m around the third-person camera; the PCF is a touch wider
        // and the shadows a touch less black, so the edges that remain read as soft shade, not drawn lines.
        public float shadowDistance = 60f;
        [Range(1, 4)] public int shadowCascades = 2;
        [Range(0.05f, 0.6f)] public float cascadeSplit = 0.25f;
        [Range(0f, 1f)] public float shadowStrength = 0.88f;
        [Tooltip("Share of the sun that still reaches shadowed surfaces (bounce). Lower = deeper shadows.")]
        [Range(0f, 1f)] public float sunInShadow = 0.09f;
        [Tooltip("Soft-shadow radius in shadow-map texels (4-tap PCF in the world shaders).")]
        public float shadowSoftness = 1.6f;
        [Tooltip("Ambient multiplier on shadowed and sun-averted surfaces; 1 = flat fill, lower = clearer sun direction.")]
        [Range(0f, 1f)] public float shadowFill = 0.8f;

        [Header("Ambient")]
        public Color skyAmbient = new Color(0.52f, 0.62f, 0.80f);
        public Color equatorAmbient = new Color(0.66f, 0.64f, 0.60f);
        public Color groundAmbient = new Color(0.32f, 0.31f, 0.27f);
        public Material skybox;

        [Header("Environment (world v3: Poly Haven HDRI)")]
        [Tooltip("The sky's HDRI as a cubemap (its sun turned to this sun): what the Kit PBR shader and the island's reflection probe (URP Lit cars, characters) reflect.")]
        public Cubemap environment;
        public float environmentIntensity = 1f;

        [Header("Sky reflection (world v3 PBR kit, without an environment)")]
        [Tooltip("What smooth v3 surfaces (glass, polished stone, metal) reflect: sky straight up, at the horizon, below it.")]
        public Color reflZenith = new Color(0.50f, 0.66f, 0.92f);
        public Color reflHorizon = new Color(0.96f, 0.90f, 0.82f);
        public Color reflGround = new Color(0.34f, 0.32f, 0.30f);

        [Header("Fog and haze")]
        public Color fogColor = new Color(0.76f, 0.84f, 0.92f);
        public float fogStart = 1400f, fogEnd = 7000f;
        public Color hazeColor = new Color(0.62f, 0.74f, 0.88f);
        public Color sunGlowColor = new Color(1f, 0.82f, 0.62f);
        [Range(0f, 1f)] public float hazeStrength = 0.30f;
        public float hazeStart = 320f, hazeEnd = 2800f, hazeHeightFalloff = 0.004f, sunGlow = 0.55f;

        private static readonly int HazeColorId = Shader.PropertyToID("_CTWorldHazeColor");
        private static readonly int HazeParamsId = Shader.PropertyToID("_CTWorldHazeParams");
        private static readonly int SunGlowId = Shader.PropertyToID("_CTWorldSunGlow");
        private static readonly int AmbSkyId = Shader.PropertyToID("_CTWorldAmbSky");
        private static readonly int AmbEquatorId = Shader.PropertyToID("_CTWorldAmbEquator");
        private static readonly int AmbGroundId = Shader.PropertyToID("_CTWorldAmbGround");
        private static readonly int ShadowParamsId = Shader.PropertyToID("_CTWorldShadowParams");
        private static readonly int ReflZenithId = Shader.PropertyToID("_CTWorldReflZenith");
        private static readonly int ReflHorizonId = Shader.PropertyToID("_CTWorldReflHorizon");
        private static readonly int ReflGroundId = Shader.PropertyToID("_CTWorldReflGround");
        private static readonly int EnvCubeId = Shader.PropertyToID("_CTWorldEnvCube");
        private static readonly int EnvParamsId = Shader.PropertyToID("_CTWorldEnvParams");
        private ReflectionProbe _probe;

        private bool _applied;
        private Light _sun;
        private Light _oldSun;
        private AmbientMode _oldMode;
        private Color _oldSky, _oldEq, _oldGround, _oldFogColor;
        private bool _oldFog;
        private FogMode _oldFogMode;
        private float _oldFogStart, _oldFogEnd, _oldShadowDistance, _oldCascadeSplit;
        private int _oldCascades;
        private bool _oldDither;
        private Camera _ditherCamera;
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
                _oldCascades = urp.shadowCascadeCount; _oldCascadeSplit = urp.cascade2Split;
                urp.shadowDistance = shadowDistance;
                urp.shadowCascadeCount = shadowCascades;
                urp.cascade2Split = cascadeSplit;
            }
            // dither the final image: the HDR sky and long soft gradients on paving otherwise band into visible steps
            _ditherCamera = Camera.main;
            if (_ditherCamera != null && _ditherCamera.TryGetComponent<UniversalAdditionalCameraData>(out var camData))
            { _oldDither = camData.dithering; camData.dithering = true; }
            if (environment != null)
            {
                // one island-wide custom probe: URP Lit surfaces (cars, characters, venue shells) reflect the same sky
                if (_probe == null)
                {
                    var pg = new GameObject("Island sky reflection");
                    pg.transform.SetParent(transform, false);
                    _probe = pg.AddComponent<ReflectionProbe>();
                    _probe.mode = ReflectionProbeMode.Custom;
                    _probe.size = new Vector3(12000f, 4000f, 12000f);
                    _probe.boxProjection = false;
                    _probe.importance = 0;
                    _probe.blendDistance = 0f;
                }
                _probe.customBakedTexture = environment;
                _probe.intensity = environmentIntensity;
                _probe.enabled = true;
            }
            PushHaze();
        }

        public void PushHaze()
        {
            Shader.SetGlobalVector(HazeColorId, new Vector4(hazeColor.r, hazeColor.g, hazeColor.b, hazeStrength));
            Shader.SetGlobalVector(HazeParamsId, new Vector4(hazeStart, hazeEnd, hazeHeightFalloff, sunGlow));
            Shader.SetGlobalVector(SunGlowId, new Vector4(sunGlowColor.r, sunGlowColor.g, sunGlowColor.b, 1f));
            // the world shaders' own trilight ambient and shadow shaping (a = 1 marks them set)
            Shader.SetGlobalVector(AmbSkyId, new Vector4(skyAmbient.r, skyAmbient.g, skyAmbient.b, 1f));
            Shader.SetGlobalVector(AmbEquatorId, new Vector4(equatorAmbient.r, equatorAmbient.g, equatorAmbient.b, 1f));
            Shader.SetGlobalVector(AmbGroundId, new Vector4(groundAmbient.r, groundAmbient.g, groundAmbient.b, 1f));
            Shader.SetGlobalVector(ShadowParamsId, new Vector4(sunInShadow, shadowSoftness, shadowFill, 0f));
            Shader.SetGlobalVector(ReflZenithId, new Vector4(reflZenith.r, reflZenith.g, reflZenith.b, 1f));
            Shader.SetGlobalVector(ReflHorizonId, new Vector4(reflHorizon.r, reflHorizon.g, reflHorizon.b, 1f));
            Shader.SetGlobalVector(ReflGroundId, new Vector4(reflGround.r, reflGround.g, reflGround.b, 1f));
            if (environment != null)
            {
                Shader.SetGlobalTexture(EnvCubeId, environment);
                Shader.SetGlobalVector(EnvParamsId, new Vector4(environmentIntensity, environment.mipmapCount, 0f, 0f));
            }
            else Shader.SetGlobalVector(EnvParamsId, Vector4.zero);
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
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = _oldShadowDistance;
                urp.shadowCascadeCount = Mathf.Max(1, _oldCascades); urp.cascade2Split = _oldCascadeSplit;
            }
            if (_ditherCamera != null && _ditherCamera.TryGetComponent<UniversalAdditionalCameraData>(out var camData)) camData.dithering = _oldDither;
            _ditherCamera = null;
            Shader.SetGlobalVector(HazeColorId, Vector4.zero);
            Shader.SetGlobalVector(AmbSkyId, Vector4.zero);
            Shader.SetGlobalVector(ReflZenithId, Vector4.zero);
            Shader.SetGlobalVector(EnvParamsId, Vector4.zero);
            if (_probe != null) _probe.enabled = false;
            if (_sun != null) _sun.enabled = false;
        }
    }
}
