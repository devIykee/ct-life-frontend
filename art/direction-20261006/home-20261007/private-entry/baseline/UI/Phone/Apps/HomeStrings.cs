using System.Collections.Generic;

namespace CTLife.UI
{
    internal static class HomeStrings
    {
        private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
        {
            ["unavailable"] = "Home actions aren't ready. Close and reopen Home to check again.",
            ["working"] = "Taking a moment…",
            ["unconfirmed"] = "That action wasn't confirmed. Your needs may have changed.",
        };
        public static string T(string key) => English.TryGetValue(key, out var value) ? value : key;
    }
}
