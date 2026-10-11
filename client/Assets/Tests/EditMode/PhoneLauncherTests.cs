using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CTLife.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>Exercises the shipping launcher and PhoneShell, including child-button pointer capture.</summary>
    public sealed class PhoneLauncherTests
    {
        private PhoneShell _phone;
        private LauncherTestWindow _window;
        private VisualElement _layer;
        private readonly List<TestApp> _apps = new();
        private PhoneLauncher Launcher => _layer.Q<PhoneLauncher>();

        [SetUp]
        public void SetUp()
        {
            _window = ScriptableObject.CreateInstance<LauncherTestWindow>();
            _window.position = new Rect(20, 20, 1000, 1100);
            foreach (string sheet in new[] { "ctlife", "phone-ui" })
                _window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/" + sheet));
            _window.Show();
            _layer = new VisualElement { style = { flexGrow = 1 } };
            _window.rootVisualElement.Add(_layer);
            _phone = new PhoneShell(_layer);
            _apps.Clear();
            foreach (string id in new[] { "map", "wallet", "messages", "spaces" }) Add(id);
            for (int i = 0; i < 25; i++) Add("test" + i);
        }

        private TestApp Add(string id, int phase = 1)
        {
            var app = new TestApp(id, phase); _apps.Add(app); _phone.Register(app); return app;
        }

        [TearDown]
        public void TearDown()
        {
            _phone?.Dispose();
            if (_window != null) _window.Close();
        }

        [UnityTest]
        public IEnumerator CatalogUsesFourPersistentDockRoutesAndTwoPagesWithoutLosingAnApp()
        {
            _phone.Open(); yield return null;
            Assert.AreEqual(2, Launcher.PageCount);
            var wallpaper = _layer.Q<PhoneWallpaper>();
            Assert.AreEqual(DisplayStyle.Flex, wallpaper.resolvedStyle.display);
            Assert.Greater(wallpaper.layout.width, 0);
            Assert.IsNotNull(wallpaper.resolvedStyle.backgroundImage.texture,
                "the photographic wallpaper must load as a shipping resource");
            var dock = Launcher.Q(className: "ct-phone-dock");
            CollectionAssert.AreEqual(new[] { "phone-app-map", "phone-app-wallet", "phone-app-messages", "phone-app-spaces" },
                dock.Query<Button>(className: "ct-app").ToList().Select(b => b.name).ToArray());
            var all = Launcher.Query<Button>(className: "ct-app").ToList();
            Assert.AreEqual(29, all.Count);
            Assert.AreEqual(29, all.Select(b => b.name).Distinct().Count());
            Assert.AreEqual(16, Launcher.Q("phone-page-0").Query<Button>(className: "ct-app").ToList().Count);
            Assert.AreEqual(9, Launcher.Q("phone-page-1").Query<Button>(className: "ct-app").ToList().Count);
        }

        [UnityTest]
        public IEnumerator PageDotsNavigateAndAppBackPreservesTheSelectedPage()
        {
            _phone.Open(); yield return null;
            Launcher.Q<Button>("phone-app-test0").Focus();
            Submit(Launcher.Q<Button>("phone-page-dot-1"));
            Assert.AreEqual(1, Launcher.PageIndex);
            Assert.AreSame(Launcher.Q<Button>("phone-page-dot-1"), Launcher.panel.focusController.focusedElement);
            Assert.IsFalse(Launcher.Q<Button>("phone-app-test0").focusable);
            Assert.IsTrue(Launcher.Q<Button>("phone-app-test16").focusable);
            Submit(Launcher.Q<Button>("phone-app-test16"));
            Assert.AreEqual(1, _apps.Single(a => a.Id == "test16").Opened);
            _phone.Back();
            Assert.AreEqual(1, Launcher.PageIndex);
            Assert.AreEqual(1, _apps.Single(a => a.Id == "test16").CloseCount);
            Launcher.ShowPage(99, false); Assert.AreEqual(1, Launcher.PageIndex);
            Launcher.ShowPage(-1, false); Assert.AreEqual(0, Launcher.PageIndex);
        }

        [UnityTest]
        public IEnumerator DockShortcutOpensItsRealAppAndHomeControlReturnsThenCloses()
        {
            _phone.Open(); yield return null;
            Submit(Launcher.Q<Button>("phone-app-wallet"));
            Assert.AreEqual(1, _apps.Single(a => a.Id == "wallet").Opened);
            Submit(_layer.Q<Button>("phone-home"));
            Assert.IsTrue(_phone.IsOpen);
            Assert.AreEqual(1, _apps.Single(a => a.Id == "wallet").CloseCount);
            Submit(_layer.Q<Button>("phone-home"));
            Assert.IsFalse(_phone.IsOpen);
        }

        [UnityTest]
        public IEnumerator FuturePhaseRemainsDiscoverableButCannotLaunch()
        {
            var future = Add("future", int.MaxValue);
            _phone.Open(); yield return null;
            var tile = Launcher.Q<Button>("phone-app-future");
            Assert.IsNotNull(tile);
            Assert.IsTrue(tile.ClassListContains("ct-app-locked"));
            Assert.IsNotNull(tile.Q<Label>(className: "ct-app-phase"));
            Submit(tile);
            Assert.AreEqual(0, future.Opened);
            Assert.IsTrue(_phone.IsOpen);
        }

        [UnityTest]
        public IEnumerator HorizontalDragAcrossAnAppChangesPageWithoutLaunchingThenNextTapWorks()
        {
            _phone.Open(); yield return null; yield return null;
            var app = Launcher.Q<Button>("phone-app-test0");
            var start = app.worldBound.center;
            Pointer(app, EventType.MouseDown, start);
            Pointer(app, EventType.MouseDrag, start + new Vector2(-220, 2));
            Assert.IsTrue(Launcher.SuppressClick, "the child Button capture must deliver the drag to the pager");
            yield return null;
            Pointer(app, EventType.MouseUp, start + new Vector2(-220, 2));
            Assert.AreEqual(1, Launcher.PageIndex);
            Assert.AreEqual(0, _apps.Sum(a => a.Opened));
            Assert.IsTrue(Launcher.SuppressClick);
            // A fresh pointer press resets suppression, including a persistent dock icon.
            var wallet = Launcher.Q<Button>("phone-app-wallet");
            Pointer(wallet, EventType.MouseDown, wallet.worldBound.center);
            Pointer(wallet, EventType.MouseUp, wallet.worldBound.center);
            Assert.IsFalse(Launcher.SuppressClick);
            Assert.AreEqual(1, _apps.Single(a => a.Id == "wallet").Opened);
        }

        [UnityTest]
        public IEnumerator VerticalDragDoesNotTurnPageOrLaunchAnApp()
        {
            _phone.Open(); yield return null; yield return null;
            var app = Launcher.Q<Button>("phone-app-test0");
            var start = app.worldBound.center;
            Pointer(app, EventType.MouseDown, start);
            Pointer(app, EventType.MouseDrag, start + new Vector2(4, 180));
            yield return null;
            Pointer(app, EventType.MouseUp, start + new Vector2(4, 180));
            Assert.AreEqual(0, Launcher.PageIndex);
            Assert.AreEqual(0, _apps.Sum(a => a.Opened));
        }

        [UnityTest]
        public IEnumerator WhitespacePressCapturesOutsideReleaseAndCloseReleasesCapture()
        {
            _phone.Open(); yield return null; yield return null;
            var viewport = Launcher.Q(className: "ct-phone-page-viewport");
            var start = viewport.worldBound.min + Vector2.one;
            Pointer(viewport, EventType.MouseDown, start);
            Assert.IsTrue(viewport.HasPointerCapture(PointerId.mousePointerId));
            // Dispatch via the panel with no forced target, as native pointer delivery does.
            Pointer(_window.rootVisualElement, EventType.MouseUp, start - new Vector2(80, 80), false);
            Assert.IsFalse(viewport.HasPointerCapture(PointerId.mousePointerId));
            Pointer(viewport, EventType.MouseDown, start);
            Assert.IsTrue(viewport.HasPointerCapture(PointerId.mousePointerId));
            _phone.Close();
            Assert.IsFalse(viewport.HasPointerCapture(PointerId.mousePointerId));
        }

        [UnityTest]
        public IEnumerator SearchFindsAppAcrossPagesAndOpensItsRoute()
        {
            _phone.Open(); yield return null;
            Launcher.ShowPage(1, false);
            Submit(Launcher.Q<Button>("phone-search"));
            yield return null;
            Assert.IsTrue(Launcher.IsSearching);
            var field = Launcher.Q<TextField>("phone-search-input");
            field.value = "TEST23";
            var results = Launcher.Q<ScrollView>("phone-search-results");
            Assert.AreEqual(1, results.Query<Button>(className: "ct-app").ToList().Count);
            Submit(results.Q<Button>("phone-app-test23"));
            Assert.AreEqual(1, _apps.Single(a => a.Id == "test23").Opened);
            _phone.Back();
            Assert.AreEqual(1, Launcher.PageIndex);
            Assert.IsFalse(Launcher.IsSearching);
        }

        [UnityTest]
        public IEnumerator EmptySearchAndHomeDismissalKeepPhoneAndSavedPage()
        {
            _phone.Open(); yield return null;
            Launcher.ShowPage(1, false);
            Launcher.OpenSearch(); yield return null;
            Launcher.Q<TextField>("phone-search-input").value = "no matching app";
            Assert.IsNotNull(Launcher.Q<Label>(className: "ct-phone-search-empty"));
            Submit(_layer.Q<Button>("phone-home"));
            Assert.IsFalse(Launcher.IsSearching);
            Assert.IsTrue(_phone.IsOpen);
            Assert.AreEqual(1, Launcher.PageIndex);
            Assert.AreEqual(0, Launcher.Q<ScrollView>("phone-search-results").contentContainer.childCount);
        }

        [UnityTest]
        public IEnumerator EscapeFromFocusedSearchInputDismissesSearchWithoutClosingPhone()
        {
            _phone.Open(); yield return null;
            Launcher.OpenSearch(); yield return null; yield return null;
            var field = Launcher.Q<TextField>("phone-search-input");
            field.value = "wallet";
            var target = field.Q<TextElement>();
            Assert.IsNotNull(target);
            using (var key = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            { key.target = target; target.SendEvent(key); }
            Assert.IsFalse(Launcher.IsSearching);
            Assert.IsTrue(_phone.IsOpen);
        }

        [UnityTest]
        public IEnumerator ClosingPhoneDuringSearchCancelsDeferredFocusAndSearchState()
        {
            _phone.Open(); yield return null;
            Launcher.OpenSearch();
            var launcher = Launcher;
            _phone.Close();
            yield return null;
            Assert.IsFalse(launcher.IsSearching);
            Assert.IsFalse(_phone.IsOpen);
            _phone.Open(); yield return null;
            Assert.IsFalse(Launcher.IsSearching);
        }

        private static void Submit(Button button)
        {
            using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e);
        }

        private static void Pointer(VisualElement target, EventType type, Vector2 point, bool forceTarget = true)
        {
            var source = new Event { type = type, button = 0, mousePosition = point };
            if (type == EventType.MouseDown)
            { using var e = PointerDownEvent.GetPooled(source); if (forceTarget) e.target = target; target.SendEvent(e); }
            else if (type == EventType.MouseUp)
            { using var e = PointerUpEvent.GetPooled(source); if (forceTarget) e.target = target; target.SendEvent(e); }
            else
            { using var e = PointerMoveEvent.GetPooled(source); if (forceTarget) e.target = target; target.SendEvent(e); }
        }

        private sealed class TestApp : PhoneApp
        {
            private readonly string _id; private readonly int _phase;
            public TestApp(string id, int phase) { _id = id; _phase = phase; }
            public override string Id => _id;
            public override string Title => _id;
            public override int Phase => _phase;
            public int Opened, CloseCount;
            public override VisualElement Open() { Opened++; return new VisualElement(); }
            public override void Closed() => CloseCount++;
        }
    }
    public sealed class LauncherTestWindow : EditorWindow { }
}
