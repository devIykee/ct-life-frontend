using System;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Terse builders for UI Toolkit elements with the ct-* USS classes.</summary>
    public static class El
    {
        public static VisualElement Div(string cls = null, params VisualElement[] children)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            foreach (var ch in children) if (ch != null) e.Add(ch);
            return e;
        }

        /// <summary>
        /// Rich text is off: labels show player names, chat and server strings, and a tag such as
        /// &lt;size=999&gt; or &lt;color&gt; in one would let a player restyle or spoof UI (toasts, sheets). No CT Life
        /// copy uses markup; opt back in per label only for fully trusted text.
        /// </summary>
        public static Label Text(string text, string cls = null)
        {
            var l = new Label(text) { enableRichText = false };
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            return l;
        }

        /// <summary>Constrain wrapping to the visible width, not ScrollView's unbounded content size.</summary>
        public static ScrollView VerticalScroll(string cls = null)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            if (!string.IsNullOrEmpty(cls)) scroll.With(cls);
            scroll.contentContainer.style.minWidth = 0;
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.width > 0) scroll.contentContainer.style.width = e.newRect.width;
            });
            return scroll;
        }

        public static Button Btn(string text, Action onClick, string cls = "ct-btn", string sfx = "sfx_ui_tap")
        {
            var b = new Button(() =>
            {
                if (!string.IsNullOrEmpty(sfx)) Bus.Publish(new SfxSignal(sfx));
                onClick?.Invoke();
            }) { text = text, enableRichText = false };
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) b.AddToClassList(c);
            // Decorative layers never participate in layout or pointer handling. Keeping the
            // real Button preserves keyboard, disabled, click and value-binding behavior.
            b.AddToClassList("ct-pressable");
            b.Add(PassThrough(Div("ct-button-depth")));
            b.Add(PassThrough(Div("ct-button-glint")));
            KeepTextWidth(b);
            return b;
        }

        /// <summary>
        /// UI Toolkit drops a text element's own text measurement as soon as it gets a child
        /// (Unity 6000.3 VisualElementHierarchy.Insert → RemoveMeasureFunction), and every Btn has the two decorative
        /// layers above. A button that no parent sizes (a centred column, an un-grown row item) therefore collapses to
        /// its padding and wraps its label one letter per line (connection-retry "Try again"). Only such a collapsed
        /// button (empty content box) gets its single-line text width back as min-width, capped to its parent;
        /// buttons a parent already sizes are left exactly as they are.
        /// </summary>
        private static void KeepTextWidth(Button b)
        {
            bool fitted = false;
            float applied = -1f;
            void Fit()
            {
                if (b.panel == null || string.IsNullOrEmpty(b.text)) return;
                // Hidden (display: none, or inside a hidden screen) lays out at zero size: not collapsed, skip.
                if (float.IsNaN(b.layout.width) || b.layout.width < 1f) return;
                if (!fitted && b.contentRect.width >= 1f) return;
                var s = b.resolvedStyle;
                float text = b.MeasureTextSize(b.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                if (float.IsNaN(text) || text <= 0f) return;
                float w = Mathf.Ceil(text) + s.paddingLeft + s.paddingRight + s.borderLeftWidth + s.borderRightWidth;
                var parent = b.hierarchy.parent;
                if (parent != null && parent.contentRect.width > 1f) w = Mathf.Min(w, parent.contentRect.width - s.marginLeft - s.marginRight);
                if (float.IsNaN(w) || Mathf.Abs(w - applied) < 0.5f) return;
                if (!fitted && Debug.isDebugBuild) Log.Info("ui", $"button \"{b.text}\" [{string.Join(" ", b.GetClasses())}] had no width from its parent; min-width {w:0} from its text");
                fitted = true;
                applied = w;
                b.style.minWidth = w;
            }
            b.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            b.RegisterCallback<ChangeEvent<string>>(e => { if (e.target == b) Fit(); });
        }

        public static T With<T>(this T e, string cls) where T : VisualElement
        {
            foreach (var c in cls.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
            return e;
        }

        public static void Show(this VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        public static Button IconButton(string icon, string label, Action onClick, string cls = "ct-btn ct-btn-icon", string sfx = "sfx_ui_tap")
        {
            var button = Btn("", onClick, cls, sfx);
            button.tooltip = label;
            button.Add(new UiIcon(icon));
            return button;
        }

        public static Button DockButton(string icon, string label, Action onClick, string cls = "ct-dock-action", string sfx = "sfx_ui_tap")
        {
            var button = IconButton(icon, label, onClick, cls, sfx);
            button.Add(Text(label, "ct-dock-label"));
            return button;
        }

        public static VisualElement Section(string number, string title, string detail = null)
        {
            var copy = Div("ct-grow", Text(title, "ct-section-title"));
            if (!string.IsNullOrEmpty(detail)) copy.Add(Text(detail, "ct-small ct-muted"));
            return Div("ct-section-heading", Text(number, "ct-section-number"), copy);
        }

        /// <summary>The official CT mark (art/branding/logo) beside "CT LIFE" in the app's current wordmark style.</summary>
        public static VisualElement Brand(bool compact = false) => Div(compact ? "ct-brand ct-brand-compact" : "ct-brand",
            LogoMark("ct-brand-logo"), Text(Strings.Brand, "ct-brand-wordmark"));

        private static Texture2D _mark;

        /// <summary>The CT mark as an image element (Resources/UI/Brand/ct-mark, generated by BrandAssets).</summary>
        public static VisualElement LogoMark(string classes)
        {
            if (_mark == null) _mark = Resources.Load<Texture2D>("UI/Brand/ct-mark");
            var e = Div(classes);
            if (_mark != null) e.style.backgroundImage = new StyleBackground(_mark);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>Read-only HUD decoration must not swallow taps intended for the city behind it.</summary>
        public static VisualElement PassThrough(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (var child in element.Children()) PassThrough(child);
            return element;
        }
    }
}
