using System.Collections.Generic;
using NUnit.Framework;

namespace CTLife.SocialFeatures.Tests
{
    public class CallSessionTests
    {
        private const string Me = "0192aaaa-0000-7000-8000-000000000001";
        private const string A = "0192aaaa-0000-7000-8000-00000000000a";
        private const string B = "0192aaaa-0000-7000-8000-00000000000b";

        [Test]
        public void InviteListIsValidatedAndNormalized()
        {
            Assert.AreEqual("calls.pickSomeone", CallSession.ValidateInvitees(new string[0], Me, out _));
            Assert.AreEqual("calls.pickSomeone", CallSession.ValidateInvitees(new[] { Me, " " }, Me, out _), "yourself and blanks are dropped");
            Assert.IsNull(CallSession.ValidateInvitees(new[] { A, A.ToUpperInvariant(), B, Me }, Me, out var list));
            CollectionAssert.AreEqual(new[] { A, B }, list);
            var eight = new List<string>();
            for (int i = 0; i < 8; i++) eight.Add("0192aaaa-0000-7000-8000-0000000001" + i.ToString("00"));
            Assert.AreEqual("calls.tooMany", CallSession.ValidateInvitees(eight, Me, out _), "groups up to 8 including the caller");
            Assert.IsNull(CallSession.ValidateInvitees(eight.GetRange(0, 7), Me, out _));
            Assert.AreEqual("calls.badUser", CallSession.ValidateInvitees(new[] { "a/b" }, Me, out _));
        }

        [Test]
        public void OutgoingCallRingsThenGoesLive()
        {
            var c = CallSession.StartOutgoing(new[] { A }, 0);
            Assert.AreEqual(CallPhase.Dialing, c.Phase);
            Assert.IsTrue(c.Busy);
            c.Dialed("call-1", 100);
            Assert.AreEqual(CallPhase.Connecting, c.Phase);
            Assert.AreEqual("call-1", c.CallId);
            c.MediaConnected(200);
            Assert.AreEqual(CallPhase.Waiting, c.Phase);
            c.RemoteJoined(A, 5000);
            Assert.AreEqual(CallPhase.Live, c.Phase);
            Assert.AreEqual(1.0, c.LiveSeconds(6000), 1e-9);
        }

        [Test]
        public void NoAnswerEndsTheCall()
        {
            var c = CallSession.StartOutgoing(new[] { A }, 0);
            c.Dialed("call-1", 0);
            c.MediaConnected(0);
            Assert.IsFalse(c.Tick(CallSession.UnansweredMs - 1));
            Assert.IsTrue(c.Tick(CallSession.UnansweredMs));
            Assert.AreEqual(CallEnd.Unanswered, c.EndReason);
            Assert.IsFalse(c.Busy);
        }

        [Test]
        public void CallEndsAfterEveryoneLeftAndTheGraceRunsOut()
        {
            var c = CallSession.StartOutgoing(new[] { A, B }, 0);
            c.Dialed("call-1", 0); c.MediaConnected(0);
            c.RemoteJoined(A, 10); c.RemoteJoined(B, 20);
            c.RemoteLeft(A, 1000);
            Assert.IsFalse(c.Tick(100_000), "B is still here");
            c.RemoteLeft(B, 100_000);
            Assert.IsFalse(c.Tick(100_000 + CallSession.AloneGraceMs - 1));
            c.RemoteJoined(B, 110_000); // came back within the grace
            Assert.IsFalse(c.Tick(200_000));
            c.RemoteLeft(B, 200_000);
            Assert.IsTrue(c.Tick(200_000 + CallSession.AloneGraceMs));
            Assert.AreEqual(CallEnd.EveryoneLeft, c.EndReason);
        }

        [Test]
        public void IncomingRingTimesOutAsMissed()
        {
            var c = CallSession.Incoming("call-9", A, false, 0, CallSession.RingMs, CallSession.RingMs);
            Assert.AreEqual(CallPhase.Ringing, c.Phase);
            Assert.IsFalse(c.Busy, "a ringing call does not take the call slot");
            Assert.AreEqual(45, c.SecondsToAnswer(0), 1e-9);
            Assert.IsTrue(c.Tick(CallSession.RingMs));
            Assert.AreEqual(CallEnd.Missed, c.EndReason);
            Assert.IsFalse(c.Accept(CallSession.RingMs + 1));
        }

        [Test]
        public void PaidInviteStopsOfferingAcceptTenSecondsEarly()
        {
            var c = CallSession.Incoming("call-9", A, true, 0, PaidCallFlow.AcceptWindowMs, PaidCallFlow.AcceptCutoffMs);
            Assert.IsTrue(c.CanAccept(109_999));
            Assert.IsFalse(c.CanAccept(110_000));
            Assert.IsFalse(c.Tick(119_999));
            Assert.IsTrue(c.Tick(120_000));
        }

