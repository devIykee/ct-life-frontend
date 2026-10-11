using System;
using System.Collections.Generic;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    public sealed class TopUpPack
    {
        public string Id, Label;
        /// <summary>The pack's USDC price as written in economy.json (display; SOL/SKR/ORE pay the live quote).</summary>
        public string PriceUsdc;
        public long CashCents;
        public bool BestValue;
    }

    public sealed class TopUpCurrency
    {
        public string Code;
        public int Decimals;
        public string Mint, DevnetMint;
        public bool MainnetOnly;
        public int BonusBps;
    }

    public enum CurrencyAccess
    {
        /// <summary>Devnet test tokens: no real value moves.</summary>
        Devnet,
        /// <summary>Moves real tokens on Solana mainnet; the UI requires an explicit confirmation before signing.</summary>
        Mainnet,
        Unavailable,
    }

    public readonly struct CurrencyAvailability
    {
        public readonly CurrencyAccess Access;
        /// <summary>MoneyStrings key explaining an Unavailable or Mainnet state.</summary>
        public readonly string ReasonKey;

        public CurrencyAvailability(CurrencyAccess access, string reasonKey)
        {
            Access = access;
            ReasonKey = reasonKey;
        }

        public bool CanPay => Access != CurrencyAccess.Unavailable;
        public bool NeedsMainnetConfirm => Access == CurrencyAccess.Mainnet;
    }

    /// <summary>Top-up packs, currencies and bonuses from config `economy` (contract section 7). Display only.</summary>
    public sealed class TopUpCatalog
    {
        public static readonly string[] CurrencyOrder = { "USDC", "SOL", "SKR", "ORE" };

        public readonly List<TopUpPack> Packs = new List<TopUpPack>();
        public readonly Dictionary<string, TopUpCurrency> Currencies = new Dictionary<string, TopUpCurrency>(StringComparer.Ordinal);
        public long? CapCentsPerDay;
        public bool Loaded => Packs.Count > 0;

        public static TopUpCatalog Parse(JToken economy)
        {
            var cat = new TopUpCatalog();
            if (!(economy is JObject e)) return cat;
            if (e["topUpPacks"] is JArray packs)
                foreach (var p in packs)
                {
                    try
                    {
                        var id = Wire.ReqStr(p["id"], "id");
                        if (!Wire.IsSafeId(id)) continue;
                        cat.Packs.Add(new TopUpPack
                        {
                            Id = id,
                            Label = Wire.Plain(p["label"] ?? p["id"], 24),
                            PriceUsdc = Wire.Str(p["priceUsdc"]) ?? "—",
                            CashCents = Wire.Long(p["cashCents"]),
                            BestValue = p["bestValue"]?.Type == JTokenType.Boolean && (bool)p["bestValue"],
                        });
                    }
                    catch (Exception ex) when (ex is FormatException || ex is OverflowException) { }
                }
            var bonus = e["currencyBonusBps"] as JObject;
            if (e["currencies"] is JObject currencies)
                foreach (var code in CurrencyOrder)
                {
                    if (!(currencies[code] is JObject c)) continue;
                    try
                    {
                        cat.Currencies[code] = new TopUpCurrency
                        {
                            Code = code,
                            Decimals = Wire.Int(c["decimals"]),
                            Mint = Wire.Str(c["mint"]),
                            DevnetMint = Wire.Str(c["devnetMint"]),
                            MainnetOnly = c["mainnetOnly"]?.Type == JTokenType.Boolean && (bool)c["mainnetOnly"],
                            BonusBps = (int)Wire.LongOr(bonus?[code], 0),
                        };
                    }
                    catch (Exception ex) when (ex is FormatException || ex is OverflowException) { }
                }
            cat.CapCentsPerDay = Wire.IsNull(e["topUpCapCentsPerDay"]) ? (long?)null : Wire.LongOr(e["topUpCapCentsPerDay"], 0);
            return cat;
        }

        /// <summary>
        /// Contract section 1: mock has no cluster (wallet flows go to staging); staging is devnet for USDC/SOL and mainnet
        /// for ORE; SKR has no verified devnet mint, so it is prod-only. ORE is mainnet-only everywhere.
        /// </summary>
        public CurrencyAvailability Availability(string code, SolanaCluster cluster)
        {
            if (!Currencies.TryGetValue(code ?? "", out var c)) return new CurrencyAvailability(CurrencyAccess.Unavailable, "topup.cur.unknown");
            if (cluster == SolanaCluster.None) return new CurrencyAvailability(CurrencyAccess.Unavailable, "topup.cur.noCluster");
            if (c.MainnetOnly) return new CurrencyAvailability(CurrencyAccess.Mainnet, "topup.cur.mainnetOnly");
            if (cluster == SolanaCluster.Mainnet) return new CurrencyAvailability(CurrencyAccess.Mainnet, "topup.cur.mainnet");
            if (code != "SOL" && string.IsNullOrEmpty(c.DevnetMint)) return new CurrencyAvailability(CurrencyAccess.Unavailable, "topup.cur.noDevnetMint");
            return new CurrencyAvailability(CurrencyAccess.Devnet, "topup.cur.devnet");
        }

        public int Decimals(string code, int fallback = -1) => Currencies.TryGetValue(code ?? "", out var c) ? c.Decimals : fallback;
    }
}
