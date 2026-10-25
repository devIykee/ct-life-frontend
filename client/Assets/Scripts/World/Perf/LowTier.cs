using CTLife.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// Applies <see cref="QualityTier"/>'s global settings (lane B, 2026-10-25): on Low, no MSAA, no HDR target, no post on
    /// the main camera, a lower LOD bias and a 30 Hz physics step with at most three steps per frame (at 7 fps the 50 Hz
    /// step ran seven physics sub-steps a frame). High restores what the game set. Island-specific parts (sun shadows,
    /// fog, far plane, kit distances, terrain shader, traffic, pedestrians) read the tier where they live.
    /// The URP asset is shared with every lane: it is only changed at runtime here, never saved (the editor restores it).
    /// </summary>
    public sealed class LowTier : MonoBehaviour
    {
        private int _msaa = -1;
        private bool _hdr;
        private float _lodBias = -1f, _fixed = -1f, _maxDelta = -1f;
        private bool _applied;
        private Camera _postCam;
        private bool _postWas;

        private void OnEnable()
        {
            QualityTier.Init();
            QualityTier.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            QualityTier.Changed -= Apply;
            if (_applied) Restore();
        }

        private void Apply()
        {
            if (QualityTier.IsLow)
            {
                if (!_applied)
                {
                    var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    if (urp != null) { _msaa = urp.msaaSampleCount; _hdr = urp.supportsHDR; urp.msaaSampleCount = 1; urp.supportsHDR = false; }
                    _lodBias = QualitySettings.lodBias; _fixed = Time.fixedDeltaTime; _maxDelta = Time.maximumDeltaTime;
                    _applied = true;
                }
                QualitySettings.lodBias = QualityTier.LowLodBias;
                Time.fixedDeltaTime = QualityTier.LowFixedDelta;
                Time.maximumDeltaTime = Mathf.Min(_maxDelta, QualityTier.LowFixedDelta * 3f);
                Log.Info("quality", FormattableString($"low tier applied: msaa 1, hdr off, post off, lodBias {QualitySettings.lodBias:0.00}, fixed {Time.fixedDeltaTime:0.000}s max {Time.maximumDeltaTime:0.000}s"));
            }
            else if (_applied) Restore();
        }

        private static string FormattableString(System.FormattableString s) => System.FormattableString.Invariant(s);

        private void Restore()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null && _msaa > 0) { urp.msaaSampleCount = _msaa; urp.supportsHDR = _hdr; }
            if (_lodBias > 0f) QualitySettings.lodBias = _lodBias;
            if (_fixed > 0f) Time.fixedDeltaTime = _fixed;
            if (_maxDelta > 0f) Time.maximumDeltaTime = _maxDelta;
            if (_postCam != null && _postCam.TryGetComponent<UniversalAdditionalCameraData>(out var d)) d.renderPostProcessing = _postWas;
            _postCam = null;
            _applied = false;
            Log.Info("quality", "high tier restored");
        }

        private void LateUpdate()
        {
            if (!_applied) return;
            // venues and the island switch post per camera: on Low the main camera never runs it
            var cam = Camera.main;
            if (cam == null || !cam.TryGetComponent<UniversalAdditionalCameraData>(out var data)) return;
            if (cam != _postCam) { _postCam = cam; _postWas = data.renderPostProcessing; }
            if (data.renderPostProcessing) data.renderPostProcessing = false;
        }
    }
}
