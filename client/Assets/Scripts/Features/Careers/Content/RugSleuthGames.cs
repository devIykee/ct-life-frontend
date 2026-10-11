using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Rug Sleuth on Audit Row: on-chain investigations. Anchors: tracing stolen funds hop by hop to an exchange
    /// deposit; launch-slot bundles from one funder (CT exposés, docs/research/ct-x-observations-2026-10-04.md);
    /// wallet clusters and supply concentration (Bubblemaps). Wallet labels are random and fictional.
    /// </summary>
    internal static class RugSleuthGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "sleuth_thread", CareerId = "rug_sleuth", InContract = true, Engine = MiniGameEngine.Pick,
                Title = "Follow the Wallets", Tagline = "Trace the stolen SOL to the exchange.",
                Rules = new[]
                {
                    "Each step shows where the stolen funds sit now and every transfer out of that wallet.",
                    "Follow the bulk: the transfer carrying almost all of it (minus a small fee), sent after it arrived.",
                    "Ignore gas top-ups, small slices and anything sent before the funds landed. A wrong pick still shows you the right hop.",
                },
                ScoringRule = "One point per hop you follow correctly.",
                RealTask = "Tracing exploit proceeds through hops to an exchange deposit address.",
                DefaultDuration = 55,
                Create = init => new PickRound(Traces(new DetRng(init.Seed), 2), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "sleuth_bundle_check", CareerId = "rug_sleuth", Engine = MiniGameEngine.Flag,
                Title = "Bundle Check", Tagline = "Who really bought the launch?",
                Rules = new[]
                {
                    "Each board lists a launch's first buyers: the slot they bought in and the wallet that funded them.",
                    "A bundle wallet bought in slot 0 AND shares its funder with another slot-0 buyer. Flag those.",
                    "Lone snipers in slot 0 and later buyers aren't bundles, even with a shared funder.",
                },
                ScoringRule = "Per board: bundle wallets found minus wallets wrongly flagged (never below zero).",
                RealTask = "Spotting same-funder launch bundles before calling a coin 'organic'.",
                DefaultDuration = 50,
                Create = init => new FlagRound(BundleBoards(new DetRng(init.Seed), 4), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "sleuth_cluster_share", CareerId = "rug_sleuth", Engine = MiniGameEngine.Pick,
                Title = "Insider Share", Tagline = "Which funder secretly holds the most supply?",
                Rules = new[]
                {
                    "Each option is a cluster: wallets funded by the same wallet, with their share of supply.",
                    "Add up each cluster. Pick the funder whose wallets hold the most combined.",
                    "The biggest single wallet is usually a decoy.",
                },
                ScoringRule = "One point per board where you pick the biggest cluster.",
                RealTask = "Measuring hidden insider supply from wallet clusters.",
                DefaultDuration = 50,
                Create = init => new PickRound(ClusterBoards(new DetRng(init.Seed), 6), init.MaxScore, init.Duration),
            };
        }

        private static string Clock(int minutes) => (minutes / 60 % 24).ToString("00", CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        private static string Sol(double v) => v.ToString(v >= 100 ? "#,0.0" : "0.00", CultureInfo.InvariantCulture) + " SOL";

        // ------------------------------------------------------------ traces

        public static List<PickBoard> Traces(DetRng rng, int traces)
        {
            var boards = new List<PickBoard>();
            for (int tr = 0; tr < traces; tr++)
            {
                int hops = rng.Range(3, 5);
                var wallets = ContentKit.Wallets(rng, hops + 8);
                double amount = Math.Round(rng.Between(800, 5000), 1);
                int time = rng.Range(120, 210);
                string current = wallets[0];
                for (int h = 0; h < hops; h++)
                {
                    bool last = h == hops - 1;
                    double next = Math.Round(amount * (1 - rng.Between(0.0005, 0.003)), 1);
                    int nextTime = time + rng.Range(1, 10);
                    string nextWallet = last ? "deposit address at a big exchange" : wallets[h + 1];
                    var options = new List<(PickOption option, bool correct)>
                    {
                        (new PickOption { Label = Sol(next) + " → " + nextWallet + " at " + Clock(nextTime), Reveal = "The bulk, after it arrived." }, true),
                        (new PickOption { Label = Sol(Math.Round(amount * rng.Between(0.97, 1.0), 1)) + " → " + wallets[hops + 1 + h % 3] + " at " + Clock(time - rng.Range(2, 15)), Reveal = "Sent before the funds arrived: a different trail." }, false),
                        (new PickOption { Label = Sol(Math.Round(rng.Between(0.5, 3), 2)) + " → " + wallets[hops + 4] + " at " + Clock(time + rng.Range(0, 4)), Reveal = "A gas top-up, not the loot." }, false),
                    };
                    if (rng.Chance(0.6))
                        options.Add((new PickOption { Label = Sol(Math.Round(amount * rng.Between(0.08, 0.2), 1)) + " → " + wallets[hops + 5] + " (cold wallet) at " + Clock(time + rng.Range(1, 12)), Reveal = "A slice, not the bulk." }, false));
                    rng.Shuffle(options);
                    boards.Add(new PickBoard
                    {
                        Title = "Trace " + (tr + 1) + " · hop " + (h + 1) + " of " + hops,
                        Context = h == 0 ? "Exploiter wallet " + current + " drained " + Sol(amount) + " at " + Clock(time) + "."
                                         : "Funds now at " + current + ": " + Sol(amount) + " arrived " + Clock(time) + ".",
                        Brief = "Which transfer carries the stolen funds?",
                        Options = options.Select(o => o.option).ToArray(), Correct = options.FindIndex(o => o.correct),
                        Why = last ? "Case closed: the funds hit an exchange deposit address, where a freeze request can land."
                                   : "The bulk moved on to " + nextWallet + " at " + Clock(nextTime) + ".",
                    });
                    amount = next;
                    time = nextTime;
                    current = nextWallet;
                }
            }
            return boards;
        }

        // ------------------------------------------------------------ bundles

        public static List<FlagBoard> BundleBoards(DetRng rng, int count)
        {
            var boards = new List<FlagBoard>();
            var coins = rng.Sample(ContentKit.Coins, count);
            for (int b = 0; b < count; b++)
            {
                var names = ContentKit.Wallets(rng, 16);
                string funder = names[0];
                int bundleSize = rng.Range(2, 5);
                var rows = new List<(string text, bool bundle, string why)>();
                for (int i = 0; i < bundleSize; i++)
                    rows.Add((FormattableString.Invariant($"{names[1 + i]} · slot 0 · funded by {funder} · {rng.Range(8, 40) / 2.0:0.0} SOL"), true, "Slot 0 and the same funder as other slot-0 buyers: bundle."));
                rows.Add((FormattableString.Invariant($"{names[6]} · slot 0 · funded by {names[7]} · {rng.Range(2, 10) / 2.0:0.0} SOL"), false, "A lone sniper: slot 0 but its own funder."));
                rows.Add((FormattableString.Invariant($"{names[8]} · slot {rng.Range(25, 90)} · funded by {funder} · {rng.Range(2, 10) / 2.0:0.0} SOL"), false, "Same funder but a later slot: not a launch bundle."));
                int organic = 7 - rows.Count;
                for (int i = 0; i < organic; i++)
                    rows.Add((FormattableString.Invariant($"{names[9 + i]} · slot {rng.Range(3, 120)} · funded by {names[12 + i % 4]} · {rng.Range(1, 12) / 2.0:0.0} SOL"), false, "A normal buyer."));
                rng.Shuffle(rows);
                boards.Add(new FlagBoard
                {
                    Title = coins[b] + " · first buyers", Brief = "Find " + bundleSize + " bundle wallets.", Code = false,
                    Lines = rows.Select(r => new FlagLine(r.text, r.bundle, r.why)).ToArray(),
                });
            }
            return boards;
        }

        // ------------------------------------------------------------ clusters

        public static List<PickBoard> ClusterBoards(DetRng rng, int count)
        {
            var boards = new List<PickBoard>();
            for (int b = 0; b < count; b++)
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    var board = Cluster(rng);
                    if (board != null) { boards.Add(board); break; }
                }
            return boards;
        }

        private static PickBoard Cluster(DetRng rng)
        {
            int n = rng.Range(3, 5);
            var funders = ContentKit.Wallets(rng, n);
            var shares = new List<double[]>();
            for (int c = 0; c < n; c++)
            {
                int k = rng.Range(2, 6);
                shares.Add(Enumerable.Range(0, k).Select(_ => Math.Round(rng.Between(0.5, 6.5), 1)).ToArray());
            }
            var totals = shares.Select(s => Math.Round(s.Sum(), 1)).ToArray();
            int best = Array.IndexOf(totals, totals.Max());
            var sorted = totals.OrderByDescending(x => x).ToArray();
            if (sorted[0] - sorted[1] < 1.5) return null;
            double maxSingle = shares.Max(s => s.Max());
            int decoy = shares.FindIndex(s => s.Max() == maxSingle);
            if (decoy == best) return null;
            return new PickBoard
            {
                Title = rng.Pick(ContentKit.Coins) + " supply map", Brief = "Pick the cluster holding the most supply combined.", Correct = best,
                Why = "Add every wallet in a cluster. The biggest single wallet sits in a smaller cluster.",
                Options = Enumerable.Range(0, n).Select(c => new PickOption
                {
                    Label = "Funder " + funders[c],
                    Facts = new[] { shares[c].Length + " wallets: " + string.Join(" + ", shares[c].Select(x => x.ToString("0.0", ContentKit.Inv) + "%")) },
                    Reveal = "Total " + totals[c].ToString("0.0", ContentKit.Inv) + "% of supply",
                }).ToArray(),
            };
        }
    }
}
