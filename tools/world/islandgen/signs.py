"""Venue sign boards for the CT Island v2 building shells (World lane): the venue's name in bold caps on a board in its
accent colour, a light inner rule, 1024 x 256 px (the sign is 8 x 2 m over the door). Written to
client/Assets/World/Island/Textures/Signs/sign_<venue>.png.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from PIL import Image, ImageDraw

import layout as L
import render

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures', 'Signs')


def main():
    os.makedirs(OUT, exist_ok=True)
    areas = {a['id']: a for a in json.load(open(os.path.join(REPO, 'client', 'Assets', 'Resources', 'UI', 'Map', 'areas.json')))['areas']}
    n = 0
    items = [(vid, sh, (sh.get('sign') or areas.get(vid, {}).get('title') or vid.replace('_', ' ')).upper()) for vid, sh in L.SHELLS.items()]
    items += [(lb['id'], lb, lb['sign'] if lb['sign'] == lb['sign'].lower() else lb['sign'].upper()) for lb in getattr(L, 'LANDMARK_BUILDINGS', []) if lb.get('sign')]
    for vid, sh, title in items:
        W, H = 1024, 256
        bg = tuple(int(c * 255) for c in sh['accent'])
        img = Image.new('RGB', (W, H), bg)
        d = ImageDraw.Draw(img)
        lum = 0.299 * bg[0] + 0.587 * bg[1] + 0.114 * bg[2]
        ink = (24, 24, 30) if lum > 150 else (250, 248, 240)
        d.rectangle([14, 14, W - 15, H - 15], outline=ink, width=6)
        size = 150
        while size > 40:
            f = render.font(size, True)
            w = d.textlength(title, font=f)
            if w < W - 100:
                break
            size -= 6
        d.text((W / 2, H / 2 + 4), title, font=f, fill=ink, anchor='mm')
        img.save(os.path.join(OUT, f'sign_{vid}.png'), optimize=True)
        n += 1
    shop_atlas()
    print(f'[signs] {n} signs + the shop-sign atlas in {OUT}', flush=True)


# Ground-floor shop signs for downtown (W3 street life): small blade signs, 4 x 8 cells of 256 x 128 px in one atlas
# (Textures/Signs/shop_signs.png). Original names in the CT spirit; no real brands. dress.py picks a cell per sign.
SHOPS = [
    ('COFFEE', (0.36, 0.22, 0.14)), ('RAMEN', (0.80, 0.18, 0.16)), ('GM CAFE', (0.98, 0.78, 0.22)), ('PIZZA', (0.20, 0.50, 0.26)),
    ('BAKERY', (0.92, 0.84, 0.66)), ('HODL DELI', (0.16, 0.30, 0.56)), ('PHARMACY', (0.18, 0.62, 0.40)), ('BOOKS', (0.42, 0.20, 0.36)),
    ('TACOS', (0.95, 0.52, 0.18)), ('NOODLES', (0.62, 0.12, 0.12)), ('BARBER', (0.88, 0.88, 0.90)), ('FLOWERS', (0.86, 0.46, 0.62)),
    ('WAGMI MART', (0.24, 0.62, 0.80)), ('GELATO', (0.98, 0.70, 0.74)), ('SUSHI', (0.14, 0.14, 0.16)), ('DINER', (0.82, 0.24, 0.28)),
    ('PRINT SHOP', (0.30, 0.34, 0.40)), ('ARCADE', (0.46, 0.20, 0.82)), ('BAGELS', (0.80, 0.58, 0.30)), ('JUICE', (0.52, 0.78, 0.22)),
    ('HARDWARE', (0.86, 0.62, 0.12)), ('LAUNDRY', (0.60, 0.80, 0.92)), ('BURGERS', (0.70, 0.30, 0.10)), ('TEA HOUSE', (0.34, 0.50, 0.30)),
    ('PAWN', (0.20, 0.20, 0.24)), ('KEBAB', (0.90, 0.36, 0.20)), ('GYM', (0.12, 0.12, 0.14)), ('DUMPLINGS', (0.94, 0.90, 0.80)),
    ('MERCH', (0.40, 0.24, 0.86)), ('NOTARY', (0.26, 0.30, 0.46)), ('PHONE FIX', (0.94, 0.50, 0.30)), ('DONUTS', (0.96, 0.62, 0.78)),
]


def shop_atlas():
    CW, CH, COLS = 256, 128, 4
    img = Image.new('RGB', (CW * COLS, CH * (len(SHOPS) // COLS)), (0, 0, 0))
    d = ImageDraw.Draw(img)
    for i, (name, col) in enumerate(SHOPS):
        x0, y0 = (i % COLS) * CW, (i // COLS) * CH
        bg = tuple(int(c * 255) for c in col)
        d.rectangle([x0, y0, x0 + CW - 1, y0 + CH - 1], fill=bg)
        lum = 0.299 * bg[0] + 0.587 * bg[1] + 0.114 * bg[2]
        ink = (26, 24, 28) if lum > 150 else (252, 248, 238)
        d.rectangle([x0 + 7, y0 + 7, x0 + CW - 8, y0 + CH - 8], outline=ink, width=4)
        size = 64
        while size > 20:
            f = render.font(size, True)
            if d.textlength(name, font=f) < CW - 34:
                break
            size -= 4
        d.text((x0 + CW / 2, y0 + CH / 2 + 2), name, font=f, fill=ink, anchor='mm')
    img.save(os.path.join(OUT, 'shop_signs.png'), optimize=True)


if __name__ == '__main__':
    main()
