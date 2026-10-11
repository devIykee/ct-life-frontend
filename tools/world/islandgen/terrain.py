"""Heightfield synthesis for CT Island v2: coast, relief, flats, river, roads, venue pads, and the ground maps.

Grid: N x N samples over [-DOMAIN, DOMAIN]^2, CELL metres apart. Arrays are indexed [iz, ix] (row = z, south first).
"""
import math

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.spatial import cKDTree

import geom as G
import layout as L

N = int(round(2 * L.DOMAIN / L.CELL)) + 1
AXIS = np.linspace(-L.DOMAIN, L.DOMAIN, N)
X, Z = np.meshgrid(AXIS, AXIS)          # X[iz, ix], Z[iz, ix]
COAST_TYPES = ['beach', 'rock', 'cliff', 'quay']


def to_px(x, z):
    return ((np.asarray(x) + L.DOMAIN) / L.CELL, (np.asarray(z) + L.DOMAIN) / L.CELL)


def raster_polygon(poly, value=255, img=None):
    img = img or Image.new('L', (N, N), 0)
    pts = [tuple(map(float, to_px(x, z))) for x, z in poly]
    ImageDraw.Draw(img).polygon(pts, fill=value)
    return img


def poly_mask(poly, blur_m=0.0):
    m = np.asarray(raster_polygon(poly), dtype=np.float64) / 255.0
    if blur_m > 0:
        m = ndimage.gaussian_filter(m, blur_m / L.CELL / 2.0)
    return m


def sample(field, x, z):
    """Bilinear sample of a grid field at world points (arrays)."""
    fx, fz = to_px(x, z)
    return ndimage.map_coordinates(field, [np.asarray(fz, float), np.asarray(fx, float)], order=1, mode='nearest')


# ------------------------------------------------------------------ coastline

def coastline():
    """Smoothed coast polyline (closed, ~4 m spacing) with per-sample type index and cliff height."""
    ctrl = np.array([(p[0], p[1]) for p in L.COAST], dtype=np.float64)
    poly = G.catmull_rom(ctrl, closed=True, spacing=4.0)
    # Type of each sample: the stretch it belongs to (nearest preceding control point along the curve).
    tree = cKDTree(ctrl)
    _, nearest = tree.query(poly)
    types = np.array([COAST_TYPES.index(L.COAST[i][2]) for i in nearest])
    cliffh = np.array([float(L.COAST[i][3]) for i in nearest])
    # Natural irregularity along the normals (none on quays, little on beaches).
    tan = G.tangents(poly, closed=True)
    nrm = G.right_normals(tan)
    amp = np.choose(types, [5.0, 11.0, 13.0, 0.0])
    amp = ndimage.gaussian_filter1d(amp, 6, mode='wrap')
    s = G.arc_lengths(np.vstack([poly, poly[:1]]))[:-1]
    noise = (G.fbm(s / 70.0, np.full_like(s, 3.7), octaves=4) - 0.5) * 2.0
    poly = poly + nrm * (noise * amp)[:, None]
    return poly, types, ndimage.gaussian_filter1d(cliffh, 4, mode='wrap')


def land_mask(coast_poly):
    img = raster_polygon(coast_poly)
    # Islet (irregular disc) and causeway strip.
    cx, cz = L.ISLET['center']
    ang = np.linspace(0, 2 * math.pi, 48, endpoint=False)
    r = L.ISLET['radius'] * (0.86 + 0.28 * G.fbm(np.cos(ang) * 2 + 9, np.sin(ang) * 2 + 4, octaves=3))
    raster_polygon(list(zip(cx + r * np.cos(ang), cz + r * np.sin(ang))), img=img)
    path = G.catmull_rom(np.array(L.CAUSEWAY['path']), spacing=2.0)
    tan = G.tangents(path)
    nr = G.right_normals(tan) * L.CAUSEWAY['half_width']
    strip = np.vstack([path + nr, (path - nr)[::-1]])
    raster_polygon(strip, img=img)
    return np.asarray(img) > 127


# ------------------------------------------------------------------ relief

def spur_field(spur):
    (x0, z0, h0), (x1, z1, h1), w = spur
    d, t = G.point_seg_dist(X, Z, x0, z0, x1, z1)
    h = h0 + (h1 - h0) * t ** 0.8
    u = d / w
    return L.SEA + (h - L.SEA) * np.exp(-2.6 * u * u)


