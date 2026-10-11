using System.Collections.Generic;
using Colyseus.Schema;
using CTLife.Net;
using CTLife.Realtime;
using NUnit.Framework;

namespace CTLife.Tests
{
    public class StateMapperTests
    {
        [Test]
        public void ContractShapedPlayerMapsEveryField()
        {
            var fields = new Dictionary<string, object>
            {
                ["userId"] = "u1", ["name"] = "anon", ["skrName"] = "anon.skr", ["tribeId"] = "trencher", ["isSeeker"] = true,
                ["minerTier"] = "miner", ["look"] = new ArraySchema<string>(new List<string> { "s", "h", "hr", "t", "b", "sh", "a" }),
                ["x"] = 1.5f, ["z"] = -2.25, ["rotY"] = 3, ["anim"] = "loco_walk", ["emoteId"] = "emote_wave", ["emoteAt"] = 1234567890123.0,
                ["spotId"] = "cafe_seat_1", ["talking"] = true,
            };
            var p = new PlayerState();
            StateMapper.ApplyPlayer(k => fields.TryGetValue(k, out var v) ? v : null, p);
            Assert.AreEqual("u1", p.userId);
            Assert.AreEqual("anon", p.name);
            Assert.IsTrue(p.isSeeker);
            Assert.AreEqual(7, p.look.Count);
            Assert.AreEqual("hr", p.look[2]);
            Assert.AreEqual(1.5, p.x, 1e-6);
            Assert.AreEqual(-2.25, p.z, 1e-6);
            Assert.AreEqual(3, p.rotY, 1e-6);
            Assert.AreEqual(1234567890123.0, p.emoteAt);
            Assert.IsTrue(p.talking);
        }

        [Test]
        public void BackendMainPlayerWithoutLookKeepsDefaults()
        {
            // server/realtime/schemas.ts @ 9f3e5ef: no `look`, numbers are `number` (float64).
            var fields = new Dictionary<string, object> { ["userId"] = "unverified-eyJhbGci", ["x"] = 0.0, ["z"] = 0.0, ["anim"] = "loco_idle" };
            var p = new PlayerState();
            StateMapper.ApplyPlayer(k => fields.TryGetValue(k, out var v) ? v : null, p);
            Assert.AreEqual(0, p.look.Count);
            Assert.AreEqual("", p.name);
            Assert.AreEqual("none", p.minerTier);
        }

        [Test]
        public void NpcArchetypeAcceptsBothFieldNames()
        {
            var contract = new NpcState();
            StateMapper.ApplyNpc(k => k == "archetypeId" ? "town_crier" : null, contract);
            Assert.AreEqual("town_crier", contract.archetypeId);
            var server = new NpcState();
            StateMapper.ApplyNpc(k => k == "archetype" ? "reply_guy" : k == "startedAt" ? (object)42.0 : null, server);
            Assert.AreEqual("reply_guy", server.archetypeId);
            Assert.AreEqual(42.0, server.startedAt);
        }

        [Test]
        public void LineParamsFromMapSchema()
        {
            var map = new MapSchema<string>();
            map["coin"] = "$PEPE";
            var n = new NpcState();
            StateMapper.ApplyNpc(k => k == "lineParams" ? map : null, n);
            Assert.AreEqual("$PEPE", n.lineParams["coin"]);
        }

        [Test]
        public void CoinsMapLargeIntegers()
        {
            var fields = new Dictionary<string, object> { ["coinId"] = "c1", ["priceMicros"] = 9007199254740.0, ["change5mBps"] = -250.0, ["holders"] = 12 };
            var c = new CoinState();
            StateMapper.ApplyCoin(k => fields.TryGetValue(k, out var v) ? v : null, c);
            Assert.AreEqual(9007199254740L, c.priceMicros);
            Assert.AreEqual(-250, c.change5mBps);
            Assert.AreEqual(12, c.holders);
        }

        [Test]
        public void ConversionsAreDefensive()
        {
            Assert.AreEqual(2.5, StateMapper.Num("2.5", 0));
            Assert.AreEqual(7, StateMapper.Num(null, 7));
            Assert.IsTrue(StateMapper.Bool(1.0, false));
            Assert.IsFalse(StateMapper.Bool("nope", false));
            Assert.IsNull(StateMapper.Strings("not a list"));
            CollectionAssert.AreEqual(new[] { "a", "b" }, StateMapper.Strings(new[] { "a", "b" }));
        }
    }
}
