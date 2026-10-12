using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using CTLife.World.Likeness;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Tests
{
    public sealed class LikenessInputTests
    {
        [Test] public async Task GetMeHandlesJsonNullCharacterAndClearsPreviousLikeness()
        {
            var rest = new ProfileRest();
            var api = new Api(rest, null);
            var profile = new PlayerProfile(api);
            Assert.That((await profile.Refresh()).Character, Is.Null);
            Assert.That(api.LastLikenessJobId, Is.Null);
            Assert.That(profile.LikenessJobId, Is.Null);

            string jobId = Guid.NewGuid().ToString();
            rest.Body["character"] = JObject.Parse(@"{""alias"":""anon"",""tribeId"":""trencher"",""traitIds"":[""fast_talker"",""iron_will""],""dreamId"":""make_10k"",""homeId"":""parents_basement"",""look"":{""skinTone"":""skin_tone3"",""headId"":""head_snow"",""hairId"":""hair_snow_bun"",""topId"":""top_vneck_charcoal"",""bottomId"":""bottom_denim_light"",""shoesId"":""shoes_sneakers_blue"",""accId"":""acc_none""}}");
            rest.Body["character"]["likenessJobId"] = jobId;
            Assert.That((await profile.Refresh()).Character, Is.Not.Null);
            Assert.That(api.LastLikenessJobId, Is.EqualTo(jobId));
            Assert.That(profile.LikenessJobId, Is.EqualTo(jobId), "The authoritative equipped job must reach the world-facing profile.");
            var savedCharacter = profile.Me.Character;
            bool Confirms(string expectedJob, string actualJob) => Onboarding.CharacterCreationMatches(profile.Me,
                savedCharacter.Alias, profile.LookArray(), savedCharacter.TraitIds, savedCharacter.DreamId,
                savedCharacter.TribeId, expectedJob, actualJob);
            Assert.IsTrue(Confirms(jobId, profile.LikenessJobId));
            Assert.IsFalse(Confirms(jobId, null), "A saved stock character cannot confirm a personal-character request.");
            Assert.IsFalse(Confirms(jobId, Guid.NewGuid().ToString()), "Another accepted job is still a different character.");
            Assert.IsFalse(Confirms(null, jobId), "An existing personal character is not a receipt for a stock-character request.");

            rest.Body["character"] = JValue.CreateNull();
            Assert.That((await profile.Refresh()).Character, Is.Null);
            Assert.That(api.LastLikenessJobId, Is.Null, "A fresh account must not retain the previous profile's generated character.");
            Assert.That(profile.LikenessJobId, Is.Null);
        }
        [Test] public void GlbRejectsExternalUrlsBeforeImport()
        {
            var bytes = Glb(new JObject { ["asset"] = new JObject { ["version"] = "2.0" }, ["buffers"] = new JArray(new JObject { ["uri"] = "https://example.test/leak", ["byteLength"] = 0 }) });
            Assert.Throws<InvalidDataException>(() => LikenessAvatarLoader.ValidateGlb(bytes));
        }
        [Test] public void GlbRejectsDeclaredOversizedImageBeforeDecode()
        {
            byte[] png = new byte[24]; png[0] = 137; png[1] = 80; png[2] = 78; png[3] = 71; png[16] = 0; png[17] = 0; png[18] = 16; png[19] = 0; png[20] = 0; png[21] = 0; png[22] = 16; png[23] = 0;
            var json = new JObject { ["asset"] = new JObject { ["version"] = "2.0" }, ["bufferViews"] = new JArray(new JObject { ["byteOffset"] = 0, ["byteLength"] = 24 }), ["images"] = new JArray(new JObject { ["bufferView"] = 0 }) };
            Assert.Throws<InvalidDataException>(() => LikenessAvatarLoader.ValidateGlb(Glb(json, png)));
        }
        [Test] public void PhotoHeaderRejectsMalformedJpegAndOverflow()
        {
            Assert.Throws<InvalidDataException>(() => LikenessImageHeader.Size(new byte[24], 0, 24, out _, out _));
            Assert.Throws<InvalidDataException>(() => LikenessImageHeader.Size(new byte[24], int.MaxValue, 100, out _, out _));
        }
        [Test] public void CharacterRequestKeepsLikenessSeparateAndBindsIdempotency()
        {
            string id = Guid.NewGuid().ToString(); var look = new[] { "skin", "head_snow", "hair", "top", "bottom", "shoes", "none" };
            var body = JObject.Parse(OnboardingRequest.CharacterBody("anon", look, new[] { "one", "two" }, "dream", "tribe", true, id));
            Assert.That(body.Value<string>("likenessJobId"), Is.EqualTo(id));
            Assert.That(body["look"].Value<string>("headId"), Is.EqualTo("head_snow"));
            var normal = JObject.Parse(OnboardingRequest.CharacterBody("anon", look, new[] { "one", "two" }, "dream", "tribe", true));
            Assert.That(normal["likenessJobId"], Is.Null);
            Assert.Throws<ArgumentException>(() => OnboardingRequest.CharacterBody("anon", look, Array.Empty<string>(), "dream", "tribe", true, "../other-user"));
            var journal = new OnboardingTransactionStore("likeness-test-" + Guid.NewGuid());
            try { Assert.That(journal.CharacterKey("anon", look, new[] { "one", "two" }, "dream", "tribe", id), Is.Not.EqualTo(journal.CharacterKey("anon", look, new[] { "one", "two" }, "dream", "tribe"))); }
            finally { journal.Clear(); }
        }
        [Test] public void CheckoutBindsNetworkAmountCharacterAndExpiryBeforeWallet()
        {
            var quote = new JObject { ["intentId"] = Guid.NewGuid().ToString(), ["modelId"] = "head_snow", ["currency"] = "USDC", ["amount"] = "3", ["amountBaseUnits"] = "3000000", ["cluster"] = "devnet", ["mint"] = "So11111111111111111111111111111111111111112", ["recipient"] = "So11111111111111111111111111111111111111112", ["reference"] = "So11111111111111111111111111111111111111112", ["transactionBase64"] = Convert.ToBase64String(new byte[200]), ["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O") };
            Assert.That(LikenessCheckout.Parse(quote, "head_snow", CTLife.Core.SolanaCluster.Devnet, DateTimeOffset.UtcNow).Amount, Is.EqualTo("3"));
            Assert.Throws<InvalidDataException>(() => LikenessCheckout.Parse(quote, "head_snow", CTLife.Core.SolanaCluster.Mainnet, DateTimeOffset.UtcNow));
            Assert.Throws<InvalidDataException>(() => LikenessCheckout.Parse(quote, "head_rain", CTLife.Core.SolanaCluster.Devnet, DateTimeOffset.UtcNow));
            quote["amountBaseUnits"] = "4000000";
            Assert.Throws<InvalidDataException>(() => LikenessCheckout.Parse(quote, "head_snow", CTLife.Core.SolanaCluster.Devnet, DateTimeOffset.UtcNow));
            quote["amountBaseUnits"] = "3000000"; quote["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
            Assert.Throws<InvalidDataException>(() => LikenessCheckout.Parse(quote, "head_snow", CTLife.Core.SolanaCluster.Devnet, DateTimeOffset.UtcNow));
        }
        private static byte[] Glb(JObject json, byte[] bin = null)
        {
            var payload = Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None));
            int jsonLength = (payload.Length + 3) / 4 * 4; bin ??= Array.Empty<byte>();
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(0x46546c67u); writer.Write(2u); writer.Write(28 + jsonLength + bin.Length); writer.Write(jsonLength); writer.Write(0x4e4f534au);
            writer.Write(payload); for (int i = payload.Length; i < jsonLength; i++) writer.Write((byte)32);
            writer.Write(bin.Length); writer.Write(0x004e4942u); writer.Write(bin); return stream.ToArray();
        }
        private sealed class ProfileRest : IRestClient
        {
            public readonly JObject Body = JObject.Parse(@"{""user"":{""id"":""01a11860-9c1f-77a4-a496-17038bb21f01"",""wallet"":""W"",""isSeeker"":false},""character"":null,""needs"":{""energy"":50,""hunger"":50,""fun"":50,""hygiene"":50,""sanity"":50},""skills"":{""clout"":1,""coding"":1,""trading"":1,""writing"":1,""design"":1,""security"":1},""cashCents"":0,""debtCents"":0,""homeId"":""parents_basement"",""minerTier"":""none"",""flags"":{}}");
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            { Assert.That(path, Is.EqualTo("/me")); return Task.FromResult((T)(object)Body.DeepClone()); }
            public Task<T> Post<T>(string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Put<T>(string p, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Patch<T>(string p, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Delete<T>(string p, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Send<T>(string method, string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<RawResponse> SendRaw(string method, string p, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
