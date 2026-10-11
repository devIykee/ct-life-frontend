using System.Collections;
using CTLife.Core;
using CTLife.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    public sealed class ToastNoticeTests
    {
        private ToastTestWindow _window;
        private VisualElement _layer;

        [SetUp]
        public void SetUp()
        {
            _window = ScriptableObject.CreateInstance<ToastTestWindow>();
            _window.position = new Rect(20, 20, 820, 620);
            _window.rootVisualElement.AddToClassList("ct-root");
            _window.rootVisualElement.AddToClassList("ct-landscape");
            foreach (string sheet in new[] { "ctlife", "landscape", "toast" })
                _window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/" + sheet));
            _layer = El.Div("layer-toast");
            _layer.style.flexGrow = 1;
            _layer.pickingMode = PickingMode.Ignore;
            _window.rootVisualElement.Add(_layer);
            _window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            DeviceFeel.OverrideReducedMotion(null);
            if (_window != null) _window.Close();
        }

        [UnityTest]
        public IEnumerator DetailIsOptionalAndUntrustedTextRemainsLiteral()
        {
            var text = "<size=200>Player text</size>";
            var single = new ToastNotice(text, null, ToastKind.Info, null);
            var detailed = new ToastNotice("Changes saved", "<b>Still plain text</b>", ToastKind.Good, null);
            _layer.Add(single); _layer.Add(detailed);
            yield return null;
            Assert.AreEqual(text, single.Q<Label>(className: "ct-toast-text").text);
            Assert.IsFalse(single.Q<Label>(className: "ct-toast-text").enableRichText);
            Assert.IsNull(single.Q<Label>(className: "ct-toast-detail"));
            Assert.AreEqual("<b>Still plain text</b>", detailed.Q<Label>(className: "ct-toast-detail").text);
            Assert.IsFalse(detailed.Q<Label>(className: "ct-toast-detail").enableRichText);
        }

        [UnityTest]
        public IEnumerator CloseIsARealButtonAndDecorationDoesNotCaptureWorldInput()
        {
            int dismissed = 0;
            var notice = new ToastNotice("Changes saved", null, ToastKind.Good, () => dismissed++);
            _layer.Add(notice);
            yield return null;
            var close = notice.Q<Button>("toast-dismiss");
            using (var submit = NavigationSubmitEvent.GetPooled())
            { submit.target = close; close.SendEvent(submit); }
            Assert.AreEqual(1, dismissed);
            Assert.AreEqual(PickingMode.Ignore, notice.pickingMode);
            Assert.AreEqual(PickingMode.Ignore, notice.Q(className: "ct-toast-copy").pickingMode);
            Assert.AreEqual(PickingMode.Position, close.pickingMode);
            Assert.GreaterOrEqual(close.layout.width, 56f);
            Assert.GreaterOrEqual(close.layout.height, 56f);
        }

        [UnityTest]
        public IEnumerator LongCopyWrapsInsideCardAndLeavesDismissAccessible()
        {
            var notice = new ToastNotice(
                "The connection was interrupted while your character was entering the city",
                "Check your connection and try again when you are ready.", ToastKind.Bad, null);
            _layer.Add(notice);
            yield return null; yield return null;
            var title = notice.Q<Label>(className: "ct-toast-text");
            var detail = notice.Q<Label>(className: "ct-toast-detail");
            var close = notice.Q<Button>("toast-dismiss");
            Assert.Greater(title.layout.height, 30f);
            Assert.LessOrEqual(title.worldBound.xMax, close.worldBound.xMin);
            Assert.LessOrEqual(detail.worldBound.xMax, close.worldBound.xMin);
            Assert.GreaterOrEqual(detail.worldBound.yMin, title.worldBound.yMax);
            Assert.LessOrEqual(close.worldBound.xMax, notice.worldBound.xMax);
            Assert.LessOrEqual(detail.worldBound.yMax, notice.worldBound.yMax);
        }

        [UnityTest]
        public IEnumerator ReducedMotionShowsTheFinalStateImmediately()
        {
            DeviceFeel.OverrideReducedMotion(true);
            var notice = new ToastNotice("Changes saved", null, ToastKind.Good, null);
            _layer.Add(notice);
            notice.Reveal();
            Assert.AreEqual(1f, notice.style.opacity.value);
            Assert.AreEqual(0f, notice.style.translate.value.y.value);
            yield return null;
        }
    }

    public sealed class ToastTestWindow : EditorWindow { }
}
