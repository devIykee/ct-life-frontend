using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Doxx Researcher (the spec's career id; in-game it is investigative research on fictional accounts only).
    /// Anchors: CT exposé threads ("X is behind ... fully bundled", "fake PnLs", bundle cabals), fake PnL screenshot
    /// flexes, alt accounts linked by tells (docs/research/ct-x-observations-2026-10-04.md). Every handle is invented;
    /// nothing here identifies a real person.
    /// </summary>
    internal static class ResearcherGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "doxx_thread", CareerId = "doxx_researcher", InContract = true, Engine = MiniGameEngine.Sequence,
                Title = "Receipts Thread", Tagline = "Put the evidence in the order it happened.",
                Rules = new[]
                {
                    "Each board is one scheme, told as loose receipts with no timestamps.",
                    "Tap the receipts in the order they must have happened: money has to arrive before it moves.",
                    "Tap a placed receipt to take it back. File when the thread is complete.",
                },
                ScoringRule = "One point per receipt in its exact place.",
                RealTask = "Writing an exposé thread that a reader can follow and check.",
                DefaultDuration = 55,
                Create = init => new SequenceRound(ContentKit.Boards(new DetRng(init.Seed), Threads, 4), init.Seed, init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "doxx_alt_match", CareerId = "doxx_researcher", Engine = MiniGameEngine.Match,
                Title = "Same Anon?", Tagline = "Two matching tells is a link. One is a coincidence.",
                Rules = new[]
                {
                    "Left: anonymous accounts. Right: known accounts. All fictional.",
                    "Pair each anon with the known account that shares two of its tells (typos, sign-offs, posting hours, habits).",
                    "Accounts that share only one tell are decoys.",
                },
                ScoringRule = "One point per correct pair.",
                RealTask = "Linking alt accounts behind a rug from writing style and habits.",
                DefaultDuration = 55,
                Create = init => new MatchRound(AltBoards(new DetRng(init.Seed), 4), init.Seed, init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "doxx_fact_check", CareerId = "doxx_researcher", Engine = MiniGameEngine.Sort,
                Title = "Source It", Tagline = "Proof, screenshot or vibes?",
                Rules = new[]
                {
                    "Every claim in a draft thread needs a grade before it ships.",
                    "On-chain proof: anyone can check it (a transaction, a wallet, a block).",
                    "Screenshot only: an image that could be edited. Vibes: no evidence at all.",
                },
                ScoringRule = "Right grades minus half a point per wrong grade, out of the stack.",
                RealTask = "Grading evidence in a sleuth thread; fake PnL screenshots are everywhere.",
                ActionLabel = "On-chain proof|Screenshot only|Vibes",
                DefaultDuration = 45,
                Create = init => new SortRound(new[] { "On-chain proof", "Screenshot only", "Vibes" }, ContentKit.Deck(new DetRng(init.Seed), EvidencePool, 3, 12), init.MaxScore, init.Duration),
            };
        }

        private static SequenceBoard Seq(string title, params string[] items) => new SequenceBoard { Title = title, Brief = "Order the receipts.", Items = items };

        private static readonly SequenceBoard[] Threads =
        {
            Seq("The $SPLORT bundle",
                "Wallet A withdraws 400 SOL from an exchange.",
                "Wallet A funds 14 brand-new wallets.",
                "$SPLORT launches; the 14 wallets buy in the very first slot.",
                "A paid KOL posts 'just aped $SPLORT, LFG'.",
                "The 14 wallets sell into the KOL's followers.",
                "Wallet A deposits 900 SOL back to the exchange."),
            Seq("The 99% win-rate course",
                "A trader buys a tiny bag of a coin that later pumps.",
                "They screenshot the PnL at the top.",
                "They post the screenshot: '+700 SOL today, 99.99% win rate'.",
                "Replies ask for the wallet; they post a different one.",
                "They launch a paid 'premium alpha' group."),
            Seq("The fake claim page",
                "A lookalike domain for the airdrop is registered.",
                "A reply bot posts 'claim is live' under the team's announcement.",
                "Users connect and sign a 'claim' message.",
                "A drainer sweeps the signed wallets.",
                "The stolen funds are bridged to another chain."),
            Seq("The slow rug",
                "The team holds 30% of supply after launch.",
                "Team wallets sell about 1% of supply every day.",
                "Holders notice and ask in the Discordia server.",
                "The team announces a 'burn' to calm everyone down.",
                "The burn turns out to be tokens sent to a team wallet.",
                "The LP is pulled."),
            Seq("The paid shill cycle",
                "A project offers a KOL 2% of supply for a post.",
                "The KOL receives the tokens to a fresh wallet.",
                "The KOL posts 'no affiliation, just like the art'.",
                "The KOL's fresh wallet sells into the pump.",
                "Someone links the fresh wallet to the KOL's main."),
            Seq("The KOL wallet leak",
                "The dev wallet sends $GLORPO to a KOL before launch.",
                "$GLORPO launches.",
                "The KOL posts 'found a hidden gem: $GLORPO'.",
                "Followers buy the KOL's call.",
                "The KOL's wallet sells into them.",
                "Someone looks the KOL up in the public KOL wallet list and posts the trades."),
            Seq("The copy-trade trap",
                "A famous trader's wallet is tracked by a copy-trading bot.",
                "The trader buys $WOBBL.",
                "Thousands of copy-traders buy right behind him.",
                "The trader sells into the copy-traders.",
                "A follower posts: 'I copied him exactly. He sold first.'"),
            Seq("The overdose stream rug",
                "A dev launches a coin and starts a livestream.",
                "Viewers pile in while the stream 'goes wrong'.",
                "The dev fakes a medical emergency on camera.",
                "The price spikes on sympathy buys.",
                "The dev 'recovers' and sells everything."),
        };

        // ------------------------------------------------------------ alt matching (procedural)

        private static readonly string[] Tells =
        {
            "writes 'definately'", "writes 'recieve'", "writes 'alot'", "signs off 'stay based'", "signs off 'skill issue'",
            "signs off 'we're so back'", "posts 02:00-04:00 UTC", "posts at 13:00 UTC sharp", "posts only on weekends",
            "uses the same wallet-tracker referral link", "spells it '$SOLL'", "ends every post with '...'",
            "quotes the same anime frame", "calls every dip 'cooked'", "double-spaces after full stops", "links the same Pixel Wall tile",
        };

        private static readonly string[] AnonHandles = { "@frogfan_9", "@moon_chair_anon", "@bags_unholder", "@nightshift_ape", "@cope_dealer", "@quiet_whale_7", "@tg_mod_irl", "@zero_bags" };
        private static readonly string[] KnownHandles = { "@larp_labs_dev", "@marina_course_guy", "@alpha_dao_ceo", "@trench_kol_xx", "@vc_row_vic", "@launch_desk_lou", "@spaces_host_sol", "@pixel_wall_pam" };

        public static List<MatchBoard> AltBoards(DetRng rng, int count)
        {
            var boards = new List<MatchBoard>();
            for (int b = 0; b < count; b++)
            {
                const int n = 4;
                var tells = rng.Sample(Tells, n * 3); // 2 shared per pair + 1 noise per anon
                var anons = rng.Sample(AnonHandles, n);
                var knowns = rng.Sample(KnownHandles, n);
                var left = new string[n];
                var right = new string[n];
                var why = new string[n];
                for (int i = 0; i < n; i++)
                {
                    string a = tells[i * 2], c = tells[i * 2 + 1], noise = tells[n * 2 + i];
                    // A decoy tell copied from the next pair, so every known account shares one tell with a wrong anon.
                    string decoy = tells[((i + 1) % n) * 2 + rng.Next(2)];
                    var leftTells = rng.Sample(new[] { a, c, noise }, 3);
                    var rightTells = rng.Sample(new[] { a, c, decoy }, 3);
                    left[i] = anons[i] + ": " + string.Join(", ", leftTells);
                    right[i] = knowns[i] + ": " + string.Join(", ", rightTells);
                    why[i] = anons[i] + " and " + knowns[i] + " both " + a + " and " + c + ".";
                }
                boards.Add(new MatchBoard
                {
                    Title = "Case file " + (b + 1), Brief = "Two shared tells make a match.", Left = left, Right = right,
                    Solution = Enumerable.Range(0, n).ToArray(), Why = why,
                });
            }
            return boards;
        }

        // ------------------------------------------------------------ evidence grading

        private static readonly SortCard[] EvidencePool =
        {
            ContentKit.Card(0, "A transaction shows the dev wallet buying in the launch slot.", "Anyone can open the transaction. Proof."),
            ContentKit.Card(0, "The LP tokens moved to the dev's wallet at 03:12 (block linked).", "A block anyone can check. Proof."),
            ContentKit.Card(0, "One wallet funded all 14 early buyers; the transfers are public.", "Public transfers. Proof."),
            ContentKit.Card(0, "The 'burn' went to a wallet the team controls, not the burn address.", "Destination is on-chain. Proof."),
            ContentKit.Card(0, "The KOL's main wallet funded the 'fresh' wallet that dumped.", "A transfer anyone can trace. Proof."),
            ContentKit.Card(0, "The public KOL wallet list shows the KOL sold 20 minutes after posting the call.", "The wallet and its trades are public. Proof."),
            ContentKit.Card(1, "\"18-year-old retires off one trade: $460 to $10M\" with a PnL image.", "An image with no wallet. Screenshot only."),
            ContentKit.Card(2, "\"He turned $1K into $15M this year, trust me.\"", "A legend with no wallet linked. Vibes."),
            ContentKit.Card(1, "A copy-trader's screenshot: 'I copied him exactly. He sold first.'", "The loss may be real; the image proves nothing. Screenshot only."),
            ContentKit.Card(2, "\"Before I go, a secret: a famous trader is behind $SOGGY and slow-rugging it.\" (no wallets)", "An exposé with no receipts. Vibes."),
            ContentKit.Card(1, "A PnL screenshot showing +700 SOL.", "Images are easy to edit. Screenshot only."),
            ContentKit.Card(1, "A DM screenshot where 'the team' admits the rug.", "DMs can be faked. Screenshot only."),
            ContentKit.Card(1, "A cropped chart with an arrow at 'the insider buy'.", "No wallet, no tx. Screenshot only."),
            ContentKit.Card(1, "A screenshot of a deleted post promising 50x.", "Possibly real, still uncheckable. Screenshot only."),
            ContentKit.Card(1, "A group-chat screenshot listing 'cabal wallets' with no addresses.", "No addresses to check. Screenshot only."),
            ContentKit.Card(2, "\"Trust me, I know the dev.\"", "No evidence. Vibes."),
            ContentKit.Card(2, "\"Everyone in the group chat says it's a cabal.\"", "Rumour. Vibes."),
            ContentKit.Card(2, "\"The logo looks like the last rug's logo.\"", "Resemblance isn't evidence. Vibes."),
            ContentKit.Card(2, "\"The dev posts at night, that's suspicious.\"", "Not evidence of anything. Vibes."),
            ContentKit.Card(2, "\"I can just feel it's bundled.\"", "Vibes."),
        };
    }
}
