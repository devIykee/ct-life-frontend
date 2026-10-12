using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CTLife.World
{
    public sealed class CharacterPaletteSelection
    {
        public CharacterRegistry.Entry Entry { get; internal set; }
        public string[] SourceLook { get; internal set; }
        public string[] IdentityLook { get; internal set; }
        public bool HasChanges { get; internal set; }
        internal CharacterPaletteCatalog.Base binding;
        internal readonly Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.Ordinal);
    }

    /// <summary>Strict, finite palette grammar shared by creator, local player and remote world avatars.</summary>
    public static class CharacterPaletteResolver
    {
        public const string ShaderName = "CT Life/Character Masked Lit";
        public static readonly string[] Regions = { "skin", "hair", "top", "bottom", "shoes" };
        private static readonly int[] Slots = { 0, 2, 3, 4, 5 };
        private static readonly string[] Properties = { "_SkinTint", "_HairTint", "_TopTint", "_BottomTint", "_ShoesTint" };

        public static bool TryResolve(string[] look, out CharacterPaletteSelection selection,
            CharacterPaletteCatalog catalog = null)
        {
            selection = null;
            if (look == null || look.Length != 7 || look.Any(s => string.IsNullOrEmpty(s) || s.Length > 96)) return false;
            catalog = catalog != null ? catalog : CharacterPaletteCatalog.Load();
            var binding = catalog?.FindBase(look[1]);
            if (!ValidBinding(binding) || binding.sourceLook.Length != 7 || look[1] != binding.sourceLook[1] ||
                look[6] != binding.sourceLook[6]) return false;
            var sourceLook = (string[])binding.sourceLook.Clone();
            if (look[2] != sourceLook[2] && !look[2].StartsWith(sourceLook[2] + "__", StringComparison.Ordinal))
            {
                var style = binding.hairStyles?.FirstOrDefault(h => h != null && !string.IsNullOrEmpty(h.hairId) &&
                    (look[2] == h.hairId || look[2].StartsWith(h.hairId + "__", StringComparison.Ordinal)));
                if (style == null) return false;
                sourceLook[2] = style.hairId;
            }
            var source = CharacterRegistry.Instance?.ForLook(sourceLook);
            if (source?.prefab == null || source.previewPrefab == null || source.key != string.Join("|", sourceLook)) return false;
            var result = new CharacterPaletteSelection { binding = binding, SourceLook = sourceLook,
                IdentityLook = (string[])binding.sourceLook.Clone() };
            for (int i = 0; i < Regions.Length; i++)
            {
                int slot = Slots[i];
                if (look[slot] == sourceLook[slot]) continue;
                if (binding.regions != null && binding.regions.Length > 0 && !binding.regions.Contains(Regions[i])) return false;
                string id = look[slot];
                if (slot != 0)
                {
                    string prefix = sourceLook[slot] + "__";
                    if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;
                    id = id.Substring(prefix.Length);
                }
                var swatch = catalog.PaletteFor(Regions[i])?.swatches?.FirstOrDefault(s => s != null && s.id == id);
                if (swatch == null) return false;
                result.colors.Add(Regions[i], swatch.color); result.HasChanges = true;
            }
            result.Entry = new CharacterRegistry.Entry { key = string.Join("|", look), prefab = source.prefab,
                previewPrefab = source.previewPrefab, controllerOverride = source.controllerOverride,
                height = source.height, variant = source.variant };
            selection = result; return true;
        }

        public static bool TryChange(string[] look, string region, string swatchId, out string[] changed)
        {
            changed = null;
            int index = Array.IndexOf(Regions, region);
            if (index < 0 || !TryResolve(look, out var selected)) return false;
            var next = (string[])look.Clone(); int slot = Slots[index];
            next[slot] = string.IsNullOrEmpty(swatchId) ? selected.SourceLook[slot] :
                slot == 0 ? swatchId : selected.SourceLook[slot] + "__" + swatchId;
            if (!TryResolve(next, out _)) return false;
            changed = next; return true;
        }

        /// <summary>Changes authored wardrobe while carrying independent supported palette selections to the new source.</summary>
        public static bool TryChangeOutfit(string[] look, string[] outfit, out string[] changed)
        {
            changed = null;
            if (!TryResolve(look, out var current) || !TryResolve(outfit, out var target)) return false;
            string Person(CharacterPaletteSelection s) => string.IsNullOrEmpty(s.binding.personId) ? s.binding.id : s.binding.personId;
            if (Person(current) != Person(target)) return false;
            var next = (string[])outfit.Clone();
            for (int i = 0; i < Regions.Length; i++)
            {
                int slot = Slots[i];
                if (look[slot] == current.SourceLook[slot]) continue;
                string swatch = slot == 0 ? look[slot] : look[slot].Substring(current.SourceLook[slot].Length + 2);
                if (TryChange(next, Regions[i], swatch, out var colored)) next = colored;
            }
            changed = next; return true;
        }

        /// <summary>Changes only the fitted hair geometry, retaining its chosen color and every other slot.</summary>
        public static bool TryChangeHairStyle(string[] look, string styleId, out string[] changed)
        {
            changed = null;
            if (!TryResolve(look, out var selected)) return false;
            var style = selected.binding.hairStyles?.FirstOrDefault(h => h != null && h.id == styleId);
            if (style == null || string.IsNullOrEmpty(style.hairId)) return false;
            var next = (string[])look.Clone();
            string color = look[2].Substring(selected.SourceLook[2].Length);
            next[2] = style.hairId + color;
            if (!TryResolve(next, out _)) return false;
            changed = next; return true;
        }

        private static bool ValidBinding(CharacterPaletteCatalog.Base binding)
        {
            var material = binding?.maskedMaterial;
            return binding?.sourceMaterial != null && material != null && material.shader != null &&
                material.shader.name == ShaderName && binding.mask0 != null && binding.mask1 != null &&
                material.GetTexture("_TintMask0") == binding.mask0 && material.GetTexture("_TintMask1") == binding.mask1 &&
                material.GetTexture("_BaseMap") == binding.sourceMaterial.GetTexture("_BaseMap");
        }

        internal static void Apply(GameObject actor, CharacterPaletteSelection selected)
        {
            if (selected == null) return;
            var source = selected.binding?.sourceMaterial;
            // Original-color thin shells still need the palette shader's back-face lighting.
            // URP/Lit can draw both sides but uses the same normal on both; that darkens the
            // previously hidden cloth face. Zero tint values preserve the authored albedo.
            if (!selected.HasChanges && (source == null || !source.HasProperty("_Cull") || source.GetFloat("_Cull") != 0f)) return;
            if (!ValidBinding(selected.binding)) throw new InvalidOperationException("Character palette binding is unavailable.");
            var renderers = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0 || renderers.Any(r => r.sharedMaterials.Length != 1 ||
                r.sharedMaterial != selected.binding.sourceMaterial))
                throw new InvalidOperationException("Character palette does not match this actor's source material.");
            var material = new Material(selected.binding.maskedMaterial) { name = selected.binding.id + "_selected_palette" };
            // Culling describes the source geometry, not a color choice. Keep it even when an older
            // serialized palette material predates the surface-side property or a source-side repair.
            material.SetFloat("_Cull", selected.binding.sourceMaterial.GetFloat("_Cull"));
            actor.AddComponent<CharacterPaletteResources>().Own(material);
            for (int i = 0; i < Regions.Length; i++)
                if (selected.colors.TryGetValue(Regions[i], out var color)) material.SetVector(Properties[i], LinearTint(color));
            if (selected.colors.TryGetValue("hair", out var hair))
            {
                // Preserved v3 brow rule: darker than the selected hair, with lower saturation.
                var linear = hair.linear;
                float luminance = Vector3.Dot(new Vector3(linear.r, linear.g, linear.b), new Vector3(.2126f, .7152f, .0722f));
                float scale = Mathf.Min(luminance * .55f, .075f) / Mathf.Max(luminance, .0001f);
                var brow = Color.Lerp(new Color(luminance, luminance, luminance), linear, .6f) * scale;
                material.SetVector("_BrowsTint", new Vector4(brow.r, brow.g, brow.b, 1f));
            }
            foreach (var renderer in renderers) renderer.sharedMaterial = material;
        }

        private static Vector4 LinearTint(Color color)
        {
            var linear = color.linear;
            return new Vector4(linear.r, linear.g, linear.b, 1f);
        }
    }
}
