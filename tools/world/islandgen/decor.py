"""W4 eye-level dressing textures (World lane, made by us, CC0): wall dressing atlas, bus-shelter posters, helipad marking.

  client/Assets/World/Island/Textures/wall_decor.png  2048 x 2048 RGBA, top-left origin:
      murals  4 cells 512 x 512 at (i * 512, 0)            sunset bay, mountain night, geometric, bull chart
      ivy     2 cells 512 x 512 at (i * 512, 512)          climbing ivy, alpha cut-out
      windows 8 cells 256 x 256 at (1024 + (i % 4) * 256, 512 + (i // 4) * 256)   one window each, alpha round it
  client/Assets/World/Island/Textures/bus_ads.png     1024 x 1024, 8 posters 256 x 512 (4 x 2), parody ads, no real brands
  client/Assets/World/Island/Textures/helipad.png     512 x 512 helipad marking (planar uv over the 18 m pad)
IslandV2Dress.cs mirrors these layouts. Run any time; deterministic (seeded).
"""
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from PIL import Image, ImageDraw, ImageFilter

import render

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures')
R = random.Random(42)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def vgrad(d, box, top, bottom):
    x0, y0, x1, y1 = box
    for y in range(y0, y1):
        d.line([(x0, y), (x1 - 1, y)], fill=lerp(top, bottom, (y - y0) / max(1, y1 - y0 - 1)))


# ------------------------------------------------------------------ murals
def mural_sunset(im, ox, oy, S=512):
    d = ImageDraw.Draw(im)
    vgrad(d, (ox, oy, ox + S, oy + int(S * 0.62)), (92, 46, 140, 255), (255, 170, 90, 255))
    vgrad(d, (ox, oy + int(S * 0.62), ox + S, oy + S), (40, 120, 170, 255), (20, 60, 110, 255))
    cx, cy, r = ox + S * 0.5, oy + S * 0.5, S * 0.2
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 226, 120, 255))
    for k in range(5):                                    # retro sun stripes
        y = cy + r * (0.15 + k * 0.17)
        d.rectangle([cx - r, y, cx + r, y + 4 + k * 2], fill=lerp((255, 160, 90, 255), (240, 120, 100, 255), k / 4))
    d.rectangle([ox, oy + int(S * 0.62), ox + S, oy + S], fill=None)
    vgrad(d, (ox, oy + int(S * 0.62), ox + S, oy + S), (50, 140, 190, 255), (18, 54, 100, 255))
    for k in range(9):                                    # sun glitter and waves
        y = oy + S * 0.65 + k * S * 0.04
        w = r * (1.0 - k * 0.08)
        d.line([(cx - w, y), (cx + w, y)], fill=(255, 210, 130, 255), width=3)
        for x in range(ox + 10, ox + S, 60):
            d.arc([x, y + 8, x + 40, y + 22], 200, 340, fill=(140, 200, 230, 255), width=2)
    for px, lean in ((ox + S * 0.12, 0.18), (ox + S * 0.86, -0.22)):   # palm silhouettes
        base = (px, oy + S * 0.96)
        top = (px + lean * S * 0.35, oy + S * 0.36)
        for t in range(40):
            a = t / 40.0
            x = base[0] + (top[0] - base[0]) * a + math.sin(a * 2.4) * 14 * lean / abs(lean)
            y = base[1] + (top[1] - base[1]) * a
            d.ellipse([x - 9 + a * 3, y - 6, x + 9 - a * 3, y + 6], fill=(40, 20, 50, 255))
        for k in range(7):
            ang = -math.pi / 2 + (k - 3) * 0.55
            pts = [top]
            for t in range(1, 12):
                a = t / 11.0
                pts.append((top[0] + math.cos(ang) * S * 0.2 * a, top[1] + math.sin(ang) * S * 0.2 * a + a * a * S * 0.13))
            d.line(pts, fill=(40, 20, 50, 255), width=10)


