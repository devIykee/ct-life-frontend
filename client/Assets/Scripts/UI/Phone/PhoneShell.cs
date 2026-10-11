using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
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
    public sealed class PhoneShell : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly VisualElement _frame, _grid, _body;
        private readonly Button _scrim;
        private readonly ScrollView _home;
        private readonly Label _title, _clock;
        private readonly List<PhoneApp> _apps = new List<PhoneApp>();
        private PhoneApp _current;
        private bool _disposed;
        private VisualElement _hiddenHud;
        private StyleEnum<DisplayStyle> _previousHudDisplay;

        public bool IsOpen => _frame.parent != null;
        public int CurrentPhase { get; set; } = 1;

        private readonly Subscriptions _subs = new Subscriptions();

        public PhoneShell(VisualElement phoneLayer)
        {
            _subs.On<DegradedChangedSignal>(_ => { if (IsOpen && _current == null) Home(); });
            _layer = phoneLayer;
            // A native button holds the outside gesture through release before closing.
            // It is mounted beside the inset PhoneLayer so unsafe strips are covered too.
            _scrim = new Button(Close) { text = "", focusable = false, tooltip = Strings.T("phone.close") };
            _scrim.AddToClassList("ct-phone-scrim");
            _title = El.Text("", "ct-phone-title");
            _clock = El.Text("", "ct-phone-clock");
            _grid = El.Div("ct-phone-grid");
            _home = El.VerticalScroll("ct-phone-home");
            _body = El.Div("ct-phone-body");
            var bar = El.Div("ct-phone-bar", El.IconButton("back", Strings.T("common.back"), Back, sfx: null), _title,
                El.Div("ct-grow"), _clock, El.IconButton("close", Strings.T("phone.close"), Close, sfx: null));
            _frame = El.Div("ct-phone", El.Div("ct-phone-handle"), bar, _home, _body, El.Div("ct-phone-home-line"));
            _frame.pickingMode = PickingMode.Position;
        }

        public void Register(PhoneApp app)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PhoneShell));
            app.Attach(this);
            _apps.Add(app);
        }

        public void Open(string appId = null)
        {
            if (_disposed) return;
            if (!IsOpen)
            {
                if (_layer.parent != null)
                    _layer.parent.Insert(_layer.parent.IndexOf(_layer), _scrim);
                else _layer.Add(_scrim);
                _layer.Add(_frame);
                _hiddenHud = UiRoot.Instance != null ? UiRoot.Instance.HudLayer : null;
                if (_hiddenHud != null)
                {
                    _previousHudDisplay = _hiddenHud.style.display;
                    // Restore the state this phone actually hid. The native Unity 6.3 phone review
                    // left resolved display at None after restoring a Null inline value.
                    // Explicit caller-owned None/Flex values remain unchanged.
                    if (_previousHudDisplay.keyword == StyleKeyword.Null)
                        _previousHudDisplay = _hiddenHud.resolvedStyle.display;
                    _hiddenHud.Show(false);
                }
                Bus.Publish(new SfxSignal("sfx_ui_open"));
            }
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
            if (!IsOpen && _current == null) return;
            if (IsOpen) Bus.Publish(new SfxSignal("sfx_ui_close"));
            var current = _current;
            _current = null;
            try { current?.Closed(); }
            finally
            {
                _frame.RemoveFromHierarchy();
                _scrim.RemoveFromHierarchy();
                if (_hiddenHud != null) _hiddenHud.style.display = _previousHudDisplay;
                _hiddenHud = null;
            }
        }

        /// <summary>Owner teardown, distinct from closing and reopening the phone.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Close(); }
            catch (Exception e) { Log.Exception("phone-dispose", e); }
            finally
            {
                _subs.Dispose();
                foreach (var app in _apps)
                {
                    if (!(app is IDisposable disposable)) continue;
                    try { disposable.Dispose(); }
                    catch (Exception e) { Log.Exception("phone-app-dispose", e); }
                }
                _apps.Clear();
                _body.Clear();
                _home.Clear();
                _grid.Clear();
            }
        }

        public void Back()
        {
            if (_disposed) return;
            if (_current != null) { Bus.Publish(new SfxSignal("sfx_ui_close")); Home(); }
            else Close();
        }

        private static string AppTone(string appId) => appId switch
        {
            "map" => "ct-tone-cyan",
            "jobs" => "ct-tone-coral",
            "bank" => "ct-tone-gold",
            "profile" => "ct-tone-violet",
            "home" => "ct-tone-green",
            "rich_list" => "ct-tone-violet",
            "richlist" => "ct-tone-violet",
            _ => "", // Settings and future apps use the shared blue; unavailable apps remain muted by USS.
        };

        private void Home()
        {
            if (_disposed) return;
            _current?.Closed();
            _current = null;
            _title.text = Strings.T("phone.title");
            _body.Clear();
            _body.Show(false);
            _home.Show(true);
            _home.Clear();
            string alias = Services.TryGet<PlayerProfile>(out var profile) ? profile.Alias : null;
            _home.Add(El.Div("ct-phone-welcome", El.Text(string.IsNullOrEmpty(alias) ? Strings.T("phone.greetingDefault") : Strings.F("phone.greeting", alias), "ct-phone-greeting"),
                El.Text(Strings.T("phone.subtitle"), "ct-small ct-muted")));
            _home.Add(El.Text(Strings.T("phone.apps"), "ct-phone-section"));
            _grid.Clear();
            _home.Add(_grid);
            var later = El.Div("ct-phone-grid ct-phone-grid-later");
            foreach (var app in _apps)
            {
                bool open = app.Phase <= CurrentPhase;
                bool available = Features.IsAvailable(app.Feature);
                var a = app;
                var icon = El.Div("ct-app-icon " + AppTone(app.Id), new UiIcon(app.Id));
                var tile = El.Btn("", () =>
                {
                    if (!open) Bus.Publish(ToastSignal.Text(Strings.F("phone.soon", a.Title)));
                    else if (!Features.IsAvailable(a.Feature)) Bus.Publish(ToastSignal.Text(Strings.F("phone.unavailable", a.Title), ToastKind.Bad));
                    else Launch(a);
                }, "ct-app");
                tile.EnableInClassList("ct-app-locked", !open || !available);
                tile.Add(icon);
                tile.Add(El.Text(app.Title, "ct-app-label"));
                if (!open) tile.Add(El.Text(Strings.T("common.soon"), "ct-app-phase"));
                else if (!available) tile.Add(El.Text(Strings.T("common.unavailable"), "ct-app-phase"));
                (open ? _grid : later).Add(tile);
            }
            if (later.childCount > 0)
            {
                _home.Add(El.Text(Strings.T("phone.later"), "ct-phone-section"));
                _home.Add(later);
            }
        }

        private void Launch(PhoneApp app)
        {
            if (_disposed) return;
            if (app.Phase > CurrentPhase || !Features.IsAvailable(app.Feature))
            {
                Home();
                Bus.Publish(ToastSignal.Text(Strings.F(app.Phase > CurrentPhase ? "phone.soon" : "phone.unavailable", app.Title)));
                return;
            }
            _current?.Closed();
            _current = app;
            _title.text = app.Title;
            _home.Show(false);
            _body.Show(true);
            _body.Clear();
            try
            {
                _body.Add(app.Open());
            }
            catch (Exception e)
            {
                Log.Exception("phone", e);
                _body.Add(El.Div("ct-app-page", El.Text(Strings.T("phone.error"), "ct-error"),
                    El.Btn(Strings.T("phone.home"), Home, "ct-btn ct-btn-primary")));
            }
        }


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
        public override VisualElement Open() => El.Div("ct-app-page", El.Text(_title, "ct-h1"), El.Text(Strings.F("phone.soon", _title), "ct-body"));
    }
}
