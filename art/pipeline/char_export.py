"""Export the CT Life modular character kit for Unity (one sex per run).

Usage: art/pipeline/blender.sh art/pipeline/char_export.py -- male|female

Writes art/processed/characters/<sex>/
  body/ctl_<sex>_body.fbx     rig + body split in regions (body_<region>_LOD0..2) + eyes_LOD0..2
  parts/<part>.fbx            rig + <part>_LOD0..2, skinned to the shared CTL skeleton
  textures/skin_<tone>.png    512 skin tiles (6 tones), eyes_<colour>.png 128,
           <part>__<variant>.png  512/256 tiles
  manifest.json               slots, variants, triangle counts, which body regions each part hides
"""
import bpy, bmesh, sys, os, json, math, re, shutil, tempfile
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
import ctl_tex as T
import ctl_face as CF
from ctl_mesh import ensure_skinned as _ensure_skinned, export_fbx as _export_fbx, lods_for as _lods_for

SEX = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else 'male'
ONLY = sys.argv[sys.argv.index('--') + 2].split(',') if len(sys.argv) > sys.argv.index('--') + 2 else None
bpy.ops.wm.open_mainfile(filepath=os.path.join(C.WORK, f'char_{SEX}_master.blend'))
rig = bpy.data.objects[f'CTL_Rig_{SEX}']
body = bpy.data.objects['CTL_Body']
eyes = bpy.data.objects['CTL_Eyes']
brows = bpy.data.objects['CTL_Brows']
lashes = bpy.data.objects['CTL_Lashes']
REG = json.loads(body['ctl_regions'])
OUT = os.path.join(C.PROCESSED, 'characters', SEX)
TEX = os.path.join(OUT, 'textures')
for d in ('body', 'parts', 'textures'):
    os.makedirs(os.path.join(OUT, d), exist_ok=True)
TMP = tempfile.mkdtemp(prefix='ctl_tex_')
for b in rig.data.bones:
    b.use_deform = True


def log(*a):
    print('[char_export]', *a, flush=True)


def rel(p):
    return os.path.relpath(p, OUT)


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


# ------------------------------------------------------------------ Cycles bake helpers
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.samples = 4
sc.cycles.device = 'CPU'
sc.render.bake.margin = 8
sc.render.bake.use_clear = True


def new_image(name, size, color=(0, 0, 0, 1)):
    img = bpy.data.images.new(name, size, size, alpha=False)
    img.generated_color = color
    return img


def save_image(img, path):
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()


