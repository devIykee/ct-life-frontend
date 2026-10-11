using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// DevRel: community manager, Spaces host and partnerships in one seat. Anchors: AMAs and Spaces with
    /// host-chosen speakers and moderation tools (docs/research/ct-social-game-direction-20261006.md); "team will never
    /// DM you first" and drainer links in community channels; BD matching between builders and service providers
    /// (agent payments, oracles, wallets) seen across 2026 hackathon tracks.
    /// </summary>
    internal static class DevRelGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "devrel_ama", CareerId = "dev_rel", InContract = true, Engine = MiniGameEngine.Sort,
                Title = "AMA Triage", Tagline = "Questions flood in. Route each one.",
                Rules = new[]
                {
                    "Answer live: a real question only the team can answer right now.",
                    "Link the docs: already answered in the docs (fees, schedules, how-tos).",
                    "Mod it: spam, scams, impersonation. Escalate: anything that smells like an exploit or stolen funds.",
                },
                ScoringRule = "Right routes minus a third of a point per wrong route, out of the queue.",
                RealTask = "Running a project AMA in Spaces and on Discordia.",
                ActionLabel = "Answer live|Link the docs|Mod it|Escalate",
                DefaultDuration = 50,
                Create = init => new SortRound(new[] { "Answer live", "Link the docs", "Mod it", "Escalate" }, ContentKit.Deck(new DetRng(init.Seed), AmaPool, 4, 12), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "devrel_spaces", CareerId = "dev_rel", Engine = MiniGameEngine.Balance,
                Title = "Keep the Room Alive", Tagline = "Host the Space. Keep the energy above the line.",
                Rules = new[]
                {
                    "Room energy drains on its own. Events pop up and need the right move within a few seconds.",
                    "Mute trolls and shillers. Bring up people with real questions. Start a new topic when it goes quiet.",
                    "The wrong move, or no move, costs energy.",
                },
                ScoringRule = "Score is the share of the show the room spends above the line.",
                RealTask = "Hosting a Space: speaker requests, moderation and dead air.",
                DefaultDuration = 45,
                Create = init => new BalanceRound(SpacesSpec(new DetRng(init.Seed), init.Duration), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "devrel_partner_match", CareerId = "dev_rel", Engine = MiniGameEngine.Match,
                Title = "Partnership Match", Tagline = "Every builder needs something. Make the intro.",
                Rules = new[]
                {
                    "Left: what a team needs. Right: what a partner offers.",
                    "Pair each need with the partner that actually solves it. Keywords overlap on purpose.",
                    "File when every team has a partner.",
                },
                ScoringRule = "One point per correct intro.",
                RealTask = "BD intros between builders and infrastructure partners.",
                DefaultDuration = 55,
                Create = init => new MatchRound(ContentKit.Boards(new DetRng(init.Seed), Partners, 4), init.Seed, init.MaxScore, init.Duration),
            };
        }

        private static readonly SortCard[] AmaPool =
        {
            ContentKit.Card(0, "\"Why did you launch on a bonding curve instead of a presale?\"", "A reasoning question only the team can answer."),
            ContentKit.Card(0, "\"What's the one feature you cut to ship on time?\"", "A good live question."),
            ContentKit.Card(0, "\"Will sign-in work on phones without a browser wallet?\"", "Roadmap question for the team."),
            ContentKit.Card(0, "\"What happens to the treasury if the token dies?\"", "Hard but fair. Answer it live."),
            ContentKit.Card(1, "\"What are the fee tiers?\"", "Fee tables live in the docs."),
            ContentKit.Card(1, "\"How do I claim my points?\"", "A how-to: link the guide."),
            ContentKit.Card(1, "\"Where's the audit report?\"", "Link it; don't paraphrase an audit."),
            ContentKit.Card(1, "\"What's the token unlock schedule?\"", "Schedules belong in the docs, not in memory."),
            ContentKit.Card(2, "\"Admin, DM me, I can 10x your volume for 2 SOL.\"", "Paid-volume spam. Mod it."),
            ContentKit.Card(2, "\"Verify your wallet here: claim-froge dot site\"", "Phishing link. Mod it."),
            ContentKit.Card(2, "\"WEN TOKEN WEN TOKEN WEN TOKEN\"", "Spam. Mod it."),
            ContentKit.Card(2, "\"I'm the founder's backup account, send me your questions privately.\"", "Impersonation. Mod it."),
            ContentKit.Card(2, "\"Free course: build your own AI trading bot, step 1 paste your private key here.\"", "The self-drain tutorial scam. Mod it."),
            ContentKit.Card(1, "\"Is there a status page for withdrawals?\"", "Link the status page; don't improvise during an incident."),
            ContentKit.Card(0, "\"Dev wallet sold 5% today. Explain or it's a slow rug.\"", "Real FUD deserves a live answer with the wallet and the reason."),
            ContentKit.Card(2, "\"Paying well for OTC help, DM me.\" (fresh account)", "Hack launderers fishing for help. Mod it."),
            ContentKit.Card(2, "\"I'm hiring reply guys for your project, $1,500 a week, reply below.\"", "Hiring bait in your AMA. Mod it."),
            ContentKit.Card(3, "\"My withdrawal failed and the vault shows a different balance than my wallet.\"", "Possible accounting bug touching funds. Escalate."),
            ContentKit.Card(3, "\"Someone is minting points without depositing anything.\"", "Looks like an exploit. Escalate."),
            ContentKit.Card(3, "\"The TeleGrum link in your bio now redirects to a drainer.\"", "Compromised account. Escalate."),
            ContentKit.Card(3, "\"My funds moved right after I signed your 'claim' message.\"", "Possible drainer on an official path. Escalate."),
        };

        public static BalanceSpec SpacesSpec(DetRng rng, double duration)
        {
            const double seg = 0.5;
            int segments = (int)Math.Ceiling(duration / seg) + 2;
            var drift = new double[segments];
            for (int i = 0; i < segments; i++) drift[i] = -rng.Between(2.5, 4.5);
            var kinds = new (string kind, string[] texts)[]
            {
                ("mute", new[] { "A troll is yelling 'WEN TOKEN' over the speaker.", "A speaker starts reading out a coin's contract address.", "Someone's playing music into their mic.", "A listener on stage asks everyone to 'drop wallets'.", "A speaker pitches their web3 marketing services mid-AMA." }),
                ("invite", new[] { "A builder raised a hand: question about the SDK.", "The team's dev asked to speak.", "A listener requests to speak: found a bug.", "A regular raised a hand with a follow-up." }),
                ("topic", new[] { "Dead air. Everyone's waiting.", "Q&A is slowing down.", "The last speaker dropped off.", "Chat says 'what's next?'", "Co-host whispers: 'bring the energy'." }),
            };
            var cues = new List<BalanceCue>();
            double t = rng.Between(1.5, 2.5);
            while (t < duration - 3)
            {
                var k = kinds[rng.Next(kinds.Length)];
                cues.Add(new BalanceCue { At = t, Window = 3.0, Kind = k.kind, Text = rng.Pick(k.texts), Reward = rng.Between(14, 18), Penalty = -rng.Between(12, 16) });
                t += rng.Between(3.6, 5.2);
            }
            return new BalanceSpec
            {
                MeterLabel = "Room energy", LowLabel = "Dead", HighLabel = "Buzzing", Min = 0, Max = 100, Start = 72, BandLo = 55, BandHi = 100,
                SegmentSeconds = seg, Drift = drift, Cues = cues.ToArray(), MisfirePenalty = -6,
                Actions = new[]
                {
                    new BalanceAction { Label = "Mute", Kind = "mute", Cooldown = 0.6 },
                    new BalanceAction { Label = "Bring up", Kind = "invite", Cooldown = 0.6 },
                    new BalanceAction { Label = "New topic", Kind = "topic", Cooldown = 0.6 },
                },
            };
        }

        private static MatchBoard Pair(string title, string[] needs, string[] offers, string[] why) =>
            new MatchBoard { Title = title, Brief = "Make four intros.", Left = needs, Right = offers, Solution = new[] { 0, 1, 2, 3 }, Why = why };

        private static readonly MatchBoard[] Partners =
        {
            Pair("Hackathon House intros",
                new[] { "Memecoin team: thin liquidity on its DEX pair", "NFT studio: mints fail on phones", "Perp DEX: needs prices for new markets", "Wallet app: nobody tries its new feature" },
                new[] { "Market-making desk with idle inventory", "Launchpad with a mobile mint page", "Oracle network adding price feeds", "Quest platform with an active community" },
                new[] { "Liquidity comes from a market maker.", "A working mobile mint page.", "Prices come from an oracle.", "Quests bring users to try features." }),
            Pair("Builder Hills intros",
                new[] { "AI agent startup: bills users per API call", "Prediction market: needs trusted resolution", "Mobile game: needs wallet sign-in", "Hardware project: needs testers" },
                new[] { "Stablecoin pay-per-call rail (HTTP 402)", "Data oracle with dispute bonds", "Mobile wallet adapter SDK team", "Builder community with spare test phones" },
                new[] { "Per-call payments need a pay-per-call rail.", "Resolution needs an oracle with disputes.", "Sign-in needs a wallet adapter.", "Testers come from a community with devices." }),
            Pair("VC Row intros",
                new[] { "Lending app: audit before launch", "DAO: treasury sits in one hot wallet", "Exchange listing: needs proof of reserves", "Launchpad: bots snipe every launch" },
                new[] { "Audit firm on Audit Row", "Multisig and custody tooling team", "On-chain attestation service", "Anti-sniper launch tooling" },
                new[] { "An audit firm audits.", "A multisig fixes the single hot wallet.", "Attestations prove reserves.", "Anti-sniper tools stop sniping." }),
            Pair("Spaces Studio intros",
                new[] { "Project: wants a weekly community show", "Creator: wants to sell paid calls", "Podcast: needs clean transcripts", "Community: wants fewer scam DMs" },
                new[] { "Spaces host with a regular audience", "Paid-call escrow app", "Speech-to-text service", "Moderation bot with DM warnings" },
                new[] { "A host with an audience runs the show.", "Paid calls need escrow.", "Transcripts come from speech-to-text.", "A moderation bot warns about scam DMs." }),
            Pair("COIN2050 Conference Week intros",
                new[] { "Bank: wants tokenized-stock trading rails", "Treasury company: wants custody for its coin pile", "Perp venue: wants oil and gold markets", "Side event: needs 1,000 lanyards by Thursday" },
                new[] { "Tokenization platform for listed equities", "Institutional custody desk", "Commodity price oracle", "Event merch printer with rush orders" },
                new[] { "Tokenized stocks need a tokenization platform.", "Custody comes from a custody desk.", "Commodity perps need commodity prices.", "Lanyards come from a printer." }),
            Pair("Lagos builders' night intros",
                new[] { "Payments app: users need a naira on-ramp", "Web3 creator: wants a monthly retainer, not airdrops", "Hausa-speaking community: wants AMAs in Hausa", "Startup Village team: needs pitch practice" },
                new[] { "Verified P2P merchant network", "Project hiring a part-time community manager", "Spaces host fluent in Hausa", "Demo-day mentor from Builder Hills" },
                new[] { "An on-ramp needs P2P merchants.", "A retainer comes from a project that's hiring.", "Hausa AMAs need a Hausa-speaking host.", "Pitch practice comes from a demo-day mentor." }),
            Pair("ORE Refinery intros",
                new[] { "Staking app: users can't see their yield", "Refinery event: needs a giant live board", "Miner group: wants a shared treasury", "Payments app: wants ORE checkout" },
                new[] { "Analytics dashboard team", "Event screen and display crew", "Multisig treasury tooling", "Token payment checkout SDK" },
                new[] { "Analytics shows the yield.", "A display crew builds the board.", "A shared treasury needs a multisig.", "Checkout needs a payment SDK." }),
        };
    }
}
