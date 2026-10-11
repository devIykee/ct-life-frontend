#!/usr/bin/env python3
"""Generate the per-venue signage atlases for the CT Life venue dioramas.

All text, layouts and graphics are original parody made for CT Life: no third-party logos, product names or copied
UI. Screens that will show live data (hooks) ship an idle/standby state only ("—" placeholders), never invented
prices, PnL, odds or counts. Fonts: Lilita One and Manrope (SIL OFL 1.1), already bundled with the client
(client/Assets/Resources/UI/Fonts); text is rasterised into the images, no font files are added.

Output per venue: client/Assets/World/Venues/<id>/Textures/<id>_signs.png + <id>_signs.json
(JSON: {"image", "width", "height", "regions": {sign_id: [x, y, w, h]}} in top-left pixel space).

Run (needs Pillow): art/lookdev/.venv/bin/python tools/venues/make_venue_signs.py [venue_id ...]
"""
import json
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONTS = os.path.join(ROOT, "client", "Assets", "Resources", "UI", "Fonts")
OUT = os.path.join(ROOT, "client", "Assets", "World", "Venues")

GREEN = (38, 222, 129)
RED = (255, 71, 87)
GOLD = (255, 196, 61)
WHITE = (245, 247, 252)
GREY = (139, 150, 176)
DIM = (86, 96, 120)
BG = (13, 18, 27)
PANEL = (22, 29, 42)
PURPLE = (153, 69, 255)
BLUE = (61, 126, 255)
TEAL = (31, 191, 184)
PINK = (255, 95, 162)
ORANGE = (255, 138, 61)
CYAN = (86, 240, 255)
BLURPLE = (123, 104, 238)  # parody purple-blue, deliberately not the real brand colour
ORE = (255, 174, 51)


def font(kind, size):
    name = {"display": "LilitaOne-Regular.ttf", "ui": "Manrope-Semibold.ttf", "body": "Manrope-Regular.ttf"}[kind]
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def text_size(d, s, f):
    b = d.textbbox((0, 0), s, font=f)
    return b[2] - b[0], b[3] - b[1], b[1]


def fit_font(d, s, kind, max_w, max_h, start=200):
    size = start
    while size > 8:
        f = font(kind, size)
        w, h, _ = text_size(d, s, f)
        if w <= max_w and h <= max_h:
            return f
        size -= 2
    return font(kind, 8)


DISPLAY_MISSING = set("∞→↓™✓♥▲▼●★⚡")


def center_text(d, box, s, kind, color, max_size=200, stroke=0, stroke_color=None):
    x0, y0, x1, y1 = box
    if kind == "display" and any(c in DISPLAY_MISSING for c in s):
        kind = "ui"
    f = fit_font(d, s, kind, x1 - x0, y1 - y0, max_size)
    w, h, top = text_size(d, s, f)
    d.text(((x0 + x1 - w) / 2, (y0 + y1 - h) / 2 - top), s, font=f, fill=color, stroke_width=stroke,
           stroke_fill=stroke_color)
    return f


def left_text(d, xy, s, f, color):
    _, _, top = text_size(d, s, f)
    d.text((xy[0], xy[1] - top), s, font=f, fill=color)


def vgrad(w, h, top, bottom):
    im = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(im)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return im


