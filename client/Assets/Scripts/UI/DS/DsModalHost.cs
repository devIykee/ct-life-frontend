using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.DS
{
    /// <summary>A modal opened or closed (the HUD hides its controls while one is up).</summary>
    public struct DsModalChangedSignal { public bool open; }

    /// <summary>
    /// The ONE modal host (SYSTEM.md 1.3/2.4): a single scrim, one surface at a time (the rest queue), the in/out motion
    /// (180 ms in / 120 ms out, ease-out), world input blocked underneath, and the back button bottom-left routed to the top
    /// surface's dismiss (Android back / Escape do the same). Created by UiRoot on its own layer above the modal layer.
    /// </summary>
    public sealed class DsModalHost
    {
        public const float OutSeconds = 0.13f;
        public static DsModalHost Current { get; private set; }

        private sealed class Entry
        {
            public VisualElement Surface;
            public DismissMode Dismiss;
            public Action OnDismiss;
            public string Key;
        }

        private readonly VisualElement _layer, _scrim, _slot;
        /// <summary>Top-right above the scrim: the HUD mirrors its Cash pill here while a card is up (v2: the only HUD piece
        /// that stays, the collect coin's target).</summary>
        public VisualElement CashSlot { get; }
        private readonly Button _back;
        private readonly Queue<Entry> _queue = new Queue<Entry>();
        private Entry _top;
        private bool _closing;

        public DsModalHost(VisualElement layer)
        {
            _layer = layer;
            _layer.AddToClassList("ds-modal-layer");
            _layer.pickingMode = PickingMode.Ignore;
            _layer.focusable = true;
            _scrim = new VisualElement { name = "ds-scrim" };
            _scrim.AddToClassList("ds-scrim");
            _scrim.RegisterCallback<PointerDownEvent>(e => { if (_top != null && _top.Dismiss == DismissMode.TapScrim) Dismiss(); e.StopPropagation(); });
            _slot = new VisualElement { name = "ds-modal-slot", pickingMode = PickingMode.Ignore };
            _slot.AddToClassList("ds-modal-slot");
            _back = Ds.Back(() => Dismiss());
            CashSlot = new VisualElement { name = "ds-modal-cash", pickingMode = PickingMode.Ignore };
            CashSlot.AddToClassList("ds-modal-cash");
            _layer.Add(_scrim);
            _layer.Add(CashSlot);
            _layer.Add(_slot);
            _layer.Add(_back);
            Hide();
            _layer.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Current = this;
        }

        public bool IsOpen => _top != null;
        public VisualElement Top => _top?.Surface;

        private void OnKey(KeyDownEvent e)
        {
            if (_top == null || e.keyCode != KeyCode.Escape) return;
            if (_top.Dismiss != DismissMode.None) Dismiss();
            e.StopPropagation();
        }

        /// <summary>Android back (from a MonoBehaviour's Update): true when a modal took it.</summary>
        public bool HandleBack()
        {
            if (_top == null) return false;
            if (_top.Dismiss != DismissMode.None) Dismiss();
            return true;
        }

        /// <summary>Shows a surface (a Ds.Dialog / Sheet / Screen). If one is up, this waits its turn (one at a time).
        /// <paramref name="key"/> de-duplicates (the same key twice shows once).</summary>
        public void Show(VisualElement surface, DismissMode dismiss = DismissMode.Back, Action onDismiss = null, string key = null)
        {
            if (surface == null) return;
            if (key != null && (_top?.Key == key || Contains(key))) return;
            var e = new Entry { Surface = surface, Dismiss = dismiss, OnDismiss = onDismiss, Key = key };
            if (_top != null || _closing) { _queue.Enqueue(e); return; }
            Open(e);
        }

        private bool Contains(string key) { foreach (var q in _queue) if (q.Key == key) return true; return false; }

        private void Open(Entry e)
        {
            _top = e;
            _layer.pickingMode = PickingMode.Position;
            _layer.style.display = DisplayStyle.Flex;
            _scrim.pickingMode = PickingMode.Position;
            _slot.Clear();
            _slot.Add(e.Surface);
            _back.style.display = e.Dismiss == DismissMode.None ? DisplayStyle.None : DisplayStyle.Flex;
            e.Surface.RemoveFromClassList("ds-out");
            // next frame: the transition runs from the initial (hidden) style to ds-in
            _layer.schedule.Execute(() => { _scrim.AddToClassList("ds-in"); CashSlot.AddToClassList("ds-in"); e.Surface.AddToClassList("ds-in"); }).ExecuteLater(16);
            Bus.Publish(new DsModalChangedSignal { open = true });
            _layer.Focus();
        }

        /// <summary>Closes the top surface (its dismiss callback runs), then shows the next queued one.</summary>
        public void Dismiss() => Close(true);

        /// <summary>Closes the top surface without running its dismiss callback (the action already handled it).</summary>
        public void Close() => Close(false);

        /// <summary>Closes a specific surface if it is up or queued.</summary>
        public void Close(VisualElement surface)
        {
            if (surface == null) return;
            if (_top != null && _top.Surface == surface) { Close(false); return; }
            var keep = new Queue<Entry>();
            while (_queue.Count > 0) { var q = _queue.Dequeue(); if (q.Surface != surface) keep.Enqueue(q); }
            while (keep.Count > 0) _queue.Enqueue(keep.Dequeue());
        }

        private void Close(bool dismissed)
        {
            if (_top == null || _closing) return;
            var e = _top;
            _closing = true;
            e.Surface.RemoveFromClassList("ds-in");
            e.Surface.AddToClassList("ds-out");
            bool last = _queue.Count == 0;
            if (last) { _scrim.RemoveFromClassList("ds-in"); CashSlot.RemoveFromClassList("ds-in"); }
            _layer.schedule.Execute(() =>
            {
                _closing = false;
                e.Surface.RemoveFromHierarchy();
                _top = null;
                if (dismissed) { try { e.OnDismiss?.Invoke(); } catch (Exception x) { Debug.LogException(x); } }
                if (_queue.Count > 0) Open(_queue.Dequeue());
                else { Hide(); Bus.Publish(new DsModalChangedSignal { open = false }); }
            }).ExecuteLater((long)(OutSeconds * 1000));
        }

        private void Hide()
        {
            _layer.pickingMode = PickingMode.Ignore;
            _scrim.pickingMode = PickingMode.Ignore;
            _back.style.display = DisplayStyle.None;
        }
    }
}
