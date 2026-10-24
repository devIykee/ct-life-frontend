using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World.Roads;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// AI traffic on CT Island v2 (Feel lane F3). Cars spawn out of sight on World's <see cref="RoadNetwork"/> lanes in a ring
    /// round the player (how many follows the district's <see cref="TrafficZone"/> density), follow their lanes at the
    /// speed limit (each driver a little faster or slower), slow for bends, keep their distance (IDM), stop at red lights
    /// and stop signs, give way at roundabouts and merges, wait for a clear junction, brake for people and for the player,
    /// honk when the player blocks them, change lanes round a stopped obstacle, and get knocked about when hit. Pooled
    /// (nothing is created after warm-up), capped (12 on a phone), quiet and driverless far away; NPC drivers sit at the
    /// wheel of the near ones (<see cref="TrafficDrivers"/>). Local to this client like every vehicle so far.
    /// </summary>
    public sealed class TrafficSystem : MonoBehaviour
    {
        public static TrafficSystem Current { get; private set; }

        // budget
        public int MaxCars = 12;
        public const float SpawnMin = 75f, SpawnMax = 190f, DespawnBeyond = 235f, QuietBeyond = 65f, Lookahead = 60f;
        /// <summary>Out of view a car may appear this close (behind the camera), so the street around the player has
        /// traffic, not just the far ring: with the ring alone at most one car was ever near enough to seat a driver.</summary>
        public const float SpawnHiddenMin = 32f;
        /// <summary>The traffic mix (spec ids and weights, normalised). All v3 models (2026-10-22: SUV, pickup, van and bike
        /// landed); vans and pickups rarer than sedans, bikes occasional. The hovercar is the player's own flyer, not traffic,
        /// and the police cruiser waits for the wanted system.</summary>
        public static readonly (string id, float weight)[] Mix =
        {
            ("compact", 0.25f), ("sedan", 0.27f), ("suv", 0.12f), ("sports", 0.08f), ("taxi", 0.11f),
            ("pickup", 0.05f), ("van", 0.04f), ("scooter", 0.05f), ("bike", 0.03f),
        };

        public readonly List<TrafficCar> Cars = new List<TrafficCar>();
        private readonly Dictionary<string, Stack<TrafficCar>> _pool = new Dictionary<string, Stack<TrafficCar>>();
        private readonly HashSet<Collider> _railColliders = new HashSet<Collider>();
        private readonly Dictionary<RoadIntersection, List<TrafficCar>> _inside = new Dictionary<RoadIntersection, List<TrafficCar>>();
        private readonly Collider[] _hits = new Collider[16];
        private System.Random _rng;
        private RoadNetwork _net;
        private LocalPlayerController _player;
        private TrafficDrivers _drivers;
        public TrafficDrivers Drivers => _drivers;
        private float _spawnAt, _quietAt;
        private int _step;
        private List<(LaneRef lane, float along, Vector3 pos)> _spawnPoints;
        private Dictionary<long, List<int>> _spawnGrid;
        public bool Paused { get; set; }
        /// <summary>Counts for captures and the device log.</summary>
        public int Spawned { get; private set; }
        public int Despawned { get; private set; }
        public int Honks { get; private set; }
        public int LaneChanges { get; private set; }
        public int RedStops { get; private set; }
        public int StopSignStops { get; private set; }
        public int Yields { get; private set; }

        public static TrafficSystem Ensure(LocalPlayerController player, Transform parent)
        {
            if (Current != null && Current._player == player) return Current;
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("Traffic");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TrafficSystem>();
            t._player = player;
            t._rng = new System.Random(Environment.TickCount);
            t._drivers = new TrafficDrivers(t);
            if (Application.isMobilePlatform) t.MaxCars = 12;
            var kinds = new List<string>();
            foreach (var m in Mix) kinds.Add(m.id);
            VehiclePrefabs.Preload(kinds);
            Current = t;
            return t;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            _drivers?.Dispose();
        }

        // ------------------------------------------------------------------ the network

        private bool Ready()
        {
            var net = RoadNetwork.Active;
            if (net == null) return false;
            if (!ReferenceEquals(net, _net)) { _net = net; IndexSpawns(); }
            return _spawnPoints != null && _spawnPoints.Count > 0;
        }

        private static long Cell(float x, float z) => ((long)Mathf.FloorToInt(x / 64f) << 32) ^ (uint)Mathf.FloorToInt(z / 64f);

        /// <summary>Candidate spawn points every 12 m along every lane (not within 9 m of a junction), on a 64 m grid.</summary>
        private void IndexSpawns()
        {
            _spawnPoints = new List<(LaneRef, float, Vector3)>();
            _spawnGrid = new Dictionary<long, List<int>>();
            foreach (var s in _net.segments)
            {
                if (s == null || s.kind == RoadKind.Tunnel) continue;
                for (int lane = 0; lane < s.LaneCount; lane++)
                {
                    var pts = _net.LaneCenterline(s.id, lane);
                    if (pts.Length < 2) continue;
                    var piece = RoutePiece.Make(pts);
                    for (float a = 9f; a < piece.Length - 9f; a += 12f)
                    {
                        var p = piece.Sample(a, out _);
                        int idx = _spawnPoints.Count;
                        _spawnPoints.Add((new LaneRef(s.id, lane), a, p));
                        var k = Cell(p.x, p.z);
                        if (!_spawnGrid.TryGetValue(k, out var list)) _spawnGrid[k] = list = new List<int>();
                        list.Add(idx);
                    }
                }
            }
            Log.Info("traffic", $"road network: {_net.segments.Count} segments, {_spawnPoints.Count} spawn points");
        }

        // ------------------------------------------------------------------ spawning

        private Vector3 Focus
        {
            get
            {
                var sys = VehicleSystem.Current;
                if (sys != null && sys.Driving != null) return sys.Driving.transform.position;
                return _player != null ? _player.transform.position : Vector3.zero;
            }
        }

        private int Target(Vector3 focus)
        {
            float density = VehicleWorld.TrafficDensityAt(focus, 0.7f);
            return Mathf.Clamp(Mathf.RoundToInt(MaxCars * Mathf.Clamp(density / 1.2f, 0.3f, 1f)), 2, MaxCars);
        }

        private void Update()
        {
            if (_player == null) { Destroy(gameObject); return; }
            if (Paused || !Ready()) return;
            var focus = Focus;
            // the player high in the air: the streets below are a model village; keep fewer, farther cars
            float air = focus.y - (VehicleWorld.TryGroundHeight(focus.x, focus.z, out var gy) ? gy : focus.y);
            int target = air > 60f ? Mathf.Max(2, Target(focus) / 2) : Target(focus);
            if (Time.time >= _spawnAt)
            {
                _spawnAt = Time.time + 0.22f;
                Despawn(focus);
                if (Cars.Count < target) TrySpawn(focus);
            }
            if (Time.time >= _quietAt)
            {
                _quietAt = Time.time + 0.5f;
                var cam = Camera.main != null ? Camera.main.transform.position : focus;
                foreach (var c in Cars) if (c.Audio != null) c.Audio.Quiet = (c.transform.position - cam).sqrMagnitude > QuietBeyond * QuietBeyond;
            }
            _drivers.Update(Cars);
        }

        private void LateUpdate() => _drivers?.LateUpdate();

        private readonly Plane[] _planes = new Plane[6];
        private int _planesFrame = -1;

        private bool Visible(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            if (_planesFrame != Time.frameCount) { GeometryUtility.CalculateFrustumPlanes(cam, _planes); _planesFrame = Time.frameCount; }
            return GeometryUtility.TestPlanesAABB(_planes, new Bounds(p + Vector3.up, new Vector3(5f, 3f, 5f)));
        }

        private void TrySpawn(Vector3 focus)
        {
            // gather candidate points in the ring from the grid cells around the focus
            int r = Mathf.CeilToInt(SpawnMax / 64f);
            int cx = Mathf.FloorToInt(focus.x / 64f), cz = Mathf.FloorToInt(focus.z / 64f);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int rr = (attempt & 1) == 0 ? 1 : r;                        // every other try in the cells around the player
                long k = ((long)(cx + _rng.Next(-rr, rr + 1)) << 32) ^ (uint)(cz + _rng.Next(-rr, rr + 1));
                if (!_spawnGrid.TryGetValue(k, out var list) || list.Count == 0) continue;
                var (lane, along, pos) = _spawnPoints[list[_rng.Next(list.Count)]];
                float d = Vector3.Distance(Flat(pos), Flat(focus));
                if (!SpawnDistanceOk(d, Visible(pos))) continue;            // pop-in only where nobody is looking
                if (!VehicleWorld.IsReady(pos) || !VehicleWorld.Dry(pos)) continue;
                bool crowded = false;
                foreach (var c in Cars) if ((c.transform.position - pos).sqrMagnitude < 22f * 22f) { crowded = true; break; }
                if (crowded) continue;
                if (Physics.CheckSphere(pos + Vector3.up * 1f, 2.2f, (1 << CharacterLayers.Vehicle) | (1 << 13), QueryTriggerInteraction.Ignore)) continue;
                var car = Take(PickKind());
                if (car == null) return;
                if (!car.Route.StartAt(_net, lane, along, _rng)) { Give(car); continue; }
                car.Route.Extend(Lookahead + 20f);
                car.ToRails();
                VehiclePaint.Apply(car.Vehicle, VehiclePaint.Pick(_rng.NextDouble()));
                car.Pace = 0.86f + (float)_rng.NextDouble() * 0.2f;
                float limit = car.Route.Current.SpeedLimit;
                car.Speed = limit * car.Pace * 0.8f;
                car.SpawnedAt = Time.time;
                var at = car.Route.At(0f, out var dir);
                car.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward));
                car.Body.position = at; car.Body.rotation = car.transform.rotation;
                car.gameObject.SetActive(true);
                foreach (var col in car.Colliders) if (col != null) _railColliders.Add(col);
                Cars.Add(car);
                Spawned++;
                return;
            }
        }

        /// <summary>Where a car may appear: anywhere in the ring out of view, or from <see cref="SpawnHiddenMin"/> when
        /// unseen; in view only beyond 150 m. Pure, EditMode-tested.</summary>
        public static bool SpawnDistanceOk(float d, bool visible)
        {
            if (d > SpawnMax) return false;
            if (visible) return d >= 150f;
            return d >= SpawnHiddenMin;
        }

        private string PickKind()
        {
            double total = 0; foreach (var m in Mix) total += m.weight;
            double r = _rng.NextDouble() * total;
            foreach (var m in Mix) { r -= m.weight; if (r <= 0) return m.id; }
            return Mix[0].id;
        }

        private TrafficCar Take(string id)
        {
            if (_pool.TryGetValue(id, out var stack) && stack.Count > 0) return stack.Pop();
            // loaded ahead (VehiclePrefabs.Preload in Ensure): a kind still loading is skipped this time, no frame waits on it
            if (!VehiclePrefabs.Ready(id)) return null;
            var prefab = VehiclePrefabs.Get(id, false);
            if (prefab == null) { Log.Warn("traffic", "missing prefab Vehicles/" + id); return null; }
            var go = Instantiate(prefab, transform);
            go.SetActive(false);
            go.name = "traffic-" + id;
            var car = go.AddComponent<TrafficCar>();
            car.Bind();
            return car;
        }

        private void Give(TrafficCar car)
        {
            if (car == null) return;
            _drivers.Detach(car);
            foreach (var col in car.Colliders) if (col != null) _railColliders.Remove(col);
            Leave(car);
            car.gameObject.SetActive(false);
            car.ToRails();
            if (!_pool.TryGetValue(car.Kind, out var stack)) _pool[car.Kind] = stack = new Stack<TrafficCar>();
            stack.Push(car);
        }

        private void Despawn(Vector3 focus)
        {
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                var c = Cars[i];
                bool gone = c == null;
                if (!gone)
                {
                    var p = c.transform.position;
                    float d = Vector3.Distance(Flat(p), Flat(focus));
                    gone = d > DespawnBeyond
                           || (d > SpawnMax + 10f && !Visible(p))
                           || (c.Route.DeadEnd && c.Route.Remaining < 1f && !Visible(p))
                           || (c.Knocked && Time.time - c.KnockedAt > 12f && (d > 35f || !Visible(p)))
                           || (c.Knocked && !VehicleWorld.Dry(p, -1f))
                           || (!c.Knocked && c.BlockedFor > 4f && !Visible(p));
                }
                if (!gone) continue;
                Cars.RemoveAt(i);
                if (c != null) Give(c);
                Despawned++;
            }
        }

        /// <summary>Every traffic car back to the pool (leaving the island, a capture resetting).</summary>
        public void Clear()
        {
            for (int i = Cars.Count - 1; i >= 0; i--) Give(Cars[i]);
            Cars.Clear();
            _inside.Clear();
        }

        // ------------------------------------------------------------------ driving

        /// <summary>Main-thread cost of one traffic physics step (ms, smoothed), and the closest any two cars came (m).</summary>
        public float StepMs { get; private set; }
        public float ClosestGap { get; private set; } = float.PositiveInfinity;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

        private void FixedUpdate()
        {
            if (Paused || _net == null || Cars.Count == 0) return;
            _clock.Restart();
            Step();
            _clock.Stop();
            StepMs = Mathf.Lerp(StepMs, (float)_clock.Elapsed.TotalMilliseconds, 0.05f);
        }

        private void Step()
        {
            float dt = Time.fixedDeltaTime;
            double now = Time.timeAsDouble;
            _step++;
            var playerCar = VehicleSystem.Current != null ? VehicleSystem.Current.Driving : null;
            for (int i = 0; i < Cars.Count; i++)
            {
                var c = Cars[i];
                if (c == null || c.Knocked) { if (c != null && c.Knocked && c.HonkAt > 0f && Time.time >= c.HonkAt) { c.HonkAt = -1f; c.Audio?.Horn(); Honks++; } continue; }
                c.Slot = i;
                // about to be hit by the player's car: become physical first, so the impact is car on car, not car on wall
                if (playerCar != null && playerCar.Body != null && !playerCar.Body.isKinematic) PreKnock(c, playerCar);
                if (!c.Knocked) Drive(c, dt, now, playerCar, (i + _step) % 5 == 0);
            }
        }

        private void PreKnock(TrafficCar c, Vehicle playerCar)
        {
            var rel = playerCar.Body.linearVelocity - c.transform.forward * c.Speed;
            if (rel.sqrMagnitude < 2.2f * 2.2f) return;
            var a = playerCar.GetComponent<Collider>(); var b = c.GetComponent<Collider>();
            if (a == null || b == null) return;
            var ba = a.bounds; ba.Expand(rel.magnitude * Time.fixedDeltaTime * 2f + 0.2f);
            if (ba.Intersects(b.bounds)) c.Knock();
        }

        private void Drive(TrafficCar c, float dt, double now, Vehicle playerCar, bool scan)
        {
            var route = c.Route;
            var piece = route.Current;
            if (piece == null) return;
            route.Extend(Lookahead + 20f);
            float v = c.Speed;
            // 1) the speed the road allows: the limit, and any bend ahead (slowing in time); bends are re-read 10 times a second
            float v0 = Mathf.Max(3f, piece.SpeedLimit * c.Pace);
            if (scan || float.IsNaN(c.BendCap))
            {
                float cap = 99f;
                for (float d = 4f; d <= Mathf.Min(Lookahead, 8f + v * 4f); d += 6f)
                    cap = Mathf.Min(cap, TrafficRules.ApproachSpeed(TrafficRules.CurveSpeed(route.Curvature(d)), d - 4f));
                c.BendCap = cap; c.BendAt = route.S;
            }
            v0 = Mathf.Min(v0, c.BendCap);
            // 2) what is ahead: the next car in its lane order, a stop line, the sensor (people, the player, wrecks)
            float gap = float.PositiveInfinity, lead = 0f; string why = "";
            LeaderAhead(c, ref gap, ref lead, ref why);
            JunctionAhead(c, now, playerCar, ref gap, ref lead, ref why);
            if (scan) Sense(c, playerCar);
            if (!float.IsInfinity(c.SensorGap))
            {
                float g = c.SensorGap - (v - c.SensorSpeed) * Mathf.Max(0f, Time.time - c.SensorAt);
                // pulling out round it: creep closer while moving across (the car steers out, it does not slide)
                if (Mathf.Abs(c.Lateral) > 0.4f) { g += 1.8f; v0 = Mathf.Min(v0, 1.4f); }
                if (g < gap) { gap = g; lead = c.SensorSpeed; why = c.SensorPlayer ? "player" : "obstacle"; }
            }
            float a = TrafficRules.Idm(v, v0, gap, lead);
            c.LastAccel = a; c.LastGap = gap; c.LastReason = why;
            if (why == "car" && gap < ClosestGap) ClosestGap = gap;
            v = Mathf.Clamp(v + a * dt, 0f, v0 + 1.5f);
            if (gap < 0.3f) v = Mathf.Min(v, lead);   // never into it
            // Kinematic MovePosition does not stop on static geometry. Sweep every physics step, including between scans.
            if (StaticClearance(c.transform.position, c.transform.rotation, c.HalfWidth, c.HalfLength, v * dt + 0.35f) < v * dt + 0.15f)
            { v = 0f; why = "obstacle"; }
            c.Speed = v;
            // 3) honk at the player, or pull round a stopped obstacle
            React(c, why, a, dt);
            // 4) move
            var before = route.Current;
            bool going = route.Advance(v * dt);
            if (route.Current != before) Passed(c, before);
            if (!going) c.Speed = 0f;
            c.Lateral = Mathf.MoveTowards(c.Lateral, 0f, (v < 4f ? 1.5f : 0.9f) * dt);
            var pos = route.At(0f, out var dir);
            if (dir.sqrMagnitude < 1e-6f) dir = c.transform.forward;
            var right = Vector3.Cross(Vector3.up, Flat(dir)).normalized;
            pos += right * c.Lateral;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            c.Body.MovePosition(pos);
            c.Body.MoveRotation(rot);
            // wheels, steering wheel, engine note
            if (c.Car != null)
            {
                c.Car.RailSpeed = v;
                float k = route.Curvature(2.5f, 3f);
                c.Car.RailSteer = Mathf.Clamp(Mathf.Atan(2.6f * k) * Mathf.Rad2Deg, -32f, 32f);
            }
            c.Vehicle.Input = new VehicleInput { throttle = a > 0.2f ? Mathf.Clamp01(a / TrafficRules.MaxAccel) : 0f, brake = a < -1f ? Mathf.Clamp01(-a / TrafficRules.MaxBrake) : 0f };
            // people stepping out at the last moment still get knocked down rather than walked through (only when the
            // sensor has someone or something close ahead: a box query per car every step would be the costliest thing here)
            if (v > 2.5f && c.Car != null && c.SensorGap < 10f) c.Car.GuardPeople(v, dir * v);
        }

        // ------------------------------------------------------------------ what is ahead

        /// <summary>Lane position of a car: its lane and metres along that lane (on a connector: negative, short of the lane).</summary>
        private static (LaneRef lane, float along) LaneSpot(TrafficCar c)
        {
            var p = c.Route.Current;
            if (p == null) return (default, 0f);
            return p.Connector ? (p.Lane, c.Route.S - p.Length) : (p.Lane, p.StartAlong + c.Route.S);
        }

        private void LeaderAhead(TrafficCar c, ref float gap, ref float lead, ref string why)
        {
            var route = c.Route;
            for (int i = 0; i < route.Pieces.Count; i++)
            {
                float start = route.ToStart(i);
                if (start > Lookahead) break;
                var piece = route.Pieces[i];
                foreach (var o in Cars)
                {
                    if (o == c || o == null || o.Knocked) continue;
                    var (lane, along) = LaneSpot(o);
                    if (!lane.Equals(piece.Lane)) continue;
                    // where the other car is along this piece: lanes count from StartAlong, connectors end at 0
                    float onPiece = piece.Connector ? along + piece.Length : along - piece.StartAlong;
                    if (piece.Connector && along >= 0f) continue;            // already past this connector (handled by the lane piece)
                    if (!piece.Connector && along < piece.StartAlong - 0.5f) continue;
                    float d = start + onPiece - c.HalfLength - o.HalfLength;
                    if (d < -c.HalfLength) continue;                          // behind
                    if (d < 0f && Mathf.Abs(o.Lateral - c.Lateral) > o.HalfWidth + c.HalfWidth) continue;   // beside us (pulling across), not ahead
                    if (Mathf.Abs(o.Lateral) > 1.2f) d += 4f;                 // still moving across into our lane: make room, no panic stop
                    if (d < gap) { gap = d; lead = o.Speed; why = "car"; }
                }
                if (gap < float.PositiveInfinity) return;
            }
        }

        private void JunctionAhead(TrafficCar c, double now, Vehicle playerCar, ref float gap, ref float lead, ref string why)
        {
            var route = c.Route;
            for (int i = 0; i < route.Pieces.Count; i++)
            {
                var piece = route.Pieces[i];
                if (piece.Connector || piece.Junction == null) continue;
                float toLine = route.ToStart(i) + piece.Length - c.HalfLength - 0.6f;
                if (toLine > Lookahead) return;
                var x = piece.Junction;
                bool hold = false;
                if (piece.Ring) { /* on the roundabout: right of way */ }
                else if (x.kind == IntersectionKind.Signal)
                {
                    var state = _net.Signal(x.id, piece.Lane.segmentId, now);
                    hold = TrafficRules.StopForSignal(state, toLine, c.Speed);
                    if (hold && state == SignalState.Red && toLine < 3f && c.Speed < 0.5f && c.StoppedAt != x) { c.StoppedAt = x; RedStops++; }
                }
                else if (x.kind == IntersectionKind.Stop)
                {
                    if (i == 0 && toLine < 2.5f && c.Speed < 0.4f)
                    {
                        if (c.StoppedAt != x) { c.StoppedAt = x; c.StoppedFor = 0f; c.StopDone = false; c.WaitingSince = Time.time; }
                        c.StoppedFor += Time.fixedDeltaTime;
                        if (c.StoppedFor > 0.8f && !c.StopDone) { c.StopDone = true; StopSignStops++; }
                    }
                    hold = !(c.StoppedAt == x && c.StopDone && FirstWaiting(c, x));
                }
                else if (piece.Entry || x.kind == IntersectionKind.Merge)
                {
                    if (i == 0 && toLine < 12f)
                    {
                        hold = !Clear(c, route, i + 1, x.kind == IntersectionKind.Merge ? 16f : 12f, playerCar);
                        if (hold && !c.Yielding) { c.Yielding = true; Yields++; }
                        else if (!hold) c.Yielding = false;
                    }
                }
                // any kind: do not enter a junction someone is still crossing on a path through ours
                if (!hold && i <= 1 && toLine < 7f) hold = Conflict(c, route, i + 1, x, playerCar);
                if (hold)
                {
                    float g = toLine;
                    if (g < gap) { gap = g; lead = 0f; why = x.kind.ToString().ToLowerInvariant(); }
                }
                return;   // only the first junction ahead matters
            }
        }

        private bool FirstWaiting(TrafficCar c, RoadIntersection x)
        {
            foreach (var o in Cars)
                if (o != c && o != null && !o.Knocked && o.StoppedAt == x && o.Inside == null && o.WaitingSince >= 0f && o.WaitingSince < c.WaitingSince) return false;
            return !_inside.TryGetValue(x, out var list) || list.Count == 0;
        }

        /// <summary>No car (or the player) near where our connector joins its next lane: we may pull out.</summary>
        private bool Clear(TrafficCar c, TrafficRoute route, int connector, float radius, Vehicle playerCar)
        {
            if (connector >= route.Pieces.Count) return true;
            var con = route.Pieces[connector];
            var join = con.Points.Length > 0 ? con.Points[con.Points.Length - 1] : c.transform.position;
            float r2 = radius * radius;
            foreach (var o in Cars)
            {
                if (o == c || o == null || o.Knocked) continue;
                if ((o.transform.position - join).sqrMagnitude > r2) continue;
                // cars queued behind us on our own lane do not count
                var (lane, _) = LaneSpot(o);
                if (lane.Equals(c.Route.Current.Lane)) continue;
                if (o.Speed < 0.5f && o.Inside == null) continue;            // parked in a queue on the ring exit, not coming
                return false;
            }
            if (playerCar != null && (playerCar.transform.position - join).sqrMagnitude < r2) return false;
            return true;
        }

        /// <summary>Someone inside the junction is on (or next to) the path our connector takes through it.</summary>
        private bool Conflict(TrafficCar c, TrafficRoute route, int connector, RoadIntersection x, Vehicle playerCar)
        {
            if (connector >= route.Pieces.Count) return false;
            var con = route.Pieces[connector];
            if (!con.Connector) return false;
            if (_inside.TryGetValue(x, out var list))
                foreach (var o in list)
                {
                    if (o == c || o == null || o.Knocked) continue;
                    // from another approach into the same lane as us: one at a time
                    var oc = o.Route.Current;
                    if (oc != null && oc.Connector && oc.Lane.Equals(con.Lane) && oc.FromSegment != con.FromSegment) return true;
                    var p = o.transform.position;
                    var f = Flat(o.transform.forward).normalized;
                    for (int k = 0; k < con.Points.Length; k++)
                    {
                        var q = con.Points[k];
                        if ((q - p).sqrMagnitude >= 3.6f * 3.6f) continue;
                        // going our way through it (a leader we simply follow) is not a conflict; crossing it is
                        var along = Flat(con.Points[Mathf.Min(k + 1, con.Points.Length - 1)] - con.Points[Mathf.Max(k - 1, 0)]).normalized;
                        if (Vector3.Dot(along, f) > 0.7f) continue;
                        return true;
                    }
                }
            if (playerCar != null)
            {
                var p = playerCar.transform.position;
                foreach (var q in con.Points) if ((q - p).sqrMagnitude < 4.2f * 4.2f) return true;
            }
            return false;
        }

        /// <summary>A piece dropped off the front: crossing into a junction, or out of it.</summary>
        private void Passed(TrafficCar c, RoutePiece left)
        {
            var now = c.Route.Current;
            if (now != null && now.Connector && now.Junction != null)
            {
                c.Inside = now.Junction;
                if (!_inside.TryGetValue(now.Junction, out var list)) _inside[now.Junction] = list = new List<TrafficCar>();
                list.Add(c);
                c.StoppedAt = null; c.StopDone = false; c.WaitingSince = -1f;
            }
            else if (left != null && left.Connector) Leave(c);
        }

        private void Leave(TrafficCar c)
        {
            if (c.Inside != null && _inside.TryGetValue(c.Inside, out var list)) list.Remove(c);
            c.Inside = null;
        }

        /// <summary>
        /// A box down the lane ahead (5 times a second): people, the player (on foot or driving), wrecks and parked cars in
        /// the road. Rail traffic is not in it (the lane order handles that).
        /// </summary>
        private void Sense(TrafficCar c, Vehicle playerCar)
        {
            float len = Mathf.Clamp(6f + c.Speed * 2.6f, 10f, 34f);
            var mid = c.Route.At(c.HalfLength + len * 0.5f, out var dir);
            if (dir.sqrMagnitude < 1e-6f) dir = c.transform.forward;
            var rot = Quaternion.LookRotation(Flat(dir), Vector3.up);
            int mask = (1 << CharacterLayers.Characters) | (1 << CharacterLayers.Vehicle) | (1 << CharacterLayers.VehicleFlight) | StaticMask;
            int n = Physics.OverlapBoxNonAlloc(mid + Vector3.up * 1.1f, new Vector3(c.HalfWidth + 1.4f, 1.2f, len * 0.5f + 1f), _hits, rot, mask, QueryTriggerInteraction.Ignore);
            float best = StaticClearance(c.transform.position, c.transform.rotation, c.HalfWidth, c.HalfLength, Mathf.Min(len, 12f));
            float speed = 0f; bool player = false;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h == null || _railColliders.Contains(h)) continue;
                if (h.transform.IsChildOf(c.transform)) continue;
                var p = h.bounds.center;
                if (!c.Route.Project(p, len + c.HalfLength + 4f, out float ahead, out float lateral)) continue;
                float halfW = Mathf.Min(1.6f, Mathf.Max(h.bounds.extents.x, h.bounds.extents.z));
                bool person = h.gameObject.layer == CharacterLayers.Characters;
                float corridor = c.HalfWidth + (person ? 0.9f : halfW * 0.8f) + 0.25f;
                if (Mathf.Abs(lateral - c.Lateral) > corridor) continue;
                float g = ahead - c.HalfLength - (person ? 0.5f : halfW);
                if (g >= best) continue;
                best = g;
                var rb = h.attachedRigidbody;
                speed = rb != null && !rb.isKinematic ? Mathf.Max(0f, Vector3.Dot(rb.linearVelocity, Flat(dir))) : 0f;
                var root = rb != null ? rb.transform : h.transform;
                player = (playerCar != null && root.IsChildOf(playerCar.transform)) || (_player != null && h.transform.IsChildOf(_player.transform));
            }
            c.SensorGap = best; c.SensorSpeed = speed; c.SensorPlayer = player; c.SensorAt = Time.time;
        }

        public const int StaticMask = (1 << 12) | (1 << 13) | (1 << 14) | (1 << CharacterLayers.VehicleBlocker) | (1 << CharacterLayers.CoastWall);
        /// <summary>Distance a car-sized box can move before a building/prop. Explicit because rail bodies are kinematic.</summary>
        public static float StaticClearance(Vector3 position, Quaternion rotation, float halfWidth, float halfLength, float distance)
        {
            return Physics.BoxCast(position + Vector3.up * 0.9f, new Vector3(halfWidth + 0.12f, 0.6f, halfLength),
                rotation * Vector3.forward, out var hit, rotation, distance, StaticMask, QueryTriggerInteraction.Ignore)
                ? hit.distance : float.PositiveInfinity;
        }

        private void React(TrafficCar c, string why, float accel, float dt)
        {
            bool blocked = (why == "player" || why == "obstacle") && c.Speed < 0.6f;
            c.BlockedFor = blocked ? c.BlockedFor + dt : 0f;
            // honk: the player stops us for a moment, or cuts in so we brake hard
            bool cutIn = why == "player" && accel < -4.5f && c.Speed > 4f;
            if (((why == "player" && c.BlockedFor > 1.6f) || cutIn) && Time.time >= c.HonkAt)
            {
                c.HonkAt = Time.time + 5.5f + (float)_rng.NextDouble() * 3f;
                c.Audio?.Horn();
                Honks++;
            }
            // pull round something that is not moving, if the road has another lane our way
            if (c.BlockedFor > 3f && Time.time >= c.LaneChangeAt) { c.LaneChangeAt = Time.time + 4f; if (ChangeLane(c)) LaneChanges++; }
        }

        private bool ChangeLane(TrafficCar c)
        {
            var piece = c.Route.Current;
            if (piece == null || piece.Connector) return false;
            var seg = _net.Segment(piece.Lane.segmentId);
            if (seg == null) return false;
            int lane = piece.Lane.lane;
            bool fwd = seg.IsForward(lane);
            int lo = fwd ? 0 : seg.lanesForward, hi = fwd ? seg.lanesForward - 1 : seg.LaneCount - 1;
            foreach (int to in new[] { lane + 1, lane - 1 })
            {
                if (to < lo || to > hi) continue;
                var pts = _net.LaneCenterline(seg.id, to);
                if (pts.Length < 2) continue;
                var full = RoutePiece.Make(pts);
                float along = full.Locate(c.transform.position, out float lateral, out _);
                if (along > full.Length - 12f) continue;
                // the new lane must be free a little behind and ahead of us
                bool free = true;
                foreach (var o in Cars)
                {
                    if (o == c || o == null) continue;
                    var (ol, oa) = LaneSpot(o);
                    if (ol.segmentId == seg.id && ol.lane == to && oa > along - 14f && oa < along + 12f) { free = false; break; }
                }
                if (!free) continue;
                Leave(c);
                if (!c.Route.StartAt(_net, new LaneRef(seg.id, to), along, _rng)) continue;
                c.Route.Extend(Lookahead + 20f);
                c.Lateral = lateral;   // where we are now, seen from the new lane; eases to 0 (the move across)
                c.BlockedFor = 0f;
                return true;
            }
            return false;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>A spot at a junction's corner beside the road, where a pedestrian would watch it from (captures, the demo).</summary>
        public static Vector3 Corner(RoadNetwork net, RoadIntersection x)
        {
            var c = x.center;
            float r = Mathf.Max(14f, x.radius + 10f);
            foreach (var a in new[] { 45f, 135f, 225f, 315f })
            {
                var q = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                var lp = net.NearestLanePoint(q, 12f);
                if (lp.valid && lp.distance < 5f) continue;   // in the road
                if (Physics.Raycast(q + Vector3.up * 30f, Vector3.down, out var hit, 60f, CharacterLayers.BodyMask, QueryTriggerInteraction.Ignore)) return hit.point;
            }
            return c + Vector3.forward * r;
        }

        /// <summary>The nearest junction of a kind to a point (null when the network has none).</summary>
        public static RoadIntersection Nearest(RoadNetwork net, Vector3 p, IntersectionKind kind)
        {
            RoadIntersection best = null; float d = float.MaxValue;
            if (net == null) return null;
            foreach (var x in net.intersections)
            {
                if (x == null || x.kind != kind) continue;
                float dd = (x.center - p).sqrMagnitude;
                if (dd < d) { d = dd; best = x; }
            }
            return best;
        }
    }
}
