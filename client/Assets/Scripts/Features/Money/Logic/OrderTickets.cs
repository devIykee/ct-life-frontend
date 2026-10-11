using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    /// <summary>
    /// Binds one Idempotency-Key (UUIDv4, contract section 1) to one exact request. While the outcome of a write is
    /// unknown (network loss, timeout, unreadable reply) the same request reuses the same key, so the server replays the
    /// first response instead of executing twice. A definitive answer (success or rejection) completes the write: the next
    /// order, even an identical one, is a new order with a new key. A different body never reuses a key (the server
    /// would answer VALIDATION_FAILED for a body-hash mismatch).
    /// </summary>
    public sealed class RetainedWrite
    {
        private readonly Func<string> _newKey;
        private string _request, _key;

        public RetainedWrite(Func<string> newKey = null)
        {
            _newKey = newKey ?? (() => Guid.NewGuid().ToString("D"));
        }

        public bool Pending => _key != null;

        public string KeyFor(string path, string body)
        {
            var request = path + "\n" + (body ?? "");
            if (_key == null || _request != request)
            {
                _key = _newKey();
                _request = request;
            }
            return _key;
        }

        /// <summary>Call after a definitive server answer, or when the user abandons an unknown outcome on purpose.</summary>
        public void Complete()
        {
            _key = null;
            _request = null;
        }

        public static string Body(JObject body) => body?.ToString(Formatting.None);
    }

    /// <summary>Exact parsing of user-typed amounts (invariant culture, '.' decimal point, no grouping).</summary>
    public static class AmountInput
    {
        /// <summary>"12.5" → 1250 cents. At most 2 decimals, strictly positive, within JavaScript's safe integer range.</summary>
        public static bool TryCash(string input, out long cents)
        {
            cents = 0;
            if (!TryDecimal(input, 2, out var d) || d <= 0) return false;
            var c = d * 100m;
            if (c > Wire.MaxSafeInteger) return false;
            cents = (long)c;
            return true;
        }

        /// <summary>A price in Cash dollars → micros (millionths of a dollar per whole coin). At most 6 decimals.</summary>
        public static bool TryMicros(string input, out long micros)
        {
            micros = 0;
            if (!TryDecimal(input, 6, out var d) || d <= 0) return false;
            var m = d * 1_000_000m;
            if (m > Wire.MaxSafeInteger) return false;
            micros = (long)m;
            return true;
        }

        /// <summary>"1.5" (%) → 150 bps. Range 0.01 % … 50 %.</summary>
        public static bool TrySlippagePercent(string input, out int bps)
        {
            bps = 0;
            if (!TryDecimal(input, 2, out var d)) return false;
            var b = d * 100m;
            if (b < 1m || b > 5000m) return false;
            bps = (int)b;
            return true;
        }

        private static bool TryDecimal(string input, int maxDecimals, out decimal value)
        {
            value = 0;
            if (input == null) return false;
            var s = input.Trim();
            if (s.Length == 0 || s.Length > 24) return false;
            foreach (var ch in s) if (!(ch >= '0' && ch <= '9') && ch != '.') return false;
            if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return false;
            int dot = s.IndexOf('.');
            return dot < 0 || s.Length - dot - 1 <= maxDecimals;
        }
    }

    /// <summary>A validated request body, or the MoneyStrings key explaining why the input is not sendable.</summary>
    public readonly struct Ticket
    {
        public readonly JObject Body;
        public readonly string ErrorKey;
        public readonly object[] Args;

        private Ticket(JObject body, string errorKey, object[] args)
        {
            Body = body;
            ErrorKey = errorKey;
            Args = args ?? Array.Empty<object>();
        }

        public bool Ok => Body != null;
        public static Ticket Send(JObject body) => new Ticket(body, null, null);
        public static Ticket Reject(string key, params object[] args) => new Ticket(null, key, args);
    }

    /// <summary>
    /// UI pre-validation for the Phase 2 writes. The server stays authoritative (it answers INSUFFICIENT_CASH,
    /// LEVERAGE_NOT_ALLOWED, SLIPPAGE_EXCEEDED...); these checks only stop obviously unsendable input.
    /// </summary>
    public static class Tickets
    {
        public const int DefaultSlippageBps = 200; // contract example for POST /market/trades
        public static readonly int[] SlippagePresetsBps = { 50, 100, 200, 500 };
        public const int ContractMaxLeverage = 20; // openapi postPerps: leverage maximum 20

        /// <summary>POST /market/trades buy: `{coinId, side: buy, cashCents, maxSlippageBps}`.</summary>
        public static Ticket Buy(string coinId, string cashInput, long? cashBalance, long minTradeCents, int slippageBps)
        {
            if (!Wire.IsSafeId(coinId)) return Ticket.Reject("ticket.pickCoin");
            if (!AmountInput.TryCash(cashInput, out var cents)) return Ticket.Reject("ticket.badCash");
            if (minTradeCents > 0 && cents < minTradeCents) return Ticket.Reject("ticket.minTrade", MoneyFormat.Cash(minTradeCents));
            if (cashBalance.HasValue && cents > cashBalance.Value) return Ticket.Reject("ticket.overBalance", MoneyFormat.Cash(cashBalance.Value));
            if (!ValidSlippage(slippageBps)) return Ticket.Reject("ticket.badSlippage");
            return Ticket.Send(new JObject { ["coinId"] = coinId, ["side"] = "buy", ["cashCents"] = cents, ["maxSlippageBps"] = slippageBps });
        }

        /// <summary>POST /market/trades sell: `{coinId, side: sell, units (base-unit string), maxSlippageBps}`.</summary>
        public static Ticket Sell(string coinId, string units, string heldUnits, int slippageBps)
        {
            if (!Wire.IsSafeId(coinId)) return Ticket.Reject("ticket.pickCoin");
            if (!TokenUnits.IsPositive(units)) return Ticket.Reject("ticket.badUnits");
            if (heldUnits != null && (TokenUnits.Compare(units, heldUnits) ?? 1) > 0) return Ticket.Reject("ticket.overHeld");
            if (!ValidSlippage(slippageBps)) return Ticket.Reject("ticket.badSlippage");
            return Ticket.Send(new JObject { ["coinId"] = coinId, ["side"] = "sell", ["units"] = units, ["maxSlippageBps"] = slippageBps });
        }

        /// <summary>POST /perps `{coinId, side, leverage, marginCents}`. `cap` is the UI pre-check from LeverageCap (null = unknown).</summary>
        public static Ticket OpenPerp(string coinId, string side, int leverage, int? cap, string marginInput, long? cashBalance)
        {
            if (!Wire.IsSafeId(coinId)) return Ticket.Reject("ticket.pickCoin");
            if (side != "long" && side != "short") return Ticket.Reject("ticket.pickSide");
            int max = Math.Min(ContractMaxLeverage, cap ?? ContractMaxLeverage);
            if (leverage < 1 || leverage > max) return Ticket.Reject("ticket.leverageCap", max + "x");
            if (!AmountInput.TryCash(marginInput, out var margin)) return Ticket.Reject("ticket.badCash");
            if (cashBalance.HasValue && margin > cashBalance.Value) return Ticket.Reject("ticket.overBalance", MoneyFormat.Cash(cashBalance.Value));
            return Ticket.Send(new JObject { ["coinId"] = coinId, ["side"] = side, ["leverage"] = leverage, ["marginCents"] = margin });
        }

        /// <summary>POST /predictions/{id}/bets `{outcomeId, stakeCents}`.</summary>
        public static Ticket Bet(string predictionId, string outcomeId, string stakeInput, long? cashBalance)
        {
            if (!Wire.IsSafeId(predictionId) || string.IsNullOrEmpty(outcomeId)) return Ticket.Reject("ticket.pickOutcome");
            if (!AmountInput.TryCash(stakeInput, out var stake)) return Ticket.Reject("ticket.badCash");
            if (cashBalance.HasValue && stake > cashBalance.Value) return Ticket.Reject("ticket.overBalance", MoneyFormat.Cash(cashBalance.Value));
            return Ticket.Send(new JObject { ["outcomeId"] = outcomeId, ["stakeCents"] = stake });
        }

        /// <summary>
        /// POST /alpha-calls `{coinId, entryMicros, targetMicros, stopMicros}` (JSON integers per the contract). The entry is
        /// the server's current price; target and stop must sit on opposite sides of it.
        /// </summary>
        public static Ticket AlphaCall(string coinId, long entryMicros, string targetInput, string stopInput)
        {
            if (!Wire.IsSafeId(coinId) || entryMicros <= 0) return Ticket.Reject("ticket.pickCoin");
            if (!AmountInput.TryMicros(targetInput, out var target)) return Ticket.Reject("ticket.badPrice");
            if (!AmountInput.TryMicros(stopInput, out var stop)) return Ticket.Reject("ticket.badPrice");
            bool bullish = target > entryMicros && stop < entryMicros;
            bool bearish = target < entryMicros && stop > entryMicros;
            if (!bullish && !bearish) return Ticket.Reject("ticket.callSides");
            return Ticket.Send(new JObject { ["coinId"] = coinId, ["entryMicros"] = entryMicros, ["targetMicros"] = target, ["stopMicros"] = stop });
        }

        /// <summary>
        /// POST /ore/stake/tx `{action, amount?}`. deposit/withdraw need a positive ORE amount (11 decimals, base units);
        /// claim/compound send no amount. Known balances (server values) bound the amount.
        /// </summary>
        public static Ticket Stake(string action, string amountInput, string balance, string staked)
        {
            switch (action)
            {
                case "claim":
                case "compound":
                    return Ticket.Send(new JObject { ["action"] = action });
                case "deposit":
                case "withdraw":
                    if (!TokenUnits.TryParse(amountInput, TokenUnits.OreDecimals, out var units)) return Ticket.Reject("ticket.badOre");
                    var limit = action == "deposit" ? balance : staked;
                    if (limit != null && (TokenUnits.Compare(units, limit) ?? 1) > 0)
                        return Ticket.Reject(action == "deposit" ? "ticket.overOre" : "ticket.overStaked");
                    return Ticket.Send(new JObject { ["action"] = action, ["amount"] = units });
                default:
                    return Ticket.Reject("ticket.badAction");
            }
        }

        private static bool ValidSlippage(int bps) => bps >= 1 && bps <= 5000;
    }
}
