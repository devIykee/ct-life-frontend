using System;
using CTLife.World;

namespace CTLife.UI
{
    /// <summary>Local art selection only. Does not invent Look IDs, aliases, ownership or server confirmation.</summary>
    public static class ExactCustomizerSelection
    {
        public static bool TryRequest(AvatarSpec spec, out ModularAvatarRequest request, out ModularAvatarFailure failure)
        {
            request = null;
            if (spec == null || spec.parts == null || spec.tints == null || spec.accessories == null || !spec.IsModular)
            { failure = new ModularAvatarFailure(ModularAvatarError.InvalidRequest, "An explicit complete modular spec is required."); return false; }
            var next = new ModularAvatarRequest { baseId = spec.baseId };
            foreach (var pair in spec.parts)
            {
                if (!Enum.TryParse(pair.Key, false, out ModularSlot slot) || !Enum.IsDefined(typeof(ModularSlot), slot) || slot.ToString() != pair.Key)
                { failure = new ModularAvatarFailure(ModularAvatarError.UnexpectedPart, "Unknown modular slot " + pair.Key + "."); return false; }
                next.parts.Add(slot, pair.Value);
            }
            foreach (var pair in spec.tints) next.tints.Add(pair.Key, pair.Value);
            foreach (var pair in spec.accessories) next.accessories.Add(pair.Key, pair.Value);
            request = next; failure = default; return true;
        }

        public static bool Resolves(AvatarSpec spec, ExactModularCatalog catalog) =>
            TryRequest(spec, out var request, out _) && ExactModularAvatar.TryResolve(catalog, request, out _, out _);

        /// <summary>
        /// Only returns a spec to commit after the new actor was built. On failure the old preview/selection can remain,
        /// but acceptedSpec is null and the caller must show failure, not select/save/react to the rejected option.
        /// </summary>
        public static bool TryApply(AvatarPreview preview, ExactModularCatalog catalog, AvatarSpec requested,
            out AvatarSpec acceptedSpec, out ModularAvatarFailure failure)
        {
            acceptedSpec = null;
            if (!TryRequest(requested, out var request, out failure)) return false;
            if (preview == null) { failure = new ModularAvatarFailure(ModularAvatarError.InvalidRequest, "No preview surface."); return false; }
            if (!preview.TrySetModular(catalog, request, out failure)) return false;
            acceptedSpec = requested.Clone(); return true;
        }
    }
}
