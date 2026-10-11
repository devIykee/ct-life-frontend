using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Original, static CTOS wallpaper drawn with existing theme colors. No texture or per-frame update.</summary>
    public sealed class PhoneWallpaper : VisualElement
    {
        private static readonly CustomStyleProperty<Color> Paper = new("--ct-wallpaper-paper");
        private static readonly CustomStyleProperty<Color> Sky = new("--ct-wallpaper-sky");
        private static readonly CustomStyleProperty<Color> Lavender = new("--ct-wallpaper-lavender");
        private static readonly CustomStyleProperty<Color> Mint = new("--ct-wallpaper-mint");
        private Color _paper = Color.white, _sky = Color.white, _lavender = Color.white, _mint = Color.white;

        public PhoneWallpaper()
        {
            AddToClassList("ct-phone-wallpaper");
            pickingMode = PickingMode.Ignore;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(Paper, out var paper)) _paper = paper;
                if (e.customStyle.TryGetValue(Sky, out var sky)) _sky = sky;
                if (e.customStyle.TryGetValue(Lavender, out var lavender)) _lavender = lavender;
                if (e.customStyle.TryGetValue(Mint, out var mint)) _mint = mint;
                MarkDirtyRepaint();
            });
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            var p = context.painter2D;
            // Small overlapping strips make a restrained pearl wash without a blur shader.
            const int steps = 64;
            for (int i = 0; i < steps; i++)
            {
                float y = h * i / steps;
                p.fillColor = Color.Lerp(_paper, _sky, 0.18f + 0.34f * i / steps);
                p.BeginPath(); p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(w, y));
                p.LineTo(new Vector2(w, y + h / steps + 1)); p.LineTo(new Vector2(0, y + h / steps + 1));
                p.ClosePath(); p.Fill();
            }
            Ribbon(p, w, h, _lavender, false);
            Ribbon(p, w, h, _mint, true);
        }

        private void Ribbon(Painter2D p, float w, float h, Color tint, bool lower)
        {
            p.lineCap = LineCap.Round;
            for (int i = 0; i < 48; i++)
            {
                float t = i / 47f;
                p.lineWidth = w * Mathf.Lerp(0.72f, 0.08f, t);
                p.strokeColor = Color.Lerp(Color.Lerp(_paper, tint, lower ? 0.7f : 0.24f), _paper, t * 0.92f);
                p.BeginPath();
                if (lower)
                {
                    p.MoveTo(new Vector2(w * 1.18f, h * 0.63f));
                    p.BezierCurveTo(new Vector2(w * 0.52f, h * 0.53f), new Vector2(w * 0.84f, h * 1.03f), new Vector2(-w * 0.2f, h * 1.09f));
                }
                else
                {
                    p.MoveTo(new Vector2(-w * 0.3f, h * 0.78f));
                    p.BezierCurveTo(new Vector2(w * 0.9f, h * 0.83f), new Vector2(w * 0.08f, h * 0.21f), new Vector2(w * 1.2f, -h * 0.07f));
                }
                p.Stroke();
            }
        }
    }
}
