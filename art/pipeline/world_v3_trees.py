"""World districts 2 (2026-10-23): semi-real forest trees for CT Island from Poly Haven's CC0 tree scans, replacing the toy
Quaternius pines and broadleaves (about 13k forest instances).

  python3 -I art/pipeline/world_v3_trees.py fetch
      downloads the CC0 Poly Haven tree models below (glTF, 1k textures, the 2k alpha maps) into art/raw/polyhaven/<asset>/
  art/pipeline/blender.sh art/pipeline/world_v3_trees.py -- [pt_fir_a ...] [--sheet]
      builds art/processed/world_v3/v3_forest/: per tree one FBX (LOD0, LOD1, LOD2) and one 2048 atlas, plus a manifest
      for the island kit (dress.py, IslandV2Kit).

Method (a billboard cloud, the standard way to bring a scanned tree to mobile):
  - the source tree is split into wood (trunk, branches) and foliage (needle twigs / leaves, millions of alpha cards);
  - the foliage is clustered (k-means on its triangles, area weighted): every cluster becomes up to three crossed cards
    on its own principal planes, and each card's texture is the cluster's real foliage projected onto it (a numpy point
    splat of the source triangles with their twig texture and alpha, z-buffered, 2x supersampled), darkened by how deep it
    sits in the crown (baked ambient occlusion), so near trees keep the scan's real silhouette and branch layout;
  - the wood is decimated (thin twigs culled) and its colour baked (Cycles, selected to active) into the same atlas, so a
    tree is one material and one draw per LOD;
  - LOD0 ~110 clusters, LOD1 ~28 clusters (+ the trunk), LOD2 a three-quad impostor (front, side and top views of LOD0,
    up-facing normals so it lights evenly from any side) for the far forest and aerials;
  - card normals point out of the crown (radial + up), so a crown shades as one volume under the kit shader.

Sources: Poly Haven models in SOURCES (all CC0, https://polyhaven.com/license, checked 2026-10-23). Credits are in each
manifest entry and art/LICENSES.md.
"""
import os, re, sys, json, math, shutil, subprocess, urllib.request, urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.abspath(os.path.join(HERE, '..'))
RAW_PH = os.path.join(ART, 'raw', 'polyhaven')
OUT = os.path.join(ART, 'processed', 'world_v3', 'v3_forest')
TEX = os.path.join(OUT, 'textures')
UA = 'CTLife-artpipeline/1.0 (CC0 asset fetch)'
SHEET = '/Users/mac/Documents/codes/projects/ct-life-evidence/world-districts2-20261023/trees-sheet.png'

# id -> (Poly Haven model, object name in its glTF (None: the only/biggest one), kind, target height m, clusters LOD0, LOD1)
TREES = {
    'pt_fir_a': ('fir_tree_01', 'fir_tree_01_a_LOD0', 'conifer', 17.5, 120, 30),
    'pt_fir_b': ('fir_tree_01', 'fir_tree_01_b_LOD0', 'conifer', 15.0, 110, 28),
    'pt_fir_c': ('fir_tree_01', 'fir_tree_01_c_LOD0', 'conifer', 9.5, 80, 22),
    'pt_pine_a': ('pine_tree_01', None, 'conifer', 18.0, 120, 30),
    'pt_broad_a': ('tree_small_02', None, 'broad', 8.5, 90, 24),
    'pt_island_a': ('island_tree_02', None, 'broad', 6.5, 90, 24),
    'pt_island_b': ('island_tree_01', None, 'broad', 8.0, 100, 26),
    'pt_jacaranda': ('jacaranda_tree', None, 'broad', 12.5, 120, 30),
}
SOURCES = sorted({v[0] for v in TREES.values()})


# hosts the Poly Haven API hands out (the API itself, its download CDN); anything else is refused
ALLOWED_HOSTS = {'api.polyhaven.com', 'dl.polyhaven.org'}


def _check_url(url):
    u = urllib.parse.urlparse(url)
    if u.scheme != 'https' or u.hostname not in ALLOWED_HOSTS:
        raise SystemExit('refusing to fetch ' + repr(url))
    return url


def _safe(name):
    """An API-derived name made safe for a file name."""
    return re.sub(r'[^A-Za-z0-9_-]', '_', str(name))


def _inside(path, root):
    p = os.path.normpath(path)
    if not p.startswith(os.path.normpath(root) + os.sep):
        raise SystemExit('refusing to write outside ' + root + ': ' + path)
    return p


def _get(url, path):
    _check_url(url)
    if os.path.exists(path):
        return
    for _ in range(8):
        if subprocess.run(['curl', '-sS', '-L', '--proto', '=https', '--proto-redir', '=https', '-C', '-', '--retry', '5', '--retry-all-errors',
                           '-A', UA, '-o', path + '.part', '--', url]).returncode == 0:
            os.replace(path + '.part', path)
            return
    raise SystemExit('download failed ' + url)