def natural_heights(land, inland, seaward, coast_poly, coast_types, coast_cliff):
    """Land relief before the river and roads are carved (y metres)."""
    # Base: rises gently inland with soft undulation.
    base = L.SEA + 3.0 + np.minimum(inland, 320.0) * 0.03 + (G.fbm(X / 260.0, Z / 260.0, octaves=4) - 0.5) * 7.0
    h = base.copy()
    # Mountains: soft union of massifs with angular spurs, then spurs and craggy detail.
    k = 0.09
    acc = np.zeros_like(h)
    for i, (mx, mz, mh, rx, rs, rn) in enumerate(L.MASSIFS):
        dx, dz = X - mx, Z - mz
        rz = np.where(dz < 0, rs, rn)
        u = np.sqrt((dx / rx) ** 2 + (dz / rz) ** 2)
        ang = np.arctan2(dz, dx)
        wob = (G.fbm(np.cos(ang) * 1.6 + i * 3.1, np.sin(ang) * 1.6 + u * 1.2, octaves=3) - 0.5) * 0.55
        ue = u * (1.0 + wob)
        f = np.clip(1.0 - np.clip(ue, 0, 1), 0.0, 1.0) ** 1.9      # pointy tops, gentle aprons
        acc += np.exp(k * (L.SEA + (mh - L.SEA) * f))
    mtn = np.log(acc + 1e-30) / k
    mtn = np.maximum(mtn, L.SEA)
    for sp in L.SPURS:
        mtn = np.maximum(mtn, spur_field(sp))
    rel = np.clip((mtn - L.SEA - 6.0) / 150.0, 0.0, 1.0)
    crag = (G.ridged(X / 70.0, Z / 70.0, octaves=5) - 0.42) * 26.0 + (G.fbm(X / 26.0, Z / 26.0, octaves=3) - 0.5) * 7.0
    mtn = mtn + crag * rel ** 1.1
    h = np.maximum(h, mtn)
    # Hills: irregular (angular wobble), never perfect circles.
    for i, (hx, hz, hh, rx, rz) in enumerate(L.HILLS):
        dx, dz = X - hx, Z - hz
        ang = np.arctan2(dz, dx)
        wob = 1.0 + (G.fbm(np.cos(ang) * 1.3 + 40 + i * 5.3, np.sin(ang) * 1.3 - 7, octaves=3) - 0.5) * 0.7
        e = ((dx / rx) ** 2 + (dz / rz) ** 2) * wob * wob
        h = np.maximum(h, base + (hh - base) * np.exp(-2.6 * e) * (1.0 + 0.12 * (G.fbm(X / 70.0, Z / 70.0) - 0.5)))
    # Builder Hills terraces: flat shelves with steep risers.
    tc, tr = L.TERRACES['center'], L.TERRACES['radius']
    tm = G.smoothstep(tr, tr * 0.6, np.hypot(X - tc[0], Z - tc[1]))
    step = L.TERRACES['step']
    t = (h - L.TERRACES['base']) / step
    q = L.TERRACES['base'] + step * (np.floor(t) + G.smoothstep(0.72, 1.0, t - np.floor(t)))
    q = np.where(h > L.TERRACES['base'], q, h)
    h = h + (q - h) * tm
    # Coast: plateau floors near cliffs, height caps near beaches and quays.
    tree = cKDTree(coast_poly)
    _, ci = tree.query(np.stack([X.ravel(), Z.ravel()], axis=1))
    onehot = np.zeros((len(COAST_TYPES),) + X.shape)
    ct = coast_types[ci].reshape(X.shape)
    for k in range(len(COAST_TYPES)):
        onehot[k] = ndimage.gaussian_filter((ct == k).astype(np.float64), 10.0)
    wsum = onehot.sum(axis=0) + 1e-9
    wb, wr, wc, wq = (onehot[k] / wsum for k in range(4))
    cliffh = ndimage.gaussian_filter(coast_cliff[ci].reshape(X.shape), 8.0)
    d = inland
    floor = (wc * (L.SEA + cliffh * G.smoothstep(2.0, 12.0, d) * (1 - G.smoothstep(70.0, 190.0, d)))
             + wr * (L.SEA + 3.0 * G.smoothstep(1.0, 9.0, d) * (1 - G.smoothstep(30.0, 70.0, d)))
             + (wb + wq) * L.SEA)
    h = np.maximum(h, floor)
    cap = (wb * (L.SEA + 0.6 + d * 0.052) + wr * (L.SEA + 0.4 + d * 0.45) + wc * (L.SEA + 0.6 + d * 3.0)
           + wq * (L.SEA + 2.3 + np.maximum(d - 2.0, 0.0) * 0.6))
    h = np.minimum(h, cap)
    # Seabed.
    s = seaward
    bed = (wb * (L.SEA - np.minimum(0.15 + s * 0.035, 9.0)) + wr * (L.SEA - np.minimum(0.6 + s * 0.09, 14.0))
           + wc * (L.SEA - np.minimum(2.5 + s * 0.22, 22.0)) + wq * (L.SEA - np.minimum(6.5 + s * 0.02, 12.0)))
    bed = np.minimum(bed, L.SEA - 0.15) - np.maximum(s - 160.0, 0.0) * 0.03
    h = np.where(land, h, bed)
    # Flat districts.
    for name, poly, target, edge in L.FLATS:
        m = poly_mask(poly, edge) * land
        if target == 'downtown':
            tgt = L.downtown_height(X, Z)
        elif target == 'farm':
            tgt = ndimage.gaussian_filter(h, 14.0)
        elif target == 'soft':
            tgt = np.minimum(ndimage.gaussian_filter(h, 18.0), ndimage.gaussian_filter(h, 18.0) * 0.6 + 4.0)
        else:
            tgt = np.full_like(h, float(target))
        h = h + (tgt - h) * m
    # Islet mound and causeway crown.
    cx, cz = L.ISLET['center']
    r = np.hypot(X - cx, Z - cz) / L.ISLET['radius']
    mound = L.SEA + 0.8 + (L.ISLET['top'] - L.SEA - 0.8) * np.exp(-2.2 * r * r)
    near_islet = r < 1.6
    h = np.where(land & near_islet, np.maximum(np.minimum(h, mound), L.SEA + 0.4), h)
    path = G.catmull_rom(np.array(L.CAUSEWAY['path']), spacing=2.0)
    cd, _ = cKDTree(path).query(np.stack([X.ravel(), Z.ravel()], axis=1), distance_upper_bound=40)
    cd = cd.reshape(X.shape)
    cw = L.CAUSEWAY['half_width']
    crown = np.isfinite(cd) & (cd < cw + 1.0)
    h = np.where(crown & land, L.CAUSEWAY['top'] - np.where(cd > cw - 2.5, (cd - (cw - 2.5)) * 0.9, 0.0), h)
    return h, dict(beach=wb, rock=wr, cliff=wc, quay=wq)


