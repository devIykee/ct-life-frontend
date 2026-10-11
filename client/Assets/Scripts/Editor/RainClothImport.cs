using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>Required one-map overlay for Rain's reviewed dielectric scarf.
    /// The original six-file import bundle remains an immutable historical source.</summary>
    public static class RainClothImport
    {
        private const string MapName = "RainAdult_MetallicSmoothness.png";

        [MenuItem("CT Life/Characters/Apply reviewed Rain cloth map")]
        public static void ApplyCloth()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string candidate = Path.Combine(repo, "art/character-candidates/rain-cloth-correction");
            var args = Environment.GetCommandLineArgs();
            int sourceIndex = Array.IndexOf(args, "-ctRainClothSource");
            if (sourceIndex >= 0 && sourceIndex + 1 < args.Length)
                candidate = Path.GetFullPath(args[sourceIndex + 1]);

            var report = JObject.Parse(File.ReadAllText(Path.Combine(candidate, "map-validation.json")));
            var visual = JObject.Parse(File.ReadAllText(Path.Combine(candidate, "visual-review.json")));
            if (report["all_pass"]?.Value<bool>() != true || visual["all_pass"]?.Value<bool>() != true)
                throw new InvalidDataException("Rain cloth map requires numeric and actual-render review passes.");
            string expected = report["sha256"]?.Value<string>();
            if (string.IsNullOrEmpty(expected) || visual["sha256"]?.Value<string>() != expected)
                throw new InvalidDataException("Rain cloth render review does not match the validated map.");
            string source = Path.Combine(candidate, "exports", MapName);
            if (Hash(source) != expected)
                throw new InvalidDataException("Rain cloth map differs from its reviewed SHA256.");

            string target = RainCharacterImport.RainRoot + MapName;
            if (!File.Exists(target) || !File.Exists(target + ".meta"))
                throw new FileNotFoundException("Import Rain's canonical bundle before applying its cloth overlay.", target);
            string before = Hash(target);
            if (before != report["source_file_sha256"]?.Value<string>() && before != expected)
                throw new InvalidDataException("Rain's installed packed map is neither the source nor the reviewed cloth correction.");
            string guid = AssetDatabase.AssetPathToGUID(target);
            File.Copy(source, target, true); // Deliberately retain the existing .meta, GUID and material reference.
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (Hash(target) != expected || AssetDatabase.AssetPathToGUID(target) != guid)
                throw new InvalidDataException("Rain cloth overlay did not preserve the reviewed bytes and texture GUID.");
            var importer = AssetImporter.GetAtPath(target) as TextureImporter;
            if (importer == null || importer.sRGBTexture || importer.alphaIsTransparency ||
                importer.alphaSource != TextureImporterAlphaSource.FromInput)
                throw new InvalidDataException("Rain packed metallic/smoothness texture must retain linear independent-alpha import settings.");
            Debug.Log($"CTLIFE [rain-cloth] reviewed dielectric scarf map applied; SHA256={expected}; texture GUID preserved. Device review still required.");
        }

        private static string Hash(string path)
        {
            using var hash = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
