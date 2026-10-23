"""World v3: side-by-side old vs new (stacked, labelled). Usage: python3 world_v3_compare.py <old.png> <new.png> <out.png>"""
import sys
from PIL import Image, ImageDraw, ImageFont
old, new, out = sys.argv[1:4]
W = 1600
a = Image.open(old).convert('RGB'); b = Image.open(new).convert('RGB')
a = a.resize((W, int(a.height * W / a.width))); b = b.resize((W, int(b.height * W / b.width)))
S = Image.new('RGB', (W, a.height + b.height + 8), (20, 20, 20))
S.paste(a, (0, 0)); S.paste(b, (0, a.height + 8))
d = ImageDraw.Draw(S)
try:
    f = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial Bold.ttf', 34)
except Exception:
    f = None
for y, t in ((0, 'NOW: in-game (2026-10-19, Kenney / KayKit / Quaternius toy kits)'), (a.height + 8, 'WORLD v3 LOOKDEV: semi-real kit, Blender EEVEE, golden hour')):
    d.rectangle((0, y, W, y + 52), fill=(0, 0, 0))
    d.text((16, y + 8), t, fill=(255, 255, 255), font=f)
S.save(out)
print('saved', out)
