#!/usr/bin/env python3
"""Grade the raw Blender renders and composite the mock in-game HUD.

Run: art/lookdev/.venv/bin/python art/lookdev/post.py
In : art/lookdev/renders/{timeline_square_hero_raw,timeline_square_phone_raw,terminal_cafe_raw}.png (+ .json)
Out: art/lookdev/timeline-square-keyart.png (2400x1350)
     art/lookdev/timeline-square-phone.png  (1200x2670, HUD on top)
     art/lookdev/terminal-cafe.png          (2400x1350)
"""
import json
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
R = os.path.join(HERE, "renders")
FONTS = os.path.join(HERE, "src", "fonts")


def font(kind, size, weight=None):
    path = {"inter": "inter_Inter[opsz,wght].ttf", "grotesk": "spacegrotesk_SpaceGrotesk[wght].ttf",
            "mono": "jetbrainsmono_JetBrainsMono[wght].ttf"}[kind]
    f = ImageFont.truetype(os.path.join(FONTS, path), size)
    if weight:
        vals = []
        for a in f.get_variation_axes():
            n = a["name"].decode() if isinstance(a["name"], bytes) else a["name"]
            vals.append(weight if n == "Weight" else min(a["maximum"], max(a["minimum"], 24)))
        f.set_variation_by_axes(vals)
    return f


