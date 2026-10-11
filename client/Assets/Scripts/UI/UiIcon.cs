using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Bundled Phosphor fill/bold icons; source, mapping and MIT notice live in Resources/UI.</summary>
    public sealed class UiIcon : VisualElement
    {
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        public UiIcon(string kind)
        {
            AddToClassList("ct-icon");
            AddToClassList("ct-icon-" + kind);
            pickingMode = PickingMode.Ignore;
            // "duo-<name>": a Phosphor fill file bundled for the Solana Duo kit (asset-sources.json duoAdditions).
            string asset = kind != null && kind.StartsWith("duo-", System.StringComparison.Ordinal) ? kind.Substring(4) : kind switch
            {
                "close" => "x",
                "edit" => "pencil-simple",
                "back" => "caret-left",
                "next" => "caret-right",
                "chat" => "chat-circle-text",
                "messages" => "chats",
                "timeline" => "newspaper",
                "map" => "map-trifold",
                "phone" => "device-mobile",
                "mic" => "microphone",
                "spaces" => "broadcast",
                "calls" => "video-camera",
                "emote" => "smiley",
                "home" => "house-line",
                "bank" => "wallet",
                "wallet" => "wallet",
                "jobs" => "briefcase",
                "projects" => "rocket-launch",
                "profile" => "user-circle",
                "contacts" => "users",
                "settings" => "gear-six",
                "ore" => "hexagon",
                "city" => "buildings",
                "pulse" => "heartbeat",
                "perps" => "chart-line-up",
                "predict" => "chart-pie",
                "topup" => "plus-circle",
                "shop" => "shopping-bag",
                "ads" => "megaphone",
                "league" => "trophy",
                "richlist" => "list-numbers",
                "rich_list" => "list-numbers",
                "needs" => "heart",
                "menu" => "dots-three",
                "face" => "user-focus",
                "body" => "person-simple",
                "rotate" => "arrows-clockwise",
                "cash" => "coins",
                "dream" => "star-four",
                "npc" => "star-four",
                "energy" => "lightning",
                "lobby" => "squares-four",
                "seat" => "armchair",
                "terminal" => "desktop",
                "mic_stage" => "microphone-stage",
                "counter" => "coffee",
                "door" => "door-open",
                "stand" => "person-simple-walk",
                _ => "dots-three",
            };
            if (!Textures.TryGetValue(asset, out var texture))
            {
                texture = Resources.Load<Texture2D>("UI/Icons/" + asset);
                Textures.Add(asset, texture);
                if (texture == null) Log.Warn("ui", "Missing bundled icon: " + asset);
            }
            if (texture != null) style.backgroundImage = Background.FromTexture2D(texture);
        }
    }
}
