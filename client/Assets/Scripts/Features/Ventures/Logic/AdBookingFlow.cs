using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Economy;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    public sealed class AdSlotList
    {
        public List<AdSlot> Items = new List<AdSlot>();
        public int Skipped;
    }

    /// <summary>The ad endpoints (contract rc1 + the Phase 5 proposal). Implemented over REST by <c>AdsApi</c>; tests use a fake.</summary>
    public interface IAdsGateway
    {
        Task<AdSlotList> Slots(CancellationToken ct);
        Task<AdAvailability> Availability(string slotId, CancellationToken ct);
        Task<UploadedCreative> Upload(byte[] data, string mime, string fileName, string key, CancellationToken ct);
        Task<BookingResult> Book(JObject body, string key, CancellationToken ct);
        Task<AdPage> Bookings(string cursor, CancellationToken ct);
        Task<AdBooking> Booking(string id, CancellationToken ct);
        Task<AdBooking> Cancel(string id, string key, CancellationToken ct);
    }

    public enum BookStage { Compose, Uploading, Booking, Booked, Failed }

    public enum BookProblem { NoSlot, NoTerm, UnknownTerm, NoCreative, CreativeInvalid, BadLink, BadStart, StartInPast, StartOverlaps, StartNotSupported }

    /// <summary>
    /// The ad booking form as a state machine: Compose → Uploading (POST /uploads) → Booking (POST /ads/bookings) → Booked (a
    /// payment intent to hand to the Money lane's flow). Each write has its own retained Idempotency-Key, so a retry after an
    /// unknown outcome replays instead of uploading or booking twice; an upload that succeeded is not repeated when only the
    /// booking failed. No price is computed here: the intent from the server is the amount.
    /// </summary>
    public sealed class AdBookingFlow
    {
        private readonly IAdsGateway _gw;
        private readonly RetainedKey _uploadKey, _bookKey;
        private string _uploadedFingerprint;

        public BookStage Stage { get; private set; } = BookStage.Compose;
        public VError Error { get; private set; }
        public BookingResult Result { get; private set; }
        public UploadedCreative Uploaded { get; private set; }

        public string SlotId { get; private set; }
        public string TermId { get; set; }
        public string Link { get; set; } = "";
        /// <summary>"YYYY-MM-DD" (UTC midnight) or empty for the next free window.</summary>
        public string StartText { get; set; } = "";
        public AdAvailability Availability { get; private set; }
        public byte[] CreativeBytes { get; private set; }
        public CreativeResult Creative { get; private set; }
        /// <summary>The last attempt failed because the window was taken; re-read availability.</summary>
        public bool NeedsAvailabilityRefresh { get; private set; }

        public event Action Changed;

        public AdBookingFlow(IAdsGateway gateway, Func<string> newKey = null)
        {
            _gw = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _uploadKey = new RetainedKey(newKey);
            _bookKey = new RetainedKey(newKey);
        }

        public bool Busy => Stage == BookStage.Uploading || Stage == BookStage.Booking;

        public void SelectSlot(AdAvailability availability)
        {
            if (Busy) return;
            if (SlotId != availability?.SlotId) { TermId = null; StartText = ""; }
            SlotId = availability?.SlotId;
            Availability = availability;
            if (TermId != null && Term() == null) TermId = null;
            if (TermId == null && availability != null && availability.Terms.Count == 1) TermId = availability.Terms[0].TermId;
            Fail(null);
            Stage = BookStage.Compose;
            Changed?.Invoke();
        }

        public void RefreshAvailability(AdAvailability availability)
        {
            if (Busy || availability == null || availability.SlotId != SlotId) return;
            Availability = availability;
            NeedsAvailabilityRefresh = false;
            Changed?.Invoke();
        }

        /// <summary>The caller started the availability re-read the last failure asked for (so a failing re-read cannot loop).</summary>
        public void AckAvailabilityRefresh() => NeedsAvailabilityRefresh = false;

        public CreativeResult SetCreative(byte[] bytes, string fileName, CreativeLimits limits)
        {
            if (Busy) return Creative;
            CreativeBytes = bytes;
            Creative = CreativeCheck.Check(bytes, fileName, limits, Availability?.CreativeWidth ?? 0, Availability?.CreativeHeight ?? 0);
            if (!Creative.Ok) CreativeBytes = null;
            Changed?.Invoke();
            return Creative;
        }

        public void ClearCreative()
        {
            if (Busy) return;
            CreativeBytes = null;
            Creative = null;
            Changed?.Invoke();
        }

        public AdTerm Term() => Availability?.Terms.Find(t => t.TermId == TermId);

        /// <summary>Parses "YYYY-MM-DD" as UTC midnight. Null for an empty text; <paramref name="ok"/> false for garbage.</summary>
        public static long? ParseStart(string text, out bool ok)
        {
            ok = true;
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
                return new DateTimeOffset(d, TimeSpan.Zero).ToUnixTimeMilliseconds();
            ok = false;
            return null;
        }

        public static bool Overlaps(IEnumerable<AdWindow> booked, long startMs, long endMs) =>
            booked.Any(w => startMs < w.EndsAtMs && endMs > w.StartsAtMs);

        public List<BookProblem> Problems(double nowMs)
        {
            var p = new List<BookProblem>();
            if (SlotId == null || Availability == null) { p.Add(BookProblem.NoSlot); return p; }
            if (TermId == null) p.Add(BookProblem.NoTerm);
            else if (Term() == null) p.Add(BookProblem.UnknownTerm);
            if (Creative == null || CreativeBytes == null) p.Add(BookProblem.NoCreative);
            else if (!Creative.Ok) p.Add(BookProblem.CreativeInvalid);
            if (!CreativeCheck.IsAllowedLink(Link ?? "")) p.Add(BookProblem.BadLink);
            var start = ParseStart(StartText, out var ok);
            if (!ok) p.Add(BookProblem.BadStart);
            else if (start.HasValue)
            {
                var term = Term();
                if (term != null && term.Days <= 0) p.Add(BookProblem.StartNotSupported);
                else
                {
                    if (start.Value < nowMs - 86_400_000.0) p.Add(BookProblem.StartInPast);
                    if (term != null && Overlaps(Availability.Booked, start.Value, start.Value + term.Days * 86_400_000L)) p.Add(BookProblem.StartOverlaps);
                }
            }
            return p;
        }

        public bool CanSubmit(double nowMs) => !Busy && Stage != BookStage.Booked && Problems(nowMs).Count == 0;

        /// <summary>Request body for POST /ads/bookings (proposal adds the optional `startsAt`).</summary>
        public JObject BookingBody()
        {
            var b = new JObject { ["slotId"] = SlotId, ["termId"] = TermId, ["uploadId"] = Uploaded?.UploadId };
            // The current server upload supplies storage metadata required by bookAd.
            // Echo only server-returned metadata; never invent an object key or image URL.
            if (!string.IsNullOrEmpty(Uploaded?.ImageKey))
            {
                b["imageKey"] = Uploaded.ImageKey;
                b["mimeType"] = Uploaded.MimeType;
                b["sizeBytes"] = Uploaded.SizeBytes;
            }
            if (!string.IsNullOrEmpty(Link)) b["link"] = Link;
            var start = ParseStart(StartText, out var ok);
            if (ok && start.HasValue)
                b["startsAt"] = DateTimeOffset.FromUnixTimeMilliseconds(start.Value).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            return b;
        }

        public async Task Submit(double nowMs, CancellationToken ct)
        {
            if (!CanSubmit(nowMs)) return;
            Fail(null);
            NeedsAvailabilityRefresh = false;
            var print = CreativeCheck.Fingerprint(CreativeBytes);
            if (Uploaded == null || _uploadedFingerprint != print)
            {
                Stage = BookStage.Uploading;
                Changed?.Invoke();
                try
                {
                    var key = _uploadKey.KeyFor("POST", "/uploads", print);
                    Uploaded = await _gw.Upload(CreativeBytes, Creative.Mime, Creative.FileName, key, ct);
                    if (Uploaded == null || string.IsNullOrEmpty(Uploaded.UploadId)) throw new FormatException("Missing uploadId");
                    _uploadedFingerprint = print;
                    _uploadKey.Complete();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    Stage = BookStage.Compose;
                    Changed?.Invoke();
                    throw;
                }
                catch (Exception e)
                {
                    Uploaded = null;
                    Fail(e);
                    _uploadKey.Settle(Error);
                    Stage = BookStage.Failed;
                    Changed?.Invoke();
                    return;
                }
            }
            Stage = BookStage.Booking;
            Changed?.Invoke();
            var body = BookingBody();
            try
            {
                var key = _bookKey.KeyFor("POST", "/ads/bookings", body.ToString(Formatting.None));
                Result = await _gw.Book(body, key, ct);
                if (Result == null || string.IsNullOrEmpty(Result.BookingId)) throw new FormatException("Missing booking");
                _bookKey.Complete();
                Stage = BookStage.Booked;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Stage = BookStage.Compose;
                Changed?.Invoke();
                throw;
            }
            catch (Exception e)
            {
                Fail(e);
                _bookKey.Settle(Error);
                NeedsAvailabilityRefresh = Error.Reason == "slot_unavailable";
                Stage = BookStage.Failed;
            }
            Changed?.Invoke();
        }

        private void Fail(Exception e) => Error = e == null ? null : VErrors.From(e, true);

        /// <summary>Back to an empty form (after the booking was handed to payment, or abandoned).</summary>
        public void Reset()
        {
            if (Busy) return;
            if (!(Stage == BookStage.Failed && (Error?.OutcomeUnknown ?? false))) { _bookKey.Complete(); _uploadKey.Complete(); }
            Stage = BookStage.Compose;
            Error = null;
            Result = null;
            Uploaded = null;
            _uploadedFingerprint = null;
            CreativeBytes = null;
            Creative = null;
            Link = "";
            StartText = "";
            TermId = null;
            SlotId = null;
            Availability = null;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Adapts the Money lane's top-up gateway to a booking's intent: <c>POST /ads/bookings</c> already created the payment
    /// intent, so "create" hands back that intent and "status" is the shared <c>GET /payments/intents/{id}</c>. This lets the
    /// ad payment run through Money's <see cref="TopUpFlow"/> (review → MWA sign → poll → pending/confirmed) unchanged.
    /// </summary>
    public sealed class AdIntentGateway : ITopUpGateway
    {
        private readonly ITopUpGateway _inner;
        private readonly Dictionary<string, PaymentIntentRow> _byBooking = new Dictionary<string, PaymentIntentRow>(StringComparer.Ordinal);

        public AdIntentGateway(ITopUpGateway inner) { _inner = inner ?? throw new ArgumentNullException(nameof(inner)); }

        public void Register(string bookingId, PaymentIntentRow intent) { if (!string.IsNullOrEmpty(bookingId) && intent != null) _byBooking[bookingId] = intent; }

        public Task<PaymentIntentRow> CreateIntent(string kind, string sku, string currency, string idempotencyKey, CancellationToken ct)
        {
            if (sku == null || !_byBooking.TryGetValue(sku, out var intent)) throw new FormatException("No intent for this booking");
            return Task.FromResult(intent);
        }

        public Task<IntentStatusRow> GetIntent(string intentId, CancellationToken ct) => _inner.GetIntent(intentId, ct);
        public Task SubmitIntent(string intentId, string signature, string idempotencyKey, CancellationToken ct) =>
            _inner.SubmitIntent(intentId, signature, idempotencyKey, ct);
    }

    /// <summary>When a real-money payment may be started from the client. Devnet only until the user lifts it.</summary>
    public static class PaymentPolicy
    {
        /// <summary>Hard rule from docs/AGENTS-BRIEF.md: devnet/localnet only for Solana writes.</summary>
        public const bool DevnetOnly = true;

        /// <returns>null when allowed, else a VStrings key explaining why not.</returns>
        public static string BlockedReason(SolanaCluster cluster, bool signerAvailable, bool paymentsEnabled)
        {
            if (!paymentsEnabled) return "pay.disabled";
            if (cluster == SolanaCluster.None) return "pay.noCluster";
            if (DevnetOnly && cluster != SolanaCluster.Devnet) return "pay.devnetOnly";
            if (!signerAvailable) return "pay.noWallet";
            return null;
        }
    }

    public static class AdPayments
    {
        /// <summary>Hands a freshly created booking intent to the flow. False when the flow is busy with another payment.</summary>
        public static async Task<bool> Begin(TopUpFlow flow, AdIntentGateway gateway, BookingResult booking, CancellationToken ct)
        {
            if (flow == null || gateway == null || booking?.Intent == null) return false; // no intent: the screen explains
            if (!flow.CanStartNew) return false;
            gateway.Register(booking.BookingId, booking.Intent);
            if (flow.Stage != TopUpStage.Choose) flow.Reset();
            await flow.Create(booking.BookingId, string.IsNullOrEmpty(booking.Intent.Currency) ? "USDC" : booking.Intent.Currency, ct);
            return flow.Intent != null;
        }
    }
}
