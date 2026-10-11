using System;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Integration helpers for the lead. Nothing here runs as part of Build/Bake/Verify.
    ///  * RegisterBuiltVenues: adds every built, baked venue/home prefab to the venue content registry
    ///    (Assets/Venues/VenueContentRegistry.asset, falling back to Assets/Resources/World/VenueContentRegistry.asset)
    ///    and, if the project has CTLife.EditorTools.VenueContentCatalog.Sync, regenerates the runtime catalog.
    ///    Runtime gates still apply: VenueContent/VenueContentRegistry only enable the parents_basement home, and the
    ///    map lists only venues present in the backend venues registry.
    ///  * MarkAddressable: puts each venue prefab in an Addressables group "Venues" with address "venue/{id}"
    ///    (for later-phase streaming; Build Spec "Performance budgets").
    ///  * WriteManifest: docs/progress/venues-manifest.json (ids, prefab paths/GUIDs, hooks, budgets) from the receipts.
    /// </summary>
    public static class VenueIntegration
    {
        private static readonly string[] RegistryPaths = { "Assets/Venues/VenueContentRegistry.asset", "Assets/Resources/World/VenueContentRegistry.asset" };

        [MenuItem("CT Life/Venues/Integration/Register built venues in content registry")]
        public static void RegisterBuiltVenues()
        {
            var path = RegistryPaths.FirstOrDefault(p => AssetDatabase.LoadAssetAtPath<VenueContentRegistry>(p) != null)
                       ?? throw new FileNotFoundException("No VenueContentRegistry asset at " + string.Join(" or ", RegistryPaths));
            var registry = AssetDatabase.LoadAssetAtPath<VenueContentRegistry>(path);
            int added = 0;
            foreach (var spec in VenueCatalog.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath)?.GetComponent<VenueSceneRoot>();
                if (prefab == null || prefab.GetComponent<VenueLightmaps>() == null) { Debug.LogWarning("CTLIFE [venues] not built/baked: " + spec.id); continue; }
                if (spec.IsHome)
                {
                    var e = registry.homes.FirstOrDefault(h => h.homeId == spec.homeId);
                    if (e == null) registry.homes.Add(e = new VenueContentRegistry.HomeEntry { homeId = spec.homeId });
                    e.prefab = prefab;
                }
                else
                {
                    var e = registry.venues.FirstOrDefault(v => v.venueId == spec.venueId);
                    if (e == null) registry.venues.Add(e = new VenueContentRegistry.Entry { venueId = spec.venueId });
                    e.prefab = prefab;
                }
                added++;
            }
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssetIfDirty(registry);
            var sync = Type.GetType("CTLife.EditorTools.VenueContentCatalog, CTLife.Editor")?.GetMethod("Sync", Type.EmptyTypes);
            sync?.Invoke(null, null);
            Debug.Log($"CTLIFE [venues] registered {added} venue/home prefabs in {path}" + (sync != null ? "; runtime catalog synced." : "."));
        }

        [MenuItem("CT Life/Venues/Integration/Mark venue prefabs addressable")]
        public static void MarkAddressable()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup("Venues") ?? settings.CreateGroup("Venues", false, false, true, settings.DefaultGroup.Schemas);
            foreach (var spec in VenueCatalog.All)
            {
                var guid = AssetDatabase.AssetPathToGUID(spec.PrefabPath);
                if (string.IsNullOrEmpty(guid)) continue;
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = "venue/" + spec.id;
            }
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);
            AssetDatabase.SaveAssets();
            Debug.Log("CTLIFE [venues] venue prefabs marked addressable in group 'Venues' (address venue/{id}).");
        }

        [MenuItem("CT Life/Venues/Integration/Write venues manifest")]
        public static void WriteManifest()
        {
            var receipts = Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts");
            var list = new JArray();
            foreach (var spec in VenueCatalog.All)
            {
                var b = Read(Path.Combine(receipts, spec.id + "-build.json"));
                var v = Read(Path.Combine(receipts, spec.id + "-verify.json"));
                list.Add(new JObject
                {
                    ["id"] = spec.id, ["venueId"] = spec.venueId, ["homeId"] = spec.homeId, ["title"] = spec.title, ["district"] = spec.district,
                    ["prefab"] = spec.PrefabPath, ["prefabGuid"] = AssetDatabase.AssetPathToGUID(spec.PrefabPath), ["identity"] = spec.identity,
                    ["spots"] = v?["spots"], ["hooks"] = b?["hooks"], ["baked"] = v?["baked"], ["lightmaps"] = v?["lightmaps"],
                    ["maxEstimatedDraws"] = v?["maxEstimatedDraws"], ["maxEstimatedTriangles"] = v?["maxEstimatedTriangles"],
                    ["wholeVenueLod0Triangles"] = v?["wholeVenueLod0Triangles"], ["staticRenderers"] = b?["staticRenderers"],
                });
            }
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/progress/venues-manifest.json"));
            File.WriteAllText(path, new JObject { ["generated"] = DateTime.UtcNow.ToString("o"), ["venues"] = list }.ToString() + "\n");
            Debug.Log("CTLIFE [venues] wrote " + path);
        }

        private static JObject Read(string p) => File.Exists(p) ? JObject.Parse(File.ReadAllText(p)) : null;
    }
}
