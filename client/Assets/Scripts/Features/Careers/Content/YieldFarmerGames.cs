using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Yield Farmer / airdrop farmer at Airdrop Farm. Anchors: LP yield is trading fees versus printed incentives
    /// (concentrated-liquidity docs), vault performance fees, impermanent loss; the "LP army" range game
    /// (ct-now-2026-10 §4a lp_farmer, §5a); sybil filters that cluster wallets by common funder, sweeps and identical
    /// activity (Arbitrum sybil-detection method) and "bots farm it all" (ct-now §3a #16).
    /// </summary>
    internal static class YieldFarmerGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "yield_farm_apy", CareerId = "yield_farmer", InContract = true, Engine = MiniGameEngine.Pick,
                Title = "Real Yield Picker", Tagline = "Printed APR is not yield. Pick the pool that pays.",
                Rules = new[]
                {
                    "Each board states a rule and shows three pools.",
                    "Incentive APR is paid in the pool's own token: ignore it unless the rule says otherwise.",
                    "Some boards make you subtract the vault's cut or the expected impermanent loss. Do the maths.",
                },
                ScoringRule = "One point per board where you pick the pool the rule says is best.",
                RealTask = "Comparing LP pools on fee yield, vault fees and impermanent loss.",
                DefaultDuration = 50,
                Create = init => new PickRound(ApyBoards(new DetRng(init.Seed), 7), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "yield_farm_lp_range", CareerId = "yield_farmer", Engine = MiniGameEngine.Balance,
                Title = "Stay in Range", Tagline = "In range, printing fees. Out of range, crying.",
                Rules = new[]
                {
                    "You run a concentrated liquidity position on $FROGE-SOL. You only earn fees while the price sits inside your range.",
                    "The price drifts and spikes. Shift your range up or down to follow it, or re-centre in one go.",
                    "Every move costs swap fees, so chasing every wiggle loses money. Read the trend.",
                },
                ScoringRule = "Share of the session in range, minus the fees your moves cost.",
                RealTask = "Managing a concentrated LP position (the 'LP army' bins game).",
                DefaultDuration = 45,
                Create = init => new BalanceRound(RangeSpec(new DetRng(init.Seed), init.Duration), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "yield_farm_sybil_check", CareerId = "yield_farmer", Engine = MiniGameEngine.Sort,
                Title = "Sybil-Safe Farming", Tagline = "Would this farm survive the Sybil Barn filter?",
                Rules = new[]
                {
                    "Each card is a farming setup at Airdrop Farm.",
                    "The filter cuts clusters: wallets with a shared funder, sweeps to one address, identical amounts and timing, loops between your own wallets.",
                    "One person farming one wallet like a human passes, even if they're small.",
                },
                ScoringRule = "Right calls minus wrong calls, out of the stack.",
                RealTask = "Reading points-programme sybil filters before a snapshot.",
                ActionLabel = "Passes|Gets flagged",
                DefaultDuration = 45,
                Create = init => new SortRound(new[] { "Passes", "Gets flagged" }, ContentKit.Deck(new DetRng(init.Seed), SybilPool, 2, 12), init.MaxScore, init.Duration),
            };
        }

        // ------------------------------------------------------------ APY boards

        private static readonly string[] PoolNames = { "USDC pool", "SOL bins", "stable bins", "SOL-USDC vault", "auto-compounder" };

        public static List<PickBoard> ApyBoards(DetRng rng, int count)
        {
            var boards = new List<PickBoard>();
            for (int b = 0; b < count; b++)
            {
                int rule = b % 3;
                for (int attempt = 0; attempt < 200; attempt++)
                {
                    var board = rule == 0 ? FeeOnly(rng) : rule == 1 ? AfterVaultFee(rng) : AfterIl(rng);
                    if (board != null) { boards.Add(board); break; }
                }
            }
            rng.Shuffle(boards);
            return boards;
        }

        private static string Name(DetRng rng, string coin) => coin + " " + rng.Pick(PoolNames);

        private static PickBoard FeeOnly(DetRng rng)
        {
            var coins = rng.Sample(ContentKit.Coins, 3);
            var fee = new double[3];
            int best = rng.Next(3);
            fee[best] = Math.Round(rng.Between(14, 38), 1);
            for (int i = 0; i < 3; i++) if (i != best) fee[i] = Math.Round(rng.Between(3, fee[best] - 3), 1);
            int bait = (best + 1 + rng.Next(2)) % 3;
            var inc = new double[3];
            for (int i = 0; i < 3; i++) inc[i] = Math.Round(i == bait ? rng.Between(180, 650) : rng.Between(0, 40), 1);
            return new PickBoard
            {
                Title = "Rule: highest fee APR", Brief = "Incentives are printed tokens. Only trading fees count.",
                Correct = best, Why = "Fee APR is the yield that comes from real trading. The big total is mostly printed incentives.",
                Options = Enumerable.Range(0, 3).Select(i => new PickOption
                {
                    Label = Name(rng, coins[i]),
                    Facts = new[] { "Total APR " + ContentKit.Pct(fee[i] + inc[i]), "Fee APR " + ContentKit.Pct(fee[i]), "Incentives " + ContentKit.Pct(inc[i]) + " in " + coins[i] },
                    Reveal = "Fee APR " + ContentKit.Pct(fee[i]),
                }).ToArray(),
            };
        }

        private static PickBoard AfterVaultFee(DetRng rng)
        {
            var coins = rng.Sample(ContentKit.Coins, 3);
            int[] cuts = { 0, 10, 20, 30, 50 };
            var gross = new double[3];
            var cut = new int[3];
            var net = new double[3];
            for (int i = 0; i < 3; i++)
            {
                gross[i] = Math.Round(rng.Between(10, 60), 1);
                cut[i] = cuts[rng.Next(cuts.Length)];
                net[i] = Math.Round(gross[i] * (100 - cut[i]) / 100.0, 1);
            }
            int best = Array.IndexOf(net, net.Max());
            int topGross = Array.IndexOf(gross, gross.Max());
            var sorted = net.OrderByDescending(x => x).ToArray();
            if (best == topGross || sorted[0] - sorted[1] < 1.5) return null;
            return new PickBoard
            {
                Title = "Rule: best yield after the vault's cut", Brief = "The vault keeps its cut of the yield. Pick the best net.",
                Correct = best, Why = "Net = gross × (1 − vault cut). The biggest gross loses after a heavy cut.",
                Options = Enumerable.Range(0, 3).Select(i => new PickOption
                {
                    Label = Name(rng, coins[i]),
                    Facts = new[] { "Gross APR " + ContentKit.Pct(gross[i]), "Vault cut " + cut[i] + "%" },
                    Reveal = "Net " + ContentKit.Pct(net[i]),
                }).ToArray(),
            };
        }

        private static PickBoard AfterIl(DetRng rng)
        {
            var coins = rng.Sample(ContentKit.Coins, 3);
            var fee = new double[3];
            var il = new double[3];
            var net = new double[3];
            for (int i = 0; i < 3; i++)
            {
                fee[i] = Math.Round(rng.Between(15, 90), 1);
                il[i] = Math.Round(rng.Between(0, 75), 1);
                net[i] = Math.Round(fee[i] - il[i], 1);
            }
            int best = Array.IndexOf(net, net.Max());
            int topFee = Array.IndexOf(fee, fee.Max());
            var sorted = net.OrderByDescending(x => x).ToArray();
            if (best == topFee || sorted[0] - sorted[1] < 2 || net[best] <= 0) return null;
            return new PickBoard
            {
                Title = "Rule: fees minus expected IL", Brief = "Volatile pairs pay big fees and lose value to impermanent loss.",
                Correct = best, Why = "Net = fee APR − expected impermanent loss. The loudest fee APR has the worst IL.",
                Options = Enumerable.Range(0, 3).Select(i => new PickOption
                {
                    Label = Name(rng, coins[i]),
                    Facts = new[] { "Fee APR " + ContentKit.Pct(fee[i]), "Expected IL " + ContentKit.Pct(il[i]) + " a year" },
                    Reveal = "Net " + ContentKit.Pct(net[i]),
                }).ToArray(),
            };
        }

        // ------------------------------------------------------------ LP range

        /// <summary>Meter = price position against the middle of your range (−100 far below … +100 far above).</summary>
        public static BalanceSpec RangeSpec(DetRng rng, double duration)
        {
            const double seg = 0.5;
            int segments = (int)Math.Ceiling(duration / seg) + 2;
            var drift = new double[segments];
            var notes = new string[segments];
            double trend = rng.Chance(0.5) ? 1 : -1;
            int i = 0;
            while (i < segments)
            {
                bool spike = rng.Chance(0.15);
                int len = spike ? 2 : rng.Range(5, 13);
                // Prices trend: the direction usually holds, so an untouched range is soon left behind.
                if (rng.Chance(0.15)) trend = -trend;
                double rate = spike ? trend * rng.Between(45, 70) : trend * rng.Between(10, 20) + rng.Between(-2, 2);
                string note = spike ? (trend > 0 ? "Pump: price ripping through your range" : "Dump: price falling out of your range")
                                    : (trend > 0 ? "Slow grind up" : "Slow bleed down");
                for (int k = 0; k < len && i < segments; k++, i++) { drift[i] = rate; notes[i] = note; }
            }
            return new BalanceSpec
            {
                MeterLabel = "Price vs your range", LowLabel = "Below range", HighLabel = "Above range", Min = -100, Max = 100, Start = 0, BandLo = -22, BandHi = 22,
                SegmentSeconds = seg, Drift = drift, Notes = notes,
                Actions = new[]
                {
                    new BalanceAction { Label = "Shift range down", Delta = 28, Cooldown = 0.6, Cost = 0.15 },
                    new BalanceAction { Label = "Shift range up", Delta = -28, Cooldown = 0.6, Cost = 0.15 },
                    new BalanceAction { Label = "Re-centre", PullToCentre = 1.0, Cooldown = 4, Cost = 1.2 },
                },
            };
        }

        // ------------------------------------------------------------ sybil check

        private static readonly SortCard[] SybilPool =
        {
            ContentKit.Card(1, "12 wallets funded from one exchange withdrawal within 3 minutes, each did the same 3 swaps.", "Common funder plus identical activity: textbook cluster."),
            ContentKit.Card(1, "After the snapshot, 30 wallets swept everything to the same address.", "Sweeps into one address tie the cluster together."),
            ContentKit.Card(1, "One swap of exactly 0.1 SOL every 10 minutes for five days.", "Machine-regular amounts and timing get flagged."),
            ContentKit.Card(1, "50 wallets created the same day, identical points totals.", "Identical histories are a cluster fingerprint."),
            ContentKit.Card(1, "Funded by a wallet that also funded 200 others.", "A shared funder is the first thing the filter checks."),
            ContentKit.Card(1, "Volume came from sending the same 5 SOL around 8 of your own wallets.", "Looping funds between your own wallets is wash activity."),
            ContentKit.Card(1, "20 wallets bridged in within the same block, all from one source.", "Same-source, same-time bridging is a cluster."),
            ContentKit.Card(1, "Each wallet followed the same 'farm guide' to the second: same apps, same order, same amounts.", "Copy-paste behaviour across wallets is flagged."),
            ContentKit.Card(0, "One wallet, six months old, swaps of different sizes, voted in two community polls.", "Varied, long-lived activity from one wallet: a human."),
            ContentKit.Card(0, "Bridged twice, provided liquidity for three weeks, never sent funds to other farmers.", "Real usage, no cluster links."),
            ContentKit.Card(0, "Funded from its own exchange account, active at irregular hours over weeks.", "Its own funder, organic timing."),
            ContentKit.Card(0, "Skipped some weeks, came back, 40 transactions across 5 apps.", "Patchy is human. Bots are perfectly regular."),
            ContentKit.Card(0, "Got 2 SOL from a friend once and never sent anything back.", "One transfer from a friend isn't a cluster."),
            ContentKit.Card(0, "Small wallet: 15 transactions, but each one used a different feature.", "Small but genuine still passes."),
            ContentKit.Card(0, "Used one wallet on phone and tablet; same wallet, different devices.", "One wallet on two devices is still one person."),
        };
    }
}
