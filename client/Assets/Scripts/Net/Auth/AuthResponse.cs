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

    /// <summary>SIWS verify body encodings. The contract says base64 for both byte fields.</summary>
    public static class SiwsEncoding
    {
        /// <summary>
        /// The signed SIWS message as UTF-8 text, for servers that read `signedMessage` as text
        /// (backend main @ 9f3e5ef does `Buffer.from(signedMessage, 'utf8')`, feedback item 46). Null if not text.
        /// </summary>
        public static string Base64ToUtf8Text(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                var bytes = Convert.FromBase64String(base64);
                var text = new UTF8Encoding(false, true).GetString(bytes);
                return text.Length > 0 ? text : null;
            }
            catch (Exception) { return null; }
        }
    }
}

namespace CTLife.Net
{
    /// <summary>
    /// Makes a server SIWS challenge acceptable to MWA wallets. The SIWS/EIP-4361 rules wallets enforce: `nonce` is at
    /// least 8 alphanumeric characters and `uri` is an absolute RFC 3986 URI. Backend main issues a hyphenated UUID nonce
    /// and `uri: "ct-life"`; the Seeker wallet throws "nonce must be at least 8 alphanumeric characters" and never
    /// answers (feedback items 43, 59). Only invalid fields are replaced; the verify body still carries the server's
    /// own nonce, which is what the server looks up.
    /// </summary>
    public static class SiwsWalletCompat
    {
        public static SiwsRequest ForWallet(SiwsRequest server, string identityUri, out string changed)
        {
            var r = new SiwsRequest
            {
                domain = server.domain, statement = server.statement, uri = server.uri, version = server.version,
                chainId = server.chainId, nonce = server.nonce, issuedAt = server.issuedAt, expirationTime = server.expirationTime,
            };
            var notes = new System.Collections.Generic.List<string>();
            if (!IsAlphanumericNonce(r.nonce))
            {
                var stripped = Alphanumeric(r.nonce);
                if (stripped.Length >= 8) { r.nonce = stripped; notes.Add("nonce"); }
            }
            if (!System.Uri.TryCreate(r.uri ?? "", System.UriKind.Absolute, out _) && System.Uri.TryCreate(identityUri ?? "", System.UriKind.Absolute, out var id))
            {
                r.uri = id.ToString();
                notes.Add("uri");
            }
            changed = notes.Count == 0 ? null : string.Join(",", notes);
            return r;
        }

        public static bool IsAlphanumericNonce(string nonce)
        {
            if (string.IsNullOrEmpty(nonce) || nonce.Length < 8) return false;
            foreach (var c in nonce) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')) return false;
            return true;
        }

        private static string Alphanumeric(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s ?? "") if (c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z') sb.Append(c);
            return sb.ToString();
        }
    }
}
