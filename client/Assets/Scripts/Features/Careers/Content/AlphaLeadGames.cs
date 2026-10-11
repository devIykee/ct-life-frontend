using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Alpha Lead: the trading desk - trenches exits, basis arbitrage and market making. Anchors: bonding-curve
    /// launches that graduate to an AMM with a 1.25% curve fee (launchpad fee page, May 2026); bundles and dev sells
    /// (docs/research/ct-x-observations-2026-10-04.md); perp mark vs spot premium and hourly funding (Hyperliquid
    /// docs); inventory risk in market making [Unverified primary source: general practice].
    /// </summary>
    internal static class AlphaLeadGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "alpha_call", CareerId = "alpha_lead", InContract = true, Engine = MiniGameEngine.Timing,
                Title = "Curve Exit", Tagline = "You aped the launch. Now get out before the dev does.",
                Rules = new[]
                {
                    "Each coin is a fresh Launchpad Tower launch you bought at 1.00x. Tap Sell once.",
                    "Price climbs the bonding curve. When the dev wallet starts moving, the dump is a second or two away.",
                    "Coins that graduate get a spike, then a sell wall. Holder rewards add a little for every second you hold. Greed is the trap.",
                },
                ScoringRule = "Each coin scores how much of the best exit you caught (compared with riding it to the bottom).",
                RealTask = "Trenches trading: exiting bonding-curve launches before bundles and devs sell.",
                ActionLabel = "Sell",
                DefaultDuration = 45,
                Create = init => new TimingRound(CurveWindows(new DetRng(init.Seed), init.Duration), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "alpha_arb_spread", CareerId = "alpha_lead", Engine = MiniGameEngine.Timing,
                Title = "Arb the Spread", Tagline = "Perp Pit vs spot. Fire only when the gap beats the fees.",
                Rules = new[]
                {
                    "The Perp Pit price drifts above and below the spot price. The gap is shown in basis points (100 bps = 1%).",
                    "One arb per window: you capture the gap minus 40 bps of fees, whichever side it's on.",
                    "Some windows never clear the fees. Let those pass.",
                },
                ScoringRule = "Each window scores your capture against its best moment; passing a no-edge window scores full.",
                RealTask = "Basis trading between a perp's mark price and spot.",
                ActionLabel = "Arb",
                DefaultDuration = 45,
                Create = init => new TimingRound(SpreadWindows(new DetRng(init.Seed), init.Duration), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "alpha_book_balance", CareerId = "alpha_lead", Engine = MiniGameEngine.Balance,
                Title = "Keep the Book Balanced", Tagline = "Make markets, not bets. Keep inventory near flat.",
                Rules = new[]
                {
                    "You quote both sides of $FROGE. Order flow pushes your inventory long or short.",
                    "Lean your quotes to sell or to buy and pull it back. Hedge on Perp Pit to snap halfway home (costs fees, slow to reload).",
                    "Stay inside the green band. Whales and normie waves will test you.",
                },
                ScoringRule = "Share of the session inside the band, minus hedge fees.",
                RealTask = "Market making: managing inventory risk while quoting.",
                DefaultDuration = 45,
                Create = init => new BalanceRound(BookSpec(new DetRng(init.Seed), init.Duration), init.MaxScore, init.Duration),
            };
        }

        // ------------------------------------------------------------ curve exits

        public static List<TimingWindow> CurveWindows(DetRng rng, double duration)
        {
            var (count, length, samples) = ContentKit.TimingLayout(duration, 7.5, 3, 6);
            var coins = rng.Sample(ContentKit.Coins, count);
            int graduate = rng.Next(count);
            int stream = (graduate + 1 + rng.Next(Math.Max(1, count - 1))) % count; // one launch is a livestream-dev coin
            var windows = new List<TimingWindow>();
            for (int w = 0; w < count; w++)
            {
                bool grad = w == graduate;
                int dump = rng.Range((int)(samples * 0.45), (int)(samples * 0.9));
                int lead = rng.Range(10, 21); // 1.0-2.0 s of warning
                int tellFrom = Math.Max(1, dump - lead);
                int gradAt = grad ? rng.Range(samples / 4, Math.Max(samples / 4 + 1, dump - 12)) : -1;
                double growth = rng.Between(0.10, 0.28); // per second
                double floor = rng.Between(0.15, 0.5);
                var price = new double[samples];
                var note = new string[samples];
                var tell = new bool[samples];
                double p = 1.0;
                for (int i = 0; i < samples; i++)
                {
                    if (i < dump)
                    {
                        p *= 1 + growth * 0.1 + rng.Between(-0.012, 0.016);
                        if (grad && i == gradAt) { p *= rng.Between(1.25, 1.45); note[i] = "Graduated to Solana Street Swap"; }
                        if (grad && i > gradAt && gradAt >= 0) p *= 1 - rng.Between(0.004, 0.02); // the sell wall
                    }
                    else
                    {
                        double k = Math.Min(1, (i - dump + 1) / 4.0);
                        p = price[dump - 1] * (1 - k) + floor * k;
                        if (i == dump) note[i] = "Dev sold";
                    }
                    p = Math.Max(0.05, p);
                    price[i] = p;
                    tell[i] = i >= tellFrom && i < dump;
                }
                // Holder rewards (the launchpad pays holders for holding longer) add a small, steady amount per second.
                double rewardRate = rng.Between(0.008, 0.02);
                double end = price[samples - 1] + rewardRate * (samples - 1) * 0.1;
                var pay = price.Select((x, i) => x + rewardRate * i * 0.1 - end).ToArray();
                var readout = price.Select((x, i) => x.ToString("0.00", ContentKit.Inv) + "x · holder rewards +" + (rewardRate * i * 0.1).ToString("0.00", ContentKit.Inv) +
                                                      "x · curve " + Math.Min(100, (int)(i * 100.0 / samples * 1.2)) + "%").ToArray();
                bool live = w == stream && w != graduate;
                windows.Add(new TimingWindow
                {
                    Title = coins[w] + (live ? " · live on the dev's stream" : " · launch #" + rng.Range(100, 999)),
                    Brief = live ? "Bought at 1.00x. Chat is begging for a stunt." : "Bought at 1.00x.", Step = 0.1, Signal = price, Payoff = pay,
                    Tell = tell, Note = note, Readout = readout, TellText = live ? "Stream: 'brb chat', webcam off" : "Dev wallet moving",
                });
            }
            return windows;
        }

        // Perps now cover commodities too (oil and gold perps, per docs/research/ct-now-2026-10.md).
        private static readonly string[] SpreadMarkets = { "$FROGE", "$GLORPO", "$WAGMEOW", "OIL", "GOLD", "$CHONK", "$SPLORT" };

        // ------------------------------------------------------------ spread windows

        public const int ArbFeeBps = 40;

        public static List<TimingWindow> SpreadWindows(DetRng rng, double duration)
        {
            var (count, length, samples) = ContentKit.TimingLayout(duration, 7.0, 3, 6);
            int noEdge = rng.Next(count);
            var coins = rng.Sample(SpreadMarkets, count);
            var windows = new List<TimingWindow>();
            for (int w = 0; w < count; w++)
            {
                TimingWindow window = null;
                for (int attempt = 0; attempt < 20 && window == null; attempt++)
                {
                    window = SpreadWindow(rng, coins[w], w == noEdge, length, samples);
                    // Edge windows must clearly clear the fees at some point (by 10+ bps); otherwise draw again.
                    if (w != noEdge && window.Best < 10) window = null;
                }
                windows.Add(window ?? SpreadWindow(rng, coins[w], true, length, samples));
            }
            return windows;
        }

        private static TimingWindow SpreadWindow(DetRng rng, string coin, bool noEdge, double length, int samples)
        {
            // A no-edge window peaks at most 22 + 8 + 3 = 33 bps, always under the 40 bps fees.
            double a = noEdge ? rng.Between(12, 22) : rng.Between(45, 95);
            double b = noEdge ? rng.Between(3, 8) : rng.Between(4, 12), f1 = rng.Between(0.5, 1.1), f2 = rng.Between(1.6, 2.6), ph = rng.Between(0, 6.28);
            var bps = new double[samples];
            var pay = new double[samples];
            var readout = new string[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = i * 0.1;
                double env = Math.Sin(Math.PI * Math.Min(1, t / (length * 0.95)));
                double v = a * env * Math.Sin(f1 * t + ph) + b * Math.Sin(f2 * t) + rng.Between(-3, 3);
                bps[i] = v;
                pay[i] = Math.Abs(v) - ArbFeeBps;
                string side = v >= 0 ? "Perp Pit above spot" : "Perp Pit below spot";
                readout[i] = side + " by " + Math.Abs(v).ToString("0", ContentKit.Inv) + " bps · fees " + ArbFeeBps + " bps";
            }
            return new TimingWindow
            {
                Title = coin + " perp vs spot", Brief = "Fire once when the gap beats " + ArbFeeBps + " bps.", Step = 0.1,
                Signal = bps, Payoff = pay, Readout = readout,
            };
        }

        // ------------------------------------------------------------ book balance

        public static BalanceSpec BookSpec(DetRng rng, double duration)
        {
            const double seg = 0.5;
            int segments = (int)Math.Ceiling(duration / seg) + 2;
            var drift = new double[segments];
            var notes = new string[segments];
            int i = 0;
            double sign = rng.Chance(0.5) ? 1 : -1;
            while (i < segments)
            {
                bool whale = rng.Chance(0.18);
                int len = whale ? 2 : rng.Range(4, 11);
                // Flow trends: it usually keeps pushing the same way, so an idle book drifts out and stays out.
                if (rng.Chance(0.3)) sign = -sign;
                double rate = whale ? sign * rng.Between(55, 85) : sign * rng.Between(9, 24);
                string note = whale ? (sign > 0 ? "Whale dumping into your bid" : "Whale sweeping your asks")
                                    : (sign > 0 ? (rng.Chance(0.25) ? "Founder posts: 'MM should buy it all back and burn it'" : "Sellers hitting your bid") : "Normie wave buying from you");
                for (int k = 0; k < len && i < segments; k++, i++) { drift[i] = rate; notes[i] = note; }
            }
            return new BalanceSpec
            {
                MeterLabel = "Inventory", LowLabel = "Short", HighLabel = "Long", Min = -100, Max = 100, Start = 0, BandLo = -20, BandHi = 20,
                SegmentSeconds = seg, Drift = drift, Notes = notes,
                Actions = new[]
                {
                    new BalanceAction { Label = "Lean to sell", Delta = -12, Cooldown = 0.25 },
                    new BalanceAction { Label = "Lean to buy", Delta = 12, Cooldown = 0.25 },
                    new BalanceAction { Label = "Hedge on Perp Pit", PullToCentre = 0.5, Cooldown = 5, Cost = 1.0 },
                },
            };
        }
    }
}
