using System;

namespace CTLife.SocialFeatures
{
    public enum PaidStage
    {
        Draft,          // fan picks minutes
        Creating,       // POST /paid-calls
        ReadyToSign,    // holds the server-built open_session tx
        Signing,        // MWA sign_and_send
        Submitting,     // POST /paid-calls/{id}/submitted {signature}
        Confirming,     // outcome unknown (wallet error after a possible broadcast, unreadable reply): poll GET /paid-calls/{id}
        Ringing,        // escrow funded; creator has 120 s to accept
        Live,
        Ending,         // POST /paid-calls/{id}/end sent; waiting for settled/refunded
        Settled,
        Refunded,
        Failed,
    }

    /// <summary>
    /// Paid video call (feature #25), both sides, on top of the server's `PaidCall.status`
    /// (awaiting_payment | ringing | live | settled | refunded), which always wins over local guesses. Pure; tested.
    ///
    /// Money rule: the client never computes an amount. It shows the server's `creatorAmount`/`refundAmount` (USDC base
    /// units) and `minutesUsed`; before signing, the wallet shows what the transaction moves.
    /// Signing rule: a transaction is signed at most once. A wallet error after a possible broadcast moves to Confirming
    /// (poll), never to a second signature. Devnet only: the controller refuses to sign unless the build targets devnet.
    /// </summary>
    public sealed class PaidCallFlow
    {
        /// <summary>ct_escrow `accept_window_secs` (120) and the solana agent's advice to stop offering Accept ~10 s early.</summary>
        public const double AcceptWindowMs = 120_000, AcceptCutoffMs = 110_000;
        /// <summary>[Inference] Blockhash lifetime: a built transaction older than this is discarded instead of signed.</summary>
        public const double TxMaxAgeMs = 60_000;
        /// <summary>How long after the window the fan keeps polling before saying "refund pending".</summary>
        public const double RefundGraceMs = 60_000;

        public readonly bool IsFan;
        public readonly string PeerId; // creator (fan side) or fan (creator side)
        public int Minutes { get; private set; }
        public string CallId { get; private set; }
        public string Tx { get; private set; }
        public string SessionPda { get; private set; }
        public string Signature { get; private set; }
        public PaidStage Stage { get; private set; }
        public PaidCallRow Server { get; private set; }
        public string ErrorKey { get; set; }
        public double TxAtMs { get; private set; }
        public double RingingSinceMs { get; private set; } = double.NaN;
        public RtcGrant EmbeddedGrant { get; private set; }
        private bool _signedOnce;

        public readonly RetainedKey CreateKey = new RetainedKey();
        public readonly RetainedKey SubmitKey = new RetainedKey();
        public readonly RetainedKey AcceptKey = new RetainedKey();
        public readonly RetainedKey DeclineKey = new RetainedKey();
        public readonly RetainedKey EndKey = new RetainedKey();

        private PaidCallFlow(bool fan, string peer) { IsFan = fan; PeerId = peer; }

        public static PaidCallFlow ForFan(string creatorId) => new PaidCallFlow(true, creatorId) { Stage = PaidStage.Draft };

        /// <summary>
        /// A fan's call whose signing started before the app was killed (persisted ids only). It can never be signed again;
        /// the server's status decides what happened.
        /// </summary>
        public static PaidCallFlow ResumeFan(string creatorId, string callId) =>
            new PaidCallFlow(true, creatorId) { CallId = callId, Stage = PaidStage.Confirming, _signedOnce = true, ErrorKey = "paid.resumed" };

        /// <summary>Gives up waiting on an unconfirmed payment (the persisted record is cleared by the caller).</summary>
        public void Abandon()
        {
            if (Stage != PaidStage.Confirming) return;
            Stage = PaidStage.Failed;
            ErrorKey = "paid.abandoned";
        }

        /// <summary>Creator side, from city `call_invite {callId, fromUserId, paid: true}`.</summary>
        public static PaidCallFlow ForCreator(string callId, string fanId, double nowMs) =>
            new PaidCallFlow(false, fanId) { CallId = callId, Stage = PaidStage.Ringing, RingingSinceMs = nowMs };

        public bool Terminal => Stage == PaidStage.Settled || Stage == PaidStage.Refunded || Stage == PaidStage.Failed;

        // ---------------------------------------------------------------- fan

        public string BeginCreate(string minutesInput)
        {
            if (!IsFan || (Stage != PaidStage.Draft && Stage != PaidStage.Failed)) return "paid.busy";
            if (!UsdcUnits.TryMinutes(minutesInput, out var m)) return "paid.minutesRange";
            Minutes = m;
            Stage = PaidStage.Creating;
            ErrorKey = null;
            return null;
        }

        public void Created(PaidCallStart start, double nowMs)
        {
            if (Stage != PaidStage.Creating) return;
            CreateKey.Complete();
            CallId = start.CallId;
            SessionPda = start.SessionPda;
            EmbeddedGrant = start.EmbeddedGrant;
            if (string.IsNullOrWhiteSpace(start.Tx)) { Stage = PaidStage.Failed; ErrorKey = "paid.noTx"; return; }
            Tx = start.Tx;
            TxAtMs = nowMs;
            Stage = PaidStage.ReadyToSign;
        }

        public void CreateFailed(SocialError e)
        {
            if (Stage != PaidStage.Creating) return;
            if (!e.OutcomeUnknown) CreateKey.Complete(); // unknown: the retry reuses the key and the server replays
            Stage = PaidStage.Draft;
            ErrorKey = e.OutcomeUnknown ? "paid.createUnknown" : e.CopyKey;
        }

        public bool TxFresh(double nowMs) => Tx != null && nowMs - TxAtMs < TxMaxAgeMs;

