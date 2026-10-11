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


def natural_heights(land, inland, seaward, coast_poly, coast_types, coast_cliff, erosion=True, stats=None):
    """Land relief before the river and roads are carved (y metres)."""
    import relief as RL
    # Base: rises gently inland with soft undulation.
    base = L.SEA + 3.0 + np.minimum(inland, 320.0) * 0.03 + (G.fbm(X / 260.0, Z / 260.0, octaves=4) - 0.5) * 7.0
    # The range: one connected crest with spurs, saddles and the summit pyramid (relief.py).
    mtn, mweight, (cs, cd, _, _) = RL.mountain_range(X, Z)
    # Foothills: a belt of rolling hills along the range's foot, so the mountains rise out of green country, not a plain.
    n_f = RL.fbm(X / 150.0 + 3.3, Z / 150.0 - 1.7, 5, seed=31)
    belt = np.where(cd >= 0, np.exp(-((cd - 215.0) / 105.0) ** 2), np.exp(-((-cd - 150.0) / 60.0) ** 2) * 0.5)
    foot = L.SEA + 3.0 + belt * (16.0 + 14.0 * n_f) * (0.6 + 0.4 * RL.ridged_mf(X / 220.0, Z / 170.0, 3, seed=33))
    k = 0.22
    h = np.log(np.exp(k * base) + np.exp(k * np.maximum(mtn, L.SEA)) + np.exp(k * foot)) / k
    # Hills: irregular (domain-warped, with ridged shoulders), never perfect domes.
    for i, (hx, hz, hh, rx, rz) in enumerate(L.HILLS):
        wx = RL.fbm(X / 140.0 + i * 1.7, Z / 140.0 - i, 4, seed=40 + i) * rx * 0.35
        wz = RL.fbm(X / 140.0 - i, Z / 140.0 + i * 2.3, 4, seed=50 + i) * rz * 0.35
        dx, dz = X + wx - hx, Z + wz - hz
        e = (dx / rx) ** 2 + (dz / rz) ** 2
        shape = np.exp(-2.4 * e) * (0.78 + 0.34 * RL.ridged_mf(X / 110.0 + i, Z / 90.0, 3, seed=60 + i))
        bump = base + (hh - base) * shape
        h = np.log(np.exp(0.3 * h) + np.exp(0.3 * bump)) / 0.3
    h = np.where(land, h, np.minimum(h, L.SEA + 1.0))
    if erosion:
        h, maps = erode_relief(h, land)
        if stats is not None:
            stats.update(maps)
    h = RL.shoulder_bench(h, X, Z)
    # Coast: plateau floors near cliffs, height caps near beaches and quays.
    tree = cKDTree(coast_poly)
    _, ci = tree.query(np.stack([X.ravel(), Z.ravel()], axis=1))
    onehot = np.zeros((len(COAST_TYPES),) + X.shape)
    ct = coast_types[ci].reshape(X.shape)
    for k in range(len(COAST_TYPES)):
        onehot[k] = ndimage.gaussian_filter((ct == k).astype(np.float64), 10.0)
    wsum = onehot.sum(axis=0) + 1e-9
    wb, wr, wc, wq = (onehot[k] / wsum for k in range(4))
    cliffh = ndimage.gaussian_filter(coast_cliff[ci].reshape(X.shape), 8.0) * (0.8 + 0.4 * G.fbm(X / 160.0 + 9, Z / 160.0, octaves=3))
    d = inland
    floor = (wc * (L.SEA + cliffh * G.smoothstep(2.0, 12.0, d) * (1 - G.smoothstep(70.0, 190.0, d)))
             + wr * (L.SEA + 3.0 * G.smoothstep(1.0, 9.0, d) * (1 - G.smoothstep(30.0, 70.0, d)))
             + (wb + wq) * L.SEA)
    h = np.maximum(h, floor)
    far = np.maximum(d - 70.0, 0.0) ** 2 * 0.012          # caps let go inland: valleys open onto beaches, no flat boxes
    cap = (wb * (L.SEA + 0.6 + d * 0.052 + far) + wr * (L.SEA + 0.4 + d * 0.45 + far) + wc * (L.SEA + 0.6 + d * 3.0)
           + wq * (L.SEA + 2.3 + np.maximum(d - 2.0, 0.0) * 0.6 + far))
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


