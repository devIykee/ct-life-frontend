#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Dev
{
    /// <summary>Read-only telemetry. No events, commands, reflection into callbacks or network listener.</summary>
    public sealed class QaUiSnapshotExporter
    {
        // worldClip is a renderer cache, not a reliable current panel-space clip after a
        // ScrollView GroupTransform moves. Read the engine's resolved clipping policy/local box
        // instead, and transform it with current layout. This includes USS overflow, border/padding
        // and ScrollView's disableClipping exception. Unavailable internals remain explicitly unknown.
        private static readonly System.Reflection.MethodInfo ShouldClip = typeof(VisualElement).GetMethod(
            "ShouldClip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.MethodInfo LocalClippingRect = typeof(VisualElement).Assembly
            .GetType("UnityEngine.UIElements.UIR.RenderData")?.GetMethod("GetLocalClippingRect",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        private readonly string _session = Guid.NewGuid().ToString("N");
        private double _next;
        private long _sequence;
        private bool _started;
        private bool _warned;
        private string PathName => Path.Combine(Application.persistentDataPath, "qa-ui.json");

        public void Tick(VisualElement root)
        {
            // Android LaunchArgs accepts qaUi only after automation.key authentication.
            if (!LaunchArgs.Flag("qaUi")) { Stop(); return; }
            if (Time.realtimeSinceStartupAsDouble < _next) return;
            _next = Time.realtimeSinceStartupAsDouble + 0.5;
            if (root?.panel == null || Screen.width <= 0 || Screen.height <= 0) return;
            try
            {
                _started = true;
                var viewport = Viewport(root);
                if (viewport.width <= 0 || viewport.height <= 0) return;
                var snapshot = Capture(root, viewport, Screen.width, Screen.height);
                snapshot.session = _session;
                snapshot.sequence = ++_sequence;
                snapshot.utc = DateTime.UtcNow.ToString("O");
                snapshot.frame = Time.frameCount;
                snapshot.realtime = Time.realtimeSinceStartupAsDouble;
                CaptureAudio(snapshot);
                QaUiSnapshotFile.Write(PathName, snapshot.ToJson());
                _warned = false;
            }
            catch (Exception)
            {
                // No exception message: it can contain paths or credential-bearing UI strings.
                if (!_warned) Log.Warn("qa-ui", "Snapshot unavailable; previous file may be stale.");
                _warned = true;
            }
        }

        public void Stop()
        {
            if (!_started) return; // Flag absent: no file operations, including deletion.
            _started = false;
            try { File.Delete(PathName); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static Rect Viewport(VisualElement root)
        {
            // RuntimePanelUtils consumes top-left screen pixels. Invert its mapping instead of assuming
            // reference resolution or safe-area padding equals panel scale/offset.
            var a = RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero);
            var b = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.width, Screen.height));
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        public static QaUiSnapshot Capture(VisualElement root, Rect viewport, int width, int height)
        {
            var snapshot = new QaUiSnapshot { screenWidth = width, screenHeight = height, panelViewport = Box(viewport) };
            if (root == null) return snapshot;
            var clip = viewport;
            bool clipKnown = root.panel != null;
            for (var ancestor = root.hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
                ApplyChildClip(ancestor, ref clip, ref clipKnown);
            Visit(root, "0", null, new List<string>(), viewport, width, height, clip, clipKnown, true, false, snapshot);
            return snapshot;
        }

        private static void Visit(VisualElement e, string id, string parentId, List<string> ancestors,
            Rect viewport, int width, int height, Rect clip, bool clipKnown, bool ancestorsVisible, bool inputAncestor, QaUiSnapshot snapshot)
        {
            var style = e.resolvedStyle;
            bool visible = ancestorsVisible && style.display != DisplayStyle.None &&
                style.visibility == Visibility.Visible && style.opacity > 0;
            // TextInputBaseField<T> covers TextField and numeric inputs. Their text children can expose
            // values even when a password field paints bullets. Never read any field's value or tooltip.
            bool input = inputAncestor || IsInput(e) || QaUiText.SensitiveName(e.name);
            var bounds = e.worldBound;
            // An element's own overflow clips its contents, not its painted border/background.
            // Only ancestor clips constrain this node's bounds; its own clip is passed to children.
            var pixels = visible ? QaUiGeometry.ClipAndMap(Box(bounds), Box(clip), Box(viewport), width, height) : null;
            var node = new QaUiNode
            {
                id = id, parentId = parentId, ancestors = ancestors.ToArray(), type = e.GetType().Name,
                name = QaUiText.Safe(e.name, input),
                label = e is TextElement text ? QaUiText.Safe(text.text, input) : "",
                clipKnown = clipKnown, input = input, disabled = !e.enabledInHierarchy, visible = visible && pixels != null,
                panelBounds = Finite(bounds) ? Box(bounds) : null, screenBounds = pixels,
                picking = e.pickingMode.ToString(),
                // Position is a candidate, not a claim that a callback is registered. This preserves custom tiles.
                tapCandidate = e.pickingMode == PickingMode.Position && !inputAncestor,
                toggle = e is Toggle toggle ? (bool?)toggle.value : null,
                scrollOffset = e is ScrollView scroll ? new[] { (double)scroll.scrollOffset.x, scroll.scrollOffset.y } : null
            };
            if (pixels != null && e.panel != null)
            {
                node.occlusionKnown = true;
                node.occluded = true;
                // Five inset sample points: partial overlays need not make an entire tile unreachable.
                foreach (var fraction in new[] { new Vector2(.5f, .5f), new Vector2(.2f, .2f),
                    new Vector2(.8f, .2f), new Vector2(.2f, .8f), new Vector2(.8f, .8f) })
                {
                    var screen = new Vector2((float)(pixels[0] + pixels[2] * fraction.x), (float)(pixels[1] + pixels[3] * fraction.y));
                    var panelPoint = new Vector2(viewport.x + screen.x * viewport.width / width,
                        viewport.y + screen.y * viewport.height / height);
                    var picked = e.panel.Pick(panelPoint);
                    bool hit = picked == e || (picked != null && e.Contains(picked));
                    if (fraction.x == .5f) node.centerOccluded = !hit;
                    if (!hit) continue;
                    node.occluded = false;
                    node.tapPoint = new[] { (double)screen.x, screen.y };
                    break;
                }
            }
            snapshot.nodes.Add(node);
            var childClip = clip;
            bool childClipKnown = clipKnown;
            ApplyChildClip(e, ref childClip, ref childClipKnown);
            var childAncestors = new List<string>(ancestors) { id };
            // hierarchy includes ScrollView internals; Children() can omit them via contentContainer.
            for (int i = 0; i < e.hierarchy.childCount; i++)
                Visit(e.hierarchy[i], id + "/" + i, id, childAncestors, viewport, width, height,
                    childClip, childClipKnown, visible, input, snapshot);
        }

        private static void ApplyChildClip(VisualElement e, ref Rect clip, ref bool clipKnown)
        {
            try
            {
                if (ShouldClip == null || e.panel == null) clipKnown = false;
                else if ((bool)ShouldClip.Invoke(e, null))
                {
                    if (LocalClippingRect == null) clipKnown = false;
                    else
                    {
                        var args = new object[] { e, default(Rect) };
                        LocalClippingRect.Invoke(null, args);
                        var currentClip = e.LocalToWorld((Rect)args[1]);
                        if (Finite(currentClip)) clip = Intersect(clip, currentClip);
                        else clipKnown = false;
                    }
                }
            }
            catch (Exception) { clipKnown = false; }
            if (!clipKnown && (e.style.overflow.value == Overflow.Hidden ||
                e.ClassListContains("unity-scroll-view__content-viewport"))) clip = Intersect(clip, e.worldBound);
        }

        private static bool IsInput(VisualElement e)
        {
            for (var type = e.GetType(); type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TextInputBaseField<>)) return true;
            return false;
        }

        private static double[] Box(Rect r) => new[] { (double)r.x, r.y, r.width, r.height };
        private static bool Finite(Rect r) => !float.IsNaN(r.x + r.y + r.width + r.height) &&
            !float.IsInfinity(r.x + r.y + r.width + r.height);
        private static Rect Intersect(Rect a, Rect b) => new Rect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
            Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)),
            Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin)));

        private static void CaptureAudio(QaUiSnapshot snapshot)
        {
            foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                snapshot.audio.Add(new QaUiAudio
                {
                    id = source.GetInstanceID(), playing = source.isPlaying, enabled = source.isActiveAndEnabled,
                    mute = source.mute, loop = source.loop, volume = source.volume, pitch = source.pitch,
                    time = source.time, clip = QaUiText.Safe(source.clip != null ? source.clip.name : "", false),
                    length = source.clip != null ? source.clip.length : 0
                });
        }
    }
}
#endif