def mural_mountain(im, ox, oy, S=512):
    d = ImageDraw.Draw(im)
    vgrad(d, (ox, oy, ox + S, oy + S), (24, 18, 70, 255), (220, 110, 150, 255))
    for _ in range(70):
        x, y = ox + R.random() * S, oy + R.random() * S * 0.45
        rr = R.choice((1, 1, 2, 3))
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=(255, 245, 220, 255))
    d.ellipse([ox + S * 0.72, oy + S * 0.1, ox + S * 0.84, oy + S * 0.22], fill=(255, 236, 200, 255))
    layers = [((112, 60, 150, 255), 0.42, 0.46), ((80, 40, 120, 255), 0.55, 0.3), ((46, 24, 80, 255), 0.7, 0.22)]
    for col, base, amp in layers:
        pts = [(ox, oy + S)]
        x = 0
        peaks = []
        while x <= S:
            hgt = base - amp * abs(math.sin(x / S * math.pi * (1.6 + R.random() * 0.4) + R.random()))
            pts.append((ox + x, oy + S * hgt))
            peaks.append((ox + x, oy + S * hgt))
            x += S / 14
        pts.append((ox + S, oy + S))
        d.polygon(pts, fill=col)
        if col == layers[0][0]:
            top = min(peaks, key=lambda q: q[1])
            d.polygon([(top[0] - 34, top[1] + 40), top, (top[0] + 30, top[1] + 36), (top[0] + 8, top[1] + 26), (top[0] - 10, top[1] + 34)],
                      fill=(250, 246, 255, 255))
    f = render.font(64, True)
    d.text((ox + S / 2, oy + S * 0.86), 'SEND IT', font=f, fill=(255, 214, 120, 255), anchor='mm')


def mural_geometric(im, ox, oy, S=512):
    d = ImageDraw.Draw(im)
    d.rectangle([ox, oy, ox + S, oy + S], fill=(250, 238, 214, 255))
    pal = [(28, 170, 160, 255), (250, 110, 90, 255), (255, 200, 60, 255), (110, 70, 200, 255), (30, 40, 70, 255)]
    for _ in range(9):
        c = R.choice(pal); x, y, r = ox + R.random() * S, oy + R.random() * S, 26 + R.random() * 60
        kind = R.random()
        if kind < 0.35:
            d.ellipse([x - r, y - r, x + r, y + r], fill=c)
        elif kind < 0.65:
            a = R.random() * math.tau
            d.polygon([(x + math.cos(a + k * 2.094) * r, y + math.sin(a + k * 2.094) * r) for k in range(3)], fill=c)
        else:
            d.rectangle([x - r * 0.7, y - r * 0.7, x + r * 0.7, y + r * 0.7], fill=c)
    for k in range(3):                                   # zigzags
        y = oy + 60 + k * 170 + R.random() * 40
        pts = [(ox + 20 + i * 36, y + (18 if i % 2 else -18)) for i in range(14)]
        d.line(pts, fill=pal[(k + 2) % 5], width=9)
    for gx in range(6):                                  # dot grid
        for gy in range(6):
            x, y = ox + S * 0.66 + gx * 16, oy + S * 0.7 + gy * 16
            d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=pal[4])
    f = render.font(96, True)
    d.text((ox + S / 2 + 4, oy + S / 2 + 4), 'GM', font=f, fill=pal[4], anchor='mm')
    d.text((ox + S / 2, oy + S / 2), 'GM', font=f, fill=(255, 255, 255, 255), anchor='mm')


def mural_chart(im, ox, oy, S=512):
    d = ImageDraw.Draw(im)
    vgrad(d, (ox, oy, ox + S, oy + S), (16, 22, 48, 255), (36, 30, 84, 255))
    for k in range(1, 8):
        d.line([(ox, oy + k * S / 8), (ox + S, oy + k * S / 8)], fill=(50, 60, 110, 255), width=1)
        d.line([(ox + k * S / 8, oy), (ox + k * S / 8, oy + S)], fill=(50, 60, 110, 255), width=1)
    price = S * 0.78
    for i in range(14):
        x = ox + 24 + i * 34
        move = R.uniform(-30, 44) - 6
        o, c = price, price - move
        up = c < o
        col = (60, 220, 140, 255) if up else (240, 80, 90, 255)
        hi, lo = min(o, c) - R.uniform(6, 22), max(o, c) + R.uniform(6, 22)
        d.line([(x + 9, oy + hi), (x + 9, oy + lo)], fill=col, width=3)
        d.rectangle([x, oy + min(o, c), x + 18, oy + max(o, c) + 2], fill=col)
        price = max(S * 0.18, min(S * 0.86, c - 10))
    d.line([(ox + 40, oy + S * 0.82), (ox + S * 0.88, oy + S * 0.22)], fill=(255, 210, 70, 255), width=10)
    d.polygon([(ox + S * 0.92, oy + S * 0.16), (ox + S * 0.80, oy + S * 0.20), (ox + S * 0.89, oy + S * 0.30)], fill=(255, 210, 70, 255))
    f = render.font(54, True)
    d.text((ox + S * 0.3, oy + S * 0.14), 'WAGMI', font=f, fill=(255, 255, 255, 255), anchor='mm')