def erode_relief(h, land):
    """Erosion look for the range and hills: slope-following gullies and spurs (several branching octaves), then a
    thermal pass that sheds the steepest faces into scree. Returns the heights and maps: talus (scree laid down),
    gully (channel strength) and fan (concave ground at the foot of the slopes, where alluvium gathers)."""
    import relief as RL
    z0, z1 = np.searchsorted(AXIS, 120.0), np.searchsorted(AXIS, 730.0)
    x0, x1 = np.searchsorted(AXIS, -880.0), np.searchsorted(AXIS, 930.0)
    win = h[z0:z1, x0:x1].copy()
    Xw, Zw = X[z0:z1, x0:x1], Z[z0:z1, x0:x1]
    g = RL.gully_detail(win, Xw, Zw, L.CELL)
    g = np.where(land[z0:z1, x0:x1], g, win)
    win2, talus = RL.erode_thermal(g, L.CELL, talus_deg=41.0, iters=14, rate=0.4, mask=(g > L.SEA + 6.0))
    out = h.copy()
    out[z0:z1, x0:x1] = win2
    tal = np.zeros_like(h)
    tal[z0:z1, x0:x1] = talus
    gully = np.zeros_like(h)
    gully[z0:z1, x0:x1] = np.clip(ndimage.gaussian_filter(win, 2.0) - ndimage.gaussian_filter(win2, 2.0), 0, None)
    sm = ndimage.gaussian_filter(out, 6.0)
    fan = np.clip(ndimage.laplace(sm) * 0.8, 0, 1) * G.smoothstep(L.SEA + 2.0, L.SEA + 12.0, out) * G.smoothstep(80.0, 30.0, out)
    return out, dict(talus=tal, gully=gully, fan=fan)


# ------------------------------------------------------------------ carving

def fit_water_levels(nat):
    """River and lake levels that suit the terrain: the authored level is an upper bound, the water stays at least
    1.2 m below the lowest bank around it, and the river never flows uphill. Updates layout.RIVER / LAKES in place."""
    ctrl = np.array([(p[0], p[1]) for p in L.RIVER])
    line = G.catmull_rom(ctrl, spacing=2.0)
    low = ndimage.minimum_filter(ndimage.gaussian_filter(nat, 1.5), size=11)
    bank = sample(low, line[:, 0], line[:, 1])
    sl = G.arc_lengths(line)
    tree = cKDTree(line)
    s_ctrl = np.array([sl[tree.query(c)[1]] for c in ctrl])
    authored = np.interp(sl, s_ctrl, [p[2] for p in L.RIVER])
    level = np.minimum(authored, bank - 1.2)
    level = np.minimum.accumulate(ndimage.gaussian_filter1d(level, 6, mode='nearest'))
    level = np.maximum(level, L.SEA)
    new = []
    for (x, z, lv, w, d), si in zip(L.RIVER, s_ctrl):
        new.append((x, z, round(float(np.interp(si, sl, level)), 2), w, d))
    L.RIVER[:] = new
    for lake in L.LAKES:
        cx, cz = lake['center']
        R = lake['radius']
        r = np.hypot(X - cx, Z - cz)
        rim = (r > R) & (r < R + 14.0)
        if rim.any():
            lake['level'] = round(float(min(lake['level'], np.percentile(nat[rim], 5) - 0.6)), 2)
    return L.RIVER, L.LAKES


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


def _window(xs, zs, margin):
    x0, x1 = np.searchsorted(AXIS, xs.min() - margin), np.searchsorted(AXIS, xs.max() + margin)
    z0, z1 = np.searchsorted(AXIS, zs.min() - margin), np.searchsorted(AXIS, zs.max() + margin)
    return slice(max(0, z0), min(N, z1)), slice(max(0, x0), min(N, x1))


