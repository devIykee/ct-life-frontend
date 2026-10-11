using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Required third-party notices ship as TextAssets and can be read without a connection.</summary>
    internal static class AssetCredits
    {
        public static VisualElement Build()
        {
            var entries = El.Div("ct-credits");
            entries.Show(false);
            var toggle = El.Btn(Strings.T("credits.title"), () => entries.Show(entries.resolvedStyle.display == DisplayStyle.None), "ct-row-btn");
            entries.Add(Credit("credits.snow", "credits.snowSummary", "https://studio.blender.org/characters/snow/v4/",
                "UI/Licenses/Snow", "UI/Licenses/CC-BY-4.0"));
            entries.Add(Credit("credits.rain", "credits.rainSummary", "https://studio.blender.org/characters/rain/v3/",
                "UI/Licenses/Rain", "UI/Licenses/CC-BY-4.0"));
            entries.Add(Credit("credits.manrope", "credits.manropeSummary", "https://github.com/googlefonts/manrope",
                "UI/Fonts/OFL"));
            entries.Add(Credit("credits.lilita", "credits.lilitaSummary", "https://github.com/google/fonts/tree/main/ofl/lilitaone",
                "UI/Fonts/LilitaOne-OFL"));
            entries.Add(Credit("credits.phosphor", "credits.phosphorSummary", "https://github.com/phosphor-icons/core",
                "UI/Icons/LICENSE"));
            entries.Add(Credit("credits.music", "credits.musicSummary", "https://incompetech.com/music/royalty-free/",
                "Audio/Music-Credits", "UI/Licenses/CC-BY-4.0"));
            return El.Div("ct-col", toggle, entries);
        }

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
            return El.Div("ct-credit", El.Text(Strings.T(title), "ct-credit-title"),
                El.Text(Strings.T(summary), "ct-small ct-muted"),
                El.Btn(Strings.T("credits.source"), () => Application.OpenURL(source), "ct-btn ct-btn-ghost"), show, content);
        }
    }
}
