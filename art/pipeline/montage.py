"""Grid montage with labels using Blender's bundled OpenImageIO.
Usage: Blender -b --factory-startup -P montage.py -- out.png cols cell_px title img1[::label] img2[::label] ...
(or run with Blender's python binary directly)."""
import sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bitfont
import OpenImageIO as oiio
from OpenImageIO import ImageBuf, ImageSpec, ImageBufAlgo, ROI

def montage(out, cols, cell, items, title=None, bg=(0.97, 0.95, 0.92), font_size=None, label_h=None):
    font_size = font_size or max(12, cell // 12)
    label_h = label_h or int(font_size * 1.6)
    rows = (len(items) + cols - 1) // cols
    title_h = int(font_size * 2.6) if title else 0
    W, H = cols * cell, rows * (cell + label_h) + title_h
    canvas = ImageBuf(ImageSpec(W, H, 3, oiio.FLOAT))
    ImageBufAlgo.fill(canvas, bg)
    labels = []
    if title:
        labels.append((16, int(font_size * 0.6), title, max(2, font_size // 6)))
    for i, (path, label) in enumerate(items):
        r, c = divmod(i, cols)
        x0, y0 = c * cell, title_h + r * (cell + label_h)
        src = ImageBuf(path)
        if src.has_error:
            continue
        spec = src.spec()
        s = min((cell - 8) / spec.width, (cell - 8) / spec.height)
        w, h = max(1, int(spec.width * s)), max(1, int(spec.height * s))
        rs = ImageBuf(ImageSpec(w, h, spec.nchannels, oiio.FLOAT))
        ImageBufAlgo.resize(rs, src)
        if spec.nchannels == 4:
            bgb = ImageBuf(ImageSpec(w, h, 4, oiio.FLOAT)); ImageBufAlgo.fill(bgb, (*bg, 1.0))
            rs = ImageBufAlgo.over(rs, bgb)
        rs3 = ImageBufAlgo.channels(rs, (0, 1, 2))
        ox, oy = x0 + (cell - w) // 2, y0 + (cell - h) // 2
        ImageBufAlgo.paste(canvas, ox, oy, 0, 0, rs3)
        if label:
            labels.append((x0 + 8, y0 + cell + int(label_h * 0.2), label[:48], max(1, font_size // 8)))
    arr = canvas.get_pixels(oiio.FLOAT).reshape(H, W, 3).copy()
    for x, y, t, sc in labels:
        bitfont.draw(arr, x, y, t, scale=sc)
    canvas.set_pixels(canvas.roi, np.ascontiguousarray(arr))
    canvas.write(out)
    print('MONTAGE', out, W, H)

if __name__ == '__main__':
    a = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    out, cols, cell, title = a[0], int(a[1]), int(a[2]), a[3]
    items = []
    for x in a[4:]:
        p, _, l = x.partition('::')
        items.append((p, l or os.path.basename(os.path.dirname(p))))
    montage(out, cols, cell, items, title or None)