def frame(im, color, width=6, radius=18):
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([width // 2, width // 2, im.width - 1 - width // 2, im.height - 1 - width // 2],
                        radius=radius, outline=color, width=width)


# ----------------------------------------------------------------------------- sign kinds

def neon(w, h, text, color, bg=(10, 12, 18), sub=None, sub_color=None):
    im = Image.new("RGB", (w, h), bg)
    glow = Image.new("RGB", (w, h), (0, 0, 0))
    gd = ImageDraw.Draw(glow)
    box = (int(w * .05), int(h * .08), int(w * .95), int(h * (.70 if sub else .92)))
    center_text(gd, box, text, "display", color, max_size=h)
    glow = glow.filter(ImageFilter.GaussianBlur(max(4, h // 12)))
    im = ImageChops.screen(ImageChops.screen(im, glow), glow)
    d = ImageDraw.Draw(im)
    center_text(d, box, text, "display", tuple(min(255, c + 80) for c in color), max_size=h)
    if sub:
        center_text(d, (int(w * .1), int(h * .72), int(w * .9), int(h * .94)), sub, "ui", sub_color or GREY, max_size=h)
    return im


def poster(w, h, lines, bg=PANEL, fg=WHITE, accent=GOLD, border=True, kinds=None):
    """Stacked lines; first line is the headline (accent colour)."""
    im = vgrad(w, h, bg, tuple(max(0, c - 10) for c in bg))
    d = ImageDraw.Draw(im)
    if border:
        frame(im, accent, width=max(4, w // 90), radius=max(8, w // 40))
    n = len(lines)
    weights = [2.2] + [1.0] * (n - 1)
    total = sum(weights)
    y = h * .08
    usable = h * .84
    for i, line in enumerate(lines):
        lh = usable * weights[i] / total
        kind = (kinds[i] if kinds else ("display" if i == 0 else "ui"))
        color = accent if i == 0 else fg
        if isinstance(line, tuple):
            line, color = line
        center_text(d, (int(w * .07), int(y + lh * .12), int(w * .93), int(y + lh * .88)), line, kind, color, max_size=int(lh))
        y += lh
    return im


def board(w, h, title, columns, rows=5, footer="LIVE FEED · STANDBY", accent=GREEN, tabs=None, subtitle=None):
    """Idle data board for runtime hooks: header, column headers, placeholder rows, status footer."""
    im = Image.new("RGB", (w, h), BG)
    d = ImageDraw.Draw(im)
    head = int(h * .16)
    d.rectangle([0, 0, w, head], fill=PANEL)
    center_text(d, (int(w * .03), int(head * .12), int(w * (.62 if tabs else .97)), int(head * .9)), title, "display", accent, max_size=head)
    if tabs:
        tx = int(w * .64)
        tw = (w - tx - int(w * .02)) // len(tabs)
        for i, t in enumerate(tabs):
            x0 = tx + i * tw
            d.rounded_rectangle([x0 + 4, int(head * .25), x0 + tw - 4, int(head * .78)], radius=10,
                                fill=accent if i == len(tabs) - 1 else (40, 48, 64))
            center_text(d, (x0 + 8, int(head * .28), x0 + tw - 8, int(head * .75)), t, "ui",
                        BG if i == len(tabs) - 1 else GREY, max_size=head)
    y = head + int(h * .03)
    if subtitle:
        center_text(d, (int(w * .04), y, int(w * .96), y + int(h * .08)), subtitle, "ui", WHITE, max_size=int(h * .08))
        y += int(h * .1)
    colh = int(h * .07)
    cw = (w - int(w * .06)) / len(columns)
    for i, c in enumerate(columns):
        x0 = int(w * .03 + i * cw)
        center_text(d, (x0, y, int(x0 + cw - 6), y + colh), c, "ui", GREY, max_size=colh)
    y += colh + int(h * .015)
    d.line([(int(w * .03), y), (int(w * .97), y)], fill=(48, 58, 78), width=3)
    foot = int(h * .1)
    rh = (h - foot - y - int(h * .03)) / rows
    for r in range(rows):
        ry = int(y + r * rh + rh * .12)
        if r % 2 == 0:
            d.rectangle([int(w * .03), int(y + r * rh), int(w * .97), int(y + (r + 1) * rh)], fill=(17, 23, 34))
        for i in range(len(columns)):
            x0 = int(w * .03 + i * cw)
            center_text(d, (x0, ry, int(x0 + cw - 6), int(ry + rh * .76)), "—", "ui", DIM, max_size=int(rh))
    d.rectangle([0, h - foot, w, h], fill=PANEL)
    d.ellipse([int(w * .03), h - foot + foot // 3, int(w * .03) + foot // 3, h - foot // 3], fill=(255, 176, 59))
    center_text(d, (int(w * .06), h - foot + 6, int(w * .97), h - 6), footer, "ui", GREY, max_size=foot)
    return im


def ticker(w, h, text, color=WHITE, bg=(16, 8, 10), accent=RED):
    im = Image.new("RGB", (w, h), bg)
    d = ImageDraw.Draw(im)
    tag = "JUST IN"
    f = font("display", int(h * .72))
    tw, th, top = text_size(d, tag, f)
    d.rectangle([0, 0, tw + h, h], fill=accent)
    d.text((h // 2, (h - th) / 2 - top), tag, font=f, fill=WHITE)
    f2 = font("ui", int(h * .6))
    x = tw + h * 1.4
    while x < w:
        sw, sh, stop = text_size(d, text, f2)
        d.text((x, (h - sh) / 2 - stop), text, font=f2, fill=color)
        x += sw + h
    return im


def floor_text(w, h, text, color, bg=(28, 30, 34), sub=None, arrow=None):
    im = Image.new("RGB", (w, h), bg)
    d = ImageDraw.Draw(im)
    frame(im, color, width=max(6, h // 18), radius=max(8, h // 10))
    ax = 0
    if arrow:
        s = int(h * .42)
        cx, cy = int(w * .11), h // 2
        if arrow == "down":
            d.polygon([(cx - s // 2, cy - s // 4), (cx + s // 2, cy - s // 4), (cx, cy + s // 2)], fill=color)
        else:
            d.polygon([(cx - s // 2, cy - s // 2), (cx + s // 2, cy), (cx - s // 2, cy + s // 2)], fill=color)
        ax = int(w * .22)
    center_text(d, (max(int(w * .06), ax), int(h * .1), int(w * .94), int(h * (.68 if sub else .9))), text, "display", color, max_size=h)
    if sub:
        center_text(d, (int(w * .1), int(h * .68), int(w * .9), int(h * .9)), sub, "ui", GREY, max_size=h)
    return im


def screen(w, h, kind, accent=GREEN, label=None):
    """Small static monitor/kiosk UI art (decorative, no data)."""
    im = Image.new("RGB", (w, h), BG)
    d = ImageDraw.Draw(im)
    rnd = random.Random(hash(kind) & 0xffff)
    bar = int(h * .16)
    d.rectangle([0, 0, w, bar], fill=PANEL)
    center_text(d, (int(w * .03), 2, int(w * .97), bar - 2), label or kind.upper(), "ui", accent, max_size=bar)
    if kind == "perp":
        # Long / short toggle, leverage slider, liq price placeholder.
        d.rounded_rectangle([int(w * .05), int(h * .25), int(w * .47), int(h * .45)], 10, fill=(20, 80, 52))
        d.rounded_rectangle([int(w * .53), int(h * .25), int(w * .95), int(h * .45)], 10, fill=(90, 26, 34))
        center_text(d, (int(w * .05), int(h * .26), int(w * .47), int(h * .44)), "LONG", "display", GREEN, max_size=h)
        center_text(d, (int(w * .53), int(h * .26), int(w * .95), int(h * .44)), "SHORT", "display", RED, max_size=h)
        y = int(h * .6)
        d.line([(int(w * .08), y), (int(w * .92), y)], fill=GREY, width=max(3, h // 60))
        d.ellipse([int(w * .86) - 10, y - 10, int(w * .86) + 10, y + 10], fill=GOLD)
        left_text(d, (int(w * .06), int(h * .66)), "1x", font("ui", int(h * .1)), GREY)
        left_text(d, (int(w * .82), int(h * .66)), "40x", font("ui", int(h * .1)), GOLD)
        center_text(d, (int(w * .05), int(h * .8), int(w * .95), int(h * .95)), "LIQ PRICE  —", "ui", WHITE, max_size=h)
    elif kind == "pulse":
        cols = ["FRESH MINTS", "FINAL LAP", "GRADUATED"]
        cw = w / 3
        for i, c in enumerate(cols):
            x0 = int(i * cw)
            center_text(d, (x0 + 6, bar + 6, int(x0 + cw - 6), bar + int(h * .12)), c, "ui", [GREEN, GOLD, CYAN][i], max_size=h)
            for r in range(3):
                y0 = bar + int(h * .16) + r * int(h * .22)
                d.rounded_rectangle([x0 + 8, y0, int(x0 + cw - 8), y0 + int(h * .18)], 8, fill=(26, 34, 48))
                d.ellipse([x0 + 14, y0 + 8, x0 + 14 + int(h * .12), y0 + 8 + int(h * .12)], fill=(48, 58, 78))
                d.rectangle([x0 + 22 + int(h * .12), y0 + 12, int(x0 + cw * .7), y0 + 22], fill=(60, 70, 92))
                d.polygon([(int(x0 + cw - 30), y0 + 8), (int(x0 + cw - 40), y0 + int(h * .1)), (int(x0 + cw - 32), y0 + int(h * .1)),
                           (int(x0 + cw - 38), y0 + int(h * .17)), (int(x0 + cw - 22), y0 + int(h * .07)), (int(x0 + cw - 30), y0 + int(h * .07))],
                          fill=GOLD)
    elif kind == "odds":
        for r in range(2):
            y0 = bar + int(h * .08) + r * int(h * .4)
            d.rounded_rectangle([int(w * .04), y0, int(w * .96), y0 + int(h * .34)], 10, fill=(24, 32, 46))
            d.rectangle([int(w * .08), y0 + int(h * .07), int(w * .6), y0 + int(h * .13)], fill=(60, 70, 92))
            d.rounded_rectangle([int(w * .62), y0 + int(h * .05), int(w * .78), y0 + int(h * .28)], 8, fill=(20, 80, 52))
            d.rounded_rectangle([int(w * .8), y0 + int(h * .05), int(w * .94), y0 + int(h * .28)], 8, fill=(90, 26, 34))
            center_text(d, (int(w * .62), y0 + int(h * .06), int(w * .78), y0 + int(h * .27)), "YES ¢—", "ui", GREEN, max_size=h)
            center_text(d, (int(w * .8), y0 + int(h * .06), int(w * .94), y0 + int(h * .27)), "NO ¢—", "ui", RED, max_size=h)
    elif kind in ("code", "diff"):
        y = bar + 10
        while y < h - 14:
            indent = rnd.choice([0, 0, 1, 2, 2, 3]) * int(w * .05)
            lw = rnd.randint(int(w * .15), int(w * .6))
            if kind == "diff" and rnd.random() < .35:
                c = (40, 120, 70) if rnd.random() < .5 else (140, 40, 50)
                d.rectangle([0, y - 3, w, y + int(h * .05)], fill=tuple(int(v * .35) for v in c))
            d.rectangle([int(w * .04) + indent, y, int(w * .04) + indent + lw, y + int(h * .035)],
                        fill=rnd.choice([(97, 175, 239), (198, 120, 221), (152, 195, 121), (229, 192, 123), (171, 178, 191)]))
            y += int(h * .075)
    elif kind == "sheet":
        cols, rows = 6, 9
        for r in range(rows):
            for c in range(cols):
                x0 = int(w * .03 + c * w * .94 / cols)
                y0 = bar + 6 + int(r * (h - bar - 12) / rows)
                d.rectangle([x0, y0, int(x0 + w * .94 / cols) - 3, int(y0 + (h - bar - 12) / rows) - 3],
                            fill=(30, 60, 40) if c == 0 else (26, 32, 44))
                if c == 0:
                    d.rectangle([x0 + 6, y0 + 6, x0 + int(w * .1), y0 + 14], fill=(90, 200, 120))
    elif kind == "vitals":
        y = int(h * .6)
        pts = []
        for i in range(0, w, 8):
            t = i / w
            v = y + int(h * .25 * t)
            if 0.2 < (t * 5) % 1 < 0.28:
                v -= int(h * .3 * (1 - t))
            pts.append((i, v))
        d.line(pts, fill=RED, width=max(3, h // 50))
        left_text(d, (int(w * .05), bar + int(h * .06)), "PNL  —", font("ui", int(h * .12)), RED)
    elif kind == "checker":
        center_text(d, (int(w * .08), int(h * .3), int(w * .92), int(h * .55)), "CONNECT WALLET", "display", accent, max_size=h)
        d.rounded_rectangle([int(w * .25), int(h * .65), int(w * .75), int(h * .85)], 14, fill=accent)
        center_text(d, (int(w * .27), int(h * .67), int(w * .73), int(h * .83)), "CHECK", "display", BG, max_size=h)
    elif kind == "stake":
        names = ["STAKE", "UNSTAKE", "CLAIM", "COMPOUND"]
        for i, n in enumerate(names):
            x0 = int(w * .04 + (i % 2) * w * .47)
            y0 = bar + int(h * .1) + (i // 2) * int(h * .36)
            d.rounded_rectangle([x0, y0, x0 + int(w * .44), y0 + int(h * .3)], 12, fill=(60, 44, 14) if i == 0 else (34, 30, 24))
            center_text(d, (x0 + 8, y0 + 8, x0 + int(w * .44) - 8, y0 + int(h * .3) - 8), n, "display", ORE, max_size=h)
    elif kind == "chat":
        for r in range(5):
            y0 = bar + 10 + r * int(h * .16)
            d.ellipse([int(w * .04), y0, int(w * .04) + int(h * .1), y0 + int(h * .1)], fill=[BLURPLE, PINK, TEAL, GOLD, GREEN][r])
            d.rounded_rectangle([int(w * .16), y0, int(w * (.5 + .08 * (r % 3))), y0 + int(h * .1)], 8, fill=(40, 46, 64))
    return im


def gauge(w, h, title):
    im = Image.new("RGB", (w, h), PANEL)
    d = ImageDraw.Draw(im)
    frame(im, GOLD, 6)
    cx, cy, r = w // 2, int(h * .9), int(min(w * .36, h * .62))
    steps = 40
    for i in range(steps):
        a0 = math.pi + math.pi * i / steps
        a1 = math.pi + math.pi * (i + 1) / steps - 0.01
        t = i / (steps - 1)
        col = (int(38 + (255 - 38) * t), int(222 - (222 - 71) * t), int(129 - (129 - 87) * t))
        d.pieslice([cx - r, cy - r, cx + r, cy + r], math.degrees(a0), math.degrees(a1), fill=col)
    d.pieslice([cx - int(r * .72), cy - int(r * .72), cx + int(r * .72), cy + int(r * .72)], 180, 360, fill=PANEL)
    for lab, t in (("1x", 0.0), ("10x", .25), ("20x", .5), ("40x", 1.0)):
        a = math.pi + math.pi * t
        tx, ty = cx + math.cos(a) * r * .58, cy + math.sin(a) * r * .58
        f = font("ui", int(h * .08))
        tw, th, top = text_size(d, lab, f)
        d.text((tx - tw / 2, ty - th / 2 - top), lab, font=f, fill=WHITE)
    a = math.pi * 1.93
    d.line([(cx, cy), (cx + math.cos(a) * r * .95, cy + math.sin(a) * r * .95)], fill=WHITE, width=max(6, w // 120))
    d.ellipse([cx - 14, cy - 14, cx + 14, cy + 14], fill=WHITE)
    center_text(d, (int(w * .05), int(h * .03), int(w * .95), int(h * .2)), title, "display", GOLD, max_size=h)
    return im


def heat(w, h, title, sub):
    im = Image.new("RGB", (w, h), (12, 10, 30))
    d = ImageDraw.Draw(im)
    rnd = random.Random(7)
    top = int(h * .2)
    for y in range(top, h - int(h * .1), 6):
        for x in range(int(w * .04), int(w * .96), 12):
            v = abs(math.sin(y * .021) * math.cos(x * .008)) * rnd.uniform(.5, 1)
            if v > .78:
                col = (255, 230, 70)
            elif v > .55:
                col = (70, 200, 160)
            elif v > .3:
                col = (60, 70, 160)
            else:
                col = (30, 24, 70)
            d.rectangle([x, y, x + 11, y + 5], fill=col)
    d.rectangle([0, 0, w, top], fill=PANEL)
    center_text(d, (int(w * .03), 6, int(w * .97), top - 6), title, "display", GOLD, max_size=top)
    d.rectangle([0, h - int(h * .1), w, h], fill=PANEL)
    center_text(d, (int(w * .03), h - int(h * .1) + 4, int(w * .97), h - 4), sub, "ui", GREY, max_size=h)
    return im


def window_view(w, h, kind):
    if kind == "night":
        im = vgrad(w, h, (20, 24, 60), (90, 60, 110))
    elif kind == "marina":
        im = vgrad(w, h, (120, 180, 230), (250, 205, 160))
    else:
        im = vgrad(w, h, (110, 170, 225), (255, 210, 165))
    d = ImageDraw.Draw(im)
    rnd = random.Random(len(kind) * 31)
    base = int(h * (.62 if kind == "marina" else .9))
    x = 0
    while x < w:
        bw = rnd.randint(int(w * .05), int(w * .11))
        bh = rnd.randint(int(h * .25), int(h * .8))
        col = (40, 50, 80) if kind == "night" else (rnd.randint(110, 150), rnd.randint(130, 165), rnd.randint(165, 200))
        d.rectangle([x, base - bh, x + bw, base], fill=col)
        for wy in range(base - bh + 8, base - 6, 14):
            for wx in range(x + 5, x + bw - 6, 10):
                if rnd.random() < (.55 if kind == "night" else .25):
                    d.rectangle([wx, wy, wx + 4, wy + 6], fill=(255, 220, 140) if kind == "night" else (220, 235, 245))
        x += bw + rnd.randint(2, int(w * .03))
    if kind == "marina":
        d.rectangle([0, base, w, h], fill=(40, 130, 170))
        for i in range(12):
            y = base + 6 + i * (h - base) // 12
            d.line([(rnd.randint(0, w // 2), y), (rnd.randint(w // 2, w), y)], fill=(120, 200, 225), width=2)
        for yx in (w * .2, w * .62):
            yb = base + int((h - base) * .35)
            d.polygon([(yx, yb), (yx + w * .16, yb), (yx + w * .13, yb + h * .06), (yx + w * .02, yb + h * .06)], fill=WHITE)
            d.rectangle([yx + w * .04, yb - h * .05, yx + w * .11, yb], fill=(235, 238, 240))
    return im


def tomb(w, h, text):
    im = Image.new("RGB", (w, h), (120, 124, 128))
    d = ImageDraw.Draw(im)
    center_text(d, (int(w * .08), int(h * .1), int(w * .92), int(h * .9)), text, "display", (40, 42, 46), max_size=h)
    return im


def badge(w, h, text, color, fg=BG):
    im = Image.new("RGB", (w, h), color)
    d = ImageDraw.Draw(im)
    center_text(d, (int(w * .08), int(h * .12), int(w * .92), int(h * .88)), text, "display", fg, max_size=h)
    return im


def ore_idle(size=256):
    """5×5 idle ORE board texture is generated in C# (point-filtered); here the frame label only."""
    return None


# ----------------------------------------------------------------------------- Spaces Studio (no letters anywhere)

def waveform(w, h, seed=3, bg=(10, 12, 30)):
    im = vgrad(w, h, bg, (18, 14, 44))
    d = ImageDraw.Draw(im)
    rnd = random.Random(seed)
    n = 72
    bw = w / n
    mid = h / 2
    for i in range(n):
        t = i / (n - 1)
        env = math.sin(t * math.pi) ** 0.8
        a = (0.18 + 0.82 * abs(math.sin(i * 0.37) * math.cos(i * 0.11 + 1.3))) * env * (0.75 + 0.25 * rnd.random())
        hh = a * h * 0.42
        e = abs(t - 0.5) * 2  # cyan core, violet tails
        col = (int(60 + 120 * e), int(235 - 120 * e), int(240 + 15 * e))
        x0 = int(i * bw + bw * 0.2)
        d.rounded_rectangle([x0, int(mid - hh), int(x0 + bw * 0.6), int(mid + hh)], radius=max(2, int(bw * 0.3)), fill=col)
    d.line([(0, int(mid)), (w, int(mid))], fill=(60, 50, 110), width=2)
    glow = im.filter(ImageFilter.GaussianBlur(6))
    return ImageChops.screen(im, glow)


def lanyards(w, h, seed=11):
    im = Image.new("RGB", (w, h), (34, 30, 40))
    d = ImageDraw.Draw(im)
    rnd = random.Random(seed)
    cols, rows = 8, 3
    cw, rh = w / cols, h / rows
    straps = [(153, 69, 255), (20, 241, 149), (255, 196, 61), (61, 126, 255), (255, 95, 162), (31, 191, 184), (255, 138, 61), (240, 240, 240)]
    for r in range(rows):
        for c in range(cols):
            x0, y0 = c * cw, r * rh
            sc = straps[(r * 3 + c) % len(straps)]
            cx = x0 + cw / 2 + rnd.uniform(-6, 6)
            # strap V
            d.polygon([(cx - cw * .28, y0 + 4), (cx - cw * .22, y0 + 4), (cx + 3, y0 + rh * .42), (cx - 3, y0 + rh * .42)], fill=sc)
            d.polygon([(cx + cw * .22, y0 + 4), (cx + cw * .28, y0 + 4), (cx + 3, y0 + rh * .42), (cx - 3, y0 + rh * .42)], fill=sc)
            # clip + card
            d.rectangle([cx - 5, y0 + rh * .40, cx + 5, y0 + rh * .47], fill=(170, 170, 180))
            card = [cx - cw * .30, y0 + rh * .46, cx + cw * .30, y0 + rh * .96]
            d.rounded_rectangle(card, radius=6, fill=(246, 244, 238))
            d.rectangle([card[0], card[1], card[2], card[1] + (card[3] - card[1]) * .22], fill=sc)
            # avatar circle + abstract bars (no letters)
            ar = (card[2] - card[0]) * .22
            acx, acy = cx, card[1] + (card[3] - card[1]) * .48
            d.ellipse([acx - ar, acy - ar, acx + ar, acy + ar], fill=straps[(c + r * 2 + 3) % len(straps)])
            for k in range(2):
                yy = card[1] + (card[3] - card[1]) * (.74 + k * .1)
                d.rounded_rectangle([card[0] + 8, yy, card[2] - 8 - k * 14, yy + 5], radius=2, fill=(190, 190, 200))
    return im


def sky_evening(w, h):
    im = vgrad(w, h, (38, 46, 110), (255, 170, 120))
    px = im.load()
    for y in range(h):
        t = y / (h - 1)
        if t > 0.55:
            k = (t - 0.55) / 0.45
            for x in range(w):
                r, g, b = px[x, y]
                px[x, y] = (min(255, int(r + 30 * k)), min(255, int(g + 10 * k)), b)
    d = ImageDraw.Draw(im)
    rnd = random.Random(5)
    for _ in range(9):
        cx, cy = rnd.uniform(0, w), rnd.uniform(h * .25, h * .6)
        for k in range(5):
            rw, rh = rnd.uniform(60, 160), rnd.uniform(14, 30)
            ox = rnd.uniform(-60, 60)
            d.ellipse([cx + ox - rw, cy - rh, cx + ox + rw, cy + rh], fill=(255, 190, 170) if cy > h * .45 else (150, 130, 190))
    im = im.filter(ImageFilter.GaussianBlur(2))
    # Layered mountain ridges on the horizon (the concept's evening range), far = lighter haze.
    d = ImageDraw.Draw(im)
    rnd = random.Random(9)
    for layer, (base, col) in enumerate([(0.80, (126, 96, 150)), (0.87, (88, 66, 118)), (0.93, (58, 46, 92))]):
        pts, x = [(0, h)], 0.0
        y = h * base
        while x <= w:
            pts.append((x, y))
            x += rnd.uniform(18, 46)
            y = min(h * (base + 0.05), max(h * (base - 0.12 + layer * 0.03), y + rnd.uniform(-22, 22)))
        pts.append((w, h))
        d.polygon(pts, fill=col)
    return im


def lanyard_frame(w, h):
    """Framed board of three big conference lanyards (purple / teal / yellow straps, geometric badge marks, no letters)."""
    im = Image.new("RGB", (w, h), (92, 70, 54))
    d = ImageDraw.Draw(im)
    d.rectangle([14, 14, w - 15, h - 15], fill=(226, 222, 214))
    straps = [((153, 69, 255), "circle"), ((31, 191, 184), "triangle"), ((255, 196, 61), "triangle")]
    cw = (w - 28) / 3
    for i, (col, mark) in enumerate(straps):
        cx = 14 + cw * (i + .5)
        top, clip = 22, h * .52
        d.polygon([(cx - cw * .16, top), (cx - cw * .08, top), (cx + 4, clip), (cx - 4, clip)], fill=col)
        d.polygon([(cx + cw * .08, top), (cx + cw * .16, top), (cx + 4, clip), (cx - 4, clip)], fill=col)
        d.rectangle([cx - 7, clip - 4, cx + 7, clip + 10], fill=(160, 160, 170))
        card = [cx - cw * .24, clip + 8, cx + cw * .24, h - 30]
        d.rounded_rectangle(card, radius=8, fill=(84, 52, 120) if i == 0 else (40, 44, 60))
        d.rectangle([card[0], card[1], card[2], card[1] + 16], fill=col)
        mx, my, r = cx, (card[1] + card[3]) / 2 + 8, cw * .11
        if mark == "circle":
            d.ellipse([mx - r, my - r, mx + r, my + r], outline=col, width=9)
        else:
            d.polygon([(mx, my - r), (mx + r, my + r * .8), (mx - r, my + r * .8)], fill=col)
    return im


FLAG_SETS = [
    ("h", [(153, 69, 255), (255, 196, 61)]), ("h", [(255, 196, 61), (153, 69, 255)]),
    ("v", [(0, 135, 81), (245, 245, 245), (0, 135, 81)]), ("h", [(255, 153, 51), (245, 245, 245), (19, 136, 8)]),
    ("v", [(0, 85, 164), (245, 245, 245), (239, 65, 53)]), ("h", [(20, 20, 20), (221, 0, 0), (255, 206, 0)]),
    ("cross", [(0, 106, 167), (254, 204, 0)]), ("h", [(218, 37, 29), (255, 255, 0)]), ("v", [(0, 146, 70), (245, 245, 245), (206, 43, 55)]),
    ("h", [(245, 245, 245), (213, 43, 30)]), ("diag", [(0, 155, 58), (254, 223, 0)]), ("h", [(255, 205, 0), (0, 56, 147), (206, 17, 38)]),
    ("v", [(0, 158, 96), (252, 209, 22)]), ("cross", [(200, 16, 46), (245, 245, 245)]), ("h", [(0, 56, 168), (206, 17, 38)]),
    ("diag", [(153, 69, 255), (255, 196, 61)]),
]


def flag(kind, colors, w=96, h=64):
    im = Image.new("RGB", (w, h), colors[0])
    d = ImageDraw.Draw(im)
    n = len(colors)
    if kind == "h":
        for i, c in enumerate(colors):
            d.rectangle([0, int(i * h / n), w, int((i + 1) * h / n)], fill=c)
    elif kind == "v":
        for i, c in enumerate(colors):
            d.rectangle([int(i * w / n), 0, int((i + 1) * w / n), h], fill=c)
    elif kind == "cross":
        d.rectangle([0, 0, w, h], fill=colors[0])
        d.rectangle([int(w * .3), 0, int(w * .42), h], fill=colors[1])
        d.rectangle([0, int(h * .42), w, int(h * .58)], fill=colors[1])
    elif kind == "diag":
        d.polygon([(0, 0), (w, 0), (0, h)], fill=colors[0])
        d.polygon([(w, 0), (w, h), (0, h)], fill=colors[1])
    return im


def stage_feed(w, h):
    im = waveform(w, h, seed=9, bg=(12, 10, 26))
    d = ImageDraw.Draw(im)
    for i, x in enumerate((w * .3, w * .5, w * .7)):
        d.ellipse([x - 26, h * .58, x + 26, h * .58 + 52], fill=(40, 30, 70))
        d.rounded_rectangle([x - 40, h * .58 + 50, x + 40, h * .98], radius=20, fill=(40, 30, 70))
    return im


def rug(w, h, seed, base, accent, round_=False):
    """Woven rug: border bands, a medallion lattice and fringe-free edges (no letters)."""
    im = Image.new("RGB", (w, h), base)
    d = ImageDraw.Draw(im)
    rnd = random.Random(seed)
    if round_:
        # Square rug with a round medallion (sign panels are opaque, so no bare corners).
        for k in range(3):
            m = 8 + k * 14
            d.rectangle([m, m, w - m, h - m], outline=accent if k != 1 else tuple(min(255, c + 30) for c in base), width=6)
        cx, cy = w / 2, h / 2
        for k, col in enumerate([accent, base, tuple(min(255, c + 25) for c in base), accent, base]):
            r = w * (0.42 - k * 0.065)
            d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=col, width=max(3, w // 60))
        for i in range(16):
            a = i * math.pi / 8
            x, y = cx + math.cos(a) * w * .26, cy + math.sin(a) * w * .26
            d.ellipse([x - 6, y - 6, x + 6, y + 6], fill=accent)
    else:
        for k in range(3):
            m = 8 + k * 14
            d.rectangle([m, m, w - m, h - m], outline=accent if k != 1 else tuple(min(255, c + 30) for c in base), width=6)
        step = 48
        for y in range(70, h - 60, step):
            for x in range(70, w - 60, step):
                d.polygon([(x, y - 14), (x + 14, y), (x, y + 14), (x - 14, y)], outline=accent, width=3)
    px = im.load()
    for _ in range(w * h // 6):
        x, y = rnd.randrange(w), rnd.randrange(h)
        r, g, b = px[x, y]
        j = rnd.randint(-10, 10)
        px[x, y] = (max(0, min(255, r + j)), max(0, min(255, g + j)), max(0, min(255, b + j)))
    return im


def gig_poster(w, h, seed):
    rnd = random.Random(seed)
    base = rnd.choice([(255, 196, 61), (153, 69, 255), (31, 191, 184), (255, 95, 162)])
    im = vgrad(w, h, base, tuple(max(0, c - 70) for c in base))
    d = ImageDraw.Draw(im)
    cx, cy = w / 2, h * .42
    for k in range(6, 0, -1):
        r = k * w * .07
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(255, 255, 255) if k % 2 else (20, 20, 30), width=4)
    d.rectangle([w * .15, h * .8, w * .85, h * .84], fill=(255, 255, 255))
    return im


# ----------------------------------------------------------------------------- venue sign sets


# ---------------------------------------------------------------------------------------------------------------
# Timeline Square (round 3 look-dev, to ingame-timeline-concept-v1.png / title-bg-eggs-v1.png): painted, letter-free.

def _jitter(im, rnd, amount=8, frac=4):
    px = im.load()
    w, h = im.size
    for _ in range(w * h // frac):
        x, y = rnd.randrange(w), rnd.randrange(h)
        r, g, b = px[x, y][:3]
        j = rnd.randint(-amount, amount)
        px[x, y] = (max(0, min(255, r + j)), max(0, min(255, g + j)), max(0, min(255, b + j)))
    return im


def cobbles(size, seed, palette, grout):
    """Tileable painted cobblestones: rounded stones on a wrapped jittered grid, soft top light, dark grout."""
    rnd = random.Random(seed)
    im = Image.new("RGB", (size, size), grout)
    d = ImageDraw.Draw(im)
    n = 12
    cell = size / n
    for gy in range(n):
        off = (gy % 2) * cell * .5
        for gx in range(n):
            cx = gx * cell + off + rnd.uniform(-cell * .08, cell * .08)
            cy = gy * cell + cell * .5 + rnd.uniform(-cell * .08, cell * .08)
            rw, rh = cell * rnd.uniform(.34, .49), cell * rnd.uniform(.30, .46)
            base = rnd.choice(palette)
            tone = rnd.randint(-14, 14)
            col = tuple(max(0, min(255, c + tone)) for c in base)
            for dx in (-size, 0, size):
                for dy in (-size, 0, size):
                    x, y = cx + dx, cy + dy
                    d.rounded_rectangle([x - rw, y - rh, x + rw, y + rh], radius=rh * .7, fill=col)
                    hl = tuple(min(255, c + 12) for c in col)
                    d.rounded_rectangle([x - rw * .78, y - rh * .82, x + rw * .55, y - rh * .05], radius=rh * .5, fill=hl)
                    sh = tuple(max(0, c - 20) for c in col)
                    d.rounded_rectangle([x - rw * .9, y + rh * .45, x + rw * .9, y + rh * .92], radius=rh * .4, fill=sh)
    im = im.filter(ImageFilter.GaussianBlur(1.2))
    return _jitter(im, rnd, 7, 3)


def stucco(size, seed, base):
    """Warm painted plaster: soft mottling, a few hairline cracks and a darker plinth wash at the bottom."""
    rnd = random.Random(seed)
    im = Image.new("RGB", (size, size), base)
    d = ImageDraw.Draw(im)
    for _ in range(60):
        x, y, r = rnd.uniform(0, size), rnd.uniform(0, size), rnd.uniform(size * .05, size * .18)
        t = rnd.randint(-12, 10)
        d.ellipse([x - r, y - r * .7, x + r, y + r * .7], fill=tuple(max(0, min(255, c + t)) for c in base))
    im = im.filter(ImageFilter.GaussianBlur(size / 28))
    d = ImageDraw.Draw(im)
    for _ in range(5):
        x, y = rnd.uniform(0, size), rnd.uniform(0, size)
        pts = [(x, y)]
        for _ in range(5):
            x += rnd.uniform(-10, 10); y += rnd.uniform(4, 14)
            pts.append((x, y))
        d.line(pts, fill=tuple(max(0, c - 40) for c in base), width=1)
    for y in range(int(size * .86), size):
        k = (y - size * .86) / (size * .14)
        for x in range(size):
            r, g, b = im.getpixel((x, y))
            im.putpixel((x, y), (int(r * (1 - .18 * k)), int(g * (1 - .2 * k)), int(b * (1 - .22 * k))))
    return _jitter(im, rnd, 5, 4)


def roof_tiles(w, h, seed, base=(196, 98, 58)):
    """Terracotta barrel tiles in courses, each tile lit on its left and shaded on its right (letter-free)."""
    rnd = random.Random(seed)
    im = Image.new("RGB", (w, h), tuple(max(0, c - 45) for c in base))
    d = ImageDraw.Draw(im)
    rows, cols = 9, 14
    rh, cw = h / rows, w / cols
    for r in range(rows):
        for c in range(cols + 1):
            x = c * cw + (r % 2) * cw * .5
            col = tuple(max(0, min(255, v + rnd.randint(-18, 14))) for v in base)
            d.rounded_rectangle([x - cw * .46, r * rh + 2, x + cw * .46, (r + 1) * rh + rh * .25], radius=cw * .4, fill=col)
            d.rectangle([x - cw * .34, r * rh + 4, x - cw * .12, (r + 1) * rh], fill=tuple(min(255, v + 28) for v in col))
            d.rectangle([x + cw * .2, r * rh + 4, x + cw * .44, (r + 1) * rh], fill=tuple(max(0, v - 30) for v in col))
    return _jitter(im.filter(ImageFilter.GaussianBlur(1.0)), rnd, 6, 4)


def solana_sky(w, h):
    """Golden-hour sky with the Solana gradient as a wash (lavender top to mint and peach), soft clouds, the sea, far
    hills and a terracotta hill town on the horizon. No bars, no logo."""
    rnd = random.Random(21)
    im = Image.new("RGB", (w, h))
    px = im.load()
    stops = [(0.0, (150, 128, 222)), (0.38, (178, 170, 236)), (0.62, (176, 226, 214)), (0.74, (252, 214, 168))]
    for y in range(h):
        t = y / (h - 1)
        for i in range(len(stops) - 1):
            if stops[i][0] <= t <= stops[i + 1][0] or i == len(stops) - 2:
                a, b = stops[i], stops[i + 1]
                k = max(0.0, min(1.0, (t - a[0]) / max(1e-6, b[0] - a[0])))
                col = tuple(int(a[1][j] + (b[1][j] - a[1][j]) * k) for j in range(3))
                break
        for x in range(w):
            px[x, y] = col
    d = ImageDraw.Draw(im)
    for _ in range(14):
        cx, cy = rnd.uniform(0, w), rnd.uniform(h * .1, h * .45)
        for k in range(6):
            rw, rh = rnd.uniform(40, 120), rnd.uniform(10, 22)
            ox = rnd.uniform(-70, 70)
            d.ellipse([cx + ox - rw, cy - rh, cx + ox + rw, cy + rh], fill=(255, 236, 228) if cy > h * .3 else (226, 214, 250))
    im = im.filter(ImageFilter.GaussianBlur(3))
    d = ImageDraw.Draw(im)
    sea = int(h * .74)
    d.rectangle([0, sea, w, h], fill=(78, 150, 188))
    for i in range(40):
        y = sea + 3 + i * 3
        if y >= h: break
        d.line([(rnd.uniform(0, w * .4), y), (rnd.uniform(w * .5, w), y)], fill=(130, 190, 214), width=1)
    for layer, (base, col) in enumerate([(0.70, (150, 168, 150)), (0.76, (112, 142, 104))]):
        pts, x, y = [(0, h)], 0.0, h * base
        while x <= w:
            pts.append((x, y)); x += rnd.uniform(20, 60)
            y = min(h * (base + .06), max(h * (base - .07), y + rnd.uniform(-14, 14)))
        pts.append((w, h))
        if layer == 0:
            pts = [(px_, py_) for px_, py_ in pts]
            d.polygon([(0, h)] + [(x_, y_) for x_, y_ in pts[1:-1] if x_ < w * .45] + [(w * .45, h)], fill=col)
        else:
            d.polygon([(0, h)] + [(x_, y_ + 18) for x_, y_ in pts[1:-1] if x_ < w * .3] + [(w * .3, h)], fill=col)
    for _ in range(70):
        x, y = rnd.uniform(0, w * .42), rnd.uniform(h * .78, h * .95)
        bw = rnd.uniform(8, 18)
        d.rectangle([x, y, x + bw, y + bw * .7], fill=rnd.choice([(238, 222, 196), (226, 200, 160), (244, 232, 214)]))
        d.polygon([(x - 2, y), (x + bw / 2, y - bw * .45), (x + bw + 2, y)], fill=(190, 96, 62))
    return im


def feed_idle(w, h):
    """Timeline screen idle art: a dark feed UI with post cards (avatars, text bars, small reaction icons), no letters."""
    rnd = random.Random(31)
    im = vgrad(w, h, (14, 18, 30), (24, 22, 44))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w, 40], fill=(28, 32, 52))
    for i, col in enumerate([(153, 69, 255), (20, 241, 149), (255, 196, 61)]):
        d.ellipse([16 + i * 30, 12, 32 + i * 30, 28], fill=col)
    cols = 3
    cw = (w - 40) / cols
    for c in range(cols):
        y = 56
        while y < h - 60:
            ch = rnd.randint(110, 170)
            x0 = 16 + c * cw
            d.rounded_rectangle([x0, y, x0 + cw - 14, y + ch], radius=12, fill=(34, 38, 62))
            av = rnd.choice([(153, 69, 255), (20, 241, 149), (255, 196, 61), (61, 126, 255), (255, 95, 162), (31, 191, 184)])
            d.ellipse([x0 + 12, y + 12, x0 + 44, y + 44], fill=av)
            d.rounded_rectangle([x0 + 54, y + 16, x0 + 54 + rnd.randint(60, 120), y + 26], radius=4, fill=(210, 214, 230))
            d.rounded_rectangle([x0 + 54, y + 32, x0 + 54 + rnd.randint(40, 80), y + 39], radius=3, fill=(110, 116, 150))
            for k in range(rnd.randint(2, 3)):
                d.rounded_rectangle([x0 + 14, y + 56 + k * 18, x0 + cw - 30 - rnd.randint(0, 80), y + 66 + k * 18], radius=4, fill=(160, 166, 196))
            for k, ic in enumerate([(120, 200, 255), (20, 241, 149), (255, 95, 162)]):
                d.ellipse([x0 + 14 + k * 46, y + ch - 24, x0 + 26 + k * 46, y + ch - 12], fill=ic)
                d.rounded_rectangle([x0 + 30 + k * 46, y + ch - 21, x0 + 46 + k * 46, y + ch - 15], radius=2, fill=(110, 116, 150))
            y += ch + 12
    return im


def pixel_canvas(w, h, seed=41):
    """Community pixel wall idle art: a coarse pixel mosaic with hearts, a sun, a smiley, a wave and a little rocket."""
    rnd = random.Random(seed)
    g = 8
    cols, rows = w // g, h // g
    base = [(242, 236, 222), (236, 228, 210)]
    grid = [[base[(x + y) % 2] for x in range(cols)] for y in range(rows)]
    def put(x, y, c):
        if 0 <= x < cols and 0 <= y < rows: grid[y][x] = c
    for _ in range(cols * rows // 9):
        put(rnd.randrange(cols), rnd.randrange(rows), rnd.choice([(153, 69, 255), (20, 241, 149), (255, 196, 61), (61, 126, 255), (255, 95, 162), (255, 138, 61), (31, 191, 184)]))
    heart = ["01100110", "11111111", "11111111", "01111110", "00111100", "00011000"]
    for ox, oy, col in [(6, 5, (230, 57, 70)), (cols - 18, rows - 14, (255, 95, 162))]:
        for yy, row in enumerate(heart):
            for xx, ch in enumerate(row):
                if ch == "1": put(ox + xx, oy + yy, col)
    cx, cy, r = cols // 2 + 6, rows // 3, 6
    for y in range(-r - 3, r + 4):
        for x in range(-r - 3, r + 4):
            dd = (x * x + y * y) ** .5
            if dd <= r: put(cx + x, cy + y, (255, 196, 61))
            elif dd <= r + 3 and (x == 0 or y == 0 or abs(x) == abs(y)): put(cx + x, cy + y, (255, 138, 61))
    sx, sy = cols // 4, rows * 2 // 3
    for y in range(-6, 7):
        for x in range(-6, 7):
            if x * x + y * y <= 36: put(sx + x, sy + y, (255, 214, 64))
    for x, y in [(-2, -2), (2, -2)]: put(sx + x, sy + y, (30, 30, 40))
    for x in range(-3, 4): put(sx + x, sy + (1 if abs(x) == 3 else 2 if abs(x) == 2 else 3), (30, 30, 40))
    for x in range(cols):
        y = int(rows * .86 + 2 * math.sin(x / 3))
        for k in range(3): put(x, y + k, (61, 126, 255))
    img = Image.new("RGB", (cols * g, rows * g))
    d = ImageDraw.Draw(img)
    for y in range(rows):
        for x in range(cols):
            d.rectangle([x * g, y * g, x * g + g - 1, y * g + g - 1], fill=grid[y][x])
    return img.resize((w, h), Image.NEAREST)


def billboard_idle(w, h, kind):
    """Letter-free billboard idle art (house ads replace it at runtime)."""
    if kind == 0:
        im = vgrad(w, h, (255, 186, 120), (255, 120, 140))
        d = ImageDraw.Draw(im)
        d.ellipse([w * .55, h * .18, w * .55 + h * .5, h * .68], fill=(255, 240, 200))
        d.rectangle([0, h * .64, w, h], fill=(60, 150, 200))
        for i in range(6):
            d.line([(w * .05, h * (.7 + i * .05)), (w * .95, h * (.7 + i * .05))], fill=(150, 210, 230), width=3)
        d.polygon([(w * .12, h * .64), (w * .2, h * .3), (w * .28, h * .64)], fill=(250, 250, 250))
        d.line([(w * .2, h * .3), (w * .2, h * .66)], fill=(80, 60, 50), width=4)
    else:
        im = vgrad(w, h, (36, 30, 62), (90, 48, 120))
        d = ImageDraw.Draw(im)
        cx, cy = w * .3, h * .55
        d.rounded_rectangle([cx - 60, cy - 70, cx + 60, cy + 70], radius=18, fill=(250, 244, 230))
        d.ellipse([cx + 50, cy - 30, cx + 100, cy + 30], outline=(250, 244, 230), width=12)
        d.rectangle([cx - 60, cy - 70, cx + 60, cy - 40], fill=(200, 130, 70))
        for i in range(3):
            d.arc([cx - 40 + i * 25, cy - 140, cx - 10 + i * 25, cy - 80], 200, 340, fill=(250, 244, 230), width=6)
        for i in range(5):
            x = w * (.55 + i * .08)
            d.rounded_rectangle([x, h * .3, x + w * .05, h * .8], radius=10, fill=[(153, 69, 255), (20, 241, 149), (255, 196, 61), (255, 95, 162), (61, 126, 255)][i])
    return im


def burst_poster(w, h):
    """Hot-red poster with a black jagged lightning-cut frame (no letters, no character)."""
    im = Image.new("RGB", (w, h), (246, 32, 56))
    d = ImageDraw.Draw(im)
    rnd = random.Random(51)
    cx, cy = w / 2, h / 2
    pts = []
    for i in range(22):
        a = i * math.pi * 2 / 22
        r = (min(w, h) * (.46 if i % 2 == 0 else .3)) * rnd.uniform(.9, 1.08)
        pts.append((cx + math.cos(a) * r * (w / min(w, h)) * .9, cy + math.sin(a) * r * (h / min(w, h)) * .9))
    d.polygon(pts, fill=(18, 14, 16))
    inner = [(cx + (x - cx) * .78, cy + (y - cy) * .78) for x, y in pts]
    d.polygon(inner, fill=(246, 32, 56))
    d.rectangle([0, 0, w - 1, h - 1], outline=(18, 14, 16), width=10)
    return im


def clock_face(size):
    im = Image.new("RGB", (size, size), (196, 170, 128))
    d = ImageDraw.Draw(im)
    c, r = size / 2, size * .46
    d.ellipse([c - r, c - r, c + r, c + r], fill=(248, 242, 226), outline=(60, 46, 34), width=size // 40)
    for i in range(60):
        a = i * math.pi / 30
        r0 = r * (.8 if i % 5 == 0 else .88)
        d.line([(c + math.cos(a) * r0, c + math.sin(a) * r0), (c + math.cos(a) * r * .94, c + math.sin(a) * r * .94)],
               fill=(40, 34, 30), width=size // 60 if i % 5 == 0 else 2)
    for ang, ln, wd in [(-math.pi / 2 + 4.2 * math.pi / 6, .5, size // 28), (-math.pi / 2 + 10 * math.pi / 30, .74, size // 44)]:
        d.line([(c, c), (c + math.cos(ang) * r * ln, c + math.sin(ang) * r * ln)], fill=(30, 26, 24), width=wd)
    d.ellipse([c - 10, c - 10, c + 10, c + 10], fill=(30, 26, 24))
    return im


def shop_window(w, h, kind, seed):
    """Warm shop interiors seen through a window (painted, letter-free): bakery, toys, café, parcels."""
    rnd = random.Random(seed)
    im = vgrad(w, h, (120, 78, 48), (70, 44, 30))
    d = ImageDraw.Draw(im)
    for i in range(3):
        y = h * (.32 + i * .26)
        d.rectangle([0, y, w, y + 8], fill=(150, 104, 66))
    d.ellipse([w * .2, -h * .3, w * .8, h * .3], fill=(255, 214, 150))
    im = im.filter(ImageFilter.GaussianBlur(4))
    d = ImageDraw.Draw(im)
    for i in range(3):
        y = h * (.32 + i * .26)
        for k in range(rnd.randint(6, 9)):
            x = rnd.uniform(w * .04, w * .92)
            if kind == "bakery":
                col = rnd.choice([(214, 150, 80), (196, 120, 60), (232, 186, 120), (170, 96, 50)])
                d.ellipse([x, y - 26, x + 48, y + 2], fill=col)
                d.arc([x + 8, y - 22, x + 40, y - 6], 200, 340, fill=tuple(min(255, c + 40) for c in col), width=3)
            elif kind == "toys":
                col = rnd.choice([(230, 70, 70), (70, 130, 230), (250, 200, 60), (90, 190, 110), (200, 110, 220)])
                if k % 3 == 0:
                    d.rectangle([x, y - 30, x + 28, y], fill=col)
                elif k % 3 == 1:
                    d.ellipse([x, y - 34, x + 30, y], fill=col); d.ellipse([x + 4, y - 44, x + 26, y - 26], fill=col)
                else:
                    d.rounded_rectangle([x, y - 18, x + 46, y], radius=6, fill=col); d.ellipse([x + 4, y - 4, x + 14, y + 6], fill=(30, 30, 30)); d.ellipse([x + 32, y - 4, x + 42, y + 6], fill=(30, 30, 30))
            elif kind == "cafe":
                col = rnd.choice([(240, 236, 226), (60, 120, 190), (200, 170, 120)])
                d.rounded_rectangle([x, y - 24, x + 22, y], radius=4, fill=col)
            else:
                col = rnd.choice([(196, 150, 96), (180, 136, 84), (210, 168, 112)])
                d.rectangle([x, y - 34, x + 46, y], fill=col); d.line([(x + 23, y - 34), (x + 23, y)], fill=(220, 200, 160), width=4)
    d.rectangle([0, 0, w - 1, h - 1], outline=(40, 28, 20), width=6)
    d.line([(w / 2, 0), (w / 2, h)], fill=(40, 28, 20), width=6)
    return im


def window_pane(w, h, lit, seed):
    rnd = random.Random(seed)
    im = vgrad(w, h, (90, 110, 140) if not lit else (250, 214, 150), (40, 50, 70) if not lit else (200, 140, 80))
    d = ImageDraw.Draw(im)
    d.polygon([(0, 0), (w * .3, 0), (w * .1, h), (0, h)], fill=(236, 226, 214) if lit else (150, 160, 180))
    d.polygon([(w, 0), (w * .7, 0), (w * .9, h), (w, h)], fill=(236, 226, 214) if lit else (150, 160, 180))
    d.rectangle([0, 0, w - 1, h - 1], outline=(250, 246, 236), width=6)
    d.line([(w / 2, 0), (w / 2, h)], fill=(250, 246, 236), width=5)
    d.line([(0, h * .45), (w, h * .45)], fill=(250, 246, 236), width=5)
    return im



# ---------------------------------------------------------------------------------------------------------------
# Terminal Café (round 5, to ingame-cafe-concept-v1.png): chalk doodles, candle charts, brick, tiles, the town outside.

def _chalk(d, pts, w=4, col=(236, 234, 222)):
    d.line(pts, fill=col, width=w, joint="curve")


def chalk_menu(w, h):
    """Chalkboard with doodles only (no letters): a steaming cup, a croissant, coffee beans, a sun over a cup, dots."""
    rnd = random.Random(201)
    im = Image.new("RGB", (w, h), (34, 44, 38))
    d = ImageDraw.Draw(im)
    for _ in range(140):  # chalk dust smudges
        x, y, r = rnd.uniform(0, w), rnd.uniform(0, h), rnd.uniform(10, 60)
        d.ellipse([x - r, y - r * .5, x + r, y + r * .5], fill=(40 + rnd.randint(0, 8), 52 + rnd.randint(0, 8), 45 + rnd.randint(0, 8)))
    im = im.filter(ImageFilter.GaussianBlur(6))
    d = ImageDraw.Draw(im)
    W = (236, 234, 222)
    # cup
    cx, cy = w * .2, h * .6
    d.rounded_rectangle([cx - 70, cy - 40, cx + 50, cy + 70], radius=24, outline=W, width=6)
    d.arc([cx + 30, cy - 10, cx + 90, cy + 50], -90, 90, fill=W, width=6)
    d.line([(cx - 90, cy + 80), (cx + 70, cy + 80)], fill=W, width=5)
    for k in range(3):
        x0 = cx - 40 + k * 35
        _chalk(d, [(x0, cy - 60), (x0 + 12, cy - 85), (x0, cy - 110), (x0 + 12, cy - 135)], 5)
    # croissant: a crescent of five segments
    ox, oy = w * .5, h * .62
    for k in range(5):
        a0, a1 = 200 + k * 28, 200 + (k + 1) * 28
        rr = 120 - abs(k - 2) * 14
        d.pieslice([ox - rr, oy - rr * .8, ox + rr, oy + rr * .8], a0, a1, outline=W, width=5)
    d.ellipse([ox - 50, oy - 46, ox + 50, oy + 10], fill=(34, 44, 38))
    # coffee beans
    for (bx, by, rot) in [(w * .78, h * .42, 20), (w * .86, h * .6, -30), (w * .74, h * .7, 60)]:
        d.ellipse([bx - 40, by - 26, bx + 40, by + 26], outline=W, width=5)
        _chalk(d, [(bx - 30, by - 4), (bx - 8, by + 6), (bx + 10, by - 6), (bx + 30, by + 4)], 4)
    # little sun and dots border
    for i in range(0, w, 34):
        d.ellipse([i + 8, 16, i + 14, 22], fill=W)
        d.ellipse([i + 8, h - 22, i + 14, h - 16], fill=W)
    return im


def chalk_sun(w, h):
    """A-frame chalk board: a sun rising over a cup (no letters)."""
    im = Image.new("RGB", (w, h), (36, 40, 38))
    d = ImageDraw.Draw(im)
    W = (238, 236, 226)
    cx, cy = w / 2, h * .62
    d.arc([cx - 120, cy - 120, cx + 120, cy + 120], 180, 360, fill=W, width=9)
    for k in range(9):
        a = math.radians(180 + k * 22.5)
        d.line([(cx + math.cos(a) * 150, cy + math.sin(a) * 150), (cx + math.cos(a) * 200, cy + math.sin(a) * 200)], fill=W, width=8)
    d.line([(cx - 190, cy + 4), (cx + 190, cy + 4)], fill=W, width=8)
    d.rounded_rectangle([cx - 70, cy + 40, cx + 50, cy + 150], radius=20, outline=W, width=8)
    d.arc([cx + 30, cy + 60, cx + 90, cy + 120], -90, 90, fill=W, width=8)
    return im


def candles(w, h, seed, accent=(20, 241, 149)):
    """Trading monitor: dark chart with grid, green/red candles and a moving average (no letters or numbers)."""
    rnd = random.Random(seed)
    im = vgrad(w, h, (12, 16, 26), (18, 22, 36))
    d = ImageDraw.Draw(im)
    for gy in range(1, 6):
        d.line([(0, gy * h / 6), (w, gy * h / 6)], fill=(30, 38, 56), width=1)
    for gx in range(1, 10):
        d.line([(gx * w / 10, 0), (gx * w / 10, h)], fill=(30, 38, 56), width=1)
    n = 28
    cw = w / (n + 2)
    price = h * .6
    ma = []
    for i in range(n):
        o = price
        trend = (-1 if seed % 2 else 1) * h * .012
        c = o + rnd.uniform(-h * .07, h * .07) - trend + (h * .55 - o) * .12
        c = min(h * .88, max(h * .14, c))
        hi, lo = min(o, c) - rnd.uniform(4, h * .05), max(o, c) + rnd.uniform(4, h * .05)
        x = (i + 1.5) * cw
        up = c < o
        col = (38, 222, 129) if up else (255, 71, 87)
        d.line([(x, hi), (x, lo)], fill=col, width=max(1, int(cw * .12)))
        d.rectangle([x - cw * .32, min(o, c), x + cw * .32, max(o, c) + 1], fill=col)
        price = c
        ma.append((x, c))
    for i in range(2, len(ma)):
        a = sum(p[1] for p in ma[i - 2:i + 1]) / 3
        b = sum(p[1] for p in ma[i - 3:i]) / 3 if i > 2 else a
        d.line([(ma[i - 1][0], b), (ma[i][0], a)], fill=(255, 196, 61), width=2)
    d.rectangle([0, 0, w, 10], fill=(28, 34, 52))
    return im


def brick_wall(w, h, seed=301):
    """Exposed warm brick with soft mortar and a few darker and lighter bricks."""
    rnd = random.Random(seed)
    im = Image.new("RGB", (w, h), (188, 160, 136))
    d = ImageDraw.Draw(im)
    bh, bw = h / 16, w / 6
    for r in range(16):
        off = (r % 2) * bw * .5
        for c in range(-1, 7):
            x0 = c * bw + off
            base = rnd.choice([(150, 72, 50), (162, 82, 56), (140, 66, 46), (172, 92, 62), (128, 60, 44)])
            j = rnd.randint(-10, 10)
            col = tuple(max(0, min(255, v + j)) for v in base)
            d.rectangle([x0 + 3, r * bh + 3, x0 + bw - 3, (r + 1) * bh - 2], fill=col)
            d.rectangle([x0 + 3, r * bh + 3, x0 + bw - 3, r * bh + 6], fill=tuple(min(255, v + 18) for v in col))
    return _jitter(im.filter(ImageFilter.GaussianBlur(0.8)), rnd, 9, 3)


def green_tiles(w, h, seed=302):
    """Glossy deep-green subway tiles with pale grout."""
    rnd = random.Random(seed)
    im = Image.new("RGB", (w, h), (200, 206, 196))
    d = ImageDraw.Draw(im)
    th, tw = h / 8, w / 4
    for r in range(8):
        off = (r % 2) * tw * .5
        for c in range(-1, 5):
            x0 = c * tw + off
            g = rnd.randint(-8, 8)
            col = (52 + g, 104 + g, 78 + g)
            d.rounded_rectangle([x0 + 2, r * th + 2, x0 + tw - 2, (r + 1) * th - 2], radius=3, fill=col)
            d.line([(x0 + 6, r * th + 6), (x0 + tw * .45, r * th + 6)], fill=(110, 160, 134), width=2)
    return im


def town_view(w, h):
    """The sunny town outside the café windows: warm sky, stucco houses with terracotta roofs, blue awnings, trees."""
    rnd = random.Random(311)
    im = vgrad(w, h, (156, 204, 236), (250, 226, 186))
    d = ImageDraw.Draw(im)
    for _ in range(8):
        cx, cy = rnd.uniform(0, w), rnd.uniform(h * .05, h * .3)
        for k in range(5):
            rw, rh = rnd.uniform(40, 100), rnd.uniform(10, 20)
            ox = rnd.uniform(-50, 50)
            d.ellipse([cx + ox - rw, cy - rh, cx + ox + rw, cy + rh], fill=(255, 250, 240))
    im = im.filter(ImageFilter.GaussianBlur(2))
    d = ImageDraw.Draw(im)
    base = h * .95
    x = -20
    walls = [(238, 222, 190), (230, 184, 118), (234, 180, 146), (226, 214, 196), (214, 196, 150)]
    while x < w:
        bw = rnd.uniform(110, 180)
        bh = rnd.uniform(h * .38, h * .62)
        col = rnd.choice(walls)
        top = base - bh
        d.rectangle([x, top, x + bw, base], fill=col)
        d.polygon([(x - 8, top + 4), (x + bw / 2, top - bh * .18), (x + bw + 8, top + 4)], fill=(196, 98, 60))
        for fy in range(2):
            for fx in range(2):
                wx, wy = x + bw * (.22 + fx * .4), top + bh * (.18 + fy * .3)
                d.rectangle([wx, wy, wx + bw * .16, wy + bh * .16], fill=(72, 98, 128))
                d.rectangle([wx - 6, wy, wx - 1, wy + bh * .16], fill=(74, 120, 168))
                d.rectangle([wx + bw * .16 + 1, wy, wx + bw * .16 + 6, wy + bh * .16], fill=(74, 120, 168))
        aw_y = base - bh * .32
        for k in range(6):
            sx = x + bw * .1 + k * bw * .8 / 6
            d.polygon([(sx, aw_y), (sx + bw * .8 / 6, aw_y), (sx + bw * .8 / 6 + 4, aw_y + 26), (sx + 4, aw_y + 26)],
                      fill=(60, 110, 182) if k % 2 == 0 else (246, 244, 236))
        x += bw + rnd.uniform(0, 12)
    for _ in range(9):
        tx = rnd.uniform(0, w)
        tr = rnd.uniform(26, 50)
        d.rectangle([tx - 4, base - 70, tx + 4, base], fill=(96, 70, 48))
        d.ellipse([tx - tr, base - 70 - tr * 1.6, tx + tr, base - 70 + tr * .2], fill=rnd.choice([(84, 140, 70), (70, 124, 60), (98, 152, 76)]))
    d.rectangle([0, base, w, h], fill=(206, 186, 160))
    return im


def art_print(w, h, seed):
    """Framed print: warm abstract hills under a sun, or colour blocks (no letters)."""
    rnd = random.Random(seed)
    if seed % 2 == 0:
        im = vgrad(w, h, (246, 208, 150), (232, 140, 96))
        d = ImageDraw.Draw(im)
        d.ellipse([w * .55, h * .18, w * .85, h * .45], fill=(255, 238, 200))
        for k, col in enumerate([(176, 96, 70), (120, 80, 70), (64, 92, 96)]):
            y = h * (.5 + k * .15)
            pts = [(0, h)] + [(x, y + math.sin(x / w * 6 + k) * h * .05) for x in range(0, w + 1, 8)] + [(w, h)]
            d.polygon(pts, fill=col)
    elif seed % 5 == 4:
        # Botanical print: a leafy stem on cream (round 7)
        im = Image.new("RGB", (w, h), (240, 232, 214))
        d = ImageDraw.Draw(im)
        d.line([(w * .5, h * .92), (w * .48, h * .12)], fill=(70, 98, 60), width=max(3, w // 60))
        for k in range(7):
            y = h * (.2 + k * .1); s = 1 if k % 2 else -1
            cx, cy = w * .49 + s * w * .14, y
            d.ellipse([cx - w * .13, cy - h * .045, cx + w * .13, cy + h * .045], fill=[(86, 132, 70), (104, 150, 82), (70, 112, 62)][k % 3])
    elif seed % 5 == 1 and seed > 3:
        # Coffee cup line drawing on terracotta (no letters)
        im = vgrad(w, h, (214, 132, 92), (184, 100, 72))
        d = ImageDraw.Draw(im)
        lw = max(3, w // 50)
        d.rounded_rectangle([w * .25, h * .42, w * .7, h * .78], radius=int(w * .08), outline=(250, 236, 214), width=lw)
        d.arc([w * .62, h * .48, w * .84, h * .68], -90, 90, fill=(250, 236, 214), width=lw)
        for k in range(3):
            x = w * (.35 + k * .12)
            d.arc([x - w * .05, h * .18, x + w * .05, h * .38], 200, 340, fill=(250, 236, 214), width=lw)
        d.line([(w * .18, h * .82), (w * .82, h * .82)], fill=(250, 236, 214), width=lw)
    else:
        im = Image.new("RGB", (w, h), (238, 230, 214))
        d = ImageDraw.Draw(im)
        for _ in range(6):
            x0, y0 = rnd.uniform(0, w * .7), rnd.uniform(0, h * .7)
            d.rectangle([x0, y0, x0 + rnd.uniform(w * .2, w * .5), y0 + rnd.uniform(h * .15, h * .4)],
                        fill=rnd.choice([(46, 110, 118), (226, 132, 64), (238, 196, 92), (64, 82, 120), (190, 90, 80)]))
    return im


def common():
    return {"exit": floor_text(512, 192, "EXIT", WHITE, sub="BACK TO THE CITY MAP", arrow="down")}


def venue_signs(vid):
    s = common()
    if vid == "perp_pit":
        s.update({
            "pp_title": neon(1024, 136, "PERP PIT", GREEN),
            "pp_ticker": ticker(2032, 48, "PERPS ON BTC · OIL · GOLD · US10Y (LOL)  ·  LONGS LIQUIDATED  ·  SHORTS LIQUIDATED  ·  "
                                         "FUNDING FLIPPED  ·  SOMEONE WENT 40x ON A FROG  ·  MARK PRICE, NOT LAST PRICE  ·  "),
            "pp_leverage_idle": board(1024, 512, "LEVERAGE BOARD", ["PAIR", "SIDE", "LEV", "SIZE", "LIQ PRICE", "PNL"],
                                      footer="FICTIONAL MARKETS · GAME CASH ONLY · FEED STANDBY", accent=GREEN, tabs=["CROSS", "ISOLATED"]),
            "pp_liq_idle": board(1024, 512, "24H CASUALTIES", ["TRADER", "SIDE", "LEV", "WIPED"], rows=4, accent=RED,
                                 tabs=["1H", "4H", "12H", "24H"], subtitle="BIGGEST SINGLE WIPEOUT: $—",
                                 footer="REKT CARDS POST HERE · FEED STANDBY"),
            "pp_heatmap": heat(1024, 512, "LIQ HEATMAP", "WHERE THE STOPS LIVE · DECOR, NOT DATA"),
            "pp_funding": poster(512, 512, ["FUNDING / HR", "PAID EVERY HOUR", ("SO ARE YOU", RED)], accent=GOLD),
            "pp_levermeter": gauge(1024, 512, "LEVER-O-METER"),
            "pp_rekt_receipts": poster(512, 640, ["REKT RECEIPTS", "WALL OF FAME", ("100x LONG · GONE", RED), ("40x SHORT · GONE", RED),
                                                 ("\"JUST A SCALP\" · GONE", RED)], accent=GOLD),
            "pp_isolated": poster(512, 224, ["ISOLATED", "MARGIN LOUNGE"], accent=CYAN),
            "pp_cross": poster(512, 224, ["CROSS", "MARGIN LOUNGE"], accent=PINK),
            "pp_floor_liq": floor_text(1024, 256, "LIQUIDATION ZONE", RED, sub="WITNESSES STAND BEHIND THE LINE"),
            "pp_nfa": poster(512, 224, ["NFA", "NOTHING IN HERE IS FINANCIAL ADVICE"], accent=GOLD),
            "pp_terminal": screen(512, 192, "perp", GREEN, "DEGEN DEX · PERPS"),
            "pp_atm": poster(256, 384, ["TOP UP", "MARGIN", "GAME CASH ONLY"], accent=GREEN),
        })
    elif vid == "rekt_clinic":
        s.update({
            "rc_title": neon(1024, 160, "REKT CLINIC", RED, sub="WALK-INS WELCOME · NO PNL SCREENSHOTS", sub_color=WHITE),
            "rc_triage": poster(512, 256, ["TRIAGE", "HOW MANY X WERE YOU?"], accent=RED),
            "rc_chart": poster(512, 640, ["PATIENT CHART", "POSITION: 100x LONG", "ENTRY: THE TOP", "DIAGNOSIS: REKT",
                                          ("PRESCRIPTION: TOUCH GRASS", GREEN)], accent=RED, bg=(236, 240, 242), fg=(40, 46, 56)),
            "rc_eye": poster(384, 512, ["EYE TEST", "W A G M I", "N G M I", "H O D L", "R E K T"], accent=RED, bg=(240, 242, 244), fg=(30, 34, 40)),
            "rc_memorial": poster(768, 320, ["IN LOVING MEMORY", "OF MY PORTFOLIO", ("TOUCH GRASS GARDEN →", GREEN)], accent=GOLD, bg=(40, 44, 40)),
            "rc_wait": poster(768, 256, ["WAITING ROOM", "PLEASE STOP CHECKING THE CHART"], accent=TEAL, bg=(230, 238, 240), fg=(40, 50, 60)),
            "rc_vitals": screen(256, 192, "vitals", RED, "VITALS"),
            "rc_tomb1": tomb(256, 160, "RIP 100x"), "rc_tomb2": tomb(256, 160, "DEV SOLD"),
            "rc_tomb3": tomb(256, 160, "BOUGHT THE TOP"), "rc_tomb4": tomb(256, 160, "GONE TO ZERO"),
            "rc_tomb5": tomb(256, 160, "IT WAS A SCALP"), "rc_tomb6": tomb(256, 160, "TRUSTED A KOL"),
            "rc_floor_grass": floor_text(768, 192, "TOUCH GRASS", GREEN, arrow="right"),
        })
    elif vid == "ore_refinery":
        s.update({
            "ore_title": neon(1024, 150, "ORE REFINERY", ORE, sub="STAKE REAL ORE · EARN A SHARE OF PROTOCOL REVENUE", sub_color=WHITE),
            "ore_board_label": poster(1024, 160, ["LIVE ORE BOARD · 5 × 5"], accent=ORE, bg=(30, 22, 10)),
            "ore_timer_idle": board(512, 384, "ROUND", ["TIME LEFT"], rows=1, accent=ORE, footer="ROUND FEED · STANDBY"),
            "ore_mother_idle": board(512, 384, "MOTHERLODE", ["ORE"], rows=1, accent=GOLD, footer="TREASURY FEED · STANDBY"),
            "ore_stake_screen": screen(512, 256, "stake", ORE, "REFINERY TERMINAL"),
            "ore_tiers": poster(512, 640, ["MINER BADGES", ("PROSPECTOR · HOLDS ORE", WHITE), ("MINER · STAKES 1+ ORE", WHITE),
                                           ("REFINER · STAKES 10+ ORE", WHITE), ("HARD HAT WHILE STAKED", ORE)], accent=ORE, bg=(34, 28, 20)),
            "ore_mine_link": poster(768, 256, ["WANT TO MINE?", "MINING HAPPENS ON ore.com — NOT IN THIS APP"], accent=ORE, bg=(34, 28, 20)),
            "ore_furnace_label": poster(768, 192, ["FURNACE HEAT", "= CITY-WIDE STAKED ORE"], accent=ORE, bg=(40, 20, 10)),
            "ore_hardhat": badge(512, 192, "HARD HAT AREA", (255, 196, 61)),
        })
    elif vid == "launchpad_tower":
        s.update({
            "lt_title": neon(1024, 150, "LAUNCHPAD TOWER", PURPLE, sub="A NEW FICTIONAL COIN EVERY FEW MINUTES", sub_color=WHITE),
            "lt_board_idle": board(1024, 576, "PULSE-ISH", ["FRESH MINTS", "FINAL LAP", "GRADUATED"], rows=4, accent=PURPLE,
                                   tabs=["NEW", "TRENDING"], footer="LAUNCH FEED · STANDBY"),
            "lt_countdown_idle": board(512, 320, "NEXT LAUNCH", ["T-MINUS"], rows=1, accent=GREEN, footer="LAUNCH CLOCK · STANDBY"),
            "lt_bonding": poster(768, 512, ["BONDING CURVE", "BUY EARLY · CURVE GOES UP", "GRADUATES AT THE TOP", ("(MOST NEVER DO)", RED)], accent=GREEN),
            "lt_desk": poster(768, 192, ["LAUNCH DESK", "FAIR LAUNCH ONLY (ALLEGEDLY)"], accent=GOLD),
            "lt_rules": poster(512, 512, ["LAUNCH RULES", "NO INSIDERS", "NO SNIPERS", "NO BUNDLES", ("(TRUST ME BRO)", RED)], accent=PINK),
            "lt_terminal": screen(512, 256, "pulse", PURPLE, "PULSE-ISH TERMINAL"),
            "lt_window": window_view(1024, 512, "day"),
            "lt_feejar": badge(512, 160, "CREATOR FEE JAR", (60, 40, 100), GOLD),
            "lt_fountain": badge(512, 160, "HOLDER-REWARDS FOUNTAIN", (40, 60, 110), CYAN),
            "lt_stream": neon(768, 160, "STREAM BOOTH", RED, sub="LIVE · KEEP IT PG", sub_color=WHITE),
            "lt_floor_curve": floor_text(1024, 192, "BONDING CURVE →", GREEN),
        })
    elif vid == "prediction_pit":
        s.update({
            "pd_title": neon(1024, 150, "PREDICTION PIT", BLUE, sub="CALL IT · GAME CASH ONLY · NEVER WITHDRAWABLE", sub_color=WHITE),
            "pd_board_idle": board(1024, 576, "ODDSBOARD", ["MARKET", "% CHANCE", "YES ¢", "NO ¢", "$ VOL"], rows=5, accent=BLUE,
                                   tabs=["TRENDING", "BREAKING", "NEW"], footer="MARKET FEED · STANDBY · RESOLVES ON IN-GAME EVENTS"),
            "pd_tabs": poster(2032, 160, ["TRENDING · BREAKING · POLITICS · AI · CELEBS · MACRO · CITY · BEEFS"], accent=WHITE, bg=(18, 30, 60), border=False),
            "pd_yes": floor_text(768, 256, "YES", GREEN), "pd_no": floor_text(768, 256, "NO", RED),
            "pd_booth": screen(512, 256, "odds", BLUE, "ODDSBOARD BOOTH"),
            "pd_payout": poster(768, 256, ["1 SHARE = 1 CASH", "IF YOU CALLED IT RIGHT"], accent=GOLD),
            "pd_oracle": poster(512, 256, ["THE ORACLE", "RESOLVES EVENTUALLY"], accent=PURPLE),
            "pd_rules": poster(768, 256, ["JUST FOR FUN", "GAME CASH ONLY · NEVER WITHDRAWABLE · THE HOUSE HAS AN EDGE"], accent=GOLD),
        })
    elif vid == "ratio_arena":
        s.update({
            "ra_title": neon(1024, 150, "RATIO ARENA", PINK, sub="REPLIES > LIKES = RATIO", sub_color=WHITE),
            "ra_score_idle": board(1024, 512, "RATIO METER", ["THE OP", "THE REPLY"], rows=2, accent=PINK,
                                   subtitle="REPLIES — vs LIKES —", footer="RATIO STATUS · PENDING"),
            "ra_banner1": poster(1024, 256, ["L + RATIO", "+ YOU FELL OFF"], accent=PINK),
            "ra_banner2": poster(1024, 256, ["COMMUNITY NOTE ADDED", "READERS ADDED CONTEXT THEY THOUGHT YOU'D WANT"], accent=GOLD),
            "ra_red": badge(512, 160, "RED CORNER · THE OP", (190, 40, 50), WHITE),
            "ra_blue": badge(512, 160, "BLUE CORNER · THE REPLY GUY", (40, 80, 190), WHITE),
            "ra_floor": floor_text(1024, 256, "RATIO", PINK, sub="QUOTE TWEETS ENTER FROM THE SOUTH"),
        })
    elif vid == "conference_hall":
        s.update({
            "cc_title": neon(1024, 150, "COIN2050", CYAN, sub="CONFERENCE CENTER · FEELS LIKE A BOARD MEETING", sub_color=WHITE),
            "cc_stage_idle": board(1024, 512, "MAIN STAGE", ["NOW", "NEXT"], rows=2, accent=CYAN, subtitle="ON STAGE: —",
                                   footer="AGENDA FEED · STANDBY"),
            "cc_village": poster(768, 256, ["STARTUP HAMLET", "18 DECKS · 0 PRODUCTS"], accent=GREEN),
            "cc_ivdrip": poster(512, 256, ["IV DRIP BAR", "HYDRATE BEFORE SIDE EVENTS"], accent=TEAL),
            "cc_plunge": poster(512, 256, ["COLD PLUNGE", "FOR AFTER THE PANEL"], accent=CYAN),
            "cc_side": poster(768, 256, ["SIDE EVENTS: DOWN 40%", "YOU WERE STILL INVITED TO NONE"], accent=PINK),
            "cc_after": neon(768, 160, "AFTER2050 ROOFTOP", PINK, sub="DEEP HOUSE · LANYARDS OFF", sub_color=WHITE),
            "cc_next": poster(768, 256, ["NEXT STOP: CHECKPOINT LONDON", "STAFF STILL SELLING 25 TICKETS TONIGHT"], accent=CYAN),
            "cc_mascot": badge(512, 128, "BOOTH MASCOT · PHOTO OP", (31, 191, 184)),
            "cc_reg": poster(768, 256, ["REGISTRATION", "GET YOUR LANYARD"], accent=GOLD),
            "cc_wifi": poster(512, 256, ["WIFI: wagmi2026", "PASSWORD: ngmi"], accent=WHITE),
            "cc_timer": board(512, 320, "PITCH TIMER", ["2:00"], rows=1, accent=RED, footer="DEMO DAY · 2 MIN EACH"),
            "cc_booth": screen(512, 256, "chat", CYAN, "BOOTH DEMO"),
        })
    elif vid == "ct_jail":
        s.update({
            "cj_title": neon(1024, 150, "COMPLIANCE JAIL", ORANGE, sub="BAIL PAYABLE IN GAME CASH", sub_color=WHITE),
            "cj_kyc": poster(512, 640, ["KNOW YOUR DEGEN", "NAME: ____", "DOB: ____", "ADDRESS: ____", ("SOURCE OF WEALTH: \"TRUST ME\"", ORANGE)],
                             accent=ORANGE, bg=(236, 236, 230), fg=(40, 40, 46)),
            "cj_booking": poster(768, 256, ["COMPLIANCE DEPT", "BOOKING · STAND ON THE LINE"], accent=ORANGE),
            "cj_cell1": badge(512, 128, "CELL 1 · UNREGISTERED OFFERING", (60, 64, 70), WHITE),
            "cj_cell2": badge(512, 128, "CELL 2 · WASH TRADING", (60, 64, 70), WHITE),
            "cj_cell3": badge(512, 128, "CELL 3 · RUG PULL", (60, 64, 70), WHITE),
            "cj_cell4": badge(512, 128, "CELL 4 · FORGOT TO KYC", (60, 64, 70), WHITE),
            "cj_rules": poster(512, 512, ["HOUSE RULES", "NO UNREGISTERED OFFERINGS", "NO WASH TRADING",
                                          ("NO \"IT'S A UTILITY TOKEN\"", ORANGE)], accent=ORANGE),
            "cj_height": poster(512, 768, ["7 FT", "6 FT", "5 FT", "4 FT"], accent=WHITE, bg=(50, 54, 60), border=False, kinds=["ui"] * 4),
        })
    elif vid == "hackathon_house":
        s.update({
            "hh_title": neon(1024, 150, "HACKATHON HOUSE", GREEN, sub="BUILDOOOR STATION · OPEN 24/7", sub_color=WHITE),
            "hh_countdown_idle": board(1024, 448, "SUBMISSIONS CLOSE", ["DAYS", "HRS", "MIN"], rows=1, accent=GREEN,
                                       subtitle="DEADLINE EXTENDED (AGAIN)", footer="DEADLINE CLOCK · STANDBY"),
            "hh_whiteboard": poster(1024, 512, ["THE PLAN", "1. WALLET  →  2. PROGRAM  →  3. ???", ("4. SHIP IT", GREEN)], accent=(40, 60, 200),
                                    bg=(244, 245, 240), fg=(40, 44, 60)),
            "hh_rules": poster(512, 640, ["HOUSE RULES", "1. SHIP", "2. SLEEP LATER", "3. NO SHILLING IN #general", ("4. DEMO IS 2 MINUTES", GREEN)], accent=GREEN),
            "hh_laptop": screen(256, 160, "code", GREEN, "main.rs"),
            "hh_pizza": badge(256, 128, "PIZZA", (232, 163, 61), (60, 30, 10)),
            "hh_sponsor": poster(1024, 192, ["BUILDOOOR STATION", "DESKS · MONITORS · SNACKS · MENTORS"], accent=GREEN),
            "hh_demo": poster(512, 256, ["DEMO PODIUM", "2 MINUTES · NO SLIDES OVER 3"], accent=GOLD),
        })
    elif vid == "audit_row":
        s.update({
            "ar_title": neon(1024, 150, "AUDIT ROW", TEAL, sub="WE READ THE CODE SO YOU DON'T HAVE TO", sub_color=WHITE),
            "ar_alarm_idle": board(1024, 448, "EXPLOIT ALARM", ["PROTOCOL", "VECTOR", "AT RISK"], rows=2, accent=RED,
                                   subtitle="STATUS: ALL CLEAR (FEED STANDBY)", footer="ON-CHAIN ALERTS · STANDBY"),
            "ar_rektboard": board(768, 640, "REKT-O-BOARD", ["#", "INCIDENT", "AUDIT", "LOST"], rows=6, accent=RED, footer="HALL OF LOSSES · DECOR"),
            "ar_bounty": poster(768, 512, ["BUG BOUNTY BOARD", "MAX BOUNTY: UP TO $∞", "PoC REQUIRED · SAFE HARBOR", ("KYC: MAYBE", GOLD)], accent=GREEN),
            "ar_safu": poster(1024, 192, ["FUNDS ARE PROBABLY FINE"], accent=GOLD),
            "ar_stamp": badge(384, 256, "AUDITED (BY A GUY)", (31, 191, 184)),
            "ar_screen": screen(512, 256, "diff", TEAL, "REVIEW · lib.rs"),
            "ar_queue": poster(768, 224, ["AUDIT QUEUE: 6 WEEKS", "RUSH FEE: YES"], accent=TEAL),
            "ar_unaudited": badge(384, 160, "UNAUDITED", (190, 40, 50), WHITE),
            "ar_hermit": poster(512, 512, ["WANTED", "THE HERMIT CREW", "FOR DRAINING", ("ANOTHER EXCHANGE", RED)], accent=RED, bg=(222, 200, 160), fg=(60, 40, 20)),
        })
    elif vid == "discord_heights":
        s.update({
            "dh_title": neon(1024, 150, "DISCORDIA HEIGHTS", BLURPLE, sub="SERVER LOUNGE · 99+ UNREAD", sub_color=WHITE),
            "dh_announce_idle": board(1024, 448, "#announcements", ["FROM", "MESSAGE"], rows=3, accent=BLURPLE, footer="@everyone · FEED STANDBY"),
            "dh_general": badge(512, 128, "# general", (54, 57, 63), WHITE), "dh_alpha": badge(512, 128, "# alpha-maybe", (54, 57, 63), GOLD),
            "dh_gm": badge(512, 128, "# gm", (54, 57, 63), GREEN), "dh_wen": badge(512, 128, "# wen-token", (54, 57, 63), PINK),
            "dh_support": badge(512, 128, "# support-ticket", (54, 57, 63), CYAN), "dh_memes": badge(512, 128, "# memes", (54, 57, 63), ORANGE),
            "dh_verify": poster(512, 512, ["VERIFY TO ENTER", "CONNECT WALLET", "GET ROLE", ("PROVE YOU'RE HUMAN", GREEN)], accent=BLURPLE),
            "dh_mod": poster(512, 256, ["MOD DESK", "MODS ARE ASLEEP"], accent=GREEN),
            "dh_rules": poster(768, 256, ["RULE 1", "ADMINS NEVER DM FIRST. THAT'S A SCAMMER."], accent=RED),
            "dh_wenposter": poster(512, 512, ["WEN TOKEN?", "WEN MOON?", "WEN LAMBO?", ("SOON (TM)", BLURPLE)], accent=PINK),
            "dh_chat": screen(512, 256, "chat", BLURPLE, "#general"),
            "dh_hiring": poster(512, 512, ["WE'RE HIRING!", "CLICK THIS LINK", "CONNECT WALLET", ("(IT'S A DRAINER)", RED)], accent=GREEN),
        })
    elif vid == "vc_row":
        s.update({
            "vc_title": neon(1024, 150, "VC ROW", GOLD, sub="PITCHES BY WARM INTRO ONLY", sub_color=WHITE),
            "vc_early": neon(1024, 200, "WE ARE SO EARLY", PINK),
            "vc_deck_idle": board(1024, 576, "PITCH DECK", ["SLIDE", "TITLE"], rows=3, accent=GOLD, subtitle="NOW PRESENTING: —",
                                  footer="DECK FEED · STANDBY"),
            "vc_termsheet": poster(512, 640, ["TERM SHEET", "VALUATION: —", "TOKEN WARRANTS: YES", "LOCKUP: \"FLEXIBLE\"", ("BOARD SEAT: OBVIOUSLY", GOLD)],
                                   accent=GOLD, bg=(240, 238, 230), fg=(40, 40, 46)),
            "vc_thesis": poster(768, 256, ["OUR THESIS", "TOKENIZE EVERYTHING · THE SUITS ARE HERE"], accent=GOLD),
            "vc_portfolio": poster(768, 256, ["PORTFOLIO", "400 COINS · 3 STILL TRADING"], accent=WHITE),
            "vc_door": badge(512, 160, "PARTNERS ONLY", (180, 145, 63)),
            "vc_art": heat(512, 512, "ABSTRACT No. 69", "ON LOAN FROM A FOUNDER"),
        })
    elif vid == "airdrop_fields":
        s.update({
            "pf_title": neon(1024, 150, "POINTS FIELDS", GOLD, sub="FARM RESPONSIBLY", sub_color=WHITE),
            "pf_board_idle": board(1024, 512, "SEASON ∞ LEADERBOARD", ["RANK", "FARMER", "POINTS"], rows=5, accent=GOLD,
                                   tabs=["7D", "30D", "ALL"], footer="POINTS FEED · STANDBY · POINTS HAVE NO STATED VALUE"),
            "pf_farm": poster(768, 256, ["1 HUMAN = 1 WALLET", "(SUPPOSEDLY)"], accent=GREEN, bg=(60, 40, 20)),
            "pf_claim": poster(512, 256, ["CLAIM WINDOW", ("CLOSED", RED)], accent=GOLD, bg=(60, 40, 20)),
            "pf_season1": badge(384, 128, "SEASON 1", (120, 80, 40), WHITE), "pf_season2": badge(384, 128, "SEASON 2", (120, 80, 40), WHITE),
            "pf_season3": badge(384, 128, "SEASON ∞", (120, 80, 40), GOLD),
            "pf_mindshare": poster(512, 256, ["YAPPER ARENA →", "YAP TO EARN (NOT REALLY)"], accent=PINK, bg=(60, 40, 20)),
            "pf_scarecrow": badge(384, 128, "SYBIL SCARECROW", (200, 160, 80), (40, 30, 10)),
        })
    elif vid == "sybil_barn":
        s.update({
            "sb_title": neon(1024, 150, "SYBIL BARN", ORANGE, sub="CLONES RAISED HERE", sub_color=WHITE),
            "sb_pen1": badge(512, 128, "WALLETS 0001 – 0420", (90, 40, 30), WHITE),
            "sb_pen2": badge(512, 128, "WALLETS 0421 – 0840", (90, 40, 30), WHITE),
            "sb_pen3": badge(512, 128, "WALLETS 0841 – ∞", (90, 40, 30), GOLD),
            "sb_wanted": poster(512, 640, ["WANTED", "SYBIL SQUAD", "FOR FARMING THE SAME", "AIRDROP 1,260 TIMES", ("REWARD: A TWEET", GOLD)],
                                accent=RED, bg=(222, 200, 160), fg=(60, 40, 20)),
            "sb_rule": poster(768, 256, ["1 HUMAN = 1 WALLET", "WE HAVE 1,260 HUMANS"], accent=GOLD, bg=(70, 40, 25)),
            "sb_phonefarm": poster(768, 192, ["PHONE FARM", "DO NOT UNPLUG"], accent=RED, bg=(40, 30, 25)),
            "sb_sheet": screen(512, 256, "sheet", GREEN, "wallets_final_v7.csv"),
        })
    elif vid == "checker_kiosk":
        s.update({
            "ck_title": neon(1024, 150, "CHECKER KIOSK", GREEN, sub="ARE YOU ELIGIBLE?", sub_color=WHITE),
            "ck_main_idle": board(1024, 448, "ELIGIBILITY", ["WALLET", "TIER", "ALLOCATION"], rows=1, accent=GREEN,
                                  subtitle="CONNECT WALLET TO CHECK", footer="CHECKER FEED · STANDBY"),
            "ck_kiosk": screen(512, 384, "checker", GREEN, "ELIGIBILITY CHECKER"),
            "ck_noteligible": poster(768, 384, ["YOU ARE NOT ELIGIBLE", ("(LOL)", RED), "TRY FARMING HARDER NEXT SEASON"], accent=RED),
            "ck_cope": badge(512, 160, "COPE BENCH", (90, 70, 120), WHITE),
            "ck_queue": poster(768, 192, ["QUEUE HERE", "EST. WAIT: FOREVER"], accent=GOLD),
            "ck_tiers": poster(512, 640, ["TIERS", "WHALE", "DOLPHIN", "SHRIMP", ("SYBIL (DISQUALIFIED)", RED)], accent=CYAN),
        })
    elif vid == "cabal_lounge":
        s.update({
            "cl_title": neon(1024, 150, "CABAL LOUNGE", GOLD, sub="MEMBERS ONLY · YOU ARE NOT A MEMBER", sub_color=WHITE),
            "cl_chat_idle": board(1024, 512, "CABAL CHAT", ["MEMBER", "MESSAGE"], rows=4, accent=GOLD, subtitle="7 MEMBERS · 0 LEAKS",
                                  footer="ENCRYPTED · FEED STANDBY"),
            "cl_guest": poster(768, 256, ["GUEST LIST ONLY", "DRESS CODE: MATCHING PFPs · NO SCREENSHOTS"], accent=GOLD, bg=(40, 10, 16)),
            "cl_bundle": neon(1024, 160, "100% NOT BUNDLED", PINK),
            "cl_menu": poster(512, 640, ["MENU", "INSIDER ROUND", "BUNDLED SHOT", "EXIT LIQUIDITY SPRITZ", ("PAY IN GAME CASH", GOLD)],
                              accent=GOLD, bg=(30, 10, 14)),
            "cl_art": heat(512, 640, "THE ALLOCATION", "OIL ON TERM SHEET"),
        })
    elif vid == "yacht_club":
        s.update({
            "yc_title": neon(1024, 150, "YACHT CLUB", CYAN, sub="DUBAI MARINA · SIDE EVENT TONIGHT", sub_color=WHITE),
            "yc_hull1": badge(1024, 128, "NUMBER GO UP", (245, 247, 252), (20, 40, 80)),
            "yc_hull2": badge(1024, 128, "EXIT LIQUIDITY", (245, 247, 252), (120, 20, 30)),
            "yc_bar": poster(768, 256, ["BOTTLE SERVICE", "PAY IN GAME CASH"], accent=GOLD, bg=(20, 40, 60)),
            "yc_dress": poster(512, 256, ["DRESS CODE", "LINEN · NO LANYARDS"], accent=WHITE, bg=(20, 40, 60)),
            "yc_side": poster(768, 256, ["YACHT PARTY TONIGHT", "INVITE ONLY · ASK A CABAL MEMBER"], accent=PINK, bg=(20, 40, 60)),
            "yc_govfee": poster(512, 384, ["GOV FEE KIOSK", "PAY YOUR VISA IN CRYPTO", ("(NOT IN GAME CASH)", GOLD)], accent=CYAN, bg=(20, 40, 60)),
            "yc_board_idle": board(1024, 448, "MARINA DEPARTURES", ["YACHT", "OWNER", "STATUS"], rows=3, accent=CYAN,
                                   footer="HARBOUR FEED · STANDBY"),
        })
    elif vid == "penthouse_row":
        s.update({
            "pr_title": neon(1024, 150, "PENTHOUSE ROW", GOLD, sub="ROOFTOP · DUBAI MARINA", sub_color=WHITE),
            "pr_ph1": badge(256, 128, "PH-1", (180, 145, 63)), "pr_ph2": badge(256, 128, "PH-2", (180, 145, 63)),
            "pr_ph3": badge(256, 128, "PH-3", (180, 145, 63)),
            "pr_rent": poster(768, 256, ["PENTHOUSE", "RENT: $1,500 / WEEK (GAME CASH)"], accent=GOLD, bg=(30, 30, 36)),
            "pr_concierge": poster(768, 256, ["CONCIERGE", "VALET · NO LANYARDS PAST THIS POINT"], accent=GOLD, bg=(30, 30, 36)),
            "pr_pool": poster(768, 256, ["INFINITY POOL", "INFINITY LEVERAGE NOT PERMITTED"], accent=CYAN, bg=(30, 30, 36)),
            "pr_window": window_view(1024, 512, "night"),
        })
    elif vid == "spaces_studio":
        s.update({
            "ss_waveform_idle": waveform(1024, 448),
            "ss_lanyards": lanyards(1024, 384),
            "ss_sky": sky_evening(1024, 512),
            "ss_greenroom_tv": stage_feed(512, 288),
            "ss_poster_1": gig_poster(256, 384, 1), "ss_poster_2": gig_poster(256, 384, 2), "ss_poster_3": gig_poster(256, 384, 3),
            "ss_rug_rect": rug(768, 384, 5, (178, 96, 72), (236, 214, 176)),
            "ss_rug_green": rug(512, 384, 6, (120, 132, 110), (226, 214, 186)),
            "ss_rug_grey": rug(768, 384, 7, (112, 102, 98), (196, 180, 160)),
            "ss_lanyard_frame": lanyard_frame(512, 384),
        })
        for i, (kind, colors) in enumerate(FLAG_SETS):
            s["ss_flag_%d" % i] = flag(kind, colors)
    elif vid == "timeline_square":
        s.update({
            "tl_cobble_a": cobbles(512, 61, [(208, 192, 164), (196, 178, 150), (214, 200, 174), (186, 168, 142), (204, 188, 162), (194, 182, 164)], (132, 116, 96)),
            "tl_stucco_cream": stucco(256, 71, (236, 218, 184)), "tl_stucco_ochre": stucco(256, 72, (226, 182, 112)),
            "tl_stucco_peach": stucco(256, 73, (232, 176, 140)), "tl_stucco_blue": stucco(256, 74, (150, 168, 210)),
            "tl_stucco_stone": stucco(256, 75, (206, 192, 166)), "tl_stucco_sage": stucco(256, 76, (178, 192, 160)),
            "tl_roof": roof_tiles(256, 256, 81),
            "tl_sky": solana_sky(1024, 512),
            "tl_timeline_idle": feed_idle(1024, 576),
            "tl_pixel_idle": pixel_canvas(640, 448),
            "tl_bb_1_idle": billboard_idle(768, 384, 0), "tl_bb_2_idle": billboard_idle(768, 384, 1),
            "tl_burst": burst_poster(320, 400),
            "tl_clock": clock_face(256),
            "tl_win_bakery": shop_window(512, 256, "bakery", 91), "tl_win_toys": shop_window(512, 256, "toys", 92),
            "tl_win_cafe": shop_window(512, 256, "cafe", 93), "tl_win_parcels": shop_window(512, 256, "parcels", 94),
            "tl_pane_lit": window_pane(96, 144, True, 95), "tl_pane_dark": window_pane(96, 144, False, 96),
            "tl_poster_1": gig_poster(192, 288, 4), "tl_poster_2": gig_poster(192, 288, 5),
        })
        for i, (kind, colors) in enumerate(FLAG_SETS):
            s["tl_flag_%d" % i] = flag(kind, colors)
    elif vid == "terminal_cafe":
        s.update({
            "cf_chalk_menu": chalk_menu(1024, 512), "cf_chalk_sun": chalk_sun(384, 512),
            "cf_candles_1": candles(512, 320, 1), "cf_candles_2": candles(512, 320, 2), "cf_candles_3": candles(512, 320, 3),
            "cf_candles_4": candles(512, 320, 4), "cf_brick": brick_wall(512, 512), "cf_tiles": green_tiles(256, 256),
            "cf_town": town_view(1024, 512), "cf_art_1": art_print(256, 320, 2), "cf_art_2": art_print(256, 320, 3),
            "cf_art_3": art_print(256, 320, 9), "cf_art_4": art_print(256, 320, 11),
            "cf_art_6": art_print(256, 320, 7),
            "cf_pastry": shop_window(512, 256, "bakery", 321),
            "cf_rug_1": rug(768, 384, 31, (196, 176, 150), (150, 110, 84)), "cf_rug_2": rug(512, 512, 32, (206, 190, 166), (146, 112, 86), round_=True),
            "cf_rug_3": rug(640, 384, 33, (176, 120, 96), (232, 214, 186)),
        })
    elif vid.startswith("home_"):
        s.update({"hm_tv": screen(512, 288, "pulse", PURPLE, "CHART TV"), "hm_monitor": screen(512, 192, "code", GREEN, "~/side-project")})
        if vid == "home_hacker_house":
            s.update({"hm_gm": neon(512, 160, "GM", GOLD), "hm_wagmi": neon(768, 160, "WAGMI", GREEN),
                      "hm_shipit": poster(512, 384, ["SHIP IT", "SLEEP IS A BEAR MARKET"], accent=GREEN),
                      "hm_rent_hacker": poster(512, 192, ["HACKER HOUSE", "RENT $60 / WEEK"], accent=GREEN)})
        elif vid == "home_studio":
            s.update({"hm_window_day": window_view(1024, 512, "day"),
                      "hm_rent_studio": poster(512, 192, ["STUDIO", "RENT $180 / WEEK"], accent=TEAL)})
        elif vid == "home_penthouse":
            s.update({"hm_window_night": window_view(1024, 512, "night"),
                      "hm_bought_top": poster(512, 384, ["I BOUGHT THE TOP", "AND ALL I GOT WAS THIS PENTHOUSE"], accent=GOLD, bg=(30, 30, 36)),
                      "hm_rent_penthouse": poster(512, 192, ["PENTHOUSE", "RENT $1,500 / WEEK"], accent=GOLD)})
        elif vid == "home_marina_villa":
            s.update({"hm_window_marina": window_view(1024, 512, "marina"),
                      "hm_rent_villa": poster(512, 192, ["MARINA VILLA", "RENT $5,000 / WEEK"], accent=CYAN)})
    return s


# ----------------------------------------------------------------------------- atlas packing

def pack(images, width=2048, pad=4):
    """Skyline bottom-left packing (largest first)."""
    items = sorted(images.items(), key=lambda kv: (-(kv[1].height * kv[1].width), kv[0]))
    skyline = [[0, 0, width]]  # segments: x, y, w
    places = {}
    for name, im in items:
        rw, rh = im.width + 2 * pad, im.height + 2 * pad
        if rw > width:
            raise ValueError(f"{name} wider than atlas")
        best = None
        for i in range(len(skyline)):
            x = skyline[i][0]
            if x + rw > width:
                break
            # Height needed across the segments this rect spans.
            y, span, j = 0, 0, i
            while span < rw:
                y = max(y, skyline[j][1])
                span += skyline[j][2]
                j += 1
            if best is None or y + rh < best[1] + best[3] or (y + rh == best[1] + best[3] and x < best[0]):
                best = (x, y, rw, rh)
        x, y, rw, rh = best
        places[name] = (x + pad, y + pad)
        # Update skyline: insert new segment, trim covered ones.
        new = [x, y + rh, rw]
        out = []
        for sx, sy, sw in skyline:
            if sx + sw <= x or sx >= x + rw:
                out.append([sx, sy, sw])
            else:
                if sx < x:
                    out.append([sx, sy, x - sx])
                if sx + sw > x + rw:
                    out.append([x + rw, sy, sx + sw - (x + rw)])
        out.append(new)
        out.sort()
        merged = []
        for seg in out:
            if merged and merged[-1][1] == seg[1] and merged[-1][0] + merged[-1][2] == seg[0]:
                merged[-1][2] += seg[2]
            else:
                merged.append(seg)
        skyline = merged
    height = max(py + images[n].height + pad for n, (px, py) in places.items())
    h = max(256, -(-height // 128) * 128)  # multiple of 128 (ASTC blocks, NPOT is fine on GLES3/Vulkan)
    if h > 2048:
        raise ValueError(f"atlas overflow: {height}px")
    atlas = Image.new("RGB", (width, h), (12, 14, 18))
    regions = {}
    for name, im in images.items():
        px, py = places[name]
        # Edge extrude into the padding against mip bleeding.
        ext = im.resize((im.width + 2 * pad, im.height + 2 * pad), Image.NEAREST)
        atlas.paste(ext, (px - pad, py - pad))
        atlas.paste(im, (px, py))
        regions[name] = [px, py, im.width, im.height]
    return atlas, regions


def build(vid):
    images = venue_signs(vid)
    scale = 1.0
    while True:
        try:
            # Live-board idle art (hooks) keeps full resolution; static decor shrinks until the atlas fits 2048².
            sized = {k: (im if k.endswith("_idle") or scale == 1.0 else
                         im.resize((max(16, int(im.width * scale)), max(16, int(im.height * scale))), Image.LANCZOS))
                     for k, im in images.items()}
            atlas, regions = pack(sized)
            break
        except ValueError as e:
            if "overflow" not in str(e) or scale < 0.5:
                raise
            scale -= 0.08
    folder = os.path.join(OUT, vid, "Textures")
    os.makedirs(folder, exist_ok=True)
    png = f"{vid}_signs.png"
    atlas.save(os.path.join(folder, png), optimize=True)
    with open(os.path.join(folder, f"{vid}_signs.json"), "w") as f:
        json.dump({"image": png, "width": atlas.width, "height": atlas.height, "regions": regions}, f, indent=1, sort_keys=True)
        f.write("\n")
    print(f"{vid}: {len(images)} signs, atlas {atlas.width}x{atlas.height}, decor scale {scale:.2f}")


VENUES = ["terminal_cafe", "timeline_square", "spaces_studio", "perp_pit", "rekt_clinic", "ore_refinery", "launchpad_tower", "prediction_pit", "ratio_arena", "conference_hall",
          "ct_jail", "hackathon_house", "audit_row", "discord_heights", "vc_row", "airdrop_fields", "sybil_barn", "checker_kiosk",
          "cabal_lounge", "yacht_club", "penthouse_row", "home_hacker_house", "home_studio", "home_penthouse", "home_marina_villa"]

if __name__ == "__main__":
    for v in (sys.argv[1:] or VENUES):
        build(v)
