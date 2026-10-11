using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Thin stroked system glyphs for the Duo (back, chevron, plus, send arrow, more, close, check, search), drawn with
    /// painter2D in the element's resolved colour so they stay crisp at every text size. Original geometry.
    /// </summary>
    public sealed class DuoGlyph : VisualElement
    {
        public enum Kind { Back, Chevron, Plus, Send, More, Close, Check, Search }

        private readonly Kind _kind;

        public DuoGlyph(Kind kind)
        {
            _kind = kind;
            AddToClassList("duo-glyph");
            AddToClassList("duo-glyph-" + kind.ToString().ToLowerInvariant());
            pickingMode = PickingMode.Ignore;
            focusable = false;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            float s = Mathf.Min(w, h);
            if (s <= 0) return;
            var p = context.painter2D;
            var c = resolvedStyle.color;
            p.strokeColor = c;
            p.fillColor = c;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.lineWidth = Mathf.Max(2f, s * (_kind == Kind.Send ? .12f : .1f));
            Vector2 o = new Vector2((w - s) * .5f, (h - s) * .5f);
            Vector2 At(float x, float y) => o + new Vector2(x * s, y * s);
            void Line(params Vector2[] pts)
            {
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.Stroke();
            }
            switch (_kind)
            {
                case Kind.Back: Line(At(.64f, .16f), At(.3f, .5f), At(.64f, .84f)); break;
                case Kind.Chevron: Line(At(.36f, .16f), At(.7f, .5f), At(.36f, .84f)); break;
                case Kind.Plus: Line(At(.5f, .14f), At(.5f, .86f)); Line(At(.14f, .5f), At(.86f, .5f)); break;
                case Kind.Send: Line(At(.5f, .84f), At(.5f, .18f)); Line(At(.22f, .44f), At(.5f, .16f), At(.78f, .44f)); break;
                case Kind.Close: Line(At(.22f, .22f), At(.78f, .78f)); Line(At(.78f, .22f), At(.22f, .78f)); break;
                case Kind.Check: Line(At(.18f, .52f), At(.4f, .74f), At(.82f, .28f)); break;
                case Kind.Search:
                    p.BeginPath(); p.Arc(At(.43f, .43f), s * .27f, 0, 360); p.Stroke();
                    Line(At(.63f, .63f), At(.86f, .86f));
                    break;
                case Kind.More:
                    foreach (float x in new[] { .2f, .5f, .8f }) { p.BeginPath(); p.Arc(At(x, .5f), s * .085f, 0, 360); p.Fill(); }
                    break;
            }
        }
    }
}
