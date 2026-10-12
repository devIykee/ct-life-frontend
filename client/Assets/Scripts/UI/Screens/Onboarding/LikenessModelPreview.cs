using System;
using CTLife.World.Likeness;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.UI
{
    /// <summary>Actual downloaded mesh preview; no stock-avatar substitute. Its loader remains owned by the panel.</summary>
    public sealed class LikenessModelPreview : IDisposable
    {
        private const int Layer = 30;
        private static int _slot;
        private readonly GameObject _root;
        private readonly Camera _camera;
        private readonly Light _key;
        private Light _priorSun;
        private LikenessLoadedAvatar _asset;
        private bool _lightOverride;
        private float _yaw;
        private int _framesToSettle;
        public RenderTexture Texture { get; }
        public Transform Parent => _root.transform;
        public LikenessModelPreview()
        {
            _root = new GameObject("Likeness review camera");
            _root.transform.position = new Vector3(8000 + _slot++ * 30, -500, 0);
            Object.DontDestroyOnLoad(_root);
            Texture = new RenderTexture(768, 768, 16, RenderTextureFormat.ARGB32) { antiAliasing = 1, name = "Likeness review" };
            Texture.Create();
            var cam = new GameObject("camera"); cam.transform.SetParent(_root.transform, false);
            _camera = cam.AddComponent<Camera>(); _camera.targetTexture = Texture; _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(0.95f, 0.95f, 0.94f, 1);
            _camera.allowMSAA = false; _camera.allowHDR = false; _camera.nearClipPlane = 0.01f; _camera.farClipPlane = 15;
            _camera.orthographic = true;
            _camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var light = new GameObject("key"); light.transform.SetParent(_root.transform, false);
            light.transform.localRotation = Quaternion.Euler(25, 135, 0);
            _key = light.AddComponent<Light>(); _key.type = LightType.Directional; _key.intensity = 1.1f; _key.cullingMask = 1 << Layer; _key.shadows = LightShadows.None;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }
        public void Present(LikenessLoadedAvatar asset)
        {
            _asset = asset;
            asset.Instance.transform.SetParent(_root.transform, false);
            asset.Instance.transform.localRotation = Quaternion.identity;
            foreach (var t in asset.Instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            _yaw = 0;
            if (asset.CanEquip && asset.Animator.runtimeAnimatorController != null)
            {
                // Humanoid retargeting changes the actual skinned pose from the imported rest pose.
                // Measure only after evaluating it, including the retargeted hips/root translation.
                asset.Animator.applyRootMotion = false;
                asset.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                asset.Animator.Play("loco_idle", 0, 0);
                asset.Animator.Update(0f);
            }
            FrameActualPose();
            // Android may finish its first animator/skinning update later in the frame. Recheck at
            // render time for the first few frames, then keep the framing steady during idle breathing.
            _framesToSettle = 3;
        }
        public void Spin(float delta)
        {
            if (_asset?.Instance == null) return;
            _yaw -= delta * 0.3f;
            _asset.Instance.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
            _framesToSettle = 1;
        }
        private void FrameActualPose()
        {
            if (_asset?.Instance == null) return;
            var b = LikenessAvatarLoader.Measure(_asset.Instance);
            // Measure returns world-space baked geometry, including this isolated preview's offset.
            // Fit both axes of the render target rather than assuming the unposed loader height.
            float aspect = (float)Texture.width / Texture.height;
            _camera.transform.position = new Vector3(b.center.x, b.center.y, b.center.z + b.extents.z + 4f);
            _camera.transform.rotation = Quaternion.Euler(0, 180, 0);
            _camera.orthographicSize = Mathf.Max(b.extents.y, b.extents.x / aspect) * 1.24f;
        }
        private void Begin(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera) return;
            if (_framesToSettle > 0) { FrameActualPose(); _framesToSettle--; }
            _priorSun = RenderSettings.sun; RenderSettings.sun = _key; _lightOverride = true;
        }
        private void End(ScriptableRenderContext context, Camera camera)
        { if (camera != _camera || !_lightOverride) return; RenderSettings.sun = _priorSun; _lightOverride = false; }
        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End;
            if (_lightOverride) RenderSettings.sun = _priorSun;
            _camera.enabled = false; _camera.targetTexture = null;
            Texture.Release(); Object.Destroy(Texture); Object.Destroy(_root);
        }
    }
}
