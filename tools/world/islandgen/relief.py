"""Mountain relief for CT Island v2: one connected range, then hydraulic and thermal erosion.

The range is a crest line (layout.RANGE) with a height profile. The cross-section is concave, so it is steep near the
crest and has long gentle aprons. Domain-warped ridged multifractal noise is laid out in crest-aligned coordinates, so
its ridges run downhill and become spurs, with saddles between the peaks. One dominant summit pyramid sits on the crest
(layout.SUMMIT) with a bench on its south shoulder for the basecamp lodge (layout.SHOULDER). Two erosion passes follow:
  - hydraulic: many water droplets carry sediment downhill. They cut gullies and valleys and drop alluvial fans in
    the foothills (coarse 4 m pass, then a fine 2 m pass)
  - thermal: material slides off slopes steeper than the talus angle and gathers as scree under rock faces
The deposition, flow and talus maps feed the ground colours (scree, dark wet gullies, fans).
"""
import math

import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree

import geom as G
import layout as L

# ------------------------------------------------------------------ gradient noise (Perlin), vectorised

_GRAD = None


def _grad_table(seed=7):
    global _GRAD
    if _GRAD is None:
        rng = np.random.default_rng(seed)
        ang = rng.uniform(0, 2 * np.pi, 1024)
        _GRAD = (np.cos(ang), np.sin(ang), rng.permutation(1024).astype(np.int64))
    return _GRAD


def perlin(x, z, seed=0):
    """2D gradient noise, roughly in [-1, 1], unit lattice."""
    gx, gz, perm = _grad_table()
    x = np.asarray(x, dtype=np.float64) + seed * 17.13
    z = np.asarray(z, dtype=np.float64) - seed * 9.71
    ix, iz = np.floor(x).astype(np.int64), np.floor(z).astype(np.int64)
    fx, fz = x - ix, z - iz

    def g(i, j, dx, dz):
        h = perm[(perm[i & 1023] + j) & 1023]
        return gx[h] * dx + gz[h] * dz

    ux = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
    uz = fz * fz * fz * (fz * (fz * 6 - 15) + 10)
    n00 = g(ix, iz, fx, fz)
    n10 = g(ix + 1, iz, fx - 1, fz)
    n01 = g(ix, iz + 1, fx, fz - 1)
    n11 = g(ix + 1, iz + 1, fx - 1, fz - 1)
    a = n00 + (n10 - n00) * ux
    b = n01 + (n11 - n01) * ux
    return (a + (b - a) * uz) * 1.41


def fbm(x, z, octaves=5, lac=2.0, gain=0.5, seed=0):
    s = np.zeros(np.broadcast(x, z).shape)
    amp, freq, norm = 1.0, 1.0, 0.0
    for i in range(octaves):
        s += perlin(x * freq, z * freq, seed + i * 3) * amp
        norm += amp
        amp *= gain
        freq *= lac
    return s / norm


def ridged_mf(x, z, octaves=6, lac=2.0, gain=0.52, offset=1.0, sharp=2.0, seed=0):
    """Musgrave's ridged multifractal in [0, 1]: sharp crests where the noise crosses zero, octaves weighted by the
    previous octave, so detail gathers on the ridges and the valleys stay smooth."""
    s = np.zeros(np.broadcast(x, z).shape)
    weight = np.ones_like(s)
    amp, freq, norm = 1.0, 1.0, 0.0
    for i in range(octaves):
        n = offset - np.abs(perlin(x * freq, z * freq, seed + 11 * i))
        n = np.clip(n, 0.0, None) ** sharp
        n *= weight
        weight = np.clip(n * 1.8, 0.0, 1.0)
        s += n * amp
        norm += amp
        amp *= gain
        freq *= lac
    return s / norm


# ------------------------------------------------------------------ crest-line coordinates

def crest_frame(X, Z, ctrl):
    """Along-crest distance s, signed distance d (positive = south side) and the crest height at s, for every grid
    point (nearest point on the densified crest polyline)."""
    pts = np.array([(p[0], p[1]) for p in ctrl], dtype=np.float64)
    hs = np.array([p[2] for p in ctrl], dtype=np.float64)
    line = G.catmull_rom(pts, spacing=0.5)          # dense: nearest-sample distance has no scallops
    sl = G.arc_lengths(line)
    s_ctrl = np.array([sl[cKDTree(line).query(c)[1]] for c in pts])
    crest_h = np.interp(sl, s_ctrl, hs)
    # smooth the crest height (no kinks at control points) but keep the summit peak exact
    crest_h = ndimage.gaussian_filter1d(crest_h, 48, mode="nearest")
    tan = G.tangents(line)
    nrm = G.right_normals(tan)          # right of travel west -> east = south
    d, i = cKDTree(line).query(np.stack([X.ravel(), Z.ravel()], axis=1))
    i = i.reshape(X.shape)
    d = d.reshape(X.shape)
    side = np.sign((X - line[i, 0]) * nrm[i, 0] + (Z - line[i, 1]) * nrm[i, 1])
    return sl[i], d * side, crest_h[i], line, crest_h


