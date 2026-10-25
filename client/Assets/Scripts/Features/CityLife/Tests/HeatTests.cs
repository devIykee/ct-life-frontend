using System.Collections.Generic;
using CTLife.CityLife.Heat;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    /// <summary>Sleuth heat (backend PR #17, docs/HEAT.md): /heat/me, the realtime messages, stars and ids.</summary>
    public sealed class HeatTests
    {
        [Test]
        public void HeatMeParsesLevelPursuitAndJail()
        {
            var me = HeatMe.Parse(JToken.Parse(@"{""heat"":82.4,""level"":""wanted"",""decayAt"":""2026-10-26T10:00:00Z"",""wantedUntil"":""2026-10-25T20:00:00Z"",
                ""levels"":{""warm"":10,""hot"":35,""wanted"":70},
                ""pursuit"":{""pursuitId"":""0195d1c2-0000-7000-8000-000000000001"",""units"":2,""startedAt"":""2026-10-25T15:00:00Z"",""expiresAt"":""2026-10-25T15:03:00Z"",""escapeRadiusM"":200,""escapeSeconds"":60},
                ""jail"":null}"));
            Assert.AreEqual(HeatLevel.Wanted, me.Level);
            Assert.AreEqual(5, me.Stars);
            Assert.AreEqual(2, me.Pursuit.Units);
            Assert.AreEqual(200f, me.Pursuit.EscapeRadiusM);
            Assert.IsNull(me.Jail);
            var jailed = HeatMe.Parse(JToken.Parse(@"{""heat"":32,""level"":""warm"",""pursuit"":null,""jail"":{""venueId"":""ct_jail"",""jailedAt"":""2026-10-25T15:03:00Z"",""until"":""2026-10-25T15:09:00Z"",""bailCents"":4100}}"));
            Assert.AreEqual(4100, jailed.Jail.BailCents);
            Assert.AreEqual("ct_jail", jailed.Jail.VenueId);
            Assert.IsNull(HeatMe.Parse(JToken.Parse("[]")));
        }

        [Test]
        public void StarsFollowHeat()
        {
            Assert.AreEqual(0, HeatRules.Stars(9.9));
            Assert.AreEqual(1, HeatRules.Stars(10));
            Assert.AreEqual(2, HeatRules.Stars(34));
            Assert.AreEqual(4, HeatRules.Stars(70));
            Assert.AreEqual(5, HeatRules.Stars(100));
            Assert.AreEqual(HeatLevel.Hot, HeatRules.LevelOf(35));
            Assert.AreEqual(HeatLevel.Cool, HeatRules.ParseLevel("bogus"));
        }

        [Test]
        public void RealtimeMessagesParse()
        {
            var start = HeatSignal.From("police_pursuit", new Dictionary<string, object>
            {
                ["pursuitId"] = "p-1", ["targetUserId"] = "u-1", ["units"] = 9.0, ["heat"] = 90.0, ["startedAt"] = 1792940000000.0,
                ["expiresAt"] = 1792940180000.0, ["escapeRadiusM"] = 200.0, ["escapeSeconds"] = 60.0, ["at"] = 1792940000000.0,
            });
            Assert.AreEqual("p-1", start.PursuitId);
            Assert.AreEqual(4, start.Pursuit.Units, "units capped at 4");
            Assert.AreEqual("u-1", start.Pursuit.TargetUserId);
            var end = HeatSignal.From("police_pursuit_end", new Dictionary<string, object> { ["pursuitId"] = "p-1", ["outcome"] = "caught", ["heat"] = 36.0 });
            Assert.AreEqual("caught", end.Outcome);
            var jailed = HeatSignal.From("jailed", new Dictionary<string, object> { ["venueId"] = "ct_jail", ["jailId"] = "j", ["until"] = 1792940400000.0, ["bailCents"] = 4500.0, ["heat"] = 90.0 });
            Assert.AreEqual(4500, jailed.Jail.BailCents);
            Assert.AreEqual(1792940400000L, jailed.Jail.UntilMs);
            Assert.AreEqual("bail", HeatSignal.From("released", new Dictionary<string, object> { ["reason"] = "bail" }).Reason);
            var stolen = HeatSignal.From("vehicle_stolen", new Dictionary<string, object> { ["vehicleId"] = "v1", ["kind"] = "sports", ["by"] = "u2" });
            Assert.AreEqual("sports", stolen.Kind);
            Assert.IsNull(HeatSignal.From("police_pursuit", new Dictionary<string, object> { ["pursuitId"] = "../x" }));
            Assert.IsNull(HeatSignal.From("event_live", new Dictionary<string, object>()));
        }

        [Test]
        public void TrafficIdsAndJailedErrors()
        {
            var id = HeatRules.TrafficId(new System.Random(1));
            StringAssert.StartsWith("traffic_", id);
            Assert.AreEqual(20, id.Length);
            Assert.IsTrue(HeatRules.SafeId(id));
            var j = HeatRules.FromJailedError(JToken.Parse(@"{""until"":""2026-10-25T15:09:00Z"",""bailCents"":3500}"));
            Assert.AreEqual(3500, j.BailCents);
            Assert.AreEqual("4:05", HeatRules.Clock(245_000));
        }
    }
}
