#if UNITY_INCLUDE_TESTS && UNITY_EDITOR && !CTLIFE_PROD
using System.Linq;
using CTLife.World;
using NUnit.Framework;

namespace CTLife.Tests
{
    public sealed class CharacterPaletteTests
    {
        private CharacterPaletteCatalog _catalog;
        [SetUp] public void LoadReviewedCatalog()
        {
            _catalog = CharacterPaletteCatalog.Load();
            Assert.IsNotNull(_catalog, "Run CTLife.EditorTools.CharacterPaletteImport.RunBatch first.");
        }

        [TestCase("head_ubc_m")]
        [TestCase("head_ubc_f")]
        public void EverySupportedSwatchRoundTripsWithoutChangingIdentityOrOtherSlots(string id)
        {
            var source = _catalog.FindBase(id).sourceLook;
            Assert.IsTrue(CharacterPaletteResolver.TryResolve(source, out var baseline));
            Assert.IsFalse(baseline.HasChanges);
            foreach (string region in CharacterPaletteResolver.Regions)
                foreach (var swatch in _catalog.PaletteFor(region).swatches)
                {
                    Assert.IsTrue(CharacterPaletteResolver.TryChange(source, region, swatch.id, out var look), region + swatch.id);
                    Assert.That(look.Zip(source, (a, b) => a == b ? 0 : 1).Sum(), Is.EqualTo(1));
                    Assert.IsTrue(CharacterPaletteResolver.TryResolve(look, out var selected));
                    Assert.IsTrue(selected.HasChanges);
                    Assert.That(selected.Entry.prefab, Is.SameAs(baseline.Entry.prefab));
                    Assert.That(selected.Entry.previewPrefab, Is.SameAs(baseline.Entry.previewPrefab));
                    Assert.That(look[1], Is.EqualTo(id));
                    Assert.That(selected.Entry.key, Is.EqualTo(string.Join("|", look)));
                    Assert.IsTrue(CharacterPaletteResolver.TryChange(look, region, null, out var restored));
                    CollectionAssert.AreEqual(source, restored);
                }
        }

        [TestCase(0, "skin_99")]
        [TestCase(0, "skin_tone999")]
        [TestCase(1, "head_ubc_f")]
        [TestCase(2, "hair_ubc_f__black")]
        [TestCase(3, "top_ubc_m__not_a_color")]
        [TestCase(3, "top_ubc_m__navy__red")]
        [TestCase(4, "bottom_ubc_f")]
        [TestCase(5, "shoes_ubc_m__skin_01")]
        [TestCase(6, "acc_unknown")]
        public void ForeignSlotsAndUnknownSwatchesAreRejected(int slot, string value)
        {
            var look = (string[])_catalog.FindBase("head_ubc_m").sourceLook.Clone(); look[slot] = value;
            Assert.IsFalse(CharacterPaletteResolver.TryResolve(look, out _));
        }

        [Test] public void MultipleColorsSurviveTheSevenIdRepresentation()
        {
            var look = _catalog.FindBase("head_ubc_f").sourceLook;
            foreach (var pair in new[] { ("skin", "skin_10"), ("hair", "platinum"), ("top", "navy"), ("bottom", "denim"), ("shoes", "white") })
            {
                Assert.IsTrue(CharacterPaletteResolver.TryChange(look, pair.Item1, pair.Item2, out var next)); look = next;
            }
            CollectionAssert.AreEqual(new[] { "skin_10", "head_ubc_f", "hair_ubc_f__platinum", "top_ubc_f__navy",
                "bottom_ubc_f__denim", "shoes_ubc_f__white", "acc_none" }, look);
            Assert.IsTrue(CharacterPaletteResolver.TryResolve(string.Join("|", look).Split('|'), out _));
        }

        [TestCase("head_ubc_m")]
        [TestCase("head_ubc_f")]
        public void FittedHairstylesKeepTheSamePersonAndIndependentColors(string id)
        {
            var binding = _catalog.FindBase(id);
            Assert.That(binding.hairStyles.Length, Is.EqualTo(4), "Import the reviewed hairstyles first.");
            Assert.IsTrue(CharacterPaletteResolver.TryResolve(binding.sourceLook, out var original));
            var colored = binding.sourceLook;
            foreach (var pair in new[] { ("skin", "skin_10"), ("hair", "platinum"), ("top", "navy"), ("bottom", "denim"), ("shoes", "white") })
            {
                Assert.IsTrue(CharacterPaletteResolver.TryChange(colored, pair.Item1, pair.Item2, out var next)); colored = next;
            }
            foreach (var style in binding.hairStyles)
            {
                Assert.IsNotNull(style.thumbnail, "Use portraits of the actual fitted mesh.");
                Assert.IsTrue(CharacterPaletteResolver.TryChangeHairStyle(colored, style.id, out var changed));
                Assert.That(changed[2], Is.EqualTo(style.hairId + "__platinum"));
                for (int i = 0; i < 7; i++) if (i != 2) Assert.That(changed[i], Is.EqualTo(colored[i]));
                Assert.IsTrue(CharacterPaletteResolver.TryResolve(changed, out var selected));
                CollectionAssert.AreEqual(binding.sourceLook, selected.IdentityLook);
                Assert.That(selected.SourceLook[2], Is.EqualTo(style.hairId));
                if (style.id != "original")
                {
                    Assert.AreNotSame(original.Entry.prefab, selected.Entry.prefab, "Style must use its actual world geometry.");
                    Assert.AreNotSame(original.Entry.previewPrefab, selected.Entry.previewPrefab);
                    Assert.That(selected.Entry.previewPrefab.GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>().sharedMesh,
                        Is.Not.SameAs(original.Entry.previewPrefab.GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>().sharedMesh));
                }
                Assert.IsTrue(CharacterPaletteResolver.TryChange(changed, "hair", null, out var clearedColor));
                Assert.That(clearedColor[2], Is.EqualTo(style.hairId), "Resetting color must keep the chosen hair geometry.");
                Assert.IsTrue(CharacterPaletteResolver.TryChangeHairStyle(changed, "original", out var restored));
                CollectionAssert.AreEqual(colored, restored);
            }
            Assert.IsFalse(CharacterPaletteResolver.TryChangeHairStyle(colored, "invented", out _));
        }

