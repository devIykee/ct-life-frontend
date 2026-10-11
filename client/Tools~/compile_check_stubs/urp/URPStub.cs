// COMPILE-CHECK STUB ONLY (never imported by Unity: lives under Tools~). Mirrors the exact public surface of
// com.unity.render-pipelines.universal 17.3.0 that CT Life code touches, including which setters are internal,
// copied from BuiltInPackages/.../Runtime/Data/UniversalRenderPipelineAsset.cs and UniversalRenderPipelineCore.cs.
namespace UnityEngine.Rendering.Universal
{
    public enum LightRenderingMode { Disabled = 0, PerVertex = 2, PerPixel = 1 }
    public enum ShadowQuality { Disabled, HardShadows, SoftShadows }

    public abstract class ScriptableRendererData : ScriptableObject { }

    public class UniversalRendererData : ScriptableRendererData { }

    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline() => null;
        public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null) => null;
        public float renderScale { get; set; }
        public bool useAdaptivePerformance { get; set; }
        public bool supportsHDR { get; set; }
        public int msaaSampleCount { get; set; }
        public bool supportsMainLightShadows { get; internal set; }
        public LightRenderingMode additionalLightsRenderingMode { get; internal set; }
        public bool supportsCameraDepthTexture { get; set; }
        public bool supportsCameraOpaqueTexture { get; set; }
        public float shadowDistance { get; set; }
        public bool useSRPBatcher { get; set; }
        public bool supportsDynamicBatching { get; set; }
    }

    public sealed class UniversalRenderPipeline
    {
        public static UniversalRenderPipelineAsset asset => null;

        // Mirrors URP 17.3.0 Runtime/UniversalRenderPipeline.cs (render-request API used by VenueVerify).
        public class SingleCameraRequest
        {
            public RenderTexture destination = null;
            public int mipLevel = 0;
            public CubemapFace face = CubemapFace.Unknown;
            public int slice = 0;
        }
    }
    // Mirrors URP 17.3.0 Runtime/UniversalAdditionalCameraData.cs (CameraExtensions line 171, class line 442,
    // renderPostProcessing line 789), used by World/Venue/VenueLook.cs.
    // UniversalAdditionalCameraData.cs l.42 (AntialiasingMode), l.93 (AntialiasingQuality), l.799/809 (properties).
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }
    public enum AntialiasingQuality { Low, Medium, High }

    public partial class UniversalAdditionalCameraData : MonoBehaviour
    {
        public AntialiasingMode antialiasing { get; set; }
        public AntialiasingQuality antialiasingQuality { get; set; }
        public bool renderPostProcessing { get; set; }
        public bool renderShadows { get; set; }
        public bool allowXRRendering { get; set; }
    }

    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera camera) => null;
    }
}
