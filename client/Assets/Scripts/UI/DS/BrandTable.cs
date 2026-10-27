using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.UI.DS
{
    /// <summary>
    /// The UI side of the ONE brand table (design system v2, DELTA.md "Brand theming"). Real brands live in
    /// contract/ids/brands.json (lane A, shipped as Resources/Contract/brands.json): each entry's `ui_id` is the brand id used
    /// here and its `mark_png` is the mark the hero tile / HUD mark tile shows; nothing is duplicated. The colours are the
    /// `.brand-&lt;id&gt;` classes in ds-tokens.uss (USS cannot read JSON); BrandTableTests checks they equal `ui_tokens`.
    /// `police` is a system theme (no real brand): its mark is the bundled Phosphor police car.
    /// </summary>
    public static class BrandTable
    {
        public const string Default = "default", McDonalds = "mcdonalds", Police = "police";

        private sealed class Brand { public string MarkArt, MarkIcon, NameKey; }

        private static Dictionary<string, Brand> _brands;

        /// <summary>Venue id → brand id (venues without a brand use the system default).</summary>
        private static readonly Dictionary<string, string> Venues = new Dictionary<string, string>
        {
            ["rekt_burger"] = McDonalds,
            ["ct_jail"] = Police,
        };

        private static Dictionary<string, Brand> Brands => _brands ??= Load();

        /// <summary>Reads Resources/Contract/brands.json (each `ui_id` with its `mark_png`) plus the system themes.</summary>
        private static Dictionary<string, Brand> Load(string json = null)
        {
            var map = new Dictionary<string, Brand> { [Police] = new Brand { MarkIcon = "police-car" } };
            try
            {
                json ??= Resources.Load<TextAsset>("Contract/brands")?.text;
                if (string.IsNullOrEmpty(json)) return map;
                if (!(JObject.Parse(json)["brands"] is JObject brands)) return map;
                foreach (var p in brands.Properties())
                {
                    if (!(p.Value is JObject b)) continue;
                    var id = (string)b["ui_id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    map[id] = new Brand { MarkArt = ResourcePath((string)b["mark_png"]), NameKey = (string)b["name"] };
                }
            }
            catch (Exception e) { Debug.LogWarning("CTLIFE [ds] brands.json unreadable: " + e.Message); }
            return map;
        }

        /// <summary>"client/Assets/Resources/UI/Brands/x.png" → "UI/Brands/x" (a Resources.Load path).</summary>
        public static string ResourcePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var p = assetPath.Replace('\\', '/');
            int i = p.IndexOf("/Resources/", StringComparison.Ordinal);
            if (i < 0) return null;
            p = p.Substring(i + "/Resources/".Length);
            int dot = p.LastIndexOf('.');
            return dot > 0 ? p.Substring(0, dot) : p;
        }

        public static bool IsKnown(string brandId) => brandId != null && Brands.ContainsKey(brandId);

        public static string ForVenue(string venueId) => venueId != null && Venues.TryGetValue(venueId, out var b) ? b : Default;

        /// <summary>The USS class that sets the brand's colours, or null for the default.</summary>
        public static string ClassFor(string brandId) => IsKnown(brandId) ? "brand-" + brandId : null;

        /// <summary>The brand's mark: real art untinted, or an icon in white.</summary>
        public static VisualElementMark Mark(string brandId) =>
            IsKnown(brandId) ? new VisualElementMark(Brands[brandId].MarkArt, Brands[brandId].MarkIcon) : null;
    }

    /// <summary>What a brand mark is made of (art path or Phosphor icon name).</summary>
    public sealed class VisualElementMark
    {
        public readonly string Art, Icon;
        public VisualElementMark(string art, string icon) { Art = art; Icon = icon; }
        public Texture2D Texture => Art != null ? Ds.IconTexture(Art) : null;
    }
}