# ------------------------------------------------------------------ carving

def river_samples():
    ctrl = np.array([(p[0], p[1]) for p in L.RIVER])
    line = G.catmull_rom(ctrl, spacing=2.0)
    sl = G.arc_lengths(line)
    tree = cKDTree(line)
    s_ctrl = [sl[tree.query(c)[1]] for c in ctrl]
    level = np.interp(sl, s_ctrl, [p[2] for p in L.RIVER])
    level = np.minimum.accumulate(level)                       # never flows uphill
    hw = np.interp(sl, s_ctrl, [p[3] for p in L.RIVER])
    depth = np.interp(sl, s_ctrl, [p[4] for p in L.RIVER])
    return line, level, hw, depth


def carve_river(h, land):
    line, level, hw, depth = river_samples()
    pts = np.stack([X.ravel(), Z.ravel()], axis=1)
    d, i = cKDTree(line).query(pts, distance_upper_bound=140.0)
    d = d.reshape(X.shape)
    i = np.where(np.isfinite(d.ravel()), i, 0).reshape(X.shape)
    ok = np.isfinite(d)
    lv, w, dp = level[i], hw[i], depth[i]
    bed = lv - dp * np.sqrt(np.clip(1.0 - (d / w) ** 2, 0.0, 1.0))
    bank_w = np.clip((h - lv) * 1.5, 5.0, 70.0)
    bank = lv + 0.5 + (h - lv - 0.5) * G.smoothstep(w, w + bank_w, d)
    carved = np.where(d < w, np.minimum(h, bed), np.minimum(h, bank))
    # levee: banks never dip below the water next to the channel
    levee = (d >= w) & (d < w + 6.0) & (h > lv)
    carved = np.where(levee, np.maximum(carved, lv + 0.35), carved)
    out = np.where(ok, carved, h)
    for lake in L.LAKES:
        cx, cz = lake['center']
        r = np.hypot(X - cx, Z - cz)
        R = lake['radius']
        lb = lake['level'] - lake['depth'] * np.sqrt(np.clip(1 - (r / R) ** 2, 0, 1))
        lbank = lake['level'] + 0.4 + (out - lake['level'] - 0.4) * G.smoothstep(R, R + 18.0, r)
        out = np.where(r < R, np.minimum(out, lb), np.where(r < R + 18.0, np.minimum(out, lbank), out))
    return out, (line, level, hw, depth)


