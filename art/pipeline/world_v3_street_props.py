"""World v3 STREET props: semi-real US street furniture for the downtown (Seeker / Mali budget).

Two modes, one file:
  python3 -I art/pipeline/world_v3_street_props.py fetch
      downloads every CC0 source (Poly Haven API, ambientCG API) into its own directory under
      art/raw/polyhaven/<asset>/ and art/raw/ambientcg/<AssetId>/ (cached; re-runs skip what is there)
  art/pipeline/blender.sh art/pipeline/world_v3_street_props.py -- [build] [ids,...] [--sheet]
      builds the assets (all, or the comma list), writes art/processed/world_v3/v3_street/<id>.fbx + textures/
      + manifest.json, and (--sheet, or when building everything) the preview sheet.

Method (self-modelled props): every part is modelled at real size with metre-scaled 'tile' UVs and a SOURCE material
built from a CC0 scan (ambientCG/Poly Haven), optionally tinted, with painted graphic layers (lenses, sign faces,
retro-reflective borders) drawn by this script in numpy. The joined LOD0 gets a second, packed 'atlas' UV (one island
scale for the whole prop = constant texel density, graphic islands get a 2x boost) and Cycles bakes base colour, alpha,
metallic, roughness (emission trick: exact values), tangent normals (OpenGL, the atlas UV's tangent space) and
ambient occlusion (with a ground plane) into ONE atlas set per asset. LOD1 is a decimated copy of LOD0 (same UVs,
same material slots). Poly Haven models: the source is decimated to the budget, re-unwrapped, and its materials are baked
high -> low (selected-to-active) into the same kind of atlas. Decals (manhole, drain) are cropped ambientCG scans.

Axis conventions (match world_v3_lib.export_fbx: Blender -Y = Unity +Z, Blender +X = Unity -X, Blender Z = Unity Y):
  traffic light: lenses face Blender -Y (Unity +Z), mast arm runs along Blender +X (Unity -X)
  stop sign: face looks along Blender -Y (Unity +Z); bus shelter: open side at Blender -Y (Unity +Z)
  vendor cart: long axis X.
"""
import os, sys, json, math, zipfile, shutil, urllib.request, urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.abspath(os.path.join(HERE, '..'))
RAW_PH = os.path.join(ART, 'raw', 'polyhaven')
RAW_ACG = os.path.join(ART, 'raw', 'ambientcg')
OUT = os.path.join(ART, 'processed', 'world_v3', 'v3_street')
TEX = os.path.join(OUT, 'textures')
SHEET = '/Users/mac/Documents/codes/projects/ct-life-evidence/downtown-finish-20261022/props/props-sheet.png'
UA = {'User-Agent': 'CTLife-artpipeline/1.0 (CC0 asset fetch)'}

# ------------------------------------------------------------------ sources (all CC0, licence pages verified 2026-10-22:
# https://polyhaven.com/license "Our assets are all licensed as CC0"; https://docs.ambientcg.com/license/ "All ambientCG
# assets are provided under the Creative Commons CC0 1.0 Universal License")
ACG = {  # id -> (resolution, what it is used for)
    'Metal011': ('1K-JPG', 'galvanised steel (poles, posts, bike hoops)'),
    'Metal027': ('1K-JPG', 'powder-coated steel (signal heads, shelter frame, bollard)'),
    'Metal009': ('1K-JPG', 'brushed stainless steel (vendor cart)'),
    'Wood049': ('1K-JPG', 'wood slats (shelter bench, cart counter)'),
    'Fabric061': ('1K-JPG', 'canvas (cart umbrella)'),
    'Rubber004': ('1K-JPG', 'rubber (cart tyres)'),
    'ManholeCover005': ('2K-JPG', 'cast-iron manhole cover scan (foundry logo painted out)'),
    'Grate002': ('2K-JPG', 'kerb drain grate scan (foundry name painted out)'),
}
PH_MODELS = ['metal_trash_can', 'outdoor_table_chair_set_01']


def _get(url, path):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=120) as r, open(path + '.part', 'wb') as f:
        shutil.copyfileobj(r, f)
    os.replace(path + '.part', path)


def _json(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def _safe_extract(zp, dest):
    with zipfile.ZipFile(zp) as z:
        for n in z.namelist():
            t = os.path.normpath(os.path.join(dest, n))
            if not t.startswith(os.path.abspath(dest) + os.sep) or n.endswith('/'):
                continue
            os.makedirs(os.path.dirname(t), exist_ok=True)
            with z.open(n) as s, open(t, 'wb') as d:
                shutil.copyfileobj(s, d)


def fetch():
    os.makedirs(RAW_ACG, exist_ok=True)
    os.makedirs(RAW_PH, exist_ok=True)
    info = _json('https://ambientcg.com/api/v2/full_json?id=' + ','.join(ACG) + '&include=downloadData,displayData&limit=50')
    for a in info['foundAssets']:
        aid = a['assetId']
        d = os.path.join(RAW_ACG, aid)
        if os.path.exists(os.path.join(d, 'meta.json')):
            continue
        res = ACG[aid][0]
        dl = [x for f in a['downloadFolders'].values() for c in f['downloadFiletypeCategories'].values()
              for x in c['downloads'] if x['attribute'] == res][0]
        os.makedirs(d, exist_ok=True)
        zp = os.path.join(d, dl['fileName'])
        print('[fetch] ambientCG', aid, res, flush=True)
        _get(dl['downloadLink'], zp)
        _safe_extract(zp, d)
        os.remove(zp)
        json.dump(dict(id=aid, name=a.get('displayName'), url=f'https://ambientcg.com/view?id={aid}', author='ambientCG (Lennart Demes)',
                       licence='CC0 1.0', creation=a.get('creationMethod'), dimension_cm=a.get('dimensionX'), file=dl['fileName'],
                       used_for=ACG[aid][1]), open(os.path.join(d, 'meta.json'), 'w'), indent=1)
    for m in PH_MODELS:
        d = os.path.join(RAW_PH, m)
        if os.path.exists(os.path.join(d, 'meta.json')):
            continue
        inf = _json('https://api.polyhaven.com/info/' + m)
        files = _json('https://api.polyhaven.com/files/' + m)
        g = files['gltf']['1k']['gltf']
        os.makedirs(d, exist_ok=True)
        print('[fetch] Poly Haven', m, flush=True)
        _get(g['url'], os.path.join(d, os.path.basename(urllib.parse.urlparse(g['url']).path)))
        for rel, v in g['include'].items():
            t = os.path.normpath(os.path.join(d, rel))
            if not t.startswith(d + os.sep):
                continue
            os.makedirs(os.path.dirname(t), exist_ok=True)
            _get(v['url'], t)
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
import glob
import bmesh
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, HERE)
import world_v3_lib as W

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


def log(*a):
    print('[street]', *a, flush=True)


# ------------------------------------------------------------------ small numpy image kit (row 0 = bottom, like Blender)
def grid(w, h):
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    return np.meshgrid(u, v)            # U, V arrays (h, w)


def sstep(d, aa):
    """coverage from a signed distance (negative inside), aa = half width of the soft edge."""
    return np.clip(0.5 - d / (2 * aa), 0.0, 1.0)


def sd_box(x, y, cx, cy, hw, hh, r=0.0):
    qx = np.abs(x - cx) - (hw - r)
    qy = np.abs(y - cy) - (hh - r)
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r


def sd_circle(x, y, cx, cy, r):
    return np.hypot(x - cx, y - cy) - r


def sd_capsule(x, y, ax, ay, bx, by, r):
    pax, pay, bax, bay = x - ax, y - ay, bx - ax, by - ay
    h = np.clip((pax * bax + pay * bay) / (bax * bax + bay * bay), 0, 1)
    return np.hypot(pax - bax * h, pay - bay * h) - r


def sd_ngon(x, y, cx, cy, R, n, rot):
    """regular polygon with circumradius R (flat-to-flat 2*R*cos(pi/n)), first vertex at angle rot."""
    a = np.arctan2(y - cy, x - cx) - rot
    seg = 2 * np.pi / n
    a = np.mod(a, seg) - seg / 2
    d = np.hypot(x - cx, y - cy)
    return d * np.cos(a) - R * np.cos(np.pi / n)


def over(dst, rgb, a):
    a = a[..., None]
    dst[..., :3] = dst[..., :3] * (1 - a) + np.array(rgb, np.float32) * a
    if dst.shape[-1] == 4:
        dst[..., 3] = np.maximum(dst[..., 3], a[..., 0])


def noise(h, w, seed, scale=8):
    """cheap value noise (bilinear upsampled random grid), 0..1"""
    rng = np.random.default_rng(seed)
    gh, gw = max(2, h // scale), max(2, w // scale)
    g = rng.random((gh + 1, gw + 1)).astype(np.float32)
    ys = np.linspace(0, gh - 1e-4, h)
    xs = np.linspace(0, gw - 1e-4, w)
    y0, x0 = ys.astype(int), xs.astype(int)
    fy, fx = (ys - y0)[:, None], (xs - x0)[None, :]
    a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
    return a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy


_FONT = None


def font():
    global _FONT
    if _FONT is None:
        p = glob.glob('/Applications/Blender.app/Contents/Resources/*/datafiles/fonts/Inter.woff2')
        _FONT = bpy.data.fonts.load(p[0]) if p else None     # Inter (SIL OFL), ships with Blender
    return _FONT


def text_mask(txt, w, h, box, bold=0.04, cond=1.0, ss=3):
    """Coverage mask (h, w) of txt fitted (aspect kept, x scaled by cond) into box=(u0, v0, u1, v1) in 0..1 image space."""
    cu = bpy.data.curves.new('tm', 'FONT')
    cu.body = txt
    f = font()
    if f:
        cu.font = f
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.offset = bold
    ob = bpy.data.objects.new('tm', cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    tri = np.array([[l.vert.co[:2] for l in f.loops] for f in bm.faces], np.float64)
    bm.free()
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)
    bpy.data.meshes.remove(me)
    mask = np.zeros((h, w), np.float32)
    if not len(tri):
        return mask
    tri[..., 0] *= cond
    mn, mx = tri.reshape(-1, 2).min(0), tri.reshape(-1, 2).max(0)
    u0, v0, u1, v1 = box
    bw, bh = (u1 - u0) * w, (v1 - v0) * h
    s = min(bw / (mx[0] - mn[0]), bh / (mx[1] - mn[1]))
    cx, cy = (u0 + u1) / 2 * w, (v0 + v1) / 2 * h
    P = (tri - (mn + mx) / 2) * s + np.array([cx, cy])
    S = ss
    big = np.zeros((h * S, w * S), np.float32)
    for t in P * S:
        x0, y0 = np.floor(t.min(0)).astype(int)
        x1, y1 = np.ceil(t.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, w * S), min(y1, h * S)
        if x1 <= x0 or y1 <= y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        (ax, ay), (bx, by), (qx, qy) = t
        e0 = (bx - ax) * (Y - ay) - (by - ay) * (X - ax)
        e1 = (qx - bx) * (Y - by) - (qy - by) * (X - bx)
        e2 = (ax - qx) * (Y - qy) - (ay - qy) * (X - qx)
        ins = ((e0 >= 0) & (e1 >= 0) & (e2 >= 0)) | ((e0 <= 0) & (e1 <= 0) & (e2 <= 0))
        big[y0:y1, x0:x1] = np.maximum(big[y0:y1, x0:x1], ins)
    return big.reshape(h, S, w, S).mean((1, 3))


def to_image(name, arr, colorspace='sRGB'):
    """numpy (h, w, 3|4) stored values -> in-memory Blender image."""
    h, w = arr.shape[:2]
    img = bpy.data.images.get(name)
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = colorspace
    full = np.ones((h, w, 4), np.float32)
    full[..., :arr.shape[2]] = arr
    img.pixels.foreach_set(np.clip(full, 0, 1).ravel())
    img.pack()
    return img


def load_px(path, colorspace='sRGB'):
    img = bpy.data.images.load(path, check_existing=True)
    img.colorspace_settings.name = colorspace
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    return img, a.reshape(h, w, 4)


def acg_file(aid, kind):
    p = glob.glob(os.path.join(RAW_ACG, aid, f'{aid}_*_{kind}.jpg'))
    return p[0] if p else None


def srgb(lin):
    lin = np.clip(lin, 0, None)
    return np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1 / 2.4) - 0.055)


def lin(s):
    return np.where(s <= 0.04045, s / 12.92, np.power((s + 0.055) / 1.055, 2.4))


def resample(a, w, h, x0=0.0, y0=0.0, x1=1.0, y1=1.0):
    """bilinear crop+resize of a (H, W, C) array (row 0 = bottom); window in 0..1 (x right, y up), 2x supersampled."""
    H, W_ = a.shape[:2]
    xs = x0 + (np.arange(w * 2) + 0.5) / (w * 2) * (x1 - x0)
    ys = y0 + (np.arange(h * 2) + 0.5) / (h * 2) * (y1 - y0)
    fx = np.clip(xs * W_ - 0.5, 0, W_ - 1.001)
    fy = np.clip(ys * H - 0.5, 0, H - 1.001)
    ix, iy = fx.astype(int), fy.astype(int)
    tx, ty = (fx - ix)[None, :, None], (fy - iy)[:, None, None]
    r = (a[iy][:, ix] * (1 - tx) * (1 - ty) + a[iy][:, ix + 1] * tx * (1 - ty) +
         a[iy + 1][:, ix] * (1 - tx) * ty + a[iy + 1][:, ix + 1] * tx * ty)
    return r.reshape(h, 2, w, 2, -1).mean((1, 3))


