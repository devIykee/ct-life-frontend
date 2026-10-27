using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>
    /// The design-system guardrail (MIGRATION.md 1.4). Colours, fonts, radii, font sizes and border colours live only in
    /// Resources/UI/DS; feature sheets do layout. Migrated files are ENFORCED (the test fails on a stray value); the rest of
    /// the game is in WARN mode (counted and logged) until the last migration step switches everything to enforced.
    /// </summary>
    public sealed class DsLintTests
    {
        private static readonly Regex UssColour = new Regex(@"#[0-9A-Fa-f]{3,8}\b|\brgba?\s*\(", RegexOptions.CultureInvariant);
        private static readonly Regex UssStyle = new Regex(@"-unity-font-definition|border-radius|font-size|border-color|border-[a-z]+-color|-unity-font\b", RegexOptions.CultureInvariant);
        private static readonly Regex CsColour = new Regex(@"new Color\(|style\.color\s*=|style\.backgroundColor\s*=|style\.borderTopColor|style\.unityFontDefinition", RegexOptions.CultureInvariant);

        /// <summary>Sheets and scripts already migrated: they must be clean. Grows with each migration step.</summary>
        public static readonly string[] EnforcedUss = { };
        public static readonly string[] EnforcedCs =
        {
            "Scripts/UI/DS/",
            // step 1 HUD, step 2 shared overlays, step 3 McDonald's job (2026-10-26)
            "Scripts/UI/Hud/Hud.cs", "Scripts/UI/Hud/LadderChip.cs", "Scripts/UI/Hud/MovementControls.cs",
            "Scripts/UI/Hud/InteractionPrompt.cs", "Scripts/UI/Overlays/ToastLayer.cs",
            "Scripts/Features/CityLife/RektBurger/RektShiftUi.cs",
            // step 4 police (2026-10-27)
            "Scripts/UI/Hud/HeatPlate.cs", "Scripts/Features/CityLife/Heat/HeatOverlays.cs",
        };

        private static string Assets => Application.dataPath;

        private static IEnumerable<string> Uss() => Directory.GetFiles(Path.Combine(Assets, "Resources/UI"), "*.uss", SearchOption.AllDirectories)
            .Select(p => p.Replace('\\', '/')).Where(p => !p.Contains("/Resources/UI/DS/"));

        private static string Rel(string p) => p.Substring(Assets.Length + 1);

        private static int UssViolations(string path, List<string> into)
        {
            int n = 0;
            int line = 0;
            foreach (var raw in File.ReadLines(path))
            {
                line++;
                var l = Regex.Replace(raw, @"/\*.*?\*/", "");
                if (UssColour.IsMatch(l) || UssStyle.IsMatch(l)) { n++; into?.Add($"{Rel(path)}:{line}: {l.Trim()}"); }
            }
            return n;
        }

        [Test]
        public void Tokens_OnlyTheTokenSheetHoldsColourLiterals()
        {
            var comp = File.ReadAllText(Path.Combine(Assets, "Resources/UI/DS/ds-components.uss"));
            comp = Regex.Replace(comp, @"/\*.*?\*/", "", RegexOptions.Singleline);
            var stray = UssColour.Matches(comp).Cast<Match>().Select(m => m.Value).ToList();
            Assert.IsEmpty(stray, "ds-components.uss must use var(--ds-*) for every colour: " + string.Join(", ", stray));
        }

        [Test]
        public void Uss_EnforcedSheetsAreClean_OthersWarn()
        {
            var bad = new List<string>();
            foreach (var rel in EnforcedUss) UssViolations(Path.Combine(Assets, rel), bad);
            int warn = 0;
            var counts = new List<string>();
            foreach (var p in Uss())
            {
                if (EnforcedUss.Any(e => p.EndsWith(e))) continue;
                int n = UssViolations(p, null);
                warn += n;
                if (n > 0) counts.Add($"{Rel(p)} {n}");
            }
            Debug.Log($"CTLIFE [ds-lint] WARN: {warn} style values outside the design system in {counts.Count} sheets: " + string.Join("; ", counts));
            Assert.IsEmpty(bad, "migrated sheets must take colours/fonts/radii/sizes from the design system:\n" + string.Join("\n", bad));
        }

        [Test]
        public void Code_EnforcedScriptsSetNoColours()
        {
            var bad = new List<string>();
            foreach (var dir in EnforcedCs)
            {
                var root = Path.Combine(Assets, dir);
                if (Directory.Exists(root))
                {
                    foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)) Scan(f, bad);
                }
                else if (File.Exists(root)) Scan(root, bad);
            }
            Assert.IsEmpty(bad, "migrated scripts must not set colours or fonts in code (use ds-* classes):\n" + string.Join("\n", bad));
        }

        private static void Scan(string f, List<string> bad)
        {
            int line = 0;
            foreach (var l in File.ReadLines(f))
            {
                line++;
                if (CsColour.IsMatch(l) && !l.Contains("ds-lint: allow")) bad.Add($"{Rel(f.Replace('\\', '/'))}:{line}: {l.Trim()}");
            }
        }

        [Test]
        public void Fonts_LilitaDisplayAndInterBodyOnly()
        {
            // v2 (2026-10-27): Lilita One for display (titles, stat values, buttons, HUD values), Inter for everything else
            var comp = File.ReadAllText(Path.Combine(Assets, "Resources/UI/DS/ds-components.uss"));
            foreach (Match m in Regex.Matches(comp, @"url\(""([^""]+\.ttf)""\)"))
                Assert.IsTrue(m.Groups[1].Value.Contains("/Inter-") || m.Groups[1].Value.EndsWith("/LilitaOne-Regular.ttf"),
                    "the design system uses Lilita One (display) and Inter (body) only: " + m.Groups[1].Value);
        }
    }
}
