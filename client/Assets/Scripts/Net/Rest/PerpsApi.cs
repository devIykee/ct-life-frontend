using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    // Opt-in transport: one HTTP attempt, no automatic replay, including after 401/429/503.
    // Existing IRestClient consumers retain their existing policy.
    public interface IUnreplayedRestClient
    {
        Task<T> PostOnce<T>(string path, object body, string idempotencyKey, CancellationToken ct = default);
    }

    public sealed class PerpsApi
    {
        private readonly IRestClient _rest;
        public PerpsApi(IRestClient rest) { _rest = rest; }
        public bool CanSendOnce => _rest is IUnreplayedRestClient;
        public async Task<List<PerpRecord>> Positions(CancellationToken ct) => PerpsWire.Positions(await _rest.Get<JToken>("/perps", ct));
        public async Task<PerpCoins> Coins(string cursor, CancellationToken ct) => PerpsWire.Coins(await _rest.Get<JToken>(
            "/market/coins" + (string.IsNullOrEmpty(cursor) ? "" : "?cursor=" + Uri.EscapeDataString(cursor)), ct));
        public async Task<PerpMarket> Market(Guid coin, CancellationToken ct) => PerpsWire.Market(await _rest.Get<JToken>("/market/coins/" + coin, ct));
        public Task<JToken> Rules(CancellationToken ct) => _rest.Get<JToken>("/config", ct);
        public Task<JToken> Me(CancellationToken ct) => _rest.Get<JToken>("/me", ct);
        public async Task<PerpRecord> Execute(PerpIntent intent, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!intent.Valid) throw new ArgumentException("Invalid retained intent");
            if (!(_rest is IUnreplayedRestClient single)) throw new InvalidOperationException("Single-attempt transport unavailable");
            var response = await single.PostOnce<JToken>(intent.Path, intent.Body, intent.Key, ct);
            var record = PerpsWire.Position(response);
            // A success with an unrelated/malformed receipt remains uncertain; never silently accept it.
            if (!intent.Matches(record)) throw new FormatException("Unrelated position receipt");
            return record;
        }
    }

    public sealed class PerpRecord
    {
        public Guid Id, CoinId;
        public string Side, Status, Ticker;
        public int Leverage;
        public long Margin, Entry, Liquidation;
        public long? Realized;
        public DateTimeOffset? ClosedAt;
    }
    public sealed class PerpCoin
    {
        public Guid Id;
        public string Ticker, Name, Stage;
        public long Price;
        public int ChangeBps;
    }
    public sealed class PerpCoins
    {
        public List<PerpCoin> Items;
        public string Next;
    }
    public readonly struct PerpPoint
    {
        public readonly long At, Price;
        public PerpPoint(long at, long price) { At = at; Price = price; }
    }
    public sealed class PerpMarket
    {
        public PerpCoin Coin;
        public List<PerpPoint> Points;
    }
    public sealed class PerpAccount
    {
        public Guid User;
        public long Cash;
        public int? Cap;
    }

    /// <summary>Strict integer parsing, tolerant only of explicitly observed rc1 wire-shape differences.</summary>
    public static class PerpsWire
    {
        public static long Integer(JToken token)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.String) ||
                !long.TryParse(token.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                throw new FormatException("Expected signed integer");
            return value;
        }
        private static long Nonnegative(JToken token) { var n = Integer(token); if (n < 0) throw new FormatException("Negative amount"); return n; }
        private static Guid Id(JToken token) { if (!Guid.TryParse((string)token, out var id) || id == Guid.Empty) throw new FormatException("Invalid ID"); return id; }
        public static string Plain(JToken token, int limit = 64)
        {
            var text = token?.Type == JTokenType.String ? (string)token : "";
            return new string((text ?? "").Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(limit).ToArray());
        }
        public static PerpRecord Position(JToken p)
        {
            if (!(p is JObject)) throw new FormatException("Missing position");
            var side = (string)p["side"]; var status = (string)p["status"];
            if ((side != "long" && side != "short") || (status != "open" && status != "closed" && status != "liquidated")) throw new FormatException("Unknown position state");
            var leverage = checked((int)Integer(p["leverage"]));
            if (leverage < 1 || leverage > 25) throw new FormatException("Invalid leverage"); // Read observed backend range; orders stay within contract 20.
            DateTimeOffset? closed = null;
            if (p["closedAt"]?.Type == JTokenType.String)
            {
                if (!DateTimeOffset.TryParse((string)p["closedAt"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)) throw new FormatException("Invalid close date");
                closed = value;
            }
            return new PerpRecord { Id = Id(p["perpId"]), CoinId = Id(p["coinId"]), Side = side, Status = status,
                Ticker = Plain(p["ticker"]), Leverage = leverage, Margin = Nonnegative(p["marginCents"]), Entry = Nonnegative(p["entryMicros"]),
                Liquidation = Nonnegative(p["liqMicros"]), Realized = p["realizedCents"] == null || p["realizedCents"].Type == JTokenType.Null ? (long?)null : Integer(p["realizedCents"]), ClosedAt = closed };
        }
        public static List<PerpRecord> Positions(JToken json)
        {
            var array = json as JArray ?? json?["perps"] as JArray;
            if (array == null) throw new FormatException("Missing positions array");
            var rows = array.Select(Position).ToList();
            if (rows.Select(x => x.Id).Distinct().Count() != rows.Count) throw new FormatException("Duplicate position ID");
            return rows;
        }
        public static PerpCoin Coin(JToken p)
        {
            if (!(p is JObject)) throw new FormatException("Missing coin");
            var stage = (string)p["stage"];
            if (stage != "new" && stage != "final" && stage != "graduated" && stage != "dead") throw new FormatException("Unknown coin stage");
            var ticker = Plain(p["ticker"], 24);
            if (string.IsNullOrWhiteSpace(ticker)) throw new FormatException("Missing ticker");
            return new PerpCoin { Id = Id(p["coinId"]), Ticker = ticker, Name = Plain(p["name"]), Stage = stage,
                Price = Nonnegative(p["priceMicros"]), ChangeBps = checked((int)Integer(p["change5mBps"])) };
        }
        public static PerpCoins Coins(JToken json)
        {
            if (!(json?["coins"] is JArray array)) throw new FormatException("Missing coins array");
            return new PerpCoins { Items = array.Select(Coin).GroupBy(x => x.Id).Select(g => g.First()).ToList(), Next = (string)json["nextCursor"] };
        }
        public static PerpMarket Market(JToken json)
        {
            var coin = Coin(json?["coin"]);
            if (!(json?["candles1m"] is JArray array)) throw new FormatException("Missing history array");
            var points = new List<PerpPoint>();
            foreach (var point in array)
            {
                long at, price;
                if (point["priceMicros"] != null) { at = Integer(point["at"]); price = Nonnegative(point["priceMicros"]); }
                else
                {
                    if (!DateTimeOffset.TryParse((string)point["openTime"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)) throw new FormatException("Invalid candle date");
                    at = time.ToUnixTimeMilliseconds(); price = Nonnegative(point["close"]);
                }
                if (at < 0 || at > 253402300799999L) throw new FormatException("Invalid history time");
                points.Add(new PerpPoint(at, price));
            }
            return new PerpMarket { Coin = coin, Points = points.GroupBy(p => p.At).Select(g => g.Last()).OrderBy(p => p.At).TakeLast(180).ToList() };
        }
        public static PerpAccount Account(JToken me, JToken config)
        {
            var account = new PerpAccount { User = Id(me?["user"]?["id"]), Cash = Nonnegative(me?["cashCents"]) };
            var skills = config?["config"]?["skills"] ?? config?["skills"];
            var level = me?["skills"]?["trading"];
            var tribe = (string)me?["character"]?["tribeId"];
            if (!(skills?["leverageByTradingLevel"] is JArray caps) || level == null) return account;
            var capRow = caps.FirstOrDefault(x => Integer(x["level"]) == Integer(level));
            if (capRow == null || !(skills["tribeBonusLeverage"] is JObject bonuses)) return account;
            var bonus = tribe == null || bonuses[tribe] == null ? 0 : Nonnegative(bonuses[tribe]);
            var cap = checked(Nonnegative(capRow["leverage"]) + bonus);
            if (cap > 0) account.Cap = (int)Math.Min(20L, cap);
            return account;
        }
    }

    /// <summary>Persisted intent contains only game IDs and fictional amounts, never tokens or wallet data.</summary>
    public sealed class PerpIntent
    {
        public string Key, Kind, Side;
        public Guid Coin, Position;
        public long Margin;
        public int Leverage;
        public const long MaxExactRequestCents = 9007199254740991L; // Backend accepts JavaScript numbers; no lossy numeric body.
        public static bool IsRequestKey(string text)
        {
            if (!Guid.TryParse(text, out var key)) return false;
            var value = key.ToString("D"); return value[14] == '4' && "89ab".Contains(value[19]);
        }
        public bool Valid => IsRequestKey(Key) &&
            ((Kind == "open" && Coin != Guid.Empty && (Side == "long" || Side == "short") && Margin > 0 && Margin <= MaxExactRequestCents && Leverage >= 1 && Leverage <= 20) ||
             (Kind == "close" && Position != Guid.Empty));
        public string Path => Kind == "close" ? "/perps/" + Position + "/close" : "/perps";
        public JObject Body => Kind == "close" ? null : new JObject { ["coinId"] = Coin.ToString(), ["side"] = Side, ["marginCents"] = Margin, ["leverage"] = Leverage };
        public bool Matches(PerpRecord p) => p != null && (Kind == "close" ? p.Id == Position && p.Status != "open" :
            p.CoinId == Coin && p.Side == Side && p.Margin == Margin && p.Leverage == Leverage);
        public PerpRecord Reconcile(IEnumerable<PerpRecord> list) => Kind != "close" ? null : list.FirstOrDefault(p => p.Id == Position && p.Status != "open");
        public string Serialize() => new JObject { ["key"] = Key, ["kind"] = Kind, ["coin"] = Coin.ToString(), ["position"] = Position.ToString(),
            ["side"] = Side, ["margin"] = Margin, ["leverage"] = Leverage }.ToString(Newtonsoft.Json.Formatting.None);
        public static PerpIntent Parse(string text)
        {
            var json = JObject.Parse(text);
            var result = new PerpIntent { Key = (string)json["key"], Kind = (string)json["kind"], Side = (string)json["side"],
                Coin = Guid.Parse((string)json["coin"]), Position = Guid.Parse((string)json["position"]), Margin = PerpsWire.Integer(json["margin"]), Leverage = checked((int)PerpsWire.Integer(json["leverage"])) };
            if (!result.Valid) throw new FormatException("Invalid journal");
            return result;
        }
        public static bool TryMargin(string text, out long cents)
        {
            cents = 0;
            // Locale-independent currency field: label explicitly gives decimal-point input convention.
            if (string.IsNullOrWhiteSpace(text) || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ||
                amount <= 0 || amount > MaxExactRequestCents / 100m || decimal.Truncate(amount * 100m) != amount * 100m) return false;
            cents = (long)(amount * 100m); return true;
        }
    }

    public static class PerpsRequest
    {
        public static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> start, CancellationToken life, int milliseconds = 20000)
        {
            life.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(life);
            var operation = start(timeout.Token);
            var deadline = Task.Delay(milliseconds, life);
            if (await Task.WhenAny(operation, deadline) != operation)
            {
                timeout.Cancel(); Observe(operation); life.ThrowIfCancellationRequested(); throw new TimeoutException();
            }
            life.ThrowIfCancellationRequested(); return await operation;
        }
        private static async void Observe(Task operation) { try { await operation; } catch { /* Fault observed after owner left. */ } }
    }
}
