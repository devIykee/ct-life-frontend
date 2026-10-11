using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Copywriter: the content side of CT work - Superteam-style content bounties, launch posts, paid-partnership
    /// copy. Real-world anchors: paid promos must be disclosed; "guaranteed" returns, fake urgency, "drop your wallet"
    /// and lookalike claim links are the scam templates seen on CT (docs/research/ct-x-observations-2026-10-04.md).
    /// </summary>
    internal static class CopywriterGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "copywriter_rewrite", CareerId = "copywriter", InContract = true, Engine = MiniGameEngine.Flag,
                Title = "Red-Pen the Shill", Tagline = "Strike every line that can't ship. Keep the hook.",
                Rules = new[]
                {
                    "Each draft is a client post for the Timeline.",
                    "Flag lines that promise returns, fake urgency, hide a paid deal, impersonate someone or ask for wallets, keys or signatures.",
                    "Leave honest lines alone: facts, disclosures and risk notes are the good stuff.",
                    "File the draft when you're done. Next draft follows.",
                },
                ScoringRule = "Per draft: problems found minus clean lines flagged (never below zero).",
                RealTask = "Editing paid-partnership and launch copy before it goes out.",
                DefaultDuration = 50,
                Create = init => new FlagRound(ContentKit.Boards(new DetRng(init.Seed), Drafts, 4), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "copywriter_char_crunch", CareerId = "copywriter", Engine = MiniGameEngine.Combo,
                Title = "280 Crunch", Tagline = "Land the post between 255 and 280 characters.",
                Rules = new[]
                {
                    "Every line shows its length. Keep or cut each one.",
                    "The post must land between 255 and 280 characters (lengths include the space after each line).",
                    "Never cut the coin or project name, the date or the disclosure.",
                    "No running total: add it up yourself, then file.",
                },
                ScoringRule = "One point per post that lands in the band with every must-keep line kept.",
                RealTask = "Fitting a launch or recap post into one Timeline post.",
                DefaultDuration = 55,
                Create = init => new ComboRound(ContentKit.Boards(new DetRng(init.Seed), CrunchPosts, 4).Select(BuildCrunch).ToList(), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "copywriter_safe_to_post", CareerId = "copywriter", Engine = MiniGameEngine.Sort,
                Title = "Safe to Post?", Tagline = "Ship it or send it back.",
                Rules = new[]
                {
                    "Clients send finished posts. Decide: Post it, or Rewrite.",
                    "Rewrite anything with a promise of returns, a hidden paid deal, a fake endorsement, a wallet or seed request, or pressure to act now.",
                    "Honest bad news is fine. Disclosed sponsorships are fine.",
                },
                ScoringRule = "Right calls minus wrong calls, out of the whole inbox.",
                RealTask = "Final review of client copy before posting.",
                ActionLabel = "Post it|Rewrite",
                DefaultDuration = 45,
                Create = init => new SortRound(new[] { "Post it", "Rewrite" }, ContentKit.Deck(new DetRng(init.Seed), SafePool, 2, 12), init.MaxScore, init.Duration),
            };
        }

        private static FlagLine Ok(string t, string why = null) => new FlagLine(t, false, why);
        private static FlagLine Bad(string t, string why) => new FlagLine(t, true, why);

        private static readonly FlagBoard[] Drafts =
        {
            new FlagBoard
            {
                Title = "Launch post · $GLORPO", Brief = "Client: Glorpo Labs. Find 2 problems.",
                Lines = new[]
                {
                    Ok("gm Timeline Square."),
                    Ok("$GLORPO is live on Launchpad Tower."),
                    Bad("Guaranteed 50x by Friday, trust the chart.", "Nobody can guarantee returns. That line is a false promise."),
                    Ok("Mint authority revoked, LP burned (both on CityScan).", "Checkable facts are the strongest hook you have."),
                    Bad("Only 9 minutes left before it goes vertical!!", "Invented urgency pushes people to skip their own checks."),
                    Ok("Paid partnership with Glorpo Labs.", "Disclosure keeps the post honest."),
                    Ok("Read the docs before you ape."),
                },
            },
            new FlagBoard
            {
                Title = "KOL review · Solana Street Swap", Brief = "Paid review for a swap app. Find 2 problems.",
                Lines = new[]
                {
                    Ok("Been using Solana Street Swap for two weeks."),
                    Ok("Fees were lower than I expected on small swaps."),
                    Bad("Not an ad, just alpha.", "It is a paid post. Hiding the deal is the one thing you can't do."),
                    Bad("Claim your bonus at solana-street-swap-claim dot site", "A lookalike claim link is how drainers get signatures."),
                    Ok("Code CITY10 gives a fee rebate."),
                    Ok("Smart contracts can fail. Size small.", "A risk note is good copy, not a weakness."),
                },
            },
            new FlagBoard
            {
                Title = "Weekly recap thread", Brief = "Community thread for a Builder Hills team. Find 2 problems.",
                Lines = new[]
                {
                    Ok("1/ This week on Builder Hills:"),
                    Ok("2/ The team shipped wallet sign-in on phones."),
                    Bad("3/ The founder of a top exchange called us 'the future'.", "An endorsement you can't source is impersonation."),
                    Ok("4/ Treasury report is on the docs page."),
                    Bad("5/ Drop your wallet below for a surprise.", "'Drop your wallet' replies are the giveaway-scam template."),
                    Ok("6/ Next AMA: Thursday in Spaces Studio."),
                },
            },
            new FlagBoard
            {
                Title = "Airdrop announcement", Brief = "Season 1 points are final. Find 2 problems.",
                Lines = new[]
                {
                    Ok("Season 1 points are final."),
                    Ok("Check eligibility in the app, not in DMs."),
                    Bad("Our team will DM winners to verify wallets.", "Teams never DM first. This line trains users to trust drainers."),
                    Ok("Claim window: 14 days."),
                    Ok("Unclaimed tokens return to the treasury."),
                    Bad("Sign one quick message to unlock 3x points.", "Blind-signing bait. Points never need a mystery signature."),
                },
            },
            new FlagBoard
            {
                Title = "Perp Pit promo", Brief = "Ad copy for a perps venue. Find 2 problems.",
                Lines = new[]
                {
                    Ok("New: hourly funding shown before you open a trade."),
                    Ok("Max leverage depends on your Trading level."),
                    Bad("Safety mode means you can't get liquidated.", "Any leveraged position can be liquidated. False claim."),
                    Ok("Losses are real Cash. Size small."),
                    Bad("Promo ends in 2 minutes: go max long now.", "Urgency plus a reckless instruction."),
                    Ok("Fees are listed on every order ticket."),
                },
            },
            new FlagBoard
            {
                Title = "Sponsored 'I tried it' thread", Brief = "A paid walkthrough of a naira-to-USDC app. Find 2 problems.",
                Lines = new[]
                {
                    Ok("I tried funding a wallet from naira with Solana Street Pay so you don't have to. 1/"),
                    Ok("Sponsored by Solana Street Pay.", "Disclosure first. Good."),
                    Ok("Step 2: verify your ID in the app; it took me 6 minutes."),
                    Bad("Zero fees forever, guaranteed.", "Nobody can promise fees forever. False promise."),
                    Bad("P2P merchants are all scammers, only use this app.", "Smearing a whole group to sell a product."),
                    Ok("My first deposit landed in 4 minutes. Yours may differ."),
                },
            },
            new FlagBoard
            {
                Title = "Hiring post", Brief = "A studio is hiring a mod. Find 1 problem.",
                Lines = new[]
                {
                    Ok("Builder Hills studio is hiring a community mod."),
                    Ok("Paid in USDC, weekly."),
                    Bad("Send 0.5 SOL to secure your interview slot.", "Pay-to-apply is a scam pattern. Real jobs don't charge you."),
                    Ok("Apply through the form on our site."),
                    Ok("Two years of moderating required."),
                    Ok("Interview in a Space, camera optional."),
                },
            },
            new FlagBoard
            {
                Title = "Memecoin community post · $CHONK", Brief = "Cult-coin community copy. Find 3 problems.",
                Lines = new[]
                {
                    Ok("$CHONK holders: thank you for a wild week."),
                    Bad("$CHONK is not bundled, PURE community, next billion-dollar meme.", "'Not bundled' needs on-chain proof, and 'next billion' is a return promise."),
                    Ok("Holder count passed 3,000 this morning."),
                    Bad("Selling now means you hate the community.", "Guilt-tripping holders out of selling is manipulation."),
                    Ok("Community art contest closes Sunday."),
                    Bad("Influencers are buying, I can't say who.", "Unnamed insider hype can't be checked."),
                    Ok("Not financial advice. Never invest rent money."),
                },
            },
        };

        private sealed class CrunchPost
        {
            public string Title;
            public string[] Must, Optional;
        }

        private static readonly CrunchPost[] CrunchPosts =
        {
            new CrunchPost
            {
                Title = "Launch post · $GLORPO",
                Must = new[] { "$GLORPO is live on Launchpad Tower.", "Paid partnership with Glorpo Labs.", "Launch: Friday 18:00 UTC." },
                Optional = new[]
                {
                    "gm Timeline Square, big day for frog-adjacent amphibians and the people who love them.",
                    "LP is burned and mint authority is revoked, both visible on CityScan.",
                    "We spent six months on the art and honestly the hats alone took two of them.",
                    "Docs and the audit summary: glorpo.city/docs",
                    "Huge thanks to everyone who joined Tuesday's Spaces, you asked great questions.",
                },
            },
            new CrunchPost
            {
                Title = "Airdrop farm update",
                Must = new[] { "Season 2 points snapshot: Sunday 20:00 UTC.", "Check eligibility in the app only.", "Team will never DM you first." },
                Optional = new[]
                {
                    "What a season. 41,000 farmers, 3 bridges, one very tired community mod.",
                    "Sybil filter details are in the docs: same-funder clusters over 20 wallets are cut.",
                    "If you farmed from one wallet like a normal human, you are fine.",
                    "Claim window is 14 days, unclaimed tokens go back to the treasury.",
                    "Shout-out to the Sybil Barn for catching 9,000 clones in one afternoon.",
                },
            },
            new CrunchPost
            {
                Title = "Funding alert · $WAGMEOW",
                Must = new[] { "$WAGMEOW funding on Perp Pit flipped negative.", "Not financial advice.", "Data: Perp Pit funding page, 14:00 UTC." },
                Optional = new[]
                {
                    "Shorts now pay longs every hour, which almost never lasts long on a cat coin.",
                    "Open interest is up 40% since this morning while the spot price barely moved.",
                    "I have been wrong about cats before, ask my landlord.",
                    "If you trade this, size small; leverage turns a good read into a REKT card.",
                    "Thread on how hourly funding is calculated coming tonight.",
                },
            },
            new CrunchPost
            {
                Title = "Audit summary",
                Must = new[] { "Audit Row report for Solana Street Swap is out.", "1 high fixed, 2 lows acknowledged.", "Full report: link in bio." },
                Optional = new[]
                {
                    "We reviewed 4,200 lines over three weeks with two auditors and a lot of coffee.",
                    "The high was an oracle account that was not checked against the pool config.",
                    "Fixed in commit 9f2 and re-reviewed on Monday.",
                    "Lows are rounding in fee math, no deposits at risk.",
                    "We are hiring junior auditors, DMs closed, apply via the form.",
                },
            },
            new CrunchPost
            {
                Title = "Hackathon call",
                Must = new[] { "Builder Hills hackathon: Oct 20-22.", "Prize pool: 20,000 USDC.", "Apply: builderhills.city/hack" },
                Optional = new[]
                {
                    "Three days, one rule: ship something a stranger can use on their phone.",
                    "Mentors from the Spaces Studio crew will be around all weekend.",
                    "Tracks: wallets, payments, games and AI agents that pay their own API bills.",
                    "Last year a team built a tip jar in 40 hours and it still runs.",
                    "Pizza is fictional but the deadlines are real.",
                },
            },
        };

        public const int CrunchMin = 255, CrunchMax = 280;

        /// <summary>Lines keep their authored order (must-keep lines are mixed in, so spotting them is part of the job).</summary>
        private static ComboBoard BuildCrunch(CrunchPost post)
        {
            var lines = new List<(string text, bool must)>();
            lines.Add((post.Must[0], true));
            lines.Add((post.Optional[0], false));
            lines.Add((post.Optional[1], false));
            lines.Add((post.Must[1], true));
            lines.Add((post.Optional[2], false));
            lines.Add((post.Optional[3], false));
            lines.Add((post.Optional[4], false));
            lines.Add((post.Must[2], true));
            var slots = lines.Select(l =>
            {
                int chars = l.text.Length + 1;
                return new ComboSlot
                {
                    Name = l.text,
                    Options = new[]
                    {
                        new ComboOption("Keep · " + chars + " chars", chars),
                        new ComboOption("Cut", 0, l.must, l.must ? "You cut a must-keep line: \"" + l.text + "\"" : null),
                    },
                };
            }).ToArray();
            return new ComboBoard { Title = post.Title, Brief = "Land between " + CrunchMin + " and " + CrunchMax + " characters.", Unit = "chars", Slots = slots, Min = CrunchMin, Max = CrunchMax };
        }

        private static readonly SortCard[] SafePool =
        {
            ContentKit.Card(1, "\"Guaranteed 10x. Not financial advice.\"", "A disclaimer doesn't cancel a guarantee. Rewrite."),
            ContentKit.Card(1, "\"Like and reply 'hi' for a surprise in DMs.\"", "Engagement bait that moves people into DMs. Rewrite."),
            ContentKit.Card(1, "\"We're partnered with every major exchange (announcement soon).\"", "An unverifiable partnership claim. Rewrite."),
            ContentKit.Card(1, "\"Last 50 whitelist spots: send 1 SOL to the dev wallet.\"", "Asking people to send funds to a wallet is the scam pattern."),
            ContentKit.Card(1, "\"Wallet issues? Support will DM you to fix it.\"", "Real support never DMs first. Drainers do."),
            ContentKit.Card(1, "\"Our audit found zero risks. Zero.\"", "No audit finds zero risk. It also hides what the report says."),
            ContentKit.Card(1, "\"Just aped $SNACKY with my own money, not paid!\" (client paid 2 SOL)", "Undisclosed paid promotion. Add the disclosure."),
            ContentKit.Card(1, "\"Price only goes up from here, buy before the normies.\"", "A return promise plus FOMO."),
            ContentKit.Card(1, "\"Under the new rules our token is officially not a security. Ape safely.\"", "A legal conclusion nobody signed off on, plus 'safely'. Rewrite."),
            ContentKit.Card(1, "\"I tried this app and it changed my life 🧵\" (paid, no disclosure)", "A sponsored 'I tried it' thread must say it's sponsored. Rewrite."),
            ContentKit.Card(0, "\"Sponsored: I tried funding from naira with this app. Fees and timings below.\"", "Disclosed and specific. Post it."),
            ContentKit.Card(0, "\"Paid partnership: we're sponsoring Thursday's Space on wallet safety.\"", "Disclosed sponsorship with a useful topic. Post it."),
            ContentKit.Card(0, "\"Audit report is linked: two medium issues fixed, one acknowledged.\"", "Specific, sourced, honest. Post it."),
            ContentKit.Card(0, "\"Gas spiked during the mint. Refunds for failed transactions go out Monday.\"", "Honest bad news with a plan. Post it."),
            ContentKit.Card(0, "\"Deposits are paused while we investigate. Vault balance is on our dashboard.\"", "Clear, checkable incident update."),
            ContentKit.Card(0, "\"No token yet. Anyone selling one is not us.\"", "A safety notice. Post it."),
            ContentKit.Card(0, "\"Points are not tokens. Nothing is promised.\"", "Sets expectations honestly."),
            ContentKit.Card(0, "\"Bug bounty: up to 50k USDC for critical issues. Scope in our docs.\"", "Normal, verifiable programme. Post it."),
            ContentKit.Card(0, "\"We were wrong about the launch date. New date: the 14th. Sorry.\"", "Owning a mistake is fine copy."),
        };
    }
}
