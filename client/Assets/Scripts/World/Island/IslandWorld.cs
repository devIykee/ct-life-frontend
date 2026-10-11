using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World.Roads;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.World
{
    /// <summary>
    /// CT Island v2 at runtime (World lane, 2026-10-17). The base prefab (always loaded) holds the far terrain, the sea,
    /// the lighting, the road network and the pedestrian NavMesh. The near world streams in 256 m cells, each a prefab under
    /// Resources/World/Island/Cells: terrain chunks with colliders, road meshes, bridges, props, markers. Cells are
    /// loaded by distance plus Feel's velocity prefetch (<see cref="IslandStreamer.Prefetch"/>), one async load at a
    /// time, so no frame blocks. Venue dioramas stream as before, but they now sit on real terrain heights and face their
    /// streets (yaw). A streamed venue keeps its own geometry, colliders, spots and baked lightmaps, but not its own sun,
    /// look, volume, backdrop or NavMesh: the island provides one light and one walkable surface.
    /// </summary>
    public sealed class IslandWorld : MonoBehaviour
    {
        public const string VenueId = "ct_island", ResourcePath = "World/Island/CTIsland", CellFolder = "World/Island/Cells";

        /// <summary>The loaded island (null when none).</summary>
        public static IslandWorld Current { get; private set; }

        public NavMeshData navigation;
        public RoadNetwork roads;
        public List<IslandVenue> venues = new List<IslandVenue>();
        [Serializable] public sealed class District { public string id, name; public Vector2 center; public float half; public Vector2[] polygon; }
        public List<District> districts = new List<District>();
        [Serializable] public sealed class Cell { public string id; public Rect rect; public GameObject far; }
        public List<Cell> cells = new List<Cell>();
        public float seaLevel = -6f, flightCeiling = 214f;
        public float cellLoadRadius = 420f, cellUnloadRadius = 560f;
        [Header("Heightfield (the ground is generated from it on load)")]
        public TextAsset heightBytes;
        public TextAsset holeBytes;
        public int hfSamples = 1025;
        public float hfCell = 2f, hfMinX = -1024f, hfMinZ = -1024f, hfMin = -40f, hfMax = 200f;

        /// <summary>The ground heightfield (loaded with the island).</summary>
        public IslandHeightfield Heightfield { get; private set; }

        public IslandHeightfield LoadHeightfield()
        {
            if (Heightfield == null && heightBytes != null)
                Heightfield = IslandHeightfield.FromBytes(heightBytes.bytes, holeBytes != null ? holeBytes.bytes : null, hfSamples, hfCell, hfMinX, hfMinZ, hfMin, hfMax, seaLevel, flightCeiling);
            return Heightfield;
        }

        /// <summary>The district polygon under a point (the countryside outside them).</summary>
        public string DistrictAt(Vector3 world)
        {
            var p = new Vector2(world.x, world.z);
            foreach (var d in districts)
            {
                if (d.polygon != null && d.polygon.Length >= 3) { if (Inside(p, d.polygon)) return d.id; }
                else if (Mathf.Abs(world.x - d.center.x) <= d.half && Mathf.Abs(world.z - d.center.y) <= d.half) return d.id;
            }
            return "countryside";
        }

        public static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y + 1e-12f) + a.x) inside = !inside;
            }
            return inside;
        }

        public string DistrictName(string id)
        {
            foreach (var d in districts) if (d.id == id) return d.name;
            return id == "countryside" ? "CT Island" : id;
        }

        private NavMeshDataInstance _nav;
        private Scene _scene;
        private readonly Dictionary<string, VenueSceneRoot> _loaded = new Dictionary<string, VenueSceneRoot>();
        private readonly HashSet<string> _loading = new HashSet<string>();
        private readonly Dictionary<int, GameObject> _cells = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _cellPrefabs = new Dictionary<int, GameObject>();
        private readonly HashSet<int> _cellLoading = new HashSet<int>();
        private readonly List<(Vector2 p, float until)> _prefetch = new List<(Vector2, float)>();
        private List<Rect> _cellRects;
        private CancellationTokenSource _life = new CancellationTokenSource();
        private float _nextPlan, _nextCellPlan;
        private Vector3 _lastPlayer;

        public VenueSceneRoot Root { get; private set; }
        public IReadOnlyDictionary<string, VenueSceneRoot> Loaded => _loaded;
        public int LoadedCellCount => _cells.Count;
        public static bool Available => Resources.Load<GameObject>(ResourcePath) != null;

        /// <summary>Is <paramref name="venueId"/> a walkable venue on the built island? (reads the prefab's placement list)</summary>
        public static bool Places(string venueId)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            var w = prefab != null ? prefab.GetComponent<IslandWorld>() : null;
            if (w == null || string.IsNullOrEmpty(venueId)) return false;
            foreach (var v in w.venues) if (v.venueId == venueId) return true;
            return false;
        }

        public IslandVenue Find(string venueId)
        {
            foreach (var v in venues) if (v.venueId == venueId) return v;
            return null;
        }

        public static async Task<IslandWorld> Load(CancellationToken owner)
        {
            var req = Resources.LoadAsync<GameObject>(ResourcePath);
            while (!req.isDone) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
            var prefab = req.asset as GameObject ?? throw new InvalidOperationException("CT Island is not built (run IslandV2Builder).");
            var scene = SceneManager.CreateScene("CTIsland_" + Time.frameCount);
            SceneManager.SetActiveScene(scene);
            var go = Object.Instantiate(prefab);
            go.name = "CTIsland";
            SceneManager.MoveGameObjectToScene(go, scene);
            var world = go.GetComponent<IslandWorld>();
            world._scene = scene;
            world.Root = go.GetComponent<VenueSceneRoot>();
            world.Root.Init(null);
            var hf = world.LoadHeightfield();
            if (hf != null) go.GetComponent<IslandTerrainFar>()?.Build(hf);
            if (world.navigation != null) world._nav = NavMesh.AddNavMeshData(world.navigation);
            go.GetComponent<VenueContentLighting>()?.Apply();
            IslandLightmaps.Clear();
            Current = world;
            RoadNetwork.Active = world.roads;
            return world;
        }

        /// <summary>The venue's diorama (and the ground around it), loading them now if they are not streamed in yet.</summary>
        public async Task<VenueSceneRoot> Ensure(string venueId, CancellationToken owner)
        {
            var v = Find(venueId) ?? throw new InvalidOperationException(venueId + " is not on CT Island.");
            await EnsureCells(v.Position, 160f, owner);
            if (_loaded.TryGetValue(venueId, out var r) && r != null) return r;
            while (_loading.Contains(venueId)) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
            if (_loaded.TryGetValue(venueId, out r) && r != null) return r;
            return await LoadVenue(v, owner);
        }

        /// <summary>Loads every cell within <paramref name="radius"/> of a point (fast travel, spawns).</summary>
        public async Task EnsureCells(Vector3 world, float radius, CancellationToken owner)
        {
            var p = new Vector2(world.x, world.z);
            var rects = CellRects();
            var need = new List<int>();
            for (int i = 0; i < rects.Count; i++) if (IslandStreaming.DistanceToRect(p, rects[i]) <= radius) need.Add(i);
            need.Sort((a, b) => IslandStreaming.DistanceToRect(p, rects[a]).CompareTo(IslandStreaming.DistanceToRect(p, rects[b])));
            foreach (var i in need)
            {
                while (_cellLoading.Contains(i)) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
                if (!_cells.ContainsKey(i)) await LoadCell(i, owner);
            }
        }

        /// <summary>Is the ground under a point streamed in (its cell loaded)? Cells outside the grid count as ready.</summary>
        public bool IsReady(Vector3 world)
        {
            var rects = CellRects();
            var p = new Vector2(world.x, world.z);
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return _cells.ContainsKey(i);
            return true;
        }

        private List<Rect> CellRects()
        {
            if (_cellRects != null) return _cellRects;
            _cellRects = new List<Rect>(cells.Count);
            foreach (var c in cells) _cellRects.Add(c.rect);
            return _cellRects;
        }

        /// <summary>Content needed soon: predicted positions are planned like the player's own for two seconds.</summary>
        public void Prefetch(Vector3 position, Vector3 velocity, float horizonSeconds)
        {
            float until = Time.unscaledTime + 2f;
            _prefetch.Add((new Vector2(position.x, position.z), until));
            foreach (var q in IslandStreaming.PredictPath(position, velocity, Mathf.Clamp(horizonSeconds, 0f, 12f), 1f)) _prefetch.Add((q, until));
            if (_prefetch.Count > 64) _prefetch.RemoveRange(0, _prefetch.Count - 64);
        }

        private List<Vector2> PrefetchPoints()
        {
            float now = Time.unscaledTime;
            _prefetch.RemoveAll(e => e.until < now);
            var o = new List<Vector2>(_prefetch.Count);
            foreach (var e in _prefetch) o.Add(e.p);
            return o;
        }

        /// <summary>Call every frame with the player position: streams cells and venues (planned a few times a second).</summary>
        public void Stream(Vector3 player)
        {
            _lastPlayer = player;
            var p = new Vector2(player.x, player.z);
            if (Time.unscaledTime >= _nextCellPlan)
            {
                _nextCellPlan = Time.unscaledTime + 0.25f;
                var loadedCells = new HashSet<int>(_cells.Keys);
                foreach (var i in _cellLoading) loadedCells.Add(i);
                var (load, unload) = IslandStreaming.PlanCells(p, CellRects(), loadedCells, cellLoadRadius, cellUnloadRadius, PrefetchPoints());
                foreach (var i in unload) UnloadCell(i);
                if (_cellLoading.Count == 0 && load.Count > 0) _ = LoadCell(load[0], _life.Token);
            }
            if (Time.unscaledTime < _nextPlan) return;
            _nextPlan = Time.unscaledTime + 0.5f;
            var loadedIds = new HashSet<string>(_loaded.Keys);
            foreach (var id in _loading) loadedIds.Add(id);
            var (vload, vunload) = IslandStreaming.Plan(p, venues, loadedIds, extra: PrefetchPoints());
            foreach (var id in vunload) UnloadVenue(id);
            if (_loading.Count == 0 && vload.Count > 0) _ = LoadVenue(vload[0], _life.Token);
        }

        public IslandVenue ZoneAt(Vector3 world) => IslandStreaming.ZoneAt(new Vector2(world.x, world.z), venues);

        // ------------------------------------------------------------------ cells

        private async Task LoadCell(int i, CancellationToken owner)
        {
            if (i < 0 || i >= cells.Count || _cells.ContainsKey(i) || !_cellLoading.Add(i)) return;
            try
            {
                if (!_cellPrefabs.TryGetValue(i, out var prefab) || prefab == null)
                {
                    var req = Resources.LoadAsync<GameObject>(CellFolder + "/" + cells[i].id);
                    while (!req.isDone) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
                    prefab = req.asset as GameObject;
                    if (prefab == null) { Log.Warn("island", "missing cell " + cells[i].id); return; }
                    _cellPrefabs[i] = prefab;
                }
                var op = Object.InstantiateAsync(prefab, transform);
                while (!op.isDone) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
                var go = op.Result[0];
                go.name = cells[i].id;
                var ground = go.GetComponentInChildren<IslandTerrainCell>(true);
                if (ground != null) await ground.Build(LoadHeightfield(), owner);
                if (go == null) return;
                _cells[i] = go;
                if (cells[i].far != null) cells[i].far.SetActive(false);
            }
            finally { _cellLoading.Remove(i); }
        }

        private void UnloadCell(int i)
        {
            if (!_cells.TryGetValue(i, out var go)) return;
            // never pull the ground from under the player or the venue in use
            if (cells[i].rect.Contains(new Vector2(_lastPlayer.x, _lastPlayer.z))) return;
            _cells.Remove(i);
            if (cells[i].far != null) cells[i].far.SetActive(true);
            if (go != null) Destroy(go);
        }

        // ------------------------------------------------------------------ venues

        private async Task<VenueSceneRoot> LoadVenue(IslandVenue v, CancellationToken owner)
        {
            _loading.Add(v.venueId);
            try
            {
                var req = Resources.LoadAsync<VenueContentEntry>(VenueContent.EntryFolder + "/" + VenueContent.EntryName(v.venueId, false));
                while (!req.isDone) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
                var entry = req.asset as VenueContentEntry;
                if (entry == null || entry.prefab == null) throw new InvalidOperationException("No shipped content for " + v.venueId);
                var holder = new GameObject("Venue_" + v.venueId);
                holder.SetActive(false);
                holder.transform.SetParent(transform, false);
                var op = Object.InstantiateAsync(entry.prefab, holder.transform, v.Position, v.Rotation);
                while (!op.isDone) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
                var root = op.Result[0];
                root.name = "Venue_" + v.venueId;
                Prepare(root);
                if (v.HasLocal && v.localFrame.width > 0f) IslandVenueTrim.Trim(root.transform, v.localFrame, v.shellHeight > 0f ? 0.5f : 4f, v.shellHeight > 0f ? v.shellHeight + 0.4f : 400f);   // inside its building: nothing pokes out
                var reg = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Registries : null;
                root.Init(reg?.SpotsFor(v.venueId));
                if (string.IsNullOrEmpty(root.venueId)) root.venueId = v.venueId;
                holder.SetActive(true);
                IslandLightmaps.Add(root.GetComponent<VenueLightmaps>());
                _loaded[v.venueId] = root;
                return root;
            }
            finally { _loading.Remove(v.venueId); }
        }

        /// <summary>Layer for venue dioramas on the island (contract "Interior").</summary>
        public const int InteriorLayer = 17;

        /// <summary>Strip what the island provides once for all: sun, look, volume, backdrop, own NavMesh, self-applied lightmaps.</summary>
        public static void Prepare(VenueSceneRoot root)
        {
            var lm = root.GetComponent<VenueLightmaps>(); if (lm != null) lm.enabled = false;
            foreach (var c in root.GetComponentsInChildren<VenueLook>(true)) Object.Destroy(c);
            foreach (var c in root.GetComponentsInChildren<VenueContentLighting>(true)) Object.Destroy(c);
            foreach (var c in root.GetComponentsInChildren<Volume>(true)) Object.Destroy(c);
            foreach (var l in root.GetComponentsInChildren<Light>(true)) if (l.type == LightType.Directional) l.enabled = false;
            var backdrop = root.transform.Find("Backdrop");
            if (backdrop != null) backdrop.gameObject.SetActive(false);
            root.navigation = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer == 0) t.gameObject.layer = InteriorLayer;
        }

        private void UnloadVenue(string id)
        {
            if (!_loaded.TryGetValue(id, out var root)) return;
            if (VenueSession.Current != null && VenueSession.Current.Venue == root) return;   // never pull the floor from the current zone
            _loaded.Remove(id);
            if (root != null)
            {
                IslandLightmaps.Remove(root.GetComponent<VenueLightmaps>());
                Destroy(root.transform.parent != null ? root.transform.parent.gameObject : root.gameObject);
            }
        }

        public async Task Unload()
        {
            _life.Cancel();
            foreach (var r in _loaded.Values) if (r != null) Destroy(r.transform.parent != null ? r.transform.parent.gameObject : r.gameObject);
            _loaded.Clear();
            foreach (var c in _cells.Values) if (c != null) Destroy(c);
            _cells.Clear();
            IslandLightmaps.Clear();
            if (_nav.valid) NavMesh.RemoveNavMeshData(_nav);
            GetComponent<VenueContentLighting>()?.Restore();
            if (ReferenceEquals(Current, this)) { Current = null; RoadNetwork.Active = null; }
            if (_scene.IsValid() && _scene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(_scene);
                while (op != null && !op.isDone) await Awaitable.NextFrameAsync();
            }
        }

        private void OnDestroy()
        {
            _life.Cancel();
            if (_nav.valid) NavMesh.RemoveNavMeshData(_nav);
            if (ReferenceEquals(Current, this)) { Current = null; RoadNetwork.Active = null; }
        }
    }

    /// <summary>
    /// Streaming entry points for other lanes (world-v2 contract). Feel calls <see cref="Prefetch"/> from vehicles and
    /// flight so cells and venues are in before arrival; loading stays budgeted (one async cell load at a time).
    /// </summary>
    public static class IslandStreamer
    {
        public static void Prefetch(Vector3 position, Vector3 velocity, float horizonSeconds) =>
            IslandWorld.Current?.Prefetch(position, velocity, horizonSeconds);

        /// <summary>Is the ground at a point loaded (colliders present)? True when no island is loaded.</summary>
        public static bool IsReady(Vector3 position) => IslandWorld.Current == null || IslandWorld.Current.IsReady(position);

        /// <summary>Loads the cells around a point and returns when they are in (teleports, fast travel, respawn).</summary>
        public static Task EnsureLoaded(Vector3 position, float radius = 160f, CancellationToken ct = default) =>
            IslandWorld.Current != null ? IslandWorld.Current.EnsureCells(position, radius, ct) : Task.CompletedTask;

        public static float SeaLevel => IslandWorld.Current != null ? IslandWorld.Current.seaLevel : -6f;

        /// <summary>Ground height (terrain, not roads or buildings) at island x/z; NaN when no island is loaded.</summary>
        public static float GroundHeight(float x, float z)
        {
            var h = IslandWorld.Current != null ? IslandWorld.Current.Heightfield : null;
            return h != null ? h.Sample(x, z) : float.NaN;
        }
        public static float FlightCeiling => IslandWorld.Current != null ? IslandWorld.Current.flightCeiling : 214f;
    }
}
