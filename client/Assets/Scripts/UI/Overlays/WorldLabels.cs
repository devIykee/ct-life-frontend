using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Name tags and speech bubbles drawn in UI Toolkit over the 3D world (one batched panel instead of a canvas
    /// per avatar). NPCs are always tagged "NPC" (Build Spec "Density"), Seeker holders get a check badge, ORE
    /// stakers a pickaxe tier mark, and speakers a live dot. Only avatars within 16 m get a tag.
    /// </summary>
    public sealed class WorldLabels : IDisposable
    {
        private const float TagDistance = 16f;
        private readonly UiRoot _ui;
        private bool _disposed;

        private readonly VisualElement _layer;
        private readonly Dictionary<AvatarView, Label> _tags = new Dictionary<AvatarView, Label>();
        private readonly List<(Transform anchor, VisualElement el, float until)> _bubbles = new List<(Transform, VisualElement, float)>();
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly List<AvatarView> _stale = new List<AvatarView>();

        public WorldLabels(VisualElement layer)
        {
            _layer = layer;
            _ui = UiRoot.Instance;
            _layer.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.On<BubbleSignal>(OnBubble);
            _ui.AddUpdater(Tick);
        }

        private void OnBubble(BubbleSignal b)
        {
            if (_disposed || b.anchor == null || string.IsNullOrEmpty(b.text)) return;
            for (int i = _bubbles.Count - 1; i >= 0; i--)
                if (_bubbles[i].anchor == b.anchor) { _bubbles[i].el.RemoveFromHierarchy(); _bubbles.RemoveAt(i); }
            var el = El.Div(b.isNpc ? "ct-bubble ct-bubble-npc" : "ct-bubble", El.Text(b.text, "ct-bubble-text"));
            el.pickingMode = PickingMode.Ignore;
            _layer.Add(el);
            _bubbles.Add((b.anchor, el, Time.unscaledTime + (b.seconds > 0 ? b.seconds : 4f)));
        }

        private void OnDetached(DetachFromPanelEvent e)
        {
            if (e.target == _layer) Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _layer.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.Dispose();
            if (_ui != null) _ui.RemoveUpdater(Tick);
            foreach (var tag in _tags.Values) tag.RemoveFromHierarchy();
            foreach (var bubble in _bubbles) bubble.el.RemoveFromHierarchy();
            _tags.Clear();
            _bubbles.Clear();
            _stale.Clear();
        }

        private void Tick()
        {
            if (_disposed) return;
            var cam = CameraRig.Instance != null ? CameraRig.Instance.Camera : Camera.main;
            if (cam == null || _ui == null) return;
            var ui = _ui;
            var camPos = cam.transform.position;

            foreach (var a in AvatarView.All)
            {
                if (a == null || a.HeadAnchor == null) continue;
                bool show = !a.Culled && (a.transform.position - camPos).sqrMagnitude < (TagDistance + 15f) * (TagDistance + 15f)
                            && ui.WorldToPanel(cam, a.HeadAnchor.position, out _);
                if (!_tags.TryGetValue(a, out var tag))
                {
                    if (!show) continue;
                    tag = El.Text("", "ct-tag");
                    tag.pickingMode = PickingMode.Ignore;
                    _layer.Add(tag);
                    _tags[a] = tag;
                }
                if (!show) { tag.style.display = DisplayStyle.None; continue; }
                ui.WorldToPanel(cam, a.HeadAnchor.position, out var p);
                tag.style.display = DisplayStyle.Flex;
                tag.style.translate = new Translate(p.x - W(tag) * 0.5f, p.y - H(tag));
                tag.text = TagText(a);
                tag.EnableInClassList("ct-tag-npc", a.isNpc);
                tag.EnableInClassList("ct-tag-talking", a.Talking);
            }

            _stale.Clear();
            foreach (var kv in _tags) if (kv.Key == null || !AvatarView.All.Contains(kv.Key)) _stale.Add(kv.Key);
            foreach (var k in _stale) { _tags[k].RemoveFromHierarchy(); _tags.Remove(k); }

            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var (anchor, el, until) = _bubbles[i];
                if (anchor == null || Time.unscaledTime > until) { el.RemoveFromHierarchy(); _bubbles.RemoveAt(i); continue; }
                if (ui.WorldToPanel(cam, anchor.position + Vector3.up * 0.35f, out var p))
                {
                    el.style.display = DisplayStyle.Flex;
                    el.style.translate = new Translate(p.x - W(el) * 0.5f, p.y - H(el) - 28f);
                }
                else el.style.display = DisplayStyle.None;
            }
        }

        private static float W(VisualElement e) => float.IsNaN(e.resolvedStyle.width) ? 0f : e.resolvedStyle.width;
        private static float H(VisualElement e) => float.IsNaN(e.resolvedStyle.height) ? 0f : e.resolvedStyle.height;

        private static string TagText(AvatarView a)
        {
            var s = a.displayName ?? "";
            if (a.isNpc) return "NPC · " + s;
            if (a.isSeeker) s += " ✓";
            if (a.minerTier == "refiner") s += " ⛏⛏⛏";
            else if (a.minerTier == "miner") s += " ⛏⛏";
            else if (a.minerTier == "prospector") s += " ⛏";
            if (a.Talking) s = "● " + s;
            return s;
        }
    }
}
