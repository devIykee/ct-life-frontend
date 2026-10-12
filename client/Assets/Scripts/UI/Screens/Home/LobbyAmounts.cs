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
    }
}
