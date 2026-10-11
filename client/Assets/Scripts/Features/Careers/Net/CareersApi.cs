using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Thin HTTP wrapper for the work endpoints (contract section 4, "Work"): GET /careers, POST /careers/{id}/apply,
    /// POST /shifts, POST /shifts/{id}/complete, GET /gigs, POST /gigs/{id}/run. Transport policy (auth refresh,
    /// version headers, 429/503 handling, network retries that reuse the Idempotency-Key) is RestClient's. Every write
    /// takes the caller's key so retries after an unknown outcome replay instead of paying twice. Bodies are always
    /// JSON objects (`{}` when empty): a bodiless POST is a 500 on the backend checkout (feedback item 50).
    /// </summary>
    public sealed class CareersApi
    {
        private readonly IRestClient _rest;

        public CareersApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        private static string Seg(string id)
        {
            if (!CareersWire.IsSafeId(id)) throw new ArgumentException("Unsafe path id");
            return Uri.EscapeDataString(id);
        }

        private Task<JToken> Post(string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("An Idempotency-Key is required", nameof(key));
            return _rest.Post<JToken>(path, (body ?? new JObject()).ToString(Formatting.None), idempotencyKey: key, ct: ct);
        }

        public async Task<List<CareerRow>> Careers(CancellationToken ct)
        {
            var rows = CareerRow.ParseList(await _rest.Get<JToken>("/careers", ct), out int malformed);
            if (malformed > 0) CTLife.Core.Log.Warn("careers", "GET /careers: " + malformed + " malformed rows skipped");
            return rows;
        }

        public async Task<JobGrant> Apply(string careerId, string key, CancellationToken ct)
        {
            var grant = JobGrant.Parse(await Post("/careers/" + Seg(careerId) + "/apply", null, key, ct), careerId);
            if (grant == null) throw new InvalidOperationException("apply.invalid");
            return grant;
        }

        public async Task<ShiftTicket> StartShift(string careerId, string key, CancellationToken ct) =>
            ShiftTicket.Parse(await Post("/shifts", new JObject { ["careerId"] = careerId }, key, ct));

        public async Task<ShiftReceipt> CompleteShift(string shiftId, JObject body, string key, CancellationToken ct)
        {
            var receipt = ShiftReceipt.Parse(await Post("/shifts/" + Seg(shiftId) + "/complete", body, key, ct));
            if (receipt == null) throw new InvalidOperationException("shift.invalidReceipt");
            return receipt;
        }

        public async Task<List<GigRow>> Gigs(CancellationToken ct)
        {
            var rows = GigRow.ParseList(await _rest.Get<JToken>("/gigs", ct), out int malformed);
            if (malformed > 0) CTLife.Core.Log.Warn("careers", "GET /gigs: " + malformed + " malformed rows skipped");
            return rows;
        }

        public async Task<GigReceipt> RunGig(string gigId, string key, CancellationToken ct)
        {
            var receipt = GigReceipt.Parse(await Post("/gigs/" + Seg(gigId) + "/run", null, key, ct), gigId);
            if (receipt == null) throw new InvalidOperationException("gig.invalidReceipt");
            return receipt;
        }
    }
}
