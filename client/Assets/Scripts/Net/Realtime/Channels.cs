using System;
using System.Collections.Generic;
using Colyseus.Schema;
using CTLife.Core;
using CTLife.Realtime;

namespace CTLife.Net
{
    /// <summary>
    /// Shared plumbing for rooms decoded with <see cref="DynamicSchema"/> (see <see cref="StateMapper"/> for why):
    /// keeps one typed copy per map entry and exactly one change subscription per live entry, released when the
    /// entry is removed (QA code-architecture §3 item 9: per-entity subscriptions used to accumulate until leave).
    /// </summary>
    internal sealed class DynamicMapMirror<T> where T : class
    {
        private readonly Dictionary<string, T> _items = new Dictionary<string, T>();
        private readonly Dictionary<string, Action> _unsubscribe = new Dictionary<string, Action>();
        public IReadOnlyDictionary<string, T> Items => _items;

        public void Bind(Func<Action, Action> track, StateCallbackStrategy<DynamicSchema> cb, string field, Func<T> create,
            Action<Func<string, object>, T> apply, Action<string, T> added, Action<string, T> changed, Action<string, T> removed)
        {
            track(cb.OnAdd<DynamicSchema>(field, (key, entry) =>
            {
                if (entry == null) return;
                if (!_items.TryGetValue(key, out var item)) _items[key] = item = create();
                apply(name => entry[name], item);
                if (_unsubscribe.TryGetValue(key, out var previous)) previous();
                _unsubscribe[key] = cb.OnChange(entry, () =>
                {
                    if (!_items.TryGetValue(key, out var current)) return;
                    apply(name => entry[name], current);
                    changed?.Invoke(key, current);
                });
                added?.Invoke(key, item);
            }));
            track(cb.OnRemove<DynamicSchema>(field, (key, entry) =>
            {
                if (_unsubscribe.TryGetValue(key, out var off)) { off(); _unsubscribe.Remove(key); }
                if (_items.TryGetValue(key, out var item)) { _items.Remove(key); removed?.Invoke(key, item); }
            }));
            track(Clear);
        }

        public int LiveSubscriptions => _unsubscribe.Count;

        private void Clear()
        {
            foreach (var off in _unsubscribe.Values)
            {
                try { off(); } catch { /* room already torn down */ }
            }
            _unsubscribe.Clear();
            _items.Clear();
        }
    }

    /// <summary>`city` room: online count, Market Mood, active events, user-targeted messages (contract section 5).</summary>
    public sealed class CityChannel : RoomChannel<DynamicSchema>
    {
        public CityChannel() : base("city") { }

        public event Action<EventMessage> EventReceived;
        public event Action<LiquidatedMessage> Liquidated;
        public event Action<PaymentConfirmedMessage> PaymentConfirmed;
        public event Action<SpaceUpdateMessage> SpaceUpdated;
        public event Action<DmMessage> DmReceived;
        public event Action<CallInviteMessage> CallInvite;
        public event Action<KickedMessage> Kicked;

        public int Online { get; private set; } = -1;
        public int Mood { get; private set; }

        protected override void OnJoined()
        {
            On<EventMessage>("event", m => { EventReceived?.Invoke(m); Bus.Publish(m); });
            On<LiquidatedMessage>("liquidated", m => { Liquidated?.Invoke(m); Bus.Publish(m); });
            On<PaymentConfirmedMessage>("payment_confirmed", m => { PaymentConfirmed?.Invoke(m); Bus.Publish(m); });
            On<SpaceUpdateMessage>("space_update", m => { SpaceUpdated?.Invoke(m); Bus.Publish(m); });
            On<DmMessage>("dm", m => { DmReceived?.Invoke(m); Bus.Publish(m); });
            On<CallInviteMessage>("call_invite", m => { CallInvite?.Invoke(m); Bus.Publish(m); });
            On<KickedMessage>("kicked", m => { Kicked?.Invoke(m); Bus.Publish(m); });
            On<ToastMessage>("toast", m => Bus.Publish(ToastSignal.Id(m.textId, m.@params)));

            var cb = Callbacks.Get(Room);
            Track(cb.OnChange(Room.State, ReadRoot));
            ReadRoot();
        }

        private void ReadRoot()
        {
            var state = State;
            if (state == null) return;
            int online = (int)StateMapper.Long(state["online"], Online);
            int mood = (int)StateMapper.Long(state["mood"], Mood);
            if (online != Online && online >= 0) { Online = online; Bus.Publish(new OnlineCountSignal { online = online }); }
            if (mood != Mood) { Mood = mood; Bus.Publish(new MoodSignal { bps = mood }); }
        }
    }

    /// <summary>`market` room: live fictional coins (0.5 Hz) and the big-trade tape (Phase 2).</summary>
    public sealed class MarketChannel : RoomChannel<DynamicSchema>
    {
        public MarketChannel() : base("market") { }

        public event Action<TapeMessage> Tape;
        public event Action<string, CoinState> CoinAdded;
        public event Action<string, CoinState> CoinChanged;
        public event Action<string> CoinRemoved;

        private DynamicMapMirror<CoinState> _coins;
        public IReadOnlyDictionary<string, CoinState> Coins => _coins?.Items ?? Empty<CoinState>.Map;

        /// <summary>
        /// Source compatibility for callers written against the typed `MarketState` (`State.coins`).
        /// Prefer <see cref="Coins"/>. Null until joined, like the typed state was.
        /// </summary>
        public new MarketStateView State => IsJoined ? new MarketStateView(Coins) : null;

