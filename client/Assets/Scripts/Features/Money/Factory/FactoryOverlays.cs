using System;
using CTLife.Core;
using CTLife.Net;
using CTLife.World;
using UnityEngine;
using CoreFeatures = CTLife.Core.Features;

namespace CTLife.Economy
{
    /// <summary>
    /// The Launchpad Tower's launch desk on CT Island (backend #14 hookup): within <see cref="Range"/> of the desk spot
    /// (contract/ids/spots.json `launchpad_tower.launch_desk`, placed with the island venue's pose) the HUD prompt offers
    /// "Launch a coin" with the live allowance under it (fee and launches left, the cooldown or the daily cap, from
    /// `/factory/me`), plus a smaller "Board" key. Both open <see cref="FactoryHooks.Open"/>. Walking into the tower
    /// refreshes the allowance (at most every 30 s). Before the markets open (livePhase &lt; 2) the prompt is dimmed.
    /// </summary>
    public sealed class LaunchpadDesk : IDisposable
    {
        public const float Range = 2.6f, WarmRange = 40f;
        public const string OfferId = "factory:desk";

        private readonly FactoryService _svc;
        private readonly Func<Vector3, WorldOffer> _source;
        private readonly WorldOffer _offer = new WorldOffer();
        private IslandWorld _world;
        private Vector3? _desk;
        private float _nextCaption;
        private bool _disposed;

        public LaunchpadDesk(FactoryService svc)
        {
            _svc = svc;
            _source = Offer;
            WorldOffers.Add(_source);
        }

        public static bool MarketsOpen => Phases.IsOpen(2) && CoreFeatures.IsAvailable(CoreFeatures.Markets);

        /// <summary>The desk and the direction from it into the room (venue-local -z), or false before the island has it.</summary>
        public static bool DeskPose(IslandWorld world, out Vector3 desk, out Vector3 towardRoom)
        {
            desk = default; towardRoom = Vector3.back;
            var p = DeskPosition(world);
            var v = world != null ? world.Find(FactoryHooks.VenueId) : null;
            if (p == null || v == null) return false;
            desk = p.Value;
            var a = v.ToIsland(FactoryHooks.DeskLocalFallback);
            var b = v.ToIsland(FactoryHooks.DeskLocalFallback + new Vector2(0f, -1f));
            towardRoom = new Vector3(b.x - a.x, 0f, b.y - a.y).normalized;
            return true;
        }

        /// <summary>The desk on the island (venue pose + the spot's venue-local x/z), or null before the island has it.</summary>
        public static Vector3? DeskPosition(IslandWorld world)
        {
            var v = world != null ? world.Find(FactoryHooks.VenueId) : null;
            if (v == null) return null;
            var local = FactoryHooks.DeskLocalFallback;
            if (Services.TryGet<ConfigStore>(out var cfg))
            {
                var spots = cfg.Registries?.SpotsFor(FactoryHooks.VenueId);
                if (spots != null)
                    foreach (var s in spots.spots.Values)
                        if (s.hasPosition && (s.wireId == FactoryHooks.DeskSpot || s.id == "launch_desk" || s.id == FactoryHooks.DeskSpot))
                        { local = new Vector2(s.x, s.z); break; }
            }
            var p = v.ToIsland(local);
            return new Vector3(p.x, v.elevation, p.y);
        }

        private WorldOffer Offer(Vector3 player)
        {
            if (_disposed || _svc.Disposed) return null;
            var s = VenueSession.Current;
            if (s == null || !s.IsIsland || s.Island == null) { FactoryHooks.SetInTower(false); return null; }
            if (!ReferenceEquals(_world, s.Island)) { _world = s.Island; _desk = DeskPosition(_world); }
            FactoryHooks.SetInTower(s.VenueId == FactoryHooks.VenueId);
            if (_desk == null) return null;
            var desk = _desk.Value;
            float d = InteractionFinder.Flat(player, desk);
            if (d > WarmRange) return null;
            if (MarketsOpen) { _ = _svc.RefreshMe(); _ = _svc.RefreshConfig(); }   // both no-ops while fresh
            if (d > Range || FactoryLaunchFlow.Open) return null;
            if (Time.unscaledTime >= _nextCaption || _offer.perform == null) { _nextCaption = Time.unscaledTime + 0.25f; Fill(desk); }
            return _offer;
        }

        private void Fill(Vector3 desk)
        {
            var o = _offer;
            o.id = OfferId;
            o.verbKey = "factory.launch";
            o.icon = "projects";   // UiIcon: rocket-launch
            o.tagKey = "factory.tagDesk";
            o.style = "jobred";
            o.tagLocked = false;
            o.primary = true;
            o.position = desk + Vector3.up * 1.5f;
            o.follow = null;
            o.range = Range;
            o.overSeats = true;   // the desk's stage seat ("Take a stage seat") would otherwise hide the launch prompt
            o.perform = Perform;
            o.secondaryVerbKey = "factory.rules";
            o.secondaryIcon = "info";
            o.secondaryPerform = () => Open(FactoryHooks.Rules);
            o.busy = _svc.Launching;
            long? cash = Services.TryGet<PlayerProfile>(out var prof) && prof.Me != null ? prof.CashCents : (long?)null;
            var (sub, tone, disabled) = Caption(_svc.Me, _svc.Config, _svc.NowMs, MarketsOpen, _svc.Offline, cash);
            o.sub = sub; o.subTone = tone; o.disabled = disabled;
        }

