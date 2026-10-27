using CTLife.Core;
using CTLife.UI;
using CTLife.UI.DS;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>The one toast channel (SYSTEM.md 3.9): plain text, one at a time, bounded queue, game kinds mapped to tones.</summary>
    public sealed class DsToastTests
    {
        [Test]
        public void Build_RendersPlayerTextLiterally()
        {
            var t = DsToastHost.Build(new DsToastSpec { Text = "<size=200>Player</size>", Detail = "<b>still plain</b>" });
            var text = t.Q<Label>("ds-toast-text");
            var detail = t.Q<Label>("ds-toast-detail");
            Assert.AreEqual("<size=200>Player</size>", text.text);
            Assert.IsFalse(text.enableRichText);
            Assert.AreEqual("<b>still plain</b>", detail.text);
            Assert.IsFalse(detail.enableRichText);
            Assert.AreEqual(PickingMode.Ignore, t.pickingMode, "a toast never captures world input");
        }

        [Test]
        public void Build_NoDetailNoSecondLine()
        {
            var t = DsToastHost.Build(new DsToastSpec { Text = "Saved" });
            Assert.IsNull(t.Q<Label>("ds-toast-detail"));
        }

        [Test]
        public void Host_OneAtATime_QueueIsBoundedAndDeduplicated()
        {
            var layer = new VisualElement();
            var host = new DsToastHost(layer);
            host.Show(new DsToastSpec { Text = "a" });
            Assert.AreEqual("a", host.Showing.Text);
            host.Show(new DsToastSpec { Text = "a" });
            Assert.AreEqual(0, host.Queued, "the same line twice shows once");
            for (int i = 0; i < 8; i++) host.Show(new DsToastSpec { Text = "n" + i });
            Assert.AreEqual(DsToastHost.MaxQueued, host.Queued);
            Assert.AreEqual(1, layer.Q("ds-toast-host").childCount, "one toast on screen");
        }

        [Test]
        public void Spec_MapsKindsToTones()
        {
            Assert.AreEqual(Tone.Accent, ToastLayer.Spec("x", null, ToastKind.Good).Tone);
            Assert.AreEqual(Tone.Danger, ToastLayer.Spec("x", null, ToastKind.Bad).Tone);
            Assert.AreEqual(Tone.Warn, ToastLayer.Spec("x", null, ToastKind.Warning).Tone);
            Assert.AreEqual(Tone.Neutral, ToastLayer.Spec("x", null, ToastKind.Info).Tone);
            Assert.Greater(ToastLayer.Spec("x", null, ToastKind.Event).Seconds, ToastLayer.Spec("x", null, ToastKind.Info).Seconds);
        }
    }
}
