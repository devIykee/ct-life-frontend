using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    /// <summary>
    /// Leverage cap from config `skills` (contract section 7): `leverageByTradingLevel[{level, leverage}]` plus
    /// `tribeBonusLeverage{tribeId: n}`, clamped to the contract's request maximum (20). UI pre-validation only: the server
    /// enforces the same rule and answers LEVERAGE_NOT_ALLOWED.
    /// </summary>
    public static class LeverageCap
    {
        /// <returns>The cap, or null when the config or the player's level is unknown (the UI then defers to the server).</returns>
        public static int? Compute(JToken skillsConfig, int? tradingLevel, string tribeId)
        {
            if (!(skillsConfig?["leverageByTradingLevel"] is JArray table) || !tradingLevel.HasValue) return null;
            long? baseCap = null;
            foreach (var row in table)
            {
                if (Wire.LongOr(row["level"], -1) == tradingLevel.Value)
                {
                    baseCap = Wire.LongOr(row["leverage"], -1);
                    break;
                }
            }
            if (!baseCap.HasValue || baseCap.Value < 1) return null;
            long bonus = 0;
            if (!string.IsNullOrEmpty(tribeId) && skillsConfig["tribeBonusLeverage"] is JObject bonuses)
                bonus = Math.Max(0, Wire.LongOr(bonuses[tribeId], 0));
            return (int)Math.Min(Tickets.ContractMaxLeverage, baseCap.Value + bonus);
        }
    }

    /// <summary>
    /// PnL card for an open spot position. Every money figure is the server's (`unrealizedCents`, `costBasisCents`); the
    /// return percentage is a display ratio of those two server values, not a computed outcome.
    /// </summary>
    public sealed class PnlCardData
    {
        public string Ticker, UnitsText;
        public long UnrealizedCents, CostBasisCents;
        public long? ReturnBps;
        public bool Up => UnrealizedCents > 0;
        public bool Down => UnrealizedCents < 0;

        public static PnlCardData From(PositionRow p, string ticker)
        {
            if (p == null) return null;
            long? ret = null;
            if (p.CostBasisCents > 0)
                ret = (long)(new BigInteger(p.UnrealizedCents) * 10000 / p.CostBasisCents);
            return new PnlCardData
            {
                Ticker = string.IsNullOrEmpty(ticker) ? (string.IsNullOrEmpty(p.Ticker) ? "?" : p.Ticker) : ticker,
                UnitsText = MoneyFormat.CoinUnits(p.Units),
                UnrealizedCents = p.UnrealizedCents,
                CostBasisCents = p.CostBasisCents,
                ReturnBps = ret,
            };
        }

        /// <summary>Plain-text summary for the clipboard ("share" until a share-sheet API exists).</summary>
        public string ShareText() =>
            "$" + Ticker + " " + MoneyFormat.SignedCash(UnrealizedCents) + (ReturnBps.HasValue ? " (" + MoneyFormat.Bps(ReturnBps.Value) + ")" : "") +
            " on " + MoneyFormat.Cash(CostBasisCents) + " in. Fictional Cash, CT Life.";
    }

    /// <summary>REKT card content from the city `liquidated {perpId, coinId, lossCents, at}` message.</summary>
    public sealed class RektCardModel
    {
        public string PerpId, CoinId, Ticker, LossText;

        public static RektCardModel From(string perpId, string coinId, long lossCents, Func<string, string> tickerOf)
        {
            string ticker = null;
            try { ticker = tickerOf?.Invoke(coinId); }
            catch (Exception) { ticker = null; }
            return new RektCardModel
            {
                PerpId = perpId,
                CoinId = coinId,
                Ticker = string.IsNullOrEmpty(ticker) ? null : ticker,
                // The server's loss; shown as a negative amount whatever sign the payload uses.
                LossText = "-" + MoneyFormat.Cash(lossCents == long.MinValue ? long.MaxValue : Math.Abs(lossCents)),
            };
        }
    }

    /// <summary>ORE Refinery display helpers (contract: ORE 11 decimals, lamports 9).</summary>
    public static class OreView
    {
        public static string Ore(string baseUnits) => MoneyFormat.Token(baseUnits, TokenUnits.OreDecimals, "ORE", 4);
        public static string Sol(string lamports) => TokenUnits.Format(lamports, TokenUnits.SolDecimals, 3) ?? "?";

        /// <summary>Seconds left in the round from the server's `endsAt` and a server-corrected clock. Null when unknown.</summary>
        public static double? SecondsLeft(long? endsAtMs, double nowMs) =>
            endsAtMs.HasValue ? Math.Max(0, (endsAtMs.Value - nowMs) / 1000.0) : (double?)null;

        /// <summary>Index of the square with the most deployed SOL (display highlight only); -1 when empty or all zero.</summary>
        public static int Busiest(IReadOnlyList<string> squares)
        {
            int best = -1;
            BigInteger bestValue = BigInteger.Zero;
            for (int i = 0; i < (squares?.Count ?? 0); i++)
            {
                if (!TokenUnits.IsBaseUnits(squares[i])) continue;
                var v = BigInteger.Parse(squares[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                if (v > bestValue) { bestValue = v; best = i; }
            }
            return best;
        }

        /// <summary>Label for a miner tier id from config `ore.minerTiers` (falls back to a readable id).</summary>
        public static string TierLabel(JToken oreConfig, string tierId)
        {
            if (oreConfig?["minerTiers"] is JArray tiers)
                foreach (var t in tiers)
                    if (Wire.Str(t["id"]) == tierId)
                        return Wire.Plain(t["label"], 24) is var l && l.Length > 0 ? l : tierId;
            return string.IsNullOrEmpty(tierId) ? "None" : char.ToUpperInvariant(tierId[0]) + tierId.Substring(1);
        }

        /// <summary>Whether the tier wears the hard hat (config `ore.minerTiers[].hardhat`).</summary>
        public static bool HardHat(JToken oreConfig, string tierId)
        {
            if (oreConfig?["minerTiers"] is JArray tiers)
                foreach (var t in tiers)
                    if (Wire.Str(t["id"]) == tierId)
                        return t["hardhat"]?.Type == JTokenType.Boolean && (bool)t["hardhat"];
            return false;
        }
    }

    /// <summary>
    /// Poll scheduling with exponential backoff on failure. Time is passed in (seconds), so it is deterministic in tests.
    /// </summary>
    public sealed class PollGate
    {
        public readonly double Interval, MaxBackoff;
        public int Failures { get; private set; }
        public bool InFlight { get; private set; }
        public double NextAt { get; private set; }
        public double LastSuccessAt { get; private set; } = double.NaN;

        public PollGate(double intervalSeconds, double maxBackoffSeconds = 30)
        {
            Interval = Math.Max(0.1, intervalSeconds);
            MaxBackoff = Math.Max(Interval, maxBackoffSeconds);
        }

        public bool Due(double now) => !InFlight && now >= NextAt;

        public void Start() => InFlight = true;

        public void Succeeded(double now)
        {
            InFlight = false;
            Failures = 0;
            LastSuccessAt = now;
            NextAt = now + Interval;
        }

        public void Failed(double now)
        {
            InFlight = false;
            Failures++;
            NextAt = now + Math.Min(MaxBackoff, Interval * Math.Pow(2, Math.Min(Failures, 10)));
        }

        /// <summary>Poll on the next check (after a write, a realtime hint, or the view reopening).</summary>
        public void Kick() => NextAt = 0;

        public double Age(double now) => double.IsNaN(LastSuccessAt) ? double.NaN : now - LastSuccessAt;
    }
}
