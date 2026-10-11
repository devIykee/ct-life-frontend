using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Ventures.Tests
{
    public sealed class BookingFlowTests
    {
        private FakeAds _gw;
        private AdBookingFlow _flow;
        private int _n;
        private SynchronizationContext _saved;
        private readonly CreativeLimits _limits = new CreativeLimits();

        [SetUp]
        public void SetUp()
        {
            _saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            _gw = new FakeAds();
            _n = 0;
            _flow = new AdBookingFlow(_gw, () => "key-" + (++_n));
            _flow.SelectSlot(Kit.Availability());
        }

        [TearDown]
        public void TearDown() => SynchronizationContext.SetSynchronizationContext(_saved);

        private void Ready(string term = "term_7d")
        {
            _flow.TermId = term;
            Assert.IsTrue(_flow.SetCreative(Kit.Png(1024, 512), "ad.png", _limits).Ok);
        }

        [Test]
        public void HappyPath_UploadsThenBooks_AndHandsBackTheServersIntent()
        {
            Ready();
            _flow.Link = "https://example.org/launch";
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual(BookStage.Booked, _flow.Stage);
            Assert.AreEqual(1, _gw.UploadCalls);
            Assert.AreEqual(1, _gw.BookCalls);
            Assert.AreEqual(Kit.Booking, _flow.Result.BookingId);
            Assert.AreEqual("20000000", _flow.Result.Intent.Amount);
            var body = _gw.BookBodies[0];
            Assert.AreEqual("billboard_01", (string)body["slotId"]);
            Assert.AreEqual("term_7d", (string)body["termId"]);
            Assert.AreEqual(Kit.Upload, (string)body["uploadId"]);
            Assert.AreEqual("https://example.org/launch", (string)body["link"]);
            Assert.IsNull(body["startsAt"], "no start date unless the player picked one");
            Assert.IsNull(body["currency"], "the proposal's booking body has no currency");
        }

        [Test]
        public void CannotSubmitUntilTheFormIsComplete()
        {
            Assert.IsFalse(_flow.CanSubmit(Kit.Now));
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.NoTerm);
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.NoCreative);
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual(0, _gw.UploadCalls, "nothing is sent for an incomplete form");
        }

        [Test]
        public void InvalidCreative_IsNotKept()
        {
            _flow.TermId = "term_7d";
            Assert.IsFalse(_flow.SetCreative(new byte[10], "x", _limits).Ok);
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.NoCreative);
        }

        [Test]
        public void UnknownTerm_IsAProblem()
        {
            Ready();
            _flow.TermId = "term_99d";
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.UnknownTerm);
        }

        [Test]
        public void BadLink_BlocksSubmit()
        {
            Ready();
            _flow.Link = "http://insecure.example";
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.BadLink);
            Assert.IsFalse(_flow.CanSubmit(Kit.Now));
        }

        [Test]
        public void OnlyTheLoneTermIsPreselected()
        {
            var one = new AdBookingFlow(_gw);
            var a = Kit.Availability();
            a.Terms.RemoveAt(1);
            one.SelectSlot(a);
            Assert.AreEqual("term_7d", one.TermId);
            Assert.IsNull(_flow.TermId, "two terms: the player chooses");
        }

        [Test]
        public void UploadFailure_ReplaysTheSameKeyAfterAnUnknownOutcome()
        {
            Ready();
            int fails = 1;
            _gw.UploadFails = () => fails-- > 0 ? new TimeoutException() : null;
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual(BookStage.Failed, _flow.Stage);
            Assert.IsTrue(_flow.Error.OutcomeUnknown);
            Assert.AreEqual(0, _gw.BookCalls);
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual(BookStage.Booked, _flow.Stage);
            Assert.AreEqual(2, _gw.UploadKeys.Count);
            Assert.AreEqual(_gw.UploadKeys[0], _gw.UploadKeys[1], "same key: the server replays instead of storing twice");
        }

        [Test]
        public void UploadRejected_GetsAFreshKeyNextTime()
        {
            Ready();
            int fails = 1;
            _gw.UploadFails = () => fails-- > 0 ? new ApiException(400, "VALIDATION_FAILED", "bad image") : null;
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.IsFalse(_flow.Error.OutcomeUnknown);
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreNotEqual(_gw.UploadKeys[0], _gw.UploadKeys[1]);
        }

        [Test]
        public void BookingFailure_DoesNotUploadAgain_AndReplaysTheBookingKey()
        {
            Ready();
            int fails = 1;
            _gw.BookFails = () => fails-- > 0 ? new ApiException(0, "NETWORK_ERROR", "x", null, true) : null;
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual(BookStage.Failed, _flow.Stage);
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual(BookStage.Booked, _flow.Stage);
            Assert.AreEqual(1, _gw.UploadCalls, "the creative is stored once");
            Assert.AreEqual(2, _gw.BookCalls);
            Assert.AreEqual(_gw.BookKeys[0], _gw.BookKeys[1]);
        }

        [Test]
        public void ChangingTheCreative_UploadsAgain()
        {
            Ready();
            _gw.BookFails = () => new ApiException(400, "VALIDATION_FAILED", "no");
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            _gw.BookFails = null;
            Assert.IsTrue(_flow.SetCreative(Kit.Png(1024, 512, 200), "other.png", _limits).Ok);
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual(2, _gw.UploadCalls);
        }

        [Test]
        public void SlotTaken_FlagsAnAvailabilityRefresh()
        {
            Ready();
            _gw.BookFails = () => new ApiException(409, "CONFLICT", "taken", new JObject { ["reason"] = "slot_unavailable" });
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual("err.slotTaken", _flow.Error.Key);
            Assert.IsTrue(_flow.NeedsAvailabilityRefresh);
            _flow.RefreshAvailability(Kit.Availability());
            Assert.IsFalse(_flow.NeedsAvailabilityRefresh);
        }

        [Test]
        public void SlotTaken_AckStopsAFailingRereadFromLooping()
        {
            Ready();
            _gw.BookFails = () => new ApiException(409, "CONFLICT", "taken", new JObject { ["reason"] = "slot_unavailable" });
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            _flow.AckAvailabilityRefresh();
            Assert.IsFalse(_flow.NeedsAvailabilityRefresh, "the re-read was started; a 404 on it must not ask again");
        }

        [Test]
        public void MissingRoute_IsReportedAsUnavailable()
        {
            Ready();
            _gw.BookFails = () => new ApiException(404, "HTTP_404", "Route not found");
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.IsTrue(_flow.Error.IsUnavailable);
        }

        [Test]
        public void Cancellation_ReturnsToComposeAndKeepsTheKeys()
        {
            Ready();
            var cts = new CancellationTokenSource();
            _gw.BookFails = () => new OperationCanceledException(cts.Token);
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => Kit.Sync(_flow.Submit(Kit.Now, cts.Token)));
            Assert.AreEqual(BookStage.Compose, _flow.Stage);
            _gw.BookFails = null;
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual(_gw.BookKeys[0], _gw.BookKeys[1], "the cancelled request may have reached the server");
        }

        // ---------------------------------------------------------------- start date

        [TestCase("", -1L, true)]
        [TestCase("  ", -1L, true)]
        [TestCase("2026-10-20", 1792454400000L, true)]
        [TestCase("20-10-2026", -1L, false)]
        [TestCase("2026-13-01", -1L, false)]
        [TestCase("soon", -1L, false)]
        public void ParseStart(string text, long expected, bool ok)
        {
            var v = AdBookingFlow.ParseStart(text, out var parsedOk);
            Assert.AreEqual(ok, parsedOk);
            Assert.AreEqual(expected < 0 ? (long?)null : expected, v);
        }

        [Test]
        public void StartDate_GoesIntoTheBodyAsUtcMidnight()
        {
            Ready();
            _flow.StartText = "2026-10-20";
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            Assert.AreEqual("2026-10-20T00:00:00.000Z", (string)_gw.BookBodies[0]["startsAt"]);
        }

        [Test]
        public void StartDate_CannotOverlapABookedWindow()
        {
            var win = new AdWindow { StartsAtMs = 1791763200000, EndsAtMs = 1792368000000 }; // 12 Oct to 19 Oct
            _flow.SelectSlot(Kit.Availability("billboard_01", 1024, 512, win));
            _flow.TermId = "term_7d";
            _flow.SetCreative(Kit.Png(1024, 512), "a.png", _limits);
            _flow.StartText = "2026-10-15";
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.StartOverlaps);
            _flow.StartText = "2026-10-06";
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.StartOverlaps, "6 Oct + 7 days runs into the 12 Oct window");
            _flow.StartText = "2026-10-05";
            CollectionAssert.DoesNotContain(_flow.Problems(Kit.Now), BookProblem.StartOverlaps, "ends exactly when the booked window begins");
            _flow.StartText = "2026-10-19";
            CollectionAssert.DoesNotContain(_flow.Problems(Kit.Now), BookProblem.StartOverlaps, "starts exactly when it ends");
        }

        [Test]
        public void StartDate_InThePast_AndMalformed()
        {
            Ready();
            _flow.StartText = "2026-09-01";
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.StartInPast);
            _flow.StartText = "tomorrow";
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.BadStart);
        }

        [Test]
        public void StartDate_NotOfferedForTermsWithoutALength()
        {
            var a = Kit.Availability();
            a.Terms.Add(new AdTerm { TermId = "term_event", Days = -1, PriceUsdcUnits = "300000000" });
            _flow.SelectSlot(a);
            _flow.TermId = "term_event";
            _flow.SetCreative(Kit.Png(1024, 512), "a.png", _limits);
            _flow.StartText = "2026-10-20";
            CollectionAssert.Contains(_flow.Problems(Kit.Now), BookProblem.StartNotSupported);
        }

        [Test]
        public void Overlaps_Boundaries()
        {
            var w = new[] { new AdWindow { StartsAtMs = 100, EndsAtMs = 200 } };
            Assert.IsFalse(AdBookingFlow.Overlaps(w, 0, 100));
            Assert.IsTrue(AdBookingFlow.Overlaps(w, 0, 101));
            Assert.IsTrue(AdBookingFlow.Overlaps(w, 150, 160));
            Assert.IsFalse(AdBookingFlow.Overlaps(w, 200, 300));
            Assert.IsTrue(AdBookingFlow.Overlaps(w, 199, 300));
        }

        [Test]
        public void Reset_ClearsTheFormButKeepsAnUnknownBookingKey()
        {
            Ready();
            _gw.BookFails = () => new TimeoutException();
            Kit.Sync(_flow.Submit(Kit.Now, CancellationToken.None));
            _flow.Reset();
            Assert.AreEqual(BookStage.Compose, _flow.Stage);
            Assert.IsNull(_flow.SlotId);
            Assert.IsNull(_flow.Creative);
        }
    }
}
