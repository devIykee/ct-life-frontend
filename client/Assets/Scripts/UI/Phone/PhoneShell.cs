using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>A phone app. Every feature is an app on the phone (Build Spec "What we take").</summary>
    public abstract class PhoneApp
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual string Monogram => Title.Substring(0, Math.Min(2, Title.Length));
        public virtual string Color => "#4b7bec";
        public virtual int Phase => 1;

        /// <summary>Feature key from GET /config `degraded` that disables this app (null = never).</summary>
        public virtual string Feature => null;
        protected PhoneShell Phone { get; private set; }

        internal void Attach(PhoneShell phone) => Phone = phone;

        /// <summary>Builds the app body each time it opens.</summary>
        public abstract VisualElement Open();

        public virtual void Closed() { }
    }

    /// <summary>The in-game phone overlay: home grid of apps, one open app at a time, back button.</summary>
    public sealed class PhoneShell
    {
        private readonly VisualElement _layer;
        private readonly VisualElement _frame, _grid, _body;
        private readonly Label _title, _clock;
        private readonly List<PhoneApp> _apps = new List<PhoneApp>();
        private PhoneApp _current;

        public bool IsOpen => _frame.parent != null;
        public int CurrentPhase { get; set; } = 1;

        private readonly Subscriptions _subs = new Subscriptions();

        public PhoneShell(VisualElement phoneLayer)
        {
            _subs.On<DegradedChangedSignal>(_ => { if (IsOpen && _current == null) Home(); });
            _layer = phoneLayer;
            _title = El.Text("", "ct-phone-title");
            _clock = El.Text("", "ct-phone-clock");
            _grid = El.Div("ct-phone-grid");
            _body = El.Div("ct-phone-body");
            var bar = El.Div("ct-phone-bar", El.Btn("‹", Back, "ct-btn ct-btn-icon"), _title, El.Div("ct-grow"), _clock, El.Btn("✕", Close, "ct-btn ct-btn-icon"));
            _frame = El.Div("ct-phone", bar, _grid, _body);
            _frame.pickingMode = PickingMode.Position;
        }

        public void Register(PhoneApp app)
        {
            app.Attach(this);
            _apps.Add(app);
        }

        public void Open(string appId = null)
        {
            if (!IsOpen) _layer.Add(_frame);
            _clock.text = DateTime.Now.ToString("HH:mm");
            if (appId != null)
            {
                var app = _apps.Find(a => a.Id == appId);
                if (app != null) { Launch(app); return; }
            }
            Home();
        }

        public void Close()
        {
            _current?.Closed();
            _current = null;
            _frame.RemoveFromHierarchy();
        }

        public void Back()
        {
            if (_current != null) Home();
            else Close();
        }

        private void Home()
        {
            _current?.Closed();
            _current = null;
            _title.text = "CT Phone";
            _body.Clear();
            _body.Show(false);
            _grid.Show(true);
            _grid.Clear();
            foreach (var app in _apps)
            {
                bool open = app.Phase <= CurrentPhase;
                bool available = Features.IsAvailable(app.Feature);
                var a = app;
                var icon = El.Div("ct-app-icon", El.Text(app.Monogram, "ct-app-mono"));
                icon.style.backgroundColor = ParseColor(open && available ? app.Color : "#9aa0a6");
                var tile = El.Btn("", () =>
                {
                    if (!open) Bus.Publish(ToastSignal.Text($"{a.Title} opens in Phase {a.Phase}"));
                    else if (!Features.IsAvailable(a.Feature)) Bus.Publish(ToastSignal.Text($"{a.Title} is temporarily unavailable", ToastKind.Bad));
                    else Launch(a);
                }, "ct-app");
                tile.Add(icon);
                tile.Add(El.Text(app.Title, "ct-app-label"));
                if (!open) tile.Add(El.Text("Ph " + app.Phase, "ct-app-phase"));
                else if (!available) tile.Add(El.Text("Unavailable", "ct-app-phase"));
                _grid.Add(tile);
            }
        }

        private void Launch(PhoneApp app)
        {
            _current?.Closed();
            _current = app;
            _title.text = app.Title;
            _grid.Show(false);
            _body.Show(true);
            _body.Clear();
            try
            {
                _body.Add(app.Open());
            }
            catch (Exception e)
            {
                Log.Exception("phone", e);
                _body.Add(El.Text("This app crashed: " + e.Message, "ct-error"));
            }
        }

        private static UnityEngine.Color ParseColor(string hex) =>
            UnityEngine.ColorUtility.TryParseHtmlString(hex, out var c) ? c : UnityEngine.Color.gray;
    }

    /// <summary>A later-phase app: listed on the home grid now, built in its phase.</summary>
    public sealed class ComingApp : PhoneApp
    {
        private readonly string _id, _title, _color;
        private readonly int _phase;

        private readonly string _feature;

        public ComingApp(string id, string title, int phase, string color, string feature = null)
        {
            _id = id;
            _title = title;
            _phase = phase;
            _color = color;
            _feature = feature;
        }

        public override string Feature => _feature;

        public override string Id => _id;
        public override string Title => _title;
        public override int Phase => _phase;
        public override string Color => _color;
        public override VisualElement Open() => El.Div("ct-app-page", El.Text(_title, "ct-h1"), El.Text($"Arrives in Phase {_phase}.", "ct-body"));
    }
}
