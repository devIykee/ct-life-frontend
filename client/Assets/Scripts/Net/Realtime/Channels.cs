using System;
using Colyseus.Schema;
using CTLife.Core;
using CTLife.Realtime;

namespace CTLife.Net
{
    /// <summary>`city` room: online count, Market Mood, active events, user-targeted messages (contract section 5).</summary>
    public sealed class CityChannel : RoomChannel<CityState>
    {
        public CityChannel() : base("city") { }

        public event Action<EventMessage> EventReceived;
        public event Action<LiquidatedMessage> Liquidated;
        public event Action<PaymentConfirmedMessage> PaymentConfirmed;
        public event Action<SpaceUpdateMessage> SpaceUpdated;
        public event Action<DmMessage> DmReceived;
        public event Action<CallInviteMessage> CallInvite;
        public event Action<KickedMessage> Kicked;

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
            Track(cb.Listen(s => s.online, (cur, prev) => Bus.Publish(new OnlineCountSignal { online = cur })));
            Track(cb.Listen(s => s.mood, (cur, prev) => Bus.Publish(new MoodSignal { bps = cur })));
        }
    }

    /// <summary>`market` room: live fictional coins (0.5 Hz) and the big-trade tape (Phase 2).</summary>
    public sealed class MarketChannel : RoomChannel<MarketState>
    {
        public MarketChannel() : base("market") { }

        public event Action<TapeMessage> Tape;
        public event Action<string, CoinState> CoinAdded;
        public event Action<string, CoinState> CoinChanged;
        public event Action<string> CoinRemoved;

        protected override void OnJoined()
        {
            On<TapeMessage>("tape", m => { Tape?.Invoke(m); Bus.Publish(m); });
            var cb = Callbacks.Get(Room);
            Track(cb.OnAdd(s => s.coins, (key, coin) =>
            {
                CoinAdded?.Invoke(key, coin);
                Track(cb.OnChange(coin, () => CoinChanged?.Invoke(key, coin)));
            }));
            Track(cb.OnRemove(s => s.coins, (key, coin) => CoinRemoved?.Invoke(key)));
        }
    }

    /// <summary>
    /// `venue` room: players and NPCs of one venue instance at 10 Hz (contract section 5).
    /// Join with `{token, venueId}`; a full instance (50) makes the server open a new one.
    /// </summary>
    public sealed class VenueChannel : RoomChannel<VenueState>
    {
        public VenueChannel() : base("venue") { }

        public string VenueId => State?.venueId;

        public event Action<string, PlayerState> PlayerAdded;
        public event Action<string, PlayerState> PlayerChanged;
        public event Action<string, PlayerState> PlayerRemoved;
        public event Action<string, NpcState> NpcAdded;
        public event Action<string, NpcState> NpcChanged;
        public event Action<string, NpcState> NpcRemoved;
        public event Action<SaidMessage> Said;

        protected override void OnJoined()
        {
            On<SaidMessage>("said", m => { Said?.Invoke(m); Bus.Publish(m); });
            On<ToastMessage>("toast", m => Bus.Publish(ToastSignal.Id(m.textId, m.@params)));

            var cb = Callbacks.Get(Room);
            Track(cb.OnAdd(s => s.players, (key, p) =>
            {
                PlayerAdded?.Invoke(key, p);
                Track(cb.OnChange(p, () => PlayerChanged?.Invoke(key, p)));
            }));
            Track(cb.OnRemove(s => s.players, (key, p) => PlayerRemoved?.Invoke(key, p)));
            Track(cb.OnAdd(s => s.npcs, (key, n) =>
            {
                NpcAdded?.Invoke(key, n);
                Track(cb.OnChange(n, () => NpcChanged?.Invoke(key, n)));
            }));
            Track(cb.OnRemove(s => s.npcs, (key, n) => NpcRemoved?.Invoke(key, n)));
        }

        // ---- client → server (contract section 5). The server enforces the rate limits; we also throttle. ----

        private float _lastMoveAt = -1f;
        private float _lastEmoteAt = -10f;
        private float _lastSayAt = -10f;

        /// <summary>At most 10 per second. Returns false when throttled.</summary>
        public bool Move(float x, float z, float rotY)
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastMoveAt < 0.1f) return false;
            _lastMoveAt = now;
            _ = Send("move", new MoveMessage { x = x, z = z, rotY = rotY });
            return true;
        }

        public void Sit(string spotId) => _ = Send("sit", new SitMessage { spotId = spotId });

        public void Stand() => _ = Send("stand", new StandMessage());

        public bool Emote(string emoteId)
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastEmoteAt < 1f) return false;
            _lastEmoteAt = now;
            _ = Send("emote", new EmoteMessage { emoteId = emoteId });
            return true;
        }

        /// <summary>140 chars, once per 2 s. Returns the seconds to wait if throttled, else 0.</summary>
        public float Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var now = UnityEngine.Time.realtimeSinceStartup;
            var wait = 2f - (now - _lastSayAt);
            if (wait > 0) return wait;
            _lastSayAt = now;
            if (text.Length > 140) text = text.Substring(0, 140);
            _ = Send("say", new SayMessage { text = text });
            return 0;
        }

        public void Talking(bool on) => _ = Send("talking", new TalkingMessage { on = on });

        public void NpcTap(string npcId) => _ = Send("npc_tap", new NpcTapMessage { npcId = npcId });
    }

}
