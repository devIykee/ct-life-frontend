"""Process CC0 environment models into the CT Life city kit.

Usage: art/pipeline/blender.sh art/pipeline/env_process.py -- [kit[,kit...]] [--only id,id]
Kits/items are declared in env_config.py. For every item:
  import (fbx/glb/gltf/obj) -> drop cameras/lights/empties -> join -> scale to metres (Z up in Blender)
  -> origin at bottom-centre -> unify flat-colour materials (incl. Kenney colour-map textures) into ONE shared
  city palette atlas (art/processed/environment/_shared/ctl_palette.png) -> keep real textures (<=1024)
  -> LOD1/LOD2 for heavy meshes -> FBX (-Z forward, Y up, Apply Transform: meshes only, no armatures).
Writes art/processed/environment/<venue|category>/<id>.fbx and manifest.json.
"""
import bpy, bmesh, sys, os, json, math, re, hashlib
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
import importlib
import ctl_config as C
import ctl_tex as T
# CTL_ENV_CONFIG / CTL_ENV_SET (World lane, 2026-10-17): another kit config module and its own output set (manifest,
# palette atlas and shared textures under art/processed/<set>/), so the island kit never edits the venue palette.
E = importlib.import_module(os.environ.get('CTL_ENV_CONFIG', 'env_config'))
SET = os.environ.get('CTL_ENV_SET')

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
KITS = args[0].split(',') if args and not args[0].startswith('--') else None
ONLY = args[args.index('--only') + 1].split(',') if '--only' in args else None
OUT = C.PROCESSED
SHARED = os.path.join(OUT, SET or 'environment', '_shared')
os.makedirs(SHARED, exist_ok=True)
PAL_NAME = f'{SET}_palette' if SET else 'ctl_palette'
PAL_MAT = 'M_' + PAL_NAME
PAL_PATH = os.path.join(SHARED, PAL_NAME + '.png')
PAL_JSON = os.path.join(SHARED, PAL_NAME + '.json')
PAL_N = 32            # 32 x 32 swatches
PAL_CELL = 16         # px per swatch -> 512 x 512 atlas


def log(*a):
    print('[env]', *a, flush=True)


# ------------------------------------------------------------------ palette (persistent across runs)
palette = json.load(open(PAL_JSON)) if os.path.exists(PAL_JSON) else {'colors': []}


TINT_BASE = PAL_N * PAL_N // 2      # swatches from here on (bottom half, uv.v < 0.5) are tintable greys (World lane)


def swatch(rgb, tint=False):
    """sRGB 0..1 triple -> palette index (quantised to 1/64 to merge near-identical colours). tint=True: a grey swatch
    in the palette's bottom half, which the island toon shader multiplies by each instance's facade colour."""
    n = CURRENT.get('quant') or 63   # per-item coarser quantisation for forced gradient maps (round 6)
    q = [round(c * n) / n for c in rgb]
    key = '%02x%02x%02x' % tuple(int(round(c * 255)) for c in q)
    if tint:
        tl = palette.setdefault('tint', [])
        if key not in tl:
            tl.append(key)
        return TINT_BASE + tl.index(key)
    if key not in palette['colors']:
        if len(palette['colors']) >= TINT_BASE:
            # palette full: nearest existing colour
            arr = np.array([[int(k[i:i + 2], 16) / 255 for i in (0, 2, 4)] for k in palette['colors']])
            return int(np.argmin(((arr - np.array(q)) ** 2).sum(axis=1)))
        palette['colors'].append(key)
    return palette['colors'].index(key)


def swatch_uv(idx):
    cx, cy = idx % PAL_N, idx // PAL_N
    u = (cx + 0.5) / PAL_N
    v = 1 - (cy + 0.5) / PAL_N
    return u, v