def carve_roads(h, net):
    """Grade every road into the terrain (v2 art direction). Each road gets a flat core (carriageway, sidewalks and
    shoulders) and smooth embankments that blend into the natural ground over 12 to 45 m: cut slopes about 1:1.3 and
    fill slopes about 1:1.8, widening with the height difference. Where roads are close (switchback legs, parallel
    streets), the target is a weighted blend of their heights, so the ground between them is one smooth slope, not a
    staircase of benches. Junction polygons are cores too, so no grass shows through them. Returns (heights, walls):
    walls are retaining walls along road edges where the graded slope would still be steeper than about 45 degrees."""
    out = h.copy()
    sw = np.zeros_like(h)          # sum of weights
    swy = np.zeros_like(h)         # sum of weights * road height
    amax = np.zeros_like(h)        # strongest influence
    core_y = np.full(h.shape, np.nan)
    core_d = np.full(h.shape, np.inf)
    WMAX = 46.0
    for s in net.segs:
        if s.y is None:
            continue
        sl = G.arc_lengths(s.pts)
        n = max(2, int(sl[-1] / 1.0) + 1)
        t = np.linspace(0, sl[-1], n)
        px, pz = np.interp(t, sl, s.pts[:, 0]), np.interp(t, sl, s.pts[:, 1])
        py = np.interp(t, sl, s.y)
        keep = np.ones(n, bool)
        if s.bridge:
            keep = (t < 6.0) | (t > sl[-1] - 6.0)          # abutments only; the deck spans the gap
        if s.underground is not None:
            ug = np.interp(t, sl, s.underground.astype(float)) > 0.5
            keep &= ~ug
        if keep.sum() < 2:
            continue
        px, pz, py = px[keep], pz[keep], py[keep]
        core = max(s.out_r, s.out_l) + 0.6
        # pieces of about 32 m: legs of one switchback (one segment) each count as their own road in the blend
        for a0 in range(0, len(px) - 1, 32):
            sl_ = slice(a0, min(len(px), a0 + 33))
            qx, qz, qy = px[sl_], pz[sl_], py[sl_]
            if len(qx) < 2:
                continue
            ws = _window(qx, qz, core + WMAX)
            Xw, Zw = X[ws], Z[ws]
            d, i = cKDTree(np.stack([qx, qz], axis=1)).query(np.stack([Xw.ravel(), Zw.ravel()], axis=1),
                                                             distance_upper_bound=core + WMAX)
            d = d.reshape(Xw.shape)
            ok = np.isfinite(d)
            i = np.where(ok.ravel(), i, 0).reshape(Xw.shape)
            ry = qy[i]
            diff = h[ws] - ry
            W = np.where(diff > 0, np.clip(12.0 + diff * 1.3, 12.0, WMAX), np.clip(12.0 - diff * 1.8, 12.0, WMAX))
            dd = np.where(ok, d, 1e9)
            alpha = np.where(ok, 1.0 - G.smoothstep(core, core + W, dd), 0.0)
            # inverse-distance (power 1) from the core edge: between two roads the ground ramps linearly from one
            # road's height to the other's (no step at the midpoint), and next to a road its own height dominates
            wgt = alpha / np.maximum(dd - core, 0.5)
            sw[ws] += wgt
            swy[ws] += wgt * ry
            amax[ws] = np.maximum(amax[ws], alpha)
            incore = ok & (d < core) & (d < core_d[ws])
            cy = core_y[ws]; cdd = core_d[ws]
            cy[incore] = ry[incore]; cdd[incore] = d[incore]
            core_y[ws] = cy; core_d[ws] = cdd
    # junction surfaces are cores at their own (interpolated) height
    for j in net.junctions:
        poly = np.array(j['polygon'])
        if len(poly) < 3:
            continue
        ws = _window(poly[:, 0], poly[:, 2], 4.0)
        img = Image.new('L', (ws[1].stop - ws[1].start, ws[0].stop - ws[0].start), 0)
        pts = [((x + L.DOMAIN) / L.CELL - ws[1].start, (z + L.DOMAIN) / L.CELL - ws[0].start) for x, _, z in poly]
        ImageDraw.Draw(img).polygon(pts, fill=255, outline=255)
        m = np.asarray(img) > 127
        m = ndimage.binary_dilation(m, iterations=1)
        cy = core_y[ws]; cdd = core_d[ws]
        yj = float(np.mean(poly[:, 1]))
        cy[m] = yj; cdd[m] = 0.0
        core_y[ws] = cy; core_d[ws] = cdd
        swin = sw[ws]; swyw = swy[ws]; aw = amax[ws]
        swin[m] += 2.0; swyw[m] += 2.0 * yj; aw[m] = 1.0
        sw[ws] = swin; swy[ws] = swyw; amax[ws] = aw
    infl = sw > 0
    Y = np.where(infl, swy / np.maximum(sw, 1e-12), out)
    out = np.where(infl, out + (Y - 0.12 - out) * amax, out)
    core = np.isfinite(core_y)
    out = np.where(core, core_y - 0.12, out)
    walls, out = retaining_walls(out, net)
    return out, walls, core_y