        public bool CanSign(double nowMs) => IsFan && Stage == PaidStage.ReadyToSign && !_signedOnce && TxFresh(nowMs);

        /// <summary>The built transaction aged out before signing. The fan starts over (a new call; the old one is unpaid).</summary>
        public void DiscardStaleTx(double nowMs)
        {
            if (Stage != PaidStage.ReadyToSign || TxFresh(nowMs)) return;
            Tx = null; CallId = null; SessionPda = null; EmbeddedGrant = null;
            Stage = PaidStage.Draft;
            ErrorKey = "paid.txExpired";
        }

        public bool BeginSign(double nowMs)
        {
            if (!CanSign(nowMs)) return false;
            Stage = PaidStage.Signing;
            ErrorKey = null;
            return true;
        }

        public void Signed(string signature)
        {
            if (Stage != PaidStage.Signing) return;
            _signedOnce = true;
            Signature = signature;
            Stage = PaidStage.Submitting;
        }

        /// <summary>The wallet says the fan declined: nothing was broadcast, the same transaction may be signed later.</summary>
        public void SignDeclined()
        {
            if (Stage != PaidStage.Signing) return;
            Stage = PaidStage.ReadyToSign;
            ErrorKey = "err.walletDeclined";
        }

        /// <summary>Any other wallet failure: the transaction may have been broadcast. Never sign again; ask the server.</summary>
        public void SignFailedUnknown()
        {
            if (Stage != PaidStage.Signing) return;
            _signedOnce = true;
            Stage = PaidStage.Confirming;
            ErrorKey = "paid.walletUnknown";
        }

        public void SubmitFailed(SocialError e)
        {
            if (Stage != PaidStage.Submitting) return;
            if (e.OutcomeUnknown) { ErrorKey = "paid.submitRetry"; return; } // retry with the same key
            SubmitKey.Complete();
            Stage = PaidStage.Confirming; // the escrow transaction is on chain either way; the server decides
            ErrorKey = e.CopyKey;
        }

        // ---------------------------------------------------------------- creator

        public bool CanAccept(double nowMs) => !IsFan && Stage == PaidStage.Ringing && !double.IsNaN(RingingSinceMs) && nowMs - RingingSinceMs < AcceptCutoffMs;

        public bool BeginAccept(double nowMs)
        {
            if (!CanAccept(nowMs)) return false;
            ErrorKey = null;
            return true;
        }

        public void AcceptFailed(SocialError e)
        {
            if (!e.OutcomeUnknown) AcceptKey.Complete();
            ErrorKey = e.OutcomeUnknown ? "paid.acceptRetry" : e.CopyKey;
        }

        // ---------------------------------------------------------------- both

        public bool CanEnd => Stage == PaidStage.Live;

        public void BeginEnd() { if (Stage == PaidStage.Live) Stage = PaidStage.Ending; }

        /// <summary>The window passed on the creator's phone without an answer (the server refunds the fan).</summary>
        public bool AcceptWindowOver(double nowMs) => Stage == PaidStage.Ringing && !double.IsNaN(RingingSinceMs) && nowMs - RingingSinceMs >= AcceptWindowMs;

        public bool RefundOverdue(double nowMs) => Stage == PaidStage.Ringing && !double.IsNaN(RingingSinceMs) && nowMs - RingingSinceMs >= AcceptWindowMs + RefundGraceMs;

        public double AcceptSecondsLeft(double nowMs)
        {
            if (Stage != PaidStage.Ringing || double.IsNaN(RingingSinceMs)) return 0;
            var limit = IsFan ? AcceptWindowMs : AcceptCutoffMs;
            return Math.Max(0, (limit - (nowMs - RingingSinceMs)) / 1000.0);
        }

        /// <summary>Applies the server's status. Terminal stages never move back. Returns true when the stage changed.</summary>
        public bool ApplyServer(PaidCallRow row, double nowMs)
        {
            if (row == null || (CallId != null && !SocialWire.SameId(row.CallId, CallId))) return false;
            if (Terminal) return false; // a settled/refunded receipt is final, later rows do not replace it
            if (CallId == null) CallId = row.CallId;
            Server = row;
            var before = Stage;
            switch (row.Status)
            {
                case PaidCallStatus.AwaitingPayment:
                    // Still unpaid. Fan: stay where the flow is (signing/submitting/confirming); the webhook may be behind.
                    if (Stage == PaidStage.Submitting) { SubmitKey.Complete(); Stage = PaidStage.Confirming; }
                    break;
                case PaidCallStatus.Ringing:
                    if (Stage != PaidStage.Ringing) { Stage = PaidStage.Ringing; if (double.IsNaN(RingingSinceMs)) RingingSinceMs = nowMs; }
                    SubmitKey.Complete();
                    break;
                case PaidCallStatus.Live:
                    if (Stage != PaidStage.Ending) Stage = PaidStage.Live;
                    SubmitKey.Complete(); AcceptKey.Complete();
                    break;
                case PaidCallStatus.Settled:
                    Stage = PaidStage.Settled; EndKey.Complete();
                    break;
                case PaidCallStatus.Refunded:
                    Stage = PaidStage.Refunded; EndKey.Complete(); DeclineKey.Complete();
                    break;
            }
            if (Stage != before) ErrorKey = null;
            return Stage != before;
        }

        /// <summary>Polling cadence for GET /paid-calls/{id}; NaN when no poll is needed.</summary>
        public double PollIntervalMs()
        {
            switch (Stage)
            {
                case PaidStage.Confirming:
                case PaidStage.Ringing:
                case PaidStage.Ending:
                    return 2000;
                case PaidStage.Live:
                    return 5000;
                default:
                    return double.NaN;
            }
        }
    }
}