        [Test] public void HairstyleIdsCannotCrossIdentitiesOrSmuggleUnknownColors()
        {
            var look = (string[])_catalog.FindBase("head_ubc_m").sourceLook.Clone();
            foreach (string hair in new[] { "hair_ubc_f_buns", "hair_ubc_m_buns__invented", "hair_ubc_m_beard", "hair_ubc_m_buns__black__pink" })
            {
                look[2] = hair;
                Assert.IsFalse(CharacterPaletteResolver.TryResolve(look, out _), hair);
            }
        }

        [Test] public void EveryHumanOutfitRetainsItsExactSourceAndPhysicalMapsWhenColored()
        {
            var humans = _catalog.bases.Where(b => b.personId == "head_snow" || b.personId == "head_rain").ToArray();
            Assert.That(humans.Length, Is.EqualTo(24), "Import the source-derived human masks.");
            foreach (var binding in humans)
            {
                Assert.IsTrue(CharacterPaletteResolver.TryResolve(binding.sourceLook, out var original), binding.id);
                foreach (string region in CharacterPaletteResolver.Regions)
                {
                    string swatch = region == "skin" ? "skin_10" : region == "hair" ? "platinum" : "white";
                    Assert.IsTrue(CharacterPaletteResolver.TryChange(binding.sourceLook, region, swatch, out var changed), binding.id + region);
                    Assert.IsTrue(CharacterPaletteResolver.TryResolve(changed, out var selection));
                    Assert.That(selection.Entry.prefab, Is.SameAs(original.Entry.prefab));
                    Assert.That(selection.Entry.previewPrefab, Is.SameAs(original.Entry.previewPrefab));
                    CollectionAssert.AreEqual(binding.sourceLook, selection.IdentityLook);
                }
                Assert.That(binding.maskedMaterial.GetTexture("_BumpMap"), Is.SameAs(binding.sourceMaterial.GetTexture("_BumpMap")));
                Assert.That(binding.maskedMaterial.GetTexture("_MetallicGlossMap"), Is.SameAs(binding.sourceMaterial.GetTexture("_MetallicGlossMap")));
                Assert.That(binding.maskedMaterial.GetFloat("_UseNormalMap"), Is.EqualTo(1f));
                Assert.That(binding.maskedMaterial.GetFloat("_UseMetallicMap"), Is.EqualTo(1f));
            }
        }

        [Test] public void OutfitChangesPreserveThePersonsIndependentColorsAndRejectAnotherPerson()
        {
            var source = _catalog.FindBase("head_snow").sourceLook;
            var target = _catalog.FindBase("head_player_m_trencher").sourceLook;
            Assert.IsTrue(CharacterPaletteResolver.TryChange(source, "skin", "skin_10", out var skin));
            Assert.IsTrue(CharacterPaletteResolver.TryChange(skin, "hair", "platinum", out var hair));
            Assert.IsTrue(CharacterPaletteResolver.TryChange(hair, "top", "navy", out var top));
            Assert.IsTrue(CharacterPaletteResolver.TryChangeOutfit(top, target, out var outfit));
            Assert.That(outfit[0], Is.EqualTo("skin_10"));
            Assert.That(outfit[1], Is.EqualTo(target[1]));
            Assert.That(outfit[2], Is.EqualTo(target[2] + "__platinum"));
            Assert.That(outfit[3], Is.EqualTo(target[3] + "__navy"));
            Assert.That(outfit[4], Is.EqualTo(target[4]));
            Assert.IsFalse(CharacterPaletteResolver.TryChangeOutfit(top, _catalog.FindBase("head_rain").sourceLook, out _));
        }

        [Test] public void BodiesWithoutReviewedMasksHaveNoColorOptions()
        {
            foreach (string id in new[] { "head_pm_shark", "head_pm_froggy", "head_hero_hazmat" })
                Assert.IsNull(_catalog.FindBase(id));
            Assert.IsNull(_catalog.PaletteFor("accent"), "Unsent accent extras must not masquerade as saved options.");
        }
    }
}
#endif