def _smooth_profile(u, k=1.25):
    """Ridge cross-section for u = 0 (crest) .. 1 (foot): rounded crest, steep flanks, long concave apron."""
    u = np.clip(u, 0.0, 1.0)
    return (1.0 - u * u * (3.0 - 2.0 * u)) ** k


def mountain_range_v1(X, Z):
    """(Superseded by ridge_network) crest envelope with noise spurs and a summit pyramid."""
    s, d, ch, line, crest_h = crest_frame(X, Z, L.RANGE)
    south = d >= 0
    # half widths: wide gentle south aprons, shorter steeper north faces down to the coast plateau
    w_s = 205.0 + 70.0 * G.smoothstep(30.0, 140.0, ch)
    w_n = 128.0 + 26.0 * G.smoothstep(30.0, 140.0, ch)
    w = np.where(south, w_s, w_n)
    # domain warp in crest coordinates: the crest meanders and the spurs bend
    wd = fbm(s / 260.0, d / 260.0, 4, seed=3) * 42.0
    ws = fbm(s / 200.0 + 4.1, d / 200.0 - 2.3, 4, seed=5) * 70.0
    prof = _smooth_profile(np.abs(d + wd) / w, 1.6)
    rng_h = (ch - L.SEA) * prof
    # the dominant summit: a three-faced pyramid standing on the crest
    sx, sz, sy, sr = L.SUMMIT
    dx, dz = X - sx, Z - sz
    r = np.hypot(dx, dz)
    ang = np.arctan2(dz, dx)
    facet = 1.0 + 0.14 * np.cos(3.0 * (ang - 0.4)) + 0.10 * fbm(np.cos(ang) * 1.7 + 3, np.sin(ang) * 1.7, 3, seed=13)
    pyr_h = (sy - L.SEA) * _smooth_profile(r * facet / sr, 1.45)
    kk = 0.12
    env = np.log(np.exp(kk * rng_h) + np.exp(kk * pyr_h)) / kk
    env = np.maximum(env, 0.0)
    # spurs and secondary ridges: ridged noise stretched across the crest, so its ridges run downhill
    r1 = ridged_mf((s + ws) / 190.0, (d + wd) / 300.0, 4, sharp=1.6, seed=1)
    r2 = ridged_mf((s + ws) / 85.0 + 7.7, (d + wd) / 125.0 - 3.3, 3, sharp=1.4, seed=9)
    near = env > 4.0
    r1 = r1 - float(np.mean(r1[near])) if near.any() else r1
    r2 = r2 - float(np.mean(r2[near])) if near.any() else r2
    var = 1.0 + r1 * 0.5 + r2 * 0.14
    h = env * np.clip(var, 0.45, 1.6)
    # keep the summit exactly where the brief puts it
    j = (int(np.argmin(np.abs(Z[:, 0] - sz))), int(np.argmin(np.abs(X[0] - sx))))
    peak = float(h[max(0, j[0] - 6):j[0] + 7, max(0, j[1] - 6):j[1] + 7].max())
    h = h * ((sy - L.SEA) / max(peak, 1.0))
    h = L.SEA + h
    weight = np.clip(prof * 1.4, 0.0, 1.0) * (h > L.SEA + 2.0)
    return h, weight, (s, d, line, crest_h)


# ------------------------------------------------------------------ hydraulic erosion (droplets)

def _brush(radius):
    r = int(math.ceil(radius))
    oy, ox = np.mgrid[-r:r + 1, -r:r + 1]
    dist = np.hypot(ox, oy)
    w = np.clip(radius - dist, 0.0, None)
    keep = w > 0
    w = w[keep]
    return ox[keep].astype(np.int64), oy[keep].astype(np.int64), w / w.sum()