        [Test]
        public void AcceptJoinAndDecline()
        {
            var c = CallSession.Incoming("call-9", A, false, 0, CallSession.RingMs, CallSession.RingMs);
            Assert.IsTrue(c.Accept(1000));
            Assert.AreEqual(CallPhase.Accepting, c.Phase);
            c.Joining(1100);
            Assert.AreEqual(CallPhase.Connecting, c.Phase);
            c.RemoteJoined(A, 1200);
            c.MediaConnected(1300);
            Assert.AreEqual(CallPhase.Live, c.Phase, "the caller was already in the room");
            var d = CallSession.Incoming("call-10", B, false, 0, CallSession.RingMs, CallSession.RingMs);
            d.Decline(10);
            Assert.AreEqual(CallEnd.Declined, d.EndReason);
        }

        [Test]
        public void DroppedMediaReconnectsThenEnds()
        {
            var c = CallSession.StartOutgoing(new[] { A }, 0);
            c.Dialed("c", 0); c.MediaConnected(0); c.RemoteJoined(A, 0);
            c.MediaLost(1000);
            Assert.AreEqual(CallPhase.Reconnecting, c.Phase);
            Assert.AreEqual(0, c.Present.Count, "presence is rebuilt after the rejoin");
            Assert.IsTrue(c.ReconnectDue(2000));
            c.ReconnectStarted();
            c.ReconnectFailed(2000);
            c.NetworkChanged(2100);
            Assert.IsTrue(c.ReconnectDue(2100), "network change retries now");
            c.ReconnectStarted();
            c.RemoteJoined(A, 2200);
            c.MediaConnected(2300);
            Assert.AreEqual(CallPhase.Live, c.Phase);
            c.MediaLost(5000);
            for (int i = 0; i < 10 && !c.IsOver; i++) { c.ReconnectStarted(); c.ReconnectFailed(100_000 * (i + 1)); }
            Assert.AreEqual(CallEnd.ConnectionLost, c.EndReason);
        }

        [Test]
        public void HangupIsFinal()
        {
            var c = CallSession.StartOutgoing(new[] { A }, 0);
            c.End(CallEnd.HungUp, 5);
            c.Dialed("late", 10);
            Assert.AreEqual(CallPhase.Ended, c.Phase);
            Assert.IsNull(c.CallId, "a dial answer after hang-up is ignored");
            c.End(CallEnd.Failed, 20);
            Assert.AreEqual(CallEnd.HungUp, c.EndReason);
        }
    }

    public class PaidCallFlowTests
    {
        private const string Creator = "0192aaaa-0000-7000-8000-0000000000cc";
        private const string Call = "0192cccc-0000-7000-8000-000000000001";

        private static PaidCallStart Start(string tx = "AQID") => new PaidCallStart { CallId = Call, SessionPda = "Pda1111", Tx = tx };
        private static PaidCallRow Row(PaidCallStatus s, string id = Call) => new PaidCallRow { CallId = id, Status = s };

        private static PaidCallFlow Ready(double now = 0)
        {
            var f = PaidCallFlow.ForFan(Creator);
            Assert.IsNull(f.BeginCreate("5"));
            f.Created(Start(), now);
            return f;
        }

        [TestCase("0", "paid.minutesRange")]
        [TestCase("61", "paid.minutesRange")]
        [TestCase("abc", "paid.minutesRange")]
        [TestCase("-3", "paid.minutesRange")]
        [TestCase("60", null)]
        [TestCase(" 1 ", null)]
        public void MinutesAreOneToSixty(string input, string error)
        {
            var f = PaidCallFlow.ForFan(Creator);
            Assert.AreEqual(error, f.BeginCreate(input));
        }

        [Test]
        public void CreateGivesASignableTransaction()
        {
            var f = Ready(1000);
            Assert.AreEqual(PaidStage.ReadyToSign, f.Stage);
            Assert.AreEqual(5, f.Minutes);
            Assert.AreEqual(Call, f.CallId);
            Assert.IsTrue(f.CanSign(1000 + PaidCallFlow.TxMaxAgeMs - 1));
            Assert.IsFalse(f.CanSign(1000 + PaidCallFlow.TxMaxAgeMs));
            Assert.IsFalse(f.CreateKey.Pending, "a definitive answer retires the create key");
        }

