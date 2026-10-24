using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    /// <summary>gm streak, chest and quest payloads (backend PR #9 contract/openapi.yaml) and the ctlife:// routes.</summary>
    public sealed class RetentionTests
    {
        [Test]
        public void DailyStatus_NewAccount_ShowsDayOneChest()
        {
            var s = DailyStatus.Parse(JObject.Parse(@"{""streakDay"":0,""claimedToday"":false,""nextResetAt"":""2026-10-25T00:00:00.000Z"",
                ""freezes"":0,""maxFreezes"":2,""freezePriceCents"":5000,""chest"":{""day"":1,""rewards"":{""cashCents"":1000,""cosmetic"":null}}}"));
            Assert.AreEqual(1, s.ShownDay);
            Assert.IsTrue(s.ChestReady);
            Assert.AreEqual(1000, s.Chest.CashCents);
            Assert.IsFalse(s.Chest.HasCosmetic);
            Assert.AreEqual(1792886400000L, s.NextResetAtMs);
            Assert.AreEqual("00:00:10", DailyStatus.Countdown(s.SecondsToReset(1792886390000.0)));
        }

        [Test]
        public void DailyClaim_MilestoneWithCosmeticAndFreezes()
        {
            var c = DailyClaim.Parse(JObject.Parse(@"{""streakDay"":7,""claimedToday"":true,""nextResetAt"":""2026-10-25T00:00:00Z"",""freezes"":0,
                ""maxFreezes"":2,""freezePriceCents"":5000,""chest"":{""day"":8,""rewards"":{""cashCents"":2500,""cosmetic"":null}},
                ""alreadyClaimed"":false,""claimed"":{""day"":7,""rewards"":{""cashCents"":2500,""cosmetic"":{""itemKey"":""head_cap_black"",""name"":""Black Cap""}}},
                ""freezesUsed"":1,""reset"":false}"));
            Assert.AreEqual(7, c.ShownDay);
            Assert.AreEqual(7, c.ClaimedDay);
            Assert.AreEqual("head_cap_black", c.Claimed.CosmeticKey);
            Assert.AreEqual(1, c.FreezesUsed);
            Assert.IsTrue(DailyStatus.IsMilestone(7));
            Assert.AreEqual(14, DailyStatus.NextMilestone(8));
            Assert.AreEqual(60, DailyStatus.NextMilestone(31));
        }

        [Test]
        public void Quests_ParseAndApplyProgress()
        {
            var b = QuestBoard.Parse(JObject.Parse(@"{""daily"":{""date"":""2026-10-24"",""resetsAt"":""2026-10-25T00:00:00Z"",""quests"":[
                {""id"":""q_daily_perp_open_1"",""title"":""Take a position"",""verb"":""perp_open"",""target"":1,""progress"":0,""completed"":false,""claimed"":false,""reward"":{""cashCents"":400,""seasonXp"":15},""deepLink"":""ctlife://perps""}]},
                ""weekly"":{""week"":""2026-W43"",""resetsAt"":""2026-10-26T00:00:00Z"",""quests"":[
                {""id"":""q_weekly_shift_5"",""title"":""Grind"",""verb"":""shift_complete"",""target"":5,""progress"":2,""completed"":false,""claimed"":false,""reward"":{""cashCents"":1500,""seasonXp"":40},""step"":2},
                {""id"":""q_weekly_tip_3"",""title"":""Tips"",""verb"":""tip_send"",""target"":3,""progress"":0,""completed"":false,""claimed"":false,""reward"":{""cashCents"":900,""seasonXp"":30},""step"":1}]}}"));
            Assert.AreEqual(1, b.Daily.Count);
            Assert.AreEqual("q_weekly_tip_3", b.Weekly[0].Id, "the chain is in step order");
            var p = QuestProgress.From(new Dictionary<string, object> { ["questId"] = "q_daily_perp_open_1", ["pool"] = "daily", ["progress"] = 1L, ["target"] = 1L, ["completed"] = true });
            var q = b.Apply(p);
            Assert.IsNotNull(q);
            Assert.IsTrue(q.Claimable);
            Assert.AreEqual(1, b.DailyDone);
            Assert.IsNull(b.Apply(QuestProgress.From(new Dictionary<string, object> { ["questId"] = "other", ["progress"] = 1L })));
            Assert.IsNull(QuestProgress.From("nope"));
        }

        [Test]
        public void DeepLinks_MapToApps()
        {
            Assert.AreEqual("quests", DeepLinks.AppFor("ctlife://quests"));
            Assert.AreEqual("predict", DeepLinks.AppFor("ctlife://predictions?x=1"));
            Assert.AreEqual("jobs", DeepLinks.AppFor("CTLIFE://jobs/shift"));
            Assert.AreEqual("week", DeepLinks.AppFor("ctlife://daily-clue"));
            Assert.AreEqual("map", DeepLinks.AppFor("ctlife://neighbours"));
            Assert.IsNull(DeepLinks.AppFor("https://evil.example/quests"));
            Assert.IsNull(DeepLinks.AppFor("ctlife://nowhere"));
        }
    }
}