# ------------------------------------------------------------------ source materials (bake inputs)
_MEAN = {}


def _mean_lum(path):
    if path not in _MEAN:
        _, a = load_px(path)
        _MEAN[path] = float(lin(a[..., :3]).mean())
    return _MEAN[path]


def _sock(coll, ident):
    return next(s for s in coll if s.identifier == ident)


def src_mat(name, aid=None, scale=1.0, tint=None, tint_k=1.0, rough=None, rough_var=0.6, metal=None, nstr=1.0,
            gfx=None, gfx_mode='over', gfx_rough=None, gfx_metal=None, alpha=None, rot90=False):
    """Bake-input material. aid: ambientCG set (tiles every `scale` metres on the 'tile' UV). tint (linear RGB): recolour
    keeping the scan's luminance variation. gfx: Blender image painted by this script, mapped 0..1 on the 'tile' UV,
    composited over the scan by its alpha ('over') or used as glass colour + alpha ('glass')."""
    m = bpy.data.materials.get(name)
    if m:
        bpy.data.materials.remove(m)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    N, L = nt.nodes.new, nt.links.new
    out = N('ShaderNodeOutputMaterial')
    b = N('ShaderNodeBsdfPrincipled')
    b.name = 'BSDF'
    L(b.outputs[0], out.inputs[0])
    uv = N('ShaderNodeUVMap')
    uv.uv_map = 'tile'
    col = rgh = met = nrm = None
    if aid:
        mp = N('ShaderNodeMapping')
        mp.inputs['Scale'].default_value = (1 / scale, 1 / scale, 1)
        if rot90:
            mp.inputs['Rotation'].default_value = (0, 0, math.pi / 2)
        L(uv.outputs[0], mp.inputs[0])

        def tex(kind, cs):
            p = acg_file(aid, kind)
            if not p:
                return None
            t = N('ShaderNodeTexImage')
            t.image = bpy.data.images.load(p, check_existing=True)
            t.image.colorspace_settings.name = cs
            L(mp.outputs[0], t.inputs[0])
            return t
        tc = tex('Color', 'sRGB')
        col = tc.outputs['Color']
        if tint is not None:
            bw = N('ShaderNodeRGBToBW')
            L(col, bw.inputs[0])
            dv = N('ShaderNodeMath')
            dv.operation = 'MULTIPLY'
            dv.inputs[1].default_value = 1.0 / max(1e-4, _mean_lum(acg_file(aid, 'Color')))
            L(bw.outputs[0], dv.inputs[0])
            mul = N('ShaderNodeMix')
            mul.data_type = 'RGBA'
            mul.blend_type = 'MULTIPLY'
            _sock(mul.inputs, 'Factor_Float').default_value = 1.0
            L(dv.outputs[0], _sock(mul.inputs, 'A_Color'))
            _sock(mul.inputs, 'B_Color').default_value = (*tint, 1)
            mix = N('ShaderNodeMix')
            mix.data_type = 'RGBA'
            _sock(mix.inputs, 'Factor_Float').default_value = tint_k
            L(col, _sock(mix.inputs, 'A_Color'))
            L(_sock(mul.outputs, 'Result_Color'), _sock(mix.inputs, 'B_Color'))
            col = _sock(mix.outputs, 'Result_Color')
        tr = tex('Roughness', 'Non-Color')
        if tr:
            rgh = tr.outputs['Color']
            if rough is not None:       # re-centre the scan's roughness on `rough`
                bw = N('ShaderNodeRGBToBW')
                L(rgh, bw.inputs[0])
                ma = N('ShaderNodeMath')
                ma.operation = 'MULTIPLY_ADD'
                ma.use_clamp = True
                p = acg_file(aid, 'Roughness')
                _, ra = load_px(p, 'Non-Color')
                mean = float(ra[..., 0].mean())
                ma.inputs[1].default_value = rough_var
                ma.inputs[2].default_value = rough - rough_var * mean
                L(bw.outputs[0], ma.inputs[0])
                rgh = ma.outputs[0]
        tm = tex('Metalness', 'Non-Color')
        if tm and metal is None:
            met = tm.outputs['Color']
        tn = tex('NormalGL', 'Non-Color')
        if tn and nstr > 0:
            nm = N('ShaderNodeNormalMap')
            nm.uv_map = 'tile'
            nm.inputs['Strength'].default_value = nstr
            L(tn.outputs['Color'], nm.inputs['Color'])
            nrm = nm.outputs['Normal']
    if col is None:
        b.inputs['Base Color'].default_value = (*(tint or (0.5, 0.5, 0.5)), 1)
    if rgh is None:
        b.inputs['Roughness'].default_value = rough if rough is not None else 0.5
    b.inputs['Metallic'].default_value = metal if metal is not None else 0.0
    b.inputs['Alpha'].default_value = alpha if alpha is not None else 1.0
    if gfx is not None:
        tg = N('ShaderNodeTexImage')
        tg.image = gfx
        tg.extension = 'EXTEND'
        L(uv.outputs[0], tg.inputs[0])
        if gfx_mode == 'glass':
            col = tg.outputs['Color']
            L(tg.outputs['Alpha'], b.inputs['Alpha'])
        else:
            mix = N('ShaderNodeMix')
            mix.data_type = 'RGBA'
            L(tg.outputs['Alpha'], _sock(mix.inputs, 'Factor_Float'))
            if col is not None:
                L(col, _sock(mix.inputs, 'A_Color'))
            else:
                _sock(mix.inputs, 'A_Color').default_value = b.inputs['Base Color'].default_value[:]
            L(tg.outputs['Color'], _sock(mix.inputs, 'B_Color'))
            col = _sock(mix.outputs, 'Result_Color')
            for val, sockname, cur in ((gfx_rough, 'Roughness', rgh), (gfx_metal, 'Metallic', met)):
                if val is None:
                    continue
                mf = N('ShaderNodeMix')
                mf.data_type = 'FLOAT'
                L(tg.outputs['Alpha'], _sock(mf.inputs, 'Factor_Float'))
                if cur is not None:
                    L(cur, _sock(mf.inputs, 'A_Float'))
                else:
                    _sock(mf.inputs, 'A_Float').default_value = b.inputs[sockname].default_value
                _sock(mf.inputs, 'B_Float').default_value = val
                if sockname == 'Roughness':
                    rgh = _sock(mf.outputs, 'Result_Float')
                else:
                    met = _sock(mf.outputs, 'Result_Float')
    if col is not None:
        L(col, b.inputs['Base Color'])
    if rgh is not None:
        L(rgh, b.inputs['Roughness'])
    if met is not None:
        L(met, b.inputs['Metallic'])
    if nrm is not None:
        L(nrm, b.inputs['Normal'])
    return m


