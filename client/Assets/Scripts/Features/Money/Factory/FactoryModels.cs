using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    // Rugpull Factory (backend #14, docs/FACTORY.md): player-launched, Cash-only, server-simulated parody coins at the
    // Launchpad Tower. Nothing here is a token, withdrawable or on chain. The server owns every number; the client mirrors
    // the published rules only to stop obviously unsendable input and to preview the launch.

    /// <summary>`GET /factory/config`: fees, limits, art ids and the (informational) arc odds.</summary>
    public sealed class FactoryConfig
    {
        public static readonly string[] DefaultTemplates =
            { "rf_frog", "rf_dog", "rf_cat", "rf_rocket", "rf_robot", "rf_skull", "rf_diamond", "rf_banana", "rf_ghost", "rf_chad", "rf_clown", "rf_crown" };

        public long LaunchFeeCents = 500, MinLiquidityCents = 1_000, MaxLiquidityCents = 100_000;
        public int MaxDevAllocationBps = 5_000, DailyLaunchCap = 3, CooldownSec = 600;
        public int NameMin = 2, NameMax = 24, TickerMin = 2, TickerMax = 6;
        public int MoonMultipleBps = 30_000, CreatorFeeShareBps = 5_000, RugSellBps = 3_000, RugWindowMin = 60;
        public List<string> Templates = new List<string>(DefaultTemplates);
        /// <summary>Outcome id (rug, slow_bleed, sideways, moon) to percent. Information only; the arc is server-side.</summary>
        public Dictionary<string, double> ArcOddsPct = new Dictionary<string, double>();
        /// <summary>False until a real `/factory/config` arrived (the defaults mirror backend #14).</summary>
        public bool FromServer;

        public static FactoryConfig Defaults => new FactoryConfig();

        public bool HasTemplate(string id) => !string.IsNullOrEmpty(id) && Templates.Contains(id);
    }

    /// <summary>`GET /factory/me`: my heat and launch allowance.</summary>
    public sealed class FactoryMe
    {
        public int Heat;
        /// <summary>cool | warm | hot | wanted.</summary>
        public string HeatLevel = "cool";
        public int LaunchesToday, DailyLaunchCap = 3;
        /// <summary>When the next launch is allowed (cooldown or the rolling 24 h cap); null = now.</summary>
        public long? NextLaunchAtMs;

        public int LaunchesLeft => Math.Max(0, DailyLaunchCap - LaunchesToday);
        public bool Capped => LaunchesLeft == 0;

        /// <summary>Milliseconds until a launch is allowed (0 = now). The server re-checks.</summary>
        public long WaitMs(double nowMs) => NextLaunchAtMs is long at && at > nowMs ? (long)Math.Ceiling(at - nowMs) : 0;

        /// <summary>A launch should go through now: no wait left, and not capped (a capped allowance always names its reset
        /// time; once that has passed, the rolling window has room again).</summary>
        public bool CanLaunch(double nowMs) => WaitMs(nowMs) == 0 && (!Capped || NextLaunchAtMs != null);
    }

    /// <summary>A player coin (`/factory/coins` rows and the launch reply).</summary>
    public sealed class FactoryCoin
    {
        public MarketCoin Coin;
        public string TemplateId;
        public long LiquidityCents;
        public int DevAllocationBps;
        public string CreatorUserId, CreatorAlias;
        public bool IsMine, Mooned, RugFlagged;
        /// <summary>The creator fee earned (creator only; null for everyone else).</summary>
        public long? CreatorFeesCents;

        public string CoinId => Coin?.CoinId;
        public string Ticker => Coin?.Ticker;
    }

    /// <summary>`GET /factory/coins/{id}`: the coin plus the market's 1 m chart, top holders and the risk hint.</summary>
    public sealed class FactoryCoinDetail
    {
        public FactoryCoin Coin;
        public List<CandleRow> Candles = new List<CandleRow>();
        public List<HolderRow> Holders = new List<HolderRow>();
        public string RiskHint;
    }

    public static class FactoryParse
    {
        public static FactoryConfig Config(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Missing factory config");
            var c = new FactoryConfig { FromServer = true };
            c.LaunchFeeCents = Wire.LongOr(o["launchFeeCents"], c.LaunchFeeCents);
            c.MinLiquidityCents = Wire.LongOr(o["minLiquidityCents"], c.MinLiquidityCents);
            c.MaxLiquidityCents = Wire.LongOr(o["maxLiquidityCents"], c.MaxLiquidityCents);
            c.MaxDevAllocationBps = (int)Wire.LongOr(o["maxDevAllocationBps"], c.MaxDevAllocationBps);
            c.DailyLaunchCap = (int)Wire.LongOr(o["dailyLaunchCap"], c.DailyLaunchCap);
            c.CooldownSec = (int)Wire.LongOr(o["cooldownSec"], c.CooldownSec);
            c.NameMin = (int)Wire.LongOr(o["name"]?["min"], c.NameMin);
            c.NameMax = (int)Wire.LongOr(o["name"]?["max"], c.NameMax);
            c.TickerMin = (int)Wire.LongOr(o["ticker"]?["min"], c.TickerMin);
            c.TickerMax = (int)Wire.LongOr(o["ticker"]?["max"], c.TickerMax);
            c.MoonMultipleBps = (int)Wire.LongOr(o["moonMultipleBps"], c.MoonMultipleBps);
            c.CreatorFeeShareBps = (int)Wire.LongOr(o["creatorFeeShareBps"], c.CreatorFeeShareBps);
            c.RugSellBps = (int)Wire.LongOr(o["rug"]?["sellBps"], c.RugSellBps);
            c.RugWindowMin = (int)Wire.LongOr(o["rug"]?["windowMin"], c.RugWindowMin);
            if (o["templates"] is JArray arr)
            {
                var ids = arr.Select(MoneyParse.TemplateIdOrNull).Where(x => x != null).Distinct().ToList();
                if (ids.Count > 0) c.Templates = ids;
            }
            if (o["arcOddsPct"] is JObject odds)
                foreach (var kv in odds)
                    if (kv.Value != null && (kv.Value.Type == JTokenType.Integer || kv.Value.Type == JTokenType.Float))
                        c.ArcOddsPct[kv.Key] = kv.Value.Value<double>();
            if (c.MinLiquidityCents <= 0 || c.MaxLiquidityCents < c.MinLiquidityCents || c.NameMax < c.NameMin || c.TickerMax < c.TickerMin)
                throw new FormatException("Inconsistent factory config");
            return c;
        }

        public static FactoryMe Me(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Missing factory me");
            var level = Wire.Str(o["heatLevel"]);
            return new FactoryMe
            {
                Heat = HeatOf(o["heat"]),
                HeatLevel = level == "warm" || level == "hot" || level == "wanted" ? level : "cool",
                LaunchesToday = (int)Math.Max(0, Wire.LongOr(o["launchesToday"], 0)),
                DailyLaunchCap = (int)Math.Max(0, Wire.LongOr(o["dailyLaunchCap"], 3)),
                NextLaunchAtMs = Wire.TimeMs(o["nextLaunchAt"]),
            };
        }

        public static FactoryCoin Coin(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Missing factory coin");
            var market = MoneyParse.Coin(o);
            market.PlayerLaunched = true;   // every /factory/coins row is a player coin
            market.Name = NoMarkup(market.Name);
            market.Ticker = NoMarkup(market.Ticker);
            var creator = o["creator"] as JObject;
            return new FactoryCoin
            {
                Coin = market,
                TemplateId = market.TemplateId,
                LiquidityCents = Wire.LongOr(o["liquidityCents"], 0),
                DevAllocationBps = (int)Wire.LongOr(o["devAllocationBps"], 0),
                CreatorUserId = creator != null && Wire.IsSafeId(Wire.Str(creator["userId"])) ? Wire.Str(creator["userId"]) : null,
                CreatorAlias = creator != null ? NonEmpty(NoMarkup(Wire.Plain(creator["alias"], 32)), "anon") : "anon",
                IsMine = Bool(o["isMine"]),
                Mooned = Bool(o["mooned"]),
                RugFlagged = Bool(o["rugFlagged"]),
                CreatorFeesCents = Wire.OptLong(o["creatorFeesCents"]),
            };
        }

        /// <summary>`{coins: [...]}`; a malformed row is skipped (counted), never fatal.</summary>
        public static List<FactoryCoin> Coins(JToken t, out int skipped)
        {
            skipped = 0;
            var list = new List<FactoryCoin>();
            foreach (var row in Wire.List(t, "coins"))
            {
                try { list.Add(Coin(row)); }
                catch (Exception e) when (e is FormatException || e is OverflowException || e is InvalidCastException) { skipped++; }
            }
            return list;
        }

        public static FactoryCoinDetail Detail(JToken t)
        {
            var coin = Coin(t);
            var d = MoneyParse.DetailFlat(t);
            return new FactoryCoinDetail { Coin = coin, Candles = d.Candles, Holders = d.Holders, RiskHint = d.RiskHint };
        }

        /// <summary>Heat 0..100. The server decays it on read, so it can be fractional (a real reply: 43.1); rounded.</summary>
        public static int HeatOf(JToken t)
        {
            double v = t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<double>()
                : t != null && t.Type == JTokenType.String && double.TryParse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            return (int)Math.Round(Math.Max(0, Math.Min(100, v)), MidpointRounding.AwayFromZero);
        }

        /// <summary>Player text never carries markup: '&lt;' and '&gt;' are dropped (labels also render with rich text off).</summary>
        public static string NoMarkup(string s) => string.IsNullOrEmpty(s) ? s : s.Replace("<", "").Replace(">", "");

        private static bool Bool(JToken t) => t != null && t.Type == JTokenType.Boolean && (bool)t;
        private static string NonEmpty(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
    }

    /// <summary>What the player is about to launch (the strict `POST /factory/coins` body).</summary>
    public sealed class LaunchDraft
    {
        public string Name = "", Ticker = "", TemplateId;
        public long LiquidityCents;
        public int DevAllocationBps;

        /// <summary>The body with the server's own normalisation applied (whitespace collapsed, ticker upper case).</summary>
        public JObject Body() => new JObject
        {
            ["name"] = FactoryRules.NormaliseName(Name),
            ["ticker"] = FactoryRules.NormaliseTicker(Ticker),
            ["templateId"] = TemplateId,
            ["liquidityCents"] = LiquidityCents,
            ["devAllocationBps"] = DevAllocationBps,
        };
    }

    /// <summary>A local pre-check result: Ok, or a FactoryText key (+ args) saying what to fix.</summary>
    public readonly struct FactoryCheck
    {
        public readonly string Key;
        public readonly object[] Args;
        public FactoryCheck(string key, params object[] args) { Key = key; Args = args ?? Array.Empty<object>(); }
        public bool Ok => Key == null;
        public static readonly FactoryCheck Pass = new FactoryCheck(null);
    }

    /// <summary>
    /// The published launch rules (docs/FACTORY.md, factory-filter.ts NAME_CHARS / TICKER_CHARS), mirrored for instant
    /// feedback. The server stays authoritative: its word filter (slurs, links, wallets, impersonation) is not copied here,
    /// so NAME_NOT_ALLOWED can still come back for a name that passes these checks.
    /// </summary>
    public static class FactoryRules
    {
        private static readonly Regex NameChars = new Regex("^[A-Za-z0-9][A-Za-z0-9 .'!_-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex TickerChars = new Regex("^[A-Z0-9]+$", RegexOptions.CultureInvariant);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);
        /// <summary>The house launchpad's price, 0.001 USD per unit (1 cent = 10,000 micros).</summary>
        public const long LaunchPriceMicros = 1_000;

        public static string NormaliseName(string name) => Spaces.Replace((name ?? "").Trim(), " ");
        public static string NormaliseTicker(string ticker) => (ticker ?? "").Trim().TrimStart('$').ToUpperInvariant();

        public static long TotalCostCents(LaunchDraft d, FactoryConfig c) => (c ?? FactoryConfig.Defaults).LaunchFeeCents + Math.Max(0, d?.LiquidityCents ?? 0);

        /// <summary>Units the liquidity buys at the launch price (preview; the server computes the real numbers).</summary>
        public static long SeedUnits(long liquidityCents) => Math.Max(0, liquidityCents) * 10_000 / LaunchPriceMicros;

        /// <summary>The creator's share of the seed units at <paramref name="devBps"/> (preview).</summary>
        public static long DevUnits(long liquidityCents, int devBps) => SeedUnits(liquidityCents) * Math.Max(0, devBps) / 10_000;

        /// <summary>
        /// Progress from the launch price to the moon line (<see cref="FactoryConfig.MoonMultipleBps"/>, 3x: where the creator
        /// fee switches on), 0..1. A player coin starts near $10k market cap and graduation is $5M, so this is the reachable goal.
        /// </summary>
        public static float MoonProgress(long priceMicros, FactoryConfig c)
        {
            c ??= FactoryConfig.Defaults;
            long target = LaunchPriceMicros * c.MoonMultipleBps / 10_000;
            if (target <= LaunchPriceMicros) return priceMicros >= target ? 1f : 0f;
            double f = (double)(priceMicros - LaunchPriceMicros) / (target - LaunchPriceMicros);
            return (float)Math.Max(0, Math.Min(1, f));
        }

        public static FactoryCheck CheckName(string raw, FactoryConfig c)
        {
            c ??= FactoryConfig.Defaults;
            var n = NormaliseName(raw);
            if (n.Length < c.NameMin || n.Length > c.NameMax) return new FactoryCheck("check.nameLength", c.NameMin, c.NameMax);
            if (!NameChars.IsMatch(n)) return new FactoryCheck("check.nameChars");
            return FactoryCheck.Pass;
        }

        public static FactoryCheck CheckTicker(string raw, FactoryConfig c)
        {
            c ??= FactoryConfig.Defaults;
            var t = NormaliseTicker(raw);
            if (t.Length < c.TickerMin || t.Length > c.TickerMax) return new FactoryCheck("check.tickerLength", c.TickerMin, c.TickerMax);
            if (!TickerChars.IsMatch(t)) return new FactoryCheck("check.tickerChars");
            return FactoryCheck.Pass;
        }

        /// <summary>Everything the client can know before sending: shape, art, ranges, Cash, cooldown and the daily cap.</summary>
        /// <param name="cashCents">The Cash balance, or null when unknown (the server decides).</param>
        /// <param name="me">The allowance, or null when unknown.</param>
        public static FactoryCheck Check(LaunchDraft d, FactoryConfig c, long? cashCents, FactoryMe me, double nowMs)
        {
            c ??= FactoryConfig.Defaults;
            if (d == null) return new FactoryCheck("check.nameLength", c.NameMin, c.NameMax);
            var name = CheckName(d.Name, c);
            if (!name.Ok) return name;
            var ticker = CheckTicker(d.Ticker, c);
            if (!ticker.Ok) return ticker;
            if (!c.HasTemplate(d.TemplateId)) return new FactoryCheck("check.art");
            if (d.LiquidityCents < c.MinLiquidityCents || d.LiquidityCents > c.MaxLiquidityCents)
                return new FactoryCheck("check.liquidity", MoneyFormat.Cash(c.MinLiquidityCents), MoneyFormat.Cash(c.MaxLiquidityCents));
            if (d.DevAllocationBps < 0 || d.DevAllocationBps > c.MaxDevAllocationBps)
                return new FactoryCheck("check.dev", Pct(c.MaxDevAllocationBps));
            if (me != null)
            {
                long wait = me.WaitMs(nowMs);
                if (me.Capped && wait > 0) return new FactoryCheck("err.limit", FactoryText.Clock(wait));
                if (me.Capped && me.NextLaunchAtMs == null) return new FactoryCheck("err.limitNoTime");
                if (wait > 0) return new FactoryCheck("err.cooldown", FactoryText.Clock(wait));
            }
            long cost = TotalCostCents(d, c);
            if (cashCents is long cash && cash < cost) return new FactoryCheck("err.cash", MoneyFormat.Cash(cost - cash));
            return FactoryCheck.Pass;
        }

        public static string Pct(int bps) => (bps / 100.0).ToString(bps % 100 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>A Factory failure for the UI: MoneyErrors' classification plus the Factory codes and their details.</summary>
    public sealed class FactoryError
    {
        public MoneyError Money;
        /// <summary>FactoryText key, or null to use the MoneyStrings key in <see cref="Money"/>.</summary>
        public string Key;
        public object[] Args = Array.Empty<object>();
        /// <summary>From LAUNCH_COOLDOWN / LAUNCH_LIMIT_REACHED `details.retryAfterMs`; null when absent.</summary>
        public long? RetryAfterMs;

        public string Code => Money?.Code;
        public bool OutcomeUnknown => Money != null && Money.OutcomeUnknown;
        public bool Is(string code) => Money != null && Money.Is(code);

        public string Message => Key != null ? FactoryText.F(Key, Args) : MoneyStrings.F(Money?.Key ?? "err.generic", Money?.Args ?? Array.Empty<object>());
    }

    public static class FactoryErrors
    {
        public static FactoryError From(Exception e, bool write)
        {
            var money = MoneyErrors.From(e, write);
            var err = new FactoryError { Money = money };
            var d = (e as CTLife.Net.ApiException)?.Details as JObject;
            switch (money.Code)
            {
                case "NAME_NOT_ALLOWED": err.Key = "err.name"; break;
                case "TICKER_TAKEN": err.Key = "err.taken"; break;
                case "LAUNCH_COOLDOWN":
                    err.RetryAfterMs = Wire.OptLong(d?["retryAfterMs"]);
                    err.Key = "err.cooldown";
                    err.Args = new object[] { err.RetryAfterMs is long c ? FactoryText.Clock(c) : "—" };
                    break;
                case "LAUNCH_LIMIT_REACHED":
                    // backend #14 computes this from the LAST launch; /factory/me nextLaunchAt (the oldest one + 24 h) is the
                    // better clock, so the service refreshes /factory/me after this error.
                    err.RetryAfterMs = Wire.OptLong(d?["retryAfterMs"]);
                    err.Key = err.RetryAfterMs is long l && l > 0 ? "err.limit" : "err.limitNoTime";
                    err.Args = new object[] { err.RetryAfterMs is long l2 ? FactoryText.Clock(l2) : "—" };
                    break;
                case "INSUFFICIENT_CASH":
                {
                    // the factory sends {have, need} (cents as strings), not shortfallCents
                    long have = ParseCents(d?["have"]), need = ParseCents(d?["need"]);
                    err.Key = "err.cash";
                    err.Args = new object[] { have >= 0 && need > have ? MoneyFormat.Cash(need - have) : "—" };
                    break;
                }
                case "JAILED": err.Key = "err.jailed"; break;
                case "VALIDATION_FAILED": err.Key = "err.validation"; break;
            }
            return err;
        }

        private static long ParseCents(JToken t)
        {
            try { return Wire.IsNull(t) ? -1 : Wire.Long(t); }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException) { return -1; }
        }
    }
}