def save_palette():
    img = np.zeros((PAL_N * PAL_CELL, PAL_N * PAL_CELL, 3), np.float32)
    for i, k in enumerate(palette['colors']):
        cx, cy = i % PAL_N, i // PAL_N
        img[cy * PAL_CELL:(cy + 1) * PAL_CELL, cx * PAL_CELL:(cx + 1) * PAL_CELL] = [int(k[j:j + 2], 16) / 255 for j in (0, 2, 4)]
    for i, k in enumerate(palette.get('tint', [])):
        cx, cy = (TINT_BASE + i) % PAL_N, (TINT_BASE + i) // PAL_N
        img[cy * PAL_CELL:(cy + 1) * PAL_CELL, cx * PAL_CELL:(cx + 1) * PAL_CELL] = [int(k[j:j + 2], 16) / 255 for j in (0, 2, 4)]
    T.save(img, PAL_PATH)
    json.dump(palette, open(PAL_JSON, 'w'), indent=0)


def lin2srgb(c):
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


# ------------------------------------------------------------------ material analysis
_img_cache = {}


def image_array(img):
    key = img.name
    if key not in _img_cache:
        w, h = img.size
        if w == 0:
            _img_cache[key] = None
        else:
            px = np.empty(w * h * img.channels, np.float32)
            img.pixels.foreach_get(px)
            _img_cache[key] = px.reshape(h, w, img.channels)   # row 0 = bottom (Blender)
    return _img_cache[key]


def base_color_source(mat):
    """('color', srgb) | ('texture', image) | ('vcol', None) | ('keep', None)"""
    if mat and mat.name.startswith('KEEP_'):
        return ('keep', None)
    if not mat or not mat.use_nodes:
        c = mat.diffuse_color if mat else (0.8, 0.8, 0.8, 1)
        return ('color', [lin2srgb(x) for x in c[:3]])
    bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        return ('color', [0.8, 0.8, 0.8])
    inp = bsdf.inputs['Base Color']
    if not inp.links:
        return ('color', [lin2srgb(x) for x in inp.default_value[:3]])
    n = inp.links[0].from_node
    seen = 0
    while n and n.type != 'TEX_IMAGE' and seen < 6:
        seen += 1
        if n.type == 'VERTEX_COLOR' or n.type == 'ATTRIBUTE':
            return ('vcol', None)
        nxt = None
        for i in n.inputs:
            if i.links:
                nxt = i.links[0].from_node
                break
        n = nxt
    if n and n.type == 'TEX_IMAGE' and n.image:
        return ('texture', n.image)
    return ('color', [lin2srgb(x) for x in inp.default_value[:3]])


def has_cutout(img):
    """Alpha-cut foliage textures stay textures even when an item forces palette colours (round 6)."""
    a = image_array(img) if img is not None else None
    return a is not None and a.shape[2] == 4 and float((a[::4, ::4, 3] < 0.5).mean()) > 0.02


