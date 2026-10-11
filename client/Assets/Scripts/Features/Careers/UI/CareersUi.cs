using System;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Careers
{
    /// <summary>
    /// Small UI builders on top of the shared El/ct-* theme. Layout-only rules live in
    /// Features/Careers/Resources/CTLifeCareers/careers.uss (class prefix `ctc-`). Functional styling only: the user
    /// styles the UI later. Every tappable element meets the 112 px minimum touch size set in that sheet.
    /// </summary>
    internal static class CUi
    {
        private static StyleSheet _sheet;

        public static void Sheet(VisualElement root)
        {
            if (_sheet == null) _sheet = Resources.Load<StyleSheet>("CTLifeCareers/careers");
            if (_sheet != null) root.styleSheets.Add(_sheet);
            else Log.Warn("careers", "Missing Resources/CTLifeCareers/careers.uss; layout falls back to the base theme");
        }

        /// <summary>A label that never interprets rich text (server and content strings are plain).</summary>
        public static Label L(string text, string classes = null)
        {
            var label = El.Text(text ?? "", classes);
            label.enableRichText = false;
            return label;
        }

        public static Button B(string text, Action onClick, string classes = "ctc-btn", string sfx = "sfx_ui_tap")
        {
            var button = El.Btn(text ?? "", onClick, "ct-btn " + classes, sfx);
            button.enableRichText = false;
            return button;
        }

        public static VisualElement Div(string classes, params VisualElement[] children) => El.Div(classes, children);

        public static VisualElement Chip(string text) => Div("ctc-chip", L(text, "ctc-chip-text"));

        public static Color Rgb(uint rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        public static void Mark(VisualElement e, string cls, bool on) => e.EnableInClassList(cls, on);

        public static double Now => Time.realtimeSinceStartupAsDouble;

        public static void Sfx(string id) => Bus.Publish(new SfxSignal(id));

        public static void Toast(string text, ToastKind kind = ToastKind.Info) => Bus.Publish(ToastSignal.Text(text, kind));

        /// <summary>A horizontal meter: track, optional band, marker. Values in [min, max].</summary>
        public sealed class Meter
        {
            public readonly VisualElement Root;
            private readonly VisualElement _band, _marker, _fill;
            private readonly double _min, _max;

            public Meter(double min, double max, double bandLo, double bandHi, bool fill)
            {
                _min = min;
                _max = Math.Max(min + 1e-6, max);
                _band = Div("ctc-meter-band");
                _marker = Div("ctc-meter-marker");
                _fill = Div("ctc-meter-fill");
                Root = Div("ctc-meter", _band, _fill, _marker);
                _band.style.left = Length.Percent(Pct(bandLo));
                _band.style.width = Length.Percent(Pct(bandHi) - Pct(bandLo));
                Show(_fill, fill);
                Show(_band, bandHi > bandLo);
                foreach (var child in Root.Children()) child.pickingMode = PickingMode.Ignore;
            }

            private float Pct(double v) => (float)(100 * Math.Max(0, Math.Min(1, (v - _min) / (_max - _min))));

            public void Set(double value)
            {
                float p = Pct(value);
                _marker.style.left = Length.Percent(p);
                _fill.style.width = Length.Percent(p);
            }
        }
    }
}
