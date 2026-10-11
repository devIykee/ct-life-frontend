// COMPILE-CHECK STUB ONLY (never imported by Unity: lives under Tools~). Mirrors the public surface of
// com.unity.render-pipelines.core 17.3.0 Runtime/Volume (VolumeProfile.cs: components l.19, Add<T> l.92, Remove l.147;
// Volume.cs: isGlobal l.23, priority l.38, sharedProfile l.63; VolumeParameter.cs: VolumeParameter<T>.Override l.234,
// BoolParameter l.351, FloatParameter l.779, MinFloatParameter l.838, ClampedFloatParameter l.1025, ColorParameter l.1256)
// and the URP 17.3.0 Runtime/Overrides used by Editor/VenueBuild/VenueLookdev.cs.
using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering
{
    public class VolumeComponent : ScriptableObject { }

    public sealed class VolumeProfile : ScriptableObject
    {
        public List<VolumeComponent> components = new List<VolumeComponent>();
        public T Add<T>(bool overrides = false) where T : VolumeComponent => null;
        public void Remove(Type type) { }
    }

    public class Volume : MonoBehaviour
    {
        public bool isGlobal { get; set; }
        public float priority = 0f;
        public VolumeProfile sharedProfile = null;
    }

    public abstract class VolumeParameter { }
    public class VolumeParameter<T> : VolumeParameter
    {
        public virtual T value { get; set; }
        public void Override(T x) { }
    }
    public class BoolParameter : VolumeParameter<bool> { public BoolParameter(bool value, bool overrideState = false) { } }
    public class FloatParameter : VolumeParameter<float> { public FloatParameter(float value, bool overrideState = false) { } }
    public class MinFloatParameter : FloatParameter { public MinFloatParameter(float value, float min, bool overrideState = false) : base(value, overrideState) { } }
    public class ClampedFloatParameter : FloatParameter { public ClampedFloatParameter(float value, float min, float max, bool overrideState = false) : base(value, overrideState) { } }
    public class IntParameter : VolumeParameter<int> { public IntParameter(int value, bool overrideState = false) { } }
    public class ClampedIntParameter : IntParameter { public ClampedIntParameter(int value, int min, int max, bool overrideState = false) : base(value, overrideState) { } }
    public class ColorParameter : VolumeParameter<Color> { public ColorParameter(Color value, bool hdr, bool showAlpha, bool showEyeDropper, bool overrideState = false) { } }
}

namespace UnityEngine.Rendering.Universal
{
    public enum TonemappingMode { None, Neutral, ACES }
    public sealed class TonemappingModeParameter : VolumeParameter<TonemappingMode> { public TonemappingModeParameter(TonemappingMode value, bool overrideState = false) { } }
    public sealed class Tonemapping : VolumeComponent { public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None); }
    public sealed class ColorAdjustments : VolumeComponent
    {
        public FloatParameter postExposure = new FloatParameter(0f);
        public ClampedFloatParameter contrast = new ClampedFloatParameter(0f, -100f, 100f);
        public ColorParameter colorFilter = new ColorParameter(Color.white, true, false, true);
        public ClampedFloatParameter saturation = new ClampedFloatParameter(0f, -100f, 100f);
    }
    public sealed class WhiteBalance : VolumeComponent
    {
        public ClampedFloatParameter temperature = new ClampedFloatParameter(0f, -100, 100f);
        public ClampedFloatParameter tint = new ClampedFloatParameter(0f, -100, 100f);
    }
    public sealed partial class Bloom : VolumeComponent
    {
        public MinFloatParameter threshold = new MinFloatParameter(0.9f, 0f);
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0f, 1f);
        public ColorParameter tint = new ColorParameter(Color.white, false, false, true);
        public BoolParameter highQualityFiltering = new BoolParameter(false);
    }
    public sealed class Vignette : VolumeComponent
    {
        public ColorParameter color = new ColorParameter(Color.black, false, false, true);
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter smoothness = new ClampedFloatParameter(0.2f, 0.01f, 1f);
    }
    // URP 17.3 Runtime/Overrides/SplitToning.cs l.107-119, DepthOfField.cs l.8, l.155-215.
    public sealed class SplitToning : VolumeComponent
    {
        public ColorParameter shadows = new ColorParameter(Color.grey, false, false, true);
        public ColorParameter highlights = new ColorParameter(Color.grey, false, false, true);
        public ClampedFloatParameter balance = new ClampedFloatParameter(0f, -100f, 100f);
    }
    public enum DepthOfFieldMode { Off, Gaussian, Bokeh }
    public sealed class DepthOfFieldModeParameter : VolumeParameter<DepthOfFieldMode> { public DepthOfFieldModeParameter(DepthOfFieldMode value, bool overrideState = false) { } }
    public sealed class DepthOfField : VolumeComponent
    {
        public DepthOfFieldModeParameter mode = new DepthOfFieldModeParameter(DepthOfFieldMode.Off);
        public MinFloatParameter focusDistance = new MinFloatParameter(10f, 0.1f);
        public ClampedFloatParameter aperture = new ClampedFloatParameter(5.6f, 1f, 32f);
        public ClampedFloatParameter focalLength = new ClampedFloatParameter(50f, 1f, 300f);
        public ClampedIntParameter bladeCount = new ClampedIntParameter(5, 3, 9);
    }
}
