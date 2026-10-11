"""Ground, road and water textures for CT Island v2 (written straight into the Unity project).

Tiling detail comes from CC0 ambientCG / Poly Haven photos (see art/LICENSES.md), reduced to normalised grayscale
detail so the stylised colours stay in the shaders and the painted macro map. Depth and far-LOD maps come from the
generated heightfield and road network.
"""
import os

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.spatial import cKDTree

import geom as G
import layout as L
import terrain as T

RAW_CANDIDATES = [
    os.path.join(os.path.dirname(__file__), '..', '..', '..', 'art', 'raw'),
    '/Users/mac/Documents/codes/projects/ct-life/art/raw',     # raw packs live in the main checkout (gitignored)
]


def raw(*parts):
    for base in RAW_CANDIDATES:
        p = os.path.join(base, *parts)
        if os.path.exists(p):
            return p
    raise FileNotFoundError(os.path.join(*parts))


def _gray(path, size):
    im = Image.open(path).convert('RGB').resize((size, size), Image.Resampling.LANCZOS)
    a = np.asarray(im, dtype=np.float64) / 255.0
    return a @ np.array([0.299, 0.587, 0.114])


def detail(path, size, highpass=48, contrast=1.0):
    """Normalised tiling detail: luminance minus its blur (wrap), scaled to mean 0.5, ~+-0.35 range."""
    g = _gray(path, size)
    low = ndimage.gaussian_filter(g, highpass, mode='wrap')
    d = g - low
    d = d / (np.std(d) * 3.2 + 1e-6) * contrast
    return np.clip(d + 0.5, 0.0, 1.0)


def save_gray(arr, path):
    Image.fromarray(np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8), 'L').save(path, optimize=True)


def save_rgba(arr, path):
    Image.fromarray(np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA').save(path, optimize=True)


def ripple_normal(size=256, seed=3):
    """Tileable ripple normal map from wrapped noise."""
    rng = np.random.default_rng(seed)
    n = rng.standard_normal((size, size))
    h = ndimage.gaussian_filter(n, 3.0, mode='wrap') * 0.6 + ndimage.gaussian_filter(n, 9.0, mode='wrap') * 1.6
    h = (h - h.min()) / (h.max() - h.min())
    gy, gx = np.gradient(np.pad(h, 1, mode='wrap'))
    gx, gy = gx[1:-1, 1:-1], gy[1:-1, 1:-1]
    s = 6.0
    nx, ny, nz = -gx * s, -gy * s, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / l * 0.5 + 0.5, ny / l * 0.5 + 0.5, nz / l * 0.5 + 0.5, h], axis=-1)
    return rgb


def clouds(size=512, seed=11):
    """Tileable soft cloud noise (r): a few octaves of wrapped blurred noise, contrast-stretched."""
    rng = np.random.default_rng(seed)
    acc = np.zeros((size, size))
    amp = 1.0
    for sigma in (40, 20, 10, 5, 2.5):
        acc += ndimage.gaussian_filter(rng.standard_normal((size, size)), sigma, mode='wrap') * amp * sigma
        amp *= 0.55
    acc = (acc - acc.min()) / (acc.max() - acc.min())
    return np.clip((acc - 0.25) / 0.6, 0, 1)


def water_depth(h, river, size=1024):
    """r = water depth / 12 m for the sea, the river and the lakes (0 on dry land)."""
    depth = np.clip(L.SEA - h, 0, None)
    line, level, hw, dp = river
    d, i = cKDTree(line).query(np.stack([T.X.ravel(), T.Z.ravel()], axis=1), distance_upper_bound=90)
    d = d.reshape(h.shape)
    ok = np.isfinite(d)
    i = np.where(ok.ravel(), i, 0).reshape(h.shape)
    near = ok & (d < hw[i] + 8.0)
    depth = np.where(near, np.maximum(depth, level[i] - h), depth)
    for lake in L.LAKES:
        cx, cz = lake['center']
        r = np.hypot(T.X - cx, T.Z - cz)
        depth = np.where(r < lake['radius'] + 6, np.maximum(depth, lake['level'] - h), depth)
    return T.resize(np.clip(depth / 12.0, 0, 1), size)


