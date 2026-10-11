using System;
using System.Text;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// A parsed auth response. The contract shape is `{accessToken, refreshToken, user:{id, wallet, isSeeker,
    /// hasCharacter}}`; backend main @ 9f3e5ef returns the user fields flat for /auth/verify and /auth/dev and no user
    /// at all for /auth/refresh (backend-dev-feedback item 45). Both shapes are accepted; missing identity falls back
    /// to the access token's own claims (`sub`, `wal`, `skr`), then to the identity the caller already holds.
    /// </summary>
    public sealed class AuthResult
    {
        public string AccessToken, RefreshToken, UserId, Wallet;
        public bool IsSeeker;
        public bool? HasCharacter;

        public static AuthResult Parse(JToken body, string knownUserId = null, string knownWallet = null, bool knownSeeker = false)
        {
            if (!(body is JObject o)) throw new ApiException(0, "DECODE_FAILED", "Auth response is not an object");
            var r = new AuthResult { AccessToken = o.Str("accessToken"), RefreshToken = o.Str("refreshToken") };
            if (string.IsNullOrEmpty(r.AccessToken)) throw new ApiException(0, "DECODE_FAILED", "Auth response has no accessToken");
            var user = o["user"] as JObject ?? o;
            var claims = Claims(r.AccessToken);
            r.UserId = First(user.Str("id"), claims?.Str("sub"), knownUserId);
            r.Wallet = First(user.Str("wallet"), claims?.Str("wal"), knownWallet);
            var seeker = user["isSeeker"] ?? claims?["skr"];
            r.IsSeeker = seeker != null && seeker.Type == JTokenType.Boolean ? seeker.Value<bool>() : knownSeeker;
            var hasCharacter = user["hasCharacter"];
            r.HasCharacter = hasCharacter != null && hasCharacter.Type == JTokenType.Boolean ? hasCharacter.Value<bool>() : (bool?)null;
            return r;
        }

        private static string First(params string[] values)
        {
            foreach (var v in values) if (!string.IsNullOrEmpty(v)) return v;
            return null;
        }

        private static JObject Claims(string jwt)
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            try
            {
                var s = parts[1].Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
                return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(s)));
            }
            catch (Exception) { return null; }
        }
    }
}
