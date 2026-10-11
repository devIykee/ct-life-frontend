using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Presentation-only pieces for Duo app interiors. Labels and values come from each feature's
    /// existing strings and live data; this layer never infers a state, manufactures a trend or changes an action.</summary>
    public static class PhonePresentation
    {
        public enum Tone { Neutral, Info, Success, Warning, Danger, Accent }

        private static string ToneClass(Tone tone) => "ct-duo-tone-" + tone.ToString().ToLowerInvariant();

        public static VisualElement Status(string text, Tone tone = Tone.Neutral)
        {
            var status = El.Div("ct-duo-status " + ToneClass(tone), new StatusGlyph(tone),
                El.Text(text ?? "", "ct-duo-status-label"));
            status.pickingMode = PickingMode.Ignore;
            return status;
        }

        public static VisualElement Metric(string label, string value, string icon, Tone tone = Tone.Info, string detail = null)
        {
            var card = El.Div("ct-duo-metric " + ToneClass(tone),
                El.Div("ct-duo-metric-heading", new UiIcon(icon), El.Text(label ?? "", "ct-duo-metric-label")),
                El.Text(value ?? "—", "ct-duo-metric-value"));
            if (!string.IsNullOrEmpty(detail)) card.Add(El.Text(detail, "ct-duo-caption"));
            return card;
        }

        public static VisualElement StatePanel(string title, string body, Tone tone = Tone.Info)
        {
            var card = El.Div("ct-duo-state-panel", Status(title, tone));
            if (!string.IsNullOrEmpty(body)) card.Add(El.Text(body, "ct-duo-state-body"));
            return card;
        }

        public static VisualElement Footer(params VisualElement[] children) => El.Div("ct-duo-card-footer", children);

        public static VisualElement Segment(params VisualElement[] children) => El.Div("ct-duo-segment", children);

        /// <summary>El.Btn has decorative children, which disable TextElement's intrinsic layout measurement.
        /// Restore the measured minimum for phone-interior text controls, including already nonzero minima.
        /// A whole chip wraps to the next row before its label wraps; oversized labels get measured height.</summary>
        public static Button SizeToText(Button button)
        {
            float floorWidth = -1f, floorHeight = -1f, appliedWidth = -1f, appliedHeight = -1f;
            VisualElement observedParent = null;
            void Fit()
            {
                if (button.panel == null || string.IsNullOrEmpty(button.text) || button.layout.width <= 1f || button.layout.height <= 1f) return;
                bool interior = false;
                for (var ancestor = button.parent; ancestor != null; ancestor = ancestor.parent)
                {
                    if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility == Visibility.Hidden) return;
                    if (ancestor.ClassListContains("ct-phone-body")) interior = true;
                }
                if (!interior || button.resolvedStyle.display == DisplayStyle.None || button.resolvedStyle.visibility == Visibility.Hidden) return;
                var parent = button.parent;
                if (parent == null || parent.contentRect.width <= 1f) return;
                var style = button.resolvedStyle;
                float available = parent.contentRect.width - style.marginLeft - style.marginRight;
                if (available <= 1f || float.IsNaN(available)) return;
                if (floorWidth < 0f) floorWidth = float.IsNaN(style.minWidth.value) ? 0f : style.minWidth.value;
                if (floorHeight < 0f) floorHeight = float.IsNaN(style.minHeight.value) ? 0f : style.minHeight.value;
                float horizontal = style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth;
                float vertical = style.paddingTop + style.paddingBottom + style.borderTopWidth + style.borderBottomWidth;
                var single = button.MeasureTextSize(button.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                if (single.x <= 0f || float.IsNaN(single.x)) return;
                float width = Mathf.Min(available, Mathf.Max(floorWidth, Mathf.Ceil(single.x) + horizontal + 2f));
                var wrapped = button.MeasureTextSize(button.text, Mathf.Max(1f, width - horizontal), VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
                float height = Mathf.Max(floorHeight, Mathf.Ceil(wrapped.y) + vertical + 2f);
                if (Mathf.Abs(width - appliedWidth) > .5f) { appliedWidth = width; button.style.minWidth = width; }
                if (!float.IsNaN(height) && Mathf.Abs(height - appliedHeight) > .5f) { appliedHeight = height; button.style.minHeight = height; }
            }
            void ParentChanged(GeometryChangedEvent _) => Fit();
            button.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                observedParent = button.parent;
                observedParent?.RegisterCallback<GeometryChangedEvent>(ParentChanged);
                button.schedule.Execute(Fit);
            });
            button.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                observedParent?.UnregisterCallback<GeometryChangedEvent>(ParentChanged);
                observedParent = null;
            });
            button.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            button.RegisterCallback<ChangeEvent<string>>(e => { if (e.target == button) Fit(); });
            return button;
        }

        /// <summary>Small outline status shapes. Text carries the meaning; colour and geometry reinforce it.</summary>
        private sealed class StatusGlyph : VisualElement
        {
            private readonly Tone _tone;
            public StatusGlyph(Tone tone)
            {
                _tone = tone;
                AddToClassList("ct-duo-status-glyph");
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                float w = contentRect.width, h = contentRect.height;
                if (w < 2 || h < 2) return;
                var p = context.painter2D;
                p.strokeColor = resolvedStyle.color;
                p.lineWidth = Mathf.Max(1.6f, w * .075f);
                p.lineJoin = LineJoin.Round;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                if (_tone == Tone.Warning)
                {
                    p.MoveTo(new Vector2(w * .5f, h * .1f)); p.LineTo(new Vector2(w * .93f, h * .88f));
                    p.LineTo(new Vector2(w * .07f, h * .88f)); p.ClosePath();
                }
                else p.Arc(new Vector2(w * .5f, h * .5f), Mathf.Min(w, h) * .39f, 0, 360);
                p.Stroke();
                p.BeginPath();
                if (_tone == Tone.Success)
                {
                    p.MoveTo(new Vector2(w * .29f, h * .5f)); p.LineTo(new Vector2(w * .44f, h * .65f)); p.LineTo(new Vector2(w * .72f, h * .35f));
                }
                else if (_tone == Tone.Danger)
                {
                    p.MoveTo(new Vector2(w * .36f, h * .36f)); p.LineTo(new Vector2(w * .64f, h * .64f));
                    p.MoveTo(new Vector2(w * .64f, h * .36f)); p.LineTo(new Vector2(w * .36f, h * .64f));
                }
                else if (_tone == Tone.Neutral)
                {
                    p.MoveTo(new Vector2(w * .34f, h * .5f)); p.LineTo(new Vector2(w * .66f, h * .5f));
                }
                else if (_tone == Tone.Accent)
                {
                    p.MoveTo(new Vector2(w * .5f, h * .3f)); p.LineTo(new Vector2(w * .5f, h * .7f));
                    p.MoveTo(new Vector2(w * .3f, h * .5f)); p.LineTo(new Vector2(w * .7f, h * .5f));
                }
                else if (_tone == Tone.Info)
                {
                    p.MoveTo(new Vector2(w * .5f, h * .44f)); p.LineTo(new Vector2(w * .5f, h * .7f));
                    p.MoveTo(new Vector2(w * .5f, h * .29f)); p.LineTo(new Vector2(w * .5f, h * .31f));
                }
                else
                {
                    p.MoveTo(new Vector2(w * .5f, h * .36f)); p.LineTo(new Vector2(w * .5f, h * .58f));
                    p.MoveTo(new Vector2(w * .5f, h * .71f)); p.LineTo(new Vector2(w * .5f, h * .73f));
                }
                p.Stroke();
            }
        }
    }
}
