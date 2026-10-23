using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.World;
using CTLife.World.Roads;
using CTLife.World.Vehicles;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// Duo Shop deliveries in the world (2026-10-23, backend PR #8). On `delivery_out` with the player near the destination
    /// (the flat's door, or the Residences parcel locker) a courier van in the CT Parcel livery comes in along the road graph
    /// from out of view and parks at the kerb in front of the door at `etaAt`; the courier gets out, carries the parcel to the
    /// doorstep (hands on the box through CharacterIK), puts it down and drives off. On `delivery_arrived` the parcel is on the
    /// doorstep (or the locker's screen lights); "Pick up" / "Collect" refetches GET /me/items. A player who is not nearby
    /// gets only the Duo notification (HomeShopService); the parcel still waits at the door.
    /// Perf lane 2 (2026-10-23): the van (with its livery), the courier and a parcel are made once, a few seconds after the
    /// island loads, drawn once by a throwaway camera (so their shader programs, meshes and textures are on the GPU) and
    /// pooled; a delivery takes them from the pool and gives them back, so nothing is loaded, instantiated or compiled
    /// while the courier is on screen (each used to cost a 7.5-15 fps frame).
    /// </summary>
    public sealed class DeliveryDirector : MonoBehaviour
    {
        public const float StageWithin = 170f, Cruise = 8.5f, Approach = 70f;
        public static DeliveryDirector Current { get; private set; }

        private sealed class Run
        {
            public DeliveryEvent ev;
            public Vector3 door; public Quaternion rot;
            public Vehicle van; public CarPhysics car;
            public AvatarView courier; public CharacterIK ik;
            public GameObject carried;
            public bool arrived, dropped, done;
            public List<Vector3> path; public float[] cum; public float s, length;
            public long etaMs;
            public Vector3 stop, endDir, kerbOffset;   // the parked point, the lane's direction there, lane -> kerb
            public float leftFor;
        }

        private sealed class Doorstep { public string orderId; public GameObject parcel; public Vector3 at; public bool ready; }
        private readonly HashSet<string> _arrived = new HashSet<string>();
        public bool HasReadyParcel => _parcels.Exists(p => p.ready && p.parcel != null) || _lockerOrders.Count > 0;

        private HomeShopService _svc;
        private readonly List<Run> _runs = new List<Run>();
        private readonly List<Doorstep> _parcels = new List<Doorstep>();
        private GameObject _locker; private Renderer _lockerScreen;
        private readonly HashSet<string> _lockerOrders = new HashSet<string>();
        private Func<Vector3, WorldOffer> _offers;
        private bool _picking;

        public int Active => _runs.Count;
        public bool VanParked => _runs.Exists(r => r.van != null && r.arrived);

        public static DeliveryDirector Create(HomeShopService svc)
        {
            var go = new GameObject("Duo Shop deliveries");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<DeliveryDirector>();
            d._svc = svc;
            svc.Delivery += d.OnDelivery;
            return d;
        }

        private void Awake()
        {
            Current = this;
            _offers = Offer;
            WorldOffers.Add(_offers);
            Prewarm();
        }

        // ------------------------------------------------------------------ the pool

        private GameObject _vanPrefab;
        private Vehicle _vanPool;
        private AvatarView _courierPool;
        private readonly Stack<GameObject> _parcelPool = new Stack<GameObject>();
        private static readonly Vector3 Backstage = new Vector3(-2000f, -400f, 0f);

        /// <summary>Once on the island: the van, the courier and a parcel, made over a few frames, drawn once off-screen,
        /// then parked inactive.</summary>
        private async void Prewarm()
        {
            try
            {
                var ct = destroyCancellationToken;
                while (VenueSession.Current == null || !VenueSession.Current.IsActive || !VenueSession.Current.IsIsland || Player() == null)
                    await Awaitable.WaitForSecondsAsync(1f, ct);
                await Awaitable.WaitForSecondsAsync(4f, ct);
                var req = Resources.LoadAsync<GameObject>("Vehicles/van");
                while (!req.isDone) await Awaitable.NextFrameAsync(ct);
                _vanPrefab = req.asset as GameObject;
                if (_vanPrefab == null) { Log.Warn("delivery", "missing prefab Vehicles/van"); return; }
                await Awaitable.NextFrameAsync(ct);
                var van = NewVan(Backstage, Quaternion.identity);
                await Awaitable.NextFrameAsync(ct);
                var courier = NewCourier();
                await Awaitable.NextFrameAsync(ct);
                var parcel = ShopProps.Parcel(transform);
                await Awaitable.NextFrameAsync(ct);
                // one off-screen render of the three: the first sight of them then costs nothing
                // (AvatarLod hides avatars 25 m from the player: this one stays drawn for the warm-up frame)
                if (courier != null) { courier.cullDistance = 1e6f; courier.transform.SetPositionAndRotation(Backstage + new Vector3(3f, 0f, 0f), Quaternion.identity); }
                parcel.transform.position = Backstage + new Vector3(-2.5f, 0f, 1f);
                await WarmRender(Backstage + new Vector3(0f, 1.2f, 0f), ct);
                if (van != null) { van.gameObject.SetActive(false); if (_vanPool == null) _vanPool = van; else Destroy(van.gameObject); }
                if (courier != null) { courier.cullDistance = 0f; courier.gameObject.SetActive(false); if (_courierPool == null) _courierPool = courier; else Destroy(courier.gameObject); }
                ReturnParcel(parcel);
                // the furniture studio too: its first thumbnail compiled the furniture shader for a shadowless camera (a
                // ~40 ms frame when Decorate first opened); one cached thumbnail now, on arrival
                FurniturePreview.Instance.Thumbnail("fu_sofa_linen");
                Log.Info("delivery", "van, courier, parcel and the furniture studio prewarmed");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>A throwaway camera looks at <paramref name="at"/> for one frame (the city's own settings and shadows), into
        /// a tiny texture: shader programs, meshes and textures of what stands there reach the GPU now.</summary>
        private static async Task WarmRender(Vector3 at, System.Threading.CancellationToken ct)
        {
            var main = Camera.main;
            var rt = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGB32) { name = "delivery_prewarm" };
            var go = new GameObject("delivery_prewarm_cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                if (main != null) cam.CopyFrom(main);
                cam.targetTexture = rt;
                cam.transform.SetPositionAndRotation(at + new Vector3(0f, 2f, -9f), Quaternion.LookRotation(new Vector3(0f, -2f, 9f)));
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 40f;
                await Awaitable.NextFrameAsync(ct);
                await Awaitable.NextFrameAsync(ct);
            }
            finally { Destroy(go); rt.Release(); Destroy(rt); }
        }

        private Vehicle NewVan(Vector3 p, Quaternion q)
        {
            var go = Instantiate(_vanPrefab, p, q, transform);
            go.name = "courier-van";
            var van = go.GetComponent<Vehicle>();
            var body = go.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.interpolation = RigidbodyInterpolation.None; }
            var car = go.GetComponent<CarPhysics>();
            if (car != null) car.SetRails(true);
            ShopProps.CourierLivery(van);
            return van;
        }

        private AvatarView NewCourier()
        {
            string look = null;
            var reg = CharacterRegistry.Instance;
            if (reg != null) foreach (var e in reg.archetypes) if (e != null && e.prefab != null && !string.IsNullOrEmpty(e.key)) { look = e.key; if (e.key.Contains("worker") || e.key.Contains("dev")) break; }
            var view = AvatarFactory.Create("courier", null, true, look, transform);
            if (view != null) view.ambient = true;
            return view;
        }

        private AvatarView TakeCourier()
        {
            var view = _courierPool;
            _courierPool = null;
            if (view == null) return NewCourier();
            view.gameObject.SetActive(true);
            return view;
        }

        private void ReturnCourier(AvatarView view)
        {
            if (view == null) return;
            if (_courierPool != null) { Destroy(view.gameObject); return; }
            view.SetSpeed(0f);
            view.gameObject.SetActive(false);
            _courierPool = view;
        }

        private void ReturnVan(Vehicle van)
        {
            if (van == null) return;
            if (_vanPool != null) { Destroy(van.gameObject); return; }
            van.gameObject.SetActive(false);
            _vanPool = van;
        }

        private GameObject TakeParcel(Transform parent)
        {
            while (_parcelPool.Count > 0)
            {
                var p = _parcelPool.Pop();
                if (p == null) continue;
                p.transform.SetParent(parent, false);
                p.transform.localPosition = Vector3.zero; p.transform.localRotation = Quaternion.identity;
                p.SetActive(true);
                return p;
            }
            return ShopProps.Parcel(parent);
        }

        private void ReturnParcel(GameObject p)
        {
            if (p == null) return;
            if (_parcelPool.Count >= 2) { Destroy(p); return; }
            p.SetActive(false);
            p.transform.SetParent(transform, false);
            _parcelPool.Push(p);
        }

        private void OnDestroy()
        {
            WorldOffers.Remove(_offers);
            if (_svc != null) _svc.Delivery -= OnDelivery;
            foreach (var r in _runs) Clear(r);
            foreach (var p in _parcels) if (p.parcel != null) Destroy(p.parcel);
            if (ReferenceEquals(Current, this)) Current = null;
        }

        private static Transform Player() => VenueSession.Current != null && VenueSession.Current.LocalPlayer != null ? VenueSession.Current.LocalPlayer.transform : null;

        // ------------------------------------------------------------------ where

        /// <summary>The destination's door (the flat's, or for the locker the first Residences walk-in door's building).</summary>
        private static bool Destination(DeliveryEvent ev, out Vector3 door, out Quaternion rot)
        {
            door = default; rot = Quaternion.identity;
            var rooms = IslandInteriors.Current;
            if (rooms == null) return false;
            if (!ev.ToLocker) return rooms.TryGetUnitDoor(ev.UnitId, out _, out door, out rot);
            return rooms.TryGetDoor(0, out door, out rot);
        }

        /// <summary>The locker stands on the facade 2.6 m to the right of door 0 (seen from the street).</summary>
        private bool EnsureLocker()
        {
            if (_locker != null) return true;
            var rooms = IslandInteriors.Current;
            if (rooms == null || !rooms.TryGetDoor(0, out var door, out var rot)) return false;
            _locker = ShopProps.Locker(transform, out _lockerScreen);
            var right = rot * Vector3.left;      // the street's view: +Z out of the building, so the viewer's right is -X
            _locker.transform.SetPositionAndRotation(door + right * 2.6f + rot * Vector3.forward * 0.22f, rot);
            return true;
        }

        private Vector3 DropPoint(DeliveryEvent ev, Vector3 door, Quaternion rot)
        {
            var fwd = rot * Vector3.forward;
            if (ev.ToLocker && EnsureLocker()) return _locker.transform.position + fwd * 0.75f;
            return door + fwd * 0.62f + rot * Vector3.left * 0.38f;
        }

        // ------------------------------------------------------------------ events

        private void OnDelivery(DeliveryEvent ev)
        {
            if (!Destination(ev, out var door, out var rot)) { Log.Info("delivery", "destination not on this island: " + ev.UnitId); return; }
            if (ev.ToLocker) EnsureLocker();
            var run = _runs.Find(r => r.ev.OrderId == ev.OrderId);
            if (ev.Arrived)
            {
                _arrived.Add(ev.OrderId);
                if (run != null) { run.ev = ev; if (run.dropped) Settle(ev, door, rot); return; }   // the courier is still walking
                Settle(ev, door, rot);
                return;
            }
            if (run != null) return;
            var me = Player();
            if (me == null || InteractionFinder.Flat(me.position, door) > StageWithin)
            {
                Log.Info("delivery", "player not nearby: notification only");
                return;
            }
            run = new Run { ev = ev, door = door, rot = rot, etaMs = ev.EtaAtMs > 0 ? ev.EtaAtMs : (long)ServerClock.NowMs + 25000 };
            if (!PlanRoute(run)) { Log.Warn("delivery", "no road near the door; parcel only"); return; }
            _runs.Add(run);
            Log.Info("delivery", $"courier staged for {ev.OrderId}: {run.length:0} m route, eta in {(run.etaMs - ServerClock.NowMs) / 1000:0} s");
        }

        /// <summary>The parcel is there: on the doorstep, or the locker's screen lights.</summary>
        private void Settle(DeliveryEvent ev, Vector3 door, Quaternion rot)
        {
            if (ev.Arrived) { _arrived.Add(ev.OrderId); foreach (var p in _parcels) if (p.orderId == ev.OrderId) p.ready = true; }
            if (ev.ToLocker)
            {
                _lockerOrders.Add(ev.OrderId);
                ShopProps.SetLockerLit(_lockerScreen, true);
                return;
            }
            if (_parcels.Exists(p => p.orderId == ev.OrderId)) return;
            var at = DropPoint(ev, door, rot);
            var parcel = TakeParcel(transform);
            parcel.transform.SetPositionAndRotation(at, rot * Quaternion.Euler(0f, 12f, 0f));
            _parcels.Add(new Doorstep { orderId = ev.OrderId, parcel = parcel, at = at, ready = ev.Arrived });
        }

        // ------------------------------------------------------------------ the van's route

        private static bool StreetLike(RoadSegment s) => s.kind == RoadKind.Street || s.kind == RoadKind.Avenue || s.kind == RoadKind.Service;

        private bool PlanRoute(Run run)
        {
            var net = RoadNetwork.Active;
            if (net == null) return false;
            var fwd = run.rot * Vector3.forward;
            var lp = net.NearestLanePoint(run.door + fwd * 7f, 45f, StreetLike);
            if (!lp.valid) lp = net.NearestLanePoint(run.door + fwd * 7f, 60f);
            if (!lp.valid) return false;
            var pts = net.LaneCenterline(lp.segmentId, lp.lane);
            var seg = net.Segment(lp.segmentId);
            var path = new List<Vector3>();
            // upstream: this lane back to `Approach` metres before the stop, and one lane further back through the junction
            float need = Approach - lp.along;
            if (need > 0f)
                foreach (var link in net.lanes)
                {
                    if (link.next == null) continue;
                    bool feeds = false;
                    foreach (var n in link.next) if (n.segmentId == lp.segmentId && n.lane == lp.lane) { feeds = true; break; }
                    if (!feeds) continue;
                    var prev = net.LaneCenterline(link.segmentId, link.lane);
                    if (prev.Length < 2) continue;
                    float acc = 0f; int k = prev.Length - 1;
                    while (k > 0 && acc < need) { acc += Vector3.Distance(prev[k], prev[k - 1]); k--; }
                    for (int i = k; i < prev.Length; i++) path.Add(prev[i]);
                    var con = net.Connector(new LaneRef(link.segmentId, link.lane), new LaneRef(lp.segmentId, lp.lane));
                    for (int i = 1; i < con.Length - 1; i++) path.Add(con[i]);
                    break;
                }
            float along = 0f;
            float start = Mathf.Max(0f, lp.along - Approach);
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0) along += Vector3.Distance(pts[i - 1], pts[i]);
                if (along >= start && along <= lp.along) path.Add(pts[i]);
            }
            // the stop: the lane point abeam the door, pulled over to the kerb (half a lane plus a little)
            var dir = lp.direction; dir.y = 0f; dir.Normalize();
            var right = new Vector3(dir.z, 0f, -dir.x);
            float side = Mathf.Sign(Vector3.Dot(run.door - lp.position, right));
            float pull = (seg != null ? seg.laneWidth : 3.3f) * 0.5f + 0.35f;
            var stop = lp.position + right * side * pull;
            // pull in over the last 16 m (an S-curve sideways), so the van eases to the kerb instead of crabbing
            int n0 = path.Count;
            var lead = new List<Vector3>();
            for (int i = 0; i < n0; i++) lead.Add(path[i]);
            path.Clear();
            float total = 0f;
            for (int i = 1; i < lead.Count; i++) total += Vector3.Distance(lead[i - 1], lead[i]);
            float acc2 = 0f;
            for (int i = 0; i < lead.Count; i++)
            {
                if (i > 0) acc2 += Vector3.Distance(lead[i - 1], lead[i]);
                float toEnd = total - acc2;
                float k = Mathf.Clamp01(1f - toEnd / 16f);
                k = k * k * (3f - 2f * k);
                path.Add(lead[i] + right * side * pull * k);
            }
            path.Add(stop);
            path.Add(stop + dir * 0.4f);
            run.stop = stop + dir * 0.4f; run.endDir = dir; run.kerbOffset = right * side * pull;
            if (path.Count < 3) return false;
            for (int i = 0; i < path.Count; i++) { var p = path[i]; p.y = IslandStreamer.GroundHeight(p.x, p.z); path[i] = p; }
            run.path = path;
            run.cum = new float[path.Count];
            for (int i = 1; i < path.Count; i++) run.cum[i] = run.cum[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            run.length = run.cum[path.Count - 1];
            return true;
        }

        private static Vector3 Sample(Run r, float s, out Vector3 dir)
        {
            s = Mathf.Clamp(s, 0f, r.length);
            int i = 1;
            while (i < r.cum.Length - 1 && r.cum[i] < s) i++;
            float seg = Mathf.Max(1e-4f, r.cum[i] - r.cum[i - 1]);
            dir = (r.path[i] - r.path[i - 1]) / seg;
            return Vector3.Lerp(r.path[i - 1], r.path[i], (s - r.cum[i - 1]) / seg);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            for (int i = _runs.Count - 1; i >= 0; i--)
            {
                var r = _runs[i];
                try { Step(r); }
                catch (Exception e) { Debug.LogException(e); r.done = true; }
                if (r.done) { Clear(r); _runs.RemoveAt(i); }
            }
        }

        private void Step(Run r)
        {
            double now = ServerClock.NowMs;
            float driveTime = r.length / Cruise + 3.5f;   // cruise plus the stop
            double startMs = r.etaMs - driveTime * 1000.0;
            if (r.van == null)
            {
                if (now < startMs) return;
                if (!SpawnVan(r)) { r.done = true; Settle(r.ev, r.door, r.rot); return; }
                // late (a reconnect): start part-way along so it still parks on time
                r.s = Mathf.Clamp((float)((now - startMs) / 1000.0) * Cruise, 0f, r.length * 0.85f);
            }
            if (!r.arrived)
            {
                // eases to a stop over the last 18 m (v² = 2 a d), never faster than Cruise
                float remain = r.length - r.s;
                float v = Mathf.Min(Cruise, Mathf.Sqrt(2f * 2.1f * Mathf.Max(0f, remain)) + 0.25f);
                r.s = Mathf.Min(r.length, r.s + v * Time.deltaTime);
                var p = Sample(r, r.s, out var dir);
                var ahead = Sample(r, r.s + 3f, out var dir2);
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-6f) r.van.transform.SetPositionAndRotation(p, Quaternion.Slerp(r.van.transform.rotation, Quaternion.LookRotation(dir.normalized), Time.deltaTime * 6f));
                else r.van.transform.position = p;
                if (r.car != null)
                {
                    r.car.RailSpeed = v;
                    r.car.RailSteer = Mathf.Clamp(Vector3.SignedAngle(dir, new Vector3(dir2.x, 0f, dir2.z), Vector3.up) * 1.6f, -30f, 30f);
                }
                if (remain <= 0.02f)
                {
                    r.arrived = true;
                    if (r.car != null) { r.car.RailSpeed = 0f; r.car.RailSteer = 0f; }
                    StartCourier(r);
                }
                return;
            }
            if (r.courier == null && r.dropped) Leave(r);
        }

        private bool SpawnVan(Run r)
        {
            var p = Sample(r, 0f, out var dir); dir.y = 0f;
            var q = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward);
            if (_vanPool != null)
            {
                r.van = _vanPool; _vanPool = null;
                r.van.transform.SetPositionAndRotation(p, q);
                r.van.gameObject.SetActive(true);
            }
            else
            {
                if (_vanPrefab == null) _vanPrefab = Resources.Load<GameObject>("Vehicles/van");
                if (_vanPrefab == null) { Log.Warn("delivery", "missing prefab Vehicles/van"); return false; }
                r.van = NewVan(p, q);
            }
            r.car = r.van.GetComponent<CarPhysics>();
            if (r.car != null) { r.car.SetRails(true); r.car.RailSpeed = 0f; r.car.RailSteer = 0f; }
            r.van.EngineOn = true; r.van.Input = VehicleInput.None;
            return true;
        }

        // ------------------------------------------------------------------ the courier

        private async void StartCourier(Run r)
        {
            try
            {
                var fwd = r.rot * Vector3.forward;
                var vanT = r.van.transform;
                var box = r.van.GetComponent<BoxCollider>();
                float half = box != null ? box.size.x * 0.5f : 1f;
                // out of the kerb-side door behind the cab
                float kerb = Mathf.Sign(Vector3.Dot(r.door - vanT.position, vanT.right));
                var from = vanT.position + vanT.right * kerb * (half + 0.45f) - vanT.forward * 0.6f;
                from.y = IslandStreamer.GroundHeight(from.x, from.z);
                var drop = DropPoint(r.ev, r.door, r.rot);
                var stand = drop + fwd * 0.55f;
                var view = TakeCourier();
                if (view == null) { r.dropped = true; Settle(r.ev, r.door, r.rot); return; }
                r.courier = view;
                r.ik = view.GetComponent<CharacterIK>();
                view.transform.SetPositionAndRotation(from, Quaternion.LookRotation(Flat(stand - from)));
                r.carried = TakeParcel(view.transform);
                r.carried.transform.localPosition = new Vector3(0f, 0.98f, 0.33f);
                await Awaitable.WaitForSecondsAsync(0.4f, destroyCancellationToken);
                await Walk(r, view, stand, 1.25f);
                // put it down: the box goes from the hands to the step
                view.SetSpeed(0f);
                view.transform.rotation = Quaternion.LookRotation(Flat(drop - view.transform.position));
                var held = r.carried; r.carried = null;
                Vector3 a = held.transform.position, b = drop;
                var q = r.rot * Quaternion.Euler(0f, 12f, 0f);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.7f)
                {
                    float e = t * t * (3f - 2f * t);
                    held.transform.SetPositionAndRotation(Vector3.Lerp(a, b, e), Quaternion.Slerp(held.transform.rotation, q, e));
                    if (r.ik != null && t < 0.8f) Grip(r, held.transform);
                    await Awaitable.NextFrameAsync(destroyCancellationToken);
                }
                held.transform.SetParent(transform, true);
                held.transform.SetPositionAndRotation(b, q);
                if (!_parcels.Exists(p => p.orderId == r.ev.OrderId) && !r.ev.ToLocker) _parcels.Add(new Doorstep { orderId = r.ev.OrderId, parcel = held, at = b, ready = _arrived.Contains(r.ev.OrderId) });
                else ReturnParcel(held);
                if (r.ev.ToLocker && _arrived.Contains(r.ev.OrderId)) { _lockerOrders.Add(r.ev.OrderId); ShopProps.SetLockerLit(_lockerScreen, true); }
                r.dropped = true;
                await Awaitable.WaitForSecondsAsync(0.5f, destroyCancellationToken);
                await Walk(r, view, from, 1.35f);
                ReturnCourier(view);
                r.courier = null;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); r.dropped = true; ReturnCourier(r.courier); r.courier = null; }
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        private void Grip(Run r, Transform box)
        {
            var c = box.position + Vector3.up * 0.15f;
            var side = box.right * 0.235f;
            r.ik.Grip(c - side, c + side, box.forward * -0.1f + Vector3.down * 0.2f);
        }

        private async Task Walk(Run r, AvatarView view, Vector3 to, float pace)
        {
            float t0 = Time.time;
            view.SetSpeed(pace);
            while (Time.time - t0 < 20f)
            {
                var p = view.transform.position;
                var d = to - p; d.y = 0f;
                if (d.magnitude < 0.08f) break;
                var step = d.normalized * Mathf.Min(d.magnitude, pace * Time.deltaTime);
                p += step;
                p.y = IslandStreamer.GroundHeight(p.x, p.z);
                var turn = Quaternion.RotateTowards(view.transform.rotation, Quaternion.LookRotation(d.normalized), 360f * Time.deltaTime);
                view.transform.SetPositionAndRotation(p, turn);
                if (r.carried != null && r.ik != null) Grip(r, r.carried.transform);
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }
            view.SetSpeed(0f);
        }

        private void Leave(Run r)
        {
            // pull out from the kerb back into the lane over 12 m, then on along it; gone out of view or after 70 m
            if (r.van == null) { r.done = true; return; }
            var t = r.van.transform;
            r.leftFor += Time.deltaTime;
            float v = Mathf.Min(Cruise, 0.9f + r.leftFor * 1.8f);
            float d = r.s - r.length + v * Time.deltaTime;
            r.s = r.length + d;
            Vector3 At(float x) { float k = Mathf.Clamp01(x / 12f); k = k * k * (3f - 2f * k); return r.stop + r.endDir * x - r.kerbOffset * k; }
            var p = At(d); var ahead = At(d + 2.5f) - p; ahead.y = 0f;
            p.y = IslandStreamer.GroundHeight(p.x, p.z);
            t.SetPositionAndRotation(p, ahead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(ahead.normalized) : t.rotation);
            if (r.car != null) { r.car.RailSpeed = v; r.car.RailSteer = Mathf.Clamp(Vector3.SignedAngle(r.endDir, ahead, Vector3.up) * 2.5f, -25f, 25f); }
            var me = Player();
            bool far = me != null && InteractionFinder.Flat(me.position, t.position) > 90f;
            if (d > 70f || far) r.done = true;
        }

        private void Clear(Run r)
        {
            ReturnVan(r.van); r.van = null;
            ReturnCourier(r.courier); r.courier = null;
            ReturnParcel(r.carried); r.carried = null;
        }

        // ------------------------------------------------------------------ pick up

        private WorldOffer Offer(Vector3 player)
        {
            if (_picking) return null;
            Doorstep best = null; float bestD = 2.2f;
            foreach (var p in _parcels)
            {
                if (p.parcel == null || !p.ready) continue;
                float d = InteractionFinder.Flat(player, p.at);
                if (d < bestD) { best = p; bestD = d; }
            }
            if (best != null)
                return new WorldOffer
                {
                    id = "parcel:" + best.orderId, verbKey = "interact.pickup", icon = "down", tagKey = "interact.tag.parcel",
                    position = best.at + Vector3.up * 0.55f, range = 2.2f, perform = () => _ = PickUp(best),
                };
            if (_locker != null && _lockerOrders.Count > 0)
                return new WorldOffer
                {
                    id = "locker", verbKey = "interact.collect", icon = "down", tagKey = "interact.tag.locker",
                    position = _locker.transform.position + _locker.transform.forward * 0.5f + Vector3.up * 1.3f, range = 2.4f,
                    perform = () => _ = Collect(),
                };
            return null;
        }

        private async Task PickUp(Doorstep p)
        {
            _picking = true;
            try
            {
                var items = await _svc.RefreshItems();
                _parcels.Remove(p);
                ReturnParcel(p.parcel);
                Bus.Publish(ToastSignal.Text(ShopText.T("pickup.done"), ToastKind.Good));
                Log.Info("delivery", $"picked up {p.orderId}: {items.Count} owned items");
            }
            catch (Exception e)
            {
                Log.Warn("delivery", "pick up: " + (e is ApiException a ? a.Code : e.Message));
                Bus.Publish(ToastSignal.Text(CityLifeStrings.T("err.network"), ToastKind.Bad));
            }
            finally { _picking = false; }
        }

        private async Task Collect()
        {
            _picking = true;
            try
            {
                await _svc.RefreshItems();
                _lockerOrders.Clear();
                ShopProps.SetLockerLit(_lockerScreen, false);
                Bus.Publish(ToastSignal.Text(ShopText.T("pickup.done"), ToastKind.Good));
            }
            catch (Exception e)
            {
                Log.Warn("delivery", "collect: " + (e is ApiException a ? a.Code : e.Message));
                Bus.Publish(ToastSignal.Text(CityLifeStrings.T("err.network"), ToastKind.Bad));
            }
            finally { _picking = false; }
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Dev: stage a delivery to a door without the server (captures and the route).</summary>
        public void DevStage(string unitId, float inSeconds, bool locker = false) =>
            OnDelivery(new DeliveryEvent { OrderId = "dev-" + Time.frameCount, Destination = locker ? "locker" : "door", UnitId = locker ? null : unitId,
                EtaAtMs = (long)(ServerClock.NowMs + inSeconds * 1000.0) });
#endif
    }
}
