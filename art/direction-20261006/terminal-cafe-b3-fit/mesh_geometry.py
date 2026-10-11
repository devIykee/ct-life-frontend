import gzip
import numpy as np

def pieces(path):
    rows = []
    v = []
    f = []
    offset = 0
    for line in gzip.open(path, 'rt'):
        a = line.split()
        if not a:
            continue
        if a[0] == 'v':
            if f:
                rows.append((np.array(v), np.array(f)))
                offset += len(v)
                v = []
                f = []
            v.append([float(x) for x in a[1:]])
        elif a[0] == 'f':
            f.append([int(x.split("/")[0]) - 1 - offset for x in a[1:]])
    if v:
        rows.append((np.array(v), np.array(f)))
    return rows

def segment_hits(p, q, triangles):
    d = q - p
    e1 = triangles[:, 1] - triangles[:, 0]
    e2 = triangles[:, 2] - triangles[:, 0]
    h = np.cross(d, e2)
    a = np.einsum('ij,ij->i', e1, h)
    valid = np.abs(a) > 1e-08
    inv = np.zeros(len(a))
    inv[valid] = 1 / a[valid]
    s = p - triangles[:, 0]
    u = inv * np.einsum('ij,ij->i', s, h)
    qv = np.cross(s, e1)
    vv = inv * (qv @ d)
    t = inv * np.einsum('ij,ij->i', e2, qv)
    return valid & (u >= 0) & (vv >= 0) & (u + vv <= 1) & (t >= 0) & (t <= 1)

def chair_intersection_count(body, chair, dz, min_y=0.32, max_y=1.1):
    bv, bf = body
    cv, cf = chair
    cv = cv.copy()
    cv[:, 2] += dz
    bt = bv[bf]
    ct = cv[cf]
    ct = ct[(ct.max(axis=1)[:, 1] > min_y) & (ct.min(axis=1)[:, 1] < max_y)]
    lo, hi = (cv.min(axis=0) - 0.0001, cv.max(axis=0) + 0.0001)
    bt = bt[(bt.max(axis=1) >= lo).all(axis=1) & (bt.min(axis=1) <= hi).all(axis=1)]
    if not len(bt):
        return 0
    bmin, bmax = (bt.min(axis=1), bt.max(axis=1))
    hits = 0
    for c in ct:
        lower, upper = (c.min(axis=0) - 1e-05, c.max(axis=0) + 1e-05)
        nearby = bt[(bmax >= lower).all(axis=1) & (bmin <= upper).all(axis=1)]
        if not len(nearby):
            continue
        if any((segment_hits(c[i], c[(i + 1) % 3], nearby).any() for i in range(3))):
            hits += 1
            continue
        a, b, c0 = c
        e1 = b - a
        e2 = c0 - a
        p = nearby[:, [0, 1, 2]].reshape(-1, 3)
        q = nearby[:, [1, 2, 0]].reshape(-1, 3)
        d = q - p
        h = np.cross(d, e2)
        det = h @ e1
        valid = np.abs(det) > 1e-08
        inv = np.zeros(len(det))
        inv[valid] = 1 / det[valid]
        s = p - a
        u = inv * np.einsum('ij,ij->i', s, h)
        qv = np.cross(s, e1)
        v = inv * np.einsum('ij,ij->i', d, qv)
        t = inv * (qv @ e2)
        if (valid & (u >= 0) & (v >= 0) & (u + v <= 1) & (t >= 0) & (t <= 1)).any():
            hits += 1
    return hits
