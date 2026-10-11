using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Competition
{
    /// <summary>
    /// UI Toolkit builders for the Phase 6 screens on the Duo kit (duo-kit.uss / duo-apps.uss): sections on the canvas instead of
    /// boxed cards, key/value rows with inset hairlines, capsule buttons, segmented tabs, a kit text field. All text is plain.
    /// </summary>
    internal static class CompUi
    {
        /// <summary>Button styles: <see cref="Primary"/> (blue capsule), <see cref="Plain"/> (grey capsule, blue label), <see cref="Danger"/>.</summary>
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
        /// <summary>Small uppercase label above a control group (iOS grouped-table header).</summary>
        public static Label Label(string text) => Text((text ?? "").ToUpperInvariant(), "duo-comp-label");

        /// <summary>A section on the canvas: its own title, rows and actions; spacing, not a box, separates sections.</summary>
        public static VisualElement Card(params VisualElement[] children) => El.Div("duo-comp-section", children);

        public static VisualElement Row(params VisualElement[] children) => El.Div("duo-comp-actions", children);

        /// <summary>Key on the left, value on the right, inset hairline below (iOS detail row).</summary>
        public static VisualElement Pair(string key, string value) =>
            El.Div("duo-comp-pair", Text(key, "duo-comp-pair-key"), Text(value, "duo-comp-pair-value"));

        /// <summary>A key/value row whose value is coloured by sign or state ("duo-value-positive" / "duo-value-negative").</summary>
        public static VisualElement Pair(string key, string value, string valueClass)
        {
            var row = Pair(key, value);
            if (!string.IsNullOrEmpty(valueClass)) row.ElementAt(1).AddToClassList(valueClass);
            return row;
        }

        public static Button Btn(string text, Action onClick, string kind = Primary)
        {
            var b = El.Btn(text, onClick, "duo-comp-btn" + (kind == Plain ? "" : kind == Danger ? " duo-comp-btn-danger" : " duo-comp-btn-primary"));
            b.enableRichText = false;
            return b;
        }

        /// <summary>Selectable capsule (topic, reason). Callers flip "duo-chip-on".</summary>
        public static Button Chip(string text, Action onClick, bool on)
        {
            var b = El.Btn(text, onClick, "duo-chip");
            b.enableRichText = false;
            b.EnableInClassList("duo-chip-on", on);
            return b;
        }

        public static VisualElement ChipRow(params VisualElement[] chips) => El.Div("duo-chip-row", chips);

        public static TextField Field(string label, string value, Action<string> changed, int maxLength)
        {
            var f = new TextField { value = value ?? "", isDelayed = false, maxLength = maxLength };
            f.AddToClassList("duo-field");
            f.textEdition.placeholder = label ?? "";
            f.tooltip = label;
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
        }

        /// <summary>Segmented control (iOS); re-created by the screens on every tab change.</summary>
        public static VisualElement Tabs(string[] labels, int selected, Action<int> pick)
        {
            var seg = new DuoSegmented(labels, selected, i => pick(i));
            return El.Div("duo-comp-tabs", seg);
        }

        /// <summary>The "not available yet" or error state, with a retry where a retry makes sense. Centred, no box.</summary>
        public static VisualElement ErrorBlock(CompError err, Action retry)
        {
            bool canRetry = retry != null && (err.Retryable || err.IsUnavailable);
            string action = canRetry ? CompStrings.T(err.IsUnavailable ? "common.checkAgain" : "common.retry") : null;
            if (err.IsUnavailable) return Duo.Empty(CompStrings.T("unavailable.title"), CompStrings.T("unavailable.body"), action, canRetry ? retry : null);
            var block = El.Div("duo-comp-error-block", Duo.Notice(err.Message, Duo.Tone.Danger));
            if (canRetry) block.Add(Row(Btn(action, retry, Plain)));
            return block;
        }

        /// <summary>Rank row for leaderboards: rank, avatar, alias, value; "you" is highlighted.</summary>
        public static VisualElement RankRow(int rank, string alias, string seed, string value, bool me)
        {
            var row = new DuoRow().Leading(El.Div("duo-rank-lead", Text(rank > 0 ? rank.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—", "duo-rank-number"),
                    Duo.Avatar(alias, seed, "duo-avatar-s")))
                .Title(alias, me ? "duo-row-title-strong" : null).Value(value, "duo-rank-value");
            row.AddToClassList("duo-row-static");
            row.EnableInClassList("duo-rank-me", me);
            return row;
        }

        /// <summary>Status tag (reference 2): a small tinted capsule naming a real server state.</summary>
        public static Label Tag(string text, string tone = "gray") => Text(text, "duo-tag duo-tag-" + tone);
    }

    /// <summary>
    /// Base for one opened Phase 6 screen: a ScrollView, a lifetime token, a 1 Hz tick while attached, and disposal on detach, phone close or a
    /// change of signed-in identity. Async work is guarded so a late reply never touches a disposed view.
    /// </summary>
    internal abstract class CompScreen : IDisposable
    {
        public readonly ScrollView Root;
        /// <summary>The Duo frame (nav + large title) around <see cref="Root"/>; what the phone shows.</summary>
        public readonly DuoPage Page;
        /// <summary>Content below the large title.</summary>
        protected VisualElement Body => Page.Body;
        internal PhoneShell Shell;
        protected readonly CompetitionHub Hub;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Session _session;
        private readonly string _user;
        private readonly IVisualElementScheduledItem _tick;
        protected readonly Subscriptions Subs = new Subscriptions();
        protected bool Disposed { get; private set; }
        protected CancellationToken Life => _life.Token;
        protected string Me => _user;

        protected CompScreen(CompetitionHub hub, string title)
        {
            Hub = hub;
            Root = CompUi.Page();
            Page = new DuoPage(Root, title);
            Page.AddToClassList("duo-comp");
            Page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => { if (!Back()) Shell?.Back(); }, "comp-back");
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

        /// <summary>A row for another player: avatar and title fill in once the alias resolves (cached aliases render at once).</summary>
        protected DuoRow PersonRow(string userId, Func<string, string> title, Action onTap = null)
        {
            var cached = Hub.AliasCached(userId);
            var row = new DuoRow(onTap).Leading(Duo.Avatar(cached ?? "…", userId, "duo-avatar-m")).Title(cached != null ? title(cached) : "…", "duo-row-title-strong");
            if (onTap == null) row.AddToClassList("duo-row-static");
            if (cached == null)
                Run(ct => Hub.AliasOf(userId, ct), a =>
                {
                    row.TitleLabel.text = title(a);
                    var letter = row.Q<Label>(className: "duo-avatar-letter");
                    if (letter != null && !string.IsNullOrWhiteSpace(a)) letter.text = System.Globalization.StringInfo.GetNextTextElement(a.Trim()).ToUpperInvariant();
                }, null, false);
            return row;
        }

        /// <summary>Nav back inside the screen (detail → list). False lets the phone go back.</summary>
        protected virtual bool Back() => false;

        /// <summary>Shows a bottom sheet over this screen; closed with the screen.</summary>
        protected DuoSheet ShowSheet(DuoSheet sheet)
        {
            _sheet?.Close();
            _sheet = sheet;
            sheet.Scrolling();
            sheet.Closed += () => { if (_sheet == sheet) _sheet = null; };
            return sheet.Show(Page);
        }

        private DuoSheet _sheet;

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
            _sheet?.Close();
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
        public override DuoChrome Chrome => DuoChrome.Plain;

        internal CompetitionHub Hub => _hub == null || _hub.Disposed ? (_hub = CompetitionHub.Acquire()) : _hub;
        internal PhoneShell Shell => Phone;

        public override VisualElement Open()
        {
            Closed();
            _view = CreateView(Hub);
            _view.Shell = Shell;
            return _view.Page;
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