def erode_hydraulic(h, cell, droplets, seed=1, spawn_mask=None, radius=3.0, steps=64, batch=60000,
                    inertia=0.06, capacity=5.0, min_capacity=0.002, erode=0.32, deposit=0.25, evaporate=0.015,
                    gravity=9.0, max_erode=None, height_scale=None):
    """Droplet erosion on a grid (rows = z). Returns (eroded h, deposition map, flow map). Heights are scaled by
    `height_scale` so the parameters behave the same at any cell size (h / cell is the slope per cell)."""
    rng = np.random.default_rng(seed)
    H, W = h.shape
    hs = height_scale or cell
    t = (h / hs).astype(np.float64).copy()
    depo = np.zeros(H * W)
    flow = np.zeros(H * W)
    bx, by, bw = _brush(radius)
    if spawn_mask is None:
        spawn_mask = np.ones_like(t, bool)
    cand = np.flatnonzero(spawn_mask[1:-1, 1:-1].ravel())
    if len(cand) == 0:
        return h, depo.reshape(H, W), flow.reshape(H, W)
    inner_w = W - 2
    done = 0
    while done < droplets:
        n = min(batch, droplets - done)
        done += n
        pick = cand[rng.integers(0, len(cand), n)]
        py = (pick // inner_w + 1).astype(np.float64) + rng.random(n)
        px = (pick % inner_w + 1).astype(np.float64) + rng.random(n)
        px = np.minimum(px, W - 2.001)
        py = np.minimum(py, H - 2.001)
        dx = np.zeros(n)
        dy = np.zeros(n)
        speed = np.ones(n)
        water = np.ones(n)
        sed = np.zeros(n)
        alive = np.ones(n, bool)
        tf = t.ravel()
        for _ in range(steps):
            idx = np.flatnonzero(alive)
            if len(idx) == 0:
                break
            x, y = px[idx], py[idx]
            ix, iy = x.astype(np.int64), y.astype(np.int64)
            fx, fy = x - ix, y - iy
            c00 = iy * W + ix
            h00, h10, h01, h11 = tf[c00], tf[c00 + 1], tf[c00 + W], tf[c00 + W + 1]
            gxv = (h10 - h00) * (1 - fy) + (h11 - h01) * fy
            gyv = (h01 - h00) * (1 - fx) + (h11 - h10) * fx
            hcur = h00 * (1 - fx) * (1 - fy) + h10 * fx * (1 - fy) + h01 * (1 - fx) * fy + h11 * fx * fy
            ndx = dx[idx] * inertia - gxv * (1 - inertia)
            ndy = dy[idx] * inertia - gyv * (1 - inertia)
            ln = np.hypot(ndx, ndy)
            still = ln < 1e-9
            if still.any():
                a = rng.uniform(0, 2 * np.pi, int(still.sum()))
                ndx[still], ndy[still], ln[still] = np.cos(a), np.sin(a), 1.0
            ndx /= ln
            ndy /= ln
            nx, ny = x + ndx, y + ndy
            inside = (nx >= 1) & (nx < W - 2) & (ny >= 1) & (ny < H - 2)
            nxc = np.clip(nx, 1, W - 2.001)
            nyc = np.clip(ny, 1, H - 2.001)
            jx, jy = nxc.astype(np.int64), nyc.astype(np.int64)
            gx2, gy2 = nxc - jx, nyc - jy
            d00 = jy * W + jx
            hnew = (tf[d00] * (1 - gx2) * (1 - gy2) + tf[d00 + 1] * gx2 * (1 - gy2) + tf[d00 + W] * (1 - gx2) * gy2
                    + tf[d00 + W + 1] * gx2 * gy2)
            dh = hnew - hcur
            s_ = sed[idx]
            cap = np.maximum(-dh * speed[idx] * water[idx] * capacity, min_capacity)
            dep_mask = (s_ > cap) | (dh > 0)
            # deposition (bilinear onto the 4 corners of the old cell)
            amt_d = np.where(dh > 0, np.minimum(dh, s_), (s_ - cap) * deposit)
            amt_d = np.where(dep_mask, amt_d, 0.0)
            # erosion (brush around the old position), never deeper than the step it just fell
            amt_e = np.where(dep_mask, 0.0, np.minimum((cap - s_) * erode, -dh))
            if max_erode is not None:
                amt_e = np.minimum(amt_e, max_erode)
            sed[idx] = s_ - amt_d + amt_e
            delta = np.zeros(H * W)
            if (amt_d > 0).any():
                # deposits spread over the brush too (point deposits leave pebbly lumps)
                sel = amt_d > 0
                cx = np.clip(ix[sel][:, None] + bx[None, :], 0, W - 1)
                cy = np.clip(iy[sel][:, None] + by[None, :], 0, H - 1)
                dd = np.bincount((cy * W + cx).ravel(), weights=(amt_d[sel][:, None] * bw[None, :]).ravel(), minlength=H * W)
                delta += dd
                depo += dd
            if (amt_e > 0).any():
                sel = amt_e > 0
                cx = np.clip(ix[sel][:, None] + bx[None, :], 0, W - 1)
                cy = np.clip(iy[sel][:, None] + by[None, :], 0, H - 1)
                ee = amt_e[sel][:, None] * bw[None, :]
                delta -= np.bincount((cy * W + cx).ravel(), weights=ee.ravel(), minlength=H * W)
            tf = tf + delta
            flow += np.bincount(c00, weights=water[idx], minlength=H * W)
            sp2 = speed[idx] ** 2 - dh * gravity
            speed[idx] = np.sqrt(np.maximum(sp2, 0.0))
            water[idx] = water[idx] * (1 - evaporate)
            px[idx], py[idx] = nxc, nyc
            dx[idx], dy[idx] = ndx, ndy
            alive[idx] = inside & (water[idx] > 0.05)
        t = tf.reshape(H, W)
    return t * hs, depo.reshape(H, W) * hs, flow.reshape(H, W)


def erode_thermal(h, cell, talus_deg=38.0, iters=40, rate=0.45, mask=None):
    """Material slides from cells steeper than the talus angle to their lower neighbours (8-neighbourhood).
    Returns (h, talus deposit)."""
    t = np.tan(math.radians(talus_deg)) * cell
    out = h.astype(np.float64).copy()
    acc = np.zeros_like(out)
    offs = [(0, 1, 1.0), (0, -1, 1.0), (1, 0, 1.0), (-1, 0, 1.0),
            (1, 1, 1.4142), (1, -1, 1.4142), (-1, 1, 1.4142), (-1, -1, 1.4142)]
    m = np.ones_like(out) if mask is None else mask.astype(np.float64)
    for _ in range(iters):
        pad = np.pad(out, 1, mode='edge')
        H, W = out.shape
        diffs = []
        for dy, dx, dist in offs:
            nb = pad[1 + dy:1 + dy + H, 1 + dx:1 + dx + W]
            diffs.append(np.maximum(out - nb - t * dist, 0.0))
        tot = sum(diffs)
        dmax = np.max(diffs, axis=0)
        move = np.where(tot > 0, rate * dmax * 0.5, 0.0) * m
        frac = [np.where(tot > 0, dd / np.maximum(tot, 1e-12), 0.0) * move for dd in diffs]
        out -= move
        for (dy, dx, _), f in zip(offs, frac):
            # what leaves (y, x) towards (y+dy, x+dx) arrives there
            shifted = np.zeros((H + 2, W + 2))
            shifted[1 + dy:1 + dy + H, 1 + dx:1 + dx + W] = f
            arr = shifted[1:-1, 1:-1]
            out += arr
            acc += arr
    return out, acc


def summit_crags(h, X, Z):
    """W3 art direction: a craggy summit silhouette. Above ~110 m the smooth eroded cone gets sharp ridged crags
    (towers and notches, ~40 m apart) and smaller pinnacles, scaled up with height; the cairn top stays walkable."""
    sx, sz, sy, _ = L.SUMMIT
    w = G.smoothstep(104.0, 150.0, h)
    if not (w > 0).any():
        return h
    r = np.hypot(X - sx, Z - sz)
    w = w * G.smoothstep(9.0, 26.0, r)                      # a level-ish top round the cairn
    big = ridged_mf(X / 42.0 + 1.3, Z / 42.0 - 2.1, 3, sharp=2.6, seed=71)
    pin = ridged_mf(X / 17.0 - 4.2, Z / 17.0 + 0.7, 2, sharp=3.2, seed=73)
    on = w > 0.05
    big = big - float(np.mean(big[on]))
    pin = pin - float(np.mean(pin[on]))
    return h + (big * 16.0 + pin * 5.0) * w


def shoulder_bench(h, X, Z):
    """A natural bench on the summit's south shoulder for the lodge: the slope eases to 30 % of its own gradient over
    a small shelf (no flat disc with cliffs round it), blended well into the slopes around."""
    cx, cz, y, rx, rz, yaw = L.SHOULDER
    a = math.radians(yaw)
    lx = (X - cx) * math.cos(a) - (Z - cz) * math.sin(a)
    lz = (X - cx) * math.sin(a) + (Z - cz) * math.cos(a)
    e = np.sqrt((lx / rx) ** 2 + (lz / rz) ** 2)
    core = e < 1.0
    if not core.any():
        return h
    sm = ndimage.gaussian_filter(h, 8.0)
    y0 = float(np.mean(sm[core]))
    # plane fitted to the smoothed slope in the core, then flattened to 30 % of its gradient
    A = np.stack([X[core] - cx, Z[core] - cz, np.ones(int(core.sum()))], axis=1)
    coef, *_ = np.linalg.lstsq(A, sm[core], rcond=None)
    plane = y0 + 0.3 * (coef[0] * (X - cx) + coef[1] * (Z - cz))
    wgt = G.smoothstep(1.5, 0.75, e)
    return h + (plane - h) * wgt


# ------------------------------------------------------------------ fluvial erosion (stream power, implicit)

_NB = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]