def far_macro(macro, h, net, size=2048):
    """The macro tint with roads, sidewalks and districts painted in, for the far terrain (seen from the hills)."""
    m = T.resize(macro, size)
    img = Image.fromarray(np.clip(m[::-1] * 255, 0, 255).astype(np.uint8), 'RGBA')
    d = ImageDraw.Draw(img)
    scale = size / (2 * L.DOMAIN)

    def px(x, z):
        return ((x + L.DOMAIN) * scale, (L.DOMAIN - z) * scale)

    for s in net.segs:
        p2, ys, ug = net.trimmed(s)
        if len(p2) < 2:
            continue
        tan = G.tangents(p2)
        rn = G.right_normals(tan)
        if s.sidewalk > 0:
            poly = np.vstack([p2 + rn * s.out_r, (p2 - rn * s.out_l)[::-1]])
            d.polygon([px(x, z) for x, z in poly], fill=(206, 200, 188, 255))
        col = (150, 118, 84, 255) if s.prof['surface'] == 'dirt' else (92, 94, 102, 255)
        poly = np.vstack([p2 + rn * s.cw_r, (p2 - rn * s.cw_l)[::-1]])
        d.polygon([px(x, z) for x, z in poly], fill=col)
    for j in net.junctions:
        poly = [px(p[0], p[2]) for p in j['polygon']]
        if len(poly) >= 3:
            d.polygon(poly, fill=(92, 94, 102, 255))
    out = np.asarray(img, dtype=np.float64)[::-1] / 255.0
    return out


def write_all(h, river, net, macro, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    pack = np.stack([
        detail(raw('ambientcg', 'Grass004', 'Grass004_1K-JPG_Color.jpg'), 1024, 40, 1.0),
        detail(raw('ambientcg', 'Rock030', 'Rock030_1K-JPG_Color.jpg'), 1024, 64, 1.15),
        detail(raw('ambientcg', 'Ground054', 'Ground054_1K-JPG_Color.jpg'), 1024, 64, 0.9),
        detail(raw('ambientcg', 'Ground037', 'Ground037_1K-JPG_Color.jpg'), 1024, 48, 1.0),
    ], axis=-1)
    save_rgba(pack[::-1], os.path.join(out_dir, 'terrain_detail.png'))
    save_gray(detail(raw('ambientcg', 'Asphalt031', 'Asphalt031_1K-JPG_Color.jpg'), 512, 40, 1.0)[::-1], os.path.join(out_dir, 'asphalt_detail.png'))
    save_gray(detail(raw('ambientcg', 'Ground048', 'Ground048_1K-JPG_Color.jpg'), 512, 40, 1.0)[::-1], os.path.join(out_dir, 'dirt_detail.png'))
    pav = Image.open(raw('ambientcg', 'PavingStones128', 'PavingStones128_1K-JPG_Color.jpg')).convert('RGB').resize((512, 512), Image.Resampling.LANCZOS)
    pa = np.asarray(pav, dtype=np.float64) / 255.0
    lum = pa @ np.array([0.299, 0.587, 0.114])
    warm = np.array([0.86, 0.82, 0.76])                      # warm, light pavers (the plaza look), detail from the photo
    pav2 = np.clip(warm[None, None, :] * (0.78 + (lum[..., None] - lum.mean()) * 1.4), 0, 1)
    Image.fromarray((pav2 * 255).astype(np.uint8), 'RGB').save(os.path.join(out_dir, 'pavers.png'), optimize=True)
    con = Image.open(raw('polyhaven', 'textures', 'concrete_floor_02', 'concrete_floor_02_diff_1k.jpg')).convert('RGB').resize((512, 512), Image.Resampling.LANCZOS) \
        if os.path.exists(os.path.join(RAW_CANDIDATES[1], 'polyhaven', 'textures', 'concrete_floor_02', 'concrete_floor_02_diff_1k.jpg')) else None
    if con is not None:
        ca = np.asarray(con, dtype=np.float64) / 255.0
        cl = ca @ np.array([0.299, 0.587, 0.114])
        con2 = np.clip(np.array([0.80, 0.78, 0.74])[None, None, :] * (0.85 + (cl[..., None] - cl.mean()) * 1.2), 0, 1)
        Image.fromarray((con2 * 255).astype(np.uint8), 'RGB').save(os.path.join(out_dir, 'concrete.png'), optimize=True)
    save_rgba(ripple_normal()[::-1], os.path.join(out_dir, 'water_ripples.png'))
    save_gray(clouds(), os.path.join(out_dir, 'clouds.png'))
    save_gray(water_depth(h, river)[::-1], os.path.join(out_dir, 'water_depth.png'))
    save_rgba(far_macro(macro, h, net)[::-1], os.path.join(out_dir, 'terrain_far_macro.png'))
