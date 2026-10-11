using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Economy;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures.Tests
{
    internal static class Kit
    {
        public const string Booking = "0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e10";
        public const string Intent = "0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e11";
        public const string Upload = "0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e12";
        public const long Now = 1_791_000_000_000; // 2026-10-03T04:00:00Z

        public static T Sync<T>(Task<T> t) => t.GetAwaiter().GetResult();
        public static void Sync(Task t) => t.GetAwaiter().GetResult();

        /// <summary>A PNG header (signature + IHDR) that declares w x h, padded to <paramref name="total"/> bytes.</summary>
        public static byte[] Png(int w, int h, int total = 64)
        {
            var b = new byte[Math.Max(total, 33)];
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Array.Copy(sig, b, 8);
            b[11] = 13;
            b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
            b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
            b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
            return b;
        }

        /// <summary>A JPEG with an APP0 segment, then an SOF0 frame header declaring w x h.</summary>
        public static byte[] Jpeg(int w, int h, bool sof = true)
        {
            var list = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
            list.AddRange(new byte[14]); // rest of APP0 (length 16 incl. the length bytes)
            if (sof)
            {
                list.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 0x03 });
                list.AddRange(new byte[9]);
            }
            list.AddRange(new byte[] { 0xFF, 0xD9 });
            return list.ToArray();
        }

        public static JObject BookingJson(string status = "pending_review", string id = Booking) => JObject.Parse(@"{
            ""bookingId"": """ + id + @""", ""slotId"": ""billboard_01"", ""termId"": ""term_7d"", ""status"": """ + status + @""",
            ""link"": ""https://example.org/launch"", ""startsAt"": ""2026-10-12T00:00:00.000Z"", ""endsAt"": ""2026-10-19T00:00:00.000Z"",
            ""intentId"": """ + Intent + @""", ""currency"": ""USDC"", ""amount"": ""20000000"",
            ""creative"": {""uploadId"": """ + Upload + @""", ""imageUrl"": ""https://cdn.ctlife.dev/ads/x.png"", ""mimeType"": ""image/png"", ""sizeBytes"": 482113, ""width"": 1024, ""height"": 512},
            ""rejectionReasonId"": null, ""refund"": null, ""createdAt"": ""2026-10-08T12:00:00.000Z""}");

        public static AdAvailability Availability(string slot = "billboard_01", int w = 1024, int h = 512, params AdWindow[] booked)
        {
            var a = new AdAvailability
            {
                SlotId = slot, Kind = "billboard", CreativeWidth = w, CreativeHeight = h,
                Terms = new List<AdTerm>
                {
                    new AdTerm { TermId = "term_7d", Days = 7, PriceUsdcUnits = "20000000" },
                    new AdTerm { TermId = "term_30d", Days = 30, PriceUsdcUnits = "70000000" },
                },
            };
            a.Booked.AddRange(booked);
            return a;
        }
    }

    internal sealed class FakeAds : IAdsGateway
    {
        public readonly List<string> UploadKeys = new List<string>(), BookKeys = new List<string>();
        public readonly List<JObject> BookBodies = new List<JObject>();
        public int UploadCalls, BookCalls;
        public Func<Exception> UploadFails, BookFails;
        public Func<BookingResult> BookResult = () => new BookingResult
        {
            BookingId = Kit.Booking,
            Intent = new PaymentIntentRow { IntentId = Kit.Intent, Currency = "USDC", Amount = "20000000", Tx = "AQID", ExpiresAtMs = Kit.Now + 600_000, Recipient = "Treasury1111" },
        };

        public Task<AdSlotList> Slots(CancellationToken ct) => Task.FromResult(new AdSlotList());
        public Task<AdAvailability> Availability(string slotId, CancellationToken ct) => Task.FromResult(Kit.Availability(slotId));
        public Task<AdPage> Bookings(string cursor, CancellationToken ct) => Task.FromResult(new AdPage());
        public Task<AdBooking> Booking(string id, CancellationToken ct) => Task.FromResult(AdParse.Booking(Kit.BookingJson("live", id)));
        public Task<AdBooking> Cancel(string id, string key, CancellationToken ct) => Task.FromResult(AdParse.Booking(Kit.BookingJson("cancelled", id)));

        public Task<UploadedCreative> Upload(byte[] data, string mime, string fileName, string key, CancellationToken ct)
        {
            UploadCalls++;
            UploadKeys.Add(key);
            if (UploadFails != null) { var e = UploadFails(); if (e != null) return Task.FromException<UploadedCreative>(e); }
            return Task.FromResult(new UploadedCreative { UploadId = Kit.Upload, Url = "https://cdn.ctlife.dev/u.png" });
        }

        public Task<BookingResult> Book(JObject body, string key, CancellationToken ct)
        {
            BookCalls++;
            BookKeys.Add(key);
            BookBodies.Add(body);
            if (BookFails != null) { var e = BookFails(); if (e != null) return Task.FromException<BookingResult>(e); }
            return Task.FromResult(BookResult());
        }
    }
}
