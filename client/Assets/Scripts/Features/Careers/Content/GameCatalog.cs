using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Every mini-game the client can play: the 9 Phase 1 games already in CTLife.UI (played through their own
    /// hosts, Engine = Legacy) and the 33 career games in this assembly. 42 total, 3 per career for all 14 careers.
    /// </summary>
    public static class GameCatalog
    {
        private static MiniGameCatalog _catalog;

        public static MiniGameCatalog Instance => _catalog ??= new MiniGameCatalog(AllSpecs());

        public static IEnumerable<MiniGameSpec> AllSpecs() =>
            Legacy()
                .Concat(CopywriterGames.Specs()).Concat(DesignerGames.Specs()).Concat(AuditorGames.Specs())
                .Concat(YieldFarmerGames.Specs()).Concat(ShillRunnerGames.Specs()).Concat(ResearcherGames.Specs())
                .Concat(TrendHunterGames.Specs()).Concat(KolManagerGames.Specs()).Concat(DevRelGames.Specs())
                .Concat(AlphaLeadGames.Specs()).Concat(RugSleuthGames.Specs());

        private static MiniGameSpec Old(string id, string career, string title, string tagline, int max, int sec, bool inContract) => new MiniGameSpec
        {
            Id = id, CareerId = career, Title = title, Tagline = tagline, Engine = MiniGameEngine.Legacy, DefaultMaxScore = max,
            DefaultDuration = sec, InContract = inContract, Rules = new[] { tagline, "This game shows its own rules when it opens." },
            ScoringRule = "Scored by the game's own rules, capped at the registry maximum.",
        };

        private static IEnumerable<MiniGameSpec> Legacy()
        {
            yield return Old("discord_mod_ban", "discord_mod", "Mod Desk", "Ban the drainer DMs. Spare the real ones.", 10, 45, true);
            yield return Old("discord_mod_raid_router", "discord_mod", "Raid Router", "Shield the chat from raid waves; let real chat through.", 100, 45, false);
            yield return Old("discord_mod_role_lock", "discord_mod", "Role Lock", "Least-privilege roles for a Discord Heights server.", 100, 50, false);
            yield return Old("junior_analyst_earnings", "junior_analyst", "Earnings Desk", "Read the report, get the numbers right.", 100, 60, true);
            yield return Old("junior_analyst_receipt_match", "junior_analyst", "Receipt Match", "Reconcile bank lines against receipts.", 100, 50, false);
            yield return Old("junior_analyst_runway_plan", "junior_analyst", "Runway Plan", "Cut the burn without breaking the service.", 100, 60, false);
            yield return Old("kols_engagement", "kol_engagement", "Thread Workshop", "Build the thread that actually informs.", 50, 50, true);
            yield return Old("kol_engagement_clip_cut", "kol_engagement", "Clip Cut", "Cut the Spaces clip to the setup and payoff.", 100, 50, false);
            yield return Old("kol_engagement_live_cues", "kol_engagement", "Live Cues", "Run the show: camera, caption, applause on cue.", 100, 45, false);
        }
    }

    /// <summary>CT-native copy for careers and gigs: blurbs, level titles, gig taglines and outcomes. FE copy only.</summary>
    public static class CareerFlavor
    {
        private sealed class Flavor
        {
            public string Venue, Blurb;
            public string[] Titles;
        }

        private static readonly Dictionary<string, Flavor> Careers = new Dictionary<string, Flavor>
        {
            ["discord_mod"] = new Flavor { Venue = "Discord Heights", Blurb = "Keep the chat alive and the drainers out.", Titles = new[] { "Trial Mod", "Mod", "Senior Mod", "Head Mod", "Community Lead" } },
            ["junior_analyst"] = new Flavor { Venue = "Terminal Café", Blurb = "Numbers, receipts and runway for CT City's teams.", Titles = new[] { "Intern", "Junior Analyst", "Analyst", "Senior Analyst", "Head of Research" } },
            ["kol_engagement"] = new Flavor { Venue = "Spaces Studio", Blurb = "Threads, clips and shows people actually finish.", Titles = new[] { "Reply Guy", "Poster", "Thread Writer", "Show Host", "Main Character" } },
            ["copywriter"] = new Flavor { Venue = "Hackathon House", Blurb = "Launch posts and paid-partnership copy that won't get your client sued.", Titles = new[] { "Bounty Hunter", "Copywriter", "Senior Copywriter", "Content Lead", "Head of Narrative" } },
            ["graphic_designer"] = new Flavor { Venue = "Timeline Square", Blurb = "Billboards, Pixel Wall tiles and trait tables.", Titles = new[] { "Meme Maker", "Designer", "Senior Designer", "Art Director", "Creative Director" } },
            ["smart_contract_auditor"] = new Flavor { Venue = "Audit Row", Blurb = "Find the bug before someone drains the vault.", Titles = new[] { "Bug Hunter", "Auditor", "Senior Auditor", "Lead Auditor", "Security Partner" } },
            ["yield_farmer"] = new Flavor { Venue = "Airdrop Farm", Blurb = "Real yield, clean claims, no sybil jail.", Titles = new[] { "Points Farmer", "Yield Farmer", "LP Strategist", "Vault Manager", "Head of Yield" } },
            ["shill_runner"] = new Flavor { Venue = "Timeline Square", Blurb = "Signal, not slop. From reply gigs to a monthly retainer.", Titles = new[] { "Reply Guy", "Gig Poster", "Campaign Runner", "Retainer Marketer", "Head of Growth" } },
            ["doxx_researcher"] = new Flavor { Venue = "Audit Row", Blurb = "Receipts over rumours. Fictional accounts only.", Titles = new[] { "Lurker", "Researcher", "Investigator", "Senior Investigator", "Head Sleuth" } },
            ["trend_hunter"] = new Flavor { Venue = "Prediction Pit", Blurb = "Narratives, odds and coin life cycles.", Titles = new[] { "Tourist", "Trend Spotter", "Trend Hunter", "Narrative Lead", "Oracle" } },
            ["kol_manager"] = new Flavor { Venue = "Cabal Lounge", Blurb = "Book real reach, audit fake reach, leash the AI agents.", Titles = new[] { "Booker", "KOL Manager", "Talent Lead", "Agency Partner", "Agency Founder" } },
            ["dev_rel"] = new Flavor { Venue = "Spaces Studio", Blurb = "AMAs, Spaces and intros for CT City's builders.", Titles = new[] { "Community Helper", "DevRel", "Senior DevRel", "Head of DevRel", "Ecosystem Lead" } },
            ["alpha_lead"] = new Flavor { Venue = "Perp Pit", Blurb = "Trenches exits, basis trades and a balanced book.", Titles = new[] { "Trencher", "Trader", "Desk Trader", "Desk Lead", "Head of Trading" } },
            ["rug_sleuth"] = new Flavor { Venue = "Audit Row", Blurb = "Follow the money until it hits an exchange.", Titles = new[] { "Watcher", "Sleuth", "Senior Sleuth", "Lead Investigator", "Legendary Sleuth" } },
        };

        public static string Venue(string careerId) => careerId != null && Careers.TryGetValue(careerId, out var f) ? f.Venue : null;
        public static string Blurb(string careerId) => careerId != null && Careers.TryGetValue(careerId, out var f) ? f.Blurb : null;

        public static string LevelTitle(string careerId, int level)
        {
            if (careerId == null || !Careers.TryGetValue(careerId, out var f) || level < 1) return null;
            return f.Titles[System.Math.Min(level, f.Titles.Length) - 1];
        }

        public static bool Known(string careerId) => careerId != null && Careers.ContainsKey(careerId);

        // ------------------------------------------------------------ gigs

        private static readonly Dictionary<string, (string tagline, Dictionary<string, string> outcomes)> Gigs = new Dictionary<string, (string, Dictionary<string, string>)>
        {
            // Contract gigs (config/gigs.json).
            ["doot_feed"] = ("Boost three good posts on the in-game Timeline. Signal only.", new Dictionary<string, string>()),
            ["drop_alpha"] = ("Post one checkable market fact to the Timeline.", new Dictionary<string, string>()),
            ["write_thread"] = ("Write a sponsored 'I tried it' thread for a Builder Hills team (disclosed).", new Dictionary<string, string>()),
            ["audit_token"] = ("Run a rug scan on a fresh Launchpad Tower coin.", new Dictionary<string, string>()),
            // Backend checkout gigs (server/application/gigs.ts), given CT City flavour.
            ["food_delivery"] = ("Ramen runs to the Hacker House. Longer routes pay more.", new Dictionary<string, string>
            { ["short_route"] = "Quick run across Timeline Square.", ["mid_route"] = "Across town to Builder Hills.", ["long_route"] = "All the way out to Dubai Marina." }),
            ["data_entry"] = ("Reconcile a P2P merchant's naira-to-USDC orders before payday.", new Dictionary<string, string>
            { ["perfect"] = "Every order reconciled. No chargebacks.", ["good"] = "Mostly clean. Two orders need a second look.", ["sloppy"] = "Some rows will haunt the merchant." }),
            ["dog_walking"] = ("Walk the Marina's dog-coin mascots around Solana Street.", new Dictionary<string, string>
            { ["one_dog"] = "One very good boy.", ["two_dogs"] = "Two leashes, no incidents.", ["big_pack"] = "A whole pack. Respect." }),
            ["ad_clicks"] = ("Test the Pixel Wall ad links for the review team.", new Dictionary<string, string>
            { ["light"] = "A light session.", ["regular"] = "A solid testing session.", ["click_storm"] = "You clicked everything. Twice." }),
            ["social_moderation"] = ("Mod the Discord Heights overnight shift: drainers, hiring bait and hack launderers.", new Dictionary<string, string>
            { ["short_shift"] = "Quiet night, two drainers banned.", ["normal_shift"] = "Normal shift, normal scams.", ["long_shift"] = "Long night. The raid did not get through." }),
        };

        public static string GigTagline(string gigId) => gigId != null && Gigs.TryGetValue(gigId, out var g) ? g.tagline : null;

        public static string GigOutcome(string gigId, string outcomeId)
        {
            if (gigId != null && outcomeId != null && Gigs.TryGetValue(gigId, out var g) && g.outcomes.TryGetValue(outcomeId, out var text)) return text;
            return null;
        }
    }
}