def _receivers(h, active, cell):
    """Steepest-descent (D8) receiver of every cell; pits and fixed cells drain to themselves."""
    H, W = h.shape
    pad = np.pad(h, 1, mode='edge')
    best = np.zeros((H, W))
    rcv = np.arange(H * W).reshape(H, W)
    dist = np.zeros((H, W))
    ii, jj = np.mgrid[0:H, 0:W]
    for dy, dx in _NB:
        nb = pad[1 + dy:1 + dy + H, 1 + dx:1 + dx + W]
        dd = cell * (1.4142135 if dy and dx else 1.0)
        s = (h - nb) / dd
        better = (s > best) & active
        ty = np.clip(ii + dy, 0, H - 1)
        tx = np.clip(jj + dx, 0, W - 1)
        best = np.where(better, s, best)
        rcv = np.where(better, ty * W + tx, rcv)
        dist = np.where(better, dd, dist)
    return rcv.ravel(), dist.ravel()


def erode_fluvial(h, cell, active, iters=120, dt=1.0, K=0.0012, m=0.45, uplift=None, diffusion=0.012, talus_deg=None,
                  kmap=None, seed=3):
    """Stream-power fluvial erosion (Braun & Willett 2013 implicit scheme, n = 1) with linear hillslope diffusion.
    Valleys cut back from the base level (sea, window edge) into the range; ridges with little drainage area keep
    their height. `active` marks cells that may change. Returns (h, drainage area in m2)."""
    from scipy import sparse
    from scipy.sparse.linalg import spsolve
    H, W = h.shape
    n = H * W
    out = h.astype(np.float64).copy()
    act = active.ravel()
    idx = np.arange(n)
    U = np.zeros(n) if uplift is None else uplift.ravel() * dt
    area_cell = cell * cell
    A = np.full(n, area_cell)
    rng = np.random.default_rng(seed)
    Kc = np.full(n, K) if kmap is None else (kmap.ravel() * K)
    for it in range(iters):
        jit = out + rng.uniform(-0.04, 0.04, out.shape)        # break D8 ties so channels meander
        rcv, dist = _receivers(jit, active, cell)
        # drainage area: A = a + sum over donors -> (I - P^T) A = a
        donors = idx[(rcv != idx)]
        P = sparse.csr_matrix((np.ones(len(donors)), (rcv[donors], donors)), shape=(n, n))
        A = spsolve((sparse.identity(n, format='csr') - P).tocsc(), np.full(n, area_cell))
        # implicit stream power: (1 + F) h_i - F h_rcv = h_old + U
        F = np.zeros(n)
        moving = act & (rcv != idx)
        F[moving] = Kc[moving] * dt * np.power(A[moving], m) / dist[moving]
        M = sparse.csr_matrix((np.concatenate([1.0 + F, -F[moving]]),
                               (np.concatenate([idx, idx[moving]]), np.concatenate([idx, rcv[moving]]))), shape=(n, n))
        flat = out.ravel() + np.where(act, U, 0.0)
        new = spsolve(M.tocsc(), flat)
        new = np.where(act, new, out.ravel())
        out = new.reshape(H, W)
        if diffusion > 0:
            lap = ndimage.laplace(out) / (cell * cell)
            out = np.where(active, out + diffusion * dt * lap * cell * cell * 0.25, out)
        if talus_deg is not None and it % 10 == 9:
            out, _ = erode_thermal(out, cell, talus_deg=talus_deg, iters=4, rate=0.5, mask=active)
    return out, A.reshape(H, W)


