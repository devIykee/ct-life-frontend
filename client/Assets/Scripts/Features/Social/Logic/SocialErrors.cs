using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Binds one Idempotency-Key (UUIDv4, contract section 1) to one exact request. While the outcome is unknown (network
    /// loss, timeout, 5xx, unreadable reply) the same request reuses the same key so the server replays the first response
    /// instead of executing twice. A definitive answer completes the write: the next one is a new request with a new key.
    /// A different body never reuses a key (the server answers VALIDATION_FAILED on a body-hash mismatch).
    /// </summary>
    public sealed class RetainedKey
    {
        private readonly Func<string> _newKey;
        private string _request, _key;

        public RetainedKey(Func<string> newKey = null) { _newKey = newKey ?? (() => Guid.NewGuid().ToString("D")); }

        public bool Pending => _key != null;
        public string Current => _key;

        public string For(string method, string path, string body)
        {
            var request = method + " " + path + "\n" + (body ?? "");
            if (_key == null || _request != request)
            {
                _key = _newKey();
                _request = request;
            }
            return _key;
        }

        public void Complete() { _key = null; _request = null; }
    }

    public enum ErrorKind { Definitive, OutcomeUnknown, Session, RouteMissing, Offline, Canceled }

    /// <summary>A failure classified for the UI. Pure; tested in EditMode.</summary>
    public sealed class SocialError
    {
        public string Code;
        public ErrorKind Kind;
        public int Status;
        public string CopyKey;
        public string ServerMessage;
        public JToken Details;

        public bool OutcomeUnknown => Kind == ErrorKind.OutcomeUnknown || Kind == ErrorKind.Offline;
        public bool Is(string code) => string.Equals(Code, code, StringComparison.Ordinal);

        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BLOCKED"] = "err.blocked",
            ["NOT_SPACE_HOST"] = "err.notHost",
            ["FORBIDDEN"] = "err.forbidden",
            ["NOT_FOUND"] = "err.notFound",
            ["VALIDATION_FAILED"] = "err.validation",
            ["RATE_LIMITED"] = "err.rateLimited",
            ["SERVER_BUSY"] = "err.busy",
            ["INSUFFICIENT_CASH"] = "err.insufficientCash",
            ["TOKEN_EXPIRED"] = "err.session",
            ["UNAUTHORIZED"] = "err.session",
            ["CLIENT_OUTDATED"] = "err.outdated",
            ["PAYMENT_EXPIRED"] = "err.paymentExpired",
        };

        /// <summary>
        /// The backend's not-found handler answers an unknown route with `NOT_FOUND` plus `details {path, method}`
        /// (ct-life-backend server/api/main.ts). That is "this server does not have the endpoint", not "the resource is gone".
        /// </summary>
        public static bool IsRouteMissing(ApiException a) =>
            a != null && a.Status == 404 && (a.Is("HTTP_404") || (a.Is("NOT_FOUND") && a.Details is JObject d && d["method"] != null && d["path"] != null));

        public static SocialError From(Exception e)
        {
            switch (e)
            {
                case null:
                    return new SocialError { Code = "ERROR", Kind = ErrorKind.Definitive, CopyKey = "err.generic" };
                case OperationCanceledException _:
                    return new SocialError { Code = "CANCELED", Kind = ErrorKind.Canceled, CopyKey = "err.generic" };
                case TimeoutException _:
                    return new SocialError { Code = "TIMEOUT", Kind = ErrorKind.OutcomeUnknown, CopyKey = "err.timeout" };
                case WalletException w:
                    return new SocialError { Code = w.UserDeclined ? "WALLET_DECLINED" : "WALLET_FAILED", Kind = ErrorKind.Definitive,
                        CopyKey = w.UserDeclined ? "err.walletDeclined" : "err.walletFailed", ServerMessage = w.Message };
                case FormatException _:
                    return new SocialError { Code = "INVALID_RESPONSE", Kind = ErrorKind.OutcomeUnknown, CopyKey = "err.invalid" };
                case ApiException a:
                    return FromApi(a);
                default:
                    return new SocialError { Code = "ERROR", Kind = ErrorKind.OutcomeUnknown, CopyKey = "err.generic" };
            }
        }

        private static SocialError FromApi(ApiException a)
        {
            var err = new SocialError { Code = a.Code, Status = a.Status, ServerMessage = a.ServerMessage, Details = a.Details };
            if (a.IsNetwork || a.Status == 0)
            {
                err.Kind = ErrorKind.Offline;
                err.CopyKey = "err.network";
                return err;
            }
            if (IsRouteMissing(a))
            {
                err.Kind = ErrorKind.RouteMissing;
                err.CopyKey = "err.routeMissing";
                return err;
            }
            if (a.Is("DECODE_FAILED") || a.Status >= 500 || a.Is("SERVER_BUSY"))
            {
                err.Kind = ErrorKind.OutcomeUnknown;
                err.CopyKey = a.Is("SERVER_BUSY") ? "err.busy" : "err.server";
                return err;
            }
            if (a.Status == 401 || a.Is("TOKEN_EXPIRED") || a.Is("UNAUTHORIZED"))
            {
                err.Kind = ErrorKind.Session;
                err.CopyKey = "err.session";
                return err;
            }
            err.Kind = ErrorKind.Definitive;
            // The backend reports NOT_SPACE_HOST as FORBIDDEN {reason: NOT_SPACE_HOST}.
            var reason = a.Details is JObject d ? SocialWire.Str(d["reason"]) : null;
            if (a.Is("FORBIDDEN") && reason == "NOT_SPACE_HOST") err.Code = "NOT_SPACE_HOST";
            if (a.Status == 429 && !Keys.ContainsKey(err.Code)) err.Code = "RATE_LIMITED";
            err.CopyKey = Keys.TryGetValue(err.Code, out var key) ? key : "err.generic";
            return err;
        }
    }

    /// <summary>Capped exponential retry schedule (reconnects, polling after errors). Pure.</summary>
    public sealed class Backoff
    {
        private static readonly int[] StepsMs = { 1000, 2000, 4000, 8000, 15000, 30000 };
        private readonly int _max;
        public int Attempts { get; private set; }
        public double NextAtMs { get; private set; }

        public Backoff(int maxAttempts = 6) { _max = Math.Max(1, maxAttempts); }

        public bool Exhausted => Attempts >= _max;
        public bool Due(double nowMs) => !Exhausted && nowMs >= NextAtMs;
        public void Reset() { Attempts = 0; NextAtMs = 0; }
        public void Fail(double nowMs)
        {
            NextAtMs = nowMs + StepsMs[Math.Min(Attempts, StepsMs.Length - 1)];
            Attempts++;
        }
        /// <summary>Network came back: try again now without consuming an attempt.</summary>
        public void Hurry(double nowMs)
        {
            if (Exhausted) Attempts = _max - 1;
            NextAtMs = nowMs;
        }
    }
}
