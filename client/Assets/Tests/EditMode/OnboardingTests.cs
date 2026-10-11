using System;
using System.Collections.Generic;
using CTLife.Net;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Tests
{
    public class OnboardingTests
    {
        /// <summary>Decodes a /me body shaped exactly like backend main's (character null before creation, flags {}).</summary>
        private static Me NewMe(long cash = 0, long debt = 0, string alias = null, Dictionary<string, object> flags = null)
        {
            var body = new JObject
            {
                ["user"] = new JObject { ["id"] = "01a11860-9c1f-77a4-a496-17038bb21f01", ["wallet"] = "HAoo9696iSShm1io2SsnU6R7XhAn8y1YH7sEHgFajESf",
                    ["isSeeker"] = false, ["skrName"] = null, ["isSybilJailed"] = false, ["sybilJailedAt"] = null },
                ["character"] = alias == null ? null : new JObject
                {
                    ["alias"] = alias, ["tribeId"] = "trencher", ["traitIds"] = new JArray("fast_talker", "iron_will"), ["dreamId"] = "make_10k",
                    ["homeId"] = "parents_basement",
                    ["look"] = new JObject { ["skinTone"] = "skin_tone3", ["headId"] = "head_snow", ["hairId"] = "hair_snow_bun", ["topId"] = "top_vneck_charcoal",
                        ["bottomId"] = "bottom_denim_light", ["shoesId"] = "shoes_sneakers_blue", ["accId"] = "acc_none" },
                },
                ["needs"] = new JObject { ["energy"] = 50, ["hunger"] = 50, ["fun"] = 50, ["hygiene"] = 50, ["sanity"] = 50 },
                ["skills"] = new JObject { ["clout"] = 1, ["coding"] = 1, ["trading"] = 1, ["writing"] = 1, ["design"] = 1, ["security"] = 1 },
                ["cashCents"] = cash, ["debtCents"] = debt, ["homeId"] = "parents_basement", ["minerTier"] = "none",
                ["flags"] = flags == null ? new JObject() : JObject.FromObject(flags),
            };
            return CTLife.Core.Json.Deserialize<Me>(MeCompat.Normalize(body).ToString());
        }

        [Test]
        public void BackendMainMeWithoutHasCharacterNeedsTheCompatShim()
        {
            var raw = JObject.Parse(@"{""user"":{""id"":""01a11860-9c1f-77a4-a496-17038bb21f01"",""wallet"":""W"",""isSeeker"":false},
                ""character"":null,""needs"":{""energy"":50,""hunger"":50,""fun"":50,""hygiene"":50,""sanity"":50},
                ""skills"":{""clout"":1,""coding"":1,""trading"":1,""writing"":1,""design"":1,""security"":1},
                ""cashCents"":0,""debtCents"":0,""homeId"":""parents_basement"",""minerTier"":""none"",""flags"":{}}");
            Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => CTLife.Core.Json.Deserialize<Me>(raw.ToString()));
            var me = CTLife.Core.Json.Deserialize<Me>(MeCompat.Normalize(raw).ToString());
            Assert.IsFalse(me.User.HasCharacter);
        }

        [Test]
        public void BackendMainMeBodyDecodes()
        {
            var me = NewMe();
            Assert.IsNotNull(me.User);
            Assert.IsNull(me.Character);
            Assert.AreEqual(Me.MinerTierEnum.None, me.MinerTier);
        }

        [Test]
        public void FreshAccountNeedsLotteryThenCharacter()
        {
            var me = NewMe();
            Assert.IsTrue(Onboarding.NeedsLottery(me), "homeId alone (always parents_basement) must not count as rolled");
            Assert.IsTrue(Onboarding.NeedsCharacter(me));
        }

        [Test]
        public void BalanceMeansTheLotteryWasRolled()
        {
            Assert.IsFalse(Onboarding.NeedsLottery(NewMe(cash: 30000, debt: 72000)));
            Assert.IsFalse(Onboarding.NeedsLottery(NewMe(debt: 72000)));
        }

        [Test]
        public void CharacterMeansRolledOnTheServer()
        {
            var me = NewMe(alias: "anon");
            Assert.IsFalse(Onboarding.NeedsLottery(me));
            Assert.IsFalse(Onboarding.NeedsCharacter(me));
        }

        [Test]
        public void ExplicitFlagWins()
        {
            Assert.IsTrue(Onboarding.NeedsLottery(NewMe(cash: 500, flags: new Dictionary<string, object> { ["lotteryRolled"] = false })));
            Assert.IsFalse(Onboarding.NeedsLottery(NewMe(flags: new Dictionary<string, object> { ["lotteryRolled"] = true })));
        }

        [Test]
        public void AlreadyRolledIsSuccess()
        {
            Assert.IsTrue(Onboarding.RollFailureMeansDone(new ApiException(409, "ALREADY_ROLLED", "x"), null));
        }

        [Test]
        public void ServerErrorWithOutcomeOnProfileIsSuccess()
        {
            // backend main returns 500 for a second pre-character roll; /me then shows the first outcome.
            Assert.IsTrue(Onboarding.RollFailureMeansDone(new ApiException(500, "INTERNAL_ERROR", "x"), NewMe(cash: 30000, debt: 72000)));
            Assert.IsFalse(Onboarding.RollFailureMeansDone(new ApiException(500, "INTERNAL_ERROR", "x"), NewMe()));
        }

        [Test]
        public void NetworkFailureIsNeverTreatedAsDone()
        {
            Assert.IsFalse(Onboarding.RollFailureMeansDone(new ApiException(0, "NETWORK_ERROR", "x", isNetwork: true), NewMe(cash: 1)));
        }

        [Test]
        public void CreatorSubmitsAuthoredOrRegistryConfirmedLooksOnly()
        {
            var look = new[] { "skin_tone3", "head_snow", "hair_snow_bun", "top_vneck_charcoal", "bottom_denim_light", "shoes_sneakers_blue", "acc_none" };
            Assert.IsTrue(CreatorLookPolicy.CanSubmit(look, true, _ => false));
            Assert.IsTrue(CreatorLookPolicy.CanSubmit(look, false, _ => true));
            Assert.IsFalse(CreatorLookPolicy.CanSubmit(look, false, _ => false));
            var incomplete = (string[])look.Clone();
            incomplete[1] = "";
            Assert.IsFalse(CreatorLookPolicy.CanSubmit(incomplete, true, _ => true));
            Assert.IsFalse(CreatorLookPolicy.CanSubmit(new string[3], true, null));
        }
    }
}
