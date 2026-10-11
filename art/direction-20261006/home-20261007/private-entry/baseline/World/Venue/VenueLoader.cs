using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
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
    /// 1) a shipped, asset-built diorama from VenueContentRegistry, instantiated into an additive scene;
    /// 2) a scene in Build Settings with that name (Phase 1–2 venues ship in the APK);
    /// 3) an Addressables scene with that key (later-phase venues stream in, Build Spec "Performance budgets");
    /// 4) a greybox generated from spots.json, only for venues whose authored content is not shipped yet.
    /// </summary>
    public sealed class VenueLoader
    {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        // Installed by the explicit review harness; never persists a catalog edit.
        public static Func<string, VenueSceneRoot> ReviewPrefabResolver { get; set; }
#endif
        private AsyncOperationHandle<SceneInstance> _addressableHandle;
        private Scene _scene;
        private Scene _returnScene;
        private NavMeshDataInstance _navMesh;

        public VenueSceneRoot Current { get; private set; }
        public string CurrentVenueId { get; private set; }

        private sealed class SceneReleaseException : AggregateException
        {
            public SceneReleaseException(IEnumerable<Exception> failures) : base("Venue scene release failed.", failures) { }
        }

        public async Task<VenueSceneRoot> Load(string venueId, CancellationToken owner = default)
        {
            owner.ThrowIfCancellationRequested();
            await Unload();
            owner.ThrowIfCancellationRequested();
            _returnScene = SceneManager.GetActiveScene();
            var reg = Services.Get<ConfigStore>().Registries;
            var def = reg.Venue(venueId);
            var spots = reg.SpotsFor(venueId);
            var key = def?.sceneKey;
            VenueSceneRoot root = null;

            // Local shipped content must not initialize Addressables or require a remote catalog/network.
            var catalog = Resources.Load<VenueContentRegistry>("World/VenueContentRegistry");
            var prefab = catalog != null ? catalog.Find(venueId) : null;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Flag("review") && ReviewPrefabResolver != null)
                prefab = ReviewPrefabResolver(venueId) ?? prefab;
#endif
            if (prefab != null)
            {
                _scene = SceneManager.CreateScene("Venue_" + venueId + "_" + Time.frameCount);
                SceneManager.SetActiveScene(_scene);
                root = UnityEngine.Object.Instantiate(prefab);
                root.name = "Venue_" + venueId;
                SceneManager.MoveGameObjectToScene(root.gameObject, _scene);
            }
            else if (!string.IsNullOrEmpty(key))
            {
                root = await TryLoadBuiltIn(key, owner);
                if (root == null) root = await TryLoadAddressable(key, owner);
            }

            owner.ThrowIfCancellationRequested();
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
                if (root.navigation != null)
                    _navMesh = NavMesh.AddNavMeshData(root.navigation, root.transform.position, root.transform.rotation);
                root.GetComponent<VenueContentLighting>()?.Apply();
            }

            if (string.IsNullOrEmpty(root.venueId)) root.venueId = venueId;
            if (string.IsNullOrEmpty(root.districtId)) root.districtId = def?.districtId;
            Current = root;
            CurrentVenueId = venueId;
            CameraRig.Ensure().ConfigureVenue(root);
            return root;
        }

        private async Task<VenueSceneRoot> TryLoadAddressable(string key, CancellationToken owner)
        {
            try
            {
                var locHandle = Addressables.LoadResourceLocationsAsync(key, typeof(SceneInstance));
                bool exists;
                try
                {
                    var locs = await locHandle.Task;
                    exists = locs != null && locs.Count > 0;
                }
                finally
                {
                    if (locHandle.IsValid()) Addressables.Release(locHandle);
                }
                owner.ThrowIfCancellationRequested();
                if (!exists) return null;

                _addressableHandle = Addressables.LoadSceneAsync(key, LoadSceneMode.Additive);
                var inst = await _addressableHandle.Task;
                _scene = inst.Scene;
                owner.ThrowIfCancellationRequested();
                var root = FindRoot(_scene);
                if (root == null) await ReleaseLoadedScene();
                else SceneManager.SetActiveScene(_scene);
                return root;
            }
            catch (SceneReleaseException) { throw; } // Failed cleanup is not a recoverable load miss.
            catch (OperationCanceledException) when (owner.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                Log.Warn("venue", $"Addressables load of '{key}' failed: {e.Message}");
                await ReleaseLoadedScene();
                return null;
            }
        }

        private async Task<VenueSceneRoot> TryLoadBuiltIn(string sceneName, CancellationToken owner)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName)) return null;
            try
            {
                var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (op == null) return null;
                await Awaitable.FromAsyncOperation(op);
                _scene = SceneManager.GetSceneByName(sceneName);
                if (!_scene.IsValid()) _scene = SceneManager.GetSceneByPath(sceneName);
                if (!_scene.IsValid()) _scene = SceneManager.GetSceneByName(System.IO.Path.GetFileNameWithoutExtension(sceneName));
                owner.ThrowIfCancellationRequested();
                var root = FindRoot(_scene);
                if (root == null) await ReleaseLoadedScene();
                else SceneManager.SetActiveScene(_scene);
                return root;
            }
            catch (SceneReleaseException) { throw; } // Failed cleanup is not a recoverable load miss.
            catch (OperationCanceledException) when (owner.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                Log.Warn("venue", $"Built-in load of '{sceneName}' failed: {e.Message}");
                await ReleaseLoadedScene();
                return null;
            }
        }

        private static VenueSceneRoot FindRoot(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
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
            var failures = new List<Exception>();
            void Attempt(Action release) { try { release(); } catch (Exception error) { failures.Add(error); } }
            // Clear ownership before awaiting; failed releases are reported, never retried against a
            // successor scene by a delayed call on this loader.
            var current = Current; Current = null; CurrentVenueId = null;
            if (current != null)
            {
                Attempt(() => current.GetComponent<VenueLightmaps>()?.Restore());
                Attempt(() => current.GetComponent<VenueContentLighting>()?.Restore());
            }
            Attempt(() => { if (CameraRig.Instance != null) CameraRig.Instance.Target = null; });
            var navigation = _navMesh; _navMesh = default;
            if (navigation.valid) Attempt(() => NavMesh.RemoveNavMeshData(navigation));
            try { await ReleaseLoadedScene(); } catch (Exception error) { failures.Add(error); }
            _returnScene = default;
            if (failures.Count > 0) throw new AggregateException("Venue scene cleanup failed.", failures);
        }

        private async Task ReleaseLoadedScene()
        {
            var scene = _scene; _scene = default;
            var addressable = _addressableHandle; _addressableHandle = default;
            var failures = new List<Exception>();
            try
            {
                if (_returnScene.IsValid() && _returnScene.isLoaded && _returnScene != scene)
                    SceneManager.SetActiveScene(_returnScene);
            }
            catch (Exception error) { failures.Add(error); }

            try
            {
                if (addressable.IsValid())
                {
                    // A failed scene-load handle has no scene to unload, but owns a reference.
                    if (addressable.Status == AsyncOperationStatus.Succeeded)
                        await Addressables.UnloadSceneAsync(addressable).Task;
                    else Addressables.Release(addressable);
                }
                else if (scene.IsValid() && scene.isLoaded)
                {
                    var unload = SceneManager.UnloadSceneAsync(scene);
                    if (unload != null) await Awaitable.FromAsyncOperation(unload);
                }
            }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new SceneReleaseException(failures);
        }
    }
}
