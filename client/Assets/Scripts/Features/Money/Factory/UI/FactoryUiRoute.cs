#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// Dev builds only, launch extra <c>factoryRoute=ui</c>: the Factory UI device check. Travels to Launchpad Tower, walks
    /// to the desk (prompt), presses Launch a coin (camera + 3D coin + sheet), picks art, types a name and ticker, sets the
    /// bag, reviews, holds to launch (the ring fills as under a finger), waits for the band, takes Done (board in view), opens
    /// the coin in Pulse, then tries to sell 100 % of the bag (the rug warning) and keeps it. Optional <c>factoryWait=1</c>
    /// idles 25 s at the desk first (an fps window without the sheet). Logs "CTLIFE [factory-ui] ..." with "shot ..." marks.
    /// </summary>
    public static class FactoryUiRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [factory-ui] " + what);

        public static void Start(FactoryService svc) => _ = Run(svc, svc.Life);

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
                VenueLinks.Open("venue:launchpad_tower");
                await Awaitable.WaitForSecondsAsync(4f, ct);
                var v = s.Island.Find(FactoryHooks.VenueId);
                var standFlat = v.ToIsland(FactoryHooks.DeskLocalFallback + new Vector2(0f, -1.4f));
                var stand = new Vector3(standFlat.x, v.elevation, standFlat.y);
                bool ok = s.LocalPlayer.MoveTo(stand);
                float t0 = Time.realtimeSinceStartup;
                while (ok && s.LocalPlayer.HasDestination && Time.realtimeSinceStartup - t0 < 60f) await Awaitable.WaitForSecondsAsync(0.25f, ct);
                await svc.RefreshMe(force: true);
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
                var offer = WorldOffers.Best(Pos());
                Mark($"prompt {offer?.id} style {offer?.style} sub '{offer?.sub}' disabled {offer?.disabled}");
                Mark("shot prompt");
                await Awaitable.WaitForSecondsAsync(5f, ct);
                if (LaunchArgs.Flag("factoryWait")) { Mark("fps window: idle at the desk"); await Awaitable.WaitForSecondsAsync(25f, ct); Mark("fps window end"); }

                offer?.perform?.Invoke();
                for (int i = 0; i < 20 && FactoryLaunchFlow.Sheet == null; i++) await Awaitable.WaitForSecondsAsync(0.1f, ct);
                var sheet = FactoryLaunchFlow.Sheet;
                if (sheet == null) { Mark("FAIL no sheet (" + (offer?.disabled == true ? "prompt disabled" : "?") + ")"); return; }
                await Awaitable.WaitForSecondsAsync(1.6f, ct);
                Mark("shot art");
                await Awaitable.WaitForSecondsAsync(3f, ct);
                var rnd = new System.Random();
                var art = svc.Config.Templates[rnd.Next(svc.Config.Templates.Count)];
                Tap(sheet.Q<Button>("factory-art-" + art));
                await Awaitable.WaitForSecondsAsync(1.2f, ct);
                Mark("picked " + art);
                Mark("shot artpicked");
                await Awaitable.WaitForSecondsAsync(3f, ct);

                Tap(sheet.Q<Button>("factory-next"));
                await Awaitable.WaitForSecondsAsync(0.5f, ct);
                string ticker = "FR" + new string(Enumerable.Range(0, 3).Select(_ => "ABCDEFGHJKMNPQRSTUVWXYZ"[rnd.Next(23)]).ToArray());
                var name = sheet.Q<TextField>("factory-name");
                var tick = sheet.Q<TextField>("factory-ticker");
                if (name != null) name.value = "Frog Of Wall Street";
                if (tick != null) tick.value = ticker.ToLowerInvariant();   // shows the upper-casing
                await Awaitable.WaitForSecondsAsync(1.0f, ct);
                Mark($"name '{sheet.Draft.Name}' ticker '{sheet.Draft.Ticker}' next enabled {sheet.Q<Button>("factory-next")?.enabledSelf}");
                Mark("shot name");
                await Awaitable.WaitForSecondsAsync(3f, ct);

                Tap(sheet.Q<Button>("factory-next"));
                await Awaitable.WaitForSecondsAsync(0.6f, ct);
                var bag = sheet.Q<SliderInt>("factory-bag-slider");
                if (bag != null) bag.value = 30;
                var liq = sheet.Q<Slider>("factory-liquidity-slider");
                if (liq != null) liq.value = FactoryLaunchSheet.LiquidityToT(5_000, svc.Config);
                await Awaitable.WaitForSecondsAsync(1.0f, ct);
                Mark($"bag {sheet.Draft.DevAllocationBps} bps liquidity {sheet.Draft.LiquidityCents}");
                Mark("shot bag");
                await Awaitable.WaitForSecondsAsync(3f, ct);

                Tap(sheet.Q<Button>("factory-next"));
                await Awaitable.WaitForSecondsAsync(0.8f, ct);
                Mark("shot review");
                await Awaitable.WaitForSecondsAsync(3f, ct);
                var hold = sheet.Hold;
                if (hold == null || !hold.enabledSelf) { Mark("FAIL hold disabled: " + sheet.Q<Label>(className: "rf-error")?.text); return; }
                Mark("hold start");
                hold.SimulateHold();
                await Awaitable.WaitForSecondsAsync(0.45f, ct);
                Mark("shot holding");
                for (int i = 0; i < 80 && !FactoryLaunchFlow.BandShown && FactoryLaunchFlow.Sheet != null; i++) await Awaitable.WaitForSecondsAsync(0.1f, ct);
                if (!FactoryLaunchFlow.BandShown)
                {
                    await Awaitable.WaitForSecondsAsync(1.5f, ct);
                    Mark("launch refused: " + FactoryLaunchFlow.Sheet?.Q<Label>(className: "rf-error")?.text);
                    Mark("shot refused");
                    await Awaitable.WaitForSecondsAsync(3f, ct);
                    FactoryLaunchFlow.Close();
                    return;
                }
                Mark("band shown");
                await Awaitable.WaitForSecondsAsync(0.4f, ct);
                Mark("shot band");
                await Awaitable.WaitForSecondsAsync(3.0f, ct);
                Mark("shot bandactions");
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
                var coin = svc.Mine.FirstOrDefault();
                Tap(UiRoot.Instance.ModalLayer.Q<Button>("factory-band-done"));
                await Awaitable.WaitForSecondsAsync(3f, ct);
                Mark("shot board");
                await Awaitable.WaitForSecondsAsync(4f, ct);

                if (coin != null)
                {
                    FactoryLaunchFlow.OpenPulseCoin?.Invoke(coin.CoinId);
                    await Awaitable.WaitForSecondsAsync(3.5f, ct);
                    Mark("shot pulsecoin");
                    await Awaitable.WaitForSecondsAsync(3f, ct);
                    var root = UiRoot.Instance.Root;
                    Tap(root.Q<Button>("pulse-sell"));
                    await Awaitable.WaitForSecondsAsync(1.2f, ct);
                    var all = MoneyStrings.F("pulse.sellPct", 100);
                    Tap(root.Q<VisualElement>("pulse-order-sheet")?.Query<Button>().Where(b => b.text == all).First());
                    await Awaitable.WaitForSecondsAsync(1.0f, ct);
                    Tap(root.Q<Button>("pulse-place"));
                    await Awaitable.WaitForSecondsAsync(1.0f, ct);
                    bool warned = UiRoot.Instance.ModalLayer.Q("factory-rug-warning") != null;
                    Mark("rug warning shown " + warned);
                    Mark("shot rug");
                    await Awaitable.WaitForSecondsAsync(3f, ct);
                    Tap(UiRoot.Instance.ModalLayer.Q<Button>("factory-rug-keep"));
                    await Awaitable.WaitForSecondsAsync(1f, ct);
                    PulseApp.PendingTab = "launch";
                    Mark("pulse list next");
                }
                Mark("done");
            }
            catch (OperationCanceledException) { Mark("cancelled"); }
            catch (Exception e) { Mark("FAIL " + e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
        }

        private static void Tap(Button b)
        {
            if (b == null) { Mark("tap: button missing"); return; }
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = b;
            b.SendEvent(e);
        }

        private static Vector3 Pos()
        {
            var me = VenueSession.Current?.LocalPlayer;
            return me != null ? me.transform.position : Vector3.zero;
        }
    }
}
#endif