# ------------------------------------------------------------------ ridge network (the v2 range)

def _densify(pts, spacing=3.0):
    pts = np.asarray(pts, dtype=np.float64)
    return G.catmull_rom(pts, spacing=spacing) if len(pts) > 2 else G.resample(pts, spacing)


def ridge_network(seed=17):
    """The range as a tree of crest lines: the main crest (layout.RANGE), spurs branching down both flanks and the
    summit's own ridges (one runs south to the lodge shoulder). Each ridge: dense points, height per point, half width
    per point. Ridges meet in saddles and junction peaks, so no peak is a lone cone."""
    rng = np.random.default_rng(seed)
    ridges = []
    pts = np.array([(p[0], p[1]) for p in L.RANGE], dtype=np.float64)
    line = _densify(pts, 3.0)
    sl = G.arc_lengths(line)
    tree = cKDTree(line)
    s_ctrl = np.array([sl[tree.query(c)[1]] for c in pts])
    ch = np.interp(sl, s_ctrl, [p[2] for p in L.RANGE])
    sx, sz, sy, _ = L.SUMMIT
    ds = np.hypot(line[:, 0] - sx, line[:, 1] - sz)
    ch = np.maximum(ch, sy - ds * 0.62)                 # the summit: crest rises steeply to the peak
    ch = ndimage.gaussian_filter1d(ch, 5, mode='nearest')
    ch = np.maximum(ch, sy - ds * 0.62 - 4.0)
    tan = G.tangents(line)
    south = G.right_normals(tan)                          # crest runs west -> east, so right = south
    ridges.append(dict(pts=line, h=ch, w=np.full(len(line), 125.0), kind='crest'))
    # spurs off the main crest
    s = 40.0 + rng.uniform(0, 40)
    while s < sl[-1] - 40:
        k = int(np.searchsorted(sl, s))
        root, rh = line[k], ch[k]
        if np.hypot(root[0] - sx, root[1] - sz) > 70:     # the summit has its own ridges
            for side, prob in ((1.0, 0.95), (-1.0, 0.55)):
                if rng.random() > prob:
                    continue
                ang = math.radians(rng.uniform(-38, 38))
                n = south[k] * side
                d = np.array([n[0] * math.cos(ang) - n[1] * math.sin(ang), n[0] * math.sin(ang) + n[1] * math.cos(ang)])
                length = rng.uniform(170, 280) if side > 0 else rng.uniform(70, 130)
                bend = rng.uniform(-0.5, 0.5)
                t = np.linspace(0, 1, 9)
                perp = np.array([-d[1], d[0]])
                path = root[None, :] + d[None, :] * (t[:, None] * length) + perp[None, :] * (bend * length * 0.25 * np.sin(np.pi * t))[:, None]
                dense = _densify(path, 3.0)
                tt = G.arc_lengths(dense) / max(G.arc_lengths(dense)[-1], 1e-6)
                tip = rng.uniform(8, 22) if side > 0 else rng.uniform(16, 30)
                hh = tip + (rh * rng.uniform(0.82, 0.94) - tip) * (1.0 - tt) ** rng.uniform(1.15, 1.6)
                w = (rng.uniform(70, 100) if side > 0 else rng.uniform(55, 75)) * (1.0 - 0.45 * tt)
                ridges.append(dict(pts=dense, h=hh, w=w, kind='spur'))
                # a secondary branch on long south spurs
                if side > 0 and length > 220 and rng.random() < 0.7:
                    j = int(len(dense) * rng.uniform(0.35, 0.55))
                    a2 = math.radians(rng.choice([-1, 1]) * rng.uniform(35, 60))
                    dv = dense[min(j + 2, len(dense) - 1)] - dense[j]
                    dv = dv / max(np.linalg.norm(dv), 1e-9)
                    d2 = np.array([dv[0] * math.cos(a2) - dv[1] * math.sin(a2), dv[0] * math.sin(a2) + dv[1] * math.cos(a2)])
                    l2 = rng.uniform(80, 130)
                    p2 = dense[j][None, :] + d2[None, :] * np.linspace(0, l2, 6)[:, None]
                    dd2 = _densify(p2, 3.0)
                    t2 = G.arc_lengths(dd2) / max(G.arc_lengths(dd2)[-1], 1e-6)
                    h0 = hh[j] * 0.9
                    ridges.append(dict(pts=dd2, h=8 + (h0 - 8) * (1 - t2) ** 1.3, w=55.0 * (1 - 0.4 * t2), kind='branch'))
        s += rng.uniform(95, 150)
    # the summit's ridges: south to the lodge shoulder and on down, north towards the coast, and a south-east arete
    shx, shz, shy = L.SHOULDER[0], L.SHOULDER[1], L.SHOULDER[2]
    for path, hs, w in (
        ([(sx, sz), (-196, 470), (-214, 432), (shx, shz), (-236, 360), (-246, 300)], [sy, 140, 104, shy, 46, 18], 80.0),
        ([(sx, sz), (-168, 566), (-156, 618)], [sy, 92, 40], 70.0),
        ([(sx, sz), (-136, 470), (-104, 420), (-84, 370)], [sy, 128, 74, 30], 75.0),
    ):
        dense = _densify(path, 3.0)
        ctl = np.array(path, dtype=np.float64)
        sl2 = G.arc_lengths(dense)
        tr = cKDTree(dense)
        sc = [sl2[tr.query(c)[1]] for c in ctl]
        ridges.append(dict(pts=dense, h=np.interp(sl2, sc, hs), w=np.full(len(dense), w), kind='summit'))
    return ridges


