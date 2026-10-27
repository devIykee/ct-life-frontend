using System.Collections.Generic;
using CTLife.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// The restaurant's menu and the customers' order lines, from the brand table (contract/ids/brands.json, shipped as
    /// Resources/Contract/brands.json): one place for the brand's product names, so a swap is one edit (2026-10-26).
    /// </summary>
    public static class RektBurgerMenu
    {
        public const string BrandId = "brand.mcdonalds";
        private static List<string> _lines;

        /// <summary>The order lines with the menu item and the restaurant's name filled in.</summary>
        public static IReadOnlyList<string> Lines
        {
            get
            {
                if (_lines != null) return _lines;
                _lines = new List<string>();
                var ta = Resources.Load<TextAsset>("Contract/brands");
                if (ta != null && JObject.Parse(ta.text)["brands"]?[BrandId] is JObject b)
                    _lines.AddRange(Build(b, NameTable.Get((string)b["name"] ?? "venue.rekt_burger", "the restaurant")));
                if (_lines.Count == 0) _lines.Add("One burger, please.");
                return _lines;
            }
        }

        public static List<string> Build(JObject brand, string name)
        {
            var menu = brand["menu"] as JArray;
            var outLines = new List<string>();
            foreach (var l in brand["order_lines"] as JArray ?? new JArray())
            {
                var text = (string)l["text"];
                if (string.IsNullOrEmpty(text)) continue;
                int? i = (int?)l["item"];
                string item = i.HasValue && menu != null && i.Value >= 0 && i.Value < menu.Count ? (string)menu[i.Value]["item"] : null;
                if (text.Contains("{item}") && item == null) continue;
                outLines.Add(text.Replace("{item}", item ?? "").Replace("{name}", name));
            }
            return outLines;
        }
    }
}
