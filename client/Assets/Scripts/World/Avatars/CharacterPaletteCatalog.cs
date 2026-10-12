using System;
using System.Linq;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Reviewed mask/material bindings for whole characters. Colors are options, never new identities.</summary>
    [CreateAssetMenu(menuName = "CT Life/Character Palette Catalog")]
    public sealed class CharacterPaletteCatalog : ScriptableObject
    {
        [Serializable] public sealed class Swatch { public string id; public Color color = Color.white; }
        [Serializable] public sealed class Palette { public string id; public Swatch[] swatches = Array.Empty<Swatch>(); }
        [Serializable] public sealed class HairStyle
        {
            public string id, hairId, labelKey;
            public Texture2D thumbnail;
        }
        [Serializable] public sealed class Base
        {
            public string id, personId;
            public string[] sourceLook = Array.Empty<string>();
            public Material sourceMaterial, maskedMaterial;
            public Texture2D mask0, mask1;
            public string[] regions = Array.Empty<string>();
            public HairStyle[] hairStyles = Array.Empty<HairStyle>();
        }
        public Base[] bases = Array.Empty<Base>();
        public Palette[] palettes = Array.Empty<Palette>();
        public static CharacterPaletteCatalog Load() => Resources.Load<CharacterPaletteCatalog>("Characters/CharacterPalettes");
        public Base FindBase(string id) => bases?.FirstOrDefault(b => b != null && b.id == id);
        public Palette PaletteFor(string region)
        {
            string id = region == "skin" || region == "hair" ? region :
                region == "top" || region == "bottom" || region == "shoes" ? "cloth" : null;
            return id == null ? null : palettes?.FirstOrDefault(p => p != null && p.id == id);
        }
    }
}
