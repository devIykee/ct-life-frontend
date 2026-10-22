"""CT Island sky from a CC0 Poly Haven HDRI (2026-10-22): the skybox panorama, the reflection/ambient environment and the
light-rig numbers, so the sky, the sun, the trilight ambient and every reflection come from one real sky.

Source: Poly Haven "Kloppenheim 06 (Pure Sky)" (Greg Zaal, sky edits Jarod Guest), CC0,
https://polyhaven.com/a/kloppenheim_06_puresky (4k and 1k .hdr, cached under art/raw/polyhaven/, gitignored).

Writes (client/Assets/World/Island/Textures/):
  sky_pano.png   4096 x 1024 8-bit panorama of the upper sky, elevation -6 deg (bottom row) to +90 deg (top row), azimuth
                 rotated so the HDRI's sun sits at u = 0.5 (the sky shader turns u = 0.5 towards the island's sun heading);
                 linear radiance x exposure through a soft filmic shoulder (the island grade does the rest).
  sky_env.hdr    the 1k HDRI rotated so its sun stands where the island's sun is (azimuth 218 deg: IslandAtmosphere heading 38)
                 under Unity's latitude-longitude cubemap layout (u = 0.5 - atan2(z, x) / 2pi), imported as the reflection
                 cubemap for the Kit PBR shader and the island's reflection probe (URP Lit cars, characters): no rotation at run time.
Prints the trilight ambient (sky / equator / ground, linear) and the horizon colour (the fog and haze must match it).
Run: uv run --no-project --with numpy==2.4.2 --with pillow==12.3.0 python -I art/pipeline/world_sky_hdri.py
"""
import os, sys, struct
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
RAW = os.path.join(REPO, 'art', 'raw', 'polyhaven', 'hdri_kloppenheim_06_puresky')
OUT = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures')
EXPOSURE = 0.62           # linear scale before the shoulder (the sky's mid blue lands near sRGB 0.6)
ELEV_MIN = -6.0


def read_hdr(path):
    """Radiance RGBE (new-style RLE scanlines) -> float32 HxWx3 linear."""
    with open(path, 'rb') as f:
        data = f.read()
    i = 0
    while True:                                   # header ends at an empty line
        j = data.index(b'\n', i)
        line = data[i:j]
        i = j + 1
        if line == b'':
            break
    j = data.index(b'\n', i)
    dims = data[i:j].split()
    i = j + 1
    H, W = int(dims[1]), int(dims[3])
    img = np.zeros((H, W, 4), np.uint8)
    buf = memoryview(data)
    for y in range(H):
        if buf[i] != 2 or buf[i + 1] != 2:
            raise ValueError('flat RGBE scanlines not supported')
        i += 4
        for c in range(4):
            x = 0
            row = img[y, :, c]
            while x < W:
                n = buf[i]; i += 1
                if n > 128:
                    n -= 128
                    row[x:x + n] = buf[i]; i += 1
                else:
                    row[x:x + n] = np.frombuffer(buf[i:i + n], np.uint8); i += n
                x += n
    e = img[..., 3].astype(np.int32)
    scale = np.where(e > 0, np.ldexp(1.0, e - 136), 0.0).astype(np.float32)
    return img[..., :3].astype(np.float32) * scale[..., None]


def write_hdr(path, rgb):
    """Float HxWx3 -> flat (uncompressed) RGBE .hdr."""
    H, W, _ = rgb.shape
    m = rgb.max(axis=2)
    e = np.zeros_like(m, dtype=np.int32)
    mant, e = np.frexp(m)
    scale = np.where(m > 1e-32, mant * 256.0 / np.maximum(m, 1e-32), 0.0)
    out = np.zeros((H, W, 4), np.uint8)
    out[..., :3] = np.clip(rgb * scale[..., None], 0, 255).astype(np.uint8)
    out[..., 3] = np.where(m > 1e-32, e + 128, 0).astype(np.uint8)
    with open(path, 'wb') as f:
        f.write(b'#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n')
        f.write(f'-Y {H} +X {W}\n'.encode())
        f.write(out.tobytes())


def sun_column(rgb):
    lum = rgb @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    H = rgb.shape[0]
    upper = lum[: H // 2]
    y, x = np.unravel_index(np.argmax(upper), upper.shape)
    return x, 90.0 - (y + 0.5) / H * 180.0


def shoulder(x):
    # soft filmic shoulder: linear to 0.6, then compressed towards 1
    return np.where(x < 0.6, x, 0.6 + 0.4 * (1.0 - np.exp(-(x - 0.6) / 0.4)))


def main():
    big = read_hdr(os.path.join(RAW, 'kloppenheim_06_puresky_4k.hdr'))
    small = read_hdr(os.path.join(RAW, 'kloppenheim_06_puresky_1k.hdr'))
    H, W, _ = big.shape
    sx, sel = sun_column(big)
    shift = W // 2 - sx                                   # the sun to u = 0.5
    big = np.roll(big, shift, axis=1)
    sun_az = np.radians(38.0 + 180.0)                              # where the island's sun stands (from the light's heading)
    tx, tz = np.sin(sun_az), np.cos(sun_az)
    u_env = (0.5 - np.arctan2(tz, tx) / (2 * np.pi)) % 1.0
    w1 = small.shape[1]
    small = np.roll(small, int(round(u_env * w1 - sx * w1 / W)), axis=1)
    print(f'sun at column {sx} of {W} (azimuth {sx / W * 360:.1f} deg in the source), elevation {sel:.1f} deg; rolled {shift}')
    # panorama of the upper sky
    rows = np.arange(1024)
    elev = 90.0 - (rows + 0.5) / 1024 * (90.0 - ELEV_MIN)          # top row = zenith
    src_y = np.clip(((90.0 - elev) / 180.0 * H).astype(int), 0, H - 1)
    pano = big[src_y]
    pano = shoulder(pano * EXPOSURE)
    srgb = np.where(pano <= 0.0031308, pano * 12.92, 1.055 * np.power(np.clip(pano, 0, None), 1 / 2.4) - 0.055)
    Image.fromarray(np.clip(srgb * 255 + 0.5, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, 'sky_pano.png'), optimize=True)
    write_hdr(os.path.join(OUT, 'sky_env.hdr'), small)
    # light-rig numbers (linear, at the panorama's exposure)
    h1 = small.shape[0]
    el = 90.0 - (np.arange(h1) + 0.5) / h1 * 180.0
    w = np.cos(np.radians(el))[:, None]
    def band(lo, hi):
        m = (el >= lo) & (el < hi)
        px = small[m] * EXPOSURE
        ww = np.broadcast_to(w[m], px.shape[:2])
        return (px * ww[..., None]).sum((0, 1)) / ww.sum()
    sky, eq, hor = band(30, 90), band(-5, 15), band(0, 6)
    print('ambient sky     (linear)', np.round(sky, 3))
    print('ambient equator (linear)', np.round(eq, 3))
    print('horizon         (linear)', np.round(hor, 3), ' sRGB', np.round(np.power(np.clip(hor, 0, 1), 1 / 2.2), 3))


if __name__ == '__main__':
    main()
