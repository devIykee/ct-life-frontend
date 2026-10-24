using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Colyseus;
using Colyseus.Schema;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;

namespace CTLife.CityLife
{
    /// <summary>
    /// Presents a granted home visit. The default implementation joins the `home` room for presence only (who is inside,
    /// shown in the phone). The World presenter that also loads the home interior and spawns avatars needs core hooks
    /// (requested in docs/progress/requests.md, "2026-10-08 city-life -> integration lead", item 6); when the lead
    /// registers one with <c>Services.Register&lt;IHomeVisitPresenter&gt;(...)</c> it is used instead.
    /// </summary>
    public interface IHomeVisitPresenter
    {
        /// <summary>Joins and presents the visit. Returns the number of people inside after joining.</summary>
        Task<int> Enter(VisitTicket ticket, CancellationToken ct);
        Task Leave();
        /// <summary>Raised with the current head count.</summary>
        event Action<int> PresenceChanged;
        /// <summary>The visit ended without our own Leave (owner closed the door, kick, drop).</summary>
        event Action Closed;
        IReadOnlyList<string> Names { get; }
        bool ShowsInterior { get; }
    }

    /// <summary>
    /// One `home` room (contract section 5: "Player home, created on visit · 12 clients · 10 Hz · same schema as venue";
    /// state `HomeState {ownerId, homeId, players}`), joined with `client.joinById(roomId)` as the contract's visit reply
    /// says. Decoded with DynamicSchema and mapped by name (same reason as CTLife.Net.StateMapper: a typed schema stops
    /// decoding on any server/contract mismatch).
    /// </summary>
    public sealed class HomeRoomClient
    {
        private Room<DynamicSchema> _room;
        private readonly List<Action> _unsubscribe = new List<Action>();
        private readonly Dictionary<string, PlayerState> _players = new Dictionary<string, PlayerState>();
        private readonly Dictionary<string, Action> _entryUnsub = new Dictionary<string, Action>();
        private bool _leaving;

        public event Action<int> PresenceChanged;
        public event Action<bool> Closed;
        /// <summary>The server's close code when the room ended without our Leave (4403 = access revoked: guest removed,
        /// move-out, eviction; docs/REALTIME.md).</summary>
        public event Action<int> LeftWithCode;
        public const int CodeAccessRevoked = 4403;

        public bool IsJoined => _room != null;
        public string SessionId => _room?.SessionId;
        public string OwnerId => _room?.State != null ? StateMapper.Str(_room.State["ownerId"], null) : null;
        public string HomeId => _room?.State != null ? StateMapper.Str(_room.State["homeId"], null) : null;
        public IReadOnlyDictionary<string, PlayerState> Players => _players;

        public async Task Join(Client client, string roomId, Dictionary<string, object> options, CancellationToken ct)
        {
            if (_room != null) throw new InvalidOperationException("Already joined");
            if (!CityWire.IsSafeRoomId(roomId)) throw new ArgumentException("Invalid roomId");
            ct.ThrowIfCancellationRequested();
            await Attach(await client.JoinById<DynamicSchema>(roomId, options), ct);
        }

        /// <summary>Joins the `home` room for a home instance (`joinOrCreate("home", {token, homeId})`, filtered by homeId):
        /// how an owner or guest who walks in through the flat's door is present there (backend PR #7).</summary>
        public async Task JoinHome(Client client, string homeId, Dictionary<string, object> options, CancellationToken ct)
        {
            if (_room != null) throw new InvalidOperationException("Already joined");
            if (!CityWire.IsSafeRoomId(homeId)) throw new ArgumentException("Invalid homeId");
            ct.ThrowIfCancellationRequested();
            options["homeId"] = homeId;
            await Attach(await client.JoinOrCreate<DynamicSchema>("home", options), ct);
        }

