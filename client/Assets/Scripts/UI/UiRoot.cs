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

        private UIDocument _doc;
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
            go.SetActive(true);
            return root;
        }

        private static PanelSettings LoadPanelSettings()
        {
            var ps = Resources.Load<PanelSettings>("UI/PanelSettings");
            if (ps != null) return ps;
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

        private void OnEnable()
        {
            Root = _doc.rootVisualElement;
            Root.Clear();
            Root.AddToClassList("ct-root");
            var sheet = Resources.Load<StyleSheet>("UI/ctlife");
            if (sheet != null) Root.styleSheets.Add(sheet);
            else Log.Warn("ui", "Missing Resources/UI/ctlife.uss");

            WorldLayer = Layer("layer-world", PickingMode.Position);
            LabelLayer = Layer("layer-labels", PickingMode.Ignore);
            HudLayer = Layer("layer-hud", PickingMode.Ignore);
            ScreenLayer = Layer("layer-screens", PickingMode.Ignore);
            PhoneLayer = Layer("layer-phone", PickingMode.Ignore);
            ModalLayer = Layer("layer-modal", PickingMode.Ignore);
            ToastLayer = Layer("layer-toast", PickingMode.Ignore);
            WorldInputLayer.Attach(WorldLayer);
            ApplySafeArea();
        }

        private VisualElement Layer(string cls, PickingMode picking)
        {
            var e = new VisualElement { name = cls, pickingMode = picking };
            e.AddToClassList("ct-layer");
            e.AddToClassList(cls);
            Root.Add(e);
            return e;
        }

        /// <summary>Pads the HUD/screen/phone layers by the device safe area (Seeker has a punch-hole camera).</summary>
        private void ApplySafeArea()
        {
            var sa = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            Root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float sx = Root.layout.width / Screen.width, sy = Root.layout.height / Screen.height;
                foreach (var l in new[] { HudLayer, ScreenLayer, PhoneLayer, ModalLayer, ToastLayer })
                {
                    l.style.paddingTop = (Screen.height - sa.yMax) * sy;
                    l.style.paddingBottom = sa.yMin * sy;
                    l.style.paddingLeft = sa.xMin * sx;
                    l.style.paddingRight = (Screen.width - sa.xMax) * sx;
                }
            });
        }

        /// <summary>Per-frame callbacks for UI that tracks the world (labels, bubbles).</summary>
        public void AddUpdater(Action a) => _updaters.Add(a);
        public void RemoveUpdater(Action a) => _updaters.Remove(a);

        private void LateUpdate()
        {
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
