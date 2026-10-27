using System;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// Player coins in Pulse (brief beat 5, Duo kit): the coin face instead of the letter avatar, a PLAYER badge (RUGGED once
    /// flagged), a bar to 3x (the reachable goal where creator fees switch on, not the $5M graduation), and DEV BAG x % (orange
    /// at 20 %+). The coin page gets the big face, "by alias", the bar with "3x = creator fees on", liquidity, and the
    /// creator's own fees. Facts come from FactoryService (boards, detail); the price and market cap are the market's.
    /// </summary>
    public static class FactoryPulse
    {
        public const int HighBagBps = 2_000;

        public static bool IsPlayer(MarketCoin c) => c != null && (c.PlayerLaunched || FactoryService.Current?.Known(c.CoinId) != null);

        public static VisualElement Leading(MarketCoin c, string size = "rf-art-s")
        {
            var f = FactoryService.Current?.Known(c.CoinId);
            return FactoryArt.Image(c.TemplateId ?? f?.TemplateId, size);
        }

        /// <summary>The row's extra line: badge, 3x bar, dev bag.</summary>
        public static VisualElement RowExtra(MarketCoin c)
        {
            var svc = FactoryService.Current;
            var f = svc?.Known(c.CoinId);
            if (f == null) svc?.Want(c.CoinId);
            var line = El.Div("rf-moon");
            line.Add(Badge(f != null && f.RugFlagged));
            line.Add(MoonBar(c.PriceMicros, svc?.Config, f?.Mooned == true, false));
            if (f != null) line.Add(DevBag(f.DevAllocationBps));
            return line;
        }

        public static VisualElement Badge(bool rugged)
        {
            var b = El.Div("rf-player-badge", El.Text(FactoryText.T(rugged ? "pulse.rugged" : "pulse.player")));
            if (rugged) b.AddToClassList("rf-rugged-badge");
            return b;
        }

        public static VisualElement DevBag(int bps)
        {
            var d = El.Div("rf-devbag", El.Text(FactoryText.F("pulse.devBag", bps / 100)));
            d.EnableInClassList("rf-devbag--high", bps >= HighBagBps);
            return d;
        }

        public static VisualElement MoonBar(long priceMicros, FactoryConfig cfg, bool mooned, bool wide)
        {
            float pct = mooned ? 1f : FactoryRules.MoonProgress(priceMicros, cfg);
            var fill = El.Div("rf-moon-fill");
            fill.style.width = new Length(pct * 100f, LengthUnit.Percent);
            var bar = El.Div("rf-moon-bar", fill);
            var wrap = El.Div("rf-moon", bar, El.Text(FactoryText.T("pulse.toMoon")));
            wrap.EnableInClassList("rf-moon--hit", pct >= 1f);
            if (!wide) { wrap.style.flexGrow = 1; wrap.style.marginTop = 0; }
            return wrap;
        }

        /// <summary>The coin page header for a player coin.</summary>
        public static VisualElement PageHeader(MarketCoin c)
        {
            var svc = FactoryService.Current;
            var f = svc?.Known(c.CoinId);
            if (f == null) svc?.Want(c.CoinId);
            var col = El.Div("rf-page-col");
            col.Add(El.Text(c.Name, "rf-page-name"));
            var sub = El.Div("rf-page-sub", El.Text("$" + c.Ticker), Badge(f != null && f.RugFlagged));
            if (f != null) sub.Add(El.Text("  " + FactoryText.F("pulse.by", f.CreatorAlias)));
            if (c.LaunchedAtMs.HasValue) sub.Add(El.Text(" · " + MoneyFormat.Ago((ServerClock.NowMs - c.LaunchedAtMs.Value) / 1000.0)));
            col.Add(sub);
            var head = El.Div("rf-page-head", Leading(c, "rf-art-l"), col);
            var box = El.Div(null, head);
            box.Add(MoonBar(c.PriceMicros, svc?.Config, f?.Mooned == true, true));
            bool hit = f?.Mooned == true || FactoryRules.MoonProgress(c.PriceMicros, svc?.Config) >= 1f;
            box.Add(El.Text(FactoryText.T(hit ? "pulse.moonHit" : "pulse.moonGoal"), "rf-page-goal"));
            if (f != null)
            {
                var facts = El.Div("rf-page-sub", El.Text(FactoryText.F("pulse.liquidity", FactoryLaunchSheet.Dollars(f.LiquidityCents))), DevBag(f.DevAllocationBps));
                if (f.IsMine && f.CreatorFeesCents is long fees) facts.Add(El.Text("  " + FactoryText.F("pulse.yourFees", MoneyFormat.Cash(fees))));
                box.Add(facts);
            }
            box.name = "pulse-factory-head";
            return box;
        }
    }
}
