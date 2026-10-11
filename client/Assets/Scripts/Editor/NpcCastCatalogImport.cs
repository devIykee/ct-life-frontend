using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>Explicit, reviewed FE art import. Never runs on general asset refresh.</summary>
    public static class NpcCastCatalogImport
    {
        public const string AssetPath = "Assets/Resources/Characters/NpcCastCatalog.asset";
        private const string Argument = "-ctlife-npc-cast-manifest";

        // Root owns execution. Both entry points require the same reviewed manifest.
        public static void ValidateReviewed() => Process(false);
        public static void ImportReviewed() => Process(true);

        private static void Process(bool write)
        {
            var args = Environment.GetCommandLineArgs();
            int argument = Array.IndexOf(args, Argument);
            if (argument < 0 || argument + 1 >= args.Length || args[argument + 1].StartsWith("-"))
                throw new ArgumentException("Supply " + Argument + " <reviewed FE manifest.json>.");
            var manifest = JObject.Parse(File.ReadAllText(Path.GetFullPath(args[argument + 1])));
            if ((int?)manifest["version"] != 1 || (bool?)manifest["approvedForRuntime"] != true)
                throw new InvalidOperationException("Catalog requires version 1 and explicit approvedForRuntime:true.");
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var rolesJson = JObject.Parse(File.ReadAllText(Path.Combine(repo, "contract/ids/archetypes.json")));
            var roles = new HashSet<string>(rolesJson["archetypes"].Select(row => (string)row["id"]), StringComparer.Ordinal);
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            if (registry == null) throw new InvalidOperationException("Import CharacterRegistry before reviewed NPC bindings.");
            if (!(manifest["bindings"] is JArray rows)) throw new InvalidOperationException("Manifest needs a bindings array.");
            var bindings = new List<NpcCastCatalog.Binding>();
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                string role = (string)row["archetypeId"];
                if (string.IsNullOrWhiteSpace(role) || !roles.Contains(role) || !assigned.Add(role))
                    throw new InvalidOperationException("Unknown or duplicate imported archetype: " + role);
                if (!(row["reviewEvidence"] is JArray evidence) || evidence.Count == 0)
                    throw new InvalidOperationException(role + " needs local reviewEvidence paths.");
                foreach (var item in evidence)
                {
                    string relative = (string)item;
                    if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                        throw new InvalidOperationException(role + " reviewEvidence must be repository-relative.");
                    string path = Path.GetFullPath(Path.Combine(repo, relative));
                    if (!path.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
                        throw new InvalidOperationException(role + " reviewEvidence is missing or outside the repository: " + relative);
                }
                if (!(row["presentationKeys"] is JArray keys) || keys.Count == 0)
                    throw new InvalidOperationException(role + " needs at least one reviewed presentation key.");
                var binding = new NpcCastCatalog.Binding { archetypeId = role };
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in keys)
                {
                    string key = (string)value;
                    if (!seen.Add(key) || !NpcCastCatalog.TryExactEntry(registry, key, out var entry))
                        throw new InvalidOperationException(role + " has a duplicate or unresolved exact presentation key: " + key);
                    ValidateWorldEntry(role, entry, registry);
                    binding.presentationKeys.Add(key);
                }
                bindings.Add(binding);
            }
            // Every row is validated before the existing runtime asset can be changed.
            if (write)
            {
                var catalog = AssetDatabase.LoadAssetAtPath<NpcCastCatalog>(AssetPath);
                if (catalog == null)
                {
                    catalog = ScriptableObject.CreateInstance<NpcCastCatalog>();
                    catalog.bindings = bindings;
                    AssetDatabase.CreateAsset(catalog, AssetPath);
                }
                else
                {
                    catalog.bindings = bindings;
                    EditorUtility.SetDirty(catalog);
                }
                AssetDatabase.SaveAssets();
            }
            string retained = string.Join(",", roles.Where(role => !assigned.Contains(role)).OrderBy(role => role, StringComparer.Ordinal));
            Debug.Log($"CTLIFE [npc-cast-import] {(write ? "IMPORTED" : "VALIDATED")}: {assigned.Count}/{roles.Count} curated roles; " +
                $"legacy/modular/procedural fallback roles=[{retained}]. This is presentation coverage, not visual acceptance.");
        }

        private static void ValidateWorldEntry(string role, CharacterRegistry.Entry entry, CharacterRegistry registry)
        {
            string path = AssetDatabase.GetAssetPath(entry.prefab);
            if (!path.StartsWith("Assets/Art/characters/authored/", StringComparison.Ordinal) ||
                !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(role + " must reference an authored world prefab: " + path);
            if (entry.previewPrefab != null && entry.previewPrefab == entry.prefab)
                throw new InvalidOperationException(role + " world prefab is the creator prefab: " + path);
            var animator = entry.prefab.GetComponentInChildren<Animator>(true);
            var lod = entry.prefab.GetComponentInChildren<LODGroup>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException(role + " needs its own valid Humanoid Avatar: " + path);
            if (lod == null || lod.GetLODs().Length < 2)
                throw new InvalidOperationException(role + " needs an authored world LODGroup: " + path);
            foreach (var tier in lod.GetLODs())
                if (tier.renderers == null || tier.renderers.Length == 0 || tier.renderers.Any(renderer => renderer == null))
                    throw new InvalidOperationException(role + " has an incomplete world LOD: " + path);
            if (entry.controllerOverride == null && registry.controller == null)
                throw new InvalidOperationException(role + " has no authored or registry animation controller.");
        }
    }
}
