using System;
using CTLife.Economy;

namespace CTLife.Ventures
{
    /// <summary>Real-money amounts: base-unit digit strings from the server shown with the currency's decimals from `economy.json`.</summary>
    public static class AdMoney
    {
        /// <param name="decimals">Decimals for a currency code, or a negative number when unknown (then the raw units are shown and flagged).</param>
        public static string Format(string units, string currency, Func<string, int> decimals)
        {
            if (string.IsNullOrEmpty(units)) return "—";
            var code = string.IsNullOrEmpty(currency) ? "USDC" : currency;
            int d = decimals != null ? decimals(code) : -1;
            if (d < 0 || d > 18) return units + " (raw) " + code;
            return MoneyFormat.Token(units, d, code, 6);
        }

        /// <summary>Same, for a term price that is always USDC.</summary>
        public static string Usdc(string units) => Format(units, "USDC", c => TokenUnits.UsdcDecimals);
    }
}
