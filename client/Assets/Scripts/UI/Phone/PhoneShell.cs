using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI.Kit;
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
        private readonly VisualElement _frame, _body;
        private readonly Button _scrim, _back, _homeControl;
        private readonly PhoneLauncher _home;
        private readonly Label _title, _clock;
        private readonly List<PhoneApp> _apps = new List<PhoneApp>();
        private PhoneApp _current;
        private bool _disposed;
        private VisualElement _hiddenHud;
        private StyleEnum<DisplayStyle> _previousHudDisplay;
        private readonly IVisualElementScheduledItem _clockTick;
        private Tween _entrance;

        public bool IsOpen => _frame.parent != null;
        /// <summary>Config-driven (GET /config livePhase → EnvConfig.livePhase). Not a constant.</summary>
        public int CurrentPhase => Phases.Live;

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
            _home = new PhoneLauncher(CreateAppTile);
            _body = El.Div("ct-phone-body");
            _back = El.IconButton("back", Strings.T("common.back"), Back, sfx: null);
            var bar = El.Div("ct-phone-bar", _back, _title,
                El.Div("ct-grow"), El.IconButton("close", Strings.T("phone.close"), Close, sfx: null));
            var status = El.Div("ct-phone-status", _clock,
                El.PassThrough(El.Div("ct-phone-camera")),
                El.PassThrough(El.Div("ct-phone-device-name", El.Text(Strings.T("phone.title"), "ct-phone-device-label"),
                    El.Div("ct-phone-emblem", El.Div("ct-phone-stripe"), El.Div("ct-phone-stripe ct-phone-stripe-middle"), El.Div("ct-phone-stripe")))));
            // The home indicator is a real, generously sized button; decoration never intercepts input.
            _homeControl = El.Btn("", () =>
            {
                if (_current != null) Home();
                else if (_home.PageIndex > 0) _home.ShowPage(0);
                else Close();
            }, "ct-phone-home-control");
            _home.PageChanged += UpdateHomeTooltip;
            _homeControl.name = "phone-home";
            _homeControl.tooltip = Strings.T("phone.home");
            _homeControl.Add(El.PassThrough(El.Div("ct-phone-home-line")));
            var glass = El.Div("ct-phone-glass", new PhoneWallpaper(), status, bar, _home, _body, _homeControl);
            _frame = El.Div("ct-phone",
                El.PassThrough(El.Div("ct-phone-hinge ct-phone-hinge-top")),
                El.PassThrough(El.Div("ct-phone-hinge ct-phone-hinge-bottom")),
                El.PassThrough(El.Div("ct-phone-side-key")), glass);
            _frame.pickingMode = PickingMode.Position;
            _clockTick = _frame.schedule.Execute(() => _clock.text = DateTime.Now.ToString("HH:mm")).Every(1000);
            _clockTick.Pause();
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
                _entrance?.Cancel();
                _entrance = Motion.Enter(_frame, 56f, 0f, 0.24f);
                _clockTick.Resume();
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
                _entrance?.Cancel();
                _entrance = null;
                _clockTick.Pause();
                _home.Suspend();
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
            "wallet" => "ct-tone-green",
            "profile" => "ct-tone-violet",
            "home" => "ct-tone-green",
            "rich_list" => "ct-tone-violet",
            "richlist" => "ct-tone-violet",
            "messages" => "ct-tone-cyan",
            "calls" => "ct-tone-green",
            "spaces" => "ct-tone-violet",
            "shop" => "ct-tone-coral",
            "ads" => "ct-tone-gold",
            _ => "", // Settings and future apps use the shared blue; unavailable apps remain muted by USS.
        };

        // Catalog route IDs stay unchanged. These aliases reuse the existing bundled Phosphor set.
        private static string AppIcon(string appId) => appId switch
        {
            "week" => "rotate",      // Weekly reset / recurring city events.
            "biz" => "city",         // Businesses and their premises.
            "builder" => "projects", // Found and launch a project.
            "vc" => "cash",          // Venture funding and deal terms.
            "beef" => "mic_stage",   // Public verbal showdowns.
            "farm" => "dream",       // Daily airdrop rewards.
            "cabal" => "contacts",   // Player group.
            "npcchat" => "chat",     // Conversation with an NPC.
            "xlink" => "face",       // Linked social identity; not a misleading close/X button.
            _ => appId,
        };

        private void Home()
        {
            if (_disposed) return;
            _current?.Closed();
            _current = null;
            _frame.AddToClassList("ct-phone-at-home");
            _back.Show(false);
            _title.text = Strings.T("phone.title");
            _body.Clear();
            _body.Show(false);
            _home.Show(true);
            _home.SetApps(_apps, CurrentPhase);
            UpdateHomeTooltip();
        }

        private void UpdateHomeTooltip() => _homeControl.tooltip = Strings.T(_current == null && _home.PageIndex == 0 ? "phone.close" : "phone.home");

        private Button CreateAppTile(PhoneApp app)
        {
            bool open = app.Phase <= CurrentPhase;
            bool available = Features.IsAvailable(app.Feature);
            var icon = El.Div("ct-app-icon " + AppTone(app.Id), new UiIcon(AppIcon(app.Id)));
            var tile = El.Btn("", () =>
            {
                if (_home.SuppressClick) return;
                if (app.Phase > CurrentPhase) Bus.Publish(ToastSignal.Text(Strings.F("phone.soon", app.Title)));
                else if (!Features.IsAvailable(app.Feature)) Bus.Publish(ToastSignal.Text(Strings.F("phone.unavailable", app.Title), ToastKind.Bad));
                else Launch(app);
            }, "ct-app");
            tile.name = "phone-app-" + app.Id;
            tile.tooltip = app.Title + (!open ? " · " + Strings.T("common.soon") : !available ? " · " + Strings.T("common.unavailable") : "");
            tile.EnableInClassList("ct-app-locked", !open || !available);
            tile.Add(El.PassThrough(icon));
            tile.Add(El.PassThrough(El.Text(app.Title, "ct-app-label")));
            if (!open) tile.Add(El.PassThrough(El.Text(Strings.T("common.soon"), "ct-app-phase")));
            else if (!available) tile.Add(El.PassThrough(El.Text(Strings.T("common.unavailable"), "ct-app-phase")));
            return tile;
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
            _frame.RemoveFromClassList("ct-phone-at-home");
            _back.Show(true);
            _title.text = app.Title;
            _homeControl.tooltip = Strings.T("phone.home");
            _home.Suspend();
            _home.Show(false);
            _body.Show(true);
            _body.Clear();
            try
            {
                var page = app.Open();
                var heading = page.Q<Label>(className: "ct-h1");
                if (heading != null && heading.text == app.Title)
                    heading.AddToClassList("ct-phone-repeated-title");
                _body.Add(page);
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
