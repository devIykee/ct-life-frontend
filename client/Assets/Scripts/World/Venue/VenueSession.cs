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

        /// <summary>
        /// CT Island (Phase B): enter the connected open world at <paramref name="venueId"/>'s door. Walking into another venue
        /// switches zone and room without a load; the map's Go is a fast travel within the island (<see cref="FastTravel"/>).
        /// </summary>
        public static Task<VenueSession> EnterIsland(string venueId, bool joinRoom = true, CancellationToken owner = default) =>
            EnterCore(venueId, joinRoom, false, owner, null, null, island: true);

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

        private static async Task<VenueSession> EnterCore(string venueId, bool joinRoom, bool voice, CancellationToken owner, string homeId, string homeUserId, bool island = false)
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
                await session.Begin(venueId, joinRoom, voice, island);
                request.Token.ThrowIfCancellationRequested();
                RequireHomeOwner(homeId, homeUserId);
                session._ready = true;
                if (island && !joinRoom && IslandDemo.Active) session.SpawnDemoNpcs(venueId);
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

        private async Task Begin(string venueId, bool joinRoom, bool voice, bool island = false)
        {
            VenueId = venueId;
            if (island)
            {
                _island = await IslandWorld.Load(LifetimeToken);
                Venue = await _island.Ensure(venueId, LifetimeToken);
                _zone = _island.Find(venueId);
            }
            else
            {
                _loader = new VenueLoader();
                Venue = IsOwnHome ? await _loader.LoadPrivateHome(HomeId, LifetimeToken) : await _loader.Load(venueId, LifetimeToken);
            }
            LifetimeToken.ThrowIfCancellationRequested();
            _avatarRoot = new GameObject("Avatars").transform;
            _avatarRoot.SetParent(transform, false);

            var rig = CameraRig.Ensure();
            if (island) rig.ConfigureVenue(_island.Root);
            rig.Bounds = island ? _island.Root.cameraBounds : Venue.cameraBounds;
            _online = joinRoom;
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
                Attach(Channel);
                if (LocalPlayer == null) SpawnLocal(null);
                Bus.Publish(new VenueChangedSignal { venueId = venueId, instanceId = Channel.InstanceId });
            }
            else
            {
                SpawnLocal(null);
                Bus.Publish(new VenueChangedSignal { venueId = venueId });
            }
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
            var islandWorld = _island; _island = null; _zone = null;
            if (islandWorld != null) await AttemptAsync(() => islandWorld.Unload());
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

        // ---------------------------------------------------------------- CT Island (open world)

        private IslandWorld _island;
        private IslandVenue _zone;
        private bool _online, _handoffBusy;
        private string _placeKey;
        public bool IsIsland => _island != null;
        public IslandWorld Island => _island;

        private void Attach(VenueChannel channel)
        {
            channel.PlayerAdded += OnPlayerAdded; channel.PlayerChanged += OnPlayerChanged; channel.PlayerRemoved += OnPlayerRemoved;
            channel.NpcAdded += OnNpcAdded; channel.NpcChanged += OnNpcChanged; channel.NpcRemoved += OnNpcRemoved;
            channel.Said += OnSaid; channel.Rejected += OnRejected;
            // State that arrived before the handlers were attached.
            foreach (var kv in new List<KeyValuePair<string, PlayerState>>(channel.Players)) if (!_players.ContainsKey(kv.Key)) OnPlayerAdded(kv.Key, kv.Value);
            foreach (var kv in new List<KeyValuePair<string, NpcState>>(channel.Npcs)) if (!_npcs.ContainsKey(kv.Key)) OnNpcAdded(kv.Key, kv.Value);
        }

        private void Detach(VenueChannel channel)
        {
            channel.PlayerAdded -= OnPlayerAdded; channel.PlayerChanged -= OnPlayerChanged; channel.PlayerRemoved -= OnPlayerRemoved;
            channel.NpcAdded -= OnNpcAdded; channel.NpcChanged -= OnNpcChanged; channel.NpcRemoved -= OnNpcRemoved;
            channel.Said -= OnSaid; channel.Rejected -= OnRejected;
        }

        /// <summary>Remove the people of the room being left (the local player stays).</summary>
        private void ClearRoomActors()
        {
            foreach (var player in _players.Values) if (player != null) Destroy(player.gameObject);
            foreach (var npc in _npcs.Values) if (npc != null) Destroy(npc.gameObject);
            _players.Clear(); _npcs.Clear();
            var mine = LocalPlayer != null ? LocalPlayer.GetComponent<AvatarView>() : null;
            var keep = mine != null && !string.IsNullOrEmpty(mine.userId) ? mine.userId : null;
            _byUserId.Clear();
            if (keep != null) _byUserId[keep] = mine;
        }

        private void Update()
        {
            if (!IsIsland || !_ready || _closing || LocalPlayer == null) return;
            var pos = LocalPlayer.transform.position;
            _island.Stream(pos);
            var zone = _island.ZoneAt(pos);
            string key = zone != null ? zone.venueId : "street:" + _island.DistrictAt(pos);
            if (key != _placeKey)
            {
                _placeKey = key;
                Bus.Publish(new IslandPlaceSignal { venueId = zone?.venueId, districtId = zone != null ? zone.district : _island.DistrictAt(pos) });
            }
            if (_handoffBusy) return;
            var action = IslandStreaming.Decide(new Vector2(pos.x, pos.z), zone, _zone);
            if (action == IslandStreaming.RoomAction.Join) _ = Handoff(zone);
            else if (action == IslandStreaming.RoomAction.Leave) LeaveZone();
        }

        /// <summary>Walked into <paramref name="zone"/>: join its room (old room still joined), then switch and leave the old.</summary>
        private async Task Handoff(IslandVenue zone)
        {
            _handoffBusy = true;
            try
            {
                var root = await _island.Ensure(zone.venueId, LifetimeToken);
                VenueChannel next = null;
                if (_online && Services.TryGet<IRealtimeService>(out var rt))
                {
                    try { next = await rt.JoinVenueHandoff(zone.venueId, LifetimeToken); }
                    catch (OperationCanceledException) when (LifetimeToken.IsCancellationRequested) { throw; }
                    catch (Exception e)
                    {
                        Log.Warn("island", $"Room for {zone.venueId} refused ({e.Message}); exploring without its room.");
                        if (Channel != null) { var old = Channel; Detach(old); Channel = null; _ = rt.LeaveVenue(); }
                    }
                }
                if (!OwnsVisit || LocalPlayer == null) return;
                if (Channel != null && !ReferenceEquals(Channel, next)) Detach(Channel);   // its leave runs inside the handoff
                ClearRoomActors();
                Channel = next;
                Venue = root; VenueId = zone.venueId; _zone = zone;
                LocalPlayer.SetRoom(root, next);
                LocalPlayer.GetComponent<InteractionController>()?.Rebind(root, next);
                if (next != null) Attach(next);
                Log.Info("island", $"Zone {zone.venueId}{(next != null ? " room " + next.InstanceId : " (no room)")}");
                if (next == null && IslandDemo.Active) SpawnDemoNpcs(zone.venueId);
                Bus.Publish(new VenueChangedSignal { venueId = zone.venueId, instanceId = next?.InstanceId, seamless = true });
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("island", "Zone handoff failed: " + e.Message); }
            finally { _handoffBusy = false; }
        }

        /// <summary>Far out on the street: leave the last venue's room (no street room exists yet; see the server asks).</summary>
        private void LeaveZone()
        {
            var old = Channel;
            if (old != null) { Detach(old); Channel = null; if (Services.TryGet<IRealtimeService>(out var rt)) _ = rt.LeaveVenue(); }
            ClearRoomActors();
            _zone = null;
            LocalPlayer.SetRoom(Venue, null);
            LocalPlayer.GetComponent<InteractionController>()?.Rebind(Venue, null);
            Log.Info("island", "On the street: room left");
        }

        /// <summary>The map's Go on the island: teleport to the venue's door and hand off to its room, no load screen.</summary>
        public async Task FastTravel(string venueId)
        {
            if (!IsIsland || LocalPlayer == null) return;
            var zone = _island.Find(venueId) ?? throw new InvalidOperationException(venueId + " is not on CT Island.");
            while (_handoffBusy) await Awaitable.NextFrameAsync();
            var root = await _island.Ensure(venueId, LifetimeToken);
            var arrival = VenueArrival.For(root, out float yaw);
            LocalPlayer.StopDirectInput();
            LocalPlayer.SnapTo(arrival, yaw);
            CameraRig.Instance?.SnapToTarget();
            if (_zone == null || _zone.venueId != venueId) await Handoff(zone);
        }

        // ---------------------------------------------------------------- players

        private void SpawnLocal(PlayerState state)
        {
            var profile = Services.TryGet<PlayerProfile>(out var pp) ? pp : null;
            var look = state != null ? ToArray(state.look) : profile?.LookArray();
            var name = !string.IsNullOrEmpty(state?.name) ? state.name : profile?.Alias ?? "you";
            if (look == null || look.Length != 7 || string.IsNullOrEmpty(look[1])) look = profile?.LookArray();
            var view = AvatarFactory.Create(name, look, false, null, _avatarRoot, likenessJobId: profile?.LikenessJobId);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            Log.Info("appearance", "Local player spawned with " +
                (string.IsNullOrEmpty(profile?.LikenessJobId) ? "stock" : "personal") + " profile appearance.");
#endif
            // The session's own user id: a server that does not verify the JWT reports a placeholder (item 38).
            view.userId = Services.Get<Session>().UserId ?? state?.userId;
            view.sessionId = Channel?.SessionId;
            // Arrive a few steps inside, clear of the door (VenueArrival); the first move publishes it to the room.
            var arrival = VenueArrival.For(Venue, out float arrivalYaw);
            view.transform.SetPositionAndRotation(arrival, Quaternion.Euler(0f, arrivalYaw * Mathf.Rad2Deg, 0f));
            Log.Info("venue", $"Arrival {VenueId}: local=({Venue.ToLocal(arrival).x:0.00}, {Venue.ToLocal(arrival).y:0.00})" +
                (state != null ? $" server=({state.x:0.00}, {state.z:0.00}) ignored" : ""));
            LocalPlayer = view.gameObject.AddComponent<LocalPlayerController>();
            view.gameObject.AddComponent<LocalPlayerMarker>();
            LocalPlayer.OpenWorld = IsIsland;
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

        /// <summary>
        /// Offline demo (IslandDemo): the venue's registry NPCs, idle at their homes, with no room behind them. Local
        /// presentation only; nothing is sent. Cleared like room NPCs when the player moves to another venue.
        /// </summary>
        private void SpawnDemoNpcs(string venueId)
        {
            if (!Services.TryGet<ConfigStore>(out var cfg) || Venue == null) return;
            int k = 0;
            foreach (var a in cfg.Registries.Archetypes.Values)
            {
                if (a.homeVenueIds == null || !a.homeVenueIds.Contains(venueId)) continue;
                int n = Mathf.Clamp(a.maxPerInstance, 1, 2);
                for (int i = 0; i < n && k < 8; i++, k++)
                {
                    try { OnNpcAdded($"demo_{a.id}_{i}", new NpcState { archetypeId = a.id, state = "idle" }); }
                    catch (Exception e) { Log.Warn("island", $"demo NPC {a.id}: {e.Message}"); }
                }
            }
            if (k > 0) Log.Info("island", $"islandDemo: {k} local NPCs at {venueId}");
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
            bool focused = InteractionController.Current != null && InteractionController.Current.Focus(spot);
            if (Debug.isDebugBuild) Log.Info("input", $"focus {spot.spotId}: {(InteractionController.Current == null ? "no controller" : focused ? "ok" : "refused")}");
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
