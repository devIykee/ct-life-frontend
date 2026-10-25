using System;
using CTLife.Core;
using CTLife.UI;
using CTLife.UI.Kit;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// One Rug Room service per signed-in player and the casino floor's hook (<see cref="CasinoHooks.Open"/>): the machines on
    /// the floor and the Duo app open <see cref="RugRoomSheet"/>. Created by FlowController next to the other overlays.
    /// </summary>
    public sealed class CasinoOverlays : IDisposable
    {
        private readonly Session _session;
        private CasinoService _svc;
        private bool _disposed;

        public CasinoOverlays()
        {
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            CasinoHooks.Open = game => { if (!_disposed) RugRoomSheet.Show(game); };
            CasinoHooks.FloorChanged += OnFloor;
            Bind();
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            RugRoomSheet.CloseOpen();
            _svc?.Dispose();
            _svc = string.IsNullOrEmpty(user) ? null : new CasinoService(user);
        }

        private void OnFloor(bool on)
        {
            if (on) RugRoomSheet.PrewarmAssets();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            CasinoHooks.FloorChanged -= OnFloor;
            CasinoHooks.Open = null;
            _svc?.Dispose();
            _svc = null;
        }
    }

    /// <summary>
    /// The Rug Room in the Duo phone: the shortcut to the casino from anywhere. The neon wordmark, the disclaimer, today's
    /// room under the caps and the three games; a game closes the phone and opens the casino at that table. Walking in through
    /// the pavilion's door on the Boardwalk pier is the other way in.
    /// </summary>
    public sealed class RugRoomApp : CityApp
    {
        public const string AppId = "rugroom";
        public override string Id => AppId;
        public override string Title => CasinoText.T("app.title");
        public override string HomeLabel => CasinoText.T("app.label");
        public override string Color => "#8a1538";

        internal override CityView CreateView(CityLifeHub hub)
        {
            RugRoomSheet.PrewarmAssets();
            return new View(hub);
        }

        private sealed class View : CityView
        {
            private readonly CasinoService _svc;
            private Label _today;

            public View(CityLifeHub hub) : base(hub, CasinoText.T("app.title"))
            {
                _svc = CasinoService.Current;
                Page.name = "rugroom-page";
                var logo = El.Div();
                logo.style.height = 150; logo.style.marginTop = 6; logo.style.marginBottom = 6;
                logo.style.backgroundColor = new Color(0.08f, 0.02f, 0.06f);
                logo.style.borderTopLeftRadius = logo.style.borderTopRightRadius = logo.style.borderBottomLeftRadius = logo.style.borderBottomRightRadius = 22;
                var tex = RugRoomSheet.Art("UI/Casino/rugroom_logo");
                if (tex != null) { logo.style.backgroundImage = new StyleBackground(tex); logo.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit; }
                Body.Add(logo);
                Body.Add(CityUi.Small(CasinoText.T("app.tagline")));
                if (_svc == null) { Body.Add(Duo.Empty(CityLifeStrings.T("common.notLive"))); return; }
                Body.Add(Duo.SectionHeader(CasinoText.T("app.today")));
                _today = CityUi.Text("");
                Body.Add(_today);
                Body.Add(Duo.SectionHeader("Games"));
                foreach (var (game, title, icon) in new[] { (CasinoHooks.Slots, "Slots", "rocket"), (CasinoHooks.Crash, "Moon or Rug", "rug"), (CasinoHooks.Coinflip, "Coinflip", "diamond_hands") })
                {
                    var g = game;
                    var row = RugRoomSheet.Btn("", () => Open(g), "duo-comp-pair");
                    row.name = "rugroom-" + g;
                    row.style.backgroundColor = new Color(0, 0, 0, 0); row.style.borderTopWidth = row.style.borderLeftWidth = row.style.borderRightWidth = 0;
                    row.style.paddingLeft = 0; row.style.height = 76;
                    row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                    var art = El.Div();
                    art.style.width = 56; art.style.height = 56; art.style.marginRight = 14;
                    var st = RugRoomSheet.Symbol(icon);
                    if (st != null) { art.style.backgroundImage = new StyleBackground(st); art.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit; }
                    row.Add(art);
                    var label = CityUi.Text(title, "duo-comp-pair-key"); label.style.unityTextAlign = TextAnchor.MiddleLeft; label.style.flexGrow = 1; row.Add(label); row.Add(CityUi.Text("›", "duo-comp-pair-value"));
                    foreach (var e in row.Children()) e.pickingMode = PickingMode.Ignore;
                    Body.Add(row);
                }
                var enter = CityUi.Btn(CasinoText.T("app.enter"), () => Open(CasinoHooks.Lobby));
                enter.name = "rugroom-enter";
                Body.Add(CityUi.Row(enter));
                Body.Add(CityUi.Small(CasinoText.T("app.walk")));
                var disc = CityUi.Small(_svc.Disclaimer);
                disc.name = "rugroom-disclaimer";
                disc.style.marginTop = 18;
                Body.Add(disc);
                _svc.StateChanged += Render;
                Render();
                _ = _svc.RefreshState(force: false);
            }

            private void Render()
            {
                if (!Alive || _today == null) return;
                var s = _svc.State;
                _today.text = s == null ? (_svc.StateError ?? CasinoText.T("gate.loading"))
                    : !s.AgeConfirmed ? CasinoText.T("gate.ageTitle")
                    : s.Excluded(ServerClock.NowMs) ? CasinoText.F("lim.done", CasinoText.Date(s.SelfExcludedUntilMs.Value))
                    : CasinoText.F("hud.left", CasinoText.Money(Math.Max(0, s.StakeRoomCents)));
            }

            private void Open(string game)
            {
                Shell?.Close();
                RugRoomSheet.Show(game);
            }

            public override void Dispose()
            {
                if (_svc != null) _svc.StateChanged -= Render;
                base.Dispose();
            }
        }
    }
}
