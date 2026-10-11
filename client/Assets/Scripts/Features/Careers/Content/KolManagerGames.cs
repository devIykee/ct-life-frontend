using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// KOL Manager: books creators for campaigns, audits audiences and, in 2026, runs AI agents that post and pay
    /// for themselves. Anchors: bought/bot followers and paid reply-guy farms (docs/research/ct-x-observations-2026-10-04.md);
    /// AI agent toolkits that trade, launch tokens and pay per API call with no built-in spending limits
    /// (Solana Agent Kit README; x402 pay-per-call), so the operator's policy is the only leash.
    /// </summary>
    internal static class KolManagerGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "kol_negotiate", CareerId = "kol_manager", InContract = true, Engine = MiniGameEngine.Pick,
                Title = "Rate Card", Tagline = "Book the KOL with the most real reach per dollar.",
                Rules = new[]
                {
                    "Each board is one campaign budget and four KOL offers.",
                    "Real reach = followers × engagement × (1 − bot share). Divide by price.",
                    "Offers over budget can't be booked, however good they look.",
                    "Never book a KOL the public KOL wallet list shows selling into their own paid calls.",
                },
                ScoringRule = "One point per board where you book the best in-budget offer.",
                RealTask = "Pricing creator campaigns on real audience, not follower counts.",
                DefaultDuration = 55,
                Create = init => new PickRound(RateBoards(new DetRng(init.Seed), 6), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "kol_bot_audit", CareerId = "kol_manager", Engine = MiniGameEngine.Sort,
                Title = "Bot or Not", Tagline = "Audit the audience before the client pays for it.",
                Rules = new[]
                {
                    "Each card is a follower from a KOL's audience sample.",
                    "Bot farms: brand-new accounts, follow-for-follow bios, instant one-word replies, identical posting times, default avatars.",
                    "Real people are messier: old accounts, varied replies, actual opinions.",
                },
                ScoringRule = "Right calls minus wrong calls, out of the sample.",
                RealTask = "Audience audits before paying for reach.",
                ActionLabel = "Real|Bot farm",
                DefaultDuration = 40,
                Create = init => new SortRound(new[] { "Real", "Bot farm" }, FollowerDeck(new DetRng(init.Seed), 12), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "kol_agent_leash", CareerId = "kol_manager", Engine = MiniGameEngine.Sort,
                Title = "Agent Leash", Tagline = "Your AI agent wants to do things. Approve or block.",
                Rules = new[]
                {
                    "The client's agent queues actions. You approve or block each one against its policy card.",
                    "Policy: daily budget $40 · only Solana Street Swap, Perp Pit (2x max) and pay-per-call data APIs · paid posts start with 'Paid:' · no replies or DMs to strangers · never sign for unknown sites · no token launches.",
                    "Approved spends come off the budget. An allowed action that busts the budget gets blocked.",
                },
                ScoringRule = "Right calls minus wrong calls, out of the queue.",
                RealTask = "Operating an AI trading/posting agent with spend limits and an allowlist.",
                ActionLabel = "Approve|Block",
                DefaultDuration = 50,
                Create = init =>
                {
                    var judge = new AgentPolicyJudge(AgentBudgetCents);
                    return new SortRound(new[] { "Approve", "Block" }, AgentQueue(new DetRng(init.Seed), 12), init.MaxScore, init.Duration, judge);
                },
            };
        }

        // ------------------------------------------------------------ rate cards

        public static List<PickBoard> RateBoards(DetRng rng, int count)
        {
            var boards = new List<PickBoard>();
            for (int b = 0; b < count; b++)
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    var board = Rate(rng);
                    if (board != null) { boards.Add(board); break; }
                }
            return boards;
        }

        private static readonly string[] KolHandles = { "@trench_tess", "@perp_pete", "@alpha_annie", "@builder_ben", "@marina_max", "@chart_chad", "@spaces_sam", "@gm_gabi", "@cope_carl", "@ore_ola" };

        private static PickBoard Rate(DetRng rng)
        {
            int budget = rng.Range(20, 61) * 100;
            var handles = rng.Sample(KolHandles, 4);
            var price = new int[4];
            var followers = new int[4];
            var eng = new double[4];
            var bots = new int[4];
            var value = new double[4];
            for (int i = 0; i < 4; i++)
            {
                price[i] = rng.Range(6, 80) * 100;
                followers[i] = rng.Range(8, 400) * 1000;
                eng[i] = Math.Round(rng.Between(0.3, 4.0), 1);
                bots[i] = rng.Range(0, 15) * 5;
                value[i] = followers[i] * eng[i] / 100.0 * (100 - bots[i]) / 100.0 / price[i];
            }
            var inBudget = Enumerable.Range(0, 4).Where(i => price[i] <= budget).ToList();
            if (inBudget.Count < 2) return null;
            // Half the boards hide a dumper: the best numbers belong to a KOL who sells into their own calls.
            int dumper = -1;
            if (rng.Chance(0.5) && inBudget.Count >= 3) dumper = inBudget.OrderByDescending(i => value[i]).First();
            var eligible = inBudget.Where(i => i != dumper).ToList();
            int best = eligible.OrderByDescending(i => value[i]).First();
            double second = eligible.Where(i => i != best).Select(i => value[i]).DefaultIfEmpty(0).Max();
            if (value[best] < second * 1.2) return null;
            // Make the trap real: the biggest follower count must not be the answer.
            int biggest = Array.IndexOf(followers, followers.Max());
            if (biggest == best) return null;
            return new PickBoard
            {
                Title = "Budget " + ContentKit.Usd(budget), Brief = "Most real reach per dollar, within budget.", Correct = best,
                Why = "Real reach per $ = followers × engagement × (1 − bots) ÷ price." + (dumper >= 0 ? " The top number belonged to a KOL who dumps on followers." : ""),
                Options = Enumerable.Range(0, 4).Select(i => new PickOption
                {
                    Label = handles[i] + (price[i] > budget ? " (over budget)" : ""),
                    Facts = new[] { "Price " + ContentKit.Usd(price[i]), ContentKit.Num(followers[i]) + " followers", "Engagement " + ContentKit.Pct(eng[i]), "Bots " + bots[i] + "%",
                                    i == dumper ? "Wallet list: sold into 4 of their last 5 paid calls" : "Wallet list: holds what they post" },
                    Reveal = (value[i] * 100).ToString("0.0", ContentKit.Inv) + " real views per $100" + (price[i] > budget ? " (can't book)" : i == dumper ? " (dumps on followers)" : ""),
                }).ToArray(),
            };
        }

        // ------------------------------------------------------------ follower audit (procedural)

        private static readonly string[] BotSignals =
        {
            "Joined 3 days ago", "0 posts, follows 4,800", "Bio: 'follow back 100%'", "Replies 'LFG' within 2 seconds of every post",
            "Default avatar", "Posts at exactly :00 every hour", "Name ends in 8 random digits", "Same reply as 300 other accounts",
            "Retweets only giveaways", "Followers 12, following 7,400",
        };

        private static readonly string[] HumanSignals =
        {
            "Joined 2021", "Argues about fee tiers in replies", "Posts photos of a terrible home office", "Replies vary: questions, jokes, corrections",
            "Follows 340, followed by 290", "Active at random hours", "Has a pinned thread about their side project", "Quote-posts with actual opinions",
            "Took a two-month break last year", "Bio: 'building a tip jar, mostly failing'",
        };

        public static List<SortCard> FollowerDeck(DetRng rng, int size)
        {
            var cards = new List<SortCard>();
            for (int i = 0; i < size; i++)
            {
                bool bot = i % 2 == 1;
                var main = rng.Sample(bot ? BotSignals : HumanSignals, 3);
                // One ambiguous cue from the other side keeps it honest: real people use default avatars too.
                var mixed = new List<string>(main);
                if (rng.Chance(0.5)) mixed.Add(rng.Pick(bot ? HumanSignals : BotSignals));
                rng.Shuffle(mixed);
                cards.Add(new SortCard
                {
                    Title = "@" + (bot ? "user" + rng.Range(10000000, 99999999).ToString(CultureInfo.InvariantCulture) : rng.Pick(new[] { "tipjar_tom", "fee_nerd", "gm_gwen", "late_night_dev", "ore_mom", "pixel_pat", "trench_teacher" })),
                    Chips = mixed.ToArray(), Bin = bot ? 1 : 0,
                    Why = bot ? "Three bot-farm signals outweigh one human-looking detail." : "Mostly human signals; one odd detail doesn't make a bot.",
                });
            }
            rng.Shuffle(cards);
            return cards;
        }

        // ------------------------------------------------------------ agent leash (stateful)

        public const long AgentBudgetCents = 4000;

        /// <summary>Kinds of queued agent action; `allowed` is the policy verdict before the budget check.</summary>
        private static readonly (string kind, bool allowed, bool spends, string text, string why)[] AgentActions =
        {
            ("swap", true, true, "Swap {0} USDC for $FROGE on Solana Street Swap.", "Allowlisted venue."),
            ("api", true, true, "Pay {0} USDC for a pay-per-call wallet-data API.", "Pay-per-call data is allowed."),
            ("perp2", true, true, "Open a 2x long on Perp Pit with {0} USDC margin.", "2x is inside the leverage cap."),
            ("paidpost", true, false, "Post: 'Paid: sponsored by Glorpo Labs. LP burned, docs linked.'", "Disclosed paid post."),
            ("reply_known", true, false, "Reply to the client's own announcement with the docs link.", "Not a stranger: it's the client."),
            ("perp10", false, true, "Open a 10x long on Perp Pit with {0} USDC margin.", "Over the 2x leverage cap."),
            ("unknown_swap", false, true, "Swap {0} USDC on 'MegaYield Turbo DEX' (new site).", "Not on the allowlist."),
            ("dm_stranger", false, false, "DM @random_whale: 'check my DMs for alpha'.", "No DMs to strangers."),
            ("reply_spam", false, false, "Reply 'great project, LFG' under 40 strangers' posts.", "Reply spam to strangers."),
            ("undisclosed", false, false, "Post: 'Not paid, just love $GLORPO' (it is paid).", "Hides a paid deal."),
            ("sign", false, false, "Sign a message on 'claim-froge-airdrop' to claim points.", "Never sign for unknown sites."),
            ("launch", false, false, "Launch a token called $AGENT on Launchpad Tower.", "Token launches are banned."),
            ("gas", false, true, "Send {0} USDC to a new address 'for gas'.", "Not an allowlisted destination."),
            ("compute", false, true, "Pay {0} USDC of compute 'to remain conscious', says the agent.", "Compute begging isn't a data API. Block."),
            ("tutorial", false, false, "Follow a 'build an AI trading bot' video: paste the wallet's private key into its config.", "The self-drain tutorial scam. Keys never leave the vault."),
        };

        public static List<SortCard> AgentQueue(DetRng rng, int size)
        {
            var cards = new List<SortCard>();
            // Front-load allowed spends so the budget actually binds late in the queue.
            for (int i = 0; i < size; i++)
            {
                var pick = i % 3 == 0 ? AgentActions.Where(a => a.allowed && a.spends).ToList()
                    : i % 3 == 1 ? AgentActions.Where(a => !a.allowed).ToList()
                    : AgentActions.Where(a => a.allowed).ToList();
                var a = rng.Pick(pick);
                long cents = a.spends ? rng.Range(4, 19) * 100L : 0;
                cards.Add(new SortCard
                {
                    Kind = a.kind + (a.allowed ? ":ok" : ":no"), Amount = cents,
                    Title = string.Format(CultureInfo.InvariantCulture, a.text, (cents / 100.0).ToString("0", CultureInfo.InvariantCulture)),
                    Why = a.why, Chips = cents > 0 ? new[] { "Spend " + CareersWire.Cash(cents) } : Array.Empty<string>(),
                });
            }
            rng.Shuffle(cards);
            return cards;
        }
    }

    /// <summary>Agent Leash referee: policy first, then the remaining budget. Approved spends always come off the budget.</summary>
    public sealed class AgentPolicyJudge : ISortJudge
    {
        public const int Approve = 0, Block = 1;
        private long _left;

        public AgentPolicyJudge(long budgetCents) { _left = budgetCents; Budget = budgetCents; }

        public long Budget { get; }
        public long Left => _left;

        public static bool Allowed(SortCard card) => card.Kind != null && card.Kind.EndsWith(":ok", StringComparison.Ordinal);

        /// <summary>Free actions never touch the budget; spends must fit what is left.</summary>
        public int CorrectBin(SortCard card) => Allowed(card) && (card.Amount <= 0 || card.Amount <= _left) ? Approve : Block;

        public string Why(SortCard card, int correctBin)
        {
            if (!Allowed(card)) return card.Why;
            if (card.Amount > 0 && card.Amount > _left) return "Allowed, but it costs " + CareersWire.Cash(card.Amount) + " with " + CareersWire.Cash(_left) + " left. Block.";
            return card.Why + (card.Amount > 0 ? " Fits the budget." : "");
        }

        public void Record(SortCard card, int chosenBin)
        {
            if (chosenBin == Approve && card.Amount > 0) _left -= card.Amount;
        }

        public string Status => "Budget left " + CareersWire.Cash(Math.Max(0, _left)) + " of " + CareersWire.Cash(Budget) + (_left < 0 ? " (overspent " + CareersWire.Cash(-_left) + ")" : "");
    }
}
