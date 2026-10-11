using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    /// <summary>
    /// Thin HTTP wrapper for the Phase 2 endpoints (contract section 4: Markets, Rekt, Payments, ORE). Transport policy
    /// (auth refresh, version headers, 429/503 handling, network retries that reuse the Idempotency-Key) is RestClient's.
    /// Every write takes the caller's retained Idempotency-Key (see RetainedWrite). Responses are parsed by MoneyParse.
    /// </summary>
    public sealed class MoneyApi : ITopUpGateway
    {
        private readonly IRestClient _rest;

        public MoneyApi(IRestClient rest)
        {
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
        }

        private static string Seg(string id)
        {
            if (!Wire.IsSafeId(id)) throw new ArgumentException("Unsafe path id");
            return Uri.EscapeDataString(id);
        }

        private Task<JToken> Get(string path, CancellationToken ct) => _rest.Get<JToken>(path, ct);

        private Task<JToken> Post(string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Post<JToken>(path, RetainedWrite.Body(body ?? new JObject()), idempotencyKey: key, ct: ct);
        }

        // ---------------------------------------------------------------- markets (Ph 2)

        /// <summary>GET /market/coins?stage=new|final|graduated (the backend rejects `dead`).</summary>
        public async Task<CoinPage> Coins(string stage, string cursor, CancellationToken ct)
        {
            var q = "/market/coins";
            var sep = '?';
            if (!string.IsNullOrEmpty(stage)) { q += sep + "stage=" + Uri.EscapeDataString(stage); sep = '&'; }
            if (!string.IsNullOrEmpty(cursor)) q += sep + "cursor=" + Uri.EscapeDataString(cursor);
            return MoneyParse.Coins(await Get(q, ct));
        }

        public async Task<CoinDetail> Coin(string coinId, CancellationToken ct) => MoneyParse.Detail(await Get("/market/coins/" + Seg(coinId), ct));

        public async Task<TradeReceipt> Trade(JObject body, string key, CancellationToken ct) => MoneyParse.Trade(await Post("/market/trades", body, key, ct));

        public async Task<Rows<PositionRow>> Positions(CancellationToken ct) => MoneyParse.Positions(await Get("/market/positions", ct));

        /// <summary>GET /perps?status=open|closed.</summary>
        public async Task<Rows<PerpRow>> Perps(string status, CancellationToken ct) =>
            MoneyParse.Perps(await Get(string.IsNullOrEmpty(status) ? "/perps" : "/perps?status=" + Uri.EscapeDataString(status), ct));

        public async Task<PerpRow> OpenPerp(JObject body, string key, CancellationToken ct) => MoneyParse.Perp(await Post("/perps", body, key, ct));

        public async Task<PerpRow> ClosePerp(string perpId, string key, CancellationToken ct) =>
            MoneyParse.Perp(await Post("/perps/" + Seg(perpId) + "/close", null, key, ct));

        public async Task<Rows<PredictionRow>> Predictions(CancellationToken ct) => MoneyParse.Predictions(await Get("/predictions?status=open", ct));

        public async Task<BetReceipt> Bet(string predictionId, JObject body, string key, CancellationToken ct) =>
            MoneyParse.Bet(await Post("/predictions/" + Seg(predictionId) + "/bets", body, key, ct));

        /// <summary>GET /alpha-calls?userId= (omit for the signed-in player).</summary>
        public async Task<AlphaCallsResult> AlphaCalls(string userId, CancellationToken ct) =>
            MoneyParse.AlphaCalls(await Get(string.IsNullOrEmpty(userId) ? "/alpha-calls" : "/alpha-calls?userId=" + Seg(userId), ct));

        public async Task<AlphaCallRow> PostAlphaCall(JObject body, string key, CancellationToken ct) => MoneyParse.AlphaCall(await Post("/alpha-calls", body, key, ct));

        public async Task<Rows<SmartMoneyRow>> SmartMoney(CancellationToken ct) => MoneyParse.SmartMoney(await Get("/smart-money", ct));

        public async Task<Rows<TombstoneRow>> Tombstones(int limit, CancellationToken ct) =>
            MoneyParse.Tombstones(await Get("/rekt/tombstones?limit=" + Math.Max(1, Math.Min(50, limit)), ct));

        // ---------------------------------------------------------------- payments (Ph 2)

        public async Task<PaymentIntentRow> CreateIntent(string kind, string sku, string currency, string idempotencyKey, CancellationToken ct) =>
            MoneyParse.Intent(await Post("/payments/intents", new JObject { ["kind"] = kind, ["sku"] = sku, ["currency"] = currency }, idempotencyKey, ct));

        public async Task<IntentStatusRow> GetIntent(string intentId, CancellationToken ct) =>
            MoneyParse.IntentStatus(await Get("/payments/intents/" + Seg(intentId), ct));

        // ---------------------------------------------------------------- ORE (Ph 2, mainnet)

        public async Task<OreBoardRow> OreBoard(CancellationToken ct) => MoneyParse.OreBoard(await Get("/ore/board", ct));

        public async Task<OreMeRow> OreMe(CancellationToken ct) => MoneyParse.OreMe(await Get("/ore/me", ct));

        /// <summary>POST /ore/stake/tx → an unsigned base64 v0 transaction for MWA. Building it moves nothing; signing does.</summary>
        public async Task<string> StakeTx(JObject body, string key, CancellationToken ct) => MoneyParse.StakeTx(await Post("/ore/stake/tx", body, key, ct));
    }

    /// <summary>Static facts about the endpoint set, for the progress log and the in-app diagnostics line.</summary>
    public static class MoneyEndpoints
    {
        public static readonly IReadOnlyList<string> Covered = new[]
        {
            "GET /market/coins", "GET /market/coins/{coinId}", "POST /market/trades", "GET /market/positions",
            "GET /perps", "POST /perps", "POST /perps/{perpId}/close", "GET /predictions", "POST /predictions/{id}/bets",
            "GET /alpha-calls", "POST /alpha-calls", "GET /smart-money", "GET /rekt/tombstones",
            "POST /payments/intents", "GET /payments/intents/{id}", "GET /ore/board", "GET /ore/me", "POST /ore/stake/tx",
        };
    }
}
