"""Shared configuration and helpers for the CT Life cast pipeline (Blender 5.1.2, headless).

Everything here runs inside Blender's Python (bpy + numpy). Large intermediates go to EVID,
never into the repository.
"""
import bpy, bmesh, json, struct, zlib, math, time
import numpy as np
from pathlib import Path
from mathutils import Vector

import os
# CTLIFE_REPO points the pipeline at another checkout (lanes work in their own worktrees); default: the main clone
REPO = Path(os.environ.get('CTLIFE_REPO', '/Users/mac/Documents/codes/projects/ct-life'))
CAST = REPO / 'art/cast'
EXPORT = CAST / 'export'
PREVIEWS = CAST / 'previews'
EVID = Path('/Users/mac/Documents/codes/projects/ct-life-evidence/cast')
CAND = REPO / 'art/character-candidates'

# Atlas layout: 8x8 cells. (x, y, w, h) in cells; v grows upward (Blender/Unity UV convention).
GRID = 8
SLOTS = {
    'HEAD':   (0, 4, 4, 4),   # face, ears, neck, scalp, eyes, brows, lashes
    'HAIR':   (4, 6, 4, 2),
    'SKIN':   (4, 4, 3, 2),   # arms, hands, shoulders, ankles, legs
    'BEARD':  (7, 4, 1, 2),
    'TOP':    (0, 1, 4, 3),
    'OUTER':  (4, 2, 2, 2),   # vests and other over-layers
    'ACC_A':  (6, 3, 2, 1),   # hats / head-top
    'ACC_B':  (6, 2, 1, 1),   # face: glasses, masks, visors
    'ACC_C':  (7, 2, 1, 1),   # neck: chains, lanyards, scarves, headsets
    'BOTTOM': (4, 0, 4, 2),
    'SHOES':  (0, 0, 2, 1),
    'HAND':   (2, 0, 1, 1),   # hand-held props
    'BACK':   (3, 0, 1, 1),   # back / floating rigs
}

def slot_rect(slot):
    x, y, w, h = SLOTS[slot]
    return x / GRID, y / GRID, w / GRID, h / GRID

def slot_px(slot, res):
    """Pixel rect (row0, row1, col0, col1) in a numpy image with row 0 = bottom (Blender order)."""
    x, y, w, h = SLOTS[slot]
    c = res // GRID
    return y * c, (y + h) * c, x * c, (x + w) * c

# Triangle budgets per tier. Part allocations are targets; looks are verified against the caps.
TIERS = ['hero', 'lod0', 'lod1', 'lod2']
CAPS = {'hero': 12000, 'lod0': 6000, 'lod1': 3000, 'lod2': 1200}
MAX_INFL = {'hero': 4, 'lod0': 4, 'lod1': 4, 'lod2': 2}

FAMILIES = {
    'm': {
        'label': 'Snow-derived adult frame',
        'blend': CAND / 'unity-snow/SnowAdult_Unity.blend',
        'mesh': 'SnowAdult_CreatorMesh', 'rig': 'SnowAdult_Rig',
        'tex': {'base': CAND / 'unity-snow/exports/SnowAdult_BaseColor.png',
                'normal': CAND / 'unity-snow/exports/SnowAdult_Normal.png',
                'ms': CAND / 'unity-snow/exports/SnowAdult_MetallicSmoothness.png'},
        # source_part_id -> (part, sub)
        'parts': {0: ('skin', 'arms'), 1: ('head', 'brows'), 2: ('head', 'eyes'), 3: ('hair', 'hair'),
                  4: ('head', 'face'), 5: ('bottom', 'pants'), 6: ('top', 'tee'), 7: ('shoes', 'shoe'),
                  8: ('shoes', 'sole'), 9: ('shoes', 'detail')},
        'credit': 'Snow Rig (CC) Blender Foundation | studio.blender.org',
        'source': 'https://studio.blender.org/characters/snow/v4/',
    },
    'f': {
        'label': 'Rain-derived adult frame',
        # weight-corrected creator (stale DEF-COR-* groups no longer mapped to Head); geometry/UVs identical
        'blend': CAND / 'rain-weight-correction/RainAdult_WeightsCorrected.blend',
        'mesh': 'RainAdult_CreatorMesh', 'rig': 'RainAdult_Rig',
        'tex': {'base': CAND / 'rain-adult-production/exports/RainAdult_BaseColor.png',
                'normal': CAND / 'rain-adult-production/exports/RainAdult_Normal.png',
                'ms': CAND / 'rain-cloth-correction/exports/RainAdult_MetallicSmoothness.png'},
        'parts': {0: ('skin', 'arms'), 1: ('head', 'brows'), 2: ('head', 'lashes'), 3: ('head', 'eyes'),
                  4: (None, 'gums'), 5: (None, 'gums'), 6: ('hair', 'hair'), 7: ('hair', 'ponytail'),
                  8: ('hair', 'strand'), 9: ('hair', 'band'), 10: ('head', 'face'), 11: ('bottom', 'jeans'),
                  12: ('scarf', 'scarf'), 13: ('shoes', 'shoe'), 14: (None, 'tongue'), 15: ('top', 'tank')},
        'credit': 'Rain Rig (CC) Blender Foundation | studio.blender.org',
        'source': 'https://studio.blender.org/characters/rain/v3/',
    },
}
SUB_IDS = {'face': 1, 'eyes': 2, 'brows': 3, 'lashes': 4, 'hair': 5, 'ponytail': 6, 'strand': 7, 'band': 8,
           'arms': 9, 'legs': 10, 'tee': 11, 'tank': 12, 'pants': 13, 'jeans': 14, 'shoe': 15, 'sole': 16,
           'detail': 17, 'scarf': 18,
           # new cloth / hair geometry (procedural textures)
           'new': 30, 'cuff': 31, 'collar': 32, 'hood': 33, 'hood_in': 34, 'dickey': 35, 'hem': 36, 'lining': 37,
           'shaft': 38, 'scalp': 39, 'beard': 40, 'afro': 41, 'loc': 42, 'skirt': 43, 'sleeve': 44, 'pocket': 45,
           # accessories (colour per variant)
           'acc_a': 50, 'acc_b': 51, 'acc_c': 52, 'acc_d': 53, 'acc_glow': 54, 'acc_metal': 55, 'acc_dark': 56,
           'acc_screen': 57, 'acc_strap': 58, 'acc_lens': 59, 'acc_tex': 60, 'acc_glow2': 61}