def _json(url):
    _check_url(url)
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': UA}), timeout=60) as r:
        return json.load(r)


def fetch():
    for m in SOURCES:
        d = _inside(os.path.join(RAW_PH, _safe(m)), RAW_PH)
        if os.path.exists(os.path.join(d, 'meta.json')):
            continue
        inf = _json('https://api.polyhaven.com/info/' + _safe(m))
        files = _json('https://api.polyhaven.com/files/' + _safe(m))
        g = files['gltf']['1k']['gltf']
        os.makedirs(d, exist_ok=True)
        print('[fetch] Poly Haven', m, flush=True)
        _get(g['url'], _inside(os.path.join(d, _safe(os.path.splitext(os.path.basename(urllib.parse.urlparse(g['url']).path))[0]) + '.gltf'), d))
        for rel, v in g['include'].items():
            # the glTF names these files (textures/<x>.jpg, <x>.bin), so the relative path is kept, each part sanitised
            parts = [p for p in rel.replace('\\', '/').split('/') if p not in ('', '.', '..')]
            if not parts:
                continue
            stem, ext = os.path.splitext(parts[-1])
            safe = [_safe(p) for p in parts[:-1]] + [_safe(stem) + '.' + _safe(ext.lstrip('.'))]
            if safe[-1] != parts[-1] or safe[:-1] != parts[:-1]:
                print('[fetch] skipped an unexpected file name', repr(rel), flush=True)
                continue
            t = _inside(os.path.join(d, *safe), d)
            os.makedirs(os.path.dirname(t), exist_ok=True)
            _get(v['url'], t)
        md = _inside(os.path.join(d, 'maps'), d)
        os.makedirs(md, exist_ok=True)
        for k, v in files.items():
            if isinstance(v, dict) and ('alpha' in k.lower() or 'opacity' in k.lower()):
                r = '2k' if '2k' in v else '1k'
                if 'png' in v.get(r, {}):
                    _get(v[r]['png']['url'], _inside(os.path.join(md, f'{_safe(m)}_{_safe(k)}_{_safe(r)}.png'), md))
        json.dump(dict(id=m, name=inf['name'], url=f'https://polyhaven.com/a/{m}', authors=list(inf['authors']), licence='CC0',
                       dimensions_mm=inf.get('dimensions'), polycount=inf.get('polycount')), open(os.path.join(d, 'meta.json'), 'w'), indent=1)
    print('[fetch] done', flush=True)


try:
    import bpy  # noqa
    IN_BLENDER = True
except ImportError:
    IN_BLENDER = False

if not IN_BLENDER:
    if __name__ == '__main__':
        fetch()
    raise SystemExit(0)

# ====================================================================== Blender part
import time
import bmesh
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, HERE)
import world_v3_lib as W

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ATLAS = 2048
WOOD_PX = 384                  # the baked wood's square in the atlas
BB_PX = 256                    # impostor view width
RNG = np.random.default_rng(20261023)


def log(*a):
    print('[trees]', *a, flush=True)


# ------------------------------------------------------------------ source
def load_source(model, obj_name):
    W.reset()
    d = os.path.join(RAW_PH, model)
    gl = [f for f in os.listdir(d) if f.endswith('.gltf')][0]
    objs = W.import_glb(os.path.join(d, gl))
    meshes = [o for o in objs if o.type == 'MESH']
    if obj_name:
        keep = [o for o in meshes if o.name.startswith(obj_name)]
    else:
        keep = [max(meshes, key=lambda o: len(o.data.polygons))]
    for o in objs:
        if o not in keep and o.type == 'MESH':
            bpy.data.objects.remove(o)
    o = keep[0]
    if len(keep) > 1:
        o = W.join_parts(keep, 'src')
    # apply the transform (glTF Y-up was turned Z-up by the importer)
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)
    return o


def foliage_material_indices(o):
    return [i for i, s in enumerate(o.material_slots) if s.material and any(k in s.material.name.lower() for k in ('twig', 'leaves', 'leaf', 'needle'))]


def tri_arrays(o):
    """-> (V (n,3), T (m,3) vertex ids, L (m,3) loop ids, mat (m,), UV (loops,2))."""
    me = o.data
    me.calc_loop_triangles()
    m = len(me.loop_triangles)
    T = np.empty(m * 3, np.int32); me.loop_triangles.foreach_get('vertices', T)
    L = np.empty(m * 3, np.int32); me.loop_triangles.foreach_get('loops', L)
    M = np.empty(m, np.int32); me.loop_triangles.foreach_get('material_index', M)
    V = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', V)
    uvl = me.uv_layers.active
    UV = np.empty(len(me.loops) * 2, np.float32); uvl.data.foreach_get('uv', UV)
    return V.reshape(-1, 3), T.reshape(-1, 3), L.reshape(-1, 3), M, UV.reshape(-1, 2)


