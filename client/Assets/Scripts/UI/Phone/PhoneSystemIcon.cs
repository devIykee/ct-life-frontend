using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Original lightweight navigation geometry, independent of launcher app artwork.</summary>
    public sealed class PhoneSystemIcon : VisualElement
    {
        private readonly string _kind;
        public PhoneSystemIcon(string kind)
        {
            _kind = kind;
            AddToClassList("ct-phone-system-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            var p = context.painter2D;
            p.strokeColor = resolvedStyle.color;
            p.lineWidth = Mathf.Max(1.5f, w * .06f);
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            if (_kind == "search")
            {
                p.BeginPath(); p.Arc(new Vector2(w * .43f, h * .43f), w * .25f, 0, 360); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(w * .62f,h * .62f)); p.LineTo(new Vector2(w * .82f,h * .82f)); p.Stroke();
            }
            else if (_kind == "back")
            {
                p.BeginPath(); p.MoveTo(new Vector2(w * .65f,h * .19f)); p.LineTo(new Vector2(w * .34f,h * .5f)); p.LineTo(new Vector2(w * .65f,h * .81f)); p.Stroke();
            }
            else
            {
                p.BeginPath(); p.MoveTo(new Vector2(w * .27f,h * .27f)); p.LineTo(new Vector2(w * .73f,h * .73f)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(w * .73f,h * .27f)); p.LineTo(new Vector2(w * .27f,h * .73f)); p.Stroke();
            }
        }
    }
}
