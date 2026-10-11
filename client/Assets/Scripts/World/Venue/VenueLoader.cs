using System;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AI;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace CTLife.World
{
    /// <summary>
    /// Loads one venue diorama by venueId (Build Spec "The world": each venue is one scene, loaded when you enter it).
    /// Resolution order for venues.json `sceneKey`:
    /// 1) an Addressables scene with that key (later-phase venues stream in, Build Spec "Performance budgets");
    /// 2) a scene in Build Settings with that name (Phase 1–2 venues ship in the APK);
    /// 3) a greybox generated from spots.json, so every venue is playable before its art lands.
    /// </summary>
    public sealed class VenueLoader
    {
        private AsyncOperationHandle<SceneInstance> _addressableHandle;
        private Scene _scene;
        private NavMeshDataInstance _navMesh;

        public VenueSceneRoot Current { get; private set; }
        public string CurrentVenueId { get; private set; }

        public async Task<VenueSceneRoot> Load(string venueId)
        {
            await Unload();
            var reg = Services.Get<ConfigStore>().Registries;
            var def = reg.Venue(venueId);
            var spots = reg.SpotsFor(venueId);
            var key = def?.sceneKey;
            VenueSceneRoot root = null;

            if (!string.IsNullOrEmpty(key))
            {
                root = await TryLoadAddressable(key);
                if (root == null) root = await TryLoadBuiltIn(key);
            }

            if (root == null)
            {
                _scene = SceneManager.CreateScene("Greybox_" + venueId + "_" + Time.frameCount);
                SceneManager.SetActiveScene(_scene);
                root = GreyboxVenueBuilder.Build(venueId, spots, out _navMesh);
                SceneManager.MoveGameObjectToScene(root.gameObject, _scene);
            }
            else
            {
                root.Init(spots);
            }

            if (string.IsNullOrEmpty(root.venueId)) root.venueId = venueId;
            if (string.IsNullOrEmpty(root.districtId)) root.districtId = def?.districtId;
            Current = root;
            CurrentVenueId = venueId;
            return root;
        }

        private async Task<VenueSceneRoot> TryLoadAddressable(string key)
        {
            try
            {
                var locHandle = Addressables.LoadResourceLocationsAsync(key, typeof(SceneInstance));
                var locs = await locHandle.Task;
                bool exists = locs != null && locs.Count > 0;
                Addressables.Release(locHandle);
                if (!exists) return null;

                _addressableHandle = Addressables.LoadSceneAsync(key, LoadSceneMode.Additive);
                var inst = await _addressableHandle.Task;
                _scene = inst.Scene;
                SceneManager.SetActiveScene(_scene);
                return FindRoot(_scene);
            }
            catch (Exception e)
            {
                Log.Warn("venue", $"Addressables load of '{key}' failed: {e.Message}");
                return null;
            }
        }

        private async Task<VenueSceneRoot> TryLoadBuiltIn(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName)) return null;
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            await Awaitable.FromAsyncOperation(op);
            _scene = SceneManager.GetSceneByName(sceneName);
            SceneManager.SetActiveScene(_scene);
            return FindRoot(_scene);
        }

        private static VenueSceneRoot FindRoot(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                var r = go.GetComponentInChildren<VenueSceneRoot>(true);
                if (r != null) return r;
            }
            Log.Warn("venue", $"Scene {scene.name} has no VenueSceneRoot; add one to the scene root");
            return null;
        }

        public async Task Unload()
        {
            Current = null;
            CurrentVenueId = null;
            if (_navMesh.valid) NavMesh.RemoveNavMeshData(_navMesh);
            _navMesh = default;

            if (_addressableHandle.IsValid())
            {
                await Addressables.UnloadSceneAsync(_addressableHandle).Task;
                _addressableHandle = default;
            }
            else if (_scene.IsValid() && _scene.isLoaded)
            {
                await Awaitable.FromAsyncOperation(SceneManager.UnloadSceneAsync(_scene));
            }
            _scene = default;
        }
    }
}
