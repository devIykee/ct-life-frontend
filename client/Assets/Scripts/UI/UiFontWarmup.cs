using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The UI fonts are .ttf files (USS <c>-unity-font-definition: url("Fonts/...ttf")</c>), so UI Toolkit builds a dynamic
    /// SDF font asset for each and adds a glyph the first time some text needs it. Each addition also reads the font's
    /// OpenType kerning (GPOS) tables: on the Seeker that is 50-130 ms (FontEngine.GetPairAdjustmentRecords), so the first
    /// toast, a HUD label's first new digit or a new screen hitched mid-game (perf lane 2, 2026-10-23). At start this lays
    /// out, unseen, every character the game's text uses in each UI font, one font per few frames, so the glyphs and their
    /// kerning are in the atlases before play.
    /// </summary>
    public static class UiFontWarmup
    {
        private static readonly string[] Fonts =
        {
            "UI/Fonts/Manrope-Semibold", "UI/Fonts/Phone/Inter-SemiBold", "UI/Fonts/LilitaOne-Regular", "UI/Fonts/Phone/Inter-Bold",
            "UI/Fonts/Phone/Inter-Medium", "UI/Fonts/Phone/Inter-Regular", "UI/Fonts/Manrope-Regular",
        };

        /// <summary>Printable ASCII plus the other characters the game's strings and UI use (no emoji: they come from the
        /// fallback font either way).</summary>
        public static readonly string Charset = BuildCharset();

        private static string BuildCharset()
        {
            var sb = new System.Text.StringBuilder();
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            sb.Append("·→…—×−é–’≤≈°±¢§“”↔²≥÷∞✓←•τ●↗›✗ü⇄⌈⌉σí€£¥‘™©®%$");
            return sb.ToString();
        }

        public static void Run(VisualElement root)
        {
            if (root == null) return;
            for (int i = 0; i < Fonts.Length; i++)
            {
                string path = Fonts[i];
                root.schedule.Execute(() => Warm(root, path)).StartingIn(100 + i * 120);
            }
        }

        private static void Warm(VisualElement root, string path)
        {
            var font = Resources.Load<Font>(path);
            if (font == null) return;
            var label = new Label(Charset) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.style.unityFontDefinition = FontDefinition.FromFont(font);
            label.style.position = Position.Absolute;
            label.style.left = -20000f; label.style.top = 0f;
            label.style.visibility = Visibility.Hidden;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            root.Add(label);
            // laid out (and its glyphs added) on the next layout pass; then it goes
            label.schedule.Execute(() => label.RemoveFromHierarchy()).StartingIn(250);
        }
    }
}
