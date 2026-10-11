#if UNITY_EDITOR && UNITY_INCLUDE_TESTS && !CTLIFE_PROD
// Uses the Unity coroutine test runner; offline compile_check intentionally omits that runner.
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine.TestTools;
using CTLife.UI.Dev;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    // Requires the Unity editor; intentionally not included in the offline pure-data runner.
    public class QaUiSnapshotTreeTests
    {
        [Test]
        public void AudioSnapshotDoesNotReadClipTimeForEmptyPooledSources()
        {
            var root = new GameObject("qa-audio-pool-test");
            var clip = AudioClip.Create("qa-audio-clip", 8000, 1, 8000, false);
            var warnings = new System.Collections.Generic.List<string>();
            var invalidTimeWarning = new System.Text.RegularExpressions.Regex(
                @"Attempting to get\s+`?time`?\s+on an audio source.*not a clip",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            void OnLog(string message, string stack, LogType kind)
            {
                if (invalidTimeWarning.IsMatch(message)) warnings.Add(message);
            }
            try
            {
                var empty = root.AddComponent<AudioSource>();
                var withClip = root.AddComponent<AudioSource>();
                withClip.clip = clip;
                Application.logMessageReceived += OnLog;
                // Calibrate against the old invalid getter so this regression cannot silently pass
                // because Unity's warning wording/format differs from the test's matcher.
                LogAssert.Expect(LogType.Warning, invalidTimeWarning);
                _ = empty.time;
                Assert.That(warnings.Count, Is.EqualTo(1), "The warning observer must detect the old unguarded read.");
                warnings.Clear();
                var capture = typeof(QaUiSnapshotExporter).GetMethod("CaptureAudio", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(capture);
                for (int sample = 0; sample < 2; sample++)
                {
                    var snapshot = new QaUiSnapshot();
                    capture.Invoke(null, new object[] { snapshot });
                    var idle = snapshot.audio.Single(a => a.id == empty.GetInstanceID());
                    Assert.That(idle.clip, Is.Empty);
                    Assert.That(idle.time, Is.Zero);
                    Assert.That(idle.length, Is.Zero);
                    var recorded = snapshot.audio.Single(a => a.id == withClip.GetInstanceID());
                    Assert.That(recorded.clip, Is.EqualTo(clip.name));
                    Assert.That(recorded.length, Is.EqualTo(1f).Within(.001f));
                }
                Assert.That(warnings, Is.Empty, "Read-only QA telemetry must not flood logs for unassigned pooled sources.");
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void HierarchyIncludesCustomTilesAndRedactsInputDescendants()
        {
            var root = new VisualElement();
            var tile = new VisualElement { name = "custom-tile", pickingMode = PickingMode.Position };
            tile.RegisterCallback<ClickEvent>(_ => Assert.Fail("Snapshot must never trigger callbacks"));
            root.Add(tile);
            tile.Add(new Label("ENTER THE CITY"));
            tile.SetEnabled(false);
            root.Add(new TextField("Username") { value = "private-user-value" });
            root.Add(new TextField("Password") { isPasswordField = true, value = "short-private-secret" });
            var scroll = new ScrollView();
            scroll.Add(new Label("Scroll entry"));
            root.Add(scroll);
            var snapshot = QaUiSnapshotExporter.Capture(root, new Rect(0, 0, 100, 200), 1000, 2000);
            var target = snapshot.nodes.Single(n => n.name == "custom-tile");
            Assert.That(target.tapCandidate, Is.True);
            Assert.That(target.disabled, Is.True);
            Assert.That(target.parentId, Is.EqualTo("0"));
            var label = snapshot.nodes.Single(n => n.label == "ENTER THE CITY");
            Assert.That(label.ancestors, Does.Contain(target.id));
            Assert.That(label.disabled, Is.True);
            Assert.That(snapshot.nodes.Any(n => n.type == "ScrollView"), Is.True);
            Assert.That(snapshot.nodes.Any(n => n.label == "Scroll entry"), Is.True);
            Assert.That(snapshot.ToJson(), Does.Not.Contain("private-user-value"));
            Assert.That(snapshot.ToJson(), Does.Not.Contain("short-private-secret"));
            Assert.That(snapshot.nodes.Select(n => n.id).Distinct().Count(), Is.EqualTo(snapshot.nodes.Count));
        }

        [UnityTest]
        public IEnumerator ScrolledOverflowHiddenButtonRetainsVisibleScreenBounds()
        {
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            var texture = new RenderTexture(500, 400, 0);
            panel.targetTexture = texture;
            var gameObject = new GameObject("qa-snapshot-test");
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            try
            {
                var root = document.rootVisualElement;
                var scroll = new ScrollView { name = "scroll" };
                scroll.style.width = 400;
                scroll.style.height = 180;
                root.Add(scroll);
                var spacer = new VisualElement();
                spacer.style.height = 400;
                spacer.style.flexShrink = 0;
                scroll.Add(spacer);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.height = 80;
                row.style.flexShrink = 0;
                var label = new Label("Ambience") { name = "volume-label" };
                row.Add(label);
                var button = new Button { name = "volume-plus", text = "+" };
                button.style.width = 80;
                button.style.height = 60;
                button.style.overflow = Overflow.Hidden;
                button.style.borderTopLeftRadius = 12;
                row.Add(button);
                scroll.Add(row);
                var tail = new VisualElement();
                tail.style.height = 400;
                tail.style.flexShrink = 0;
                scroll.Add(tail);
                for (int i = 0; i < 10; i++) yield return null;
                var viewport = root.worldBound;
                var before = QaUiSnapshotExporter.Capture(root, viewport, 500, 400);
                Assert.That(before.nodes.Single(n => n.name == "volume-plus").visible, Is.False,
                    "The button starts below the ScrollView viewport.");
                for (int i = 0; i < 10; i++)
                {
                    yield return null;
                    QaUiSnapshotExporter.Capture(root, viewport, 500, 400);
                }
                var clipProperty = typeof(VisualElement).GetProperty("worldClip", BindingFlags.Instance | BindingFlags.NonPublic);
                TestContext.WriteLine("Before bounds=" + button.worldBound + " clip=" + clipProperty?.GetValue(button) +
                    " hints=" + scroll.contentContainer.usageHints);
                var cachedClip = (Rect)clipProperty.GetValue(button);
                scroll.scrollOffset = new Vector2(0, 380);
                for (int i = 0; i < 10; i++) yield return null;
                // Native scrolling alone did not reproduce the device failure in this editor.
                // Replay a stale own clip to model the observed geometry/visibility mismatch;
                // layout, resolved styles, panel and ancestor clips remain real.
                var renderData = typeof(VisualElement).GetField("renderData", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(button);
                var clippingRect = renderData.GetType().GetProperty("clippingRect", BindingFlags.Instance | BindingFlags.Public);
                clippingRect.GetValue(renderData); // Resolve dirty state before replaying the previous frame's cache.
                clippingRect.SetValue(renderData, cachedClip);
                TestContext.WriteLine("Button bounds=" + button.worldBound + " own clip=" + clipProperty?.GetValue(button) +
                    " row clip=" + clipProperty?.GetValue(row) + " viewport=" + scroll.contentViewport.worldBound);
                var after = QaUiSnapshotExporter.Capture(root, viewport, 500, 400);
                Assert.That(after.nodes.Single(n => n.name == "volume-label").visible, Is.True);
                Assert.That(scroll.contentViewport.worldBound.Contains(button.worldBound.center), Is.True);
                var actual = after.nodes.Single(n => n.name == "volume-plus");
                Assert.That(actual.visible, Is.True, "A rendered overflow-hidden button must not use its stale own worldClip.");
                Assert.That(actual.screenBounds, Is.Not.Null);
                Assert.That(actual.clipKnown, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(panel);
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator ResolvedAncestorClipsStillConstrainPartiallyVisibleAndOutsideChildren()
        {
            var window = ScriptableObject.CreateInstance<SnapshotTestWindow>();
            try
            {
                window.position = new Rect(40, 40, 500, 400);
                window.Show();
                var root = window.rootVisualElement;
                // Use real USS overflow (not inline) so dropping the engine policy read fails.
                root.styleSheets.Add(Resources.Load<StyleSheet>("UI/ctlife"));
                var ancestor = new VisualElement { name = "clipping-ancestor" };
                ancestor.AddToClassList("ct-pressable");
                ancestor.style.position = Position.Absolute;
                ancestor.style.left = 30;
                ancestor.style.top = 30;
                ancestor.style.width = 200;
                ancestor.style.height = 120;
                root.Add(ancestor);
                var partial = new Button { name = "partial", text = "+" };
                partial.style.position = Position.Absolute;
                partial.style.left = 160;
                partial.style.top = 20;
                partial.style.width = 80;
                partial.style.height = 60;
                ancestor.Add(partial);
                var outside = new Button { name = "outside", text = "−" };
                outside.style.position = Position.Absolute;
                outside.style.left = 260;
                outside.style.top = 20;
                outside.style.width = 80;
                outside.style.height = 60;
                ancestor.Add(outside);
                for (int i = 0; i < 10; i++) { window.Repaint(); yield return null; }
                var viewport = root.worldBound;
                var snapshot = QaUiSnapshotExporter.Capture(root, viewport, 500, 400);
                var parentNode = snapshot.nodes.Single(n => n.name == "clipping-ancestor");
                var partialNode = snapshot.nodes.Single(n => n.name == "partial");
                var outsideNode = snapshot.nodes.Single(n => n.name == "outside");
                Assert.That(ancestor.style.overflow.keyword, Is.EqualTo(StyleKeyword.Null));
                Assert.That(parentNode.clipKnown, Is.True);
                Assert.That(parentNode.screenBounds[2], Is.EqualTo(ancestor.worldBound.width * 500 / viewport.width).Within(.01),
                    "Own overflow must not trim the painted border.");
                Assert.That(partialNode.clipKnown, Is.True);
                Assert.That(partialNode.visible, Is.True);
                var right = ancestor.worldBound.xMax - ancestor.resolvedStyle.borderRightWidth;
                Assert.That(partialNode.screenBounds[2], Is.EqualTo((right - partial.worldBound.xMin) * 500 / viewport.width).Within(.01));
                Assert.That(outsideNode.clipKnown, Is.True);
                Assert.That(outsideNode.visible, Is.False, "Ancestor overflow must remain authoritative.");
                Assert.That(outsideNode.screenBounds, Is.Null);
                var subtree = QaUiSnapshotExporter.Capture(partial, viewport, 500, 400);
                Assert.That(subtree.nodes[0].screenBounds, Is.EqualTo(partialNode.screenBounds),
                    "Capturing a subtree must retain clips above its requested root.");
            }
            finally { window.Close(); }
        }

        private sealed class SnapshotTestWindow : EditorWindow { }

        [Test]
        public void DetachedHierarchyDoesNotClaimKnownClipping()
        {
            var root = new VisualElement();
            root.Add(new Button { text = "+" });
            var snapshot = QaUiSnapshotExporter.Capture(root, new Rect(0, 0, 100, 200), 100, 200);
            Assert.That(snapshot.nodes.All(n => !n.clipKnown), Is.True);
        }

        [Test]
        public void HiddenAncestorSuppressesDescendantVisibility()
        {
            var root = new VisualElement();
            root.style.display = DisplayStyle.None;
            root.Add(new Label("Hidden"));
            var snapshot = QaUiSnapshotExporter.Capture(root, new Rect(0, 0, 100, 200), 1000, 2000);
            Assert.That(snapshot.nodes.All(n => !n.visible && n.screenBounds == null), Is.True);
        }
    }
}
#endif