# ------------------------------------------------------------------ geometry builder
class Asset:
    """Accumulates parts (bmesh in local metres -> world) with per-face material and 'tile' UVs."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('tile')
        self.mats = []          # material objects in slot order
        self.meta = {}          # material name -> dict(boost=, glass=, ad=)

    def m(self, mat, boost=1.0, glass=False, ad=False):
        if mat not in self.mats:
            self.mats.append(mat)
            self.meta[mat.name] = dict(boost=boost, glass=glass, ad=ad)
        return self.mats.index(mat)

    def add(self, pbm, M=None):
        """append part bmesh (with 'tile' uv layer and material indices) transformed by M."""
        if M is not None:
            bmesh.ops.transform(pbm, matrix=M, verts=pbm.verts[:])
        me = bpy.data.meshes.new('_part')
        pbm.to_mesh(me)
        pbm.free()
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)

    def finish(self, name=None):
        me = bpy.data.meshes.new(name or self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        for mt in self.mats:
            me.materials.append(mt)
        o = bpy.data.objects.new(name or self.name, me)
        bpy.context.scene.collection.objects.link(o)
        return o


def T(x=0.0, y=0.0, z=0.0, rx=0.0, ry=0.0, rz=0.0, s=None):
    M = Matrix.Translation((x, y, z)) @ (Matrix.Rotation(math.radians(rz), 4, 'Z') @ Matrix.Rotation(math.radians(ry), 4, 'Y') @
                                         Matrix.Rotation(math.radians(rx), 4, 'X'))
    if s is not None:
        M = M @ Matrix.Diagonal((*s, 1.0))
    return M


def _new():
    bm = bmesh.new()
    bm.loops.layers.uv.new('tile')
    return bm


def _uv_box(bm, faces, off=(0.0, 0.0, 0.0)):
    uvl = bm.loops.layers.uv['tile']
    for f in faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for l in f.loops:
            c = l.vert.co
            l[uvl].uv = (c[a] + off[a], c[b] + off[b])


def _uv_face01(bm, f, right, up=Vector((0, 0, 1)), flipu=False):
    """map face f to 0..1 along `right` / `up` (local directions) over its own extent."""
    uvl = bm.loops.layers.uv['tile']
    us = [l.vert.co.dot(right) for l in f.loops]
    vs = [l.vert.co.dot(up) for l in f.loops]
    u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
    for l, uu, vv in zip(f.loops, us, vs):
        u = (uu - u0) / max(1e-6, u1 - u0)
        l[uvl].uv = (1 - u if flipu else u, (vv - v0) / max(1e-6, v1 - v0))


def RIGHT_OF(n):
    """viewer-right for a face whose outward normal is n (viewer looks along -n, up = +Z)."""
    fwd = -Vector(n)
    r = fwd.cross(Vector((0, 0, 1)))
    return r.normalized() if r.length > 1e-6 else Vector((1, 0, 0))


def p_box(sx, sy, sz, mi, bevel=0.0, z0=False, faces=None, drop_bottom=False, uvoff=(0, 0, 0)):
    """box (centred; z0=True: bottom at z=0). faces = {'-y': mat_index, ...} gives that side its own material with a
    0..1 UV (for graphics). Bevel = chamfer width."""
    bm = _new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz + (sz / 2 if z0 else 0)))
    if bevel:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=1, profile=0.5, affect='EDGES',
                        clamp_overlap=True)
    bm.normal_update()
    for f in bm.faces:
        f.material_index = mi
        f.smooth = False
    _uv_box(bm, bm.faces, uvoff)
    dirs = {'-x': (-1, 0, 0), '+x': (1, 0, 0), '-y': (0, -1, 0), '+y': (0, 1, 0), '+z': (0, 0, 1), '-z': (0, 0, -1)}
    for k, fm in (faces or {}).items():
        d = Vector(dirs[k])
        cand = [f for f in bm.faces if f.normal.dot(d) > 0.99]
        if not cand:
            continue
        f = max(cand, key=lambda f: f.calc_area())
        f.material_index = fm
        up = Vector((0, 0, 1)) if abs(d.z) < 0.5 else Vector((0, 1, 0))
        _uv_face01(bm, f, RIGHT_OF(d) if abs(d.z) < 0.5 else Vector((1, 0, 0)), up)
    if drop_bottom:
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -0.99], context='FACES')
    return bm


def p_lathe(prof, seg, mi, cap_top=True, cap_bottom=False, sharp=(), mats=None, a0=0.0, uvr=None):
    """revolve profile [(r, z), ...] (outer surface, listed bottom -> top) about Z. r = 0 points become poles.
    sharp: profile indices whose ring is a hard edge. mats: per-span material indices (len(prof) - 1)."""
    bm = _new()
    uvl = bm.loops.layers.uv['tile']
    rings = []
    for (r, z) in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0.0, 0.0, z))] * seg)
        else:
            rings.append([bm.verts.new((r * math.cos(a0 + 2 * math.pi * i / seg), r * math.sin(a0 + 2 * math.pi * i / seg), z))
                          for i in range(seg)])
    R = uvr or max(p[0] for p in prof)
    V = [0.0]
    for i in range(1, len(prof)):
        V.append(V[-1] + math.hypot(prof[i][0] - prof[i - 1][0], prof[i][1] - prof[i - 1][1]))
    for j in range(len(prof) - 1):
        p0, p1 = prof[j][0] < 1e-6, prof[j + 1][0] < 1e-6
        if p0 and p1:
            continue
        for i in range(seg):
            i2 = (i + 1) % seg
            u0, u1 = 2 * math.pi * R * i / seg, 2 * math.pi * R * (i + 1) / seg
            if p0:
                vs, uvs = [rings[j][i], rings[j + 1][i2], rings[j + 1][i]], [((u0 + u1) / 2, V[j]), (u1, V[j + 1]), (u0, V[j + 1])]
            elif p1:
                vs, uvs = [rings[j][i], rings[j][i2], rings[j + 1][i]], [(u0, V[j]), (u1, V[j]), ((u0 + u1) / 2, V[j + 1])]
            else:
                vs = [rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]]
                uvs = [(u0, V[j]), (u1, V[j]), (u1, V[j + 1]), (u0, V[j + 1])]
            f = bm.faces.new(vs)
            f.material_index = mats[j] if mats else mi
            f.smooth = True
            for l, uv_ in zip(f.loops, uvs):
                l[uvl].uv = uv_
    for j, top in ((0, False), (len(prof) - 1, True)):
        if prof[j][0] < 1e-6 or not (cap_top if top else cap_bottom):
            continue
        f = bm.faces.new(rings[j] if top else rings[j][::-1])
        f.material_index = (mats[-1] if top else mats[0]) if mats else mi
        f.smooth = False
        for l in f.loops:
            l[uvl].uv = (l.vert.co.x, l.vert.co.y)
    bm.normal_update()
    for j in sharp:
        ringset = set(rings[j])
        for e in bm.edges:
            if e.verts[0] in ringset and e.verts[1] in ringset:
                e.smooth = False
    return bm


def p_tube(path, r, seg, mi, caps=True, r_end=None, uvr=None):
    """sweep a circle along a polyline (parallel-transport frames)."""
    bm = _new()
    uvl = bm.loops.layers.uv['tile']
    P = [Vector(p) for p in path]
    n = len(P)
    tang = []
    for i in range(n):
        t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        tang.append(t)
    up = Vector((0, 0, 1)) if abs(tang[0].z) < 0.9 else Vector((1, 0, 0))
    nrm = tang[0].cross(up).normalized()
    frames = []
    for i in range(n):
        if i:
            q = tang[i - 1].rotation_difference(tang[i])
            nrm = (q @ nrm).normalized()
        frames.append((nrm.copy(), tang[i].cross(nrm).normalized()))
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    rings = []
    for i in range(n):
        rr = r if r_end is None else r + (r_end - r) * L[i] / L[-1]
        a_, b_ = frames[i]
        rings.append([bm.verts.new(P[i] + (a_ * math.cos(2 * math.pi * k / seg) + b_ * math.sin(2 * math.pi * k / seg)) * rr)
                      for k in range(seg)])
    R = uvr or r
    for i in range(n - 1):
        for k in range(seg):
            k2 = (k + 1) % seg
            f = bm.faces.new([rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]])
            f.material_index = mi
            f.smooth = True
            us = [k, k + 1, k + 1, k]
            vs = [L[i], L[i], L[i + 1], L[i + 1]]
            for l, uu, vv in zip(f.loops, us, vs):
                l[uvl].uv = (2 * math.pi * R * uu / seg, vv)
    if caps:
        for ring, rev in ((rings[0], True), (rings[-1], False)):
            f = bm.faces.new(ring[::-1] if rev else ring)
            f.material_index = mi
            f.smooth = False
            c = sum((v.co for v in ring), Vector()) / seg
            a_, b_ = frames[0] if rev else frames[-1]
            for l in f.loops:
                d = l.vert.co - c
                l[uvl].uv = (d.dot(a_), d.dot(b_))
    bm.normal_update()
    return bm


def p_prism(pts, depth, mi_front, mi_back, mi_side, back_uv01=False):
    """flat polygon pts [(x, z)] in the XZ plane, extruded from y=0 (front, faces -Y) to y=depth (back)."""
    bm = _new()
    uvl = bm.loops.layers.uv['tile']
    fr = [bm.verts.new((x, 0.0, z)) for x, z in pts]
    bk = [bm.verts.new((x, depth, z)) for x, z in pts]
    ff = bm.faces.new(fr[::-1])
    bf = bm.faces.new(bk)
    bm.normal_update()
    if ff.normal.y > 0:
        ff.normal_flip()
        bf.normal_flip()
    ff.material_index, bf.material_index = mi_front, mi_back
    for f in (ff, bf):
        f.smooth = False
    _uv_face01(bm, ff, Vector((1, 0, 0)))
    if back_uv01:
        _uv_face01(bm, bf, Vector((-1, 0, 0)))
    else:
        for l in bf.loops:
            l[uvl].uv = (l.vert.co.x, l.vert.co.z)
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        f = bm.faces.new([fr[i], fr[j], bk[j], bk[i]])
        f.material_index = mi_side
        f.smooth = False
    bm.normal_update()
    for f in bm.faces:
        if f.material_index == mi_side:
            if f.normal.dot(Vector(f.calc_center_median()) - Vector((sum(p[0] for p in pts) / n, depth / 2, sum(p[1] for p in pts) / n))) < 0:
                f.normal_flip()
    _uv_box(bm, [f for f in bm.faces if f.material_index == mi_side and f not in (ff, bf)])
    return bm


def p_disc(r, seg, mi, depth=0.0, a0=0.0, mi_side=None):
    """disc in the XZ plane facing -Y (y=0), optional side ring back to y=depth; front UV 0..1 over the disc."""
    bm = _new()
    uvl = bm.loops.layers.uv['tile']
    fr = [bm.verts.new((r * math.cos(a0 + 2 * math.pi * i / seg), 0.0, r * math.sin(a0 + 2 * math.pi * i / seg))) for i in range(seg)]
    f = bm.faces.new(fr[::-1])
    bm.normal_update()
    if f.normal.y > 0:
        f.normal_flip()
    f.material_index = mi
    f.smooth = False
    for l in f.loops:
        l[uvl].uv = (0.5 + l.vert.co.x / (2 * r), 0.5 + l.vert.co.z / (2 * r))
    if depth:
        bk = [bm.verts.new((v.co.x, depth, v.co.z)) for v in fr]
        for i in range(seg):
            j = (i + 1) % seg
            s = bm.faces.new([fr[i], bk[i], bk[j], fr[j]])
            s.material_index = mi if mi_side is None else mi_side
            s.smooth = True
            for l, (uu, vv) in zip(s.loops, ((i, 0), (i, depth), (i + 1, depth), (i + 1, 0))):
                l[uvl].uv = (2 * math.pi * r * uu / seg, vv)
        bm.normal_update()
        for s in bm.faces:
            if s is not f and s.normal.dot(Vector((s.calc_center_median().x, 0, s.calc_center_median().z))) < 0:
                s.normal_flip()
    return bm


def p_shell(r, length, seg, mi, a_from, a_to, thick=0.006):
    """partial tube shell along -Y from y=0 to y=-length (signal visor): inner + outer surfaces + rim (double-sided)."""
    bm = _new()
    uvl = bm.loops.layers.uv['tile']
    ang = [math.radians(a_from + (a_to - a_from) * i / seg) for i in range(seg + 1)]
    grid_ = {}
    for side, rr in (('i', r), ('o', r + thick)):
        for k, yy in enumerate((0.0, -length)):
            grid_[side, k] = [bm.verts.new((rr * math.cos(a), yy, rr * math.sin(a))) for a in ang]
    def quad(vs, uvs, smooth=True):
        f = bm.faces.new(vs)
        f.material_index = mi
        f.smooth = smooth
        for l, uv_ in zip(f.loops, uvs):
            l[uvl].uv = uv_
        return f
    for i in range(seg):
        u0, u1 = r * ang[i], r * ang[i + 1]
        quad([grid_['i', 0][i], grid_['i', 1][i], grid_['i', 1][i + 1], grid_['i', 0][i + 1]], [(u0, 0), (u0, length), (u1, length), (u1, 0)])
        quad([grid_['o', 0][i], grid_['o', 0][i + 1], grid_['o', 1][i + 1], grid_['o', 1][i]], [(u0, 0), (u1, 0), (u1, length), (u0, length)])
        quad([grid_['i', 1][i], grid_['o', 1][i], grid_['o', 1][i + 1], grid_['i', 1][i + 1]], [(u0, 0), (u0, thick), (u1, thick), (u1, 0)], False)
    for i in (0, seg):
        quad([grid_['i', 0][i], grid_['o', 0][i], grid_['o', 1][i], grid_['i', 1][i]], [(0, 0), (thick, 0), (thick, length), (0, length)], False)
    bm.normal_update()
    # orient: outer faces outward (away from axis), inner faces inward, rim/end faces sensibly
    for f in bm.faces:
        c = f.calc_center_median()
        radial = Vector((c.x, 0, c.z))
        rl = radial.length
        if abs(rl - r) < 1e-4 and f.smooth:
            if f.normal.dot(radial) > 0:
                f.normal_flip()
        elif abs(rl - r - thick) < 1e-4 and f.smooth:
            if f.normal.dot(radial) < 0:
                f.normal_flip()
    bm.normal_update()
    return bm


# ------------------------------------------------------------------ atlas UV + Cycles bake
def _cycles():
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'METAL'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        sc.cycles.device = 'GPU'
    except Exception as e:      # CPU is fine, just slower
        log('cycles GPU unavailable:', e)
        sc.cycles.device = 'CPU'
    if not sc.world:
        sc.world = bpy.data.worlds.new('bake_world')
    sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1)
    sc.render.bake.margin = 8
    sc.render.bake.margin_type = 'EXTEND'
    return sc


def _principled(m):
    return next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if m and m.node_tree else None


def _emit_swap(mats, chan):
    saved = []
    for m in mats:
        b = _principled(m)
        if b is None:
            continue
        nt = m.node_tree
        outs = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL']
        out = next((n for n in outs if n.is_active_output), outs[0])
        old = out.inputs['Surface'].links[0].from_socket if out.inputs['Surface'].is_linked else None
        em = nt.nodes.new('ShaderNodeEmission')
        em.inputs['Strength'].default_value = 1.0
        s = b.inputs[chan]
        if s.is_linked:
            nt.links.new(s.links[0].from_socket, em.inputs['Color'])
        else:
            dv = s.default_value
            em.inputs['Color'].default_value = (dv[0], dv[1], dv[2], 1) if hasattr(dv, '__len__') else (dv, dv, dv, 1)
        nt.links.new(em.outputs[0], out.inputs['Surface'])
        saved.append((m, out, old, em))
    return saved


def _emit_restore(saved):
    for m, out, old, em in saved:
        nt = m.node_tree
        nt.nodes.remove(em)
        if old is not None:
            nt.links.new(old, out.inputs['Surface'])


def _set_target(mats, img):
    for m in mats:
        nt = m.node_tree
        n = nt.nodes.get('__bake') or nt.nodes.new('ShaderNodeTexImage')
        n.name = '__bake'
        n.image = img
        nt.nodes.active = n


def _clear_target(mats):
    for m in mats:
        n = m.node_tree.nodes.get('__bake')
        if n:
            m.node_tree.nodes.remove(n)


def _float_img(name, size):
    im = bpy.data.images.get(name)
    if im:
        bpy.data.images.remove(im)
    im = bpy.data.images.new(name, size, size, alpha=True, float_buffer=True)
    im.colorspace_settings.name = 'Non-Color'
    return im


def _px(im):
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    return a.reshape(h, w, 4)


def _select_only(objs, active):
    bpy.ops.object.mode_set(mode='OBJECT') if bpy.context.object and bpy.context.object.mode != 'OBJECT' else None
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def triangulate(o):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


def atlas_uv(o, boost=None, skip=(), margin=0.006):
    """'atlas' UV: smart project (islands keep their real size ratio) -> per-material island boost -> pack."""
    me = o.data
    at = me.uv_layers.get('atlas') or me.uv_layers.new(name='atlas')
    me.uv_layers.active = at
    at.active_render = True
    _select_only([o], o)
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    sel = np.array([p.material_index not in skip for p in me.polygons])
    me.polygons.foreach_set('select', sel)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.0, area_weight=0.0, correct_aspect=True,
                             scale_to_bounds=False)
    bm = bmesh.from_edit_mesh(me)
    uvl = bm.loops.layers.uv['atlas']
    if boost and any(k != 1.0 for k in boost.values()):
        faces = [f for f in bm.faces if f.select]
        seen = set()
        for f0 in faces:
            if f0 in seen:
                continue
            isl, stack = [], [f0]
            seen.add(f0)
            while stack:
                f = stack.pop()
                isl.append(f)
                for l in f.loops:
                    for lo in l.edge.link_loops:
                        g = lo.face
                        if g in seen or not g.select:
                            continue
                        # same island when the shared edge has the same UVs on both sides
                        a1, a2 = l[uvl].uv, l.link_loop_next[uvl].uv
                        b1, b2 = lo[uvl].uv, lo.link_loop_next[uvl].uv
                        if ((a1 - b2).length < 1e-5 and (a2 - b1).length < 1e-5) or ((a1 - b1).length < 1e-5 and (a2 - b2).length < 1e-5):
                            seen.add(g)
                            stack.append(g)
            k = max(boost.get(f.material_index, 1.0) for f in isl)
            if k == 1.0:
                continue
            c = sum((l[uvl].uv for f in isl for l in f.loops), Vector((0, 0))) / sum(len(f.loops) for f in isl)
            for f in isl:
                for l in f.loops:
                    l[uvl].uv = c + (l[uvl].uv - c) * k
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.select_all(action='SELECT')
    try:
        bpy.ops.uv.pack_islands(rotate=True, margin_method='FRACTION', margin=margin, shape_method='CONCAVE', scale=True)
    except TypeError:
        bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')
    # texel density report (non-boosted faces)
    uv = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers['atlas'].data.foreach_get('uv', uv)
    uv = uv.reshape(-1, 2)
    r = []
    for p in me.polygons:
        if p.material_index in skip or (boost and boost.get(p.material_index, 1.0) != 1.0) or p.area < 1e-5:
            continue
        idx = list(p.loop_indices)
        a = 0.0
        for i in range(1, len(idx) - 1):
            e1, e2 = uv[idx[i]] - uv[idx[0]], uv[idx[i + 1]] - uv[idx[0]]
            a += abs(e1[0] * e2[1] - e1[1] * e2[0]) / 2
        r.append(math.sqrt(a / p.area))
    r = np.array(r)
    return float(np.median(r)) if len(r) else 0.0, float(np.percentile(r, 10)) if len(r) else 0, float(np.percentile(r, 90)) if len(r) else 0


def ground_plane(size=40.0):
    me = bpy.data.meshes.new('_ground')
    s = size / 2
    me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    o = bpy.data.objects.new('_ground', me)
    bpy.context.scene.collection.objects.link(o)
    return o


def bake_set(low, size, src=None, ao_dist=0.5, ao_samples=96, ground=True, cage=0.02):
    """Bake base(linear), alpha, metal, rough, normal (tangent, OpenGL) and AO into float arrays.
    src: high-poly objects (selected-to-active) or None (low bakes its own materials)."""
    sc = _cycles()
    sc.world.light_settings.distance = ao_dist
    gp = ground_plane() if ground else None
    if src:
        tgt = bpy.data.materials.get('__bake_target') or bpy.data.materials.new('__bake_target')
        tgt.use_nodes = True
        low.data.materials.clear()
        low.data.materials.append(tgt)
        tmats = [tgt]
        smats = list({s.material for o in src for s in o.material_slots if s.material})
    else:
        tmats = [s.material for s in low.material_slots if s.material]
        smats = tmats
    res = {}
    kw = dict(use_selected_to_active=bool(src), cage_extrusion=cage, max_ray_distance=0.0, margin=8, use_clear=True,
              target='IMAGE_TEXTURES')
    sel = (list(src) + [low]) if src else [low]
    for key, chan in (('base', 'Base Color'), ('alpha', 'Alpha'), ('metal', 'Metallic'), ('rough', 'Roughness')):
        im = _float_img('__b_' + key, size)
        _set_target(tmats, im)
        sv = _emit_swap(smats, chan)
        sc.cycles.samples = 4
        _select_only(sel, low)
        bpy.ops.object.bake(type='EMIT', **kw)
        _emit_restore(sv)
        res[key] = _px(im)
    im = _float_img('__b_normal', size)
    _set_target(tmats, im)
    sc.cycles.samples = 8
    _select_only(sel, low)
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', normal_r='POS_X', normal_g='POS_Y', normal_b='POS_Z', **kw)
    res['normal'] = _px(im)
    # AO on the low itself (with the ground), sources hidden
    im = _float_img('__b_ao', size)
    _set_target(tmats, im)
    for o in (src or []):
        o.hide_render = True
    sc.cycles.samples = ao_samples
    _select_only([low], low)
    bpy.ops.object.bake(type='AO', use_selected_to_active=False, margin=8, use_clear=True, target='IMAGE_TEXTURES')
    for o in (src or []):
        o.hide_render = False
    res['ao'] = _px(im)
    _clear_target(tmats)
    if gp:
        bpy.data.objects.remove(gp)
    return res


def save_set(stem, res, glass_mask=None, ao_floor=0.25):
    """compose + save <stem>_BaseColor (sRGB, A = opacity), _Normal (OpenGL), _MaskMap (R metal, G AO, B 0, A smooth)."""
    os.makedirs(TEX, exist_ok=True)
    base = np.zeros_like(res['base'])
    base[..., :3] = srgb(res['base'][..., :3])
    base[..., 3] = np.clip(res['alpha'][..., 0], 0, 1)
    nrm = res['normal'][..., :3]
    ao = np.clip(res['ao'][..., 0], 0, 1)
    ao = ao_floor + (1 - ao_floor) * ao
    mask = np.zeros_like(base)
    mask[..., 0] = np.clip(res['metal'][..., 0], 0, 1)
    mask[..., 1] = ao
    mask[..., 3] = 1 - np.clip(res['rough'][..., 0], 0, 1)
    paths = {k: os.path.join(TEX, f'{stem}_{k}.png') for k in ('BaseColor', 'Normal', 'MaskMap')}
    W.save_png(base, paths['BaseColor'], 4)
    W.save_png(nrm, paths['Normal'], 3)
    W.save_png(mask, paths['MaskMap'], 4)
    return paths


def finalize(asset, o, size, lod1, ao_dist=0.5, credit_tex=(), keep_origin=False, ad_paths=None, cage=0.02, src=None,
             base_meta=None, ao_samples=96):
    """o: LOD0 object with source materials + 'tile' UVs -> atlas bake, final materials, LOD1, FBX, manifest entry."""
    name = asset if isinstance(asset, str) else asset.name
    meta = {} if isinstance(asset, str) else asset.meta
    triangulate(o)
    mats = [s.material for s in o.material_slots]
    boost = {i: meta.get(m.name, {}).get('boost', 1.0) for i, m in enumerate(mats) if m}
    ad_idx = {i for i, m in enumerate(mats) if m and meta.get(m.name, {}).get('ad')}
    glass_idx = {i for i, m in enumerate(mats) if m and meta.get(m.name, {}).get('glass')}
    td = atlas_uv(o, boost, skip=ad_idx)
    log(name, 'texel density (atlas units per metre, median/p10/p90):', [round(x, 4) for x in td], '=', round(td[0] * size), 'px/m')
    me = o.data
    a = me.uv_layers['atlas'].data
    for p in me.polygons:              # ad faces: park them outside the atlas while baking
        if p.material_index in ad_idx:
            for li in p.loop_indices:
                a[li].uv = (5.0, 5.0)
    res = bake_set(o, size, src=src, ao_dist=ao_dist, cage=cage, ao_samples=ao_samples)
    if ad_idx:                          # ... then they get their 0..1 'tile' UVs (own material)
        t = me.uv_layers['tile'].data
        a = me.uv_layers['atlas'].data
        for p in me.polygons:
            if p.material_index in ad_idx:
                for li in p.loop_indices:
                    a[li].uv = t[li].uv
    paths = save_set(name, res)
    # final materials: M_<id> (opaque), M_<id>_glass, M_v3_bus_shelter_ad
    final = [W.make_material(f'M_{name}', paths, blend=False)]
    remap = {}
    if glass_idx:
        final.append(W.make_material(f'M_{name}_glass', paths, blend=True))
    if ad_idx:
        final.append(W.make_material('M_v3_bus_shelter_ad', ad_paths, blend=False))
    src_idx = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('material_index', src_idx)
    new_idx = np.zeros_like(src_idx)
    if src is None:
        for i in range(len(mats)):
            if i in glass_idx:
                new_idx[src_idx == i] = 1
            elif i in ad_idx:
                new_idx[src_idx == i] = len(final) - 1
    me.materials.clear()
    for m in final:
        me.materials.append(m)
    me.polygons.foreach_set('material_index', new_idx)
    if 'tile' in me.uv_layers:
        me.uv_layers.remove(me.uv_layers['tile'])
    me.uv_layers['atlas'].name = 'UVMap'
    me.update()
    o.name = o.data.name = name + '_LOD0'
    l1 = W.lod_under(o, lod1, name + '_LOD1', planar_first=True)
    vs = np.array([v.co[:] for v in me.vertices])
    mn, mx = vs.min(0), vs.max(0)
    sz = [round(float(x), 3) for x in (mx - mn)]
    W.export_fbx([o, l1], os.path.join(OUT, name + '.fbx'))
    texs = [os.path.basename(p) for p in paths.values()] + ([os.path.basename(p) for p in ad_paths.values()] if ad_idx else [])
    entry = dict(fbx=f'world_v3/v3_street/{name}.fbx', kind='prop', tris=[W.tris(o), W.tris(l1)], size_m=sz,
                 materials=[m.name for m in final], textures=texs, tags=['prop', 'kit:v3', 'street'])
    entry.update(base_meta or dict(source='CT Life original (modelled in Blender by art/pipeline/world_v3_street_props.py)',
                                   source_uid=name, licence='CC0', credit='CT Life original (CC0); textures: ' + '; '.join(credit_tex)))
    log(name, 'tris', entry['tris'], 'size', sz, 'bbox min', [round(float(x), 3) for x in mn])
    return entry, o, l1


# ------------------------------------------------------------------ painted graphic layers (stored sRGB values)
def C(*rgb):
    return np.array(rgb, np.float32)


def g_backplate(w_m=0.60, h_m=1.32, border=0.075):
    W_, H_ = 256, 512
    U, V = grid(W_, H_)
    x, y = U * w_m, V * h_m
    a = np.zeros((H_, W_, 4), np.float32)
    a[..., :3] = C(0.035, 0.037, 0.04)
    a[..., 3] = 1
    inner = sd_box(x, y, w_m / 2, h_m / 2, w_m / 2 - border, h_m / 2 - border)
    over(a, (0.93, 0.74, 0.08), sstep(-inner, 0.002) * (1 - 0.08 * noise(H_, W_, 3, 16)))
    return to_image('gfx_backplate', a)


def g_lens(kind, lit=False):
    S = 256
    U, V = grid(S, S)
    x, y = U - 0.5, V - 0.5
    r = np.hypot(x, y) / 0.5
    base = {'red': C(0.20, 0.035, 0.03), 'amber': C(0.24, 0.13, 0.02), 'green': C(0.02, 0.15, 0.12)}[kind]
    a = np.zeros((S, S, 4), np.float32)
    rings = 0.5 + 0.5 * np.cos(r * 26.0)
    if lit:
        hot = {'red': C(1.0, 0.36, 0.22), 'amber': C(1.0, 0.75, 0.25), 'green': C(0.4, 1.0, 0.85)}[kind]
        rim = {'red': C(0.92, 0.07, 0.04), 'amber': C(0.98, 0.55, 0.04), 'green': C(0.02, 0.85, 0.62)}[kind]
        t = np.clip(r, 0, 1)[..., None] ** 1.6
        col = hot * (1 - t) + rim * t
        dots = 0.5 + 0.5 * np.cos(U * 2 * np.pi * 22) * np.cos(V * 2 * np.pi * 22)
        col = col * (0.9 + 0.1 * dots[..., None]) * (0.93 + 0.07 * rings[..., None])
    else:
        col = base * (0.85 + 0.15 * rings[..., None])
        # a soft specular smear baked in (reads as glass on mobile, where the reflection probe is dim)
        hl = np.exp(-(((x + 0.16) / 0.12) ** 2 + ((y - 0.2) / 0.07) ** 2))
        col = col + 0.18 * hl[..., None]
    edge = sstep(r - 0.97, 0.01)
    a[..., :3] = col * edge[..., None] + C(0.03, 0.03, 0.03) * (1 - edge[..., None])
    a[..., 3] = 1
    return to_image(f'gfx_lens_{kind}_{int(lit)}', a)


def g_ped():
    S = 512
    U, V = grid(S, S)
    a = np.zeros((S, S, 4), np.float32)
    a[..., :3] = C(0.03, 0.03, 0.032)
    a[..., 3] = 1
    # lens window
    win = sd_box(U, V, 0.5, 0.5, 0.42, 0.42, 0.03)
    over(a, (0.055, 0.05, 0.045), sstep(win, 0.004))
    # upraised hand (Don't Walk), lit Portland orange with an LED dot pitch
    x, y = (U - 0.5) / 0.36, (V - 0.47) / 0.36
    d = sd_box(x, y, 0.0, -0.25, 0.42, 0.42, 0.18)
    for fx, top in ((-0.30, 0.55), (-0.10, 0.70), (0.10, 0.72), (0.30, 0.58)):
        d = np.minimum(d, sd_capsule(x, y, fx, 0.0, fx, top, 0.085))
    d = np.minimum(d, sd_capsule(x, y, -0.36, -0.30, -0.70, 0.10, 0.09))
    d = np.minimum(d, sd_box(x, y, 0.0, -0.75, 0.30, 0.18, 0.05))
    hand = sstep(d, 0.01)
    dots = (0.5 + 0.5 * np.cos(U * 2 * np.pi * 64) * np.cos(V * 2 * np.pi * 64))
    over(a, (1.0, 0.50, 0.08), hand * (0.75 + 0.25 * dots))
    return to_image('gfx_ped', a)


def g_button_sign():
    W_, H_ = 256, 336
    U, V = grid(W_, H_)
    a = np.zeros((H_, W_, 4), np.float32)
    a[..., :3] = C(0.92, 0.92, 0.9)
    a[..., 3] = 1
    over(a, (0.05, 0.05, 0.05), sstep(-sd_box(U, V, 0.5, 0.5, 0.44, 0.45, 0.04), 0.004) * 0)  # (kept flat white)
    border = np.abs(sd_box(U, V, 0.5, 0.5, 0.455, 0.465, 0.04)) - 0.012
    over(a, (0.05, 0.05, 0.05), sstep(border, 0.004))
    over(a, (0.05, 0.05, 0.05), text_mask('PUSH BUTTON', W_, H_, (0.1, 0.72, 0.9, 0.86), bold=0.05, cond=0.85))
    over(a, (0.05, 0.05, 0.05), text_mask('FOR WALK', W_, H_, (0.18, 0.55, 0.82, 0.68), bold=0.05, cond=0.85))
    arr = np.minimum(sd_box(U, V, 0.42, 0.33, 0.22, 0.045), sd_ngon(U, V, 0.70, 0.33, 0.13, 3, 0.0))
    over(a, (0.05, 0.05, 0.05), sstep(arr, 0.004))
    return to_image('gfx_btnsign', a)


def g_blade(text, w_m, h_m, name):
    W_ = 1024
    H_ = max(64, int(W_ * h_m / w_m))
    U, V = grid(W_, H_)
    x, y = U * w_m, V * h_m
    a = np.zeros((H_, W_, 4), np.float32)
    a[..., :3] = C(0.0, 0.40, 0.24) * (0.94 + 0.06 * noise(H_, W_, 7, 32))[..., None]
    a[..., 3] = 1
    bd = np.abs(sd_box(x, y, w_m / 2, h_m / 2, w_m / 2 - 0.018, h_m / 2 - 0.018, 0.02)) - 0.007
    over(a, (0.95, 0.95, 0.93), sstep(bd, 0.0015))
    over(a, (0.95, 0.95, 0.93), text_mask(text, W_, H_, (0.08, 0.2, 0.92, 0.8), bold=0.04, cond=0.9))
    return to_image(name, a)


def g_stop():
    S = 512
    U, V = grid(S, S)
    w = 0.762                       # 30 in flat to flat (R1-1)
    x, y = (U - 0.5) * w, (V - 0.5) * w
    R = (w / 2) / math.cos(math.pi / 8)
    d = sd_ngon(x, y, 0, 0, R, 8, math.pi / 8)
    a = np.zeros((S, S, 4), np.float32)
    red = C(0.72, 0.05, 0.07) * (0.95 + 0.05 * noise(S, S, 11, 24))[..., None]
    a[..., :3] = red
    a[..., 3] = 1
    ring = np.abs(d + 0.0127 + 0.0095) - 0.0095      # 3/4 in white border, 1/2 in red margin
    over(a, (0.95, 0.95, 0.94), sstep(ring, 0.0012))
    over(a, (0.95, 0.95, 0.94), text_mask('STOP', S, S, (0.5 - 0.36, 0.5 - 0.165, 0.5 + 0.36, 0.5 + 0.165), bold=0.035, cond=0.86))
    # light road grime towards the bottom edge
    g = noise(S, S, 5, 10) * np.clip(0.5 - V, 0, 1)
    a[..., :3] *= (1 - 0.18 * g)[..., None]
    return to_image('gfx_stop', a)


def g_holes(length_m, w_m=0.0508, name='gfx_holes'):
    """square perforated sign post: 7/16 in holes on 1 in centres down the middle of each face."""
    W_, H_ = 32, 2048
    U, V = grid(W_, H_)
    x, y = U * w_m, V * length_m
    yy = np.mod(y, 0.0254) - 0.0127
    d = np.hypot(x - w_m / 2, yy) - 0.0056
    a = np.zeros((H_, W_, 4), np.float32)
    a[..., :3] = C(0.02, 0.02, 0.02)
    a[..., 3] = sstep(d, 0.0008)
    return to_image(name, a)


# ------------------------------------------------------------------ 1. traffic light
def build_traffic_light():
    A = Asset('v3_traffic_light')
    galv = A.m(src_mat('tl_galv', 'Metal011', scale=1.2, rough=0.42, rough_var=0.5))
    paint = A.m(src_mat('tl_paint', 'Metal027', scale=0.6, tint=(0.014, 0.015, 0.017), rough=0.5, metal=0.0, nstr=0.6))
    bp = A.m(src_mat('tl_backplate', 'Metal027', scale=0.6, tint=(0.014, 0.015, 0.017), rough=0.45, metal=0.0, nstr=0.6,
                     gfx=g_backplate(), gfx_rough=0.35), boost=1.4)
    lr = A.m(src_mat('tl_lens_red', None, rough=0.15, metal=0.0, gfx=g_lens('red', True)), boost=2.2)
    la = A.m(src_mat('tl_lens_amber', None, rough=0.08, metal=0.0, gfx=g_lens('amber')), boost=2.2)
    lg = A.m(src_mat('tl_lens_green', None, rough=0.08, metal=0.0, gfx=g_lens('green')), boost=2.2)
    ped = A.m(src_mat('tl_ped', None, rough=0.2, metal=0.0, gfx=g_ped()), boost=2.0)
    bs = A.m(src_mat('tl_btnsign', None, rough=0.4, metal=0.0, gfx=g_button_sign()), boost=2.5)
    bl = A.m(src_mat('tl_blade', None, rough=0.35, metal=0.0, gfx=g_blade('BLOCK AVE', 1.4, 0.28, 'gfx_blade_tl')), boost=1.6)
    # foundation plate, anchor nuts, collar, tapered pole with a domed cap
    A.add(p_box(0.5, 0.5, 0.04, galv, bevel=0.008, z0=True, drop_bottom=True))
    for sx in (-1, 1):
        for sy in (-1, 1):
            A.add(p_lathe([(0.026, 0.04), (0.026, 0.072), (0.016, 0.08), (0.0, 0.08)], 6, galv, sharp=(1,)), T(sx * 0.19, sy * 0.19))
    A.add(p_lathe([(0.17, 0.04), (0.17, 0.09), (0.14, 0.15), (0.127, 0.165)], 16, galv, cap_top=False, sharp=(1,)))
    A.add(p_lathe([(0.127, 0.16), (0.105, 6.42), (0.092, 6.47), (0.06, 6.505), (0.0, 6.52)], 16, galv, sharp=(1,)))
    A.add(p_box(0.11, 0.03, 0.24, galv, bevel=0.005), T(0, -0.118, 0.6))                       # hand hole cover
    # mast-arm clamp and the tapered arm (Blender +X = Unity -X), rising 0.2 m over its 6.45 m
    for zb in (5.84, 6.18):
        A.add(p_lathe([(0.106, zb), (0.116, zb), (0.116, zb + 0.08), (0.106, zb + 0.08)], 16, galv, cap_top=False, sharp=(1, 2)))
    A.add(p_box(0.05, 0.34, 0.50, galv, bevel=0.008), T(0.14, 0, 6.05))
    x0, x1, z0_, z1_ = 0.16, 6.62, 6.05, 6.25
    A.add(p_tube([(x0, 0, z0_), (x1, 0, z1_)], 0.09, 12, galv, r_end=0.055))

    def arm(x):
        t = (x - x0) / (x1 - x0)
        return z0_ + (z1_ - z0_) * t, 0.09 + (0.055 - 0.09) * t
    for hx in (3.4, 6.0):
        za, ra = arm(hx)
        zc = 5.40                                           # head 4.85 .. 5.95, lowest lens bottom 4.89 m
        A.add(p_box(0.36, 0.26, 1.10, paint, bevel=0.014), T(hx, 0, zc))
        A.add(p_box(0.60, 0.016, 1.32, paint, bevel=0.003, faces={'-y': bp}), T(hx, 0.138, zc))
        A.add(p_box(0.16, 0.16, 0.03, paint, bevel=0.004), T(hx, 0, 5.965))
        A.add(p_lathe([(0.03, 5.98), (0.03, za - ra * 0.6)], 8, galv, cap_top=False))
        A.add(p_box(0.12, 0.14, 0.05, galv, bevel=0.006), T(hx, 0, za - ra * 0.75))
        for dz, mi in ((0.36, lr), (0.0, la), (-0.36, lg)):
            A.add(p_disc(0.15, 16, mi, depth=0.014, mi_side=paint), T(hx, -0.138, zc + dz))
            A.add(p_shell(0.165, 0.24, 8, paint, -28, 208), T(hx, -0.13, zc + dz))
    # pedestrian signal (faces +X: across the street the arm spans), push button (faces -Y), street-name blade
    A.add(p_box(0.07, 0.12, 0.12, paint, bevel=0.005), T(0.135, 0, 2.6))
    A.add(p_box(0.24, 0.46, 0.46, paint, bevel=0.012, faces={'+x': ped}), T(0.29, 0, 2.6))
    A.add(p_box(0.20, 0.48, 0.012, paint, bevel=0.002), T(0.50, 0, 2.836))
    for sy in (-1, 1):
        A.add(p_box(0.20, 0.012, 0.46, paint, bevel=0.002), T(0.50, sy * 0.236, 2.6))
    A.add(p_box(0.10, 0.07, 0.16, paint, bevel=0.008), T(0, -0.14, 1.04))
    A.add(p_disc(0.028, 12, galv, depth=0.014), T(0, -0.185, 1.02))
    A.add(p_box(0.23, 0.006, 0.30, galv, faces={'-y': bs}), T(0, -0.132, 1.32))
    za, ra = arm(1.55)
    for bx in (1.05, 2.05):
        A.add(p_box(0.05, 0.05, 0.10, galv, bevel=0.004), T(bx, 0, arm(bx)[0] + arm(bx)[1] + 0.03))
    A.add(p_box(1.40, 0.012, 0.28, galv, bevel=0.002, faces={'-y': bl, '+y': bl}), T(1.55, 0, za + ra + 0.08 + 0.14))
    o = A.finish()
    return finalize(A, o, 1024, lod1=540, ao_dist=0.35,
                    credit_tex=['ambientCG Metal011 and Metal027 (CC0, https://ambientcg.com)'])


# ====================================================================== preview renders
def _preview_mat(name):
    stem = name[2:]
    glass = stem.endswith('_glass')
    if glass:
        stem = stem[:-6]
    paths = {k: os.path.join(TEX, f'{stem}_{k}.png') for k in ('BaseColor', 'Normal', 'MaskMap')}
    if not os.path.exists(paths['BaseColor']):
        return None
    m = W.make_material(name + '__pv', paths, blend=glass)
    # preview only: multiply the baked AO (MaskMap G) into base colour, like URP Lit's occlusion on indirect light
    nt = m.node_tree
    b = _principled(m)
    tb = next(n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.filepath.endswith('_BaseColor.png'))
    tm = next(n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.filepath.endswith('_MaskMap.png'))
    sep = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(tm.outputs['Color'], sep.inputs[0])
    mix = nt.nodes.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    mix.blend_type = 'MULTIPLY'
    _sock(mix.inputs, 'Factor_Float').default_value = 0.85
    nt.links.new(tb.outputs['Color'], _sock(mix.inputs, 'A_Color'))
    nt.links.new(sep.outputs['Green'], _sock(mix.inputs, 'B_Color'))
    nt.links.new(_sock(mix.outputs, 'Result_Color'), b.inputs['Base Color'])
    if glass:
        m.surface_render_method = 'BLENDED'
    return m


def import_prop(pid, lod=0):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, pid + '.fbx'))
    new = [o for o in bpy.data.objects if o not in before]
    keep = None
    for o in new:
        if o.type == 'MESH' and o.name.startswith(f'{pid}_LOD{lod}'):
            keep = o
    for o in new:
        if o is not keep:
            bpy.data.objects.remove(o)
    keep.parent = None
    for s in keep.material_slots:
        if s.material:
            pm = _preview_mat(s.material.name.split('.')[0])
            if pm:
                s.material = pm
    return keep


def _scene_setup(res=(1600, 900)):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'AgX'
    try:
        sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass
    try:
        sc.eevee.use_shadows = True
        sc.eevee.use_raytracing = True
    except Exception:
        pass
    if not sc.world:
        sc.world = bpy.data.worlds.new('pv')
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes['Background']
    bg.inputs[0].default_value = (0.42, 0.50, 0.66, 1)      # cool sky fill
    bg.inputs[1].default_value = 0.55
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 5.5
    sun.data.color = (1.0, 0.70, 0.42)                        # warm golden-hour key, 17 deg above the horizon
    sun.data.angle = math.radians(1.0)
    sun.rotation_euler = (math.radians(73), 0, math.radians(-50))
    sc.collection.objects.link(sun)
    me = bpy.data.meshes.new('floor')
    s = 60
    me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    fl = bpy.data.objects.new('floor', me)
    sc.collection.objects.link(fl)
    fm = bpy.data.materials.new('floor')
    fm.use_nodes = True
    _principled(fm).inputs['Base Color'].default_value = (0.20, 0.20, 0.20, 1)
    fl.scale = (8, 8, 1)
    _principled(fm).inputs['Roughness'].default_value = 0.9
    me.materials.append(fm)
    return sc


def human_box(x, y):
    me = bpy.data.meshes.new('human')
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * 0.45, v.co.y * 0.25, (v.co.z + 0.5) * 1.8))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new('human_1.8m', me)
    o.location = (x, y, 0)
    m = bpy.data.materials.new('human')
    m.use_nodes = True
    _principled(m).inputs['Base Color'].default_value = (0.05, 0.25, 0.6, 1)
    me.materials.append(m)
    bpy.context.scene.collection.objects.link(o)
    return o


def cam_to(sc, target, dist, elev_deg, azim_deg, lens=50, name='cam'):
    cam = bpy.data.objects.get(name) or bpy.data.objects.new(name, bpy.data.cameras.new(name))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cam.data.lens = lens
    e, a = math.radians(elev_deg), math.radians(azim_deg)
    d = Vector((math.cos(e) * math.sin(a), -math.cos(e) * math.cos(a), math.sin(e)))
    cam.location = Vector(target) + d * dist
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    return cam


def render_to(sc, path):
    sc.render.filepath = path
    sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)


PV_DIR = os.path.join(os.path.dirname(SHEET), 'tiles')


def render_view(pid, path, azim=-32, elev=14, lod=0, target=None, dist=None, human=True, res=(900, 900), lens=50):
    W.reset()
    sc = _scene_setup(res)
    o = import_prop(pid, lod)
    vs = np.array([o.matrix_world @ v.co for v in o.data.vertices])
    mn, mx = vs.min(0), vs.max(0)
    allmn, allmx = mn.copy(), mx.copy()
    if human:
        hb = human_box(mx[0] + 0.55, (mn[1] + mx[1]) / 2)
        allmx = np.maximum(mx, (hb.location.x + 0.23, hb.location.y + 0.13, 1.8))
        allmn = np.minimum(mn, (hb.location.x - 0.23, hb.location.y - 0.13, 0))
    c = (allmn + allmx) / 2 if target is None else np.array(target)
    diag = float(np.linalg.norm(allmx - allmn))
    cam_to(sc, c, dist or (diag * 1.45 + 0.5), elev, azim, lens=lens)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    render_to(sc, path)
    return path


ALL_IDS = ['v3_traffic_light', 'v3_stop_sign', 'v3_bus_shelter', 'v3_bollard', 'v3_bike_rack', 'v3_vendor_cart', 'v3_manhole',
           'v3_storm_drain', 'v3_trash_bin', 'v3_cafe_set']


def render_overview(path):
    """every prop in a golden-hour street line-up (small props in front, big ones behind) with the 1.8 m reference box."""
    W.reset()
    sc = _scene_setup((2400, 1100))
    rows = [(4.2, ['v3_vendor_cart', 'v3_bus_shelter', 'v3_traffic_light'], 1.2),
            (0.0, ['v3_bollard', 'v3_bike_rack', 'v3_trash_bin', 'v3_stop_sign', 'v3_cafe_set', 'v3_manhole', 'v3_storm_drain'], 0.6)]
    width = {}
    placed = []
    for y, ids, gap in rows:
        objs = []
        x = 0.0
        for pid in ids:
            o = import_prop(pid)
            vs = np.array([v.co[:] for v in o.data.vertices])
            mn, mx = vs.min(0), vs.max(0)
            x += -mn[0]
            o.location = (x, y, 0)
            objs.append(o)
            x += mx[0] + gap
        width[y] = x - gap
        placed.append((y, objs))
        if y == 0.0:
            hb = human_box(x + 0.1, 0.0)
            objs.append(hb)
            width[y] += 0.6
    W_ = max(width.values())
    for y, objs in placed:
        for o in objs:
            o.location.x += (W_ - width[y]) / 2 - W_ / 2
    cam_to(sc, (0, 2.0, 1.9), W_ * 1.05 + 4, 12, -6, lens=35)
    render_to(sc, path)


def render_sheet():
    import importlib
    import montage as MT
    importlib.reload(MT)
    os.makedirs(PV_DIR, exist_ok=True)
    items = []
    ov = os.path.join(PV_DIR, '_overview.png')
    render_overview(ov)
    views = [('v3_traffic_light', {}, 'traffic light'),
             ('v3_traffic_light', dict(target=(4.7, 0, 5.45), dist=5.2, elev=4, azim=-24, human=False), 'signal heads'),
             ('v3_traffic_light', dict(target=(0.15, 0, 1.9), dist=3.4, elev=8, azim=58, human=False), 'pole: ped signal, push button'),
             ('v3_stop_sign', dict(azim=-24), 'stop sign'),
             ('v3_bus_shelter', dict(azim=-28), 'bus shelter'),
             ('v3_bus_shelter', dict(azim=150, elev=12), 'shelter rear, ad box'),
             ('v3_vendor_cart', dict(azim=-30), 'vendor cart'),
             ('v3_bollard', dict(azim=-30, elev=10), 'bollard'),
             ('v3_bike_rack', dict(azim=-40), 'bike rack'),
             ('v3_trash_bin', dict(azim=-30, elev=12), 'trash bin (Poly Haven)'),
             ('v3_cafe_set', dict(azim=-35, elev=16), 'cafe set (Poly Haven)'),
             ('v3_manhole', dict(azim=-20, elev=55, human=False, dist=1.6), 'manhole decal'),
             ('v3_storm_drain', dict(azim=-20, elev=55, human=False, dist=1.6), 'storm drain decal'),
             ('v3_traffic_light', dict(lod=1), 'traffic light LOD1'),
             ('v3_bus_shelter', dict(lod=1, azim=-28), 'bus shelter LOD1'),
             ('v3_vendor_cart', dict(lod=1, azim=-30), 'vendor cart LOD1')]
    for i, (pid, kw, label) in enumerate(views):
        p = os.path.join(PV_DIR, f'{i:02d}_{pid}.png')
        render_view(pid, p, **kw)
        items.append((p, label))
    tiles = os.path.join(PV_DIR, '_tiles.png')
    MT.montage(tiles, 4, 600, items, title='CT LIFE V3 STREET PROPS. EEVEE, GOLDEN HOUR, 1.8 M BLUE BOX', font_size=30)
    import OpenImageIO as oiio
    from OpenImageIO import ImageBuf, ImageBufAlgo, ROI
    a, b = ImageBuf(ov), ImageBuf(tiles)
    wa = a.spec().width
    wb = b.spec().width
    a2 = ImageBufAlgo.resize(a, roi=ROI(0, wb, 0, int(a.spec().height * wb / wa), 0, 1, 0, 3))
    out = ImageBuf(oiio.ImageSpec(wb, a2.spec().height + b.spec().height, 3, oiio.UINT8))
    ImageBufAlgo.paste(out, 0, 0, 0, 0, a2)
    ImageBufAlgo.paste(out, 0, a2.spec().height, 0, 0, b)
    out.write(SHEET)
    log('sheet', SHEET)


BUILD = {}


def run_build(ids):
    man = {}
    for key, fn in BUILD.items():
        if ids and key not in ids:
            continue
        global _FONT
        W.reset()
        _FONT = None
        log('==== build', key)
        r = fn()
        if isinstance(r, tuple):
            man[key] = r[0]
        else:
            man.update(r)
    for v in man.values():
        v['family'] = 'v3_street'
    if man:
        W.write_manifest(os.path.join(OUT, 'manifest.json'), man)
    return man



# ------------------------------------------------------------------ 2. stop sign
def build_stop_sign():
    A = Asset('v3_stop_sign')
    H = 2.65
    galv = A.m(src_mat('ss_galv', 'Metal011', scale=0.8, rough=0.42, rough_var=0.5))
    post = A.m(src_mat('ss_post', 'Metal011', scale=0.8, rough=0.42, rough_var=0.5, gfx=g_holes(H - 0.012), gfx_rough=0.9, gfx_metal=0.0), boost=1.6)
    alu = A.m(src_mat('ss_alu', 'Metal011', scale=0.5, tint=(0.55, 0.56, 0.57), tint_k=0.8, rough=0.5, rough_var=0.4))
    face = A.m(src_mat('ss_face', 'Metal027', scale=0.4, tint=(0.5, 0.5, 0.5), tint_k=0.0, rough=0.38, rough_var=0.2, metal=0.0, nstr=0.25,
                       gfx=g_stop(), gfx_rough=0.36), boost=2.2)
    A.add(p_box(0.0508, 0.0508, H - 0.012, galv, bevel=0.003, z0=True, drop_bottom=True,
                faces={'-y': post, '+y': post, '-x': post, '+x': post}))
    A.add(p_box(0.064, 0.064, 0.22, galv, bevel=0.003, z0=True, drop_bottom=True))           # anchor sleeve
    A.add(p_box(0.058, 0.058, 0.012, galv, bevel=0.002), T(0, 0, H - 0.006))                # post cap
    w = 0.762
    R = (w / 2) / math.cos(math.pi / 8)
    pts = [(R * math.cos(math.pi / 8 + k * math.pi / 4), 2.2 + R * math.sin(math.pi / 8 + k * math.pi / 4)) for k in range(8)]
    A.add(p_prism(pts, 0.004, face, alu, alu), T(0, -0.0298, 0))
    for dz in (0.33, -0.33):
        A.add(p_disc(0.011, 8, galv, depth=0.006), T(0, -0.0335, 2.2 + dz))
    o = A.finish()
    return finalize(A, o, 512, lod1=60, ao_dist=0.2,
                    credit_tex=['ambientCG Metal011 and Metal027 (CC0, https://ambientcg.com)'])


# ------------------------------------------------------------------ 4. bollard
def build_bollard():
    A = Asset('v3_bollard')
    iron = A.m(src_mat('bo_iron', 'Metal027', scale=0.35, tint=(0.012, 0.013, 0.014), tint_k=1.0, rough=0.42, rough_var=0.5, metal=0.0, nstr=1.0))
    band = A.m(src_mat('bo_band', 'Metal027', scale=0.35, tint=(0.75, 0.75, 0.74), rough=0.3, rough_var=0.3, metal=0.0, nstr=0.5))
    prof = [(0.10, 0.0), (0.10, 0.022), (0.093, 0.032), (0.086, 0.055), (0.080, 0.09), (0.080, 0.70), (0.080, 0.76),
            (0.080, 0.80), (0.088, 0.808), (0.088, 0.835), (0.080, 0.845), (0.079, 0.868), (0.068, 0.905), (0.046, 0.935),
            (0.022, 0.949), (0.0, 0.952)]
    mats = [iron] * (len(prof) - 1)
    mats[5] = band
    A.add(p_lathe(prof, 16, iron, mats=mats, sharp=(1, 4, 7, 8, 9, 10)))
    o = A.finish()
    return finalize(A, o, 256, lod1=100, ao_dist=0.15,
                    credit_tex=['ambientCG Metal027 (CC0, https://ambientcg.com)'])


# ------------------------------------------------------------------ 5. bike rack
def build_bike_rack():
    A = Asset('v3_bike_rack')
    galv = A.m(src_mat('br_galv', 'Metal011', scale=0.6, rough=0.4, rough_var=0.5))
    r, half, top, cr = 0.024, 0.35, 0.85 - 0.024, 0.17
    for x in (-0.8, 0.0, 0.8):
        path = [(x, -half, -0.02)]
        for k in range(5):
            a = math.pi - k * (math.pi / 2) / 4
            path.append((x, -half + cr + cr * math.cos(a), top - cr + cr * math.sin(a)))
        for k in range(5):
            a = math.pi / 2 - k * (math.pi / 2) / 4
            path.append((x, half - cr + cr * math.cos(a), top - cr + cr * math.sin(a)))
        path.append((x, half, -0.02))
        A.add(p_tube(path, r, 8, galv, caps=False))
        for y in (-half, half):
            A.add(p_lathe([(0.05, 0.0), (0.05, 0.006), (0.026, 0.013)], 8, galv, cap_top=False, sharp=(1,)), T(x, y, 0))
    o = A.finish()
    return finalize(A, o, 256, lod1=200, ao_dist=0.2,
                    credit_tex=['ambientCG Metal011 (CC0, https://ambientcg.com)'])


# ------------------------------------------------------------------ 3. bus shelter
def g_glass():
    W_, H_ = 256, 512
    U, V = grid(W_, H_)
    z = 0.14 + V * 2.16
    a = np.zeros((H_, W_, 4), np.float32)
    a[..., :3] = C(0.62, 0.70, 0.72)
    a[..., 3] = 0.26 + 0.12 * np.clip((0.6 - z) / 0.5, 0, 1) * noise(H_, W_, 21, 16)       # grime at the bottom
    band = (z > 1.32) & (z < 1.47)
    stripe = (np.mod((z - 1.32) / 0.03, 1.0) < 0.5) & band
    a[stripe, :3] = C(0.86, 0.88, 0.88)
    a[stripe, 3] = 0.68
    return to_image('gfx_glass', a)


def g_bus_sign():
    W_, H_ = 1024, 102
    U, V = grid(W_, H_)
    a = np.zeros((H_, W_, 4), np.float32)
    a[..., :3] = C(0.02, 0.22, 0.48)
    a[..., 3] = 1
    over(a, (0.97, 0.97, 0.95), text_mask('BUS STOP', W_, H_, (0.2, 0.18, 0.8, 0.82), bold=0.03, cond=1.0))
    return to_image('gfx_bussign', a)


def ad_textures():
    """neutral placeholder poster for M_v3_bus_shelter_ad (the game's own ad art replaces it): 512 x 1024."""
    W_, H_ = 512, 1024
    U, V = grid(W_, H_)
    a = np.zeros((H_, W_, 4), np.float32)
    top, bot = C(0.93, 0.90, 0.84), C(0.80, 0.84, 0.88)
    a[..., :3] = bot * (1 - V[..., None]) + top * V[..., None]
    a[..., 3] = 1
    over(a, (0.95, 0.62, 0.30), sstep(sd_circle(U, V * 2.0, 0.56, 1.25, 0.30), 0.004))
    over(a, (0.24, 0.32, 0.42), sstep(sd_box(U, V, 0.5, 0.2, 0.36, 0.022, 0.01), 0.003))
    over(a, (0.24, 0.32, 0.42), 0.6 * sstep(sd_box(U, V, 0.42, 0.14, 0.28, 0.012, 0.006), 0.003))
    nrm = np.zeros((H_, W_, 3), np.float32)
    nrm[...] = (0.5, 0.5, 1.0)
    mask = np.zeros((H_, W_, 4), np.float32)
    mask[..., 1] = 1.0
    mask[..., 3] = 0.82
    paths = {k: os.path.join(TEX, f'v3_bus_shelter_ad_{k}.png') for k in ('BaseColor', 'Normal', 'MaskMap')}
    os.makedirs(TEX, exist_ok=True)
    W.save_png(a, paths['BaseColor'], 4)
    W.save_png(nrm, paths['Normal'], 3)
    W.save_png(mask, paths['MaskMap'], 4)
    return paths


def build_bus_shelter():
    A = Asset('v3_bus_shelter')
    frame = A.m(src_mat('bs_frame', 'Metal027', scale=0.6, tint=(0.032, 0.034, 0.037), rough=0.45, rough_var=0.4, metal=0.0, nstr=0.7))
    roof = A.m(src_mat('bs_roof', 'Metal011', scale=1.0, tint=(0.30, 0.31, 0.32), tint_k=0.85, rough=0.5, rough_var=0.4))
    glass = A.m(src_mat('bs_glass', None, rough=0.05, metal=0.0, gfx=g_glass(), gfx_mode='glass'), boost=0.45, glass=True)
    wood = A.m(src_mat('bs_wood', 'Wood049', scale=1.0, rough=0.55, rough_var=0.4, nstr=0.8))
    light = A.m(src_mat('bs_light', None, tint=(0.95, 0.93, 0.85), rough=0.3, metal=0.0))
    sign = A.m(src_mat('bs_sign', None, rough=0.35, metal=0.0, gfx=g_bus_sign()), boost=2.0)
    ad = A.m(src_mat('bs_ad', None, rough=0.2, metal=0.0), ad=True)
    # posts (open side Blender -Y = Unity +Z; ad box at Blender -X = Unity +X; side glass at Blender +X = Unity -X)
    for (x, y) in ((-2.1, 0.62), (0.0, 0.62), (2.1, 0.62), (-2.1, -0.62), (2.1, -0.50)):
        A.add(p_box(0.08, 0.08, 2.44, frame, bevel=0.006, z0=True, drop_bottom=True), T(x, y, 0))
        A.add(p_box(0.16, 0.16, 0.012, frame, bevel=0.003, z0=True, drop_bottom=True), T(x, y, 0))
    A.add(p_box(4.5, 1.6, 0.14, frame, bevel=0.012, faces={'+z': roof}), T(0, 0.0, 2.51))
    A.add(p_box(3.8, 0.06, 0.012, light), T(0, -0.40, 2.434))
    A.add(p_box(1.0, 0.006, 0.10, frame, faces={'-y': sign}), T(0, -0.803, 2.51))
    for x in (-1.05, 1.05):
        A.add(p_box(2.02, 0.012, 2.16, glass, faces={'-y': glass, '+y': glass}), T(x, 0.62, 1.22))
    A.add(p_box(4.12, 0.05, 0.05, frame, bevel=0.004), T(0, 0.62, 2.33))
    A.add(p_box(4.12, 0.05, 0.04, frame, bevel=0.004), T(0, 0.62, 0.12))
    A.add(p_box(0.012, 1.04, 2.16, glass, faces={'-x': glass, '+x': glass}), T(2.1, 0.06, 1.22))
    A.add(p_box(0.05, 1.04, 0.05, frame, bevel=0.004), T(2.1, 0.06, 2.33))
    A.add(p_box(0.05, 1.04, 0.04, frame, bevel=0.004), T(2.1, 0.06, 0.12))
    # illuminated advertising box (end wall), poster faces on both sides use their own material slot
    A.add(p_box(0.13, 1.16, 1.88, frame, bevel=0.01), T(-2.1, 0.0, 1.06))
    for sx in (-1, 1):
        A.add(p_box(0.006, 1.06, 1.76, frame, faces={('-x' if sx < 0 else '+x'): ad}), T(-2.1 + sx * 0.068, 0.0, 1.06))
    # slim bench: three wooden slats on two steel cantilevers, plus a lean rail on the back rail
    for y in (0.24, 0.345, 0.45):
        A.add(p_box(2.4, 0.09, 0.034, wood, bevel=0.005), T(0, y, 0.457))
    for x in (-0.95, 0.95):
        A.add(p_box(0.05, 0.40, 0.035, frame, bevel=0.004), T(x, 0.38, 0.423))
        A.add(p_box(0.05, 0.05, 0.42, frame, bevel=0.004, z0=True, drop_bottom=True), T(x, 0.52, 0))
    A.add(p_box(2.4, 0.06, 0.09, wood, bevel=0.006), T(0, 0.565, 0.86))
    o = A.finish()
    return finalize(A, o, 1024, lod1=260, ao_dist=0.8, ad_paths=ad_textures(),
                    credit_tex=['ambientCG Metal027, Metal011 and Wood049 (CC0, https://ambientcg.com)'])


# ------------------------------------------------------------------ 6. vendor cart
def g_menu():
    W_, H_ = 1024, 392
    U, V = grid(W_, H_)
    a = np.zeros((H_, W_, 4), np.float32)
    a[..., :3] = C(0.07, 0.075, 0.08) * (0.9 + 0.1 * noise(H_, W_, 31, 24))[..., None]
    a[..., 3] = 1
    fr = np.abs(sd_box(U, V, 0.5, 0.5, 0.485, 0.46, 0.0)) - 0.012
    over(a, (0.36, 0.22, 0.12), sstep(fr, 0.003))
    over(a, (0.93, 0.91, 0.86), text_mask('COFFEE', W_, H_, (0.15, 0.45, 0.85, 0.85), bold=0.03, cond=1.0))
    over(a, (0.90, 0.78, 0.55), text_mask('ESPRESSO  TEA  PASTRIES', W_, H_, (0.12, 0.16, 0.88, 0.32), bold=0.02, cond=1.0))
    return to_image('gfx_menu', a)


def build_vendor_cart():
    A = Asset('v3_vendor_cart')
    steel = A.m(src_mat('vc_steel', 'Metal009', scale=0.8, rough=0.32, rough_var=0.4))
    dark = A.m(src_mat('vc_dark', 'Metal027', scale=0.5, tint=(0.02, 0.021, 0.022), rough=0.5, metal=0.0))
    wood = A.m(src_mat('vc_wood', 'Wood049', scale=1.0, rough=0.5, rough_var=0.4))
    tyre = A.m(src_mat('vc_tyre', 'Rubber004', scale=0.3, tint=(0.018, 0.018, 0.018), rough=0.8, rough_var=0.3))
    menu = A.m(src_mat('vc_menu', None, rough=0.7, metal=0.0, gfx=g_menu()), boost=1.8)
    red = A.m(src_mat('vc_canvas_red', 'Fabric061', scale=0.35, tint=(0.42, 0.035, 0.03), rough=0.85, rough_var=0.2))
    cream = A.m(src_mat('vc_canvas_cream', 'Fabric061', scale=0.35, tint=(0.72, 0.64, 0.50), rough=0.85, rough_var=0.2))
    A.add(p_box(1.9, 0.78, 0.72, steel, bevel=0.015), T(0, 0, 0.64))
    A.add(p_box(1.96, 0.70, 0.05, dark, bevel=0.006), T(0, 0, 0.265))
    A.add(p_box(1.1, 0.008, 0.42, dark, faces={'-y': menu}), T(-0.2, -0.394, 0.66))
    for x in (-0.94, 0.94):
        A.add(p_box(0.03, 0.80, 0.70, steel, bevel=0.006), T(x, 0, 0.64))
    A.add(p_box(2.04, 0.88, 0.04, wood, bevel=0.008), T(0, 0, 1.02))
    for y in (-0.455, 0.455):           # wheels (axle along Y) at the +X end
        A.add(p_lathe([(0.13, -0.04), (0.22, -0.046), (0.255, -0.032), (0.27, 0.0), (0.255, 0.032), (0.22, 0.046), (0.13, 0.04)],
                      16, tyre, cap_top=False), T(0.5, y, 0.27, rx=90))
        A.add(p_lathe([(0.135, 0.035), (0.10, 0.042), (0.04, 0.05), (0.0, 0.052)], 12, steel, sharp=(1,)),
              T(0.5, y, 0.27, rx=(90 if y < 0 else -90)))
    for y in (-0.28, 0.28):             # legs at the -X end
        A.add(p_lathe([(0.02, 0.03), (0.02, 0.26)], 8, steel, cap_top=False), T(-0.8, y, 0))
        A.add(p_lathe([(0.04, 0.0), (0.04, 0.03), (0.0, 0.035)], 8, dark), T(-0.8, y, 0))
    A.add(p_tube([(-0.95, -0.26, 0.78), (-1.17, -0.26, 0.80), (-1.17, 0.26, 0.80), (-0.95, 0.26, 0.78)], 0.016, 8, steel))
    for (x, y) in ((0.45, 0.2), (0.75, 0.2)):   # two hot-drink urns
        A.add(p_lathe([(0.11, 1.04), (0.11, 1.38), (0.095, 1.40)], 12, steel, cap_top=False, sharp=(1,)), T(x, y, 0))
        A.add(p_lathe([(0.095, 1.40), (0.06, 1.425), (0.02, 1.44), (0.0, 1.45)], 12, dark, sharp=(0,)), T(x, y, 0))
        A.add(p_box(0.03, 0.05, 0.04, dark), T(x, y - 0.12, 1.12))
    # market umbrella: pole, eight alternating gores (double-sided), valance, finial
    ux, uy = -0.15, 0.1
    A.add(p_lathe([(0.019, 1.04), (0.019, 2.45)], 8, steel, cap_top=False), T(ux, uy, 0))
    prof = [(1.15, 2.12), (0.62, 2.31), (0.08, 2.43), (0.0, 2.44)]
    for inner in (False, True):
        pr = [(r * (0.995 if inner else 1.0), z - (0.012 if inner else 0.0)) for r, z in prof]
        bm = p_lathe(pr, 8, red, cap_top=False, a0=0.0)
        val = p_lathe([(1.15 * (0.993 if inner else 1.0), 1.99), (1.15 * (0.993 if inner else 1.0), 2.12 - (0.012 if inner else 0))], 8, red, cap_top=False)
        for b_ in (bm, val):
            for f in b_.faces:
                c = f.calc_center_median()
                k = int(math.floor((math.atan2(c.y, c.x) % (2 * math.pi)) / (math.pi / 4)))
                f.material_index = red if k % 2 == 0 else cream
                f.smooth = False
            if inner:
                bmesh.ops.reverse_faces(b_, faces=b_.faces[:])
            A.add(b_, T(ux, uy, 0, rz=22.5))
    A.add(p_lathe([(0.022, 2.43), (0.03, 2.46), (0.0, 2.50)], 8, dark), T(ux, uy, 0))
    o = A.finish()
    return finalize(A, o, 1024, lod1=330, ao_dist=0.5,
                    credit_tex=['ambientCG Metal009, Metal027, Wood049, Rubber004 and Fabric061 (CC0, https://ambientcg.com)'])


# ------------------------------------------------------------------ 7. decals: manhole cover, storm drain
def _acg_layers(aid):
    out = {}
    for k, cs in (('Color', 'sRGB'), ('NormalGL', 'Non-Color'), ('Roughness', 'Non-Color'), ('Metalness', 'Non-Color'),
                  ('AmbientOcclusion', 'Non-Color')):
        p = acg_file(aid, k)
        if p:
            img, a = load_px(p, cs)
            out[k] = a.copy()
            bpy.data.images.remove(img)
    return out


def _fill_median(L, box, ring=8):
    """box = (x0, y0, x1, y1) in pixel coords, rows TOP-DOWN as in an image viewer."""
    for k, a in L.items():
        H_ = a.shape[0]
        x0, y0, x1, y1 = box
        r0, r1 = H_ - y1, H_ - y0
        rg = np.concatenate([a[max(0, r0 - ring):r0, x0:x1].reshape(-1, a.shape[2]), a[r1:r1 + ring, x0:x1].reshape(-1, a.shape[2]),
                             a[r0:r1, max(0, x0 - ring):x0].reshape(-1, a.shape[2]), a[r0:r1, x1:x1 + ring].reshape(-1, a.shape[2])])
        med = np.median(rg, axis=0)
        if k == 'NormalGL':
            med[:3] = (0.5, 0.5, 1.0)
        n = noise(r1 - r0, x1 - x0, 77, 6)[..., None]
        a[r0:r1, x0:x1] = med * (0.92 + 0.16 * n) if k == 'Color' else med


def _shift_copy(L, box, dx, dy):
    """replace box (top-down pixel coords) with the scan translated by (dx, dy) px, feathered at the edges."""
    x0, y0, x1, y1 = box
    for k, a in L.items():
        H_ = a.shape[0]
        dst = a[H_ - y1:H_ - y0, x0:x1]
        src = a[H_ - y1 - dy:H_ - y0 - dy, x0 + dx:x1 + dx].copy()
        h, w = dst.shape[:2]
        fy = np.minimum(np.arange(h), np.arange(h)[::-1])[:, None]
        fx = np.minimum(np.arange(w), np.arange(w)[::-1])[None, :]
        f = np.clip(np.minimum(fx, fy) / 6.0, 0, 1)[..., None]
        a[H_ - y1:H_ - y0, x0:x1] = dst * (1 - f) + src * f


def _rot_copy(L, box, cx, cy, deg):
    """replace box (top-down pixel coords) with the scan rotated by deg about (cx, cy) (same ring of a round cover)."""
    t = math.radians(deg)
    x0, y0, x1, y1 = box
    X, Y = np.meshgrid(np.arange(x0, x1), np.arange(y0, y1))
    dx, dy = X - cx, Y - cy
    sx = (cx + dx * math.cos(t) - dy * math.sin(t)).astype(int)
    sy = (cy + dx * math.sin(t) + dy * math.cos(t)).astype(int)
    for k, a in L.items():
        H_ = a.shape[0]
        src = a[H_ - 1 - sy, sx].copy()
        if k == 'NormalGL':      # rotate the tangent-space xy with the copy (image y is flipped)
            nx, ny = src[..., 0] * 2 - 1, src[..., 1] * 2 - 1
            c_, s_ = math.cos(-t), math.sin(-t)
            src[..., 0] = (nx * c_ - ny * s_) * 0.5 + 0.5
            src[..., 1] = (nx * s_ + ny * c_) * 0.5 + 0.5
        a[H_ - 1 - Y, X] = src


def _decal_set(stem, L, win, w, h):
    x0, y0, x1, y1 = win
    base = resample(L['Color'], w, h, x0, y0, x1, y1)
    base[..., 3] = 1
    nrm = resample(L['NormalGL'], w, h, x0, y0, x1, y1)[..., :3]
    n = nrm * 2 - 1
    n /= np.linalg.norm(n, axis=2, keepdims=True) + 1e-6
    nrm = n * 0.5 + 0.5
    mask = np.zeros((h, w, 4), np.float32)
    if 'Metalness' in L:
        mask[..., 0] = resample(L['Metalness'], w, h, x0, y0, x1, y1)[..., 0]
    mask[..., 1] = resample(L['AmbientOcclusion'], w, h, x0, y0, x1, y1)[..., 0] if 'AmbientOcclusion' in L else 1.0
    mask[..., 3] = 1 - resample(L['Roughness'], w, h, x0, y0, x1, y1)[..., 0]
    paths = {k: os.path.join(TEX, f'{stem}_{k}.png') for k in ('BaseColor', 'Normal', 'MaskMap')}
    os.makedirs(TEX, exist_ok=True)
    W.save_png(base, paths['BaseColor'], 4)
    W.save_png(nrm, paths['Normal'], 3)
    W.save_png(mask, paths['MaskMap'], 4)
    return paths


def _decal_mesh(name, verts, uvs, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], [list(range(len(verts)))])
    uvl = me.uv_layers.new(name='UVMap')
    for li, uv in zip(me.polygons[0].loop_indices, uvs):
        uvl.data[li].uv = uv
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    triangulate(o)
    return o


