using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using RestSpace = CTLife.Rest.Model.Space;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Spaces Studio runtime (feature #23): the live list, one Space session (<see cref="SpaceSession"/>) and its audio.
    /// Audio reuses the existing LiveKit wrapper <see cref="SpacesAudio"/> (stage-only subscriptions, explicit mic, never
    /// resumed after a reconnect); this class adds what the phone-scoped SpacesApp lacked: the session survives closing the
    /// phone, `space_update` and polling keep the roster fresh, media is rejoined with backoff after a drop or a network
    /// change, blocked speakers are not subscribed, and the backend's Space shape (string ids, wrapped list) is accepted.
    /// </summary>
    public sealed class SpacesController : IDisposable
    {
        public const double ListPollMs = 15000, MediaLostAfterMs = 8000;

        private readonly SocialHub _hub;
        public SpaceSession Session { get; private set; }
        public List<SpaceInfo> List { get; private set; }
        public string ListError { get; private set; }
        public bool ListLoading { get; private set; }
        public string Message { get; set; }
        public bool Busy { get; private set; }
        public bool Unavailable { get; private set; }
        public bool MicBusy => _audio?.MicBusy == true || _micStarting;
        public bool MicOn => _audio?.MicOn == true;
        public bool AudioConnected => _audio?.Connected == true;
        public bool Muted => _audio?.Muted == true;

        private SpacesAudio _audio;
        private bool _joining, _micStarting, _fetching, _disposed;
        private double _listAt = double.NegativeInfinity, _disconnectedSince = double.NaN;
        private CancellationTokenSource _media;
        private readonly RetainedKey _createKey = new RetainedKey(), _opKey = new RetainedKey(), _promoteFallbackKey = new RetainedKey();
        private string _pendingTitle;
        public string PendingTitle => _pendingTitle;
        public event Action Changed;

        public SpacesController(SocialHub hub) { _hub = hub; }

        private void Notify() { if (!_disposed) { Changed?.Invoke(); _hub.NotifyChanged(); } }

        public bool InSpace => Session != null && Session.InSpace;

        // ---------------------------------------------------------------- list and create

        public void RefreshList()
        {
            if (ListLoading || _disposed) return;
            ListLoading = true; Notify();
            _hub.Run(async ct =>
            {
                try
                {
                    var list = await _hub.Api.LiveSpaces(ct);
                    List = list;
                    ListError = null;
                    Unavailable = false;
                    _listAt = SocialHub.NowMs;
                }
                finally { ListLoading = false; Notify(); }
            }, e =>
            {
                ListLoading = false;
                _listAt = SocialHub.NowMs;
                Unavailable = e.Kind == ErrorKind.RouteMissing;
                ListError = e.CopyKey;
                Notify();
            });
        }

        /// <summary>POST /spaces. An unknown outcome keeps the title and key: "Try again" replays, never creates twice.</summary>
        public void Create(string titleInput)
        {
            if (Busy || _disposed) return;
            var title = (_pendingTitle ?? titleInput ?? "").Trim();
            if (title.Length == 0) { Message = "spaces.topicRequired"; Notify(); return; }
            if (title.Length > 80) { Message = "spaces.topicTooLong"; Notify(); return; }
            _pendingTitle = title;
            Busy = true; Message = null; Notify();
            _hub.Run(async ct =>
            {
                SpaceInfo created;
                try { created = await _hub.Api.CreateSpace(title, _createKey, ct); }
                finally { Busy = false; }
                _createKey.Complete(); _pendingTitle = null;
                Open(created.Id, created);
            }, e =>
            {
                Busy = false;
                if (!e.OutcomeUnknown) { _createKey.Complete(); _pendingTitle = null; }
                Message = e.OutcomeUnknown ? "spaces.createUncertain" : e.CopyKey;
                Notify();
            });
        }

        public void ForgetPendingCreate() { _createKey.Complete(); _pendingTitle = null; Message = null; Notify(); }

        // ---------------------------------------------------------------- join / leave

        public void Open(string spaceId, SpaceInfo known = null)
        {
            if (_disposed || string.IsNullOrEmpty(spaceId)) return;
            if (Session != null && Session.InSpace && !SocialWire.SameId(Session.SpaceId, spaceId)) LeaveMedia();
            if (_hub.Calls.Busy) { Message = "spaces.inCall"; Notify(); return; }
            if (Session == null) Session = new SpaceSession(_hub.Me);
            Session.Open(spaceId);
            if (known != null) Session.ApplySnapshot(known, Session.BeginFetch(), SocialHub.NowMs);
            Message = null;
            Fetch();
            JoinMedia();
            Notify();
        }

        public void Leave()
        {
            if (Session == null) return;
            Session.Leave();
            LeaveMedia();
            Message = null;
            RefreshList();
            Notify();
        }

        public void Rejoin()
        {
            if (Session == null || !Session.CanRejoin) return;
            Open(Session.SpaceId);
        }

        public void Close()
        {
            if (Session != null && Session.InSpace) Leave();
            Session = null;
            Notify();
        }

        private void LeaveMedia()
        {
            _media?.Cancel(); _media = null;
            DropAudio();
            _joining = false;
            _disconnectedSince = double.NaN;
        }

        private void DropAudio()
        {
            var a = _audio; _audio = null;
            if (a == null) return;
            a.Changed -= OnAudioChanged; a.Failed -= OnAudioFailed;
            a.Dispose();
        }

        private void OnAudioChanged() => Notify();
        private void OnAudioFailed(string key) { Message = "spaces." + key; Notify(); }

        private void JoinMedia()
        {
            if (_joining || Session == null || _disposed) return;
            if (!Features.IsAvailable(Features.Voice)) { Message = "spaces.voiceDegraded"; return; }
            _joining = true;
            Session.MediaConnecting();
            if (Session.Phase == SpacePhase.Reconnecting) Session.ReconnectStarted();
            var media = new CancellationTokenSource();
            _media?.Cancel(); _media = media;
            var spaceId = Session.SpaceId;
            _hub.Run(async ct =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, media.Token);
                try
                {
                    var grant = await _hub.Api.RtcToken("space", spaceId, linked.Token);
                    if (Session == null || !SocialWire.SameId(Session.SpaceId, spaceId) || !Session.InSpace) return;
                    // One voice context at a time: proximity voice off while on a Space.
                    if (Services.TryGet<VoiceService>(out var nearby)) await SpacesRequest.Run(async _ => { await nearby.Disconnect(); return true; }, linked.Token, 15000);
                    DropAudio();
                    var audio = new SpacesAudio();
                    audio.Changed += OnAudioChanged; audio.Failed += OnAudioFailed;
                    _audio = audio;
                    var me = ParseGuid(_hub.Me);
                    await audio.Connect(ToRestGrant(grant), Roster(Session.Space), me, linked.Token);
                    if (!ReferenceEquals(_audio, audio)) return;
                    Session.MediaConnected();
                    _disconnectedSince = double.NaN;
                    Bus.Publish(new SfxSignal("sfx_vr_join"));
                }
                finally { _joining = false; Notify(); }
            }, e =>
            {
                _joining = false;
                if (Session == null || !SocialWire.SameId(Session.SpaceId, spaceId)) return;
                DropAudio();
                if (e.Kind == ErrorKind.RouteMissing) { Session.Fail("spaces.noVoiceRoute"); Message = "spaces.noVoiceRoute"; }
                else if (e.Is("NOT_FOUND")) { Session.ApplyNotFound(); Message = "spaces.ended"; }
                else if (Session.Phase == SpacePhase.Reconnecting) Session.ReconnectFailed(SocialHub.NowMs);
                else { Session.MediaLost(SocialHub.NowMs); Message = e.CopyKey; }
                Notify();
            });
        }

        // ---------------------------------------------------------------- roster refresh

        private void Fetch()
        {
            if (_fetching || Session == null || !Session.InSpace) return;
            _fetching = true;
            var spaceId = Session.SpaceId;
            var rev = Session.BeginFetch();
            _hub.Run(async ct =>
            {
                try
                {
                    var s = await _hub.Api.Space(spaceId, ct);
                    if (Session == null || !SocialWire.SameId(Session.SpaceId, spaceId)) return;
                    var change = Session.ApplySnapshot(s, rev, SocialHub.NowMs);
                    Apply(change);
                }
                finally { _fetching = false; Notify(); }
            }, e =>
            {
                _fetching = false;
                if (Session == null || !SocialWire.SameId(Session.SpaceId, spaceId)) return;
                if (e.Is("NOT_FOUND") && e.Kind == ErrorKind.Definitive) { Session.ApplyNotFound(); LeaveMedia(); Message = "spaces.ended"; }
                Notify();
            });
        }

        private void Apply(SpaceChange change)
        {
            if (change.Ended)
            {
                LeaveMedia();
                Message = "spaces.ended";
                Bus.Publish(new SfxSignal("sfx_vr_leave"));
                return;
            }
            if (change.MicMustStop) { _audio?.MuteMicrophone(); Message = "spaces.movedToListeners"; }
            if (change.Promoted) { Message = "spaces.promoted"; Bus.Publish(new SfxSignal("sfx_vr_promoted")); }
            var now = SocialHub.NowMs;
            _audio?.SetRoster(Session.Fresh(now) ? Roster(Session.Space) : null, ParseGuid(_hub.Me));
        }

        public void OnSpaceUpdate(string spaceId)
        {
            if (Session == null || !Session.Invalidate(spaceId)) return;
            // Backend revocation unpublishes tracks; refresh without cutting speakers on a listener-count update.
            _audio?.InvalidateRoster();
            Fetch();
            Notify();
        }

        public void OnNetworkChanged(double now)
        {
            if (Session == null) return;
            Session.NetworkChanged(now);
            Fetch();
            Notify();
        }

        public void OnDegraded()
        {
            if (!Features.IsAvailable(Features.Voice) && Session != null && Session.InSpace) { LeaveMedia(); Session.Leave(); Message = "spaces.voiceDegraded"; Notify(); }
        }

        public void Tick(double now)
        {
            if (_disposed) return;
            if (Session != null && Session.InSpace)
            {
                if (Session.NeedsFetch(now) && !_hub.Offline) Fetch();
                // Media drop detection: the SDK reconnects by itself for a while; past that, rejoin with a fresh token.
                if ((Session.Phase == SpacePhase.Live) && !_joining)
                {
                    if (_audio == null || !_audio.Connected)
                    {
                        if (double.IsNaN(_disconnectedSince)) _disconnectedSince = now;
                        else if (now - _disconnectedSince >= MediaLostAfterMs)
                        {
                            Log.Info("spaces", "Media lost, rejoining");
                            DropAudio();
                            Session.MediaLost(now);
                            _disconnectedSince = double.NaN;
                            Message = "spaces.reconnecting";
                            Notify();
                        }
                    }
                    else _disconnectedSince = double.NaN;
                }
                if (Session.ReconnectDue(now) && !_hub.Offline) JoinMedia();
            }
            else if (List != null || ListError != null)
            {
                if (now - _listAt >= ListPollMs && !_hub.Offline && _listViewers > 0) RefreshList();
            }
        }

        private int _listViewers;
        /// <summary>The Spaces app is visible: poll the live list while it is.</summary>
        public void Watch(bool on) { _listViewers = Math.Max(0, _listViewers + (on ? 1 : -1)); if (on && (List == null || SocialHub.NowMs - _listAt > ListPollMs)) RefreshList(); }

        // ---------------------------------------------------------------- role actions

        private void Op(SpaceOp op, string target, Func<CancellationToken, Task> call)
        {
            if (Session == null || !Session.Begin(op, target)) return;
            Message = null; Notify();
            var spaceId = Session.SpaceId;
            _hub.Run(async ct =>
            {
                try { await call(ct); _opKey.Complete(); _promoteFallbackKey.Complete(); }
                finally
                {
                    if (Session != null && SocialWire.SameId(Session.SpaceId, spaceId)) Session.Finish();
                    Fetch();
                    Notify();
                }
            }, e =>
            {
                if (!e.OutcomeUnknown) { _opKey.Complete(); _promoteFallbackKey.Complete(); }
                Message = e.Is("NOT_SPACE_HOST") ? "err.notHost" : e.Kind == ErrorKind.RouteMissing ? "err.routeMissing" : e.CopyKey;
                Notify();
            });
        }

        public void RequestToSpeak() { if (Session != null && Session.CanRequest(SocialHub.NowMs)) Op(SpaceOp.Request, null, ct => _hub.Api.RaiseHand(Session.SpaceId, _opKey, ct)); }
        public void WithdrawRequest() { if (Session != null && Session.CanWithdraw(SocialHub.NowMs)) Op(SpaceOp.Withdraw, null, ct => _hub.Api.LowerHand(Session.SpaceId, _opKey, ct)); }

        public void Promote(string user)
        {
            if (Session != null && Session.CanPromote(user, SocialHub.NowMs))
                Op(SpaceOp.Promote, user, ct => _hub.Api.Promote(Session.SpaceId, user, _opKey, _promoteFallbackKey, ct));
        }

        public void Demote(string user)
        {
            if (Session != null && Session.CanDemote(user, SocialHub.NowMs)) Op(SpaceOp.Demote, user, ct => _hub.Api.Demote(Session.SpaceId, user, _opKey, ct));
        }

        public void End()
        {
            if (Session == null || !Session.CanEnd(SocialHub.NowMs)) return;
            var spaceId = Session.SpaceId;
            Op(SpaceOp.End, null, async ct =>
            {
                await _hub.Api.EndSpace(spaceId, _opKey, ct);
                if (Session != null && SocialWire.SameId(Session.SpaceId, spaceId)) { Session.ApplyNotFound(); LeaveMedia(); Message = "spaces.endedByYou"; }
                RefreshList();
            });
        }

        /// <summary>Unmute (publish) after a fresh roster and a server grant that allows publishing; mute is immediate.</summary>
        public void ToggleMic()
        {
            if (_audio == null || Session == null) return;
            if (_audio.MicOn || _micStarting) { _audio.MuteMicrophone(); Notify(); return; }
            if (!Session.CanPublish(SocialHub.NowMs)) { Message = "spaces.roleChanged"; Fetch(); Notify(); return; }
            _micStarting = true; Notify();
            var spaceId = Session.SpaceId;
            _hub.Run(async ct =>
            {
                try
                {
                    var perm = await MediaPermission.Request(MediaPermission.Kind.Microphone, ct);
                    if (perm != PermissionResult.Granted) { Message = MediaPermission.Key(MediaPermission.Kind.Microphone, perm); return; }
                    var fresh = await _hub.Api.Space(spaceId, ct);
                    if (Session == null || !SocialWire.SameId(Session.SpaceId, spaceId)) return;
                    Apply(Session.ApplySnapshot(fresh, Session.BeginFetch(), SocialHub.NowMs));
                    if (!Session.CanPublish(SocialHub.NowMs) || _audio == null) { Message = "spaces.roleChanged"; return; }
                    bool serverAllows = true;
                    try { serverAllows = (await _hub.Api.RtcToken("space", spaceId, ct)).CanPublish; }
                    catch (ApiException e) when (SocialError.IsRouteMissing(e)) { serverAllows = true; } // no route: the snapshot decides
                    if (!serverAllows) { Message = "spaces.roleChanged"; return; }
                    _audio.SetRoster(Roster(Session.Space), ParseGuid(_hub.Me)); // refreshes SpacesAudio's 8 s publish gate
                    await _audio.EnableMicrophone(true, ct);
                    Message = null;
                }
                finally { _micStarting = false; Notify(); }
            }, e => { _micStarting = false; Message = e.CopyKey; Notify(); });
        }

        public void ToggleSpeakerMute() { if (_audio != null) { _audio.SetMuted(!_audio.Muted); Notify(); } }

        public bool IsSpeaking(string userId) => _audio != null && Guid.TryParse(userId, out var g) && _audio.IsSpeaking(g);

        // ---------------------------------------------------------------- adapters for SpacesAudio (generated models)

        /// <summary>
        /// SpacesAudio subscribes only to the host and speakers in this roster. Blocked users are left out, so their audio is
        /// never delivered to this client (X Spaces labels blocked speakers; CT Life also silences them).
        /// </summary>
        private RestSpace Roster(SpaceInfo s)
        {
            if (s == null) return null;
            Guid.TryParse(s.HostId, out var host);
            var speakers = s.SpeakerIds.Where(u => !_hub.Blocks.Contains(u)).Select(ParseGuid).Where(g => g != Guid.Empty).ToList();
            if (_hub.Blocks.Contains(s.HostId)) host = Guid.NewGuid(); // host stays a valid roster id but nobody has that identity
            return new RestSpace(SocialWire.StableGuid(s.Id), string.IsNullOrWhiteSpace(s.Title) ? "Space" : s.Title, host == Guid.Empty ? Guid.NewGuid() : host,
                speakers, new List<Guid>(), Math.Max(0, s.ListenerCount));
        }

        private static PostRtcToken200Response ToRestGrant(RtcGrant g)
        {
            var env = Env.Current;
            var url = env != null && !string.IsNullOrEmpty(env.liveKitUrlOverride) ? env.liveKitUrlOverride : g.Url;
            var expires = g.ExpiresAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(g.ExpiresAtMs).UtcDateTime
                : DateTimeOffset.FromUnixTimeMilliseconds((long)ServerClock.NowMs + 300_000).UtcDateTime; // no expiry sent: assume 5 min
            return new PostRtcToken200Response(url, g.Token, expires, g.CanPublish);
        }

        private static Guid ParseGuid(string s) => Guid.TryParse(s, out var g) ? g : Guid.Empty;

        public void Dispose()
        {
            if (_disposed) return;
            LeaveMedia();
            _disposed = true;
        }
    }
}
