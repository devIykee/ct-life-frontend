using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.WorldLife.Tests
{
    public class SpotResolverTests
    {
        private static IEnumerable<string> Venues => new[] { "timeline_square", "perp_pit", "rekt_clinic", "spaces_studio", "ore_refinery", "terminal_cafe", "vc_row" };

        /// <summary>A resolver over the contract spots.json for one venue (global `venue.spot` ids, like rc1).</summary>
        public static SpotResolver For(string venueId)
        {
            var spots = TestData.Contract("ids/spots.json")["venues"][venueId]["spots"];
            var points = new List<SpotPoint>();
            foreach (var s in spots)
                points.Add(new SpotPoint { Id = (string)s["id"], X = s["x"]?.Value<float>() ?? 0, Z = s["z"]?.Value<float>() ?? 0, HasPosition = s["x"] != null });
            return new SpotResolver(venueId, points, Venues, SpotResolver.AliasesFrom(TestData.Presentation, venueId));
        }

        [Test]
        public void ContractSpotIdsResolveInBothForms()
        {
            var r = For("timeline_square");
            Assert.AreEqual("square_center", r.Resolve("square_center", "n").SpotId);
            Assert.AreEqual("center", r.Resolve("timeline_square.center", "n").SpotId);
            Assert.AreEqual(SpotKind.Keep, r.Resolve("", "n").Kind);
        }

        [Test]
        public void BrainSpotNamesMapToRealSpotsStablyPerNpc()
        {
            var r = For("perp_pit");
            var a = r.Resolve("perp_terminal", "npc-0-perp_degen_npc");
            Assert.AreEqual(SpotKind.Local, a.Kind);
            StringAssert.StartsWith("terminal_", a.SpotId);
            Assert.AreEqual(a.SpotId, r.Resolve("perp_terminal", "npc-0-perp_degen_npc").SpotId);
            Assert.AreEqual("exit", r.Resolve("venue_exit", "n").SpotId);
            StringAssert.StartsWith("terminal_", For("terminal_cafe").Resolve("cafe_terminal", "npc-0-wallet_tracker").SpotId);
        }

        [Test]
        public void AnotherVenueAsSpotMeansOffstage()
        {
            var r = For("perp_pit").Resolve("rekt_clinic", "n");
            Assert.AreEqual(SpotKind.Offstage, r.Kind);
            Assert.AreEqual("rekt_clinic", r.SpotId);
        }

        [Test]
        public void OwnVenueAsSpotUsesTheVenueAlias()
        {
            StringAssert.StartsWith("bed_", For("rekt_clinic").Resolve("rekt_clinic", "npc-0-rekt_guy").SpotId);
            Assert.AreEqual("miner_post", For("ore_refinery").Resolve("ore_refinery", "n").SpotId);
            Assert.AreEqual("stage_host", For("spaces_studio").Resolve("spaces_studio", "n").SpotId);
        }

        [Test]
        public void CoordinateSpotsSnapToTheNearestNamedSpot()
        {
            var points = new[]
            {
                new SpotPoint { Id = "a", X = 0, Z = 0, HasPosition = true }, new SpotPoint { Id = "b", X = 10, Z = 10, HasPosition = true },
                new SpotPoint { Id = "exit", X = 9, Z = 9, HasPosition = true },
            };
            var r = new SpotResolver("v", points, null, null);
            Assert.AreEqual("b", r.Resolve("9.5,9.0", "n").SpotId);
            Assert.AreEqual("a", r.Resolve("-1,1", "n").SpotId);
            Assert.AreEqual(SpotKind.Unknown, r.Resolve("NaN,1", "n").Kind);
            Assert.AreEqual(SpotKind.Unknown, r.Resolve("builder_hq_9", "n").Kind);
        }
    }

    public class NpcBeatInterpreterTests
    {
        private static NpcSnapshot S(string state, string spot, string anim, string line = "", double at = 1000, string emote = "", string target = "",
            string arch = "x") =>
            new NpcSnapshot { ArchetypeId = arch, State = state, SpotId = spot, AnimId = anim, LineId = line, StartedAt = at, EmoteId = emote, TargetUserId = target };

        [Test]
        public void StartedAtRewrittenEveryTickDoesNotRestartTheRun()
        {
            var i = new NpcBeatInterpreter();
            var r = SpotResolverTests.For("timeline_square");
            var a = i.Next(S("goto", "square_center", "loco_walk", at: 1000), r, "n");
            var b = i.Next(S("goto", "square_center", "loco_idle", at: 2000), r, "n");
            var c = i.Next(S("goto", "square_center", "loco_idle", at: 3000), r, "n");
            Assert.IsTrue(a.NewRun);
            Assert.IsFalse(b.NewRun);
            Assert.IsFalse(c.NewRun);
            Assert.AreEqual(1000, c.Shown.StartedAt);
            Assert.AreEqual(a.Shown.AnimId, c.Shown.AnimId, "follower key must stay identical inside a run");
        }

        [Test]
        public void ActionAnimationIsStickyInsideARun()
        {
            var i = new NpcBeatInterpreter();
            var r = SpotResolverTests.For("perp_pit");
            var first = i.Next(S("act", "perp_terminal", "act_frantic_type"), r, "n");
            var later = i.Next(S("act", "perp_terminal", "loco_idle", at: 5000), r, "n");
            Assert.IsTrue(first.FreshAnim);
            Assert.AreEqual("act_frantic_type", later.Shown.AnimId);
            Assert.IsFalse(later.FreshAnim);
        }

        [Test]
        public void GiveawayGuyCycleShowsEachPitchExactlyOnce()
        {
            var i = new NpcBeatInterpreter();
            var r = SpotResolverTests.For("timeline_square");
            var seq = new[]
            {
                S("goto", "square_center", "loco_walk", at: 0), S("goto", "square_center", "loco_idle", at: 1),
                S("talk", "square_center", "loco_idle", "giveaway_pitch", 5), S("talk", "square_center", "loco_idle", "", 6),
                S("talk", "square_center", "loco_idle", "", 7), S("idle", "square_center", "loco_idle", "", 35),
                S("goto", "square_center", "loco_walk", "", 36), S("talk", "square_center", "loco_idle", "giveaway_pitch", 41),
            };
            var beats = seq.Select(s => i.Next(s, r, "npc-1-giveaway_guy")).ToList();
            Assert.AreEqual(2, beats.Count(b => b.FreshLine));
            Assert.AreEqual("giveaway_pitch", beats[4].Shown.LineId, "line id stays stable for the follower inside the talk run");
        }

        [Test]
        public void ReplyGuyEmotesAndLinesEveryTalk()
        {
            var i = new NpcBeatInterpreter();
            var r = SpotResolverTests.For("timeline_square");
            var beats = new[]
            {
                S("follow", "", "loco_walk", target: "u1"), S("talk", "", "loco_idle", "reply_gm", emote: "emote_wave", target: "u1"),
                S("follow", "", "loco_walk", target: "u1"), S("talk", "", "loco_idle", "reply_gm", emote: "emote_wave", target: "u1"),
            }.Select(s => i.Next(s, r, "n")).ToList();
            Assert.AreEqual(2, beats.Count(b => b.FreshLine));
            Assert.AreEqual(2, beats.Count(b => b.FreshEmote));
            Assert.AreEqual("u1", beats[1].Shown.TargetUserId);
        }

        [Test]
        public void PerpDegenLiquidationCycleGoesOffstageAndRespawns()
        {
            var i = new NpcBeatInterpreter();
            var r = SpotResolverTests.For("perp_pit");
            var trade = i.Next(S("act", "perp_terminal", "act_frantic_type", at: 0), r, "npc-0-perp_degen_npc");
            var rekt = i.Next(S("react", "perp_terminal", "react_liquidated", at: 90), r, "npc-0-perp_degen_npc");
            var leave = i.Next(S("leave", "rekt_clinic", "loco_walk", at: 95), r, "npc-0-perp_degen_npc");
            var cry = i.Next(S("act", "rekt_clinic", "act_cry", "perp_its_so_over", 100), r, "npc-0-perp_degen_npc");
            var cry2 = i.Next(S("act", "rekt_clinic", "loco_idle", at: 101), r, "npc-0-perp_degen_npc");
            var back = i.Next(S("react", "perp_terminal", "react_moon_jump", "perp_max_long", 130, "emote_moon"), r, "npc-0-perp_degen_npc");

            Assert.IsFalse(trade.Offstage);
            Assert.IsTrue(rekt.FreshAnim);
            Assert.AreEqual("react_liquidated", rekt.Shown.AnimId);
            Assert.IsTrue(leave.Offstage);
            Assert.AreEqual("leave", leave.Shown.State);
            Assert.AreEqual("exit", leave.Shown.SpotId);
            Assert.IsTrue(cry.Offstage, "crying in the Rekt Clinic is not visible in the Perp Pit");
            Assert.AreEqual("leave", cry.Shown.State);
            Assert.AreEqual("", cry.Shown.EmoteId);
            Assert.IsFalse(cry2.NewRun);
            Assert.IsTrue(back.Respawn);
            Assert.IsFalse(back.Offstage);
            Assert.IsTrue(back.FreshLine);
            Assert.AreEqual("emote_moon_jump", i.Next(S("react", "perp_terminal", "react_moon_jump", "perp_max_long", 131, "emote_moon"), r, "npc-0-perp_degen_npc",
                Behaviours.EmoteAliases(TestData.Presentation)).EmoteId);
        }

        [Test]
        public void DoomposterLeavingEveryTickStaysHiddenThenRespawns()
        {
            var i = new NpcBeatInterpreter();
            var r = SpotResolverTests.For("timeline_square");
            var first = i.Next(S("leave", "venue_exit", "loco_walk", at: 1), r, "d");
            for (int t = 2; t < 10; t++) Assert.IsFalse(i.Next(S("leave", "venue_exit", "loco_walk", at: t), r, "d").NewRun);
            Assert.IsTrue(i.Hidden);
            Assert.AreEqual("exit", first.Shown.SpotId);
            var talk = i.Next(S("talk", "venue_exit", "loco_idle", "doom_bear", 20), r, "d");
            Assert.IsTrue(talk.Respawn);
            Assert.IsTrue(talk.FreshLine);
            Assert.IsFalse(i.Hidden);
        }

        [Test]
        public void TalkingAtAnotherVenueNameStaysVisible()
        {
            // Backend spawns the Spaces Host in Timeline Square and talks with spotId "spaces_studio".
            var i = new NpcBeatInterpreter();
            var b = i.Next(S("talk", "spaces_studio", "loco_idle", "spaces_gm"), SpotResolverTests.For("timeline_square"), "h");
            Assert.IsFalse(b.Offstage);
            Assert.AreEqual("talk", b.Shown.State);
        }

        [Test]
        public void UnknownStateBecomesIdleAndIsFlagged()
        {
            var b = new NpcBeatInterpreter().Next(S("Dance_Battle", "", "loco_idle"), null, "n");
            Assert.AreEqual("idle", b.Shown.State);
            Assert.IsTrue(b.UnknownState);
            Assert.AreEqual("talk", NpcBeatInterpreter.NormalizeState(" TALK ", out var unknown));
            Assert.IsFalse(unknown);
        }

        [Test]
        public void UnknownSpotKeepsThePreviousSpot()
        {
            var i = new NpcBeatInterpreter();
            var r = SpotResolverTests.For("timeline_square");
            i.Next(S("goto", "bench_2", "loco_walk"), r, "n");
            var b = i.Next(S("act", "builder_hq_1_typo", "act_type"), r, "n");
            Assert.IsTrue(b.UnknownSpot);
            Assert.AreEqual("bench_2", b.Shown.SpotId);
        }

        [Test]
        public void SnapshotsAreCopiedNotShared()
        {
            var s = S("talk", "", "loco_idle", "reply_gm");
            s.LineParams["coin"] = "FROGE";
            var c = s.Clone();
            c.LineParams["coin"] = "OTHER";
            Assert.AreEqual("FROGE", s.LineParams["coin"]);
        }
    }

    public class BehaviourTests
    {
        private static ArchetypeBook Book => ArchetypeBook.Build(TestData.Looks, TestData.Presentation);

        private static NpcBeat Beat(string state, string line = "", string anim = "loco_idle", string emote = "", string target = "",
            bool freshLine = false, bool freshAnim = false, bool freshEmote = false, bool newRun = false) =>
            new NpcBeat
            {
                Shown = new NpcSnapshot { State = state, LineId = line, AnimId = anim, EmoteId = emote, TargetUserId = target },
                LineId = line, EmoteId = emote, FreshLine = freshLine, FreshAnim = freshAnim, FreshEmote = freshEmote, NewRun = newRun,
            };

        [Test]
        public void ReplyGuyFacesHisTargetWhenHeTalks()
        {
            var cues = Behaviours.Cues(Book.Get("reply_guy"), Beat("talk", "reply_gm", target: "u1", freshLine: true), null, false);
            CollectionAssert.Contains(cues, Behaviours.FaceTarget);
        }

        [Test]
        public void TimelinePostersPostFreshLines()
        {
            foreach (var id in new[] { "ai_agent", "sleuth", "thread_boy", "town_crier" })
                CollectionAssert.Contains(Behaviours.Cues(Book.Get(id), Beat("talk", "x", freshLine: true), null, false), Behaviours.PostToTimeline, id);
            CollectionAssert.DoesNotContain(Behaviours.Cues(Book.Get("rekt_guy"), Beat("talk", "x", freshLine: true), null, false), Behaviours.PostToTimeline);
        }

        [Test]
        public void SybilSplitsOnItsTriggersAndTheModClearsOnArrival()
        {
            var cfg = TestData.Presentation["behaviours"];
            var sybil = Book.Get("sybil_farmer");
            CollectionAssert.Contains(Behaviours.Cues(sybil, Beat("act", anim: "act_sybil_split", freshAnim: true), cfg, false), Behaviours.SplitClones);
            CollectionAssert.Contains(Behaviours.Cues(sybil, Beat("talk", "sybil_split", freshLine: true), cfg, false), Behaviours.SplitClones);
            CollectionAssert.DoesNotContain(Behaviours.Cues(sybil, Beat("act", anim: "act_farm", freshAnim: true), cfg, false), Behaviours.SplitClones);
            var mod = Book.Get("community_mod");
            CollectionAssert.Contains(Behaviours.Cues(mod, Beat("act", newRun: true), cfg, true), Behaviours.ClearClones);
            CollectionAssert.DoesNotContain(Behaviours.Cues(mod, Beat("act", newRun: true), cfg, false), Behaviours.ClearClones, "nothing to clear");
        }

        [Test]
        public void PerpDegenLiquidationSoundsTheSiren()
        {
            CollectionAssert.Contains(Behaviours.Cues(Book.Get("perp_degen_npc"), Beat("react", anim: "react_liquidated", freshAnim: true), null, false), Behaviours.Siren);
        }

        [Test]
        public void OffstageNpcsGetNoCues()
        {
            var b = Beat("leave", "x", freshLine: true);
            b.Offstage = true;
            Assert.IsEmpty(Behaviours.Cues(Book.Get("ai_agent"), b, null, false));
        }
    }
}
