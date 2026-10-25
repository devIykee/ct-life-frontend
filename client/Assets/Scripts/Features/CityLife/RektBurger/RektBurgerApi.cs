using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>Rekt Burger routes (backend PR #16). Writes carry the caller's retained Idempotency-Key; no amount is ever sent.</summary>
    public sealed class RektBurgerApi
    {
        private readonly IRestClient _rest;
        public RektBurgerApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        public static readonly string[] Routes =
        {
            "GET /jobs/rekt-burger", "POST /jobs/rekt-burger/leave",
        };

        private Task<JToken> Write(string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Send<JToken>("POST", path, (body ?? new JObject()).ToString(Formatting.None), true, key, ct);
        }

        public async Task<RbState> State(CancellationToken ct) => RbState.Parse(await _rest.Get<JToken>("/jobs/rekt-burger", ct));

        /// <summary>Hang up the apron: (glowUp, postId).</summary>
        public async Task<(bool glowUp, string postId)> Leave(string key, CancellationToken ct)
        {
            var r = await Write("/jobs/rekt-burger/leave", null, key, ct);
            return (CityWire.BoolOr(r?["glowUp"], false), CityWire.Str(r?["postId"]));
        }
    }
}
