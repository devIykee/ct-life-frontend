using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The single UI Toolkit document. Layers, back to front: world input (taps/drag/pinch to the 3D world),
    /// world labels (name tags, speech bubbles), HUD, screens (sign-in, creator, lottery, map), phone, modals, toasts.
    /// PanelSettings comes from Resources/UI/PanelSettings (created by CT Life → Project Setup) or is made at runtime
    /// with the theme at Resources/UI/CTLifeTheme.tss.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        public static UiRoot Instance { get; private set; }

        public VisualElement Root { get; private set; }
        public VisualElement WorldLayer { get; private set; }
        public VisualElement LabelLayer { get; private set; }
        public VisualElement HudLayer { get; private set; }
        public VisualElement ScreenLayer { get; private set; }
        public VisualElement PhoneLayer { get; private set; }
        public VisualElement ModalLayer { get; private set; }
        public VisualElement ToastLayer { get; private set; }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private Dev.QaUiSnapshotExporter _qaUi;
        private void OnDisable() => _qaUi?.Stop();
#endif
        private UIDocument _doc;
        private PanelSettings _ownedPanelSettings;
        private VisualElement _screenBackdrop;
        private Rect _lastSafeArea;
        private Vector2 _lastPanelSize;
        private readonly List<Action> _updaters = new List<Action>();

        public static UiRoot Create()
        {
            if (Instance != null) return Instance;
            // Configure while inactive so UIDocument.OnEnable already has its PanelSettings, then UiRoot.OnEnable
            // (added second, so enabled second) finds a live rootVisualElement.
            var go = new GameObject("CTLife.UI");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = LoadPanelSettings();
            var root = go.AddComponent<UiRoot>();
            root._ownedPanelSettings = doc.panelSettings;
            go.SetActive(true);
            return root;
        }

        private static PanelSettings LoadPanelSettings()
        {
            var ps = Resources.Load<PanelSettings>("UI/PanelSettings");
            // Runtime layout must not mutate the imported settings asset in Editor review.
            if (ps != null) return Instantiate(ps);
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "RuntimePanelSettings";
            ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/CTLifeTheme");
            if (ps.themeStyleSheet == null) Log.Warn("ui", "No ThemeStyleSheet at Resources/UI/CTLifeTheme; run CT Life → Project Setup");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1080, 2400);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            return ps;
        }

        private void Awake()
        {
            Instance = this;
            _doc = GetComponent<UIDocument>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _updaters.Clear();
            if (_ownedPanelSettings == null) return;
            if (_doc != null && _doc.panelSettings == _ownedPanelSettings)
                _doc.panelSettings = null;
            Destroy(_ownedPanelSettings);
            _ownedPanelSettings = null;
        }

        private void OnEnable()
        {
            Root = _doc.rootVisualElement;
            Root.Clear();
            Root.AddToClassList("ct-root");
            var sheet = Resources.Load<StyleSheet>("UI/ctlife");
            if (sheet != null) Root.styleSheets.Add(sheet);
            else Log.Warn("ui", "Missing Resources/UI/ctlife.uss");
            var worldSheet = Resources.Load<StyleSheet>("UI/city-hud");
            if (worldSheet != null) Root.styleSheets.Add(worldSheet);
            var phoneSheet = Resources.Load<StyleSheet>("UI/phone-ui");
            if (phoneSheet != null) Root.styleSheets.Add(phoneSheet);
            var landscapeSheet = Resources.Load<StyleSheet>("UI/landscape");
            if (landscapeSheet != null) Root.styleSheets.Add(landscapeSheet);
            foreach (var name in new[] { "phone-social", "phone-account", "toast" })
            {
                var detailSheet = Resources.Load<StyleSheet>("UI/" + name);
                if (detailSheet != null) Root.styleSheets.Add(detailSheet);
            }

            WorldLayer = Layer("layer-world", PickingMode.Position);
            LabelLayer = Layer("layer-labels", PickingMode.Ignore);
            HudLayer = Layer("layer-hud", PickingMode.Ignore);
            _screenBackdrop = Layer("layer-screen-backdrop", PickingMode.Position);
            _screenBackdrop.Show(false);
            ScreenLayer = Layer("layer-screens", PickingMode.Ignore);
            PhoneLayer = Layer("layer-phone", PickingMode.Ignore);
            ModalLayer = Layer("layer-modal", PickingMode.Ignore);
            ToastLayer = Layer("layer-toast", PickingMode.Ignore);
            WorldInputLayer.Attach(WorldLayer);
            Root.RegisterCallback<GeometryChangedEvent>(_ => ApplySafeArea(true));
            ApplySafeArea(true);
        }

        private VisualElement Layer(string cls, PickingMode picking)
        {
            var e = new VisualElement { name = cls, pickingMode = picking };
            e.AddToClassList("ct-layer");
            e.AddToClassList(cls);
            Root.Add(e);
            return e;
        }

        // A full-bleed backing covers the unsafe strips while controls remain inside safe insets.
        internal void RefreshScreenBackdrop()
        {
            if (_screenBackdrop == null) return;
            VisualElement topScreen = null;
            if (ScreenLayer != null)
                foreach (var screen in ScreenLayer.Children())
                    if (screen.resolvedStyle.display != DisplayStyle.None) topScreen = screen;
            _screenBackdrop.EnableInClassList("ct-backdrop-lobby", topScreen != null && topScreen.ClassListContains("ct-lobby"));
            _screenBackdrop.EnableInClassList("ct-backdrop-brand", topScreen != null && topScreen.ClassListContains("ct-brand-screen"));
            _screenBackdrop.Show(topScreen != null);
        }

        /// <summary>Inset layer bounds: absolute-positioned children do not respect their parent's padding.</summary>
        private void ApplySafeArea(bool force = false)
        {
            bool landscape = Screen.width > Screen.height;
            var reference = landscape ? new Vector2Int(2400, 1080) : new Vector2Int(1080, 2400);
            if (_doc.panelSettings.referenceResolution != reference)
                _doc.panelSettings.referenceResolution = reference;
            Root.EnableInClassList("ct-landscape", landscape);
            var sa = Screen.safeArea;
            var size = Root.layout.size;
            if (Screen.width <= 0 || Screen.height <= 0 || size.x <= 0f || size.y <= 0f || float.IsNaN(size.x)) return;
            if (!force && sa == _lastSafeArea && size == _lastPanelSize) return;
            _lastSafeArea = sa;
            _lastPanelSize = size;
            float sx = size.x / Screen.width, sy = size.y / Screen.height;
            foreach (var layer in new[] { HudLayer, ScreenLayer, PhoneLayer, ModalLayer, ToastLayer })
            {
                layer.style.top = Mathf.Max(0f, Screen.height - sa.yMax) * sy;
                layer.style.bottom = Mathf.Max(0f, sa.yMin) * sy;
                layer.style.left = Mathf.Max(0f, sa.xMin) * sx;
                layer.style.right = Mathf.Max(0f, Screen.width - sa.xMax) * sx;
            }
        }

        /// <summary>Per-frame callbacks for UI that tracks the world (labels, bubbles).</summary>
        public void AddUpdater(Action a) => _updaters.Add(a);
        public void RemoveUpdater(Action a) => _updaters.Remove(a);

        private void LateUpdate()
        {
            ApplySafeArea();
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (_qaUi == null && LaunchArgs.Flag("qaUi")) _qaUi = new Dev.QaUiSnapshotExporter();
            _qaUi?.Tick(Root);
#endif
            for (int i = 0; i < _updaters.Count; i++)
            {
                try { _updaters[i](); } catch (Exception e) { Log.Exception("ui", e); }
            }
        }

        /// <summary>World position → panel coordinates (top-left origin). Returns false if behind the camera.</summary>
        public bool WorldToPanel(Camera cam, Vector3 world, out Vector2 panelPos)
        {
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f || Root.layout.width <= 0f)
            {
                panelPos = default;
                return false;
            }
            panelPos = new Vector2(sp.x / Screen.width * Root.layout.width, (1f - sp.y / Screen.height) * Root.layout.height);
            return true;
        }
    }
}
