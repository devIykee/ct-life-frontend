using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    public enum WalletBalanceStatus { Disconnected, Loading, Ready, Partial, Unavailable }
    public enum WalletTokenState { Loading, Ready, Unavailable }

    /// <summary>Observed wallet holdings, never fictional game Cash or an estimate of USD value.</summary>
    public sealed class WalletTokenBalance
    {
        public string Symbol { get; }
        public string Mint { get; }
        public WalletTokenState State { get; }
        public decimal? Amount { get; }
        public string RawAmount { get; }
        public int Decimals { get; }
        public DateTimeOffset? UpdatedAtUtc { get; }
        public string ErrorCode { get; }
        private readonly bool _stale;
        private readonly Func<DateTimeOffset> _now;
        public bool IsStale => Amount.HasValue && (_stale || _now() - UpdatedAtUtc.Value > TimeSpan.FromMinutes(1));

        internal WalletTokenBalance(string symbol, string mint, WalletTokenState state, WalletBalanceValue value,
            DateTimeOffset? updated, bool stale, string error, Func<DateTimeOffset> now)
        {
            Symbol = symbol; Mint = mint; State = state; UpdatedAtUtc = updated; _stale = stale; ErrorCode = error; _now = now;
            Amount = value?.Amount; RawAmount = value?.RawAmount; Decimals = value?.Decimals ?? 0;
        }

        /// <summary>Null means unknown, not zero. Tiny positive holdings never round down to a false zero.</summary>
        public string FormatAmount(int maxDecimals = 4)
        {
            if (!Amount.HasValue) return null;
            int digits = Math.Max(0, Math.Min(Decimals, Math.Min(18, maxDecimals)));
            decimal scale = WalletBalanceParser.Scale(digits), threshold = 1m / scale;
            string pattern = digits == 0 ? "0" : "0." + new string('#', digits);
            if (Amount.Value > 0 && Amount.Value < threshold)
                return "<" + threshold.ToString(pattern, CultureInfo.InvariantCulture);
            // Truncate the compact display: do not round a holding up to a spendable amount.
            return (decimal.Truncate(Amount.Value * scale) / scale).ToString(pattern, CultureInfo.InvariantCulture);
        }
    }

    public sealed class WalletBalanceSnapshot
    {
        public string Wallet { get; }
        public SolanaCluster Cluster { get; }
        public WalletBalanceStatus Status { get; }
        public bool IsRefreshing { get; }
        public IReadOnlyList<WalletTokenBalance> Tokens { get; }
        public WalletTokenBalance Token(string symbol) => Tokens.FirstOrDefault(t => t.Symbol == symbol);
        internal WalletBalanceSnapshot(string wallet, SolanaCluster cluster, WalletBalanceStatus status,
            bool refreshing, WalletTokenBalance[] tokens)
        { Wallet = wallet; Cluster = cluster; Status = status; IsRefreshing = refreshing; Tokens = Array.AsReadOnly(tokens); }
    }

    public sealed class WalletBalanceValue
    {
        public string RawAmount { get; }
        public int Decimals { get; }
        public decimal Amount { get; }
        internal WalletBalanceValue(decimal raw, int decimals)
        { RawAmount = raw.ToString("0", CultureInfo.InvariantCulture); Decimals = decimals; Amount = raw / WalletBalanceParser.Scale(decimals); }
    }

    public sealed class WalletMintInfo
    {
        public string Program { get; }
        public int Decimals { get; }
        internal WalletMintInfo(string program, int decimals) { Program = program; Decimals = decimals; }
    }

    /// <summary>Strict RPC parsing: missing, malformed and mismatched data must not become a zero balance.</summary>
    public static class WalletBalanceParser
    {
        public const string TokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
        public const string Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
        public static WalletBalanceValue Native(JObject result)
        {
            RequireContext(result);
            if (result["value"]?.Type != JTokenType.Integer) throw Invalid();
            return new WalletBalanceValue(Unsigned(result["value"]), 9);
        }

        public static WalletMintInfo Mint(JObject result)
        {
            RequireContext(result);
            var account = result["value"] as JObject;
            string program = (string)account?["owner"];
            var parsed = account?["data"]?["parsed"];
            var info = parsed?["info"];
            if (!IsTokenProgram(program) || (string)parsed?["type"] != "mint" || (bool?)info?["isInitialized"] != true ||
                info?["decimals"]?.Type != JTokenType.Integer) throw Invalid();
            int decimals = (int)info["decimals"];
            if (decimals < 0 || decimals > 18) throw Invalid();
            return new WalletMintInfo(program, decimals);
        }

        public static WalletBalanceValue Tokens(JObject result, string wallet, string mint, WalletMintInfo metadata)
        {
            RequireContext(result);
            if (!(result["value"] is JArray accounts) || accounts.Count > 1024 || metadata == null) throw Invalid();
            decimal total = 0;
            var seen = new HashSet<string>();
            foreach (var row in accounts)
            {
                string pubkey = (string)row["pubkey"];
                var account = row["account"];
                var parsed = account?["data"]?["parsed"];
                var info = parsed?["info"];
                var amount = info?["tokenAmount"];
                if (!IsAddress(pubkey) || !seen.Add(pubkey) || (string)account?["owner"] != metadata.Program ||
                    (string)parsed?["type"] != "account" || (string)info?["owner"] != wallet || (string)info?["mint"] != mint ||
                    amount?["amount"]?.Type != JTokenType.String || amount?["decimals"]?.Type != JTokenType.Integer ||
                    (int)amount["decimals"] != metadata.Decimals) throw Invalid();
                total = checked(total + Unsigned(amount["amount"]));
            }
            return new WalletBalanceValue(total, metadata.Decimals);
        }

        public static bool IsAddress(string value) => value != null && value.Length >= 32 && value.Length <= 44 && Base58.IsPublicKeyLength(value);
        internal static decimal Scale(int decimals) { decimal scale = 1; for (int i = 0; i < decimals; i++) scale *= 10; return scale; }
        private static bool IsTokenProgram(string value) => value == TokenProgram || value == Token2022Program;
        private static void RequireContext(JObject result)
        {
            if (result == null || result["context"]?["slot"]?.Type != JTokenType.Integer) throw Invalid();
            Unsigned(result["context"]["slot"]);
        }
        private static ulong Unsigned(JToken value)
        {
            if (value == null || value.Type != JTokenType.Integer && value.Type != JTokenType.String ||
                !ulong.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong n)) throw Invalid();
            return n;
        }
        private static FormatException Invalid() => new FormatException("Invalid wallet balance response");
    }
}