def network_height(X, Z, ridges, k=0.16, warp=22.0):
    """Soft maximum over the ridges of height * cross-profile(distance / half width): valleys where neighbouring
    profiles are low, saddles where crests meet."""
    wx = fbm(X / 180.0 + 2.2, Z / 180.0, 4, seed=71) * warp
    wz = fbm(X / 180.0, Z / 180.0 - 5.1, 4, seed=73) * warp
    qx, qz = X + wx, Z + wz
    acc = np.zeros(X.shape)
    hmax = np.zeros(X.shape)
    for r in ridges:
        pts, hh, w = r['pts'], r['h'], r['w']
        tree = cKDTree(pts)
        x0, x1 = pts[:, 0].min() - w.max() - 40, pts[:, 0].max() + w.max() + 40
        z0, z1 = pts[:, 1].min() - w.max() - 40, pts[:, 1].max() + w.max() + 40
        sel = (qx > x0) & (qx < x1) & (qz > z0) & (qz < z1)
        if not sel.any():
            continue
        d, i = tree.query(np.stack([qx[sel], qz[sel]], axis=1))
        u = d / w[i]
        prof = (1.0 - np.clip(u, 0, 1) ** 1.0) ** 1.85
        val = (hh[i] - L.SEA) * prof
        acc[sel] += np.exp(k * val)
        hmax[sel] = np.maximum(hmax[sel], val)
    h = np.where(acc > 0, np.log(np.maximum(acc, 1e-30)) / k, 0.0)
    h = np.maximum(h, hmax)                       # the soft max never sits below the highest single ridge
    # surface texture: low-amplitude ridged noise, more on the high faces
    rel = np.clip(h / 150.0, 0, 1)
    tex = (ridged_mf(qx / 60.0, qz / 60.0, 4, sharp=1.5, seed=77) - 0.35) * (4.0 + 10.0 * rel)
    return L.SEA + np.maximum(h + tex * (h > 3.0), 0.0)


