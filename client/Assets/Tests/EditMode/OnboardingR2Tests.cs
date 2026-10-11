using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>
    /// Onboarding round 2 (docs/progress/onboarding-ui.md, "Round 2"): plain trait names, the customizer framework and its
    /// step, the v2 draft, the title theme choice, the icon fallback, the 18+ checkbox and the white palette.
    /// </summary>
    public class OnboardingR2Tests
    {
        // ---------------------------------------------------------------- traits

        [Test]
        public void Traits_PlainNames_IdsUnchanged()
        {
            Assert.AreEqual("Diamond Hands", OnboardingCopy.TraitName(new SimpleDef { id = "iron_will", name = "Iron Will" }));
            Assert.AreEqual("Tipster", OnboardingCopy.TraitName(new SimpleDef { id = "sleuth", name = "Sleuth" }));
            Assert.AreEqual("Fast Talker", OnboardingCopy.TraitName(new SimpleDef { id = "fast_talker", name = "x" }));
            Assert.AreEqual("New One", OnboardingCopy.TraitName(new SimpleDef { id = "new_trait", name = "New One" }), "unknown ids keep the registry label");
            Assert.AreEqual("diamond_hands", OnboardingCopy.TraitArt("iron_will"));
            Assert.AreEqual("tipster", OnboardingCopy.TraitArt("sleuth"));
            Assert.AreEqual("early_bird", OnboardingCopy.TraitArt("early_bird"));
        }

        [Test]
        public void Traits_PlainEffects()
        {
            string E(string id, string raw) => OnboardingCopy.TraitEffect(new SimpleDef { id = id, description = raw });
            Assert.AreEqual("Chat cooldown 1.5s instead of 2s", E("fast_talker", "say cooldown 2s -> 1.5s"));
            Assert.AreEqual("Rent penalty halved", E("deep_pockets", "rent penalty 0.5x"));
            Assert.AreEqual("Sanity drains 20% slower", E("iron_will", "Sanity decay 0.8x"));
            Assert.AreEqual("+10% shift pay before noon", E("early_bird", "shift pay +10% before noon"));
            Assert.AreEqual("The detective pays for your tips", E("sleuth", "Sleuth NPC pays you for tips"));
            Assert.AreEqual("A special look on your REKT card", E("rug_veteran", "REKT card visual variant"));
            foreach (var label in new[] { "Iron Will", "Sleuth" })
                foreach (var id in new[] { "iron_will", "sleuth" })
                    Assert.AreNotEqual(label, OnboardingCopy.TraitName(new SimpleDef { id = id, name = label }));
        }

        // ---------------------------------------------------------------- customizer

        private static CreatorLookCatalog.Entry Entry(string head, string skin = "skin_tan") =>
            new CreatorLookCatalog.Entry { labelKey = "x", label = head, look = new[] { skin, head, "hair_a", "top_a", "bottom_a", "shoes_a", "acc_none" } };

        private static CustomizerCatalog Modular(string baseId)
        {
            var cat = ScriptableObject.CreateInstance<CustomizerCatalog>();
            var hair = new CustomizerCatalog.Palette { id = "hair2" };
            hair.swatches.Add(new CustomizerCatalog.Swatch { id = "black", label = "Black" });
            hair.swatches.Add(new CustomizerCatalog.Swatch { id = "ginger", label = "Ginger" });
            cat.palettes.Add(hair);
            var b = new CustomizerCatalog.Base { id = baseId };
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Head, id = "head_q1", label = "Q1" });
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Hair, id = "hair_bob", label = "Bob" });
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Hair, id = "hair_fade", label = "Fade" });
            b.parts.Add(new CustomizerCatalog.Part { slot = PartSlot.Top, id = "top_hoodie", label = "Hoodie" });
            b.regions.Add(new CustomizerCatalog.Region { region = TintRegion.Skin, paletteId = CustomizerCatalog.DefaultSkinPaletteId, defaultSwatch = "skin_04" });
            b.regions.Add(new CustomizerCatalog.Region { region = TintRegion.Hair, paletteId = "hair2" });
            b.accessories.Add(new CustomizerCatalog.Accessory { socket = AccessorySocket.Face, id = "acc_shades", label = "Shades" });
            b.accessories.Add(new CustomizerCatalog.Accessory { socket = AccessorySocket.Back, id = "acc_backpack", label = "Backpack" });
            cat.bases.Add(b);
            return cat;
        }

        [Test]
        public void Customizer_BaseModelsFromTheCreatorCatalog()
        {
            var models = CustomizerModel.Build(new[] { Entry("head_player_f_kol"), Entry("head_snow") }, null);
            Assert.AreEqual(2, models.Count);
            Assert.AreEqual("head_player_f_kol", models[0].id);
            Assert.AreEqual('f', models[0].family);
            Assert.AreEqual('m', models[1].family);
            var spec = CustomizerModel.DefaultSpec(models[0]);
            Assert.IsFalse(spec.IsModular, "today's cast: whole authored looks, no parts");
            CollectionAssert.AreEqual(models[0].look, CustomizerModel.ToContract(spec), "a whole look maps to itself");
            Assert.AreEqual(0, CustomizerModel.Visible(models[0], spec, null, _ => true).Count, "nothing to customize: step skipped");
        }

        [Test]
        public void Customizer_TakesPartsTintsTenStepSkinAndSockets()
        {
            var e = Entry("head_q1");
            var cat = Modular("head_q1");
            var m = CustomizerModel.Build(new[] { e }, cat)[0];
            var spec = CustomizerModel.DefaultSpec(m, cat);
            Assert.AreEqual("head_q1", spec.Get(PartSlot.Head));
            Assert.AreEqual("hair_bob", spec.Get(PartSlot.Hair), "the first part per slot is the default");
            Assert.AreEqual("skin_04", spec.Get(TintRegion.Skin));
            Assert.AreEqual("black", spec.Get(TintRegion.Hair), "a region without a default takes its palette's first swatch");
            Assert.AreEqual(10, CustomizerModel.AllOptions(m, LookCategory.SkinTone, cat).Count, "the default skin palette has 10 steps");
            Assert.AreEqual(10, CustomizerCatalog.DefaultSkinPalette().swatches.Count);
            var visible = CustomizerModel.Visible(m, spec, cat, _ => true);
            CollectionAssert.AreEqual(new[] { LookCategory.SkinTone, LookCategory.Hair, LookCategory.HairColor, LookCategory.Accessories }, visible,
                "Top has one part (hidden); no Bottom/Shoes/Colours data (hidden)");
            var acc = CustomizerModel.AllOptions(m, LookCategory.Accessories, cat);
            Assert.AreEqual(4, acc.Count, "per socket: none + its accessories");
        }

        [Test]
        public void Customizer_MapsToTheSevenContractIds_ExtrasAside()
        {
            var cat = Modular("head_q1");
            var m = CustomizerModel.Build(new[] { Entry("head_q1") }, cat)[0];
            var spec = CustomizerModel.DefaultSpec(m, cat);
            spec = CustomizerModel.AllOptions(m, LookCategory.HairColor, cat).First(o => o.value == "ginger").ApplyTo(spec);
            spec = CustomizerModel.AllOptions(m, LookCategory.Accessories, cat).First(o => o.value == "acc_backpack").ApplyTo(spec);
            spec = CustomizerModel.AllOptions(m, LookCategory.Accessories, cat).First(o => o.value == "acc_shades").ApplyTo(spec);
            var look = CustomizerModel.ToContract(spec);
            Assert.AreEqual(7, look.Length);
            Assert.AreEqual("skin_04", look[0]);
            Assert.AreEqual("head_q1", look[1]);
            Assert.AreEqual("hair_bob__ginger", look[2], "hair colour rides on the hair id as a variant");
            Assert.AreEqual("top_hoodie", look[3]);
            Assert.AreEqual("acc_shades", look[6], "accId takes the face socket first");
            var extras = CustomizerModel.Extras(spec);
            Assert.AreEqual("acc_backpack", (string)extras["accessories"]["Back"], "other sockets go to the proposed extras");
            var none = CustomizerModel.AllOptions(m, LookCategory.Accessories, cat).First(o => o.key == "Face" && o.value == null);
            Assert.IsNull(none.ApplyTo(spec).Get(AccessorySocket.Face));
            Assert.IsTrue(none.ApplyTo(spec).Get(AccessorySocket.Back) == "acc_backpack");
        }

        [Test]
        public void Customizer_ResolverHidesOptionsWithoutArt()
        {
            var cat = Modular("head_q1");
            var m = CustomizerModel.Build(new[] { Entry("head_q1") }, cat)[0];
            var spec = CustomizerModel.DefaultSpec(m, cat);
            Assert.IsFalse(CustomizerModel.Resolves(spec, m), "Catalog membership alone is not assembled art.");
            var bad = spec.Clone();
            bad.parts["Hair"] = "hair_not_in_catalog";
            Assert.IsFalse(CustomizerModel.Resolves(bad, m));
            var visible = CustomizerModel.Visible(m, spec, cat, s => s.Get(PartSlot.Hair) != "hair_fade");
            CollectionAssert.DoesNotContain(visible, LookCategory.Hair, "only one hair resolves: hidden");
            var round = AvatarSpec.FromJson(spec.ToJson());
            Assert.AreEqual(spec.Key(), round.Key());
        }

        [Test]
        public void Flow_CustomizeIsSkippedWhenUnavailable_AndSharesTheLookSegment()
        {
            var f = new CreatorFlow(new CharacterDraft()) { HasLook = true };
            Assert.IsTrue(f.Next());
            Assert.AreEqual(CreatorStep.Tribe, f.Step, "no customizer data: Look → Tribe");
            Assert.IsTrue(f.Back());
            Assert.AreEqual(CreatorStep.Look, f.Step);
            Assert.IsFalse(f.JumpTo(CreatorStep.Customize));
            f.CustomizeAvailable = true;
            Assert.IsTrue(f.Next());
            Assert.AreEqual(CreatorStep.Customize, f.Step);
            Assert.IsNull(f.CurrentProblem, "customizing is optional");
            Assert.IsTrue(f.Next());
            Assert.AreEqual(CreatorStep.Tribe, f.Step);
            Assert.AreEqual(CreatorFlow.ProgressFor(CreatorStep.Look), CreatorFlow.ProgressFor(CreatorStep.Customize));
            Assert.AreEqual(1, CreatorFlow.ProgressFor(CreatorStep.Tribe));
            Assert.AreEqual(5, CreatorFlow.ProgressFor(CreatorStep.Confirm));
            var g = new CreatorFlow(new CharacterDraft()) { HasLook = true, CustomizeAvailable = false };
            g.Restore(CreatorStep.Customize, CreatorStep.Customize);
            Assert.AreEqual(CreatorStep.Look, g.Step, "a restored Customize step without data falls back to Look");
        }

        [Test]
        public void Draft_V2_KeepsTheCustomizedLookAndSpec()
        {
            var f = new CreatorFlow(new CharacterDraft()) { HasLook = true };
            var look = new[] { "skin_04", "head_q1", "hair_bob__ginger", "top_hoodie", "bottom_a", "shoes_a", "acc_none" };
            System.Array.Copy(look, f.Draft.look, 7);
            var spec = new AvatarSpec { baseId = "head_q1" };
            spec.parts["Hair"] = "hair_bob";
            spec.tints["Hair"] = "ginger";
            spec.accessories["Back"] = "acc_backpack";
            var saved = CreatorDraftStore.Parse(CreatorDraftStore.Serialize(f, "head_q1", 10f, false, spec));
            CollectionAssert.AreEqual(look, saved.look);
            Assert.AreEqual("head_q1", saved.lookKey);
            Assert.AreEqual(spec.Key(), saved.spec.Key());
        }

        // ---------------------------------------------------------------- audio, icons, checkbox, colour

        [Test]
        public void TitleTheme_TitleFirst_ThenLobby_ElseSilence()
        {
            Assert.AreEqual("title", OnboardingAudio.ThemeKey(new List<string> { "lobby", "title" }));
            Assert.AreEqual("mus_title", OnboardingAudio.ThemeKey(new List<string> { "lobby", "mus_title" }));
            Assert.AreEqual("lobby", OnboardingAudio.ThemeKey(new List<string> { "timeline", "lobby" }));
            Assert.IsNull(OnboardingAudio.ThemeKey(new List<string> { "timeline" }));
            Assert.IsNull(OnboardingAudio.ThemeKey(null));
        }

        [Test]
        public void Icons_MissingArtFallsBackToTheDrawnBadge()
        {
            UI.Kit.KitIcons.Reset();
            Assert.IsNull(UI.Kit.KitIcons.Load("definitely_not_an_icon"));
            Assert.AreEqual("explore", UI.Kit.KitIcons.ForRoute("map"));
            Assert.AreEqual("me", UI.Kit.KitIcons.ForRoute("profile"));
            Assert.AreEqual("phone", UI.Kit.KitIcons.ForRoute(null));
            var tile = new UI.Kit.IconTile("map", "Explore", "cyan", null, sfx: null, art: "definitely_not_an_icon");
            Assert.IsNotNull(tile.Q(className: "kit-badge__glyph"), "drawn badge while the art is missing");
            var chip = new UI.Kit.StatChip("definitely_not_a_stat", "cash", "gold", "CASH", null);
            Assert.IsNotNull(chip.Q(className: "kit-badge__glyph"));
        }

        [Test]
        public void AgeCheckbox_IsAPlainCheckboxWithItsText()
        {
            int toggles = 0;
            var row = new UI.Kit.CheckRow("I'm 18 or older", () => toggles++);
            Assert.IsNotNull(row.Q(className: "kit-check__box"));
            Assert.AreEqual("I'm 18 or older", row.Q<UnityEngine.UIElements.Label>(className: "kit-check__label").text);
            row.Press();
            Assert.AreEqual(1, toggles);
            row.SetChecked(true);
            Assert.IsTrue(row.Checked);
            Assert.IsTrue(row.ClassListContains("kit-check--on"));
        }

        [Test]
        public void Palette_NoCreamLeft_GreenIsSolana()
        {
            foreach (var file in new[] { "kit.uss", "onboarding.uss", "home.uss", "title.uss" })
            {
                var text = File.ReadAllText(Path.Combine(Application.dataPath, "Resources/UI", file));
                StringAssert.DoesNotContain("rgb(255, 246, 228)", text, file + " still has the cream");
                if (file != "title.uss") StringAssert.DoesNotContain("rgb(92, 196, 74)", text, file + " still has the old green");
            }
            StringAssert.Contains("rgb(20, 241, 149)", File.ReadAllText(Path.Combine(Application.dataPath, "Resources/UI/kit.uss")));
        }

        [Test]
        public void Credits_MusicAndSoundNotOneAuthor_AndTheFullNoticeShips()
        {
            Assert.AreEqual("Music & sound credits", Strings.T("credits.music"));
            StringAssert.Contains("spuispuin", Strings.T("credits.musicSummary"));
            StringAssert.Contains("Gregor Quendel", Strings.T("credits.musicSummary"));
            Assert.AreNotEqual("credits.models", Strings.T("credits.models"));
            var notice = Resources.Load<TextAsset>("Audio/Music-Credits");
            Assert.IsNotNull(notice, "the full music & sound notice ships");
            StringAssert.Contains("Kevin MacLeod", notice.text);
        }

        [Test]
        public void RoundTwoCopyExists()
        {
            foreach (var k in new[] { "ob.trait.iron_will", "ob.trait.sleuth", "ob.custom.title", "ob.custom.sub", "ob.custom.empty",
                         "ob.card.band", "ob.say.custom", "ob.cat.skintone", "ob.cat.haircolor", "ob.cat.accessories", "ob.cat.colors" })
                Assert.AreNotEqual(k, Strings.T(k), "missing copy for " + k);
            Assert.AreEqual("Pick your character", Strings.T("ob.look.title"));
        }
    }
}