# ----------------------------------------------------------------------------- grading
def radial(size, cx, cy, r, inner=255, outer=0):
    w, h = size
    small = Image.new("L", (w // 8, h // 8))
    px = small.load()
    for y in range(small.height):
        for x in range(small.width):
            d = math.hypot((x * 8 - cx) / r, (y * 8 - cy) / r)
            t = max(0.0, min(1.0, d))
            t = t * t * (3 - 2 * t)
            px[x, y] = int(inner + (outer - inner) * t)
    return small.resize(size, Image.BICUBIC)


def vgrad(size, top, bottom, frac=1.0):
    w, h = size
    g = Image.new("L", (1, h))
    for y in range(h):
        t = min(1.0, y / max(1, h * frac))
        g.putpixel((0, y), int(top + (bottom - top) * t))
    return g.resize(size)


def grade(im, bloom=0.55, leak=(0.0, 0.0), leak_strength=0.32, haze_frac=0.28, haze_strength=0.30, vignette=0.28,
          sat=1.06, contrast=1.04, warm=(255, 196, 140)):
    im = im.convert("RGB")
    w, h = im.size
    # bloom: threshold + wide blur + add
    bright = im.point(lambda v: max(0, v - 170) * 3)
    b1 = bright.filter(ImageFilter.GaussianBlur(w / 160))
    b2 = bright.filter(ImageFilter.GaussianBlur(w / 50))
    glow = ImageChops.add(b1, b2)
    im = ImageChops.add(im, glow.point(lambda v: int(v * bloom)))
    # aerial haze at the top (skyline)
    if haze_frac > 0:
        mask = vgrad((w, h), int(255 * haze_strength), 0, haze_frac)
        im = Image.composite(Image.new("RGB", (w, h), (255, 214, 172)), im, mask)
    # golden light leak from the sun side
    if leak_strength > 0:
        lm = radial((w, h), leak[0] * w, leak[1] * h, max(w, h) * 0.75, int(255 * leak_strength), 0)
        leak_img = Image.new("RGB", (w, h), warm)
        im = Image.composite(ImageChops.screen(im, leak_img), im, lm)
    im = ImageEnhance.Color(im).enhance(sat)
    im = ImageEnhance.Contrast(im).enhance(contrast)
    # vignette
    if vignette > 0:
        vm = radial((w, h), w / 2, h / 2, math.hypot(w, h) * 0.62, 255, int(255 * (1 - vignette)))
        im = ImageChops.multiply(im, Image.merge("RGB", [vm, vm, vm]))
    return im


# ----------------------------------------------------------------------------- HUD
INK = (255, 255, 255, 255)
CARD = (14, 18, 32, 196)
GREEN = (38, 222, 129, 255)
RED = (255, 71, 87, 255)
GOLD = (255, 196, 61, 255)


def shadowed(layer, box, radius, fill, shadow=36, blur=18, offset=(0, 8)):
    """Rounded card with a soft drop shadow drawn into an RGBA layer."""
    sh = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle([box[0] + offset[0], box[1] + offset[1], box[2] + offset[0], box[3] + offset[1]], radius, fill=(0, 0, 0, shadow * 3))
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    layer.alpha_composite(sh)
    ImageDraw.Draw(layer).rounded_rectangle(box, radius, fill=fill)


def icon_map(d, cx, cy, s, col):
    pts = [(-1, -0.75), (-0.33, -1), (0.33, -0.75), (1, -1), (1, 0.75), (0.33, 1), (-0.33, 0.75), (-1, 1)]
    d.polygon([(cx + x * s, cy + y * s) for x, y in pts], outline=col, width=int(s * 0.16))
    d.line([(cx - 0.33 * s, cy - s), (cx - 0.33 * s, cy + 0.75 * s)], fill=col, width=int(s * 0.14))
    d.line([(cx + 0.33 * s, cy - 0.75 * s), (cx + 0.33 * s, cy + s)], fill=col, width=int(s * 0.14))


def icon_phone(d, cx, cy, s, col):
    d.rounded_rectangle([cx - 0.6 * s, cy - s, cx + 0.6 * s, cy + s], int(s * 0.25), outline=col, width=int(s * 0.16))
    d.line([(cx - 0.2 * s, cy + 0.7 * s), (cx + 0.2 * s, cy + 0.7 * s)], fill=col, width=int(s * 0.14))


def icon_mic(d, cx, cy, s, col):
    d.rounded_rectangle([cx - 0.38 * s, cy - s, cx + 0.38 * s, cy + 0.3 * s], int(s * 0.38), fill=col)
    d.arc([cx - 0.7 * s, cy - 0.55 * s, cx + 0.7 * s, cy + 0.65 * s], 0, 180, fill=col, width=int(s * 0.14))
    d.line([(cx, cy + 0.65 * s), (cx, cy + s)], fill=col, width=int(s * 0.14))
    d.line([(cx - 0.4 * s, cy + s), (cx + 0.4 * s, cy + s)], fill=col, width=int(s * 0.14))


def icon_camera(d, cx, cy, s, col):
    d.rounded_rectangle([cx - s, cy - 0.6 * s, cx + s, cy + 0.85 * s], int(s * 0.22), outline=col, width=int(s * 0.16))
    d.rectangle([cx - 0.35 * s, cy - 0.85 * s, cx + 0.35 * s, cy - 0.6 * s], fill=col)
    d.ellipse([cx - 0.42 * s, cy - 0.3 * s, cx + 0.42 * s, cy + 0.54 * s], outline=col, width=int(s * 0.15))


def hud(base, pts):
    S = 2  # supersample
    W, H = base.size
    L = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(L)

    def s(v):
        return int(v * S)
    # ---------------- top bar
    y0 = 92
    # player card: avatar + time + mood
    shadowed(L, [s(36), s(y0), s(470), s(y0 + 132)], s(40), CARD)
    d = ImageDraw.Draw(L)
    d.ellipse([s(54), s(y0 + 18), s(150), s(y0 + 114)], fill=(255, 140, 0, 255))
    d.ellipse([s(54), s(y0 + 18), s(150), s(y0 + 114)], outline=(255, 255, 255, 230), width=s(4))
    d.text((s(102), s(y0 + 66)), "R", font=font("inter", s(46), 900), fill=INK, anchor="mm")
    d.text((s(172), s(y0 + 22)), "16:20", font=font("inter", s(44), 800), fill=INK)
    # mood meter
    d.text((s(172), s(y0 + 80)), "Mood", font=font("inter", s(26), 600), fill=(200, 208, 230, 255))
    d.rounded_rectangle([s(250), s(y0 + 86), s(446), s(y0 + 108)], s(11), fill=(255, 255, 255, 40))
    d.rounded_rectangle([s(250), s(y0 + 86), s(250 + 196 * 0.78), s(y0 + 108)], s(11), fill=GREEN)
    d.text((s(446), s(y0 + 58)), "Bullish", font=font("inter", s(24), 800), fill=GREEN, anchor="rm")
    # online pill (centre)
    shadowed(L, [s(492), s(y0 + 30), s(760), s(y0 + 102)], s(36), CARD)
    d = ImageDraw.Draw(L)
    d.ellipse([s(518), s(y0 + 55), s(540), s(y0 + 77)], fill=GREEN)
    d.text((s(556), s(y0 + 66)), "online 1,204", font=font("inter", s(30), 700), fill=INK, anchor="lm")
    # cash pill with + button (right)
    shadowed(L, [s(782), s(y0 + 18), s(1164), s(y0 + 114)], s(48), CARD)
    d = ImageDraw.Draw(L)
    d.text((s(812), s(y0 + 36)), "CASH", font=font("inter", s(20), 800), fill=GOLD)
    d.text((s(810), s(y0 + 58)), "$3,000", font=font("inter", s(44), 900), fill=INK)
    d.ellipse([s(1066), s(y0 + 30), s(1142), s(y0 + 106)], fill=GREEN)
    d.line([(s(1104), s(y0 + 48)), (s(1104), s(y0 + 88))], fill=(10, 40, 20, 255), width=s(8))
    d.line([(s(1084), s(y0 + 68)), (s(1124), s(y0 + 68))], fill=(10, 40, 20, 255), width=s(8))

    # ---------------- toast (Town Crier)
    ty = 262
    shadowed(L, [s(36), s(ty), s(1164), s(ty + 116)], s(32), (255, 255, 255, 240))
    d = ImageDraw.Draw(L)
    d.rounded_rectangle([s(36), s(ty), s(52), s(ty + 116)], s(8), fill=RED)
    d.rounded_rectangle([s(76), s(ty + 30), s(222), s(ty + 86)], s(28), fill=RED)
    d.text((s(149), s(ty + 58)), "JUST IN", font=font("inter", s(26), 900), fill=INK, anchor="mm")
    d.text((s(244), s(ty + 30)), "$4.2M longs liquidated", font=font("inter", s(40), 800), fill=(18, 20, 30, 255))
    d.text((s(244), s(ty + 76)), "Town Crier  ·  Timeline Square  ·  now", font=font("inter", s(24), 600), fill=(110, 116, 135, 255))

    # ---------------- speech bubble over a character
    bx, by = pts["gm_head"][0] * W, pts["gm_head"][1] * H
    tw = 230
    bub = [bx - tw / 2, by - 150, bx + tw / 2, by - 58]
    shadowed(L, [s(v) for v in bub], s(46), (255, 255, 255, 250), shadow=30, blur=12)
    d = ImageDraw.Draw(L)
    d.polygon([(s(bx - 22), s(by - 62)), (s(bx + 22), s(by - 62)), (s(bx), s(by - 26))], fill=(255, 255, 255, 250))
    d.text((s(bx), s((bub[1] + bub[3]) / 2)), "gm ser", font=font("inter", s(44), 800), fill=(18, 20, 30, 255), anchor="mm")

    # ---------------- bottom bar
    bh = 196
    yb = H - 70 - bh
    shadowed(L, [s(36), s(yb), s(1164), s(yb + bh)], s(56), CARD)
    d = ImageDraw.Draw(L)
    items = [("Map", icon_map), ("Phone", icon_phone), ("Mic", icon_mic), ("Camera", icon_camera)]
    cw = (1164 - 36) / 4
    for i, (label, fn) in enumerate(items):
        cx = 36 + cw * (i + 0.5)
        if label == "Mic":  # live voice: green ring
            d.ellipse([s(cx - 58), s(yb + 22), s(cx + 58), s(yb + 138)], fill=(38, 222, 129, 60), outline=GREEN, width=s(4))
        fn(d, s(cx), s(yb + 80), s(32), INK)
        d.text((s(cx), s(yb + 166)), label, font=font("inter", s(28), 700), fill=INK, anchor="mm")
    # notification dot on Phone
    pcx = 36 + cw * 1.5
    d.ellipse([s(pcx + 22), s(yb + 30), s(pcx + 50), s(yb + 58)], fill=RED)
    d.text((s(pcx + 36), s(yb + 44)), "3", font=font("inter", s(20), 900), fill=INK, anchor="mm")

    L = L.resize((W, H), Image.LANCZOS)
    out = base.convert("RGBA")
    out.alpha_composite(L)
    return out.convert("RGB")


def main(which):
    if "hero" in which:
        raw = Image.open(os.path.join(R, "timeline_square_hero_raw.png"))
        im = grade(raw, leak=(0.05, 0.0), haze_frac=0.30, haze_strength=0.32, vignette=0.2)
        im.save(os.path.join(HERE, "timeline-square-keyart.png"))
        print("wrote timeline-square-keyart.png", im.size)
    if "phone" in which:
        raw = Image.open(os.path.join(R, "timeline_square_phone_raw.png"))
        with open(os.path.join(R, "timeline_square_phone_raw.json")) as f:
            pts = json.load(f)
        im = grade(raw, leak=(0.0, 0.05), haze_frac=0.22, haze_strength=0.28, vignette=0.18)
        im = hud(im, pts)
        im.save(os.path.join(HERE, "timeline-square-phone.png"))
        print("wrote timeline-square-phone.png", im.size)
    if "cafe" in which:
        raw = Image.open(os.path.join(R, "terminal_cafe_raw.png"))
        im = grade(raw, bloom=0.75, leak=(0.95, 0.1), leak_strength=0.18, haze_frac=0.0, vignette=0.32, sat=1.04)
        im.save(os.path.join(HERE, "terminal-cafe.png"))
        print("wrote terminal-cafe.png", im.size)


if __name__ == "__main__":
    main(sys.argv[1:] or ["hero", "phone", "cafe"])
