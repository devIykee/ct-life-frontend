using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    public enum ModularSlot { Body, Head, Hair, Top, Bottom, Shoes }
    [Flags] public enum ModularRole { None = 0, Body = 1, Head = 2, Eyes = 4, Brows = 8, Hair = 16, Top = 32, Bottom = 64, Feet = 128 }

    /// <summary>Local art IDs only. This is deliberately not the seven-field server Look.</summary>
    public sealed class ModularAvatarRequest
    {
        public string baseId;
        public readonly Dictionary<ModularSlot, string> parts = new Dictionary<ModularSlot, string>();
        public readonly Dictionary<string, string> tints = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> accessories = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    [Serializable] public sealed class ModularSwatch { public string id; public Color color = Color.white; }
    [Serializable] public sealed class ModularPalette { public string region; public ModularSwatch[] swatches = Array.Empty<ModularSwatch>(); }
    [Serializable] public sealed class ModularTintBinding
    {
        public string region;
        public int materialIndex;
        public Shader shader;
        public string colorProperty;
        // A reviewed shader/mask pair, or a deliberate full-material region. HasColor alone is not a region contract.
        public bool wholeMaterial;
        public string maskProperty;
        public Texture mask;
    }
    [Serializable] public sealed class ModularRenderer
    {
        public GameObject prefab; // Identity root, exactly one selected-LOD SMR, source rest skeleton, no scripts.
        public ModularRole roles;
        public ModularTintBinding[] tints = Array.Empty<ModularTintBinding>();
    }
    [Serializable] public sealed class ModularPart
    {
        public string id, baseId;
        public ModularSlot slot;
        public ModularRenderer[] renderers = Array.Empty<ModularRenderer>();
    }

    /// <summary>
    /// Explicit complete art contract, assigned by a reviewed importer. Never loaded implicitly by legacy callers.
    /// A bald/embedded-head model must state its actual required roles; UBC requires Head/Eyes/Brows/Hair/Top/Bottom/Feet.
    /// Accessories are intentionally unsupported in this revision. No request is silently discarded.
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/Exact Modular Catalog")]
    public sealed class ExactModularCatalog : ScriptableObject
    {
        public string baseId;
        public GameObject rigPrefab; // Own-proportion hierarchy with one root Animator and no renderers.
        public Avatar avatar;
        public RuntimeAnimatorController controller;
        public ModularSlot[] requiredSlots = Array.Empty<ModularSlot>();
        public ModularRole requiredRoles;
        public ModularPart[] parts = Array.Empty<ModularPart>();
        public ModularPalette[] palettes = Array.Empty<ModularPalette>();
        public float height = 1.75f;
    }

    public enum ModularAvatarError
    {
        None, InvalidRequest, InvalidCatalog, MissingPart, UnexpectedPart, UnsupportedTint, UnsupportedAccessory,
        IncompatibleBase, MissingGeometry, InvalidRig, InvalidMesh, MissingBone, BindPoseMismatch, InvalidMaterial, AssemblyFailed
    }
    public readonly struct ModularAvatarFailure
    {
        public readonly ModularAvatarError code;
        public readonly string detail;
        public ModularAvatarFailure(ModularAvatarError code, string detail) { this.code = code; this.detail = detail; }
        public override string ToString() => code + ": " + detail;
    }
}
