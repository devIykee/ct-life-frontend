using System.Linq;
using CTLife.UI;
using NUnit.Framework;

namespace CTLife.Tests
{
    /// <summary>Coordinator 2026-10-27: creature / robot looks stay in the project but the picker offers only the realistic cast.</summary>
    public sealed class CreatorLookHiddenTests
    {
        [Test]
        public void Picker_OffersOnlyHumanLooks_HiddenOnesStillResolve()
        {
            var offered = CreatorLookCatalog.Available();
            Assert.IsNotEmpty(offered);
            Assert.IsTrue(offered.All(e => string.IsNullOrEmpty(e.species) || e.species == "human"), "no creature or robot in the picker");
            Assert.IsFalse(offered.Any(e => e.look[1] == "head_pm_shark"));
            Assert.IsTrue(CreatorLookCatalog.Available(true).Any(e => e.look[1] == "head_pm_shark"), "the asset and its look stay");
        }
    }
}
