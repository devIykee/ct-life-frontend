using System;
using System.Reflection;
using CTLife.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>Title v2 (2026-10-16): real boot steps on the bar, tap-to-continue only once ready, the sign-in sheet without a session.</summary>
    public class TitleScreenTests
    {
        private sealed class TitleTestWindow : EditorWindow { }
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void BootProgressCountsRealStepsOnlyAndNeverGoesBack()
        {
            var b = new TitleBoot();
            Assert.AreEqual(0f, b.Progress);
            Assert.IsFalse(b.Ready);
            Assert.AreEqual("title.status.connecting", b.StatusKey);
            b.Begin(TitleBoot.Step.Config);
            Assert.AreEqual(0f, b.Progress, "starting a step is not progress");
            b.Complete(TitleBoot.Step.Config);
            Assert.AreEqual(0.5f, b.Progress);
            b.Begin(TitleBoot.Step.Session);
            Assert.AreEqual("title.status.signingIn", b.StatusKey);
            b.Fail("offline");
            Assert.AreEqual("offline", b.Error);
            Assert.IsNull(b.StatusKey, "a failure shows the error, not a status");
            Assert.AreEqual(0.5f, b.Progress, "a failure keeps finished steps");
            Assert.IsFalse(b.Ready);
            b.Begin(TitleBoot.Step.Session); // retry
            Assert.IsNull(b.Error);
            b.Complete(TitleBoot.Step.Session);
            Assert.AreEqual(1f, b.Progress);
            Assert.IsTrue(b.Ready);
            Assert.AreEqual("title.tap", b.StatusKey);
        }

        private static (SignInScreen screen, VisualElement root, TitleTestWindow window) Open(Action wallet, Action play)
        {
            var screen = new SignInScreen(wallet, true, false, play);
            var root = (VisualElement)typeof(SignInScreen).GetMethod("Build", Any).Invoke(screen, null);
            var window = ScriptableObject.CreateInstance<TitleTestWindow>();
            window.Show();
            window.rootVisualElement.Add(root);
            return (screen, root, window);
        }

        private static void Tap(SignInScreen s) => typeof(SignInScreen).GetMethod("Continue", Any).Invoke(s, null);

        [Test]
        public void TapDoesNothingWhileLoadingAndShowsTheStatusLine()
        {
            int plays = 0;
            var (s, root, w) = Open(() => { }, () => plays++);
            try
            {
                s.SetLoading(0.5f, "Connecting…");
                Assert.AreEqual("Connecting…", root.Q<Label>("title-status").text);
                Tap(s);
                Assert.AreEqual(0, plays);
                Assert.IsFalse(s.SheetOpen);
                Assert.IsNull(root.Q("title-play"), "the glossy PLAY pill is gone");
            }
            finally { w.Close(); }
        }

        [Test]
        public void ReadyWithASessionContinuesOnTap()
        {
            int plays = 0;
            var (s, root, w) = Open(() => { }, () => plays++);
            try
            {
                s.SetReady(returning: true);
                Assert.AreEqual("TAP TO PLAY", root.Q<Label>("title-status").text);
                Tap(s);
                Assert.AreEqual(1, plays);
                Assert.IsFalse(s.SheetOpen);
            }
            finally { w.Close(); }
        }

        [Test]
        public void ReadyWithoutASessionOpensTheSignInSheetWhoseButtonRunsTheWalletSignIn()
        {
            int wallet = 0, plays = 0;
            var (s, root, w) = Open(() => wallet++, () => plays++);
            try
            {
                s.SetReady(returning: false);
                Tap(s);
                Assert.IsTrue(s.SheetOpen);
                Assert.AreEqual(0, plays);
                var button = root.Q<Button>("title-signin");
                Assert.IsNotNull(button);
                using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); }
                Assert.AreEqual(1, wallet);
                s.SetBusy(true);
                Assert.IsFalse(button.enabledSelf);
                s.SetBusy(false);
                s.ShowError("You cancelled in the wallet.");
                Assert.AreEqual("You cancelled in the wallet.", root.Q<Label>("title-signin-error").text, "sign-in errors show in the sheet");
            }
            finally { w.Close(); }
        }

        [Test]
        public void BootErrorShowsTheRealErrorAndRetry()
        {
            int retries = 0;
            var (s, root, w) = Open(() => { }, () => { });
            try
            {
                s.SetLoading(0.5f, "Signing in…");
                s.ShowBootError("You're offline.", () => retries++);
                Assert.AreEqual("You're offline.", root.Q<Label>("title-error").text);
                var retry = root.Q<Button>("title-retry");
                Assert.AreNotEqual(DisplayStyle.None, retry.style.display.value, "retry is offered");
                using (var e = NavigationSubmitEvent.GetPooled()) { e.target = retry; retry.SendEvent(e); }
                Assert.AreEqual(1, retries);
                Tap(s);
                Assert.IsFalse(s.SheetOpen, "no continue while not ready");
            }
            finally { w.Close(); }
        }
    }
}
