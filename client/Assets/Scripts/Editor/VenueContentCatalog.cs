using System.Collections.Generic;
using System.IO;
using CTLife.World;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Generates the runtime venue catalog from the authoring registry:
    /// `Assets/Venues/VenueContentRegistry.asset` (not shipped by itself) →
    /// `Assets/Resources/World/VenueContent/{venue|home}_{id}.asset` (one prefab each) and
    /// `Assets/Resources/World/VenueContentIndex.asset` (ids only). Runs before every build and from the menu.
    /// Batch: Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.VenueContentCatalog.SyncBatch
    /// </summary>
    public static class VenueContentCatalog
    {
        public const string RegistryPath = "Assets/Venues/VenueContentRegistry.asset";
        private const string ResourcesRoot = "Assets/Resources/";

        [MenuItem("CT Life/Venues/Sync runtime catalog")]
        public static void Sync()
        {
            var registry = AssetDatabase.LoadAssetAtPath<VenueContentRegistry>(RegistryPath);
            if (registry == null) throw new FileNotFoundException("Venue authoring registry missing", RegistryPath);
            string folder = ResourcesRoot + VenueContent.EntryFolder;
            Directory.CreateDirectory(folder);
            var wanted = new HashSet<string>();
            var index = LoadOrCreate<VenueContentIndex>(ResourcesRoot + VenueContent.IndexPath + ".asset");
            index.venueIds.Clear();
            index.playableHomeIds.Clear();
            foreach (var v in registry.venues)
            {
                if (v == null || string.IsNullOrEmpty(v.venueId) || v.prefab == null) continue;
                WriteEntry(folder, v.venueId, false, v.prefab, wanted);
                index.venueIds.Add(v.venueId);
            }
            foreach (var h in registry.homes)
            {
                if (h == null || string.IsNullOrEmpty(h.homeId) || h.prefab == null) continue;
                WriteEntry(folder, h.homeId, true, h.prefab, wanted);
                if (IsPlayableHome(h.prefab)) index.playableHomeIds.Add(h.homeId);
                else Debug.LogWarning($"CTLIFE [venue-catalog] home {h.homeId} has no navigation or baked lightmaps; not playable");
            }
            foreach (var path in Directory.GetFiles(folder, "*.asset"))
                if (!wanted.Contains(Path.GetFileNameWithoutExtension(path))) AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
            EditorUtility.SetDirty(index);
            AssetDatabase.SaveAssets();
            Debug.Log($"CTLIFE [venue-catalog] {index.venueIds.Count} venues ({string.Join(", ", index.venueIds)}), " +
                      $"{index.playableHomeIds.Count} playable homes");
        }

        public static void SyncBatch()
        {
            int code = 0;
            try { Sync(); }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static bool IsPlayableHome(VenueSceneRoot prefab)
        {
            var maps = prefab.GetComponent<VenueLightmaps>();
            return prefab.venueId == "home" && prefab.navigation != null && maps != null && maps.colors != null && maps.colors.Length > 0 &&
                   maps.bindings != null && maps.bindings.Length > 0;
        }

        private static void WriteEntry(string folder, string id, bool home, VenueSceneRoot prefab, HashSet<string> wanted)
        {
            string name = VenueContent.EntryName(id, home);
            wanted.Add(name);
            var entry = LoadOrCreate<VenueContentEntry>($"{folder}/{name}.asset");
            entry.contentId = id;
            entry.isHome = home;
            entry.prefab = prefab;
            EditorUtility.SetDirty(entry);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
