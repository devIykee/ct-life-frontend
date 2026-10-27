using CTLife.CityLife.Season;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    /// <summary>Season tracks and the nods book (backend #13): fixtures are real local responses, trimmed.</summary>
    public sealed class SeasonTests
    {
        private const string SeasonJson = @"{""season"": {""seasonId"": ""s1_airdrop_season"", ""name"": ""Airdrop Season"", ""startsAt"": ""2026-10-19T00:00:00Z"", ""endsAt"": ""2026-12-14T00:00:00Z"", ""xpPerTier"": 250}, ""xp"": 520, ""tier"": 2, ""xpToNext"": 230, ""hasPass"": false, ""tracks"": {""free"": [{""tier"": 1, ""xpRequired"": 250, ""reward"": {""cashCents"": 500}, ""status"": ""claimed""}, {""tier"": 2, ""xpRequired"": 500, ""reward"": {""cashCents"": 750}, ""status"": ""claimable""}, {""tier"": 3, ""xpRequired"": 750, ""reward"": {""cosmeticId"": ""reward_s1_gm_cap""}, ""status"": ""locked""}, {""tier"": 4, ""xpRequired"": 1000, ""reward"": {""cashCents"": 1000}, ""status"": ""locked""}], ""paid"": [{""tier"": 1, ""xpRequired"": 250, ""reward"": {""cashCents"": 1000}, ""status"": ""pass_required""}, {""tier"": 2, ""xpRequired"": 500, ""reward"": {""furnitureItemId"": ""lamp_architect""}, ""status"": ""pass_required""}, {""tier"": 3, ""xpRequired"": 750, ""reward"": {""cashCents"": 2000}, ""status"": ""locked""}]}}";
        private const string BookJson = @"{""total"": 80, ""collected"": 0, ""sets"": [{""setId"": ""memecoin_row"", ""name"": ""Memecoin Row"", ""reward"": {""cosmeticId"": ""reward_nod_memecoin_pin""}, ""total"": 18, ""collected"": 0, ""completed"": false, ""rewardGranted"": false, ""nods"": [{""nodId"": ""bonk"", ""name"": ""BONK"", ""tier"": ""A"", ""found"": true, ""collectedAt"": ""2026-10-25T10:00:00Z""}, {""nodId"": ""wif"", ""name"": ""dogwifhat (WIF)"", ""tier"": ""A"", ""found"": false, ""collectedAt"": null}, {""nodId"": ""popcat"", ""name"": ""Popcat (POPCAT)"", ""tier"": ""A"", ""found"": false, ""collectedAt"": null}]}, {""setId"": ""nft_gallery"", ""name"": ""NFT Gallery"", ""reward"": {""cosmeticId"": ""reward_nod_gallery_lanyard""}, ""total"": 18, ""collected"": 0, ""completed"": false, ""rewardGranted"": false, ""nods"": [{""nodId"": ""madlads"", ""name"": ""Mad Lads"", ""tier"": ""A"", ""found"": false, ""collectedAt"": null}, {""nodId"": ""smb"", ""name"": ""Solana Monkey Business"", ""tier"": ""B"", ""found"": false, ""collectedAt"": null}, {""nodId"": ""claynosaurz"", ""name"": ""Claynosaurz"", ""tier"": ""A"", ""found"": false, ""collectedAt"": null}]}]}";

        [Test]
        public void Season_ParsesTracksAndStatus()
        {
            var s = SeasonView.Parse(JObject.Parse(SeasonJson));
            Assert.AreEqual("s1_airdrop_season", s.SeasonId);
            Assert.AreEqual(250, s.XpPerTier);
            Assert.AreEqual(4, s.Free.Count);
            Assert.IsTrue(s.Free[0].Claimed);
            Assert.IsTrue(s.Free[1].Claimable);
            Assert.IsTrue(s.Paid[0].PassRequired);
            Assert.AreEqual(1, s.ClaimableCount);
            Assert.AreEqual("cash", s.Free[0].Reward.Kind);
            Assert.AreEqual(0.08f, s.TierProgress, 0.001f);
        }

        [Test]
        public void Book_FoundAndMarkFound()
        {
            var b = NodsBook.Parse(JObject.Parse(BookJson));
            Assert.AreEqual(2, b.Sets.Count);
            Assert.IsTrue(b.IsFound("bonk"));
            Assert.IsFalse(b.IsFound("wif"));
            int before = b.Sets[0].Collected;
            b.MarkFound("wif", 1);
            Assert.IsTrue(b.IsFound("wif"));
            Assert.AreEqual(before + 1, b.Sets[0].Collected);
        }

        [Test]
        public void Collect_NullSetReward()
        {
            // device 2026-10-25: a real reply (setReward null) threw on a child access
            var c = NodCollect.Parse(JObject.Parse(@"{""nodId"":""mtndao"",""alreadyCollected"":true,""xp"":0,""set"":{""setId"":""event_hall"",""collected"":1,""total"":4,""completed"":false},""setReward"":null}"));
            Assert.IsTrue(c.AlreadyCollected);
            Assert.AreEqual(4, c.SetTotal);
            Assert.IsNull(c.SetRewardCosmeticId);
        }

        [Test]
        public void Collect_ParsesSetReward()
        {
            var c = NodCollect.Parse(JObject.Parse(@"{""nodId"":""bonk"",""alreadyCollected"":false,""xp"":25,""set"":{""setId"":""memecoin_row"",""collected"":18,""total"":18,""completed"":true},""setReward"":{""setId"":""memecoin_row"",""cosmeticId"":""reward_nod_memecoin_pin""}}"));
            Assert.AreEqual(25, c.Xp);
            Assert.IsTrue(c.SetCompleted);
            Assert.AreEqual("reward_nod_memecoin_pin", c.SetRewardCosmeticId);
        }
    
        [Test]
        public void Rewards_PrettyNames()
        {
            Assert.AreEqual("GM Cap", SeasonRewards.Pretty("reward_s1_gm_cap"));
            Assert.AreEqual("TGE Hoodie", SeasonRewards.Pretty("reward_s1_tge_hoodie"));
            Assert.AreEqual("Memecoin Pin", SeasonRewards.Pretty("reward_nod_memecoin_pin"));
            Assert.AreEqual("Zebra Plant", SeasonRewards.Pretty("plant_zebra"));
            Assert.AreEqual("Architect Lamp", SeasonRewards.Pretty("lamp_architect"));
            var cash = SeasonRewards.Describe(SeasonReward.Parse(Newtonsoft.Json.Linq.JToken.Parse(@"{""cashCents"":500}")));
            Assert.AreEqual("kind.cash", cash.KindKey);
            Assert.IsNotNull(cash.Amount);
        }
    }
}
