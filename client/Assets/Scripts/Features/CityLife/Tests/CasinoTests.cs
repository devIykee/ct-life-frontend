using System.Collections.Generic;
using CTLife.CityLife.Casino;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    /// <summary>The Rug Room payloads (backend PR #12 contract/openapi.yaml, tag casino), the crash curve and the fairness check.
    /// The fairness vector was computed with the backend's own casino-math.ts (drawStream, spinSlots, crashPointX100).</summary>
    public sealed class CasinoTests
    {
        private const string Seed = "a3f1c2d4e5b60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00";
        private const string Hash = "1309f953015195d9eadc0455c1e42a5cfc463d4cad90c0cc940ff77b5f1633bd";
        private const string Rid = "0192e1f0-0000-7000-8000-000000000001";

        private static Dictionary<string, int> Weights() => new Dictionary<string, int>
            { ["rocket"] = 5, ["diamond_hands"] = 9, ["ape"] = 13, ["laser_eyes"] = 8, ["rug"] = 10, ["wild"] = 2 };

        [Test]
        public void State_ParsesGatesCapsAndPaytable()
        {
            var s = CasinoState.Parse(JObject.Parse(@"{""disclaimer"":""Play money. No cash value. 18+"",""sessionMinutes"":31,""sessionReminder"":true,
                ""ageConfirmed"":true,""selfExcludedUntil"":null,
                ""limits"":{""minStakeCents"":100,""maxStakeCents"":10000,""dailyStakeCapCents"":50000,""dailyNetLossCapCents"":25000},
                ""today"":{""stakedCents"":49950,""netLossCents"":1000,""stakeRemainingCents"":50,""lossRemainingCents"":24000,""resetsAt"":""2026-10-25T00:00:00.000Z""},
                ""games"":{""slots"":{""reelWeights"":{""rocket"":5,""wild"":2},""paytable"":{""three"":{""rocket"":30},""two"":{""rocket"":1.5}},""rtp"":0.9505,""hitRate"":0.488},
                ""crash"":{""growthPerSecond"":0.07,""maxMultiplier"":100,""minAutoCashout"":1.01,""rtp"":0.95},
                ""coinflip"":{""payoutMultiplier"":1.9,""rtp"":0.95,""h2hHouseCutBps"":500,""challengeTtlSeconds"":120}}}"));
            Assert.IsTrue(s.AgeConfirmed);
            Assert.IsTrue(s.SessionReminder);
            Assert.AreEqual(31, s.SessionMinutes);
            Assert.AreEqual(50, s.StakeRoomCents);
            Assert.IsTrue(s.CapReached);
            Assert.IsTrue(s.StakeCapHit);
            Assert.AreEqual(30.0, s.PayThree["rocket"]);
            Assert.AreEqual(1792886400000L, s.ResetsAtMs);
            Assert.IsFalse(s.Excluded(1792800000000.0));
        }

        [Test]
        public void State_SelfExcluded_UntilDate()
        {
            var s = CasinoState.Parse(JObject.Parse(@"{""ageConfirmed"":false,""selfExcludedUntil"":""2026-11-01T00:00:00Z"",""limits"":{},""today"":{},""games"":{}}"));
            Assert.IsFalse(s.AgeConfirmed);
            Assert.IsTrue(s.Excluded(1792800000000.0));
            Assert.AreEqual(CasinoGames.FallbackDisclaimer, s.Disclaimer);
            Assert.AreEqual(6, s.ReelWeights.Count);   // the documented weights when the server sends none
        }

        [Test]
        public void Bet_ParsesSlotsAndOpenCrash()
        {
            var b = CasinoBet.Parse(JObject.Parse(@"{""disclaimer"":""Play money. No cash value. 18+"",""sessionMinutes"":2,""sessionReminder"":false,""cashCents"":123450,
                ""round"":{""roundId"":""" + Rid + @""",""game"":""slots"",""status"":""settled"",""stakeCents"":500,""payoutCents"":5000,""params"":{},
                ""outcome"":{""reels"":[""ape"",""wild"",""ape""],""multiplier"":10,""winSymbol"":""ape"",""jackpot"":false},
                ""seedHash"":""" + Hash + @""",""seed"":""" + Seed + @""",""challengeId"":null,""createdAt"":""2026-10-24T10:00:00Z"",""settledAt"":""2026-10-24T10:00:00Z""}}"));
            Assert.AreEqual(123450, b.CashCents);
            CollectionAssert.AreEqual(new[] { "ape", "wild", "ape" }, b.Round.Reels);
            Assert.AreEqual(10.0, b.Round.Multiplier);
            Assert.AreEqual(4500, b.Round.NetCents);

            var c = CasinoBet.Parse(JObject.Parse(@"{""cashCents"":1,""round"":{""roundId"":""" + Rid + @""",""game"":""crash"",""status"":""open"",""stakeCents"":100,
                ""payoutCents"":null,""params"":{""startedAtMs"":1792800000000,""growthPerSecond"":0.07,""maxMultiplier"":100},""outcome"":null,
                ""seedHash"":""x"",""seed"":null,""challengeId"":null,""createdAt"":""2026-10-24T10:00:00Z"",""settledAt"":null}}"));
            Assert.IsTrue(c.Round.Open);
            Assert.AreEqual(1792800000000L, c.Round.StartedAtMs);
            Assert.IsNull(c.Round.PayoutCents);
            Assert.IsNull(c.Round.AutoCashout);
        }

        [Test]
        public void Challenge_RolesAndSides()
        {
            var list = CasinoChallenge.ParseList(JObject.Parse(@"{""challenges"":[{""challengeId"":""c1"",""role"":""opponent"",""challengerId"":""u1"",""challengerAlias"":""degen"",
                ""opponentId"":""u2"",""opponentAlias"":""me"",""stakeCents"":500,""challengerSide"":""heads"",""status"":""open"",""seedHash"":""h"",""seed"":null,
                ""result"":null,""winnerId"":null,""potCents"":null,""houseCutCents"":null,""payoutCents"":null,""createdAt"":""2026-10-24T10:00:00Z"",
                ""expiresAt"":""2026-10-24T10:02:00Z"",""settledAt"":null}]}"));
            Assert.AreEqual(1, list.Count);
            var c = list[0];
            Assert.IsTrue(c.Incoming && c.Open);
            Assert.AreEqual("tails", c.MySide);
            Assert.AreEqual("degen", c.PeerAlias);
            Assert.AreEqual(120.0, c.SecondsLeft(c.CreatedAtMs), 0.001);
        }

        [Test]
        public void CrashCurve_MatchesTheServer()
        {
            Assert.AreEqual(1.0, CasinoMath.CrashMultiplier(0, 0.07), 1e-9);
            Assert.AreEqual(2.0, CasinoMath.CrashMultiplier(CasinoMath.SecondsTo(2.0, 0.07), 0.07), 1e-9);
            Assert.AreEqual(9.9, CasinoMath.SecondsTo(2.0, 0.07), 0.05);
            Assert.AreEqual(1.99, CasinoMath.Floor2(1.999));
            Assert.AreEqual(1150, CasinoMath.Payout(500, 2.3));
        }

        [Test]
        public void Fairness_RecomputesTheBackendVector()
        {
            Assert.AreEqual(Hash, CasinoMath.Sha256Hex(Seed));
            Assert.AreEqual(0.8430412116336603, CasinoMath.Draw(Seed, "slots:" + Rid, 0), 1e-15);
            var slots = new CasinoRound
            {
                RoundId = Rid, Game = "slots", Status = "settled", SeedHash = Hash, Seed = Seed,
                Outcome = JObject.Parse(@"{""reels"":[""rug"",""diamond_hands"",""ape""],""multiplier"":0}"),
            };
            var v = CasinoMath.Verify(slots, Weights());
            Assert.IsTrue(v.HashOk);
            Assert.IsTrue(v.OutcomeOk, v.Recomputed);
            var crash = new CasinoRound { RoundId = Rid, Game = "crash", Status = "settled", SeedHash = Hash, Seed = Seed, Outcome = JObject.Parse(@"{""crashPoint"":7.7}") };
            Assert.IsTrue(CasinoMath.Verify(crash, Weights()).Ok);
            var flip = new CasinoRound { RoundId = Rid, Game = "coinflip", Status = "settled", SeedHash = Hash, Seed = Seed, Outcome = JObject.Parse(@"{""result"":""tails""}") };
            Assert.IsTrue(CasinoMath.Verify(flip, Weights()).Ok);
            flip.Outcome = JObject.Parse(@"{""result"":""heads""}");
            Assert.IsFalse(CasinoMath.Verify(flip, Weights()).Ok);
        }

        [Test]
        public void Copy_HasNoCashOutWording()
        {
            foreach (var key in new[] { "crash.take", "crash.took", "slots.win", "flip.won", "app.tagline", "lim.excludeBody", "rem.body" })
            {
                var t = CasinoText.T(key).ToLowerInvariant();
                StringAssert.DoesNotContain("cash out", t);
                StringAssert.DoesNotContain("cash-out", t);
                StringAssert.DoesNotContain("withdraw", t);
            }
        }
    }
}
