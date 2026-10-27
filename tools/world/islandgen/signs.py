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

sys.path.insert(0, os.path.join(HERE, '..', '..'))
import names_table as N   # noqa: E402  contract/ids/names.json: every venue name comes from the one table

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures', 'Signs')


def main():
    os.makedirs(OUT, exist_ok=True)
    areas = {a['id']: a for a in json.load(open(os.path.join(REPO, 'client', 'Assets', 'Resources', 'UI', 'Map', 'areas.json')))['areas']}
    n = 0
    items = [(vid, sh, N.upper('venue.' + vid) if N.has('venue.' + vid) else
              (sh.get('sign') or areas.get(vid, {}).get('title') or vid.replace('_', ' ')).upper()) for vid, sh in L.SHELLS.items()]
    # sign_art: the board is drawn elsewhere (The Rug Room's neon board: art/source/casino, install.sh)
    items += [(lb['id'], lb, N.upper('venue.' + lb['id']) if N.has('venue.' + lb['id']) else
               (lb['sign'] if lb['sign'] == lb['sign'].lower() else lb['sign'].upper())) for lb in getattr(L, 'LANDMARK_BUILDINGS', [])
              if lb.get('sign') and not lb.get('sign_art')]
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
    sign_route()
    shop_atlas()
    post_signs()
    event_banners()
    print(f'[signs] {n} signs + the shop-sign atlas in {OUT}', flush=True)


def sign_route():
    """The dev `signTour` launch extra's list (IslandWaypoints.SignTour): each venue with a facade board and where it stands."""
    plan = json.load(open(os.path.join(HERE, 'out', 'plan.json')))
    vs = [dict(id=v['id'], x=float(v['center'][0]), z=float(v['center'][1])) for v in plan['venues'] if v['id'] in L.SHELLS]
    vs += [dict(id=lb['id'], x=float(lb['at'][0]), z=float(lb['at'][1])) for lb in getattr(L, 'LANDMARK_BUILDINGS', []) if lb.get('sign')]
    p = os.path.join(REPO, 'client', 'Assets', 'Resources', 'World', 'Island', 'sign_route.json')
    json.dump(dict(version=1, source='tools/world/islandgen/signs.py', venues=vs), open(p, 'w'), indent=1)


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



# W4.1 signposts (512 x 256 each, Textures/Signs/sign_post_<id>.png) and the convention centre's event banners
# (Textures/Signs/event_banners.png, 1024 x 1024: the arch banner 1024 x 256 on top, three vertical banners 256 x 768 below
# at x = 0, 256, 512). 'CHECKPOINT' is our own name for the event: navy and coral, no logo (community-eggs rules).
NAVY, CORAL, WHITE = (24, 34, 82), (250, 128, 104), (248, 248, 250)


def post_signs():
    for sid, lines, bg, ink in (('trail', ['SUMMIT TRAIL', '1.2 km  ^'], (110, 72, 44), (250, 240, 220)),
                                ('checkpoint', [N.upper('venue.conference_hall'), 'MAIN STAGE  >'], NAVY, WHITE)):
        img = Image.new('RGB', (512, 256), bg)
        d = ImageDraw.Draw(img)
        if sid == 'trail':
            for y in range(0, 256, 32):
                d.line([(0, y), (512, y)], fill=tuple(max(0, c - 18) for c in bg), width=3)     # planks
        else:
            for x in range(512):
                t = x / 511.0
                d.line([(x, 220), (x, 256)], fill=tuple(int(CORAL[k] * t + NAVY[k] * (1 - t)) for k in range(3)))
        d.rectangle([10, 10, 501, 245], outline=ink, width=6)
        size = 64
        while size > 24 and d.textlength(lines[0], font=render.font(size, True)) > 450:
            size -= 2
        d.text((256, 96), lines[0], font=render.font(size, True), fill=ink, anchor='mm')
        d.text((256, 170), lines[1], font=render.font(40, True), fill=ink, anchor='mm')
        img.save(os.path.join(OUT, f'sign_post_{sid}.png'), optimize=True)


def event_banners():
    img = Image.new('RGB', (1024, 1024), NAVY)
    d = ImageDraw.Draw(img)
    for x in range(1024):                                   # arch banner: navy to coral sweep
        t = x / 1023.0
        d.line([(x, 0), (x, 255)], fill=tuple(int(NAVY[k] * (1 - t * 0.85) + CORAL[k] * t * 0.85) for k in range(3)))
    title0 = N.upper('venue.conference_hall')      # the place's name from names.json (no invented event name)
    size = 120
    while size > 40 and d.textlength(title0, font=render.font(size, True)) > 960:
        size -= 4
    d.text((512, 110), title0, font=render.font(size, True), fill=WHITE, anchor='mm')
    d.text((512, 205), 'TALKS  /  MEET BUILDERS  /  GM', font=render.font(40, True), fill=WHITE, anchor='mm')
    for k, (title, sub) in enumerate((('TALKS', 'TODAY'), ('SHIP', 'IT'), ('GM', 'BUILDERS'))):
        x0 = k * 256
        for y in range(256, 1024):
            t = (y - 256) / 767.0
            c = tuple(int(NAVY[j] * (1 - t) + CORAL[j] * t) for j in range(3)) if k != 1 else tuple(int(CORAL[j] * (1 - t) + NAVY[j] * t) for j in range(3))
            d.line([(x0, y), (x0 + 255, y)], fill=c)
        for r in range(3):
            d.ellipse([x0 + 128 - 70 - r * 26, 560 - 70 - r * 26, x0 + 128 + 70 + r * 26, 560 + 70 + r * 26], outline=WHITE, width=3)
        d.text((x0 + 128, 360), title, font=render.font(62 if len(title) <= 5 else 54, True), fill=WHITE, anchor='mm')
        d.text((x0 + 128, 430), sub, font=render.font(46 if len(sub) <= 5 else 34, True), fill=WHITE, anchor='mm')
        d.rectangle([x0 + 6, 262, x0 + 249, 1017], outline=WHITE, width=4)
    img.save(os.path.join(OUT, 'event_banners.png'), optimize=True)


if __name__ == '__main__':
    main()
