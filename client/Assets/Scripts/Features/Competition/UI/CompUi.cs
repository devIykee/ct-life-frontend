using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Competition
{
    /// <summary>Small UI Toolkit builders on CTLife.UI's El and the existing ct-* theme. Functional, not styled. All text is plain.</summary>
    internal static class CompUi
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

        public static VisualElement Pair(string key, string value)
        {
            var spacer = El.Div("ct-grow");
            spacer.style.flexGrow = 1;
            return Row(Text(key, "ct-small ct-muted"), spacer, Text(value, "ct-body"));
        }

        public static Button Btn(string text, Action onClick, string cls = "ct-btn ct-btn-primary") => El.Btn(text, onClick, cls);

        public static Button Chip(string text, Action onClick, bool on)
        {
            var b = PhonePresentation.SizeToText(El.Btn(text, onClick, "ct-chip"));
            b.EnableInClassList("ct-chip-on", on);
            return b;
        }

        public static TextField Field(string label, string value, Action<string> changed, int maxLength)
        {
            var f = new TextField(label) { value = value ?? "", isDelayed = false, maxLength = maxLength };
            f.AddToClassList("ct-field");
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
        }

        public static VisualElement Tabs(string[] labels, int selected, Action<int> pick)
        {
            var row = El.Div("ct-app-tabs");
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                row.Add(Chip(labels[i], () => pick(idx), i == selected));
            }
            return row;
        }

        /// <summary>The "not available yet" or error block, with a retry button where a retry makes sense.</summary>
        public static VisualElement ErrorBlock(CompError err, Action retry)
        {
            var card = Card();
            if (err.IsUnavailable)
            {
                card.Add(H2(CompStrings.T("unavailable.title")));
                card.Add(Text(CompStrings.T("unavailable.body")));
            }
            else
            {
                card.Add(Error(err.Message));
            }
            if (retry != null && (err.Retryable || err.IsUnavailable)) card.Add(Btn(CompStrings.T(err.IsUnavailable ? "common.checkAgain" : "common.retry"), retry, "ct-btn"));
            return card;
        }
    }

    /// <summary>
    /// Base for one opened Phase 6 screen: a ScrollView, a lifetime token, a 1 Hz tick while attached, and disposal on detach, phone close or a
    /// change of signed-in identity. Async work is guarded so a late reply never touches a disposed view.
    /// </summary>
    internal abstract class CompScreen : IDisposable
    {
        public readonly ScrollView Root;
        protected readonly CompetitionHub Hub;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Session _session;
        private readonly string _user;
        private readonly IVisualElementScheduledItem _tick;
        protected readonly Subscriptions Subs = new Subscriptions();
        protected bool Disposed { get; private set; }
        protected CancellationToken Life => _life.Token;
        protected string Me => _user;

        protected CompScreen(CompetitionHub hub)
        {
            Hub = hub;
            Root = CompUi.Page();
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
            catch (Exception e) { Log.Exception("competition", e); }
        }

        protected virtual void Tick() { }

        /// <summary>Runs async work bound to this view. Callbacks run only while the view is alive.</summary>
        protected async void Run<T>(Func<CancellationToken, Task<T>> work, Action<T> done, Action<CompError> fail, bool write)
        {
            if (!Alive) return;
            T result;
            try { result = await work(Life); }
            catch (OperationCanceledException) when (Life.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                if (!Alive) return;
                var err = CompErrors.From(e, write);
                Log.Warn("competition", (write ? "write" : "read") + " failed: " + err.Code);
                try { fail?.Invoke(err); } catch (Exception inner) { Log.Exception("competition", inner); }
                return;
            }
            if (!Alive) return;
            try { done?.Invoke(result); } catch (Exception e) { Log.Exception("competition", e); }
        }

        /// <summary>Loads into a body container: "Loading…", then `render` on success or the error block (with retry) on failure.</summary>
        protected void Load<T>(VisualElement body, Func<CancellationToken, Task<T>> work, Action<VisualElement, T> render)
        {
            body.Clear();
            body.Add(CompUi.Small(CompStrings.T("common.loading")));
            Run(work, r => { body.Clear(); render(body, r); }, e => { body.Clear(); body.Add(CompUi.ErrorBlock(e, () => Load(body, work, render))); }, false);
        }

        public virtual void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            _tick?.Pause();
            Subs.Dispose();
            _session.Changed -= SessionChanged;
            Root.UnregisterCallback<DetachFromPanelEvent>(Detached);
            OnDisposed();
        }

        protected virtual void OnDisposed() { }
    }

    /// <summary>Base phone app for Phase 6: holds the shared hub and one open screen at a time.</summary>
    public abstract class CompApp : PhoneApp, IDisposable
    {
        private CompetitionHub _hub;
        private CompScreen _view;

        protected CompApp()
        {
            try { _hub = CompetitionHub.Acquire(); }
            catch (Exception e) { Log.Warn("competition", "Hub deferred: " + e.Message); }
        }

        public override int Phase => 6;

        internal CompetitionHub Hub => _hub == null || _hub.Disposed ? (_hub = CompetitionHub.Acquire()) : _hub;
        internal PhoneShell Shell => Phone;

        public override VisualElement Open()
        {
            Closed();
            _view = CreateView(Hub);
            return _view.Root;
        }

        internal abstract CompScreen CreateView(CompetitionHub hub);

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
