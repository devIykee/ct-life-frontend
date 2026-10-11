using System;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Shapes for the Arena lobby (approved mock lobby-v1, 2026-10-16) that USS cannot draw: skewed parallelograms (the mock's
    /// clip-path pills, name bar, tiles, pass and the big CTA), with an optional coloured left edge, an inner bottom lip and a
    /// solid drop edge. Drawn with Painter2D, so they stay crisp at any size; children lay out inside the shape's padding.
    /// </summary>
    public sealed class SkewBox : VisualElement
    {
        /// <summary>How far the right edge leans in, in units, at the top (left) and the bottom (right).</summary>
        public float SlantLeft { get; set; }
        public float SlantRight { get; set; }
        public Color Fill { get; set; } = new Color(20f / 255f, 6f / 255f, 44f / 255f, 0.8f);
        public Color? FillBottom { get; set; }
        public Color? Edge { get; set; }
        public float EdgeWidth { get; set; } = 9f;
        public Color? Lip { get; set; }
        public float LipHeight { get; set; }
        public Color? Drop { get; set; }
        public float DropHeight { get; set; }
        public Color? Border { get; set; }
        public float BorderWidth { get; set; } = 3.6f;

        public SkewBox(string classes = null)
        {
            if (!string.IsNullOrEmpty(classes)) foreach (var c in classes.Split(' ')) AddToClassList(c);
            generateVisualContent += Draw;
        }

        public SkewBox Shape(float slantLeft, float slantRight) { SlantLeft = slantLeft; SlantRight = slantRight; MarkDirtyRepaint(); return this; }

        public void Repaint() => MarkDirtyRepaint();

        private void Poly(Painter2D p, float x0, float y0, float x1, float y1, float sl, float sr)
        {
            // Top-left leans right by sl, bottom-right leans left by sr (the mock's polygon(sl 0, 100% 0, 100%-sr 100%, 0 100%)).
            p.BeginPath();
            p.MoveTo(new Vector2(x0 + sl, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1 - sr, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = layout.width, h = layout.height;
            if (w < 2f || h < 2f) return;
            var p = ctx.painter2D;
            float body = h - (Drop.HasValue ? DropHeight : 0f);
            if (Drop.HasValue && DropHeight > 0f)
            {
                p.fillColor = Drop.Value;
                Poly(p, 0, DropHeight, w, h, SlantLeft, SlantRight);
                p.Fill();
            }
            if (Border.HasValue)
            {
                p.fillColor = Border.Value;
                Poly(p, 0, 0, w, body, SlantLeft, SlantRight);
                p.Fill();
            }
            float b = Border.HasValue ? BorderWidth : 0f;
            // Body (a two-stop vertical blend when FillBottom is set: USS has no gradients).
            if (FillBottom.HasValue)
            {
                const int Bands = 24;
                float y0 = b, y1 = body - b;
                for (int i = 0; i < Bands; i++)
                {
                    float a = y0 + (y1 - y0) * i / Bands, z = y0 + (y1 - y0) * (i + 1) / Bands + 0.6f;
                    float k = (i + 0.5f) / Bands;
                    p.fillColor = Color.Lerp(Fill, FillBottom.Value, k);
                    float sl = SlantLeft * (1f - (a - y0) / Mathf.Max(1f, y1 - y0)), slz = SlantLeft * (1f - (Mathf.Min(z, y1) - y0) / Mathf.Max(1f, y1 - y0));
                    float sr0 = SlantRight * ((a - y0) / Mathf.Max(1f, y1 - y0)), sr1 = SlantRight * ((Mathf.Min(z, y1) - y0) / Mathf.Max(1f, y1 - y0));
                    p.BeginPath();
                    p.MoveTo(new Vector2(b + sl, a));
                    p.LineTo(new Vector2(w - b - sr0, a));
                    p.LineTo(new Vector2(w - b - sr1, Mathf.Min(z, y1)));
                    p.LineTo(new Vector2(b + slz, Mathf.Min(z, y1)));
                    p.ClosePath();
                    p.Fill();
                }
            }
            else
            {
                p.fillColor = Fill;
                Poly(p, b, b, w - b, body - b, Mathf.Max(0, SlantLeft - b * 0.5f), Mathf.Max(0, SlantRight - b * 0.5f));
                p.Fill();
            }
            if (Lip.HasValue && LipHeight > 0f)
            {
                // Inner bottom lip (the mock's inset 0 -9px 0 shadow): a band along the bottom inside the shape.
                float top = body - b - LipHeight, frac = (top - b) / Mathf.Max(1f, body - 2 * b);
                p.fillColor = Lip.Value;
                p.BeginPath();
                p.MoveTo(new Vector2(b + SlantLeft * (1f - frac), top));
                p.LineTo(new Vector2(w - b - SlantRight * frac, top));
                p.LineTo(new Vector2(w - b - SlantRight, body - b));
                p.LineTo(new Vector2(b, body - b));
                p.ClosePath();
                p.Fill();
            }
            if (Edge.HasValue)
            {
                // The coloured left edge of the event tiles (border-left in the mock).
                p.fillColor = Edge.Value;
                p.BeginPath();
                p.MoveTo(new Vector2(SlantLeft, 0));
                p.LineTo(new Vector2(SlantLeft + EdgeWidth, 0));
                p.LineTo(new Vector2(EdgeWidth, body));
                p.LineTo(new Vector2(0, body));
                p.ClosePath();
                p.Fill();
            }
        }
    }

    /// <summary>Textures for the Arena lobby built once in code (no files): the purple vignette, the mint podium glow, the sticker face.</summary>
    public static class LobbyArt
    {
        private static Texture2D _vignette, _podium, _sticker, _stickerDark, _gold;

        /// <summary>Mock .tint: radial(60% 70% at 50% 60%), clear in the middle to rgba(30,8,60,.55) at the edge.</summary>
        public static Texture2D Vignette
        {
            get
            {
                if (_vignette != null) return _vignette;
                const int W = 128, H = 64;
                _vignette = New("Lobby vignette", W, H);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float dx = (x + 0.5f) / W - 0.5f, dy = (1f - (y + 0.5f) / H) - 0.6f;
                        float d = Mathf.Sqrt((dx / 0.6f) * (dx / 0.6f) + (dy / 0.7f) * (dy / 0.7f));
                        float a = Mathf.Clamp01(d) * 0.55f;
                        _vignette.SetPixel(x, y, new Color(30f / 255f, 8f / 255f, 60f / 255f, a));
                    }
                _vignette.Apply(false, true);
                return _vignette;
            }
        }

        /// <summary>Mock .pod: an ellipse, pale mint core → #3ddc84 → #178a52 at 70%, clear past 72%, with a brighter inner top disc.</summary>
        public static Texture2D Podium
        {
            get
            {
                if (_podium != null) return _podium;
                const int W = 256, H = 128;
                _podium = New("Lobby podium", W, H);
                Color core = new Color32(0xb9, 0xf5, 0xd8, 0xff), mid = new Color32(0x3d, 0xdc, 0x84, 0xff), rim = new Color32(0x17, 0x8a, 0x52, 0xff);
                Color topA = new Color32(0xe6, 0xff, 0xf2, 0xff), topB = new Color32(0x7f, 0xf0, 0xb8, 0xff);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x + 0.5f) / W, v = 1f - (y + 0.5f) / H;
                        float dx = (u - 0.5f) / 0.5f, dy = (v - 0.4f) / 0.6f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float de = Mathf.Sqrt(((u - 0.5f) / 0.5f) * ((u - 0.5f) / 0.5f) + ((v - 0.5f) / 0.5f) * ((v - 0.5f) / 0.5f));
                        Color c = d < 0.35f ? Color.Lerp(core, mid, d / 0.35f) : Color.Lerp(mid, rim, Mathf.Clamp01((d - 0.35f) / 0.35f));
                        float alpha = de <= 0.96f ? 1f : Mathf.Clamp01((1f - de) / 0.04f);
                        // Inner top disc (inset 18% 8% 30% 8%): a brighter lens, 85% opaque.
                        float iu = (u - 0.08f) / 0.84f, iv = (v - 0.18f) / 0.52f;
                        float di = Mathf.Sqrt((iu - 0.5f) * (iu - 0.5f) * 4f + (iv - 0.5f) * (iv - 0.5f) * 4f);
                        if (di < 1f)
                        {
                            float e = Mathf.Clamp01((1f - di) / 0.06f);
                            c = Color.Lerp(c, Color.Lerp(topA, topB, di), 0.85f * e);
                        }
                        c.a = alpha;
                        _podium.SetPixel(x, y, c);
                    }
                _podium.Apply(false, true);
                return _podium;
            }
        }

        /// <summary>Sticker face: white at the top to #e9e3ff at the bottom (mock .stk .t).</summary>
        public static Texture2D Sticker => _sticker != null ? _sticker : _sticker = Ramp("Lobby sticker", Color.white, new Color32(0xe9, 0xe3, 0xff, 0xff));

        /// <summary>The Duo sticker's dark face: #2b2c31 to #08080a.</summary>
        public static Texture2D StickerDark => _stickerDark != null ? _stickerDark : _stickerDark = Ramp("Lobby sticker dark", new Color32(0x2b, 0x2c, 0x31, 0xff), new Color32(0x08, 0x08, 0x0a, 0xff));

        /// <summary>The Dream badge: #ffe27a to #ffb000.</summary>
        public static Texture2D Gold => _gold != null ? _gold : _gold = Ramp("Lobby gold", new Color32(0xff, 0xe2, 0x7a, 0xff), new Color32(0xff, 0xb0, 0x00, 0xff));

        private static Texture2D Ramp(string name, Color top, Color bottom)
        {
            var t = New(name, 1, 64);
            for (int y = 0; y < 64; y++) t.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            t.Apply(false, true);
            return t;
        }

        private static Texture2D New(string name, int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };

        public static Color Plum => new Color32(0x2a, 0x0b, 0x4a, 0xff);
        public static Color Dark => new Color(20f / 255f, 6f / 255f, 44f / 255f, 0.8f);

        /// <summary>A Windows 11 Color (Icons8 "fluent") icon from Resources/UI/Lobby/Icons, or null.</summary>
        public static VisualElement Icon(string name, string cls = "lobby-icon")
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            var tex = Resources.Load<Texture2D>("UI/Lobby/Icons/" + name);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            return e;
        }

        /// <summary>
        /// A Lilita label with the mock's thick plum stroke. UITK's text outline is capped thin by the font atlas padding, so
        /// the stroke is eight plum copies around the white text plus one plum drop copy below it, drawn behind it in a wrapper.
        /// The white label (returned inside the wrapper) carries <paramref name="cls"/>.
        /// </summary>
        public static VisualElement Outlined(string text, string cls)
        {
            var wrap = new VisualElement { pickingMode = PickingMode.Ignore };
            wrap.AddToClassList("lobby-outline-wrap");
            const float R = 3f, D = 0.7071f;
            var offsets = new[] { new Vector2(R, 0), new Vector2(-R, 0), new Vector2(0, R), new Vector2(0, -R),
                new Vector2(R * D, R * D), new Vector2(-R * D, R * D), new Vector2(R * D, -R * D), new Vector2(-R * D, -R * D), new Vector2(0, R + 2f) };
            foreach (var o in offsets)
            {
                var copy = El.Text(text, "lobby-outlined lobby-outline-copy " + cls);
                copy.enableRichText = false;
                copy.pickingMode = PickingMode.Ignore;
                copy.style.position = Position.Absolute;
                copy.style.left = o.x;
                copy.style.top = o.y;
                copy.style.color = Plum;
                wrap.Add(copy);
            }
            var l = El.Text(text, "lobby-outlined " + cls);
            l.enableRichText = false;
            l.pickingMode = PickingMode.Ignore;
            wrap.Add(l);
            return wrap;
        }
    }

    /// <summary>
    /// A sticker button (mock .stk): a white-to-lilac rounded tile with a thick plum outline and a 3D bottom lip, slightly
    /// tilted, an icon, a Lilita label with the plum outline, and an optional red count badge.
    /// </summary>
    public sealed class LobbySticker : Button
    {
        public Label Badge { get; }

        public LobbySticker(string icon, string label, Action onTap, float tilt, bool dark = false) : base(() => { DeviceFeel.Tick(); Cues.Play(Cues.SfxTap); onTap?.Invoke(); })
        {
            AddToClassList("lobby-sticker");
            tooltip = label;
            var lip = new VisualElement { pickingMode = PickingMode.Ignore };
            lip.AddToClassList("lobby-sticker-lip");
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("lobby-sticker-face");
            face.style.backgroundImage = new StyleBackground(dark ? LobbyArt.StickerDark : LobbyArt.Sticker);
            face.Add(LobbyArt.Icon(icon, "lobby-sticker-icon"));
            var tile = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.AddToClassList("lobby-sticker-tile");
            tile.style.rotate = new Rotate(tilt);
            tile.Add(lip);
            tile.Add(face);
            Add(tile);
            Add(LobbyArt.Outlined(label, "lobby-sticker-label"));
            Badge = Duo.Badge(0, "lobby-badge");
            tile.Add(Badge);
        }
    }
}