# ------------------------------------------------------------------ ivy and windows
def ivy(im, ox, oy, S=512, seed=0):
    rr = random.Random(100 + seed)
    d = ImageDraw.Draw(im)
    greens = [(46, 110, 50, 255), (62, 140, 60, 255), (84, 160, 70, 255), (34, 90, 44, 255), (110, 170, 80, 255)]
    for stem in range(16):
        x, y = ox + rr.random() * S, oy + S
        ang = -math.pi / 2 + rr.uniform(-0.35, 0.35)
        L_ = S * rr.uniform(0.45, 1.0)
        steps = int(L_ / 8)
        for t in range(steps):
            ang += rr.uniform(-0.25, 0.25)
            ang = max(-math.pi * 0.85, min(-math.pi * 0.15, ang))
            nx, ny = x + math.cos(ang) * 8, y + math.sin(ang) * 8
            if not (ox + 4 < nx < ox + S - 4 and oy + 4 < ny < oy + S):
                break
            d.line([(x, y), (nx, ny)], fill=(80, 60, 40, 255), width=3)
            x, y = nx, ny
            for _ in range(2):
                lx, ly = x + rr.uniform(-16, 16), y + rr.uniform(-14, 14)
                lr = rr.uniform(7, 15)
                if ox + lr < lx < ox + S - lr and oy + lr < ly < oy + S - lr:
                    c = rr.choice(greens)
                    d.ellipse([lx - lr, ly - lr * 0.75, lx + lr, ly + lr * 0.75], fill=c)
                    d.line([(lx - lr * 0.5, ly), (lx + lr * 0.5, ly)], fill=lerp(c, (20, 50, 20, 255), 0.5), width=1)


WIN_FRAMES = [(246, 244, 236), (40, 44, 52), (120, 82, 54), (236, 236, 240), (60, 90, 70), (246, 244, 236), (200, 70, 60), (40, 44, 52)]
SHUTTERS = [None, (54, 110, 90), None, (60, 90, 150), None, (170, 70, 60), None, None]


def window(im, ox, oy, i, S=256):
    d = ImageDraw.Draw(im)
    fr = WIN_FRAMES[i] + (255,)
    x0, y0, x1, y1 = ox + 52, oy + 28, ox + S - 52, oy + S - 44         # the opening (152 x 184 px)
    if SHUTTERS[i]:
        sc = SHUTTERS[i] + (255,)
        for sx in (x0 - 44, x1 + 4):
            d.rectangle([sx, y0, sx + 40, y1], fill=sc)
            for k in range(8):
                yy = y0 + 10 + k * 21
                d.line([(sx + 4, yy), (sx + 36, yy)], fill=lerp(sc, (0, 0, 0, 255), 0.35), width=3)
    d.rectangle([x0 - 8, y0 - 8, x1 + 8, y1 + 8], fill=fr)
    lit = i in (2, 5)
    top, bot = ((255, 214, 140, 255), (230, 150, 80, 255)) if lit else ((150, 196, 226, 255), (60, 90, 130, 255))
    vgrad(d, (x0, y0, x1, y1), top, bot)
    if not lit:
        d.polygon([(x0 + 10, y1), (x0 + 60, y0), (x0 + 90, y0), (x0 + 40, y1)], fill=(190, 222, 240, 255))
    if i in (3, 7):                                     # curtains
        cc = (230, 210, 190, 255) if i == 3 else (180, 60, 80, 255)
        d.polygon([(x0, y0), (x0 + 44, y0), (x0 + 22, y1)], fill=cc)
        d.polygon([(x1, y0), (x1 - 44, y0), (x1 - 22, y1)], fill=cc)
    d.line([((x0 + x1) / 2, y0), ((x0 + x1) / 2, y1)], fill=fr, width=8)
    d.line([(x0, y0 + (y1 - y0) * 0.38), (x1, y0 + (y1 - y0) * 0.38)], fill=fr, width=8)
    d.rectangle([x0 - 16, y1 + 8, x1 + 16, y1 + 20], fill=lerp(fr, (0, 0, 0, 255), 0.15))   # sill
    if i in (1, 4, 6):                                  # flower box
        d.rectangle([x0 - 4, y1 + 20, x1 + 4, y1 + 40], fill=(120, 80, 50, 255))
        for k in range(12):
            fx = x0 + 6 + k * 13
            d.ellipse([fx - 7, y1 + 6, fx + 7, y1 + 24], fill=(70, 140, 60, 255))
            d.ellipse([fx - 4, y1 + 4, fx + 4, y1 + 12], fill=[(240, 80, 110, 255), (255, 210, 70, 255), (250, 250, 250, 255)][k % 3])


