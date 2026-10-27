using System.Collections.Generic;
using System.Linq;
using CTLife.Realtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures.Tests
{
    /// <summary>Timeline as the world feed (queue 36): event cards, the mixes, action cards, toasts, plain text.</summary>
    public sealed class TimelineWorldTests
    {
        private const double T0 = 1_792_966_400_000;

        private static EventMessage Ev(string id, double at, params (string k, string v)[] p) =>
            new EventMessage { eventId = id, at = at, @params = p.ToDictionary(x => x.k, x => x.v) };

        private static FeedPostRow Post(string id, double at, string text = "gm", string user = "u1") =>
            new FeedPostRow { PostId = id, UserId = user, Alias = "dana", Text = text, AtMs = (long)at, Kind = FeedKinds.Player };

        [Test]
        public void EventCard_FromRealtimeLaunch()
        {
            var c = CityEventCards.FromEvent(Ev("evt_coin_launch", T0, ("coinId", "01a1"), ("ticker", "FROG"), ("playerLaunched", "1"), ("creatorAlias", "smoothbrain")), T0);
            Assert.AreEqual(EventKinds.Launch, c.Kind);
            Assert.AreEqual("smoothbrain launched $FROG", TimelineText.Event(c));
            var card = ActionCards.For(c, null);
            Assert.AreEqual(ActionCard.CardKind.Coin, card.Kind);
            Assert.AreEqual("01a1", card.CoinId);
            Assert.IsNull(CityEventCards.FromEvent(Ev("evt_unknown", T0), T0));
        }

        [Test]
        public void EventCard_RugAndLiquidationWords()
        {
            var rug = CityEventCards.FromEvent(Ev("evt_rug", T0, ("coinId", "c2"), ("ticker", "RUGME"), ("kind", "creator_dump"), ("launcherAlias", "lanec_rugger")), T0);
            Assert.AreEqual("$RUGME got rugged by its dev, lanec_rugger", TimelineText.Event(rug));
            var liq = CityEventCards.FromEvent(Ev("evt_liquidation", T0), T0);
            Assert.AreEqual("Longs liquidated on " + CTLife.Core.NameTable.Get("venue.perp_pit"), TimelineText.Event(liq));
            Assert.AreEqual(ActionCard.CardKind.Place, ActionCards.For(liq, null).Kind);
            Assert.AreEqual("perp_pit", ActionCards.For(liq, null).VenueId);
        }

        [Test]
        public void ForYou_AtMostOneEventInFourRows()
        {
            var posts = Enumerable.Range(0, 8).Select(i => Post("p" + i, T0 - i * 1000)).ToList();
            var events = Enumerable.Range(0, 5).Select(i => CityEventCards.FromEvent(Ev("evt_coin_launch", T0 - i * 500, ("coinId", "c" + i), ("ticker", "T" + i)), T0)).ToList();
            var mix = TimelineMix.ForYou(posts, events);
            for (int i = 0; i < mix.Count; i++)
                if (EventKinds.IsCity(mix[i].Kind))
                    for (int j = i + 1; j < System.Math.Min(mix.Count, i + TimelineMix.EventEvery); j++)
                        Assert.IsFalse(EventKinds.IsCity(mix[j].Kind), $"two events within {TimelineMix.EventEvery} rows at {i},{j}");
            Assert.AreEqual(8, mix.Count(p => p.Kind == FeedKinds.Player), "every post is shown");
            Assert.Less(mix.Count(p => EventKinds.IsCity(p.Kind)), events.Count, "extra events wait in City");
        }

        [Test]
        public void City_OnlyEvents_FilterAndDedupe()
        {
            var server = new FeedPostRow { PostId = "s1", UserId = "city", AtMs = (long)T0, Kind = EventKinds.Rug, Meta = new JObject { ["coinId"] = "c9", ["ticker"] = "X" } };
            var posts = new List<FeedPostRow> { Post("p1", T0), server };
            var live = new List<FeedPostRow>
            {
                CityEventCards.FromEvent(Ev("evt_rug", T0 + 30_000, ("coinId", "c9"), ("ticker", "X")), T0),   // same rug, from realtime
                CityEventCards.FromEvent(Ev("evt_coin_launch", T0 + 1000, ("coinId", "c1"), ("ticker", "A")), T0),
            };
            var all = TimelineMix.City(posts, live, null);
            Assert.AreEqual(2, all.Count);
            Assert.IsTrue(all.All(p => EventKinds.IsCity(p.Kind)));
            Assert.AreEqual(1, TimelineMix.City(posts, live, EventKinds.Launch).Count);
        }

        [Test]
        public void ActionCard_CoinBeatsPlace_AndSleuthCard()
        {
            var p = Post("p1", T0, "$FROG is sending. entry here");
            var card = ActionCards.For(p, t => t == "FROG" ? "coin-frog" : null);
            Assert.AreEqual(ActionCard.CardKind.Coin, card.Kind);
            Assert.AreEqual("coin-frog", card.CoinId);
            Assert.AreEqual(ActionCard.CardKind.None, ActionCards.For(Post("p2", T0, "gm"), _ => null).Kind);
            var sleuth = SocialParse.Post(JToken.Parse(@"{""postId"":""s"",""userId"":""npc"",""alias"":""The Sleuth"",""text"":""x"",""createdAt"":""2026-10-25T22:12:46Z"",""kind"":""sleuth"",""meta"":{""coinId"":""01a1751f-9262"",""ticker"":""RUGME"",""deepLink"":""venue:launchpad_tower"",""sellSharePct"":63,""heat"":44}}"));
            var sc = ActionCards.For(sleuth, null);
            Assert.AreEqual("01a1751f-9262", sc.CoinId);
            Assert.AreEqual("launchpad_tower", sc.VenueId);
            Assert.AreEqual(1, TimelineMix.Verdicts(new[] { sleuth }).Count);
        }

        [Test]
        public void Toasts_AboutMeAndDistrictMomentsOnly()
        {
            var policy = new TimelineToastPolicy();
            var sleuth = SocialParse.Post(JToken.Parse(@"{""postId"":""s"",""userId"":""npc"",""alias"":""The Sleuth"",""text"":""x"",""createdAt"":""2026-10-25T22:13:00Z"",""kind"":""sleuth"",""meta"":{""coinId"":""c"",""ticker"":""RUGME"",""launcherUserId"":""me-1"",""launcherAlias"":""me""}}"));
            Assert.AreEqual(TimelineToastPolicy.Reason.AboutMe, policy.Decide(sleuth, "me-1", "me", null, null, sleuth.AtMs + 1000));
            Assert.AreEqual(TimelineToastPolicy.Reason.None, policy.Decide(sleuth, "me-1", "me", null, null, sleuth.AtMs + 2000), "once per post");
            var mention = Post("m1", T0, "big call from @me today", "u9");
            Assert.AreEqual(TimelineToastPolicy.Reason.AboutMe, policy.Decide(mention, "me-1", "me", null, null, T0 + 1000));
            string District(string v) => v == "launchpad_tower" ? "trenches" : "timeline";
            var rug1 = CityEventCards.FromEvent(Ev("evt_rug", T0, ("coinId", "a"), ("ticker", "A")), T0);
            var rug2 = CityEventCards.FromEvent(Ev("evt_rug", T0 + 1000, ("coinId", "b"), ("ticker", "B")), T0);
            var rug3 = CityEventCards.FromEvent(Ev("evt_rug", T0 + 200_000, ("coinId", "c"), ("ticker", "C")), T0);
            Assert.AreEqual(TimelineToastPolicy.Reason.None, policy.Decide(rug1, "me-1", "me", "timeline", District, T0), "not my district");
            Assert.AreEqual(TimelineToastPolicy.Reason.CityMoment, policy.Decide(rug2, "me-1", "me", "trenches", District, T0 + 1000));
            Assert.AreEqual(TimelineToastPolicy.Reason.CityMoment, policy.Decide(rug3, "me-1", "me", "trenches", District, T0 + 200_000), "2 min later");
            var launch = CityEventCards.FromEvent(Ev("evt_coin_launch", T0 + 300_000, ("coinId", "d")), T0);
            Assert.AreEqual(TimelineToastPolicy.Reason.None, policy.Decide(launch, "me-1", "me", "trenches", District, T0 + 300_000), "launches are not big moments");
        }

        [Test]
        public void Toasts_NeverFromABlockedAuthor()
        {
            // security review 2026-10-26: a blocked player's post must not pop a HUD toast
            var policy = new TimelineToastPolicy();
            var mention = Post("m2", T0, "hey @me", "troll-1");
            Assert.AreEqual(TimelineToastPolicy.Reason.None, policy.Decide(mention, "me-1", "me", null, null, T0 + 1000, id => id == "troll-1"));
            var other = Post("m3", T0, "hey @me", "friend-1");
            Assert.AreEqual(TimelineToastPolicy.Reason.AboutMe, policy.Decide(other, "me-1", "me", null, null, T0 + 1000, id => id == "troll-1"));
        }

        [Test]
        public void PlayerText_RendersLiterally()
        {
            // security review 2026-10-26: names and bodies never render as markup
            var l = CTLife.UI.El.Text("<color=red><size=99>admin</size></color>");
            Assert.IsFalse(l.enableRichText);
            var e = CityEventCards.FromEvent(Ev("evt_coin_launch", T0, ("coinId", "c"), ("ticker", "X"), ("creatorAlias", "<b>dev</b>")), T0);
            var label = CTLife.UI.El.Text(TimelineText.Event(e), "tl-event-text");
            Assert.IsFalse(label.enableRichText);
            StringAssert.Contains("<b>dev</b>", label.text, "shown as typed");
        }
    }
}
