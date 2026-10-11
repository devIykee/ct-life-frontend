using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    /// <summary>A failure, classified for the UI.</summary>
    public sealed class MoneyError
    {
        /// <summary>errors.json code, or a client code: NETWORK_ERROR, TIMEOUT, DECODE_FAILED, INVALID_RESPONSE, WALLET_DECLINED, WALLET_FAILED, UNAVAILABLE, ERROR.</summary>
        public string Code;
        /// <summary>MoneyStrings key for the message.</summary>
        public string Key;
        /// <summary>Values for the message's {0}, {1} slots (already formatted).</summary>
        public object[] Args = Array.Empty<object>();
        /// <summary>The same request may be sent again (with the same Idempotency-Key when OutcomeUnknown).</summary>
        public bool Retryable;
        /// <summary>A write may have been applied; reconcile before treating it as rejected.</summary>
        public bool OutcomeUnknown;
        public string ServerMessage;

        public bool Is(string code) => string.Equals(Code, code, StringComparison.Ordinal);
    }

    /// <summary>Maps exceptions from the money endpoints and the wallet to UI messages. Pure; tested in EditMode.</summary>
    public static class MoneyErrors
    {
        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TOPUP_CAP_REACHED"] = "err.topupCap",
            ["PAYMENT_EXPIRED"] = "err.paymentExpired",
            ["PAYMENT_UNDERPAID"] = "err.paymentUnderpaid",
            ["INSUFFICIENT_CASH"] = "err.insufficientCash",
            ["SLIPPAGE_EXCEEDED"] = "err.slippage",
            ["LEVERAGE_NOT_ALLOWED"] = "err.leverage",
            ["VALIDATION_FAILED"] = "err.validation",
            ["NOT_FOUND"] = "err.notFound",
            ["FORBIDDEN"] = "err.forbidden",
            ["RATE_LIMITED"] = "err.rateLimited",
            ["SERVER_BUSY"] = "err.busy",
            ["TOKEN_EXPIRED"] = "err.session",
            ["UNAUTHORIZED"] = "err.session",
            ["CLIENT_OUTDATED"] = "err.outdated",
        };

        /// <param name="write">True for POSTs: network/timeout/decode failures then leave the outcome unknown.</param>
        public static MoneyError From(Exception e, bool write)
        {
            switch (e)
            {
                case null:
                    return new MoneyError { Code = "ERROR", Key = "err.generic" };
                case WalletException w when w.UserDeclined:
                    return new MoneyError { Code = "WALLET_DECLINED", Key = "err.walletDeclined", Retryable = true };
                case WalletException w:
                    return new MoneyError { Code = "WALLET_FAILED", Key = "err.walletFailed", Retryable = false, OutcomeUnknown = true, ServerMessage = w.Message };
                case TimeoutException _:
                    return new MoneyError { Code = "TIMEOUT", Key = write ? "err.unknownOutcome" : "err.network", Retryable = true, OutcomeUnknown = write };
                case FormatException _:
                case OverflowException _:
                    return new MoneyError { Code = "INVALID_RESPONSE", Key = write ? "err.unknownOutcome" : "err.invalidResponse", Retryable = true, OutcomeUnknown = write };
                case ApiException a:
                    return FromApi(a, write);
                default:
                    return new MoneyError { Code = "ERROR", Key = "err.generic", Retryable = true, OutcomeUnknown = write };
            }
        }

        private static MoneyError FromApi(ApiException a, bool write)
        {
            var err = new MoneyError { Code = a.Code, ServerMessage = a.ServerMessage };
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
            if (a.Status == 404 && !a.Is("NOT_FOUND") || a.Status == 501)
            {
                // A 404 without the contract error body means the route itself is missing on this server.
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
            err.Key = Keys.TryGetValue(a.Code ?? "", out var k) ? k : "err.generic";
            err.Retryable = a.Is("RATE_LIMITED") || a.Is("SERVER_BUSY") || a.Is("SLIPPAGE_EXCEEDED");
            err.Args = ArgsFor(a);
            return err;
        }

        /// <summary>Server-provided details only (contract examples), already formatted for display.</summary>
        private static object[] ArgsFor(ApiException a)
        {
            var d = a.Details as JObject;
            switch (a.Code)
            {
                case "INSUFFICIENT_CASH":
                {
                    var shortfall = Wire.LongOr(d?["shortfallCents"], -1);
                    return new object[] { shortfall >= 0 ? MoneyFormat.Cash(shortfall) : "-" };
                }
                case "LEVERAGE_NOT_ALLOWED":
                {
                    var max = Wire.LongOr(d?["max"], Wire.LongOr(d?["allowed"], -1));
                    return new object[] { max > 0 ? max + "x" : "-" };
                }
                case "SLIPPAGE_EXCEEDED":
                {
                    var bps = Wire.LongOr(d?["executedBps"], -1);
                    return new object[] { bps >= 0 ? MoneyFormat.Bps(bps, false) : "-" };
                }
                default:
                    return Array.Empty<object>();
            }
        }
    }
}