# ------------------------------------------------------------------ bus posters and helipad
ADS = [
    ('HODL', 'MOBILE', 'Unlimited data.\nZero paper hands.', (30, 30, 40), (255, 210, 70)),
    ('GM', 'COFFEE', 'Wake up early.\nStay up late.', (110, 60, 40), (250, 236, 210)),
    ('WAGMI', 'INSURANCE', 'Coverage for\nevery dip.', (30, 90, 160), (255, 255, 255)),
    ('MOON', 'TOURS', 'Next departure:\nsoon (tm)', (40, 20, 80), (200, 180, 255)),
    ('DIAMOND', 'HANDS GYM', 'Grip strength\nfor any market.', (20, 120, 110), (230, 255, 250)),
    ('RUG', 'DOCTOR', 'We fix rugs.\nAll kinds.', (200, 70, 50), (255, 240, 220)),
    ('SEED', 'SAFE', 'Write it down.\nNever online.', (240, 200, 60), (40, 30, 20)),
    ('APE', 'REALTY', 'Buy the block.\nNot the top.', (90, 50, 140), (255, 220, 120)),
]


def poster(im, ox, oy, k, W=256, H=512):
    a, b, tag, bg, ink = ADS[k]
    d = ImageDraw.Draw(im)
    vgrad(d, (ox, oy, ox + W, oy + H), bg + (255,), lerp(bg, (0, 0, 0), 0.35) + (255,))
    for j in range(5):                                  # bold shapes
        r = 40 + j * 30
        d.ellipse([ox + W * 0.5 - r, oy + H * 0.42 - r, ox + W * 0.5 + r, oy + H * 0.42 + r], outline=lerp(bg, ink, 0.25 + j * 0.08) + (255,), width=6)
    d.text((ox + W / 2, oy + 74), a, font=render.font(70 if len(a) <= 4 else (56 if len(a) <= 5 else 44), True), fill=ink + (255,), anchor='mm')
    d.text((ox + W / 2, oy + 132), b, font=render.font(34 if len(b) <= 7 else 26, True), fill=ink + (255,), anchor='mm')
    d.multiline_text((ox + W / 2, oy + H - 92), tag, font=render.font(24), fill=ink + (255,), anchor='mm', align='center', spacing=8)
    d.rectangle([ox + 6, oy + 6, ox + W - 7, oy + H - 7], outline=ink + (255,), width=4)


def helipad(S=512):
    im = Image.new('RGBA', (S, S), (58, 60, 64, 255))
    d = ImageDraw.Draw(im)
    for _ in range(1600):                                # asphalt speckle
        x, y = R.random() * S, R.random() * S
        c = R.randint(48, 76)
        d.point((x, y), fill=(c, c, c + 3, 255))
    c = S / 2
    d.ellipse([c - S * 0.47, c - S * 0.47, c + S * 0.47, c + S * 0.47], outline=(246, 246, 240, 255), width=14)
    d.ellipse([c - S * 0.40, c - S * 0.40, c + S * 0.40, c + S * 0.40], outline=(250, 200, 50, 255), width=8)
    w, h, t = S * 0.36, S * 0.46, S * 0.085
    for x in (c - w / 2, c + w / 2 - t):
        d.rectangle([x, c - h / 2, x + t, c + h / 2], fill=(250, 250, 246, 255))
    d.rectangle([c - w / 2, c - t / 2, c + w / 2, c + t / 2], fill=(250, 250, 246, 255))
    for k in range(16):                                  # perimeter lights
        a = k / 16 * math.tau
        x, y = c + math.cos(a) * S * 0.485, c + math.sin(a) * S * 0.485
        d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=(120, 220, 120, 255))
    return im


def main():
    os.makedirs(OUT, exist_ok=True)
    atlas = Image.new('RGBA', (2048, 2048), (0, 0, 0, 0))
    for i, fn in enumerate((mural_sunset, mural_mountain, mural_geometric, mural_chart)):
        fn(atlas, i * 512, 0)
    for i in range(2):
        ivy(atlas, i * 512, 512, seed=i)
    for i in range(8):
        window(atlas, 1024 + (i % 4) * 256, 512 + (i // 4) * 256, i)
    atlas.save(os.path.join(OUT, 'wall_decor.png'), optimize=True)
    ads = Image.new('RGBA', (1024, 1024), (0, 0, 0, 255))
    for k in range(8):
        poster(ads, (k % 4) * 256, (k // 4) * 512, k)
    ads.convert('RGB').save(os.path.join(OUT, 'bus_ads.png'), optimize=True)
    helipad().convert('RGB').save(os.path.join(OUT, 'helipad.png'), optimize=True)
    print(f'[decor] wall_decor.png, bus_ads.png, helipad.png in {OUT}')


if __name__ == '__main__':
    main()
