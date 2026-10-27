using System;
using System.Linq;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Palette bindings of the uniform looks (Rekt Burger crew variants): each crew prefab's own material and its masked twin, so
    /// the wearer's chosen skin and hair swatches move onto the uniform (identity first, review 2026-10-26). Kept apart from the
    /// creator's CharacterPalettes catalog. Asset: Resources/Characters/UniformPalettes.asset (CrewPaletteImport).
    /// </summary>
    public sealed class UniformPalettes : ScriptableObject
    {
        [Serializable] public sealed class Entry { public string lookId; public Material sourceMaterial, maskedMaterial; }
        public Entry[] entries = Array.Empty<Entry>();

        private static UniformPalettes _instance; private static bool _loaded;
        public static UniformPalettes Instance
        {
            get { if (!_loaded) { _instance = Resources.Load<UniformPalettes>("Characters/UniformPalettes"); _loaded = true; } return _instance; }
        }
        public Entry Find(string lookId) => entries?.FirstOrDefault(e => e != null && e.lookId == lookId);
    }
}
