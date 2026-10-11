using System;
using CTLife.Core;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>A lightweight notification surface. Only its dismiss control intercepts game input.</summary>
    public sealed class ToastNotice : VisualElement
    {
        private Tween _reveal;

        public ToastNotice(string title, string detail, ToastKind kind, Action dismiss)
        {
            name = "app-toast";
            AddToClassList("ct-toast");
            AddToClassList("ct-toast-" + kind.ToString().ToLowerInvariant());
            pickingMode = PickingMode.Ignore;
            Add(El.PassThrough(El.Div("ct-toast-shadow ct-toast-shadow-far")));
            Add(El.PassThrough(El.Div("ct-toast-shadow ct-toast-shadow-near")));

            var copy = El.Div("ct-toast-copy", El.Text(title, "ct-toast-text"));
            if (!string.IsNullOrWhiteSpace(detail))
                copy.Add(El.Text(detail, "ct-toast-detail"));
            var icon = new ToastSymbol(kind);
            var close = new Button(dismiss)
            {
                name = "toast-dismiss", tooltip = Strings.T("common.close"),
                enableRichText = false,
            };
            close.AddToClassList("ct-toast-dismiss");
            close.Add(new ToastSymbol(kind, true));
            var surface = El.Div("ct-toast-surface", icon, El.PassThrough(copy), close);
            surface.pickingMode = PickingMode.Ignore;
            Add(surface);
            RegisterCallback<DetachFromPanelEvent>(e =>
            {
                if (e.target != this) return;
                _reveal?.Cancel();
                _reveal = null;
            });
        }

        public void Reveal()
        {
            _reveal?.Cancel();
            _reveal = Tween.Run(this, .18f, t =>
            {
                style.opacity = t;
                style.translate = new Translate(0, -8f * (1f - t));
            }, Ease.OutCubic);
        }

        /// <summary>Original vector symbols: stable on Android without emoji or a platform glyph font.</summary>
        private sealed class ToastSymbol : VisualElement
        {
            private readonly ToastKind _kind;
            private readonly bool _close;

            public ToastSymbol(ToastKind kind, bool close = false)
            {
                _kind = kind; _close = close;
                AddToClassList(close ? "ct-toast-close-symbol" : "ct-toast-symbol");
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                float size = Mathf.Min(contentRect.width, contentRect.height);
                if (size <= 0) return;
                var p = context.painter2D;
                var centre = contentRect.center;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                p.strokeColor = resolvedStyle.color;
                p.lineWidth = size * .085f;
                Vector2 At(float x, float y) => centre + new Vector2(x - .5f, y - .5f) * size;
                if (_close)
                {
                    p.BeginPath(); p.MoveTo(At(.25f, .25f)); p.LineTo(At(.75f, .75f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(At(.75f, .25f)); p.LineTo(At(.25f, .75f)); p.Stroke();
                    return;
                }
                p.fillColor = resolvedStyle.color;
                p.BeginPath();
                if (_kind == ToastKind.Warning)
                {
                    p.MoveTo(At(.5f, .09f)); p.LineTo(At(.94f, .87f)); p.LineTo(At(.06f, .87f));
                    p.ClosePath(); p.Fill();
                }
                else if (_kind == ToastKind.Bad)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = (22.5f + i * 45f) * Mathf.Deg2Rad;
                        var vertex = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * .43f;
                        if (i == 0) p.MoveTo(vertex); else p.LineTo(vertex);
                    }
                    p.ClosePath(); p.Fill();
                }
                else
                {
                    p.Arc(centre, size * .43f, 0, 360); p.ClosePath(); p.Fill();
                }
                p.strokeColor = Color.white;
                p.fillColor = Color.white;
                p.lineWidth = size * .075f;
                if (_kind == ToastKind.Good)
                {
                    p.BeginPath(); p.MoveTo(At(.29f, .51f)); p.LineTo(At(.44f, .65f)); p.LineTo(At(.73f, .35f)); p.Stroke();
                }
                else if (_kind == ToastKind.Event)
                {
                    p.BeginPath(); p.MoveTo(At(.5f, .25f)); p.LineTo(At(.5f, .75f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(At(.25f, .5f)); p.LineTo(At(.75f, .5f)); p.Stroke();
                    p.lineWidth = size * .05f;
                    p.BeginPath(); p.MoveTo(At(.34f, .34f)); p.LineTo(At(.66f, .66f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(At(.66f, .34f)); p.LineTo(At(.34f, .66f)); p.Stroke();
                }
                else if (_kind == ToastKind.Info)
                {
                    p.BeginPath(); p.MoveTo(At(.5f, .47f)); p.LineTo(At(.5f, .72f)); p.Stroke();
                    p.BeginPath(); p.Arc(At(.5f, .30f), size * .045f, 0, 360); p.Fill();
                }
                else
                {
                    p.BeginPath(); p.MoveTo(At(.5f, .34f)); p.LineTo(At(.5f, .56f)); p.Stroke();
                    p.BeginPath(); p.Arc(At(.5f, .72f), size * .04f, 0, 360); p.Fill();
                }
            }
        }
    }
}
