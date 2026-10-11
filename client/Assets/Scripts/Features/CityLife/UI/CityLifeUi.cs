using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// UI Toolkit builders for the City Life phone screens on the Duo kit: sections on the canvas, inset key/value rows, capsule
    /// buttons, a kit field and small meters. <see cref="Legacy"/> keeps the world-overlay look for sheets outside the phone.
    /// </summary>
    internal static class CityUi
    {
        public const string Primary = "primary", Plain = "plain", Danger = "danger";

        public static ScrollView Page() => El.VerticalScroll();

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

        /// <summary>A section on the canvas, separated by space rather than a box.</summary>
        public static VisualElement Card(params VisualElement[] children) => El.Div("duo-comp-section", children);

        public static VisualElement Row(params VisualElement[] children) => El.Div("duo-comp-actions", children);

        /// <summary>Key on the left, value on the right, inset hairline below.</summary>
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

        /// <summary>A small rounded meter 0..1 (kit duo-meter).</summary>
        public static VisualElement Bar(out VisualElement fill)
        {
            fill = El.Div("duo-meter-fill");
            fill.style.width = Length.Percent(0);
            return El.Div("duo-meter", fill);
        }

        public static void SetBar(VisualElement fill, double fraction, NeedLevel level)
        {
            fill.style.width = Length.Percent((float)(Math.Max(0, Math.Min(1, fraction)) * 100));
            var track = fill.parent;
            if (track == null) return;
            track.EnableInClassList("duo-meter-green", level == NeedLevel.Ok);
            track.EnableInClassList("duo-meter-orange", level == NeedLevel.Low);
            track.EnableInClassList("duo-meter-red", level != NeedLevel.Ok && level != NeedLevel.Low);
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
        public static Label Tag(string text, string tone = "gray") => Text(text, "duo-tag duo-tag-" + tone);

        /// <summary>The world-overlay look (furniture sheet), outside the phone's Duo styles.</summary>
        internal static class Legacy
        {
            public static Label Text(string text, string cls = "ct-body") => CityUi.Text(text, cls);
            public static Label H1(string text) => CityUi.Text(text, "ct-h1");
            public static Label Small(string text) => CityUi.Text(text, "ct-small ct-muted");
            public static Button Btn(string text, Action onClick, string cls = "ct-btn ct-btn-primary") => El.Btn(text, onClick, cls);
        }

        public static void Copy(string text)
        {
            GUIUtility.systemCopyBuffer = text ?? "";
            Bus.Publish(ToastSignal.Text(CityLifeStrings.T("common.copied")));
        }

        public static string When(long epochMs)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToUniversalTime();
            return t.ToString("ddd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        }
    }

    /// <summary>
    /// Base for one opened City Life screen: a ScrollView, a lifetime token, a 1 Hz tick while attached, disposal on detach,
    /// phone close or a change of signed-in identity. Async work is guarded so a late reply never touches a disposed view.
    /// </summary>
    internal abstract class CityView : IDisposable
    {
        public readonly ScrollView Root;
        /// <summary>The Duo frame (nav + large title) the phone shows; content goes into <see cref="Body"/>.</summary>
        public readonly DuoPage Page;
        protected VisualElement Body => Page.Body;
        internal PhoneShell Shell;
        private DuoSheet _sheet;
        protected readonly CityLifeHub Hub;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Session _session;
        private readonly string _user;
        private readonly IVisualElementScheduledItem _tick;
        protected readonly Subscriptions Subs = new Subscriptions();
        protected bool Disposed { get; private set; }
        protected CancellationToken Life => _life.Token;

        protected CityView(CityLifeHub hub, string title, bool largeTitle = true)
        {
            Hub = hub;
            Root = CityUi.Page();
            Page = new DuoPage(Root, title, largeTitle);
            Page.AddToClassList("duo-comp");
            Page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => { if (!Back()) Shell?.Back(); }, "city-back");
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
            catch (Exception e) { Log.Exception("citylife", e); }
        }

        protected virtual void Tick() { }

        /// <summary>Nav back inside the screen. False lets the phone go back.</summary>
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
        protected async void Run<T>(Func<CancellationToken, Task<T>> work, Action<T> done, Action<CityError> fail, bool write)
        {
            if (!Alive) return;
            T result;
            try { result = await work(Life); }
            catch (OperationCanceledException) when (Life.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                if (!Alive) return;
                var err = CityErrors.From(e, write);
                Log.Warn("citylife", (write ? "write" : "read") + " failed: " + err.Code);
                try { fail?.Invoke(err); } catch (Exception inner) { Log.Exception("citylife", inner); }
                return;
            }
            if (!Alive) return;
            try { done?.Invoke(result); } catch (Exception e) { Log.Exception("citylife", e); }
        }

        protected void Run(Func<CancellationToken, Task> work, Action done, Action<CityError> fail, bool write) =>
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

    /// <summary>Base phone app for City Life: holds the shared hub and one open view at a time.</summary>
    public abstract class CityApp : PhoneApp, IDisposable
    {
        private CityLifeHub _hub;
        private CityView _view;

        protected CityApp()
        {
            try { _hub = CityLifeHub.Acquire(); }
            catch (Exception e) { Log.Warn("citylife", "Hub deferred: " + e.Message); }
        }

        internal CityLifeHub Hub => _hub == null || _hub.Disposed ? (_hub = CityLifeHub.Acquire()) : _hub;
        internal PhoneShell Shell => Phone;
        public override DuoChrome Chrome => DuoChrome.Plain;

        public override VisualElement Open()
        {
            Closed();
            _view = CreateView(Hub);
            _view.Shell = Shell;
            return _view.Page;
        }

        internal abstract CityView CreateView(CityLifeHub hub);

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
