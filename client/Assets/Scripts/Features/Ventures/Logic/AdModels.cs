using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    /// <summary>Booking lifecycle from the proposal (`AdBooking.status`): awaiting_payment → pending_review → scheduled → live → expired, with rejected / cancelled / refunded exits.</summary>
    public enum AdStatus { Unknown, AwaitingPayment, PendingReview, Scheduled, Live, Expired, Rejected, Cancelled, Refunded }

    public static class AdStatuses
    {
        public static AdStatus Parse(string wire)
        {
            switch (wire)
            {
                case "awaiting_payment": return AdStatus.AwaitingPayment;
                case "pending_review": return AdStatus.PendingReview;
                case "scheduled": return AdStatus.Scheduled;
                case "live": return AdStatus.Live;
                case "expired": return AdStatus.Expired;
                case "rejected": return AdStatus.Rejected;
                case "cancelled": return AdStatus.Cancelled;
                case "refunded": return AdStatus.Refunded;
                default: return AdStatus.Unknown;
            }
        }

        public static string Wire(AdStatus s)
        {
            switch (s)
            {
                case AdStatus.AwaitingPayment: return "awaiting_payment";
                case AdStatus.PendingReview: return "pending_review";
                case AdStatus.Scheduled: return "scheduled";
                case AdStatus.Live: return "live";
                case AdStatus.Expired: return "expired";
                case AdStatus.Rejected: return "rejected";
                case AdStatus.Cancelled: return "cancelled";
                case AdStatus.Refunded: return "refunded";
                default: return "unknown";
            }
        }

        /// <summary>Proposal: cancel is allowed only in `awaiting_payment`; any later state answers CONFLICT.</summary>
        public static bool CanCancel(AdStatus s) => s == AdStatus.AwaitingPayment;

        /// <summary>Nothing more will happen to a booking in these states (a rejected booking still moves to refunded).</summary>
        public static bool IsFinished(AdStatus s) => s == AdStatus.Expired || s == AdStatus.Cancelled || s == AdStatus.Refunded;

        /// <summary>Whether a pushed move from <paramref name="from"/> to <paramref name="to"/> is a legal step of the lifecycle. An illegal one triggers a re-read.</summary>
        public static bool CanMove(AdStatus from, AdStatus to)
        {
            if (from == to) return true;
            switch (from)
            {
                case AdStatus.AwaitingPayment: return to == AdStatus.PendingReview || to == AdStatus.Cancelled || to == AdStatus.Scheduled || to == AdStatus.Live;
                case AdStatus.PendingReview: return to == AdStatus.Scheduled || to == AdStatus.Live || to == AdStatus.Rejected || to == AdStatus.Refunded;
                case AdStatus.Scheduled: return to == AdStatus.Live || to == AdStatus.Rejected || to == AdStatus.Expired || to == AdStatus.Refunded;
                case AdStatus.Live: return to == AdStatus.Expired || to == AdStatus.Rejected || to == AdStatus.Refunded;
                case AdStatus.Rejected: return to == AdStatus.Refunded;
                case AdStatus.Cancelled: return to == AdStatus.Refunded;
                case AdStatus.Unknown: return true;
                default: return false;
            }
        }
    }

    public sealed class AdTerm
    {
        public string TermId;
        /// <summary>-1 when the server did not say.</summary>
        public int Days = -1;
        /// <summary>USDC price in base units (6 decimals), or null when the server sent no price.</summary>
        public string PriceUsdcUnits;
    }

    public sealed class AdWindow
    {
        public long StartsAtMs, EndsAtMs;
    }

    public sealed class AdSlot
    {
        public string SlotId, Kind, VenueId;
        public List<AdTerm> Terms = new List<AdTerm>();
        public long? NextFreeAtMs;
    }

    public sealed class AdAvailability
    {
        public string SlotId, Kind;
        /// <summary>0 when the server did not say.</summary>
        public int CreativeWidth, CreativeHeight;
        public List<AdTerm> Terms = new List<AdTerm>();
        public List<AdWindow> Booked = new List<AdWindow>();
        public long? NextFreeAtMs;

        /// <summary>What is known about a slot before its availability is read (or when that route is missing): terms and kind, no windows, no size.</summary>
        public static AdAvailability FromSlot(AdSlot s) =>
            new AdAvailability { SlotId = s.SlotId, Kind = s.Kind, Terms = new List<AdTerm>(s.Terms), NextFreeAtMs = s.NextFreeAtMs };
    }

    public sealed class AdCreativeInfo
    {
        public string UploadId, ImageUrl, MimeType;
        public long SizeBytes;
        public int Width, Height;
    }

    public sealed class AdRefund
    {
        /// <summary>pending | sent | failed (other values are kept as sent by the server).</summary>
        public string Status, Amount, Currency, Signature;
    }

    public sealed class AdBooking
    {
        public string BookingId, SlotId, TermId, Link, IntentId, Currency, Amount, RejectionReasonId, StatusRaw;
        public AdStatus Status;
        public long? StartsAtMs, EndsAtMs;
        public long CreatedAtMs;
        public AdCreativeInfo Creative;
        public AdRefund Refund;

        public AdBooking Clone() => (AdBooking)MemberwiseClone();
    }

    public sealed class AdPage
    {
        public List<AdBooking> Items = new List<AdBooking>();
        public string NextCursor;
        /// <summary>Rows that could not be read (shown as a count, never silently dropped).</summary>
        public int Skipped;
    }

    public sealed class UploadedCreative
    {
        public string UploadId, Url;
    }

    public sealed class BookingResult
    {
        public string BookingId;
        /// <summary>Null when the server created the booking but sent no payment intent.</summary>
        public CTLife.Economy.PaymentIntentRow Intent;
    }

    public static class AdParse
    {
        /// <summary>
        /// The contract's `AdSlot.terms[].priceUsdc` is a JSON number (whole USDC, config/ads.json has 20 for a billboard week);
        /// the proposal's availability uses a digit string of USDC base units. A number is scaled by 1e6 when it has at most six
        /// decimals; a digit string is already base units. [Inference] for the number form.
        /// </summary>
        public static string UsdcUnits(JToken t)
        {
            if (VWire.IsNull(t)) return null;
            if (t.Type == JTokenType.String) return VWire.Units(t);
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
            {
                decimal d;
                try { d = Convert.ToDecimal(((JValue)t).Value, CultureInfo.InvariantCulture); }
                catch (OverflowException) { throw new FormatException("Price out of range"); }
                if (d < 0) throw new FormatException("Negative price");
                var units = d * 1_000_000m;
                if (units != decimal.Truncate(units) || units > 1e18m) throw new FormatException("Price has more than six decimals");
                return decimal.Truncate(units).ToString(CultureInfo.InvariantCulture);
            }
            throw new FormatException("Expected a price");
        }

        public static AdTerm Term(JToken t)
        {
            var id = VWire.Str(t?["termId"]);
            if (!VWire.IsSafeId(id)) throw new FormatException("Bad termId");
            return new AdTerm { TermId = id, Days = VWire.IntOr(t["days"], -1), PriceUsdcUnits = UsdcUnits(t["priceUsdc"]) };
        }

        private static List<AdTerm> Terms(JToken arr)
        {
            var list = new List<AdTerm>();
            if (!(arr is JArray a)) return list;
            foreach (var t in a)
            {
                try { list.Add(Term(t)); }
                catch (FormatException) { /* a term we cannot read is not offered */ }
            }
            return list;
        }

        public static AdSlot Slot(JToken t)
        {
            var id = VWire.FirstStr(t, "slotId", "id");
            if (!VWire.IsSafeId(id)) throw new FormatException("Bad slotId");
            return new AdSlot
            {
                SlotId = id, Kind = VWire.Plain(t["kind"], 32), VenueId = VWire.Plain(t["venueId"], 40),
                Terms = Terms(t["terms"]), NextFreeAtMs = VWire.TimeMsOrNull(t["nextFreeAt"]),
            };
        }

        /// <summary>`GET /ads/slots`: a bare array (contract) or `{slots}`. Returns the readable rows and how many were skipped.</summary>
        public static List<AdSlot> Slots(JToken body, out int skipped)
        {
            skipped = 0;
            var list = new List<AdSlot>();
            foreach (var row in VWire.List(body, "slots", "items"))
            {
                try { list.Add(Slot(row)); }
                catch (FormatException) { skipped++; }
            }
            return list;
        }

        public static AdAvailability Availability(JToken t)
        {
            var id = VWire.FirstStr(t, "slotId", "id");
            if (!VWire.IsSafeId(id)) throw new FormatException("Bad slotId");
            var a = new AdAvailability
            {
                SlotId = id, Kind = VWire.Plain(t["kind"], 32), CreativeWidth = Math.Max(0, VWire.IntOr(t["creativeWidth"], 0)),
                CreativeHeight = Math.Max(0, VWire.IntOr(t["creativeHeight"], 0)), Terms = Terms(t["terms"]),
                NextFreeAtMs = VWire.TimeMsOrNull(t["nextFreeAt"]),
            };
            if (t["booked"] is JArray booked)
                foreach (var w in booked)
                {
                    var s = VWire.TimeMsOrNull(w?["startsAt"]);
                    var e = VWire.TimeMsOrNull(w?["endsAt"]);
                    if (s.HasValue && e.HasValue && e.Value > s.Value) a.Booked.Add(new AdWindow { StartsAtMs = s.Value, EndsAtMs = e.Value });
                }
            a.Booked.Sort((x, y) => x.StartsAtMs.CompareTo(y.StartsAtMs));
            return a;
        }

        public static AdCreativeInfo Creative(JToken t)
        {
            if (!(t is JObject)) return null;
            return new AdCreativeInfo
            {
                UploadId = VWire.Plain(t["uploadId"], 80), ImageUrl = HttpsOrNull(VWire.Str(t["imageUrl"])), MimeType = VWire.Plain(t["mimeType"], 24),
                SizeBytes = VWire.LongOr(t["sizeBytes"], 0), Width = VWire.IntOr(t["width"], 0), Height = VWire.IntOr(t["height"], 0),
            };
        }

        /// <summary>Server URLs are untrusted: only https, bounded length, no control characters or spaces.</summary>
        public static string HttpsOrNull(string url, int max = 2048)
        {
            if (string.IsNullOrEmpty(url) || url.Length > max || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            return url.Any(c => char.IsControl(c) || c == ' ') ? null : url;
        }

        public static AdRefund Refund(JToken t)
        {
            if (!(t is JObject)) return null;
            return new AdRefund
            {
                Status = VWire.Plain(t["status"], 16), Amount = VWire.UnitsOr(t["amount"], null), Currency = VWire.Plain(t["currency"], 8),
                Signature = VWire.Plain(t["signature"], 100),
            };
        }

        public static AdBooking Booking(JToken t)
        {
            var id = VWire.FirstStr(t, "bookingId", "id");
            if (!VWire.IsSafeId(id)) throw new FormatException("Bad bookingId");
            var raw = VWire.Str(t["status"]);
            var intent = VWire.Str(t["intentId"]);
            return new AdBooking
            {
                BookingId = id, SlotId = VWire.Plain(t["slotId"], 80), TermId = VWire.Plain(t["termId"], 80),
                StatusRaw = VWire.Plain(raw, 24), Status = AdStatuses.Parse(raw),
                Link = HttpsOrNull(VWire.Str(t["link"])), IntentId = VWire.IsSafeId(intent) ? intent : null,
                Currency = VWire.Plain(t["currency"], 8), Amount = VWire.UnitsOr(t["amount"], null),
                StartsAtMs = VWire.TimeMsOrNull(t["startsAt"]), EndsAtMs = VWire.TimeMsOrNull(t["endsAt"]),
                CreatedAtMs = VWire.TimeMsOrNull(t["createdAt"]) ?? 0, Creative = Creative(t["creative"]),
                RejectionReasonId = VWire.IsSafeId(VWire.Str(t["rejectionReasonId"])) ? VWire.Str(t["rejectionReasonId"]) : null,
                Refund = Refund(t["refund"]),
            };
        }

        /// <summary>`GET /ads/bookings`: `{bookings, nextCursor}` (proposal) or a bare array / `{bookings}` (backend 9f3e5ef).</summary>
        public static AdPage Page(JToken body)
        {
            var page = new AdPage { NextCursor = body is JObject o ? VWire.Str(o["nextCursor"]) : null };
            foreach (var row in VWire.List(body, "bookings", "items"))
            {
                try { page.Items.Add(Booking(row)); }
                catch (FormatException) { page.Skipped++; }
            }
            return page;
        }

        public static UploadedCreative Upload(JToken t)
        {
            var id = VWire.Str(t?["uploadId"]);
            if (!VWire.IsSafeId(id)) throw new FormatException("Bad uploadId");
            return new UploadedCreative { UploadId = id, Url = HttpsOrNull(VWire.Str(t["url"])) };
        }

        /// <summary>`POST /ads/bookings` → `{bookingId, intent}`. The intent is read with the Money lane's parser.</summary>
        public static BookingResult BookingResult(JToken t)
        {
            var id = VWire.Str(t?["bookingId"]);
            if (!VWire.IsSafeId(id)) throw new FormatException("Bad bookingId");
            // Backend 9f3e5ef creates the booking without a payment intent (README observation 8): the booking exists, so keep its id
            // with a null intent and let the screen say that payment is not offered, instead of failing and replaying forever.
            if (VWire.IsNull(t["intent"])) return new BookingResult { BookingId = id, Intent = null };
            var intent = CTLife.Economy.MoneyParse.Intent(t["intent"]);
            if (intent == null || string.IsNullOrEmpty(intent.IntentId)) throw new FormatException("Missing intent");
            return new BookingResult { BookingId = id, Intent = intent };
        }
    }

    /// <summary>The "my bookings" list: newest first, de-duplicated by id, with pushed status updates applied through the lifecycle rules.</summary>
    public sealed class AdBoard
    {
        public enum UpdateResult { Applied, Unchanged, Unknown, Illegal }

        private readonly List<AdBooking> _items = new List<AdBooking>();
        public IReadOnlyList<AdBooking> Items => _items;
        public string NextCursor { get; private set; }
        public int Skipped { get; private set; }

        public void Replace(AdPage page)
        {
            _items.Clear();
            Skipped = page.Skipped;
            NextCursor = page.NextCursor;
            Merge(page.Items);
        }

        public void Append(AdPage page)
        {
            Skipped += page.Skipped;
            NextCursor = page.NextCursor;
            Merge(page.Items);
        }

        private void Merge(IEnumerable<AdBooking> rows)
        {
            foreach (var r in rows)
            {
                var i = _items.FindIndex(x => x.BookingId == r.BookingId);
                if (i >= 0) _items[i] = r; else _items.Add(r);
            }
            _items.Sort((a, b) => b.CreatedAtMs.CompareTo(a.CreatedAtMs));
        }

        public AdBooking Find(string id) => _items.Find(x => x.BookingId == id);

        public void Upsert(AdBooking b) => Merge(new[] { b });

        /// <summary>
        /// `ad_booking_update {bookingId, status, reasonId?}`. Unknown id or an illegal step means the local copy is stale:
        /// the caller re-reads the list instead of guessing.
        /// </summary>
        public UpdateResult Apply(string bookingId, AdStatus status, string reasonId)
        {
            var b = Find(bookingId);
            if (b == null) return UpdateResult.Unknown;
            if (status == AdStatus.Unknown) return UpdateResult.Illegal;
            if (b.Status == status && (reasonId == null || reasonId == b.RejectionReasonId)) return UpdateResult.Unchanged;
            if (!AdStatuses.CanMove(b.Status, status)) return UpdateResult.Illegal;
            b.Status = status;
            b.StatusRaw = AdStatuses.Wire(status);
            if (status == AdStatus.Rejected && !string.IsNullOrEmpty(reasonId)) b.RejectionReasonId = reasonId;
            return UpdateResult.Applied;
        }

        /// <summary>Active (anything not finished) first in the order the player cares about: needs action, live, scheduled, in review.</summary>
        public static int Rank(AdStatus s)
        {
            switch (s)
            {
                case AdStatus.AwaitingPayment: return 0;
                case AdStatus.Live: return 1;
                case AdStatus.Scheduled: return 2;
                case AdStatus.PendingReview: return 3;
                case AdStatus.Rejected: return 4;
                default: return 5;
            }
        }
    }
}