def range_envelope(X, Z):
    """Broad uneroded range: the crest profile (wide concave south aprons, shorter north faces), the summit as the
    crest's high point with its own ridges (south to the lodge shoulder, south-east arete, north rib), and
    low-frequency ridged variation that seeds the valleys. Fluvial erosion then cuts the real valleys."""
    s, d, ch, line, crest_h = crest_frame(X, Z, L.RANGE)
    sx, sz, sy, _ = L.SUMMIT
    # the summit rises along the crest (a high ridge section, not a radial cone)
    tree = cKDTree(line)
    k0 = tree.query((sx, sz))[1]
    sl = G.arc_lengths(line)
    s_sum = sl[k0]
    crest_peak = sy - np.abs(s - s_sum) * 0.58
    ch2 = np.maximum(ch, crest_peak)
    wd = fbm(s / 260.0, d / 260.0, 4, seed=3) * 38.0
    ws = fbm(s / 200.0 + 4.1, d / 200.0 - 2.3, 4, seed=5) * 60.0
    dw = d + wd
    # half width blends across the (warped) crest, so the two flanks meet without a seam
    w_s = 240.0 + 50.0 * G.smoothstep(30.0, 140.0, ch2)
    w_n = 138.0 + 18.0 * G.smoothstep(30.0, 140.0, ch2)
    w = w_n + (w_s - w_n) * G.smoothstep(-25.0, 25.0, dw)
    u = np.abs(dw) / w
    env = (ch2 - L.SEA) * _smooth_profile(u, 1.55)
    # summit ridges as broad ridges (soft max)
    shx, shz, shy = L.SHOULDER[0], L.SHOULDER[1], L.SHOULDER[2]
    kk = 0.09
    acc = np.exp(kk * env)
    for path, hs, wr in (
        ([(sx, sz), (-198, 470), (-214, 442), (-232, 418), (shx, shz), (-258, 376), (-262, 340), (-266, 300)],
         [sy, 138, 108, 84, shy + 2, 60, 40, 18], 100.0),
        ([(sx, sz), (-140, 468), (-108, 420), (-88, 366)], [sy, 126, 74, 26], 95.0),
        ([(sx, sz), (-170, 568), (-160, 620)], [sy, 96, 36], 85.0),
    ):
        dense = _densify(path, 0.5)
        ctl = np.array(path, dtype=np.float64)
        sl2 = G.arc_lengths(dense)
        tr = cKDTree(dense)
        sc = [sl2[tr.query(c)[1]] for c in ctl]
        hh = np.interp(sl2, sc, hs)
        dd, ii = tr.query(np.stack([X.ravel(), Z.ravel()], axis=1), distance_upper_bound=wr * 1.2)
        dd = dd.reshape(X.shape)
        ii = np.where(np.isfinite(dd.ravel()), ii, 0).reshape(X.shape)
        ok = np.isfinite(dd)
        val = np.where(ok, (hh[ii] - L.SEA) * _smooth_profile(np.where(ok, dd, 1e9) / wr, 1.4), 0.0)
        acc += np.exp(kk * val)
    env = np.log(acc) / kk
    env = np.maximum(env, 0.0)
    # valley seeds: ridged variation stretched across the crest (ridges run downhill)
    r1 = ridged_mf((s + ws) / 170.0, (d + wd) / 280.0, 4, sharp=1.5, seed=1)
    near = env > 4.0
    r1 = r1 - float(np.mean(r1[near])) if near.any() else r1
    h = env * np.clip(1.0 + r1 * 0.42, 0.6, 1.4)
    # keep the summit height
    j = (int(np.argmin(np.abs(Z[:, 0] - sz))), int(np.argmin(np.abs(X[0] - sx))))
    peak = float(h[max(0, j[0] - 8):j[0] + 9, max(0, j[1] - 8):j[1] + 9].max())
    h = h * ((sy - L.SEA) / max(peak, 1.0))
    return L.SEA + h, (s, d, line, crest_h)


