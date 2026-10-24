using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Colyseus;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>A delivery event from the `city` room (docs/REALTIME.md, backend PR #8): `delivery_out` (the courier leaves,
    /// about 60 s before <see cref="EtaAtMs"/>) or `delivery_arrived`. <see cref="UnitId"/> is null for the parcel locker.</summary>
    public sealed class DeliveryEvent
    {
        public string OrderId, Destination, UnitId;
        public long EtaAtMs;
        public bool Arrived;
        public bool ToLocker => Destination == "locker" || string.IsNullOrEmpty(UnitId);

        public static DeliveryEvent From(string type, object payload)
        {
            if (!(payload is IDictionary<string, object> d)) return null;
            string S(string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;
            var id = S("orderId");
            if (string.IsNullOrEmpty(id) || id.Length > 64) return null;
            long eta = 0;
            if (d.TryGetValue("etaAt", out var e) && e != null)
                try { eta = Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture); } catch (Exception) { eta = 0; }
            return new DeliveryEvent { OrderId = id, Destination = S("destination") ?? "door", UnitId = S("unitId"), EtaAtMs = eta, Arrived = type == "delivery_arrived" };
        }
    }

    /// <summary>
    /// Homes behind flat doors and the Duo Shop for the signed-in player (2026-10-23, backend PRs #7 and #8):
    /// - <see cref="MyHome"/> from GET /me `home` (rent status, the flat's door), refreshed on profile changes and every minute;
    /// - the door gate for <see cref="IslandInteriors"/>: GET /units/{unitId}/access per door the player walks up to (cached);
    /// - presence in the `home` room while the player is inside a flat they may enter; a 4403 close (access revoked) shows
    ///   "You were shown out" and walks them out through the door;
    /// - the catalogue, the bag, orders and owned items for the Duo Shop app, and `delivery_out` / `delivery_arrived` from the
    ///   city room (Duo notifications; <see cref="DeliveryDirector"/> stages the courier in the world).
    /// One per signed-in user, owned by <see cref="HomeShopOverlays"/>.
    /// </summary>
    public sealed class HomeShopService : IDoorGate, IDisposable
    {
        public const string AppId = "duoshop";
        public const double AccessTtlSeconds = 20, HomeRefreshSeconds = 60;

        public static HomeShopService Current { get; private set; }
        /// <summary>Opens a Duo app (FlowController sets it to the phone).</summary>
        public static Action<string> Opener;

        public readonly DuoShopApi Api;
        public readonly ShopCart Cart = new ShopCart();
        public string UserId { get; }
        public MyHome MyHome { get; private set; }
        public bool HomeKnown { get; private set; }
        public long CashCents { get; private set; } = -1;
        public ShopCatalog Catalog { get; private set; }
        public List<ShopOrder> Orders { get; private set; } = new List<ShopOrder>();
        public List<OwnedItem> Items { get; private set; } = new List<OwnedItem>();
        /// <summary>The home whose room the player is in (owner or guest), null outside.</summary>
        public string InsideHomeId { get; private set; }
        public string InsideUnitId { get; private set; }
        public bool InsideOwnFlat => InsideUnitId != null && MyHome != null && InsideUnitId == MyHome.UnitId;
        public bool Disposed { get; private set; }

        public event Action HomeChanged, OrdersChanged, ItemsChanged, InsideChanged;
        public event Action<DeliveryEvent> Delivery;

        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly Dictionary<string, (DoorVerdict verdict, float at, string homeId)> _access = new Dictionary<string, (DoorVerdict, float, string)>();
        private readonly HashSet<string> _asking = new HashSet<string>();
        private HomeRoomClient _room;
        private Client _client;
        private float _homeAt = -1e9f;
        private bool _homeLoading;
        private Task<ShopCatalog> _catalogLoad;

        public HomeShopService(string userId)
        {
            UserId = userId;
            Api = new DuoShopApi(Services.Get<IRestClient>());
            Current = this;
            IslandInteriors.Gate = this;
            IslandInteriors.Entered += OnEntered;
            IslandInteriors.Exited += OnExited;
            RawRoomMessages.Received += OnRaw;
            _subs.On<ProfileChangedSignal>(_ => RefreshHome());
            RefreshHome(force: true);
            _ = Loop();
        }

        public CancellationToken Life => _life.Token;

        // ------------------------------------------------------------------ home (GET /me)

        public async void RefreshHome(bool force = false)
        {
            if (Disposed || RuntimeMode.IsOfflineDemo || _homeLoading) return;
            if (!force && Time.realtimeSinceStartup - _homeAt < 2f) return;
            _homeLoading = true;
            try
            {
                var (home, cash) = await Api.Me(_life.Token);
                if (Disposed) return;
                _homeAt = Time.realtimeSinceStartup;
                var before = MyHome;
                MyHome = home; HomeKnown = true; CashCents = cash;
                // my own door opens at once; a lost lease drops the cached answer for the old door
                if (home?.HasDoor == true) _access[home.UnitId] = (DoorVerdict.Open, Time.realtimeSinceStartup, home.HomeId);
                if (before?.UnitId != null && before.UnitId != home?.UnitId) _access.Remove(before.UnitId);
                HomeChanged?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("homeshop", "GET /me failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
            finally { _homeLoading = false; }
        }

        private async Task Loop()
        {
            try
            {
                while (!Disposed)
                {
                    await Awaitable.WaitForSecondsAsync(5f, _life.Token);
                    if (Time.realtimeSinceStartup - _homeAt > HomeRefreshSeconds) RefreshHome(force: true);
                    // tracking: refresh orders in flight (the city room pushes the changes; this is the safety net)
                    if (Orders.Any(o => o.Active) && Time.frameCount % 2 == 0) _ = RefreshOrders();
                }
            }
            catch (OperationCanceledException) { }
        }

        // ------------------------------------------------------------------ the door gate

        public DoorVerdict Verdict(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return DoorVerdict.Open;
            if (MyHome?.HasDoor == true && MyHome.UnitId == unitId) return DoorVerdict.Open;
            return _access.TryGetValue(unitId, out var a) ? a.verdict : DoorVerdict.Unknown;
        }

        public void Want(string unitId)
        {
            if (Disposed || string.IsNullOrEmpty(unitId) || _asking.Contains(unitId)) return;
            if (MyHome?.HasDoor == true && MyHome.UnitId == unitId) return;
            if (_access.TryGetValue(unitId, out var a) && Time.realtimeSinceStartup - a.at < AccessTtlSeconds) return;
            _ = Ask(unitId);
        }

        private async Task Ask(string unitId)
        {
            _asking.Add(unitId);
            try
            {
                var r = await Api.Access(unitId, _life.Token);
                if (Disposed) return;
                var known = _access.TryGetValue(unitId, out var prev) ? prev.homeId : null;
                _access[unitId] = (r.CanEnter ? DoorVerdict.Open : DoorVerdict.Locked, Time.realtimeSinceStartup, r.HomeId ?? (r.CanEnter ? known : null));
                Log.Info("homeshop", $"door {unitId}: {(r.CanEnter ? "open" : "locked")} ({r.Reason})");
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                // the route is not deployed (404) or the call failed: fail closed for a while, then ask again
                Log.Warn("homeshop", $"GET /units/{unitId}/access failed: " + (e is ApiException a ? a.Code : e.GetType().Name));
                if (!Disposed) _access[unitId] = (DoorVerdict.Locked, Time.realtimeSinceStartup - (float)AccessTtlSeconds + 5f, null);
            }
            finally { _asking.Remove(unitId); }
        }

        /// <summary>The home behind a door when we know it: ours, or the server named it in the access answer.</summary>
        public string HomeIdFor(string unitId)
        {
            if (MyHome?.HasDoor == true && MyHome.UnitId == unitId) return MyHome.HomeId;
            return _access.TryGetValue(unitId, out var a) ? a.homeId : null;
        }

        /// <summary>
        /// Visit another player's flat: POST /homes/{ownerId}/visit (PR #7 answers `homeId, unitId, roomType`, or 403 when
        /// the visitor is neither the owner nor a guest). The door then opens for us and the home room is joined on the way in.
        /// </summary>
        public async Task<(string homeId, string unitId)> Visit(string ownerId)
        {
            var r = await Services.Get<IRestClient>().Send<Newtonsoft.Json.Linq.JToken>("POST", "/homes/" + CityWire.Seg(ownerId) + "/visit", "{}", true,
                Guid.NewGuid().ToString("N"), _life.Token);
            var homeId = CityWire.Str(r?["homeId"]); var unitId = CityWire.Str(r?["unitId"]);
            if (!string.IsNullOrEmpty(unitId) && !string.IsNullOrEmpty(homeId)) _access[unitId] = (DoorVerdict.Open, Time.realtimeSinceStartup, homeId);
            Log.Info("homeshop", $"visit {ownerId}: home {homeId} door {unitId}");
            return (homeId, unitId);
        }

        // ------------------------------------------------------------------ inside a flat: the `home` room

        private void OnEntered(int door, string unitId, InteriorRoom room)
        {
            if (Disposed || string.IsNullOrEmpty(unitId)) return;
            InsideUnitId = unitId;
            InsideHomeId = HomeIdFor(unitId);
            InsideChanged?.Invoke();
            if (!string.IsNullOrEmpty(InsideHomeId)) _ = JoinHomeRoom(InsideHomeId);
        }

        private void OnExited(int door, string unitId, InteriorRoom room)
        {
            if (Disposed) return;
            InsideUnitId = null; InsideHomeId = null;
            InsideChanged?.Invoke();
            _ = LeaveHomeRoom();
        }

        private async Task JoinHomeRoom(string homeId)
        {
            if (RuntimeMode.IsOfflineDemo || (_room != null && _room.IsJoined && _room.HomeId == homeId)) return;
            await LeaveHomeRoom();
            try
            {
                if (Services.TryGet<IAuthService>(out var auth)) await auth.EnsureFresh(_life.Token);
                var options = new Dictionary<string, object>();
                if (Services.TryGet<PlayerProfile>(out var profile))
                {
                    var extra = RealtimeJoinOptions.FromProfile(profile);
                    if (extra != null) foreach (var kv in extra) if (kv.Value != null) options[kv.Key] = kv.Value;
                }
                options["token"] = Services.Get<Session>().AccessToken ?? "";
                _client = _client ?? new Client(Env.Current.realtimeUrl);
                var room = new HomeRoomClient();
                room.LeftWithCode += code => OnHomeRoomClosed(room, code);
                await room.JoinHome(_client, homeId, options, _life.Token);
                if (Disposed || InsideHomeId != homeId) { await room.Leave(); return; }
                _room = room;
                Log.Info("homeshop", "in the home room of " + homeId);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Log.Warn("homeshop", "home room join failed: " + e.Message);
                // refused on join (FORBIDDEN): access was revoked between the door check and the join
                if (e.Message != null && e.Message.Contains("FORBIDDEN")) ShowOut(InsideUnitId);
            }
        }

        private async Task LeaveHomeRoom()
        {
            var r = _room; _room = null;
            if (r != null) await r.Leave();
        }

        private void OnHomeRoomClosed(HomeRoomClient room, int code)
        {
            if (Disposed || !ReferenceEquals(room, _room)) return;
            _room = null;
            Log.Info("homeshop", "home room closed with " + code);
            if (code == HomeRoomClient.CodeAccessRevoked) ShowOut(InsideUnitId);
        }

        /// <summary>Access revoked while inside: the toast, the door locks for us, and the player walks out through it.</summary>
        public void ShowOut(string unitId)
        {
            if (Disposed) return;
            Bus.Publish(ToastSignal.Text(ShopText.T("shownOut"), ToastKind.Warning));
            if (!string.IsNullOrEmpty(unitId)) _access[unitId] = (DoorVerdict.Locked, Time.realtimeSinceStartup, null);
            if (MyHome?.UnitId == unitId) RefreshHome(force: true);
            var rooms = IslandInteriors.Current;
            var me = VenueSession.Current != null ? VenueSession.Current.LocalPlayer : null;
            if (rooms != null && me != null && rooms.TryGetUnitDoor(unitId, out _, out var door, out var rot)) _ = WalkOut(me, rooms, door, rot);
        }

        private async Task WalkOut(LocalPlayerController me, IslandInteriors rooms, Vector3 door, Quaternion rot)
        {
            var fwd = rot * Vector3.forward;
            var room = rooms.ActiveRoom;
            var path = new List<Vector3>();
            if (room != null && rooms.PlayerInside) path.Add(room.transform.TransformPoint(new Vector3(0f, 0f, -1.4f)));
            path.Add(door + fwd * 0.6f);
            path.Add(door + fwd * 2.6f);
            try
            {
                me.StandIfSeated();
                foreach (var target in path)
                {
                    float t0 = Time.realtimeSinceStartup;
                    while (!Disposed && me != null && Time.realtimeSinceStartup - t0 < 6f)
                    {
                        var d = target - me.transform.position; d.y = 0f;
                        if (d.magnitude < 0.35f) break;
                        var cam = Camera.main;
                        float cy = cam != null ? cam.transform.eulerAngles.y : 0f;
                        var local = Quaternion.Euler(0f, -cy, 0f) * d.normalized;
                        me.SetMoveInput(new Vector2(local.x, local.z) * 0.6f);
                        await Awaitable.NextFrameAsync(_life.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally { if (me != null) me.SetMoveInput(Vector2.zero); }
        }

        // ------------------------------------------------------------------ shop data

        public Task<ShopCatalog> LoadCatalog(bool force = false)
        {
            if (!force && Catalog != null) return Task.FromResult(Catalog);
            if (_catalogLoad != null && !_catalogLoad.IsCompleted) return _catalogLoad;
            return _catalogLoad = LoadCatalogNow();
        }

        private async Task<ShopCatalog> LoadCatalogNow()
        {
            var c = await Api.Catalog(_life.Token);
            Catalog = c;
            return c;
        }

        public async Task RefreshOrders()
        {
            try
            {
                var list = await Api.Orders(_life.Token);
                if (Disposed) return;
                Orders = list.OrderByDescending(o => o.PlacedAtMs ?? 0).ToList();
                OrdersChanged?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("homeshop", "GET /shop/orders failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
        }

        public void Upsert(ShopOrder o)
        {
            if (o == null) return;
            int i = Orders.FindIndex(x => x.OrderId == o.OrderId);
            if (i >= 0) Orders[i] = o; else Orders.Insert(0, o);
            OrdersChanged?.Invoke();
        }

        public async Task<List<OwnedItem>> RefreshItems()
        {
            var list = await Api.MyItems(_life.Token);
            if (Disposed) return list;
            Items = list;
            ItemsChanged?.Invoke();
            return list;
        }

        // ------------------------------------------------------------------ city room: delivery_out / delivery_arrived

        private void OnRaw(string type, object payload)
        {
            if (Disposed || (type != "delivery_out" && type != "delivery_arrived")) return;
            var ev = DeliveryEvent.From(type, payload);
            if (ev == null) return;
            Log.Info("homeshop", $"{type} {ev.OrderId} {ev.Destination} {ev.UnitId} eta {ev.EtaAtMs}");
            var o = Orders.Find(x => x.OrderId == ev.OrderId);
            if (o != null)
            {
                o.Status = ev.Arrived ? OrderStatus.Delivered : OrderStatus.OutForDelivery;
                if (ev.EtaAtMs > 0) o.EtaAtMs = ev.EtaAtMs;
                if (ev.Arrived) o.DeliveredAtMs = (long)ServerClock.NowMs;
                OrdersChanged?.Invoke();
            }
            _ = RefreshOrders();
            if (ev.Arrived) _ = RefreshItemsQuiet();
            string what = o?.Summary ?? ShopText.T("app");
            DuoNotificationCenter.Post(new DuoNotification
            {
                AppId = AppId, ThreadKey = "order:" + ev.OrderId,
                Title = ev.Arrived ? ShopText.T(ev.ToLocker ? "notify.arrived.locker" : "notify.arrived.door") : ShopText.T("notify.out"),
                Body = ev.Arrived ? ShopText.F("notify.arrived.body", what) : ShopText.F("notify.out.body", what, ev.EtaAtMs > 0 ? ShopText.Clock(ev.EtaAtMs) : "soon"),
                Open = () => Opener?.Invoke(AppId),
            });
            try { Delivery?.Invoke(ev); } catch (Exception e) { Log.Exception("homeshop", e); }
        }

        private async Task RefreshItemsQuiet()
        {
            try { await RefreshItems(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("homeshop", "GET /me/items failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            _subs.Dispose();
            RawRoomMessages.Received -= OnRaw;
            IslandInteriors.Entered -= OnEntered;
            IslandInteriors.Exited -= OnExited;
            if (ReferenceEquals(IslandInteriors.Gate, this)) IslandInteriors.Gate = null;
            _ = LeaveHomeRoom();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
