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
        /// <summary>The name players see: names.json `app.&lt;id&gt;` when present (one table for every name), else <see cref="Title"/>.</summary>
        public string DisplayTitle => NameTable.Get("app." + Id, Title);
        public virtual string Monogram => Title.Substring(0, Math.Min(2, Title.Length));
        /// <summary>Short name under the home-screen icon (iOS labels are one short word); the app title stays.</summary>
        public virtual string HomeLabel => Title;
        public virtual string Color => "#4b7bec";
        public virtual int Phase => 1;
        /// <summary>False for routes that only other apps open (e.g. "repay" from the Wallet's Loan card): no launcher tile.</summary>
        public virtual bool ShowOnHome => true;

        /// <summary>Feature key from GET /config `degraded` that disables this app (null = never).</summary>
        public virtual string Feature => null;
        protected PhoneShell Phone { get; private set; }

        internal void Attach(PhoneShell phone) => Phone = phone;

        /// <summary>Builds the app body each time it opens.</summary>
        public abstract VisualElement Open();

        public virtual void Closed() { }

        /// <summary>
        /// Legacy apps keep the shell's title bar. Duo-kit apps draw their own DuoNav and large title; Grouped also
        /// paints the status bar iOS grouped grey (Settings).
        /// </summary>
        public virtual DuoChrome Chrome => DuoChrome.Legacy;
    }

    public enum DuoChrome { Legacy, Plain, Grouped }

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
        private readonly Action<DuoTextSize> _onTextSize;
        private readonly Action _onBadges;

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
            _home = new PhoneLauncher(CreateAppTile, id =>
            {
                var app = _apps.Find(a => a.Id == id);
                if (app != null) Launch(app);
            });
            _body = El.Div("ct-phone-body");
            _back = SystemButton("back", Strings.T("common.back"), Back);
            var bar = El.Div("ct-phone-bar", _back, _title,
                El.Div("ct-grow"), SystemButton("close", Strings.T("phone.close"), Close));
            var status = El.Div("ct-phone-status", _clock,
                El.PassThrough(El.Div("ct-phone-camera")),
                El.PassThrough(El.Div("ct-phone-device-name", El.Div("ct-phone-solana-mark"),
                    El.Text(Strings.T("phone.device_model"), "ct-phone-device-label"))));
            // The home indicator is a real, generously sized button; decoration never intercepts input.
            _homeControl = El.Btn("", () =>
            {
                if (_current != null) Home();
                else if (_home.IsSearching) _home.CloseSearch();
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
            _clockTick = _frame.schedule.Execute(() =>
            {
                _clock.text = DateTime.Now.ToString("HH:mm");
                if (_current == null) _home.RefreshWidgets();
            }).Every(1000);
            _clockTick.Pause();
            UnityEngine.Application.focusChanged += OnFocusChanged;
            DuoText.Apply(_frame, DuoText.Current);
            _onTextSize = size => DuoText.Apply(_frame, size);
            DuoText.Changed += _onTextSize;
            _onBadges = RefreshBadges;
            DuoNotificationCenter.Queue.BadgesChanged += _onBadges;
        }

        private void OnFocusChanged(bool focused)
        {
            if (focused && !_disposed && IsOpen) _home.RefreshGlass();
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

        /// <summary>Registered title for an app id (Settings lists banner sources by name); null when unknown.</summary>
        public string AppTitle(string appId) => _apps.Find(a => a.Id == appId)?.Title;

        /// <summary>The app in front, for banner suppression (null at home or when closed).</summary>
        public string ForegroundAppId => IsOpen ? _current?.Id : null;

        private void ReportForeground() => DuoNotificationCenter.Queue.SetForeground(IsOpen, ForegroundAppId);

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
                ReportForeground();
            }
        }

        /// <summary>Owner teardown, distinct from closing and reopening the phone.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnityEngine.Application.focusChanged -= OnFocusChanged;
            DuoText.Changed -= _onTextSize;
            DuoNotificationCenter.Queue.BadgesChanged -= _onBadges;
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
            else if (_home.IsSearching) _home.CloseSearch();
            else Close();
        }

        private static Button SystemButton(string icon, string tooltip, Action action)
        {
            var button = new Button(action) { tooltip = tooltip };
            button.AddToClassList("ct-phone-system-button");
            button.Add(new PhoneSystemIcon(icon));
            return button;
        }

        private void Home()
        {
            if (_disposed) return;
            _current?.Closed();
            _current = null;
            SetChrome(DuoChrome.Legacy);
            _frame.AddToClassList("ct-phone-at-home");
            _back.Show(false);
            _title.text = Strings.T("phone.title");
            _body.Clear();
            _body.Show(false);
            _home.Show(true);
            _home.SetApps(_apps, CurrentPhase);
            UpdateHomeTooltip();
            RefreshBadges();
            ReportForeground();
        }

        private void SetChrome(DuoChrome chrome)
        {
            _frame.EnableInClassList("ct-phone-app-chrome", chrome != DuoChrome.Legacy);
            _frame.EnableInClassList("ct-phone-grouped", chrome == DuoChrome.Grouped);
        }

        /// <summary>Red counts on launcher icons (dock and grid) from the notification center.</summary>
        private void RefreshBadges()
        {
            if (_disposed) return;
            foreach (var badge in _frame.Query<Label>(className: "ct-app-badge").ToList())
                Duo.SetBadge(badge, DuoNotificationCenter.Badge(badge.userData as string));
        }

        private void UpdateHomeTooltip() => _homeControl.tooltip = Strings.T(_current == null && _home.PageIndex == 0 ? "phone.close" : "phone.home");

        private Button CreateAppTile(PhoneApp app)
        {
            bool open = app.Phase <= CurrentPhase;
            bool available = Features.IsAvailable(app.Feature);
            var badge = Duo.Badge(DuoNotificationCenter.Badge(app.Id), "ct-app-badge");
            badge.userData = app.Id;
            badge.name = "badge-" + app.Id;
            // Two very soft planes under the tile; the artwork carries its own rim and sheen.
            var icon = El.Div("ct-app-icon", El.Div("ct-app-icon-shadow ct-app-icon-shadow-far"),
                El.Div("ct-app-icon-shadow ct-app-icon-shadow-near"), new PhoneAppIcon(app.Id), badge);
            var tile = El.Btn("", () =>
            {
                if (_home.SuppressClick) return;
                if (app.Phase > CurrentPhase) Bus.Publish(ToastSignal.Text(Strings.F("phone.soon", app.DisplayTitle)));
                else if (!Features.IsAvailable(app.Feature)) Bus.Publish(ToastSignal.Text(Strings.F("phone.unavailable", app.DisplayTitle), ToastKind.Bad));
                else Launch(app);
            }, "ct-app");
            tile.name = "phone-app-" + app.Id;
            tile.tooltip = app.DisplayTitle + (!open ? " · " + Strings.T("common.soon") : !available ? " · " + Strings.T("common.unavailable") : "");
            tile.EnableInClassList("ct-app-locked", !open || !available);
            tile.Add(El.PassThrough(icon));
            tile.Add(El.PassThrough(El.Text(NameTable.Get("app." + app.Id, NameTable.Apply(app.HomeLabel)), "ct-app-label")));
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
                Bus.Publish(ToastSignal.Text(Strings.F(app.Phase > CurrentPhase ? "phone.soon" : "phone.unavailable", app.DisplayTitle)));
                return;
            }
            _current?.Closed();
            _current = app;
            ReportForeground();
            _frame.RemoveFromClassList("ct-phone-at-home");
            SetChrome(app.Chrome);
            _back.Show(true);
            _title.text = app.DisplayTitle;
            _homeControl.tooltip = Strings.T("phone.home");
            _home.Suspend();
            _home.Show(false);
            _body.Show(true);
            _body.Clear();
            try
            {
                var page = app.Open();
                var heading = page.Q<Label>(className: "ct-h1");
                if (heading != null && heading.text == app.DisplayTitle)
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
