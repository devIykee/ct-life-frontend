using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CTLife.CityLife
{
    /// <summary>
    /// Studio renders of the Duo Shop's furniture (2026-10-23): a live turntable for the product page (drag to turn) and
    /// cached 256 px thumbnails for the catalogue rows and the place-mode tray, one per frame from a queue, each model's prefab
    /// loaded asynchronously before its frame. Off-stage on
    /// layer 30 with its own key light (the city sun is swapped out for the preview cameras, as AvatarPreview does).
    /// </summary>
    public sealed class FurniturePreview : MonoBehaviour
    {
        public const int Layer = 30, Mask = 1 << Layer;
        private static FurniturePreview _instance;
        public static FurniturePreview Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("CTLife.FurniturePreview");
                DontDestroyOnLoad(go);
                go.transform.position = new Vector3(-1000f, -500f, 0f);
                _instance = go.AddComponent<FurniturePreview>();
                return _instance;
            }
        }

        private Camera _thumbCam, _liveCam;
        private Light _key, _fill;
        private Transform _thumbStage, _liveStage, _liveSpin;
        private GameObject _liveModel; private string _liveKey;
        private readonly Dictionary<string, RenderTexture> _thumbs = new Dictionary<string, RenderTexture>();
        private readonly Queue<string> _queue = new Queue<string>();
        private string _rendering; private GameObject _thumbModel;
        private Light _previousSun; private bool _swapped;
        private float _yaw = -35f, _yawVel;
        public RenderTexture Live { get; private set; }
        public bool Dragging;

        private void Awake()
        {
            _thumbStage = Stage("thumb_stage", Vector3.zero);
            _liveStage = Stage("live_stage", new Vector3(0f, 0f, 60f));
            _liveSpin = new GameObject("spin").transform; _liveSpin.SetParent(_liveStage, false);
            _thumbCam = Cam("thumb_cam", _thumbStage);
            _liveCam = Cam("live_cam", _liveStage);
            Live = new RenderTexture(1024, 768, 16, RenderTextureFormat.ARGB32) { name = "furniture_live", antiAliasing = 1 };
            Live.Create();
            _liveCam.targetTexture = Live;
            _liveCam.enabled = false;
            _thumbCam.enabled = false;
            var k = new GameObject("key").AddComponent<Light>();
            k.transform.SetParent(transform, false);
            k.transform.rotation = Quaternion.Euler(42f, -30f, 0f);
            k.type = LightType.Directional; k.intensity = 1.25f; k.color = new Color(1f, 0.96f, 0.9f); k.cullingMask = Mask; k.shadows = LightShadows.None;
            _key = k;
            var f = new GameObject("fill").AddComponent<Light>();
            f.transform.SetParent(transform, false);
            f.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            f.type = LightType.Directional; f.intensity = 0.45f; f.color = new Color(0.85f, 0.9f, 1f); f.cullingMask = Mask; f.shadows = LightShadows.None;
            _fill = f;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            foreach (var rt in _thumbs.Values) if (rt != null) rt.Release();
            if (Live != null) Live.Release();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private Transform Stage(string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            t.localPosition = at;
            return t;
        }

        private static Camera Cam(string name, Transform stage)
        {
            var c = new GameObject(name).AddComponent<Camera>();
            c.transform.SetParent(stage, false);
            c.cullingMask = Mask;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0f, 0f, 0f, 0f);
            c.allowMSAA = false; c.allowHDR = false;
            c.fieldOfView = 26f; c.nearClipPlane = 0.05f; c.farClipPlane = 25f;
            var data = c.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            return c;
        }

        private void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _thumbCam && cam != _liveCam) return;
            _previousSun = RenderSettings.sun;
            RenderSettings.sun = _key;
            _swapped = true;
        }

        private void End(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_swapped || (cam != _thumbCam && cam != _liveCam)) return;
            RenderSettings.sun = _previousSun;
            _swapped = false;
        }

        private static GameObject Place(string modelKey, Transform stage, out Bounds bounds)
        {
            bounds = new Bounds();
            var go = ShopProps.SpawnFurniture(modelKey, stage);
            if (go == null) return null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            // stand it on the stage, centred
            var c = bounds.center; c.y = bounds.min.y;
            go.transform.position += stage.position - c;
            bounds.center += stage.position - c;
            return go;
        }

        /// <summary>Frames the bounds from a three-quarter view (yaw about the stage, pitch down).</summary>
        private static void Frame(Camera cam, Transform stage, Bounds b, float pitch, float yaw, float fill)
        {
            float radius = Mathf.Max(0.12f, b.extents.magnitude);
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * fill;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var target = new Vector3(stage.position.x, stage.position.y + b.size.y * 0.45f, stage.position.z);
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
        }

        // ------------------------------------------------------------------ thumbnails

        /// <summary>A 256 px studio thumbnail of the model (rendered on a later frame; the texture fills in when done).</summary>
        public RenderTexture Thumbnail(string modelKey)
        {
            if (string.IsNullOrEmpty(modelKey)) return null;
            if (_thumbs.TryGetValue(modelKey, out var rt)) return rt;
            rt = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32) { name = "thumb_" + modelKey, antiAliasing = 1 };
            rt.Create();
            var prev = RenderTexture.active; RenderTexture.active = rt; GL.Clear(true, true, new Color(0, 0, 0, 0)); RenderTexture.active = prev;
            _thumbs[modelKey] = rt;
            _queue.Enqueue(modelKey);
            return rt;
        }

        private void LateUpdate()
        {
            // one thumbnail per frame: the camera rendered last frame's model into its texture; set up the next
            if (_rendering != null)
            {
                if (_thumbModel != null) Destroy(_thumbModel);
                _thumbModel = null; _rendering = null;
                _thumbCam.enabled = false;
            }
            // the next model's prefab loads asynchronously first (ShopProps.FurnitureReady): no frame pays for its upload
            if (_queue.Count > 0 && ShopProps.FurnitureReady(_queue.Peek()))
            {
                var key = _queue.Dequeue();
                _thumbModel = Place(key, _thumbStage, out var b);
                if (_thumbModel != null)
                {
                    Frame(_thumbCam, _thumbStage, b, 22f, -38f, 1.02f);
                    _thumbCam.targetTexture = _thumbs[key];
                    _thumbCam.enabled = true;
                    _rendering = key;
                }
            }
            // the live turntable: a slow spin, or the drag's yaw with a little inertia
            if (_liveModel != null && _liveCam.enabled)
            {
                if (!Dragging) { _yawVel = Mathf.Lerp(_yawVel, 10f, Time.unscaledDeltaTime * 1.5f); _yaw += _yawVel * Time.unscaledDeltaTime; }
                _liveSpin.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            }
        }

        // ------------------------------------------------------------------ the live turntable

        public RenderTexture ShowLive(string modelKey)
        {
            if (_liveKey != modelKey)
            {
                if (_liveModel != null) Destroy(_liveModel);
                _liveSpin.localRotation = Quaternion.identity;
                _liveModel = Place(modelKey, _liveSpin, out var b);
                _liveKey = modelKey;
                _yaw = -35f; _yawVel = 10f;
                if (_liveModel != null) Frame(_liveCam, _liveStage, b, 16f, 0f, 1.08f);
            }
            _liveCam.enabled = _liveModel != null;
            return Live;
        }

        public void Turn(float degrees) { _yaw += degrees; _yawVel = degrees / Mathf.Max(1e-3f, Time.unscaledDeltaTime) * 0.25f; _yawVel = Mathf.Clamp(_yawVel, -240f, 240f); }

        public void HideLive()
        {
            _liveCam.enabled = false;
            if (_liveModel != null) Destroy(_liveModel);
            _liveModel = null; _liveKey = null;
        }
    }
}