        /// <summary>The caption under the verb (pure, tested): fee and launches left, the cooldown, the cap, too little Cash,
        /// offline, or closed. <paramref name="cashCents"/> null = unknown (not checked).</summary>
        public static (string sub, string tone, bool disabled) Caption(FactoryMe me, FactoryConfig config, double nowMs, bool marketsOpen, bool offline, long? cashCents = null)
        {
            config ??= FactoryConfig.Defaults;
            if (!marketsOpen) return (FactoryText.T("desk.soon"), null, true);
            if (offline) return (FactoryText.T("desk.offline"), "warn", true);
            string fee = MoneyFormat.Cash(config.LaunchFeeCents);
            long wait = me != null ? me.WaitMs(nowMs) : 0;
            if (me != null && me.Capped && wait > 0) return (FactoryText.F("desk.capped", FactoryText.Clock(wait)), "bad", true);
            if (wait > 0) return (FactoryText.F("desk.cooldown", FactoryText.Clock(wait)), "warn", true);
            long least = config.LaunchFeeCents + config.MinLiquidityCents;
            if (cashCents is long cash && cash < least)
                return (FactoryText.F("desk.cash", MoneyFormat.Cash(least), MoneyFormat.Cash(config.MinLiquidityCents)), "warn", true);
            if (me == null) return (FactoryText.F("desk.sub", fee, config.DailyLaunchCap, config.DailyLaunchCap), null, false);
            return (FactoryText.F("desk.sub", fee, me.LaunchesLeft, me.DailyLaunchCap), "good", false);
        }

        private void Perform()
        {
            Log.Info("factory", "desk: launch (markets " + (MarketsOpen ? "open" : "closed") + ")");
            if (!MarketsOpen) { Bus.Publish(ToastSignal.Text(FactoryText.T("desk.soon"), ToastKind.Info)); return; }
            Open(FactoryHooks.Launch);
        }

        private static void Open(string view)
        {
            if (FactoryHooks.Open == null) { Log.Warn("factory", "desk: no Factory opener bound"); return; }
            FactoryHooks.Open(view);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            WorldOffers.Remove(_source);
            FactoryHooks.SetInTower(false);
        }
    }

    /// <summary>
    /// One Factory service per signed-in player (rebinds on sign-in / sign-out) and the Launchpad Tower hookup: the desk
    /// prompt and <see cref="FactoryHooks.Open"/>. Until the Factory screens are approved (ct-life-evidence/factory-r5/
    /// ui-refs/BRIEF.md), opening the Factory shows Pulse on its Launchpad tab, where player coins trade today.
    /// Created by FlowController.
    /// </summary>
    public sealed class FactoryOverlays : IDisposable
    {
        private readonly Session _session;
        private readonly Action<string> _openApp;
        private FactoryService _svc;
        private LaunchpadDesk _desk;
        private LaunchBoardScreen _board;
        private bool _disposed;

        public FactoryOverlays(Action<string> openApp)
        {
            _openApp = openApp;
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            Bind();
        }

        public FactoryService Service => _svc;

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            Unbind();
            if (string.IsNullOrEmpty(user) || RuntimeMode.IsOfflineDemo) return;
            _svc = new FactoryService(user, Services.Get<IRestClient>(), () => ServerClock.NowMs, () => Time.realtimeSinceStartupAsDouble);
            _desk = new LaunchpadDesk(_svc);
            if (LaunchpadDesk.MarketsOpen) _ = _svc.Board("mine");   // so Pulse knows my coins (the rug warning) after a restart
            FactoryHooks.Open = OpenFactory;
            FactoryLaunchFlow.OpenPulseCoin = OpenPulseCoin;
            _board = LaunchBoardScreen.Create(_svc);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Get("factoryRoute") == "ui") FactoryUiRoute.Start(_svc);
            else if (LaunchArgs.Flag("factoryRoute")) FactoryRoute.Start(_svc);
#endif
        }

        private void OpenFactory(string view)
        {
            Log.Info("factory", "open " + view);
            if (view == FactoryHooks.Launch) { FactoryLaunchFlow.Start(_svc); return; }
            if (view == FactoryHooks.Rules) { FactoryLaunchFlow.ShowRules(); return; }
            CTLife.Economy.PulseApp.PendingTab = "launch";
            _openApp?.Invoke("pulse");
        }

        private void OpenPulseCoin(string coinId)
        {
            CTLife.Economy.PulseApp.PendingCoinNext = coinId;
            _openApp?.Invoke("pulse");
        }

        private void Unbind()
        {
            _desk?.Dispose();
            _desk = null;
            if (_board != null) UnityEngine.Object.Destroy(_board.gameObject);
            _board = null;
            if (FactoryLaunchFlow.Open) FactoryLaunchFlow.Close();
            _svc?.Dispose();
            _svc = null;
            if (FactoryHooks.Open == (Action<string>)OpenFactory) FactoryHooks.Open = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            Unbind();
            FactoryHooks.Open = null;
        }
    }
}
