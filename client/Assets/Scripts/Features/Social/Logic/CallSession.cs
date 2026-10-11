using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.SocialFeatures
{
    public enum CallPhase { Idle, Dialing, Ringing, Accepting, Connecting, Waiting, Live, Reconnecting, Ended }

    public enum CallEnd { None, HungUp, Declined, Missed, Unanswered, EveryoneLeft, Failed, ConnectionLost, Busy, Expired }

    /// <summary>
    /// Lifecycle of one call in the phone (features #24 and the media half of #25). Pure; timestamps are passed in.
    ///
    /// Outgoing: Dialing (POST /video-calls) → Connecting (token + LiveKit) → Waiting (in the room, nobody else yet; the
    /// invitees' phones ring via city `call_invite`) → Live (a remote participant joined) → Ended.
    /// Incoming: Ringing (`call_invite`) → Accepting (token) → Connecting → Live → Ended. Declining a free call is local:
    /// the contract has no decline endpoint for free calls, the caller sees "No answer" after <see cref="UnansweredMs"/>.
    /// </summary>
    public sealed class CallSession
    {
        /// <summary>[Inference] No contract value; matches a typical phone ring. Paid calls use the 120 s escrow window.</summary>
        public const double RingMs = 45_000, UnansweredMs = 45_000, AloneGraceMs = 15_000;
        /// <summary>Contract: `userIds` 1..7 (groups up to 8 including the caller).</summary>
        public const int MaxInvitees = 7;

        public string CallId { get; private set; }
        public readonly bool Outgoing, Paid;
        public readonly string FromUserId;
        public readonly IReadOnlyList<string> Invitees;
        public CallPhase Phase { get; private set; }
        public CallEnd EndReason { get; private set; } = CallEnd.None;
        public string ErrorKey { get; set; }
        public double RingDeadlineMs { get; private set; }
        public double AcceptCutoffMs { get; private set; }
        public double PhaseSinceMs { get; private set; }
        public double LiveSinceMs { get; private set; } = double.NaN;
        public readonly Backoff Reconnect = new Backoff(5);
        public bool ReconnectInFlight { get; private set; }

        private readonly HashSet<string> _present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _everHadRemote;
        private double _aloneSinceMs = double.NaN;

        private CallSession(bool outgoing, bool paid, string from, IReadOnlyList<string> invitees, double nowMs)
        {
            Outgoing = outgoing; Paid = paid; FromUserId = from; Invitees = invitees; PhaseSinceMs = nowMs;
        }

        public IReadOnlyCollection<string> Present => _present;
        public bool IsOver => Phase == CallPhase.Ended;
        /// <summary>Holds the phone's single call slot (a ringing incoming call does not).</summary>
        public bool Busy => Phase != CallPhase.Idle && Phase != CallPhase.Ended && Phase != CallPhase.Ringing;

        /// <summary>Validates the invite list. Returns a SocialStrings key, or null with the normalized list.</summary>
        public static string ValidateInvitees(IEnumerable<string> userIds, string me, out List<string> normalized)
        {
            normalized = (userIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Where(x => !SocialWire.SameId(x, me)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (normalized.Count == 0) return "calls.pickSomeone";
            if (normalized.Count > MaxInvitees) return "calls.tooMany";
            if (normalized.Any(x => !SocialWire.IsSafeId(x))) return "calls.badUser";
            return null;
        }

        public static CallSession StartOutgoing(IReadOnlyList<string> invitees, double nowMs) =>
            new CallSession(true, false, null, invitees, nowMs) { Phase = CallPhase.Dialing };

        /// <summary>A fan's paid call after the creator accepted is joined like an outgoing call that already has an id.</summary>
        public static CallSession JoinPaid(string callId, string peer, bool outgoing, double nowMs)
        {
            var c = new CallSession(outgoing, true, outgoing ? null : peer, new[] { peer }, nowMs) { CallId = callId, Phase = CallPhase.Connecting };
            return c;
        }

        public static CallSession Incoming(string callId, string fromUserId, bool paid, double nowMs, double windowMs, double cutoffMs)
        {
            return new CallSession(false, paid, fromUserId, new[] { fromUserId }, nowMs)
            {
                CallId = callId,
                Phase = CallPhase.Ringing,
                RingDeadlineMs = nowMs + windowMs,
                AcceptCutoffMs = nowMs + Math.Min(windowMs, cutoffMs),
            };
        }

        private void Go(CallPhase p, double nowMs) { Phase = p; PhaseSinceMs = nowMs; }

        public void End(CallEnd reason, double nowMs)
        {
            if (Phase == CallPhase.Ended) return;
            EndReason = reason;
            ReconnectInFlight = false;
            Go(CallPhase.Ended, nowMs);
        }

        public void Dialed(string callId, double nowMs)
        {
            if (Phase != CallPhase.Dialing) return;
            CallId = callId;
            Go(CallPhase.Connecting, nowMs);
        }

        public bool CanAccept(double nowMs) => Phase == CallPhase.Ringing && nowMs < AcceptCutoffMs;

        public bool Accept(double nowMs)
        {
            if (!CanAccept(nowMs)) return false;
            Go(CallPhase.Accepting, nowMs);
            return true;
        }

        public void Decline(double nowMs) { if (Phase == CallPhase.Ringing || Phase == CallPhase.Accepting) End(CallEnd.Declined, nowMs); }

        /// <summary>Accepted (and, for paid calls, the server confirmed); the client is fetching a token and joining.</summary>
        public void Joining(double nowMs) { if (Phase == CallPhase.Accepting) Go(CallPhase.Connecting, nowMs); }

        public void MediaConnected(double nowMs)
        {
            if (Phase != CallPhase.Connecting && Phase != CallPhase.Reconnecting) return;
            ReconnectInFlight = false;
            Reconnect.Reset();
            Go(_present.Count > 0 ? CallPhase.Live : CallPhase.Waiting, nowMs);
            if (Phase == CallPhase.Live && double.IsNaN(LiveSinceMs)) LiveSinceMs = nowMs;
            if (_present.Count == 0 && _everHadRemote) _aloneSinceMs = nowMs;
        }

        public void RemoteJoined(string identity, double nowMs)
        {
            if (string.IsNullOrEmpty(identity) || Phase == CallPhase.Ended) return;
            _present.Add(identity);
            _everHadRemote = true;
            _aloneSinceMs = double.NaN;
            if (Phase == CallPhase.Waiting) { Go(CallPhase.Live, nowMs); if (double.IsNaN(LiveSinceMs)) LiveSinceMs = nowMs; }
        }

        public void RemoteLeft(string identity, double nowMs)
        {
            if (!_present.Remove(identity ?? "")) return;
            if (_present.Count == 0 && (Phase == CallPhase.Live || Phase == CallPhase.Waiting)) _aloneSinceMs = nowMs;
        }

        public void MediaLost(double nowMs)
        {
            if (Phase != CallPhase.Live && Phase != CallPhase.Waiting && Phase != CallPhase.Connecting && Phase != CallPhase.Reconnecting) return;
            _present.Clear();
            ReconnectInFlight = false;
            Go(CallPhase.Reconnecting, nowMs);
            Reconnect.Fail(nowMs);
        }

        public bool ReconnectDue(double nowMs) => Phase == CallPhase.Reconnecting && !ReconnectInFlight && Reconnect.Due(nowMs);
        public void ReconnectStarted() { if (Phase == CallPhase.Reconnecting) ReconnectInFlight = true; }

        public void ReconnectFailed(double nowMs)
        {
            if (Phase != CallPhase.Reconnecting) return;
            ReconnectInFlight = false;
            Reconnect.Fail(nowMs);
            if (Reconnect.Exhausted) End(CallEnd.ConnectionLost, nowMs);
        }

        public void NetworkChanged(double nowMs) { if (Phase == CallPhase.Reconnecting) Reconnect.Hurry(nowMs); }

        /// <summary>Timeouts. Returns true when the phase changed.</summary>
        public bool Tick(double nowMs)
        {
            switch (Phase)
            {
                case CallPhase.Ringing when nowMs >= RingDeadlineMs:
                    End(CallEnd.Missed, nowMs); return true;
                case CallPhase.Waiting when !_everHadRemote && nowMs - PhaseSinceMs >= UnansweredMs:
                    End(CallEnd.Unanswered, nowMs); return true;
                case CallPhase.Live:
                case CallPhase.Waiting:
                    if (_everHadRemote && _present.Count == 0 && !double.IsNaN(_aloneSinceMs) && nowMs - _aloneSinceMs >= AloneGraceMs)
                    {
                        End(CallEnd.EveryoneLeft, nowMs); return true;
                    }
                    if (Phase == CallPhase.Live && _present.Count == 0 && double.IsNaN(_aloneSinceMs)) _aloneSinceMs = nowMs;
                    return false;
                default:
                    return false;
            }
        }

        public double SecondsToAnswer(double nowMs) => Phase == CallPhase.Ringing ? Math.Max(0, (AcceptCutoffMs - nowMs) / 1000.0) : 0;
        public double LiveSeconds(double nowMs) => double.IsNaN(LiveSinceMs) ? 0 : Math.Max(0, (nowMs - LiveSinceMs) / 1000.0);
    }
}
