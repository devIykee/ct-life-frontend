#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// Dev builds only (World lane, W4): frame-rate bisection on the device. Re-applied every second by FlowController:
    /// `islandRenderScale=0.7` pins the URP render scale (the cool-device budget PerfManager uses at thermal 0-1),
    /// `islandSkip=shadows` turns the island sun's shadows off, `islandSkip=post` zeroes the look volumes.
    /// </summary>
    public static class IslandPerfHarness
    {
        public static void Apply(string renderScale)
        {
            if (!string.IsNullOrEmpty(renderScale) && float.TryParse(renderScale, NumberStyles.Float, CultureInfo.InvariantCulture, out var rs)
                && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = rs;
            if (IslandInstanceRenderer.SkipKinds.Contains("shadows"))
                foreach (var atm in Object.FindObjectsByType<IslandAtmosphere>(FindObjectsSortMode.None))
                    if (atm.Sun != null) atm.Sun.shadows = LightShadows.None;
            if (IslandInstanceRenderer.SkipKinds.Contains("post"))
                foreach (var vol in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) vol.weight = 0f;
        }
    }
}
#endif
