using System.IO;
using CTLife.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>The Arena lobby (2026-10-16): Cash formatting, its art and Icons8 credit, copy, and the drawn pieces.</summary>
    public class LobbyArenaTests
    {
        private static readonly string[] Icons =
            { "treasure-map", "briefcase", "bank", "shop", "coins", "lightning-bolt", "brain", "crown", "calendar", "chat", "smartphone",
              // Map and city HUD (2026-10-17)
              "door", "laptop", "microphone", "center-direction", "search", "multiply", "crane", "home-page", "place-marker", "speech-bubble", "smiling" };

        [Test]
        public void Cash_ExactBelowTenThousand_CompactAbove()
        {
            Assert.AreEqual("$0.00", LobbyAmounts.Cash(0));
            Assert.AreEqual("$303.50", LobbyAmounts.Cash(30350));
            Assert.AreEqual("$9,999.99", LobbyAmounts.Cash(999999));
            Assert.AreEqual("$10K", LobbyAmounts.Cash(1000000));
            Assert.AreEqual("$1.23B", LobbyAmounts.Cash(123456789000L));
            Assert.AreEqual("-$720.00", LobbyAmounts.Cash(-72000));
        }

        [Test]
        public void Art_ShipsAndEveryIcons8IconIsCreditedWithItsUrl()
        {
            Assert.IsNotNull(Resources.Load<Texture2D>("UI/Lobby/lobby-plaza"), "plaza backdrop");
            Assert.IsNotNull(Resources.Load<Texture2D>("UI/Lobby/invite-silhouette"), "invite silhouette");
            Assert.IsNull(Resources.Load<Texture2D>("UI/Onboarding/lobby-rooftop"), "the old painted rooftop left the build");
            var notice = Resources.Load<TextAsset>("UI/Licenses/Icons8");
            Assert.IsNotNull(notice, "Icons8 notice ships with the credits");
            StringAssert.Contains("https://icons8.com", notice.text);
            foreach (var name in Icons)
            {
                var tex = Resources.Load<Texture2D>("UI/Lobby/Icons/" + name);
                Assert.IsNotNull(tex, name);
                Assert.LessOrEqual(Mathf.Max(tex.width, tex.height), 256, name);
                StringAssert.Contains("https://img.icons8.com/fluent/256/" + name + ".png", notice.text, name + " source URL");
            }
            // Every icon file in the folder is one the notice credits.
            foreach (var file in Directory.GetFiles("Assets/Resources/UI/Lobby/Icons", "*.png"))
                CollectionAssert.Contains(Icons, Path.GetFileNameWithoutExtension(file), file);
        }

        [Test]
        public void ArenaCopy_Exists()
        {
            foreach (var k in new[] { "lobby.invite", "lobby.duo", "lobby.shop", "lobby.owe", "lobby.messages", "lobby.week", "lobby.wallet.none",
                         "lobby.wallet.title", "lobby.wallet.open", "home.today.marketLive", "credits.icons8", "credits.icons8Summary" })
                Assert.AreNotEqual(k, Strings.T(k), "missing copy for " + k);
            Assert.AreEqual("Invite fren", Strings.T("lobby.invite"));
        }

        [Test]
        public void InviteFren_OpensDuoCalls()
        {
            Assert.AreEqual("calls", DashboardScreen.InviteApp);
        }

        [Test]
        public void Sticker_TiltsAndCarriesAHiddenBadge()
        {
            int taps = 0;
            var s = new LobbySticker("treasure-map", "Explore", () => taps++, -4f);
            Assert.AreEqual(-4f, s.Q(className: "lobby-sticker-tile").style.rotate.value.angle.value, 0.001f);
            Assert.AreEqual("Explore", s.Q<Label>(className: "lobby-sticker-label").text);
            Assert.IsNotNull(s.Badge);
            Assert.AreEqual(DisplayStyle.None, s.Badge.style.display.value, "no count, no badge");
        }

        [Test]
        public void SkewBox_KeepsItsShape()
        {
            var box = new SkewBox("a b") { Edge = Color.green }.Shape(14f, 22f);
            Assert.IsTrue(box.ClassListContains("a") && box.ClassListContains("b"));
            Assert.AreEqual(14f, box.SlantLeft);
            Assert.AreEqual(22f, box.SlantRight);
        }

        [Test]
        public void Textures_AreBuiltOnce()
        {
            Assert.AreSame(LobbyArt.Vignette, LobbyArt.Vignette);
            Assert.AreSame(LobbyArt.Podium, LobbyArt.Podium);
            Assert.AreEqual(2, LobbyArt.Podium.width / LobbyArt.Podium.height, "podium is a wide ellipse");
            Assert.IsNotNull(LobbyArt.Gold);
            Assert.IsNotNull(LobbyArt.Sticker);
            Assert.IsNotNull(LobbyArt.StickerDark);
        }
    }
}
