#!/usr/bin/env python3
"""Generate the parody screen / billboard / neon textures for the CT Life lookdev scenes.

All text and graphics are original. Fonts: SIL OFL 1.1 (google/fonts), downloaded by
fetch_assets.py into src/fonts/. Output: art/lookdev/tex/*.png

Run: art/lookdev/.venv/bin/python art/lookdev/make_textures.py
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.join(HERE, "src", "fonts")
OUT = os.path.join(HERE, "tex")
os.makedirs(OUT, exist_ok=True)
random.seed(7)

GREEN = (38, 222, 129)
RED = (255, 71, 87)
GOLD = (255, 196, 61)
WHITE = (245, 247, 252)
GREY = (139, 150, 176)


def font(kind, size, weight=None):
    path = {
        "inter": "inter_Inter[opsz,wght].ttf",
        "grotesk": "spacegrotesk_SpaceGrotesk[wght].ttf",
        "mono": "jetbrainsmono_JetBrainsMono[wght].ttf",
        "bebas": "bebasneue_BebasNeue-Regular.ttf",
    }[kind]
    f = ImageFont.truetype(os.path.join(FONTS, path), size)
    if weight is not None and kind != "bebas":
        axes = f.get_variation_axes()
        vals = []
        for a in axes:
            n = a["name"].decode() if isinstance(a["name"], bytes) else a["name"]
            vals.append(weight if n == "Weight" else min(a["maximum"], max(a["minimum"], 32)))
        f.set_variation_by_axes(vals)
    return f


def vgrad(w, h, top, bottom):
    im = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(im)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return im


def text_w(d, s, f):
    b = d.textbbox((0, 0), s, font=f)
    return b[2] - b[0]


def tri(d, x, y, s, up, fill):
    if up:
        d.polygon([(x, y + s), (x + s, y + s), (x + s / 2, y)], fill=fill)
    else:
        d.polygon([(x, y), (x + s, y), (x + s / 2, y + s)], fill=fill)


def candles(d, x0, y0, w, h, n, trend=1.0, seed=1, wick=True, vol=0.06):
    rnd = random.Random(seed)
    p = 1.0
    data = []
    for i in range(n):
        o = p
        p = max(0.05, p * (1 + rnd.gauss(0.012 * trend, vol)))
        hi = max(o, p) * (1 + abs(rnd.gauss(0, vol / 2)))
        lo = min(o, p) * (1 - abs(rnd.gauss(0, vol / 2)))
        data.append((o, hi, lo, p))
    mn = min(c[2] for c in data)
    mx = max(c[1] for c in data)
    cw = w / n
    def Y(v):
        return y0 + h - (v - mn) / (mx - mn) * h
    for i, (o, hi, lo, c) in enumerate(data):
        col = GREEN if c >= o else RED
        cx = x0 + i * cw + cw / 2
        if wick:
            d.line([(cx, Y(hi)), (cx, Y(lo))], fill=col, width=max(1, int(cw * 0.12)))
        top, bot = sorted([Y(o), Y(c)])
        d.rectangle([cx - cw * 0.34, top, cx + cw * 0.34, max(bot, top + 2)], fill=col)
    return data


def rounded(d, box, r, fill, outline=None, width=1):
    d.rounded_rectangle(box, r, fill=fill, outline=outline, width=width)


# ------------------------------------------------------------------------------------------
def timeline_screen():
    W, H = 2048, 1152
    im = vgrad(W, H, (12, 17, 34), (18, 12, 40))
    d = ImageDraw.Draw(im)
    # header
    d.rectangle([0, 0, W, 120], fill=(9, 12, 24))
    d.ellipse([36, 22, 112, 98], fill=(29, 155, 240))
    d.text((74, 60), "CT", font=font("inter", 40, 900), fill=WHITE, anchor="mm")
    d.text((136, 60), "TIMELINE", font=font("bebas", 96), fill=WHITE, anchor="lm")
    rounded(d, [520, 34, 660, 88], 26, RED)
    d.text((590, 61), "LIVE", font=font("inter", 34, 800), fill=WHITE, anchor="mm")
    d.text((W - 40, 60), "CT CITY  ·  16:20", font=font("inter", 40, 600), fill=GREY, anchor="rm")
    posts = [
        ("Town Crier", "@just_in_bot", "JUST IN: $4.2M in longs liquidated in the past 60 minutes", (255, 140, 0), "TC", "8.1K", "21K"),
        ("froge maxi", "@ribbit_ser", "$FROGE graduating in 3... 2... wagmi", (38, 200, 110), "FM", "2.4K", "9.6K"),
        ("Thread Boy", "@one_slash", "1/ Here's why the Ore Refinery changes everything (thread)", (130, 90, 255), "TB", "640", "3.3K"),
    ]
    y = 150
    for name, handle, text, col, ini, rts, likes in posts:
        rounded(d, [36, y, 1280, y + 300], 28, (22, 29, 52))
        d.ellipse([66, y + 34, 166, y + 134], fill=col)
        d.text((116, y + 84), ini, font=font("inter", 40, 800), fill=WHITE, anchor="mm")
        d.text((196, y + 40), name, font=font("inter", 46, 800), fill=WHITE)
        d.text((196 + text_w(d, name, font("inter", 46, 800)) + 18, y + 46), handle + " · 2m", font=font("inter", 38, 500), fill=GREY)
        # wrap
        f = font("inter", 50, 600)
        words, line, ly = text.split(), "", y + 108
        for wd in words:
            t = (line + " " + wd).strip()
            if text_w(d, t, f) > 1040:
                d.text((196, ly), line, font=f, fill=(226, 232, 245)); ly += 62; line = wd
            else:
                line = t
        d.text((196, ly), line, font=f, fill=(226, 232, 245))
        # engagement row
        ey = y + 250
        fs = font("inter", 32, 600)
        d.ellipse([196, ey - 14, 222, ey + 12], outline=GREY, width=4)
        d.text((236, ey), "312", font=fs, fill=GREY, anchor="lm")
        d.line([(360, ey - 10), (392, ey - 10), (392, ey + 10)], fill=GREEN, width=4)
        d.text((410, ey), rts, font=fs, fill=GREEN, anchor="lm")
        d.text((560, ey), "<3", font=font("inter", 34, 900), fill=RED, anchor="lm")
        d.text((610, ey), likes, font=fs, fill=RED, anchor="lm")
        y += 320
    # right column: trending
    x0 = 1320
    rounded(d, [x0, 150, W - 36, 1070], 28, (22, 29, 52))
    d.text((x0 + 36, 186), "TRENDING IN CT CITY", font=font("inter", 40, 800), fill=GOLD)
    trends = [("$FROGE", "+420%", True), ("$HOPIUM", "+69%", True), ("$COPE", "-88%", False), ("#RugWeek", "1.2M posts", None), ("gm", "4.0M posts", None)]
    ty = 262
    for i, (t, v, up) in enumerate(trends):
        d.text((x0 + 36, ty), f"{i + 1}", font=font("inter", 44, 700), fill=GREY)
        d.text((x0 + 90, ty), t, font=font("inter", 54, 800), fill=WHITE)
        col = GREEN if up else RED if up is False else GREY
        if up is not None:
            tri(d, W - 300, ty + 16, 30, up, col)
        d.text((W - 70, ty + 30), v, font=font("inter", 46, 800), fill=col, anchor="rm")
        ty += 98
    # mini chart
    cy = 770
    d.text((x0 + 36, cy), "$FROGE  0.00420", font=font("mono", 40, 700), fill=WHITE)
    candles(d, x0 + 40, cy + 70, W - x0 - 120, 210, 28, trend=1.6, seed=3)
    # bottom ticker
    d.rectangle([0, 1092, W, H], fill=GOLD)
    d.text((24, 1122), "JUST IN:  $4.2M LONGS LIQUIDATED   ·   $FROGE +420%   ·   $COPE -88%   ·   RUG WEEK STARTS MONDAY   ·   gm",
           font=font("inter", 40, 900), fill=(20, 16, 10), anchor="lm")
    im.save(os.path.join(OUT, "timeline_screen.png"))


def billboard_froge():
    W, H = 1536, 768
    im = vgrad(W, H, (10, 60, 34), (4, 24, 16))
    d = ImageDraw.Draw(im)
    # rising chart backdrop
    candles(d, 60, 260, W - 120, 440, 34, trend=2.2, seed=11, vol=0.05)
    over = Image.new("RGBA", (W, H), (4, 24, 16, 150))
    im = Image.alpha_composite(im.convert("RGBA"), over).convert("RGB")
    d = ImageDraw.Draw(im)
    # frog mascot
    cx, cy = 300, 420
    d.ellipse([cx - 210, cy - 170, cx + 210, cy + 200], fill=(72, 196, 92))
    for ex in (-110, 110):
        d.ellipse([cx + ex - 85, cy - 260, cx + ex + 85, cy - 90], fill=(72, 196, 92))
        d.ellipse([cx + ex - 60, cy - 235, cx + ex + 60, cy - 115], fill=WHITE)
        d.ellipse([cx + ex - 26, cy - 200, cx + ex + 26, cy - 148], fill=(15, 20, 15))
    d.arc([cx - 130, cy - 40, cx + 130, cy + 120], 20, 160, fill=(20, 70, 30), width=16)
    d.text((620, 120), "$FROGE", font=font("bebas", 300), fill=WHITE)
    tri(d, 640, 470, 120, True, GREEN)
    d.text((790, 420), "+420%", font=font("bebas", 260), fill=GREEN)
    d.text((W - 50, H - 50), "the only coin that ribbits", font=font("inter", 44, 700), fill=(200, 255, 210), anchor="rs")
    im.save(os.path.join(OUT, "bb_froge.png"))


def billboard_rugweek():
    W, H = 1536, 768
    im = Image.new("RGB", (W, H), (176, 28, 40))
    d = ImageDraw.Draw(im)
    # persian-rug border
    for i in range(0, W, 48):
        d.polygon([(i, 0), (i + 24, 34), (i + 48, 0)], fill=GOLD)
        d.polygon([(i, H), (i + 24, H - 34), (i + 48, H)], fill=GOLD)
    d.rectangle([40, 60, W - 40, H - 60], outline=GOLD, width=10)
    d.rectangle([64, 84, W - 64, H - 84], outline=(255, 228, 160), width=4)
    for k in range(5):
        x = 200 + k * 290
        d.polygon([(x, 384 - 70), (x + 70, 384), (x, 384 + 70), (x - 70, 384)], outline=(120, 14, 24), width=8)
    d.text((W // 2, 300), "RUG WEEK", font=font("bebas", 330), fill=WHITE, anchor="mm")
    rounded(d, [330, 510, W - 330, 640], 30, GOLD)
    d.text((W // 2, 576), "ALL CARPETS 100% OFF", font=font("inter", 64, 900), fill=(120, 14, 24), anchor="mm")
    d.text((W // 2, 705), "liquidity not included", font=font("inter", 36, 600), fill=(255, 220, 200), anchor="mm")
    im.save(os.path.join(OUT, "bb_rugweek.png"))


def billboard_gm():
    W, H = 1024, 1024
    im = vgrad(W, H, (255, 158, 84), (255, 92, 150))
    d = ImageDraw.Draw(im)
    d.ellipse([W // 2 - 300, 720, W // 2 + 300, 1320], fill=(255, 226, 120))
    for k in range(9):
        a = math.radians(200 + k * 17.5)
        d.line([(W // 2 + math.cos(a) * 340, 1020 + math.sin(a) * 340), (W // 2 + math.cos(a) * 440, 1020 + math.sin(a) * 440)],
               fill=(255, 236, 160), width=16)
    d.text((W // 2, 330), "gm", font=font("inter", 420, 900), fill=WHITE, anchor="mm")
    d.text((W // 2, 560), "good morning, CT City", font=font("inter", 54, 700), fill=(120, 30, 70), anchor="mm")
    im.save(os.path.join(OUT, "bb_gm.png"))


def billboard_wagmi():
    W, H = 1536, 768
    im = vgrad(W, H, (247, 239, 222), (236, 222, 196))
    d = ImageDraw.Draw(im)
    # skyline logo
    base = 560
    for i, (x, h) in enumerate([(110, 220), (190, 330), (270, 260), (350, 400), (430, 300)]):
        d.rectangle([x, base - h, x + 66, base], fill=(26, 46, 92))
        for wy in range(base - h + 24, base - 20, 40):
            d.rectangle([x + 14, wy, x + 26, wy + 16], fill=GOLD)
            d.rectangle([x + 40, wy, x + 52, wy + 16], fill=GOLD)
    d.rectangle([90, base, 520, base + 12], fill=(26, 46, 92))
    d.text((580, 150), "WAGMI", font=font("bebas", 250), fill=(26, 46, 92))
    d.text((585, 380), "REALTY", font=font("bebas", 170), fill=(196, 144, 40))
    d.text((590, 580), "Penthouses at the top. Literally.", font=font("inter", 52, 700), fill=(26, 46, 92))
    d.text((590, 650), "Floor price ser? Ask about our rooftop units.", font=font("inter", 36, 500), fill=(90, 100, 130))
    im.save(os.path.join(OUT, "bb_wagmi_realty.png"))


def billboard_ore():
    W, H = 1536, 512
    im = vgrad(W, H, (40, 34, 36), (22, 18, 20))
    d = ImageDraw.Draw(im)
    for i in range(-H, W, 70):  # hazard stripes
        d.polygon([(i, H), (i + 35, H), (i + 35 + 50, H - 50), (i + 50, H - 50)], fill=GOLD)
    d.rectangle([0, H - 50, W, H - 46], fill=(22, 18, 20))
    # ore nugget icon
    d.polygon([(130, 120), (250, 80), (330, 170), (290, 300), (150, 320), (90, 220)], fill=(255, 140, 40))
    d.polygon([(150, 140), (240, 110), (290, 180), (200, 200)], fill=(255, 200, 110))
    d.text((380, 80), "ORE REFINERY", font=font("bebas", 200), fill=(255, 150, 50))
    d.text((395, 300), "STAKE  ·  SMELT  ·  REPEAT", font=font("inter", 56, 800), fill=WHITE)
    # arrow
    ax, ay = W - 200, 200
    d.rectangle([ax - 110, ay - 40, ax + 30, ay + 40], fill=WHITE)
    d.polygon([(ax + 30, ay - 100), (ax + 170, ay), (ax + 30, ay + 100)], fill=WHITE)
    d.text((W - 60, 380), "2 BLOCKS", font=font("inter", 40, 800), fill=GOLD, anchor="ra")
    im.save(os.path.join(OUT, "bb_ore_refinery.png"))


def terminal_chart(name, trend, seed, ticker, price, change, accent=(29, 155, 240), W=1280, H=800):
    im = Image.new("RGB", (W, H), (10, 13, 22))
    d = ImageDraw.Draw(im)
    for gx in range(0, W, 80):
        d.line([(gx, 90), (gx, H - 120)], fill=(22, 28, 44), width=2)
    for gy in range(90, H - 120, 70):
        d.line([(0, gy), (W, gy)], fill=(22, 28, 44), width=2)
    d.rectangle([0, 0, W, 80], fill=(16, 20, 34))
    d.text((30, 40), ticker, font=font("mono", 50, 800), fill=WHITE, anchor="lm")
    up = not change.startswith("-")
    d.text((W - 30, 40), f"{price}  {change}", font=font("mono", 46, 800), fill=GREEN if up else RED, anchor="rm")
    candles(d, 30, 120, W - 60, H - 270, 36, trend=trend, seed=seed)
    # volume bars
    rnd = random.Random(seed + 5)
    for i in range(36):
        h = rnd.randint(10, 70)
        x = 30 + i * (W - 60) / 36
        d.rectangle([x + 4, H - 110 - h, x + (W - 60) / 36 - 4, H - 110], fill=(40, 60, 90))
    rounded(d, [30, H - 95, W // 2 - 15, H - 20], 18, GREEN)
    rounded(d, [W // 2 + 15, H - 95, W - 30, H - 20], 18, RED)
    d.text((W // 4, H - 57), "BUY", font=font("inter", 46, 900), fill=(6, 30, 16), anchor="mm")
    d.text((3 * W // 4, H - 57), "SELL", font=font("inter", 46, 900), fill=(40, 6, 10), anchor="mm")
    im.save(os.path.join(OUT, name))


def orderbook(name, W=1280, H=800):
    im = Image.new("RGB", (W, H), (10, 13, 22))
    d = ImageDraw.Draw(im)
    d.text((30, 30), "PULSE  ·  FINAL STRETCH", font=font("mono", 46, 800), fill=GOLD)
    rnd = random.Random(9)
    rows = ["$FROGE", "$HOPIUM", "$GMGM", "$COPE", "$RUGZ", "$MOONR", "$WAGMI", "$NGMI", "$SEND"]
    y = 120
    for r in rows:
        ch = rnd.choice([1, 1, -1]) * rnd.randint(3, 420)
        col = GREEN if ch > 0 else RED
        d.text((30, y), r, font=font("mono", 44, 700), fill=WHITE)
        bw = min(560, abs(ch) * 1.6 + 40)
        d.rectangle([420, y + 8, 420 + bw, y + 48], fill=tuple(int(c * 0.45) for c in col))
        d.text((W - 30, y), f"{'+' if ch > 0 else ''}{ch}%", font=font("mono", 44, 800), fill=col, anchor="ra")
        y += 72
    im.save(os.path.join(OUT, name))


def neon(name, text, color, W=2048, H=512, size=300, kind="grotesk", weight=700, sub=None):
    """Neon tube sign on black (used as emission with black = off)."""
    base = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(base)
    f = font(kind, size, weight)
    cy = H // 2 - (40 if sub else 0)
    d.text((W // 2, cy), text, font=f, fill=color, anchor="mm")
    if sub:
        d.text((W // 2, H - 90), sub, font=font("grotesk", 90, 700), fill=sub[1] if isinstance(sub, tuple) else WHITE, anchor="mm")
    halo = base.filter(ImageFilter.GaussianBlur(26))
    core = Image.new("RGB", (W, H), (0, 0, 0))
    dc = ImageDraw.Draw(core)
    dc.text((W // 2, cy), text, font=f, fill=(255, 255, 255), anchor="mm")
    core = core.filter(ImageFilter.GaussianBlur(2))
    out = Image.eval(Image.merge("RGB", [
        Image.blend(halo.split()[i], base.split()[i], 0.6) for i in range(3)]), lambda v: min(255, int(v * 1.25)))
    out = Image.blend(out, Image.composite(core, out, core.convert("L").point(lambda v: int(v * 0.55))), 1.0)
    out.save(os.path.join(OUT, name))


def sign(name, text, bg, fg, W=1536, H=256, kind="grotesk", weight=700, size=150):
    im = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(im)
    d.rectangle([12, 12, W - 12, H - 12], outline=fg, width=6)
    d.text((W // 2, H // 2), text, font=font(kind, size, weight), fill=fg, anchor="mm")
    im.save(os.path.join(OUT, name))


def pixel_wall():
    W, H = 1024, 1024
    im = Image.new("RGB", (W, H), (30, 30, 40))
    d = ImageDraw.Draw(im)
    rnd = random.Random(4)
    words = ["gm", "wen", "LFG", "ser", "ngmi", "hodl", "ape", "fren", "moon", "pump", "rekt", "wagmi", "alpha", "dyor", "ribbit", "send"]
    pal = [(255, 92, 150), (38, 222, 129), (29, 155, 240), (255, 196, 61), (130, 90, 255), (255, 140, 0), (245, 247, 252), (0, 200, 200)]
    n = 8
    s = W // n
    for i in range(n):
        for j in range(n):
            c = rnd.choice(pal)
            d.rectangle([i * s + 4, j * s + 4, (i + 1) * s - 4, (j + 1) * s - 4], fill=c)
            if rnd.random() < 0.7:
                fg = (20, 20, 30) if sum(c) > 450 else WHITE
                d.text((i * s + s // 2, j * s + s // 2), rnd.choice(words), font=font("inter", 34, 900), fill=fg, anchor="mm")
    im.save(os.path.join(OUT, "pixel_wall.png"))


def podium_banner():
    W, H = 1024, 512
    im = Image.new("RGB", (W, H), (200, 30, 40))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, 90], fill=GOLD)
    d.rectangle([0, H - 90, W, H], fill=GOLD)
    d.text((W // 2, H // 2), "JUST IN", font=font("bebas", 300), fill=WHITE, anchor="mm")
    im.save(os.path.join(OUT, "podium_justin.png"))


def shop_awning(name, c1, c2, W=512, H=512):
    im = Image.new("RGB", (W, H), c1)
    d = ImageDraw.Draw(im)
    for x in range(0, W, 64):
        d.rectangle([x, 0, x + 31, H], fill=c2)
    im.save(os.path.join(OUT, name))


if __name__ == "__main__":
    timeline_screen()
    billboard_froge()
    billboard_rugweek()
    billboard_gm()
    billboard_wagmi()
    billboard_ore()
    terminal_chart("kiosk_chart.png", 1.8, 21, "$FROGE / CASH", "0.00420", "+420%")
    terminal_chart("cafe_chart_pump.png", 2.2, 31, "$HOPIUM", "0.0690", "+69%")
    terminal_chart("cafe_chart_dump.png", -2.0, 41, "$COPE", "0.0012", "-88%")
    terminal_chart("cafe_chart_crab.png", 0.0, 51, "$GMGM", "1.0000", "+0.4%")
    orderbook("cafe_pulse.png")
    neon("neon_terminal_cafe.png", "TERMINAL CAFÉ", (255, 60, 190), size=260)
    neon("neon_wagmi.png", "WAGMI", (40, 240, 255), W=1024, H=384, size=230)
    neon("neon_gm.png", "gm", (255, 210, 60), W=512, H=384, size=260, kind="inter", weight=800)
    neon("neon_nfa.png", "not financial advice", (140, 255, 120), W=2048, H=320, size=150)
    sign("sign_terminal_cafe.png", "TERMINAL CAFÉ", (18, 22, 36), (255, 90, 200))
    sign("sign_gm_coffee.png", "gm coffee", (250, 240, 225), (120, 70, 40), kind="inter", weight=800)
    sign("sign_rekt_clinic.png", "REKT CLINIC", (245, 245, 250), (200, 30, 40))
    sign("sign_cope_bakery.png", "COPE & SEETHE BAKERY", (60, 40, 30), (255, 220, 160), size=120)
    pixel_wall()
    podium_banner()
    shop_awning("awning_red.png", (200, 40, 50), (250, 245, 235))
    shop_awning("awning_green.png", (40, 120, 80), (250, 245, 235))
    shop_awning("awning_blue.png", (40, 80, 160), (250, 245, 235))
    print("wrote", sorted(os.listdir(OUT)))
