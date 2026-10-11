using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Required third-party notices ship as TextAssets and can be read without a connection. Grouped: 3D models (Blender
    /// Studio characters, plus Resources/UI/Licenses/Model-Credits.txt when the cast lane ships its Sketchfab hero models),
    /// music and sound (the full Audio/Music-Credits.txt: every CC-BY music and sound author), type and icons.
    /// </summary>
    internal static class AssetCredits
    {
        /// <param name="expanded">Duo Settings → Credits &amp; Licences shows the notices directly, without the toggle row.</param>
        public static VisualElement Build(bool expanded = false)
        {
            var entries = El.Div("ct-credits");
            entries.Show(expanded);
            var toggle = El.Btn(Strings.T("credits.title"), () => entries.Show(entries.resolvedStyle.display == DisplayStyle.None), "ct-row-btn");
            entries.Add(El.Text(Strings.T("credits.section.models"), "ct-eyebrow"));
            entries.Add(Credit("credits.snow", "credits.snowSummary", "https://studio.blender.org/characters/snow/v4/",
                "UI/Licenses/Snow", "UI/Licenses/CC-BY-4.0"));
            entries.Add(Credit("credits.rain", "credits.rainSummary", "https://studio.blender.org/characters/rain/v3/",
                "UI/Licenses/Rain", "UI/Licenses/CC-BY-4.0"));
            // The multi-model cast's credits (Sketchfab hero authors and others) land as one notice file from the cast lane.
            if (Resources.Load<TextAsset>(ModelCredits) != null)
                entries.Add(Credit("credits.models", "credits.modelsSummary", null, ModelCredits, "UI/Licenses/CC-BY-4.0"));
            // Feel lane F2: vehicle models (two CC BY 3.0) and their CC0 recorded sounds
            entries.Add(Credit("credits.vehicles", "credits.vehiclesSummary", "https://poly.pizza", "UI/Licenses/Vehicle-Credits", "UI/Licenses/CC-BY-3.0"));
            entries.Add(El.Text(Strings.T("credits.section.audio"), "ct-eyebrow"));
            entries.Add(Credit("credits.music", "credits.musicSummary", null, "Audio/Music-Credits", "UI/Licenses/CC-BY-4.0"));
            entries.Add(El.Text(Strings.T("credits.section.type"), "ct-eyebrow"));
            entries.Add(Credit("credits.inter", "credits.interSummary", "https://rsms.me/inter/",
                "UI/Fonts/Phone/Inter-OFL"));
            entries.Add(Credit("credits.manrope", "credits.manropeSummary", "https://github.com/googlefonts/manrope",
                "UI/Fonts/OFL"));
            entries.Add(Credit("credits.lilita", "credits.lilitaSummary", "https://github.com/google/fonts/tree/main/ofl/lilitaone",
                "UI/Fonts/LilitaOne-OFL"));
            entries.Add(Credit("credits.phosphor", "credits.phosphorSummary", "https://github.com/phosphor-icons/core",
                "UI/Icons/LICENSE"));
            entries.Add(Credit("credits.icons8", "credits.icons8Summary", Icons8Url, "UI/Licenses/Icons8"));
            return expanded ? entries : El.Div("ct-col", toggle, entries);
        }

        /// <summary>The Icons8 free licence asks for a link to icons8.com wherever its icons are used (the lobby).</summary>
        public const string Icons8Url = "https://icons8.com";

        /// <summary>Optional notice from the cast lane (one block per model: title, author, source, licence, changes).</summary>
        public const string ModelCredits = "UI/Licenses/Model-Credits";

        private static VisualElement Credit(string title, string summary, string source, params string[] notices)
        {
            var content = El.Div("ct-col");
            content.Show(false);
            bool loaded = false;
            var show = El.Btn(Strings.T("credits.read"), () =>
            {
                if (!loaded)
                {
                    foreach (var path in notices)
                    {
                        var notice = Resources.Load<TextAsset>(path);
                        string text = notice == null ? Strings.T("credits.missing") : notice.text;
                        // Separate paragraphs keep a long license from becoming one enormous text mesh.
                        foreach (string paragraph in text.Replace("\r\n", "\n").Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var label = El.Text(paragraph, "ct-license");
                            label.enableRichText = false;
                            content.Add(label);
                        }
                    }
                    loaded = true;
                }
                content.Show(content.resolvedStyle.display == DisplayStyle.None);
            }, "ct-btn ct-btn-ghost");
            var credit = El.Div("ct-credit", El.Text(Strings.T(title), "ct-credit-title"), El.Text(Strings.T(summary), "ct-small ct-muted"));
            // Notices that list many sources (music & sound, models) carry each source's link in the text instead.
            if (!string.IsNullOrEmpty(source)) credit.Add(El.Btn(Strings.T("credits.source"), () => Application.OpenURL(source), "ct-btn ct-btn-ghost"));
            credit.Add(show);
            credit.Add(content);
            return credit;
        }
    }
}
