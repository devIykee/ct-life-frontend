using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Onboarding round 2 (`-ctlife-captureSet r2`, output ct-life-evidence/onboarding-r2/): bakes the base-model portraits
    /// for the Character grid, then captures white UI, plain trait names, the glossy icons (where present), the Character
    /// grid, the customizer framework (demo data, since today's cast has no per-slot parts), the player card with the 18+
    /// checkbox, the celebration, the home, and the kit; and records which title-music set would play.
    /// </summary>
    internal static class OnboardingCaptureR2
    {
        public const string PortraitAssets = "Assets/Resources/UI/Customizer/Portraits/";

        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait, Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            const string seedKey = "ctlife.creator.lookSeed";
            var draftKeys = new HashSet<string> { "ctlife.creator.draft.local" };
            if (Services.TryGet<PlayerProfile>(out var profile) && !string.IsNullOrEmpty(profile.UserId))
                draftKeys.Add("ctlife.creator.draft." + profile.UserId);
            var drafts = draftKeys.ToDictionary(key => key, key => (present: PlayerPrefs.HasKey(key), value: PlayerPrefs.GetString(key)));
            bool hadSeed = PlayerPrefs.HasKey(seedKey);
            int seed = PlayerPrefs.GetInt(seedKey);
            var motionField = typeof(DeviceFeel).GetField("_reducedMotionOverride", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingFieldException("DeviceFeel reduced-motion capture override not found");
            var motionOverride = (bool?)motionField.GetValue(null);
            var owned = new List<UiScreen>();
            try { await RunScreens(shot, wait, expect, step, receipt, owned); }
            finally
            {
                try
                {
                    // Close before restoring preferences: no fixture creator may save over the restored draft on pause.
                    foreach (var screen in owned.AsEnumerable().Reverse()) screen.Close();
                }
                finally
                {
                    foreach (var draft in drafts)
                    {
                        if (draft.Value.present) PlayerPrefs.SetString(draft.Key, draft.Value.value);
                        else PlayerPrefs.DeleteKey(draft.Key);
                    }
                    if (hadSeed) PlayerPrefs.SetInt(seedKey, seed);
                    else PlayerPrefs.DeleteKey(seedKey);
                    DeviceFeel.OverrideReducedMotion(motionOverride);
                    PlayerPrefs.Save();
                    bool restored = drafts.All(d => PlayerPrefs.HasKey(d.Key) == d.Value.present &&
                        (!d.Value.present || PlayerPrefs.GetString(d.Key) == d.Value.value)) &&
                        PlayerPrefs.HasKey(seedKey) == hadSeed && (!hadSeed || PlayerPrefs.GetInt(seedKey) == seed) &&
                        Equals(motionField.GetValue(null), motionOverride);
                    receipt["fixturePreferencesRestored"] = restored;
                    expect(restored, "capture restores the pre-existing draft, look seed and reduced-motion override");
                }
            }
        }

        private static async Task RunScreens(Func<string, float, Task> shot, Func<float, Task> wait, Action<bool, string> expect, Action<string> step, JObject receipt, List<UiScreen> owned)
        {
            var config = Services.Get<ConfigStore>();
            UI.Kit.KitIcons.Reset();
            await BakePortraits(step, receipt);

            // Audio: which set the title/onboarding theme resolves to with the shipped map.
            var mapFile = Resources.Load<TextAsset>("Audio/sfx-map");
            var keys = mapFile != null && JObject.Parse(mapFile.text)["music"] is JObject music
                ? music.Properties().Select(p => p.Name).ToList() : new System.Collections.Generic.List<string>();
            receipt["titleMusic"] = OnboardingAudio.ThemeKey(keys) ?? "(none)";
            receipt["musicSets"] = new JArray(keys);
            step("title music → " + receipt["titleMusic"]);

            // 1. Title (white text).
            var title = new SignInScreen(() => { }, true);
            owned.Add(title);
            title.Show();
            await shot("01-title-white", 1.0f);
            title.Close();

            // 2. Lottery result (white UI, Solana green).
            var exit = new PostLotteryRoll200Response(PostLotteryRoll200Response.OutcomeEnum.ExitLiquidity, 30000, 72000, "parents_basement");
            var lottery = new LotteryScreen(false, async () => { await Awaitable.WaitForSecondsAsync(0.3f); return exit; }, _ => { });
            owned.Add(lottery);
            lottery.Show();
            await shot("02-lottery-idle-white", 0.8f);
            DeviceFeel.OverrideReducedMotion(true);
            lottery.PressCta();
            await wait(1.6f);
            await shot("03-lottery-result-white", 1.6f);
            DeviceFeel.OverrideReducedMotion(null);
            lottery.Close();

            // 3. Creator, seed 0: the Character grid, then Customize skipped (today's data), traits, card.
            new CreatorDraftStore(null).Clear();
            CreatorLookOrder.SetInstallSeed(0);
            int submits = 0;
            CharacterCreatorScreen screen = null;
            screen = new CharacterCreatorScreen(config.Registries, _ => { submits++; screen.SetBusy(true); }, serverLookConfirmed: _ => true)
            { Origin = OriginSummary.From(300000, 0, "studio", config.Config("lottery")) };
            owned.Add(screen);
            screen.Show();
            await shot("10-character-grid", 1.6f);
            screen.PickForAutomation(3);
            await shot("11-character-picked", 1.2f);
            screen.SetFaceForAutomation(true);
            await shot("12-character-face-zoom", 1.0f);
            screen.SetFaceForAutomation(false);
            screen.PressCta();
            await wait(0.4f);
            expect(screen.Step == CreatorStep.Tribe, "Customize is skipped when the model has no customizer data (today's cast)");
            screen.PickForAutomation(0);
            screen.PressCta();
            await shot("13-traits-plain-names", 1.0f);
            screen.PickForAutomation(2);
            screen.PickForAutomation(4);
            screen.PressCta();
            screen.PickForAutomation(1);
            screen.PressCta();
            screen.TypeAlias("gm_anon");
            screen.PressCta();
            await shot("14-confirm-player-card", 1.4f);
            expect(screen.Step == CreatorStep.Confirm, "reached Confirm");
            screen.PickForAutomation(0);
            await shot("15-confirm-card-18-checked", 0.6f);
            screen.PressCta();
            expect(submits == 1, "START MY STORY submits once 18+ is ticked");
            screen.ShowError(Strings.Error("ALIAS_TAKEN", "Alias is taken."));
            screen.SetBusy(false);
            await shot("16-confirm-card-error", 0.7f);
            var celebrate = screen.Celebrate();
            await shot("17-celebration", 2.2f);
            screen.PressEnterCity();
            await Task.WhenAny(celebrate, Task.Delay(3000));
            screen.Close();

            // 4. The customizer framework with demo data on one model (art for these options does not exist yet).
            new CreatorDraftStore(null).Clear();
            screen = new CharacterCreatorScreen(config.Registries, _ => { }, serverLookConfirmed: _ => true);
            owned.Add(screen);
            var first = CreatorLookOrder.ForCreator(CreatorLookCatalog.Available(), 0).FirstOrDefault();
            screen.ConfigureCustomizer(DemoCatalog(first?.look), _ => true);
            screen.Show();
            await wait(0.6f);
            screen.PressCta();
            await shot("20-customize-skin-tone", 1.2f);
            expect(screen.Step == CreatorStep.Customize, "Customize shows when the model has options");
            CheckCustomizerLayout(screen, expect, receipt, "skin", true);
            await CheckRejectedWordWidths(screen, wait, expect, receipt);
            screen.SetFaceForAutomation(true);
            await shot("20b-customize-face-zoom", 0.8f);
            CheckCustomizerLayout(screen, expect, receipt, "skin-face", true);
            screen.SetFaceForAutomation(false);
            screen.PickForAutomation(2);
            await shot("21-customize-skin-picked", 0.8f);
            screen.SelectCategoryForAutomation(1);
            await shot("22-customize-hair", 0.8f);
            screen.SelectCategoryForAutomation(2);
            await shot("23-customize-hair-colour", 0.8f);
            screen.SelectCategoryForAutomation(4);
            await shot("24-customize-accessories", 0.8f);
            CheckCustomizerLayout(screen, expect, receipt, "accessories", false);
            screen.SelectCategoryForAutomation(5);
            await shot("25-customize-colours", 0.8f);
            CheckCustomizerLayout(screen, expect, receipt, "colours", false);
            screen.Close();
            new CreatorDraftStore(null).Clear();

            // 5. Home (white chips) with debt.
            var profile = Services.TryGet<PlayerProfile>(out var existing) ? existing : Services.Register(new PlayerProfile(null));
            typeof(PlayerProfile).GetProperty("Me").SetValue(profile, HomeMe());
            Bus.Publish(new ProfileChangedSignal());
            var home = new DashboardScreen(() => { }, _ => { });
            owned.Add(home);
            home.Show();
            Bus.Publish(new MoodSignal { bps = 420 });
            await shot("30-home-white", 1.6f);
            home.Close();
            step("r2 set done");
        }

        private static void CheckCustomizerLayout(CharacterCreatorScreen screen, Action<bool, string> expect, JObject receipt, string category, bool allOptions)
        {
            var root = screen.Element;
            var rail = root.Q<ScrollView>(className: "ob-rail-scroll");
            var selected = root.Q<Button>(className: "ob-rail-item--on");
            expect(selected != null && Contains(rail.contentViewport.worldBound, selected.worldBound),
                "selected customizer category stays inside the rail viewport");
            // Only the active page has nonzero geometry; search that page rather than an earlier hidden heading.
            var heading = root.Query<VisualElement>(className: "ob-page").ToList()
                .First(p => p.resolvedStyle.display != DisplayStyle.None).Q(className: "ob-title-row");
            expect(!root.Q(className: "ob-bubble").worldBound.Overlaps(heading.worldBound),
                "customizer speech bubble does not cover the heading");
            var stage = root.Q(className: "ob-stage").worldBound;
            var zoom = root.Q(className: "ob-zoom-col").worldBound;
            expect(!stage.Overlaps(zoom) && !zoom.Overlaps(root.Q(className: "ob-right").worldBound),
                "customizer zoom controls do not intrude on the preview or choices");
            var cards = root.Query<VisualElement>(className: "ob-option-card").ToList();
            var textFits = receipt["customizerTextFit"] as JArray;
            if (textFits == null) receipt["customizerTextFit"] = textFits = new JArray();
            receipt["textFitMethod"] = "whole-words-and-actual-layout-bounds-v3";
            foreach (var button in root.Query<Button>(className: "ob-rail-item").ToList())
                CheckTextFit(button.Q<Label>(className: "ob-rail-label"), button, category, expect, textFits);
            foreach (var card in cards)
                CheckTextFit(card.Q<Label>(className: "kit-card__title"), card.Q(className: "kit-card__face"), category, expect, textFits);
            if (!allOptions) return;
            var viewport = root.Q<ScrollView>(className: "ob-option-scroll").contentViewport.worldBound;
            expect(cards.Count == 10 && cards.All(c => Contains(viewport, c.worldBound)),
                "all ten skin swatches fit inside the palette viewport");
            expect(cards.Where(c => c.ClassListContains("kit-card--selected"))
                .All(c => Contains(viewport, c.Q(className: "kit-card__check").worldBound)),
                "selected skin checkmark stays inside the palette viewport");
        }

        private static void CheckTextFit(Label label, VisualElement container, string category, Action<bool, string> expect, JArray records)
        {
            // Bounds alone permit a label to wrap halfway through a word. Measure the actual font at its current size:
            // whole words must fit the available line width; natural breaks such as "Hair colour" remain allowed.
            float width = label.contentRect.width;
            float longest = LongestWordWidth(label);
            bool wordsFit = width > 0 && longest <= width + 0.5f;
            // MeasureTextSize(AtMost, contentRect.width) is a new preferred-size calculation, not the rendered label:
            // a sub-pixel width roundoff can make it wrap an already correctly laid-out single word onto two lines.
            bool labelFits = Contains(container.worldBound, label.worldBound);
            records.Add(new JObject { ["category"] = category, ["text"] = label.text, ["availableWidth"] = width,
                ["longestWordWidth"] = longest, ["availableHeight"] = label.contentRect.height,
                ["labelBounds"] = Bounds(label.worldBound), ["containerBounds"] = Bounds(container.worldBound),
                ["wholeWordsFit"] = wordsFit, ["labelFitsContainer"] = labelFits });
            expect(wordsFit, "customizer label does not split a word: " + label.text);
            expect(labelFits, "customizer label stays inside its card: " + label.text);
        }

        private static float LongestWordWidth(Label label) => label.text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => label.MeasureTextSize(word, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x)
            .DefaultIfEmpty(0f).Max();

        private static JArray Bounds(Rect rect) => new JArray(rect.xMin, rect.yMin, rect.xMax, rect.yMax);

        private static async Task CheckRejectedWordWidths(CharacterCreatorScreen screen, Func<float, Task> wait,
            Action<bool, string> expect, JObject receipt)
        {
            // Negative control: the exact horizontal allocations from the visually rejected v1 layout. This changes
            // only fixture element styles, never the USS asset, and restores them before the next actual screenshot.
            var right = screen.Element.Q(className: "ob-right");
            var rail = screen.Element.Q(className: "ob-rail");
            var railScroll = screen.Element.Q<ScrollView>(className: "ob-rail-scroll");
            var left = right.style.left;
            var railWidth = rail.style.width;
            var scrollWidth = railScroll.style.width;
            try
            {
                right.style.left = 920f;
                rail.style.width = 330f;
                railScroll.style.width = 370f;
                await wait(0.15f);
                var records = new JArray();
                foreach (var text in new[] { "Porcelain", "Chestnut", "Espresso", "Accessories" })
                {
                    var label = screen.Element.Query<Label>().ToList().First(l => l.text == text && l.contentRect.width > 0);
                    float width = label.contentRect.width;
                    float longest = LongestWordWidth(label);
                    bool rejected = longest > width + 0.5f;
                    records.Add(new JObject { ["text"] = text, ["availableWidth"] = width,
                        ["longestWordWidth"] = longest, ["rejectedByWordFit"] = rejected });
                    expect(rejected, "word-fit check rejects the observed v1 split: " + text);
                }
                receipt["rejectedV1WidthControl"] = records;
            }
            finally
            {
                right.style.left = left;
                rail.style.width = railWidth;
                railScroll.style.width = scrollWidth;
                await wait(0.15f);
            }
        }

        private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 0.5f &&
            inner.yMin >= outer.yMin - 0.5f && inner.xMax <= outer.xMax + 0.5f && inner.yMax <= outer.yMax + 0.5f;

        /// <summary>
        /// Bakes each creator look's head-and-shoulders portrait (160×200, transparent) into Resources/UI/Customizer/Portraits:
        /// rendered at 3x and downsampled. Re-run after a cast import. The importer applies ASTC 6x6.
        /// </summary>
        private static async Task BakePortraits(Action<string> step, JObject receipt)
        {
            var models = CustomizerModel.Build(CreatorLookCatalog.Available(), null);
            Directory.CreateDirectory(Path.GetFullPath(PortraitAssets));
            var preview = new AvatarPreview(480, 600, transparent: true);
            var small = new RenderTexture(160, 200, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var read = new Texture2D(160, 200, TextureFormat.RGBA32, false, false);
            int baked = 0;
            try
            {
                foreach (var m in models)
                {
                    preview.SetIdle("idle_fidget_foldarms");
                    preview.SetLook(m.look);
                    preview.SetPortrait(true);
                    preview.SetYaw(m.previewYaw - 18f);
                    for (int i = 0; i < 12; i++) await Awaitable.NextFrameAsync();
                    preview.Camera.Render();
                    Graphics.Blit(preview.Texture, small);
                    var prev = RenderTexture.active;
                    RenderTexture.active = small;
                    read.ReadPixels(new Rect(0, 0, 160, 200), 0, 0, false);
                    read.Apply(false);
                    RenderTexture.active = prev;
                    var path = PortraitAssets + CustomizerModel.Safe(m.id) + ".png";
                    File.WriteAllBytes(Path.GetFullPath(path), read.EncodeToPNG());
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    baked++;
                }
            }
            finally
            {
                preview.Dispose();
                small.Release();
                UnityEngine.Object.Destroy(small);
                UnityEngine.Object.Destroy(read);
            }
            receipt["portraits"] = baked;
            step("baked " + baked + " portraits");
        }

        /// <summary>
        /// Demo data for one model in the shape the multi-model cast will use: parts per slot (hair, top), tint regions (skin
        /// with the 10-step palette, hair, top, accent), accessories per socket (face, head). Ids are placeholders; no art.
        /// </summary>
        private static CustomizerCatalog DemoCatalog(string[] look)
        {
            var cat = ScriptableObject.CreateInstance<CustomizerCatalog>();
            if (look == null) return cat;
            CustomizerCatalog.Palette Pal(string id, params (string id, string label, string hex)[] sw)
            {
                var p = new CustomizerCatalog.Palette { id = id };
                foreach (var x in sw) { ColorUtility.TryParseHtmlString(x.hex, out var c); p.swatches.Add(new CustomizerCatalog.Swatch { id = x.id, label = x.label, color = c }); }
                return p;
            }
            cat.palettes.Add(CustomizerCatalog.DefaultSkinPalette());
            cat.palettes.Add(Pal("hair6", ("black", "Black", "#17120f"), ("brown", "Brown", "#4f3322"), ("auburn", "Auburn", "#6e3420"),
                ("ginger", "Ginger", "#a2532a"), ("blonde", "Blonde", "#deba6e"), ("pink", "Pink", "#f08cc0")));
            cat.palettes.Add(Pal("cloth6", ("purple", "Purple", "#9258dc"), ("mint", "Mint", "#14f195"), ("navy", "Navy", "#24305e"),
                ("red", "Red", "#d8434d"), ("white", "White", "#ffffff"), ("black", "Black", "#222222")));
            var b = new CustomizerCatalog.Base { id = CustomizerModel.BaseId(look) };
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Hair, id = "hair_demo_bun", label = "Bun" });
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Hair, id = "hair_demo_fade", label = "Fade" });
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Hair, id = "hair_demo_curls", label = "Curls" });
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Top, id = "top_demo_hoodie", label = "Hoodie" });
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Top, id = "top_demo_tee", label = "Tee" });
            b.regions.Add(new CustomizerCatalog.Region { region = TintRegion.Skin, paletteId = CustomizerCatalog.DefaultSkinPaletteId, defaultSwatch = "skin_05" });
            b.regions.Add(new CustomizerCatalog.Region { region = TintRegion.Hair, paletteId = "hair6", defaultSwatch = "black" });
            b.regions.Add(new CustomizerCatalog.Region { region = TintRegion.Top, paletteId = "cloth6", defaultSwatch = "purple" });
            b.regions.Add(new CustomizerCatalog.Region { region = TintRegion.Accent, paletteId = "cloth6", defaultSwatch = "mint" });
            b.accessories.Add(new CustomizerCatalog.Accessory { socket = AccessorySocket.Face, id = "acc_demo_shades", label = "Shades" });
            b.accessories.Add(new CustomizerCatalog.Accessory { socket = AccessorySocket.Head, id = "acc_demo_cap", label = "Cap" });
            cat.bases.Add(b);
            return cat;
        }

        private static Me HomeMe()
        {
            var l = CreatorLookCatalog.Available().FirstOrDefault()?.look ?? new[] { "skin_tan", "head_x", "h", "t", "b", "s", "acc_none" };
            var body = new JObject
            {
                ["user"] = new JObject { ["id"] = "01a11860-9c1f-77a4-a496-17038bb21f01", ["wallet"] = "HAoo9696iSShm1io2SsnU6R7XhAn8y1YH7sEHgFajESf",
                    ["isSeeker"] = false, ["skrName"] = null },
                ["character"] = new JObject
                {
                    ["alias"] = "gm_anon", ["tribeId"] = "trencher", ["traitIds"] = new JArray("fast_talker", "iron_will"), ["dreamId"] = "make_10k",
                    ["look"] = new JObject { ["skinTone"] = l[0], ["headId"] = l[1], ["hairId"] = l[2], ["topId"] = l[3], ["bottomId"] = l[4],
                        ["shoesId"] = l[5], ["accId"] = l[6] },
                },
                ["needs"] = new JObject { ["energy"] = 60, ["hunger"] = 50, ["fun"] = 50, ["hygiene"] = 50, ["sanity"] = 40 },
                ["skills"] = new JObject { ["clout"] = 1, ["coding"] = 1, ["trading"] = 1, ["writing"] = 1, ["design"] = 1, ["security"] = 1 },
                ["cashCents"] = 30000, ["debtCents"] = 72000, ["homeId"] = "parents_basement", ["minerTier"] = "none", ["flags"] = new JObject(),
            };
            return Json.Deserialize<Me>(MeCompat.Normalize(body).ToString());
        }
    }
}
