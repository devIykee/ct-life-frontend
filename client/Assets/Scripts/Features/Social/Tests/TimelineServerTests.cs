using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.SocialFeatures.Tests
{
    /// <summary>Backend #32 (Timeline): real local replies (branch feat/timeline-world-feed 704c47d), trimmed.</summary>
    public sealed class TimelineServerTests
    {
        private const string ForYouJson = @"{""posts"":[{""postId"":""01a175b5-7429-77ae-abe0-7484e6c07b0b"",""userId"":""01a1751f-1b8c-7063-ac7d-7b491b1a972b"",""alias"":""lanec_sniper"",""text"":""$TLPVC is sending. entry here, NFA"",""createdAt"":""2026-10-26T00:56:28.454Z"",""kind"":""player"",""isNpc"":false,""district"":null,""meta"":{""card"":{""type"":""coin"",""coinId"":""01a175b5-6821-73dc-a6d8-4649753c10b5""}},""card"":{""type"":""coin"",""coinId"":""01a175b5-6821-73dc-a6d8-4649753c10b5"",""ticker"":""TLPVC"",""name"":""Ape Signal PVC"",""stage"":""new"",""mcapCents"":""768000"",""change5mBps"":-2320,""holders"":0,""rugged"":false,""dead"":false,""deepLink"":""ctlife://coin/01a175b5-6821-73dc-a6d8-4649753c10b5""},""secondaryCard"":null,""verdict"":null,""social"":null},{""postId"":""01a175b4-d5fe-7835-a1d2-3ece807297c6"",""userId"":""00000000-0000-4000-8000-0000000051e7"",""alias"":""The Sleuth"",""text"":""Receipts for $RBARN holders: lanec_rugger dumped 63% right after 1 of you bought in. Pattern noted, name logged, heat now 85."",""createdAt"":""2026-10-26T00:55:47.956Z"",""kind"":""sleuth"",""isNpc"":true,""district"":null,""meta"":{""heat"":85,""coinId"":""01a175b4-c5be-7ddd-af69-5b1fb3567f88"",""ticker"":""RBARN"",""deepLink"":""venue:launchpad_tower"",""sellSharePct"":63,""launcherAlias"":""lanec_rugger"",""launcherUserId"":""01a1751e-4395-7f39-aabe-ae41cf57e656""},""card"":{""type"":""coin"",""coinId"":""01a175b4-c5be-7ddd-af69-5b1fb3567f88"",""ticker"":""RBARN"",""name"":""Rug Bait ARN"",""stage"":""new"",""mcapCents"":""988000"",""change5mBps"":-60,""holders"":0,""rugged"":true,""dead"":false,""deepLink"":""ctlife://coin/01a175b4-c5be-7ddd-af69-5b1fb3567f88""},""secondaryCard"":{""type"":""venue"",""venueId"":""launchpad_tower"",""name"":""Launchpad Tower"",""deepLink"":""ctlife://venue/launchpad_tower""},""verdict"":null,""social"":null}],""nextBefore"":null,""quiet"":false}";
        private const string CityJson = @"{""posts"":[{""postId"":""01a175b5-8cdc-770a-a8c4-9b95363af1d1"",""userId"":""00000000-0000-4000-8000-00000000c17e"",""alias"":""CT City"",""text"":""RUGGED: $RBPVC by lanec_rugger"",""createdAt"":""2026-10-26T00:56:34.779Z"",""kind"":""rug"",""isNpc"":true,""district"":null,""meta"":{""card"":{""type"":""coin"",""coinId"":""01a175b5-7c3f-7e07-ac8f-9eef7832103b""},""coinId"":""01a175b5-7c3f-7e07-ac8f-9eef7832103b"",""reason"":""creator_dump"",""ticker"":""RBPVC"",""launcherAlias"":""lanec_rugger""},""card"":{""type"":""coin"",""coinId"":""01a175b5-7c3f-7e07-ac8f-9eef7832103b"",""ticker"":""RBPVC"",""name"":""Rug Bait PVC"",""stage"":""new"",""mcapCents"":""997000"",""change5mBps"":-30,""holders"":0,""rugged"":true,""dead"":false,""deepLink"":""ctlife://coin/01a175b5-7c3f-7e07-ac8f-9eef7832103b""},""secondaryCard"":{""type"":""venue"",""venueId"":""launchpad_tower"",""name"":""Launchpad Tower"",""deepLink"":""ctlife://venue/launchpad_tower""},""verdict"":null,""social"":null},{""postId"":""01a175b5-7c51-7081-a3e3-034355fe1e1d"",""userId"":""00000000-0000-4000-8000-00000000c17e"",""alias"":""CT City"",""text"":""lanec_rugger launched $RBPVC"",""createdAt"":""2026-10-26T00:56:30.544Z"",""kind"":""launch"",""isNpc"":true,""district"":null,""meta"":{""card"":{""type"":""coin"",""coinId"":""01a175b5-7c3f-7e07-ac8f-9eef7832103b""},""coinId"":""01a175b5-7c3f-7e07-ac8f-9eef7832103b"",""ticker"":""RBPVC"",""launcherAlias"":""lanec_rugger"",""launcherUserId"":""01a1751e-4395-7f39-aabe-ae41cf57e656""},""card"":{""type"":""coin"",""coinId"":""01a175b5-7c3f-7e07-ac8f-9eef7832103b"",""ticker"":""RBPVC"",""name"":""Rug Bait PVC"",""stage"":""new"",""mcapCents"":""997000"",""change5mBps"":-30,""holders"":0,""rugged"":true,""dead"":false,""deepLink"":""ctlife://coin/01a175b5-7c3f-7e07-ac8f-9eef7832103b""},""secondaryCard"":null,""verdict"":null,""social"":{""holders"":0,""followed"":[]}}],""nextBefore"":null,""quiet"":false}";

        [Test]
        public void ForYou_PageWithServerCoinCard()
        {
            var page = SocialParse.Page(JToken.Parse(ForYouJson));
            Assert.AreEqual(false, page.Quiet);
            Assert.IsNull(page.NextBefore);
            var post = page.Posts[0];
            Assert.AreEqual(FeedKinds.Player, post.Kind);
            Assert.IsFalse(post.IsNpc);
            Assert.AreEqual("coin", post.Card.Type);
            Assert.AreEqual("TLPVC", post.Card.Ticker);
            Assert.IsFalse(post.Card.Rugged);
            var sleuth = page.Posts[1];
            Assert.IsTrue(sleuth.IsNpc);
            Assert.IsTrue(sleuth.Card.Rugged, "the exposed coin's card is red");
            Assert.AreEqual("launchpad_tower", sleuth.SecondaryCard.VenueId);
        }

        [Test]
        public void City_RugCardIsAnEventWithTwoCards()
        {
            var page = SocialParse.Page(JToken.Parse(CityJson));
            var rug = page.Posts[0];
            Assert.AreEqual(EventKinds.Rug, rug.Kind);
            Assert.IsTrue(EventKinds.IsCity(rug.Kind));
            StringAssert.StartsWith("RUGGED: $", rug.Text);
            Assert.AreEqual("coin", rug.Card.Type);
            Assert.IsTrue(rug.Card.Rugged);
            Assert.Greater(rug.Card.McapCents, 0);
            Assert.AreEqual("ctlife://venue/launchpad_tower", rug.SecondaryCard.DeepLink);
        }

        [Test]
        public void OldServer_HasNoQuietFlag()
        {
            var page = SocialParse.Page(JToken.Parse(@"{""posts"":[{""postId"":""p"",""userId"":""u"",""alias"":""a"",""text"":""gm"",""createdAt"":""2026-10-25T22:00:00Z""}]}"));
            Assert.IsNull(page.Quiet);
            Assert.IsNull(page.Posts[0].Card);
        }

        [Test]
        public void Cards_RejectBadIds()
        {
            Assert.IsNull(ServerCard.Parse(JToken.Parse(@"{""type"":""venue"",""venueId"":""../x""}")));
            Assert.IsNull(ServerCard.Parse(JToken.Parse(@"{""type"":""coin""}")));
            Assert.IsNull(ServerCard.Parse(JToken.Parse(@"{""type"":""ad"",""venueId"":""perp_pit""}")));
            Assert.AreEqual("rug_room", ServerCard.Parse(JToken.Parse(@"{""type"":""venue"",""venueId"":""rug_room"",""name"":""The Rug Room""}")).VenueId);
        }

        [Test]
        public void DeepLinks_ProfileCoinVenue()
        {
            Assert.IsTrue(CTLife.Core.VenueLinks.TryParseProfile("ctlife://profile/01a1751f-1b8c", out var u));
            Assert.AreEqual("01a1751f-1b8c", u);
            Assert.IsFalse(CTLife.Core.VenueLinks.TryParseProfile("ctlife://profile/<b>", out _));
            Assert.IsTrue(CTLife.Core.VenueLinks.TryParse("ctlife://venue/rug_room", out var v));
            Assert.AreEqual("rug_room", v);
        }
    }
}
