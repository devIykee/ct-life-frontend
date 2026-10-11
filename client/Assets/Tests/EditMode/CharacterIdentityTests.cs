using System;
using System.Linq;
using System.Reflection;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>
    /// A person is not an outfit. Exercise the shipped legacy catalog, exact wire IDs, foreign-look rejection and
    /// interrupted creator drafts without depending on how many independently sourced characters are added later.
    /// </summary>
    public sealed class CharacterIdentityTests
    {
        private static CreatorLookCatalog OriginalCatalog()
        {
            var catalog = Resources.Load<CreatorLookCatalog>("UI/CreatorLookCatalog");
            Assert.IsNotNull(catalog);
            return catalog;
        }

        private static CreatorLookCatalog.Entry Entry(string head, string identity = null) => new CreatorLookCatalog.Entry
        {
            labelKey = "test.character", label = head, identityId = identity,
            look = new[] { "skin_tan", head, "hair_test", "top_test", "bottom_test", "shoes_test", "acc_none" },
        };

        [Test]
        public void OriginalTwentyFourLooks_BecomeTwoPeopleWithoutLosingAnyOutfits()
        {
            var source = OriginalCatalog().looks;
            Assert.AreEqual(24, source.Count, "This assertion covers the original source catalog, not later additions.");
            var models = CustomizerModel.Build(source, null);
            CollectionAssert.AreEquivalent(new[] { "head_snow", "head_rain" }, models.Select(m => m.id));
            Assert.AreEqual(24, models.Sum(m => m.outfits.Count));
            foreach (var outfit in source)
                Assert.AreEqual(1, models.Count(m => m.OwnsLook(outfit.look)), "Every original outfit has exactly one owner.");
            foreach (var model in models)
            {
                Assert.Greater(model.outfits.Count, 1);
                CollectionAssert.Contains(CustomizerModel.Visible(model, CustomizerModel.DefaultSpec(model), null, _ => true),
                    LookCategory.Outfit, "Grouped outfits remain available in the customizer.");
            }
        }

        [Test]
        public void IndependentHeads_StaySeparateEvenWhenBodyFamilyAndClothingMatch()
        {
            var a = Entry("head_new_m_alex");
            var b = Entry("head_new_m_lee");
            var models = CustomizerModel.Build(new[] { a, b }, null);
            Assert.AreEqual(2, models.Count, "A common rig or clothing key cannot establish that two faces are one person.");
            Assert.AreNotEqual(models[0].id, models[1].id);
            Assert.IsTrue(models[0].OwnsLook(a.look));
            Assert.IsFalse(models[0].OwnsLook(b.look));
        }

        [Test]
        public void ExplicitIdentity_OverridesOnlyTheKnownLegacyNameMapping()
        {
            var source = Entry("head_player_m_builder");
            var newPerson = Entry("head_player_m_new_person", "new_distinct_person");
            var models = CustomizerModel.Build(new[] { source, newPerson }, null);
            CollectionAssert.AreEqual(new[] { "head_snow", "new_distinct_person" }, models.Select(m => m.id));
        }

        [Test]
        public void SourceCharacterRemainsDefault_WhenOutfitsAreOrderedFirst()
        {
            var source = OriginalCatalog().looks;
            var reversed = source.AsEnumerable().Reverse().ToArray();
            var models = CustomizerModel.Build(reversed, null);
            foreach (var id in new[] { "head_snow", "head_rain" })
            {
                var model = models.Single(m => m.id == id);
                var original = source.Single(e => e.look[1] == id);
                CollectionAssert.AreEqual(original.look, CustomizerModel.ToContract(CustomizerModel.DefaultSpec(model)));
                Assert.AreEqual(original.previewYaw, model.previewYaw);
            }
        }

        [Test]
        public void EveryAuthoredOutfit_ChangesClothesWithoutChangingIdentityOrWireIds()
        {
            foreach (var model in CustomizerModel.Build(OriginalCatalog().looks, null))
            {
                var initial = CustomizerModel.DefaultSpec(model);
                var initialKey = initial.Key();
                var options = CustomizerModel.AllOptions(model, LookCategory.Outfit, null);
                Assert.AreEqual(model.outfits.Count, options.Count);
                foreach (var option in options)
                {
                    var changed = option.ApplyTo(initial);
                    Assert.AreEqual(model.id, changed.baseId);
                    Assert.AreEqual(7, CustomizerModel.ToContract(changed).Length);
                    CollectionAssert.AreEqual(option.authoredLook, CustomizerModel.ToContract(changed));
                    Assert.IsTrue(model.OwnsLook(changed.legacyLook));
                    Assert.IsTrue(option.IsCurrent(changed));
                    Assert.IsFalse(changed.IsModular);
                    Assert.AreNotSame(option.authoredLook, changed.legacyLook, "A preview must not mutate catalog data.");
                }
                Assert.AreEqual(initialKey, initial.Key(), "Trying outfits does not mutate the previous selection.");
            }
        }

        [Test]
        public void AuthoredOutfit_ClearsStaleModularOverrides()
        {
            var model = CustomizerModel.Build(OriginalCatalog().looks, null).First();
            var current = CustomizerModel.DefaultSpec(model);
            current.parts[PartSlot.Head.ToString()] = "stale_head";
            current.tints[TintRegion.Skin.ToString()] = "stale_skin";
            current.accessories[AccessorySocket.Face.ToString()] = "stale_glasses";
            var option = CustomizerModel.AllOptions(model, LookCategory.Outfit, null).Last();
            var changed = option.ApplyTo(current);
            Assert.IsFalse(changed.IsModular);
            CollectionAssert.AreEqual(option.authoredLook, CustomizerModel.ToContract(changed));
            Assert.IsTrue(current.IsModular, "Applying a new look does not mutate the discarded prior draft.");
        }

        [Test]
        public void ForeignAndUnknownOutfits_AreRejectedBeforeAnyPreviewIsAllocated()
        {
            var models = CustomizerModel.Build(OriginalCatalog().looks, null);
            var snow = models.Single(m => m.id == "head_snow");
            var rain = models.Single(m => m.id == "head_rain");
            Assert.IsTrue(CustomizerModel.ResolvesLook(rain.look), "The foreign outfit is real shipped art, not a missing file.");
            var screen = new CharacterCreatorScreen(new Registries(), _ => { });
            var method = typeof(CharacterCreatorScreen).GetMethod("TryPresent", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            foreach (var look in new[] { rain.look, Entry("head_not_authored").look })
            {
                var forged = new AvatarSpec { baseId = snow.id, legacyLook = (string[])look.Clone() };
                Assert.IsFalse(CustomizerModel.Resolves(forged, snow));
                Assert.IsFalse((bool)method.Invoke(screen, new object[] { snow, forged }),
                    "The screen must reject the foreign selection before touching its uncreated preview.");
            }
            Assert.IsNull(typeof(CharacterCreatorScreen).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(screen));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreviousDraft_RetainsItsExactOutfitWhenMigratedToItsPerson(bool hadSpec)
        {
            var source = OriginalCatalog().looks;
            var outfit = source.First(e => e.look[1] == "head_player_f_kol");
            var model = CustomizerModel.Build(source, null).Single(m => m.id == "head_rain");
            var flow = new CreatorFlow(new CharacterDraft());
            Array.Copy(outfit.look, flow.Draft.look, 7);
            var oldSpec = hadSpec ? new AvatarSpec { baseId = outfit.look[1], legacyLook = (string[])outfit.look.Clone() } : null;
            var persisted = CreatorDraftStore.Parse(CreatorDraftStore.Serialize(flow, outfit.look[1], 35, true, oldSpec));
            var restored = CustomizerModel.RestoreSpec(model, persisted.spec, persisted.look);
            Assert.IsNotNull(restored);
            Assert.AreEqual("head_rain", restored.baseId);
            CollectionAssert.AreEqual(outfit.look, CustomizerModel.ToContract(restored));
            Assert.IsFalse(restored.IsModular);
            Assert.AreEqual(outfit.look[1], persisted.lookKey, "Reading an old draft does not rewrite the persisted source.");
            if (hadSpec) Assert.AreEqual(outfit.look[1], persisted.spec.baseId);
            restored.legacyLook[3] = "modified_in_memory";
            Assert.AreEqual(outfit.look[3], persisted.look[3], "Migration owns its copy of the seven IDs.");
        }

        [Test]
        public void PreviousDraft_CannotUseARealOutfitOwnedByAnotherPerson()
        {
            var models = CustomizerModel.Build(OriginalCatalog().looks, null);
            var snow = models.Single(m => m.id == "head_snow");
            var rain = models.Single(m => m.id == "head_rain");
            var forged = new AvatarSpec { baseId = snow.id, legacyLook = (string[])rain.look.Clone() };
            Assert.IsNull(CustomizerModel.RestoreSpec(snow, forged, snow.look),
                "A forged spec must not silently fall back to an unrelated look array.");
            Assert.IsNull(CustomizerModel.RestoreSpec(snow, null, rain.look));
            Assert.IsNull(CustomizerModel.RestoreSpec(snow, null, Entry("head_not_authored").look));
            Assert.IsNull(CustomizerModel.RestoreSpec(null, forged, rain.look));
        }

        [Test]
        public void ModularDraft_MigratesOnlyWithinItsExistingIdentity()
        {
            var model = CustomizerModel.Build(new[] { Entry("head_unique_human") }, null).Single();
            var spec = CustomizerModel.DefaultSpec(model);
            spec.parts[PartSlot.Hair.ToString()] = "authored_modular_hair";
            var restored = CustomizerModel.RestoreSpec(model, spec, spec.legacyLook);
            Assert.IsNotNull(restored);
            Assert.AreEqual(spec.Key(), restored.Key());
            Assert.AreNotSame(spec.parts, restored.parts);
            spec.baseId = "head_other_person";
            Assert.IsNull(CustomizerModel.RestoreSpec(model, spec, model.look));
        }

        [Test]
        public void IdentityOrder_IsStableAcrossCatalogOrderAndRepeatedReads()
        {
            var source = Enumerable.Range(0, 10).Select(i => Entry("head_person_" + i)).ToArray();
            var forward = CustomizerModel.Build(source, null);
            var backward = CustomizerModel.Build(source.Reverse().ToArray(), null);
            var expected = CreatorLookOrder.ForModels(forward, 20261011).Select(m => m.id).ToArray();
            CollectionAssert.AreEqual(expected, CreatorLookOrder.ForModels(backward, 20261011).Select(m => m.id));
            CollectionAssert.AreEqual(expected, CreatorLookOrder.ForModels(forward, 20261011).Select(m => m.id));
            Assert.Greater(Enumerable.Range(0, 8)
                .Select(seed => string.Join("|", CreatorLookOrder.ForModels(forward, seed).Select(m => m.id)))
                .Distinct().Count(), 1, "Different install seeds can present different first impressions.");
        }

        [Test]
        public void IdentityOrder_DoesNotWeightOutfitCountsOrClothingMetadata()
        {
            var source = new[] { Entry("head_alpha"), Entry("head_beta"), Entry("head_gamma"), Entry("head_delta") };
            var lean = CustomizerModel.Build(source, null);
            var manyOutfits = source.Concat(Enumerable.Range(0, 22)
                .Select(i => Entry("head_alpha_outfit_" + i, "head_alpha"))).Reverse().ToArray();
            var expanded = CustomizerModel.Build(manyOutfits, null);
            Assert.AreEqual(23, expanded.Single(m => m.id == "head_alpha").outfits.Count);
            foreach (var model in expanded)
            {
                model.look[0] = "skin_other";
                model.family = 'x';
                model.species = "shark";
            }
            foreach (int seed in new[] { 0, 1, 20261011, int.MaxValue })
                CollectionAssert.AreEqual(CreatorLookOrder.ForModels(lean, seed).Select(m => m.id),
                    CreatorLookOrder.ForModels(expanded, seed).Select(m => m.id));
        }

        [Test]
        public void IdentityOrder_PreservesWholeGroupsAndLeavesItsSourceUntouched()
        {
            var source = CustomizerModel.Build(OriginalCatalog().looks, null);
            var original = source.ToArray();
            var outfits = source.ToDictionary(m => m.id, m => m.outfits.ToArray());
            var looks = source.ToDictionary(m => m.id, m => m.look);
            var ordered = CreatorLookOrder.ForModels(source, 19);
            Assert.AreNotSame(source, ordered);
            CollectionAssert.AreEqual(original, source, "Ordering does not mutate the caller's list.");
            CollectionAssert.AreEquivalent(original, ordered);
            foreach (var model in ordered)
            {
                Assert.AreSame(original.Single(m => m.id == model.id), model);
                Assert.AreSame(looks[model.id], model.look);
                CollectionAssert.AreEqual(outfits[model.id], model.outfits);
            }
            Assert.IsEmpty(CreatorLookOrder.ForModels(null, 19));
        }

        [TestCase("head_ubc_m", 'm')]
        [TestCase("head_ubc_f", 'f')]
        public void KnownUbcSources_RetainTheirAuthoringFamily(string head, char family)
        {
            Assert.AreEqual(family, CreatorLookOrder.FamilyOf(head));
            Assert.AreEqual('x', CreatorLookOrder.FamilyOf("head_pm_shark"));
        }

        [TestCase("head_snow", CharacterBrowse.Men)]
        [TestCase("head_ubc_m", CharacterBrowse.Men)]
        [TestCase("head_rain", CharacterBrowse.Women)]
        [TestCase("head_ubc_f", CharacterBrowse.Women)]
        [TestCase("head_hero_assassin", CharacterBrowse.Women)]
        [TestCase("head_hero_gynoid", CharacterBrowse.Others)]
        [TestCase("head_hero_hazmat", CharacterBrowse.Others)]
        [TestCase("head_hero_balaclava", CharacterBrowse.Others)]
        [TestCase("head_pm_shark", CharacterBrowse.Others)]
        [TestCase("head_pm_froggy", CharacterBrowse.Others)]
        [TestCase("head_new_unspecified", CharacterBrowse.Others)]
        public void BrowseFiltersPartitionAuthoredCharactersWithoutGuessingFromClothing(string id, CharacterBrowse expected)
        {
            var model = new BaseModel { id = id, family = 'm', species = "human", label = "A dress is not a gender flag" };
            Assert.IsTrue(CustomizerModel.MatchesBrowse(model, CharacterBrowse.All));
            var groups = new[] { CharacterBrowse.Men, CharacterBrowse.Women, CharacterBrowse.Others };
            CollectionAssert.AreEqual(new[] { expected }, groups.Where(g => CustomizerModel.MatchesBrowse(model, g)));
            Assert.IsFalse(CustomizerModel.MatchesBrowse(null, expected));
        }

        [TestCase("head_ubc_m")]
        [TestCase("head_ubc_f")]
        public void PaletteOptionsEditIndependentRegionsAndResetOnlyTheChosenRegion(string id)
        {
            var model = PaletteModel(id);
            var initial = CustomizerModel.DefaultSpec(model);
            var key = initial.Key();
            var skin = CustomizerModel.AllOptions(model, LookCategory.SkinTone, null);
            var top = CustomizerModel.AllOptions(model, LookCategory.Top, null);
            Assert.That(skin.Count, Is.EqualTo(11)); Assert.That(top.Count, Is.EqualTo(17));
            Assert.That(skin.Count(o => o.IsCurrent(initial)), Is.EqualTo(1));
            Assert.IsNull(skin.Single(o => o.IsCurrent(initial)).value, "The original skin is not relabeled as another swatch.");
            var skinChoice = skin.Single(o => o.value == "skin_10");
            var shirtChoice = top.Single(o => o.value == "navy");
            var skinOnly = skinChoice.ApplyTo(initial);
            var combined = shirtChoice.ApplyTo(skinOnly);
            Assert.IsFalse(combined.IsModular, "Masked colors must stay in the persisted seven-ID look.");
            Assert.That(combined.baseId, Is.EqualTo(id));
            Assert.That(combined.legacyLook[0], Is.EqualTo("skin_10"));
            Assert.That(combined.legacyLook[3], Is.EqualTo(model.look[3] + "__navy"));
            Assert.IsTrue(skinChoice.IsCurrent(combined)); Assert.IsTrue(shirtChoice.IsCurrent(combined));
            Assert.IsTrue(model.OwnsLook(combined.legacyLook)); Assert.IsTrue(CustomizerModel.Resolves(combined, model));
            var resetTop = top.Single(o => o.value == null).ApplyTo(combined);
            CollectionAssert.AreEqual(skinOnly.legacyLook, resetTop.legacyLook, "Resetting shirt color must preserve skin selection.");
            Assert.That(initial.Key(), Is.EqualTo(key));
            CollectionAssert.AreEqual(model.look, initial.legacyLook, "The authored source look must stay untouched.");
            CollectionAssert.AreEqual(new[] { LookCategory.SkinTone, LookCategory.Hair, LookCategory.HairColor, LookCategory.Top,
                LookCategory.Bottom, LookCategory.Shoes }, CustomizerModel.Visible(model, combined, null, s => CustomizerModel.Resolves(s, model)));
        }

        [TestCase("head_ubc_m", "head_ubc_f")]
        [TestCase("head_ubc_f", "head_ubc_m")]
        public void PaletteDraftRestoresExactColorsOnlyToItsOwnCharacter(string id, string foreignId)
        {
            var model = PaletteModel(id); var foreign = PaletteModel(foreignId);
            var spec = CustomizerModel.DefaultSpec(model);
            foreach (var choice in new[] { (LookCategory.SkinTone, "skin_08"), (LookCategory.HairColor, "pink"),
                (LookCategory.Top, "teal"), (LookCategory.Bottom, "black"), (LookCategory.Shoes, "white") })
                spec = CustomizerModel.AllOptions(model, choice.Item1, null).Single(o => o.value == choice.Item2).ApplyTo(spec);
            var flow = new CreatorFlow(new CharacterDraft()); Array.Copy(spec.legacyLook, flow.Draft.look, 7);
            var stored = CreatorDraftStore.Parse(CreatorDraftStore.Serialize(flow, id, 42f, false, spec));
            var restored = CustomizerModel.RestoreSpec(model, stored.spec, stored.look);
            Assert.IsNotNull(restored); Assert.IsFalse(restored.IsModular);
            CollectionAssert.AreEqual(spec.legacyLook, CustomizerModel.ToContract(restored));
            Assert.IsTrue(CustomizerModel.Resolves(restored, model));
            Assert.IsFalse(foreign.OwnsLook(restored.legacyLook));
            Assert.IsNull(CustomizerModel.RestoreSpec(foreign, stored.spec, stored.look));
            restored.legacyLook[0] = "skin_01";
            Assert.That(stored.spec.legacyLook[0], Is.EqualTo("skin_08"), "Restoration must own a copy of the stored IDs.");
        }

        [TestCase("head_ubc_m")]
        [TestCase("head_ubc_f")]
        public void HairstyleOptionsKeepColorAndRestoreTheSamePerson(string id)
        {
            var model = PaletteModel(id); var spec = CustomizerModel.DefaultSpec(model);
            var colors = CustomizerModel.AllOptions(model, LookCategory.HairColor, null);
            var colored = colors.Single(o => o.value == "pink").ApplyTo(spec);
            var hair = CustomizerModel.AllOptions(model, LookCategory.Hair, null);
            Assert.That(hair.Count, Is.EqualTo(4));
            Assert.That(hair.Count(o => o.IsCurrent(colored)), Is.EqualTo(1));
            var buzz = hair.Single(o => o.value == "buzzed");
            Assert.IsNotNull(buzz.thumb);
            var changed = buzz.ApplyTo(colored);
            Assert.That(changed.legacyLook[2], Is.EqualTo("hair_" + id.Substring(5) + "_buzzed__pink"));
            Assert.IsTrue(buzz.IsCurrent(changed)); Assert.IsTrue(colors.Single(o => o.value == "pink").IsCurrent(changed));
            Assert.IsTrue(CustomizerModel.Resolves(changed, model));
            var restored = CustomizerModel.RestoreSpec(model, changed, changed.legacyLook);
            Assert.IsNotNull(restored); Assert.That(restored.Key(), Is.EqualTo(changed.Key()));
            var reset = colors.Single(o => o.value == null).ApplyTo(restored);
            Assert.IsTrue(buzz.IsCurrent(reset), "Resetting hair color keeps its selected geometry.");
            Assert.That(spec.legacyLook[2], Is.EqualTo(model.look[2]));
        }

        [TestCase("head_snow")]
        [TestCase("head_rain")]
        public void ColoredHumanOutfitsKeepTheirSelectedTileAndSurviveDraftRestore(string id)
        {
            var model = CustomizerModel.Build(OriginalCatalog().looks, null).Single(m => m.id == id);
            var initial = CustomizerModel.DefaultSpec(model);
            var deep = CustomizerModel.AllOptions(model, LookCategory.SkinTone, null).Single(o => o.value == "skin_10");
            var navy = CustomizerModel.AllOptions(model, LookCategory.Top, null).Single(o => o.value == "navy");
            var colored = navy.ApplyTo(deep.ApplyTo(initial));
            var outfit = CustomizerModel.AllOptions(model, LookCategory.Outfit, null).Last();
            var dressed = outfit.ApplyTo(colored);
            Assert.IsTrue(deep.IsCurrent(dressed)); Assert.IsTrue(navy.IsCurrent(dressed));
            Assert.IsTrue(outfit.IsCurrent(dressed), "Color does not deselect the underlying wardrobe geometry.");
            Assert.IsTrue(model.OwnsLook(dressed.legacyLook));
            var flow = new CreatorFlow(new CharacterDraft()); Array.Copy(dressed.legacyLook, flow.Draft.look, 7);
            var saved = CreatorDraftStore.Parse(CreatorDraftStore.Serialize(flow, id, 35f, false, dressed));
            var restored = CustomizerModel.RestoreSpec(model, saved.spec, saved.look);
            Assert.IsNotNull(restored); Assert.That(restored.Key(), Is.EqualTo(dressed.Key()));
        }

        [Test] public void AnUnsupportedPaletteOptionIsNotReportedSelected()
        {
            var model = PaletteModel("head_ubc_m"); var spec = CustomizerModel.DefaultSpec(model);
            var invalid = new CustomizerOption { kind = OptionKind.Palette, key = "skin", value = "skin_99" };
            Assert.IsFalse(invalid.IsCurrent(spec));
            Assert.That(invalid.ApplyTo(spec).Key(), Is.EqualTo(spec.Key()), "An invalid option cannot corrupt a valid selection.");
        }

        private static BaseModel PaletteModel(string id)
        {
            Assert.IsNotNull(CharacterPaletteCatalog.Load(), "Run the reviewed palette importer before this fixture.");
            var catalog = Resources.Load<CreatorLookCatalog>("UI/CreatorLookAdditions"); Assert.IsNotNull(catalog);
            return CustomizerModel.Build(catalog.looks, null).Single(m => m.id == id);
        }
    }
}
