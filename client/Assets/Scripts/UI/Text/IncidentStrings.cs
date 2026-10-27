using CTLife.Core;
using System.Collections.Generic;
using System.Globalization;

namespace CTLife.UI
{
    /// <summary>Localizable copy for city incidents; kept separate from the shared copy during parallel work.</summary>
    public static class IncidentStrings
    {
        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>
        {
            ["wire"] = "CITY WIRE",
            ["recent"] = "Recent city moments",
            ["history"] = "The receipts · {0}",
            ["dismiss"] = "Dismiss this moment",
            ["close"] = "Close receipts",
            ["now"] = "Just in",
            ["earlier"] = "Earlier this session",
            ["minute"] = "{0}m ago",
            ["fictional"] = "In-game event · Game Cash",
            ["market"] = "Market mood",
            ["ore"] = "ORE city report",
            ["liquidation.title"] = "Conviction met liquidation.",
            ["liquidation.body"] = "A position in the city was liquidated.",
            ["liquidation.actor"] = "{0} was liquidated on {1}.",
            ["liquidation.loss"] = "{0} Game Cash lost",
            ["rug.title"] = "The floor was a rug.",
            ["rug.body"] = "A fictional coin in CT City was rugged.",
            ["rug.coin"] = "{0} was rugged.",
            ["rug.peak"] = "Peak market cap · {0} Game Cash",
            ["launch.title"] = "Fresh ticker. New lore.",
            ["launch.body"] = "A new fictional coin launched in CT City.",
            ["launch.coin"] = "{0} has entered the timeline.",
            ["mood.title"] = "The timeline changed its mood.",
            ["mood.up"] = "The market mood turned green.",
            ["mood.down"] = "The market mood turned red.",
            ["mood.body"] = "The city received a market mood update.",
            ["mood.value"] = "Market mood · {0}",
            ["exploit.title"] = "The audit got interesting.",
            ["exploit.body"] = "An exploit was reported in CT City's fictional economy.",
            ["exploit.target"] = "An in-game exploit was reported at {0}.",
            ["ore.title"] = "Motherlode moment.",
            ["ore.body"] = "The city received an ORE motherlode report.",
        };

        public static string T(string key) => Copy.TryGetValue(key, out var text) ? NameTable.Apply(text) : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    }
}
