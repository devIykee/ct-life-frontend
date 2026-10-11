using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife
{
    /// <summary>
    /// The two reads the live surfaces need. Transport policy (auth refresh, version headers, 429/503) is RestClient's.
    /// A 404/405/501 means the server does not serve the route (backend 9f3e5ef has `/v1/ads/live` and `/v1/feed`); the
    /// caller then stops asking for this session and shows house content.
    /// </summary>
    public sealed class WorldLifeApi
    {
        private readonly IRestClient _rest;
        private string _adsEtag;
        private JToken _adsBody;

        public WorldLifeApi(IRestClient rest) { _rest = rest; }

        public static bool IsUnsupported(Exception e) => e is ApiException a && (a.Status == 404 || a.Status == 405 || a.Status == 501);

        /// <summary>GET /ads/live with If-None-Match (contract: Cache-Control 5 min + ETag).</summary>
        public async Task<JToken> LiveAds(CancellationToken ct)
        {
            if (_rest == null) throw new InvalidOperationException("No REST client");
            var res = await _rest.SendRaw("GET", "/ads/live", null, true, null, _adsEtag, ct);
            if (res.status == 304 && _adsBody != null) return _adsBody;
            var body = string.IsNullOrEmpty(res.body) ? new JObject() : JToken.Parse(res.body);
            _adsBody = body;
            _adsEtag = res.etag;
            return body;
        }

        /// <summary>GET /feed?scope=global (the in-game Timeline, never X).</summary>
        public Task<JToken> GlobalFeed(CancellationToken ct)
        {
            if (_rest == null) throw new InvalidOperationException("No REST client");
            return _rest.Get<JToken>("/feed?scope=global", ct);
        }
    }
}