def retaining_walls(hc, net, max_slope=0.95):
    """Retaining walls along road edges where the graded ground beside the road is still steeper than about 45 degrees
    (cut: ground rising from the edge; fill: ground falling away). The wall takes up the excess height so the earth
    slope behind or below it stays at max_slope; the ground is stepped under the wall (hidden by its mesh). Each wall:
    segment id, side, kind, points [x, bottom y, z, top y] every 2 m. Returns (walls, adjusted heights)."""
    walls = []
    out = hc.copy()
    for s in net.segs:
        if s.y is None or s.bridge or (s.prof['kerb'] and s.kind in ('street', 'avenue', 'suburban')):
            continue                                        # town streets have kerbs and pads, not walls
        pts, ys, ug = net.trimmed(s)
        if len(pts) < 3:
            continue
        tan = G.tangents(pts)
        rn = G.right_normals(tan)
        for side, sgn, w in (('right', 1.0, s.out_r), ('left', -1.0, s.out_l)):
            prof = [sample(hc, (pts + rn * sgn * (w + o))[:, 0], (pts + rn * sgn * (w + o))[:, 1]) for o in (1.0, 4.0, 8.0)]
            h1, h4, h8 = prof
            run = []

            def flush():
                if len(run) >= 3:
                    walls.append(dict(segment=s.id, side=side, kind=max(set(r[4] for r in run), key=[r[4] for r in run].count),
                                      points=[r[:4] for r in run]))
            for k in range(len(pts)):
                if ug is not None and ug[k]:
                    flush(); run = []
                    continue
                rise = h8[k] - ys[k]
                excess = abs(rise) - max_slope * 7.0
                steep = max(abs(h4[k] - h1[k]) / 3.0, abs(h8[k] - h4[k]) / 4.0) > max_slope
                if excess > 1.0 and steep:
                    hw = float(np.clip(excess, 1.0, 8.0))
                    q = pts[k] + rn[k] * sgn * (w + 0.35)
                    if rise > 0:      # cut wall: from the road up to the stepped ground
                        run.append([round(float(q[0]), 2), round(float(ys[k] - 0.2), 2), round(float(q[1]), 2), round(float(ys[k] + hw), 2), 'cut'])
                    else:             # fill wall: holds the road edge above the falling ground (parapet 0.9 m)
                        run.append([round(float(q[0]), 2), round(float(ys[k] - hw), 2), round(float(q[1]), 2), round(float(ys[k] + 0.9), 2), 'fill'])
                else:
                    flush(); run = []
            flush()
    # step the ground under each wall: cut walls lift the ground behind them to the wall top, fill walls drop it
    for wall in walls:
        P = np.array(wall['points'])
        for x, yb, z, yt in P:
            ix, iz = int(round((x + L.DOMAIN) / L.CELL)), int(round((z + L.DOMAIN) / L.CELL))
            if 1 <= ix < N - 1 and 1 <= iz < N - 1:
                pass   # the graded slope already matches; the mesh hides the face (kept for clarity)
    return walls, out


