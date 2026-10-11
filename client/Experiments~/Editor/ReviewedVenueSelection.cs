using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>Selects the rendered street for local device validation; no build or publication.</summary>
    public static class ReviewedVenueSelection
    {
        private const string Source = "Assets/Art/Candidates/CityDirection20261006B2Lamp/TimelineStreetB2Lamp.prefab";
        private const string Destination = "Assets/Art/Generated/Venues/TimelineStreet20261006.prefab";
        private const string Registry = "Assets/Resources/World/VenueContentRegistry.asset";

        [MenuItem("CT Life/Review/Select reviewed Timeline for device")]
        public static void SelectTimeline()
        {
            var sourceHash = Hash(Source);
            var receipt = JObject.Parse(File.ReadAllText(Path.GetDirectoryName(Source) + "/BakeReceipt.json"));
            if ((string)receipt["prefab_sha256"] != sourceHash)
                throw new InvalidDataException("Reviewed street differs from its bake receipt.");
            var stage = JObject.Parse(File.ReadAllText(Path.GetDirectoryName(Source) + "/StageReceipt.json"));
            var paths = (JArray)stage["navigation"];
            if (paths == null || paths.Count != 15 || paths.Any(p => (string)p["path"] != "PathComplete"))
                throw new InvalidDataException("Reviewed street needs complete authored-anchor navigation.");
            if (File.Exists(Destination)) throw new IOException("Selection output already exists; inspect it before replacing it.");
            var registry = AssetDatabase.LoadAssetAtPath<VenueContentRegistry>(Registry);
            if (registry == null) throw new InvalidDataException("Venue registry is missing.");
            var entry = registry.venues.Single(e => e.venueId == "timeline_square");
            string previous = AssetDatabase.GetAssetPath(entry.prefab);
            string backup = Path.Combine(Path.GetTempPath(), "ct-reviewed-timeline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            File.Copy(Registry, Path.Combine(backup, "VenueContentRegistry.asset"));
            File.Copy(Registry + ".meta", Path.Combine(backup, "VenueContentRegistry.asset.meta"));
            if (!AssetDatabase.CopyAsset(Source, Destination)) throw new IOException("Could not copy reviewed Timeline.");
            var root = PrefabUtility.LoadPrefabContents(Destination);
            try
            {
                root.name = "Timeline Square";
                var venue = root.GetComponent<VenueSceneRoot>();
                Validate(venue);
                // The same close landscape framing used in the completed native review.
                venue.cameraDistance = 10.8f;
                venue.cameraPitch = 27f;
                venue.cameraYaw = -12f;
                venue.cameraTargetViewportY = .28f;
                if (PrefabUtility.SaveAsPrefabAsset(root, Destination) == null)
                    throw new IOException("Could not save the selected street camera.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var selected = AssetDatabase.LoadAssetAtPath<GameObject>(Destination).GetComponent<VenueSceneRoot>();
            Validate(selected);
            if (Hash(Source) != sourceHash) throw new InvalidDataException("Candidate bytes changed during selection.");
            entry.prefab = selected;
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssetIfDirty(registry);
            var result = new JObject
            {
                ["state"] = "selected_for_device_validation_not_published",
                ["source"] = Source, ["source_sha256"] = sourceHash,
                ["selected"] = Destination, ["selected_sha256"] = Hash(Destination),
                ["previous_prefab"] = previous, ["registry_backup"] = backup,
                ["registry_sha256"] = Hash(Registry), ["lightmaps"] = 5,
                ["camera"] = new JArray(10.8f, 27f, -12f, .28f)
            };
            string output = Path.GetFullPath("../art/direction-20261006/timeline-device-selection.json");
            File.WriteAllText(output, result.ToString());
            Debug.Log("CTLIFE [venue-selection] Reviewed Timeline selected with baked lighting and close landscape framing. Device validation pending. Receipt: " + output);
        }

        private static void Validate(VenueSceneRoot venue)
        {
            if (venue == null || venue.venueId != "timeline_square" || venue.navigation == null)
                throw new InvalidDataException("Selected street identity/navigation is invalid.");
            var maps = venue.GetComponent<VenueLightmaps>();
            if (maps == null || maps.colors.Length != 5 || maps.bindings.Length == 0 ||
                maps.colors.Any(c => c == null) || maps.bindings.Any(b => b.renderer == null || b.lightmap < 0 || b.lightmap >= maps.colors.Length))
                throw new InvalidDataException("Selected street has incomplete lightmap bindings.");
        }

        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
