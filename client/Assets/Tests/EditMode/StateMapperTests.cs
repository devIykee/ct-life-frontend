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
        public void NpcMapsContractFields()
        {
            var npc = new NpcState();
            StateMapper.ApplyNpc(k => k == "archetypeId" ? "town_crier" : k == "startedAt" ? (object)42.0 : null, npc);
            Assert.AreEqual("town_crier", npc.archetypeId);
            Assert.AreEqual(42.0, npc.startedAt);
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
        public void Bigint64FieldsDecodeAndKeepTheStreamAligned()
        {
            // Backend 7243073 declares CoinState's 64-bit fields "bigint64" (@colyseus/schema 5: two little-endian int32,
            // the int64 layout). The vendored SDK's Decode.cs reads them (CT Life patch); without it the decoder returned
            // null and read nothing, so every later field in the patch was misread.
            long big = -9007199254740993L; // below -2^53: a double could not hold it
            var bytes = new List<byte>(System.BitConverter.GetBytes(big)); // test hosts are little-endian
            bytes.AddRange(System.BitConverter.GetBytes(18446744073709551615UL));
            bytes.Add(0x2a); // a positive fixint after both, to prove the iterator advanced 8 + 8 bytes
            var it = new Colyseus.Schema.Iterator { Offset = 0 };
            var arr = bytes.ToArray();
            Assert.AreEqual(big, Colyseus.Schema.Utils.Decode.DecodePrimitiveType("bigint64", arr, it));
            Assert.AreEqual(18446744073709551615UL, Colyseus.Schema.Utils.Decode.DecodePrimitiveType("biguint64", arr, it));
            Assert.AreEqual(16, it.Offset);
            Assert.AreEqual(42f, System.Convert.ToSingle(Colyseus.Schema.Utils.Decode.DecodePrimitiveType("number", arr, it)));

            var fields = new Dictionary<string, object> { ["coinId"] = "c1", ["priceMicros"] = big, ["launchedAt"] = 1791468213069L };
            var c = new CoinState();
            StateMapper.ApplyCoin(k => fields.TryGetValue(k, out var v) ? v : null, c);
            Assert.AreEqual(big, c.priceMicros);
            Assert.AreEqual(1791468213069L, c.launchedAt);
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
