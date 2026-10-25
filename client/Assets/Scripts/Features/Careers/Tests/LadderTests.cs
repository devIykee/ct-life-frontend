using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace CTLife.Careers.Tests
{
    /// <summary>Career ladders (backend PR #16, GET /careers?view=ladders); the fixture is a real local response, trimmed.</summary>
    public sealed class LadderTests
    {
        private const string Fixture = @"{""myLadderId"": ""trader"", ""suggestedLadderId"": null, ""joinCooldownHours"": 72, ""canSwitchAt"": ""2026-10-27T12:00:00.000Z"", ""venueAccess"": [], ""ladders"": [{""id"": ""builder"", ""name"": ""Builder"", ""tribeIds"": [""builder""], ""jobIds"": [""junior_analyst"", ""dev_rel"", ""smart_contract_auditor""], ""active"": false, ""suggested"": false, ""xp"": 0, ""rank"": 1, ""title"": ""Builder Intern"", ""payBonusBps"": 0, ""nextRank"": {""rank"": 2, ""id"": ""junior"", ""title"": ""Junior Dev"", ""xpRequired"": 200, ""payBonusBps"": 300, ""venueAccess"": [], ""cosmeticId"": null}, ""xpToNext"": 200, ""ranks"": [{""rank"": 1, ""id"": ""intern"", ""title"": ""Builder Intern"", ""xpRequired"": 0, ""payBonusBps"": 0, ""venueAccess"": [], ""cosmeticId"": null, ""status"": ""current""}, {""rank"": 2, ""id"": ""junior"", ""title"": ""Junior Dev"", ""xpRequired"": 200, ""payBonusBps"": 300, ""venueAccess"": [], ""cosmeticId"": null, ""status"": ""next""}, {""rank"": 3, ""id"": ""mid"", ""title"": ""Shipper"", ""xpRequired"": 600, ""payBonusBps"": 700, ""venueAccess"": [], ""cosmeticId"": ""reward_career_builder_mid"", ""status"": ""locked""}, {""rank"": 4, ""id"": ""senior"", ""title"": ""Senior Builder"", ""xpRequired"": 1500, ""payBonusBps"": 1200, ""venueAccess"": [""hackathon_house:vip_desk""], ""cosmeticId"": null, ""status"": ""locked""}, {""rank"": 5, ""id"": ""lead"", ""title"": ""Tech Lead"", ""xpRequired"": 3500, ""payBonusBps"": 1800, ""venueAccess"": [], ""cosmeticId"": ""reward_career_builder_lead"", ""status"": ""locked""}, {""rank"": 6, ""id"": ""head"", ""title"": ""Head of Eng"", ""xpRequired"": 7000, ""payBonusBps"": 2600, ""venueAccess"": [""launchpad_tower:roof_deck""], ""cosmeticId"": null, ""status"": ""locked""}, {""rank"": 7, ""id"": ""legend"", ""title"": ""Builder Legend"", ""xpRequired"": 12000, ""payBonusBps"": 3500, ""venueAccess"": [], ""cosmeticId"": ""reward_career_builder_legend"", ""status"": ""locked""}]}, {""id"": ""trader"", ""name"": ""Trader"", ""tribeIds"": [""perp_degen"", ""prediction_shark""], ""jobIds"": [""junior_analyst"", ""alpha_lead""], ""active"": true, ""suggested"": false, ""xp"": 320, ""rank"": 2, ""title"": ""Scalper"", ""payBonusBps"": 300, ""nextRank"": {""rank"": 3, ""id"": ""mid"", ""title"": ""Swing Trader"", ""xpRequired"": 600, ""payBonusBps"": 700, ""venueAccess"": [], ""cosmeticId"": ""reward_career_trader_mid""}, ""xpToNext"": 280, ""ranks"": [{""rank"": 1, ""id"": ""intern"", ""title"": ""Trading Intern"", ""xpRequired"": 0, ""payBonusBps"": 0, ""venueAccess"": [], ""cosmeticId"": null, ""status"": ""achieved""}, {""rank"": 2, ""id"": ""junior"", ""title"": ""Scalper"", ""xpRequired"": 200, ""payBonusBps"": 300, ""venueAccess"": [], ""cosmeticId"": null, ""status"": ""current""}, {""rank"": 3, ""id"": ""mid"", ""title"": ""Swing Trader"", ""xpRequired"": 600, ""payBonusBps"": 700, ""venueAccess"": [], ""cosmeticId"": ""reward_career_trader_mid"", ""status"": ""next""}, {""rank"": 4, ""id"": ""senior"", ""title"": ""Desk Trader"", ""xpRequired"": 1500, ""payBonusBps"": 1200, ""venueAccess"": [""perp_pit:high_roller_floor""], ""cosmeticId"": null, ""status"": ""locked""}, {""rank"": 5, ""id"": ""lead"", ""title"": ""Prop Lead"", ""xpRequired"": 3500, ""payBonusBps"": 1800, ""venueAccess"": [], ""cosmeticId"": ""reward_career_trader_lead"", ""status"": ""locked""}, {""rank"": 6, ""id"": ""head"", ""title"": ""Fund Manager"", ""xpRequired"": 7000, ""payBonusBps"": 2600, ""venueAccess"": [""prediction_pit:vip_booth""], ""cosmeticId"": null, ""status"": ""locked""}, {""rank"": 7, ""id"": ""legend"", ""title"": ""Trading Legend"", ""xpRequired"": 12000, ""payBonusBps"": 3500, ""venueAccess"": [], ""cosmeticId"": ""reward_career_trader_legend"", ""status"": ""locked""}]}]}";

        [Test]
        public void Board_ParsesMineRanksAndCooldown()
        {
            var b = LadderBoard.Parse(JObject.Parse(Fixture));
            Assert.AreEqual(2, b.Ladders.Count);
            Assert.AreEqual("trader", b.MyLadderId);
            var mine = b.Mine;
            Assert.IsNotNull(mine);
            Assert.AreEqual(2, mine.Rank);
            Assert.AreEqual(7, mine.Ranks.Count);
            Assert.AreEqual(280, mine.XpToNext);
            Assert.IsNotNull(mine.NextRank);
            Assert.IsTrue(mine.Ranks[1].Current);
            Assert.IsTrue(mine.Progress > 0f && mine.Progress < 1f);
            Assert.IsFalse(b.CanSwitchNow(new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)));
            Assert.IsTrue(b.CanSwitchNow(new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void Formatting()
        {
            Assert.AreEqual("+7%", LadderBoard.Bonus(700));
            Assert.AreEqual("+3.5%", LadderBoard.Bonus(350));
            Assert.AreEqual("Perp pit · high roller floor", LadderBoard.Area("perp_pit:high_roller_floor"));
        }
    }
}
