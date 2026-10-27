using CTLife.Core;
using System.Collections.Generic;

namespace CTLife.UI
{
    public static class HomeStrings
    {
        private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
        {
            ["unavailable"] = "Home actions aren't ready. Close and reopen Home to check again.",
            ["enterHome"] = "Go home",
            ["returnCity"] = "Return to city",
            ["explore"] = "Take a look around. Home actions stay on your phone.",
            ["roomUnavailable"] = "Your home isn't available to explore yet. You can still use Home actions.",
            ["entryFailed"] = "Couldn't open your home. Try again from Home.",
            ["working"] = "Taking a moment…",
            ["unconfirmed"] = "That action wasn't confirmed. Your needs may have changed.",
        };
        public static string T(string key) => English.TryGetValue(key, out var value) ? NameTable.Apply(value) : key;
    }
}
