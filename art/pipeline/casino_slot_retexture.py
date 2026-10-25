"""The Rug Room (R3, 2026-10-24): retexture the slot cabinet atlas with our own art.

The Sketchfab slot pair (nermin, CC BY) ships two skins with a film's title and dinosaur art, a "Vegas Slot" logo, classic
fruit/BAR/7/JACKPOT reels and a "CASH OUT" button. This replaces every one of those regions in the processed base colour
atlas with the Rug Room panels from art/source/casino (CT symbols, our neon wordmark, our paytable, SPIN/BET/MAX/INFO/AUTO
caps). Only RGB is replaced: the atlas alpha (the reel windows in the glass) is kept.

Run after world_v3_process.py re-creates the atlas and after art/source/casino/render.sh:
  uv run --no-project --with pillow==12.3.0 python -I art/pipeline/casino_slot_retexture.py
The atlas is stored top-down with the cabinet's UVs flipped vertically, so every panel is flipped before pasting.
"""
import os
from PIL import Image, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
ATLAS = os.path.join(REPO, 'art', 'processed', 'world_v3', 'v3_casino', 'textures', 'cs_slot_machine_BaseColor.png')
PNG = os.path.join(REPO, 'art', 'source', 'casino', 'out', 'png')

# panel -> atlas box (x0, y0, x1, y1) as stored. Skin 1 (left cabinet) = theme a, skin 2 = theme b.
BOXES = [
    ('slot_top_a', (0, 0, 512, 512)), ('slot_top_b', (512, 0, 1024, 512)),            # top signs
    ('slot_glass_a', (0, 512, 512, 1024)), ('slot_glass_b', (512, 512, 1024, 1024)),  # reel glass
    ('slot_belly_a', (1024, 512, 1536, 1024)), ('slot_belly_b', (1536, 512, 2048, 1024)),  # belly glass
    ('slot_strip', (1024, 1536, 1434, 2048)), ('slot_strip', (1536, 1536, 1946, 2048)),   # reel strips (3 columns each)
    ('slot_buttons', (1434, 1542, 1525, 1939)), ('slot_buttons', (1946, 1542, 2037, 1939)),  # button caps
]
# blank the white margins round the button caps (their old labels' shadow) with the cap column's background
CLEAR = [(1434, 1536, 1536, 1542), (1525, 1536, 1536, 2048), (1434, 1939, 1536, 2048),
         (1946, 1536, 2048, 1542), (2037, 1536, 2048, 2048), (1946, 1939, 2048, 2048),
         (1544, 468, 1604, 485)]   # the bill acceptor's 'insert bills' sticker


def main():
    atlas = Image.open(ATLAS).convert('RGBA')
    alpha = atlas.getchannel('A')
    rgb = atlas.convert('RGB')
    for box in CLEAR:
        rgb.paste((24, 22, 30), box)
    for name, (x0, y0, x1, y1) in BOXES:
        p = Image.open(os.path.join(PNG, name + '.png')).convert('RGBA')
        bg = Image.new('RGBA', p.size, (20, 8, 28, 255))
        p = Image.alpha_composite(bg, p).convert('RGB').resize((x1 - x0, y1 - y0), Image.LANCZOS)
        rgb.paste(ImageOps.flip(p), (x0, y0))
    out = rgb.convert('RGBA')
    out.putalpha(alpha)
    out.save(ATLAS, optimize=True)
    print('retextured', ATLAS, len(BOXES), 'panels')


if __name__ == '__main__':
    main()
