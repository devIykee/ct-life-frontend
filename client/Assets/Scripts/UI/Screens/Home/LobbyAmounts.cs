using System;
using System.Globalization;

namespace CTLife.UI
{
    /// <summary>Compact display only. Exact balances remain in the wallet or cash explainer.</summary>
    public static class LobbyAmounts
    {
        public static string Compact(decimal amount)
        {
            decimal magnitude = Math.Abs(amount);
            var units = new[] { (1000000000000000m, "Q"), (1000000000000m, "T"), (1000000000m, "B"), (1000000m, "M"), (1000m, "K") };
            if (magnitude < 10000m) return amount.ToString("0.####", CultureInfo.InvariantCulture);
            foreach (var unit in units)
                if (magnitude >= unit.Item1)
                    return (decimal.Truncate(amount / unit.Item1 * 100m) / 100m).ToString("0.##", CultureInfo.InvariantCulture) + unit.Item2;
            return amount.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Lobby Cash: "$303.50" below $10,000, then compact ("$12.34K"); negative keeps its sign.</summary>
        public static string Cash(long cents)
        {
            decimal amount = cents / 100m;
            string sign = amount < 0 ? "-" : "";
            decimal m = Math.Abs(amount);
            return sign + "$" + (m >= 10000m ? Compact(m) : m.ToString("#,0.00", CultureInfo.InvariantCulture));
        }
    }
}
