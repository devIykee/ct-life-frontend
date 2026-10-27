#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.World;
using UnityEngine;

namespace CTLife.Economy
{
    /// <summary>
    /// Dev builds only, launch extra <c>factoryRoute=1</c> (R5 device check against a local backend #14): follows the
    /// Sleuth's deep link `venue:launchpad_tower` (the game's travel), walks to the launch desk, waits for the desk prompt
    /// (logs its caption), launches a coin through <see cref="FactoryService"/>, waits for the market room to show it as a
    /// player coin, tries a second launch (the cooldown pre-check), buys a little of it, reads `view=mine` and the coin's
    /// detail, then presses the prompt (Pulse's Launchpad tab, the interim Factory screen). Each step logs
    /// "CTLIFE [factory-route] ..." and pauses on "shot ..." lines so screenshots and recordings can be timed from logcat.
    /// </summary>
    public static class FactoryRoute
    {
        private static bool _running;
        private static void Mark(string what) => Debug.Log("CTLIFE [factory-route] " + what);

        public static void Start(FactoryService svc)
        {
            if (_running) return;
            _running = true;
            _ = Run(svc, svc.Life);
        }

        private static async Task Run(FactoryService svc, CancellationToken ct)
        {
            try
            {
                float deadline = Time.realtimeSinceStartup + 300f;
                while (!ct.IsCancellationRequested && (VenueSession.Current == null || !VenueSession.Current.IsActive || !VenueSession.Current.IsIsland) && Time.realtimeSinceStartup < deadline)
                    await Awaitable.WaitForSecondsAsync(0.5f, ct);
                var s = VenueSession.Current;
                if (s == null || !s.IsIsland) { Mark("FAIL not on CT Island"); return; }
                await Awaitable.WaitForSecondsAsync(4f, ct);
                var desk = LaunchpadDesk.DeskPosition(s.Island);
                if (desk == null) { Mark("FAIL no launchpad_tower on the island"); return; }
                Mark($"start at {s.LocalPlayer.transform.position} desk {desk.Value} zone {s.VenueId}");

                // 1. the Sleuth card's deep link: the game's own travel to the tower
                bool linked = VenueLinks.Open("venue:launchpad_tower");
                Mark("deep link venue:launchpad_tower -> " + (linked ? "travel" : "NO TRAVEL BOUND"));
                for (int i = 0; i < 80 && linked && InteractionFinder.Flat(Pos(), desk.Value) > 60f; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
                s = VenueSession.Current;
                await Awaitable.WaitForSecondsAsync(3f, ct);
                Mark($"after travel at {Pos()} ({InteractionFinder.Flat(Pos(), desk.Value):0.0} m from the desk) zone {s?.VenueId}");

                // 2. walk to the desk (stand 1.4 m in front of it, on the room side)
                var v = s.Island.Find(FactoryHooks.VenueId);
                var standFlat = v.ToIsland(FactoryHooks.DeskLocalFallback + new Vector2(0f, -1.4f));
                var stand = new Vector3(standFlat.x, v.elevation, standFlat.y);
                bool ok = s.LocalPlayer.MoveTo(stand);
                Mark("walk to the desk: path " + (ok ? "ok" : "REFUSED (snapping)"));
                float t0 = Time.realtimeSinceStartup;
                while (ok && s.LocalPlayer != null && s.LocalPlayer.HasDestination && Time.realtimeSinceStartup - t0 < 60f) await Awaitable.WaitForSecondsAsync(0.25f, ct);
                if (!ok || InteractionFinder.Flat(Pos(), stand) > 1.5f)
                {
                    float yaw = Mathf.Atan2(desk.Value.x - stand.x, desk.Value.z - stand.z);
                    s.LocalPlayer.SnapTo(new Vector3(stand.x, IslandStreamer.GroundHeight(stand.x, stand.z) + 0.05f, stand.z), yaw);
                    CameraRig.Instance?.SettleBehind();
                    Mark("snapped in front of the desk");
                }
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
                Mark($"at {Pos()} zone {VenueSession.Current?.VenueId} inTower {FactoryHooks.InTower}");

                // 3. the desk prompt with the live allowance
                await svc.RefreshConfig(force: true);
                await svc.RefreshMe(force: true);
                WorldOffer offer = null;
                for (int i = 0; i < 40 && (offer = WorldOffers.Best(Pos())) == null; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
                await Awaitable.WaitForSecondsAsync(0.6f, ct);
                offer = WorldOffers.Best(Pos());
                Mark($"offer {(offer?.id ?? "NONE")} verb {offer?.verbKey} sub '{offer?.sub}' disabled {offer?.disabled}");
                Mark("shot desk");
                await Awaitable.WaitForSecondsAsync(6f, ct);

                // 4. launch a coin (service layer, no Factory UI yet)
                var rnd = new System.Random();
                string ticker = "LC" + new string(Enumerable.Range(0, 3).Select(_ => "ABCDEFGHJKMNPQRSTUVWXYZ23456789"[rnd.Next(31)]).ToArray());
                var draft = new LaunchDraft
                {
                    Name = "Lane C Probe " + ticker.Substring(2), Ticker = ticker,
                    TemplateId = svc.Config.Templates[rnd.Next(svc.Config.Templates.Count)],
                    LiquidityCents = svc.Config.MinLiquidityCents, DevAllocationBps = 2000,
                };
                long? cash = Services.TryGet<PlayerProfile>(out var prof) ? prof.CashCents : (long?)null;
                var r = await svc.Launch(draft, cash);
                Mark($"launch {ticker}: {(r.Ok ? "OK " + r.Coin.CoinId + " art " + r.Coin.TemplateId : "REFUSED " + (r.Check.Ok ? r.Error?.Code : r.Check.Key))} '{r.Message}'");
                if (!r.Ok) { Mark("done (launch refused)"); return; }

                // 5. the market room carries it as a player coin
                var hub = MoneyHub.Acquire();
                try
                {
                    MarketCoin live = null;
                    for (int i = 0; i < 60; i++)
                    {
                        hub.Market.Pump(new[] { "new" }, ct);
                        live = hub.Market.RoomLive ? hub.Market.Find(r.Coin.CoinId) : null;
                        if (live != null && live.PlayerLaunched) break;
                        await Awaitable.WaitForSecondsAsync(0.5f, ct);
                    }
                    Mark($"market room: {(live != null ? $"{live.Ticker} playerLaunched {live.PlayerLaunched} art {live.TemplateId} price {live.PriceMicros}" : "NOT SEEN (roomLive " + hub.Market.RoomLive + ")")}");
                }
                finally { hub.Release(); }

                // 6. a second launch right away: the cooldown pre-check (no request sent)
                var again = await svc.Launch(new LaunchDraft { Name = "Lane C Again", Ticker = ticker + "X", TemplateId = draft.TemplateId, LiquidityCents = draft.LiquidityCents }, cash);
                Mark($"second launch: {(again.Ok ? "OK (unexpected)" : "refused " + (again.Check.Ok ? again.Error?.Code : again.Check.Key))} '{again.Message}'");
                await Awaitable.WaitForSecondsAsync(0.6f, ct);
                offer = WorldOffers.Best(Pos());
                Mark($"offer after launch: sub '{offer?.sub}' disabled {offer?.disabled} tone {offer?.subTone}");
                Mark("shot cooldown");
                await Awaitable.WaitForSecondsAsync(6f, ct);

                // 7. trade it through the ordinary market endpoint, then the lists
                try
                {
                    var buy = await svc.Buy(r.Coin.CoinId, 500, 2000);
                    Mark($"buy $5: units {buy.Units} price {buy.PriceMicros} fee {buy.FeeCents}");
                }
                catch (Exception e) { Mark("buy failed: " + FactoryErrors.From(e, true).Code); }
                var mine = await svc.Board("mine");
                Mark($"mine: {mine?.Count ?? -1} coins, newest {mine?.FirstOrDefault()?.Ticker} isMine {mine?.FirstOrDefault()?.IsMine} fees {mine?.FirstOrDefault()?.CreatorFeesCents}");
                var trending = await svc.Board("trending", 10);
                Mark($"trending: {string.Join(",", trending?.Select(c => c.Ticker) ?? Array.Empty<string>())}");
                try
                {
                    var d = await svc.Detail(r.Coin.CoinId);
                    Mark($"detail {d.Coin.Ticker}: candles {d.Candles.Count} holders {d.Holders.Count} risk '{d.RiskHint}'");
                }
                catch (Exception e) { Mark("detail failed: " + FactoryErrors.From(e, false).Code); }

                // 8. press the prompt: the interim Factory screen (Pulse, Launchpad tab)
                offer = WorldOffers.Best(Pos());
                if (offer != null && offer.id == LaunchpadDesk.OfferId) { offer.perform(); Mark("pressed Launch a coin"); }
                else Mark("prompt gone before press: " + (offer?.id ?? "none"));
                await Awaitable.WaitForSecondsAsync(2f, ct);
                Mark("shot pulse");
                await Awaitable.WaitForSecondsAsync(6f, ct);
                Mark("done");
            }
            catch (OperationCanceledException) { Mark("cancelled"); }
            catch (Exception e) { Mark("FAIL " + e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
            finally { _running = false; }
        }

        private static Vector3 Pos()
        {
            var me = VenueSession.Current?.LocalPlayer;
            return me != null ? me.transform.position : Vector3.zero;
        }
    }
}
#endif
