using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Economy;
using NUnit.Framework;

namespace CTLife.Ventures.Tests
{
    internal sealed class VClock : IMoneyClock
    {
        public double NowMs { get; set; } = Kit.Now;
        public Task Delay(int milliseconds, CancellationToken ct) { NowMs += milliseconds; return Task.CompletedTask; }
    }

    internal sealed class VStatusSource : ITopUpGateway
    {
        public readonly Queue<string> Statuses = new Queue<string>();
        public int Calls;
        public Task<PaymentIntentRow> CreateIntent(string kind, string sku, string currency, string key, CancellationToken ct) => throw new InvalidOperationException("must not be called");
        public Task<IntentStatusRow> GetIntent(string intentId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new IntentStatusRow { Status = Statuses.Count > 0 ? Statuses.Dequeue() : "pending", Signature = "SigX" });
        }
    }

    internal sealed class VSigner : ITransactionSigner
    {
        public int Calls;
        public string LastTx;
        public Func<Task<string>> Behaviour = () => Task.FromResult("5igSig");
        public Task<string> SignAndSendBase64(string tx, CancellationToken ct) { Calls++; LastTx = tx; return Behaviour(); }
    }

    internal sealed class VStore : IPendingIntentStore
    {
        public string Json;
        public string Load() => Json;
        public void Save(string json) => Json = json;
        public void Clear() => Json = null;
    }

    public sealed class PaymentTests
    {
        private VStatusSource _status;
        private AdIntentGateway _gateway;
        private VSigner _signer;
        private VClock _clock;
        private VStore _store;
        private TopUpFlow _flow;
        private SynchronizationContext _saved;

        [SetUp]
        public void SetUp()
        {
            _saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            _status = new VStatusSource();
            _gateway = new AdIntentGateway(_status);
            _signer = new VSigner();
            _clock = new VClock();
            _store = new VStore();
            _flow = new TopUpFlow(_gateway, _signer, _clock, _store);
        }

        [TearDown]
        public void TearDown() => SynchronizationContext.SetSynchronizationContext(_saved);

        private static BookingResult Booking(string tx = "AQID", long? expires = null) => new BookingResult
        {
            BookingId = Kit.Booking,
            Intent = new PaymentIntentRow { IntentId = Kit.Intent, Currency = "USDC", Amount = "20000000", Tx = tx, ExpiresAtMs = expires ?? Kit.Now + 600_000, Recipient = "Treasury1111" },
        };

        [Test]
        public void BookingIntent_RunsThroughMoneysFlow_ToConfirmed()
        {
            Assert.IsTrue(Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(), CancellationToken.None)));
            Assert.AreEqual(TopUpStage.Review, _flow.Stage);
            Assert.AreEqual("20000000", _flow.Intent.Amount, "the amount shown is the server's intent");
            Assert.AreEqual(Kit.Booking, _flow.Sku);
            _status.Statuses.Enqueue("pending");
            _status.Statuses.Enqueue("confirmed");
            Kit.Sync(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.AreEqual("AQID", _signer.LastTx);
            Assert.AreEqual(1, _signer.Calls);
            Assert.AreEqual(2, _status.Calls);
        }

        [Test]
        public void NoSecondIntentIsCreated_TheBookingsOwnIsUsed()
        {
            // VStatusSource.CreateIntent throws: if the flow asked the server for a new intent this would fail.
            Assert.IsTrue(Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(), CancellationToken.None)));
        }

        [Test]
        public void BookingWithoutAnIntent_IsNotPayable_AndTheFlowIsLeftAlone()
        {
            var noIntent = new BookingResult { BookingId = Kit.Booking, Intent = null };
            Assert.IsFalse(Kit.Sync(AdPayments.Begin(_flow, _gateway, noIntent, CancellationToken.None)));
            Assert.AreEqual(TopUpStage.Choose, _flow.Stage);
        }

        [Test]
        public void WalletDeclined_LeavesTheIntentReadyToPayAgain()
        {
            Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(), CancellationToken.None));
            _signer.Behaviour = () => Task.FromException<string>(new WalletException("no", userDeclined: true));
            Kit.Sync(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Review, _flow.Stage);
            Assert.AreEqual(0, _status.Calls, "nothing was broadcast, so nothing to poll");
        }

        [Test]
        public void ExpiredIntent_IsNotSigned()
        {
            Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(expires: Kit.Now + 1000), CancellationToken.None));
            Kit.Sync(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(TopUpStage.Expired, _flow.Stage);
            Assert.AreEqual(0, _signer.Calls);
        }

        [Test]
        public void IntentWithoutATransaction_CannotBeSigned()
        {
            Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(tx: null), CancellationToken.None));
            Assert.IsFalse(_flow.Intent.HasTx);
            Kit.Sync(_flow.Pay(CancellationToken.None));
            Assert.AreEqual(0, _signer.Calls);
            Assert.AreEqual("NOT_SIGNABLE", _flow.Error.Code);
        }

        [Test]
        public void ConfirmedByTheCityRoom_ClosesTheFlow()
        {
            Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(), CancellationToken.None));
            Assert.IsTrue(_flow.OnPaymentConfirmed(Kit.Intent, 0));
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
            Assert.IsFalse(_flow.OnPaymentConfirmed("another-intent", 0));
        }

        [Test]
        public void NewBookingAfterAConfirmedOne_ResetsTheFlowFirst()
        {
            Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(), CancellationToken.None));
            _flow.OnPaymentConfirmed(Kit.Intent, 0);
            var second = Booking();
            second.BookingId = "0192a1c2-0000-7000-8000-00000000bbbb";
            second.Intent.IntentId = "0192a1c2-0000-7000-8000-00000000cccc";
            Assert.IsTrue(Kit.Sync(AdPayments.Begin(_flow, _gateway, second, CancellationToken.None)));
            Assert.AreEqual(TopUpStage.Review, _flow.Stage);
            Assert.AreEqual(second.BookingId, _flow.Sku);
        }

        [Test]
        public void ABusyFlow_RefusesAnotherPayment()
        {
            Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(), CancellationToken.None));
            var gate = new TaskCompletionSource<string>();
            _signer.Behaviour = () => gate.Task;
            var pay = _flow.Pay(CancellationToken.None);
            Assert.AreEqual(TopUpStage.Signing, _flow.Stage);
            Assert.IsFalse(Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(), CancellationToken.None)));
            _status.Statuses.Enqueue("confirmed");
            gate.SetResult("sig");
            Kit.Sync(pay);
            Assert.AreEqual(TopUpStage.Confirmed, _flow.Stage);
        }

        [Test]
        public void AdPayment_IsStoredAndRestoredUnderItsOwnStore()
        {
            Kit.Sync(AdPayments.Begin(_flow, _gateway, Booking(), CancellationToken.None));
            _status.Statuses.Enqueue("pending");
            Kit.Sync(_flow.Pay(CancellationToken.None)); // stays unconfirmed for the poll window -> Pending
            Assert.AreEqual(TopUpStage.Pending, _flow.Stage);
            Assert.IsNotNull(_store.Json);
            var restored = new TopUpFlow(new AdIntentGateway(_status), _signer, _clock, _store);
            Assert.IsTrue(restored.Resume());
            Assert.AreEqual(Kit.Intent, restored.Intent.IntentId);
            Assert.AreEqual(Kit.Booking, restored.Sku, "the booking id rides in the sku, so the screen can match it");
        }

        [Test]
        public void StatusSource_IsTheSharedIntentRoute()
        {
            _status.Statuses.Enqueue("confirmed");
            var row = Kit.Sync(_gateway.GetIntent(Kit.Intent, CancellationToken.None));
            Assert.AreEqual("confirmed", row.Status);
        }

        // ---------------------------------------------------------------- policy

        [Test]
        public void Policy_DevnetOnly()
        {
            Assert.IsNull(PaymentPolicy.BlockedReason(SolanaCluster.Devnet, true, true));
            Assert.AreEqual("pay.devnetOnly", PaymentPolicy.BlockedReason(SolanaCluster.Mainnet, true, true));
            Assert.AreEqual("pay.noCluster", PaymentPolicy.BlockedReason(SolanaCluster.None, true, true));
            Assert.AreEqual("pay.noWallet", PaymentPolicy.BlockedReason(SolanaCluster.Devnet, false, true));
            Assert.AreEqual("pay.disabled", PaymentPolicy.BlockedReason(SolanaCluster.Devnet, true, false));
            Assert.IsTrue(PaymentPolicy.DevnetOnly);
        }

        [Test]
        public void AdMoney_UsesTheCurrencysDecimals()
        {
            Assert.AreEqual("20 USDC", AdMoney.Usdc("20000000"));
            Assert.AreEqual("2.5 USDC", AdMoney.Usdc("2500000"));
            Assert.AreEqual("0.5 SOL", AdMoney.Format("500000000", "SOL", c => c == "SOL" ? 9 : -1));
            StringAssert.Contains("(raw)", AdMoney.Format("123", "SKR", c => -1), "unknown decimals are shown as raw units, flagged");
            Assert.AreEqual("—", AdMoney.Format(null, "USDC", c => 6));
        }
    }
}
