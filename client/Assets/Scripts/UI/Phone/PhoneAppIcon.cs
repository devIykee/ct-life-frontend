using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Original CTOS launcher artwork. Each texture contains its own continuous-corner enclosure,
    /// object layers and material lighting. Do not apply the game's monochrome UiIcon tint or tile background.
    /// </summary>
    public sealed class PhoneAppIcon : VisualElement
    {
        private static readonly HashSet<string> Authored = new(StringComparer.Ordinal)
        {
            "map", "jobs", "bank", "wallet", "richlist", "home", "profile", "settings", "pulse",
            "perps", "predict", "ore", "topup", "timeline", "messages", "spaces", "calls", "shop",
            "week", "biz", "ads", "builder", "vc", "league", "beef", "farm", "cabal", "npcchat", "xlink", "duoshop"
        };
        private static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);

        public static bool HasDedicatedArtwork(string appId) => appId != null && Authored.Contains(Normalize(appId));

        public PhoneAppIcon(string appId)
        {
            AddToClassList("ct-phone-app-art");
            pickingMode = PickingMode.Ignore;
            focusable = false;
            string key = HasDedicatedArtwork(appId) ? Normalize(appId) : "app";
            if (!Textures.TryGetValue(key, out var texture))
            {
                texture = Resources.Load<Texture2D>("UI/PhoneOS/Icons/" + key);
                Textures.Add(key, texture);
                if (texture == null) Log.Warn("ui", "Missing CTOS artwork: " + key);
            }
            if (texture != null) style.backgroundImage = Background.FromTexture2D(texture);
        }

        private static string Normalize(string appId) => appId == "rich_list" ? "richlist" : appId;
    }
}
