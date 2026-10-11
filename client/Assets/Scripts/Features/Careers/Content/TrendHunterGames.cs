using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Trend Hunter: narrative rotation, prediction markets and coin life cycles. Anchors: prediction-market share
    /// prices are implied probabilities and pay $1 per winning share (Polymarket docs); humans-vs-AI CT reading
    /// contests (docs/research/ct-x-observations-2026-10-04.md); the market sim's hidden coin arc
    /// (stealth → hype → peak → outcome) in contract/spec.
    /// </summary>
    internal static class TrendHunterGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "trend_call", CareerId = "trend_hunter", InContract = true, Engine = MiniGameEngine.Pick,
                Title = "Call the Rotation", Tagline = "Biggest isn't fastest. Call what's heating up.",
                Rules = new[]
                {
                    "Each board shows mentions per hour for four narratives: two hours ago, one hour ago, now.",
                    "Read the board's rule: fastest growth this hour, or biggest acceleration (this hour's growth minus last hour's).",
                    "The loudest narrative is usually the trap.",
                },
                ScoringRule = "One point per board where you call the narrative the rule picks.",
                RealTask = "Spotting narrative rotation from mention velocity, not mention volume.",
                DefaultDuration = 50,
                Create = init => new PickRound(RotationBoards(new DetRng(init.Seed), 7), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "trend_odds", CareerId = "trend_hunter", Engine = MiniGameEngine.Sort,
                Title = "Price the Odds", Tagline = "Trade the edge. Pass on the noise.",
                Rules = new[]
                {
                    "Each card is a Prediction Pit market. A YES share pays 1 Cash dollar if YES happens; its price is the crowd's odds.",
                    "Your model gives a probability. Buy YES if your number beats the price by more than the costs; buy NO if the price beats your number by more than the costs.",
                    "Inside the costs, pass.",
                },
                ScoringRule = "Right calls minus half a point per wrong call, out of the stack.",
                RealTask = "Prediction-market trading: comparing your probability with the market's price.",
                ActionLabel = "Buy YES|Buy NO|Pass",
                DefaultDuration = 45,
                Create = init => new SortRound(new[] { "Buy YES", "Buy NO", "Pass" }, OddsDeck(new DetRng(init.Seed), 12), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "trend_arc_order", CareerId = "trend_hunter", Engine = MiniGameEngine.Sequence,
                Title = "Read the Arc", Tagline = "Stealth, hype, peak, aftermath. Put the snapshots in order.",
                Rules = new[]
                {
                    "Every coin on Launchpad Tower runs the same arc: stealth, hype, peak, then the aftermath.",
                    "Each board has four snapshots of one coin, shuffled. Order them from earliest to latest.",
                    "Holder count alone won't do it: the aftermath can have more holders than stealth.",
                },
                ScoringRule = "One point per snapshot in its exact place.",
                RealTask = "Reading where a coin is in its life cycle before you buy someone's exit.",
                DefaultDuration = 50,
                Create = init => new SequenceRound(ArcBoards(new DetRng(init.Seed), 4), init.Seed, init.MaxScore, init.Duration),
            };
        }

        // ------------------------------------------------------------ rotation boards

        public static List<PickBoard> RotationBoards(DetRng rng, int count)
        {
            var boards = new List<PickBoard>();
            for (int b = 0; b < count; b++)
            {
                bool accel = b % 2 == 1;
                for (int attempt = 0; attempt < 300; attempt++)
                {
                    var board = Rotation(rng, accel);
                    if (board != null) { boards.Add(board); break; }
                }
            }
            return boards;
        }

        private static PickBoard Rotation(DetRng rng, bool accel)
        {
            var names = rng.Sample(ContentKit.Narratives, 4);
            var h2 = new int[4];
            var h1 = new int[4];
            var now = new int[4];
            var score = new double[4];
            for (int i = 0; i < 4; i++)
            {
                h2[i] = rng.Range(300, 6000);
                h1[i] = (int)Math.Round(h2[i] * rng.Between(0.6, 2.2) / 10) * 10;
                now[i] = (int)Math.Round(h1[i] * rng.Between(0.6, 2.4) / 10) * 10;
                if (h1[i] <= 0 || now[i] <= 0) return null;
                double g1 = now[i] / (double)h1[i] - 1, g0 = h1[i] / (double)h2[i] - 1;
                score[i] = accel ? g1 - g0 : g1;
            }
            int best = Array.IndexOf(score, score.Max());
            var sorted = score.OrderByDescending(x => x).ToArray();
            int loudest = Array.IndexOf(now, now.Max());
            if (sorted[0] - sorted[1] < 0.12 || best == loudest) return null;
            return new PickBoard
            {
                Title = accel ? "Rule: biggest acceleration" : "Rule: fastest growth this hour",
                Brief = accel ? "Pick the narrative whose growth sped up the most." : "Pick the narrative with the biggest % jump in the last hour.",
                Correct = best,
                Why = accel ? "Acceleration = this hour's growth minus last hour's growth." : "Growth = now ÷ one hour ago. Volume doesn't matter.",
                Options = Enumerable.Range(0, 4).Select(i => new PickOption
                {
                    Label = names[i],
                    Facts = new[] { "2h ago " + ContentKit.Num(h2[i]), "1h ago " + ContentKit.Num(h1[i]), "Now " + ContentKit.Num(now[i]) },
                    Reveal = accel
                        ? "Growth " + Pct(now[i] / (double)h1[i] - 1) + " after " + Pct(h1[i] / (double)h2[i] - 1)
                        : "Growth " + Pct(now[i] / (double)h1[i] - 1),
                }).ToArray(),
            };
        }

        private static string Pct(double r) => (r >= 0 ? "+" : "") + (r * 100).ToString("0", ContentKit.Inv) + "%";

        // ------------------------------------------------------------ odds deck

        private static readonly string[] Questions =
        {
            "Will $FROGE graduate before Sunday Unlock?", "Will the Town Crier announce a $1M+ liquidation today?",
            "Will Market Mood be green at 20:00 UTC?", "Will the ORE motherlode hit this week?", "Will a coin on Launchpad Tower do 10x today?",
            "Will Perp Pit open interest beat yesterday's?", "Will Spaces Studio host 500+ listeners tonight?",
            "Will $GLORPO hold 1,000 holders by Friday?", "Will the Sybil Barn jail more than 5,000 wallets this season?",
            "Will a Rekt Clinic tombstone top $50k this week?", "Will the Weekly Meta pick perps?", "Will Timeline Square's billboard sell out?",
            "Will Perp Pit list oil perps this week?", "Will the Town Crier report $500M+ liquidated in 10 minutes today?",
            "Will COIN2050 Week's rooftop party sell out?", "Will a Launchpad Tower coin pay out holder rewards above 5% this week?",
            "Will Mayor Toad win round one of the CT City vote?", "Will the FOMO Committee hike rates at its next meeting?",
        };

        public const int OddsCost = 3;

        public static List<SortCard> OddsDeck(DetRng rng, int size)
        {
            var qs = rng.Sample(Questions, Math.Min(size, Questions.Length));
            var cards = new List<SortCard>();
            for (int i = 0; i < qs.Count; i++)
            {
                int kind = i % 3; // 0 yes, 1 no, 2 pass
                int price = rng.Range(12, 89);
                int model;
                if (kind == 0) model = Math.Min(97, price + rng.Range(OddsCost + 6, OddsCost + 25));
                else if (kind == 1) model = Math.Max(3, price - rng.Range(OddsCost + 6, OddsCost + 25));
                else model = price + rng.Range(-2, 3);
                int edge = model - price;
                if (kind == 0 && edge <= OddsCost + 3) { model = Math.Min(97, price + OddsCost + 8); edge = model - price; }
                if (kind == 1 && -edge <= OddsCost + 3) { model = Math.Max(3, price - OddsCost - 8); edge = model - price; }
                if (kind == 0 && edge <= OddsCost + 3) kind = 2; // price too high to leave room for a YES edge
                if (kind == 1 && -edge <= OddsCost + 3) kind = 2;
                string why = kind == 0 ? FormattableString.Invariant($"Model {model}% vs price {price}¢: +{edge}¢ edge on YES beats the {OddsCost}¢ cost.")
                    : kind == 1 ? FormattableString.Invariant($"Price {price}¢ vs model {model}%: NO is worth {-edge}¢ more than it costs, beating the {OddsCost}¢ cost.")
                    : FormattableString.Invariant($"Model {model}% vs price {price}¢: the {Math.Abs(edge)}¢ gap is inside the {OddsCost}¢ cost. Pass.");
                cards.Add(new SortCard
                {
                    Title = qs[i], Bin = kind, Why = why,
                    Chips = new[] { "YES trades at " + price + "¢", "Your model: " + model + "%", "Fees + spread: " + OddsCost + "¢" },
                });
            }
            rng.Shuffle(cards);
            return cards;
        }

        // ------------------------------------------------------------ arc boards

        public static List<SequenceBoard> ArcBoards(DetRng rng, int count)
        {
            var coins = rng.Sample(ContentKit.Coins, count);
            var boards = new List<SequenceBoard>();
            for (int b = 0; b < count; b++)
            {
                int h0 = rng.Range(25, 90);
                int h1 = h0 * rng.Range(5, 12);
                int h2 = h1 * rng.Range(2, 4);
                bool rug = rng.Chance(0.5);
                int h3 = rug ? Math.Max(h0 + rng.Range(40, 200), h2 / rng.Range(5, 9)) : h2 / 2 + rng.Range(10, 80);
                int k1 = rng.Range(3, 9), k2 = k1 * rng.Range(3, 6);
                string[] items =
                {
                    FormattableString.Invariant($"Holders {h0} · KOL posts 0 · 'Too late?' replies 0 · Top 10 hold {rng.Range(55, 75)}%"),
                    FormattableString.Invariant($"Holders {h1:#,0} · KOL posts {k1} · 'Too late?' replies {rng.Range(0, 4)} · Volume climbing"),
                    FormattableString.Invariant($"Holders {h2:#,0} · KOL posts {k2} · 'Too late?' replies {rng.Range(60, 240)} · Top wallets selling"),
                    rug ? FormattableString.Invariant($"Holders {h3:#,0} · KOL posts deleted · LP pulled · Volume near zero")
                        : FormattableString.Invariant($"Holders {h3:#,0} · KOL posts {rng.Range(0, 3)} · Price down {rng.Range(60, 90)}% from the top · Volume drying up"),
                };
                boards.Add(new SequenceBoard
                {
                    Title = coins[b] + " snapshots", Brief = "Earliest first.", Items = items,
                    Why = new[] { "Stealth: a handful of holders, no KOLs.", "Hype: KOLs arrive, holders multiply.", "Peak: 'is it too late?' replies and insiders selling.", rug ? "Aftermath: rugged." : "Aftermath: the slow bleed." },
                });
            }
            return boards;
        }
    }
}
