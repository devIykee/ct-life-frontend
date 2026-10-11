using CTLife.UI;
using CTLife.UI.Map;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>The city HUD rebuild (2026-10-17): every route stays a real control; the minimap is the map route.</summary>
    public class HudMapTests
    {
        [Test]
        public void Hud_HasTheMinimapAsItsMapRoute()
        {
            var hud = new Hud(() => null);
            try
            {
                var map = hud.Element.Q<Button>("hud-map");
                Assert.IsNotNull(map, "hud-map remains a real control");
                Assert.IsNotNull(map.Q<Minimap>(), "the minimap is the map route");
                foreach (var id in new[] { "hud-lobby", "hud-needs", "hud-chat", "hud-emotes", "hud-mic", "hud-phone" })
                    Assert.IsNotNull(hud.Element.Q<Button>(id), id);
                Assert.IsNotNull(hud.Element.Q<Label>(className: "ct-cash"));
                Assert.AreEqual("Duo", hud.Element.Q<Button>("hud-phone").Q<Label>(className: "ct-dock-label").text, "the device is the Solana Duo, as in the lobby");
            }
            finally { hud.Dispose(); }
        }
    }
}
