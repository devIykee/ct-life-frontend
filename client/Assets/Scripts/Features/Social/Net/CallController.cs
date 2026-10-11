using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    public sealed class IncomingCall
    {
        public CallSession Call;
        /// <summary>Creator side of a paid call (`call_invite.paid`), else null.</summary>
        public PaidCallFlow Paid;
        public bool Working;
    }

    public sealed class RecentCall
    {
        public string UserId, Kind; // outgoing | incoming | missed
        public bool Paid;
        public long AtMs;
    }

    /// <summary>
    /// Phone calls (features #24, #25): one active call with its media (<see cref="VideoRoom"/>), a queue of ringing invites
    /// from the city `call_invite`, the fan's paid-call flow (create → sign with MWA on devnet → submitted → ringing → live →
    /// end → settled/refunded, all from GET /paid-calls/{id}), the creator's accept/decline within the 120 s window, and
    /// creator settings (PUT /creator).
    /// </summary>
    public sealed class CallController : IDisposable
    {
        private readonly SocialHub _hub;
        public CallSession Active { get; private set; }
        public VideoRoom Media { get; private set; }
        public PaidCallFlow Paid { get; private set; }
        public readonly List<IncomingCall> Incoming = new List<IncomingCall>();
        public readonly List<RecentCall> Recents = new List<RecentCall>();
        public string Message { get; set; }
        public bool Dialing { get; private set; }
        public bool PaidWorking { get; private set; }
        public CreatorProfileRow Creator { get; private set; }

        private readonly HashSet<string> _seenInvites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly RetainedKey _dialKey = new RetainedKey();
        private RtcGrant _embedded;
        private bool _joining, _disposed, _paidPolling;
        private double _nextRing, _paidPollAt, _submitRetryAt, _confirmingSince = double.NaN;
        private int _submitAttempts;
        public event Action Changed;

        public CallController(SocialHub hub)
        {
            _hub = hub;
            LoadDevice();
        }

        private void Notify() { if (!_disposed) { Changed?.Invoke(); _hub.NotifyChanged(); } }

        /// <summary>The phone's call slot is taken (dialing, connecting, waiting or live).</summary>
        public bool Busy => Active != null && Active.Busy;

        /// <summary>Devnet only (task brief): paid calls never sign on mainnet or without a cluster.</summary>
        public static bool PaidCallsAllowedHere => Env.Current != null && Env.Current.cluster == SolanaCluster.Devnet;

        // ---------------------------------------------------------------- device memory

        private void LoadDevice()
        {
            try
            {
                var creator = _hub.Store.GetJson("creator");
                if (creator != null) Creator = SocialParse.Creator(creator);
                var recents = JToken.Parse(string.IsNullOrEmpty(_hub.Store.Get("recentCalls")) ? "[]" : _hub.Store.Get("recentCalls")) as JArray;
                if (recents != null)
                    foreach (var r in recents.OfType<JObject>().Take(20))
                    {
                        var id = SocialWire.Str(r["userId"]);
                        if (!SocialWire.IsSafeId(id)) continue;
                        Recents.Add(new RecentCall { UserId = id, Kind = SocialWire.Str(r["kind"]) ?? "outgoing", Paid = SocialWire.BoolOr(r["paid"], false), AtMs = SocialWire.OptLong(r["at"]) ?? 0 });
                    }
                var inflight = _hub.Store.GetJson("paidInFlight");
                var callId = SocialWire.Str(inflight?["callId"]);
                var creatorId = SocialWire.Str(inflight?["creatorId"]);
                if (SocialWire.IsSafeId(callId) && SocialWire.IsSafeId(creatorId))
                {
                    Paid = PaidCallFlow.ResumeFan(creatorId, callId);
                    _confirmingSince = SocialHub.NowMs;
                }
            }
            catch (Exception e) { Log.Warn("calls", "Device memory unreadable: " + e.GetType().Name); }
        }

        private void AddRecent(string userId, string kind, bool paid)
        {
            if (string.IsNullOrEmpty(userId)) return;
            Recents.Insert(0, new RecentCall { UserId = userId, Kind = kind, Paid = paid, AtMs = (long)ServerClock.NowMs });
            while (Recents.Count > 20) Recents.RemoveAt(Recents.Count - 1);
            var arr = new JArray(Recents.Select(r => new JObject { ["userId"] = r.UserId, ["kind"] = r.Kind, ["paid"] = r.Paid, ["at"] = r.AtMs }));
            _hub.Store.Set("recentCalls", arr.ToString(Newtonsoft.Json.Formatting.None));
        }

        private void PersistPaid(bool on)
        {
            if (on && Paid != null && Paid.CallId != null) _hub.Store.SetJson("paidInFlight", new JObject { ["callId"] = Paid.CallId, ["creatorId"] = Paid.PeerId });
            else _hub.Store.Delete("paidInFlight");
        }

        // ---------------------------------------------------------------- free calls: outgoing

        public void StartCall(IEnumerable<string> userIds)
        {
            if (_disposed || Dialing) return;
            if (Busy) { Message = "calls.alreadyInCall"; Notify(); return; }
            if (!Features.IsAvailable(Features.Voice)) { Message = "calls.voiceDegraded"; Notify(); return; }
            var err = CallSession.ValidateInvitees(userIds, _hub.Me, out var invitees);
            if (err != null) { Message = err; Notify(); return; }
            if (invitees.Any(_hub.Blocks.Contains)) { Message = "calls.blockedInvitee"; Notify(); return; }
            var call = Active = CallSession.StartOutgoing(invitees, SocialHub.NowMs);
            Dialing = true; Message = null;
            foreach (var u in invitees) AddRecent(u, "outgoing", false);
            Notify();
            _hub.Run(async ct =>
            {
                VideoCallStart start;
                try { start = await _hub.Api.StartVideoCall(invitees, _hub.Me, _dialKey, ct); }
                finally { Dialing = false; }
                _dialKey.Complete();
                if (!ReferenceEquals(Active, call) || call.IsOver) return; // hung up while dialing
                call.Dialed(start.CallId, SocialHub.NowMs);
                JoinMedia(call, start.EmbeddedGrant);
                Notify();
            }, e =>
            {
                Dialing = false;
                if (!e.OutcomeUnknown) _dialKey.Complete();
                if (ReferenceEquals(Active, call)) call.End(CallEnd.Failed, SocialHub.NowMs);
                Message = e.OutcomeUnknown ? "calls.dialUnknown" : e.CopyKey;
                Notify();
            });
        }

        // ---------------------------------------------------------------- invites

        public void OnInvite(CallInviteMessage m)
        {
            if (_disposed || m == null || !SocialWire.IsSafeId(m.callId) || !SocialWire.IsSafeId(m.fromUserId)) return;
            if (!_seenInvites.Add(m.callId)) return; // reconnects can replay
            if (_hub.Blocks.Contains(m.fromUserId)) return;
            var now = SocialHub.NowMs;
            if (Busy || Incoming.Count >= 3 || !Features.IsAvailable(Features.Voice))
            {
                AddRecent(m.fromUserId, "missed", m.paid);
                Bus.Publish(ToastSignal.Text(SocialStrings.F("calls.missedBusy", _hub.Names.Name(m.fromUserId))));
                return;
            }
            var call = CallSession.Incoming(m.callId, m.fromUserId, m.paid, now,
                m.paid ? PaidCallFlow.AcceptWindowMs : CallSession.RingMs, m.paid ? PaidCallFlow.AcceptCutoffMs : CallSession.RingMs);
            Incoming.Add(new IncomingCall { Call = call, Paid = m.paid ? PaidCallFlow.ForCreator(m.callId, m.fromUserId, now) : null });
            _nextRing = 0;
            Notify();
        }

        public IncomingCall FindIncoming(string callId) => Incoming.FirstOrDefault(i => SocialWire.SameId(i.Call.CallId, callId));

        public void Accept(string callId)
        {
            var inc = FindIncoming(callId);
            if (inc == null || inc.Working) return;
            var now = SocialHub.NowMs;
            if (Busy) { Message = "calls.alreadyInCall"; Notify(); return; }
            if (!inc.Call.CanAccept(now)) { Message = "calls.tooLate"; Notify(); return; }
            if (inc.Paid == null)
            {
                inc.Call.Accept(now);
                Incoming.Remove(inc);
                Active = inc.Call;
                Active.Joining(now);
                AddRecent(inc.Call.FromUserId, "incoming", false);
                JoinMedia(Active, null);
                Notify();
                return;
            }
            // Paid: the server must confirm (POST accept) before joining; the 120 s window keeps running meanwhile.
            if (!inc.Paid.BeginAccept(now)) { Message = "calls.tooLate"; Notify(); return; }
            inc.Working = true; Notify();
            _hub.Run(async ct =>
            {
                PaidCallRow row;
                try { row = await _hub.Api.AcceptPaidCall(callId, inc.Paid.AcceptKey, ct); }
                finally { inc.Working = false; }
                inc.Paid.ApplyServer(row, SocialHub.NowMs);
                if (inc.Paid.Stage == PaidStage.Live)
                {
                    Incoming.Remove(inc);
                    inc.Call.Accept(SocialHub.NowMs);
                    inc.Call.Joining(SocialHub.NowMs);
                    Active = inc.Call;
                    Paid = inc.Paid;
                    _paidPollAt = SocialHub.NowMs + Paid.PollIntervalMs();
                    AddRecent(inc.Call.FromUserId, "incoming", true);
                    JoinMedia(Active, null);
                }
                else if (inc.Paid.Terminal) { Incoming.Remove(inc); inc.Call.End(CallEnd.Expired, SocialHub.NowMs); Message = "paid.alreadyRefunded"; }
                else Message = "paid.notLive";
                Notify();
            }, e => { inc.Working = false; inc.Paid.AcceptFailed(e); Message = inc.Paid.ErrorKey; Notify(); });
        }

        public void Decline(string callId)
        {
            var inc = FindIncoming(callId);
            if (inc == null || inc.Working) return;
            var now = SocialHub.NowMs;
            if (inc.Paid == null)
            {
                inc.Call.Decline(now);
                Incoming.Remove(inc);
                AddRecent(inc.Call.FromUserId, "missed", false);
                Notify();
                return;
            }
            inc.Working = true; Notify();
            _hub.Run(async ct =>
            {
                PaidCallRow row;
                try { row = await _hub.Api.DeclinePaidCall(callId, inc.Paid.DeclineKey, ct); }
                finally { inc.Working = false; }
                inc.Paid.ApplyServer(row, SocialHub.NowMs);
                inc.Call.Decline(SocialHub.NowMs);
                Incoming.Remove(inc);
                AddRecent(inc.Call.FromUserId, "missed", true);
                Notify();
            }, e =>
            {
                inc.Working = false;
                if (!e.OutcomeUnknown) inc.Paid.DeclineKey.Complete();
                Message = e.OutcomeUnknown ? "paid.declineRetry" : e.CopyKey;
                Notify();
            });
        }

        // ---------------------------------------------------------------- media

        private void JoinMedia(CallSession call, RtcGrant embedded)
        {
            if (_joining || call == null || call.CallId == null || _disposed) return;
            _joining = true;
            if (embedded != null) _embedded = embedded;
            if (call.Phase == CallPhase.Reconnecting) call.ReconnectStarted();
            if (_hub.Spaces.InSpace) _hub.Spaces.Leave(); // one voice context at a time
            _hub.Run(async ct =>
            {
                try
                {
                    RtcGrant grant;
                    try { grant = await _hub.Api.RtcToken("call", call.CallId, ct); }
                    catch (ApiException e) when (SocialError.IsRouteMissing(e) && _embedded != null && _embedded.Usable(ServerClock.NowMs))
                    {
                        grant = _embedded; // backend checkout: no /rtc/token route, but POST /video-calls embedded the caller's grant
                    }
                    if (!ReferenceEquals(Active, call) || call.IsOver) return;
                    if (Services.TryGet<VoiceService>(out var nearby)) await SpacesRequest.Run(async _ => { await nearby.Disconnect(); return true; }, ct, 15000);
                    DisposeMedia();
                    var media = new VideoRoom();
                    media.Changed += Notify;
                    media.Failed += k => { Message = k; Notify(); };
                    media.PeerJoined += id => { if (ReferenceEquals(Media, media)) { call.RemoteJoined(id, SocialHub.NowMs); _hub.Names.Name(id); Bus.Publish(new SfxSignal("sfx_call_connect")); } };
                    media.PeerLeft += id => { if (ReferenceEquals(Media, media)) call.RemoteLeft(id, SocialHub.NowMs); };
                    media.Lost += () => { if (ReferenceEquals(Media, media)) { Media = null; call.MediaLost(SocialHub.NowMs); Message = "calls.reconnecting"; Notify(); } };
                    Media = media;
                    await media.Connect(grant, ct);
                    if (!ReferenceEquals(Media, media) || call.IsOver) { media.Dispose(); return; }
                    foreach (var p in media.Peers) call.RemoteJoined(p.Identity, SocialHub.NowMs);
                    call.MediaConnected(SocialHub.NowMs);
                    Message = null;
                }
                finally { _joining = false; Notify(); }
            }, e =>
            {
                _joining = false;
                DisposeMedia();
                if (!ReferenceEquals(Active, call) || call.IsOver) return;
                if (e.Kind == ErrorKind.RouteMissing) { Finish(CallEnd.Failed, "calls.noVoiceRoute"); return; }
                if (e.Kind == ErrorKind.Definitive && !e.Is("RATE_LIMITED")) { Finish(CallEnd.Failed, e.CopyKey); return; }
                if (call.Phase == CallPhase.Reconnecting) call.ReconnectFailed(SocialHub.NowMs);
                else call.MediaLost(SocialHub.NowMs);
                if (call.IsOver) Finish(call.EndReason, "calls.connectionLost");
                Notify();
            });
        }

        private void DisposeMedia()
        {
            var m = Media; Media = null;
            m?.Dispose();
        }

        public void ToggleMic() { var m = Media; if (m != null) _hub.Run(ct => m.SetMic(!m.MicOn, ct), e => { Message = e.CopyKey; Notify(); }); }
        public void ToggleCamera() { var m = Media; if (m != null) _hub.Run(ct => m.SetCamera(!m.CameraOn, ct), e => { Message = e.CopyKey; Notify(); }); }
        public void ToggleSpeaker() { if (Media != null) Media.SetSpeakerMuted(!Media.SpeakerMuted); }

        public void Hangup()
        {
            if (Active == null || Active.IsOver) return;
            Finish(CallEnd.HungUp, null);
        }

        /// <summary>Ends the active call: media off, paid calls ask the server to end (it settles), sound, recents.</summary>
        private void Finish(CallEnd reason, string messageKey)
        {
            var call = Active;
            if (call == null) return;
            call.End(reason, SocialHub.NowMs);
            DisposeMedia();
            Message = messageKey ?? EndKey(reason);
            Bus.Publish(new SfxSignal("sfx_call_end"));
            if (Paid != null && call.Paid && (Paid.Stage == PaidStage.Live)) EndPaid();
            Notify();
        }

        private static string EndKey(CallEnd r) => r == CallEnd.None ? null : "calls.end." + r.ToString().ToLowerInvariant();

        public void DismissEnded()
        {
            if (Active != null && Active.IsOver) Active = null;
            if (Paid != null && Paid.Terminal) { Paid = null; PersistPaid(false); }
            Message = null;
            Notify();
        }

        // ---------------------------------------------------------------- paid calls: fan

        public void BeginPaid(string creatorId)
        {
            if (Paid != null && !Paid.Terminal && Paid.Stage != PaidStage.Draft) { Message = "paid.busy"; Notify(); return; }
            if (!SocialWire.IsSafeId(creatorId) || SocialWire.SameId(creatorId, _hub.Me)) { Message = "paid.badCreator"; Notify(); return; }
            Paid = PaidCallFlow.ForFan(creatorId);
            Message = null;
            Notify();
        }

        public void CreatePaid(string minutesInput)
        {
            var flow = Paid;
            if (flow == null || !flow.IsFan || PaidWorking) return;
            if (!PaidCallsAllowedHere) { flow.ErrorKey = "paid.devnetOnly"; Notify(); return; }
            var err = flow.BeginCreate(minutesInput);
            if (err != null) { flow.ErrorKey = err; Notify(); return; }
            PaidWorking = true; Notify();
            _hub.Run(async ct =>
            {
                PaidCallStart start;
                try { start = await _hub.Api.StartPaidCall(flow.PeerId, flow.Minutes, flow.CreateKey, ct); }
                finally { PaidWorking = false; }
                flow.Created(start, SocialHub.NowMs);
                Notify();
            }, e => { PaidWorking = false; flow.CreateFailed(e); Notify(); });
        }

        public string SignBlocker()
        {
            if (!PaidCallsAllowedHere) return "paid.devnetOnly";
            if (!Services.TryGet<ITransactionSigner>(out _)) return "paid.noWallet";
            if (Services.TryGet<IWalletSignIn>(out var w) && !w.IsAvailable) return "paid.noWallet";
            return null;
        }

        public void SignPaid()
        {
            var flow = Paid;
            if (flow == null || PaidWorking) return;
            var now = SocialHub.NowMs;
            flow.DiscardStaleTx(now);
            var blocker = SignBlocker();
            if (blocker != null) { flow.ErrorKey = blocker; Notify(); return; }
            if (!Services.TryGet<ITransactionSigner>(out var signer) || !flow.BeginSign(now)) { Notify(); return; }
            PersistPaid(true); // the wallet may kill/background the app; resume by polling, never by signing again
            PaidWorking = true; Notify();
            _hub.Run(async ct =>
            {
                string signature;
                try { signature = await signer.SignAndSendBase64(flow.Tx, ct); }
                catch (WalletException w) when (w.UserDeclined) { PaidWorking = false; flow.SignDeclined(); PersistPaid(false); Notify(); return; }
                catch (OperationCanceledException) { throw; }
                catch (Exception w)
                {
                    PaidWorking = false;
                    Log.Warn("calls", "Wallet failed after a possible broadcast: " + w.GetType().Name);
                    flow.SignFailedUnknown();
                    _confirmingSince = SocialHub.NowMs; _paidPollAt = 0;
                    Notify();
                    return;
                }
                PaidWorking = false;
                flow.Signed(signature);
                Notify();
                Submit(flow);
            }, e => { PaidWorking = false; flow.SignFailedUnknown(); _confirmingSince = SocialHub.NowMs; Notify(); });
        }

        private void Submit(PaidCallFlow flow)
        {
            if (flow.Stage != PaidStage.Submitting || flow.Signature == null) return;
            _submitAttempts++;
            _hub.Run(async ct =>
            {
                var row = await _hub.Api.SubmitPaidCall(flow.CallId, flow.Signature, flow.SubmitKey, ct);
                flow.ApplyServer(row, SocialHub.NowMs);
                if (flow.Stage == PaidStage.Confirming) _confirmingSince = SocialHub.NowMs;
                _paidPollAt = SocialHub.NowMs + 1000;
                Notify();
            }, e =>
            {
                flow.SubmitFailed(e);
                if (flow.Stage == PaidStage.Submitting) _submitRetryAt = SocialHub.NowMs + Math.Min(15000, 2000 * _submitAttempts);
                else { _confirmingSince = SocialHub.NowMs; _paidPollAt = 0; }
                Notify();
            });
        }

        private void EndPaid()
        {
            var flow = Paid;
            if (flow == null || flow.Stage != PaidStage.Live) return;
            flow.BeginEnd();
            _hub.Run(async ct =>
            {
                var row = await _hub.Api.EndPaidCall(flow.CallId, flow.EndKey, ct);
                flow.ApplyServer(row, SocialHub.NowMs);
                _paidPollAt = SocialHub.NowMs + 1000;
                Notify();
            }, e =>
            {
                if (!e.OutcomeUnknown) flow.EndKey.Complete();
                flow.ErrorKey = e.OutcomeUnknown ? "paid.endRetry" : "paid.endRejected"; // the server still settles when the room closes
                _paidPollAt = SocialHub.NowMs + 2000;
                Notify();
            });
        }

        public void CheckPaid() { _paidPollAt = 0; }

        public void AbandonPaid()
        {
            if (Paid == null || Paid.Stage != PaidStage.Confirming) return;
            Paid.Abandon();
            PersistPaid(false);
            Notify();
        }

        public bool CanAbandonPaid => Paid != null && Paid.Stage == PaidStage.Confirming && !double.IsNaN(_confirmingSince) && SocialHub.NowMs - _confirmingSince > 90_000;

        public void ClosePaid()
        {
            if (Paid == null) return;
            if (Paid.Stage == PaidStage.Draft || Paid.Stage == PaidStage.ReadyToSign || Paid.Terminal) { Paid = null; PersistPaid(false); Notify(); }
        }

        private void PollPaid(double now)
        {
            var flow = Paid;
            if (flow == null || flow.CallId == null || _paidPolling || _hub.Offline) return;
            var interval = flow.PollIntervalMs();
            if (double.IsNaN(interval) || now < _paidPollAt) return;
            _paidPolling = true;
            _paidPollAt = now + interval;
            _hub.Run(async ct =>
            {
                try
                {
                    var row = await _hub.Api.PaidCall(flow.CallId, ct);
                    if (!ReferenceEquals(Paid, flow)) return;
                    var before = flow.Stage;
                    flow.ApplyServer(row, SocialHub.NowMs);
                    OnPaidStage(flow, before);
                }
                finally { _paidPolling = false; Notify(); }
            }, e =>
            {
                _paidPolling = false;
                if (e.Is("NOT_FOUND") && e.Kind == ErrorKind.Definitive && flow.Stage == PaidStage.Confirming) { flow.ErrorKey = "paid.unknownCall"; }
                Notify();
            });
        }

        private void OnPaidStage(PaidCallFlow flow, PaidStage before)
        {
            if (flow.Stage == before) return;
            if (flow.Terminal)
            {
                PersistPaid(false);
                if (Active != null && Active.Paid && !Active.IsOver) Finish(flow.Stage == PaidStage.Refunded ? CallEnd.Expired : CallEnd.HungUp, flow.Stage == PaidStage.Refunded ? "paid.refunded" : "paid.settled");
                Bus.Publish(new SfxSignal(flow.Stage == PaidStage.Settled ? "sfx_ui_success" : "sfx_ui_toast"));
                return;
            }
            if (flow.IsFan && flow.Stage == PaidStage.Ringing) Bus.Publish(new SfxSignal("sfx_call_ring", 0.4f));
            if (flow.IsFan && flow.Stage == PaidStage.Live && (Active == null || Active.IsOver))
            {
                if (Busy) return;
                if (_hub.Spaces.InSpace) _hub.Spaces.Leave();
                Active = CallSession.JoinPaid(flow.CallId, flow.PeerId, true, SocialHub.NowMs);
                AddRecent(flow.PeerId, "outgoing", true);
                JoinMedia(Active, flow.EmbeddedGrant);
            }
        }

        // ---------------------------------------------------------------- creator settings (PUT /creator)

        public void SaveCreator(bool enabled, string rateInput, string maxMinutesInput, Action<string> done)
        {
            if (!UsdcUnits.TryParse(rateInput, out var rate)) { done?.Invoke("creator.rateInvalid"); return; }
            var rateErr = UsdcUnits.RateError(rate);
            if (rateErr != null) { done?.Invoke(rateErr); return; }
            if (!UsdcUnits.TryMinutes(maxMinutesInput, out var max)) { done?.Invoke("creator.maxRange"); return; }
            var key = new RetainedKey();
            _hub.Run(async ct =>
            {
                var saved = await _hub.Api.PutCreator(enabled, rate, max, key, ct);
                Creator = saved;
                _hub.Store.SetJson("creator", new JObject { ["enabled"] = saved.Enabled, ["ratePerMinUsdc"] = saved.RatePerMinUsdc, ["maxMinutes"] = saved.MaxMinutes });
                done?.Invoke(null);
                Notify();
            }, e => done?.Invoke(e.Kind == ErrorKind.RouteMissing ? "creator.noRoute" : e.CopyKey));
        }

        // ---------------------------------------------------------------- ticks and signals

        public void Tick(double now)
        {
            if (_disposed) return;
            for (int i = Incoming.Count - 1; i >= 0; i--)
            {
                var inc = Incoming[i];
                if (inc.Working) continue;
                if (inc.Call.Tick(now))
                {
                    Incoming.RemoveAt(i);
                    AddRecent(inc.Call.FromUserId, "missed", inc.Paid != null);
                    Bus.Publish(ToastSignal.Text(SocialStrings.F("calls.missed", _hub.Names.Name(inc.Call.FromUserId))));
                    Notify();
                }
            }
            if (Active != null && !Active.IsOver)
            {
                if (Active.Tick(now)) Finish(Active.EndReason, null);
                else if (Active.ReconnectDue(now) && !_hub.Offline) JoinMedia(Active, null);
            }
            if (Incoming.Count > 0 && now >= _nextRing) { _nextRing = now + 3000; Bus.Publish(new SfxSignal("sfx_call_ring")); }
            else if (Active != null && Active.Phase == CallPhase.Waiting && now >= _nextRing) { _nextRing = now + 3000; Bus.Publish(new SfxSignal("sfx_call_ring", 0.35f)); }
            if (Paid != null)
            {
                if (Paid.Stage == PaidStage.Submitting && Paid.ErrorKey == "paid.submitRetry" && now >= _submitRetryAt && !_hub.Offline) { _submitRetryAt = double.MaxValue; Submit(Paid); }
                if (Paid.Stage == PaidStage.Ringing && Paid.RefundOverdue(now)) Paid.ErrorKey = "paid.refundPending";
                PollPaid(now);
            }
        }

        public void OnNetworkChanged(double now)
        {
            Active?.NetworkChanged(now);
            _paidPollAt = 0;
            Notify();
        }

        public void OnDegraded()
        {
            if (!Features.IsAvailable(Features.Voice) && Active != null && !Active.IsOver) Finish(CallEnd.Failed, "calls.voiceDegraded");
        }

        public void Dispose()
        {
            if (_disposed) return;
            DisposeMedia();
            _disposed = true;
        }
    }
}
