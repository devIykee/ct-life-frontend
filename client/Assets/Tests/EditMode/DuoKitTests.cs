using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CTLife.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>Solana Duo iOS redesign (2026-10-16): text size persistence, notification queue, badges.</summary>
    public class DuoKitTests
    {
        private IDuoPrefs _saved;
        private DuoPrefs.Memory _prefs;

        [SetUp]
        public void SetUp()
        {
            _saved = DuoPrefs.Store;
            _prefs = new DuoPrefs.Memory();
            DuoPrefs.Store = _prefs;
        }

        [TearDown]
        public void TearDown() => DuoPrefs.Store = _saved;

        private static DuoNotification Dm(string peer, string text = "gm") =>
            new DuoNotification { AppId = "messages", ThreadKey = peer, Title = peer, Body = text };

        // ------------------------------------------------------------------ text size

        [Test]
        public void TextSize_DefaultsToLarge_AndPersists()
        {
            Assert.AreEqual(DuoTextSize.L, DuoText.Current);
            DuoTextSize? seen = null;
            Action<DuoTextSize> h = s => seen = s;
            DuoText.Changed += h;
            try
            {
                DuoText.Set(DuoTextSize.XXL);
                Assert.AreEqual(DuoTextSize.XXL, DuoText.Current);
                Assert.AreEqual((int)DuoTextSize.XXL, _prefs.Values[DuoText.PrefKey]);
                Assert.Greater(_prefs.Saves, 0, "the size is saved to disk at once");
                Assert.AreEqual(DuoTextSize.XXL, seen);
                seen = null;
                DuoText.Set(DuoTextSize.XXL);
                Assert.IsNull(seen, "setting the same size raises no change");
            }
            finally { DuoText.Changed -= h; }
        }

        [Test]
        public void TextSize_CorruptPreferenceFallsBackToLarge()
        {
            _prefs.Values[DuoText.PrefKey] = 42;
            Assert.AreEqual(DuoTextSize.L, DuoText.Current);
            _prefs.Values[DuoText.PrefKey] = -1;
            Assert.AreEqual(DuoTextSize.L, DuoText.Current);
        }

        [Test]
        public void TextSize_ApplyKeepsExactlyOneRootClass()
        {
            var root = new VisualElement();
            DuoText.Apply(root, DuoTextSize.S);
            DuoText.Apply(root, DuoTextSize.XL);
            Assert.IsTrue(root.ClassListContains("duo-size-xl"));
            Assert.AreEqual(1, root.GetClasses().Count(c => c.StartsWith("duo-size-", StringComparison.Ordinal)));
        }

        [Test]
        public void TextSize_UssTableMatchesScales()
        {
            // duo-kit.uss carries one token block per size; each must equal round(Large × scale).
            var uss = File.ReadAllText(Path.Combine(Application.dataPath, "Resources/UI/duo-kit.uss"));
            var large = new[] { ("large-title", 66), ("title2", 44), ("title3", 38), ("headline", 33), ("body", 33), ("subhead", 29), ("footnote", 26), ("caption", 23) };
            for (int i = 0; i < DuoText.Count; i++)
            {
                var size = (DuoTextSize)i;
                string selector = size == DuoTextSize.L ? @"^\.ct-phone \{" : @"^\.ct-phone\." + DuoText.ClassName(size) + @" \{";
                var line = uss.Split('\n').FirstOrDefault(l => Regex.IsMatch(l, selector) && l.Contains("--duo-body"));
                Assert.IsNotNull(line, "token block for " + size);
                foreach (var (token, px) in large)
                {
                    var m = Regex.Match(line, "--duo-" + token + @": (\d+)px;");
                    Assert.IsTrue(m.Success, size + " defines --duo-" + token);
                    Assert.AreEqual(DuoText.Px(px, size), int.Parse(m.Groups[1].Value), size + " --duo-" + token);
                }
            }
        }

        // ------------------------------------------------------------------ banners

        [Test]
        public void Queue_StacksNewestFirst_OnlyFrontClockRuns()
        {
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            Assert.AreEqual(DuoPostResult.Shown, q.Post(Dm("a"), 0f));
            Assert.AreEqual(DuoPostResult.Shown, q.Post(Dm("b"), 1f));
            Assert.AreEqual("b", q.Stack[0].ThreadKey);
            Assert.AreEqual("a", q.Stack[1].ThreadKey);
            Assert.IsTrue(float.IsPositiveInfinity(q.Stack[1].Deadline), "the peeking banner is paused");
            Assert.IsFalse(q.Tick(4.9f));
            Assert.IsTrue(q.Tick(5f), "front banner leaves 4 s after it came forward");
            Assert.AreEqual("a", q.Top.ThreadKey);
            Assert.AreEqual(9f, q.Top.Deadline, 1e-4, "came forward with a fresh 4 s");
            Assert.IsFalse(q.Tick(8.9f));
            Assert.IsTrue(q.Tick(9f));
            Assert.IsNull(q.Top);
        }

        [Test]
        public void Queue_SameConversationReplaces_AndStackIsCapped()
        {
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            q.Post(Dm("a", "one"), 0f);
            Assert.AreEqual(DuoPostResult.Replaced, q.Post(Dm("a", "two"), 1f));
            Assert.AreEqual(1, q.Stack.Count);
            Assert.AreEqual("two", q.Top.Body);
            for (int i = 0; i < 10; i++) q.Post(Dm("p" + i), 2f + i);
            Assert.AreEqual(DuoNotificationQueue.MaxStack, q.Stack.Count);
            Assert.AreEqual("p9", q.Top.ThreadKey);
        }

        [Test]
        public void Queue_DismissFrontPromotesNext()
        {
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            q.Post(Dm("a"), 0f);
            q.Post(Dm("b"), 1f);
            Assert.IsTrue(q.Dismiss(q.Top.Id, 2f));
            Assert.AreEqual("a", q.Top.ThreadKey);
            Assert.AreEqual(6f, q.Top.Deadline, 1e-4);
            Assert.IsFalse(q.Dismiss(9999, 2f));
        }

        [Test]
        public void Queue_SuppressedWhileDuoShowsThatAppOrThread()
        {
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            q.SetForeground(true, "messages");           // inbox in front
            Assert.AreEqual(DuoPostResult.SuppressedForeground, q.Post(Dm("a"), 0f));
            q.SetForegroundThread("messages", "a");       // chat with a open
            Assert.AreEqual(DuoPostResult.SuppressedForeground, q.Post(Dm("a"), 0f));
            Assert.AreEqual(DuoPostResult.Shown, q.Post(Dm("b"), 0f), "another conversation still banners");
            q.SetForeground(true, "settings");
            Assert.AreEqual(DuoPostResult.Shown, q.Post(Dm("a"), 0f));
            q.SetForeground(false, null);
            Assert.AreEqual(DuoPostResult.Replaced, q.Post(Dm("a"), 0f), "Duo closed: banners show");
        }

        [Test]
        public void Queue_OpeningTheAppRemovesItsBanners()
        {
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            q.Post(Dm("a"), 0f);
            q.Post(new DuoNotification { AppId = "calls", Title = "Call" }, 0f);
            q.Post(Dm("b"), 0f);
            q.SetForeground(true, "messages");
            Assert.AreEqual(1, q.Stack.Count, "opening Messages clears its banners; other apps' stay");
            Assert.AreEqual("calls", q.Top.AppId);
            q.Tick(1f);
            Assert.AreEqual(5f, q.Top.Deadline, 1e-4, "the banner that came forward starts its clock on the next tick");
        }

        [Test]
        public void Queue_DoNotDisturbAndSwitchesSilenceBannersOnly()
        {
            var settings = new DuoNotificationSettings();
            var q = new DuoNotificationQueue(settings);
            settings.DoNotDisturb = true;
            Assert.IsTrue(_prefs.Values.ContainsKey(DuoNotificationSettings.DndKey), "DND persists");
            Assert.AreEqual(DuoPostResult.SuppressedDoNotDisturb, q.Post(Dm("a"), 0f));
            q.SetBadge("messages", 3);
            Assert.AreEqual(3, q.Badge("messages"), "badges still count under DND");
            settings.DoNotDisturb = false;
            settings.Banners = false;
            Assert.AreEqual(DuoPostResult.SuppressedDisabled, q.Post(Dm("a"), 0f));
            settings.Banners = true;
            settings.SetAppBanners("messages", false);
            Assert.AreEqual(DuoPostResult.SuppressedDisabled, q.Post(Dm("a"), 0f));
            Assert.IsFalse(new DuoNotificationSettings().AppBanners("messages"), "per-app switch persists");
            settings.SetAppBanners("messages", true);
            Assert.AreEqual(DuoPostResult.Shown, q.Post(Dm("a"), 0f));
        }

        [Test]
        public void Queue_RetitleUpdatesALiveBanner()
        {
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            q.Post(Dm("a"), 0f);
            int changes = 0, rev = q.Revision;
            q.Changed += () => changes++;
            Assert.IsTrue(q.Retitle(q.Top.Id, "kemi.sol"));
            Assert.AreEqual("kemi.sol", q.Top.Title);
            Assert.AreEqual(rev + 1, q.Revision);
            Assert.AreEqual(1, changes);
            Assert.IsFalse(q.Retitle(q.Top.Id, "kemi.sol"), "same title is no change");
            Assert.IsFalse(q.Retitle(9999, "x"));
        }

        [Test]
        public void Queue_RejectsEmptyNotifications()
        {
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            Assert.AreEqual(DuoPostResult.Rejected, q.Post(null, 0f));
            Assert.AreEqual(DuoPostResult.Rejected, q.Post(new DuoNotification { AppId = "messages" }, 0f));
            Assert.AreEqual(DuoPostResult.Rejected, q.Post(new DuoNotification { Title = "x" }, 0f));
        }

        [Test]
        public void Banner_TextIsLiteralAndNothingInsideIsTappable()
        {
            // Rule 4 of the 2026-10-16 security review: rich text stays off and links are never tappable.
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            var layer = new VisualElement();
            var banners = new DuoBannerLayer(layer, q);
            try
            {
                const string body = "<color=red><b>claim</b></color> https://evil.example/<link=\"x\">here</link>";
                q.Post(new DuoNotification { AppId = "messages", ThreadKey = "p", Title = "<size=99>kemi</size>", Body = body }, 0f);
                var card = layer.Q("duo-banner");
                Assert.IsNotNull(card);
                var labels = card.Query<Label>().ToList();
                Assert.IsTrue(labels.Count > 0 && labels.All(l => !l.enableRichText), "every banner label renders literally");
                Assert.IsTrue(labels.Any(l => l.text == body), "the body is shown exactly as sent, tags and all");
                Assert.IsTrue(labels.Any(l => l.text == "<size=99>kemi</size>"));
                Assert.AreEqual(0, card.Query<Button>().ToList().Count, "no link or control inside the banner");
                Assert.IsTrue(card.Query<VisualElement>().ToList().Where(e => e != card).All(e => e.pickingMode == PickingMode.Ignore),
                    "only the card itself takes input (tap opens the Duo, swipe dismisses)");
            }
            finally { banners.Dispose(); }
        }

        // ------------------------------------------------------------------ badges

        [Test]
        public void Badges_CountTotalAndClearWhenRead()
        {
            var q = new DuoNotificationQueue(new DuoNotificationSettings());
            int changes = 0;
            q.BadgesChanged += () => changes++;
            q.SetBadge("messages", 2);
            q.SetBadge("jobs", 1);
            Assert.AreEqual(3, q.TotalBadge);
            q.SetBadge("messages", 2);
            Assert.AreEqual(2, changes, "an unchanged count raises nothing");
            q.SetBadge("messages", 0);
            Assert.AreEqual(0, q.Badge("messages"));
            Assert.AreEqual(1, q.TotalBadge);
            q.SetBadge("jobs", -4);
            Assert.AreEqual(0, q.TotalBadge, "negative counts clamp to zero");
            q.SetBadge("messages", 5);
            q.ClearBadges();
            Assert.AreEqual(0, q.TotalBadge);
        }

        [Test]
        public void Badges_TextAndElement()
        {
            Assert.AreEqual("", DuoNotificationQueue.BadgeText(0));
            Assert.AreEqual("7", DuoNotificationQueue.BadgeText(7));
            Assert.AreEqual("99+", DuoNotificationQueue.BadgeText(120));
            var badge = Duo.Badge(0);
            Assert.AreEqual(DisplayStyle.None, badge.style.display.value);
            Duo.SetBadge(badge, 4);
            Assert.AreEqual("4", badge.text);
            Assert.AreEqual(DisplayStyle.Flex, badge.style.display.value);
            Assert.IsFalse(badge.enableRichText);
        }

        // ------------------------------------------------------------------ wave 2

        [Test]
        public void Timeframes_OnlyWindowsTheDataReallyCovers()
        {
            const long min = 60_000L;
            Assert.IsEmpty(DuoTimeframes.Available(0, 0, 1), "one point: no pills");
            Assert.IsEmpty(DuoTimeframes.Available(0, 10 * min, 11), "10 minutes of data: a single view, no pills");
            CollectionAssert.AreEqual(new[] { "15m", "All" }, DuoTimeframes.Available(0, 40 * min, 41));
            CollectionAssert.AreEqual(new[] { "15m", "1H", "4H", "All" }, DuoTimeframes.Available(0, 5 * 60 * min, 301));
            Assert.AreEqual(60 * min, DuoTimeframes.WindowMs("1H"));
            Assert.AreEqual(long.MaxValue, DuoTimeframes.WindowMs("All"));
        }

        [Test]
        public void LineChart_DropsInvalidPointsAndReportsDirection()
        {
            var chart = new DuoLineChart();
            chart.SetData(new[] { 1.0, double.NaN, 2.0, double.PositiveInfinity, 3.0 });
            Assert.AreEqual(3, chart.Count);
            Assert.IsTrue(chart.Rising);
            chart.SetData(new[] { 3.0, 1.0 });
            Assert.IsFalse(chart.Rising);
            chart.SetData(null);
            Assert.AreEqual(0, chart.Count);
        }

        [Test]
        public void Pill_IsALargeTargetAroundALiteralFace()
        {
            int taps = 0;
            var pill = Duo.Pill("<b>Work</b>", () => taps++, Duo.PillKind.Primary);
            var label = pill.Q<Label>(className: "duo-pill-label");
            Assert.AreEqual("<b>Work</b>", label.text);
            Assert.IsFalse(label.enableRichText);
            Assert.AreEqual("", pill.text, "the caption is the face label, not the button text");
            Assert.IsTrue(pill.ClassListContains("duo-pill-primary"));
            Duo.SetPillText(pill, "Busy");
            Assert.AreEqual("Busy", label.text);
            Assert.AreEqual("Busy", pill.tooltip);
        }

        // ------------------------------------------------------------------ time labels

        [Test]
        public void Time_SeparatorsAndStamps()
        {
            Assert.IsTrue(DuoTime.NeedsSeparator(0, 1000, true));
            Assert.IsFalse(DuoTime.NeedsSeparator(0, DuoTime.GapMs, false));
            Assert.IsTrue(DuoTime.NeedsSeparator(0, DuoTime.GapMs + 1, false));
            var now = new DateTime(2026, 10, 16, 22, 40, 0);
            Assert.AreEqual("22:38", DuoTime.ListStamp(new DateTime(2026, 10, 16, 22, 38, 0), now));
            Assert.AreEqual("Today 22:31", DuoTime.ThreadStamp(new DateTime(2026, 10, 16, 22, 31, 0), now));
            Assert.AreEqual(DateTime.Parse("2026-10-13").ToString("ddd", System.Globalization.CultureInfo.InvariantCulture),
                DuoTime.ListStamp(new DateTime(2026, 10, 13, 9, 0, 0), now));
        }
    }
}
