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
    for vid, sh in L.SHELLS.items():
        title = (areas.get(vid, {}).get('title') or vid.replace('_', ' ')).upper()
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
    print(f'[signs] {n} signs in {OUT}', flush=True)


if __name__ == '__main__':
    main()