def is_palette_texture(img):
    """Kenney/KayKit-style colour maps (flat colour cells) vs real textures (photos, gradients)."""
    if img is None:
        return False
    w, h = img.size
    if w == 0:
        return False
    a = image_array(img)
    if a is None:
        return max(w, h) <= 128
    if a.shape[2] == 4 and float((a[::4, ::4, 3] < 0.5).mean()) > 0.02:
        return False            # cut-out foliage/decals keep their texture + alpha
    if max(w, h) <= 128:
        return True
    sm = a[::max(1, h // 64), ::max(1, w // 64), :3]
    q = np.round(sm * 255).astype(np.int32)
    keys = q[:, :, 0] * 65536 + q[:, :, 1] * 256 + q[:, :, 2]
    return len(np.unique(keys)) <= 64


# ------------------------------------------------------------------ import + normalise
def import_any(path):
    before = set(bpy.data.objects)
    ext = os.path.splitext(path)[1].lower()
    if ext == '.fbx':
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext in ('.glb', '.gltf'):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == '.obj':
        bpy.ops.wm.obj_import(filepath=path)
    else:
        raise ValueError(path)
    return [o for o in bpy.data.objects if o not in before]


def to_single_mesh(objs, name):
    meshes = [o for o in objs if o.type == 'MESH']
    for o in objs:
        if o.type != 'MESH':
            continue
    # bake object transforms into the meshes (static props only)
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in objs:
        if o.type != 'MESH' and o.name in bpy.data.objects:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
        if o.data.users > 1:
            o.data = o.data.copy()
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = o.data.name = name
    return o


def palettise(o):
    """Faces whose material is a flat colour or a colour-map texture get the shared palette material.
    Returns list of kept texture images (real textures)."""
    me = o.data
    mats = list(me.materials)
    src = [base_color_source(m) for m in mats]
    pal_mat = bpy.data.materials.get(PAL_MAT)
    if pal_mat is None:
        pal_mat = bpy.data.materials.new(PAL_MAT)
        pal_mat.use_nodes = True
        n = pal_mat.node_tree.nodes.new('ShaderNodeTexImage')
        if os.path.exists(PAL_PATH):
            n.image = bpy.data.images.load(PAL_PATH)
        n.interpolation = 'Closest'
        pal_mat.node_tree.links.new(n.outputs['Color'], pal_mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    bm = bmesh.new()
    bm.from_mesh(me)
    uvl = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    col = bm.loops.layers.color.active if bm.loops.layers.color else None
    # pass 1: decide per face (palette colour or keep its material)
    plan = []
    keep_names = []
    tint_faces = set()
    for f in bm.faces:
        mi = f.material_index
        kind, data = src[mi] if mi < len(src) else ('color', [0.8, 0.8, 0.8])
        rgb = None
        if kind == 'color':
            rgb = data
        elif kind == 'vcol' and col is not None:
            c = sum((Vector(l[col][:3]) for l in f.loops), Vector()) / len(f.loops)
            rgb = [lin2srgb(x) for x in c]
        elif kind == 'texture' and ((CURRENT.get('force_palette') and not has_cutout(data)) or is_palette_texture(data)):
            a = image_array(data)
            uv = sum((l[uvl].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
            h, w = a.shape[:2]
            x = int((uv.x % 1.0) * (w - 1))
            y = int((uv.y % 1.0) * (h - 1))
            rgb = list(a[y, x, :3])
            if data.colorspace_settings.name != 'sRGB':
                rgb = [lin2srgb(v) for v in rgb]
            tm = CURRENT.get('tint_mask')
            if tm is not None:
                th, tw = tm.shape
                if tm[int((uv.y % 1.0) * (th - 1)), int((uv.x % 1.0) * (tw - 1))]:
                    lum = 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]
                    tint_faces.add(f.index)
                    rgb = [lum, lum, lum]
        if rgb is not None:
            rgb = apply_recolor(rgb, CURRENT['recolor'])
        if rgb is None and mi < len(mats) and mats[mi] is not None and mats[mi].name not in keep_names:
            keep_names.append(mats[mi].name)
        plan.append((f, rgb, mi))
    use_pal = any(rgb is not None for _, rgb, _ in plan)
    new_mats = ([pal_mat] if use_pal else []) + [m for m in mats if m is not None and m.name in keep_names]
    names = [m.name for m in new_mats]
    # pass 2: assign (materials must exist on the mesh before bm.to_mesh, or indices get clamped)
    for f, rgb, mi in plan:
        if rgb is not None:
            u, v = swatch_uv(swatch(rgb, tint=f.index in tint_faces))
            for l in f.loops:
                l[uvl].uv = (u, v)
            f.material_index = 0
        else:
            f.material_index = names.index(mats[mi].name) if mi < len(mats) and mats[mi] is not None else 0
    me.materials.clear()
    for m in new_mats:
        me.materials.append(m)
    bm.to_mesh(me)
    bm.free()
    return [m for m in me.materials if m.name != PAL_MAT]


def copy_textures(mats, tex_dir, tex_max=512, prefix=''):
    """Copy (resize) the base-colour texture of every kept material into the shared texture folder and
    rebuild the material as a plain Principled BSDF with the image wired straight into Base Color
    (+Alpha for cut-out foliage). Blender's FBX exporter only sees directly-linked textures, and Unity
    can then share one material per texture (material name = texture name)."""
    out = []
    remap = {}
    for m in mats:
        kind, img = base_color_source(m)
        if kind != 'texture' or img is None:
            continue
        src = bpy.path.abspath(img.filepath) if img.filepath else None
        if src and os.path.dirname(os.path.abspath(src)) == os.path.abspath(tex_dir):
            dst = os.path.abspath(src)
        else:
            name = re.sub(r'[^A-Za-z0-9_.-]', '_', prefix + os.path.basename(src or (img.name + '.png')))
            name = os.path.splitext(name)[0] + ('_' + CURRENT['recolor'] if CURRENT['recolor'] else '') + '.png'
            dst = os.path.join(tex_dir, name)
            if not os.path.exists(dst):
                try:
                    a = T.load(src) if src and os.path.exists(src) else None
                except Exception:
                    a = None
                if a is None:
                    arr = image_array(img)
                    a = arr[::-1].copy() if arr is not None else None
                if a is not None and CURRENT['recolor'] == 'steel':
                    rgb = a[:, :, :3]
                    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
                    sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)
                    r_, g_, b_ = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
                    d = np.maximum(mx - mn, 1e-6)
                    hue = np.where(mx == r_, ((g_ - b_) / d) % 6, np.where(mx == g_, (b_ - r_) / d + 2, (r_ - g_) / d + 4)) / 6.0
                    mask = ((hue >= 0.55) & (hue <= 0.84) & (sat > 0.12))[:, :, None]
                    steel = np.array([0.56, 0.6, 0.66])[None, None, :] * (0.55 + 0.6 * mx)[:, :, None]
                    a = a.copy()
                    a[:, :, :3] = np.where(mask, np.clip(steel, 0, 1), rgb)
                if a is not None:
                    h, w = a.shape[:2]
                    s = min(1.0, tex_max / max(h, w))
                    if s < 1:
                        a = T.resize(a, int(w * s), int(h * s))
                    has_alpha = a.shape[2] == 4 and float(a[:, :, 3].min()) < 0.98
                    T.save(a, dst, channels=4 if has_alpha else 3)
        rel_dst = os.path.relpath(dst, OUT)
        if rel_dst not in out:
            out.append(rel_dst)
        mname = 'M_' + os.path.splitext(os.path.basename(dst))[0]
        nm = bpy.data.materials.get(mname)
        if nm is None:
            nm = bpy.data.materials.new(mname)
            nm.use_nodes = True
            b = nm.node_tree.nodes['Principled BSDF']
            t = nm.node_tree.nodes.new('ShaderNodeTexImage')
            t.image = bpy.data.images.load(dst, check_existing=True)
            nm.node_tree.links.new(t.outputs['Color'], b.inputs['Base Color'])
            b.inputs['Roughness'].default_value = 0.8
            if t.image.channels == 4 and ('leaf' in dst.lower() or 'leaves' in dst.lower() or 'flower' in dst.lower() or 'grass' in dst.lower() or 'petal' in dst.lower() or 'plant' in dst.lower() or 'fern' in dst.lower() or 'bush' in dst.lower()):
                nm.node_tree.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
                nm['ctl_alpha_clip'] = True
        remap[m.name] = nm
    return out, remap


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def make_lods(o, item):
    t = tris(o)
    lods = [o]
    ratios = item.get('lods') or ((0.5, 0.2) if t > 600 else ((0.5,) if t > 250 else ()))
    for i, r in enumerate(ratios, start=1):
        d = o.copy(); d.data = o.data.copy()
        bpy.context.scene.collection.objects.link(d)
        d.name = d.data.name = f'{item["id"]}_LOD{i}'
        m = d.modifiers.new('Dec', 'DECIMATE')
        m.ratio = r
        m.use_collapse_triangulate = True
        bpy.ops.object.select_all(action='DESELECT')
        d.select_set(True)
        bpy.context.view_layer.objects.active = d
        bpy.ops.object.modifier_apply(modifier='Dec')
        lods.append(d)
    if len(lods) > 1:
        o.name = o.data.name = f'{item["id"]}_LOD0'
    return lods


def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        path_mode='RELATIVE', embed_textures=False, bake_anim=False)


SCALES = {}
CURRENT = {'recolor': None}


def _in(x, rng):
    lo, hi = rng
    return (lo <= x <= hi) if lo <= hi else (x >= lo or x <= hi)   # hue ranges may wrap (e.g. (0.95, 0.05))


def apply_recolor(rgb, mode):
    """'steel': Kenney's signature lavender/purple -> blue-grey steel (keeps orange/yellow accents, ORE-like).
    A list of rules {'h': (lo, hi), 's': (lo, hi), 'v': (lo, hi), 'to': 'rrggbb'} (round 6): the first rule whose HSV
    ranges (all optional, hue may wrap) contain the source colour replaces it with 'to', so one source model can ship as
    café-coloured variants (wood, cushion, metal) through the shared palette. Colours no rule matches are kept.
    CTL_LOG_COLORS=1 prints every distinct source colour per item (to write the rules)."""
    import colorsys
    h, s_, v = colorsys.rgb_to_hsv(*[max(0.0, min(1.0, float(c))) for c in rgb])
    if os.environ.get('CTL_LOG_COLORS'):
        key = '%02x%02x%02x' % tuple(int(round(max(0.0, min(1.0, c)) * 255)) for c in rgb)
        seen = CURRENT.setdefault('logged', set())
        if key not in seen:
            seen.add(key)
            log('colour', CURRENT.get('id'), key, 'h=%.2f s=%.2f v=%.2f' % (h, s_, v))
    if not mode:
        return rgb
    if isinstance(mode, (list, tuple)):
        for r in mode:
            if _in(h, r.get('h', (0.0, 1.0))) and _in(s_, r.get('s', (0.0, 1.0))) and _in(v, r.get('v', (0.0, 1.0))):
                t = r['to']
                return [int(t[i:i + 2], 16) / 255 for i in (0, 2, 4)]
        return rgb
    if mode == 'steel' and 0.55 <= h <= 0.84 and s_ > 0.12:
        t = (0.56, 0.6, 0.66)
        k = 0.55 + 0.6 * v
        return [min(1.0, c * k) for c in t]
    return rgb
EXT_ORDER = ('.glb', '.gltf', '.fbx', '.obj')
_index = {}


def resolve(src):
    if '::' not in src:
        return os.path.join(C.RAW, src)
    pack, name = src.split('::', 1)
    root = os.path.join(C.RAW, pack)
    if root not in _index:
        idx = {}
        for r, _, files in os.walk(root):
            for f in files:
                b, e = os.path.splitext(f)
                if e.lower() in EXT_ORDER and '__MACOSX' not in r:
                    idx.setdefault(b.lower(), []).append(os.path.join(r, f))
        _index[root] = idx
    cands = _index[root].get(name.lower(), [])
    if not cands:
        raise FileNotFoundError(src)
    # prefer glTF/GLB, then FBX (Unity flavour), then OBJ
    def rank(p):
        e = os.path.splitext(p)[1].lower()
        return (EXT_ORDER.index(e), 0 if 'unity' in p.lower() else 1, len(p))
    return sorted(cands, key=rank)[0]


def make_blossom_texture():
    dst = os.path.join(C.RAW, 'ctlife_custom', 'BlossomTree_Leaves.png')
    if os.path.exists(dst):
        return
    src = os.path.join(C.RAW, 'quaternius', 'ultimate_stylized_nature', 'glTF', 'MapleTree_Leaves_BW.png')
    a = T.load(src)
    L = T.lum(a)
    pink = np.array([0.98, 0.62, 0.78]); white = np.array([1.0, 0.93, 0.96]); deep = np.array([0.86, 0.38, 0.58])
    t = np.clip(L, 0, 1)[:, :, None]
    rgb = np.where(t > 0.6, pink * (1 - (t - 0.6) / 0.4) + white * ((t - 0.6) / 0.4), deep * (1 - t / 0.6) + pink * (t / 0.6))
    out = a.copy(); out[:, :, :3] = rgb
    T.save(out, dst, channels=4)
    print('[env] blossom texture', dst)


def process(item):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    CURRENT['recolor'] = item.get('recolor')
    CURRENT['id'] = item['id']
    CURRENT['force_palette'] = item.get('force_palette')   # KayKit/Kenney gradient maps -> flat palette colours
    CURRENT['quant'] = item.get('quant')
    CURRENT['tint_mask'] = None
    if item.get('tint_mask'):
        mpath = item['tint_mask'] if os.path.isabs(item['tint_mask']) else os.path.join(OUT, item['tint_mask'])
        mimg = bpy.data.images.load(mpath, check_existing=True)
        mw, mh = mimg.size
        px_ = np.empty(mw * mh * mimg.channels, np.float32)
        mimg.pixels.foreach_get(px_)
        CURRENT['tint_mask'] = px_.reshape(mh, mw, mimg.channels)[:, :, 0] > 0.5     # row 0 = bottom, like image_array
    CURRENT['logged'] = set()
    _img_cache.clear()
    path = resolve(item['src'])
    objs = import_any(path)
    # texture overrides (e.g. pink blossom leaves, Quaternius building colourways)
    for sub, tex in (item.get('tex') or {}).items():
        img = bpy.data.images.load(os.path.join(C.RAW, tex), check_existing=True)
        for m in bpy.data.materials:
            if sub.lower() in m.name.lower() and m.use_nodes:
                b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
                if b is None:
                    continue
                tn = next((n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.outputs['Color'].links), None)
                if tn is None:
                    tn = m.node_tree.nodes.new('ShaderNodeTexImage')
                    m.node_tree.links.new(tn.outputs['Color'], b.inputs['Base Color'])
                tn.image = img
    if item.get('keep'):
        objs = [o for o in objs if o.type != 'MESH' or re.search(item['keep'], o.name)] + [o for o in objs if o.type != 'MESH']
        for o in [x for x in bpy.data.objects if x.type == 'MESH' and x not in objs]:
            bpy.data.objects.remove(o, do_unlink=True)
    o = to_single_mesh(objs, item['id'])
    s = item.get('scale', 1.0)
    if item.get('fit'):
        ax, target = item['fit']
        bb0 = [o.matrix_world @ Vector(c) for c in o.bound_box]
        dims = [max(p[i] for p in bb0) - min(p[i] for p in bb0) for i in range(3)]
        cur = max(dims) if ax == 'max' else dims['xyz'.index(ax)]
        s = target / max(cur, 1e-6)
    sc3 = (s, s, s)
    if item.get('fit3'):   # non-uniform: chunky kit furniture -> real-world counter/table heights
        bb0 = [o.matrix_world @ Vector(c) for c in o.bound_box]
        dims = [max(p[i] for p in bb0) - min(p[i] for p in bb0) for i in range(3)]
        sc3 = tuple((t / max(d, 1e-6)) if t else None for t, d in zip(item['fit3'], dims))
        u = next((x for x in sc3 if x), 1.0)
        sc3 = tuple(x or u for x in sc3)
    if item.get('scale_like') and item['scale_like'] in SCALES:
        sc3 = SCALES[item['scale_like']]
    SCALES[item['id']] = sc3
    if item.get('rot_z'):
        o.rotation_euler.z = math.radians(item['rot_z'])
    o.scale = sc3
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    # origin at bottom-centre (or keep tile corner for grid pieces)
    bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
    mn = Vector((min(p.x for p in bb), min(p.y for p in bb), min(p.z for p in bb)))
    mx = Vector((max(p.x for p in bb), max(p.y for p in bb), max(p.z for p in bb)))
    pivot = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    o.data.transform(Matrix.Translation(-pivot))
    o.location = (0, 0, 0)
    sm = item.get('smooth', False)
    for p in o.data.polygons:
        p.use_smooth = bool(sm)
    if not isinstance(sm, bool) and sm:
        # 'smooth': <degrees> (round 6): smooth shading with hard edges above the angle (cushions stay soft, frames crisp)
        bm = bmesh.new()
        bm.from_mesh(o.data)
        lim = math.radians(float(sm))
        for e in bm.edges:
            if len(e.link_faces) == 2:
                e.smooth = e.calc_face_angle(0.0) <= lim
        bm.to_mesh(o.data)
        bm.free()
    kept = palettise(o)
    dst_dir = os.path.join(OUT, item['dir'])
    os.makedirs(dst_dir, exist_ok=True)
    pack = item['src'].split('::')[0]
    prefix = '' if pack.startswith('ctlife') or 'polyhaven' in pack else re.sub(r'[^a-z0-9]+', '_', pack.split('/')[-1].lower()) + '__'
    tex, remap = copy_textures([m for m in kept if not m.name.startswith('KEEP_')], os.path.join(SHARED, 'textures'), item.get('tex_max', 512), prefix)
    for i, m in enumerate(o.data.materials):
        if m and m.name in remap:
            o.data.materials[i] = remap[m.name]
    # merge duplicate slots that now point at the same material
    names = [m.name for m in o.data.materials]
    if len(set(names)) < len(names):
        first = {}
        idx_map = []
        for i, n in enumerate(names):
            first.setdefault(n, len(first))
            idx_map.append(first[n])
        for p in o.data.polygons:
            p.material_index = idx_map[p.material_index]
        uniq = []
        for n in names:
            if n not in uniq:
                uniq.append(n)
        mats_u = [bpy.data.materials[n] for n in uniq]
        o.data.materials.clear()
        for m in mats_u:
            o.data.materials.append(m)
    if item.get('decimate'):
        # round 6: lighter LOD0 for foliage placed by the hundred (flower beds); LOD1/LOD2 derive from it
        m_ = o.modifiers.new('Pre', 'DECIMATE')
        m_.ratio = item['decimate']
        m_.use_collapse_triangulate = True
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier='Pre')
    lods = make_lods(o, item)
    fbx = os.path.join(dst_dir, item['id'] + '.fbx')
    export(lods, fbx)
    dims = [round(x, 3) for x in (mx - mn)]
    return {'fbx': os.path.relpath(fbx, OUT), 'tris': [tris(x) for x in lods], 'size_m': dims,
            'materials': [m.name for m in o.data.materials], 'textures': tex, 'source': item['src'],
            'pack': item.get('pack'), 'tags': item.get('tags', [])}


os.makedirs(os.path.join(SHARED, 'textures'), exist_ok=True)
make_blossom_texture()
man_path = os.path.join(OUT, SET or 'environment', 'manifest.json')
manifest = json.load(open(man_path)) if os.path.exists(man_path) else {}
for item in E.ITEMS:
    if KITS and not any(item['dir'].endswith(k) for k in KITS):
        continue
    if ONLY and item['id'] not in ONLY:
        continue
    try:
        manifest[item['id']] = process(item)
        log(item['id'], manifest[item['id']]['tris'], manifest[item['id']]['size_m'])
    except Exception as ex:
        log('FAILED', item['id'], repr(ex))
        import traceback; traceback.print_exc()
    save_palette()
json.dump(manifest, open(man_path, 'w'), indent=1)
log('done', len(manifest), 'items; palette colours', len(palette['colors']))
