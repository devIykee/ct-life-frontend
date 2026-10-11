"""Texture helpers (numpy + Blender's bundled OpenImageIO). Images are handled as float32
arrays in sRGB-encoded values (what PNGs store), shape (H, W, C), row 0 = top."""
import numpy as np
import OpenImageIO as oiio
from OpenImageIO import ImageBuf, ImageBufAlgo, ImageSpec


def load(path, channels=4):
    b = ImageBuf(path)
    if b.has_error:
        raise IOError(path + ': ' + b.geterror())
    a = b.get_pixels(oiio.FLOAT)
    if a.ndim == 2:
        a = a[:, :, None]
    c = a.shape[2]
    if c == 1:
        a = np.repeat(a, 3, axis=2)
        c = 3
    if channels == 4 and c == 3:
        a = np.concatenate([a, np.ones(a.shape[:2] + (1,), np.float32)], axis=2)
    if channels == 3 and c >= 3:
        a = a[:, :, :3]
    if c == 2 and channels == 4:  # grey + alpha
        a = np.concatenate([np.repeat(a[:, :, :1], 3, axis=2), a[:, :, 1:2]], axis=2)
    return np.ascontiguousarray(a.astype(np.float32))


def resize(a, w, h):
    src = ImageBuf(ImageSpec(a.shape[1], a.shape[0], a.shape[2], oiio.FLOAT))
    src.set_pixels(src.roi, np.ascontiguousarray(a))
    dst = ImageBuf(ImageSpec(w, h, a.shape[2], oiio.FLOAT))
    ImageBufAlgo.resize(dst, src)
    return dst.get_pixels(oiio.FLOAT).reshape(h, w, a.shape[2])


def save(a, path, channels=3):
    a = np.clip(a[:, :, :channels], 0, 1)
    b = ImageBuf(ImageSpec(a.shape[1], a.shape[0], channels, oiio.UINT8))
    b.set_pixels(b.roi, np.ascontiguousarray(a.astype(np.float32)))
    b.write(path)


def hex_rgb(h):
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], np.float32)


def lum(a):
    return a[:, :, 0] * 0.2126 + a[:, :, 1] * 0.7152 + a[:, :, 2] * 0.0722


def recolor(a, tint_hex, k=0.7, mask=None):
    """Keep the fabric/hair detail (luminance ratio) but replace the colour with tint."""
    L = lum(a)
    valid = (a[:, :, 3] > 0.5) if a.shape[2] == 4 else np.ones(L.shape, bool)
    valid &= L > 0.02
    mean = float(L[valid].mean()) if valid.any() else 0.5
    det = np.power(np.clip(L / mean, 0, 4), k)
    out = a.copy()
    rgb = hex_rgb(tint_hex)[None, None, :] * det[:, :, None]
    if mask is None:
        out[:, :, :3] = rgb
    else:
        m = mask[:, :, None]
        out[:, :, :3] = a[:, :, :3] * (1 - m) + rgb * m
    return out


def flatten_alpha(a, base=None, dark=0.75):
    """Hair/alpha textures -> opaque: transparent texels get the average opaque colour (darkened)."""
    if a.shape[2] < 4:
        return a
    al = a[:, :, 3:4]
    valid = a[:, :, 3] > 0.5
    if base is None:
        base = a[:, :, :3][valid].mean(axis=0) * dark if valid.any() else np.array([0.2, 0.15, 0.1])
    out = a.copy()
    out[:, :, :3] = a[:, :, :3] * al + base[None, None, :] * (1 - al)
    out[:, :, 3] = 1
    return out


def dilate(a, mask, iters=8):
    """Grow valid texels (mask True) into empty space to avoid seams after mip-mapping."""
    out = a.copy()
    m = mask.copy()
    for _ in range(iters):
        acc = np.zeros_like(out)
        cnt = np.zeros(m.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sm = np.roll(np.roll(m, dy, 0), dx, 1)
            sa = np.roll(np.roll(out, dy, 0), dx, 1)
            acc += sa * sm[:, :, None]
            cnt += sm
        grow = (~m) & (cnt > 0)
        out[grow] = acc[grow] / cnt[grow][:, None]
        m = m | grow
    return out
