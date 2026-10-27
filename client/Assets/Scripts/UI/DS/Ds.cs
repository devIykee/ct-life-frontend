using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.DS
{
    public enum Tone { Neutral, Accent, Warn, Danger, Ink, Glass }
    public enum BtnKind { Primary, Secondary, Danger, Text }
    public enum DialogWidth { Confirm, Standard, Wide }
    public enum DismissMode { Back, None, TapScrim }

    /// <summary>A button's content and behaviour (label verb-first, ≤ 3 words; Sub is the price, "· $5.00").</summary>
    public sealed class DsAction
    {
        public string Label, Sub, Icon, Name;
        public BtnKind Kind = BtnKind.Primary;
        public Action OnClick;
        public bool Disabled, Medium;
    }

    /// <summary>The approved dialog skeleton (SYSTEM.md 3.1): hero → title + one line → tiles → one progress row → ONE primary
    /// (+ one text button) → the back button bottom-left (outside the card).</summary>
    public sealed class DialogSpec
    {
        public VisualElement Hero;
        /// <summary>Optional art band above the title (a 3D venue render or brand art; <see cref="Ds.Art(string, float)"/>).
        /// The hero icon, when both are set, sits on the band's lower edge.</summary>
        public VisualElement Art;
        public string Title, Subtitle, Name;
        public List<VisualElement> Tiles = new List<VisualElement>();
        public VisualElement Progress;
        /// <summary>A small caps divider above the progress row ("WORK 5 SHIFTS TO GET PROMOTED"), inside the well.</summary>
        public string ProgressLabel;
        /// <summary>A brand id (BrandTable): the hero mark, title colour, primary button and progress fill take it.</summary>
        public string Brand;
        public VisualElement Body;          // extra content between tiles/progress and the button (chips, a section)
        public DsAction Primary, Secondary;
        public DismissMode Dismiss = DismissMode.Back;
        public DialogWidth Width = DialogWidth.Standard;
        public Action OnDismiss;
    }

    /// <summary>
    /// The CT Life design system's builders (approved 2026-10-26; ct-life-evidence/design-system/SYSTEM.md). The only code
    /// that knows the ds-* class names; features compose these and never set colours or fonts. Every text element is
    /// rich-text off (player names and server strings render literally).
    /// </summary>
    public static class Ds
    {
        private static StyleSheet _tokens, _components;
        private static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();

        /// <summary>Adds the token and component sheets to a root once (UiRoot calls it; panels made elsewhere may too).</summary>
        public static void Ensure(VisualElement root)
        {
            if (root == null) return;
            _tokens ??= Resources.Load<StyleSheet>("UI/DS/ds-tokens");
            _components ??= Resources.Load<StyleSheet>("UI/DS/ds-components");
            if (_tokens != null && !root.styleSheets.Contains(_tokens)) root.styleSheets.Add(_tokens);
            if (_components != null && !root.styleSheets.Contains(_components)) root.styleSheets.Add(_components);
        }

        public static bool Loaded => (_tokens ??= Resources.Load<StyleSheet>("UI/DS/ds-tokens")) != null && (_components ??= Resources.Load<StyleSheet>("UI/DS/ds-components")) != null;

        // ------------------------------------------------------------------ primitives

        public static Label Text(string text, string cls)
        {
            var l = new Label(text ?? "") { enableRichText = false, pickingMode = PickingMode.Ignore };
            foreach (var c in (cls ?? "ds-t").Split(' ')) if (c.Length > 0) l.AddToClassList(c);
            GuardDisplay(l);
            return l;
        }

        /// <summary>Sets a label's text and re-checks the display font's glyph coverage (player names, server strings).</summary>
        public static void SetText(Label l, string text)
        {
            if (l == null) return;
            l.text = text ?? "";
            GuardDisplay(l);
        }

        /// <summary>Lilita One covers Latin-1 plus € £ × · ’ … Ł only (v2/DELTA.md glyph check: no Cyrillic, no arrows, no
        /// ₦). Any other character: the label falls back to Inter Bold (class ds-font-body), never tofu.</summary>
        public static bool LilitaCovers(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            foreach (var c in text)
            {
                if (c >= 0x20 && c <= 0x7E) continue;
                if (c >= 0xA0 && c <= 0xFF) continue;
                if (c == '\n' || c == '€' || c == '£' || c == '×' || c == '·' || c == '\u2019' || c == '…' || c == 'Ł') continue;
                return false;
            }
            return true;
        }

        private static readonly string[] DisplayClasses = { "ds-t-title", "ds-t-display", "ds-t-stat", "ds-t-hud", "ds-btn-label", "ds-toast-text", "ds-progress-title", "ds-progress-of" };

        private static void GuardDisplay(Label l)
        {
            bool display = false;
            foreach (var c in DisplayClasses) if (l.ClassListContains(c)) { display = true; break; }
            l.EnableInClassList("ds-font-body", display && !LilitaCovers(l.text));
        }

        public static Label Title(string t) => Text(t, "ds-t-title");
        public static Label LabelCapsText(string t) => LabelCaps(t);
        public static Label Body(string t) => Text(t, "ds-t-body");
        public static Label LabelCaps(string t) => Text((t ?? "").ToUpperInvariant(), "ds-t-label");

        public static Texture2D IconTexture(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return null;
            if (Icons.TryGetValue(nameOrPath, out var t) && t != null) return t;
            t = nameOrPath.Contains("/") ? Resources.Load<Texture2D>(nameOrPath) : Resources.Load<Texture2D>("UI/Icons/" + nameOrPath);
            Icons[nameOrPath] = t;
            return t;
        }

        /// <summary>A Phosphor icon (name in Resources/UI/Icons) or real art (a Resources path with '/'), tinted by tone.</summary>
        public static VisualElement Icon(string name, int size = 40, string tint = null)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("ds-ic");
            if (size != 40) e.AddToClassList("ds-ic--" + size);
            bool art = name != null && name.Contains("/");
            e.AddToClassList(art ? "ds-ic--art" : tint != null ? "ds-ic--" + tint : "ds-ic");
            var t = IconTexture(name);
            if (t != null) e.style.backgroundImage = Background.FromTexture2D(t);
            return e;
        }

        /// <summary>The currency coin art (real art, untinted).</summary>
        public const string Coin = "UI/Casino/coin_heads";

        private static string ToneTint(Tone t) => t switch { Tone.Accent => "accent", Tone.Danger => "danger", Tone.Warn => "warn", Tone.Glass => "white", Tone.Ink => "white", _ => "ink2" };

        // ------------------------------------------------------------------ buttons

        public static DsAction Action(string label, Action onClick, BtnKind kind = BtnKind.Primary, string sub = null, string icon = null) =>
            new DsAction { Label = label, OnClick = onClick, Kind = kind, Sub = sub, Icon = icon };

        public static DsAction TextAction(string label, Action onClick) => new DsAction { Label = label, OnClick = onClick, Kind = BtnKind.Text };

        public static Button Btn(DsAction a)
        {
            var b = new Button(() => { if (!a.Disabled) a.OnClick?.Invoke(); }) { text = "" };
            b.AddToClassList("ds-btn");
            b.AddToClassList(a.Kind switch { BtnKind.Secondary => "ds-btn--secondary", BtnKind.Danger => "ds-btn--danger", BtnKind.Text => "ds-btn--text", _ => "ds-btn--primary" });
            if (a.Medium) b.AddToClassList("ds-btn--md");
            if (!string.IsNullOrEmpty(a.Name)) b.name = a.Name;
            if (a.Icon != null) b.Add(Icon(a.Icon, a.Kind == BtnKind.Text ? 40 : 60, a.Kind == BtnKind.Primary ? "on-accent" : a.Kind == BtnKind.Danger ? "white" : "ink2"));
            var l = Text(a.Label, a.Kind == BtnKind.Text ? null : "ds-btn-label");
            if (a.Kind == BtnKind.Text) l.AddToClassList("ds-btn-label--text");
            b.Add(l);
            if (!string.IsNullOrEmpty(a.Sub)) b.Add(Text(a.Sub, "ds-btn-sub"));
            b.SetEnabled(!a.Disabled);
            return b;
        }

        /// <summary>Busy state: the label gives way to a turning spinner, the width stays.</summary>
        public static void SetBusy(Button b, bool busy)
        {
            if (b == null) return;
            var spin = b.Q(className: "ds-btn-spinner");
            foreach (var c in b.Children()) if (c != spin) c.style.visibility = busy ? Visibility.Hidden : Visibility.Visible;
            if (busy && spin == null)
            {
                spin = new VisualElement { pickingMode = PickingMode.Ignore };
                spin.AddToClassList("ds-btn-spinner");
                spin.style.position = Position.Absolute;
                b.Add(spin);
                spin.schedule.Execute(() => spin.style.rotate = new Rotate(new Angle(Time.unscaledTime * 360f % 360f))).Every(16);
            }
            else if (!busy) spin?.RemoveFromHierarchy();
            b.SetEnabled(!busy);
        }

        /// <summary>The back button (the only dismiss control in the game), bottom-left.</summary>
        public static Button Back(Action onClick)
        {
            var b = new Button(onClick) { name = "ds-back", text = "", tooltip = "Back" };
            b.AddToClassList("ds-back");
            b.Add(Icon("caret-left", 56, "ink"));
            return b;
        }

        // ------------------------------------------------------------------ content blocks

        /// <summary>The hero (v2): a 184 rounded-square tile in the brand's hero colour (white ring, a base) holding a system
        /// icon in white. Danger / warn tones colour the tile; accent and neutral take the brand (default green).</summary>
        public static VisualElement Hero(string icon, Tone tone)
        {
            var h = HeroShell(tone);
            h[0].Add(Icon(icon, 112, "white"));
            return h;
        }

        /// <summary>The hero carrying a brand's mark (BrandTable): the dialog must also carry <see cref="DialogSpec.Brand"/>.</summary>
        public static VisualElement HeroBrand(string brandId)
        {
            var mark = BrandTable.Mark(brandId);
            if (mark == null) return Hero("star-four", Tone.Accent);
            var h = HeroShell(Tone.Accent);
            h[0].Add(MarkElement(mark, false));
            return h;
        }

        /// <summary>A brand mark element: real art untinted (scale to fit), or a Phosphor icon in white.</summary>
        public static VisualElement MarkElement(VisualElementMark mark, bool small)
        {
            if (mark.Art != null)
            {
                var m = new VisualElement { pickingMode = PickingMode.Ignore };
                m.AddToClassList("ds-hero-mark");
                var t = mark.Texture;
                if (t != null) m.style.backgroundImage = Background.FromTexture2D(t);
                return m;
            }
            return Icon(mark.Icon, small ? 36 : 112, "white");
        }

        private static VisualElement HeroShell(Tone tone)
        {
            var h = new VisualElement { pickingMode = PickingMode.Ignore, name = "ds-hero" };
            h.AddToClassList("ds-hero");
            if (tone == Tone.Danger) h.AddToClassList("ds-hero--danger");
            if (tone == Tone.Warn) h.AddToClassList("ds-hero--warn");
            var tile = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.AddToClassList("ds-hero-tile");
            h.Add(tile);
            return h;
        }

        /// <summary>A small caps label between two rules (inside the well).</summary>
        public static VisualElement Divider(string label)
        {
            var d = new VisualElement { pickingMode = PickingMode.Ignore };
            d.AddToClassList("ds-divider");
            var l1 = new VisualElement { pickingMode = PickingMode.Ignore }; l1.AddToClassList("ds-divider-line");
            var l2 = new VisualElement { pickingMode = PickingMode.Ignore }; l2.AddToClassList("ds-divider-line");
            d.Add(l1); d.Add(LabelCaps(label)); d.Add(l2);
            return d;
        }

        /// <summary>The art band at the top of a dialog card (edge to edge, top corners rounded, cropped to fill): real art from
        /// Resources (a venue render, brand art). Height in panel units; 0 keeps the default (SYSTEM v2 sizes it).</summary>
        public static VisualElement Art(string resourcesPath, float height = 0f)
        {
            var tex = string.IsNullOrEmpty(resourcesPath) ? null : Resources.Load<Texture2D>(resourcesPath);
            return Art(tex, height);
        }

        /// <summary>The art band from a texture: a Texture2D, or a RenderTexture for a live 3D venue render.</summary>
        public static VisualElement Art(Texture texture, float height = 0f)
        {
            var a = new VisualElement { name = "ds-dialog-art", pickingMode = PickingMode.Ignore };
            a.AddToClassList("ds-dialog-art");
            if (texture is Texture2D t2) a.style.backgroundImage = Background.FromTexture2D(t2);
            else if (texture is RenderTexture rt) a.style.backgroundImage = Background.FromRenderTexture(rt);
            if (height > 0f) a.style.height = height;
            return a;
        }

        /// <summary>A stat tile: LABEL on top, icon/coin + value under it. Tone colours the value only (never the tile).</summary>
        public static VisualElement Stat(string label, string value, string icon = null, Tone tone = Tone.Neutral, string name = null)
        {
            var t = new VisualElement { name = name };
            t.AddToClassList("ds-tile");
            t.Add(LabelCaps(label));
            var row = new VisualElement();
            row.AddToClassList("ds-tile-value");
            if (icon != null && icon.Contains("/")) row.Add(Icon(icon, 60));
            else if (icon != null)
            {
                // a system icon sits in a brand-coloured badge (the clock on Time worked)
                var badge = new VisualElement { pickingMode = PickingMode.Ignore };
                badge.AddToClassList("ds-badge-ic");
                badge.Add(Icon(icon, 36, "white"));
                row.Add(badge);
            }
            var v = Text(value, "ds-t-stat");
            v.name = "ds-tile-value";
            if (tone == Tone.Accent) v.AddToClassList("ds-ink-accent");
            else if (tone == Tone.Danger) v.AddToClassList("ds-ink-danger");
            else if (tone == Tone.Warn) v.AddToClassList("ds-ink-warn");
            row.Add(v);
            t.Add(row);
            return t;
        }

        public static VisualElement Tiles(params VisualElement[] tiles)
        {
            var r = new VisualElement();
            r.AddToClassList("ds-tiles");
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                if (r.childCount == 0) tiles[i].AddToClassList("ds-first");
                r.Add(tiles[i]);
            }
            return r;
        }

        public static VisualElement Section(params VisualElement[] children)
        {
            var s = new VisualElement();
            s.AddToClassList("ds-section");
            foreach (var c in children) if (c != null) s.Add(c);
            return s;
        }

        public static VisualElement Chip(string text, Tone tone = Tone.Neutral, string icon = null)
        {
            var c = new VisualElement { pickingMode = PickingMode.Ignore };
            c.AddToClassList("ds-chip");
            if (tone != Tone.Neutral) c.AddToClassList("ds-chip--" + tone.ToString().ToLowerInvariant());
            if (icon != null) c.Add(Icon(icon, 28));
            c.Add(Text(text, "ds-chip-text"));
            return c;
        }

        public static VisualElement Chips(params VisualElement[] chips)
        {
            var r = new VisualElement();
            r.AddToClassList("ds-chips");
            foreach (var c in chips) if (c != null) r.Add(c);
            return r;
        }

        public static VisualElement Row(string leadIcon, string title, string meta, VisualElement trailing = null, Action onClick = null)
        {
            VisualElement r = onClick != null ? new Button(onClick) { text = "" } : new VisualElement();
            r.AddToClassList("ds-row");
            if (leadIcon != null)
            {
                var lead = new VisualElement { pickingMode = PickingMode.Ignore };
                lead.AddToClassList("ds-row-lead");
                lead.Add(Icon(leadIcon, 40, leadIcon.Contains("/") ? null : "ink2"));
                r.Add(lead);
            }
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.AddToClassList("ds-row-text");
            col.Add(Text(title, "ds-t-headline ds-row-title"));
            if (!string.IsNullOrEmpty(meta)) col.Add(Text(meta, "ds-t-body ds-row-meta"));
            r.Add(col);
            if (trailing != null) r.Add(trailing);
            return r;
        }

        // ------------------------------------------------------------------ dialog

        public static VisualElement Dialog(DialogSpec spec)
        {
            var wrap = new VisualElement { name = spec.Name ?? "ds-dialog" };
            wrap.AddToClassList("ds-dialog");
            wrap.AddToClassList("ds-card-wrap");
            if (spec.Width == DialogWidth.Confirm) wrap.AddToClassList("ds-dialog--confirm");
            if (spec.Width == DialogWidth.Wide) wrap.AddToClassList("ds-dialog--wide");
            var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
            shadow.AddToClassList("ds-card-shadow");
            wrap.Add(shadow);
            var card = new VisualElement();
            card.AddToClassList("ds-card");
            wrap.Add(card);
            if (spec.Art != null) { wrap.AddToClassList("ds-dialog--art"); card.Add(spec.Art); }
            if (spec.Hero != null) { if (spec.Art == null) wrap.AddToClassList("ds-dialog--hero"); card.Add(spec.Hero); }
            var brandClass = BrandTable.ClassFor(spec.Brand);
            if (brandClass != null) wrap.AddToClassList(brandClass);
            var head = new VisualElement();
            head.AddToClassList("ds-head");
            head.Add(Title(spec.Title));
            if (!string.IsNullOrEmpty(spec.Subtitle)) head.Add(Body(spec.Subtitle));
            card.Add(head);
            // v2: tiles, the divider and the progress row share one inner well
            if (spec.Tiles.Count > 0 || spec.Progress != null)
            {
                var well = new VisualElement { name = "ds-well" };
                well.AddToClassList("ds-well");
                if (spec.Tiles.Count > 0) well.Add(Tiles(spec.Tiles.ToArray()));
                if (spec.Progress != null)
                {
                    if (!string.IsNullOrEmpty(spec.ProgressLabel)) well.Add(Divider(spec.ProgressLabel));
                    well.Add(spec.Progress);
                }
                well[well.childCount - 1].AddToClassList("ds-last");
                card.Add(well);
            }
            if (spec.Body != null) card.Add(spec.Body);
            if (spec.Primary == null) throw new ArgumentException("A dialog needs exactly one primary action (SYSTEM.md 3.1)");
            var primary = Btn(spec.Primary);
            primary.name = spec.Primary.Name ?? "ds-primary";
            card.Add(primary);
            if (spec.Secondary != null)
            {
                spec.Secondary.Kind = spec.Secondary.Kind == BtnKind.Primary ? BtnKind.Text : spec.Secondary.Kind;
                var sec = Btn(spec.Secondary);
                sec.name = spec.Secondary.Name ?? "ds-secondary";
                sec.style.marginTop = -6;
                card.Add(sec);
            }
            card[card.childCount - 1].AddToClassList("ds-last");
            return wrap;
        }

        /// <summary>A one-decision confirm (880 wide): title, one line, up to 3 chips, one action (+ one text "no").</summary>
        public static VisualElement Confirm(string title, string body, DsAction primary, DsAction secondary = null, VisualElement hero = null, params VisualElement[] chips)
        {
            var spec = new DialogSpec { Title = title, Subtitle = body, Primary = primary, Secondary = secondary, Hero = hero, Width = DialogWidth.Confirm };
            if (chips != null && chips.Length > 0) spec.Body = Chips(chips);
            return Dialog(spec);
        }

        /// <summary>Replaces an inline error line above the primary (never a second dialog).</summary>
        public static void SetError(VisualElement dialog, string text)
        {
            var card = dialog?.Q(className: "ds-card");
            if (card == null) return;
            var err = card.Q<Label>(className: "ds-error");
            if (string.IsNullOrEmpty(text)) { err?.RemoveFromHierarchy(); return; }
            if (err == null)
            {
                err = Text("", "ds-error");
                var primary = card.Q<Button>(className: "ds-btn--primary") ?? card.Q<Button>(className: "ds-btn--danger");
                int at = primary != null ? card.IndexOf(primary) : card.childCount;
                card.Insert(Math.Max(0, at), err);
            }
            err.text = text;
        }

        // ------------------------------------------------------------------ progress row

        /// <summary>A progress / reward row. <paramref name="icon"/> is a Phosphor name, or a Resources path to a real picture
        /// (the reward: a crew-uniform head render) shown in the 100 white-ringed frame.</summary>
        public static DsProgressRow Progress(string icon, string title, string meta, float value01, string count, string of = null, Tone tone = Tone.Accent) =>
            new DsProgressRow(icon, title, meta, value01, count, of, tone);

        // ------------------------------------------------------------------ segmented control

        public static DsSegmented Seg(IReadOnlyList<string> labels, int selected, Action<int> onSelect, bool glass = true) => new DsSegmented(labels, selected, onSelect, glass);

        // ------------------------------------------------------------------ HUD

        public static Button HudRound(string icon, Action onClick, bool big = false, string name = null)
        {
            var b = new Button(onClick) { text = "", name = name };
            b.AddToClassList("ds-hud-round");
            if (big) b.AddToClassList("ds-hud-round--big");
            b.Add(Icon(icon, big ? 76 : 48, "white"));
            return b;
        }

        public static void SetBadge(VisualElement round, int count)
        {
            var badge = round.Q(className: "ds-badge");
            if (count <= 0) { badge?.RemoveFromHierarchy(); return; }
            if (badge == null)
            {
                badge = new VisualElement { pickingMode = PickingMode.Ignore };
                badge.AddToClassList("ds-badge");
                badge.Add(Text("", null));
                round.Add(badge);
            }
            badge.Q<Label>().text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static VisualElement Stars(int on, int max = 5)
        {
            var r = new VisualElement { pickingMode = PickingMode.Ignore };
            r.AddToClassList("ds-stars");
            SetStars(r, on, max);
            return r;
        }

        public static void SetStars(VisualElement stars, int on, int max = 5)
        {
            while (stars.childCount < max) { var s = new VisualElement { pickingMode = PickingMode.Ignore }; s.AddToClassList("ds-star"); stars.Add(s); }
            for (int i = 0; i < stars.childCount; i++) stars[i].EnableInClassList("ds-star--on", i < on);
        }

        // ------------------------------------------------------------------ prompt (world, contextual)

        public static Button Orb(string icon, Action onClick, bool main = true, bool danger = false, bool off = false, string name = null)
        {
            var b = new Button(onClick) { text = "", name = name };
            b.AddToClassList("ds-orb");
            b.AddToClassList(off ? "ds-orb--off" : danger ? "ds-orb--danger" : main ? "ds-orb--main" : "ds-orb--alt");
            if (main && !off && !danger)
            {
                var ring = new VisualElement { pickingMode = PickingMode.Ignore };
                ring.AddToClassList("ds-orb-ring");
                b.Add(ring);
            }
            var key = new VisualElement { pickingMode = PickingMode.Ignore };
            key.AddToClassList("ds-orb-key");
            key.Add(Icon(icon, main ? 60 : 44, off ? "ink2" : main && !danger ? "on-accent" : "white"));
            b.Add(key);
            return b;
        }

        public static VisualElement Pill(string text, string cls)
        {
            var p = new VisualElement { pickingMode = PickingMode.Ignore };
            p.AddToClassList(cls);
            p.Add(Text(text, null));
            return p;
        }
    }

    /// <summary>A progress row (SYSTEM.md 3.4): lead icon · title + meta over a pill bar · count "x / y".</summary>
    public sealed class DsProgressRow : VisualElement
    {
        private readonly VisualElement _fill, _bar;
        private readonly Label _count, _of, _meta, _title;

        public DsProgressRow(string icon, string title, string meta, float value01, string count, string of, Tone tone)
        {
            AddToClassList("ds-progress-row");
            name = "ds-progress";
            var lead = new VisualElement { pickingMode = PickingMode.Ignore };
            lead.AddToClassList("ds-progress-lead");
            var pic = new VisualElement { pickingMode = PickingMode.Ignore, name = "ds-progress-pic" };
            pic.AddToClassList("ds-progress-pic");
            if (icon != null && icon.Contains("/")) { var t = Ds.IconTexture(icon); if (t != null) pic.style.backgroundImage = Background.FromTexture2D(t); }
            else pic.Add(Ds.Icon(icon, 56, "ink2"));
            lead.Add(pic);
            Add(lead);
            var mid = new VisualElement();
            mid.AddToClassList("ds-progress-mid");
            var top = new VisualElement();
            top.AddToClassList("ds-progress-top");
            _title = Ds.Text(title, "ds-progress-title");
            top.Add(_title);
            _meta = Ds.Text(meta ?? "", "ds-progress-meta");
            top.Add(_meta);
            mid.Add(top);
            _bar = new VisualElement { pickingMode = PickingMode.Ignore };
            _bar.AddToClassList("ds-bar");
            if (tone == Tone.Danger) _bar.AddToClassList("ds-bar--danger");
            if (tone == Tone.Warn) _bar.AddToClassList("ds-bar--warn");
            _fill = new VisualElement { pickingMode = PickingMode.Ignore };
            _fill.AddToClassList("ds-bar-fill");
            var hi = new VisualElement { pickingMode = PickingMode.Ignore };
            hi.AddToClassList("ds-bar-hi");
            _fill.Add(hi);
            _bar.Add(_fill);
            mid.Add(_bar);
            Add(mid);
            var c = new VisualElement { pickingMode = PickingMode.Ignore };
            c.AddToClassList("ds-progress-count");
            _count = Ds.Text(count ?? "", "ds-t-stat");
            c.Add(_count);
            _of = Ds.Text(of != null ? "/" + of : "", "ds-t-stat ds-progress-of");
            c.Add(_of);
            Add(c);
            // the fill grows in on first layout (400 ms, ease-out) rather than appearing full
            float v = Mathf.Clamp01(value01);
            schedule.Execute(() => SetValue(v)).ExecuteLater(60);
        }

        public void SetValue(float value01) => _fill.style.width = new Length(Mathf.Clamp01(value01) * 100f, LengthUnit.Percent);
        public void SetCount(string count, string of = null) { _count.text = count ?? ""; if (of != null) _of.text = "/" + of; }
        public void SetMeta(string meta) => _meta.text = meta ?? "";
        public void SetTitle(string title) => Ds.SetText(_title, title);
    }

    /// <summary>A segmented control (SYSTEM.md 3.12): 2-4 segments, the selected one white with ink text.</summary>
    public sealed class DsSegmented : VisualElement
    {
        private readonly List<Button> _items = new List<Button>();
        private readonly Action<int> _onSelect;
        public int Selected { get; private set; }

        public DsSegmented(IReadOnlyList<string> labels, int selected, Action<int> onSelect, bool glass)
        {
            _onSelect = onSelect;
            AddToClassList("ds-seg");
            if (!glass) AddToClassList("ds-seg--tile");
            for (int i = 0; i < labels.Count; i++)
            {
                int idx = i;
                var b = new Button(() => Select(idx, true)) { text = "", name = "ds-seg-" + i };
                b.AddToClassList("ds-seg-item");
                b.Add(Ds.Text(labels[i], null));
                _items.Add(b);
                Add(b);
            }
            Select(selected, false);
        }

        public void Select(int i, bool notify)
        {
            Selected = Mathf.Clamp(i, 0, _items.Count - 1);
            for (int k = 0; k < _items.Count; k++) _items[k].EnableInClassList("ds-seg-item--on", k == Selected);
            if (notify) _onSelect?.Invoke(Selected);
        }
    }
}
