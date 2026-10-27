using CTLife.UI.DS;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>Dialog anatomy (SYSTEM.md 3.1 + the v2 hero slot) and the one modal host's scrim.</summary>
    public sealed class DsDialogTests
    {
        private static DialogSpec Spec() => new DialogSpec { Title = "Shift done", Primary = Ds.Action("Collect", null) };

        [Test]
        public void Art_IsTheFirstThingInTheCard_AndTheHeroSitsUnderIt()
        {
            var spec = Spec();
            spec.Art = Ds.Art(new Texture2D(4, 4), 240f);
            spec.Hero = Ds.Hero("briefcase", Tone.Accent);
            var d = Ds.Dialog(spec);
            var card = d.Q(className: "ds-card");
            Assert.AreEqual("ds-dialog-art", card[0].name);
            Assert.IsTrue(card[1].ClassListContains("ds-hero"));
            Assert.IsTrue(d.ClassListContains("ds-dialog--art"));
            Assert.IsFalse(d.ClassListContains("ds-dialog--hero"), "with art the hero no longer floats above the card");
            Assert.AreEqual(240f, card[0].style.height.value.value);
        }

        [Test]
        public void NoArt_KeepsTheFloatingHero()
        {
            var spec = Spec();
            spec.Hero = Ds.Hero("briefcase", Tone.Accent);
            var d = Ds.Dialog(spec);
            Assert.IsTrue(d.ClassListContains("ds-dialog--hero"));
            Assert.IsNull(d.Q("ds-dialog-art"));
        }

        [Test]
        public void V2_BrandWellDividerAndHeroMark()
        {
            var spec = Spec();
            spec.Brand = BrandTable.McDonalds;
            spec.Hero = Ds.HeroBrand(BrandTable.McDonalds);
            spec.Tiles.Add(Ds.Stat("Time worked", "1 min", "clock"));
            spec.Tiles.Add(Ds.Stat("Earned", "+$0.63", Ds.Coin));
            spec.Progress = Ds.Progress("trophy", "Crew", null, 0.2f, "1", "5");
            spec.ProgressLabel = "Work 5 shifts to get promoted";
            var d = Ds.Dialog(spec);
            Assert.IsTrue(d.ClassListContains("brand-mcdonalds"), "the brand's colours come from its class");
            var well = d.Q("ds-well");
            Assert.IsNotNull(well, "tiles, divider and progress share one well");
            Assert.IsNotNull(well.Q(className: "ds-tiles"));
            Assert.AreEqual("WORK 5 SHIFTS TO GET PROMOTED", well.Q(className: "ds-divider").Q<Label>().text);
            Assert.IsNotNull(d.Q(className: "ds-hero-mark"), "the brand's mark sits in the hero tile");
            Assert.IsNotNull(well.Q(className: "ds-badge-ic"), "a system icon on a stat sits in a brand badge");
            Assert.IsNull(BrandTable.ClassFor("unknown"));
            Assert.AreEqual(BrandTable.McDonalds, BrandTable.ForVenue("rekt_burger"));
        }

        [Test]
        public void Lilita_FallsBackToInterForGlyphsItLacks()
        {
            Assert.IsTrue(Ds.LilitaCovers("Shift done! +$0.63 · 1/5 é £ €"));
            Assert.IsFalse(Ds.LilitaCovers("Привет"), "no Cyrillic");
            Assert.IsFalse(Ds.LilitaCovers("Basement → Hacker House"), "no arrows");
            Assert.IsFalse(Ds.LilitaCovers("₦500"));
            var title = Ds.Title("Привет");
            Assert.IsTrue(title.ClassListContains("ds-font-body"));
            Ds.SetText(title, "gm");
            Assert.IsFalse(title.ClassListContains("ds-font-body"));
            Assert.IsFalse(Ds.Body("Привет").ClassListContains("ds-font-body"), "body text is Inter already");
        }

        [Test]
        public void ModalHost_DimsTheSceneBehindEveryCard()
        {
            var layer = new VisualElement();
            var host = new DsModalHost(layer);
            var scrim = layer.Q("ds-scrim");
            Assert.IsNotNull(scrim);
            Assert.AreEqual(PickingMode.Ignore, scrim.pickingMode, "no modal: the scrim lets the world through");
            host.Show(Ds.Dialog(Spec()));
            Assert.AreEqual(PickingMode.Position, scrim.pickingMode, "a card is up: the scrim blocks the world under it");
            Assert.AreEqual(0, layer.IndexOf(scrim), "the scrim is under the card");
            Assert.Greater(layer.IndexOf(host.CashSlot), layer.IndexOf(scrim), "the Cash pill mirror stays above the scrim");
            Assert.IsTrue(host.IsOpen);
        }
    }
}