def image_array(path, size=None):
    img = bpy.data.images.load(path, check_existing=True)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    a = px.reshape(h, w, 4)
    bpy.data.images.remove(img)
    return a


def foliage_texture(model, o, fidx):
    """The foliage's base colour (sRGB as stored) with alpha from the separate 2k alpha map (the glTF's JPGs have none),
    plus the material's UV transform if the importer added a mapping node."""
    m = o.material_slots[fidx[0]].material
    base = None
    off, sc = (0.0, 0.0), (1.0, 1.0)
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image and 'diff' in n.image.name.lower():
            base = n.image
        if n.type == 'MAPPING':
            off = tuple(n.inputs['Location'].default_value[:2]); sc = tuple(n.inputs['Scale'].default_value[:2])
    if base is None:
        base = next(n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
    w, h = base.size
    px = np.empty(w * h * 4, np.float32)
    base.pixels.foreach_get(px)
    col = px.reshape(h, w, 4).copy()
    md = os.path.join(RAW_PH, model, 'maps')
    al = [f for f in sorted(os.listdir(md)) if 'alpha' in f or 'opacity' in f] if os.path.isdir(md) else []
    role = 'twig' if 'twig' in m.name.lower() else 'leaves'
    al = [f for f in al if role in f] or al
    if al:
        a = image_array(os.path.join(md, al[0]))
        ys = (np.arange(h) * a.shape[0] / h).astype(int); xs = (np.arange(w) * a.shape[1] / w).astype(int)
        col[..., 3] = a[ys][:, xs][..., 0]
    log('foliage texture', base.name, (w, h), 'alpha', al[:1], 'uv map', off, sc)
    return col, off, sc


# ------------------------------------------------------------------ splat renderer
def splat(P, UVt, tex, origin, U, Vv, N, ext, size, ss=2, density=3.5, ao_fn=None, max_samples=6_000_000):
    """Project triangles P (m,3,3) with UVs UVt (m,3,2) textured by tex (H,W,4) onto the plane (origin, U, Vv) seen from +N;
    ext = (u0, u1, v0, v1) metres; size = (pw, ph) px. -> RGBA (ph, pw, 4), row 0 = v0 (bottom)."""
    pw, ph = size
    W_, H_ = pw * ss, ph * ss
    u0, u1, v0, v1 = ext
    rel = P - origin
    pu = rel @ U; pv = rel @ Vv; pd = rel @ N
    x = (pu - u0) / (u1 - u0) * W_
    y = (pv - v0) / (v1 - v0) * H_
    ar = 0.5 * np.abs((x[:, 1] - x[:, 0]) * (y[:, 2] - y[:, 0]) - (x[:, 2] - x[:, 0]) * (y[:, 1] - y[:, 0]))
    n = np.clip(np.ceil(ar * density), 1, 600).astype(np.int64)
    tot = int(n.sum())
    if tot > max_samples:
        n = np.maximum(1, (n * max_samples / tot)).astype(np.int64); tot = int(n.sum())
    ti = np.repeat(np.arange(len(P)), n)
    r1 = np.sqrt(RNG.random(tot, dtype=np.float32)); r2 = RNG.random(tot, dtype=np.float32)
    b0 = 1 - r1; b1 = r1 * (1 - r2); b2 = r1 * r2
    sx = b0 * x[ti, 0] + b1 * x[ti, 1] + b2 * x[ti, 2]
    sy = b0 * y[ti, 0] + b1 * y[ti, 1] + b2 * y[ti, 2]
    sd = b0 * pd[ti, 0] + b1 * pd[ti, 1] + b2 * pd[ti, 2]
    su = b0 * UVt[ti, 0, 0] + b1 * UVt[ti, 1, 0] + b2 * UVt[ti, 2, 0]
    sv = b0 * UVt[ti, 0, 1] + b1 * UVt[ti, 1, 1] + b2 * UVt[ti, 2, 1]
    th, tw = tex.shape[:2]
    tx = (np.mod(su, 1.0) * tw).astype(np.int32) % tw
    ty = (np.mod(sv, 1.0) * th).astype(np.int32) % th
    c = tex[ty, tx]
    ok = (c[:, 3] > 0.5) & (sx >= 0) & (sx < W_) & (sy >= 0) & (sy < H_)
    if ao_fn is not None:
        p3 = (b0[:, None] * P[ti, 0] + b1[:, None] * P[ti, 1] + b2[:, None] * P[ti, 2])
        shade = ao_fn(p3[ok])
    sx, sy, sd, c = sx[ok], sy[ok], sd[ok], c[ok]
    pix = sy.astype(np.int64) * W_ + sx.astype(np.int64)
    order = np.lexsort((sd, pix))
    pix_o = pix[order]
    last = np.ones(len(order), bool)
    last[:-1] = pix_o[1:] != pix_o[:-1]
    sel = order[last]
    img = np.zeros((H_ * W_, 4), np.float32)
    col = c[sel, :3]
    if ao_fn is not None:
        col = col * shade[sel][:, None]
    img[pix[sel], :3] = col
    img[pix[sel], 3] = 1.0
    img = img.reshape(H_, W_, 4)
    # supersampled -> output: colour averaged over covered samples, alpha = coverage
    img = img.reshape(ph, ss, pw, ss, 4)
    cov = img[..., 3].mean(axis=(1, 3))
    rgb = img[..., :3].sum(axis=(1, 3)) / np.maximum(img[..., 3].sum(axis=(1, 3)), 1e-6)[..., None]
    out = np.zeros((ph, pw, 4), np.float32)
    out[..., :3] = rgb
    out[..., 3] = cov
    return out


def dilate(a, iters=6):
    """Grow covered colours into transparent texels (no dark fringes once mip-mapped); alpha untouched."""
    rgb = a[..., :3].copy(); valid = a[..., 3] > 0.02
    for _ in range(iters):
        acc = np.zeros_like(rgb); cnt = np.zeros(valid.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sv = np.roll(valid, (dy, dx), (0, 1)); acc += np.roll(rgb, (dy, dx), (0, 1)) * sv[..., None]; cnt += sv
        grow = (~valid) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        valid |= grow
    a[..., :3] = rgb
    return a


# ------------------------------------------------------------------ clusters -> cards
def kmeans(X, w, k, iters=12):
    idx = RNG.choice(len(X), size=min(len(X), 200_000), replace=False, p=w / w.sum())
    S = X[idx]
    C = S[RNG.choice(len(S), size=k, replace=False)]
    for _ in range(iters):
        d = ((S[:, None, :] - C[None]) ** 2).sum(-1)
        a = d.argmin(1)
        for j in range(k):
            m = a == j
            if m.any():
                C[j] = S[m].mean(0)
            else:
                C[j] = S[RNG.integers(len(S))]
    lab = np.empty(len(X), np.int32)
    for s in range(0, len(X), 250_000):
        lab[s:s + 250_000] = (((X[s:s + 250_000, None, :] - C[None]) ** 2).sum(-1)).argmin(1)
    return lab, C


def cards_for(P, area, k, kind, H):
    """Cluster foliage triangles; -> list of card dicts (origin, U, V, N, ext, tri ids)."""
    cen = P.mean(1)
    X = cen.copy()
    X[:, 2] *= 1.6 if kind == 'conifer' else 1.0       # conifers layer by height: clusters stay within a whorl
    lab, _ = kmeans(X, area, k)
    cards = []
    for j in range(k):
        ids = np.where(lab == j)[0]
        if len(ids) < 4:
            continue
        pts = P[ids].reshape(-1, 3)
        wts = np.repeat(area[ids], 3)
        c = (pts * wts[:, None]).sum(0) / wts.sum()
        cov = np.cov((pts - c).T, aweights=wts)
        ev, evec = np.linalg.eigh(cov)                  # ascending
        e2, e1, e0 = evec[:, 0], evec[:, 1], evec[:, 2]
        total = float(area[ids].sum())
        planes = [(e0, e1, e2), (e0, e2, e1), (e1, e2, e0)]
        made = []
        for a_, b_, n_ in planes:
            # keep 'up' as +v where the plane allows, so the texture reads upright
            if abs(b_[2]) < abs(a_[2]):
                a_, b_ = b_, a_
            if b_[2] < 0:
                b_ = -b_
            n_ = np.cross(a_, b_)
            pu = (pts - c) @ a_; pv = (pts - c) @ b_
            u0, u1 = np.percentile(pu, [0.5, 99.5]); v0, v1 = np.percentile(pv, [0.5, 99.5])
            ar = (u1 - u0) * (v1 - v0)
            made.append(dict(origin=c, U=a_, V=b_, N=n_, ext=(u0, u1, v0, v1), ids=ids, area=ar, weight=total))
        big = max(m['area'] for m in made)
        cards += [m for m in made if m['area'] > 0.18 * big and m['area'] > 0.04]
    return cards


def crown_ao(P, kind, H):
    """Baked crown shading: texels deep inside the crown (near the trunk axis for their height) and low down darker."""
    cen = P.mean(1)
    r = np.hypot(cen[:, 0], cen[:, 1]); z = cen[:, 2]
    bins = np.linspace(0, H * 1.02, 25)
    bi = np.clip(np.digitize(z, bins) - 1, 0, len(bins) - 2)
    rmax = np.array([np.percentile(r[bi == i], 92) if (bi == i).sum() > 20 else np.nan for i in range(len(bins) - 1)])
    good = ~np.isnan(rmax)
    rmax = np.interp(np.arange(len(rmax)), np.where(good)[0], rmax[good]) if good.any() else np.ones(len(rmax))
    rmax = np.maximum(rmax, 0.3)
    lo = 0.42 if kind == 'conifer' else 0.5

    def fn(p3):
        rr = np.hypot(p3[:, 0], p3[:, 1])
        b = np.clip(np.digitize(p3[:, 2], bins) - 1, 0, len(rmax) - 1)
        depth = np.clip(rr / rmax[b], 0, 1)
        hgt = np.clip(p3[:, 2] / H, 0, 1)
        return (lo + (1 - lo) * depth ** 0.8) * (0.86 + 0.14 * hgt)
    return fn


def pack(rects, size, fixed=()):
    """Shelf-pack rects (w, h) px scaled by one factor to fill the atlas, plus fixed-size rects; -> (scale, positions)."""
    area = sum(w * h for w, h in rects)
    farea = sum(w * h for w, h in fixed)
    scale = math.sqrt(0.86 * (size * size - farea) / max(area, 1.0))
    while True:
        allr = [(int(w * scale) + 3, int(h * scale) + 3, i) for i, (w, h) in enumerate(rects)] + \
               [(w + 3, h + 3, len(rects) + j) for j, (w, h) in enumerate(fixed)]
        allr.sort(key=lambda r: -r[1])
        pos, x, y, row, ok = {}, 0, 0, 0, True
        for w, h, i in allr:
            if x + w > size:
                x, y, row = 0, y + row, 0
            if y + h > size:
                ok = False
                break
            pos[i] = (x + 1, y + 1, max(2, w - 3), max(2, h - 3))
            x += w
            row = max(row, h)
        if ok:
            return scale, pos
        scale *= 0.97


# ------------------------------------------------------------------ wood
def wood_lods(src, fidx, H, kind):
    """Wood (trunk + branches) -> (hi-res wood, LOD0 wood): thin twigs culled, decimated."""
    w = src.copy(); w.data = src.data.copy(); w.name = 'wood'
    bpy.context.scene.collection.objects.link(w)
    bm = bmesh.new(); bm.from_mesh(w.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in fidx], context='FACES')
    bm.to_mesh(w.data); bm.free()
    keep_ground_cull(w, H * (0.06 if kind == 'conifer' else 0.1))
    target = 1100 if kind == 'conifer' else 1500
    l0 = W.decimate_copy(w, target, 'wood_l0', planar_first=False)
    for _ in range(4):                          # dense, many-part wood (the jacaranda) needs more than one collapse pass
        if W.tris(l0) <= target * 1.2:
            break
        l0b = W.decimate_copy(l0, target, 'wood_l0b', planar_first=False)
        bpy.data.objects.remove(l0)
        l0 = l0b
        l0.name = l0.data.name = 'wood_l0'
    return w, l0


def keep_ground_cull(o, min_size):
    """cull_small, but a loose part touching the ground (the trunk's foot, root flare) always stays."""
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
    seen, kill = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, part = [v], []
        seen.add(v.index)
        while stack:
            q = stack.pop(); part.append(q)
            for e in q.link_edges:
                y = e.other_vert(q)
                if y.index not in seen:
                    seen.add(y.index); stack.append(y)
        cs = np.array([p.co[:] for p in part])
        if np.linalg.norm(cs.max(0) - cs.min(0)) < min_size and cs[:, 2].min() > 0.6:
            kill.extend(part)
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(o.data); bm.free(); o.data.update()


def wood_lod1(l0, H, kind):
    """LOD1 wood from the baked LOD0 (UVs carried through the decimation): the trunk and the main limbs only."""
    l1 = W.dup(l0, 'wood_l1x')
    keep_ground_cull(l1, H * 0.22)
    l1b = W.decimate_copy(l1, 260 if kind == 'conifer' else 380, 'wood_l1', planar_first=False)
    bpy.data.objects.remove(l1)
    return l1b


def bake_wood(hi, lo, px):
    """Smart-UV the low wood and bake the source wood's base colour onto it (Cycles, selected to active)."""
    lo.data.materials.clear()
    while lo.data.uv_layers:
        lo.data.uv_layers.remove(lo.data.uv_layers[0])
    lo.data.uv_layers.new(name='UVMap')
    img = bpy.data.images.new('wood_bake', px, px, alpha=False)
    m = bpy.data.materials.new('wood_bake_m'); m.use_nodes = True
    t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = img; m.node_tree.nodes.active = t
    lo.data.materials.append(m)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 4; sc.cycles.device = 'CPU'
    sc.render.bake.use_selected_to_active = True
    sc.render.bake.cage_extrusion = 0.06
    sc.render.bake.max_ray_distance = 0.4
    sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False; sc.render.bake.use_pass_color = True
    sc.render.bake.margin = 4
    sc.render.bake.use_clear = True
    bpy.ops.object.select_all(action='DESELECT'); lo.select_set(True); bpy.context.view_layer.objects.active = lo
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT'); hi.select_set(True); lo.select_set(True); bpy.context.view_layer.objects.active = lo
    r = bpy.ops.object.bake(type='DIFFUSE')
    a = np.empty(px * px * 4, np.float32); img.pixels.foreach_get(a); a = a.reshape(px, px, 4)
    log('wood bake', r, 'mean', a[..., :3].mean((0, 1)).round(3))
    a[..., 3] = 1.0
    return a


# ------------------------------------------------------------------ the build
def card_mesh(name, cards, pos, scale_px, atlas_size, kind, H):
    verts, faces, uvs, nrms = [], [], [], []
    for i, cd in enumerate(cards):
        X, Y, Wp, Hp = pos[i]
        u0, u1, v0, v1 = cd['ext']
        c = cd['origin']
        # normals out of the crown: radial from the trunk axis at the card's height, lifted up
        rad = np.array([c[0], c[1], 0.0]); rl = np.linalg.norm(rad)
        rad = rad / rl if rl > 1e-3 else np.zeros(3)
        nn = rad * 0.85 + np.array([0, 0, 0.55 if kind == 'conifer' else 0.45])
        nn /= np.linalg.norm(nn)
        base = len(verts)
        for (a, b) in ((0, 0), (1, 0), (1, 1), (0, 1)):
            p = c + cd['U'] * (u0 + a * (u1 - u0)) + cd['V'] * (v0 + b * (v1 - v0))
            verts.append(tuple(p)); nrms.append(tuple(nn))
            uvs.append(((X + a * Wp) / atlas_size, (Y + b * Hp) / atlas_size))
        faces.append((base, base + 1, base + 2, base + 3))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        for li in p.loop_indices:
            uvl.data[li].uv = uvs[me.loops[li].vertex_index]
    me.normals_split_custom_set_from_vertices(nrms)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def remap_uv(o, rect, atlas_size):
    X, Y, Wp, Hp = rect
    uv = np.empty(len(o.data.loops) * 2, np.float32)
    o.data.uv_layers.active.data.foreach_get('uv', uv)
    uv = uv.reshape(-1, 2)
    uv[:, 0] = (X + np.clip(uv[:, 0], 0, 1) * Wp) / atlas_size
    uv[:, 1] = (Y + np.clip(uv[:, 1], 0, 1) * Hp) / atlas_size
    o.data.uv_layers.active.data.foreach_set('uv', uv.ravel())


def mesh_tris_uv(objs):
    P, UV = [], []
    for o in objs:
        V, T, L, M, UVl = tri_arrays(o)
        mw = np.array(o.matrix_world)
        Vw = V @ mw[:3, :3].T + mw[:3, 3]
        P.append(Vw[T]); UV.append(UVl[L])
    return np.concatenate(P), np.concatenate(UV)


def impostor(tid, objs, atlas, H, Wd):
    """Front, side and top views of LOD0 (splat of its cards and wood with the atlas) -> one 3-view texture and a
    three-quad mesh (two crossed verticals, one horizontal at 0.45 H) with up normals."""
    P, UV = mesh_tris_uv(objs)
    vs = P.reshape(-1, 3)
    mn, mx = vs.min(0), vs.max(0)
    half = max(abs(mn[0]), abs(mx[0]), abs(mn[1]), abs(mx[1])) * 1.02
    hpx = int(min(512, max(128, BB_PX * H / (2 * half))))
    tex = np.zeros((max(hpx, BB_PX), BB_PX * 3, 4), np.float32)
    views = [((0, 0, 0), np.array([1.0, 0, 0]), np.array([0, 0, 1.0]), np.array([0, -1.0, 0])),
             ((0, 0, 0), np.array([0, 1.0, 0]), np.array([0, 0, 1.0]), np.array([1.0, 0, 0]))]
    for k, (o_, U, Vv, N) in enumerate(views):
        im = splat(P, UV, atlas, np.array(o_), U, Vv, N, (-half, half, 0.0, H * 1.01), (BB_PX, hpx), ss=2, density=2.0)
        tex[:hpx, k * BB_PX:(k + 1) * BB_PX] = im
    top = splat(P, UV, atlas, np.zeros(3), np.array([1.0, 0, 0]), np.array([0, 1.0, 0]), np.array([0, 0, 1.0]),
                (-half, half, -half, half), (BB_PX, BB_PX), ss=2, density=2.0)
    tex[:BB_PX, 2 * BB_PX:3 * BB_PX] = top
    th, tw = tex.shape[:2]
    # the impostor's up normals catch more sun than the crown's radial card normals: darkened to meet LOD1 at the switch
    tex[..., :3] *= 0.78
    tex = dilate(tex, 8)
    W.save_png(tex, os.path.join(TEX, f'{tid}_billboard.png'))
    verts, faces, uvs = [], [], []
    for k, ang in enumerate((0.0, 90.0)):
        a = math.radians(ang)
        dx, dy = math.cos(a) * half, math.sin(a) * half
        b = len(verts)
        verts += [(-dx, -dy, 0), (dx, dy, 0), (dx, dy, H * 1.01), (-dx, -dy, H * 1.01)]
        uvs += [(k * BB_PX / tw, 0), ((k + 1) * BB_PX / tw, 0), ((k + 1) * BB_PX / tw, hpx / th), (k * BB_PX / tw, hpx / th)]
        faces.append((b, b + 1, b + 2, b + 3))
    b = len(verts); zt = H * 0.45
    verts += [(-half, -half, zt), (half, -half, zt), (half, half, zt), (-half, half, zt)]
    uvs += [(2 * BB_PX / tw, 0), (1.0, 0), (1.0, BB_PX / th), (2 * BB_PX / tw, BB_PX / th)]
    faces.append((b, b + 1, b + 2, b + 3))
    me = bpy.data.meshes.new(tid + '_LOD2')
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        for li in p.loop_indices:
            uvl.data[li].uv = uvs[me.loops[li].vertex_index]
    me.normals_split_custom_set_from_vertices([(0.0, 0.0, 1.0)] * len(verts))
    m = bpy.data.materials.new(f'M_{tid}_billboard')
    me.materials.append(m)
    o = bpy.data.objects.new(tid + '_LOD2', me)
    bpy.context.scene.collection.objects.link(o)
    return o


def build(tid):
    model, oname, kind, H, k0, k1 = TREES[tid]
    t0 = time.time()
    src = load_source(model, oname)
    V, T, L, M, UVl = tri_arrays(src)
    # the trunk's foot to the origin, the tree to its target height
    low = V[V[:, 2] < V[:, 2].min() + 0.5]
    foot = np.array([low[:, 0].mean(), low[:, 1].mean(), V[:, 2].min()])
    h0 = V[:, 2].max() - V[:, 2].min()
    s = H / h0
    src.data.transform(Matrix.Translation(Vector(tuple(-foot))))
    src.data.transform(Matrix.Scale(s, 4))
    src.data.update()
    V = (V - foot) * s
    fidx = foliage_material_indices(src)
    log(tid, model, oname, 'source', len(T), 'tris, height', round(h0, 2), '-> scale', round(s, 3), 'foliage slots', fidx)
    tex, off, sc = foliage_texture(model, src, fidx)
    fol = np.isin(M, fidx)
    P = V[T[fol]]
    UVt = UVl[L[fol]] * np.array(sc) + np.array(off)
    area = 0.5 * np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) + 1e-9
    ao = crown_ao(P, kind, H)
    cards0 = cards_for(P, area, k0, kind, H)
    cards1 = cards_for(P, area, k1, kind, H)
    log(tid, 'cards', len(cards0), len(cards1), f'({time.time() - t0:.0f}s)')
    # wood
    hi, w0 = wood_lods(src, fidx, H, kind)
    wood = bake_wood(hi, w0, 512)
    w1 = wood_lod1(w0, H, kind)
    # atlas: LOD0 cards at full density, LOD1 cards at 0.55 of it, the wood square
    px_m = 100.0
    rects = [((c['ext'][1] - c['ext'][0]) * px_m, (c['ext'][3] - c['ext'][2]) * px_m) for c in cards0] + \
            [((c['ext'][1] - c['ext'][0]) * px_m * 0.55, (c['ext'][3] - c['ext'][2]) * px_m * 0.55) for c in cards1]
    scale, pos = pack(rects, ATLAS, fixed=[(WOOD_PX, WOOD_PX)])
    log(tid, 'atlas texel density', round(px_m * scale), 'px/m')
    atlas = np.zeros((ATLAS, ATLAS, 4), np.float32)
    allc = cards0 + cards1
    for i, cd in enumerate(allc):
        X, Y, Wp, Hp = pos[i]
        im = splat(P[cd['ids']], UVt[cd['ids']], tex, cd['origin'], cd['U'], cd['V'], cd['N'], cd['ext'], (Wp, Hp), ao_fn=ao)
        atlas[Y:Y + Hp, X:X + Wp] = im
    wr = pos[len(allc)]
    X, Y, Wp, Hp = wr
    ys = (np.arange(Hp) * 512 / Hp).astype(int); xs = (np.arange(Wp) * 512 / Wp).astype(int)
    atlas[Y:Y + Hp, X:X + Wp] = wood[ys][:, xs]
    atlas = dilate(atlas, 4)
    os.makedirs(TEX, exist_ok=True)
    W.save_png(atlas, os.path.join(TEX, f'{tid}_atlas.png'))
    log(tid, 'atlas written', f'({time.time() - t0:.0f}s)')
    # meshes
    c0 = card_mesh(tid + '_c0', cards0, [pos[i] for i in range(len(cards0))], scale, ATLAS, kind, H)
    c1 = card_mesh(tid + '_c1', cards1, [pos[len(cards0) + i] for i in range(len(cards1))], scale, ATLAS, kind, H)
    for w_ in (w0, w1):
        remap_uv(w_, wr, ATLAS)
    mat = bpy.data.materials.new(f'{tid}_atlas')
    lods = []
    for li, (cm, wm) in enumerate(((c0, w0), (c1, w1))):
        for o in (cm, wm):
            o.data.materials.clear(); o.data.materials.append(mat)
        j = W.join_parts([cm, wm], f'{tid}_LOD{li}')
        lods.append(j)
    bb = impostor(tid, [lods[0]], atlas, H, None)
    bpy.data.objects.remove(hi)
    bpy.data.objects.remove(src)
    W.export_fbx(lods + [bb], os.path.join(OUT, tid + '.fbx'))
    vs = np.array([v.co[:] for v in lods[0].data.vertices])
    size = (vs.max(0) - vs.min(0)).tolist()
    meta = json.load(open(os.path.join(RAW_PH, model, 'meta.json')))
    by = ', '.join(f'{a} ({r})' for a, r in meta['authors'].items()) if isinstance(meta['authors'], dict) else ', '.join(meta['authors'])
    entry = dict(fbx=f'world_v3/v3_forest/{tid}.fbx', kind='tree', tris=[W.tris(o) for o in lods + [bb]], size_m=[round(x, 3) for x in size],
                 materials=[f'{tid}_atlas'], lod2_material=f'M_{tid}_billboard',
                 textures=[f'world_v3/v3_forest/textures/{tid}_atlas.png', f'world_v3/v3_forest/textures/{tid}_billboard.png'],
                 tags=['tree', 'kit:v3', 'pack:pt', kind], source=meta['url'], source_uid=model, licence='CC0',
                 credit=f'"{meta["name"]}" by {by} (Poly Haven, {meta["url"]}), CC0; rebuilt by CT Life as a billboard-cloud game tree '
                        f'(art/pipeline/world_v3_trees.py)', family='v3_forest')
    log(tid, 'done', entry['tris'], entry['size_m'], f'({time.time() - t0:.0f}s)')
    return {tid: entry}


