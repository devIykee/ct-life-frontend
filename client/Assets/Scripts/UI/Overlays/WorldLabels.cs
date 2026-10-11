using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Name tags and speech bubbles drawn in UI Toolkit over the 3D world (one batched panel instead of a canvas per avatar).
    /// Which tags show and where is decided by <see cref="TagPlanner"/>: never your own; speakers, friends (people you follow)
    /// and NPCs you face always; then the six nearest; collisions push up or hide the lower priority; the centre of the
    /// screen stays clear; tags fade with distance. Tags are small rounded pills; NPCs get a tiny star and just their name.
    /// Seeker holders get a check, ORE stakers a pickaxe tier mark, speakers a live dot.
    /// </summary>
    public sealed class WorldLabels : IDisposable
    {
        /// <summary>Is this user a friend (someone the player follows)? Set by the composition root; null = nobody.</summary>
        public static Func<string, bool> IsFriend;
        /// <summary>An NPC within this distance and inside the facing cone of the player counts as "faced".</summary>
        private const float FacedDistance = 10f;
        private static readonly float FacedCos = Mathf.Cos(50f * Mathf.Deg2Rad);
        private readonly UiRoot _ui;
        private bool _disposed;

        private readonly VisualElement _layer;
        private readonly Dictionary<AvatarView, (VisualElement root, Label text, VisualElement icon)> _tags =
            new Dictionary<AvatarView, (VisualElement, Label, VisualElement)>();
        private readonly List<(Transform anchor, VisualElement el, float until)> _bubbles = new List<(Transform, VisualElement, float)>();
        private readonly List<VisualElement> _bubbleShown = new List<VisualElement>();
        private readonly List<Rect> _bubbleWanted = new List<Rect>(), _bubblePlaced = new List<Rect>();
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly List<AvatarView> _stale = new List<AvatarView>();
        private readonly List<AvatarView> _avatars = new List<AvatarView>();
        private readonly List<TagCandidate> _candidates = new List<TagCandidate>();
        private readonly List<TagPlacement> _placements = new List<TagPlacement>();

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
            foreach (var tag in _tags.Values) tag.root.RemoveFromHierarchy();
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
            var panel = _layer.layout.size;
            if (float.IsNaN(panel.x) || panel.x <= 0f) return;

            AvatarView local = null;
            foreach (var a in AvatarView.All) if (a != null && a.isLocal) { local = a; break; }
            var origin = local != null ? local.transform.position : cam.transform.position;
            var forward = local != null ? local.transform.forward : cam.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;

            _avatars.Clear();
            _candidates.Clear();
            foreach (var a in AvatarView.All)
            {
                if (a == null || a.HeadAnchor == null) continue;
                var delta = a.transform.position - origin;
                delta.y = 0f;
                float dist = delta.magnitude;
                Vector2 p = default;
                bool onScreen = !a.Culled && dist <= TagPlanner.MaxDistance && ui.WorldToPanel(cam, a.HeadAnchor.position, out p);
                if (!onScreen) p = default;
                _tags.TryGetValue(a, out var tag);
                Vector2 size = tag.root != null && !float.IsNaN(tag.root.resolvedStyle.width) && tag.root.resolvedStyle.width > 1f
                    ? new Vector2(tag.root.resolvedStyle.width, tag.root.resolvedStyle.height)
                    : new Vector2(28f + 11f * (a.displayName?.Length ?? 6), 32f); // first frame, before layout
                bool faced = a.isNpc && dist <= FacedDistance && dist > 0.01f && Vector3.Dot(delta / dist, forward) >= FacedCos;
                _avatars.Add(a);
                _candidates.Add(new TagCandidate
                {
                    Key = a.GetInstanceID(), IsLocal = a.isLocal, IsNpc = a.isNpc, Talking = a.Talking, Faced = faced, OnScreen = onScreen,
                    IsFriend = !a.isNpc && !string.IsNullOrEmpty(a.userId) && IsFriend != null && SafeIsFriend(a.userId),
                    Distance = dist, Anchor = p, Size = size,
                });
            }
            TagPlanner.Plan(_candidates, panel, _placements);

            for (int i = 0; i < _avatars.Count; i++)
            {
                var a = _avatars[i];
                var place = _placements[i];
                _tags.TryGetValue(a, out var tag);
                if (!place.Visible)
                {
                    if (tag.root != null) tag.root.style.display = DisplayStyle.None;
                    continue;
                }
                if (tag.root == null) _tags[a] = tag = NewTag();
                tag.root.style.display = DisplayStyle.Flex;
                tag.root.style.translate = new Translate(place.TopLeft.x, place.TopLeft.y);
                tag.root.style.opacity = place.Alpha;
                tag.text.text = TagText(a);
                tag.icon.style.display = a.isNpc ? DisplayStyle.Flex : DisplayStyle.None;
                tag.root.EnableInClassList("ct-tag-npc", a.isNpc);
                tag.root.EnableInClassList("ct-tag-talking", a.Talking);
            }

            _stale.Clear();
            foreach (var kv in _tags) if (kv.Key == null || !AvatarView.All.Contains(kv.Key)) _stale.Add(kv.Key);
            foreach (var k in _stale) { _tags[k].root.RemoveFromHierarchy(); _tags.Remove(k); }

            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var (anchor, el, until) = _bubbles[i];
                if (anchor == null || Time.unscaledTime > until) { el.RemoveFromHierarchy(); _bubbles.RemoveAt(i); }
            }
            // Each bubble wants to sit above its speaker; BubbleStacker moves overlapping ones up so none hides another.
            _bubbleShown.Clear();
            _bubbleWanted.Clear();
            foreach (var (anchor, el, _) in _bubbles)
            {
                if (ui.WorldToPanel(cam, anchor.position + Vector3.up * 0.35f, out var p))
                {
                    el.style.display = DisplayStyle.Flex;
                    float w = W(el), h = H(el);
                    _bubbleShown.Add(el);
                    _bubbleWanted.Add(new Rect(p.x - w * 0.5f, p.y - h - 28f, w, h));
                }
                else el.style.display = DisplayStyle.None;
            }
            BubbleStacker.Resolve(_bubbleWanted, _bubblePlaced);
            for (int i = 0; i < _bubbleShown.Count; i++)
                _bubbleShown[i].style.translate = new Translate(_bubblePlaced[i].x, _bubblePlaced[i].y);
        }

        private (VisualElement root, Label text, VisualElement icon) NewTag()
        {
            var icon = new UiIcon("npc");
            icon.AddToClassList("ct-tag-icon");
            var text = El.Text("", "ct-tag-text");
            var root = El.Div("ct-tag", icon, text);
            root.pickingMode = PickingMode.Ignore;
            text.pickingMode = PickingMode.Ignore;
            _layer.Add(root);
            return (root, text, icon);
        }

        private static bool SafeIsFriend(string userId)
        {
            try { return IsFriend(userId); }
            catch (Exception e) { Log.Debug("ui", "IsFriend failed: " + e.Message); return false; }
        }

        private static float W(VisualElement e) => float.IsNaN(e.resolvedStyle.width) ? 0f : e.resolvedStyle.width;
        private static float H(VisualElement e) => float.IsNaN(e.resolvedStyle.height) ? 0f : e.resolvedStyle.height;

        private static string TagText(AvatarView a)
        {
            var s = a.displayName ?? "";
            if (a.isNpc) return s;
            if (a.isSeeker) s += " ✓";
            if (a.minerTier == "refiner") s += " ⛏⛏⛏";
            else if (a.minerTier == "miner") s += " ⛏⛏";
            else if (a.minerTier == "prospector") s += " ⛏";
            if (a.Talking) s = "● " + s;
            return s;
        }
    }
}
