using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Shill Runner: the reply-guy and engagement economy. Anchors: X cut off pay-to-post "InfoFi" apps on
    /// 15 Jan 2026 citing AI slop and reply spam (Decrypt); the giveaway / "drop your wallet" / "reply hi for a
    /// surprise" templates and paid reply-guy farms seen on CT (docs/research/ct-x-observations-2026-10-04.md);
    /// the growth-ladder meme "0-10K be a reply guy". The games reward signal over bait.
    /// </summary>
    internal static class ShillRunnerGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "shill_post", CareerId = "shill_runner", InContract = true, Engine = MiniGameEngine.Sort,
                Title = "Bait or Alpha", Tagline = "Engagement bait vs real alpha. Sort the feed.",
                Rules = new[]
                {
                    "Alpha is specific and checkable: a wallet, a transaction, a docs change, a number with a source.",
                    "Bait asks for likes, replies, wallets or DMs, flexes unprovable PnL, or promises a moon.",
                    "Speed matters, but a wrong call costs a right one.",
                },
                ScoringRule = "Right calls minus wrong calls, out of the feed.",
                RealTask = "Curating a feed worth quoting in a post-InfoFi Timeline.",
                ActionLabel = "Bait|Alpha",
                DefaultDuration = 45,
                Create = init => new SortRound(new[] { "Bait", "Alpha" }, ContentKit.Deck(new DetRng(init.Seed), FeedPool, 2, 12), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "shill_room_match", CareerId = "shill_runner", Engine = MiniGameEngine.Match,
                Title = "Right Shill, Right Room", Tagline = "Every room has rules. Break them and you're banned.",
                Rules = new[]
                {
                    "Left: rooms and their rules. Right: messages you've drafted.",
                    "Tap a room, then the message that fits its rules. Tap a paired room to undo.",
                    "File when all rooms have a message.",
                },
                ScoringRule = "One point per message in the room whose rules it follows.",
                RealTask = "Posting one campaign across Discordia, TeleGrum chats, the Timeline and Spaces without getting banned.",
                DefaultDuration = 55,
                Create = init => new MatchRound(ContentKit.Boards(new DetRng(init.Seed), Rooms, 4), init.Seed, init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "shill_reply_timing", CareerId = "shill_runner", Engine = MiniGameEngine.Timing,
                Title = "Top Reply", Tagline = "Reply while the post climbs, before the pile-on.",
                Rules = new[]
                {
                    "A big account posts. Views per minute climb, peak and fade; replies pile up.",
                    "Your reply is seen most when views are still strong and few replies are ahead of you.",
                    "Never reply to bait (giveaways, 'drop your wallet'): it gets you muted. Let those pass.",
                },
                ScoringRule = "Each post scores your reply's reach against the best moment; passing on bait scores full.",
                RealTask = "Reply-guy timing on big accounts without becoming reply spam.",
                ActionLabel = "Reply",
                DefaultDuration = 45,
                Create = init => new TimingRound(ReplyWindows(new DetRng(init.Seed), init.Duration), init.MaxScore, init.Duration),
            };
        }

        private static readonly SortCard[] FeedPool =
        {
            ContentKit.Card(0, "\"Like, RT and drop your wallet: 10 winners get 1 SOL each. Join my TeleGrum.\"", "The giveaway template. Bait."),
            ContentKit.Card(0, "\"Reply 'hi' for a surprise in DMs.\"", "Pulls people into DMs. Bait."),
            ContentKit.Card(0, "\"GUARANTEED 100X. Huge cabal launch. Who wants early CA?\"", "A guarantee and a secret. Bait."),
            ContentKit.Card(0, "\"This coin will change your life. Not saying which. DM me.\"", "Unverifiable and DM-gated. Bait."),
            ContentKit.Card(0, "\"+700 SOL today, 99.99% win rate. Join my premium.\" (screenshot)", "A screenshot proves nothing and sells a course. Bait."),
            ContentKit.Card(0, "\"gm fam! What are we building today? Drop your project below!\"", "Engagement farming prompt. Bait."),
            ContentKit.Card(0, "\"Tag 3 friends who need to see this.\"", "Asks for engagement, gives nothing. Bait."),
            ContentKit.Card(0, "\"Bull is back?? Thoughts? Wrong answers only.\"", "Reply farming with no information. Bait."),
            ContentKit.Card(0, "\"Quote this post with your username and why you deserve the airdrop.\"", "A loyalty-test airdrop: free engagement for the project, random reward. Bait."),
            ContentKit.Card(0, "\"Shill me your hidden gem\" (green candle image)", "Engagement bait. Bait."),
            ContentKit.Card(0, "\"This guy turned $1K into $15M this year. If you're not following him...\"", "A tracker legend post with no wallet to check. Bait."),
            ContentKit.Card(0, "\"I'm hiring! Reply guys, 3+ hours a day, $1,500 a week. Reply below.\"", "Hiring bait: a fake job that farms replies. Bait."),
            ContentKit.Card(0, "\"GOOD MORNING SOLANA MAXIS: if SOL breaks $120 today, 1 SOL to someone in the replies.\"", "A price-trigger giveaway. Bait."),
            ContentKit.Card(0, "\"Warning: the dev is about to scam $CHONK holders again, chart looks for 150M next. Set a reminder.\"", "A reverse-psychology shill dressed as a warning. Bait."),
            ContentKit.Card(1, "\"$GLORPO dev wallet just sent 30% of supply to a fresh wallet. Tx on CityScan, 4 min ago.\"", "Specific, timed, checkable. Alpha."),
            ContentKit.Card(1, "\"Launchpad Tower lowers the graduation market cap from Monday (docs changed today).\"", "A sourced rule change. Alpha."),
            ContentKit.Card(1, "\"$FROGE LP burn confirmed and mint authority revoked. Still not advice.\"", "Verifiable token facts. Alpha."),
            ContentKit.Card(1, "\"Perp Pit funding on $WAGMEOW flipped negative: shorts now pay longs hourly.\"", "A checkable market fact. Alpha."),
            ContentKit.Card(1, "\"Airdrop Farm season 2 snapshot is Sunday 20:00 UTC per the team's docs.\"", "Dated and sourced. Alpha."),
            ContentKit.Card(1, "\"Sybil Barn published its filter: same-funder clusters over 20 wallets get cut.\"", "Sourced, useful, specific. Alpha."),
            ContentKit.Card(1, "\"Audit Row report for Solana Street Swap: 1 high fixed, 2 lows open. Link in reply.\"", "Sourced security info. Alpha."),
            ContentKit.Card(1, "\"14 wallets bought $SPLORT in the launch slot, all funded by 8Kp…w2. Thread below.\"", "Checkable on-chain evidence. Alpha."),
            ContentKit.Card(1, "\"Launchpad Tower added holder rewards and dropped cashback mode today (announcement linked).\"", "A sourced rule change that changes how launches trade. Alpha."),
            ContentKit.Card(1, "\"$GLORPO creator fees now go to a community treasury, not the dev (tx linked).\"", "Checkable and it changes who sells. Alpha."),
        };

        private static MatchBoard Room(string title, string[] rooms, string[] msgs, string[] why) =>
            new MatchBoard { Title = title, Brief = "Pair every room with the message that follows its rules.", Left = rooms, Right = msgs, Solution = new[] { 0, 1, 2, 3 }, Why = why };

        private static readonly MatchBoard[] Rooms =
        {
            Room("Campaign: $GLORPO launch",
                new[]
                {
                    "Discord Heights #general: no shilling, links only in #share",
                    "Group chat 'Trench Boys': no links from new members",
                    "Timeline post: 280 chars, ticker + one link, disclose paid deals",
                    "Spaces Studio Q&A: one spoken question, no tickers",
                },
                new[]
                {
                    "\"Wrote a guide to $GLORPO's tokenomics. Mods, can I post it in #share?\"",
                    "\"$GLORPO LP burned, mint revoked. What do you think of the chart?\"",
                    "\"Paid partnership: $GLORPO is live on Launchpad Tower. LP burned. glorpo.city\"",
                    "\"When will the team publish the LP lock report?\"",
                },
                new[] { "Asks the mods before linking.", "No link from a new member.", "Ticker, one link, disclosed.", "A spoken question, no ticker." }),
            Room("Campaign: Solana Street Swap fee rebate",
                new[]
                {
                    "Reply under a big account: must add information",
                    "DMs: only people who asked you first",
                    "Discord Heights #share: links OK, no price talk",
                    "Timeline post: disclose paid deals",
                },
                new[]
                {
                    "\"Data point: small swaps cost 0.3% here vs 1% on the old pool (fee page linked).\"",
                    "\"You asked for the rebate code yesterday: it's CITY10.\"",
                    "\"Fee rebate details for the swap app: link to the docs page.\"",
                    "\"Ad: Solana Street Swap gives a fee rebate with code CITY10.\"",
                },
                new[] { "Adds a sourced data point.", "Answers someone who asked.", "A link with no price talk.", "Discloses the ad." }),
            Room("Campaign: $CHONK community art contest",
                new[]
                {
                    "Group chat 'Cat Coin Club': no other tickers",
                    "Spaces Studio stage: 30 seconds, speak to the topic 'art'",
                    "Discord Heights #art: images only, credit the artist",
                    "Timeline post: no 'tag 3 friends' asks",
                },
                new[]
                {
                    "\"Contest reminder: entries close Sunday, judges announced Friday.\"",
                    "\"Quick one: we're judging on originality, not follower count.\"",
                    "\"[image] Chonk in a hard hat, by @pixelpaw (entry #12)\"",
                    "\"The $CHONK art contest closes Sunday. Rules: chonk.city/art\"",
                },
                new[] { "No other tickers.", "Short and on topic.", "An image with credit.", "Plain info, no engagement ask." }),
            Room("Campaign: Perp Pit trading league",
                new[]
                {
                    "Discord Heights #trading: no leverage advice",
                    "Reply under a big account: must add information",
                    "Group chat 'Perp Degens': no links at all",
                    "Timeline post: risk note required for leverage promos",
                },
                new[]
                {
                    "\"League starts Monday; scoring is weekly return, not size.\"",
                    "\"Correction: league scoring uses return %, so small accounts can win.\"",
                    "\"League sign-ups open in the Perp Pit app, no link needed.\"",
                    "\"Perp Pit league opens Monday. Leverage can liquidate you; size small.\"",
                },
                new[] { "Facts, no leverage advice.", "Adds a correction.", "No link.", "Includes the risk note." }),
            Room("Campaign: Builder Hills hackathon",
                new[]
                {
                    "Spaces Studio Q&A: one spoken question, no links",
                    "Discord Heights #general: no shilling, links only in #share",
                    "DMs: only people who asked you first",
                    "Timeline post: 280 chars, one link",
                },
                new[]
                {
                    "\"Will mobile-only projects get their own judging track?\"",
                    "\"Is anyone else going to the Builder Hills hackathon? Link's in #share.\"",
                    "\"You asked about team size: up to four people per team.\"",
                    "\"Builder Hills hackathon, Oct 20-22, 20k USDC prizes: builderhills.city/hack\"",
                },
                new[] { "A spoken question, no link.", "Points to #share instead of linking.", "Answers someone who asked.", "One link, under 280." }),
        };

        // ------------------------------------------------------------ reply timing

        private static readonly (string handle, string post, bool bait)[] BigPosts =
        {
            ("@crier_bot_ct", "JUST IN: $360M of longs liquidated in the past 10 minutes.", false),
            ("@odds_bot_pit", "JUST IN: Prediction Pit now gives 68% that Perp Pit lists oil perps this week.", false),
            ("@airdrop_loyalty", "Quote this with your username and why you deserve the airdrop.", true),
            ("@hiring_reply_guys", "I'm hiring reply guys, $1,500 a week. Reply below.", true),
            ("@clip_farm_ct", "+$40,000 → liquidated. The entire life of a trader in under 1 minute.", false),
            ("@audit_row_anna", "Thread: the oracle bug that almost drained a lending pool, explained.", false),
            ("@terminal_cafe_tom", "Hourly funding explained in one chart.", false),
            ("@marina_mogul", "Giveaway! Like, RT, drop your wallet. 10 SOL to 10 winners.", true),
            ("@builder_bea", "We just shipped wallet sign-in on phones. Ask me anything below.", false),
            ("@reply_gm_king", "Reply 'hi' and I'll DM you a surprise.", true),
            ("@sleuth_sam", "A thread on how $SPLORT was bundled. 1/12", false),
        };

        public static List<TimingWindow> ReplyWindows(DetRng rng, double duration)
        {
            var (count, length, samples) = ContentKit.TimingLayout(duration, 7.5, 3, 6);
            var picks = new List<(string handle, string post, bool bait)>();
            var real = BigPosts.Where(p => !p.bait).ToList();
            var bait = BigPosts.Where(p => p.bait).ToList();
            picks.Add(rng.Pick(bait));
            picks.AddRange(rng.Sample(real, count - 1));
            rng.Shuffle(picks);
            var windows = new List<TimingWindow>();
            foreach (var p in picks)
            {
                double peakAt = rng.Between(length * 0.3, length * 0.6);
                double peakViews = rng.Between(3000, 12000);
                double replyRate = rng.Between(4, 12);
                var sig = new double[samples];
                var pay = new double[samples];
                var readout = new string[samples];
                for (int i = 0; i < samples; i++)
                {
                    double t = i * 0.1;
                    double views = t <= peakAt ? peakViews * (0.15 + 0.85 * t / peakAt) : peakViews * Math.Exp(-(t - peakAt) / (length * 0.35));
                    double replies = Math.Floor(replyRate * t * t / 2 + 2);
                    double reach = views * 10 / (10 + replies);
                    sig[i] = views;
                    pay[i] = p.bait ? -1 : reach;
                    string trend = i == 0 ? "" : views >= sig[i - 1] ? " rising" : " fading";
                    readout[i] = "Views/min " + ContentKit.Num(views) + trend + " · Replies " + ContentKit.Num(replies);
                }
                windows.Add(new TimingWindow
                {
                    Title = p.handle + ": \"" + p.post + "\"", Brief = p.bait ? "A big account, a big post." : "A big account, a big post.",
                    Step = 0.1, Signal = sig, Payoff = pay, Readout = readout,
                });
            }
            return windows;
        }
    }
}
