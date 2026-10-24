using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CTLife.CityLife
{
    /// <summary>
    /// The daily chest in 3D for the gm streak sheet (2026-10-24): Poly Haven "Treasure Chest" (CC0, art/pipeline/ui_chest.py)
    /// at Resources/UI/Chest/ui_chest, rendered off-stage on layer 30 with its own key and rim light into a square
    /// RenderTexture the sheet shows. The lid swings on its hinge (<see cref="Lid"/>, 0 shut to 1 open); <see cref="Wobble"/>
    /// rocks the body for the anticipation shake. The prefab loads asynchronously and is rendered once, unseen, by
    /// <see cref="Prewarm"/> when the dashboard tile appears, so the open never pays for a load or a shader compile.
    /// </summary>
    public sealed class ChestStage : MonoBehaviour
    {
        public const int Layer = 30, Mask = 1 << Layer, Size = 1024;   // drawn at ~690 px: the 1.5x supersample stands in for MSAA
        private const string Path = "UI/Chest/ui_chest";
        private static ChestStage _instance;
        private static GameObject _prefab;
        private static ResourceRequest _load;

        private Camera _cam;
        private Light _key, _rim;
        private Transform _spin, _chest, _lid;
        private Quaternion _lidShut;
        private Light _previousSun; private bool _swapped;
        private int _renderOnce;
        private bool _loggedOpen;

        public RenderTexture Texture { get; private set; }
        /// <summary>0 shut, 1 open (about 105 degrees back).</summary>
        public float Lid;
        /// <summary>Body rock in degrees about the forward axis plus a hop in metres (anticipation shake).</summary>
        public float Wobble, Hop;
        /// <summary>Slow turn of the chest in degrees.</summary>
        public float Yaw = 162f;
        public float KeyBoost;
        public bool Live { get => _cam != null && _cam.enabled; set { if (_cam != null) _cam.enabled = value && _chest != null; } }
        public bool Ready => _chest != null;

        public static ChestStage Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("CTLife.ChestStage");
                if (Application.isPlaying) DontDestroyOnLoad(go);
                go.transform.position = new Vector3(1000f, -500f, 0f);
                _instance = go.AddComponent<ChestStage>();
                return _instance;
            }
        }

        /// <summary>Starts the async load of the chest prefab (idempotent).</summary>
        public static void BeginLoad()
        {
            if (_prefab != null || _load != null) return;
            _load = Resources.LoadAsync<GameObject>(Path);
            _load.completed += _ => { _prefab = _load.asset as GameObject; if (_prefab == null) Log.Warn("chest", "Missing " + Path); };
        }

        /// <summary>Loads the prefab without blocking and renders it once off-screen (texture upload, shader variant).</summary>
        public static async Task Prewarm()
        {
            BeginLoad();
            while (_prefab == null && _load != null && !_load.isDone) await Task.Yield();
            if (_prefab == null) return;
            var s = Instance;
            s.EnsureChest();
            s._renderOnce = 2;
            s._cam.enabled = true;
        }

        private void Awake()
        {
            _spin = new GameObject("spin").transform; _spin.SetParent(transform, false);
            _cam = new GameObject("chest_cam").AddComponent<Camera>();
            _cam.transform.SetParent(transform, false);
            _cam.cullingMask = Mask;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.allowMSAA = false; _cam.allowHDR = false;
            _cam.fieldOfView = 24f; _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 20f;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderShadows = false; data.renderPostProcessing = false;
            Texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "chest_live", antiAliasing = 1 };   // MSAA RTs lost the depth test on the Seeker (GLES3)
            Texture.Create();
            _cam.targetTexture = Texture;
            _cam.enabled = false;
            // camera: three-quarter, a little above, the chest's front facing us
            var rot = Quaternion.Euler(17f, 0f, 0f);
            var target = new Vector3(0f, 0.36f, 0f);
            _cam.transform.localRotation = rot;
            _cam.transform.localPosition = target - rot * Vector3.forward * 3.7f;
            _key = MakeLight("key", Quaternion.Euler(38f, -35f, 0f), 1.35f, new Color(1f, 0.95f, 0.86f));
            _rim = MakeLight("rim", Quaternion.Euler(18f, 160f, 0f), 0.75f, new Color(0.78f, 0.62f, 1f));
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }

        private Light MakeLight(string name, Quaternion rot, float intensity, Color color)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localRotation = rot;
            l.type = LightType.Directional; l.intensity = intensity; l.color = color; l.cullingMask = Mask; l.shadows = LightShadows.None;
            return l;
        }

        private void EnsureChest()
        {
            if (_chest != null || _prefab == null) return;
            var go = Instantiate(_prefab, _spin, false);
            go.name = "chest";
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            _chest = go.transform;
            _lid = go.transform.Find("model/lid") ?? FindDeep(go.transform, "lid");
            if (_lid != null) _lidShut = _lid.localRotation;
            Log.Info("chest", "chest ready: lid " + (_lid != null ? _lid.name + " under " + _lid.parent.name : "MISSING") + ", " +
                go.GetComponentsInChildren<Renderer>(true).Length + " renderers");
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }

        /// <summary>Shows the chest (shut) and starts rendering; false until the prefab has loaded.</summary>
        public bool Show()
        {
            BeginLoad();
            EnsureChest();
            if (_chest == null) return false;
            Lid = 0f; Wobble = 0f; Hop = 0f; Yaw = 162f; KeyBoost = 0f;
            Apply();
            _cam.enabled = true;
            return true;
        }

        public void Hide() { if (_cam != null) _cam.enabled = false; }

        private void Apply()
        {
            if (_chest == null) return;
            _spin.localRotation = Quaternion.Euler(0f, Yaw, 0f);
            _chest.localRotation = Quaternion.Euler(0f, 0f, Wobble);
            _chest.localPosition = new Vector3(0f, Hop, 0f);
            // the hinge runs along X at the back; negative X rotation swings the lid up and back
            if (_lid != null) _lid.localRotation = _lidShut * Quaternion.Euler(-105f * Mathf.Clamp01(Lid), 0f, 0f);
            _key.intensity = 1.35f + KeyBoost;
            if (Lid > 0.5f && !_loggedOpen) { _loggedOpen = true; Log.Info("chest", Describe()); }
            if (Lid <= 0f) _loggedOpen = false;
        }

        /// <summary>Diagnostics: where the lid, the chest and the camera are (orientation checks on device).</summary>
        public string Describe() => _lid == null ? "no lid" :
            $"lid euler {_lid.localEulerAngles:F0} lidLocalPos {_lid.localPosition:F2} lidWorld-chest {(_lid.position - _chest.position):F2} " +
            $"spinYaw {_spin.localEulerAngles.y:F0} Yaw {Yaw:F0} chestFwd {_chest.forward:F2} camPos-stage {(_cam.transform.position - transform.position):F2} camFwd {_cam.transform.forward:F2} stageRot {transform.rotation.eulerAngles:F0}";

        private void LateUpdate()
        {
            if (_cam == null || !_cam.enabled) return;
            Apply();
            if (_renderOnce > 0 && --_renderOnce == 0) _cam.enabled = false;
        }

        private void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam) return;
            _previousSun = RenderSettings.sun;
            RenderSettings.sun = _key;
            _swapped = true;
        }

        private void End(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_swapped || cam != _cam) return;
            RenderSettings.sun = _previousSun;
            _swapped = false;
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            if (Texture != null) Texture.Release();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
    }
}
