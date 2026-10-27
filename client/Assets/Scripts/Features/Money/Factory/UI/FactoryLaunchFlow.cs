using System;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// One launch at the desk (brief beats 2-4): the desk stage (camera + 3D coin), the sheet, and on HOLD the POST with the
    /// coin's drop/stamp/shoot playing while it runs. Success: the GTA-style band "COIN LAUNCHED / $T is live on Pulse" over
    /// the live world (held 2.5 s, then Open in Pulse / Done). Refusal: the coin falls back, the sheet returns on the step
    /// that fixes it. Also the Rules card (the desk's second key) and the rug warning before a big bag sell (Pulse).
    /// </summary>
    public static class FactoryLaunchFlow
    {
        public static bool Open { get; private set; }
        public static FactoryLaunchSheet Sheet => _sheet;
        public static bool BandShown => _band != null;
        /// <summary>Opens Pulse at a coin (set by FactoryOverlays).</summary>
        public static Action<string> OpenPulseCoin;

        private static FactoryDeskStage _stage;
        private static FactoryLaunchSheet _sheet;
        private static VisualElement _band, _bandActions, _rules, _rug;

        private static VisualElement Layer => UiRoot.Instance != null ? UiRoot.Instance.ModalLayer : null;

        public static void Start(FactoryService svc)
        {
            var layer = Layer;
            var s = VenueSession.Current;
            if (svc == null || layer == null || s == null || !s.IsIsland || Open) return;
            if (!LaunchpadDesk.DeskPose(s.Island, out var desk, out var toward)) return;
            Open = true;
            FactoryArt.EnsureStyles(layer);
            _ = svc.RefreshConfig();
            _ = svc.RefreshMe(force: true);
            _stage = FactoryDeskStage.Open(desk, toward);
            _sheet = new FactoryLaunchSheet(svc, _stage, d => _ = Launch(svc, d), Close);
            layer.Add(_sheet);
            Log.Info("factory", "launch sheet open");
        }

        public static void Close()
        {
            _sheet?.RemoveFromHierarchy(); _sheet = null;
            HideBand();
            _stage?.Close(); _stage = null;
            Open = false;
        }

        private static async Task Launch(FactoryService svc, LaunchDraft draft)
        {
            if (_sheet == null) return;
            var sheet = _sheet;
            sheet.SlideOut();
            Log.Info("factory", $"hold complete: launching ${FactoryRules.NormaliseTicker(draft.Ticker)}");
            long? cash = Services.TryGet<CTLife.Net.PlayerProfile>(out var p) && p.Me != null ? p.CashCents : (long?)null;
            var post = svc.Launch(draft, cash);
            var anim = _stage != null ? _stage.PlayLaunch() : Task.CompletedTask;
            var r = await post;
            if (!r.Ok)
            {
                await anim;
                if (_stage != null) await _stage.PlayRefused();
                if (_sheet == sheet) sheet.ShowRefusal(r.Error, r.Check);
                Bus.Publish(new SfxSignal("sfx_ui_error"));
                return;
            }
            await anim;
            sheet.RemoveFromHierarchy();
            if (_sheet == sheet) _sheet = null;
            ShowBand(r.Coin);
        }

        // ------------------------------------------------------------------ the launch band

        private static void ShowBand(FactoryCoin coin)
        {
            var layer = Layer;
            if (layer == null) { Close(); return; }
            HideBand();
            var word = El.Text(FactoryText.T("band.title")); word.AddToClassList("heat-band-word");
            var sub = El.Text(FactoryText.F("band.sub", coin.Ticker)); sub.AddToClassList("heat-band-sub");
            _band = El.Div("heat-band rf-band", word, sub);
            _band.name = "factory-band";
            _band.pickingMode = PickingMode.Ignore;
            layer.Add(_band);
            var pulse = new Button(() => { var id = coin.CoinId; Close(); OpenPulseCoin?.Invoke(id); }) { text = FactoryText.T("band.pulse"), name = "factory-band-pulse" };
            pulse.AddToClassList("heat-pill"); pulse.AddToClassList("rf-pill-yellow");
            var done = new Button(Close) { text = FactoryText.T("band.done"), name = "factory-band-done" };
            done.AddToClassList("heat-pill"); done.AddToClassList("heat-pill-ghost");
            _bandActions = El.Div("rf-band-actions", pulse, done);
            layer.Add(_bandActions);
            Bus.Publish(new SfxSignal("sfx_moon_horn"));
            _band.schedule.Execute(() => _band?.AddToClassList("heat-in")).ExecuteLater(30);
            _band.schedule.Execute(() => _bandActions?.AddToClassList("heat-in")).ExecuteLater(2500);
            Log.Info("factory", "band: coin launched " + coin.Ticker);
        }

        private static void HideBand()
        {
            _band?.RemoveFromHierarchy(); _band = null;
            _bandActions?.RemoveFromHierarchy(); _bandActions = null;
        }

        // ------------------------------------------------------------------ rules card (desk's second key)

        public static void ShowRules()
        {
            var layer = Layer;
            if (layer == null) return;
            FactoryArt.EnsureStyles(layer);
            _rules?.RemoveFromHierarchy();
            var text = El.Text(FactoryText.T("rules.body")); text.AddToClassList("heat-help-text");
            var credit = El.Text(FactoryArt.Credit); credit.AddToClassList("rf-credit");
            var ok = new Button(() => { _rules?.RemoveFromHierarchy(); _rules = null; }) { text = FactoryText.T("rules.ok"), name = "factory-rules-ok" };
            ok.AddToClassList("heat-pill"); ok.AddToClassList("rf-pill-yellow");
            _rules = El.Div("heat-sheet rf-rules", text, credit, El.Div("heat-sheet-actions", ok));
            _rules.name = "factory-rules";
            layer.Add(_rules);
            _rules.schedule.Execute(() => _rules?.AddToClassList("heat-in")).ExecuteLater(30);
        }

        // ------------------------------------------------------------------ rug warning (Pulse sell of 30 %+ of my bag)

        /// <summary>The help box before a big bag sell on your own coin (always warns; the client can't know if the rule fires).</summary>
        public static void WarnRug(string ticker, Action sell, Action keep)
        {
            var layer = Layer;
            if (layer == null) { sell?.Invoke(); return; }
            FactoryArt.EnsureStyles(layer);
            _rug?.RemoveFromHierarchy();
            var (lo, hi, sLo, sHi) = FactoryLaunchSheet.RugRange(FactoryService.Current?.Me?.Heat ?? 0);
            var text = El.Text(FactoryText.F("rug.body", ticker, lo, hi)); text.AddToClassList("heat-help-text");
            var keepBtn = new Button(() => { _rug?.RemoveFromHierarchy(); _rug = null; keep?.Invoke(); }) { text = FactoryText.T("rug.keep"), name = "factory-rug-keep" };
            keepBtn.AddToClassList("heat-pill"); keepBtn.AddToClassList("heat-pill-ghost");
            var sellBtn = new Button(() => { _rug?.RemoveFromHierarchy(); _rug = null; sell?.Invoke(); }) { text = FactoryText.T("rug.sell"), name = "factory-rug-sell" };
            sellBtn.AddToClassList("heat-pill"); sellBtn.AddToClassList("heat-pill-steal");
            _rug = El.Div("heat-sheet rf-rug-sheet", text, El.Div("heat-sheet-actions", keepBtn, sellBtn));
            _rug.name = "factory-rug-warning";
            layer.Add(_rug);
            _rug.schedule.Execute(() => _rug?.AddToClassList("heat-in")).ExecuteLater(30);
            Log.Info("factory", "rug warning shown for $" + ticker);
        }

        /// <summary>A sell of this share (percent) of the creator's own bag gets the warning (brief beat 6). Pure; tested.</summary>
        public static bool NeedsRugWarning(bool mine, int sellPct) => mine && sellPct >= 30;
    }
}
