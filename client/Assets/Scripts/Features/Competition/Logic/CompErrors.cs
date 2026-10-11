using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;

namespace CTLife.Competition
{
    /// <summary>A failure, classified for the UI.</summary>
    public sealed class CompError
    {
        /// <summary>errors.json code, or a client code: NETWORK_ERROR, TIMEOUT, INVALID_RESPONSE, UNAVAILABLE, ERROR.</summary>
        public string Code;
        /// <summary>CompStrings key for the message.</summary>
        public string Key;
        public object[] Args = Array.Empty<object>();
        public bool Retryable;
        /// <summary>A write may have been applied; reconcile before treating it as rejected.</summary>
        public bool OutcomeUnknown;
        /// <summary>`details.reason` from the server, when present.</summary>
        public string Reason;
        public long RetryAfterMs = -1;
        public string ServerMessage;

        public bool Is(string code) => string.Equals(Code, code, StringComparison.Ordinal);

        /// <summary>The route is not served by this backend yet (404/405/501 without a contract error body).</summary>
        public bool IsUnavailable => Is("UNAVAILABLE");

        public string Message => CompStrings.F(Key, Args);

        public static CompError Unavailable() => new CompError { Code = "UNAVAILABLE", Key = "err.unavailable" };
    }

    /// <summary>
    /// Maps exceptions from the Phase 6 endpoints to UI messages. Pure; tested. A missing route (404 without the contract's
    /// `{error}` body, 405 or 501) is "not available yet", never an empty list or invented data.
    /// </summary>
    public static class CompErrors
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
            ["CONFLICT"] = "err.conflict",
            ["NET_WORTH_TOO_LOW"] = "err.netWorth",
            ["INVITE_REQUIRED"] = "err.inviteRequired",
            ["INVITE_INVALID"] = "err.inviteInvalid",
            ["SYBIL_JAILED"] = "err.sybilJailed",
            ["VOTE_NOT_ALLOWED"] = "err.voteNotAllowed",
            ["ALREADY_VOTED"] = "err.alreadyVoted",
            ["PASS_REQUIRED"] = "err.passRequired",
            ["LLM_DISABLED"] = "err.llmDisabled",
            ["X_LINK_DISABLED"] = "err.xDisabled",
            ["X_ACCOUNT_ALREADY_LINKED"] = "err.xAlreadyLinked",
        };

        public static CompError From(Exception e, bool write)
        {
            switch (e)
            {
                case null:
                    return new CompError { Code = "ERROR", Key = "err.generic" };
                case TimeoutException _:
                    return new CompError { Code = "TIMEOUT", Key = write ? "err.unknownOutcome" : "err.network", Retryable = true, OutcomeUnknown = write };
                case FormatException _:
                case OverflowException _:
                    return new CompError { Code = "INVALID_RESPONSE", Key = write ? "err.unknownOutcome" : "err.invalidResponse", Retryable = true, OutcomeUnknown = write };
                case ApiException a:
                    return FromApi(a, write);
                default:
                    return new CompError { Code = "ERROR", Key = write ? "err.unknownOutcome" : "err.generic", Retryable = true, OutcomeUnknown = write };
            }
        }

        private static CompError FromApi(ApiException a, bool write)
        {
            var d = a.Details as JObject;
            var err = new CompError { Code = a.Code, ServerMessage = a.ServerMessage, Reason = CompWire.Str(d?["reason"]) };
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
            bool contractBody = Keys.ContainsKey(a.Code ?? "") || a.Is("NOT_FOUND");
            // The backend answers an unknown route with a contract-shaped body too: 404 NOT_FOUND "No route for this path and
            // method." (verified on main @ 9f3e5ef, 2026-10-08). That is a missing route, not a missing record.
            bool noRoute = a.Status == 404 && (a.ServerMessage ?? "").StartsWith("No route", StringComparison.Ordinal);
            if ((a.Status == 404 && (!contractBody || noRoute)) || a.Status == 405 || a.Status == 501)
            {
                err.Code = "UNAVAILABLE";
                err.Key = "err.unavailable";
                return err;
            }
            if (a.Status >= 500 && !contractBody && !a.Is("SERVER_BUSY"))
            {
                err.Key = write ? "err.unknownOutcome" : "err.server";
                err.Retryable = true;
                err.OutcomeUnknown = write;
                return err;
            }
            if (a.Is("VALIDATION_FAILED") && (err.Reason ?? "").IndexOf("insufficient cash", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                err.Code = "INSUFFICIENT_CASH";
                long need = CompWire.LongOr(d?["need"], -1), have = CompWire.LongOr(d?["have"], -1);
                err.Key = "err.insufficientCash";
                err.Args = new object[] { need >= 0 && have >= 0 && need > have ? Money.Cash(need - have) : "-" };
                return err;
            }
            if (a.Is("FORBIDDEN") && err.Reason == "sybil_jailed")
            {
                err.Code = "SYBIL_JAILED";
                err.Key = "err.sybilJailed";
                return err;
            }
            if (a.Is("FORBIDDEN") && err.Reason == "insufficient_cash_for_stake")
            {
                // Backend 9f3e5ef wording for a stake the player cannot cover.
                err.Code = "INSUFFICIENT_CASH";
                err.Key = "err.insufficientCash";
                err.Args = new object[] { "-" };
                return err;
            }
            err.Key = Keys.TryGetValue(a.Code ?? "", out var k) ? k : "err.generic";
            err.Retryable = a.Is("RATE_LIMITED") || a.Is("SERVER_BUSY");
            if (a.Is("RATE_LIMITED"))
            {
                err.RetryAfterMs = CompWire.LongOr(d?["retryAfterMs"], -1);
                err.Args = new object[] { err.RetryAfterMs > 0 ? CompTime.Countdown(err.RetryAfterMs) : "-" };
            }
            else if (a.Is("INSUFFICIENT_CASH"))
            {
                var shortfall = CompWire.LongOr(d?["shortfallCents"], -1);
                err.Args = new object[] { shortfall >= 0 ? Money.Cash(shortfall) : "-" };
            }
            else if (a.Is("NET_WORTH_TOO_LOW"))
            {
                long need = CompWire.LongOr(d?["requiredNetWorthCents"], -1);
                err.Key = need >= 0 ? "err.netWorthAmount" : "err.netWorth";
                err.Args = need >= 0 ? new object[] { Money.Cash(need) } : Array.Empty<object>();
            }
            else if (a.Is("CONFLICT") && !string.IsNullOrEmpty(err.Reason))
            {
                err.Key = CompStrings.Has("conflict." + err.Reason) ? "conflict." + err.Reason : "err.conflict";
            }
            return err;
        }

        /// <summary>
        /// Venue join rejections (`RejectedMessage.code` or the join error text from a gated room's onAuth), as one message.
        /// Null for any other code.
        /// </summary>
        public static string JoinRejection(string code)
        {
            switch (code)
            {
                case "NET_WORTH_TOO_LOW": return CompStrings.T("err.netWorth");
                case "INVITE_REQUIRED": return CompStrings.T("err.inviteRequired");
                case "SYBIL_JAILED": return CompStrings.T("err.sybilJailed");
                default: return null;
            }
        }
    }

    /// <summary>
    /// Binds one Idempotency-Key (UUIDv4) to one exact request. While the outcome of a write is unknown the same request
    /// reuses the same key, so the server replays the first response instead of executing twice.
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

        public void Complete()
        {
            _key = null;
            _request = null;
        }

        public void Settle(CompError error)
        {
            if (error == null || !error.OutcomeUnknown) Complete();
        }
    }

    /// <summary>Time formatting shared by the screens.</summary>
    public static class CompTime
    {
        public static string Countdown(long ms)
        {
            if (ms <= 0) return "0s";
            long s = (ms + 999) / 1000;
            long d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60, sec = s % 60;
            if (d > 0) return d + "d " + h + "h";
            if (h > 0) return h + "h " + m + "m";
            if (m > 0) return m + "m " + sec + "s";
            return sec + "s";
        }

        public static string When(long epochMs)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToUniversalTime();
            return t.ToString("ddd d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        }

        public static string Date(long epochMs)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToUniversalTime();
            return t.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
