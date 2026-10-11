using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World.Roads;
using CTLife.World.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.World.Pedestrians
{
    /// <summary>
    /// Ambient pedestrians round the player on CT Island v2 (Feel lane F6), on World W4's walkable network
    /// (<see cref="PedestrianPaths"/>). Pooled and capped (fewer on a phone, fewer again when it runs hot), more where the
    /// network is dense (16 on a Seeker, 12 when it runs hot); they appear out of view between <see cref="SpawnMin"/> and <see cref="SpawnMax"/> metres and leave
    /// unseen beyond <see cref="DespawnBeyond"/>. Bodies are the cast archetypes (varied looks); the avatar LOD budget
    /// draws them out to <see cref="DrawWithin"/> m at reduced animation rates. Local to this client, like the traffic.
    /// </summary>
    public sealed class PedestrianSystem : MonoBehaviour
    {
        public const float SpawnMin = 28f, SpawnMax = 80f, DespawnBeyond = 100f, DrawWithin = 70f;
        public const int MaxDesktop = 24, MaxMobile = 16;
        /// <summary>The chance a walker takes a crossing they pass.</summary>
        public float CrossChance = 0.35f;
        public int Max = MaxDesktop;

        public static PedestrianSystem Current { get; private set; }
        public readonly List<Pedestrian> People = new List<Pedestrian>();
        public int Spawned { get; private set; }
        public int Despawned { get; private set; }
        /// <summary>Smoothed cost of one frame of the whole crowd (ms).</summary>
        public float StepMs { get; private set; }
        private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();

        public sealed class BenchSeat { public Vector3 position, forward; public bool taken; }

        private LocalPlayerController _player;
        private PedestrianPaths _net;
        private System.Random _rng;
        private readonly List<Pedestrian> _pool = new List<Pedestrian>();
        private readonly List<(int path, float s, Vector3 p)> _spawnPoints = new List<(int, float, Vector3)>();
        private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
        private readonly List<BenchSeat> _benches = new List<BenchSeat>();
        private string[] _archetypes;
        private int _nextLook;
        private float _spawnAt, _createAt, _benchAt;

        public static PedestrianSystem Ensure(LocalPlayerController player, Transform parent)
        {
            if (Current != null && Current._player == player) return Current;
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("Pedestrians");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<PedestrianSystem>();
            s._player = player;
            s._rng = new System.Random(Environment.TickCount ^ 0x5eed);
            if (Application.isMobilePlatform) s.Max = MaxMobile;
            Current = s;
            return s;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            foreach (var p in People) if (p != null) Object.Destroy(p.gameObject);
            foreach (var p in _pool) if (p != null) Object.Destroy(p.gameObject);
        }

        private bool Ready()
        {
            if (!VehicleWorld.OnIsland) return false;
            if (_net == null)
            {
                _net = PedestrianPaths.Island;
                if (_net == null) { enabled = false; Log.Warn("peds", "no " + PedestrianPaths.ResourcePath); return false; }
                IndexSpawns();
                Log.Info("peds", $"walkable network: {_net.Paths.Count} paths ({_net.TotalLength / 1000f:F1} km), {_net.Nodes.Count} nodes, {_net.Crossings.Count} crossings, {_net.Areas.Count} areas, {_spawnPoints.Count} spawn points");
            }
            return true;
        }

        private static long Cell(float x, float z) => ((long)Mathf.FloorToInt(x / 48f) << 32) ^ (uint)Mathf.FloorToInt(z / 48f);

        private void IndexSpawns()
        {
            for (int i = 0; i < _net.Paths.Count; i++)
            {
                var p = _net.Paths[i];
                if (p.corner) continue;
                for (float s = 3f; s < p.Length - 3f; s += 6f)
                {
                    var at = p.Sample(s, out _);
                    long k = Cell(at.x, at.z);
                    if (!_grid.TryGetValue(k, out var list)) _grid[k] = list = new List<int>();
                    list.Add(_spawnPoints.Count);
                    _spawnPoints.Add((i, s, at));
                }
            }
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (_player == null) { Destroy(gameObject); return; }
            if (!Ready()) return;
            _watch.Restart();
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            var me = _player.transform.position;
            bool driving = VehicleSystem.Current != null && VehicleSystem.Current.Driving != null;
            for (int i = People.Count - 1; i >= 0; i--)
            {
                var p = People[i];
                if (p == null) { People.RemoveAt(i); continue; }
                bool near = !driving && (p.transform.position - me).sqrMagnitude < 9f;
                p.Tick(dt, me, near);
            }
            if (Time.time >= _benchAt) { _benchAt = Time.time + 4f; RefreshBenches(me); }
            if (Time.time >= _spawnAt)
            {
                _spawnAt = Time.time + 0.3f;
                Despawn(me);
                if (People.Count < Target(me)) TrySpawn(me);
            }
            StepMs = Mathf.Lerp(StepMs, (float)_watch.Elapsed.TotalMilliseconds, 0.05f);
        }

        /// <summary>How many people round <paramref name="focus"/>: the walkable length nearby (dense downtown, sparse hills).</summary>
        public int Target(Vector3 focus)
        {
            int near = 0;
            int r = Mathf.CeilToInt(SpawnMax / 48f);
            int cx = Mathf.FloorToInt(focus.x / 48f), cz = Mathf.FloorToInt(focus.z / 48f);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    if (_grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var list)) near += list.Count;
            int max = AvatarLod.ThermalScale < 1f ? Mathf.RoundToInt(Max * 0.75f) : Max;
            return TargetFor(near, max);
        }

        /// <summary>One person per 15 spawn points (90 m of sidewalk) nearby, capped. Pure, EditMode-tested.</summary>
        public static int TargetFor(int spawnPointsNear, int max) => Mathf.Clamp(spawnPointsNear / 15, 0, max);

        private readonly Plane[] _planes = new Plane[6];
        private int _planesFrame = -1;

        public bool Visible(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            if (_planesFrame != Time.frameCount) { GeometryUtility.CalculateFrustumPlanes(cam, _planes); _planesFrame = Time.frameCount; }
            return GeometryUtility.TestPlanesAABB(_planes, new Bounds(p + Vector3.up, new Vector3(1f, 2f, 1f)));
        }

        /// <summary>Where someone may appear: out of view from <see cref="SpawnMin"/>, in view only far off. Pure, EditMode-tested.</summary>
        public static bool SpawnDistanceOk(float d, bool visible) => d <= SpawnMax && (visible ? d >= 75f : d >= SpawnMin);

        private void TrySpawn(Vector3 focus)
        {
            int r = Mathf.CeilToInt(SpawnMax / 48f);
            int cx = Mathf.FloorToInt(focus.x / 48f), cz = Mathf.FloorToInt(focus.z / 48f);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int rr = (attempt & 1) == 0 ? 1 : r;
                long k = ((long)(cx + _rng.Next(-rr, rr + 1)) << 32) ^ (uint)(cz + _rng.Next(-rr, rr + 1));
                if (!_grid.TryGetValue(k, out var list) || list.Count == 0) continue;
                var (path, s, pos) = _spawnPoints[list[_rng.Next(list.Count)]];
                var flat = pos - focus; flat.y = 0f;
                if (!SpawnDistanceOk(flat.magnitude, Visible(pos))) continue;
                if (!VehicleWorld.IsReady(pos)) continue;
                bool crowded = false;
                foreach (var o in People) if ((o.transform.position - pos).sqrMagnitude < 9f) { crowded = true; break; }
                if (crowded) continue;
                var ped = Take();
                if (ped == null) return;
                // some stroll the open areas (plazas, parks, the boardwalk) instead of a sidewalk
                int area = AreaNear(pos);
                if (area >= 0 && _rng.NextDouble() < 0.4 && RandomIn(area, out var inArea) && !Visible(inArea)) ped.BeginStroll(_net, area, inArea, _rng);
                else ped.Begin(_net, path, s, _rng);
                ped.gameObject.SetActive(true);
                People.Add(ped);
                Spawned++;
                return;
            }
        }

        private int AreaNear(Vector3 p)
        {
            for (int i = 0; i < _net.Areas.Count; i++)
            {
                var b = _net.Areas[i].Bounds;
                if (p.x > b.xMin - 30f && p.x < b.xMax + 30f && p.z > b.yMin - 30f && p.z < b.yMax + 30f) return i;
            }
            return -1;
        }

        private bool RandomIn(int area, out Vector3 at)
        {
            var a = _net.Areas[area];
            var b = a.Bounds;
            for (int i = 0; i < 10; i++)
            {
                float x = b.xMin + (float)_rng.NextDouble() * b.width, z = b.yMin + (float)_rng.NextDouble() * b.height;
                if (!a.Contains(x, z)) continue;
                float y = a.y ?? GroundY(new Vector3(x, 200f, z), float.NaN);
                if (float.IsNaN(y)) continue;
                at = new Vector3(x, y, z);
                if (!ClearOfRoad(at, at)) continue;
                return true;
            }
            at = default;
            return false;
        }

        private void Despawn(Vector3 focus)
        {
            for (int i = People.Count - 1; i >= 0; i--)
            {
                var p = People[i];
                var d = p.transform.position - focus; d.y = 0f;
                float m = d.magnitude;
                if (m > DespawnBeyond + 30f || (m > DespawnBeyond && !Visible(p.transform.position))) { Retire(p); Despawned++; }
            }
        }

        public void Retire(Pedestrian p)
        {
            if (p == null) return;
            p.ReleaseBench();
            People.Remove(p);
            p.gameObject.SetActive(false);
            if (!_pool.Contains(p)) _pool.Add(p);
        }

        private Pedestrian Take()
        {
            if (_pool.Count > 0 && (_rng.NextDouble() < 0.7 || People.Count + _pool.Count >= Max))
            {
                int i = _rng.Next(_pool.Count);
                var p = _pool[i]; _pool.RemoveAt(i);
                return p;
            }
            if (Time.time < _createAt) return null;
            _createAt = Time.time + 0.35f;   // one new body at a time (instantiating a cast avatar costs a frame)
            return Create();
        }

        private string NextLook()
        {
            if (_archetypes == null)
            {
                var keys = new List<string>();
                var reg = CharacterRegistry.Instance;
                if (reg != null) foreach (var e in reg.archetypes) if (e != null && e.prefab != null && !string.IsNullOrEmpty(e.key)) keys.Add(e.key);
                // shuffled once, then dealt in turn: no two neighbours look alike
                for (int i = keys.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (keys[i], keys[j]) = (keys[j], keys[i]); }
                _archetypes = keys.ToArray();
            }
            return _archetypes.Length == 0 ? null : _archetypes[_nextLook++ % _archetypes.Length];
        }

        private Pedestrian Create()
        {
            try
            {
                string look = NextLook();
                var view = AvatarFactory.Create("pedestrian", null, true, look, transform);
                if (view == null) return null;
                view.cullDistance = DrawWithin;
                view.ambient = true;
                var ped = view.gameObject.AddComponent<Pedestrian>();   // before any Knockdown: it finds its owner on Awake
                ped.Init(this, view, look);
                view.gameObject.SetActive(false);
                return ped;
            }
            catch (Exception e)
            {
                Log.Warn("peds", "pedestrian avatar: " + e.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ the street around them

        /// <summary>Is the way across clear? Signals: the cars on that road have red. Zebra: no car about to arrive.</summary>
        public bool MayCross(PedestrianPaths.Crossing c, out bool safe)
        {
            var net = RoadNetwork.Active;
            bool carsClose = CarNear(c);
            if (c.signal && net != null && !string.IsNullOrEmpty(c.junction) && !string.IsNullOrEmpty(c.segment))
            {
                var state = net.Signal(c.junction, c.segment, Time.timeAsDouble);
                safe = state == SignalState.Red;
                return safe && !carsClose;
            }
            safe = !carsClose;
            return safe;
        }

        /// <summary>A traffic car moving within 18 m of the crossing (or on it).</summary>
        private static bool CarNear(PedestrianPaths.Crossing c)
        {
            var traffic = TrafficSystem.Current;
            if (traffic == null) return false;
            var mid = (c.a + c.b) * 0.5f;
            foreach (var car in traffic.Cars)
            {
                if (car == null) continue;
                var d = car.transform.position - mid; d.y = 0f;
                if (d.sqrMagnitude < 4.5f * 4.5f) return true;                    // on it, moving or not
                if (d.sqrMagnitude < 18f * 18f && car.Speed > 1.5f && Vector3.Dot(car.transform.forward, -d) > 0f) return true;
            }
            return false;
        }

        /// <summary>Unit direction from the road to <paramref name="p"/> (the shop side of a sidewalk); zero when no road is near.</summary>
        public Vector3 AwayFromRoad(Vector3 p)
        {
            var net = RoadNetwork.Active;
            if (net == null) return Vector3.zero;
            var lp = net.NearestLanePoint(p, 25f);
            if (!lp.valid) return Vector3.zero;
            var d = p - lp.position; d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.zero;
        }

        /// <summary>A straight walk from <paramref name="a"/> to <paramref name="b"/> stays off the carriageway (open areas and
        /// benches are reached directly, not along a path, so check the line against the lanes).</summary>
        public bool ClearOfRoad(Vector3 a, Vector3 b)
        {
            var net = RoadNetwork.Active;
            if (net == null) return true;
            float len = Vector3.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 3f));
            for (int i = 0; i <= steps; i++)
            {
                var lp = net.NearestLanePoint(Vector3.Lerp(a, b, i / (float)steps), 4f);
                if (lp.valid && lp.distance < RoadClearance) return false;
            }
            return true;
        }

        /// <summary>Metres from a lane centreline that count as the road (a lane is about 3.5 m wide).</summary>
        public const float RoadClearance = 2.6f;

        public float GroundY(Vector3 p, float fallback)
        {
            int mask = CharacterLayers.BodyMask & ~(1 << CharacterLayers.Vehicle) & ~(1 << CharacterLayers.VehicleFlight);
            if (Physics.Raycast(new Vector3(p.x, (float.IsNaN(fallback) ? p.y : fallback) + 2f, p.z), Vector3.down, out var hit, float.IsNaN(fallback) ? 400f : 4f, mask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return float.IsNaN(fallback) ? float.NaN : fallback;
        }

        private void RefreshBenches(Vector3 focus)
        {
            // keep the taken ones (someone is on them), rebuild the rest from the loaded cells' dressing
            var kept = _benches.FindAll(b => b.taken);
            _benches.Clear();
            _benches.AddRange(kept);
            foreach (var cell in FindObjectsByType<IslandInstances>(FindObjectsSortMode.None))
            {
                if (cell == null || cell.kit == null) continue;
                int proto = cell.kit.IndexOf("bench_city");
                if (proto < 0 || !cell.Groups.TryGetValue(proto, out var chunks)) continue;
                foreach (var ch in chunks)
                {
                    if ((ch.bounds.center - focus).sqrMagnitude > 140f * 140f) continue;
                    foreach (var m in ch.matrices)
                    {
                        if (m.m33 == 0f) continue;   // hidden
                        var pos = new Vector3(m.m03, m.m13, m.m23);
                        if ((pos - focus).sqrMagnitude > 120f * 120f) continue;
                        var fwd = new Vector3(m.m02, 0f, m.m22);
                        if (fwd.sqrMagnitude < 1e-6f) continue;
                        fwd.Normalize();
                        var side = new Vector3(fwd.z, 0f, -fwd.x);
                        foreach (float off in new[] { -0.45f, 0.45f })
                        {
                            var seat = pos + side * off + fwd * 0.12f;
                            bool dup = false;
                            foreach (var b in kept) if ((b.position - seat).sqrMagnitude < 0.04f) { dup = true; break; }
                            if (!dup) _benches.Add(new BenchSeat { position = seat, forward = fwd });
                        }
                    }
                }
            }
        }

        public bool TakeBench(Vector3 near, float within, out BenchSeat seat)
        {
            seat = null; float best = within * within;
            foreach (var b in _benches)
            {
                if (b.taken) continue;
                float d = (b.position - near).sqrMagnitude;
                if (d < best && ClearOfRoad(near, b.position)) { best = d; seat = b; }
            }
            if (seat != null) seat.taken = true;
            return seat != null;
        }

        public int Benches => _benches.Count;

        public int CountIn(Pedestrian.Mode mode)
        {
            int n = 0;
            foreach (var p in People) if (p != null && p.State == mode) n++;
            return n;
        }
    }
}