        protected override void OnJoined()
        {
            On<TapeMessage>("tape", m => { Tape?.Invoke(m); Bus.Publish(m); });
            _coins = new DynamicMapMirror<CoinState>();
            _coins.Bind(TrackAndReturn, Callbacks.Get(Room), "coins", () => new CoinState(), StateMapper.ApplyCoin,
                (k, c) => CoinAdded?.Invoke(k, c), (k, c) => CoinChanged?.Invoke(k, c), (k, c) => CoinRemoved?.Invoke(k));
        }
    }

    /// <summary>Read-only typed view of the market room state (see <see cref="MarketChannel.State"/>).</summary>
    public sealed class MarketStateView
    {
        public readonly IReadOnlyDictionary<string, CoinState> coins;
        public MarketStateView(IReadOnlyDictionary<string, CoinState> coins) { this.coins = coins; }
    }

    /// <summary>
    /// `venue` room: players and NPCs of one venue instance at 10 Hz (contract section 5).
    /// Join with `{token, venueId}`; a full instance (50) makes the server open a new one.
    /// Players and NPCs are exposed as the contract's generated classes, filled by name from the live schema.
    /// </summary>
    public sealed class VenueChannel : RoomChannel<DynamicSchema>
    {
        public VenueChannel() : base("venue") { }

        /// <summary>The venue the server says this room is (may differ from the requested one on a broken server).</summary>
        public string VenueId => State != null ? StateMapper.Str(State["venueId"], null) : null;

        public event Action<string, PlayerState> PlayerAdded;
        public event Action<string, PlayerState> PlayerChanged;
        public event Action<string, PlayerState> PlayerRemoved;
        public event Action<string, NpcState> NpcAdded;
        public event Action<string, NpcState> NpcChanged;
        public event Action<string, NpcState> NpcRemoved;
        public event Action<SaidMessage> Said;

        private DynamicMapMirror<PlayerState> _players;
        private DynamicMapMirror<NpcState> _npcs;
        public IReadOnlyDictionary<string, PlayerState> Players => _players?.Items ?? Empty<PlayerState>.Map;
        public IReadOnlyDictionary<string, NpcState> Npcs => _npcs?.Items ?? Empty<NpcState>.Map;
        /// <summary>Change subscriptions currently held (one per live player/NPC). Exposed for leak tests.</summary>
        public int LiveEntitySubscriptions => (_players?.LiveSubscriptions ?? 0) + (_npcs?.LiveSubscriptions ?? 0);

        public bool TryGetPlayer(string sessionId, out PlayerState player)
        {
            player = null;
            return !string.IsNullOrEmpty(sessionId) && Players.TryGetValue(sessionId, out player);
        }

        protected override void OnJoined()
        {
            On<SaidMessage>("said", m => { Said?.Invoke(m); Bus.Publish(m); });
            On<ToastMessage>("toast", m => Bus.Publish(ToastSignal.Id(m.textId, m.@params)));

            var cb = Callbacks.Get(Room);
            _players = new DynamicMapMirror<PlayerState>();
            _players.Bind(TrackAndReturn, cb, "players", () => new PlayerState(), StateMapper.ApplyPlayer,
                (k, p) => PlayerAdded?.Invoke(k, p), (k, p) => PlayerChanged?.Invoke(k, p), (k, p) => PlayerRemoved?.Invoke(k, p));
            _npcs = new DynamicMapMirror<NpcState>();
            _npcs.Bind(TrackAndReturn, cb, "npcs", () => new NpcState(), StateMapper.ApplyNpc,
                (k, n) => NpcAdded?.Invoke(k, n), (k, n) => NpcChanged?.Invoke(k, n), (k, n) => NpcRemoved?.Invoke(k, n));
        }

        // ---- client → server (contract section 5). The server enforces the rate limits; we also throttle. ----

        private float _lastMoveAt = -1f;
        private float _lastEmoteAt = -10f;
        private float _lastSayAt = -10f;

        /// <summary>At most 10 per second. Returns false when throttled.</summary>
        public bool Move(float x, float z, float rotY)
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastMoveAt < 1f / RealtimeLimitsClient.MovePerSecond) return false;
            _lastMoveAt = now;
            _ = Send("move", new MoveMessage { x = x, z = z, rotY = rotY });
            return true;
        }

        public void Sit(string spotId) => _ = Send("sit", new SitMessage { spotId = spotId });

        public void Stand() => _ = Send("stand", new StandMessage());

        public bool Emote(string emoteId)
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastEmoteAt < RealtimeLimitsClient.EmoteIntervalSeconds) return false;
            _lastEmoteAt = now;
            _ = Send("emote", new EmoteMessage { emoteId = emoteId });
            return true;
        }

        /// <summary>140 chars, once per 2 s. Returns the seconds to wait if throttled, else 0.</summary>
        public float Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var now = UnityEngine.Time.realtimeSinceStartup;
            var wait = RealtimeLimitsClient.SayIntervalSeconds - (now - _lastSayAt);
            if (wait > 0) return wait;
            _lastSayAt = now;
            if (text.Length > RealtimeLimitsClient.SayMaxChars) text = text.Substring(0, RealtimeLimitsClient.SayMaxChars);
            _ = Send("say", new SayMessage { text = text });
            return 0;
        }

        public void Talking(bool on) => _ = Send("talking", new TalkingMessage { on = on });

        public void NpcTap(string npcId) => _ = Send("npc_tap", new NpcTapMessage { npcId = npcId });
    }

    /// <summary>Client-side throttles mirroring contract section 5 (the server enforces the same limits).</summary>
    public static class RealtimeLimitsClient
    {
        public const float MovePerSecond = 10f;
        public const float EmoteIntervalSeconds = 1f;
        public const float SayIntervalSeconds = 2f;
        public const int SayMaxChars = 140;
    }

    internal static class Empty<T>
    {
        public static readonly IReadOnlyDictionary<string, T> Map = new Dictionary<string, T>();
    }
}
