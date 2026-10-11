using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>A failure, classified for the UI.</summary>
    public sealed class CityError
    {
        /// <summary>errors.json code, or a client code: NETWORK_ERROR, TIMEOUT, DECODE_FAILED, INVALID_RESPONSE, UNAVAILABLE, WALLET_DECLINED, WALLET_FAILED, ERROR.</summary>
        public string Code;
        /// <summary>CityLifeStrings key for the message.</summary>
        public string Key;
        /// <summary>Values for the message's {0}, {1} slots (already formatted).</summary>
        public object[] Args = Array.Empty<object>();
        /// <summary>The same request may be sent again (with the same Idempotency-Key when OutcomeUnknown).</summary>
        public bool Retryable;
        /// <summary>A write may have been applied; reconcile (GET /me etc.) before treating it as rejected.</summary>
        public bool OutcomeUnknown;
        /// <summary>A server reason from `details.reason` (backend), when present.</summary>
        public string Reason;
        public string ServerMessage;

        public bool Is(string code) => string.Equals(Code, code, StringComparison.Ordinal);

        public string Message => CityLifeStrings.F(Key, Args);

        public static CityError Unavailable(string key = "err.unavailable") => new CityError { Code = "UNAVAILABLE", Key = key };
    }

    /// <summary>
    /// Maps exceptions from the City Life endpoints to UI messages. Pure; tested in EditMode.
    /// Accepts the contract's error envelope and the backend's variants (VALIDATION_FAILED with `details.reason =
    /// "insufficient cash"` instead of INSUFFICIENT_CASH; FORBIDDEN `already_attempted` for the daily clue).
    /// </summary>
    public static class CityErrors
    {
        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["INSUFFICIENT_CASH"] = "err.insufficientCash",
            ["VALIDATION_FAILED"] = "err.validation",
            ["NOT_FOUND"] = "err.notFound",
            ["FORBIDDEN"] = "err.forbidden",
            ["BLOCKED"] = "err.blocked",
            ["RATE_LIMITED"] = "err.rateLimited",
            ["SERVER_BUSY"] = "err.busy",
            ["TOKEN_EXPIRED"] = "err.session",
            ["UNAUTHORIZED"] = "err.session",
            ["CLIENT_OUTDATED"] = "err.outdated",
            ["PAYMENT_EXPIRED"] = "err.paymentExpired",
            ["PAYMENT_UNDERPAID"] = "err.paymentUnderpaid",
            ["VENUE_FULL"] = "err.homeFull",
        };

        /// <param name="write">True for writes: network/timeout/decode failures then leave the outcome unknown.</param>
        public static CityError From(Exception e, bool write)
        {
            switch (e)
            {
                case null:
                    return new CityError { Code = "ERROR", Key = "err.generic" };
                case UnavailableException _:
                    return CityError.Unavailable();
                case WalletException w when w.UserDeclined:
                    return new CityError { Code = "WALLET_DECLINED", Key = "err.walletDeclined", Retryable = true };
                case WalletException w:
                    return new CityError { Code = "WALLET_FAILED", Key = "err.walletFailed", OutcomeUnknown = true, ServerMessage = w.Message };
                case TimeoutException _:
                    return new CityError { Code = "TIMEOUT", Key = write ? "err.unknownOutcome" : "err.network", Retryable = true, OutcomeUnknown = write };
                case FormatException _:
                case OverflowException _:
                    return new CityError { Code = "INVALID_RESPONSE", Key = write ? "err.unknownOutcome" : "err.invalidResponse", Retryable = true, OutcomeUnknown = write };
                case ApiException a:
                    return FromApi(a, write);
                default:
                    return new CityError { Code = "ERROR", Key = write ? "err.unknownOutcome" : "err.generic", Retryable = true, OutcomeUnknown = write };
            }
        }

        private static CityError FromApi(ApiException a, bool write)
        {
            var err = new CityError { Code = a.Code, ServerMessage = a.ServerMessage, Reason = CityWire.Str((a.Details as JObject)?["reason"]) };
            if (a.IsNetwork || a.Is("NETWORK_ERROR"))
            {
                err.Code = "NETWORK_ERROR";
                err.Key = write ? "err.unknownOutcome" : "err.network";
                err.Retryable = true;
                err.OutcomeUnknown = write;
                return err;
            }
            if (a.Is("DECODE_FAILED"))
            {
                err.Key = write ? "err.unknownOutcome" : "err.invalidResponse";
                err.Retryable = true;
                err.OutcomeUnknown = write;
                return err;
            }
            var details = a.Details as JObject;
            bool noRoute = a.Is("NOT_FOUND") && a.ServerMessage == "No route for this path and method." &&
                !string.IsNullOrEmpty(CityWire.Str(details?["path"])) && !string.IsNullOrEmpty(CityWire.Str(details?["method"]));
            if ((a.Status == 404 && (!a.Is("NOT_FOUND") || noRoute)) || a.Status == 405 || a.Status == 501)
            {
                // Recognize the server's explicit route-missing envelope, not an entity NOT_FOUND.
                err.Code = "UNAVAILABLE";
                err.Key = "err.unavailable";
                return err;
            }
            if (a.Status >= 500 && !a.Is("SERVER_BUSY"))
            {
                err.Key = write ? "err.unknownOutcome" : "err.server";
                err.Retryable = true;
                err.OutcomeUnknown = write;
                return err;
            }
            var reason = err.Reason ?? "";
            if (a.Is("VALIDATION_FAILED") && reason.IndexOf("insufficient cash", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Backend 9f3e5ef reports a short balance as VALIDATION_FAILED {reason, have, need}.
                err.Code = "INSUFFICIENT_CASH";
                var d = a.Details as JObject;
                long need = CityWire.LongOr(d?["need"], -1), have = CityWire.LongOr(d?["have"], -1);
                err.Key = "err.insufficientCash";
                err.Args = new object[] { need >= 0 && have >= 0 && need > have ? Money.Cash(need - have) : "—" };
                return err;
            }
            if (a.Is("FORBIDDEN") && reason == "already_attempted")
            {
                err.Key = "err.alreadyAnswered";
                return err;
            }
            if (a.Is("FORBIDDEN") && (reason == "visits_closed" || reason == "followers_only" || reason == "blocked"))
            {
                err.Key = "visit.denied." + reason;
                return err;
            }
            err.Key = Keys.TryGetValue(a.Code ?? "", out var k) ? k : "err.generic";
            err.Retryable = a.Is("RATE_LIMITED") || a.Is("SERVER_BUSY");
            err.Args = ArgsFor(a);
            return err;
        }

        private static object[] ArgsFor(ApiException a)
        {
            var d = a.Details as JObject;
            switch (a.Code)
            {
                case "INSUFFICIENT_CASH":
                {
                    var shortfall = CityWire.LongOr(d?["shortfallCents"], -1);
                    return new object[] { shortfall >= 0 ? Money.Cash(shortfall) : "—" };
                }
                case "RATE_LIMITED":
                {
                    var ms = CityWire.LongOr(d?["retryAfterMs"], -1);
                    return new object[] { ms > 0 ? WeekClock.Countdown(ms) : "—" };
                }
                default:
                    return Array.Empty<object>();
            }
        }
    }

    /// <summary>
    /// Binds one Idempotency-Key (UUIDv4, contract section 1) to one exact request. While the outcome of a write is
    /// unknown the same request reuses the same key, so the server replays the first response instead of executing twice.
    /// A definitive answer retires the key; a different body never reuses a key.
    /// </summary>
    public sealed class RetainedKey
    {
        private readonly Func<string> _newKey;
        private string _request, _key;

        public RetainedKey(Func<string> newKey = null) { _newKey = newKey ?? (() => Guid.NewGuid().ToString("D")); }

        public bool Pending => _key != null;

        public string KeyFor(string method, string path, string body)
        {
            var request = method + " " + path + "\n" + (body ?? "");
            if (_key == null || _request != request)
            {
                _key = _newKey();
                _request = request;
            }
            return _key;
        }

        /// <summary>Call after a definitive server answer, or when the player abandons an unknown outcome on purpose.</summary>
        public void Complete()
        {
            _key = null;
            _request = null;
        }

        /// <summary>Keeps the key only when the failure left the outcome unknown.</summary>
        public void Settle(CityError error)
        {
            if (error == null || !error.OutcomeUnknown) Complete();
        }
    }
}