def mountain_range(X, Z):
    """Uneroded range height (y metres, >= SEA), a 0..1 mountain weight and the crest frame."""
    h, frame = range_envelope(X, Z)
    weight = G.smoothstep(L.SEA + 6.0, L.SEA + 40.0, h)
    return h, weight, frame


# ------------------------------------------------------------------ gully detail (slope-aligned erosion noise)

def _hash2(ix, iz, seed):
    a = (ix * 374761393 + iz * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    a = ((a ^ (a >> 13)) * 1274126177) & 0xFFFFFFFF
    b = ((a ^ (a >> 16)) * 2246822519) & 0xFFFFFFFF
    return (a & 0xFFFF) / 65535.0, ((b >> 8) & 0xFFFF) / 65535.0


def gully_octave(px, pz, dirx, dirz, seed=0):
    """Stripes that run along (dirx, dirz) on a jittered lattice, blended with gaussian weights (Fewes' erosion
    noise). Returns (value in [-1, 1], d/dx, d/dz) in lattice units."""
    ipx, ipz = np.floor(px), np.floor(pz)
    fx, fz = px - ipx, pz - ipz
    va = np.zeros(px.shape)
    vdx = np.zeros(px.shape)
    vdz = np.zeros(px.shape)
    wt = np.zeros(px.shape)
    f = 2.0 * np.pi
    for i in (-2, -1, 0, 1):
        for j in (-2, -1, 0, 1):
            hx, hz = _hash2((ipx - i).astype(np.int64), (ipz - j).astype(np.int64), seed)
            ppx = fx + i - hx * 0.5
            ppz = fz + j - hz * 0.5
            d = ppx * ppx + ppz * ppz
            w = np.exp(-d * 2.0)
            wt += w
            mag = ppx * dirx + ppz * dirz
            va += np.cos(mag * f) * w
            s = -np.sin(mag * f) * w
            vdx += s * dirx
            vdz += s * dirz
    return va / wt, vdx / wt, vdz / wt


def gully_detail(h, X, Z, cell, wavelengths=(78.0, 36.0, 15.0), amps=(15.0, 6.5, 2.4), seed=5):
    """Gullies and spurs that follow the slope (downhill stripes), several octaves, each octave's direction bent by
    the previous ones so channels branch. Strongest on steep high ground, nothing on flats."""
    gz, gx = np.gradient(h, cell)
    slope = np.hypot(gx, gz)
    out = h.copy()
    ax, az = gx.copy(), gz.copy()
    rel = np.clip((h - L.SEA) / 140.0, 0.0, 1.0)
    mask = G.smoothstep(0.12, 0.55, slope) * (0.35 + 0.65 * rel)
    for k, (lam, amp) in enumerate(zip(wavelengths, amps)):
        ln = np.maximum(np.hypot(ax, az), 1e-6)
        # stripes run downhill: their lattice direction is perpendicular to the gradient
        dirx, dirz = -az / ln, ax / ln
        v, dvx, dvz = gully_octave(X / lam, Z / lam, dirx, dirz, seed + k * 7)
        out += v * amp * mask
        ax = ax + dvx * amp * mask / lam * 0.6
        az = az + dvz * amp * mask / lam * 0.6
    return out