def select_only(objs, active):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def emission_material(name, build):
    """build(nt) -> colour socket. Returns a material that emits that colour + an image node for baking."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    nt.links.new(build(nt), em.inputs['Color'])
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    return m


def bake_self(obj, mat, size, path):
    """Bake mat's emission into obj's active UV map as a size^2 PNG."""
    saved = [s.material for s in obj.material_slots]
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    img = new_image('bake', size)
    n = mat.node_tree.nodes.new('ShaderNodeTexImage')
    n.image = img
    mat.node_tree.nodes.active = n
    select_only([obj], obj)
    bpy.ops.object.bake(type='EMIT')
    save_image(img, path)
    obj.data.materials.clear()
    for m in saved:
        obj.data.materials.append(m)
    bpy.data.images.remove(img)


def image_node(nt, path, non_color=False):
    n = nt.nodes.new('ShaderNodeTexImage')
    n.image = bpy.data.images.load(path, check_existing=True)
    if non_color:
        n.image.colorspace_settings.name = 'Non-Color'
    return n


# ------------------------------------------------------------------ mpfb material lookup
def mpfb_paths(src):
    """src 'mpfb:clothes/<dir>/<file>.mhclo' -> dict(diffuse, ao)"""
    kind, relp = src.split(':', 1)[1].split('/', 1)
    folder = os.path.join(C.MPFB_DATA, kind, os.path.dirname(relp))
    mhclo = os.path.join(C.MPFB_DATA, kind, relp)
    mat = None
    for line in open(mhclo, encoding='utf8', errors='ignore'):
        if line.startswith('material '):
            mat = os.path.join(folder, line.split(None, 1)[1].strip())
    if not mat or not os.path.exists(mat):
        cands = [f for f in os.listdir(folder) if f.endswith('.mhmat')]
        mat = os.path.join(folder, cands[0])
    d = {}
    for line in open(mat, encoding='utf8', errors='ignore'):
        w = line.split(None, 1)
        if len(w) == 2 and w[0] in ('diffuseTexture', 'aomapTexture'):
            p = os.path.join(os.path.dirname(mat), os.path.basename(w[1].strip()))
            if os.path.exists(p):
                d[w[0]] = p
    return d


def mpfb_base_texture(src, is_hair):
    d = mpfb_paths(src)
    a = T.load(d['diffuseTexture'])
    if 'aomapTexture' in d:
        ao = T.load(d['aomapTexture'], 3)
        if ao.shape[:2] != a.shape[:2]:
            ao = T.resize(ao, a.shape[1], a.shape[0])
        a[:, :, :3] *= (0.4 + 0.6 * ao[:, :, :1])
    if is_hair:
        a = T.flatten_alpha(a, dark=0.7)
    elif a.shape[2] == 4 and (a[:, :, 3] < 0.5).any():
        # cut-outs (frayed hems etc.) -> fill with neighbouring fabric colour; we render opaque
        m = a[:, :, 3] > 0.5
        small = T.resize(a, 512, 512) if a.shape[0] > 512 else a
        ms = small[:, :, 3] > 0.5
        filled = T.dilate(small[:, :, :3], ms, iters=24)
        big = T.resize(filled, a.shape[1], a.shape[0])
        a[:, :, :3] = np.where(m[:, :, None], a[:, :, :3], big)
        a[:, :, 3] = 1
    return a


# ------------------------------------------------------------------ 1. skin tiles
def skin_textures():
    mh = os.path.join(C.MPFB_DATA, 'skins', os.path.dirname(C.BODIES[SEX]['skin']))
    mhmat = os.path.join(C.MPFB_DATA, 'skins', C.BODIES[SEX]['skin'])
    diff = None
    for line in open(mhmat):
        if line.startswith('diffuseTexture'):
            diff = os.path.join(mh, os.path.basename(line.split(None, 1)[1].strip()))
    W = 1024
    base = T.resize(T.load(diff, 3), W, W)
    # detail ratio around the mean skin colour (keeps lips/brow ridge/nipple tint, drops photo realism)
    valid = T.lum(base) > 0.05
    mean = base[valid].mean(axis=0)
    detail = np.power(np.clip(base / mean[None, None, :], 0, 3), 0.42)
    # smooth: Sims skin is clean (keeps lip/brow-ridge colour shifts, drops pores and photo noise)
    detail = 0.35 * detail + 0.65 * T.resize(T.resize(detail, 96, 96), W, W)

    # brows + lash line + underwear: baked onto the body UV
    def bake_sel(src_obj, color_from_alpha):
        mats = []
        for slot in src_obj.material_slots:
            m = slot.material
            imgs = [n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image]
            texn = next((n for n in imgs if 'diffuse' in n.name.lower() or 'diffuse' in n.label.lower()), None) \
                or next((n for n in imgs if n.image.colorspace_settings.name == 'sRGB'), None)
            path = bpy.path.abspath(texn.image.filepath) if texn else None
            def build(nt, path=path):
                if not path:
                    c = nt.nodes.new('ShaderNodeRGB'); c.outputs[0].default_value = (0, 0, 0, 1)
                    return c.outputs[0]
                t = image_node(nt, path)
                if color_from_alpha:
                    return t.outputs['Alpha']
                mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
                mix.inputs['Factor'].default_value = 1
                nt.links.new(t.outputs['Color'], mix.inputs[6])
                nt.links.new(t.outputs['Alpha'], mix.inputs[7])
                return mix.outputs[2]
            mats.append(emission_material('selbake', build))
        saved = [s.material for s in src_obj.material_slots]
        src_obj.data.materials.clear()
        for m in mats:
            src_obj.data.materials.append(m)
        tgt_mat = emission_material('tgt', lambda nt: nt.nodes.new('ShaderNodeRGB').outputs[0])
        saved_b = [s.material for s in body.material_slots]
        body.data.materials.clear()
        body.data.materials.append(tgt_mat)
        img = new_image('sel', W)
        n = tgt_mat.node_tree.nodes.new('ShaderNodeTexImage'); n.image = img
        tgt_mat.node_tree.nodes.active = n
        src_obj.hide_render = False
        select_only([src_obj, body], body)
        bpy.ops.object.bake(type='EMIT', use_selected_to_active=True, cage_extrusion=0.015, max_ray_distance=0.03,
                            use_clear=True)
        p = os.path.join(TMP, f'sel_{src_obj.name}_{int(color_from_alpha)}.png')
        save_image(img, p)
        bpy.data.images.remove(img)
        src_obj.data.materials.clear()
        for m in saved:
            src_obj.data.materials.append(m)
        body.data.materials.clear()
        for m in saved_b:
            body.data.materials.append(m)
        return T.load(p, 3)

    brow_rgb = bake_sel(brows, False)
    brow_a = bake_sel(brows, True)[:, :, :1]
    lash_a = bake_sel(lashes, True)[:, :, :1]

    pel = rig.matrix_world @ rig.data.bones['pelvis'].head_local
    thigh = rig.matrix_world @ rig.data.bones['thigh_l'].head_local
    sp3 = rig.matrix_world @ rig.data.bones['spine_03'].head_local
    sp2 = rig.matrix_world @ rig.data.bones['spine_02'].head_local

    def under_mask(nt):
        tc = nt.nodes.new('ShaderNodeTexCoord')
        sep = nt.nodes.new('ShaderNodeSeparateXYZ')
        nt.links.new(tc.outputs['Object'], sep.inputs[0])
        def cmp(sock, op, val):
            m = nt.nodes.new('ShaderNodeMath'); m.operation = op
            nt.links.new(sock, m.inputs[0]); m.inputs[1].default_value = val
            return m.outputs[0]
        def mul(a, b):
            m = nt.nodes.new('ShaderNodeMath'); m.operation = 'MULTIPLY'
            nt.links.new(a, m.inputs[0]); nt.links.new(b, m.inputs[1]); return m.outputs[0]
        def add(a, b):
            m = nt.nodes.new('ShaderNodeMath'); m.operation = 'MAXIMUM'
            nt.links.new(a, m.inputs[0]); nt.links.new(b, m.inputs[1]); return m.outputs[0]
        absx = nt.nodes.new('ShaderNodeMath'); absx.operation = 'ABSOLUTE'
        nt.links.new(sep.outputs['X'], absx.inputs[0])
        # briefs: below the waist line, above a leg opening that rises towards the hips
        crotch = thigh.z - 0.07
        leg = nt.nodes.new('ShaderNodeMath'); leg.operation = 'MULTIPLY_ADD'
        nt.links.new(absx.outputs[0], leg.inputs[0]); leg.inputs[1].default_value = 0.55; leg.inputs[2].default_value = crotch
        above_leg = nt.nodes.new('ShaderNodeMath'); above_leg.operation = 'GREATER_THAN'
        nt.links.new(sep.outputs['Z'], above_leg.inputs[0]); nt.links.new(leg.outputs[0], above_leg.inputs[1])
        waist = pel.z + (0.035 if SEX == 'male' else 0.0)
        m = mul(above_leg.outputs[0], cmp(sep.outputs['Z'], 'LESS_THAN', waist))
        m = mul(m, cmp(absx.outputs[0], 'LESS_THAN', 0.3))
        if SEX == 'female':
            band = mul(cmp(sep.outputs['Z'], 'GREATER_THAN', sp2.z + 0.06), cmp(sep.outputs['Z'], 'LESS_THAN', sp3.z + 0.035))
            band = mul(band, cmp(absx.outputs[0], 'LESS_THAN', 0.2))
            m = add(m, band)
        rgb = nt.nodes.new('ShaderNodeCombineColor')
        for i in range(3):
            nt.links.new(m, rgb.inputs[i])
        return rgb.outputs[0]

    p = os.path.join(TMP, 'under.png')
    bake_self(body, emission_material('under', under_mask), W, p)
    under = T.load(p, 3)[:, :, :1]
    under = T.resize(T.resize(under, 256, 256), W, W)  # soften edges a touch
    # face paint masks baked in object space: R = cheek blush, G = lips, B = lip seam (reads as an open mouth when the jaw drops)
    LM = CF.landmarks(rig, eyes, body)
    ex, ey, ez = abs(LM['eye_l'].x), LM['eye_l'].y, LM['eye_l'].z
    mo = LM['mouth']
    mw_ = LM['mouth_w']
    def ellipsoid(nt, sep_xyz, centre, radii, soft=0.55):
        # 1 at the centre -> 0 at the ellipsoid surface (smooth)
        sub = nt.nodes.new('ShaderNodeVectorMath'); sub.operation = 'SUBTRACT'
        nt.links.new(sep_xyz, sub.inputs[0]); sub.inputs[1].default_value = centre
        mul = nt.nodes.new('ShaderNodeVectorMath'); mul.operation = 'MULTIPLY'
        nt.links.new(sub.outputs[0], mul.inputs[0]); mul.inputs[1].default_value = tuple(1.0 / r for r in radii)
        ln = nt.nodes.new('ShaderNodeVectorMath'); ln.operation = 'LENGTH'
        nt.links.new(mul.outputs[0], ln.inputs[0])
        mr = nt.nodes.new('ShaderNodeMapRange'); mr.interpolation_type = 'SMOOTHSTEP'
        nt.links.new(ln.outputs['Value'], mr.inputs['Value'])
        mr.inputs['From Min'].default_value = soft; mr.inputs['From Max'].default_value = 1.0
        mr.inputs['To Min'].default_value = 1.0; mr.inputs['To Max'].default_value = 0.0
        return mr.outputs['Result']
    def face_masks(nt):
        tc = nt.nodes.new('ShaderNodeTexCoord')
        obj_co = tc.outputs['Object']
        bl = ellipsoid(nt, obj_co, (ex + 0.004, ey + 0.006, ez - 0.036), (0.026, 0.05, 0.02))
        br = ellipsoid(nt, obj_co, (-ex - 0.004, ey + 0.006, ez - 0.036), (0.026, 0.05, 0.02))
        mx = nt.nodes.new('ShaderNodeMath'); mx.operation = 'MAXIMUM'
        nt.links.new(bl, mx.inputs[0]); nt.links.new(br, mx.inputs[1])
        lips = ellipsoid(nt, obj_co, (0.0, mo.y + 0.004, mo.z - 0.0005), (mw_ * 1.02, 0.03, 0.0125), soft=0.7)
        seam = ellipsoid(nt, obj_co, (0.0, mo.y + 0.004, mo.z), (mw_ * 0.92, 0.03, 0.0026), soft=0.5)
        cc = nt.nodes.new('ShaderNodeCombineColor')
        nt.links.new(mx.outputs[0], cc.inputs[0]); nt.links.new(lips, cc.inputs[1]); nt.links.new(seam, cc.inputs[2])
        return cc.outputs[0]
    p = os.path.join(TMP, 'face.png')
    bake_self(body, emission_material('face', face_masks), W, p)
    face = T.load(p, 3)
    blush, lipm, seam = face[:, :, 0:1], face[:, :, 1:2], face[:, :, 2:3]
    def grow(a, n=1):
        for _ in range(n):
            a = np.maximum.reduce([a, np.roll(a, 1, 0), np.roll(a, -1, 0), np.roll(a, 1, 1), np.roll(a, -1, 1)])
        return a
    brow_a2 = np.clip(grow(brow_a, 1) * 1.35, 0, 1)
    lash_a2 = np.clip(grow(lash_a, 1) * 1.2, 0, 1)
    out = {}
    for tone, hx in C.SKIN_TONES.items():
        tone_rgb = T.hex_rgb(hx)
        col = tone_rgb[None, None, :] * detail
        dark_tone = float(tone_rgb.mean()) < 0.45
        col = col * (1 - blush * 0.45) + col * np.array([1.06, 0.86, 0.84])[None, None, :] * blush * 0.45
        lipc = np.array([1.0, 0.70, 0.70]) * (0.95 if not dark_tone else 0.8)
        col = col * (1 - lipm * 0.7) + col * lipc[None, None, :] * lipm * 0.7
        col = col * (1 - seam * 0.75) + col * 0.28 * seam * 0.75
        browc = np.clip(brow_rgb / np.maximum(brow_a, 1e-3), 0, 1) * 0.35 + np.array([0.16, 0.11, 0.08]) * 0.65
        browc = np.minimum(browc, tone_rgb * 0.45 + 0.02)
        col = col * (1 - brow_a2 * 0.92) + browc * brow_a2 * 0.92
        col = col * (1 - lash_a2) + np.array([0.06, 0.045, 0.04])[None, None, :] * lash_a2
        uc = T.hex_rgb('3A3F4A' if SEX == 'male' else '4A3F55')
        col = col * (1 - under) + uc[None, None, :] * (0.85 + 0.15 * detail.mean(axis=2, keepdims=True)) * under
        tile = T.resize(np.clip(col, 0, 1), 512, 512)
        path = os.path.join(TEX, f'skin_{tone}.png')
        T.save(tile, path)
        out[tone] = rel(path)
    log('skin tiles', list(out))
    return out


def eye_textures():
    """Stylised Sims-like eyes (own work, CC0): bigger darker iris, limbal ring, painted highlights, lash corner."""
    out = {}
    for name, (main, ring) in CF.EYE_COLOURS.items():
        a = CF.eye_texture(main, ring, 512)
        path = os.path.join(TEX, f'eyes_{name}.png')
        T.save(T.resize(a, 256, 256), path)
        out[name] = rel(path)
    return out


# ------------------------------------------------------------------ 2. part textures
def part_textures(key, obj):
    src = obj.get('ctl_source', '')
    tile = int(obj.get('ctl_tile', 256))
    slot = obj.get('ctl_slot')
    out = {}
    if src.startswith('mpfb:') or src.startswith('quaternius:'):
        is_hair = slot in ('hair', 'facial_hair')
        base_key = 'sweater' if key == 'hoodie' else key
        if src.startswith('quaternius:'):
            a = T.load(obj['ctl_texture'])
        else:
            a = mpfb_base_texture(src, is_hair)
        if is_hair:
            variants = {c: ('recolor', hx, 0.8) for c, hx in C.HAIR_COLORS.items()}
        else:
            variants = C.VARIANTS.get(key, {'default': None})
        for vname, op in variants.items():
            b = a if op is None else T.recolor(a, op[1], op[2])
            if op and op[0] == 'rekt':      # torn + stained: dark blotches and holes filled with skin-ish pink
                rng = np.random.default_rng(7)
                n1 = T.resize(rng.random((24, 24, 1)).astype(np.float32), b.shape[1], b.shape[0])[:, :, 0]
                n2 = T.resize(rng.random((96, 96, 1)).astype(np.float32), b.shape[1], b.shape[0])[:, :, 0]
                stain = np.clip((n1 - 0.62) * 6, 0, 1)[:, :, None]
                b = b.copy()
                b[:, :, :3] = b[:, :, :3] * (1 - 0.45 * stain) + np.array([0.35, 0.28, 0.2]) * 0.45 * stain
                tear = (n2 > 0.86)[:, :, None]
                b[:, :, :3] = np.where(tear, np.array([0.12, 0.08, 0.08]), b[:, :, :3])
            if op and op[0] == 'logo':      # startup tee: big round logo on the chest (texture space centre)
                h_, w_ = b.shape[:2]
                yy, xx = np.mgrid[0:h_, 0:w_]
                b = b.copy()
                for cxr, cyr in ((0.25, 0.3), (0.75, 0.3)):
                    r = np.hypot(xx / w_ - cxr, yy / h_ - cyr)
                    ring = (r < 0.07)[:, :, None]
                    b[:, :, :3] = np.where(ring, np.array([0.98, 0.76, 0.2]), b[:, :, :3])
            if key == 'hoodie':
                p = os.path.join(TMP, f'hoodie_{vname}.png')
                T.save(b, p)
                path = os.path.join(TEX, f'{key}__{vname}.png')
                bake_self(obj, emission_material('hoodie', lambda nt: hoodie_overlay(nt, p)), tile, path)
            else:
                path = os.path.join(TEX, f'{key}__{vname}.png')
                T.save(T.resize(b, tile, tile), path)
            out[vname] = rel(path)
    else:  # procedural (ctlife)
        mat = obj.material_slots[0].material
        variants = C.PROC_VARIANTS.get(key, {'default': None})
        for vname, hx in variants.items():
            m2 = mat.copy()
            nt = m2.node_tree
            if hx and 'base_rgb' in nt.nodes:
                c = [int(hx[i:i + 2], 16) / 255 for i in (0, 2, 4)]
                nt.nodes['base_rgb'].outputs[0].default_value = (*[x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c], 1)
            bsdf = nt.nodes['Principled BSDF']
            link = bsdf.inputs['Base Color'].links[0]
            em = nt.nodes.new('ShaderNodeEmission')
            nt.links.new(link.from_socket, em.inputs['Color'])
            outn = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
            nt.links.new(em.outputs[0], outn.inputs['Surface'])
            path = os.path.join(TEX, f'{key}__{vname}.png')
            bake_self(obj, m2, tile, path)
            out[vname] = rel(path)
    return out


def hoodie_overlay(nt, img_path):
    """Sweater texture (recoloured) + kangaroo pocket, drawstrings, ribbed hem/cuffs, in object space."""
    t = image_node(nt, img_path)
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['Object'], sep.inputs[0])
    pel = rig.matrix_world @ rig.data.bones['pelvis'].head_local
    nk = rig.matrix_world @ rig.data.bones['neck_01'].head_local
    hand = rig.matrix_world @ rig.data.bones['hand_l'].head_local
    def box(xr, yr, zr):
        acc = None
        for ax, r in zip('XYZ', (xr, yr, zr)):
            if r is None:
                continue
            g = nt.nodes.new('ShaderNodeMath'); g.operation = 'GREATER_THAN'
            l = nt.nodes.new('ShaderNodeMath'); l.operation = 'LESS_THAN'
            src = sep.outputs[ax]
            nt.links.new(src, g.inputs[0]); g.inputs[1].default_value = r[0]
            nt.links.new(src, l.inputs[0]); l.inputs[1].default_value = r[1]
            m = nt.nodes.new('ShaderNodeMath'); m.operation = 'MULTIPLY'
            nt.links.new(g.outputs[0], m.inputs[0]); nt.links.new(l.outputs[0], m.inputs[1])
            if acc is None:
                acc = m.outputs[0]
            else:
                m2 = nt.nodes.new('ShaderNodeMath'); m2.operation = 'MULTIPLY'
                nt.links.new(acc, m2.inputs[0]); nt.links.new(m.outputs[0], m2.inputs[1])
                acc = m2.outputs[0]
        return acc
    absx = nt.nodes.new('ShaderNodeMath'); absx.operation = 'ABSOLUTE'
    nt.links.new(sep.outputs['X'], absx.inputs[0])
    def absbox(xr, yr, zr):
        # like box() but on |x|
        g = nt.nodes.new('ShaderNodeMath'); g.operation = 'GREATER_THAN'
        l = nt.nodes.new('ShaderNodeMath'); l.operation = 'LESS_THAN'
        nt.links.new(absx.outputs[0], g.inputs[0]); g.inputs[1].default_value = xr[0]
        nt.links.new(absx.outputs[0], l.inputs[0]); l.inputs[1].default_value = xr[1]
        m = nt.nodes.new('ShaderNodeMath'); m.operation = 'MULTIPLY'
        nt.links.new(g.outputs[0], m.inputs[0]); nt.links.new(l.outputs[0], m.inputs[1])
        rest = box(None, yr, zr)
        m2 = nt.nodes.new('ShaderNodeMath'); m2.operation = 'MULTIPLY'
        nt.links.new(m.outputs[0], m2.inputs[0]); nt.links.new(rest, m2.inputs[1])
        return m2.outputs[0]
    col = t.outputs['Color']
    def shade(col, mask, factor=None, rgb=None):
        mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
        nt.links.new(mask, mix.inputs['Factor'])
        nt.links.new(col, mix.inputs[6])
        if factor is not None:
            mm = nt.nodes.new('ShaderNodeMix'); mm.data_type = 'RGBA'; mm.blend_type = 'MULTIPLY'
            mm.inputs['Factor'].default_value = 1
            nt.links.new(col, mm.inputs[6]); mm.inputs[7].default_value = (factor, factor, factor, 1)
            nt.links.new(mm.outputs[2], mix.inputs[7])
        else:
            mix.inputs[7].default_value = (*rgb, 1)
        return mix.outputs[2]
    pz0, pz1 = pel.z + 0.02, pel.z + 0.19
    col = shade(col, box((-0.13, 0.13), (-1, -0.02), (pz0, pz1)), factor=0.84)            # pocket
    col = shade(col, box((-0.135, 0.135), (-1, -0.02), (pz1, pz1 + 0.008)), factor=0.65)  # pocket top seam
    col = shade(col, absbox((0.022, 0.032), (-1, -0.02), (nk.z - 0.2, nk.z - 0.01)), rgb=(0.9, 0.9, 0.88))  # strings
    col = shade(col, box(None, None, (-1, pel.z - 0.02)), factor=0.8)                     # ribbed hem
    col = shade(col, absbox((abs(hand.x) - 0.09, 9), (-1, 1), (-1, 9)), factor=0.8)        # cuffs
    return col


# ------------------------------------------------------------------ 3. LODs + export
from ctl_mesh import clean_weights, dup, decimate


def set_material(o, name, tex_rel):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    if not any(n.type == 'TEX_IMAGE' for n in nt.nodes):
        n = nt.nodes.new('ShaderNodeTexImage')
        n.image = bpy.data.images.load(os.path.join(OUT, tex_rel), check_existing=True)
        nt.links.new(n.outputs['Color'], nt.nodes['Principled BSDF'].inputs['Base Color'])
        nt.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.8
    o.data.materials.clear()
    o.data.materials.append(m)


def ensure_skinned(o):
    _ensure_skinned(o, rig)


def export_fbx(path, objs):
    _export_fbx(path, objs, rig)


def lods_for(o, key, slot):
    return _lods_for(o, key, slot, rig)


# ------------------------------------------------------------------ coverage
from mathutils.bvhtree import BVHTree
_body_bm = bmesh.new()
_body_bm.from_mesh(body.data)
_body_bm.normal_update()
_body_bm.verts.ensure_lookup_table()
_body_attr = body.data.attributes['ctl_region']
_region_verts = {}
for p in body.data.polygons:
    _region_verts.setdefault(REG[_body_attr.data[p.index].value], set()).update(p.vertices)
NO_HIDE = {'hair', 'facial_hair', 'hat', 'glasses', 'acc', 'held', 'neck', 'top_addon'}


def region_cover(part_obj, slot, thresh=0.97, reach=0.07):
    """A body region is hidden by a part when (almost) every vertex of the region has the part's surface
    within `reach` along its normal. Geometric test, so it also works for our procedural shells."""
    if slot in NO_HIDE:
        return []
    bm = bmesh.new()
    bm.from_mesh(part_obj.data)
    bm.transform(part_obj.matrix_world)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    covered = set()
    for v in _body_bm.verts:
        n = v.normal
        hit = tree.ray_cast(v.co - n * 0.003, n, reach)
        if hit[0] is not None:
            covered.add(v.index)
    out = []
    for r, vs in _region_verts.items():
        if r == 'head':
            continue
        if len(vs & covered) / len(vs) >= thresh:
            out.append(r)
    return sorted(out)


# ================================================================== run
manifest_path = os.path.join(OUT, 'manifest.json')
manifest = json.load(open(manifest_path)) if (ONLY and os.path.exists(manifest_path)) else {}
manifest.update({
    'sex': SEX,
    'skeleton': {'rig_object': rig.name, 'bones': [b.name for b in rig.data.bones],
                 'parents': {b.name: (b.parent.name if b.parent else None) for b in rig.data.bones}},
    'conventions': {
        'units': 'metres (FBX exported with FBX_SCALE_ALL; import at scale 1)',
        'axes': 'Blender -Y front / Z up exported as -Z forward, Y up: tick Bake Axis Conversion in Unity',
        'rest_pose': 'T-pose', 'max_influences': {'LOD0': 4, 'LOD1': 4, 'LOD2': 2},
        'lod_naming': '<name>_LOD0/1/2 inside one FBX (Unity builds the LODGroup)',
        'textures': 'one tile per part (UV 0-1); pack per avatar at runtime into one atlas -> one material',
    },
})
manifest.setdefault('parts', {})

# hoodie: a copy of the sweater mesh with its own texture set
if 'part.sweater' in bpy.data.objects and 'part.hoodie' not in bpy.data.objects:
    sw = bpy.data.objects['part.sweater']
    hd = sw.copy(); hd.data = sw.data.copy(); hd.name = hd.data.name = 'part.hoodie'
    bpy.context.scene.collection.objects.link(hd)
    hd['ctl_slot'] = 'top'

FACE_L = CF.landmarks(rig, eyes, body)
if not ONLY or 'body' in ONLY:
    skin = skin_textures()
    eyetex = eye_textures()
    # body LODs: decimate the whole body, then split by region (no cracks between regions)
    body_objs, body_tris = [], {}
    for i, ratio in enumerate(C.LOD_BUDGET['body']):
        b = dup(body, f'body_LOD{i}')
        if i == 0:
            # LOD0: keep the face/torso, thin out the (mitten) hands and feet only
            vg = b.vertex_groups.new(name='_thin')
            attr = b.data.attributes['ctl_region']
            thin = set()
            for p in b.data.polygons:
                if REG[attr.data[p.index].value].startswith(('hand_', 'foot_')):
                    thin.update(p.vertices)
            vg.add(list(thin), 1.0, 'REPLACE')
            m = b.modifiers.new('Dec', 'DECIMATE'); m.ratio = ratio; m.vertex_group = '_thin'; m.use_symmetry = True
            select_only([b], b)
            bpy.ops.object.modifier_move_to_index(modifier='Dec', index=0)
            bpy.ops.object.modifier_apply(modifier='Dec')
        elif ratio:
            decimate(b, ratio=ratio)
        clean_weights(b, 2 if i == 2 else 4)
        bm = bmesh.new()
        bm.from_mesh(b.data)
        rl = bm.faces.layers.int.get('ctl_region')
        for ri, rname in enumerate(REG):
            faces = [f for f in bm.faces if f[rl] == ri]
            if not faces:
                continue
            bm2 = bm.copy()
            rl2 = bm2.faces.layers.int.get('ctl_region')
            bmesh.ops.delete(bm2, geom=[f for f in bm2.faces if f[rl2] != ri], context='FACES')
            bmesh.ops.delete(bm2, geom=[v for v in bm2.verts if not v.link_faces], context='VERTS')
            me = bpy.data.meshes.new(f'body_{rname}_LOD{i}')
            bm2.to_mesh(me)
            bm2.free()
            o = bpy.data.objects.new(me.name, me)
            bpy.context.scene.collection.objects.link(o)
            for vg in b.vertex_groups:
                o.vertex_groups.new(name=vg.name)
            # bmesh copies deform weights through the deform layer
            if rname == 'head' and i < 2:
                manifest.setdefault('face_shapes', CF.add_face_shapes(o, FACE_L, mouth=True))
            ensure_skinned(o)
            set_material(o, f'M_{SEX}_skin', skin['tone2'])
            body_objs.append(o)
            body_tris.setdefault(rname, [0, 0, 0])[i] = tris(o)
        bm.free()
        bpy.data.objects.remove(b, do_unlink=True)
        e, lash_idx = CF.eyes_with_lashes(eyes, lashes, body, f'eyes_LOD{i}')
        clean_weights(e, 1)
        if i < 2:
            CF.add_blink_only(e, FACE_L, lash_idx)
        ensure_skinned(e)
        set_material(e, f'M_{SEX}_eyes', eyetex['brown'])
        body_objs.append(e)
    fbx = os.path.join(OUT, 'body', f'ctl_{SEX}_body.fbx')
    export_fbx(fbx, body_objs)
    manifest['body'] = {'fbx': rel(fbx), 'regions': REG, 'tris_per_region': body_tris,
                        'tris_total': [sum(v[i] for v in body_tris.values()) for i in range(3)],
                        'eyes_tris': tris(eyes) + tris(lashes), 'skin_textures': skin, 'eye_textures': eyetex,
                        'blend_shapes': {'meshes': ['body_head_LOD0', 'body_head_LOD1', 'eyes_LOD0 (blink only)', 'eyes_LOD1 (blink only)'],
                                         'names': manifest.get('face_shapes', []),
                                         'note': 'blink/blink_l/blink_r = eyelids; jaw_open + viseme_A/E/I/O/U for lip-sync (uLipSync), '
                                                 'mouth_smile/mouth_frown for mood. The jaw bone can drive mouth opening too.'},
                        'source': 'MPFB 2.0.17 / MakeHuman system assets (CC0): proxy ' + C.BODIES[SEX]['proxy']}
    for o in body_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    log('body', manifest['body']['tris_total'])

for o in sorted([o for o in bpy.data.objects if o.name.startswith('part.')], key=lambda o: o.name):
    key = o.name[5:]
    if ONLY and key not in ONLY:
        continue
    slot = o.get('ctl_slot', 'acc')
    try:
        texs = part_textures(key, o)
    except Exception as ex:  # keep going, report
        log('TEXTURE FAILED', key, repr(ex))
        raise
    lods = lods_for(o, key, slot)
    default = next(iter(texs))
    for d in lods:
        set_material(d, f'M_{SEX}_{key}', texs[default])
    fbx = os.path.join(OUT, 'parts', f'{key}.fbx')
    export_fbx(fbx, lods)
    src = o.get('ctl_source', '')
    hides = region_cover(o, slot)
    manifest['parts'][key] = {
        'slot': slot, 'fbx': rel(fbx), 'tris': [tris(d) for d in lods], 'textures': texs, 'default': default,
        'tile': int(o.get('ctl_tile', 256)), 'hides_regions': hides,
        'hair_mode': o.get('ctl_hair_mode', 'short' if slot == 'hat' else None), 'source': src or 'ctlife:procedural',
        'emissive_variants': [v for v in texs if (key, None) in C.EMISSIVE or (key, v) in C.EMISSIVE],
        'license': 'CC0',
    }
    for d in lods:
        bpy.data.objects.remove(d, do_unlink=True)
    log('part', key, slot, manifest['parts'][key]['tris'], 'variants', list(texs), 'hides', hides)

json.dump(manifest, open(manifest_path, 'w'), indent=1)
shutil.rmtree(TMP, ignore_errors=True)
log('manifest', manifest_path)
