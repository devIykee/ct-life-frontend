using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CTLife.Careers.Tests
{
    public class CatalogTests
    {
        /// <summary>contract/config/careers.json (rc1): career id → the miniGameId its levels reference.</summary>
        internal static readonly Dictionary<string, string> ContractCareers = new Dictionary<string, string>
        {
            ["discord_mod"] = "discord_mod_ban", ["junior_analyst"] = "junior_analyst_earnings", ["kol_engagement"] = "kols_engagement",
            ["copywriter"] = "copywriter_rewrite", ["graphic_designer"] = "designer_canvas", ["smart_contract_auditor"] = "auditor_report",
            ["yield_farmer"] = "yield_farm_apy", ["shill_runner"] = "shill_post", ["doxx_researcher"] = "doxx_thread",
            ["trend_hunter"] = "trend_call", ["kol_manager"] = "kol_negotiate", ["dev_rel"] = "devrel_ama", ["alpha_lead"] = "alpha_call",
            ["rug_sleuth"] = "sleuth_thread",
        };

        private static MiniGameCatalog Catalog => GameCatalog.Instance;

        [Test]
        public void FortyTwoGamesThreePerCareer()
        {
            Assert.AreEqual(42, Catalog.All.Count);
            Assert.AreEqual(42, Catalog.All.Select(s => s.Id).Distinct().Count());
            foreach (var career in ContractCareers.Keys)
                Assert.AreEqual(3, Catalog.ForCareer(career).Count, career);
            CollectionAssert.IsSubsetOf(Catalog.All.Select(s => s.CareerId).Distinct(), ContractCareers.Keys);
        }

        [Test]
        public void EveryContractReferencedGameExists()
        {
            foreach (var kv in ContractCareers)
            {
                var spec = Catalog.Get(kv.Value);
                Assert.IsNotNull(spec, kv.Value);
                Assert.AreEqual(kv.Key, spec.CareerId);
                Assert.IsTrue(spec.InContract, kv.Value + " should be marked as already in the contract");
            }
        }

        [Test]
        public void IdsAreSafeSnakeCaseAndSpecsHaveTutorials()
        {
            foreach (var s in Catalog.All)
            {
                Assert.IsTrue(CareersWire.IsSafeId(s.Id), s.Id);
                Assert.AreEqual(s.Id.ToLowerInvariant(), s.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.Title), s.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.Tagline), s.Id);
                Assert.GreaterOrEqual(s.Rules.Length, 2, s.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.ScoringRule), s.Id);
                Assert.That(s.DefaultDuration, Is.InRange(30, 60), s.Id);
                Assert.Greater(s.DefaultMaxScore, 0, s.Id);
                if (s.Engine != MiniGameEngine.Legacy)
                {
                    Assert.IsNotNull(s.Create, s.Id);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(s.RealTask), s.Id);
                }
            }
        }

        [Test]
        public void NoRealBrandNamesInContent()
        {
            // A guard list of real platforms/brands the content must not name (parody names only).
            // Real platforms must appear as the parody names from docs/research/ct-now-2026-10.md §6 (or not at all).
            string[] banned = { "pump.fun", "Pump.fun", "Polymarket", "Hyperliquid", "Bubblemaps", "RugCheck", "Kaito", "Binance", "Coinbase", "Phantom", "Jupiter", "Raydium", "Meteora", "Twitter", "Discord.com", "Superteam", "Immunefi", "Seed Vault",
                                "Telegram", "USDT", "YouTube", "Twitch", "Bloomberg", "BlackRock", "TOKEN2049", "Breakpoint", "Bitget", "Robinhood" };
            // Slang the dossier marks fading or never-use (§2a): ser / fren / wagmi / HFSP, and "jeet" (read as a slur).
            var fading = new System.Text.RegularExpressions.Regex(@"\b(ser|frens?|wagmi|hfsp|have fun staying poor|jeets?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var tg = new System.Text.RegularExpressions.Regex(@"\bTG\b"); // Telegram's short name; random wallet labels like "TGAk…" are fine
            foreach (var s in Catalog.All.Where(x => x.Engine != MiniGameEngine.Legacy))
                for (int seed = 0; seed < 25; seed++)
                {
                    var text = string.Join("\n", new[] { s.Title, s.Tagline, s.ScoringRule, s.RealTask }.Concat(s.Rules)) + "\n" + Dump(s.Build(new RoundInit(seed * 7919 + 7, s.DefaultMaxScore, s.DefaultDuration)));
                    foreach (var b in banned) StringAssert.DoesNotContain(b, text, s.Id);
                    Assert.IsFalse(tg.IsMatch(text), s.Id + " says TG (use TeleGrum)");
                    var m = fading.Match(text);
                    Assert.IsFalse(m.Success, s.Id + " uses fading/never-use slang: " + m.Value);
                }
            foreach (var id in new[] { "doot_feed", "drop_alpha", "write_thread", "audit_token", "food_delivery", "data_entry", "dog_walking", "ad_clicks", "social_moderation" })
            {
                var tag = CareerFlavor.GigTagline(id);
                foreach (var b in banned) StringAssert.DoesNotContain(b, tag, id);
            }
        }

        internal static string Dump(IMiniGameRound round)
        {
            switch (round)
            {
                case SortRound r: return string.Join("|", r.Cards.Select(c => c.Title + "~" + c.Body + "~" + string.Join(",", c.Chips) + "~" + c.Why + "~" + c.Bin + "~" + c.Swatch?.Sample + c.Swatch?.Foreground + "/" + c.Swatch?.Background));
                case FlagRound r: return string.Join("|", r.Boards.Select(b => b.Title + "~" + b.Brief + "~" + string.Join(",", b.Lines.Select(l => l.Text + (l.Flaw ? "*" : "")))));
                case SequenceRound r: return string.Join("|", r.Boards.Select(b => b.Title + "~" + string.Join(",", b.Items)));
                case MatchRound r: return string.Join("|", r.Boards.Select(b => b.Title + "~" + string.Join(",", b.Left) + "~" + string.Join(",", b.Right)));
                case PickRound r: return string.Join("|", r.Boards.Select(b => b.Title + "~" + b.Context + "~" + b.Correct + "~" + string.Join(",", b.Options.Select(o => o.Label + ":" + string.Join(";", o.Facts) + ":" + o.Reveal))));
                case ComboRound r: return string.Join("|", r.Boards.Select(b => b.Title + "~" + b.Min + "-" + b.Max + "~" + string.Join(",", b.Slots.Select(s => s.Name + ":" + string.Join(";", s.Options.Select(o => o.Label + "=" + o.Value))))));
                case TimingRound r: return string.Join("|", r.Windows.Select(w => w.Title + "~" + string.Join(",", w.Payoff.Select(p => p.ToString("0.###", ContentKit.Inv))) + "~" + (w.Readout == null ? "" : string.Join(",", w.Readout))));
                case BalanceRound r: return string.Join(",", r.Spec.Drift.Select(d => d.ToString("0.###", ContentKit.Inv))) + "~" + string.Join(",", r.Spec.Cues.Select(c => c.At.ToString("0.###", ContentKit.Inv) + c.Kind + c.Text)) + "~" + (r.Spec.Notes == null ? "" : string.Join(",", r.Spec.Notes));
                default: throw new InvalidOperationException("Unknown round " + round.GetType());
            }
        }
    }

    public class ContentValidityTests
    {
        private static IEnumerable<MiniGameSpec> Built => GameCatalog.Instance.All.Where(s => s.Engine != MiniGameEngine.Legacy);

        [Test]
        public void SameSeedSameRoundDifferentSeedDifferentRound()
        {
            foreach (var s in Built)
            {
                string a = CatalogTests.Dump(s.Build(new RoundInit(1234, s.DefaultMaxScore, s.DefaultDuration)));
                string b = CatalogTests.Dump(s.Build(new RoundInit(1234, s.DefaultMaxScore, s.DefaultDuration)));
                Assert.AreEqual(a, b, s.Id + " is not deterministic");
                var others = Enumerable.Range(1, 5).Select(k => CatalogTests.Dump(s.Build(new RoundInit(1234 + k * 7919, s.DefaultMaxScore, s.DefaultDuration))));
                Assert.IsTrue(others.Any(o => o != a), s.Id + " ignores its seed");
            }
        }

        [Test]
        public void EveryGameBuildsValidRoundsAcrossManySeeds()
        {
            foreach (var s in Built)
                for (int seed = -40; seed < 160; seed += 3)
                {
                    var round = s.Build(new RoundInit(unchecked(seed * 1103515245 + 12345), s.DefaultMaxScore, s.DefaultDuration));
                    Assert.AreEqual(s.DefaultMaxScore, round.MaxScore, s.Id);
                    Assert.AreEqual(0, round.Score, s.Id + " starts above zero");
                    CheckContent(s, round);
                }
        }

        [Test]
        public void RegistryCapsAndDurationsOverrideDefaults()
        {
            foreach (var s in Built)
            {
                var round = s.Build(new RoundInit(99, 10, 30));
                Assert.AreEqual(10, round.MaxScore, s.Id);
                Assert.AreEqual(30, round.Duration, s.Id);
                Assert.LessOrEqual(Bot(round, perfect: true), 10, s.Id);
            }
        }

        [Test]
        public void PerfectPlayReachesTheCapAndIdlePlayStaysLow()
        {
            foreach (var s in Built)
                for (int seed = 0; seed < 20; seed++)
                {
                    int perfect = Bot(s.Build(new RoundInit(seed, 100, s.DefaultDuration)), perfect: true);
                    int idle = Bot(s.Build(new RoundInit(seed, 100, s.DefaultDuration)), perfect: false);
                    // The balance bot is a naive controller (it nudges at a quarter-band and pays every fee), so its floor is lower.
                    Assert.GreaterOrEqual(perfect, s.Engine == MiniGameEngine.Balance ? 55 : 95, s.Id + " seed " + seed + ": a perfect player can't reach the cap");
                    Assert.LessOrEqual(idle, s.Engine == MiniGameEngine.Timing ? 34 : s.Engine == MiniGameEngine.Balance ? 35 : 25, s.Id + " seed " + seed + ": doing nothing scores too much");
                }
        }

        private static void CheckContent(MiniGameSpec s, IMiniGameRound round)
        {
            switch (round)
            {
                case SortRound r:
                    Assert.GreaterOrEqual(r.Cards.Count, 8, s.Id);
                    if (!Procedural.Contains(s.Id)) Assert.AreEqual(r.Cards.Count, r.Cards.Select(c => c.Title).Distinct().Count(), s.Id + " repeats a card");
                    if (s.Id != "kol_agent_leash")
                        for (int b = 0; b < r.Bins.Count; b++) Assert.IsTrue(r.Cards.Any(c => c.Bin == b), s.Id + " deck misses bin " + r.Bins[b]);
                    foreach (var c in r.Cards) Assert.IsFalse(string.IsNullOrWhiteSpace(c.Why), s.Id);
                    if (s.Id == "designer_canvas")
                        foreach (var c in r.Cards)
                        {
                            double ratio = DesignerGames.Contrast(c.Swatch.Foreground, c.Swatch.Background);
                            Assert.IsTrue(c.Bin == 0 ? ratio >= DesignerGames.ReadableAtLeast : ratio <= DesignerGames.UnreadableAtMost, "ambiguous swatch " + ratio);
                        }
                    break;
                case FlagRound r:
                    Assert.GreaterOrEqual(r.Boards.Count, 3, s.Id);
                    foreach (var b in r.Boards)
                    {
                        Assert.That(b.FlawCount, Is.InRange(1, 4), s.Id);
                        foreach (var l in b.Lines.Where(l => l.Flaw)) Assert.IsFalse(string.IsNullOrWhiteSpace(l.Why), s.Id + " flaw without a reason");
                        StringAssert.Contains(b.FlawCount.ToString(ContentKit.Inv), b.Brief, s.Id + " brief must state the count");
                    }
                    break;
                case SequenceRound r:
                    Assert.GreaterOrEqual(r.Boards.Count, 3, s.Id);
                    foreach (var b in r.Boards) Assert.AreEqual(b.Items.Length, b.Items.Distinct().Count(), s.Id);
                    break;
                case MatchRound r:
                    Assert.GreaterOrEqual(r.Boards.Count, 3, s.Id);
                    foreach (var b in r.Boards) Assert.AreEqual(b.Right.Length, b.Right.Distinct().Count(), s.Id);
                    break;
                case PickRound r:
                    Assert.GreaterOrEqual(r.Boards.Count, 5, s.Id);
                    foreach (var b in r.Boards)
                    {
                        Assert.AreEqual(b.Options.Length, b.Options.Select(o => o.Label + string.Join(";", o.Facts)).Distinct().Count(), s.Id + " duplicate options");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(b.Why), s.Id);
                    }
                    break;
                case ComboRound r:
                    Assert.GreaterOrEqual(r.Boards.Count, 3, s.Id);
                    foreach (var b in r.Boards)
                    {
                        Assert.GreaterOrEqual(b.CountSolutions(), 1, s.Id + " unsolvable board " + b.Title);
                        var all = b.Slots.Select(_ => 0).ToArray();
                        Assert.IsFalse(b.Passes(all, out _, out _) && s.Id == "copywriter_char_crunch", s.Id + ": keeping everything must not fit");
                    }
                    break;
                case TimingRound r:
                    Assert.LessOrEqual(r.Windows.Sum(w => w.Length + TimingRound.FeedbackSeconds), r.Duration + 1e-6, s.Id + " windows overflow even with feedback beats");
                    Assert.GreaterOrEqual(r.Windows.Count, 3, s.Id);
                    Assert.GreaterOrEqual(r.Windows.Count(w => w.Best > 0), r.Windows.Count - 1, s.Id + " too many dead windows");
                    break;
                case BalanceRound r:
                    Assert.GreaterOrEqual(r.Spec.Drift.Length * r.Spec.SegmentSeconds, r.Duration, s.Id + " drift shorter than the round");
                    break;
            }
        }

        // Generated decks may reuse a title (a placement name, a follower handle, an agent action) with different content.
        private static readonly HashSet<string> Procedural = new HashSet<string> { "designer_canvas", "kol_bot_audit", "kol_agent_leash" };

        /// <summary>Plays a round with a perfect (oracle) or idle strategy at 20 Hz and returns the final score.</summary>
        internal static int Bot(IMiniGameRound round, bool perfect)
        {
            double t = 0;
            var session = new MiniGameSession(round);
            session.Begin(0);
            int guard = 0;
            while (!session.IsTerminal && guard++ < 20000)
            {
                if (perfect) Play(round, t);
                session.AfterInput();
                t += 0.05;
                session.Tick(t);
            }
            return session.FinalScore;
        }

        private static void Play(IMiniGameRound round, double t)
        {
            switch (round)
            {
                case SortRound r when r.Open:
                    var judge = typeof(SortRound).GetField("_judge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(r) as ISortJudge;
                    r.Answer(judge?.CorrectBin(r.Current) ?? r.Current.Bin);
                    break;
                case FlagRound r when r.Open:
                    for (int i = 0; i < r.Board.Lines.Length; i++) if (r.Board.Lines[i].Flaw) r.Toggle(i);
                    r.File();
                    break;
                case SequenceRound r when r.Open:
                    for (int i = 0; i < r.Board.Items.Length; i++) r.Place(i);
                    r.File();
                    break;
                case MatchRound r when r.Open:
                    for (int i = 0; i < r.Board.Left.Length; i++) { r.SelectLeft(i); r.PairWith(r.Board.Solution[i]); }
                    r.File();
                    break;
                case PickRound r when r.Open:
                    r.Pick(r.Board.Correct);
                    break;
                case ComboRound r when r.Open:
                    var b = r.Board;
                    var choice = new int[b.Slots.Length];
                    bool found = false;
                    void Walk(int slot)
                    {
                        if (found) return;
                        if (slot == choice.Length) { found = b.Passes(choice, out _, out _); return; }
                        for (int o = 0; o < b.Slots[slot].Options.Length && !found; o++) { choice[slot] = o; Walk(slot + 1); }
                    }
                    Walk(0);
                    for (int i = 0; i < choice.Length; i++) r.Choose(i, choice[i]);
                    r.File();
                    break;
                case TimingRound r when r.Open && !r.Acted && r.Window != null:
                    var w = r.Window;
                    if (w.Best <= 0) break;
                    double best = w.Best;
                    if (w.Payoff[r.Sample] >= best - 1e-9) r.Act();
                    break;
                case BalanceRound r when r.Open:
                    var spec = r.Spec;
                    if (r.ActiveCue != null)
                    {
                        int a = Array.FindIndex(spec.Actions, x => x.Kind == r.ActiveCue.Kind);
                        if (a >= 0 && r.CooldownLeft(a) <= 0) r.Apply(a);
                    }
                    else if (spec.Actions.All(x => x.Kind == null))
                    {
                        double c = spec.Centre;
                        int hedge = Array.FindIndex(spec.Actions, x => x.PullToCentre > 0);
                        if (Math.Abs(r.Value - c) > 45 && hedge >= 0 && r.CooldownLeft(hedge) <= 0) r.Apply(hedge);
                        else if (r.Value > c + (spec.BandHi - spec.BandLo) / 4) { int a = Array.FindIndex(spec.Actions, x => x.Delta < 0); if (r.CooldownLeft(a) <= 0) r.Apply(a); }
                        else if (r.Value < c - (spec.BandHi - spec.BandLo) / 4) { int a = Array.FindIndex(spec.Actions, x => x.Delta > 0); if (r.CooldownLeft(a) <= 0) r.Apply(a); }
                    }
                    break;
            }
        }
    }
}
