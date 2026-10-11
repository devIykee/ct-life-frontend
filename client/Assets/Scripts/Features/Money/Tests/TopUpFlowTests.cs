using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using NUnit.Framework;

namespace CTLife.Economy.Tests
{
    internal sealed class FakeClock : IMoneyClock
    {
        public double NowMs { get; set; } = 1_800_000_000_000;
        public int Delays;
        public Action OnDelay;

        public Task Delay(int milliseconds, CancellationToken ct)
        {
            Delays++;
            NowMs += milliseconds;
            OnDelay?.Invoke();
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeGateway : ITopUpGateway
    {
        public readonly List<string> Keys = new List<string>();
        public Func<PaymentIntentRow> Create = () => Intent();
        public readonly Queue<Func<IntentStatusRow>> Statuses = new Queue<Func<IntentStatusRow>>();
        public Func<IntentStatusRow> DefaultStatus = () => new IntentStatusRow { Status = "pending" };
        public int StatusCalls;
        public readonly List<(string IntentId, string Signature, string Key)> Submissions = new List<(string, string, string)>();
        public Action Submit;

        public Task SubmitIntent(string intentId, string signature, string idempotencyKey, CancellationToken ct)
        {
            Submissions.Add((intentId, signature, idempotencyKey));
            Submit?.Invoke();
            return Task.CompletedTask;
        }

        public static PaymentIntentRow Intent(string id = "0192a1c2-0000-7000-8000-000000000001", long expiresAt = 1_800_000_900_000, string tx = "AQID") =>
            new PaymentIntentRow { IntentId = id, Currency = "USDC", Amount = "10000000", CashCents = 700000, BonusBps = 0, Tx = tx, ExpiresAtMs = expiresAt, Recipient = "Treasury1111" };

        public Task<PaymentIntentRow> CreateIntent(string kind, string sku, string currency, string idempotencyKey, CancellationToken ct)
        {
            Keys.Add(idempotencyKey);
            return Task.FromResult(Create());
        }

        public Task<IntentStatusRow> GetIntent(string intentId, CancellationToken ct)
        {
            StatusCalls++;
            var next = Statuses.Count > 0 ? Statuses.Dequeue() : DefaultStatus;
            return Task.FromResult(next());
        }
    }

    internal sealed class FakeSigner : ITransactionSigner
    {
        public Func<string> Result = () => "5igSig1111";
        public int Calls;
        public string LastTx;

        public Task<string> SignAndSendBase64(string transactionBase64, CancellationToken ct)
        {
            Calls++;
            LastTx = transactionBase64;
            return Task.FromResult(Result());
        }
    }

    internal sealed class MemoryStore : IPendingIntentStore
    {
        public string Json;
        public string Load() => Json;
        public void Save(string json) => Json = json;
        public void Clear() => Json = null;
    }

    public sealed class TopUpFlowTests
    {
        private FakeGateway _gw;
        private FakeSigner _signer;
        private FakeClock _clock;
        private MemoryStore _store;
        private TopUpFlow _flow;
        private int _keyCounter;

        private SynchronizationContext _savedContext;

        [SetUp]
        public void SetUp()
        {
            // The editor's UnitySynchronizationContext would post continuations to the next editor tick; blocking on them
            // in a test would deadlock. Without a context, continuations run inline.
            _savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            _gw = new FakeGateway();
            _signer = new FakeSigner();
            _clock = new FakeClock();
            _store = new MemoryStore();
            _keyCounter = 0;
            _flow = new TopUpFlow(_gw, _signer, _clock, _store, () => "key-" + (++_keyCounter));
        }

        [TearDown]
        public void TearDown() => SynchronizationContext.SetSynchronizationContext(_savedContext);

        private void Run(Task t) => t.GetAwaiter().GetResult();

        [Test]
        public void HappyPath_CreatesReviewsSignsAndConfirms()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Assert.AreEqual(TopUpStage.Review, _flow.Stage);
            Assert.AreEqual(700000, _flow.Intent.CashCents);
            Assert.AreEqual(new[] { "key-1" }, _gw.Keys.ToArray());

            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "pending" });
            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "pending" });
            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "confirmed", Signature = "5igSig1111" });
            Run(_flow.Pay(CancellationToken.None));

            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.AreEqual("AQID", _signer.LastTx);
            Assert.AreEqual("5igSig1111", _flow.Signature);
            Assert.AreEqual(1, _gw.Submissions.Count);
            Assert.AreEqual((_flow.Intent.IntentId, "5igSig1111", "key-2"), _gw.Submissions[0]);
            Assert.IsTrue(_flow.SubmissionReported);
            Assert.AreEqual(3, _gw.StatusCalls);
            Assert.AreEqual(2, _clock.Delays, "polls 1 s apart");
            Assert.IsNull(_store.Json, "a confirmed payment is no longer persisted");
        }

        [Test]
        public void PollWindow_EndsInPendingAfterThirtySeconds_ThenPaymentConfirmedCloses()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.AreEqual(31, _gw.StatusCalls, "1 s interval for 30 s: polls at t=0..30");
            Assert.IsNotNull(_store.Json, "pending payment survives a restart");

            Assert.IsFalse(_flow.OnPaymentConfirmed("some-other-intent", 1), "another intent is ignored");
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.IsTrue(_flow.OnPaymentConfirmed(_flow.Intent.IntentId, 805000));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.AreEqual(805000, _flow.CreditedCashCents, "credited amount is the server's message value");
            Assert.IsNull(_store.Json);
        }

        [Test]
        public void PaymentConfirmedDuringPolling_StopsPolling()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            int delays = 0;
            _clock.OnDelay = () => { if (++delays == 3) _flow.OnPaymentConfirmed(_flow.Intent.IntentId, 700000); };
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.AreEqual(3, _gw.StatusCalls);
        }

        [Test]
        public void ExpiredStatusOr410_EndsExpired()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _gw.Statuses.Enqueue(() => throw new ApiException(410, "PAYMENT_EXPIRED", "expired"));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Expired, _flow.Stage);
            Assert.AreEqual("PAYMENT_EXPIRED", _flow.Error.Code);

            _flow.Reset();
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "expired" });
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Expired, _flow.Stage);
        }

        [Test]
        public void FailedStatusAndUnderpaid_EndFailed()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "failed" });
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Failed, _flow.Stage);

            _flow.Reset();
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _gw.Statuses.Enqueue(() => throw new ApiException(402, "PAYMENT_UNDERPAID", "under"));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Failed, _flow.Stage);
            Assert.AreEqual("err.paymentUnderpaid", _flow.Error.Key);
        }

        [Test]
        public void MissingStatusEndpoint_GoesPendingWithoutBusyLooping()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _gw.DefaultStatus = () => throw new ApiException(404, "HTTP_404", "Not Found");
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.IsTrue(_flow.StatusUnavailable);
            Assert.AreEqual(1, _gw.StatusCalls);
        }

        [Test]
        public void TransientPollErrors_KeepPolling()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _gw.Statuses.Enqueue(() => throw new ApiException(0, "NETWORK_ERROR", "offline", isNetwork: true));
            _gw.Statuses.Enqueue(() => throw new ApiException(500, "HTTP_500", "oops"));
            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "confirmed" });
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
        }

        [Test]
        public void TopUpCap_IsADefinitiveErrorWithAFreshKeyNextTime()
        {
            _gw.Create = () => throw new ApiException(429, "TOPUP_CAP_REACHED", "cap");
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Assert.AreEqual(TopUpStage.Error, _flow.Stage);
            Assert.AreEqual("TOPUP_CAP_REACHED", _flow.Error.Code);
            Assert.AreEqual("err.topupCap", _flow.Error.Key);
            Assert.IsFalse(_flow.Error.OutcomeUnknown);

            _gw.Create = () => FakeGateway.Intent();
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Assert.AreEqual(new[] { "key-1", "key-2" }, _gw.Keys.ToArray());
        }

        [Test]
        public void NetworkFailureOnCreate_RetriesWithTheSameKey_ButNotForAnotherPack()
        {
            _gw.Create = () => throw new ApiException(0, "NETWORK_ERROR", "offline", isNetwork: true);
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Assert.IsTrue(_flow.Error.OutcomeUnknown);
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Assert.AreEqual("key-1", _gw.Keys[1], "same request, unknown outcome: server replays");
            Run(_flow.Create("whale", "SOL", CancellationToken.None));
            Assert.AreEqual("key-2", _gw.Keys[2], "different request: new key");
        }

        [Test]
        public void WalletDecline_ReturnsToReview_AndCanPayAgain()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _signer.Result = () => throw new WalletException("no", userDeclined: true);
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Review, _flow.Stage);
            Assert.AreEqual("WALLET_DECLINED", _flow.Error.Code);
            Assert.IsNull(_store.Json, "nothing was sent; nothing to track");

            _signer.Result = () => "sig2";
            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "confirmed" });
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.AreEqual(2, _signer.Calls);
        }

        [Test]
        public void WalletFailureAfterPossibleBroadcast_PollsAndNeverSignsAgain()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _signer.Result = () => throw new WalletException("submit failed");
            Run(_flow.Pay(CancellationToken.None));
            Assert.IsTrue(_flow.SignUncertain);
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(1, _signer.Calls, "Pay is only valid from Review");
            Assert.AreEqual(0, _gw.Submissions.Count, "there is no signature to report");
        }

        [Test]
        public void SubmissionFailureSurvivesRestartAndRetriesTheSameSignatureAndKey()
        {
            _gw.Submit = () => throw new ApiException(0, "NETWORK_ERROR", "offline", isNetwork: true);
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.IsFalse(_flow.SubmissionReported);
            var original = _gw.Submissions[0];

            var restarted = new TopUpFlow(_gw, _signer, _clock, _store);
            Assert.IsTrue(restarted.Resume());
            _gw.Submit = null;
            _gw.DefaultStatus = () => new IntentStatusRow { Status = "confirmed" };
            Run(restarted.CheckAgain(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, restarted.Stage);
            Assert.AreEqual(original, _gw.Submissions[1]);
            Assert.AreEqual(1, _signer.Calls, "reporting retry must not broadcast another payment");
        }

        [Test]
        public void AcknowledgedSubmissionIsNotReportedAgainAfterRestart()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Run(_flow.Pay(CancellationToken.None));
            var restarted = new TopUpFlow(_gw, _signer, _clock, _store);
            Assert.IsTrue(restarted.Resume());
            Assert.IsTrue(restarted.SubmissionReported);
            Run(restarted.CheckAgain(CancellationToken.None));
            Assert.AreEqual(1, _gw.Submissions.Count);
            Assert.AreEqual(TopUpStage.Pending, restarted.Stage, "submission acknowledgement does not credit Cash");
        }

        [Test]
        public void CancelledSubmissionReturnsToRecoverablePending()
        {
            using var cts = new CancellationTokenSource();
            _gw.Submit = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); };
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Assert.Throws<OperationCanceledException>(() => Run(_flow.Pay(cts.Token)));
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.IsFalse(_flow.Busy);
            Assert.IsNotNull(_store.Json);
            _gw.Submit = null;
            _gw.DefaultStatus = () => new IntentStatusRow { Status = "confirmed" };
            Run(_flow.CheckAgain(CancellationToken.None));
            Assert.AreEqual(_gw.Submissions[0], _gw.Submissions[1]);
            Assert.AreEqual(1, _signer.Calls);
        }

        [Test]
        public void ConfirmationDuringSubmissionDoesNotReopenOrPersistTheFinishedPayment()
        {
            _gw.Submit = () => _flow.OnPaymentConfirmed(_flow.Intent.IntentId, 700000);
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.AreEqual(0, _gw.StatusCalls);
            Assert.IsNull(_store.Json);
        }

        [Test]
        public void ConfirmationBeforeWalletReturnsDoesNotRestartPolling()
        {
            _signer.Result = () => { _flow.OnPaymentConfirmed(_flow.Intent.IntentId, 700000); return "sig"; };
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.AreEqual(0, _gw.StatusCalls);
            Assert.AreEqual(0, _gw.Submissions.Count);
            Assert.IsNull(_store.Json);
        }

        [Test]
        public void ExpiredIntent_IsNeverSigned()
        {
            _gw.Create = () => FakeGateway.Intent(expiresAt: (long)_clock.NowMs + 3000);
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Expired, _flow.Stage);
            Assert.AreEqual(0, _signer.Calls);
        }

        [Test]
        public void IntentWithoutTx_IsNotSignable()
        {
            _gw.Create = () => FakeGateway.Intent(tx: null);
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Review, _flow.Stage);
            Assert.AreEqual("NOT_SIGNABLE", _flow.Error.Code);
            Assert.AreEqual(0, _signer.Calls);
        }

        [Test]
        public void PendingSignedPayment_ResumesAfterRestart()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);

            var restarted = new TopUpFlow(_gw, _signer, _clock, _store);
            Assert.IsTrue(restarted.Resume());
            Assert.AreEqual(TopUpStage.Pending, restarted.Stage);
            Assert.AreEqual(_flow.Intent.IntentId, restarted.Intent.IntentId);
            Assert.AreEqual("5igSig1111", restarted.Signature);

            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "confirmed" });
            Run(restarted.CheckAgain(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, restarted.Stage);
        }

        [Test]
        public void UnsignedOrMalformedPersistedIntent_IsDiscarded()
        {
            _store.Json = new PendingIntent { IntentId = "abc", SigningStarted = false, SavedAtMs = _clock.NowMs }.Serialize();
            Assert.IsFalse(_flow.Resume());
            Assert.IsNull(_store.Json);
            _store.Json = "{not json";
            Assert.IsFalse(_flow.Resume());
            Assert.AreEqual(TopUpStage.Choose, _flow.Stage);
        }

        [TestCase("old-signed-signature")]
        [TestCase(null)]
        public void OldWalletStartedIntentResumesUntilAuthoritativeResolutionWithoutSigningAgain(string signature)
        {
            _store.Json = new PendingIntent
            {
                IntentId = "old-intent", Signature = signature, SubmissionKey = "old-report-key",
                SigningStarted = true, SavedAtMs = _clock.NowMs - 30 * 24 * 3600 * 1000.0,
                ExpiresAtMs = (long)_clock.NowMs - 29 * 24 * 3600 * 1000L,
            }.Serialize();
            Assert.IsTrue(_flow.Resume(), "age and displayed expiry are not authoritative payment status");
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.AreEqual(signature == null, _flow.SignUncertain);
            Assert.IsNotNull(_store.Json);
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(0, _signer.Calls);
            _flow.Reset();
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.IsNotNull(_store.Json, "a new-purchase shortcut cannot discard an unresolved transfer");

            _gw.DefaultStatus = () => new IntentStatusRow { Status = "confirmed" };
            Run(_flow.CheckAgain(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.AreEqual(0, _signer.Calls);
            Assert.AreEqual(signature == null ? 0 : 1, _gw.Submissions.Count);
            if (signature != null) Assert.AreEqual(("old-intent", signature, "old-report-key"), _gw.Submissions[0]);
            Assert.IsNull(_store.Json, "only server confirmation ended recovery");
        }

        [Test]
        public void ResetDuringConfirmationDoesNotClearRecoveryEvidence()
        {
            Run(_flow.Create("whale", "USDC", CancellationToken.None));
            _gw.Submit = () =>
            {
                _flow.Reset();
                Assert.AreEqual(TopUpStage.Confirming, _flow.Stage);
                Assert.IsNotNull(_store.Json);
            };
            Run(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.IsNotNull(_store.Json);
        }

        [Test]
        public void ResetIsIgnoredWhileTheWalletIsOpen()
        {
            var tcs = new TaskCompletionSource<string>();
            var slowSigner = new SlowSigner(tcs.Task);
            var flow = new TopUpFlow(_gw, slowSigner, _clock, _store);
            Run(flow.Create("whale", "USDC", CancellationToken.None));
            var pay = flow.Pay(CancellationToken.None);
            Assert.AreEqual(TopUpStage.Signing, flow.Stage);
            flow.Reset();
            Assert.AreEqual(TopUpStage.Signing, flow.Stage);
            _gw.Statuses.Enqueue(() => new IntentStatusRow { Status = "confirmed" });
            tcs.SetResult("sig");
            Run(pay);
            Assert.AreEqual(TopUpStage.Confirmed, flow.Stage);
        }

        [Test]
        public void UnsafeSkuOrCurrency_IsNotSent()
        {
            Run(_flow.Create("../admin", "USDC", CancellationToken.None));
            Run(_flow.Create("whale", "", CancellationToken.None));
            Assert.AreEqual(0, _gw.Keys.Count);
            Assert.AreEqual(TopUpStage.Choose, _flow.Stage);
        }

        private sealed class SlowSigner : ITransactionSigner
        {
            private readonly Task<string> _result;
            public SlowSigner(Task<string> result) { _result = result; }
            public Task<string> SignAndSendBase64(string transactionBase64, CancellationToken ct) => _result;
        }
    }
}
