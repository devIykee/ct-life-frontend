"""Quick contact sheet: peek_sheet.py out.jpg cell_h img1 img2 ..."""
import sys
from PIL import Image
out, h = sys.argv[1], int(sys.argv[2])
ims = []
for p in sys.argv[3:]:
    try:
        im = Image.open(p); im.seek(0); im = im.convert('RGBA')
        bg = Image.new('RGBA', im.size, (235, 230, 222, 255)); bg.alpha_composite(im); im = bg.convert('RGB')
        ims.append(im.resize((max(1, int(im.width * h / im.height)), h)))
    except Exception as e:
        print('skip', p, e)
W = sum(i.width for i in ims)
sheet = Image.new('RGB', (W, h), (235, 230, 222)); x = 0
for i in ims: sheet.paste(i, (x, 0)); x += i.width
sheet.save(out, quality=88); print(out, sheet.size)