        private async Task Attach(Room<DynamicSchema> room, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                try { await room.Leave(); } catch (Exception e) { Log.Warn("home-room", "Discarded join leave: " + e.Message); }
                ct.ThrowIfCancellationRequested();
            }
            _room = room;
            _leaving = false;
            _unsubscribe.Add(room.OnMessage<WelcomeMessage>("welcome", w => { try { ServerClock.OnWelcome(w.serverNow); } catch (Exception e) { Log.Exception("home-room", e); } }));
            _unsubscribe.Add(room.OnMessage<ToastMessage>("toast", m => Bus.Publish(ToastSignal.Id(m.textId, m.@params))));
            _unsubscribe.Add(room.OnMessage<RejectedMessage>("rejected", r => Log.Warn("home-room", "rejected " + r.type + ": " + r.code)));
            _unsubscribe.Add(room.OnMessage<SaidMessage>("said", m => Bus.Publish(m)));
            var cb = Callbacks.Get(room);
            _unsubscribe.Add(cb.OnAdd<DynamicSchema>("players", (key, entry) =>
            {
                if (entry == null) return;
                if (!_players.TryGetValue(key, out var p)) _players[key] = p = new PlayerState();
                StateMapper.ApplyPlayer(name => entry[name], p);
                if (_entryUnsub.TryGetValue(key, out var old)) old();
                _entryUnsub[key] = cb.OnChange(entry, () =>
                {
                    if (_players.TryGetValue(key, out var current)) StateMapper.ApplyPlayer(name => entry[name], current);
                });
                PresenceChanged?.Invoke(_players.Count);
            }));
            _unsubscribe.Add(cb.OnRemove<DynamicSchema>("players", (key, entry) =>
            {
                if (_entryUnsub.TryGetValue(key, out var off)) { off(); _entryUnsub.Remove(key); }
                if (_players.Remove(key)) PresenceChanged?.Invoke(_players.Count);
            }));
            room.OnLeave += code =>
            {
                bool unexpected = !_leaving;
                Release();
                _room = null;
                if (unexpected) { try { LeftWithCode?.Invoke(code); } catch (Exception e) { Log.Exception("home-room", e); } }
                Closed?.Invoke(unexpected);
            };
            room.OnError += (code, message) => Log.Warn("home-room", $"error {code}: {message}");
            Log.Info("home-room", $"Joined home room {room.RoomId} as {room.SessionId}");
        }

        private void Release()
        {
            foreach (var off in _entryUnsub.Values) { try { off(); } catch { /* room torn down */ } }
            _entryUnsub.Clear();
            foreach (var off in _unsubscribe) { try { off(); } catch { /* room torn down */ } }
            _unsubscribe.Clear();
            _players.Clear();
        }

        public async Task Leave()
        {
            var room = _room;
            if (room == null) return;
            _leaving = true;
            try { await room.Leave(); }
            catch (Exception e) { Log.Warn("home-room", "Leave: " + e.Message); }
            finally
            {
                if (ReferenceEquals(_room, room)) { Release(); _room = null; }
            }
        }

        public List<string> Names()
        {
            var list = new List<string>();
            foreach (var p in _players.Values) list.Add(string.IsNullOrEmpty(p.name) ? "player" : CityWire.Plain(p.name, 24));
            return list;
        }
    }

    /// <summary>Presence-only visit presenter (no interior): the default until the World hook lands.</summary>
    public sealed class RoomOnlyHomeVisitPresenter : IHomeVisitPresenter
    {
        private HomeRoomClient _room;
        private Client _client;

        public event Action<int> PresenceChanged;
        public event Action Closed;
        public bool ShowsInterior => false;
        public IReadOnlyList<string> Names => _room != null ? _room.Names() : new List<string>();

        public async Task<int> Enter(VisitTicket ticket, CancellationToken ct)
        {
            await Leave();
            if (Services.TryGet<IAuthService>(out var auth)) await auth.EnsureFresh(ct);
            ct.ThrowIfCancellationRequested();
            var options = new Dictionary<string, object>();
            if (Services.TryGet<PlayerProfile>(out var profile))
            {
                var extra = RealtimeJoinOptions.FromProfile(profile);
                if (extra != null) foreach (var kv in extra) if (kv.Value != null) options[kv.Key] = kv.Value;
            }
            options["token"] = Services.Get<Session>().AccessToken ?? "";
            if (!string.IsNullOrEmpty(ticket.HomeId)) options["homeId"] = ticket.HomeId;
            options["ownerId"] = ticket.OwnerId;
            _client = _client ?? new Client(Env.Current.realtimeUrl);
            var room = new HomeRoomClient();
            room.PresenceChanged += n => PresenceChanged?.Invoke(n);
            room.Closed += unexpected => { if (ReferenceEquals(_room, room)) _room = null; if (unexpected) Closed?.Invoke(); };
            await room.Join(_client, ticket.RoomId, options, ct);
            _room = room;
            return room.Players.Count;
        }

        public async Task Leave()
        {
            var room = _room;
            _room = null;
            if (room != null) await room.Leave();
        }
    }
}
