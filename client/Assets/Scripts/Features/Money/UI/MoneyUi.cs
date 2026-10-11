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
    /// <summary>Small UI Toolkit builders on top of CTLife.UI's El and the existing ct-* theme. Functional, unstyled beyond the theme.</summary>
    internal static class MoneyUi
    {
        private static StyleSheet _sheet;

        public static ScrollView Page()
        {
            var s = El.VerticalScroll("ct-app-page ct-money");
            if (_sheet == null) _sheet = Resources.Load<StyleSheet>("UI/Money/money");
            if (_sheet != null) s.styleSheets.Add(_sheet);
            return s;
        }

        public static Label Text(string text, string cls = "ct-body")
        {
            var l = El.Text(text ?? "", cls);
            l.enableRichText = false;
            return l;
        }

        public static Label Small(string text) => Text(text, "ct-small ct-muted");

        public static VisualElement Row(params VisualElement[] children) => El.Div("ct-row ct-money-row", children);

        public static VisualElement Card(params VisualElement[] children) => El.Div("ct-card ct-money-card", children);

        /// <summary>A muted label on the left, a value on the right.</summary>
        public static VisualElement Pair(string key, string value, string valueCls = null)
        {
            var v = Text(value, "ct-money-value" + (string.IsNullOrEmpty(valueCls) ? "" : " " + valueCls));
            return El.Div("ct-row ct-money-pair", Text(key, "ct-money-key"), El.Div("ct-grow"), v);
        }

        public static string PnlClass(long cents) => cents > 0 ? "ct-money-up" : cents < 0 ? "ct-money-down" : "";

        public static Label Pnl(long cents, string prefixKey = null)
        {
            var text = MoneyFormat.SignedCash(cents);
            return Text(prefixKey == null ? text : MoneyStrings.F(prefixKey, text), "ct-money-value " + PnlClass(cents));
        }

        public static Label Notice(string text, string kind = "info") => Text(text, "ct-money-notice ct-money-notice-" + kind);

        public static Button Btn(string text, Action onClick, string cls = "ct-btn ct-btn-primary") => El.Btn(text, onClick, cls);

        public static Button Chip(string text, Action onClick, bool on)
        {
            var b = PhonePresentation.SizeToText(El.Btn(text, onClick, "ct-chip ct-money-chip"));
            b.EnableInClassList("ct-chip-on", on);
            return b;
        }

        public static TextField Field(string label, string value, Action<string> changed)
        {
            var f = new TextField(label) { value = value ?? "", isDelayed = false };
            f.AddToClassList("ct-field");
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
        }

        /// <summary>A required acknowledgement toggle (mainnet confirmations).</summary>
        public static Toggle Ack(string label, Action<bool> changed)
        {
            var t = new Toggle(label) { value = false };
            t.AddToClassList("ct-toggle");
            t.AddToClassList("ct-money-ack");
            t.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return t;
        }

        public static void Copy(string text)
        {
            GUIUtility.systemCopyBuffer = text ?? "";
            Bus.Publish(ToastSignal.Text(MoneyStrings.T("common.copied")));
        }

        public static string Age(double seconds) => double.IsNaN(seconds) ? MoneyStrings.T("common.notUpdated") : MoneyStrings.F("common.updated", MoneyFormat.Ago(seconds));
    }

    /// <summary>A row of chips that selects one key.</summary>
    internal sealed class ChipTabs
    {
        public readonly VisualElement Root = El.Div("ct-app-tabs ct-money-tabs");
        private readonly Dictionary<string, Button> _chips = new Dictionary<string, Button>();
        private readonly Action<string> _select;
        public string Current { get; private set; }

        public ChipTabs(Action<string> select) { _select = select; }

        public ChipTabs Add(string key, string label)
        {
            var b = PhonePresentation.SizeToText(El.Btn(label, () => Select(key), "ct-chip ct-money-chip"));
            _chips[key] = b;
            Root.Add(b);
            return this;
        }

        public void Select(string key, bool notify = true)
        {
            Current = key;
            foreach (var kv in _chips) kv.Value.EnableInClassList("ct-chip-on", kv.Key == key);
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
            AddToClassList("ct-money-chart");
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
            painter.strokeColor = up ? new Color(0.075f, 0.467f, 0.329f) : new Color(0.765f, 0.259f, 0.345f);
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
        public virtual VisualElement Container => Root;

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
