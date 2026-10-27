using CTLife.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>The v2 wanted-stars pill: shown with real stars, a danger edge while wanted, hidden at zero.</summary>
    public sealed class HeatPlateV2Tests
    {
        [Test]
        public void Stars_ShowLitAndDangerEdge_HideAtZero()
        {
            var plate = new HeatPlate();
            plate.Set(5, "WANTED", 3, true);
            Assert.AreEqual(DisplayStyle.Flex, plate.Element.style.display.value);
            Assert.IsFalse(plate.Element.ClassListContains("hud-off"));
            Assert.AreEqual(5, plate.Element.Query(className: "ds-star--on").ToList().Count);
            Assert.IsTrue(plate.Element.ClassListContains("ds-hud-pill--danger"));
            plate.Set(0, null, 0, false);
            Assert.AreEqual(0, plate.Element.Query(className: "ds-star--on").ToList().Count);
        }
    }
}
