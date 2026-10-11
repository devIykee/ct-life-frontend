using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.SocialFeatures
{
    public enum SpacePhase { Idle, Opening, Connecting, Live, Reconnecting, Left, Ended, Failed }

    /// <summary>
    /// The player's role, derived only from the server's Space (contract: `hostId`, `speakerIds`, `handIds`). Wording follows
    /// X Spaces (host, speaker, listener; listeners "Request" to speak), see docs/progress/phase3-client.md "Research".
    /// </summary>
    public enum SpaceRole { None, Listener, Requested, Speaker, Host }

    public enum SpaceOp { None, Request, Withdraw, Promote, Demote, End }

    /// <summary>What a server snapshot changed, for the controller to act on (mute the mic, toast, close the stage).</summary>
    public struct SpaceChange
    {
        public SpaceRole Before, After;
        public bool Ended;
        /// <summary>The player left the stage (demoted or the host moved them): stop the microphone immediately.</summary>
        public bool MicMustStop;
        public bool Promoted;
    }

    /// <summary>
    /// Client state machine for one Space (feature #23). Pure: time is passed in as epoch/monotonic milliseconds, so the
    /// EditMode tests drive every transition. The server stays authoritative for roles; this only decides what the client
    /// may offer, when to refetch, and when to reconnect media.
    ///
    /// Freshness: every `space_update {spaceId}` (city room) bumps a revision; a snapshot fetched before the latest bump is
    /// stale and role actions stay disabled until a newer fetch lands. Without the city room (the backend's city room never
    /// sends `space_update`), the Space is polled every <see cref="PollMs"/>.
    /// </summary>
    public sealed class SpaceSession
    {
        public const double PollMs = 5000, StaleAfterMs = 12000;
        /// <summary>Build spec: one host and up to 10 speakers on stage (the server enforces its own cap).</summary>
        public const int SpeakerLimit = 10;

        public readonly string Me;
        public string SpaceId { get; private set; }
        public SpaceInfo Space { get; private set; }
        public SpacePhase Phase { get; private set; } = SpacePhase.Idle;
        public SpaceRole Role { get; private set; } = SpaceRole.None;
        public SpaceOp Pending { get; private set; } = SpaceOp.None;
        public string PendingTarget { get; private set; }
        public string ErrorKey { get; set; }
        public readonly Backoff Reconnect = new Backoff(6);
        public bool ReconnectInFlight { get; private set; }

        private int _revision, _appliedRevision = -1;
        private double _fetchedAtMs = double.NegativeInfinity;

        public SpaceSession(string me)
        {
            if (string.IsNullOrWhiteSpace(me)) throw new ArgumentException("Signed-in user id required", nameof(me));
            Me = me;
        }

        public bool InSpace => Phase == SpacePhase.Opening || Phase == SpacePhase.Connecting || Phase == SpacePhase.Live || Phase == SpacePhase.Reconnecting;

        public static SpaceRole Derive(SpaceInfo s, string me)
        {
            if (s == null || string.IsNullOrEmpty(me)) return SpaceRole.None;
            if (SocialWire.SameId(s.HostId, me)) return SpaceRole.Host;
            if (s.SpeakerIds.Any(x => SocialWire.SameId(x, me))) return SpaceRole.Speaker;
            if (s.HandRaised(me)) return SpaceRole.Requested;
            return SpaceRole.Listener;
        }

        public static bool CanTalk(SpaceRole r) => r == SpaceRole.Host || r == SpaceRole.Speaker;

        public void Open(string spaceId)
        {
            if (string.IsNullOrWhiteSpace(spaceId)) throw new ArgumentException("Space id required", nameof(spaceId));
            if (!SocialWire.SameId(SpaceId, spaceId)) { Space = null; Role = SpaceRole.None; }
            SpaceId = spaceId;
            Phase = SpacePhase.Opening;
            ErrorKey = null;
            Pending = SpaceOp.None; PendingTarget = null;
            Reconnect.Reset(); ReconnectInFlight = false;
            _revision++;
        }

        /// <summary>Call before a GET /spaces/{id}; pass the result to <see cref="ApplySnapshot"/>.</summary>
        public int BeginFetch() => _revision;

        public SpaceChange ApplySnapshot(SpaceInfo s, int revision, double nowMs)
        {
            var change = new SpaceChange { Before = Role, After = Role };
            if (s == null || !SocialWire.SameId(s.Id, SpaceId) || Phase == SpacePhase.Idle || Phase == SpacePhase.Left || Phase == SpacePhase.Ended)
                return change;
            if (revision < _appliedRevision) return change; // an older fetch finished late
            if (s.Ended) { MarkEnded(); change.After = SpaceRole.None; change.Ended = true; change.MicMustStop = CanTalk(change.Before); return change; }
            Space = s;
            Role = Derive(s, Me);
            _appliedRevision = revision;
            _fetchedAtMs = nowMs;
            change.After = Role;
            change.MicMustStop = CanTalk(change.Before) && !CanTalk(Role);
            change.Promoted = !CanTalk(change.Before) && CanTalk(Role) && change.Before != SpaceRole.None;
            return change;
        }

        /// <summary>GET /spaces/{id} answered 404 (not a missing route): the Space is over.</summary>
        public void ApplyNotFound() => MarkEnded();

        private void MarkEnded()
        {
            Phase = SpacePhase.Ended;
            Role = SpaceRole.None;
            Pending = SpaceOp.None; PendingTarget = null;
            ReconnectInFlight = false;
        }

        /// <summary>City `space_update {spaceId}`. Returns true when it concerns this Space.</summary>
        public bool Invalidate(string spaceId)
        {
            if (!InSpace || !SocialWire.SameId(spaceId, SpaceId)) return false;
            _revision++;
            return true;
        }

        public bool Fresh(double nowMs) => Space != null && _appliedRevision == _revision && nowMs - _fetchedAtMs < StaleAfterMs;

        public bool NeedsFetch(double nowMs) => InSpace && (_appliedRevision != _revision || nowMs - _fetchedAtMs >= PollMs);

        // ---------------------------------------------------------------- media

        public void MediaConnecting()
        {
            if (Phase == SpacePhase.Opening || Phase == SpacePhase.Left || Phase == SpacePhase.Failed) Phase = SpacePhase.Connecting;
        }

        public void MediaConnected()
        {
            if (Phase == SpacePhase.Ended || Phase == SpacePhase.Left) return;
            Phase = SpacePhase.Live;
            ErrorKey = null;
            Reconnect.Reset();
            ReconnectInFlight = false;
        }

        /// <summary>The LiveKit room dropped without the player leaving. Schedules a rejoin with backoff.</summary>
        public void MediaLost(double nowMs)
        {
            if (Phase != SpacePhase.Live && Phase != SpacePhase.Connecting && Phase != SpacePhase.Reconnecting) return;
            Phase = SpacePhase.Reconnecting;
            ReconnectInFlight = false;
            Reconnect.Fail(nowMs);
        }

        public bool ReconnectDue(double nowMs) => Phase == SpacePhase.Reconnecting && !ReconnectInFlight && Reconnect.Due(nowMs);

        public void ReconnectStarted() { if (Phase == SpacePhase.Reconnecting) ReconnectInFlight = true; }

        public void ReconnectFailed(double nowMs)
        {
            if (Phase != SpacePhase.Reconnecting) return;
            ReconnectInFlight = false;
            Reconnect.Fail(nowMs);
            if (Reconnect.Exhausted) { Phase = SpacePhase.Failed; ErrorKey = "spaces.connectionLost"; }
        }

        /// <summary>Connectivity changed (offline → online, Wi-Fi ↔ mobile): retry now and refetch.</summary>
        public void NetworkChanged(double nowMs)
        {
            if (Phase == SpacePhase.Reconnecting || (Phase == SpacePhase.Failed && ErrorKey == "spaces.connectionLost"))
            {
                Phase = SpacePhase.Reconnecting;
                Reconnect.Hurry(nowMs);
            }
            if (InSpace) _revision++;
        }

        public void Leave()
        {
            if (Phase == SpacePhase.Idle || Phase == SpacePhase.Ended) return;
            Phase = SpacePhase.Left;
            Pending = SpaceOp.None; PendingTarget = null;
            ReconnectInFlight = false;
        }

        /// <summary>Opening failed definitively (unreadable Space, server refusal).</summary>
        public void Fail(string errorKey)
        {
            if (Phase == SpacePhase.Ended) return;
            Phase = SpacePhase.Failed;
            ErrorKey = errorKey;
            ReconnectInFlight = false;
        }

        public bool CanRejoin => (Phase == SpacePhase.Left || Phase == SpacePhase.Failed) && SpaceId != null;

        // ---------------------------------------------------------------- role actions (offered only on a fresh snapshot)

        private bool Ready(double nowMs) => (Phase == SpacePhase.Live || Phase == SpacePhase.Connecting || Phase == SpacePhase.Reconnecting) &&
                                           Pending == SpaceOp.None && Fresh(nowMs);

        public bool CanRequest(double nowMs) => Ready(nowMs) && Role == SpaceRole.Listener;
        public bool CanWithdraw(double nowMs) => Ready(nowMs) && Role == SpaceRole.Requested;
        public bool StageFull => Space != null && Space.SpeakerIds.Count >= SpeakerLimit;
        public bool CanPromote(string user, double nowMs) => Ready(nowMs) && Role == SpaceRole.Host && !StageFull && Space.HandRaised(user);
        public bool CanDemote(string user, double nowMs) => Ready(nowMs) && Role == SpaceRole.Host && Space.SpeakerIds.Any(x => SocialWire.SameId(x, user));
        public bool CanEnd(double nowMs) => (Phase == SpacePhase.Live || Phase == SpacePhase.Reconnecting || Phase == SpacePhase.Connecting) && Role == SpaceRole.Host && Pending == SpaceOp.None;
        /// <summary>The microphone may be published: on stage per a fresh snapshot and connected.</summary>
        public bool CanPublish(double nowMs) => Phase == SpacePhase.Live && CanTalk(Role) && Fresh(nowMs);

        public bool Begin(SpaceOp op, string target = null)
        {
            if (Pending != SpaceOp.None || op == SpaceOp.None) return false;
            Pending = op; PendingTarget = target;
            return true;
        }

        /// <summary>The op finished (either way); the next snapshot decides the new role.</summary>
        public void Finish()
        {
            Pending = SpaceOp.None; PendingTarget = null;
            _revision++;
        }

        /// <summary>Speaker requests in server order, for the host's queue.</summary>
        public IReadOnlyList<string> Requests => Space?.HandIds ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
}
