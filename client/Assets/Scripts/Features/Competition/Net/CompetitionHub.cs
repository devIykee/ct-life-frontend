using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;

namespace CTLife.Competition
{
    /// <summary>
    /// Composition-root glue for Phase 6, independent of any open screen: binds the shared <see cref="CompetitionFeed"/> to the
    /// session, profile and toast layer and receives the raw city/venue room messages. Call <see cref="Bind"/> after sign-in and
    /// <see cref="Unbind"/> on sign-out (hooks in docs/progress/requests.md). Safe to call repeatedly.
    /// </summary>
    public static class CompetitionRuntime
    {
        private static bool _bound;

        public static CompetitionFeed Feed => CompetitionFeed.Shared;

        public static void Bind()
        {
            var f = Feed;
            f.MyUserId = () => Services.TryGet<Session>(out var s) && s.IsSignedIn ? s.UserId : null;
            f.MyTribeId = () => Services.TryGet<PlayerProfile>(out var p) ? p.Me?.Character?.TribeId : null;
            f.TribeName = TribeName;
            f.NowMs = () => (long)ServerClock.NowMs;
            f.Toast = (text, tone) => Bus.Publish(ToastSignal.Text(text, tone == ToastTone.Good ? ToastKind.Good : tone == ToastTone.Bad ? ToastKind.Bad : ToastKind.Info));
            _bound = true;
        }

        public static void Unbind()
        {
            Feed.Reset();
            Feed.Toast = null;
            CompetitionHub.DisposeShared();
            _bound = false;
        }

        /// <summary>Raw room message sink: wire it to `CityChannel.RawMessage` and `VenueChannel.RawMessage`.</summary>
        public static void OnRaw(string type, object payload)
        {
            if (!_bound) Bind();
            try { Feed.Dispatch(type, payload); }
            catch (Exception e) { Log.Exception("competition", e); }
        }

        public static string TribeName(string tribeId)
        {
            if (string.IsNullOrEmpty(tribeId)) return "-";
            var hub = CompetitionHub.Current;
            var server = hub?.Tribes?.FirstOrDefault(t => t.Id == tribeId);
            if (server != null && !string.IsNullOrEmpty(server.Name)) return server.Name;
            if (Services.TryGet<ConfigStore>(out var cfg) && cfg.Registries.Tribes.TryGetValue(tribeId, out var def) && !string.IsNullOrEmpty(def.name)) return def.name;
            return CompStrings.Readable(tribeId);
        }
    }

    /// <summary>
    /// Shared Phase 6 state while any competition screen exists: the API, server caches and the retained Idempotency-Keys of
    /// writes whose outcome is unknown. Reference counted by the screens; the last release disposes it.
    /// </summary>
    public sealed class CompetitionHub : IDisposable
    {
        private static CompetitionHub _shared;
        private int _refs;
        private readonly string _user;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();

        public readonly CompetitionApi Api;
        public readonly CompetitionFeed Feed = CompetitionFeed.Shared;
        public bool Disposed { get; private set; }
        public CancellationToken Life => _life.Token;

        public List<TribeInfo> Tribes { get; private set; }
        public List<LeagueInfo> Leagues { get; private set; }

        /// <summary>One retained key per write kind; a different request body gets a new key automatically.</summary>
        public readonly Dictionary<string, RetainedKey> Keys = new Dictionary<string, RetainedKey>();

        public static CompetitionHub Current => _shared != null && !_shared.Disposed ? _shared : null;

        public static CompetitionHub Acquire()
        {
            var session = Services.Get<Session>();
            if (_shared == null || _shared.Disposed || _shared._user != session.UserId) _shared = new CompetitionHub(session.UserId);
            _shared._refs++;
            return _shared;
        }

        internal static void DisposeShared() { _shared?.Dispose(); _shared = null; }

        public void Release()
        {
            if (Disposed) return;
            if (--_refs <= 0) Dispose();
        }

        private CompetitionHub(string userId)
        {
            _user = userId;
            Api = new CompetitionApi(Services.Get<IRestClient>());
            CompetitionRuntime.Bind();
            Feed.LeagueName = id => LeagueRules.LeagueLabel(Leagues, id);
            Feed.DistrictName = id => Feed.Districts.Get(id)?.Name ?? CompStrings.Readable(id);
        }

        public string UserId => _user;
        public long NowMs => (long)ServerClock.NowMs;
        public PlayerProfile Profile => Services.TryGet<PlayerProfile>(out var p) ? p : null;
        public string MyTribeId => Profile?.Me?.Character?.TribeId;

        /// <summary>Conversations with the two LLM NPCs, kept while the hub lives so reopening the app keeps the thread.</summary>
        public readonly Dictionary<string, ChatSession> Chats = new Dictionary<string, ChatSession>();

        public ChatSession ChatFor(string archetype)
        {
            if (!Chats.TryGetValue(archetype, out var c)) Chats[archetype] = c = new ChatSession(archetype);
            return c;
        }

        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();

        /// <summary>Cached alias, or null when not fetched yet.</summary>
        public string AliasCached(string userId) => userId != null && _aliases.TryGetValue(userId, out var a) ? a : null;

        /// <summary>Alias for a user id (cached). Falls back to a neutral label when the profile cannot be read.</summary>
        public async Task<string> AliasOf(string userId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(userId)) return CompStrings.T("arena.waiting");
            if (_aliases.TryGetValue(userId, out var cached)) return cached;
            string alias;
            try { alias = await Api.Alias(userId, ct); }
            catch (Exception e) when (!(e is OperationCanceledException)) { alias = null; }
            if (string.IsNullOrEmpty(alias)) return CompStrings.T("arena.anon");
            _aliases[userId] = alias;
            return alias;
        }