        [Test]
        public void ResponseWithoutTxFails()
        {
            var f = PaidCallFlow.ForFan(Creator);
            f.BeginCreate("5");
            f.Created(Start(tx: null), 0);
            Assert.AreEqual(PaidStage.Failed, f.Stage);
            Assert.AreEqual("paid.noTx", f.ErrorKey);
        }

        [Test]
        public void UnknownCreateOutcomeReusesTheKey()
        {
            var f = PaidCallFlow.ForFan(Creator);
            f.BeginCreate("5");
            var k1 = f.CreateKey.For("POST", "/paid-calls", "{\"creatorId\":\"x\",\"minutes\":5}");
            f.CreateFailed(new SocialError { Code = "NETWORK_ERROR", Kind = ErrorKind.Offline });
            Assert.AreEqual(PaidStage.Draft, f.Stage);
            Assert.AreEqual("paid.createUnknown", f.ErrorKey);
            f.BeginCreate("5");
            Assert.AreEqual(k1, f.CreateKey.For("POST", "/paid-calls", "{\"creatorId\":\"x\",\"minutes\":5}"), "the server replays instead of opening a second call");
            f.CreateFailed(new SocialError { Code = "VALIDATION_FAILED", Kind = ErrorKind.Definitive, CopyKey = "err.validation" });
            Assert.IsFalse(f.CreateKey.Pending);
            Assert.AreEqual("err.validation", f.ErrorKey);
        }

        [Test]
        public void DeclinedInWalletCanSignTheSameTransactionAgain()
        {
            var f = Ready();
            Assert.IsTrue(f.BeginSign(10));
            Assert.AreEqual(PaidStage.Signing, f.Stage);
            Assert.IsFalse(f.BeginSign(10), "no second signature request while one is open");
            f.SignDeclined();
            Assert.AreEqual(PaidStage.ReadyToSign, f.Stage);
            Assert.IsTrue(f.CanSign(20));
        }

        [Test]
        public void WalletErrorAfterPossibleBroadcastNeverSignsAgain()
        {
            var f = Ready();
            f.BeginSign(10);
            f.SignFailedUnknown();
            Assert.AreEqual(PaidStage.Confirming, f.Stage);
            Assert.IsFalse(f.CanSign(20));
            Assert.AreEqual(2000, f.PollIntervalMs());
            f.ApplyServer(Row(PaidCallStatus.AwaitingPayment), 30);
            Assert.AreEqual(PaidStage.Confirming, f.Stage, "still unpaid: keep checking");
            f.ApplyServer(Row(PaidCallStatus.Ringing), 40);
            Assert.AreEqual(PaidStage.Ringing, f.Stage, "the server saw the escrow");
        }

        [Test]
        public void SignedThenSubmittedThenRinging()
        {
            var f = Ready();
            f.BeginSign(10);
            f.Signed("5igSig");
            Assert.AreEqual(PaidStage.Submitting, f.Stage);
            Assert.AreEqual("5igSig", f.Signature);
            Assert.IsTrue(f.ApplyServer(Row(PaidCallStatus.Ringing), 1000));
            Assert.AreEqual(PaidStage.Ringing, f.Stage);
            Assert.AreEqual(120, f.AcceptSecondsLeft(1000), 1e-9);
            Assert.AreEqual(60, f.AcceptSecondsLeft(61_000), 1e-9);
            Assert.IsFalse(f.RefundOverdue(1000 + PaidCallFlow.AcceptWindowMs));
            Assert.IsTrue(f.RefundOverdue(1000 + PaidCallFlow.AcceptWindowMs + PaidCallFlow.RefundGraceMs));
        }

        [Test]
        public void SubmitFailuresRetryOrFallBackToPolling()
        {
            var f = Ready();
            f.BeginSign(0); f.Signed("sig");
            f.SubmitFailed(new SocialError { Code = "NETWORK_ERROR", Kind = ErrorKind.Offline });
            Assert.AreEqual(PaidStage.Submitting, f.Stage);
            Assert.AreEqual("paid.submitRetry", f.ErrorKey);
            f.SubmitFailed(new SocialError { Code = "VALIDATION_FAILED", Kind = ErrorKind.Definitive, CopyKey = "err.validation" });
            Assert.AreEqual(PaidStage.Confirming, f.Stage, "the escrow is on chain either way; ask the server");
        }

        [Test]
        public void AwaitingPaymentAfterSubmitMeansConfirming()
        {
            var f = Ready();
            f.BeginSign(0); f.Signed("sig");
            f.ApplyServer(Row(PaidCallStatus.AwaitingPayment), 10);
            Assert.AreEqual(PaidStage.Confirming, f.Stage);
        }

