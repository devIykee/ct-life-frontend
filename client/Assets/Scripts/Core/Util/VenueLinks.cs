using System;

namespace CTLife.Core
{
    /// <summary>
    /// Server deep links to a place in the city, `venue:&lt;venueId&gt;` (backend #14: a Sleuth exposé's
    /// `meta.deepLink = "venue:launchpad_tower"`). The app sets <see cref="Travel"/> to the game's own travel (the map's Go);
    /// the Timeline and the Factory call <see cref="Open"/>. Ids are lower-case venue ids from contract/ids/venues.json.
    /// </summary>
    public static class VenueLinks
    {
        public const string Prefix = "venue:", AppPrefix = "ctlife://venue/", CoinPrefix = "ctlife://coin/";

        /// <summary>Opens a coin's page in Pulse (`ctlife://coin/&lt;coinId&gt;`). Null until the phone exists.</summary>
        public static Action<string> OpenCoin;

        /// <summary>Travel to a venue id. Null until a game is running.</summary>
        public static Action<string> Travel;

        public static bool TryParse(string link, out string venueId)
        {
            venueId = null;
            if (string.IsNullOrWhiteSpace(link)) return false;
            var s = link.Trim();
            if (s.StartsWith(AppPrefix, StringComparison.OrdinalIgnoreCase)) s = s.Substring(AppPrefix.Length);
            else if (s.StartsWith(Prefix, StringComparison.Ordinal)) s = s.Substring(Prefix.Length);
            else return false;
            int cut = s.IndexOfAny(new[] { '?', '#', '/' });
            if (cut >= 0) s = s.Substring(0, cut);
            if (s.Length == 0 || s.Length > 48) return false;
            foreach (var ch in s) if (!(ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9' || ch == '_')) return false;
            venueId = s;
            return true;
        }

        /// <summary>`ctlife://coin/&lt;id&gt;` → the coin id (letters, digits, '-', '_').</summary>
        public static bool TryParseCoin(string link, out string coinId)
        {
            coinId = null;
            if (string.IsNullOrWhiteSpace(link)) return false;
            var s = link.Trim();
            if (!s.StartsWith(CoinPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            s = s.Substring(CoinPrefix.Length);
            int cut = s.IndexOfAny(new[] { '?', '#', '/' });
            if (cut >= 0) s = s.Substring(0, cut);
            if (s.Length == 0 || s.Length > 64) return false;
            foreach (var ch in s) if (!(char.IsLetterOrDigit(ch) && ch < 128 || ch == '-' || ch == '_')) return false;
            coinId = s;
            return true;
        }

        public const string ProfilePrefix = "ctlife://profile/";

        /// <summary>`ctlife://profile/&lt;userId&gt;` → the user id (backend #32 profile cards).</summary>
        public static bool TryParseProfile(string link, out string userId)
        {
            userId = null;
            if (string.IsNullOrWhiteSpace(link) || !link.Trim().StartsWith(ProfilePrefix, StringComparison.OrdinalIgnoreCase)) return false;
            var s = link.Trim().Substring(ProfilePrefix.Length);
            int cut = s.IndexOfAny(new[] { '?', '#', '/' });
            if (cut >= 0) s = s.Substring(0, cut);
            if (s.Length == 0 || s.Length > 64) return false;
            foreach (var ch in s) if (!(char.IsLetterOrDigit(ch) && ch < 128 || ch == '-' || ch == '_')) return false;
            userId = s;
            return true;
        }

        /// <summary>Follows a `venue:` link; false when it is not one or no game can travel right now.</summary>
        public static bool Open(string link)
        {
            if (!TryParse(link, out var id) || Travel == null) return false;
            Travel(id);
            return true;
        }
    }
}
