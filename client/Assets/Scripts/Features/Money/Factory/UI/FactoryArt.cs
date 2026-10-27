using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// The 12 coin faces (Twemoji v17.0.3, CC BY 4.0; art/LICENSES.md) at Resources/UI/Factory/Coins/rf_*.png, and the
    /// Factory stylesheet. Art ids come from `/factory/config` templates; an unknown id falls back to the frog.
    /// </summary>
    public static class FactoryUiExt
    {
        public static T Named<T>(this T e, string n) where T : VisualElement { e.name = n; return e; }
    }

    public static class FactoryArt
    {
        public const string Credit = "Coin art: Twemoji by Twitter, Inc and contributors, CC BY 4.0";
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static StyleSheet _style, _heat;

        public static Texture2D Face(string templateId)
        {
            var id = string.IsNullOrEmpty(templateId) ? "rf_frog" : templateId;
            if (Cache.TryGetValue(id, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("UI/Factory/Coins/" + id);
            if (t == null && id != "rf_frog") t = Face("rf_frog");
            Cache[id] = t;
            return t;
        }

        /// <summary>The art id's short name ("frog"), for tiles and the review line.</summary>
        public static string Name(string templateId) =>
            string.IsNullOrEmpty(templateId) ? "" : templateId.StartsWith("rf_") ? templateId.Substring(3) : templateId;

        public static VisualElement Image(string templateId, string cls)
        {
            var e = new VisualElement();
            e.AddToClassList("rf-art");
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            e.pickingMode = PickingMode.Ignore;
            var t = Face(templateId);
            if (t != null) e.style.backgroundImage = Background.FromTexture2D(t);
            return e;
        }

        /// <summary>Adds the Factory (and the heat band/help box) stylesheets to the UI root once.</summary>
        public static void EnsureStyles(VisualElement any)
        {
            var root = CTLife.UI.UiRoot.Instance != null ? CTLife.UI.UiRoot.Instance.Root : any?.panel?.visualTree;
            if (root == null) return;
            _style ??= Resources.Load<StyleSheet>("UI/Factory/factory");
            _heat ??= Resources.Load<StyleSheet>("UI/heat");
            if (_heat != null && !root.styleSheets.Contains(_heat)) root.styleSheets.Add(_heat);
            if (_style != null && !root.styleSheets.Contains(_style)) root.styleSheets.Add(_style);
        }
    }
}