def build_decals():
    out = {}
    # manhole: ambientCG ManholeCover005 (photogrammetry), cover + frame ring fill the 0.8 m disc
    L = _acg_layers('ManholeCover005')
    cx, cy, R = 1007, 1027, 972                      # cover centre / outer frame radius, px (top-down)
    _shift_copy(L, (925, 980, 1092, 1076), -330, 0)  # centre foundry logo -> studded surface from 330 px to the left
    _rot_copy(L, (1515, 1530, 1655, 1700), cx, cy, -24)   # rim foundry name + logo -> rim copied from 24 deg round
    _rot_copy(L, (1845, 1110, 1935, 1250), cx, cy, -18)   # rim certification mark
    H_, W_ = L['Color'].shape[:2]
    win = ((cx - R) / W_, 1 - (cy + R) / H_, (cx + R) / W_, 1 - (cy - R) / H_)
    paths = _decal_set('v3_manhole', L, win, 512, 512)
    mat = W.make_material('M_v3_manhole', paths)
    lods = []
    for n, nm in ((16, '_LOD0'), (8, '_LOD1')):
        vs = [(0.4 * math.cos(2 * math.pi * i / n), 0.4 * math.sin(2 * math.pi * i / n), 0.01) for i in range(n)]
        uvs = [(0.5 + 0.5 * math.cos(2 * math.pi * i / n), 0.5 + 0.5 * math.sin(2 * math.pi * i / n)) for i in range(n)]
        lods.append(_decal_mesh('v3_manhole' + nm, vs, uvs, mat))
    W.export_fbx(lods, os.path.join(OUT, 'v3_manhole.fbx'))
    out['v3_manhole'] = dict(fbx='world_v3/v3_street/v3_manhole.fbx', kind='prop', tris=[W.tris(o) for o in lods], size_m=[0.8, 0.8, 0.01],
                             materials=['M_v3_manhole'], textures=[os.path.basename(p) for p in paths.values()],
                             tags=['prop', 'kit:v3', 'street', 'decal'],
                             source='https://ambientcg.com/view?id=ManholeCover005', source_uid='acg_ManholeCover005', licence='CC0',
                             credit='"Manhole Cover 005" by ambientCG / Lennart Demes (https://ambientcg.com/view?id=ManholeCover005), CC0; '
                                    'modified by CT Life (cropped, foundry marks painted out)')
    # storm drain: ambientCG Grate002 (photogrammetry), frame cropped, foundry name painted out
    L = _acg_layers('Grate002')
    H_, W_ = L['Color'].shape[:2]
    _fill_median(L, (int(0.103 * W_), int(0.32 * H_), int(0.168 * W_), int(0.69 * H_)), ring=6)
    win = (0.085, 1 - 0.772, 0.932, 1 - 0.219)
    paths = _decal_set('v3_storm_drain', L, win, 512, 256)
    mat = W.make_material('M_v3_storm_drain', paths)
    sx, sy = 0.86, 0.86 * (win[3] - win[1]) / (win[2] - win[0])
    vs = [(-sx / 2, -sy / 2, 0.01), (sx / 2, -sy / 2, 0.01), (sx / 2, sy / 2, 0.01), (-sx / 2, sy / 2, 0.01)]
    uvs = [(0, 0), (1, 0), (1, 1), (0, 1)]
    lods = [_decal_mesh('v3_storm_drain_LOD0', vs, uvs, mat), _decal_mesh('v3_storm_drain_LOD1', vs, uvs, mat)]
    W.export_fbx(lods, os.path.join(OUT, 'v3_storm_drain.fbx'))
    out['v3_storm_drain'] = dict(fbx='world_v3/v3_street/v3_storm_drain.fbx', kind='prop', tris=[2, 2], size_m=[round(sx, 3), round(sy, 3), 0.01],
                                 materials=['M_v3_storm_drain'], textures=[os.path.basename(p) for p in paths.values()],
                                 tags=['prop', 'kit:v3', 'street', 'decal'],
                                 source='https://ambientcg.com/view?id=Grate002', source_uid='acg_Grate002', licence='CC0',
                                 credit='"Grate 002" by ambientCG / Lennart Demes (https://ambientcg.com/view?id=Grate002), CC0; '
                                        'modified by CT Life (cropped, foundry name painted out)')
    log('decals', {k: (v['tris'], v['size_m']) for k, v in out.items()})
    return out



