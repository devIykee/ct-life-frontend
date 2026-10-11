using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    /// <summary>`multipart/form-data` upload with the signed-in session (IRestClient has no multipart verb).</summary>
    public interface IMultipartClient
    {
        Task<JToken> PostFile(string path, string field, string fileName, string mime, byte[] data, string idempotencyKey, CancellationToken ct);
    }

    /// <summary>
    /// The ad endpoints over REST: rc1 (`GET /ads/slots`, `POST /uploads`, `POST /ads/bookings`) and the Phase 5 proposal
    /// (`GET /ads/bookings`, `GET /ads/bookings/{id}`, `POST /ads/bookings/{id}/cancel`, `GET /ads/slots/{slotId}/availability`).
    /// Transport policy (auth refresh, version headers, 429/503, retries with the same Idempotency-Key) is RestClient's.
    /// A missing route answers 404/501, which <see cref="VErrors"/> reports as UNAVAILABLE.
    /// </summary>
    public sealed class AdsApi : IAdsGateway
    {
        public const int PageSize = 20;
        private readonly IRestClient _rest;
        private readonly IMultipartClient _files;

        public AdsApi(IRestClient rest, IMultipartClient files)
        {
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _files = files;
        }

        /// <summary>Operations this lane calls (for the progress log and the contract check).</summary>
        public static readonly string[] Routes =
        {
            "GET /ads/slots", "POST /uploads", "POST /ads/bookings", "GET /ads/bookings", "GET /ads/bookings/{id}",
            "POST /ads/bookings/{id}/cancel", "GET /ads/slots/{slotId}/availability", "GET /payments/intents/{id}",
        };

        public async Task<AdSlotList> Slots(CancellationToken ct)
        {
            var list = new AdSlotList();
            list.Items = AdParse.Slots(await _rest.Get<JToken>("/ads/slots", ct), out list.Skipped);
            return list;
        }

        public async Task<AdAvailability> Availability(string slotId, CancellationToken ct) =>
            AdParse.Availability(await _rest.Get<JToken>("/ads/slots/" + VWire.Seg(slotId) + "/availability", ct));

        public async Task<UploadedCreative> Upload(byte[] data, string mime, string fileName, string key, CancellationToken ct)
        {
            if (_files == null) throw new InvalidOperationException("No multipart client");
            var name = string.IsNullOrEmpty(fileName) ? "creative" + (mime == "image/png" ? ".png" : ".jpg") : fileName;
            return AdParse.Upload(await _files.PostFile("/uploads", "file", name, mime, data, key, ct));
        }

        public async Task<BookingResult> Book(JObject body, string key, CancellationToken ct) =>
            AdParse.BookingResult(await _rest.Send<JToken>("POST", "/ads/bookings", body.ToString(Formatting.None), true, key, ct));

        public async Task<AdPage> Bookings(string cursor, CancellationToken ct)
        {
            var q = "?limit=" + PageSize + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            return AdParse.Page(await _rest.Get<JToken>("/ads/bookings" + q, ct));
        }

        public async Task<AdBooking> Booking(string id, CancellationToken ct) =>
            AdParse.Booking(await _rest.Get<JToken>("/ads/bookings/" + VWire.Seg(id), ct));

        public async Task<AdBooking> Cancel(string id, string key, CancellationToken ct) =>
            AdParse.Booking(await _rest.Send<JToken>("POST", "/ads/bookings/" + VWire.Seg(id) + "/cancel", "{}", true, key, ct));
    }
}
