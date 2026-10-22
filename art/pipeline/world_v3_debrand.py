"""World v3: paint out real brand names / logos in processed atlas textures (reproducible list of rectangles).

Usage: python3 world_v3_debrand.py   (needs Pillow; run after world_v3_process.py re-creates a texture)
Each entry: texture path under art/processed/world_v3, list of (x0, y0, x1, y1) pixel boxes in the PNG as stored,
fill = the median colour of a 3 px ring around the box (a blank plaque/shop sign, ready for CT Life signage).
"""
import os, sys
from PIL import Image
import statistics

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'processed', 'world_v3')
FIX = {
    # source-image fixes live in world_v3_config.PACKS[...]['debrand'] (applied before atlasing); add atlas-space fixes here
}


def ring(im, b, w=3):
    x0, y0, x1, y1 = b
    px = []
    for x in range(x0 - w, x1 + w):
        for y in list(range(y0 - w, y0)) + list(range(y1, y1 + w)):
            px.append(im.getpixel((x, y)))
    return tuple(int(statistics.median(c[i] for c in px)) for i in range(len(px[0])))


for rel, boxes in FIX.items():
    p = os.path.join(ROOT, rel)
    if not os.path.exists(p):
        print('skip', rel)
        continue
    im = Image.open(p)
    for b in boxes:
        im.paste(ring(im, b), b)
    im.save(p)
    print('debranded', rel, len(boxes))

# Neighbourhood City signage (2026-10-22, world v3 integration): every anchor building carries Japanese shop and
# "residential building" blade signs, legible text with no CT Life meaning. Each sign becomes a blank plate of its own
# background colour (the most common colour in the panel), keeping a 4 px frame from the original; the food photos and
# lantern art go with them. Boxes (x0, y0, x1, y1) in PNG pixels.
#  - facade cards: the hanging plates found by world_v3 integration (near-white / pink plates taller than wide)
#  - base colour atlases: the packed sign tile (2 rows x 6 panels) at its atlas origin, tile size T
BLANK = {
    'nc_buildings/textures/nc_building_01_cards_BaseColor.png': [[6, 293, 135, 666], [170, 293, 299, 657], [357, 293, 486, 657], [521, 293, 650, 657], [662, 293, 791, 657], [1824, 745, 1953, 1118]],
    'nc_buildings/textures/nc_building_02_cards_BaseColor.png': [[3, 119, 80, 333], [1843, 406, 1954, 693]],
    'nc_buildings/textures/nc_building_03_cards_BaseColor.png': [[815, 148, 942, 511], [977, 148, 1102, 502], [1114, 148, 1241, 511]],
    'nc_buildings/textures/nc_building_04_cards_BaseColor.png': [[5, 51, 125, 386], [179, 51, 299, 386], [331, 51, 451, 386], [483, 51, 603, 386], [612, 128, 706, 391], [969, 128, 1063, 391], [1716, 475, 1841, 830], [1899, 475, 2024, 830], [1147, 1719, 1246, 1948], [1607, 1719, 1706, 1948]],
    'nc_buildings/textures/nc_building_05_cards_BaseColor.png': [[1221, 666, 1388, 1104], [1409, 666, 1576, 1104], [1301, 676, 1323, 725], [1489, 676, 1511, 725], [1605, 787, 1714, 1094], [1764, 787, 1873, 1094]],
    'nc_buildings/textures/nc_building_06_cards_BaseColor.png': [[3, 949, 172, 1421], [217, 977, 373, 1418], [414, 1077, 536, 1424], [592, 1077, 714, 1424]],
}
SIGN_TILES = {      # base colour atlas -> (x, y, T) of the sign tile
    'nc_buildings/textures/nc_building_01_BaseColor.png': (1024, 512, 512),
    'nc_buildings/textures/nc_building_02_BaseColor.png': (1792, 768, 256),
    'nc_buildings/textures/nc_building_03_BaseColor.png': (0, 0, 512),
    'nc_buildings/textures/nc_building_04_BaseColor.png': (0, 0, 512),
    'nc_buildings/textures/nc_building_05_BaseColor.png': (1792, 768, 256),
    'nc_buildings/textures/nc_building_06_BaseColor.png': (512, 512, 512),
}
for rel, (x, y, t) in SIGN_TILES.items():
    rows = [(0.0, 0.41), (0.41, 0.86)]
    BLANK.setdefault(rel, []).extend([[x + round(t * c / 6), y + round(t * r0), x + round(t * (c + 1) / 6), y + round(t * r1), 0]
                                      for r0, r1 in rows for c in range(6)])


def blank(im, b):
    """Fill the panel inside a frame-pixel border (4, or b[4]) with its most common colour (16-level bins), alpha untouched."""
    x0, y0, x1, y1 = b[:4]
    frame = b[4] if len(b) > 4 else 4
    x0, y0 = max(0, x0), max(0, y0)
    x1, y1 = min(im.width, x1), min(im.height, y1)
    region = im.crop((x0, y0, x1, y1))
    px = list(region.get_flattened_data()) if hasattr(region, 'get_flattened_data') else list(region.getdata())
    bins = {}
    for c in px:
        k = tuple(v // 16 for v in c[:3])
        bins.setdefault(k, []).append(c)
    best = max(bins.values(), key=len)
    col = tuple(int(statistics.median(c[i] for c in best)) for i in range(len(px[0])))
    inner = (x0 + frame, y0 + frame, x1 - frame, y1 - frame)
    if im.mode == 'RGBA':
        a = im.crop(inner).getchannel('A')
        fill = Image.new('RGBA', a.size, col[:3] + (255,))
        fill.putalpha(a)
        im.paste(fill, inner[:2])
    else:
        im.paste(col[:3], inner)


for rel, boxes in BLANK.items():
    p = os.path.join(ROOT, rel)
    if not os.path.exists(p):
        print('skip', rel)
        continue
    im = Image.open(p)
    for b in boxes:
        blank(im, b)
    im.save(p)
    print('signs blanked', rel, len(boxes))


# 99.Miles office tower: the crown band samples a hot-pink gradient tile (the source's LED crown), which reads as a
# missing-texture magenta on the island skyline. Recoloured to a cool white of the same brightness (alpha kept).
RECOLOR = {
    'v3_buildings/textures/m99_office_tower_BaseColor.png': [((1536, 760, 1792, 1024), (0.86, 0.89, 0.93))],
}
for rel, jobs in RECOLOR.items():
    p = os.path.join(ROOT, rel)
    if not os.path.exists(p):
        print('skip', rel)
        continue
    im = Image.open(p)
    for box, (tr, tg, tb) in jobs:
        region = im.crop(box)
        px = region.load()
        for y in range(region.height):
            for x in range(region.width):
                c = px[x, y]
                lum = max(c[0], c[1], c[2])               # the gradient's own brightness
                px[x, y] = (int(lum * tr), int(lum * tg), int(lum * tb)) + tuple(c[3:])
        im.paste(region, box[:2])
    im.save(p)
    print('recoloured', rel, len(jobs))
