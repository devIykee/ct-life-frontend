using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.World
{
    /// <summary>
    /// CT Island at runtime (Phase B): the terrain prefab (IslandBuilder) plus the island NavMesh, with every venue diorama
    /// streamed in and out by distance (async load + async instantiate, one at a time, so the frame never blocks). A streamed
    /// venue keeps its own geometry, colliders, spots and baked lightmaps (IslandLightmaps), but not its own sun, look, volume,
    /// backdrop or NavMesh: the island provides one light and one walkable surface. Far venues are the painted ground texture.
    /// </summary>
    public sealed class IslandWorld : MonoBehaviour
    {
        public const string VenueId = "ct_island", ResourcePath = "World/Island/CTIsland";

        public NavMeshData navigation;
        public List<IslandVenue> venues = new List<IslandVenue>();
        [Serializable] public sealed class District { public string id, name; public Vector2 center; public float half; }
        public List<District> districts = new List<District>();

        /// <summary>The district plot under a point (the countryside outside them).</summary>
        public string DistrictAt(Vector3 world)
        {
            foreach (var d in districts)
                if (Mathf.Abs(world.x - d.center.x) <= d.half && Mathf.Abs(world.z - d.center.y) <= d.half) return d.id;
            return "countryside";
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
        private CancellationTokenSource _life = new CancellationTokenSource();
        private float _nextPlan;

        public VenueSceneRoot Root { get; private set; }
        public IReadOnlyDictionary<string, VenueSceneRoot> Loaded => _loaded;
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
            var prefab = req.asset as GameObject ?? throw new InvalidOperationException("CT Island is not built (run IslandBuilder).");
            var scene = SceneManager.CreateScene("CTIsland_" + Time.frameCount);
            SceneManager.SetActiveScene(scene);
            var go = Object.Instantiate(prefab);
            go.name = "CTIsland";
            SceneManager.MoveGameObjectToScene(go, scene);
            var world = go.GetComponent<IslandWorld>();
            world._scene = scene;
            world.Root = go.GetComponent<VenueSceneRoot>();
            world.Root.Init(null);
            if (world.navigation != null) world._nav = NavMesh.AddNavMeshData(world.navigation);
            go.GetComponent<VenueContentLighting>()?.Apply();
            IslandLightmaps.Clear();
            return world;
        }

        /// <summary>The venue's diorama, loading it now if it is not streamed in yet.</summary>
        public async Task<VenueSceneRoot> Ensure(string venueId, CancellationToken owner)
        {
            if (_loaded.TryGetValue(venueId, out var r) && r != null) return r;
            var v = Find(venueId) ?? throw new InvalidOperationException(venueId + " is not on CT Island.");
            while (_loading.Contains(venueId)) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
            if (_loaded.TryGetValue(venueId, out r) && r != null) return r;
            return await LoadVenue(v, owner);
        }

        /// <summary>Call every frame with the player position: streams by distance (planned twice a second).</summary>
        public void Stream(Vector3 player)
        {
            if (Time.unscaledTime < _nextPlan) return;
            _nextPlan = Time.unscaledTime + 0.5f;
            var p = new Vector2(player.x, player.z);
            var loadedIds = new HashSet<string>(_loaded.Keys);
            foreach (var id in _loading) loadedIds.Add(id);
            var (load, unload) = IslandStreaming.Plan(p, venues, loadedIds);
            foreach (var id in unload) UnloadVenue(id);
            if (_loading.Count == 0 && load.Count > 0) _ = LoadVenue(load[0], _life.Token);
        }

        public IslandVenue ZoneAt(Vector3 world) => IslandStreaming.ZoneAt(new Vector2(world.x, world.z), venues);

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
                var op = Object.InstantiateAsync(entry.prefab, holder.transform, new Vector3(v.origin.x, 0f, v.origin.y), Quaternion.identity);
                while (!op.isDone) { owner.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(); }
                var root = op.Result[0];
                root.name = "Venue_" + v.venueId;
                Prepare(root);
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
        }

        private void UnloadVenue(string id)
        {
            if (!_loaded.TryGetValue(id, out var root)) return;
            _loaded.Remove(id);
            if (VenueSession.Current != null && VenueSession.Current.Venue == root) return;   // never pull the floor from the current zone
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
            IslandLightmaps.Clear();
            if (_nav.valid) NavMesh.RemoveNavMeshData(_nav);
            GetComponent<VenueContentLighting>()?.Restore();
            if (_scene.IsValid() && _scene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(_scene);
                while (op != null && !op.isDone) await Awaitable.NextFrameAsync();
            }
        }

        private void OnDestroy() { _life.Cancel(); if (_nav.valid) NavMesh.RemoveNavMeshData(_nav); }
    }
}
