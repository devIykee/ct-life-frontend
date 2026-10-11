using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.WorldLife.Tests
{
    public class EventMomentTests
    {
        private const double Now = 1_801_828_800_000;

        private static CityEvent E(string id, double at = Now, params string[] kv)
        {
            var e = new CityEvent { EventId = id, At = at };
            for (int i = 0; i + 1 < kv.Length; i += 2) e.Params[kv[i]] = kv[i + 1];
            return e;
        }

        private static List<CrowdMember> Crowd(int n, string state = "idle") =>
            Enumerable.Range(0, n).Select(i => new CrowdMember { NpcId = "npc-" + i, ArchetypeId = "normie", State = state }).ToList();

        private static WorldMomentPlan Plan(CityEvent e, string venue, List<CrowdMember> crowd = null, bool synced = true, double now = Now) =>
            EventMoments.Plan(e, venue, TestData.Presentation["moments"], TestData.Book(), crowd ?? new List<CrowdMember>(), now, synced);

        [TestCase("evt_liquidation", "perp_pit", "liquidation", "siren")]
        [TestCase("evt_liquidation", "timeline_square", "liquidation", null)]
        [TestCase("evt_rug", "timeline_square", "rug", "carpet_yank")]
        [TestCase("evt_rug", "launchpad_tower", "rug", "carpet_yank")]
        [TestCase("evt_coin_launch", "launchpad_tower", "launch", "sniper_swarm")]
        [TestCase("evt_exploit", "audit_row", "exploit", "alarm")]
        [TestCase("evt_exploit", "timeline_square", "exploit", null)]
        [TestCase("evt_ore_motherlode", "ore_refinery", "motherlode", "furnace_flash")]
        [TestCase("evt_mood_change", "timeline_square", "mood", null)]
        public void EveryContractEventBecomesAMoment(string eventId, string venue, string moment, string fx)
        {
            var plan = Plan(E(eventId), venue);
            Assert.IsNotNull(plan);
            Assert.AreEqual(moment, plan.MomentId);
            Assert.AreEqual(fx, plan.VenueFx);
            Assert.IsTrue(plan.Live);
            Assert.IsNotNull(plan.CrierText, "Town Crier has a JUST IN shout for " + eventId);
            StringAssert.StartsWith("JUST IN:", plan.CrierText);
            StringAssert.DoesNotContain("{", plan.CrierText);
            Assert.AreEqual(plan.CrierText, plan.TimelineText);
        }

        [Test]
        public void ContractEventListIsCovered()
        {
            var ids = TestData.Contract("ids/events.json")["events"].Select(e => (string)e["id"]).ToList();
            CollectionAssert.AreEquivalent(ids, EventMoments.ContractEvents);
            foreach (var id in ids) Assert.IsNotNull(TestData.Presentation["moments"][id], id);
        }

        [Test]
        public void VenueEffectsCarryPositionalSoundCandidates()
        {
            foreach (var m in ((JObject)TestData.Presentation["moments"]).Properties())
                if (m.Value["venueFx"] is JObject fx)
                    foreach (var kind in fx.Properties().Select(p => (string)p.Value).Distinct())
                        Assert.IsNotEmpty(WireText.Strs(m.Value["venueSfx"]?[kind]), m.Name + " " + kind);
        }

        [Test]
        public void StaleUnsyncedAndFutureEventsAreNotStaged()
        {
            Assert.IsFalse(Plan(E("evt_rug", Now - 31_000), "timeline_square").Live, "older than 30 s");
            Assert.IsTrue(Plan(E("evt_rug", Now - 29_000), "timeline_square").Live);
            Assert.IsFalse(Plan(E("evt_rug"), "timeline_square", synced: false).Live, "no server clock, no breaking news");
            Assert.IsNull(Plan(E("evt_rug", Now + 120_000), "timeline_square"), "implausible future");
            Assert.IsNull(Plan(E("evt_rug", double.NaN), "timeline_square"));
            Assert.IsNull(Plan(E("evt_not_in_contract"), "timeline_square"));
        }

        [Test]
        public void CrowdReactionsAreBoundedDeterministicAndOnlyForStillNpcs()
        {
            var crowd = Crowd(12);
            crowd[0].State = "talk";
            crowd[1].State = "goto";
            crowd[2].Hidden = true;
            crowd[3].ArchetypeId = "sniper_bot";
            var a = Plan(E("evt_liquidation", Now, "alias", "x"), "perp_pit", crowd);
            var b = Plan(E("evt_liquidation", Now, "alias", "x"), "perp_pit", crowd);
            Assert.LessOrEqual(a.Crowd.Count, 4);
            Assert.Greater(a.Crowd.Count, 0);
            CollectionAssert.AreEqual(a.Crowd.Select(c => c.NpcId + c.AnimId + c.Delay), b.Crowd.Select(c => c.NpcId + c.AnimId + c.Delay));
            foreach (var c in a.Crowd)
            {
                Assert.IsFalse(new[] { "npc-0", "npc-1", "npc-2", "npc-3" }.Contains(c.NpcId), c.NpcId);
                Assert.That(c.Delay, Is.InRange(0.15f, 0.9f));
                CollectionAssert.Contains(new[] { "react_shock", "emote_facepalm", "emote_laugh", "emote_head_shake" }, c.AnimId);
            }
        }

        [Test]
        public void MoodChangeDirectionPicksShoutAndCrowd()
        {
            var down = Plan(E("evt_mood_change", Now, "direction", "down", "moodBps", "-300"), "timeline_square", Crowd(8));
            Assert.AreEqual(-1, down.MoodDirection);
            Assert.IsTrue(down.CrierText.Contains("red") || down.CrierText.Contains("-3%"), down.CrierText);
            Assert.IsTrue(down.Crowd.All(c => new[] { "react_sad", "emote_head_shake", "act_head_in_hands" }.Contains(c.AnimId)));
            var up = Plan(E("evt_mood_change", Now, "moodBps", "250"), "timeline_square");
            Assert.AreEqual(1, up.MoodDirection);
            Assert.AreEqual(0, EventMoments.MoodDirection(new Dictionary<string, string>()));
        }

        [Test]
        public void EventKeyIgnoresParamOrder()
        {
            var a = E("evt_rug", Now, "ticker", "FROGE", "alias", "dev");
            var b = new CityEvent { EventId = "evt_rug", At = Now, Params = new Dictionary<string, string> { ["alias"] = "dev", ["ticker"] = "FROGE" } };
            Assert.AreEqual(a.Key, b.Key);
            Assert.AreNotEqual(a.Key, E("evt_rug", Now, "ticker", "FROG", "alias", "Edev").Key);
        }

        [Test]
        public void RugShoutUsesTheTickerFromTheEvent()
        {
            var plan = Plan(E("evt_rug", Now, "ticker", "froge", "peakMcapCents", "420000000"), "timeline_square");
            StringAssert.Contains("$FROGE", plan.CrierText);
        }
    }

    public class CrierQueueTests
    {
        [Test]
        public void ShoutsWaitForTheHoldThenSpaceOut()
        {
            var q = new CrierQueue();
            Assert.IsTrue(q.Enqueue("a", "evt_rug", "JUST IN: rug", 0f));
            Assert.IsTrue(q.Enqueue("b", "evt_coin_launch", "JUST IN: launch", 0.1f));
            Assert.IsNull(q.Due(0.5f, out _), "held");
            Assert.AreEqual("JUST IN: launch", q.Due(1.5f, out var k1), "newest first");
            Assert.AreEqual("b", k1);
            Assert.IsNull(q.Due(3f, out _), "gap between shouts");
            Assert.AreEqual("JUST IN: rug", q.Due(5.6f, out _));
        }

        [Test]
        public void ServerCrierLineCancelsTheClientShout()
        {
            var q = new CrierQueue();
            q.Enqueue("a", "evt_rug", "JUST IN: rug", 0f);
            q.ServerLine("crier_rug", 0.5f);
            Assert.AreEqual(0, q.Count);
            Assert.IsFalse(q.Enqueue("b", "evt_rug", "JUST IN: rug again", 1f), "server just said it");
            Assert.AreEqual("evt_liquidation", CrierQueue.CategoryOfServerLine("crier_long_liquidation"));
            Assert.IsNull(CrierQueue.CategoryOfServerLine("crier_air_thin"));
        }

        [Test]
        public void QueueIsBoundedDedupedAndExpires()
        {
            var q = new CrierQueue();
            for (int i = 0; i < 10; i++) q.Enqueue("k" + i, null, "t" + i, 0f);
            Assert.AreEqual(CrierQueue.Capacity, q.Count);
            Assert.IsFalse(q.Enqueue("k9", null, "dup", 0f));
            Assert.IsNull(q.Due(CrierQueue.TtlSeconds + 1f, out _), "expired");
            q.Enqueue("x", null, "t", 100f);
            Assert.IsNotNull(q.Due(102f, out _));
            Assert.IsFalse(q.Enqueue("x", null, "t", 103f), "already shouted");
            Assert.IsFalse(q.Enqueue("y", null, "", 103f));
        }
    }

    public class MoodModelTests
    {
        [Test]
        public void BandsUseTheDoomposterThresholdWithHysteresis()
        {
            var m = new MoodModel();
            Assert.IsFalse(m.Update(0));
            Assert.AreEqual(MoodBand.Neutral, m.Band);
            Assert.IsTrue(m.Update(-201));
            Assert.AreEqual(MoodBand.Red, m.Band);
            Assert.IsFalse(m.Update(-160), "inside hysteresis");
            Assert.AreEqual(MoodBand.Red, m.Band);
            Assert.IsTrue(m.Update(-140));
            Assert.AreEqual(MoodBand.Neutral, m.Band);
            Assert.IsTrue(m.Update(250));
            Assert.AreEqual(MoodBand.Green, m.Band);
            Assert.IsTrue(m.Update(-900));
            Assert.AreEqual(MoodBand.Red, m.Band);
            Assert.AreEqual(-1f, m.Normalized);
        }

        [Test]
        public void TintIsRedForRedAndGreenForGreen()
        {
            var m = new MoodModel();
            m.Update(-400);
            var red = m.Tint(out var s1);
            Assert.Greater(red.R, red.G);
            Assert.Greater(s1, 0.5f);
            m.Update(400);
            var green = m.Tint(out _);
            Assert.Greater(green.G, green.R);
            m.Update(0);
            Assert.AreEqual("1,1,1", m.Tint(out var s0).ToString());
            Assert.AreEqual(0f, s0);
        }
    }
}
