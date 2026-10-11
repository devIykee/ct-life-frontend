using System;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Tests
{
    public class NeedsCodecTests
    {
        private static JObject Payload() => new JObject
        {
            ["energy"] = 49.875, ["hunger"] = 0.25, ["fun"] = 100,
            ["hygiene"] = 83.999, ["sanity"] = 0
        };

        [Test]
        public void LiveFractionalDecayDecodesWithoutOverstatingAvailableNeeds()
        {
            var raw = Payload();
            var needs = NeedsCodec.Decode(raw);
            Assert.AreEqual(49, needs.Energy);
            Assert.AreEqual(0, needs.Hunger);
            Assert.AreEqual(100, needs.Fun);
            Assert.AreEqual(83, needs.Hygiene);
            Assert.AreEqual(0, needs.Sanity);
            Assert.AreEqual(49.875, (double)raw["energy"], "Do not mutate server evidence");
        }

        [TestCase(-0.1)]
        [TestCase(100.1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidNeedCannotBecomeAValidLookingPercentage(double invalid)
        {
            var raw = Payload(); raw["energy"] = invalid;
            Assert.Throws<FormatException>(() => NeedsCodec.Decode(raw));
        }

        [Test]
        public void MissingAndTextNeedsAreNotInvented()
        {
            var raw = Payload(); raw.Remove("hunger");
            Assert.Throws<FormatException>(() => NeedsCodec.Decode(raw));
            raw["hunger"] = "50";
            Assert.Throws<FormatException>(() => NeedsCodec.Decode(raw));
            Assert.Throws<FormatException>(() => NeedsCodec.Decode(null));
        }
    }
}
