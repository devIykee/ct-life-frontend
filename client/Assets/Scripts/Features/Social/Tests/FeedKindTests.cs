using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.SocialFeatures.Tests
{
    /// <summary>Feed post kinds (backend #14 Sleuth exposé, #16 career posts): a real local GET /feed reply, trimmed.</summary>
    public sealed class FeedKindTests
    {
        private const string SleuthFeed = @"{""posts"":[{""postId"":""01a1751f-93cb-757b-a3ef-602a721c734a"",""userId"":""00000000-0000-4000-8000-0000000051e7"",""alias"":""The Sleuth"",""text"":""Public service note. $RUGME dev lanec_rugger exited 63% of the coin the minute liquidity showed up. Check who you ape for. Their heat reads 44."",""createdAt"":""2026-10-25T22:12:46.141Z"",""kind"":""sleuth"",""meta"":{""heat"":44,""coinId"":""01a1751f-9262-78cd-ae09-8df5ddc5eb25"",""ticker"":""RUGME"",""deepLink"":""venue:launchpad_tower"",""sellSharePct"":63,""launcherAlias"":""lanec_rugger"",""launcherUserId"":""01a1751e-4395-7f39-aabe-ae41cf57e656""}}]}";

        [Test]
        public void Sleuth_ParsesMetaAndDeepLink()
        {
            var posts = SocialParse.Feed(JToken.Parse(SleuthFeed), out int skipped);
            Assert.AreEqual(0, skipped);
            var p = posts[0];
            Assert.AreEqual(FeedKinds.Sleuth, p.Kind);
            Assert.IsTrue(p.IsSleuth);
            Assert.AreEqual("The Sleuth", p.Alias);
            Assert.AreEqual("venue:launchpad_tower", p.DeepLink);
            Assert.AreEqual("RUGME", p.Sleuth.Ticker);
            Assert.AreEqual("01a1751f-9262-78cd-ae09-8df5ddc5eb25", p.Sleuth.CoinId);
            Assert.AreEqual("lanec_rugger", p.Sleuth.LauncherAlias);
            Assert.AreEqual(63, p.Sleuth.SellSharePct);
            Assert.AreEqual(44, p.Sleuth.Heat);
        }

        [Test]
        public void PlayerPosts_IgnoreMeta_UnknownKindsReadAsPlayer()
        {
            var p = SocialParse.Post(JToken.Parse(@"{""postId"":""p1"",""userId"":""u1"",""alias"":""a"",""text"":""gm"",""createdAt"":""2026-10-25T22:00:00Z"",""kind"":""player"",""meta"":{""deepLink"":""venue:x""}}"));
            Assert.AreEqual(FeedKinds.Player, p.Kind);
            Assert.IsNull(p.DeepLink);
            Assert.IsNull(p.Sleuth);
            var old = SocialParse.Post(JToken.Parse(@"{""postId"":""p2"",""userId"":""u1"",""alias"":""a"",""text"":""gm"",""createdAt"":""2026-10-25T22:00:00Z""}"));
            Assert.AreEqual(FeedKinds.Player, old.Kind);
            var odd = SocialParse.Post(JToken.Parse(@"{""postId"":""p3"",""userId"":""u1"",""text"":""x"",""kind"":""ad""}"));
            Assert.AreEqual(FeedKinds.Player, odd.Kind);
        }

        [Test]
        public void Sleuth_BadMetaKeepsThePost()
        {
            var p = SocialParse.Post(JToken.Parse(@"{""postId"":""p4"",""userId"":""npc"",""alias"":""The Sleuth"",""text"":""x"",""kind"":""sleuth"",""meta"":{""coinId"":""../x"",""ticker"":""""}}"));
            Assert.AreEqual(FeedKinds.Sleuth, p.Kind);
            Assert.IsNull(p.Sleuth);
            Assert.IsNull(p.DeepLink);
        }
    }
}
