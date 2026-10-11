using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures
{
    /// <summary>Shared social layouts; phone-local presentation lives in phone-social.uss.</summary>
    internal static class SocialUi
    {
        private static StyleSheet _sheet;

        public static void Style(VisualElement root)
        {
            if (_sheet == null) _sheet = Resources.Load<StyleSheet>("UI/Social/social");
            if (_sheet != null && !root.styleSheets.Contains(_sheet)) root.styleSheets.Add(_sheet);
        }

        public static ScrollView Page()
        {
            var s = El.VerticalScroll("ct-app-page ct-social");
            Style(s);
            return s;
        }

        /// <summary>A label that never interprets rich text (player-written content is displayed literally).</summary>
        public static Label Text(string text, string cls = "ct-social-text")
        {
            var l = El.Text(text ?? "", cls);
            l.enableRichText = false;
            return l;
        }

        public static Label Copy(string key, string cls = "ct-social-text") => Text(SocialStrings.T(key), cls);
        public static Label Small(string text) => Text(text, "ct-small ct-muted");
        public static Label H1(string text) => Text(text, "ct-h1");
        public static Label H2(string text) => Text(text, "ct-h2");
        public static Label Eyebrow(string key) => Text(SocialStrings.T(key), "ct-eyebrow");

        public static VisualElement Row(params VisualElement[] children) => El.Div("ct-social-row", children);
        public static VisualElement Card(params VisualElement[] children) => El.Div("ct-card ct-social-card", children);

        public static Label Notice(string text, string kind = "info") =>
            Text(text, "ct-social-notice" + (kind == "warn" ? " ct-social-warn" : kind == "error" ? " ct-social-error" : ""));

        public static Button Btn(string key, Action onClick, bool primary = false, string name = null)
        {
            var b = El.Btn(SocialStrings.T(key), onClick, primary ? "ct-btn ct-btn-primary" : "ct-btn");
            if (name != null) b.name = name;
            return b;
        }

        public static Button Danger(string key, Action onClick) => El.Btn(SocialStrings.T(key), onClick, "ct-btn ct-btn-danger");
        public static Button Ghost(string key, Action onClick) => El.Btn(SocialStrings.T(key), onClick, "ct-btn ct-btn-ghost");

        public static Button Chip(string text, Action onClick, bool on)
        {
            var b = El.Btn(text, onClick, "ct-chip");
            b.EnableInClassList("ct-chip-on", on);
            return b;
        }

        public static Label Badge(string text) => Text(text, "ct-social-badge");

        public static VisualElement Status(string text, string tone = "neutral", string icon = null)
        {
            var status = El.Div("ct-social-status ct-social-status-" + tone);
            if (icon != null) status.Add(new UiIcon(icon));
            status.Add(Text(text, "ct-social-status-copy"));
            return status;
        }

        public static VisualElement Avatar(string name)
        {
            var initials = string.IsNullOrWhiteSpace(name) ? "?" : System.Globalization.StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
            return El.PassThrough(El.Div("ct-social-avatar", Text(initials, "ct-social-avatar-letter")));
        }

        /// <summary>Visible icon + label: use only for actions the service actually supports.</summary>
        public static Button Action(string key, string icon, Action clicked, bool primary = false, bool danger = false)
        {
            var cls = "ct-btn ct-social-action" + (primary ? " ct-btn-primary" : "") + (danger ? " ct-btn-danger" : "");
            var button = El.Btn("", clicked, cls);
            button.tooltip = SocialStrings.T(key);
            button.name = "social-" + key.Replace('.', '-');
            button.Add(icon == "send" || icon == "speaker" || icon == "speaker-off"
                ? (VisualElement)new SocialControlIcon(icon) : new UiIcon(icon));
            button.Add(El.PassThrough(Text(SocialStrings.T(key), "ct-social-action-label")));
            return button;
        }

        public static VisualElement Composer(TextField field, VisualElement detail, Button send)
        {
            field.AddToClassList("ct-social-compose-field");
            detail.AddToClassList("ct-social-compose-detail");
            var tools = El.Div("ct-social-compose-tools", detail, send);
            return El.Div("ct-social-composer", field, tools);
        }

        public static VisualElement Empty(string icon, string title, string detail = null)
        {
            var card = Card(El.Div("ct-social-empty-symbol", new UiIcon(icon)), H2(title));
            card.AddToClassList("ct-social-empty");
            if (!string.IsNullOrEmpty(detail)) card.Add(Small(detail));
            return card;
        }

        public static TextField Field(string labelKey, string value, int maxLength, Action<string> changed, bool multiline = false)
        {
            var f = new TextField(SocialStrings.T(labelKey)) { value = value ?? "", maxLength = maxLength, multiline = multiline, isDelayed = false };
            f.AddToClassList("ct-field");
            f.AddToClassList("ct-social-input");
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
        }

        /// <summary>Report reasons from the config registry (report_reasons.json) as buttons.</summary>
        public static VisualElement ReportPanel(string targetLabel, Action<string> pick, Action cancel)
        {
            var card = Card(H2(SocialStrings.F("common.reportTitle", targetLabel)), Copy("common.reportWhy", "ct-small ct-muted"));
            var reasons = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Registries.ReportReasons : null;
            var row = Row();
            if (reasons != null)
                foreach (var r in reasons)
                {
                    var id = r.id;
                    row.Add(El.Btn(Strings.ReportReason(r.id, r.name), () => pick(id), "ct-btn"));
                }
            card.Add(row);
            card.Add(Ghost("common.cancel", cancel));
            return card;
        }

        /// <summary>Scam hint under a message/post/invite (display only).</summary>
        public static Label Flag(string text)
        {
            var f = ScamSignals.Detect(text);
            return f == ScamSignals.Flag.None ? null : Notice(SocialStrings.T("flag." + f), "warn");
        }

        public static string Err(string key) => string.IsNullOrEmpty(key) ? null : SocialStrings.T(key);
    }

    /// <summary>
    /// Base for one opened social screen: a ScrollView with a re-rendered <see cref="Body"/> (throttled to 4 Hz on hub
    /// changes) between a persistent <see cref="Header"/> and <see cref="Footer"/> for composers. A render is deferred while a field inside Body has focus,
    /// so typing is never interrupted. Disposed on detach, phone close or identity change.
    /// </summary>
    internal abstract class SocialView : IDisposable
    {
        public readonly ScrollView Root;
        protected readonly SocialHub Hub;
        protected readonly PhoneShell Phone;
        protected readonly VisualElement Header, Body, Footer;
        private readonly IVisualElementScheduledItem _tick;
        private bool _dirty = true;
        private double _lastRender = double.NegativeInfinity;
        private bool _pressing;
        protected bool Disposed { get; private set; }
        protected static double Now => SocialHub.NowMs;

        protected SocialView(SocialHub hub, PhoneShell phone)
        {
            Hub = hub;
            Phone = phone;
            Root = SocialUi.Page();
            Header = El.Div("ct-social-header");
            Body = El.Div("ct-social-body");
            Footer = El.Div("ct-social-footer");
            Root.Add(Header);
            Root.Add(Body);
            Root.Add(Footer);
            Hub.Changed += MarkDirty;
            Root.RegisterCallback<DetachFromPanelEvent>(Detached);
            // Never rebuild under a finger: a tap pressed on a button that a render replaces would be lost.
            Root.RegisterCallback<PointerDownEvent>(_ => _pressing = true, TrickleDown.TrickleDown);
            Root.RegisterCallback<PointerUpEvent>(_ => _pressing = false, TrickleDown.TrickleDown);
            Root.RegisterCallback<PointerCancelEvent>(_ => _pressing = false, TrickleDown.TrickleDown);
            Root.RegisterCallback<PointerLeaveEvent>(_ => _pressing = false);
            _tick = Root.schedule.Execute(OnTick).Every(250);
        }

        protected void MarkDirty() => _dirty = true;

        private void Detached(DetachFromPanelEvent e) { if (e.target == Root) Dispose(); }

        private void OnTick()
        {
            if (Disposed) return;
            if (Hub.Disposed) { Body.Clear(); Body.Add(SocialUi.Notice(SocialStrings.T("err.session"), "error")); Dispose(); return; }
            try
            {
                if ((_dirty || (NeedsClockRender && Now - _lastRender >= 1000)) && !_pressing)
                {
                    var focused = Root.panel?.focusController?.focusedElement as VisualElement;
                    if (focused != null && Body.Contains(focused) &&
                        (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null)) return; // typing: render after the field loses focus
                    _dirty = false;
                    _lastRender = Now;
                    var scroll = Root.scrollOffset;
                    Body.Clear();
                    if (Hub.Offline) Body.Add(SocialUi.Notice(SocialStrings.T("common.offline"), "warn"));
                    Render();
                    var toEnd = ScrollToEnd;
                    ScrollToEnd = false;
                    Root.schedule.Execute(() =>
                    {
                        if (Disposed) return;
                        Root.scrollOffset = toEnd ? new UnityEngine.Vector2(scroll.x, Mathf.Max(0, Root.contentContainer.layout.height - Root.contentViewport.layout.height)) : scroll;
                    });
                }
            }
            catch (Exception e) { Log.Exception("social", e); }
        }

        /// <summary>Set before a render to scroll to the bottom afterwards (chat: new messages).</summary>
        protected bool ScrollToEnd;

        /// <summary>True while something on screen counts down (ringing, live call timer): render once a second.</summary>
        protected virtual bool NeedsClockRender => false;

        protected abstract void Render();

        public virtual void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _tick?.Pause();
            Hub.Changed -= MarkDirty;
            Root.UnregisterCallback<DetachFromPanelEvent>(Detached);
        }
    }

    /// <summary>Base phone app for the social features: holds the shared hub and one open view at a time.</summary>
    public abstract class SocialApp : PhoneApp, IDisposable
    {
        private SocialHub _hub;
        private SocialView _view;

        internal SocialHub Hub
        {
            get
            {
                if (_hub == null || _hub.Disposed) { _hub?.Release(); _hub = SocialHub.Acquire(); }
                return _hub;
            }
        }

        public override int Phase => 3;

        public override VisualElement Open()
        {
            Closed();
            var hub = Hub;
            if (hub == null) return El.Div("ct-app-page", SocialUi.Notice(SocialStrings.T("err.session"), "error"));
            _view = CreateView(hub, Phone);
            return _view.Root;
        }

        internal abstract SocialView CreateView(SocialHub hub, PhoneShell phone);

        public override void Closed()
        {
            var v = _view;
            _view = null;
            v?.Dispose();
        }

        public void Dispose()
        {
            Closed();
            _hub?.Release();
            _hub = null;
        }
    }
}
