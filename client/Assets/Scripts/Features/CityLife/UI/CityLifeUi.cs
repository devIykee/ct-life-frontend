using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>Small UI Toolkit builders on CTLife.UI's El and the existing ct-* theme. Functional, not styled.</summary>
    internal static class CityUi
    {
        public static ScrollView Page() => El.VerticalScroll("ct-app-page");

        public static Label Text(string text, string cls = "ct-body")
        {
            var l = El.Text(text ?? "", cls);
            l.enableRichText = false;
            return l;
        }

        public static Label H1(string text) => Text(text, "ct-h1");
        public static Label H2(string text) => Text(text, "ct-h2");
        public static Label Small(string text) => Text(text, "ct-small ct-muted");
        public static Label Error(string text) => Text(text, "ct-error");

        public static VisualElement Card(params VisualElement[] children) => El.Div("ct-card", children);

        public static VisualElement Row(params VisualElement[] children)
        {
            var r = El.Div("ct-row", children);
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            r.style.flexWrap = Wrap.Wrap;
            return r;
        }

        /// <summary>A muted label on the left, a value on the right.</summary>
        public static VisualElement Pair(string key, string value)
        {
            var row = Row(Text(key, "ct-small ct-muted"), El.Div("ct-grow"), Text(value, "ct-body"));
            row.Q(className: "ct-grow").style.flexGrow = 1;
            return row;
        }

        public static Button Btn(string text, Action onClick, string cls = "ct-btn ct-btn-primary") => El.Btn(text, onClick, cls);

        public static Button Chip(string text, Action onClick, bool on)
        {
            var b = PhonePresentation.SizeToText(El.Btn(text, onClick, "ct-chip"));
            b.EnableInClassList("ct-chip-on", on);
            return b;
        }

        /// <summary>A horizontal bar 0..1 drawn with two elements (no USS file needed).</summary>
        public static VisualElement Bar(out VisualElement fill)
        {
            var track = El.Div("ct-need-track");
            track.style.height = 10;
            track.style.flexGrow = 1;
            track.style.backgroundColor = new Color(0, 0, 0, 0.15f);
            fill = El.Div("ct-need-fill");
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(0);
            track.Add(fill);
            return track;
        }

        public static void SetBar(VisualElement fill, double fraction, NeedLevel level)
        {
            fill.style.width = Length.Percent((float)(Math.Max(0, Math.Min(1, fraction)) * 100));
            fill.style.backgroundColor = level == NeedLevel.Ok ? new Color(0.13f, 0.62f, 0.4f) : level == NeedLevel.Low ? new Color(0.96f, 0.65f, 0.14f) : new Color(0.85f, 0.25f, 0.25f);
        }

        public static TextField Field(string label, string value, Action<string> changed, int maxLength = 200)
        {
            var f = new TextField(label) { value = value ?? "", isDelayed = false, maxLength = maxLength };
            f.AddToClassList("ct-field");
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
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
        protected readonly CityLifeHub Hub;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Session _session;
        private readonly string _user;
        private readonly IVisualElementScheduledItem _tick;
        protected readonly Subscriptions Subs = new Subscriptions();
        protected bool Disposed { get; private set; }
        protected CancellationToken Life => _life.Token;

        protected CityView(CityLifeHub hub)
        {
            Hub = hub;
            Root = CityUi.Page();
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

        public override VisualElement Open()
        {
            Closed();
            _view = CreateView(Hub);
            return _view.Root;
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
