using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using CTLife.Audio;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// One visit to one venue: loads the diorama, joins the `venue` room, and keeps avatars in sync with
    /// VenueState (players by sessionId, NPCs by npcId). Owns the local player controller, chat bubbles,
    /// `rejected` handling and the venue's proximity-voice room.
    /// </summary>
    public sealed class VenueSession : MonoBehaviour
    {
        public static VenueSession Current { get; private set; }

        /// <summary>Look for a remote player whose state has no look (set by the composition root; presentation only).</summary>
        public static Func<string, string[]> RemoteLookFallback;

        public string VenueId { get; private set; }
        public string HomeId { get; private set; }
        public bool IsOwnHome => !string.IsNullOrEmpty(HomeId);
        public VenueSceneRoot Venue { get; private set; }
        public VenueChannel Channel { get; private set; }
        public LocalPlayerController LocalPlayer { get; private set; }

        private readonly Dictionary<string, RemotePlayer> _players = new Dictionary<string, RemotePlayer>();
        private readonly Dictionary<string, NpcController> _npcs = new Dictionary<string, NpcController>();
        private readonly Dictionary<string, AvatarView> _byUserId = new Dictionary<string, AvatarView>();
        private Transform _avatarRoot;
        private VenueLoader _loader;
        private Subscriptions _subs;

        public IReadOnlyDictionary<string, RemotePlayer> Players => _players;
        public IReadOnlyDictionary<string, NpcController> Npcs => _npcs;

        private static readonly SemaphoreSlim Transition = new SemaphoreSlim(1, 1);
        private static CancellationTokenSource _latestRequest;
        private CancellationTokenSource _visit;
        private CancellationToken _visitToken;
        private CancellationTokenRegistration _exitRegistration, _destroyRegistration;
        private Task _exitTask, _cleanupTask;
        private Task<bool> _voiceConnect;
        private bool _ready, _cleaned, _closing;
        private GameObject _ownedObject;
        private VoiceService _voice;
        private Func<string, AudioSource> _speakerResolver;
        private Func<string, Vector3?> _positionResolver;
        private Func<Vector3> _listenerPosition;
        public CancellationToken LifetimeToken => _visit != null ? _visitToken : new CancellationToken(true);
        private bool OwnsVisit => this != null && !_closing && !_cleaned && _visit != null && !_visit.IsCancellationRequested && ReferenceEquals(Current, this);
        public bool IsActive => _ready && OwnsVisit;

        public static Task<VenueSession> Enter(string venueId, bool joinRoom = true, bool voice = false, CancellationToken owner = default)
        {
            if (venueId == "home" && joinRoom) throw new InvalidOperationException("Own Home is a local visit, not a public venue room.");
            return EnterCore(venueId, joinRoom, voice, owner, null, null);
        }

        public static bool CanEnterOwnHome()
        {
            if (!Services.TryGet<Session>(out var session) || !session.IsSignedIn || string.IsNullOrEmpty(session.UserId) ||
                !Services.TryGet<PlayerProfile>(out var profile) || profile.UserId != session.UserId || profile.Me == null)
                return false;
            // Index lookup only: navigation and lightmaps were verified when the catalog was synced, so this
            // availability check (polled by the Home app) no longer loads any venue art.
            return VenueContent.HasPlayableHome(profile.Me.HomeId);
        }

        public static Task<VenueSession> EnterOwnHome(CancellationToken owner = default)
        {
            owner.ThrowIfCancellationRequested();
            if (!CanEnterOwnHome()) throw new InvalidOperationException("Own Home is unavailable for the current profile.");
            var profile = Services.Get<PlayerProfile>();
            return EnterCore("home", false, false, owner, profile.Me.HomeId, profile.UserId);
        }

        private static void RequireHomeOwner(string homeId, string userId)
        {
            if (string.IsNullOrEmpty(homeId)) return;
            var session = Services.Get<Session>(); var profile = Services.Get<PlayerProfile>();
            if (!session.IsSignedIn || session.UserId != userId || profile.UserId != userId || profile.Me?.HomeId != homeId || !CanEnterOwnHome())
                throw new OperationCanceledException("Own Home selection no longer belongs to this profile.");
        }

        private static async Task<VenueSession> EnterCore(string venueId, bool joinRoom, bool voice, CancellationToken owner, string homeId, string homeUserId)
        {
            owner.ThrowIfCancellationRequested();
            var context = SynchronizationContext.Current;
            if (context == null) throw new InvalidOperationException("Venue transitions require the Unity main-thread context.");
            int thread = Thread.CurrentThread.ManagedThreadId;
            var request = CancellationTokenSource.CreateLinkedTokenSource(owner);
            var previousRequest = _latestRequest;
            _latestRequest = request;
            previousRequest?.Cancel();
            bool locked = false;
            VenueSession session = null;
            try
            {
                await Transition.WaitAsync(request.Token); locked = true;
                request.Token.ThrowIfCancellationRequested();
                RequireHomeOwner(homeId, homeUserId);
                var previous = Current;
                if (!ReferenceEquals(previous, null)) await previous.ExitCore();
                request.Token.ThrowIfCancellationRequested();
                RequireHomeOwner(homeId, homeUserId);
                var go = new GameObject("VenueSession_" + venueId);
                DontDestroyOnLoad(go);
                session = go.AddComponent<VenueSession>();
                session._ownedObject = go; session._visit = request; session._visitToken = request.Token;
                session.HomeId = homeId;
                Current = session;
                var captured = session;
                session._exitRegistration = request.Token.Register(() =>
                {
                    if (Thread.CurrentThread.ManagedThreadId == thread) captured.RequestExit();
                    else context.Post(_ => captured.RequestExit(), null);
                });
                session._destroyRegistration = session.destroyCancellationToken.Register(() => captured.RequestExit());
                await session.Begin(venueId, joinRoom, voice);
                request.Token.ThrowIfCancellationRequested();
                RequireHomeOwner(homeId, homeUserId);
                session._ready = true;
                if (voice) _ = session.ConnectVoice();
                return session;
            }
            catch (Exception entering)
            {
                if (!ReferenceEquals(session, null))
                {
                    try { await session.ExitCore(); }
                    catch (Exception cleanup) { throw new AggregateException("Venue entry and cleanup failed.", entering, cleanup); }
                }
                throw;
            }
            finally
            {
                if (locked) Transition.Release();
                if (ReferenceEquals(session, null))
                {
                    if (ReferenceEquals(_latestRequest, request)) _latestRequest = null;
                    request.Dispose();
                }
            }
        }

        private async void RequestExit()
        {
            try { await Exit(); }
            catch (Exception error) { Log.Warn("venue", "Visit cleanup failed: " + error.GetType().Name); }
        }

        private async Task Begin(string venueId, bool joinRoom, bool voice)
        {
            VenueId = venueId;
            _loader = new VenueLoader();
            Venue = IsOwnHome ? await _loader.LoadPrivateHome(HomeId, LifetimeToken) : await _loader.Load(venueId, LifetimeToken);
            LifetimeToken.ThrowIfCancellationRequested();
            _avatarRoot = new GameObject("Avatars").transform;
            _avatarRoot.SetParent(transform, false);

            var rig = CameraRig.Ensure();
            rig.Bounds = Venue.cameraBounds;
            AvatarLod.Ensure();

            var vdef = Services.Get<ConfigStore>().Registries.Venue(venueId);
            CTLife.Audio.Music.SetVenue(venueId, Venue.districtId, vdef != null && vdef.isClub);

            _subs = new Subscriptions();
            WorldInput.SpotTapped += OnSpotTapped;

            if (joinRoom)
            {
                var rt = Services.Get<IRealtimeService>();
                Channel = await VenueWait.Run(rt.JoinVenue(venueId, LifetimeToken), LifetimeToken);
                LifetimeToken.ThrowIfCancellationRequested();
                await RequireRequestedVenue(Channel, venueId);
                Channel.PlayerAdded += OnPlayerAdded;
                Channel.PlayerChanged += OnPlayerChanged;
                Channel.PlayerRemoved += OnPlayerRemoved;
                Channel.NpcAdded += OnNpcAdded;
                Channel.NpcChanged += OnNpcChanged;
                Channel.NpcRemoved += OnNpcRemoved;
                Channel.Said += OnSaid;
                Channel.Rejected += OnRejected;
                // State that arrived before the handlers were attached.
                foreach (var kv in new List<KeyValuePair<string, PlayerState>>(Channel.Players)) if (!_players.ContainsKey(kv.Key)) OnPlayerAdded(kv.Key, kv.Value);
                foreach (var kv in new List<KeyValuePair<string, NpcState>>(Channel.Npcs)) if (!_npcs.ContainsKey(kv.Key)) OnNpcAdded(kv.Key, kv.Value);
                if (LocalPlayer == null) SpawnLocal(null);
                Bus.Publish(new VenueChangedSignal { venueId = venueId, instanceId = Channel.InstanceId });
            }
            else
            {
                SpawnLocal(null);
                Bus.Publish(new VenueChangedSignal { venueId = venueId });
            }
        }

        /// <summary>
        /// The room must be the venue we asked for. A server without `filterBy(['venueId'])` (backend main, feedback
        /// item 39) can put a Terminal Café join into the Timeline Square room; rendering those players in the café
        /// would be wrong, so the join is treated as failed (the caller enters offline and says so).
        /// </summary>
        private async Task RequireRequestedVenue(VenueChannel channel, string venueId)
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (string.IsNullOrEmpty(channel.VenueId) && Time.realtimeSinceStartup < deadline)
                await Awaitable.WaitForSecondsAsync(0.05f, LifetimeToken);
            var actual = channel.VenueId;
            if (VenueRoomCheck.Matches(venueId, actual)) return;
            Log.Warn("venue", $"Joined room {channel.InstanceId} is venue '{actual}', not '{venueId}'; leaving it");
            throw new InvalidOperationException($"The server put this visit in '{actual}' instead of '{venueId}'.");
        }

        public Task Exit()
        {
            if (_cleaned) return Task.CompletedTask;
            if (_exitTask != null) return _exitTask;
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _exitTask = done.Task; // Install before cancellation can invoke RequestExit re-entrantly.
            _ready = false;
            _visit?.Cancel();
            _ = FinishExit(done);
            return done.Task;
        }

        private async Task FinishExit(TaskCompletionSource<bool> done)
        {
            await Transition.WaitAsync();
            try { await ExitCore(); done.TrySetResult(true); }
            catch (Exception error) { done.TrySetException(error); }
            finally { Transition.Release(); }
        }

        private Task ExitCore() => _cleaned ? Task.CompletedTask : _cleanupTask ?? (_cleanupTask = CleanupOwned());

        private async Task CleanupOwned()
        {
            _closing = true; _ready = false;
            var failures = new List<Exception>();
            void Attempt(Action release) { try { release(); } catch (Exception error) { failures.Add(error); } }
            async Task AttemptAsync(Func<Task> release)
            {
                try { await release(); } catch (Exception error) { failures.Add(error); }
            }

            // Detach every owned reference even when another release fails. The first caller observes
            // the collected failure; a failed leave must not strand Current or poison the next visit.
            Attempt(() => _exitRegistration.Dispose()); Attempt(() => _destroyRegistration.Dispose());
            Attempt(() => _visit?.Cancel());
            Attempt(() => WorldInput.SpotTapped -= OnSpotTapped);
            var subs = _subs; _subs = null;
            Attempt(() => subs?.Dispose());
            var channel = Channel; Channel = null;
            if (channel != null)
            {
                channel.PlayerAdded -= OnPlayerAdded; channel.PlayerChanged -= OnPlayerChanged; channel.PlayerRemoved -= OnPlayerRemoved;
                channel.NpcAdded -= OnNpcAdded; channel.NpcChanged -= OnNpcChanged; channel.NpcRemoved -= OnNpcRemoved;
                channel.Said -= OnSaid; channel.Rejected -= OnRejected;
                await AttemptAsync(() => channel.Leave()); // Captured visit only, never a successor room.
            }
            var voice = _voice; _voice = null;
            if (voice != null)
            {
                Attempt(() => voice.LocalSpeakingChanged -= OnLocalSpeaking);
                if (ReferenceEquals(voice.SpeakerResolver, _speakerResolver)) voice.SpeakerResolver = null;
                if (ReferenceEquals(voice.PositionResolver, _positionResolver)) voice.PositionResolver = null;
                if (ReferenceEquals(voice.ListenerPosition, _listenerPosition)) voice.ListenerPosition = null;
                // Accepted voice ownership is cancelled by this visit's token.
            }
            foreach (var player in _players.Values) if (player != null) Attempt(() => Destroy(player.gameObject));
            foreach (var npc in _npcs.Values) if (npc != null) Attempt(() => Destroy(npc.gameObject));
            _players.Clear(); _npcs.Clear(); _byUserId.Clear();
            var local = LocalPlayer; LocalPlayer = null;
            if (local != null) Attempt(() => Destroy(local.gameObject));
            var loader = _loader; _loader = null;
            if (loader != null) await AttemptAsync(() => loader.Unload());
            Venue = null; _avatarRoot = null;
            if (ReferenceEquals(Current, this))
            {
                Current = null;
                Attempt(() => Bus.Publish(new VenueChangedSignal { venueId = null }));
            }
            if (ReferenceEquals(_latestRequest, _visit)) _latestRequest = null;
            Attempt(() => _visit?.Dispose());
            _cleaned = true;
            var ownedObject = _ownedObject; _ownedObject = null;
            if (ownedObject != null) Attempt(() => Destroy(ownedObject));
            if (failures.Count > 0) throw new AggregateException("Venue cleanup failed.", failures);
        }

        private void OnDestroy() { if (_visit != null && !_cleaned) RequestExit(); }

        // ---------------------------------------------------------------- players

        private void SpawnLocal(PlayerState state)
        {
            var profile = Services.TryGet<PlayerProfile>(out var pp) ? pp : null;
            var look = state != null ? ToArray(state.look) : profile?.LookArray();
            var name = !string.IsNullOrEmpty(state?.name) ? state.name : profile?.Alias ?? "you";
            if (look == null || look.Length != 7 || string.IsNullOrEmpty(look[1])) look = profile?.LookArray();
            var view = AvatarFactory.Create(name, look, false, null, _avatarRoot);
            // The session's own user id: a server that does not verify the JWT reports a placeholder (item 38).
            view.userId = Services.Get<Session>().UserId ?? state?.userId;
            view.sessionId = Channel?.SessionId;
            // (0,0) is the schema default: the server has not placed us yet. Spawn at the entrance and publish it.
            bool placed = state != null && !VenueRoomCheck.IsUnplaced(state.x, state.z);
            view.transform.position = placed ? Venue.ToWorld((float)state.x, (float)state.z) : Venue.EntrancePosition;
            LocalPlayer = view.gameObject.AddComponent<LocalPlayerController>();
            LocalPlayer.Init(Venue, Channel);
            // Player-chosen interactions (CTA): nothing sits or uses anything until the player presses it.
            view.gameObject.AddComponent<InteractionController>().Init(Venue, Channel, this);
            if (state != null) LocalPlayer.OnServerSeat(state.spotId, state.anim);
            if (!string.IsNullOrEmpty(view.userId)) _byUserId[view.userId] = view;
            var rig = CameraRig.Ensure();
            rig.Target = view.transform;
            rig.SnapToTarget();
        }

        private void OnPlayerAdded(string sessionId, PlayerState p)
        {
            if (!OwnsVisit) return;
            if (Channel != null && sessionId == Channel.SessionId)
            {
                if (LocalPlayer == null) SpawnLocal(p);
                else
                {
                    var v = LocalPlayer.GetComponent<AvatarView>();
                    if (!string.IsNullOrEmpty(p.name)) v.displayName = p.name;
                    v.isSeeker = p.isSeeker;
                    v.minerTier = p.minerTier;
                    LocalPlayer.OnServerSeat(p.spotId, p.anim);
                }
                return;
            }
            if (_players.ContainsKey(sessionId)) return;
            var look = ToArray(p.look) ?? RemoteLookFallback?.Invoke(sessionId);
            var view = AvatarFactory.Create(string.IsNullOrEmpty(p.name) ? "player" : p.name, look, false, null, _avatarRoot);
            view.userId = p.userId;
            view.sessionId = sessionId;
            var rp = view.gameObject.AddComponent<RemotePlayer>();
            rp.Init(Venue, p);
            _players[sessionId] = rp;
            // Never let another player's identity overwrite ours (a non-verifying server gives everyone the same id).
            if (!string.IsNullOrEmpty(p.userId) && p.userId != Services.Get<Session>().UserId && !_byUserId.ContainsKey(p.userId))
                _byUserId[p.userId] = view;
        }

        private void OnPlayerChanged(string sessionId, PlayerState p)
        {
            if (!OwnsVisit) return;
            if (Channel != null && sessionId == Channel.SessionId)
            {
                var v = LocalPlayer != null ? LocalPlayer.GetComponent<AvatarView>() : null;
                if (v != null)
                {
                    v.SetTalking(p.talking);
                    LocalPlayer.OnServerSeat(p.spotId, p.anim);
                    // The local emote already played on tap (EmotePicker); the server echo is ignored here.
                }
                return;
            }
            if (_players.TryGetValue(sessionId, out var rp)) rp.Apply(p);
        }

        private void OnPlayerRemoved(string sessionId, PlayerState p)
        {
            if (!OwnsVisit) return;
            if (_players.TryGetValue(sessionId, out var rp))
            {
                _players.Remove(sessionId);
                if (p != null && !string.IsNullOrEmpty(p.userId) && _byUserId.TryGetValue(p.userId, out var mapped) &&
                    rp != null && ReferenceEquals(mapped, rp.GetComponent<AvatarView>())) _byUserId.Remove(p.userId);
                if (rp != null) Destroy(rp.gameObject);
            }
        }

        // ---------------------------------------------------------------- NPCs

        private void OnNpcAdded(string npcId, NpcState n)
        {
            if (!OwnsVisit) return;
            if (_npcs.ContainsKey(npcId)) return;
            var def = Services.Get<ConfigStore>().Registries.Archetype(n.archetypeId);
            var view = AvatarFactory.CreateNpc(def?.name ?? n.archetypeId, npcId, n.archetypeId, _avatarRoot);
            var npc = view.gameObject.AddComponent<NpcController>();
            npc.Init(npcId, n, Venue, FindUser);
            _npcs[npcId] = npc;
        }

        private void OnNpcChanged(string npcId, NpcState n)
        {
            if (!OwnsVisit) return;
            if (_npcs.TryGetValue(npcId, out var npc) && npc != null)
            {
                if (!npc.gameObject.activeSelf && n.state != "leave" && !KeepNpcHidden(npc, n)) npc.gameObject.SetActive(true);
                npc.Apply(n);
            }
        }

        private static bool KeepNpcHidden(NpcController npc, NpcState n)
        {
            if (NpcController.PresentKeepHidden == null) return false;
            try { return NpcController.PresentKeepHidden(npc, n); }
            catch (Exception e) { Log.Exception("npc-present", e); return false; }
        }

        private void OnNpcRemoved(string npcId, NpcState n)
        {
            if (!OwnsVisit) return;
            if (_npcs.TryGetValue(npcId, out var npc))
            {
                _npcs.Remove(npcId);
                if (npc != null) Destroy(npc.gameObject);
            }
        }

        private Transform FindUser(string userId) =>
            !string.IsNullOrEmpty(userId) && _byUserId.TryGetValue(userId, out var v) && v != null ? v.transform : null;

        public AvatarView AvatarForUser(string userId) =>
            !string.IsNullOrEmpty(userId) && _byUserId.TryGetValue(userId, out var v) ? v : null;

        // ---------------------------------------------------------------- chat, input, rejections

        private void OnSaid(SaidMessage m)
        {
            if (!OwnsVisit) return;
            AvatarView v = null;
            if (Channel != null && m.sessionId == Channel.SessionId && LocalPlayer != null) v = LocalPlayer.GetComponent<AvatarView>();
            else if (_players.TryGetValue(m.sessionId ?? "", out var rp) && rp != null) v = rp.GetComponent<AvatarView>();
            if (v != null) Bus.Publish(new BubbleSignal { anchor = v.HeadAnchor, text = m.text, isNpc = false, seconds = 4.5f });
        }

        private void OnRejected(RejectedMessage r)
        {
            if (!OwnsVisit) return;
            if (r.type == "sit") LocalPlayer?.OnSeatRejected();
            if (r.type == "move" && LocalPlayer != null && Channel != null &&
                Channel.TryGetPlayer(Channel.SessionId, out var me))
                LocalPlayer.SnapTo(Venue.ToWorld((float)me.x, (float)me.z), (float)me.rotY);
            if (r.code == "SPOT_TAKEN") Bus.Publish(ToastSignal.Id("toast_spot_taken", new Dictionary<string, string>()));
            else if (r.code == "RATE_LIMITED") Bus.Publish(ToastSignal.Text("Slow down", ToastKind.Bad));
        }

        private void OnSpotTapped(SpotAnchor spot)
        {
            if (!OwnsVisit || IsOwnHome) return;
            // Own Home currently supports exploration and the existing phone needs actions.
            // A tap only offers the seat (the CTA, from farther away); the player chooses to sit with the button.
            InteractionController.Current?.Focus(spot);
        }

        // ---------------------------------------------------------------- voice

        public Task<bool> ConnectVoice()
        {
            if (!IsActive) return Task.FromResult(false);
            if (_voiceConnect != null && !_voiceConnect.IsCompleted) return _voiceConnect;
            return _voiceConnect = ConnectVoiceOwned();
        }

        private async Task<bool> ConnectVoiceOwned()
        {
            if (!Services.TryGet<VoiceService>(out var voice) || Channel == null || !IsActive) return false;
            if (!Features.IsAvailable(Features.Voice)) return false;
            var channel = Channel;
            var tokenOwner = LifetimeToken;
            try
            {
                var env = Env.Current;
                string url, token;
                if (!string.IsNullOrEmpty(env.liveKitDevToken))
                {
                    url = env.liveKitUrlOverride; token = env.liveKitDevToken;
                }
                else
                {
                    var response = await VenueWait.Run(Services.Get<Api>().RtcToken("venue", channel.InstanceId, tokenOwner), tokenOwner);
                    url = string.IsNullOrEmpty(env.liveKitUrlOverride) ? response.Url : env.liveKitUrlOverride;
                    token = response.Token;
                }
                if (!IsActive || !ReferenceEquals(Channel, channel) || !Services.TryGet<VoiceService>(out var currentVoice) || !ReferenceEquals(voice, currentVoice)) return false;
                _voice = voice;
                _speakerResolver = id => IsActive ? AvatarForUser(id)?.Voice : null;
                _positionResolver = id => { var avatar = IsActive ? AvatarForUser(id) : null; return avatar != null ? avatar.transform.position : (Vector3?)null; };
                _listenerPosition = () => IsActive && LocalPlayer != null ? LocalPlayer.transform.position : Vector3.zero;
                voice.SpeakerResolver = _speakerResolver; voice.PositionResolver = _positionResolver; voice.ListenerPosition = _listenerPosition;
                voice.LocalSpeakingChanged -= OnLocalSpeaking; voice.LocalSpeakingChanged += OnLocalSpeaking;
                return await VenueWait.Run(voice.Connect(url, token, "venue:" + channel.InstanceId, tokenOwner), tokenOwner);
            }
            catch (OperationCanceledException) when (tokenOwner.IsCancellationRequested) { return false; }
            catch (Exception error)
            {
                if (IsActive) Log.Warn("voice", "Voice unavailable: " + error.GetType().Name);
                return false;
            }
        }

        private void OnLocalSpeaking(bool on) { if (IsActive) Channel?.Talking(on); }

        private static string[] ToArray(Colyseus.Schema.ArraySchema<string> a)
        {
            if (a == null || a.Count == 0) return null;
            return ToArrayCore(a);
        }

        private static string[] ToArrayCore(Colyseus.Schema.ArraySchema<string> a)
        {
            var r = new string[7];
            if (a == null) return r;
            for (int i = 0; i < 7 && i < a.Count; i++) r[i] = a[i];
            return r;
        }
    }
}