        [Test]
        public void LiveEndSettledIsTerminal()
        {
            var f = Ready();
            f.BeginSign(0); f.Signed("sig");
            f.ApplyServer(Row(PaidCallStatus.Live), 10);
            Assert.AreEqual(PaidStage.Live, f.Stage);
            Assert.IsTrue(f.CanEnd);
            Assert.AreEqual(5000, f.PollIntervalMs());
            f.BeginEnd();
            Assert.AreEqual(PaidStage.Ending, f.Stage);
            f.ApplyServer(Row(PaidCallStatus.Live), 20);
            Assert.AreEqual(PaidStage.Ending, f.Stage, "a stale live does not reopen an ended call");
            var settled = new PaidCallRow { CallId = Call, Status = PaidCallStatus.Settled, MinutesUsed = 3, CreatorAmount = "6000000", RefundAmount = "4000000" };
            f.ApplyServer(settled, 30);
            Assert.AreEqual(PaidStage.Settled, f.Stage);
            Assert.IsTrue(double.IsNaN(f.PollIntervalMs()));
            Assert.AreEqual("6000000", f.Server.CreatorAmount, "money comes from the server row");
            Assert.IsFalse(f.ApplyServer(Row(PaidCallStatus.Live), 40));
            Assert.AreEqual(PaidStage.Settled, f.Stage, "terminal never moves back");
        }

        [Test]
        public void RowsForAnotherCallAreIgnored()
        {
            var f = Ready();
            Assert.IsFalse(f.ApplyServer(Row(PaidCallStatus.Refunded, "0192cccc-0000-7000-8000-000000000999"), 0));
            Assert.AreEqual(PaidStage.ReadyToSign, f.Stage);
        }

        [Test]
        public void StaleTransactionIsDiscardedNotSigned()
        {
            var f = Ready(0);
            f.DiscardStaleTx(PaidCallFlow.TxMaxAgeMs - 1);
            Assert.AreEqual(PaidStage.ReadyToSign, f.Stage);
            f.DiscardStaleTx(PaidCallFlow.TxMaxAgeMs);
            Assert.AreEqual(PaidStage.Draft, f.Stage);
            Assert.AreEqual("paid.txExpired", f.ErrorKey);
            Assert.IsNull(f.Tx);
        }

        [Test]
        public void CreatorHas120SecondsAndStopsAcceptingAt110()
        {
            var f = PaidCallFlow.ForCreator(Call, "fan", 0);
            Assert.IsFalse(f.IsFan);
            Assert.AreEqual(PaidStage.Ringing, f.Stage);
            Assert.IsTrue(f.BeginAccept(109_999));
            Assert.IsFalse(f.CanAccept(110_000));
            Assert.IsFalse(f.AcceptWindowOver(119_999));
            Assert.IsTrue(f.AcceptWindowOver(120_000));
            f.ApplyServer(Row(PaidCallStatus.Live), 5000);
            Assert.AreEqual(PaidStage.Live, f.Stage);
            Assert.IsFalse(f.CanSign(5000), "creators never sign");
        }

        [Test]
        public void CreatorDeclineRefunds()
        {
            var f = PaidCallFlow.ForCreator(Call, "fan", 0);
            f.ApplyServer(Row(PaidCallStatus.Refunded), 10);
            Assert.AreEqual(PaidStage.Refunded, f.Stage);
            Assert.IsTrue(f.Terminal);
        }

        [Test]
        public void ResumedFanFlowOnlyPolls()
        {
            var f = PaidCallFlow.ResumeFan(Creator, Call);
            Assert.AreEqual(PaidStage.Confirming, f.Stage);
            Assert.IsFalse(f.BeginSign(0));
            f.ApplyServer(Row(PaidCallStatus.Refunded), 10);
            Assert.AreEqual(PaidStage.Refunded, f.Stage);
            var g = PaidCallFlow.ResumeFan(Creator, Call);
            g.Abandon();
            Assert.AreEqual(PaidStage.Failed, g.Stage);
        }

        [Test]
        public void AcceptFailureKeepsKeyOnlyWhenUnknown()
        {
            var f = PaidCallFlow.ForCreator(Call, "fan", 0);
            var k = f.AcceptKey.For("POST", "/paid-calls/x/accept", null);
            f.AcceptFailed(new SocialError { Kind = ErrorKind.OutcomeUnknown, Code = "TIMEOUT" });
            Assert.AreEqual(k, f.AcceptKey.Current);
            f.AcceptFailed(new SocialError { Kind = ErrorKind.Definitive, Code = "FORBIDDEN", CopyKey = "err.forbidden" });
            Assert.IsFalse(f.AcceptKey.Pending);
        }
    }
}