def carve_roads(h, nat, road_pts):
    """road_pts: dict of arrays x, z, y, hw (half width to the outer edge), carve (bool). Cut/fill with embankments."""
    x, z, y, hw, carve = road_pts['x'], road_pts['z'], road_pts['y'], road_pts['hw'], road_pts['carve']
    keep = carve
    tree = cKDTree(np.stack([x[keep], z[keep]], axis=1))
    yk, hk = y[keep], hw[keep]
    pts = np.stack([X.ravel(), Z.ravel()], axis=1)
    d, i = tree.query(pts, distance_upper_bound=60.0)
    d = d.reshape(X.shape)
    ok = np.isfinite(d)
    i = np.where(ok.ravel(), i, 0).reshape(X.shape)
    ry, rw = yk[i], hk[i]
    core = rw + 0.6
    diff = nat - ry
    # cuts (ground above the road) are steep rock/earth cuts; fills are wider embankments
    bank = np.where(diff > 0, np.clip(diff * 0.75, 2.0, 22.0), np.clip(-diff * 1.5, 2.0, 30.0))
    road = ry - 0.12
    blend = G.smoothstep(core, core + bank, d)
    target = road + (h - road) * blend
    return np.where(ok & (d < core + bank), target, h)


def carve_pads(h, pads):
    """pads: list of dict(cx, cz, w, d, yaw, y). Oriented rectangles flattened to y - 0.12 with embankments."""
    out = h.copy()
    for p in pads:
        a = math.radians(p['yaw'])
        c, s = math.cos(a), math.sin(a)
        r = max(p['w'], p['d']) / 2 + 45
        x0, x1 = np.searchsorted(AXIS, p['cx'] - r), np.searchsorted(AXIS, p['cx'] + r)
        z0, z1 = np.searchsorted(AXIS, p['cz'] - r), np.searchsorted(AXIS, p['cz'] + r)
        sx, sz = X[z0:z1, x0:x1] - p['cx'], Z[z0:z1, x0:x1] - p['cz']
        # world -> local (inverse of local_to_world)
        lx = sx * c - sz * s
        lz = sx * s + sz * c
        qx, qz = np.abs(lx) - p['w'] / 2, np.abs(lz) - p['d'] / 2
        dist = np.hypot(np.maximum(qx, 0), np.maximum(qz, 0)) + np.minimum(np.maximum(qx, qz), 0)
        win = out[z0:z1, x0:x1]
        tgt = p['y'] - 0.12
        dd = win - tgt
        bank = np.where(dd > 0, np.clip(dd * 0.8, 3.0, 20.0), np.clip(-dd * 1.4, 3.0, 26.0))
        blend = G.smoothstep(0.0, bank, dist)
        out[z0:z1, x0:x1] = np.where(dist < bank, tgt + (win - tgt) * blend, win)
    return out


# ------------------------------------------------------------------ ground maps