def carve_pads(h, pads):
    """pads: list of dict(cx, cz, w, d, yaw, y). Oriented rectangles flattened to y - 0.12 with embankments."""
    out = h.copy()
    interiors = []
    for p in pads:
        a = math.radians(p['yaw'])
        c, s = math.cos(a), math.sin(a)
        r = max(p['w'], p['d']) / 2 + 50
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
        bank = np.where(dd > 0, np.clip(8.0 + dd * 1.3, 8.0, 40.0), np.clip(8.0 - dd * 1.8, 8.0, 45.0))
        blend = G.smoothstep(0.0, bank, dist)
        out[z0:z1, x0:x1] = np.where(dist < bank, tgt + (win - tgt) * blend, win)
        interiors.append(((z0, z1, x0, x1), dist < 0.0, tgt))
    for (z0, z1, x0, x1), inside, tgt in interiors:    # every pad's own floor stays exact, whatever banks came later
        win = out[z0:z1, x0:x1]
        out[z0:z1, x0:x1] = np.where(inside, tgt, win)
    return out


# ------------------------------------------------------------------ ground maps

def _polyline_mask(lines, width):
    """Rasterised polylines (island metres) of the given width, soft edges, 0..1."""
    img = Image.new('L', (N, N), 0)
    dr = ImageDraw.Draw(img)
    for pts in lines:
        q = [tuple(map(float, to_px(x, z))) for x, z in pts]
        if len(q) >= 2:
            dr.line(q, fill=255, width=max(1, int(round(width / L.CELL))), joint='curve')
    m = np.asarray(img, dtype=np.float64) / 255.0
    return ndimage.gaussian_filter(m, 0.6)