# ------------------------------------------------------------------ 8. Poly Haven models (CC0): decimated low poly, high -> low bake
def _apply_world(o):
    mw = o.matrix_world.copy()
    o.parent = None
    o.data.transform(mw)
    o.matrix_world = Matrix.Identity(4)


def prep_trash_can(objs):
    """keep the cleaner can of the pair and put its lid on (the scan has it leaning against the can)."""
    for o in objs:
        _apply_world(o)
    lid = next(o for o in objs if o.name.endswith('_lid'))
    body = next(o for o in objs if o.name == 'metal_trash_can')
    V = np.array([v.co[:] for v in lid.data.vertices])
    c = V.mean(0)
    w, vec = np.linalg.eigh(np.cov((V - c).T))
    n = vec[:, 0]
    d = (V - c) @ n
    if abs(d.max()) < abs(d.min()):          # the handle side sticks out furthest: make it face up
        n = -n
    R = Vector(n).rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4()
    lid.data.transform(Matrix.Translation(-Vector(c)))
    lid.data.transform(R)
    B = np.array([v.co[:] for v in body.data.vertices])
    L_ = np.array([v.co[:] for v in lid.data.vertices])
    bc = (B.min(0) + B.max(0)) / 2
    lc = (L_.min(0) + L_.max(0)) / 2
    lid.data.transform(Matrix.Translation(Vector((bc[0] - lc[0], bc[1] - lc[1], B.max(0)[2] - 0.028 - L_.min(0)[2]))))
    log('trash can lid placed; lid size', np.round(L_.max(0) - L_.min(0), 3), 'body top', round(float(B.max(0)[2]), 3))


