using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    /// <summary>A failure, classified for the UI.</summary>
    public sealed class VError
    {
        /// <summary>errors.json code, or a client code: NETWORK_ERROR, TIMEOUT, INVALID_RESPONSE, UNAVAILABLE, WALLET_DECLINED, WALLET_FAILED, ERROR.</summary>
        public string Code;
        /// <summary>VStrings key for the message.</summary>
        public string Key;
        public object[] Args = Array.Empty<object>();
        /// <summary>The same request may be sent again (with the same Idempotency-Key when OutcomeUnknown).</summary>
        public bool Retryable;
        /// <summary>A write may have been applied; reconcile before treating it as rejected.</summary>
        public bool OutcomeUnknown;
        /// <summary>`details.reason` of a CONFLICT / VALIDATION_FAILED, when present.</summary>
        public string Reason;
        /// <summary>For quote_changed: the server's new launch cost.</summary>
        public long? NewCostCents;
        public string ServerMessage;

        public bool Is(string code) => string.Equals(Code, code, StringComparison.Ordinal);

        /// <summary>The route is missing on this server (404 without the contract body, 405, 501): show "not available yet", never fake data.</summary>
        public bool IsUnavailable => Is("UNAVAILABLE");

        public string Message => VStrings.F(Key, Args);

        public static VError Unavailable() => new VError { Code = "UNAVAILABLE", Key = "err.unavailable" };
    }

    /// <summary>Maps exceptions from the Ventures endpoints to UI messages. Pure; tested.</summary>
    public static class VErrors
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
            ["CONFLICT"] = "err.conflict",
        };

        private static readonly Dictionary<string, string> ConflictKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["slot_unavailable"] = "err.slotTaken",
            ["project_limit"] = "err.projectLimit",
            ["quote_changed"] = "err.quoteChanged",
            ["sold_out"] = "err.soldOut",
        };

        /// <param name="write">True for writes: network/timeout/decode failures then leave the outcome unknown.</param>
        public static VError From(Exception e, bool write)
        {
            switch (e)
            {
                case null:
                    return new VError { Code = "ERROR", Key = "err.generic" };
                case WalletException w when w.UserDeclined:
                    return new VError { Code = "WALLET_DECLINED", Key = "err.walletDeclined", Retryable = true };
                case WalletException w:
                    return new VError { Code = "WALLET_FAILED", Key = "err.walletFailed", OutcomeUnknown = true, ServerMessage = w.Message };
                case TimeoutException _:
                    return new VError { Code = "TIMEOUT", Key = write ? "err.unknownOutcome" : "err.network", Retryable = true, OutcomeUnknown = write };
                case FormatException _:
                case OverflowException _:
                    return new VError { Code = "INVALID_RESPONSE", Key = write ? "err.unknownOutcome" : "err.invalidResponse", Retryable = true, OutcomeUnknown = write };
                case ApiException a:
                    return FromApi(a, write);
                default:
                    return new VError { Code = "ERROR", Key = write ? "err.unknownOutcome" : "err.generic", Retryable = true, OutcomeUnknown = write };
            }
        }

        private static VError FromApi(ApiException a, bool write)
        {
            var d = a.Details as JObject;
            var err = new VError { Code = a.Code, ServerMessage = a.ServerMessage, Reason = VWire.Str(d?["reason"]) };
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
            if ((a.Status == 404 && !a.Is("NOT_FOUND")) || a.Status == 405 || a.Status == 501)
            {
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
                err.Code = "INSUFFICIENT_CASH";
                err.Key = "err.insufficientCash";
                long need = VWire.LongOr(d?["need"], -1), have = VWire.LongOr(d?["have"], -1);
                err.Args = new object[] { need >= 0 && have >= 0 && need > have ? Money.Cash(need - have) : "—" };
                return err;
            }
            if (a.Is("CONFLICT"))
            {
                err.Key = ConflictKeys.TryGetValue(reason, out var ck) ? ck : "err.conflict";
                if (reason == "quote_changed")
                {
                    var nc = VWire.LongOr(d?["launchCostCents"], -1);
                    err.NewCostCents = nc >= 0 ? nc : (long?)null;
                    err.Args = new object[] { err.NewCostCents.HasValue ? Money.Cash(err.NewCostCents.Value) : "—" };
                }
                else if (reason == "project_limit")
                    err.Args = new object[] { VWire.LongOr(d?["max"], 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
                return err;
            }
            err.Key = Keys.TryGetValue(a.Code ?? "", out var k) ? k : "err.generic";
            err.Retryable = a.Is("RATE_LIMITED") || a.Is("SERVER_BUSY");
            if (a.Is("INSUFFICIENT_CASH"))
            {
                var shortfall = VWire.LongOr(d?["shortfallCents"], -1);
                err.Args = new object[] { shortfall >= 0 ? Money.Cash(shortfall) : "—" };
            }
            else if (a.Is("RATE_LIMITED"))
            {
                var ms = VWire.LongOr(d?["retryAfterMs"], -1);
                err.Args = new object[] { ms > 0 ? Math.Ceiling(ms / 1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s" : "—" };
            }
            return err;
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

        public void Settle(VError error)
        {
            if (error == null || !error.OutcomeUnknown) Complete();
        }
    }

    public enum WriteStage { Idle, Sending, Done, Rejected, Unknown }

    /// <summary>
    /// One retained write (fund / launch / cancel / accept ...): Idle → Sending → Done | Rejected | Unknown. Unknown keeps the
    /// key so "Try again" replays the same request instead of executing twice.
    /// </summary>
    public sealed class WriteFlow
    {
        private readonly RetainedKey _key;
        public WriteStage Stage { get; private set; } = WriteStage.Idle;
        public VError Error { get; private set; }
        public string Request { get; private set; }
        public event Action Changed;

        public WriteFlow(Func<string> newKey = null) { _key = new RetainedKey(newKey); }

        public bool KeyRetained => _key.Pending;

        /// <summary>Returns the key, or null when busy or when a different request would abandon an unknown outcome.</summary>
        public string Begin(string method, string path, string body)
        {
            if (Stage == WriteStage.Sending) return null;
            var req = method + " " + path + "\n" + (body ?? "");
            if (Stage == WriteStage.Unknown && Request != req) return null;
            Request = req;
            Stage = WriteStage.Sending;
            Error = null;
            Changed?.Invoke();
            return _key.KeyFor(method, path, body);
        }

        public void Succeeded()
        {
            if (Stage != WriteStage.Sending) return;
            _key.Complete();
            Stage = WriteStage.Done;
            Changed?.Invoke();
        }

        public void Failed(VError e)
        {
            if (Stage != WriteStage.Sending) return;
            Error = e ?? new VError { Code = "ERROR", Key = "err.generic" };
            _key.Settle(Error);
            Stage = Error.OutcomeUnknown ? WriteStage.Unknown : WriteStage.Rejected;
            Changed?.Invoke();
        }

        /// <summary>Back to Idle. Abandoning an Unknown outcome is explicit (the caller re-reads server state first).</summary>
        public void Reset()
        {
            if (Stage == WriteStage.Sending) return;
            _key.Complete();
            Stage = WriteStage.Idle;
            Error = null;
            Request = null;
            Changed?.Invoke();
        }
    }
}
