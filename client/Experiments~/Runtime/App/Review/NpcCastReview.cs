#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.App.Review
{
    /// <summary>Temporary reviewed-art plumbing checks through the production NPC factory; no runtime catalog write.</summary>
    internal static class NpcCastReview
    {
        private const string Snow = "skin_tone3|head_snow|hair_snow_bun|top_vneck_charcoal|bottom_denim_light|shoes_sneakers_blue|acc_none";
        private const string Rain = "skin_tone3|head_rain|hair_rain_ponytail|top_sleeveless_white|bottom_denim_fitted|shoes_canvas_ochre|acc_scarf_sky";

        public static async Task Run()
        {
            var registry = CharacterRegistry.Instance;
            Require(registry != null, "Cast review requires the actual CharacterRegistry.");
            var roles = Services.Get<ConfigStore>().Registries.Archetypes.Keys.OrderBy(role => role, StringComparer.Ordinal).ToArray();
            Require(roles.Length > 0 && roles.Contains("town_crier") && roles.Contains("spaces_host") && roles.Contains("normie"),
                "Cast review requires the imported role registry.");
            Require(NpcCastCatalog.TryExactEntry(registry, Snow, out var snow) &&
                NpcCastCatalog.TryExactEntry(registry, Rain, out _), "Both exact authored Snow/Rain world entries are required.");
            var root = new GameObject("Review NPC cast factory");
            var catalog = ScriptableObject.CreateInstance<NpcCastCatalog>();
            var reordered = ScriptableObject.CreateInstance<NpcCastCatalog>();
            var empty = ScriptableObject.CreateInstance<NpcCastCatalog>();
            var invalid = ScriptableObject.CreateInstance<NpcCastCatalog>();
            try
            {
                // Explicit fixture-only assignments, not approved role art or a production rollout.
                catalog.bindings.Add(Binding("town_crier", Snow));
                catalog.bindings.Add(Binding("spaces_host", Rain));
                catalog.bindings.Add(Binding("normie", Snow, Rain));
                reordered.bindings.Add(Binding("normie", Snow, Rain));
                reordered.bindings.Add(Binding("spaces_host", Rain));
                reordered.bindings.Add(Binding("town_crier", Snow));

                foreach (string role in new[] { "town_crier", "spaces_host" })
                {
                    string id = "review-cast-" + role;
                    Require(catalog.TryResolve(id, role, registry, out var expected, out _), "Fixture role did not resolve: " + role);
                    var view = AvatarFactory.CreateNpc("Cast review", id, role, root.transform, catalog);
                    CheckEntry(view, expected, registry);
                    Require(view.npcId == id && view.archetypeId == role && view.isNpc, "Factory changed server role/identity metadata.");
                    view.SetBase("loco_walk");
                    view.SetBase("loco_idle");
                    CheckEntry(view, expected, registry);
                    Log.Info("review-cast", $"PASS actual CreateNpc role={role}; presentation={expected.key}; " +
                        $"worldPrefab={expected.prefab.name}; controller={view.Animator.runtimeAnimatorController.name}; " +
                        $"worldLods={view.GetComponentInChildren<LODGroup>(true).GetLODs().Length}.");
                    Object.Destroy(view.gameObject);
                    await Awaitable.NextFrameAsync();
                }

                var selected = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < 32; i++)
                {
                    string id = "review-pool-" + i;
                    Require(catalog.TryResolve(id, "normie", registry, out var first, out _) &&
                        reordered.TryResolve(id, "normie", registry, out _, out _), "Stable fixture pool failed resolution.");
                    reordered.TryResolve(id, "normie", registry, out var second, out _);
                    Require(first.key == second.key, "NPC choice changed when role rows were reordered/recreated.");
                    if (selected.Add(first.key))
                    {
                        var view = AvatarFactory.CreateNpc("Cast pool review", id, "normie", root.transform, catalog);
                        CheckEntry(view, first, registry);
                        Object.Destroy(view.gameObject);
                        await Awaitable.NextFrameAsync();
                        var recreated = AvatarFactory.CreateNpc("Cast pool review", id, "normie", root.transform, reordered);
                        CheckEntry(recreated, second, registry);
                        Object.Destroy(recreated.gameObject);
                        await Awaitable.NextFrameAsync();
                    }
                }
                Require(selected.Count == 2, "Fixture NPC IDs did not exercise both real variants.");
                Log.Info("review-cast", "PASS 32 stable fixture identities across independently created/reordered role catalogs; " +
                    "both authored entries instantiated through CreateNpc and retained on recreation. Variant order stays fixed.");

                int registered = 0, fallback = 0;
                foreach (string role in roles)
                {
                    Require(!empty.TryResolve("review-empty-" + role, role, registry, out _, out _), "Empty catalog unexpectedly selected art.");
                    var actual = AvatarFactory.CreateNpc("Fallback review", "review-empty-" + role, role, root.transform, empty);
                    var legacy = AvatarFactory.Create("Fallback baseline", null, true, role, root.transform);
                    CheckSameFallback(actual, legacy, role);
                    if (registry.ForArchetype(role) != null) registered++; else fallback++;
                    Object.Destroy(actual.gameObject);
                    Object.Destroy(legacy.gameObject);
                    await Awaitable.NextFrameAsync();
                }
                Log.Info("review-cast", $"PASS empty-catalog fallback for {roles.Length} imported roles; " +
                    $"registryPrefab={registered}; modularOrProcedural={fallback}. Compared with the prior Create route.");

                invalid.bindings.Add(Binding("town_crier", Snow, "missing|exact|presentation|entry|must|not|alias"));
                Require(!invalid.TryResolve("review-invalid", "town_crier", registry, out _, out var invalidReason),
                    "An incomplete variant pool was accepted.");
                var invalidView = AvatarFactory.CreateNpc("Invalid cast fallback review", "review-invalid", "town_crier", root.transform, invalid);
                var invalidBaseline = AvatarFactory.Create("Invalid baseline", null, true, "town_crier", root.transform);
                CheckSameFallback(invalidView, invalidBaseline, "town_crier");
                Object.Destroy(invalidView.gameObject);
                Object.Destroy(invalidBaseline.gameObject);
                invalid.bindings.Clear();
                invalid.bindings.Add(Binding("town_crier", Snow));
                invalid.bindings.Add(Binding("town_crier", Rain));
                Require(!invalid.TryResolve("review-duplicate", "town_crier", registry, out _, out var duplicateReason) &&
                    duplicateReason == "duplicate-role", "Duplicate role binding was accepted.");
                Require(!NpcCastCatalog.TryExactEntry(registry, "head_snow", out _), "Partial cosmetic alias was accepted as a complete presentation.");
                Log.Info("review-cast", $"PASS invalid pool fallback ({invalidReason}), duplicate role rejection, and partial-key rejection. " +
                    "Fixture catalog only; no production role bindings enabled. Varied-cast art/animation/performance acceptance remains separate.");
            }
            finally
            {
                root.SetActive(false);
                Object.Destroy(root);
                Object.Destroy(catalog);
                Object.Destroy(reordered);
                Object.Destroy(empty);
                Object.Destroy(invalid);
            }
        }

        private static NpcCastCatalog.Binding Binding(string role, params string[] keys) =>
            new NpcCastCatalog.Binding { archetypeId = role, presentationKeys = new List<string>(keys) };

        private static void CheckEntry(AvatarView view, CharacterRegistry.Entry entry, CharacterRegistry registry)
        {
            Require(view != null && view.Animator != null && view.Procedural == null, "Authored NPC fell back to procedural art.");
            var sourceAnimator = entry.prefab.GetComponentInChildren<Animator>(true);
            Require(sourceAnimator != null && sourceAnimator.avatar != null && sourceAnimator.avatar.isHuman &&
                sourceAnimator.avatar.isValid && view.Animator.avatar == sourceAnimator.avatar,
                "Factory did not retain the authored Humanoid Avatar.");
            var controller = entry.controllerOverride != null ? entry.controllerOverride : registry.controller;
            Require(controller != null && view.Animator.runtimeAnimatorController == controller,
                "Factory lost the entry's controller override or shared controller.");
            var sourceLod = entry.prefab.GetComponentInChildren<LODGroup>(true);
            var actualLod = view.GetComponentInChildren<LODGroup>(true);
            Require(sourceLod != null && actualLod != null, "NPC used a preview or missing-LOD prefab.");
            var source = sourceLod.GetLODs();
            var actual = actualLod.GetLODs();
            Require(source.Length >= 2 && source.Length == actual.Length, "Factory changed world LOD count.");
            for (int tier = 0; tier < source.Length; tier++)
            {
                Require(source[tier].renderers.Length == actual[tier].renderers.Length &&
                    source[tier].screenRelativeTransitionHeight == actual[tier].screenRelativeTransitionHeight,
                    "Factory changed the authored LOD transition or renderer count.");
                for (int index = 0; index < source[tier].renderers.Length; index++)
                {
                    var expected = source[tier].renderers[index] as SkinnedMeshRenderer;
                    var got = actual[tier].renderers[index] as SkinnedMeshRenderer;
                    Require(expected != null && got != null && expected.sharedMesh != null &&
                        got.sharedMesh == expected.sharedMesh, "Factory did not retain the exact authored LOD mesh.");
                }
            }
        }

        private static void CheckSameFallback(AvatarView actual, AvatarView legacy, string role)
        {
            Require(actual != null && legacy != null && actual.isNpc && actual.archetypeId == role &&
                (actual.Procedural != null) == (legacy.Procedural != null) &&
                (actual.Animator != null) == (legacy.Animator != null), "Empty/invalid catalog changed fallback kind for " + role);
            if (actual.Animator != null)
                Require(actual.Animator.avatar == legacy.Animator.avatar &&
                    actual.Animator.runtimeAnimatorController == legacy.Animator.runtimeAnimatorController,
                    "Empty/invalid catalog changed fallback animation for " + role);
            Require(actual.Body != null && legacy.Body != null && actual.Body.sharedMesh != null && legacy.Body.sharedMesh != null,
                "Fallback has no body for " + role);
            if (actual.Procedural == null)
                Require(actual.Body.sharedMesh == legacy.Body.sharedMesh, "Empty/invalid catalog changed fallback mesh for " + role);
            else
                Require(actual.Body.sharedMesh.vertexCount == legacy.Body.sharedMesh.vertexCount &&
                    actual.PlaceholderLods.Length == legacy.PlaceholderLods.Length, "Empty/invalid catalog changed procedural shape for " + role);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
