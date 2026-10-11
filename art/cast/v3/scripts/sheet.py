# /// script
# dependencies = ["pillow"]
# ///
"""Contact-sheet compositor for cast renders.
uv run sheet.py <spec.json>
spec: {"out": path, "title": str, "footer": str, "cols": int, "cell_w": int,
       "cells": [{"images": [path, ...], "label": str, "sub": str}]}
Each cell stacks its images vertically (scaled to cell_w), then two caption lines."""
import json, sys
from PIL import Image, ImageDraw, ImageFont

spec = json.load(open(sys.argv[1]))
cw = spec.get('cell_w', 300)
cols = spec['cols']
pad = spec.get('pad', 8)
bg = tuple(spec.get('bg', (22, 26, 36)))

def font(size, bold=False):
    for p in (['/System/Library/Fonts/Supplemental/Arial Bold.ttf'] if bold else []) + ['/System/Library/Fonts/Supplemental/Arial.ttf',
              '/System/Library/Fonts/Helvetica.ttc']:
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            continue
    return ImageFont.load_default()

F1, F2, FT, FF = font(spec.get('label_size', 17), True), font(spec.get('sub_size', 13)), font(30, True), font(13)
cells = []
for c in spec['cells']:
    ims = []
    for p in c['images']:
        im = Image.open(p).convert('RGB')
        h = round(im.height * cw / im.width)
        ims.append(im.resize((cw, h), Image.LANCZOS))
    cells.append((ims, c.get('label', ''), c.get('sub', ''), c.get('sub2', '')))
cap = spec.get('caption_h', 54)
cell_h = max(sum(i.height for i in ims) for ims, *_ in cells) + cap
rows = (len(cells) + cols - 1) // cols
top = 64 if spec.get('title') else pad
foot = 30 if spec.get('footer') else pad
W = cols * (cw + pad) + pad
H = top + rows * (cell_h + pad) + foot
sheet = Image.new('RGB', (W, H), bg)
d = ImageDraw.Draw(sheet)
if spec.get('title'):
    d.text((pad + 4, 16), spec['title'], fill=(240, 242, 246), font=FT)
for k, (ims, lab, sub, sub2) in enumerate(cells):
    r, c = divmod(k, cols)
    x = pad + c * (cw + pad); y = top + r * (cell_h + pad)
    yy = y
    for im in ims:
        sheet.paste(im, (x, yy)); yy += im.height
    d.text((x + 4, yy + 4), lab, fill=(242, 244, 248), font=F1)
    d.text((x + 4, yy + 25), sub, fill=(170, 182, 200), font=F2)
    if sub2:
        d.text((x + 4, yy + 40), sub2, fill=(150, 160, 176), font=F2)
if spec.get('footer'):
    d.text((pad + 4, H - 22), spec['footer'], fill=(150, 156, 168), font=FF)
sheet.save(spec['out'], quality=spec.get('quality', 88))
print(spec['out'], sheet.size)