def ground_maps(h, land, coastw, inland, river, forest, maps=None):
    """Control weights (grass, rock, sand, dirt) and the painted macro tint + AO.

    v2 art direction: no uniform felt green. Meadow patches, darker grass, dry yellow-green on sunny convex slopes,
    greener damp gullies, dark forest floors, hedgerows round the fields, dirt tracks, scree under the rock faces,
    AO in the valleys. Snow and the wet-sand band come from height and aspect in the shader."""
    maps = maps or {}
    gz, gx = np.gradient(h, L.CELL)
    slope = np.hypot(gx, gz)
    nlen = np.sqrt(1.0 + gx * gx + gz * gz)
    face_n = -gz / nlen                                        # > 0 faces north, < 0 faces the (southern) sun
    lap = ndimage.laplace(ndimage.gaussian_filter(h, 3.0))     # > 0 concave (hollows), < 0 convex (ridges)
    # rock: steep faces, coast cliffs, exposed high ground
    # rock: cliffs and very steep faces, exposed convex crests and spurs, gully beds high up; steep smooth slopes stay
    # grassy (alpine meadows), so the range reads as grass and forest below, rock above
    hi = G.smoothstep(40.0, 120.0, h)
    r0 = 1.18 - 0.34 * hi                                      # grass holds on steeper slopes low down (alpine meadows)
    rock = G.smoothstep(r0, r0 + 0.35, slope)
    rock = np.maximum(rock, G.smoothstep(-0.02, -0.12, lap) * G.smoothstep(0.55, 0.9, slope) * (0.35 + 0.65 * hi))
    rock = np.maximum(rock, G.smoothstep(0.6, 2.4, maps.get('gully', np.zeros_like(h))) * hi * 0.7)
    rock = np.maximum(rock, coastw['cliff'] * G.smoothstep(0.35, 0.6, slope))
    rock = np.maximum(rock, G.smoothstep(128.0, 160.0, h) * G.smoothstep(0.28, 0.55, slope) * 0.8)
    # scree: what the thermal pass shed, and talus under the faces
    talus = ndimage.gaussian_filter(maps.get('talus', np.zeros_like(h)), 1.5)
    scree = G.smoothstep(0.03, 0.35, talus) * G.smoothstep(0.2, 0.45, slope) * (1 - rock)
    sand = coastw['beach'] * G.smoothstep(L.SEA + 4.6, L.SEA + 2.4, h) * (1 - G.smoothstep(80, 110, inland))
    sand = np.maximum(sand, (~land).astype(float))
    sand = np.maximum(sand, coastw['rock'] * G.smoothstep(L.SEA + 1.2, L.SEA + 0.2, h))
    line, level, hw, depth = river
    rd, ri = cKDTree(line).query(np.stack([X.ravel(), Z.ravel()], axis=1), distance_upper_bound=40)
    rd = rd.reshape(X.shape)
    ri = np.where(np.isfinite(rd.ravel()), ri, 0).reshape(X.shape)
    near = np.isfinite(rd) & (rd < hw[ri] + 3.0)
    dirt = np.where(near, 0.85, 0.0)                           # wet gravel banks
    dirt = np.maximum(dirt, scree * 0.9)
    fan = maps.get('fan', np.zeros_like(h))
    dirt = np.maximum(dirt, G.smoothstep(0.25, 0.8, fan) * 0.25 * (1 - forest))
    tracks = _polyline_mask([p for _, p, w in L.TRACKS], 3.0) if getattr(L, 'TRACKS', None) else np.zeros_like(h)
    dirt = np.maximum(dirt, tracks * 0.92 * (1 - rock))
    rock = rock * (1 - sand)
    dirt = dirt * (1 - sand) * (1 - rock * 0.8)
    grass = np.clip(1.0 - rock - sand - dirt, 0.0, 1.0)
    ctl = np.stack([grass, rock, sand, dirt], axis=-1)
    ctl = ndimage.gaussian_filter(ctl, (1.1, 1.1, 0))           # soft edges: no stair-steps on the 2 m grid
    ctl = ctl / np.maximum(ctl.sum(axis=-1, keepdims=True), 1e-6)
    # ---- macro tint (sRGB 0..1)
    lush = np.array([0.35, 0.52, 0.22])
    olive_hi = np.array([0.45, 0.50, 0.27])
    meadow = np.array([0.56, 0.64, 0.30])
    darkg = np.array([0.24, 0.40, 0.17])
    dry = np.array([0.67, 0.62, 0.35])
    damp = np.array([0.22, 0.42, 0.20])
    deep = np.array([0.18, 0.31, 0.15])
    grey = np.array([0.58, 0.56, 0.52])

    def mix(base, col, w):
        w = np.clip(w, 0, 1)[..., None]
        return base * (1 - w) + col * w
    tint = mix(np.broadcast_to(lush, h.shape + (3,)).copy(), olive_hi, G.smoothstep(18.0, 110.0, h))
    n1 = G.fbm(X / 95.0 + 3.1, Z / 95.0 - 7.3, octaves=4)
    n2 = G.fbm(X / 58.0 - 4.4, Z / 58.0 + 1.9, octaves=4)
    n3 = G.fbm(X / 24.0 + 9.0, Z / 24.0, octaves=3)
    tint = mix(tint, meadow, G.smoothstep(0.58, 0.72, n1) * 0.55)            # meadow patches
    tint = mix(tint, darkg, G.smoothstep(0.40, 0.28, n2) * 0.45)             # darker grass
    sunny = G.smoothstep(-0.05, -0.35, face_n) * G.smoothstep(0.12, 0.35, slope) * G.smoothstep(0.0, -0.06, lap)
    tint = mix(tint, dry, sunny * 0.55 + G.smoothstep(0.62, 0.8, n3) * 0.12)  # dry yellow-green on sunny ridges
    hollow = G.smoothstep(0.02, 0.12, lap) * G.smoothstep(L.SEA + 2.0, L.SEA + 8.0, h)
    gully = maps.get('gully', np.zeros_like(h))
    tint = mix(tint, damp, hollow * 0.45 + G.smoothstep(0.3, 2.0, gully) * 0.35)   # damp green hollows and gullies
    tint = mix(tint, deep, forest * 0.6)                                      # forest floor
    tint = tint + (n3[..., None] - 0.5) * 0.06
    tint = mix(tint, grey, scree * 0.85)
    # farm patchwork with hedgerows round each field
    crop = {'wheat': (0.86, 0.74, 0.36), 'corn': (0.52, 0.66, 0.24), 'sunflower': (0.90, 0.76, 0.22),
            'cabbage': (0.46, 0.66, 0.40), 'lavender': (0.62, 0.52, 0.78), 'pumpkin': (0.78, 0.56, 0.26)}
    hedge = np.zeros_like(h)
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
        edge = poly_mask(poly, 0.0)
        ring = np.clip(ndimage.gaussian_filter(edge, 1.2) * (1 - ndimage.binary_erosion(edge > 0.5, iterations=2)), 0, 1)
        hedge = np.maximum(hedge, ring)
    tint = mix(tint, np.array([0.20, 0.34, 0.14]), hedge * 0.8)
    town = np.zeros_like(h)
    for name, poly, target, edge in L.FLATS:
        if name in ('downtown', 'trenches', 'marina', 'boardwalk'):
            town = np.maximum(town, poly_mask(poly, 12.0))
    tint = mix(tint, np.array([0.40, 0.48, 0.28]) + (n2[..., None] - 0.5) * 0.08, town * 0.55)    # town verges: muted, worn grass
    for name, poly in L.PARKS:
        pm = poly_mask(poly, 4.0) * land
        tint = mix(tint, np.array([0.36, 0.60, 0.26]), pm * 0.5)
    tint = mix(tint, np.array([0.60, 0.50, 0.36]), tracks * 0.8)
    # ---- AO: cavities and valleys
    blur = ndimage.gaussian_filter(h, 9.0)
    valley = ndimage.gaussian_filter(h, 40.0)
    ao = 1.0 - np.maximum(blur - h, 0.0) * 0.05 - np.maximum(valley - h, 0.0) * 0.006 - forest * 0.08
    ao = np.clip(ao, 0.55, 1.0)
    macro = np.concatenate([np.clip(tint, 0, 1), ao[..., None]], axis=-1)
    return ctl, macro, slope