SUB_NAME = {v: k for k, v in SUB_IDS.items()}
# Body-chunk regions on SKIN faces
REGION = {'hand': 1, 'forearm': 2, 'upperarm': 3, 'torso': 4, 'ankle': 5, 'legs': 6, 'waist': 7, 'hip': 8}

def fam_dir(fam):
    d = EVID / 'families' / fam
    d.mkdir(parents=True, exist_ok=True)
    return d

# ---------------------------------------------------------------- image IO (no colour management)

def write_png(path, img, bits=8):
    """img: float array HxWxC in [0,1], row 0 = bottom (Blender order). Written verbatim (no
    premultiplication, no colour transform)."""
    a = np.asarray(img, dtype=np.float32)
    if a.ndim == 2:
        a = a[:, :, None]
    h, w, c = a.shape
    ctype = {1: 0, 2: 4, 3: 2, 4: 6}[c]
    if bits == 16:
        data = np.clip(np.rint(a * 65535), 0, 65535).astype('>u2')
    else:
        data = np.clip(np.rint(a * 255), 0, 255).astype(np.uint8)
    rows = data[::-1].reshape(h, -1)
    raw = np.concatenate([np.zeros((h, 1), dtype=np.uint8), rows.view(np.uint8).reshape(h, -1)], axis=1).tobytes()
    def chunk(kind, payload):
        return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload) & 0xffffffff)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, bits, ctype, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))

