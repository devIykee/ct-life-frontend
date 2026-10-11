using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Ventures.Tests
{
    public sealed class AdParseTests
    {
        [TestCase("20", "20000000")]
        [TestCase("2.5", "2500000")]
        [TestCase("0.000001", "1")]
        [TestCase("\"20000000\"", "20000000")]
        [TestCase("0", "0")]
        public void UsdcUnits_ScalesNumbersAndPassesStrings(string json, string expected) => Assert.AreEqual(expected, AdParse.UsdcUnits(JToken.Parse(json)));

        [TestCase("0.0000001")]
        [TestCase("-1")]
        [TestCase("\"1.5\"")]
        [TestCase("true")]
        public void UsdcUnits_RejectsBadPrices(string json) => Assert.Throws<FormatException>(() => AdParse.UsdcUnits(JToken.Parse(json)));

        [Test]
        public void UsdcUnits_NullIsNoPrice() => Assert.IsNull(AdParse.UsdcUnits(JValue.CreateNull()));

        [Test]
        public void Slots_ReadsTheContractArray_AndCountsBadRows()
        {
            var body = JToken.Parse(@"[
              {""slotId"":""billboard_01"",""kind"":""billboard"",""terms"":[{""termId"":""term_7d"",""priceUsdc"":20},{""termId"":""bad id"",""priceUsdc"":1}],""nextFreeAt"":""2026-10-19T00:00:00.000Z""},
              {""kind"":""booth""},
              {""slotId"":""booth_01"",""kind"":""booth"",""terms"":[{""termId"":""term_event"",""priceUsdc"":300}]}]");
            var slots = AdParse.Slots(body, out var skipped);
            Assert.AreEqual(2, slots.Count);
            Assert.AreEqual(1, skipped);
            Assert.AreEqual(1, slots[0].Terms.Count, "a term with an unsafe id is not offered");
            Assert.AreEqual("20000000", slots[0].Terms[0].PriceUsdcUnits);
            Assert.AreEqual(1_792_368_000_000, slots[0].NextFreeAtMs);
            Assert.AreEqual("300000000", slots[1].Terms[0].PriceUsdcUnits);
        }

        [Test]
        public void Slots_AcceptsAWrappedList() => Assert.AreEqual(1, AdParse.Slots(JToken.Parse(@"{""slots"":[{""slotId"":""a"",""kind"":""billboard""}]}"), out _).Count);

        [Test]
        public void Slots_RejectsANonList() => Assert.Throws<FormatException>(() => AdParse.Slots(JToken.Parse("{\"x\":1}"), out _));

        [Test]
        public void Availability_ReadsTheProposalExample()
        {
            var a = AdParse.Availability(JToken.Parse(@"{""slotId"":""billboard_01"",""kind"":""billboard"",""creativeWidth"":1024,""creativeHeight"":512,
              ""terms"":[{""termId"":""term_7d"",""days"":7,""priceUsdc"":""20000000""},{""termId"":""term_30d"",""days"":30,""priceUsdc"":""70000000""}],
              ""booked"":[{""startsAt"":""2026-10-19T00:00:00.000Z"",""endsAt"":""2026-10-26T00:00:00.000Z""},{""startsAt"":""2026-10-12T00:00:00.000Z"",""endsAt"":""2026-10-19T00:00:00.000Z""},{""startsAt"":""2026-10-30T00:00:00.000Z"",""endsAt"":""2026-10-29T00:00:00.000Z""}],
              ""nextFreeAt"":""2026-10-26T00:00:00.000Z""}"));
            Assert.AreEqual(1024, a.CreativeWidth);
            Assert.AreEqual(2, a.Terms.Count);
            Assert.AreEqual(7, a.Terms[0].Days);
            Assert.AreEqual(2, a.Booked.Count, "a window that ends before it starts is dropped");
            Assert.Less(a.Booked[0].StartsAtMs, a.Booked[1].StartsAtMs, "sorted by start");
            Assert.IsNotNull(a.NextFreeAtMs);
        }

        [Test]
        public void Availability_MissingSizeIsZero()
        {
            var a = AdParse.Availability(JToken.Parse(@"{""slotId"":""s"",""terms"":[]}"));
            Assert.AreEqual(0, a.CreativeWidth);
            Assert.AreEqual(0, a.Booked.Count);
        }

        [Test]
        public void Booking_ReadsAFullRow()
        {
            var b = AdParse.Booking(Kit.BookingJson("live"));
            Assert.AreEqual(AdStatus.Live, b.Status);
            Assert.AreEqual("20000000", b.Amount);
            Assert.AreEqual(1024, b.Creative.Width);
            Assert.AreEqual("https://example.org/launch", b.Link);
            Assert.AreEqual(Kit.Intent, b.IntentId);
            Assert.IsNull(b.Refund);
            Assert.IsNotNull(b.StartsAtMs);
        }

        [Test]
        public void Booking_RejectedWithReasonAndRefund()
        {
            var j = Kit.BookingJson("rejected");
            j["rejectionReasonId"] = "ad_x_engagement_reward";
            j["refund"] = JObject.Parse(@"{""status"":""sent"",""amount"":""20000000"",""currency"":""USDC"",""signature"":""Sig1""}");
            var b = AdParse.Booking(j);
            Assert.AreEqual(AdStatus.Rejected, b.Status);
            Assert.AreEqual("ad_x_engagement_reward", b.RejectionReasonId);
            Assert.AreEqual("sent", b.Refund.Status);
            Assert.AreEqual("20000000", b.Refund.Amount);
        }

        [Test]
        public void Booking_UnknownStatusIsKeptNotGuessed()
        {
            var b = AdParse.Booking(Kit.BookingJson("quarantined"));
            Assert.AreEqual(AdStatus.Unknown, b.Status);
            Assert.AreEqual("quarantined", b.StatusRaw);
        }

        [Test]
        public void Booking_AcceptsBackendIdKeyAndDropsUnsafeUrls()
        {
            var j = Kit.BookingJson();
            j.Remove("bookingId");
            j["id"] = "abc-1";
            j["link"] = "javascript:alert(1)";
            j["creative"]["imageUrl"] = "http://cdn.example/x.png";
            var b = AdParse.Booking(j);
            Assert.AreEqual("abc-1", b.BookingId);
            Assert.IsNull(b.Link, "only https links are kept");
            Assert.IsNull(b.Creative.ImageUrl, "only https images are kept");
        }

        [Test]
        public void Booking_RejectsAnUnsafeId()
        {
            var j = Kit.BookingJson();
            j["bookingId"] = "../etc";
            Assert.Throws<FormatException>(() => AdParse.Booking(j));
        }

        [Test]
        public void Page_ReadsBothWrappers_AndCountsSkips()
        {
            var good = Kit.BookingJson("live");
            var page = AdParse.Page(new JObject { ["bookings"] = new JArray(good, new JObject { ["slotId"] = "x" }), ["nextCursor"] = "c2" });
            Assert.AreEqual(1, page.Items.Count);
            Assert.AreEqual(1, page.Skipped);
            Assert.AreEqual("c2", page.NextCursor);
            Assert.AreEqual(1, AdParse.Page(new JArray(good)).Items.Count);
            Assert.IsNull(AdParse.Page(new JArray(good)).NextCursor);
        }

        [Test]
        public void BookingResult_ReadsTheIntentWithTheMoneyParser()
        {
            var r = AdParse.BookingResult(JObject.Parse(@"{""bookingId"":""" + Kit.Booking + @""",""intent"":{""intentId"":""" + Kit.Intent + @""",""currency"":""USDC"",
              ""recipient"":""Dgw3"",""mint"":""EPj"",""amount"":""20000000"",""reference"":""r"",""memo"":""m"",""tx"":""AQID"",""expiresAt"":""2026-10-08T12:10:00.000Z""}}"));
            Assert.AreEqual(Kit.Booking, r.BookingId);
            Assert.AreEqual("20000000", r.Intent.Amount);
            Assert.IsTrue(r.Intent.HasTx);
            Assert.IsNotNull(r.Intent.ExpiresAtMs);
        }

        [Test]
        public void BookingResult_WithoutAnIntent_KeepsTheBookingForTheScreenToExplain()
        {
            var r = AdParse.BookingResult(JObject.Parse(@"{""bookingId"":""b1""}"));
            Assert.AreEqual("b1", r.BookingId);
            Assert.IsNull(r.Intent);
        }

        [Test]
        public void BookingResult_NeedsABookingId_AndAReadableIntent()
        {
            Assert.Throws<FormatException>(() => AdParse.BookingResult(JObject.Parse(@"{""intent"":{""intentId"":""i""}}")));
            Assert.Throws<FormatException>(() => AdParse.BookingResult(JObject.Parse(@"{""bookingId"":""b1"",""intent"":{""currency"":""USDC""}}")), "an intent object without an id is garbage");
        }

        [Test]
        public void Upload_RequiresAnId()
        {
            Assert.AreEqual(Kit.Upload, AdParse.Upload(JObject.Parse(@"{""uploadId"":""" + Kit.Upload + @""",""url"":""https://cdn/x.png""}")).UploadId);
            Assert.Throws<FormatException>(() => AdParse.Upload(JObject.Parse(@"{""url"":""https://cdn/x.png""}")));
        }

        [TestCase("https://example.org/a", true)]
        [TestCase("HTTPS://EXAMPLE.ORG", true)]
        [TestCase("http://example.org", false)]
        [TestCase("javascript:alert(1)", false)]
        [TestCase("https://exa mple.org", false)]
        [TestCase("", false)]
        public void HttpsOrNull(string url, bool ok) => Assert.AreEqual(ok, AdParse.HttpsOrNull(url) != null);
    }

    public sealed class AdStatusTests
    {
        [Test]
        public void ParseAndWire_RoundTrip()
        {
            foreach (AdStatus s in Enum.GetValues(typeof(AdStatus)))
            {
                if (s == AdStatus.Unknown) continue;
                Assert.AreEqual(s, AdStatuses.Parse(AdStatuses.Wire(s)));
            }
            Assert.AreEqual(AdStatus.Unknown, AdStatuses.Parse("nope"));
            Assert.AreEqual(AdStatus.Unknown, AdStatuses.Parse(null));
        }

        [Test]
        public void OnlyAwaitingPaymentCanBeCancelled()
        {
            foreach (AdStatus s in Enum.GetValues(typeof(AdStatus)))
                Assert.AreEqual(s == AdStatus.AwaitingPayment, AdStatuses.CanCancel(s), s.ToString());
        }

        [TestCase(AdStatus.AwaitingPayment, AdStatus.PendingReview, true)]
        [TestCase(AdStatus.AwaitingPayment, AdStatus.Cancelled, true)]
        [TestCase(AdStatus.PendingReview, AdStatus.Scheduled, true)]
        [TestCase(AdStatus.PendingReview, AdStatus.Rejected, true)]
        [TestCase(AdStatus.Scheduled, AdStatus.Live, true)]
        [TestCase(AdStatus.Live, AdStatus.Expired, true)]
        [TestCase(AdStatus.Rejected, AdStatus.Refunded, true)]
        [TestCase(AdStatus.Live, AdStatus.Live, true)]
        [TestCase(AdStatus.Live, AdStatus.PendingReview, false)]
        [TestCase(AdStatus.Expired, AdStatus.Live, false)]
        [TestCase(AdStatus.Refunded, AdStatus.Rejected, false)]
        [TestCase(AdStatus.Cancelled, AdStatus.Live, false)]
        [TestCase(AdStatus.Scheduled, AdStatus.PendingReview, false)]
        public void CanMove(AdStatus from, AdStatus to, bool expected) => Assert.AreEqual(expected, AdStatuses.CanMove(from, to));

        [Test]
        public void Finished_States()
        {
            Assert.IsTrue(AdStatuses.IsFinished(AdStatus.Expired));
            Assert.IsTrue(AdStatuses.IsFinished(AdStatus.Refunded));
            Assert.IsFalse(AdStatuses.IsFinished(AdStatus.Rejected), "rejected still moves to refunded");
            Assert.IsFalse(AdStatuses.IsFinished(AdStatus.Live));
        }

        [Test]
        public void Rank_PutsActionableFirst()
        {
            Assert.Less(AdBoard.Rank(AdStatus.AwaitingPayment), AdBoard.Rank(AdStatus.Live));
            Assert.Less(AdBoard.Rank(AdStatus.Live), AdBoard.Rank(AdStatus.PendingReview));
            Assert.Less(AdBoard.Rank(AdStatus.PendingReview), AdBoard.Rank(AdStatus.Expired));
        }
    }

    public sealed class AdBoardTests
    {
        private static AdBooking B(string id, AdStatus s, long created)
        {
            var b = AdParse.Booking(Kit.BookingJson(AdStatuses.Wire(s), id));
            b.CreatedAtMs = created;
            return b;
        }

        private static AdPage PageOf(string cursor, params AdBooking[] rows)
        {
            var p = new AdPage { NextCursor = cursor };
            p.Items.AddRange(rows);
            return p;
        }

        [Test]
        public void Replace_SortsNewestFirst_AndAppendDedupes()
        {
            var board = new AdBoard();
            board.Replace(PageOf("c", B("a", AdStatus.Live, 10), B("b", AdStatus.Live, 30)));
            Assert.AreEqual(new[] { "b", "a" }, board.Items.Select(x => x.BookingId).ToArray());
            board.Append(PageOf(null, B("a", AdStatus.Expired, 10), B("c", AdStatus.Live, 20)));
            Assert.AreEqual(new[] { "b", "c", "a" }, board.Items.Select(x => x.BookingId).ToArray());
            Assert.AreEqual(AdStatus.Expired, board.Find("a").Status, "a re-read row replaces the old one");
            Assert.IsNull(board.NextCursor);
        }

        [Test]
        public void Apply_MovesAlongTheLifecycle()
        {
            var board = new AdBoard();
            board.Replace(PageOf(null, B("a", AdStatus.PendingReview, 1)));
            Assert.AreEqual(AdBoard.UpdateResult.Applied, board.Apply("a", AdStatus.Scheduled, null));
            Assert.AreEqual(AdBoard.UpdateResult.Unchanged, board.Apply("a", AdStatus.Scheduled, null));
            Assert.AreEqual(AdBoard.UpdateResult.Applied, board.Apply("a", AdStatus.Live, null));
            Assert.AreEqual(AdStatus.Live, board.Find("a").Status);
            Assert.AreEqual("live", board.Find("a").StatusRaw);
        }

        [Test]
        public void Apply_UnknownBookingOrIllegalStepAsksForAResync()
        {
            var board = new AdBoard();
            board.Replace(PageOf(null, B("a", AdStatus.Live, 1)));
            Assert.AreEqual(AdBoard.UpdateResult.Unknown, board.Apply("zzz", AdStatus.Live, null));
            Assert.AreEqual(AdBoard.UpdateResult.Illegal, board.Apply("a", AdStatus.PendingReview, null));
            Assert.AreEqual(AdBoard.UpdateResult.Illegal, board.Apply("a", AdStatus.Unknown, null));
            Assert.AreEqual(AdStatus.Live, board.Find("a").Status, "a refused update changes nothing");
        }

        [Test]
        public void Apply_RejectedKeepsTheReason()
        {
            var board = new AdBoard();
            board.Replace(PageOf(null, B("a", AdStatus.PendingReview, 1)));
            Assert.AreEqual(AdBoard.UpdateResult.Applied, board.Apply("a", AdStatus.Rejected, "ad_nsfw"));
            Assert.AreEqual("ad_nsfw", board.Find("a").RejectionReasonId);
            Assert.AreEqual(AdBoard.UpdateResult.Applied, board.Apply("a", AdStatus.Refunded, null));
            Assert.AreEqual("ad_nsfw", board.Find("a").RejectionReasonId, "the reason survives the refund step");
        }

        [Test]
        public void Upsert_AddsAndReplaces()
        {
            var board = new AdBoard();
            board.Upsert(B("a", AdStatus.AwaitingPayment, 5));
            board.Upsert(B("a", AdStatus.Cancelled, 5));
            Assert.AreEqual(1, board.Items.Count);
            Assert.AreEqual(AdStatus.Cancelled, board.Items[0].Status);
        }
    }
}
