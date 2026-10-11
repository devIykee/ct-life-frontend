using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Ventures
{
    /// <summary>
    /// UI Toolkit builders for the Ventures phone screens on the Duo kit: sections on the canvas, capsule buttons, filter chips,
    /// kit field, status tags (real states only). Every label is plain text.
    /// </summary>
    internal static class VUi
    {
        public const string Primary = "primary", Plain = "plain", Danger = "danger";

        public static ScrollView Page() => El.VerticalScroll();

        /// <summary>Every label goes through El.Text with rich text off: names, aliases and server strings are untrusted.</summary>
        public static Label Text(string text, string cls = "duo-comp-text")
        {
            var l = El.Text(text ?? "", cls);
            l.enableRichText = false;
            return l;
        }

        public static Label H2(string text) => Text(text, "duo-comp-h2");
        public static Label Small(string text) => Text(text, "duo-comp-note");
        public static Label Error(string text) => Text(text, "duo-comp-error");
        public static Label Label(string text) => Text((text ?? "").ToUpperInvariant(), "duo-comp-label");

        /// <summary>A section on the canvas (no box).</summary>
        public static VisualElement Card(params VisualElement[] children) => El.Div("duo-comp-section", children);

        public static VisualElement Row(params VisualElement[] children) => El.Div("duo-comp-actions", children);

        public static VisualElement ChipRow(params VisualElement[] children) => El.Div("duo-chip-row", children);

        public static VisualElement Pair(string key, string value) =>
            El.Div("duo-comp-pair", Text(key, "duo-comp-pair-key"), Text(value, "duo-comp-pair-value"));

        public static Button Btn(string text, Action onClick, string kind = Primary)
        {
            var b = El.Btn(text, onClick, "duo-comp-btn" + (kind == Plain ? "" : kind == Danger ? " duo-comp-btn-danger" : " duo-comp-btn-primary"));
            b.enableRichText = false;
            return b;
        }

        public static Button Chip(string text, Action onClick, bool on)
        {
            var b = El.Btn(text, onClick, "duo-chip");
            b.enableRichText = false;
            b.EnableInClassList("duo-chip-on", on);
            return b;
        }

        public static TextField Field(string label, string value, Action<string> changed, int maxLength = 200)
        {
            var f = new TextField { value = value ?? "", isDelayed = false, maxLength = maxLength };
            f.AddToClassList("duo-field");
            f.textEdition.placeholder = label ?? "";
            f.tooltip = label;
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
        }

        /// <summary>Status tag (reference 2) for a real server state.</summary>
        public static VisualElement Status(string text, PhonePresentation.Tone tone = PhonePresentation.Tone.Neutral)
        {
            string t = tone switch
            {
                PhonePresentation.Tone.Success => "green",
                PhonePresentation.Tone.Warning => "orange",
                PhonePresentation.Tone.Danger => "red",
                PhonePresentation.Tone.Info => "blue",
                PhonePresentation.Tone.Accent => "blue",
                _ => "gray",
            };
            return El.Div("duo-comp-tags", Text(text, "duo-tag duo-tag-" + t));
        }

        /// <summary>A number with its label as a key/value row (no tile).</summary>
        public static VisualElement Metric(string label, string value, string icon, PhonePresentation.Tone tone = PhonePresentation.Tone.Info, string detail = null)
        {
            var row = Pair(label, value ?? "—");
            if (!string.IsNullOrEmpty(detail)) row.Add(Text(detail, "duo-comp-pair-detail"));
            return row;
        }

        public static string When(long epochMs)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToUniversalTime();
            return t.ToString("ddd d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";
        }

        public static string Day(long epochMs) =>
            DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToUniversalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        /// <summary>"3h 05m" style countdown for an expiry in the future; the "expired" copy otherwise.</summary>
        public static string Until(long targetMs, double nowMs)
        {
            double s = (targetMs - nowMs) / 1000.0;
            if (s <= 0) return VStrings.T("time.expired");
            if (s < 3600) return Math.Floor(s / 60).ToString("0", CultureInfo.InvariantCulture) + "m " + Math.Floor(s % 60).ToString("00", CultureInfo.InvariantCulture) + "s";
            if (s < 86400) return Math.Floor(s / 3600).ToString("0", CultureInfo.InvariantCulture) + "h " + Math.Floor(s % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + "m";
            return Math.Floor(s / 86400).ToString("0", CultureInfo.InvariantCulture) + "d " + Math.Floor(s % 86400 / 3600).ToString("0", CultureInfo.InvariantCulture) + "h";
        }

        public static string Short(string s, int head = 4, int tail = 4) =>
            string.IsNullOrEmpty(s) || s.Length <= head + tail + 1 ? s ?? "" : s.Substring(0, head) + "…" + s.Substring(s.Length - tail);
    }

    /// <summary>
    /// Base for one opened Ventures screen: a ScrollView, a lifetime token, a 1 Hz tick while attached, disposal on detach, phone
    /// close or a change of signed-in identity. Async work is guarded so a late reply never touches a disposed view.
    /// </summary>
    internal abstract class VView : IDisposable
    {
        public readonly ScrollView Root;
        /// <summary>The Duo frame (nav + large title) the phone shows; content goes into <see cref="Body"/>.</summary>
        public readonly DuoPage Page;
        protected VisualElement Body => Page.Body;
        internal PhoneShell Shell;
        private DuoSheet _sheet;
        protected readonly VenturesHub Hub;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Session _session;
        private readonly string _user;
        private readonly IVisualElementScheduledItem _tick;
        protected readonly Subscriptions Subs = new Subscriptions();
        protected bool Disposed { get; private set; }
        protected CancellationToken Life => _life.Token;

        protected VView(VenturesHub hub, string title)
        {
            Hub = hub;
            Root = VUi.Page();
            Page = new DuoPage(Root, title);
            Page.AddToClassList("duo-comp");
            Page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => { if (!Back()) Shell?.Back(); }, "ventures-back");
            _session = Services.Get<Session>();
            _user = _session.UserId;
            _session.Changed += SessionChanged;
            Root.RegisterCallback<DetachFromPanelEvent>(Detached);
            _tick = Root.schedule.Execute(OnTick).Every(1000);
        }

        protected bool Alive => !Disposed && !Hub.Disposed && _session.IsSignedIn && _session.UserId == _user;

        private void SessionChanged() { if (!Alive) Dispose(); }

        private void Detached(DetachFromPanelEvent e) { if (e.target == Root) Dispose(); }

        private void OnTick()
        {
            if (!Alive) return;
            try { Tick(); }
            catch (Exception e) { Log.Exception("ventures", e); }
        }

        protected virtual void Tick() { }

        /// <summary>Nav back inside the screen (booking → slots). False lets the phone go back.</summary>
        protected virtual bool Back() => false;

        protected DuoSheet ShowSheet(DuoSheet sheet)
        {
            _sheet?.Close();
            _sheet = sheet;
            sheet.Scrolling();
            sheet.Closed += () => { if (_sheet == sheet) _sheet = null; };
            return sheet.Show(Page);
        }

        /// <summary>Runs async work bound to this view. Callbacks run only while the view is alive.</summary>
        protected async void Run<T>(Func<CancellationToken, Task<T>> work, Action<T> done, Action<VError> fail, bool write)
        {
            if (!Alive) return;
            T result;
            try { result = await work(Life); }
            catch (OperationCanceledException) when (Life.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                if (!Alive) return;
                var err = VErrors.From(e, write);
                Log.Warn("ventures", (write ? "write" : "read") + " failed: " + err.Code);
                try { fail?.Invoke(err); } catch (Exception inner) { Log.Exception("ventures", inner); }
                return;
            }
            if (!Alive) return;
            try { done?.Invoke(result); } catch (Exception e) { Log.Exception("ventures", e); }
        }

        protected void Run(Func<CancellationToken, Task> work, Action done, Action<VError> fail, bool write) =>
            Run<bool>(async ct => { await work(ct); return true; }, _ => done?.Invoke(), fail, write);

        public virtual void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _sheet?.Close();
            _life.Cancel();
            _tick?.Pause();
            Subs.Dispose();
            _session.Changed -= SessionChanged;
            Root.UnregisterCallback<DetachFromPanelEvent>(Detached);
        }
    }

    /// <summary>Base phone app for Ventures: holds the shared hub and one open view at a time.</summary>
    public abstract class VenturesApp : PhoneApp, IDisposable
    {
        private VenturesHub _hub;
        private VView _view;

        protected VenturesApp()
        {
            try { _hub = VenturesHub.Acquire(); }
            catch (Exception e) { Log.Warn("ventures", "Hub deferred: " + e.Message); }
        }

        internal VenturesHub Hub => _hub == null || _hub.Disposed ? (_hub = VenturesHub.Acquire()) : _hub;
        public override DuoChrome Chrome => DuoChrome.Plain;

        public override VisualElement Open()
        {
            Closed();
            _view = CreateView(Hub);
            _view.Shell = Phone;
            return _view.Page;
        }

        internal abstract VView CreateView(VenturesHub hub);

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
