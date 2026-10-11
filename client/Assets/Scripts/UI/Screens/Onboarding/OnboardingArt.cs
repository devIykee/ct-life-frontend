using System.Collections.Generic;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// Painted onboarding art in Resources/UI/Onboarding (import settings: Editor/OnboardingArtImporter.cs; licences:
    /// art/LICENSES.md "Onboarding"). Every loader returns null when a file is missing, and every caller has a fallback,
    /// so art can land later by id (tribe-&lt;id&gt;.png, origin-&lt;outcome&gt;_baby.png) without code changes.
    /// </summary>
    public static class OnboardingArt
    {
        public const string Folder = "UI/Onboarding/";
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static Texture2D _glow;

        /// <summary>The title painting, pre-blurred and dimmed at import time (onboarding-bg.png); the sharp title art otherwise.</summary>
        public static Texture2D Backdrop => Load(Folder + "onboarding-bg") ?? Load("UI/Title/title-keyart-b");

        public static Texture2D CardBack => Load(Folder + "origin-card-back");
        public static Texture2D PlayerCardSurface => Load(Folder + "player-card-surface");

        /// <summary>Finished landscape origin surface; text and resource values stay live.</summary>
        public static Texture2D OriginSurface(string outcomeId) => string.IsNullOrEmpty(outcomeId) ? null : Load(Folder + "origin-surface-" + outcomeId);

        /// <summary>Card face for a lottery outcome id ("insider", "exit_liquidity", "seeker").</summary>
        public static Texture2D CardFront(string outcomeId) => string.IsNullOrEmpty(outcomeId) ? null : Load(Folder + "origin-" + outcomeId + "_baby");

        /// <summary>Painted tribe badge by registry id (tribe-trencher.png …).</summary>
        public static Texture2D TribeBadge(string tribeId) => string.IsNullOrEmpty(tribeId) ? null : Load(Folder + "tribe-" + tribeId);

        private static Texture2D Load(string path)
        {
            if (Cache.TryGetValue(path, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(path);
            if (t != null) Cache[path] = t;
            return t;
        }

        /// <summary>
        /// A soft radial glow (white, smooth alpha falloff): Resources/UI/Kit/soft-glow.png (128², shared with the kit's
        /// puffs). Used for the character's spotlight and the lottery's hot-card ring, tinted per use with
        /// -unity-background-image-tint-color. A code-made fallback covers a missing file.
        /// </summary>
        public static Texture2D Glow
        {
            get
            {
                if (_glow != null) return _glow;
                _glow = Resources.Load<Texture2D>("UI/Kit/soft-glow");
                if (_glow != null) return _glow;
                const int n = 64;
                _glow = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ob_glow", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                        float a = 1f - d;
                        a = a * a * (3f - 2f * a);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                _glow.SetPixels32(px);
                _glow.Apply(false, true);
                return _glow;
            }
        }
    }
}
