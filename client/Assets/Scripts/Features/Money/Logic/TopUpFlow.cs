using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    /// <summary>POST /payments/intents and GET /payments/intents/{id} (contract section 4, Payments).</summary>
    public interface ITopUpGateway
    {
        Task<PaymentIntentRow> CreateIntent(string kind, string sku, string currency, string idempotencyKey, CancellationToken ct);
        Task SubmitIntent(string intentId, string signature, string idempotencyKey, CancellationToken ct);
        Task<IntentStatusRow> GetIntent(string intentId, CancellationToken ct);
    }

    /// <summary>Server-corrected time and delays; replaced by a fake clock in tests.</summary>
    public interface IMoneyClock
    {
        /// <summary>Epoch milliseconds in server time (contract: clients correct with `welcome.serverNow`).</summary>
        double NowMs { get; }
        Task Delay(int milliseconds, CancellationToken ct);
    }

    /// <summary>Persists the one in-flight top-up so a payment survives the app being killed while the wallet is open.</summary>
    public interface IPendingIntentStore
    {
        string Load();
        void Save(string json);
        void Clear();
    }

    public enum TopUpStage
    {
        /// <summary>Pick a pack and a currency.</summary>
        Choose,
        /// <summary>POST /payments/intents in flight.</summary>
        Creating,
        /// <summary>Intent created; the player reviews amount, bonus and expiry before opening the wallet.</summary>
        Review,
        /// <summary>MWA sign_and_send_transactions is open.</summary>
        Signing,
        /// <summary>Polling GET /payments/intents/{id} every 1 s for up to 30 s.</summary>
        Confirming,
        Confirmed,
        /// <summary>Still unconfirmed after the poll window; the city room's payment_confirmed closes it later.</summary>
        Pending,
        Expired,
        Failed,
        Error,
    }

    /// <summary>
    /// The Cash top-up flow (feature #13), as a pure state machine over an injected gateway, signer and clock:
    /// create intent (retained Idempotency-Key) → review → sign `tx` with MWA → poll 1 s / 30 s → confirmed | pending.
    /// `payment_confirmed {intentId, cashCents}` from the city room confirms at any point. Amounts come from the server's
    /// intent; nothing is computed here.
    /// </summary>
    public sealed class TopUpFlow
    {
        public const int PollIntervalMs = 1000;
        public const int PollWindowMs = 30000;
        /// <summary>Do not open the wallet for an intent that expires within this margin.</summary>
        public const int ExpiryMarginMs = 5000;
        private const string IntentsPath = "/payments/intents";

        private readonly ITopUpGateway _gateway;
        private readonly ITransactionSigner _signer;
        private readonly IMoneyClock _clock;
        private readonly IPendingIntentStore _store;
        private readonly RetainedWrite _create;
        private readonly Func<string> _newKey;
        private string _submissionKey;
        private int _pollGeneration;

        public TopUpStage Stage { get; private set; } = TopUpStage.Choose;
        public string Sku { get; private set; }
        public string Currency { get; private set; }
        public PaymentIntentRow Intent { get; private set; }
        public string Signature { get; private set; }
        /// <summary>The server acknowledged the submitted signature; this is not payment confirmation.</summary>
        public bool SubmissionReported { get; private set; }
        /// <summary>Cash credited according to `payment_confirmed` (server value), when that message closed the flow.</summary>
        public long? CreditedCashCents { get; private set; }
        public MoneyError Error { get; private set; }
        /// <summary>The wallet failed after it may have broadcast; the same intent must not be signed again.</summary>
        public bool SignUncertain { get; private set; }
        /// <summary>GET /payments/intents/{id} is missing on this server (404); only payment_confirmed can close the flow.</summary>
        public bool StatusUnavailable { get; private set; }
        public int Polls { get; private set; }
        public double PollStartedMs { get; private set; }

        public event Action Changed;

        public bool Busy => Stage == TopUpStage.Creating || Stage == TopUpStage.Signing || Stage == TopUpStage.Confirming;
        public bool CanStartNew => Stage == TopUpStage.Choose || Stage == TopUpStage.Error || Stage == TopUpStage.Expired ||
                                   Stage == TopUpStage.Failed || Stage == TopUpStage.Confirmed;

        public TopUpFlow(ITopUpGateway gateway, ITransactionSigner signer, IMoneyClock clock, IPendingIntentStore store = null, Func<string> newKey = null)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _signer = signer;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _store = store;
            _newKey = newKey ?? (() => Guid.NewGuid().ToString("D"));
            _create = new RetainedWrite(_newKey);
        }

        /// <summary>POST /payments/intents `{kind: topup, sku, currency}`. A retry after an unknown outcome reuses the key.</summary>
        public async Task Create(string sku, string currency, CancellationToken ct)
        {
            if (!CanStartNew || !Wire.IsSafeId(sku) || !Wire.IsSafeId(currency)) return;
            var key = _create.KeyFor(IntentsPath, sku + "|" + currency);
            Stage = TopUpStage.Creating;
            Sku = sku;
            Currency = currency;
            Error = null;
            Intent = null;
            Signature = null;
            _submissionKey = null;
            SubmissionReported = false;
            SignUncertain = false;
            StatusUnavailable = false;
            CreditedCashCents = null;
            Polls = 0;
            Notify();
            PaymentIntentRow intent;
            try
            {
                intent = await _gateway.CreateIntent("topup", sku, currency, key, ct);
                if (intent == null || string.IsNullOrEmpty(intent.IntentId)) throw new FormatException("Missing intent");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The request may have reached the server; the retained key makes the next attempt replay it.
                Stage = TopUpStage.Choose;
                Notify();
                throw;
            }
            catch (Exception e)
            {
                var err = MoneyErrors.From(e, write: true);
                if (!err.OutcomeUnknown) _create.Complete();
                Error = err;
                Stage = TopUpStage.Error;
                Notify();
                return;
            }
            _create.Complete();
            Intent = intent;
            Stage = TopUpStage.Review;
            Notify();
        }

        /// <summary>Signs the intent's `tx` with MWA, then polls. Only from Review.</summary>
        public async Task Pay(CancellationToken ct)
        {
            if (Stage != TopUpStage.Review || Intent == null) return;
            if (!Intent.HasTx || _signer == null)
            {
                Error = new MoneyError { Code = "NOT_SIGNABLE", Key = "topup.noTx" };
                Notify();
                return;
            }
            if (Intent.ExpiresAtMs.HasValue && _clock.NowMs >= Intent.ExpiresAtMs.Value - ExpiryMarginMs)
            {
                Error = new MoneyError { Code = "PAYMENT_EXPIRED", Key = "err.paymentExpired" };
                Stage = TopUpStage.Expired;
                Notify();
                return;
            }
            Stage = TopUpStage.Signing;
            Error = null;
            Persist(signingStarted: true);
            Notify();
            string signature = null;
            try
            {
                signature = await _signer.SignAndSendBase64(Intent.Tx, ct);
            }
            catch (WalletException w) when (w.UserDeclined)
            {
                // Declined in the wallet: nothing was broadcast, the intent stays valid.
                ClearStore();
                Error = MoneyErrors.From(w, write: true);
                Stage = TopUpStage.Review;
                Notify();
                return;
            }
            catch (Exception e)
            {
                // The wallet may have broadcast before failing (or the app lost the result). Find out by polling;
                // never sign this intent again.
                SignUncertain = true;
                Error = MoneyErrors.From(e is OperationCanceledException ? new WalletException("cancelled", false, e) : e, write: true);
            }
            // A realtime confirmation can arrive before the wallet returns. Do not reopen that completed flow.
            if (Stage == TopUpStage.Confirmed) return;
            if (!string.IsNullOrEmpty(signature)) Signature = signature;
            Persist(signingStarted: true);
            await PollWindow(ct);
        }

        /// <summary>Another 30 s poll window for a Pending payment ("Check again", or the Bank app reopening).</summary>
        public Task CheckAgain(CancellationToken ct)
        {
            if (Stage != TopUpStage.Pending || Intent == null) return Task.CompletedTask;
            return PollWindow(ct);
        }

        /// <summary>City room `payment_confirmed {intentId, cashCents}` for this player. Returns true if it closed this flow.</summary>
        public bool OnPaymentConfirmed(string intentId, long cashCents)
        {
            if (Intent == null || string.IsNullOrEmpty(intentId) || Intent.IntentId != intentId || Stage == TopUpStage.Confirmed) return false;
            _pollGeneration++;
            CreditedCashCents = cashCents;
            Error = null;
            Stage = TopUpStage.Confirmed;
            ClearStore();
            Notify();
            return true;
        }

        /// <summary>Back to the pack list. Unresolved wallet-started payments retain their recovery evidence.</summary>
        public void Reset()
        {
            if (Stage == TopUpStage.Signing || Stage == TopUpStage.Confirming || Stage == TopUpStage.Pending) return;
            _pollGeneration++;
            if (Stage != TopUpStage.Error || !(Error?.OutcomeUnknown ?? false)) _create.Complete();
            ClearStore();
            Stage = TopUpStage.Choose;
            Intent = null;
            Signature = null;
            _submissionKey = null;
            SubmissionReported = false;
            Error = null;
            SignUncertain = false;
            StatusUnavailable = false;
            CreditedCashCents = null;
            Notify();
        }

        /// <summary>Restores a payment whose wallet step started before the app was closed. Returns true when one was restored.</summary>
        public bool Resume()
        {
            if (Stage != TopUpStage.Choose || _store == null) return false;
            string json;
            try { json = _store.Load(); }
            catch (Exception) { return false; }
            if (string.IsNullOrEmpty(json)) return false;
            PendingIntent p;
            try { p = PendingIntent.Parse(json); }
            catch (Exception)
            {
                ClearStore();
                return false;
            }
            // Never signed: nothing can have moved. Elapsed time (including the server's dedupe window) says
            // nothing about whether a wallet-started transfer landed; retain it until the server resolves it.
            if (!p.SigningStarted)
            {
                ClearStore();
                return false;
            }
            Intent = new PaymentIntentRow
            {
                IntentId = p.IntentId, Currency = p.Currency, Amount = p.Amount, CashCents = p.CashCents, BonusBps = p.BonusBps, ExpiresAtMs = p.ExpiresAtMs,
            };
            Sku = p.Sku;
            Currency = p.Currency;
            Signature = p.Signature;
            _submissionKey = p.SubmissionKey;
            SubmissionReported = p.SubmissionReported;
            SignUncertain = string.IsNullOrEmpty(p.Signature);
            Stage = TopUpStage.Pending;
            Notify();
            return true;
        }

        private async Task PollWindow(CancellationToken ct)
        {
            int generation = ++_pollGeneration;
            Stage = TopUpStage.Confirming;
            PollStartedMs = _clock.NowMs;
            Notify();
            try { await PollWindowCore(generation, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A cancelled view/session must remain recoverable, never stuck in the busy state.
                if (generation == _pollGeneration && Stage == TopUpStage.Confirming) ToPending();
                throw;
            }
        }

        private async Task PollWindowCore(int generation, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(Signature) && !SubmissionReported)
            {
                _submissionKey ??= _newKey();
                Persist(signingStarted: true); // retain before sending, including across process restarts
                try
                {
                    await _gateway.SubmitIntent(Intent.IntentId, Signature, _submissionKey, ct);
                    if (generation != _pollGeneration || Stage != TopUpStage.Confirming) return;
                    SubmissionReported = true;
                    Persist(signingStarted: true);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    // The wallet already broadcast. Keep the same signature/key for Check again; a webhook can
                    // still confirm while this report is offline. Never ask the wallet to sign a second transfer.
                }
            }
            while (true)
            {
                if (generation != _pollGeneration || Stage != TopUpStage.Confirming) return;
                IntentStatusRow status = null;
                Exception failure = null;
                try { status = await _gateway.GetIntent(Intent.IntentId, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e) { failure = e; }
                Polls++;
                if (generation != _pollGeneration || Stage != TopUpStage.Confirming) return;
                if (status != null)
                {
                    if (!string.IsNullOrEmpty(status.Signature) && string.IsNullOrEmpty(Signature)) Signature = status.Signature;
                    switch (status.Status)
                    {
                        case "confirmed":
                            Finish(TopUpStage.Confirmed, null);
                            return;
                        case "expired":
                            Finish(TopUpStage.Expired, new MoneyError { Code = "PAYMENT_EXPIRED", Key = "err.paymentExpired" });
                            return;
                        case "failed":
                            Finish(TopUpStage.Failed, new MoneyError { Code = "PAYMENT_FAILED", Key = "topup.failed" });
                            return;
                    }
                }
                else if (failure is ApiException api)
                {
                    if (api.Is("PAYMENT_EXPIRED") || api.Status == 410)
                    {
                        Finish(TopUpStage.Expired, MoneyErrors.From(api, write: false));
                        return;
                    }
                    if (api.Is("PAYMENT_UNDERPAID"))
                    {
                        Finish(TopUpStage.Failed, MoneyErrors.From(api, write: false));
                        return;
                    }
                    if (api.Status == 404 || api.Status == 501)
                    {
                        StatusUnavailable = true;
                        ToPending();
                        return;
                    }
                }
                if (_clock.NowMs - PollStartedMs >= PollWindowMs)
                {
                    ToPending();
                    return;
                }
                await _clock.Delay(PollIntervalMs, ct);
            }
        }

        private void Finish(TopUpStage stage, MoneyError error)
        {
            _pollGeneration++;
            Stage = stage;
            if (error != null) Error = error;
            else if (stage == TopUpStage.Confirmed) Error = null;
            ClearStore();
            Notify();
        }

        private void ToPending()
        {
            Stage = TopUpStage.Pending;
            Notify();
        }

        private void Persist(bool signingStarted)
        {
            if (_store == null || Intent == null) return;
            try
            {
                _store.Save(new PendingIntent
                {
                    IntentId = Intent.IntentId, Sku = Sku, Currency = Currency ?? Intent.Currency, Amount = Intent.Amount,
                    CashCents = Intent.CashCents, BonusBps = Intent.BonusBps, ExpiresAtMs = Intent.ExpiresAtMs,
                    Signature = Signature, SigningStarted = signingStarted, SavedAtMs = _clock.NowMs,
                    SubmissionKey = _submissionKey, SubmissionReported = SubmissionReported,
                }.Serialize());
            }
            catch (Exception) { /* Persistence is best effort; the live flow continues. */ }
        }

        private void ClearStore()
        {
            try { _store?.Clear(); }
            catch (Exception) { }
        }

        private void Notify()
        {
            try { Changed?.Invoke(); }
            catch (Exception) { /* A view failure must not break the payment state machine. */ }
        }
    }

    /// <summary>What is persisted for an in-flight top-up: ids and server display values only, never the transaction or tokens.</summary>
    public sealed class PendingIntent
    {
        public string IntentId, Sku, Currency, Amount, Signature, SubmissionKey;
        public long? CashCents, ExpiresAtMs;
        public int? BonusBps;
        public bool SigningStarted;
        public bool SubmissionReported;
        public double SavedAtMs;

        public string Serialize() => new JObject
        {
            ["intentId"] = IntentId, ["sku"] = Sku, ["currency"] = Currency, ["amount"] = Amount, ["signature"] = Signature,
            ["cashCents"] = CashCents, ["bonusBps"] = BonusBps, ["expiresAtMs"] = ExpiresAtMs,
            ["signingStarted"] = SigningStarted, ["savedAtMs"] = SavedAtMs,
            ["submissionKey"] = SubmissionKey, ["submissionReported"] = SubmissionReported,
        }.ToString(Formatting.None);

        public static PendingIntent Parse(string json)
        {
            var o = JObject.Parse(json);
            var p = new PendingIntent
            {
                IntentId = Wire.SafeId(o["intentId"], "intentId"),
                Sku = Wire.Str(o["sku"]),
                Currency = Wire.Str(o["currency"]),
                Amount = Wire.Str(o["amount"]),
                Signature = Wire.Str(o["signature"]),
                SubmissionKey = Wire.Str(o["submissionKey"]),
                SubmissionReported = o["submissionReported"]?.Type == JTokenType.Boolean && (bool)o["submissionReported"],
                CashCents = Wire.OptLong(o["cashCents"]),
                BonusBps = Wire.OptInt(o["bonusBps"]),
                ExpiresAtMs = Wire.OptLong(o["expiresAtMs"]),
                SigningStarted = o["signingStarted"]?.Type == JTokenType.Boolean && (bool)o["signingStarted"],
                SavedAtMs = o["savedAtMs"]?.Type == JTokenType.Float || o["savedAtMs"]?.Type == JTokenType.Integer ? (double)o["savedAtMs"] : 0,
            };
            return p;
        }
    }
}