def sheet(ids):
    """Lookdev sheet: each tree's LOD0, LOD1 and impostor side by side (EEVEE, sun + sky)."""
    W.reset()
    sc = bpy.context.scene
    x = 0.0
    for tid in ids:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, tid + '.fbx'))
        new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
        atlas = bpy.data.images.load(os.path.join(TEX, f'{tid}_atlas.png'))
        bbi = bpy.data.images.load(os.path.join(TEX, f'{tid}_billboard.png'))
        for o in new:
            m = bpy.data.materials.new(o.name + '_m'); m.use_nodes = True
            nt = m.node_tree; b = nt.nodes['Principled BSDF']; t = nt.nodes.new('ShaderNodeTexImage')
            t.image = bbi if 'LOD2' in o.name else atlas
            nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
            mth = nt.nodes.new('ShaderNodeMath'); mth.operation = 'GREATER_THAN'; mth.inputs[1].default_value = 0.45
            nt.links.new(t.outputs['Alpha'], mth.inputs[0]); nt.links.new(mth.outputs[0], b.inputs['Alpha'])
            b.inputs['Roughness'].default_value = 0.9
            o.data.materials.clear(); o.data.materials.append(m)
            li = 0 if 'LOD0' in o.name else 1 if 'LOD1' in o.name else 2
            o.location = (x + li * max(6.0, o.dimensions[0] * 1.15), 0, 0)
        x += 3 * max(6.0, max(o.dimensions[0] for o in new) * 1.15) + 2.0
    objs = [o for o in bpy.data.objects if o.type == 'MESH']
    mn, mx = W.world_bounds(objs)
    span = max(mx.x - mn.x, (mx.z - mn.z) * 2.0) * 1.04
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = span
    cam.location = ((mn.x + mx.x) / 2, -150, (mn.z + mx.z) / 2); cam.rotation_euler = (math.radians(90), 0, 0)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.65, 0.8, 1)
    sc.world.node_tree.nodes['Background'].inputs[1].default_value = 0.8
    sc.camera = cam; sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = 2400; sc.render.resolution_y = 1200
    sc.view_settings.view_transform = 'Standard'
    sc.render.filepath = SHEET
    os.makedirs(os.path.dirname(SHEET), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    log('sheet', SHEET)


def main():
    ids = [a for a in ARGS if a in TREES] or list(TREES)
    if '--sheet-only' not in ARGS:
        man = {}
        for tid in ids:
            if not os.path.exists(os.path.join(RAW_PH, TREES[tid][0], 'meta.json')):
                log('missing source', TREES[tid][0], '(run fetch)')
                continue
            man.update(build(tid))
        if man:
            W.write_manifest(os.path.join(OUT, 'manifest.json'), man)
            W.write_manifest(os.path.join(ART, 'processed', 'world_v3', 'manifest.json'), man)
    if '--sheet' in ARGS or '--sheet-only' in ARGS:
        sheet([t for t in ids if os.path.exists(os.path.join(OUT, t + '.fbx'))])


main()
