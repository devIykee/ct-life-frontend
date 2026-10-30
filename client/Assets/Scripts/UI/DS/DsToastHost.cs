using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.DS
{
    /// <summary>One toast (SYSTEM.md 3.9): an icon dot, one line, an optional trailing value and progress, sticky for objectives.</summary>
    public sealed class DsToastSpec
    {
        public string Icon = "info", Text, Trailing, Key;
        /// <summary>Optional supporting copy: a quieter second line (plain text).</summary>
        public string Detail;
        /// <summary>A brand id (BrandTable): the toast leads with the brand's mark tile and its trailing value takes the brand
        /// colour (v2 objective pill: "Head to McDonald's · 110 m").</summary>
        public string Brand;
        public Tone Tone = Tone.Neutral;
        public float Seconds = 3f;
        public float? Progress01;
        public bool Sticky;
    }

    /// <summary>
    /// The one messaging channel (SYSTEM.md 3.9): top centre under the Cash pill, dark glass, one at a time (queued), 3 s,
    /// in 200 ms (slide 24 + fade) / out 120 ms. Sticky toasts (objectives) stay until <see cref="Clear"/> with their key.
    /// </summary>
    public sealed class DsToastHost
    {
        /// <summary>Waiting toasts beyond this drop the oldest non-sticky one (a burst never lags the game by minutes).</summary>
        public const int MaxQueued = 3;
        public static DsToastHost Current { get; private set; }
        private readonly VisualElement _host;
        private readonly Queue<DsToastSpec> _queue = new Queue<DsToastSpec>();
        private VisualElement _shown;
        private DsToastSpec _spec;
        private IVisualElementScheduledItem _timer;

        public DsToastHost(VisualElement layer)
        {
            _host = new VisualElement { name = "ds-toast-host", pickingMode = PickingMode.Ignore };
            _host.AddToClassList("ds-toast-host");
            layer.Add(_host);
            Current = this;
        }

        public DsToastSpec Showing => _spec;

        /// <summary>The HUD's top-centre column (Cash, wanted stars, on-shift): toasts sit just under it, never over a pill.</summary>
        public VisualElement Anchor { get; set; }

        private void Place()
        {
            var a = Anchor;
            if (a == null || a.panel == null || _host.parent == null || a.resolvedStyle.display == DisplayStyle.None) { _host.style.top = StyleKeyword.Null; return; }
            var wb = a.worldBound;
            if (float.IsNaN(wb.height) || wb.height <= 0) { _host.style.top = StyleKeyword.Null; return; }
            float y = _host.parent.WorldToLocal(new Vector2(wb.center.x, wb.yMax)).y + 14f;
            _host.style.top = Mathf.Max(126f, y);
        }

        public void Show(DsToastSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Text)) return;
            if (spec.Key != null && _spec?.Key == spec.Key) { Update(spec); return; }
            if (_shown != null)
            {
                // the same line twice in a row shows once
                if (_spec != null && _spec.Text == spec.Text && _spec.Detail == spec.Detail) return;
                foreach (var q in _queue) if (q.Text == spec.Text && q.Detail == spec.Detail) return;
                _queue.Enqueue(spec);
                Trim();
                return;
            }
            Present(spec);
        }

        /// <summary>Updates the visible toast with the same key in place (an objective's countdown / progress).</summary>
        public void Update(DsToastSpec spec)
        {
            if (_shown == null || _spec?.Key != spec.Key) { Show(spec); return; }
            _spec = spec;
            var t = _shown.Q<Label>("ds-toast-text"); if (t != null) Ds.SetText(t, spec.Text);
            var tr = _shown.Q<Label>("ds-toast-trail"); if (tr != null) tr.text = spec.Trailing ?? "";
            var fill = _shown.Q(className: "ds-bar-fill"); if (fill != null && spec.Progress01 is float p) fill.style.width = new Length(Mathf.Clamp01(p) * 100f, LengthUnit.Percent);
        }

        public int Queued => _queue.Count;

        private void Trim()
        {
            if (_queue.Count <= MaxQueued) return;
            var keep = new Queue<DsToastSpec>();
            int drop = _queue.Count - MaxQueued;
            while (_queue.Count > 0)
            {
                var q = _queue.Dequeue();
                if (drop > 0 && !q.Sticky) { drop--; continue; }
                keep.Enqueue(q);
            }
            while (keep.Count > 0) _queue.Enqueue(keep.Dequeue());
        }

        public void Clear(string key)
        {
            if (_spec?.Key == key) Hide();
            if (_queue.Count == 0) return;
            var keep = new Queue<DsToastSpec>();
            while (_queue.Count > 0) { var q = _queue.Dequeue(); if (q.Key != key) keep.Enqueue(q); }
            while (keep.Count > 0) _queue.Enqueue(keep.Dequeue());
        }

        public static VisualElement Build(DsToastSpec spec)
        {
            var t = new VisualElement { name = "ds-toast", pickingMode = PickingMode.Ignore };
            t.AddToClassList("ds-toast");
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("ds-toast-dot");
            var brand = BrandTable.Mark(spec.Brand);
            if (brand != null)
            {
                t.AddToClassList(BrandTable.ClassFor(spec.Brand));
                dot.AddToClassList("ds-toast-dot--brand");
                var m = Ds.MarkElement(brand, true);
                m.style.width = 40; m.style.height = 34;
                dot.Add(m);
            }
            else
            {
                string tint = spec.Tone switch { Tone.Accent => "accent", Tone.Danger => "danger", Tone.Warn => "warn", _ => "ink2" };
                if (spec.Tone == Tone.Accent || spec.Tone == Tone.Danger || spec.Tone == Tone.Warn) dot.AddToClassList("ds-toast-dot--" + tint);
                dot.Add(Ds.Icon(spec.Icon ?? "info", 44, "white"));
            }
            t.Add(dot);
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.AddToClassList("ds-toast-col");
            var text = Ds.Text(spec.Text, "ds-toast-text");
            text.name = "ds-toast-text";
            col.Add(text);
            if (!string.IsNullOrWhiteSpace(spec.Detail)) { var d = Ds.Text(spec.Detail, "ds-toast-detail"); d.name = "ds-toast-detail"; col.Add(d); }
            if (spec.Progress01 is float p)
            {
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.AddToClassList("ds-bar");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("ds-bar-fill");
                fill.style.width = new Length(Mathf.Clamp01(p) * 100f, LengthUnit.Percent);
                bar.Add(fill);
                col.Add(bar);
            }
            t.Add(col);
            var trail = Ds.Text(spec.Trailing ?? "", "ds-toast-trail");
            trail.name = "ds-toast-trail";
            if (string.IsNullOrEmpty(spec.Trailing)) trail.style.display = DisplayStyle.None;
            t.Add(trail);
            return t;
        }

        private void Present(DsToastSpec spec)
        {
            _spec = spec;
            _shown = Build(spec);
            Place();
            _host.Add(_shown);
            var shown = _shown;
            _host.schedule.Execute(() => shown.AddToClassList("ds-in")).ExecuteLater(16);
            _timer?.Pause();
            if (!spec.Sticky) _timer = _host.schedule.Execute(Hide).StartingIn((long)(Mathf.Max(1f, spec.Seconds) * 1000));
        }

        private void Hide()
        {
            _timer?.Pause();
            var t = _shown;
            if (t == null) return;
            _shown = null; _spec = null;
            t.RemoveFromClassList("ds-in");
            t.AddToClassList("ds-out");
            _host.schedule.Execute(() =>
            {
                t.RemoveFromHierarchy();
                if (_queue.Count > 0 && _shown == null) Present(_queue.Dequeue());
            }).ExecuteLater(130);
        }
    }
}
