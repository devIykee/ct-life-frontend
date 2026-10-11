using System;
using System.Text;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Tests
{
    public class JwtTests
    {
        internal static string Token(JObject payload)
        {
            string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return B64("{\"alg\":\"RS256\",\"typ\":\"JWT\"}") + "." + B64(payload.ToString()) + ".sig";
        }

        [Test]
        public void ExpiryIsReadFromTheExpClaim()
        {
            var exp = new DateTime(2026, 10, 7, 22, 15, 0, DateTimeKind.Utc);
            var token = Token(new JObject { ["exp"] = new DateTimeOffset(exp).ToUnixTimeSeconds(), ["sub"] = "u1" });
            Assert.AreEqual(exp, Jwt.ExpiryUtc(token));
            Assert.AreEqual("u1", Jwt.Subject(token));
        }

        [Test]
        public void RefreshHappensBeforeExpiryByTheMargin()
        {
            var now = new DateTime(2026, 10, 7, 22, 0, 0, DateTimeKind.Utc);
            var token = Token(new JObject { ["exp"] = new DateTimeOffset(now.AddMinutes(15)).ToUnixTimeSeconds() });
            Assert.AreEqual(now.AddMinutes(15).AddSeconds(-90), Jwt.RefreshAtUtc(token, now, TimeSpan.FromSeconds(90)));
        }

        [Test]
        public void ShortLivedTokenRefreshesAtHalfItsRemainingLife()
        {
            var now = new DateTime(2026, 10, 7, 22, 0, 0, DateTimeKind.Utc);
            var token = Token(new JObject { ["exp"] = new DateTimeOffset(now.AddSeconds(60)).ToUnixTimeSeconds() });
            Assert.AreEqual(now.AddSeconds(30), Jwt.RefreshAtUtc(token, now, TimeSpan.FromSeconds(90)));
        }

        [Test]
        public void UnreadableTokenFallsBackToFourteenMinutes()
        {
            var now = DateTime.UtcNow;
            Assert.IsNull(Jwt.ExpiryUtc("not-a-jwt"));
            Assert.AreEqual(now.AddMinutes(14), Jwt.RefreshAtUtc("not-a-jwt", now, TimeSpan.FromSeconds(90)));
        }
    }

    public class AuthResultTests
    {
        private static string Access(string sub = "11111111-1111-1111-1111-111111111111", string wal = "WaLLet1", bool skr = true) =>
            JwtTests.Token(new JObject { ["sub"] = sub, ["wal"] = wal, ["skr"] = skr, ["exp"] = 4102444800 });

        [Test]
        public void ContractShapeWithUserObject()
        {
            var body = new JObject
            {
                ["accessToken"] = Access(), ["refreshToken"] = "r",
                ["user"] = new JObject { ["id"] = "u-contract", ["wallet"] = "W", ["isSeeker"] = false, ["hasCharacter"] = true },
            };
            var r = AuthResult.Parse(body);
            Assert.AreEqual("u-contract", r.UserId);
            Assert.AreEqual("W", r.Wallet);
            Assert.IsFalse(r.IsSeeker);
            Assert.AreEqual(true, r.HasCharacter);
        }

        [Test]
        public void FlatShapeFromBackendMain()
        {
            var body = new JObject
            {
                ["id"] = "u-flat", ["wallet"] = "W2", ["isSeeker"] = true, ["hasCharacter"] = false,
                ["accessToken"] = Access(), ["refreshToken"] = "r",
            };
            var r = AuthResult.Parse(body);
            Assert.AreEqual("u-flat", r.UserId);
            Assert.AreEqual("W2", r.Wallet);
            Assert.IsTrue(r.IsSeeker);
            Assert.AreEqual(false, r.HasCharacter);
        }

        [Test]
        public void RefreshWithoutUserFallsBackToTokenClaims()
        {
            var body = new JObject { ["accessToken"] = Access("u-claims", "WalClaim", false), ["refreshToken"] = "r2" };
            var r = AuthResult.Parse(body, knownUserId: "ignored", knownWallet: "ignored");
            Assert.AreEqual("u-claims", r.UserId);
            Assert.AreEqual("WalClaim", r.Wallet);
            Assert.IsFalse(r.IsSeeker);
            Assert.IsNull(r.HasCharacter);
        }

        [Test]
        public void MissingAccessTokenIsADecodeFailure()
        {
            var e = Assert.Throws<ApiException>(() => AuthResult.Parse(new JObject { ["refreshToken"] = "r" }));
            Assert.AreEqual("DECODE_FAILED", e.Code);
        }
    }
}

namespace CTLife.Tests
{
    public class Base58Tests
    {
        [Test]
        public void KnownSolanaAddressRoundTrips()
        {
            const string addr = "GT22s89nU4iWFkNXj1Bw6uYhJJWDRPpShHt4Bk8f99Te"; // SGT group used by the backend
            var bytes = CTLife.Core.Base58.Decode(addr);
            NUnit.Framework.Assert.AreEqual(32, bytes.Length);
            NUnit.Framework.Assert.AreEqual(addr, CTLife.Core.Base58.Encode(bytes));
        }

        [Test]
        public void RandomFortyFourCharactersAreUsuallyNotAKey()
        {
            NUnit.Framework.Assert.IsFalse(CTLife.Core.Base58.IsPublicKeyLength("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"));
            NUnit.Framework.Assert.IsTrue(CTLife.Core.Base58.IsPublicKeyLength(CTLife.Core.Base58.Encode(new byte[32])));
        }
    }
}