def forest_mask(h, land, slope, exclusions):
    """Where forests grow (0..1 density): foothills and mountain flanks up to the tree line, hill sides, never on
    cliffs, beaches, towns or flats. Patchy by low-frequency noise, so there are clearings and meadows."""
    n = G.fbm(X / 120.0 + 7, Z / 120.0 + 3, octaves=4)
    n2 = G.fbm(X / 40.0 - 3, Z / 40.0 + 9, octaves=3)
    line = 112.0 + (n2 - 0.5) * 30.0                            # ragged tree line
    m = G.smoothstep(3.0, 12.0, h) * G.smoothstep(line, line - 22.0, h) * G.smoothstep(1.2, 0.85, slope)
    m = m * G.smoothstep(0.34, 0.48, n)
    m = m * land * (1 - exclusions)
    return np.clip(m, 0, 1)


def resize(field, size):
    """Resample a grid field to size x size texels covering the same domain (texel centres)."""
    t = (np.arange(size) + 0.5) / size * (N - 1)
    zz, xx = np.meshgrid(t, t, indexing='ij')
    if field.ndim == 2:
        return ndimage.map_coordinates(field, [zz, xx], order=1, mode='nearest')
    return np.stack([ndimage.map_coordinates(field[..., k], [zz, xx], order=1, mode='nearest') for k in range(field.shape[-1])], axis=-1)
