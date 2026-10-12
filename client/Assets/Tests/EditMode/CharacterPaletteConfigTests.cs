using CTLife.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.IO;
using UnityEngine;

namespace CTLife.Tests
{
    public sealed class CharacterPaletteConfigTests
    {
        private byte[] _cache, _etag;
        private static string Cache => Path.Combine(Application.persistentDataPath, "config_cache.json");
        private static string Etag => Path.Combine(Application.persistentDataPath, "config_cache.etag");
        [SetUp] public void PreserveCache()
        {
            _cache = File.Exists(Cache) ? File.ReadAllBytes(Cache) : null;
            _etag = File.Exists(Etag) ? File.ReadAllBytes(Etag) : null;
        }
        [TearDown] public void RestoreCache()
        {
            void Restore(string path, byte[] bytes) { if (bytes != null) File.WriteAllBytes(path, bytes); else if (File.Exists(path)) File.Delete(path); }
            Restore(Cache, _cache); Restore(Etag, _etag);
        }
        private static string[] Look() => new[] { "skin_08", "head_ubc_m", "hair_ubc_m__black", "top_ubc_m__navy", "bottom_ubc_m", "shoes_ubc_m", "acc_none" };
        private static JObject Payload()
        {
            var look = Look();
            var model = new JObject { ["headId"] = look[1] };
            var keys = new[] { "skinTones", null, "hairIds", "topIds", "bottomIds", "shoesIds", "accIds" };
            for (int i = 0; i < 7; i++) if (i != 1) model[keys[i]] = new JArray(look[i]);
            return new JObject { ["version"] = "palette-test", ["characterPalettes"] =
                new JObject { ["version"] = 1, ["models"] = new JArray(model) } };
        }

        [Test]
        public void OnlyFreshNetworkAllowlistAuthorizesExactPaletteAndIdentity()
        {
            var config = new ConfigStore();
            config.Apply(Payload().ToString(), null, persist: false);
            Assert.IsFalse(config.IsLookConfirmed(Look()), "A cached payload must not grant fresh character IDs.");
            config.Apply(Payload().ToString(), null);
            Assert.IsTrue(config.IsLookConfirmed(Look()));
            for (int i = 0; i < 7; i++)
            {
                var forged = Look(); forged[i] += "_foreign";
                Assert.IsFalse(config.IsLookConfirmed(forged), "Slot " + i + " escaped its model allowlist.");
            }
            config.Apply(new JObject { ["version"] = "withdrawn" }.ToString(), null);
            Assert.IsFalse(config.IsLookConfirmed(Look()), "Removed capability must not linger.");
        }

        [TestCase(2)]
        [TestCase(0)]
        public void UnknownCapabilityVersionsDoNotAuthorizePaletteIds(int version)
        {
            var payload = Payload(); payload["characterPalettes"]["version"] = version;
            var config = new ConfigStore(); config.Apply(payload.ToString(), null);
            Assert.IsFalse(config.IsLookConfirmed(Look()));
        }

        [Test]
        public void PartialAndMalformedAllowlistsAreRejected()
        {
            var payload = Payload();
            payload["characterPalettes"]["models"][0]["shoesIds"] = new JArray();
            var config = new ConfigStore(); config.Apply(payload.ToString(), null);
            Assert.IsFalse(config.IsLookConfirmed(Look()));
            payload["characterPalettes"]["models"][0]["shoesIds"] = new JArray(123);
            config.Apply(payload.ToString(), null);
            Assert.IsFalse(config.IsLookConfirmed(Look()));
        }
    }
}