        public RetainedKey KeyFor(string kind)
        {
            if (!Keys.TryGetValue(kind, out var k)) Keys[kind] = k = new RetainedKey();
            return k;
        }

        // ---------------------------------------------------------------- cached reads

        public async Task<List<TribeInfo>> LoadTribes(CancellationToken ct)
        {
            Tribes = await Api.Tribes(ct);
            return Tribes;
        }

        public async Task<List<LeagueInfo>> LoadLeagues(CancellationToken ct)
        {
            Leagues = await Api.Leagues(ct);
            return Leagues;
        }

        /// <summary>Fetches `GET /districts` into the shared board the city map reads.</summary>
        public async Task<DistrictBoard> LoadDistricts(CancellationToken ct)
        {
            var list = await Api.Districts(ct);
            Feed.Districts.ApplyList(list);
            return Feed.Districts;
        }

        /// <summary>Server access answer for a gated venue; null when the endpoint is missing so the caller lets the room's onAuth decide.</summary>
        public async Task<VenueAccess> TryAccess(string venueId, CancellationToken ct)
        {
            try { return await Api.Access(venueId, ct); }
            catch (Exception e) when (CompErrors.From(e, false).IsUnavailable) { return null; }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            if (ReferenceEquals(_shared, this)) _shared = null;
        }
    }

    /// <summary>
    /// Entry points the rest of the client calls (all pure or thin): which phone app a venue or NPC opens, venue access checks
    /// before joining a gated room, and join-rejection wording. See the hooks in docs/progress/requests.md.
    /// </summary>
    public static class CompetitionLinks
    {
        public const string BeefApp = "beef", FarmApp = "farm", CabalApp = "cabal", LeagueApp = "league", ChatApp = "npcchat", XLinkApp = "xlink";

        /// <summary>Set by the composition root: opens a phone app by id (PhoneShell.Open).</summary>
        public static Action<string> OpenApp;

        /// <summary>Phone app a venue's interactive spot opens, or null.</summary>
        public static string AppForVenue(string venueId)
        {
            switch (venueId)
            {
                case "ratio_arena": return BeefApp;
                case "airdrop_farm": case "airdrop_fields": case "sybil_barn": return FarmApp;
                case "cabal_lounge": case "dubai_marina": return CabalApp;
                default: return null;
            }
        }

        public static string PendingChatArchetype { get; private set; }
        public static string PendingChatNpcId { get; private set; }
        public static string PendingCabalGuestId { get; private set; }
        public static string PendingCabalGuestName { get; private set; }

        /// <summary>Open the Chat app on the AI Agent or Sleuth tab. Returns false for any other archetype.</summary>
        public static bool OpenNpcChat(string archetypeId, string npcId)
        {
            if (!ChatSession.IsLlmNpc(archetypeId)) return false;
            PendingChatArchetype = archetypeId;
            PendingChatNpcId = npcId;
            OpenApp?.Invoke(ChatApp);
            return true;
        }

        public static void TakePendingChat(out string archetype, out string npcId)
        {
            archetype = PendingChatArchetype;
            npcId = PendingChatNpcId;
            PendingChatArchetype = PendingChatNpcId = null;
        }

        /// <summary>Profile card "Invite to Cabal": opens the Cabal app with that player ready to invite (the server still decides).</summary>
        public static void InviteToCabal(string userId, string name)
        {
            if (!CompWire.IsSafeId(userId)) return;
            PendingCabalGuestId = userId;
            PendingCabalGuestName = CompWire.Plain(name, 32);
            OpenApp?.Invoke(CabalApp);
        }

        public static void TakePendingCabalGuest(out string userId, out string name)
        {
            userId = PendingCabalGuestId;
            name = PendingCabalGuestName;
            PendingCabalGuestId = PendingCabalGuestName = null;
        }

        /// <summary>Profile card "Challenge to beef".</summary>
        public static string PendingBeefOpponentId { get; private set; }
        public static string PendingBeefOpponentName { get; private set; }

        public static void ChallengeToBeef(string userId, string name)
        {
            if (!CompWire.IsSafeId(userId)) return;
            PendingBeefOpponentId = userId;
            PendingBeefOpponentName = CompWire.Plain(name, 32);
            OpenApp?.Invoke(BeefApp);
        }

        public static void TakePendingOpponent(out string userId, out string name)
        {
            userId = PendingBeefOpponentId;
            name = PendingBeefOpponentName;
            PendingBeefOpponentId = PendingBeefOpponentName = null;
        }

        /// <summary>
        /// Call before `JoinVenue` for a gated venue. Returns null to go ahead (not gated, allowed, or the server cannot answer yet and the
        /// room's own check decides), else the message to show the player.
        /// </summary>
        public static async Task<string> BlockMessageBeforeJoin(string venueId, CancellationToken ct)
        {
            if (!GateRules.IsGated(venueId)) return null;
            CompetitionHub hub = null;
            try
            {
                hub = CompetitionHub.Acquire();
                var access = await hub.TryAccess(venueId, ct);
                return GateRules.Message(access);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Log.Warn("competition", "venue access check failed: " + CompErrors.From(e, false).Code);
                return null;
            }
            finally { hub?.Release(); }
        }
    }
}