def ground_maps(h, land, coastw, inland, river, forest):
    gz, gx = np.gradient(h, L.CELL)
    slope = np.hypot(gx, gz)
    rock = G.smoothstep(0.62, 0.95, slope)
    rock = np.maximum(rock, coastw['cliff'] * G.smoothstep(0.35, 0.6, slope))
    rock = np.maximum(rock, G.smoothstep(118.0, 150.0, h) * 0.55)
    sand = coastw['beach'] * G.smoothstep(L.SEA + 4.6, L.SEA + 2.4, h) * (1 - G.smoothstep(80, 110, inland))
    sand = np.maximum(sand, (~land).astype(float))
    sand = np.maximum(sand, coastw['rock'] * G.smoothstep(L.SEA + 1.2, L.SEA + 0.2, h))
    line, level, hw, depth = river
    rd, ri = cKDTree(line).query(np.stack([X.ravel(), Z.ravel()], axis=1), distance_upper_bound=40)
    rd = rd.reshape(X.shape)
    ri = np.where(np.isfinite(rd.ravel()), ri, 0).reshape(X.shape)
    near = np.isfinite(rd) & (rd < hw[ri] + 3.0)
    dirt = np.where(near, 0.85, 0.0)                       # wet gravel banks
    dirt = np.maximum(dirt, (G.fbm(X / 18.0, Z / 18.0) > 0.68) * 0.35 * (1 - rock))
    rock = rock * (1 - sand)
    dirt = dirt * (1 - sand) * (1 - rock)
    grass = np.clip(1.0 - rock - sand - dirt, 0.0, 1.0)
    ctl = np.stack([grass, rock, sand, dirt], axis=-1)
    ctl = ndimage.gaussian_filter(ctl, (1.1, 1.1, 0))           # soft edges: no stair-steps on the 2 m grid
    ctl = ctl / np.maximum(ctl.sum(axis=-1, keepdims=True), 1e-6)
    # Macro tint (sRGB 0..1): lush low grass, olive higher, darker under forest, warm dry patches; A = cavity AO.
    n1 = G.fbm(X / 140.0, Z / 140.0, octaves=4)
    n2 = G.fbm(X / 38.0 + 5, Z / 38.0 - 2, octaves=3)
    lush = np.array([0.34, 0.50, 0.22])
    olive = np.array([0.46, 0.50, 0.26])
    dry = np.array([0.68, 0.62, 0.36])
    deep = np.array([0.19, 0.32, 0.16])
    teal = np.array([0.30, 0.52, 0.32])
    hi = G.smoothstep(10.0, 90.0, h)[..., None]
    tint = lush * (1 - hi) + olive * hi
    tint = tint * (1 - 0.35 * G.smoothstep(0.62, 0.8, n1)[..., None]) + dry * 0.35 * G.smoothstep(0.62, 0.8, n1)[..., None]
    n3 = G.fbm(X / 260.0 - 11, Z / 260.0 + 4, octaves=3)[..., None]
    tint = tint * (1 - 0.3 * G.smoothstep(0.55, 0.75, n3)) + teal * 0.3 * G.smoothstep(0.55, 0.75, n3)
    tint = tint + (n2[..., None] - 0.5) * 0.08
    tint = tint * (1 - forest[..., None] * 0.55) + deep * forest[..., None] * 0.55
    # Farm patchwork: crop colours with darker rows; the furrows show some dirt.
    crop = {'wheat': (0.86, 0.74, 0.36), 'corn': (0.52, 0.66, 0.24), 'sunflower': (0.90, 0.76, 0.22),
            'cabbage': (0.46, 0.66, 0.40), 'lavender': (0.62, 0.52, 0.78), 'pumpkin': (0.78, 0.56, 0.26)}
    for poly, ang, kind in L.FIELDS:
        fm = poly_mask(poly, 3.0) * land
        a = np.radians(ang)
        rows = 0.5 + 0.5 * np.cos((X * np.cos(a) - Z * np.sin(a)) * (2 * np.pi / 3.2))
        colr = np.array(crop.get(kind, (0.7, 0.6, 0.3)))
        fieldcol = colr * (0.82 + 0.18 * rows[..., None])
        tint = tint * (1 - fm[..., None]) + fieldcol * fm[..., None]
        dirtw = fm * (1 - rows) * 0.45
        ctl[..., 3] = np.maximum(ctl[..., 3], dirtw)
        ctl[..., 0] = ctl[..., 0] * (1 - dirtw)
    for name, poly in L.PARKS:
        pm = poly_mask(poly, 4.0) * land
        tint = tint * (1 - pm[..., None] * 0.35) + np.array([0.36, 0.66, 0.28]) * pm[..., None] * 0.35
    blur = ndimage.gaussian_filter(h, 9.0)
    ao = np.clip(1.0 - np.maximum(blur - h, 0.0) * 0.05, 0.62, 1.0)
    macro = np.concatenate([np.clip(tint, 0, 1), ao[..., None]], axis=-1)
    return ctl, macro, slope


def forest_mask(h, land, slope, exclusions):
    """Where forests grow: foothills, mountain slopes below the tree line, hill flanks (not towns, roads, rock)."""
    n = G.fbm(X / 120.0 + 7, Z / 120.0 + 3, octaves=4)
    m = G.smoothstep(4.0, 16.0, h) * G.smoothstep(128.0, 104.0, h) * G.smoothstep(0.85, 0.55, slope)
    m = m * G.smoothstep(0.38, 0.52, n)
    m = m * land * (1 - exclusions)
    return np.clip(m, 0, 1)


def resize(field, size):
    """Resample a grid field to size x size texels covering the same domain (texel centres)."""
    t = (np.arange(size) + 0.5) / size * (N - 1)
    zz, xx = np.meshgrid(t, t, indexing='ij')
    if field.ndim == 2:
        return ndimage.map_coordinates(field, [zz, xx], order=1, mode='nearest')
    return np.stack([ndimage.map_coordinates(field[..., k], [zz, xx], order=1, mode='nearest') for k in range(field.shape[-1])], axis=-1)
