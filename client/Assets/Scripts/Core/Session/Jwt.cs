using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CTLife.Core
{
    /// <summary>
    /// Reads claims from a JWT payload without verifying it (the server verifies). Used only to schedule refreshes:
    /// the access token's real `exp` replaces the old "now + 14 min" assumption (QA code-architecture §2.2).
    /// </summary>
    public static class Jwt
    {
        /// <summary>The `exp` claim as UTC, or null when the token is not a readable JWT.</summary>
        public static DateTime? ExpiryUtc(string token)
        {
            var payload = Payload(token);
            var exp = payload?["exp"];
            if (exp == null || (exp.Type != JTokenType.Integer && exp.Type != JTokenType.Float)) return null;
            try { return DateTimeOffset.FromUnixTimeSeconds((long)exp.Value<double>()).UtcDateTime; }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        public static string Subject(string token) => Payload(token)?["sub"]?.ToString();

        /// <summary>When to treat the token as stale: `exp` minus a safety margin, else issue time + 14 minutes.</summary>
        public static DateTime RefreshAtUtc(string token, DateTime nowUtc, TimeSpan margin)
        {
            var exp = ExpiryUtc(token);
            if (exp == null) return nowUtc.AddMinutes(14);
            var at = exp.Value - margin;
            // A token whose lifetime is shorter than the margin is refreshed at half its remaining life.
            if (at <= nowUtc) at = nowUtc + TimeSpan.FromTicks(Math.Max(0, (exp.Value - nowUtc).Ticks / 2));
            return at;
        }

        private static JObject Payload(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var parts = token.Split('.');
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
