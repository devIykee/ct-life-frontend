using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    /// <summary>
    /// The Rugpull Factory endpoints (backend #14). Transport policy (auth refresh, retries that reuse the Idempotency-Key)
    /// is RestClient's. Buying and selling a player coin is the ordinary `POST /market/trades` (MoneyApi.Trade).
    /// </summary>
    public sealed class FactoryApi
    {
        public static readonly string[] Views = { "trending", "new", "mine" };
        private readonly IRestClient _rest;

        public FactoryApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        public async Task<FactoryConfig> Config(CancellationToken ct) => FactoryParse.Config(await _rest.Get<JToken>("/factory/config", ct));

        public async Task<FactoryMe> Me(CancellationToken ct) => FactoryParse.Me(await _rest.Get<JToken>("/factory/me", ct));

        /// <summary>GET /factory/coins?view=trending|new|mine&amp;limit=1..50.</summary>
        public async Task<List<FactoryCoin>> Coins(string view, int limit, CancellationToken ct)
        {
            if (Array.IndexOf(Views, view) < 0) throw new ArgumentException("Unknown factory view", nameof(view));
            int n = Math.Max(1, Math.Min(50, limit));
            return FactoryParse.Coins(await _rest.Get<JToken>("/factory/coins?view=" + view + "&limit=" + n, ct), out _);
        }

        public async Task<FactoryCoinDetail> Coin(string coinId, CancellationToken ct)
        {
            if (!Wire.IsSafeId(coinId)) throw new ArgumentException("Unsafe coin id");
            return FactoryParse.Detail(await _rest.Get<JToken>("/factory/coins/" + Uri.EscapeDataString(coinId), ct));
        }

        /// <summary>POST /factory/coins (201 with the coin). The caller retains <paramref name="key"/> until a definitive answer.</summary>
        public async Task<FactoryCoin> Launch(JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return FactoryParse.Coin(await _rest.Post<JToken>("/factory/coins", RetainedWrite.Body(body), idempotencyKey: key, ct: ct));
        }
    }
}
