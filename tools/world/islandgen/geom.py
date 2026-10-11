"""Geometry helpers for the CT Island v2 generator (pure numpy).

Conventions: metres, x east, z north, y up. 2D points are (x, z). Headings are measured in the x/z plane.
Right-hand traffic: forward lanes run on the RIGHT of a segment's centreline (right of from -> to).
"""
import math

import numpy as np


# ------------------------------------------------------------------ splines

def catmull_rom(points, closed=False, spacing=2.0, alpha=0.5):
    """Centripetal Catmull-Rom spline through `points` (N x 2), resampled to ~`spacing` metres by arc length.

    Open curves use mirrored phantom end points (P0' = 2 P0 - P1), so the curve starts and ends exactly on the first
    and last control point. The C# side never re-evaluates the spline: it uses the dense samples written by this tool.
    """
    p = np.asarray(points, dtype=np.float64)
    if len(p) < 2:
        return p.copy()
    if closed:
        ext = np.vstack([p[-1], p, p[0], p[1]])
    else:
        ext = np.vstack([2 * p[0] - p[1], p, 2 * p[-1] - p[-2]])
    out = []
    nseg = len(p) if closed else len(p) - 1
    for i in range(nseg):
        p0, p1, p2, p3 = ext[i], ext[i + 1], ext[i + 2], ext[i + 3]
        d01 = max(np.linalg.norm(p1 - p0), 1e-6) ** alpha
        d12 = max(np.linalg.norm(p2 - p1), 1e-6) ** alpha
        d23 = max(np.linalg.norm(p3 - p2), 1e-6) ** alpha
        t0, t1 = 0.0, d01
        t2, t3 = t1 + d12, t1 + d12 + d23
        seg_len = np.linalg.norm(p2 - p1)
        n = max(2, int(math.ceil(seg_len / (spacing * 0.5))))
        t = np.linspace(t1, t2, n, endpoint=False)[:, None]
        a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1
        a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2
        a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3
        b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2
        b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3
        c = (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2
        out.append(c)
    out.append(p[0:1] if closed else p[-1:])
    dense = np.vstack(out)
    return resample(dense, spacing, closed=closed)


def arc_lengths(poly):
    d = np.linalg.norm(np.diff(poly, axis=0), axis=1)
    return np.concatenate([[0.0], np.cumsum(d)])


def resample(poly, spacing, closed=False):
    """Uniform arc-length resampling; keeps both end points (closed: first point repeated at the end is dropped)."""
    poly = np.asarray(poly, dtype=np.float64)
    if closed and np.linalg.norm(poly[0] - poly[-1]) > 1e-6:
        poly = np.vstack([poly, poly[:1]])
    s = arc_lengths(poly)
    total = s[-1]
    if total < 1e-6:
        return poly[:1].copy()
    n = max(2, int(round(total / spacing)) + 1)
    t = np.linspace(0.0, total, n)
    out = np.stack([np.interp(t, s, poly[:, k]) for k in range(poly.shape[1])], axis=1)
    if closed:
        out = out[:-1]
    return out


def tangents(poly, closed=False):
    p = np.asarray(poly, dtype=np.float64)[:, :2]   # (N, 2) x/z points
    if closed:
        t = np.roll(p, -1, axis=0) - np.roll(p, 1, axis=0)
    else:
        t = np.empty_like(p)
        t[1:-1] = p[2:] - p[:-2]
        t[0] = p[1] - p[0]
        t[-1] = p[-1] - p[-2]
    n = np.linalg.norm(t, axis=1, keepdims=True)
    return t / np.maximum(n, 1e-9)


def right_normals(tan):
    """Right of heading (tx, tz) in the x/z plane is (tz, -tx)."""
    return np.stack([tan[:, 1], -tan[:, 0]], axis=1)


def left_normals(tan):
    return -right_normals(tan)


def heading_deg(dx, dz):
    """Yaw in degrees clockwise from north (+z), the Unity +Y rotation of a forward vector (dx, dz)."""
    return math.degrees(math.atan2(dx, dz)) % 360.0


# ------------------------------------------------------------------ 2D tests

def seg_intersect(p, r, q, s):
    """Intersection of lines p + t r and q + u s. Returns (t, u) or None when parallel."""
    rxs = r[0] * s[1] - r[1] * s[0]
    if abs(rxs) < 1e-9:
        return None
    qp = (q[0] - p[0], q[1] - p[1])
    t = (qp[0] * s[1] - qp[1] * s[0]) / rxs
    u = (qp[0] * r[1] - qp[1] * r[0]) / rxs
    return t, u


def point_seg_dist(px, pz, ax, az, bx, bz):
    """Vectorised distance from points (px, pz arrays) to segment a-b; also returns the clamped parameter t."""
    vx, vz = bx - ax, bz - az
    l2 = vx * vx + vz * vz
    t = np.clip(((px - ax) * vx + (pz - az) * vz) / max(l2, 1e-12), 0.0, 1.0)
    dx, dz = px - (ax + t * vx), pz - (az + t * vz)
    return np.sqrt(dx * dx + dz * dz), t


def polygon_area(poly):
    x, z = poly[:, 0], poly[:, 1]
    return 0.5 * float(np.sum(x * np.roll(z, -1) - np.roll(x, -1) * z))


def oriented_rect(cx, cz, w, d, yaw_deg):
    """Corners (4 x 2) of a w (local x) by d (local z) rectangle centred at (cx, cz), rotated by yaw (clockwise)."""
    a = math.radians(yaw_deg)
    c, s = math.cos(a), math.sin(a)
    pts = []
    for lx, lz in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)):
        # Unity yaw: local +z (north) turns towards +x (east) for positive angles.
        pts.append((cx + lx * c + lz * s, cz - lx * s + lz * c))
    return np.array(pts)


