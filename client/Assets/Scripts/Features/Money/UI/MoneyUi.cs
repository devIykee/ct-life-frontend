using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// UI Toolkit builders for the money screens on the Duo kit: sections on the canvas, inset key/value rows, capsule buttons,
    /// filter chips, kit field and toggle, tinted notices. Plain text only. (The Rekt card overlay passes its own classes.)
    /// </summary>
    internal static class MoneyUi
    {
        public const string Primary = "primary", Plain = "plain", Danger = "danger";

        public static ScrollView Page() => El.VerticalScroll();

        public static Label Text(string text, string cls = "duo-comp-text")
        {
            var l = El.Text(text ?? "", cls);
            l.enableRichText = false;
            return l;
        }

        public static Label Small(string text) => Text(text, "duo-comp-note");
        public static Label H2(string text) => Text(text, "duo-comp-h2");
        public static Label Label(string text) => Text((text ?? "").ToUpperInvariant(), "duo-comp-label");

        public static VisualElement Row(params VisualElement[] children) => El.Div("duo-comp-actions", children);

        /// <summary>A section on the canvas (no box).</summary>
        public static VisualElement Card(params VisualElement[] children) => El.Div("duo-comp-section", children);

        /// <summary>Key on the left, value on the right, inset hairline below.</summary>
        public static VisualElement Pair(string key, string value, string valueCls = null)
        {
            var v = Text(value, "duo-comp-pair-value" + (string.IsNullOrEmpty(valueCls) ? "" : " " + valueCls));
            return El.Div("duo-comp-pair", Text(key, "duo-comp-pair-key"), v);
        }

        public static string PnlClass(long cents) => cents > 0 ? "duo-value-positive" : cents < 0 ? "duo-value-negative" : "";

        public static Label Pnl(long cents, string prefixKey = null)
        {
            var text = MoneyFormat.SignedCash(cents);
            return Text(prefixKey == null ? text : MoneyStrings.F(prefixKey, text), "duo-money-value " + PnlClass(cents));
        }

        /// <summary>Tinted inline note (kit notice look) as one label so callers can update its text.</summary>
        public static Label Notice(string text, string kind = "info")
        {
            string tone = kind == "warn" ? "warning" : kind == "error" ? "danger" : kind == "good" ? "success" : "info";
            return Text(text, "duo-money-notice duo-money-notice-" + tone);
        }

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

        public static TextField Field(string label, string value, Action<string> changed)
        {
            var f = new TextField { value = value ?? "", isDelayed = false };
            f.AddToClassList("duo-field");
            f.textEdition.placeholder = label ?? "";
            f.tooltip = label;
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
        }

        /// <summary>A required acknowledgement (mainnet confirmations) as a kit toggle row.</summary>
        public static VisualElement Ack(string label, Action<bool> changed)
        {
            var t = new DuoToggle(false, v => changed?.Invoke(v));
            var row = t.InRow(label);
            row.AddToClassList("duo-money-ack");
            return row;
        }

        public static void Copy(string text)
        {
            GUIUtility.systemCopyBuffer = text ?? "";
            Bus.Publish(ToastSignal.Text(MoneyStrings.T("common.copied")));
        }

        public static string Age(double seconds) => double.IsNaN(seconds) ? MoneyStrings.T("common.notUpdated") : MoneyStrings.F("common.updated", MoneyFormat.Ago(seconds));
    }

    /// <summary>A row of filter chips that selects one key (kit duo-chip).</summary>
    internal sealed class ChipTabs
    {
        public readonly VisualElement Root = El.Div("duo-chip-row duo-money-tabs");
        private readonly Dictionary<string, Button> _chips = new Dictionary<string, Button>();
        private readonly Action<string> _select;
        public string Current { get; private set; }

        public ChipTabs(Action<string> select) { _select = select; }

        public ChipTabs Add(string key, string label)
        {
            var b = El.Btn(label, () => Select(key), "duo-chip");
            _chips[key] = b;
            Root.Add(b);
            return this;
        }

        public void Select(string key, bool notify = true)
        {
            Current = key;
            foreach (var kv in _chips) kv.Value.EnableInClassList("duo-chip-on", kv.Key == key);
            if (notify) _select?.Invoke(key);
        }

        public void SetEnabled(string key, bool on)
        {
            if (_chips.TryGetValue(key, out var b)) b.SetEnabled(on);
        }
    }

    /// <summary>Close-price line chart for candles (server micros), drawn with Painter2D. Display only.</summary>
    internal sealed class Sparkline : VisualElement
    {
        private IReadOnlyList<CandleRow> _points = Array.Empty<CandleRow>();

        public Sparkline()
        {
            AddToClassList("duo-money-chart");
            generateVisualContent += Draw;
        }

        public void SetData(IReadOnlyList<CandleRow> points)
        {
            _points = points ?? Array.Empty<CandleRow>();
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (_points.Count < 2 || rect.width < 4 || rect.height < 4) return;
            long min = long.MaxValue, max = long.MinValue;
            foreach (var p in _points)
            {
                min = Math.Min(min, p.Low);
                max = Math.Max(max, p.High);
            }
            double span = Math.Max(1, max - min);
            var painter = ctx.painter2D;
            bool up = _points[_points.Count - 1].Close >= _points[0].Close;
            painter.strokeColor = up ? new Color(0.188f, 0.78f, 0.369f) : new Color(1f, 0.231f, 0.188f);
            painter.lineWidth = 4f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            for (int i = 0; i < _points.Count; i++)
            {
                float x = rect.xMin + rect.width * i / (_points.Count - 1);
                float y = rect.yMax - (float)((_points[i].Close - min) / span) * rect.height;
                if (i == 0) painter.MoveTo(new Vector2(x, y));
                else painter.LineTo(new Vector2(x, y));
            }
            painter.Stroke();
        }
    }

    /// <summary>
    /// Base for one opened money screen: owns a ScrollView, a lifetime token, a 4 Hz tick while attached, and disposal on
    /// detach, phone close or a change of signed-in identity. Async work is guarded so a late reply never touches a
    /// disposed view.
    /// </summary>
    internal abstract class MoneyView : IDisposable
    {
        public readonly ScrollView Root;
        protected readonly MoneyHub Hub;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Session _session;
        private readonly string _user;
        private readonly IVisualElementScheduledItem _tick;
        protected readonly Subscriptions Subs = new Subscriptions();
        protected bool Disposed { get; private set; }
        protected CancellationToken Life => _life.Token;
        protected static double Now => Time.realtimeSinceStartupAsDouble;

        protected MoneyView(MoneyHub hub)
        {
            Hub = hub;
            Root = MoneyUi.Page();
            _session = Services.Get<Session>();
            _user = _session.UserId;
            _session.Changed += SessionChanged;
            Root.RegisterCallback<DetachFromPanelEvent>(Detached);
            _tick = Root.schedule.Execute(OnTick).Every(250);
        }

        protected bool Alive => !Disposed && !Hub.Disposed && _session.IsSignedIn && _session.UserId == _user;

        /// <summary>The Duo that shows this view (nav back); set by MoneyApp after creation, null in tests.</summary>
        public PhoneShell Phone { get; internal set; }

        /// <summary>What the phone shows: the scroll view, or a Duo frame around it (nav, bottom bar).</summary>
        public virtual VisualElement Container => (VisualElement)_frame ?? Root;

        private DuoPage _frame;
        private DuoSheet _sheet;

        /// <summary>Wraps <see cref="Root"/> in the Duo frame (large title, back circle) and makes it the container.</summary>
        protected DuoPage Framed(string title, string name, bool largeTitle = true)
        {
            _frame = new DuoPage(Root, title, largeTitle);
            _frame.name = name;
            _frame.AddToClassList("duo-comp");
            _frame.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => { if (!Back()) Phone?.Back(); }, name + "-back");
            return _frame;
        }

        /// <summary>Nav back inside the screen (coin page → list). False lets the phone go back.</summary>
        protected virtual bool Back() => false;

        /// <summary>Shows a bottom sheet over this screen; closed with the view.</summary>
        protected DuoSheet ShowSheet(DuoSheet sheet)
        {
            _sheet?.Close();
            _sheet = sheet;
            sheet.Scrolling();
            sheet.Closed += () => { if (_sheet == sheet) _sheet = null; };
            return sheet.Show(Container);
        }

        private void SessionChanged()
        {
            if (!Alive) Dispose();
        }

        private void Detached(DetachFromPanelEvent e)
        {
            if (e.target == Root) Dispose();
        }

        private void OnTick()
        {
            if (!Alive) return;
            try { Tick(); }
            catch (Exception e) { Log.Exception("money", e); }
        }

        /// <summary>Called 4 times a second while the view is attached.</summary>
        protected virtual void Tick() { }

        /// <summary>
        /// Runs async work bound to this view. <paramref name="done"/> runs on success and <paramref name="fail"/> on a
        /// classified failure, both only while the view is alive.
        /// </summary>
        protected async void Run<T>(Func<CancellationToken, Task<T>> work, Action<T> done, Action<MoneyError> fail, bool write)
        {
            if (!Alive) return;
            T result;
            try
            {
                result = await work(Life);
            }
            catch (OperationCanceledException) when (Life.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                if (!Alive) return;
                var err = MoneyErrors.From(e, write);
                Log.Warn("money", (write ? "write" : "read") + " failed: " + err.Code);
                try { fail?.Invoke(err); }
                catch (Exception inner) { Log.Exception("money", inner); }
                return;
            }
            if (!Alive) return;
            try { done?.Invoke(result); }
            catch (Exception e) { Log.Exception("money", e); }
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
        }
    }

    /// <summary>Base phone app for the money features: holds the shared hub and one open view at a time.</summary>
    public abstract class MoneyApp : PhoneApp, IDisposable
    {
        private MoneyHub _hub;
        private MoneyView _view;

        /// <summary>Acquires the shared hub at registration so `payment_confirmed` is handled while no money app is open.</summary>
        protected MoneyApp()
        {
            try { _hub = MoneyHub.Acquire(); }
            catch (Exception e) { Log.Warn("money", "Hub deferred: " + e.Message); }
        }

        internal MoneyHub Hub => _hub == null || _hub.Disposed ? (_hub = MoneyHub.Acquire()) : _hub;
        internal PhoneShell Shell => Phone;
        public override DuoChrome Chrome => DuoChrome.Plain;

        public override VisualElement Open()
        {
            Closed();
            _view = CreateView(Hub);
            _view.Phone = Phone;
            return _view.Container;
        }

        internal abstract MoneyView CreateView(MoneyHub hub);

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
