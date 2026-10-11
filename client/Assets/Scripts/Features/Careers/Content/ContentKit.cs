using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Shared building blocks for mini-game content. Every name here is a parody invented for CT City: no real
    /// brand, project, exchange or person. Venue names come from the build spec.
    /// </summary>
    public static class ContentKit
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly string[] Coins =
        {
            "$FROGE", "$GLORPO", "$WAGMEOW", "$SPLORT", "$CHONK", "$TOASTY", "$HODLHOG", "$SOGGY", "$NUGGO", "$BLEEPO",
            "$SNACKY", "$GRUMBO", "$PLONK", "$WOBBL", "$ZORPY", "$MOONCHAIR",
        };

        public static readonly string[] Narratives =
        {
            "AI agents", "Prediction markets", "Perp DEXs", "Frog coins", "Dog coins", "ORE staking", "Points farms",
            "Tokenized maple syrup", "Seeker apps", "Pay-per-call APIs", "Cat coins", "Restaking",
        };

        private const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        /// <summary>A short fictional wallet label in the "7fQx…k2Lm" style. Random from the seed; never a real address.</summary>
        public static string Wallet(DetRng rng)
        {
            var c = new char[8];
            for (int i = 0; i < 8; i++) c[i] = Base58[rng.Next(Base58.Length)];
            return new string(c, 0, 4) + "…" + new string(c, 4, 4);
        }

        public static List<string> Wallets(DetRng rng, int count)
        {
            var set = new List<string>();
            while (set.Count < count)
            {
                var w = Wallet(rng);
                if (!set.Contains(w)) set.Add(w);
            }
            return set;
        }

        public static string Usd(double dollars) => "$" + dollars.ToString(dollars >= 100 ? "#,0" : "#,0.00", Inv);
        public static string Pct(double pct, string fmt = "0.#") => pct.ToString(fmt, Inv) + "%";
        public static string Num(double v, string fmt = "#,0") => v.ToString(fmt, Inv);

        public static SortCard Card(int bin, string title, string why, params string[] chips) =>
            new SortCard { Bin = bin, Title = title, Why = why, Chips = chips ?? Array.Empty<string>() };

        /// <summary>
        /// A deck of `size` cards from an authored pool, balanced across bins (each bin gets ⌈size/bins⌉ before trimming),
        /// then shuffled. Every bin present in the pool appears at least once.
        /// </summary>
        public static List<SortCard> Deck(DetRng rng, IReadOnlyList<SortCard> pool, int bins, int size)
        {
            var byBin = Enumerable.Range(0, bins).Select(b => pool.Where(c => c.Bin == b).ToList()).ToList();
            foreach (var list in byBin) rng.Shuffle(list);
            int per = (int)Math.Ceiling(size / (double)bins);
            var deck = new List<SortCard>();
            foreach (var list in byBin) deck.AddRange(list.Take(per));
            // Trim from the largest bins, never removing a bin's last card.
            while (deck.Count > size)
            {
                var counts = Enumerable.Range(0, bins).Select(b => deck.Count(c => c.Bin == b)).ToList();
                int bin = counts.IndexOf(counts.Max());
                var candidates = deck.Where(c => c.Bin == bin).ToList();
                if (candidates.Count <= 1) break;
                deck.Remove(candidates[rng.Next(candidates.Count)]);
            }
            rng.Shuffle(deck);
            return deck;
        }

        /// <summary>Takes `count` boards from an authored pool in a seeded order.</summary>
        public static List<T> Boards<T>(DetRng rng, IReadOnlyList<T> pool, int count) => rng.Sample(pool, Math.Min(count, pool.Count));

        /// <summary>
        /// Splits a timing round into windows that fit even when the player acts at the last moment of every window
        /// (each action adds TimingRound.FeedbackSeconds). Windows are sampled every 0.1 s.
        /// </summary>
        public static (int count, double length, int samples) TimingLayout(double duration, double targetLength, int minCount = 3, int maxCount = 7)
        {
            int count = Math.Max(minCount, Math.Min(maxCount, (int)Math.Floor(duration / (targetLength + TimingRound.FeedbackSeconds + 0.2))));
            double length = Math.Floor((duration / count - TimingRound.FeedbackSeconds - 0.2) * 10) / 10;
            if (length < 2) length = 2;
            return (count, length, (int)Math.Round(length * 10));
        }

        public static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }
}