def build_ph(name, model, keep, lod0, lod1, size, prep=None, cage=0.012, ao_dist=0.25, credit=''):
    W.reset()
    W.import_glb(os.path.join(RAW_PH, model, f'{model}_1k.gltf'))
    objs = [o for o in bpy.data.objects if o.type == 'MESH' and keep(o.name)]
    for o in [o for o in bpy.data.objects if o.type == 'MESH' and o not in objs]:
        bpy.data.objects.remove(o)
    if prep:
        prep(objs)
    high = W.join_copy(objs, name + '_high')
    for o in list(bpy.data.objects):
        if o is not high:
            bpy.data.objects.remove(o)
    vs = np.array([v.co[:] for v in high.data.vertices])
    mn, mx = vs.min(0), vs.max(0)
    high.data.transform(Matrix.Translation(-Vector(((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, mn[2]))))
    src_tris = W.tris(high)
    low = W.dup(high, '_lowsrc')
    while low.data.uv_layers:
        low.data.uv_layers.remove(low.data.uv_layers[0])
    bm = bmesh.new()
    bm.from_mesh(low.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
    bm.to_mesh(low.data)
    bm.free()
    l0 = W.lod_under(low, lod0, name, planar_first=True)
    bpy.data.objects.remove(low)
    meta = dict(source=f'https://polyhaven.com/a/{model}', source_uid=f'ph_{model}', licence='CC0', credit=credit)
    entry, o, l1 = finalize(name, l0, size, lod1, ao_dist=ao_dist, src=[high], cage=cage, base_meta=meta)
    entry['source_tris'] = src_tris
    return entry, o, l1


def build_trash_bin():
    return build_ph('v3_trash_bin', 'metal_trash_can', lambda n: n.startswith('metal_trash_can') and 'rust' not in n, 780, 200, 512,
                    prep=prep_trash_can, cage=0.015, ao_dist=0.25,
                    credit='"Metal Trash Can" by GurJas Studios (Poly Haven, https://polyhaven.com/a/metal_trash_can), CC0; modified by CT Life '
                           '(one can of the pair, lid put on, decimated, re-baked to one atlas)')


def build_cafe_set():
    return build_ph('v3_cafe_set', 'outdoor_table_chair_set_01', lambda n: True, 1600, 420, 512, cage=0.006, ao_dist=0.3,
                    credit='"Outdoor Table Chair Set 01" by James Ray Cock (Poly Haven, https://polyhaven.com/a/outdoor_table_chair_set_01), CC0; '
                           'modified by CT Life (decimated, re-baked to one atlas)')


def main():
    ids = [a for a in ARGS if not a.startswith('--') and a not in ('build', 'fetch')]
    ids = ids[0].split(',') if ids else []
    if '--tiles-only' not in ARGS:
        run_build(ids)
    if '--tile' in ARGS:
        for pid in ids:
            render_view(pid, os.path.join(PV_DIR, pid + '.png'))
    if '--sheet' in ARGS or (not ids and '--no-sheet' not in ARGS):
        render_sheet()


BUILD.update({'v3_traffic_light': build_traffic_light, 'v3_stop_sign': build_stop_sign, 'v3_bus_shelter': build_bus_shelter,
              'v3_bollard': build_bollard, 'v3_bike_rack': build_bike_rack, 'v3_vendor_cart': build_vendor_cart,
              'v3_decals': build_decals, 'v3_trash_bin': build_trash_bin, 'v3_cafe_set': build_cafe_set})

main()
