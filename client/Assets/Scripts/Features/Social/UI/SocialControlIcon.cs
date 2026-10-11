using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures
{
    /// <summary>Original line geometry for the few supported actions without a bundled Phosphor counterpart.</summary>
    internal sealed class SocialControlIcon : VisualElement
    {
        private readonly string _kind;

        public SocialControlIcon(string kind)
        {
            _kind = kind;
            AddToClassList("ct-social-control-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float w = contentRect.width, h = contentRect.height;
            p.strokeColor = resolvedStyle.color;
            p.lineWidth = Mathf.Max(1.5f, w * .065f);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            if (_kind == "send")
            {
                p.BeginPath(); p.MoveTo(new Vector2(w * .5f, h * .83f)); p.LineTo(new Vector2(w * .5f, h * .18f));
                p.MoveTo(new Vector2(w * .23f, h * .45f)); p.LineTo(new Vector2(w * .5f, h * .18f)); p.LineTo(new Vector2(w * .77f, h * .45f)); p.Stroke();
                return;
            }
            p.BeginPath(); p.MoveTo(new Vector2(w * .15f, h * .37f)); p.LineTo(new Vector2(w * .31f, h * .37f));
            p.LineTo(new Vector2(w * .53f, h * .19f)); p.LineTo(new Vector2(w * .53f, h * .81f));
            p.LineTo(new Vector2(w * .31f, h * .63f)); p.LineTo(new Vector2(w * .15f, h * .63f)); p.ClosePath(); p.Stroke();
            if (_kind == "speaker-off")
            {
                p.BeginPath(); p.MoveTo(new Vector2(w * .69f, h * .36f)); p.LineTo(new Vector2(w * .89f, h * .64f));
                p.MoveTo(new Vector2(w * .89f, h * .36f)); p.LineTo(new Vector2(w * .69f, h * .64f)); p.Stroke();
            }
            else
            {
                p.BeginPath(); p.Arc(new Vector2(w * .47f, h * .5f), w * .28f, -48, 48); p.Stroke();
                p.BeginPath(); p.Arc(new Vector2(w * .47f, h * .5f), w * .43f, -48, 48); p.Stroke();
            }
        }
    }
}