def local_to_world(ox, oz, yaw_deg, lx, lz):
    a = math.radians(yaw_deg)
    c, s = math.cos(a), math.sin(a)
    return ox + lx * c + lz * s, oz - lx * s + lz * c


def bezier2(a, c, b, n):
    t = np.linspace(0.0, 1.0, n)[:, None]
    a, c, b = np.asarray(a), np.asarray(c), np.asarray(b)
    return (1 - t) ** 2 * a + 2 * (1 - t) * t * c + t ** 2 * b


# ------------------------------------------------------------------ noise (deterministic, vectorised)

_PERM = None


def _perm(seed=1337):
    global _PERM
    if _PERM is None:
        rng = np.random.default_rng(seed)
        p = rng.permutation(512)
        _PERM = np.concatenate([p, p]).astype(np.int64)
    return _PERM


def _hash2(ix, iz):
    p = _perm()
    return p[(p[ix & 511] + iz) & 1023]


def value_noise(x, z):
    """Smooth value noise in [0, 1] on a unit lattice."""
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    ix, iz = np.floor(x).astype(np.int64), np.floor(z).astype(np.int64)
    fx, fz = x - ix, z - iz
    ux = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
    uz = fz * fz * fz * (fz * (fz * 6 - 15) + 10)
    a = _hash2(ix, iz) / 511.0
    b = _hash2(ix + 1, iz) / 511.0
    c = _hash2(ix, iz + 1) / 511.0
    d = _hash2(ix + 1, iz + 1) / 511.0
    return (a + (b - a) * ux) + ((c + (d - c) * ux) - (a + (b - a) * ux)) * uz


def fbm(x, z, octaves=4, lac=2.03, gain=0.5):
    s = np.zeros_like(np.asarray(x, dtype=np.float64))
    amp, freq, norm = 0.5, 1.0, 0.0
    for i in range(octaves):
        s = s + value_noise(x * freq + i * 17.3, z * freq - i * 9.1) * amp
        norm += amp
        amp *= gain
        freq *= lac
    return s / norm


def ridged(x, z, octaves=4, lac=2.07, gain=0.55):
    """Ridged multifractal in [0, 1]: sharp crests (1) along noise zero-crossings, for craggy rock."""
    s = np.zeros_like(np.asarray(x, dtype=np.float64))
    amp, freq, norm = 0.5, 1.0, 0.0
    prev = np.ones_like(s)
    for i in range(octaves):
        n = 1.0 - np.abs(value_noise(x * freq + i * 31.7, z * freq + i * 11.9) * 2.0 - 1.0)
        n = n * n * prev
        prev = np.clip(n * 1.6, 0.0, 1.0)
        s = s + n * amp
        norm += amp
        amp *= gain
        freq *= lac
    return s / norm


def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, dtype=np.float64) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)
