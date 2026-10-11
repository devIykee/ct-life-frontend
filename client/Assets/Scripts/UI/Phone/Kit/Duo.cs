using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The Solana Duo kit (spec docs/design/duo-ios-redesign-20261016.md): iOS-style building blocks shared by every
    /// app. Content sits on the canvas with inset hairlines; glass is for navigation and controls only. Styles live in
    /// Resources/UI/duo-kit.uss. Text is always literal (El.Text, rich text off).
    /// </summary>
    public static class Duo
    {
        public enum PillKind { Plain, Primary, Locked, Destructive }
        public enum Tone { Neutral, Info, Success, Warning, Danger }

        private static readonly string[] AvatarTints = { "red", "blue", "purple", "green", "orange", "pink", "teal", "indigo" };

        // ------------------------------------------------------------------ surfaces

        /// <summary>Soft shadow planes (UI Toolkit has no box-shadow): add first, before the surface.</summary>
        public static void AddShadow(VisualElement owner, string variant = null)
        {
            foreach (var plane in new[] { "far", "mid", "near" })
            {
                var e = El.Div("duo-shadow duo-shadow-" + plane + (variant != null ? " duo-shadow-" + variant : ""));
                e.pickingMode = PickingMode.Ignore;
                owner.Add(e);
            }
        }

        /// <summary>Round glass button (72 units) for navigation: back, more, call, plus.</summary>
        public static Button Circle(DuoGlyph.Kind glyph, string tooltip, Action onClick, string name = null) =>
            Circle(new DuoGlyph(glyph), tooltip, onClick, name);

        public static Button Circle(VisualElement icon, string tooltip, Action onClick, string name = null)
        {
            var b = new Button(() => { Bus.Publish(new SfxSignal("sfx_ui_tap")); onClick?.Invoke(); }) { tooltip = tooltip, enableRichText = false };
            if (name != null) b.name = name;
            b.AddToClassList("duo-circle");
            AddShadow(b, "circle");
            var face = El.Div("duo-circle-face", icon);
            face.pickingMode = PickingMode.Ignore;
            b.Add(face);
            return b;
        }

        public static Label LargeTitle(string text) => El.Text(text, "duo-large-title");

        /// <summary>Section header: title3 with an optional blue action ("See all").</summary>
        public static VisualElement SectionHeader(string title, string actionText = null, Action action = null)
        {
            var row = El.Div("duo-section-header", El.Text(title, "duo-section-title"));
            if (actionText != null && action != null)
            {
                var a = El.Btn(actionText, action, "duo-link");
                row.Add(a);
            }
            return row;
        }

        /// <summary>iOS inset grouped list (Settings only). Marks the last row so its separator is hidden.</summary>
        public static VisualElement Group(params VisualElement[] rows)
        {
            var g = El.Div("duo-group");
            foreach (var r in rows) if (r != null) g.Add(r);
            MarkLast(g);
            return g;
        }

        /// <summary>Hides the separator on the final row of a list (USS has no :last-child).</summary>
        public static void MarkLast(VisualElement list)
        {
            DuoRow last = null;
            foreach (var child in list.Children())
                if (child is DuoRow row) { row.RemoveFromClassList("duo-row-last"); last = row; }
            last?.AddToClassList("duo-row-last");
        }

        public static Label GroupHeader(string text) => El.Text(text.ToUpperInvariant(), "duo-group-header");
        public static Label GroupFooter(string text) => El.Text(text, "duo-group-footer");

        /// <summary>App-Store "GET" capsule.</summary>
        public static Button Pill(string text, Action onClick, PillKind kind = PillKind.Plain)
        {
            var b = El.Btn(text, onClick, "duo-pill duo-pill-" + kind.ToString().ToLowerInvariant());
            return b;
        }

        /// <summary>A big number with its label and an optional real delta line.</summary>
        public static VisualElement Hero(string label, string value, string delta = null, bool? positive = null, string deltaCaption = null)
        {
            var hero = El.Div("duo-hero", El.Text(label, "duo-hero-label"), El.Text(value, "duo-hero-value"));
            if (!string.IsNullOrEmpty(delta))
            {
                var line = El.Div("duo-hero-delta-row", El.Text(delta, "duo-hero-delta"));
                if (positive.HasValue) line.AddToClassList(positive.Value ? "duo-positive" : "duo-negative");
                if (deltaCaption != null) line.Add(El.Text(deltaCaption, "duo-hero-delta-caption"));
                hero.Add(line);
            }
            return hero;
        }

        /// <summary>Floating glass capsule holding one or two large buttons.</summary>
        public static VisualElement BottomBar(params Button[] buttons)
        {
            var bar = El.Div("duo-bottom-bar");
            AddShadow(bar, "bar");
            var face = El.Div("duo-bottom-bar-face");
            foreach (var b in buttons) { b.AddToClassList("duo-bottom-bar-button"); face.Add(b); }
            bar.Add(face);
            return bar;
        }

        /// <summary>Circular initial avatar, colour stable per seed (user id).</summary>
        public static VisualElement Avatar(string name, string seed, string sizeClass = "duo-avatar-m")
        {
            var initial = string.IsNullOrWhiteSpace(name) ? "?" : System.Globalization.StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
            uint h = 2166136261;
            foreach (char ch in seed ?? name ?? "") h = (h ^ ch) * 16777619;
            var sheen = El.Div("duo-avatar-sheen");
            sheen.style.backgroundImage = Background.FromTexture2D(Sheen);
            var a = El.Div("duo-avatar " + sizeClass + " duo-tint-" + AvatarTints[h % (uint)AvatarTints.Length],
                sheen, El.Text(initial, "duo-avatar-letter"));
            return El.PassThrough(a);
        }

        private static Texture2D _sheen;

        /// <summary>
        /// A soft top-to-bottom white fade (USS has no gradients): laid over a solid tint it gives the board's
        /// gradient avatars. Built once in code, 1×64 px, never a file.
        /// </summary>
        public static Texture2D Sheen
        {
            get
            {
                if (_sheen != null) return _sheen;
                _sheen = new Texture2D(1, 64, TextureFormat.RGBA32, false) { name = "Duo sheen", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < 64; y++)
                {
                    float t = y / 63f; // texture y=0 is the bottom row
                    _sheen.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, .3f, t) * t));
                }
                _sheen.Apply(false, true);
                return _sheen;
            }
        }

        /// <summary>Coloured rounded square with a white glyph (Settings rows).</summary>
        public static VisualElement IconSquare(string uiIcon, string tint)
        {
            var sq = El.Div("duo-icon-square duo-tint-" + tint, new UiIcon(uiIcon));
            return El.PassThrough(sq);
        }

        /// <summary>Red count badge; hidden at zero.</summary>
        public static Label Badge(int count, string extraClass = null)
        {
            var l = El.Text(DuoNotificationQueue.BadgeText(count), "duo-badge" + (extraClass != null ? " " + extraClass : ""));
            l.pickingMode = PickingMode.Ignore;
            l.Show(count > 0);
            return l;
        }

        public static void SetBadge(Label badge, int count)
        {
            if (badge == null) return;
            badge.text = DuoNotificationQueue.BadgeText(count);
            badge.Show(count > 0);
        }

        /// <summary>Inline status note (not a card): a tinted line used for real warnings and errors.</summary>
        public static VisualElement Notice(string text, Tone tone = Tone.Info)
        {
            var n = El.Div("duo-notice duo-notice-" + tone.ToString().ToLowerInvariant(), El.Text(text, "duo-notice-text"));
            return n;
        }

        /// <summary>Empty state: a friendly line plus at most one action, centred, no box.</summary>
        public static VisualElement Empty(string title, string detail = null, string actionText = null, Action action = null)
        {
            var e = El.Div("duo-empty", El.Text(title, "duo-empty-title"));
            if (!string.IsNullOrEmpty(detail)) e.Add(El.Text(detail, "duo-empty-detail"));
            if (actionText != null && action != null) e.Add(Pill(actionText, action, PillKind.Primary));
            return e;
        }

        /// <summary>Plain centred footnote ("Start of the conversation", version).</summary>
        public static Label Footnote(string text, string extra = null) => El.Text(text, "duo-footnote" + (extra != null ? " " + extra : ""));

        /// <summary>The panel element a sheet covers: the Duo glass (or the root when outside a phone).</summary>
        internal static VisualElement SheetHost(VisualElement from)
        {
            for (var e = from; e != null; e = e.parent)
                if (e.ClassListContains("ct-phone-glass")) return e;
            return from?.panel?.visualTree;
        }
    }

    /// <summary>
    /// One list row: leading (icon tile / avatar), title + subtitle, trailing (value, chevron, pill, toggle).
    /// The separator belongs to the text column, so it is inset like iOS. Tappable rows press to system grey.
    /// </summary>
    public sealed class DuoRow : Button
    {
        private readonly VisualElement _leading, _text, _trailing;
        public Label TitleLabel { get; private set; }
        public Label SubtitleLabel { get; private set; }
        public VisualElement Body { get; }
        public VisualElement TextColumn => _text;

        public DuoRow(Action onTap = null) : base(onTap == null ? (Action)null : () => { Bus.Publish(new SfxSignal("sfx_ui_tap")); onTap(); })
        {
            enableRichText = false;
            AddToClassList("duo-row");
            if (onTap == null) { AddToClassList("duo-row-static"); focusable = false; }
            _leading = El.Div("duo-row-leading");
            _leading.pickingMode = PickingMode.Ignore;
            _text = El.Div("duo-row-text");
            _text.pickingMode = PickingMode.Ignore;
            _trailing = El.Div("duo-row-trailing");
            Body = El.Div("duo-row-body", _text, _trailing);
            Body.pickingMode = PickingMode.Ignore;
            _leading.Show(false);
            Add(_leading);
            Add(Body);
        }

        public DuoRow Leading(VisualElement e)
        {
            if (e == null) return this;
            _leading.Clear();
            _leading.Add(e);
            _leading.Show(true);
            return this;
        }

        public DuoRow Title(string text, string extra = null)
        {
            TitleLabel = El.Text(text ?? "", "duo-row-title" + (extra != null ? " " + extra : ""));
            TitleLabel.pickingMode = PickingMode.Ignore;
            _text.Add(TitleLabel);
            return this;
        }

        /// <summary>Secondary line. Two lines clip at the second line (USS has no line-clamp).</summary>
        public DuoRow Subtitle(string text, bool twoLines = false)
        {
            if (string.IsNullOrEmpty(text)) return this;
            SubtitleLabel = El.Text(text, "duo-row-subtitle");
            SubtitleLabel.pickingMode = PickingMode.Ignore;
            if (twoLines)
            {
                var clip = El.Div("duo-row-subtitle-clip", SubtitleLabel);
                clip.pickingMode = PickingMode.Ignore;
                _text.Add(clip);
            }
            else
            {
                SubtitleLabel.AddToClassList("duo-one-line");
                _text.Add(SubtitleLabel);
            }
            return this;
        }

        public DuoRow Trailing(VisualElement e)
        {
            if (e != null) _trailing.Add(e);
            return this;
        }

        public DuoRow Value(string text, string extra = null)
        {
            var l = El.Text(text ?? "", "duo-row-value" + (extra != null ? " " + extra : ""));
            l.pickingMode = PickingMode.Ignore;
            return Trailing(l);
        }

        public DuoRow Chevron() => Trailing(new DuoGlyph(DuoGlyph.Kind.Chevron).With("duo-row-chevron"));
    }

    /// <summary>
    /// The page scaffold: a scroll view with an optional large title and a nav bar of glass circle buttons on top.
    /// When the large title scrolls under the bar, the bar shows the title inline (iOS collapse).
    /// </summary>
    public sealed class DuoPage : VisualElement
    {
        public ScrollView Scroll { get; }
        public DuoNav Nav { get; }
        /// <summary>Content below the large title.</summary>
        public VisualElement Body { get; }
        public Label Title { get; }

        public DuoPage(string title, bool largeTitle = true, bool grouped = false)
        {
            AddToClassList("duo-page");
            if (grouped) AddToClassList("duo-page-grouped");
            Scroll = El.VerticalScroll("duo-scroll");
            Scroll.mode = ScrollViewMode.Vertical;
            Nav = new DuoNav();
            Nav.SetTitle(title);
            Body = El.Div("duo-page-body");
            if (largeTitle && !string.IsNullOrEmpty(title))
            {
                Title = Duo.LargeTitle(title);
                Scroll.Add(Title);
            }
            else Nav.SetCollapsed(true, instant: true);
            Scroll.Add(Body);
            Add(Scroll);
            Add(Nav);
            if (Title != null)
                Scroll.verticalScroller.valueChanged += v =>
                {
                    float threshold = Title.layout.yMax - Nav.layout.height * .35f;
                    Nav.SetCollapsed(!float.IsNaN(threshold) && v > Mathf.Max(8f, threshold));
                };
        }
    }

    /// <summary>Top navigation: circle buttons left/right, inline title centred (shown when collapsed).</summary>
    public sealed class DuoNav : VisualElement
    {
        public VisualElement Left { get; }
        public VisualElement Right { get; }
        public VisualElement Center { get; }
        public Label TitleLabel { get; }
        public bool Collapsed { get; private set; }

        public DuoNav()
        {
            AddToClassList("duo-nav");
            pickingMode = PickingMode.Ignore;
            var backdrop = El.Div("duo-nav-backdrop");
            backdrop.pickingMode = PickingMode.Ignore;
            Left = El.Div("duo-nav-side duo-nav-left");
            Right = El.Div("duo-nav-side duo-nav-right");
            Left.pickingMode = Right.pickingMode = PickingMode.Ignore;
            TitleLabel = El.Text("", "duo-nav-title");
            Center = El.Div("duo-nav-center", TitleLabel);
            Center.pickingMode = PickingMode.Ignore;
            TitleLabel.pickingMode = PickingMode.Ignore;
            Add(backdrop);
            Add(Left);
            Add(Center);
            Add(Right);
        }

        public void SetTitle(string text) => TitleLabel.text = text ?? "";

        public void SetCollapsed(bool collapsed, bool instant = false)
        {
            if (collapsed == Collapsed && !instant) return;
            Collapsed = collapsed;
            EnableInClassList("duo-nav-collapsed", collapsed);
        }

        public Button AddLeft(DuoGlyph.Kind glyph, string tooltip, Action action, string name = null)
        {
            var b = Duo.Circle(glyph, tooltip, action, name);
            Left.Add(b);
            return b;
        }

        public Button AddRight(DuoGlyph.Kind glyph, string tooltip, Action action, string name = null)
        {
            var b = Duo.Circle(glyph, tooltip, action, name);
            Right.Add(b);
            return b;
        }
    }

    /// <summary>iOS switch. A Button, so taps, focus and keyboard work like every other control.</summary>
    public sealed class DuoToggle : Button
    {
        private bool _value;
        public event Action<bool> Changed;

        public DuoToggle(bool value, Action<bool> changed = null)
        {
            enableRichText = false;
            AddToClassList("duo-toggle");
            // The Button is the (larger) touch target; the track is the visible switch.
            var knob = El.Div("duo-toggle-knob");
            var track = El.Div("duo-toggle-track", knob);
            El.PassThrough(track);
            Add(track);
            SetValueWithoutNotify(value);
            if (changed != null) Changed += changed;
            clicked += () =>
            {
                Bus.Publish(new SfxSignal("sfx_ui_tap"));
                SetValueWithoutNotify(!_value);
                Changed?.Invoke(_value);
            };
        }

        public bool Value => _value;

        public void SetValueWithoutNotify(bool value)
        {
            _value = value;
            EnableInClassList("duo-toggle-on", value);
        }
    }

    /// <summary>
    /// Discrete slider (Display &amp; Text Size). Tap a point or drag sideways; vertical swipes stay with the scroll view
    /// (device QA: continuous sliders in scrolling pages took swipes as drags). Small/large "A" step down/up.
    /// </summary>
    public sealed class DuoSlider : VisualElement
    {
        private readonly int _steps;
        private readonly VisualElement _track, _fill, _thumb;
        private readonly List<VisualElement> _ticks = new List<VisualElement>();
        private int _value;
        private int _pointer = -1;
        private Vector2 _start;
        private bool _dragging;
        public event Action<int> Changed;

        public DuoSlider(int steps, int value, string smallLabel, string largeLabel, string decreaseTip, string increaseTip)
        {
            _steps = Math.Max(2, steps);
            AddToClassList("duo-slider");
            var minus = El.Btn(smallLabel, () => Set(_value - 1, true), "duo-slider-end duo-slider-small");
            minus.tooltip = decreaseTip;
            var plus = El.Btn(largeLabel, () => Set(_value + 1, true), "duo-slider-end duo-slider-large");
            plus.tooltip = increaseTip;
            _fill = El.Div("duo-slider-fill");
            _thumb = El.Div("duo-slider-thumb");
            Duo.AddShadow(_thumb, "thumb");
            _thumb.Add(El.Div("duo-slider-thumb-face"));
            var rail = El.Div("duo-slider-rail", _fill);
            for (int i = 0; i < _steps; i++)
            {
                var tick = El.Div("duo-slider-tick");
                tick.style.left = Length.Percent(i * 100f / (_steps - 1));
                _ticks.Add(tick);
                rail.Add(tick);
            }
            _track = El.Div("duo-slider-track", rail, _thumb);
            El.PassThrough(rail);
            _thumb.pickingMode = PickingMode.Ignore;
            foreach (var c in _thumb.Children()) c.pickingMode = PickingMode.Ignore;
            Add(minus); Add(_track); Add(plus);
            _track.RegisterCallback<PointerDownEvent>(OnDown);
            _track.RegisterCallback<PointerMoveEvent>(OnMove);
            _track.RegisterCallback<PointerUpEvent>(OnUp);
            _track.RegisterCallback<PointerCancelEvent>(_ => End());
            _track.RegisterCallback<PointerCaptureOutEvent>(_ => End());
            Set(value, false);
        }

        public int Value => _value;

        public void Set(int value, bool notify)
        {
            value = Mathf.Clamp(value, 0, _steps - 1);
            bool changed = value != _value;
            _value = value;
            float pct = value * 100f / (_steps - 1);
            _fill.style.width = Length.Percent(pct);
            _thumb.style.left = Length.Percent(pct);
            for (int i = 0; i < _ticks.Count; i++) _ticks[i].EnableInClassList("duo-slider-tick-on", i <= value);
            if (notify && changed)
            {
                Bus.Publish(new SfxSignal("sfx_ui_tap"));
                Changed?.Invoke(value);
            }
        }

        private int StepAt(Vector2 world)
        {
            var local = _track.WorldToLocal(world);
            float w = _track.contentRect.width;
            if (w <= 1f) return _value;
            return Mathf.RoundToInt(Mathf.Clamp01(local.x / w) * (_steps - 1));
        }

        private void OnDown(PointerDownEvent e)
        {
            if (_pointer >= 0 || e.button != 0) return;
            _pointer = e.pointerId;
            _start = e.position;
            _dragging = false;
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId != _pointer) return;
            Vector2 d = (Vector2)e.position - _start;
            if (!_dragging)
            {
                if (d.magnitude < 14f) return;
                if (Mathf.Abs(d.x) <= Mathf.Abs(d.y)) { _pointer = -1; return; } // a scroll, not a drag
                _dragging = true;
                _track.CapturePointer(e.pointerId);
            }
            Set(StepAt(e.position), true);
            e.StopPropagation();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != _pointer) return;
            Set(StepAt(e.position), true);
            End();
            e.StopPropagation();
        }

        private void End()
        {
            if (_pointer >= 0 && _track.HasPointerCapture(_pointer)) _track.ReleasePointer(_pointer);
            _pointer = -1;
            _dragging = false;
        }
    }

    /// <summary>Capsule track with a white thumb that slides to the selected option.</summary>
    public sealed class DuoSegmented : VisualElement
    {
        private readonly List<Button> _options = new List<Button>();
        private readonly VisualElement _thumb;
        public int Selected { get; private set; } = -1;
        public event Action<int> Changed;

        public DuoSegmented(IReadOnlyList<string> labels, int selected, Action<int> changed = null)
        {
            AddToClassList("duo-segmented");
            _thumb = El.Div("duo-segmented-thumb");
            _thumb.pickingMode = PickingMode.Ignore;
            Add(_thumb);
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                var b = El.Btn(labels[i], () => Select(index, true), "duo-segment");
                _options.Add(b);
                Add(b);
            }
            _thumb.style.width = Length.Percent(100f / Math.Max(1, labels.Count));
            if (changed != null) Changed += changed;
            Select(selected, false);
        }

        public IReadOnlyList<Button> Options => _options;

        public void Select(int index, bool notify)
        {
            index = Mathf.Clamp(index, 0, _options.Count - 1);
            bool changed = index != Selected;
            Selected = index;
            _thumb.style.left = Length.Percent(index * 100f / Math.Max(1, _options.Count));
            for (int i = 0; i < _options.Count; i++) _options[i].EnableInClassList("duo-segment-on", i == index);
            if (notify && changed) Changed?.Invoke(index);
        }
    }

    /// <summary>Rounded grey search field with a magnifier; reports every edit.</summary>
    public sealed class DuoSearchField : VisualElement
    {
        public TextField Field { get; }

        public DuoSearchField(string placeholder, Action<string> changed, int maxLength = 60)
        {
            AddToClassList("duo-search");
            Add(new DuoGlyph(DuoGlyph.Kind.Search).With("duo-search-icon"));
            Field = new TextField { maxLength = maxLength, isDelayed = false };
            Field.AddToClassList("duo-search-field");
            Field.textEdition.placeholder = placeholder;
            Field.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue ?? ""));
            Add(Field);
        }
    }

    /// <summary>
    /// Bottom sheet over the Duo screen: grabber, title, optional message and content, one primary and a quiet secondary.
    /// Tap the dimmed screen or drag the grabber down to close.
    /// </summary>
    public sealed class DuoSheet : VisualElement
    {
        private readonly VisualElement _panel, _content, _actions;
        private Tween _motion;
        private bool _closing;
        private int _pointer = -1;
        private float _startY, _drag;
        public event Action Closed;

        public DuoSheet(string title, string message = null)
        {
            AddToClassList("duo-sheet-layer");
            var scrim = new Button(Close) { tooltip = Strings.T("common.cancel") };
            scrim.AddToClassList("duo-sheet-scrim");
            _panel = El.Div("duo-sheet");
            var grabArea = El.Div("duo-sheet-grab-area", El.Div("duo-sheet-grabber"));
            _panel.Add(grabArea);
            if (!string.IsNullOrEmpty(title)) _panel.Add(El.Text(title, "duo-sheet-title"));
            if (!string.IsNullOrEmpty(message)) _panel.Add(El.Text(message, "duo-sheet-message"));
            _content = El.Div("duo-sheet-content");
            _actions = El.Div("duo-sheet-actions");
            _panel.Add(_content);
            _panel.Add(_actions);
            Add(scrim);
            Add(_panel);
            grabArea.RegisterCallback<PointerDownEvent>(e => { _pointer = e.pointerId; _startY = e.position.y; _drag = 0; grabArea.CapturePointer(e.pointerId); });
            grabArea.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != _pointer) return;
                _drag = Mathf.Max(0, e.position.y - _startY);
                _panel.style.translate = new Translate(0, _drag);
            });
            grabArea.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != _pointer) return;
                _pointer = -1;
                if (grabArea.HasPointerCapture(e.pointerId)) grabArea.ReleasePointer(e.pointerId);
                if (_drag > 110f) Close(); else _panel.style.translate = new Translate(0, 0);
            });
            RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == this) _motion?.Cancel(); });
        }

        public VisualElement Content => _content;

        public DuoSheet AddContent(VisualElement e)
        {
            if (e != null) _content.Add(e);
            return this;
        }

        /// <summary>A full-width row action inside the sheet (report reasons, conversation actions).</summary>
        public Button AddOption(string text, Action action, bool destructive = false, string name = null)
        {
            var b = El.Btn(text, () => { Close(); action?.Invoke(); }, "duo-sheet-option" + (destructive ? " duo-destructive" : ""));
            if (name != null) b.name = name;
            _content.Add(b);
            return b;
        }

        public Button AddPrimary(string text, Action action, bool destructive = false, string name = null)
        {
            var b = El.Btn(text, () => { Close(); action?.Invoke(); }, "duo-sheet-primary" + (destructive ? " duo-sheet-primary-destructive" : ""));
            if (name != null) b.name = name;
            _actions.Add(b);
            return b;
        }

        public Button AddSecondary(string text, Action action = null, string name = null)
        {
            var b = El.Btn(text, () => { Close(); action?.Invoke(); }, "duo-sheet-secondary");
            if (name != null) b.name = name;
            _actions.Add(b);
            return b;
        }

        /// <summary>Shows the sheet over the Duo screen that contains <paramref name="from"/>.</summary>
        public DuoSheet Show(VisualElement from)
        {
            var host = Duo.SheetHost(from);
            if (host == null) return this;
            host.Q<DuoSheet>()?.Close();
            host.Add(this);
            _motion?.Cancel();
            _motion = Tween.Run(this, .24f, t =>
            {
                style.opacity = Mathf.Clamp01(t * 1.6f);
                _panel.style.translate = new Translate(0, (1f - t) * 420f);
            }, Ease.OutCubic);
            return this;
        }

        public void Close()
        {
            if (_closing) return;
            _closing = true;
            _motion?.Cancel();
            if (panel == null) { RemoveFromHierarchy(); Closed?.Invoke(); return; }
            _motion = Tween.Run(this, .18f, t =>
            {
                style.opacity = 1f - t;
                _panel.style.translate = new Translate(0, _drag + t * 420f);
                if (t >= 1f) { RemoveFromHierarchy(); Closed?.Invoke(); }
            }, Ease.OutCubic, skipWhenReduced: false);
        }
    }
}