def read_image(path, channels=4):
    """Read via Blender as raw data (Non-Color, channel-packed alpha). Returns HxWx4 float32, row0=bottom."""
    im = bpy.data.images.load(str(path), check_existing=False)
    im.colorspace_settings.name = 'Non-Color'
    im.alpha_mode = 'CHANNEL_PACKED'
    w, h = im.size
    a = np.empty(w * h * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(h, w, 4)[:, :, :channels]

def new_float_image(name, res):
    im = bpy.data.images.new(name, width=res, height=res, alpha=True, float_buffer=True)
    im.colorspace_settings.name = 'Non-Color'
    im.alpha_mode = 'CHANNEL_PACKED'
    im.generated_color = (0, 0, 0, 0)
    return im

def image_array(im):
    w, h = im.size
    a = np.empty(w * h * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    return a.reshape(h, w, 4)

def resize(img, res):
    """Box-filter downsample by an integer factor (or nearest upsample)."""
    h = img.shape[0]
    if h == res:
        return img
    if h > res:
        f = h // res
        return img.reshape(res, f, res, f, -1).mean(axis=(1, 3))
    f = res // h
    return np.repeat(np.repeat(img, f, axis=0), f, axis=1)

def dilate(img, mask, iters=24):
    """Fill uncovered texels from covered neighbours (for mip/ASTC bleeding)."""
    out = img.copy()
    m = mask.astype(bool).copy()
    for _ in range(iters):
        if m.all():
            break
        acc = np.zeros_like(out)
        cnt = np.zeros(m.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            sm = np.roll(np.roll(m, dy, 0), dx, 1)
            si = np.roll(np.roll(out, dy, 0), dx, 1)
            acc += si * sm[..., None]
            cnt += sm
        grow = (~m) & (cnt > 0)
        out[grow] = acc[grow] / cnt[grow][:, None]
        m = m | grow
    if not m.all():
        fill = out[m].mean(axis=0) if m.any() else 0
        out[~m] = fill
    return out

# ---------------------------------------------------------------- colour helpers (sRGB-encoded floats)

def hexrgb(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], dtype=np.float32)

def luminance(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722

def srgb_to_lin(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

def lin_to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)

def recolor(rgb, mask, target_hex, keep_detail=1.0, ref=None):
    """Luminance-preserving recolour of sRGB texels under mask to a target colour.
    Shading/fold detail = (L / Lref) in linear light; ref = reference mean luminance."""
    tgt = srgb_to_lin(hexrgb(target_hex))
    lin = srgb_to_lin(rgb)
    L = luminance(lin)
    m = mask > 0.5
    if ref is None:
        ref = float(np.median(L[m])) if m.any() else 0.5
    shade = (L / max(ref, 1e-4)) ** keep_detail
    out = lin.copy()
    out[m] = np.clip(tgt[None, :] * shade[m][:, None], 0, 1)
    return lin_to_srgb(out)

# ---------------------------------------------------------------- mesh helpers

def tri_count(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)

def mesh_arrays(obj):
    me = obj.data
    n = len(me.vertices)
    co = np.empty(n * 3, dtype=np.float32)
    me.vertices.foreach_get('co', co)
    return co.reshape(-1, 3)

def set_co(obj, co):
    obj.data.vertices.foreach_set('co', np.asarray(co, dtype=np.float32).ravel())
    obj.data.update()

def weights_matrix(obj, bone_names):
    """Dense (nverts x nbones) weight matrix for the given bone order."""
    idx = {g.index: bone_names.index(g.name) for g in obj.vertex_groups if g.name in bone_names}
    W = np.zeros((len(obj.data.vertices), len(bone_names)), dtype=np.float32)
    for v in obj.data.vertices:
        for g in v.groups:
            j = idx.get(g.group)
            if j is not None:
                W[v.index, j] += g.weight
    return W

def set_weights_matrix(obj, W, bone_names, max_infl=4):
    """Replace all vertex groups with the top-k normalised weights from W."""
    obj.vertex_groups.clear()
    groups = [obj.vertex_groups.new(name=n) for n in bone_names]
    k = max_infl
    order = np.argsort(-W, axis=1)[:, :k]
    top = np.take_along_axis(W, order, axis=1)
    top[top < 1e-4] = 0
    s = top.sum(axis=1, keepdims=True)
    bad = s[:, 0] <= 0
    if bad.any():
        raise RuntimeError(f'{obj.name}: {int(bad.sum())} vertices without weights')
    top = top / s
    # group-wise batch add
    for j in range(len(bone_names)):
        rows, cols = np.nonzero(order == j)
        if len(rows) == 0:
            continue
        wv = top[rows, cols]
        keep = wv > 0
        for vi, w in zip(rows[keep].tolist(), wv[keep].tolist()):
            groups[j].add([vi], w, 'REPLACE')
    # remove empty groups? keep all 55 so FBX cluster order is stable across parts

def limit_weights(obj, bone_names, max_infl):
    W = weights_matrix(obj, bone_names)
    set_weights_matrix(obj, W, bone_names, max_infl)

def max_influences(obj):
    return max((sum(1 for g in v.groups if g.weight > 1e-5) for v in obj.data.vertices), default=0)

def duplicate(obj, name, link=True):
    o = obj.copy()
    o.data = obj.data.copy()
    o.name = name
    o.data.name = name
    if link:
        bpy.context.scene.collection.objects.link(o)
    return o

def keep_faces(obj, predicate):
    """Delete faces for which predicate(face) is False; remove loose verts."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    kill = [f for f in bm.faces if not predicate(f)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

def face_attr(obj, name, values=None, default=0):
    me = obj.data
    a = me.attributes.get(name)
    if a is None:
        a = me.attributes.new(name=name, type='INT', domain='FACE')
        if values is None:
            a.data.foreach_set('value', [default] * len(me.polygons))
    if values is not None:
        a.data.foreach_set('value', list(map(int, values)))
    return a

def get_face_attr(obj, name):
    a = obj.data.attributes.get(name)
    out = np.zeros(len(obj.data.polygons), dtype=np.int32)
    if a is not None:
        a.data.foreach_get('value', out)
    return out

def select_only(objs, active=None):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]

def _dec_mod(o, r, protect_group, protect_factor, symmetry):
    mod = o.modifiers.new('dec', 'DECIMATE')
    mod.ratio = r
    mod.use_collapse_triangulate = True
    mod.use_symmetry = symmetry
    mod.symmetry_axis = 'X'
    if protect_group and protect_group in o.vertex_groups:
        mod.vertex_group = protect_group
        mod.vertex_group_factor = protect_factor
        mod.invert_vertex_group = True
    return mod

def _probe(obj, r, protect_group, protect_factor, symmetry):
    tmp = obj.data.copy()
    probe = bpy.data.objects.new('__probe', tmp)
    bpy.context.scene.collection.objects.link(probe)
    for g in obj.vertex_groups:
        probe.vertex_groups.new(name=g.name)
    _dec_mod(probe, r, protect_group, protect_factor, symmetry)
    dg = bpy.context.evaluated_depsgraph_get()
    ev = probe.evaluated_get(dg)
    m2 = ev.to_mesh()
    m2.calc_loop_triangles()
    n = len(m2.loop_triangles)
    ev.to_mesh_clear()
    bpy.data.objects.remove(probe, do_unlink=True)
    bpy.data.meshes.remove(tmp)
    return n

def decimate_to(obj, target, protect_group=None, protect_factor=1.0, symmetry=True):
    """Collapse-decimate obj (in place) to approximately `target` triangles (never above it when
    reachable). Falls back to non-symmetric collapse when the topology is not mirror-symmetric."""
    n0 = tri_count(obj)
    if n0 <= target:
        return n0
    if symmetry and _probe(obj, 0.02, protect_group, protect_factor, True) > target:
        symmetry = False
    if _probe(obj, 0.02, protect_group, protect_factor, symmetry) > target:
        protect_group = None
    lo, hi, best = 0.0, min(1.0, 1.6 * target / n0), None
    r = min(1.0, target / n0)
    for _ in range(16):
        n = _probe(obj, r, protect_group, protect_factor, symmetry)
        if n <= target:
            best = r
            lo = r
            if target - n < max(6, target * 0.012):
                break
        else:
            hi = r
        r = (lo + hi) / 2
    r = best if best is not None else 0.02
    mod = _dec_mod(obj, r, protect_group, protect_factor, symmetry)
    select_only([obj])
    with bpy.context.temp_override(object=obj, active_object=obj):
        bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=list(bm.edges))
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return tri_count(obj)

DENSITY = {SUB_IDS['eyes']: 1.35, SUB_IDS['lashes']: 0.8, SUB_IDS['legs']: 0.6}

def pack_slot(objs, slot, margin=0.012, density=None, src_uv='AtlasUV'):
    """Copy src_uv -> SlotUV, equalise texel density, pack islands (no rotation, so tangent-space
    normal maps stay valid) into the unit square and map it onto the slot rectangle."""
    dens = dict(DENSITY)
    dens.update(density or {})
    for o in objs:
        uvs = o.data.uv_layers
        if 'SlotUV' in uvs:
            uvs.remove(uvs['SlotUV'])
        l = uvs.new(name='SlotUV')
        arr = np.empty(len(o.data.loops) * 2, dtype=np.float32)
        uvs[src_uv].data.foreach_get('uv', arr)
        l.data.foreach_set('uv', arr)
        uvs.active = uvs['SlotUV']
    select_only(objs)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.reveal(select=True)
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode='OBJECT')
    for o in objs:
        subs = get_face_attr(o, 'sub')
        l = o.data.uv_layers['SlotUV']
        arr = np.empty(len(o.data.loops) * 2, dtype=np.float32)
        l.data.foreach_get('uv', arr)
        arr = arr.reshape(-1, 2)
        for p in o.data.polygons:
            f = dens.get(int(subs[p.index]), 1.0)
            if f != 1.0:
                arr[p.loop_start:p.loop_start + p.loop_total] *= f
        l.data.foreach_set('uv', arr.ravel())
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=False, scale=True, merge_overlap=False,
                            margin_method='FRACTION', margin=margin, shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')
    x, y, w, h = slot_rect(slot)
    for o in objs:
        l = o.data.uv_layers['SlotUV']
        arr = np.empty(len(o.data.loops) * 2, dtype=np.float32)
        l.data.foreach_get('uv', arr)
        arr = arr.reshape(-1, 2)
        arr[:, 0] = x + np.clip(arr[:, 0], 0, 1) * w
        arr[:, 1] = y + np.clip(arr[:, 1], 0, 1) * h
        l.data.foreach_set('uv', arr.ravel())

def log(*a):
    print('[cast]', *a, flush=True)
