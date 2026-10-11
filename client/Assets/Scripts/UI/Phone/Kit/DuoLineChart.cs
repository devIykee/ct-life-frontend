using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Edge-to-edge price line (Duo board #6): faint dashed gridlines, the line in green or red by direction, a soft
    /// fill under it and a dot on the last value. Draws only the points it is given; with fewer than two it draws
    /// nothing (callers hide it), so no chart is ever invented.
    /// </summary>
    public sealed class DuoLineChart : VisualElement
    {
        private static readonly Color Up = new Color32(0x30, 0xc7, 0x5e, 0xff), Down = new Color32(0xff, 0x3b, 0x30, 0xff);
        private static readonly Color Grid = new Color(60f / 255f, 60f / 255f, 67f / 255f, .14f);
        private readonly List<double> _values = new List<double>();

        public DuoLineChart()
        {
            AddToClassList("duo-line-chart");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public int Count => _values.Count;
        public bool Rising => _values.Count >= 2 && _values[_values.Count - 1] >= _values[0];

        public void SetData(IEnumerable<double> values)
        {
            _values.Clear();
            if (values != null) foreach (var v in values) if (!double.IsNaN(v) && !double.IsInfinity(v)) _values.Add(v);
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            var p = ctx.painter2D;
            // Faint dashed gridlines at quarters.
            p.strokeColor = Grid;
            p.lineWidth = 1.5f;
            for (int g = 1; g <= 3; g++)
            {
                float y = h * g / 4f;
                for (float x = 0; x < w; x += 18f)
                {
                    p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(Mathf.Min(w, x + 9f), y)); p.Stroke();
                }
            }
            if (_values.Count < 2) return;
            double min = double.MaxValue, max = double.MinValue;
            foreach (var v in _values) { min = Math.Min(min, v); max = Math.Max(max, v); }
            double lo = min, hi = max;
            if (hi - lo <= 0) { double pad = Math.Abs(hi) > 0 ? Math.Abs(hi) * .01 : 1; lo -= pad; hi += pad; } // flat: mid line
            float top = h * .1f, bottom = h * .9f;
            Vector2 At(int i) => new Vector2(w * i / (_values.Count - 1), bottom - (float)((_values[i] - lo) / (hi - lo)) * (bottom - top));
            var color = Rising ? Up : Down;
            // Soft fill (USS has no gradients; a low-alpha band reads as the board's fade).
            p.fillColor = new Color(color.r, color.g, color.b, .10f);
            p.BeginPath();
            p.MoveTo(new Vector2(0, h));
            for (int i = 0; i < _values.Count; i++) p.LineTo(At(i));
            p.LineTo(new Vector2(w, h));
            p.ClosePath();
            p.Fill();
            p.strokeColor = color;
            p.lineWidth = 4f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(At(0));
            for (int i = 1; i < _values.Count; i++) p.LineTo(At(i));
            p.Stroke();
            var last = At(_values.Count - 1);
            p.fillColor = new Color(color.r, color.g, color.b, .25f);
            p.BeginPath(); p.Arc(last, 13f, 0, 360); p.Fill();
            p.fillColor = color;
            p.BeginPath(); p.Arc(last, 7f, 0, 360); p.Fill();
        }
    }

    /// <summary>Which timeframe windows a series really covers (pills are shown only for those).</summary>
    public static class DuoTimeframes
    {
        public static readonly (string label, long ms)[] Windows =
        {
            ("15m", 15 * 60_000L), ("1H", 60 * 60_000L), ("4H", 4 * 60 * 60_000L), ("1D", 24 * 60 * 60_000L),
        };

        /// <summary>
        /// Windows strictly shorter than the data's span (so each shows a different slice), plus "All". With one
        /// option only (or under two points) nothing is offered.
        /// </summary>
        public static List<string> Available(long firstMs, long lastMs, int points)
        {
            var list = new List<string>();
            if (points < 2 || lastMs <= firstMs) return list;
            long span = lastMs - firstMs;
            foreach (var (label, ms) in Windows) if (ms < span) list.Add(label);
            if (list.Count == 0) return list;
            list.Add("All");
            return list;
        }

        public static long WindowMs(string label)
        {
            foreach (var (l, ms) in Windows) if (l == label) return ms;
            return long.MaxValue;
        }
    }
}
