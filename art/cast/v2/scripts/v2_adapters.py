"""Source adapters for cast v2. Each adapter loads one base character into the current Blender scene and
returns (armature, [mesh objects]) where:
  * the armature uses the CTS hierarchy/names (Quaternius Universal skeleton) in the base's own proportions,
    feet on z=0, facing -Y, metres;
  * every mesh is skinned to that armature (vertex groups named after CTS bones) and carries face int
    attributes 'region' (REGION_ID) and 'part' (PART_ID: which swappable slot it belongs to).
"""
import bpy, bmesh, math, json, re, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
from v2_common import *

PART_ID = {'body': 1, 'head': 2, 'hair': 3, 'top': 4, 'bottom': 5, 'feet': 6, 'brows': 7, 'eyes': 8, 'acc': 9}
PART_NAME = {v: k for k, v in PART_ID.items()}

def set_face_int(o, name, values):
    me = o.data
    a = me.attributes.get(name) or me.attributes.new(name=name, type='INT', domain='FACE')
    if isinstance(values, int):
        values = [values] * len(me.polygons)
    a.data.foreach_set('value', list(map(int, values)))

def get_face_int(o, name):
    a = o.data.attributes.get(name)
    out = np.zeros(len(o.data.polygons), dtype=np.int32)
    if a is not None:
        a.data.foreach_get('value', out)
    return out

def import_any(path):
    before = set(bpy.data.objects)
    p = str(path)
    if p.lower().endswith(('.gltf', '.glb')):
        bpy.ops.import_scene.gltf(filepath=p)
    elif p.lower().endswith('.fbx'):
        bpy.ops.import_scene.fbx(filepath=p, automatic_bone_orientation=False, ignore_leaf_bones=False)
    else:
        raise ValueError(p)
    return [o for o in bpy.data.objects if o not in before]

def face_uv_centers(o, uv_name=None):
    me = o.data
    uvl = me.uv_layers[uv_name] if uv_name else me.uv_layers.active
    uv = np.empty(len(me.loops) * 2, dtype=np.float32); uvl.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
    out = np.zeros((len(me.polygons), 2), dtype=np.float32)
    for p in me.polygons:
        out[p.index] = uv[p.loop_start:p.loop_start + p.loop_total].mean(0)
    return out

def material_base_image(mat):
    if not mat or not mat.node_tree:
        return None
    bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        return None
    stack = [bsdf.inputs['Base Color']]
    while stack:
        s = stack.pop()
        for l in s.links:
            n = l.from_node
            if n.type == 'TEX_IMAGE' and n.image:
                return n.image
            stack += list(n.inputs)
    return None

def material_uv_name(mat):
    """Name of the UV map the material's base-colour image samples (via a UV Map node), else None."""
    if not mat or not mat.node_tree:
        return None
    img = None
    for n in mat.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image is not None and n.image == material_base_image(mat):
            img = n
            break
    if img is None:
        return None
    stack = [img.inputs['Vector']]
    while stack:
        sck = stack.pop()
        for l in sck.links:
            fn = l.from_node
            if fn.type == 'UVMAP':
                return fn.uv_map or None
            stack += list(fn.inputs)
    return None

def material_base_color(mat):
    if not mat:
        return (0.8, 0.8, 0.8)
    if mat.node_tree:
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is not None and not bsdf.inputs['Base Color'].is_linked:
            return tuple(bsdf.inputs['Base Color'].default_value[:3])
    return tuple(mat.diffuse_color[:3])

def sample_image(img, uvs):
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32); img.pixels.foreach_get(px); px = px.reshape(h, w, 4)
    x = np.clip((uvs[:, 0] % 1.0) * (w - 1), 0, w - 1).astype(int)
    y = np.clip((uvs[:, 1] % 1.0) * (h - 1), 0, h - 1).astype(int)
    return px[y, x, :3]

# ------------------------------------------------------------------ UBC (already CTS)
# Default hair per body; the rest of the UBC hair set is exported as a swappable hair library. Every
# style was authored for one of the two bodies (FIT); styles from the other body are cross-fitted by
# mapping the source head box onto the target head box (anisotropic scale about the head centre).
UBC_HAIR_DEFAULT = {'ubc_m': ['Hair_SimpleParted', 'Eyebrows_Regular'], 'ubc_f': ['Hair_Long', 'Eyebrows_Female']}
UBC_HAIR_FIT = {'Hair_Buzzed': 'ubc_m', 'Hair_SimpleParted': 'ubc_m', 'Hair_Beard': 'ubc_m', 'Eyebrows_Regular': 'ubc_m',
                'Hair_BuzzedFemale': 'ubc_f', 'Hair_Buns': 'ubc_f', 'Hair_Long': 'ubc_f', 'Eyebrows_Female': 'ubc_f'}
UBC_HAIR_LIBRARY = {'ubc_m': {'hair': ['Hair_SimpleParted', 'Hair_Buzzed', 'Hair_Long', 'Hair_Buns'], 'facial': ['Hair_Beard']},
                    'ubc_f': {'hair': ['Hair_Long', 'Hair_Buns', 'Hair_BuzzedFemale', 'Hair_SimpleParted'], 'facial': []}}
UBC_BODY_FILE = {'ubc_m': 'Superhero_Male_FullBody.gltf', 'ubc_f': 'Superhero_Female_FullBody.gltf'}
EXTRAS = []      # [{'kind': 'hair'|'facial', 'style': str, 'obj': Object}] filled by load_ubc, consumed by v2_build

BONE_PART = [('Head', 'head'), ('neck_01', 'head'), ('foot', 'feet'), ('ball', 'feet'), ('thigh', 'bottom'), ('calf', 'bottom'),
             ('pelvis', 'bottom')]

def part_by_bone(o):
    """Per-face body slot from the dominant bone of its vertices (head / top / bottom / feet)."""
    gi = {g.index: g.name for g in o.vertex_groups}
    dom = []
    for v in o.data.vertices:
        g = max(v.groups, key=lambda g: g.weight) if v.groups else None
        dom.append(gi.get(g.group, '') if g else '')
    out = []
    for p in o.data.polygons:
        votes = {}
        for vi in p.vertices:
            n = dom[vi]
            part = 'top'
            for pre, pt in BONE_PART:
                if n.startswith(pre):
                    part = pt; break
            votes[part] = votes.get(part, 0) + 1
        out.append(PART_ID[max(votes, key=votes.get)])
    return out

def head_box(o):
    """(centre, size) of the vertices dominated by the Head bone (world space)."""
    gi = {g.index: g.name for g in o.vertex_groups}
    P = []
    for v in o.data.vertices:
        if v.groups:
            g = max(v.groups, key=lambda g: g.weight)
            if gi.get(g.group) == 'Head' and g.weight > 0.5:
                P.append(tuple(o.matrix_world @ v.co))
    P = np.array(P)
    lo, hi = P.min(0), P.max(0)
    return (lo + hi) / 2, hi - lo

def ubc_body_head_box(base_id):
    objs = import_any(UBC_GLTF / UBC_BODY_FILE[base_id])
    body = max((o for o in objs if o.type == 'MESH' and o.vertex_groups), key=lambda o: len(o.data.vertices))
    bpy.context.view_layer.update()
    box = head_box(body)
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    return box

def bone_w(arm, name, tail=False):
    b = arm.data.bones[name]
    return arm.matrix_world @ (b.tail_local if tail else b.head_local)

def ubc_outfit(arm, o):
    """Paint a default CT-casual outfit onto the UBC body (no new garment geometry): crew-neck tee with
    short sleeves, long trousers, sneakers. Hem lines are cut into the mesh with bisect planes so the
    tint-region borders are clean; trainer feet are smoothed (no toes) and slightly inflated; cloth faces
    get neutral fabric materials (tinted at runtime via the region masks)."""
    me = o.data
    gi = {g.index: g.name for g in o.vertex_groups}
    inv = o.matrix_world.inverted()
    def plane(p, n):
        n = Vector(n).normalized()
        return inv @ Vector(p), (inv.to_3x3() @ n).normalized()
    pelvis = bone_w(arm, 'pelvis'); neck = bone_w(arm, 'neck_01')
    z_hem = pelvis.z + 0.045
    ank = {s: bone_w(arm, f'foot_{s}') for s in 'lr'}
    z_ank = {s: ank[s].z + 0.035 for s in 'lr'}
    P_neck = neck + Vector((0, 0, -0.012)); n_neck = Vector((0, -0.35, 1))
    sleeve = {}
    for s in 'lr':
        h, t = bone_w(arm, f'upperarm_{s}'), bone_w(arm, f'upperarm_{s}', True)
        sleeve[s] = (h + 0.45 * (t - h), (t - h).normalized())
    bm = bmesh.new(); bm.from_mesh(me)
    deform = bm.verts.layers.deform.active
    def dom_v(v):
        d = v[deform] if deform else {}
        if not d:
            return ''
        gidx = max(d.items(), key=lambda kv: kv[1])[0]
        return gi.get(gidx, '')
    def dom_f(f):
        votes = {}
        for v in f.verts:
            n = dom_v(v); votes[n] = votes.get(n, 0) + 1
        return max(votes, key=votes.get)
    def cut(pred, p, n):
        bm.faces.ensure_lookup_table()
        faces = [f for f in bm.faces if pred(dom_f(f))]
        geom = list({e for f in faces for e in f.edges}) + faces + list({v for f in faces for v in f.verts})
        lp, ln = plane(p, n)
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=lp, plane_no=ln, dist=1e-5)
    for s in 'lr':
        cut(lambda d, s=s: d in (f'upperarm_{s}', f'lowerarm_{s}'), *sleeve[s])
        cut(lambda d, s=s: d in (f'calf_{s}', f'foot_{s}', f'ball_{s}'), Vector((0, 0, z_ank[s])), (0, 0, 1))
    cut(lambda d: d in ('pelvis', 'spine_01', 'spine_02', 'thigh_l', 'thigh_r'), Vector((0, 0, z_hem)), (0, 0, 1))
    cut(lambda d: d in ('neck_01', 'spine_03', 'spine_02', 'clavicle_l', 'clavicle_r'), P_neck, n_neck)
    bm.faces.index_update(); bm.faces.ensure_lookup_table()
    mw = o.matrix_world
    # neck radius: horizontal spread of neck-dominated vertices around the neck axis (traps outside it stay 'top')
    nd = [ (mw @ v.co) for v in bm.verts if dom_v(v) == 'neck_01']
    rr = sorted(math.hypot(p.x - neck.x, p.y - neck.y) for p in nd) or [0.06]
    R_neck = rr[int(0.85 * (len(rr) - 1))] + 0.012
    REG = {}
    for f in bm.faces:
        d = dom_f(f)
        c = mw @ f.calc_center_median()
        side = d[-1] if d[-2:] in ('_l', '_r') else None
        r = 'skin'
        if d.startswith(('upperarm', 'lowerarm', 'hand')) or d.startswith(('thumb', 'index', 'middle', 'ring', 'pinky')):
            if d.startswith('upperarm') and (c - sleeve[side][0]).dot(sleeve[side][1]) < 0:
                r = 'top'
        elif d in ('pelvis', 'spine_01', 'spine_02', 'spine_03', 'clavicle_l', 'clavicle_r', 'neck_01'):
            if c.z < z_hem:
                r = 'bottom'
            elif (c - P_neck).dot(n_neck.normalized()) < 0 or math.hypot(c.x - neck.x, c.y - neck.y) > R_neck:
                r = 'top'
        elif d.startswith(('thigh', 'calf')):
            r = 'shoes' if c.z < z_ank[side] else ('top' if c.z >= z_hem else 'bottom')
        elif d.startswith(('foot', 'ball')):
            r = 'shoes'
        if r == 'shoes' and c.z < 0.018:
            r = 'sole'
        REG[f.index] = r
    # trainers: smooth away toes, inflate a little; cloth: inflate a hair so hems read
    shoe_v = {v for f in bm.faces if REG[f.index] in ('shoes', 'sole') for v in f.verts}
    skinish = {v for f in bm.faces if REG[f.index] not in ('shoes', 'sole') for v in f.verts}
    inner = [v for v in shoe_v if v not in skinish]
    bmesh.ops.smooth_vert(bm, verts=inner, factor=0.6, use_axis_x=True, use_axis_y=True, use_axis_z=False, mirror_clip_x=False)
    bmesh.ops.smooth_vert(bm, verts=inner, factor=0.6, use_axis_x=True, use_axis_y=True, use_axis_z=False, mirror_clip_x=False)
    bm.normal_update()
    for v in inner:
        v.co += v.normal * 0.006
        v.co.z = max(v.co.z, 0.0)
    for rname, off in (('top', 0.003), ('bottom', 0.0035)):
        vs = {v for f in bm.faces if REG[f.index] == rname for v in f.verts}
        others = {v for f in bm.faces if REG[f.index] != rname for v in f.verts}
        for v in vs - others:
            v.co += v.normal * off
    bm.to_mesh(me); bm.free()
    # fabric materials (flat neutral + subtle procedural grain, baked by the DIFFUSE colour pass)
    def fabric(name, grey, grain):
        m = bpy.data.materials.new(name); m.use_nodes = True
        nt = m.node_tree; b = nt.nodes['Principled BSDF']
        nz = nt.nodes.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 140; nz.inputs['Detail'].default_value = 2
        rp = nt.nodes.new('ShaderNodeValToRGB')
        rp.color_ramp.elements[0].color = (grey * (1 - grain),) * 3 + (1,)
        rp.color_ramp.elements[1].color = (grey * (1 + grain),) * 3 + (1,)
        nt.links.new(nz.outputs['Fac'], rp.inputs['Fac']); nt.links.new(rp.outputs['Color'], b.inputs['Base Color'])
        return m
    mats = {'top': fabric('ubc_tee', 0.42, 0.10), 'bottom': fabric('ubc_trousers', 0.36, 0.14),
            'shoes': fabric('ubc_trainers', 0.70, 0.04), 'sole': fabric('ubc_sole', 0.05, 0.05)}
    idx = {}
    for k, m in mats.items():
        me.materials.append(m); idx[k] = len(me.materials) - 1
    regs = []
    for p in me.polygons:
        r = REG.get(p.index, 'skin')
        if r in idx:
            p.material_index = idx[r]
        regs.append({'skin': REGION_ID['skin'], 'top': REGION_ID['top'], 'bottom': REGION_ID['bottom'],
                     'shoes': REGION_ID['shoes'], 'sole': REGION_ID['none']}[r])
    return regs

def load_ubc_hair(style, arm, target, boxes):
    hobjs = import_any(UBC_HAIR / f'{style}.gltf')
    harm = next((x for x in hobjs if x.type == 'ARMATURE'), None)
    out = []
    for x in hobjs:
        if x.type == 'MESH' and x.vertex_groups:
            bpy.context.view_layer.update()
            mw = x.matrix_world.copy()
            x.parent = None; x.matrix_world = mw
            src = UBC_HAIR_FIT[style]
            if src != target:
                (cs, ss), (ct, st) = boxes[src], boxes[target]
                sc = st / ss
                M = Matrix.Translation(Vector(ct)) @ Matrix.Diagonal((*sc, 1.0)) @ Matrix.Translation(-Vector(cs))
                x.data.transform(M @ mw); x.matrix_world = Matrix.Identity(4)
            x.parent = arm
            x.matrix_parent_inverse = arm.matrix_world.inverted()
            for md in list(x.modifiers):
                if md.type == 'ARMATURE':
                    x.modifiers.remove(md)
            x.modifiers.new('arm', 'ARMATURE').object = arm
            img = material_base_image(x.active_material)
            hset = 2 if img is not None and 'Hair_2' in img.name else 1
            set_face_int(x, 'region', REGION_ID['hair'])
            set_face_int(x, 'hair_set', hset)
            set_face_int(x, 'part', PART_ID['brows'] if style.startswith('Eyebrows') else PART_ID['hair'])
            x['hair_style'] = style
            out.append(x)
        elif x is not harm:
            bpy.data.objects.remove(x, do_unlink=True)
    if harm:
        bpy.data.objects.remove(harm, do_unlink=True)
    return out

def load_ubc(base_id):
    EXTRAS.clear()
    other = 'ubc_f' if base_id == 'ubc_m' else 'ubc_m'
    boxes = {other: ubc_body_head_box(other)}
    objs = import_any(UBC_GLTF / UBC_BODY_FILE[base_id])
    arm = next(o for o in objs if o.type == 'ARMATURE')
    meshes = []
    for o in objs:
        if o.type != 'MESH':
            continue
        if not o.vertex_groups or o.name.startswith('Eyebrows'):
            bpy.data.objects.remove(o, do_unlink=True)   # eyebrows come from the hairstyle set
            continue
        meshes.append(o)
    bpy.context.view_layer.update()
    for o in meshes:
        if o.name.startswith('Eyes'):
            set_face_int(o, 'region', REGION_ID['eyes']); set_face_int(o, 'part', PART_ID['eyes'])
        else:
            boxes[base_id] = head_box(o)
            regs = ubc_outfit(arm, o)
            set_face_int(o, 'region', regs); set_face_int(o, 'part', part_by_bone(o))
    for h in UBC_HAIR_DEFAULT[base_id]:
        meshes += load_ubc_hair(h, arm, base_id, boxes)
    lib = UBC_HAIR_LIBRARY[base_id]
    for kind in ('hair', 'facial'):
        for style in lib[kind]:
            if style in UBC_HAIR_DEFAULT[base_id]:
                continue
            for x in load_ubc_hair(style, arm, base_id, boxes):
                EXTRAS.append({'kind': kind, 'style': style, 'obj': x})
    return arm, meshes

# ------------------------------------------------------------------ Quaternius Ultimate Modular Men/Women
# Filled in after the official pack files are on disk (see fetch_modular.sh); the rig/material names are
# read from the actual files, never assumed.
MODULAR_REGION_RULES = [
    (r'skin|face|head(?!phone)|body_skin', 'skin'),
    (r'hair|beard|brow|mustache|moustache', 'hair'),
    (r'eye', 'eyes'),
    (r'shoe|boot|sneaker|sole|feet|foot', 'shoes'),
    (r'pant|jean|trouser|short|skirt|leg', 'bottom'),
    (r'shirt|top|jacket|hoodie|coat|vest|suit|sweater|dress|torso|body|chest|armor|armour', 'top'),
]

def region_from_material_name(name):
    n = name.lower()
    for pat, reg in MODULAR_REGION_RULES:
        if re.search(pat, n):
            return reg
    return 'accent'

ADAPTERS = {'ubc': load_ubc}

# ------------------------------------------------------------------ generic rigged source -> CTS
# Name patterns for common humanoid rigs (Mixamo, Quaternius classic, UE/CTS, Blender Rigify-like).
# Each CTS bone lists regexes tried in order against the source bone name (side handled separately).
_SIDE_L = r'(?:^|[._:\s-])(?:l|left)(?:$|[._:\s-])|left|_l$|\.l$|^l_|l$'
CTS_PATTERNS = {
    'pelvis': [r'^(mixamorig\d*:)?hips?$', r'pelvis', r'^hips?$', r'^root_?hips?$'],
    'spine_01': [r'^(mixamorig\d*:)?spine$', r'spine_?0?1$', r'^abdomen', r'^spine$', r'lower_?back'],
    'spine_02': [r'^(mixamorig\d*:)?spine1$', r'spine_?0?2$', r'^torso$', r'^chest$', r'^spine\.?001$'],
    'spine_03': [r'^(mixamorig\d*:)?spine2$', r'spine_?0?3$', r'upper_?chest', r'^chest$', r'^spine\.?002$'],
    'neck_01': [r'neck'],
    'Head': [r'^(mixamorig\d*:)?head$', r'^head$'],
}
LIMB_PATTERNS = {
    'clavicle': [r'shoulder', r'clavicle', r'collar'],
    'upperarm': [r'upper_?arm', r'(?<!fore)arm$', r'^(mixamorig\d*:)?(left|right)arm$'],
    'lowerarm': [r'fore_?arm', r'lower_?arm', r'elbow'],
    'hand': [r'hand$', r'wrist', r'fist', r'palm'],
    'thigh': [r'up_?leg', r'upper_?leg', r'thigh'],
    'calf': [r'(?<!up)leg$', r'lower_?leg', r'calf', r'shin', r'knee'],
    'foot': [r'foot$', r'ankle'],
    'ball': [r'toe_?base', r'toes?$', r'ball'],
}
FINGERS = {'thumb': r'thumb', 'index': r'index', 'middle': r'middle', 'ring': r'ring', 'pinky': r'pinky|little'}

def side_of(name, x):
    n = name.lower()
    if re.search(r'(left|_l$|\.l$|^l_|:l|lef)', n):
        return 'l'
    if re.search(r'(right|_r$|\.r$|^r_|:r|rig)', n) and 'mixamorig' not in n.replace('mixamorig', ''):
        return 'r'
    return 'l' if x > 0 else 'r'

def map_source_bones(arm):
    """Return {source_bone: cts_bone} using name patterns + hierarchy sanity (side from name or x)."""
    mw = arm.matrix_world
    out = {}
    names = [b.name for b in arm.data.bones]
    low = {n: re.sub(r'^mixamorig\d*:', '', n).lower() for n in names}
    for cts, pats in CTS_PATTERNS.items():
        for pat in pats:
            hit = [n for n in names if re.search(pat, n.lower()) and n not in out]
            if hit:
                out[min(hit, key=len)] = cts
                break
    for n in names:
        if n in out:
            continue
        b = arm.data.bones[n]
        x = (mw @ b.head_local).x
        l = low[n]
        if abs(x) < 0.02 and not re.search('finger|thumb|index|middle|ring|pinky|little', l):
            continue
        s = side_of(n, x)
        fmatch = next((f for f, pat in FINGERS.items() if re.search(pat, l)), None)
        if fmatch:
            m = re.search(r'(\d+)(?!.*\d)', l)
            idx = int(m.group(1)) if m else 1
            if 'mixamo' in n.lower() or re.search(r'[1-4]$', l):
                idx = max(1, min(4, idx))
            out[n] = f'{fmatch}_0{min(idx, 4)}_{s}' if idx < 4 else f'{fmatch}_04_leaf_{s}'
            continue
        for part, pats in LIMB_PATTERNS.items():
            if any(re.search(p, l) for p in pats):
                out[n] = f'{part}_{s}'
                break
    return out

def build_cts_from_source(src_arm, mapping):
    """New armature 'CTS' fitted to the source joints. Missing CTS joints are interpolated."""
    mw = src_arm.matrix_world
    pos = {}
    for sname, cts in mapping.items():
        b = src_arm.data.bones[sname]
        pos.setdefault(cts, (mw @ b.head_local, mw @ b.tail_local))
    def P(n):
        return pos[n][0] if n in pos else None
    # spine interpolation
    if 'pelvis' in pos and 'neck_01' in pos:
        a, b_ = P('pelvis'), P('neck_01')
        for k, f in (('spine_01', 0.18), ('spine_02', 0.45), ('spine_03', 0.72)):
            if k not in pos:
                pos[k] = (a.lerp(b_, f), a.lerp(b_, f + 0.25))
    if 'Head' in pos:
        h = P('Head')
        top = max((mw @ bb.tail_local).z for bb in src_arm.data.bones)
        pos['Head'] = (h, Vector((h.x, h.y, max(h.z + 0.12, top))))
    for s in ('l', 'r'):
        if f'clavicle_{s}' not in pos and f'upperarm_{s}' in pos:
            u = P(f'upperarm_{s}'); c = P('spine_03')
            pos[f'clavicle_{s}'] = (c.lerp(u, 0.15) + Vector((0, 0, 0.02)), u)
        if f'ball_{s}' not in pos and f'foot_{s}' in pos:
            f_ = P(f'foot_{s}')
            pos[f'ball_{s}'] = (Vector((f_.x, f_.y - 0.10, 0.02)), Vector((f_.x, f_.y - 0.16, 0.02)))
        pos[f'ball_leaf_{s}'] = (pos[f'ball_{s}'][1], pos[f'ball_{s}'][1] + Vector((0, -0.03, 0)))
        hand = P(f'hand_{s}'); fore = P(f'lowerarm_{s}')
        hdir = (hand - fore).normalized() if hand is not None and fore is not None else Vector((1 if s == 'l' else -1, 0, 0))
        for i, f in enumerate(('thumb', 'index', 'middle', 'ring', 'pinky')):
            for j in (1, 2, 3):
                k = f'{f}_0{j}_{s}'
                if k not in pos:
                    off = Vector((0, -0.03 + 0.015 * i, 0)) if f != 'thumb' else Vector((0, -0.035, -0.01))
                    base = hand + hdir * (0.06 + 0.025 * (j - 1)) + off
                    pos[k] = (base, base + hdir * 0.022)
            k4 = f'{f}_04_leaf_{s}'
            if k4 not in pos:
                p3 = pos[f'{f}_03_{s}']
                pos[k4] = (p3[1], p3[1] + hdir * 0.015)
    pos['root'] = (Vector((0, 0, 0)), Vector((0, 0.1, 0)))
    arm_d = bpy.data.armatures.new('CTS'); arm = bpy.data.objects.new('CTS', arm_d)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for n in CTS_BONES:
        h, t = pos[n]
        if (t - h).length < 1e-4:
            t = h + Vector((0, 0, 0.02))
        eb = arm_d.edit_bones.new(n); eb.head = h; eb.tail = t
    for n, p in CTS_PARENT.items():
        if p:
            arm_d.edit_bones[n].parent = arm_d.edit_bones[p]
    bpy.ops.object.mode_set(mode='OBJECT')
    for b in arm_d.bones:
        b.use_deform = 'leaf' not in b.name and b.name != 'root'
    return arm

def retarget_weights(mesh, src_arm, mapping, cts_arm):
    """Rename/merge source vertex groups into CTS bones (unmapped groups fall back to the nearest mapped
    ancestor), rebind the mesh to the CTS armature."""
    parent_of = {b.name: (b.parent.name if b.parent else None) for b in src_arm.data.bones}
    def resolve(n):
        seen = 0
        while n and n not in mapping and seen < 32:
            n = parent_of.get(n); seen += 1
        return mapping.get(n)
    W = {}
    gi = {g.index: g.name for g in mesh.vertex_groups}
    for v in mesh.data.vertices:
        acc = {}
        for g in v.groups:
            tgt = resolve(gi.get(g.group))
            if tgt and g.weight > 0:
                acc[tgt] = acc.get(tgt, 0) + g.weight
        W[v.index] = acc
    mesh.vertex_groups.clear()
    groups = {n: mesh.vertex_groups.new(name=n) for n in CTS_BONES}
    for vi, acc in W.items():
        s = sum(acc.values())
        if s <= 0:
            continue
        for n, w in acc.items():
            groups[n].add([vi], w / s, 'REPLACE')
    for m in list(mesh.modifiers):
        if m.type == 'ARMATURE':
            mesh.modifiers.remove(m)
    mw = mesh.matrix_world.copy()
    mesh.parent = cts_arm; mesh.matrix_world = mw
    mesh.modifiers.new('arm', 'ARMATURE').object = cts_arm

# ------------------------------------------------------------------ Quaternius modular + CC-BY heroes (rigged)
MESH_PART_RULES = [(r'head|face|hat|helmet|hair', 'head'), (r'feet|foot|shoe|boot', 'feet'),
                   (r'leg|pant|lower|bottom|skirt', 'bottom'), (r'body|torso|upper|top|chest|arm', 'top')]

def part_from_mesh_name(name):
    n = name.lower()
    for pat, part in MESH_PART_RULES:
        if re.search(pat, n):
            return part
    return 'top'

def load_rigged(base_id):
    """Generic rigged source (FBX/glTF) -> CTS. Spec keys: file, mesh_parts ('by_name' | 'single'),
    region_overrides {material_name: region}."""
    import v2_spec as SPEC
    spec = SPEC.BASES[base_id]
    objs = import_any(spec['file'])
    src = next(o for o in objs if o.type == 'ARMATURE')
    src.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    mapping = map_source_bones(src)
    need = ['pelvis', 'neck_01', 'Head', 'upperarm_l', 'upperarm_r', 'lowerarm_l', 'lowerarm_r', 'hand_l', 'hand_r',
            'thigh_l', 'thigh_r', 'calf_l', 'calf_r', 'foot_l', 'foot_r']
    got = set(mapping.values())
    miss = [n for n in need if n not in got]
    if miss:
        raise RuntimeError(f'{base_id}: cannot map {miss}; source bones: {[b.name for b in src.data.bones][:60]}')
    cts = build_cts_from_source(src, mapping)
    meshes = []
    ovr = {k.lower(): v for k, v in spec.get('region_overrides', {}).items()}
    for o in objs:
        if o.type != 'MESH':
            continue
        if not o.vertex_groups:
            bpy.data.objects.remove(o, do_unlink=True); continue
        retarget_weights(o, src, mapping, cts)
        regs = []
        for p in o.data.polygons:
            mat = o.material_slots[p.material_index].material if o.material_slots else None
            mn = (mat.name if mat else '').split('.')[0]
            r = ovr.get(mn.lower()) or region_from_material_name(mn)
            regs.append(REGION_ID.get(r, REGION_ID['accent']))
        set_face_int(o, 'region', regs)
        if spec.get('mesh_parts', 'by_name') == 'by_name':
            pid = PART_ID[part_from_mesh_name(o.name)]
            parts = [PART_ID['hair'] if r == REGION_ID['hair'] and pid == PART_ID['head'] else pid for r in regs]
        else:
            parts = part_by_bone(o)
            parts = [PART_ID['hair'] if r == REGION_ID['hair'] else p for r, p in zip(regs, parts)]
        set_face_int(o, 'part', parts)
        meshes.append(o)
    log(base_id, 'bone map', len(mapping), 'meshes', [o.name for o in meshes])
    for o in objs:
        if o.type not in ('MESH',) and o is not src:
            try:
                bpy.data.objects.remove(o, do_unlink=True)
            except ReferenceError:
                pass
    bpy.data.objects.remove(src, do_unlink=True)
    return cts, meshes

ADAPTERS['rigged'] = load_rigged

# ------------------------------------------------------------------ explicit rig-family tables
def _mixamo_table():
    t = {'Hips': 'pelvis', 'Spine': 'spine_01', 'Spine1': 'spine_02', 'Spine2': 'spine_03', 'Neck': 'neck_01', 'Head': 'Head'}
    for side, s in (('Left', 'l'), ('Right', 'r')):
        t.update({f'{side}Shoulder': f'clavicle_{s}', f'{side}Arm': f'upperarm_{s}', f'{side}ForeArm': f'lowerarm_{s}',
                  f'{side}Hand': f'hand_{s}', f'{side}UpLeg': f'thigh_{s}', f'{side}Leg': f'calf_{s}',
                  f'{side}Foot': f'foot_{s}', f'{side}ToeBase': f'ball_{s}'})
        for f, q in (('Thumb', 'thumb'), ('Index', 'index'), ('Middle', 'middle'), ('Ring', 'ring'), ('Pinky', 'pinky')):
            for i in (1, 2, 3):
                t[f'{side}Hand{f}{i}'] = f'{q}_0{i}_{s}'
    return t
MIXAMO = _mixamo_table()

def _rigify_table():
    t = {'DEF-spine': 'pelvis', 'DEF-spine.001': 'spine_01', 'DEF-spine.002': 'spine_02', 'DEF-spine.003': 'spine_03',
         'DEF-spine.004': 'neck_01', 'DEF-spine.005': 'neck_01', 'DEF-spine.006': 'Head', 'DEF-neck': 'neck_01', 'DEF-head': 'Head'}
    for S, s in (('L', 'l'), ('R', 'r')):
        t.update({f'DEF-pelvis.{S}': 'pelvis', f'DEF-shoulder.{S}': f'clavicle_{s}', f'DEF-upper_arm.{S}': f'upperarm_{s}',
                  f'DEF-upper_arm.{S}.001': f'upperarm_{s}', f'DEF-forearm.{S}': f'lowerarm_{s}', f'DEF-forearm.{S}.001': f'lowerarm_{s}',
                  f'DEF-hand.{S}': f'hand_{s}', f'DEF-thigh.{S}': f'thigh_{s}', f'DEF-thigh.{S}.001': f'thigh_{s}',
                  f'DEF-shin.{S}': f'calf_{s}', f'DEF-shin.{S}.001': f'calf_{s}', f'DEF-foot.{S}': f'foot_{s}', f'DEF-toe.{S}': f'ball_{s}',
                  f'DEF-breast.{S}': 'spine_03'})
        for k in (1, 2, 3, 4):
            t[f'DEF-palm.0{k}.{S}'] = f'hand_{s}'
        for f, q in (('thumb', 'thumb'), ('f_index', 'index'), ('f_middle', 'middle'), ('f_ring', 'ring'), ('f_pinky', 'pinky')):
            for i in (1, 2, 3):
                t[f'DEF-{f}.0{i}.{S}'] = f'{q}_0{i}_{s}'
                t[f'{f}.0{i}.{S}'] = f'{q}_0{i}_{s}'          # control-bone groups some exports keep
        t[f'shoulder.{S}'] = f'clavicle_{s}'
    return t
RIGIFY = _rigify_table()

def canon_mixamo(n):
    n = re.sub(r'^mixamorig\d*:', '', n)
    return re.sub(r'_\d+$', '', n)

def map_rig_family(arm):
    names = [b.name for b in arm.data.bones]
    out = {}
    if any(n.startswith('mixamorig') for n in names):
        for n in names:
            c = canon_mixamo(n)
            if c in MIXAMO:
                out[n] = MIXAMO[c]
    elif any(n.startswith('DEF-') for n in names):
        low = {k.lower(): v for k, v in RIGIFY.items()}
        for n in names:
            if n in RIGIFY:
                out[n] = RIGIFY[n]
            elif n.lower() in low:
                out[n] = low[n.lower()]
    else:
        out = map_source_bones(arm)
    # primary segments first so joint positions come from the segment heads
    return dict(sorted(out.items(), key=lambda kv: ('.001' in kv[0], kv[0])))

def relink_images(model_dir):
    """Re-point missing textures to files in the model folder (matched by normalised basename);
    unresolvable images are unlinked so bakes fall back to the material's flat colour."""
    norm = lambda s: re.sub(r'[^a-z0-9]', '', Path(s).stem.lower())
    files = {}
    for f in Path(model_dir).rglob('*'):
        if f.suffix.lower() in ('.png', '.jpg', '.jpeg', '.tga'):
            files[norm(f.name)] = f
    for im in bpy.data.images:
        if im.source == 'FILE' and (im.size[0] == 0 or not Path(bpy.path.abspath(im.filepath)).exists()):
            k = norm(Path(im.filepath.replace('\\', '/')).name) or norm(im.name)
            if k in files:
                im.filepath = str(files[k]); im.reload()
    for m in bpy.data.materials:
        if not m.node_tree:
            continue
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and (n.image is None or n.image.size[0] == 0):
                for o in n.outputs:
                    for l in list(o.links):
                        m.node_tree.links.remove(l)

def fill_unweighted(mesh, arm):
    """Vertices without CTS weights get inverse-distance weights from the two nearest deform bones."""
    segs = [(b.name, arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones if b.use_deform]
    gi = {g.name: g for g in mesh.vertex_groups}
    n = 0
    for v in mesh.data.vertices:
        if sum(g.weight for g in v.groups) > 1e-4:
            continue
        p = mesh.matrix_world @ v.co
        ds = []
        for name, h, t in segs:
            ab = t - h
            u = max(0.0, min(1.0, (p - h).dot(ab) / max(ab.length_squared, 1e-9)))
            ds.append(((h + ab * u - p).length, name))
        ds.sort()
        (d0, b0), (d1, b1) = ds[0], ds[1]
        w0 = 1 / max(d0, 1e-4); w1 = 1 / max(d1, 1e-4)
        gi[b0].add([v.index], w0 / (w0 + w1), 'REPLACE'); gi[b1].add([v.index], w1 / (w0 + w1), 'ADD')
        n += 1
    return n

def head_top_z(objs):
    """Top of the head: highest vertex whose dominant weight is Head (robust to stray geometry)."""
    z = 0.0
    for o in objs:
        gi = {g.index: g.name for g in o.vertex_groups}
        for v in o.data.vertices:
            if v.groups:
                g = max(v.groups, key=lambda g: g.weight)
                if gi.get(g.group) == 'Head':
                    z = max(z, (o.matrix_world @ v.co).z)
    return z

def drop_strays(o, arm, max_dist=0.45):
    """Delete vertices farther than max_dist from every deform bone (stray props / helper geometry)."""
    segs = [(arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones if b.use_deform]
    H = np.array([tuple(h) for h, t in segs]); T = np.array([tuple(t) for h, t in segs])
    P = np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices])
    AB = T - H
    far = []
    for i, p in enumerate(P):
        u = np.clip(((p - H) * AB).sum(1) / np.maximum((AB * AB).sum(1), 1e-9), 0, 1)
        d = np.linalg.norm(H + AB * u[:, None] - p, axis=1).min()
        if d > max_dist:
            far.append(i)
    if far:
        bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in far], context='VERTS')
        bm.to_mesh(o.data); bm.free(); o.data.update()
    return len(far)

def scale_to_height(objs, arm, target):
    """Uniform scale so the top of the head is at target metres. Children of the armature are scaled
    through it (never twice)."""
    bpy.context.view_layer.update()
    top = head_top_z(objs) or max(max((o.matrix_world @ v.co).z for v in o.data.vertices) for o in objs)
    zmin = min(min((o.matrix_world @ v.co).z for v in o.data.vertices) for o in objs)
    k = target / max(top - zmin, 1e-3)
    for o in [arm] + [o for o in objs if o.parent is not arm]:
        o.scale = o.scale * k
        o.location = o.location * k
    bpy.context.view_layer.update()
    return k

def regions_by_rules(o, rules, default='top', height=None):
    """rules: list of (kind, pattern, region) with kind 'mat' (material name regex) or 'col' (texture colour
    predicate name) evaluated per face; height split for top/bottom at hips."""
    import v2_spec as SPEC
    regs = []
    uvc = face_uv_centers(o) if o.data.uv_layers else None
    cache = {}
    for p in o.data.polygons:
        mat = o.material_slots[p.material_index].material if o.material_slots else None
        mn = (mat.name if mat else '').lower()
        z = (o.matrix_world @ p.center).z
        if height:
            z = z / height          # rules use fractions of the character height
        reg = None
        for kind, pat, r in rules:
            if kind == 'mat' and re.search(pat, mn):
                reg = r; break
            if kind == 'zbelow' and z < float(pat):
                reg = r; break
            if kind == 'zabove' and z > float(pat):
                reg = r; break
        reg = reg or default
        regs.append(reg)
    return regs

def load_hero(base_id):
    """CC-BY masked heroes: rigged sources mapped by family table; unrigged sources auto-rigged onto CTS."""
    import v2_spec as SPEC
    spec = SPEC.BASES[base_id]
    path = Path(spec['file'])
    if path.suffix.lower() == '.fbx':
        bpy.ops.import_scene.fbx(filepath=str(path), automatic_bone_orientation=False, ignore_leaf_bones=False)
    else:
        bpy.ops.import_scene.gltf(filepath=str(path))
    relink_images(path.parent.parent if path.parent.name == 'source' else path.parent)
    # Optional, source-pinned exclusions for isolated review candidates only.
    # Default specs declare none; never infer exclusions from a generic material name.
    for exclusion in spec.get('source_component_exclusions', []):
        if hashlib.sha256(path.read_bytes()).hexdigest() != exclusion['source_sha256']:
            raise ValueError('Source changed before component exclusion')
        matches = [o for o in bpy.context.scene.objects if o.name == exclusion['object'] and o.type == 'MESH']
        if len(matches) != 1:
            raise ValueError('Expected exactly one source component: ' + exclusion['object'])
        obj = matches[0]
        if len(obj.data.vertices) != exclusion['vertices'] or [m.name for m in obj.data.materials] != exclusion['materials']:
            raise ValueError('Component identity differs; exclusion refused')
        log('candidate component exclusion', obj.name, len(obj.data.vertices), exclusion['reason'])
        bpy.data.objects.remove(obj, do_unlink=True)
    objs = list(bpy.context.scene.objects)
    drops = [d.lower() for d in spec.get('drop_meshes', [])]
    for o in objs:
        if o.type != 'MESH':
            continue
        mats = [sl.material.name.lower() for sl in o.material_slots if sl.material]
        if any(d in o.name.lower() for d in drops) or any(m in drops for m in mats):
            bpy.data.objects.remove(o, do_unlink=True)
    src = next((o for o in bpy.context.scene.objects if o.type == 'ARMATURE'), None)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.data.polygons and o.name != 'Icosphere']
    for o in list(bpy.context.scene.objects):
        if o.type == 'MESH' and o not in meshes:
            bpy.data.objects.remove(o, do_unlink=True)
    if src is not None:
        src.data.pose_position = 'REST'
        if src.animation_data:
            src.animation_data.action = None
        bpy.context.view_layer.update()
        for fx in spec.get('snap', []):
            snap_mesh_to_bones(fx, meshes, src)
        mapping = map_rig_family(src)
        cts = build_cts_from_source(src, mapping)
        for o in meshes:
            retarget_weights(o, src, mapping, cts)
        # drop helper objects (empties, extra rigs)
        for o in list(bpy.context.scene.objects):
            if o.type in ('EMPTY',) or (o.type == 'ARMATURE' and o is not cts):
                bpy.data.objects.remove(o, do_unlink=True)
    else:
        cts = autorig(meshes, spec)
    for o in meshes:
        k = drop_strays(o, cts)
        if k:
            log(base_id, o.name, 'removed stray vertices', k)
        n = fill_unweighted(o, cts)
        if n:
            log(base_id, o.name, 'filled unweighted vertices', n)
    meshes = [o for o in meshes if len(o.data.polygons)]
    if spec.get('add_hands'):
        meshes.append(mitten_hands(cts))
    if spec.get('target_height'):
        scale_to_height(meshes, cts, spec['target_height'])
    Hc = max(max((o.matrix_world @ v.co).z for v in o.data.vertices) for o in meshes)
    for o in meshes:
        regs = regions_by_rules(o, spec.get('region_rules', []), spec.get('region_default', 'top'), Hc)
        hip_z = (cts.matrix_world @ cts.data.bones['pelvis'].head_local).z
        out = []
        for p, r in zip(o.data.polygons, regs):
            if r == 'suit':
                r = 'top' if (o.matrix_world @ p.center).z > hip_z else 'bottom'
            out.append(REGION_ID.get(r, REGION_ID['none']))
        set_face_int(o, 'region', out)
        set_face_int(o, 'part', part_by_bone(o))
    return cts, meshes

def snap_mesh_to_bones(fx, meshes, arm):
    """Sketchfab-converted parts whose bind space is offset. mode 'head': move the whole mesh (and the
    'with' meshes) so its bottom sits just under the Head bone; mode 'hand': move each island so its centroid
    sits 'along' metres past the dominant bone head, along its parent->bone direction."""
    name, mode = fx['mesh'], fx['mode']
    def find(nm):
        return next((m for m in meshes if nm == m.name or nm in [s.material.name for s in m.material_slots if s.material]), None)
    o = find(name)
    if o is None:
        log('snap: mesh not found', name); return
    if mode == 'head':
        b = arm.data.bones[fx['bone']]
        head = arm.matrix_world @ b.head_local
        P = np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices])
        d = Vector((head.x - P[:, 0].mean(), 0.0, head.z - P[:, 2].min() - fx.get('below_head', 0.05)))
        for m in [o] + [find(w) for w in fx.get('with', [])]:
            if m is None:
                continue
            inv = m.matrix_world.inverted().to_3x3()
            for v in m.data.vertices:
                v.co += inv @ d
            m.data.update()
        log('snap head', name, tuple(round(x, 3) for x in d))
        return
    gi = {g.index: g.name for g in o.vertex_groups}
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
    dl = bm.verts.layers.deform.active
    seen = set()
    for v0 in bm.verts:
        if v0.index in seen:
            continue
        isl, st = [], [v0]; seen.add(v0.index)
        while st:
            x = st.pop(); isl.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen:
                    seen.add(y.index); st.append(y)
        votes = {}
        for v in isl:
            for g, w in v[dl].items():
                votes[gi[g]] = votes.get(gi[g], 0) + w
        if not votes:
            continue
        bone = max(votes, key=votes.get)
        bb = arm.data.bones.get(bone)
        if bb is None or bb.parent is None:
            continue
        head = arm.matrix_world @ bb.head_local; ph = arm.matrix_world @ bb.parent.head_local
        c = sum((o.matrix_world @ v.co for v in isl), Vector()) / len(isl)
        d = head + (head - ph).normalized() * fx.get('along', 0.07) - c
        if d.length > fx.get('min_shift', 0.1):
            inv = o.matrix_world.inverted().to_3x3()
            for v in isl:
                v.co += inv @ d
            log('snap island', name, bone, tuple(round(x, 3) for x in d))
    bm.to_mesh(o.data); bm.free(); o.data.update()

ADAPTERS['hero'] = load_hero

# ------------------------------------------------------------------ auto-rig (unrigged heroes)
def _slice(P, z, tol):
    return P[np.abs(P[:, 2] - z) < tol]

def estimate_joints(P):
    """Joint positions for an unrigged standing humanoid (T, A or arms-down pose), facing -Y, feet at z=0."""
    H = float(P[:, 2].max())
    J = {}
    torso = lambda z: _slice(P, z, 0.012 * H)
    def centre(z, xr=0.12):
        s_ = torso(z); s_ = s_[np.abs(s_[:, 0]) < xr * H]
        return Vector((0.0, float(np.median(s_[:, 1])) if len(s_) else 0.0, z))
    for n, f in (('pelvis', 0.52), ('spine_01', 0.58), ('spine_02', 0.655), ('spine_03', 0.735), ('neck_01', 0.835), ('Head', 0.875)):
        J[n] = centre(f * H)
    J['head_top'] = Vector((0, J['Head'].y, H))
    for s, sg in (('l', 1), ('r', -1)):
        def legc(z):
            s_ = torso(z); s_ = s_[(s_[:, 0] * sg > 0.01) & (np.abs(s_[:, 0]) < 0.16 * H)]
            if len(s_) < 5:
                return Vector((sg * 0.05 * H, 0, z))
            return Vector((float(np.median(s_[:, 0])), float(np.median(s_[:, 1])), z))
        J[f'thigh_{s}'] = legc(0.49 * H); J[f'thigh_{s}'].z = 0.50 * H
        J[f'calf_{s}'] = legc(0.285 * H)
        J[f'foot_{s}'] = legc(0.05 * H)
        foot = P[(P[:, 2] < 0.04 * H) & (P[:, 0] * sg > 0)]
        fy = float(foot[:, 1].min()) if len(foot) else J[f'foot_{s}'].y - 0.12
        J[f'ball_{s}'] = Vector((J[f'foot_{s}'].x, J[f'foot_{s}'].y + 0.62 * (fy - J[f'foot_{s}'].y), 0.02 * H))
        J[f'ball_leaf_{s}'] = Vector((J[f'foot_{s}'].x, fy, 0.02 * H))
        # arm: outermost cluster in torso slices between shoulder and hip height
        sh = _slice(P, 0.80 * H, 0.012 * H)
        sh = sh[sh[:, 0] * sg > 0]
        xs = float(np.percentile(np.abs(sh[:, 0]), 99)) if len(sh) else 0.2 * H
        J[f'upperarm_{s}'] = Vector((sg * (xs - 0.035 * H), float(np.median(sh[:, 1])) if len(sh) else 0.0, 0.805 * H))
        J[f'clavicle_{s}'] = Vector((sg * 0.02 * H, J['spine_03'].y - 0.01, 0.79 * H))
        cents = []
        for f in np.linspace(0.74, 0.45, 9):
            sl = _slice(P, f * H, 0.01 * H); sl = sl[sl[:, 0] * sg > 0]
            if len(sl) < 6:
                continue
            ax = np.sort(np.abs(sl[:, 0]))
            gaps = np.diff(ax)
            k = int(np.argmax(gaps))
            if gaps[k] > 0.012 * H and ax[k + 1] > 0.10 * H:
                arm = sl[np.abs(sl[:, 0]) >= ax[k + 1]]
                cents.append(arm.mean(0))
        sp = J[f'upperarm_{s}']
        if len(cents) >= 3:
            C = np.array(cents)
            d = Vector(tuple(C[-1] - np.array(sp))).normalized()
        else:   # T-pose fallback: arm points beyond the shoulder at shoulder height
            far = sh[np.abs(sh[:, 0]) > xs * 0.5]
            d = Vector((sg, 0, 0)) if len(far) == 0 else Vector((sg, 0, 0))
        J[f'lowerarm_{s}'] = sp + d * 0.186 * H
        J[f'hand_{s}'] = J[f'lowerarm_{s}'] + d * 0.146 * H
        J[f'_handdir_{s}'] = d
    return J, H

def autorig(meshes, spec):
    C1 = __import__('cast_common')
    for o in meshes:
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    C1.select_only(meshes)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    P = np.concatenate([np.array([tuple(v.co) for v in o.data.vertices]) for o in meshes])
    zmin = P[:, 2].min(); cx = (P[:, 0].max() + P[:, 0].min()) / 2
    for o in meshes:
        for v in o.data.vertices:
            v.co.z -= zmin; v.co.x -= cx
    P[:, 2] -= zmin; P[:, 0] -= cx
    J, H = estimate_joints(P)
    pos = {}
    nxt = {'pelvis': 'spine_01', 'spine_01': 'spine_02', 'spine_02': 'spine_03', 'spine_03': 'neck_01', 'neck_01': 'Head', 'Head': 'head_top'}
    for a, b in nxt.items():
        pos[a] = (J[a], J[b])
    for s in ('l', 'r'):
        d = J[f'_handdir_{s}']
        pos[f'clavicle_{s}'] = (J[f'clavicle_{s}'], J[f'upperarm_{s}'])
        pos[f'upperarm_{s}'] = (J[f'upperarm_{s}'], J[f'lowerarm_{s}'])
        pos[f'lowerarm_{s}'] = (J[f'lowerarm_{s}'], J[f'hand_{s}'])
        pos[f'hand_{s}'] = (J[f'hand_{s}'], J[f'hand_{s}'] + d * 0.06 * H)
        pos[f'thigh_{s}'] = (J[f'thigh_{s}'], J[f'calf_{s}'])
        pos[f'calf_{s}'] = (J[f'calf_{s}'], J[f'foot_{s}'])
        pos[f'foot_{s}'] = (J[f'foot_{s}'], J[f'ball_{s}'])
        pos[f'ball_{s}'] = (J[f'ball_{s}'], J[f'ball_leaf_{s}'])
    # build a fake source armature from the joints and reuse the CTS builder (fingers are placed by it)
    sa = bpy.data.armatures.new('__src'); so = bpy.data.objects.new('__src', sa)
    bpy.context.scene.collection.objects.link(so); bpy.context.view_layer.objects.active = so
    bpy.ops.object.mode_set(mode='EDIT')
    for n, (h, t) in pos.items():
        eb = sa.edit_bones.new(n); eb.head = h; eb.tail = t if (t - h).length > 1e-3 else h + Vector((0, 0, 0.02))
    bpy.ops.object.mode_set(mode='OBJECT')
    cts = build_cts_from_source(so, {n: n for n in pos})
    bpy.data.objects.remove(so, do_unlink=True)
    # heat weights; fingers/leaf bones excluded so hands stay rigid (mitten/glove sources)
    for b in cts.data.bones:
        b.use_deform = not (b.name == 'root' or 'leaf' in b.name or re.match(r'(thumb|index|middle|ring|pinky)_', b.name))
    # Heat weights on a watertight voxel proxy, then transferred to the real (non-manifold) meshes.
    proxies = []
    for o in meshes:
        c = C1.duplicate(o, '__proxy_' + o.name); c.modifiers.clear(); proxies.append(c)
    C1.select_only(proxies)
    if len(proxies) > 1:
        bpy.ops.object.join()
    px = bpy.context.object
    rm = px.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = 0.012
    with bpy.context.temp_override(object=px, active_object=px):
        bpy.ops.object.modifier_apply(modifier=rm.name)
    C1.decimate_to(px, 24000, symmetry=False)
    px.vertex_groups.clear()
    C1.select_only([px, cts], cts)
    try:
        bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    except Exception as e:
        log('auto weights failed on proxy:', e)
    for o in meshes:
        o.vertex_groups.clear()
        for n in CTS_BONES:
            o.vertex_groups.new(name=n)
        dt = o.modifiers.new('dt', 'DATA_TRANSFER'); dt.object = px
        dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
        dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
        C1.select_only([o])
        with bpy.context.temp_override(object=o, active_object=o):
            bpy.ops.object.modifier_apply(modifier=dt.name)
        o.parent = cts
        o.modifiers.new('arm', 'ARMATURE').object = cts
    bpy.data.objects.remove(px, do_unlink=True)
    for b in cts.data.bones:
        b.use_deform = 'leaf' not in b.name and b.name != 'root'
    for o in meshes:
        for n in CTS_BONES:
            if n not in o.vertex_groups:
                o.vertex_groups.new(name=n)
    return cts

def mitten_hands(arm):
    """Simple gloved hands (box palm + thumb) for sources whose glove meshes cannot be recovered."""
    import v2_geo as G
    bm = bmesh.new()
    for s in ('l', 'r'):
        h = arm.matrix_world @ arm.data.bones[f'hand_{s}'].head_local
        f = arm.matrix_world @ arm.data.bones[f'lowerarm_{s}'].head_local
        d = (h - f).normalized()
        up = Vector((0, 0, 1)); side = d.cross(up).normalized(); up = side.cross(d).normalized()
        rot = Matrix((d, side, up)).transposed()
        palm = G.bm_box(bm, h + d * 0.05, (0.10, 0.085, 0.035), rot=rot)
        th = G.bm_box(bm, h + d * 0.035 + side * (0.05 if s == 'l' else -0.05) * -1 - up * 0.01, (0.055, 0.025, 0.025), rot=rot)
        bmesh.ops.bevel(bm, geom=[e for e in bm.edges if all(v in palm for v in e.verts)], offset=0.008, segments=1, affect='EDGES')
    me = bpy.data.meshes.new('mitten_hands'); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new('mitten_hands', me); bpy.context.scene.collection.objects.link(o)
    for n in CTS_BONES:
        o.vertex_groups.new(name=n)
    for v in me.vertices:
        p = v.co
        side = 'l' if p.x > 0 else 'r'
        o.vertex_groups[f'hand_{side}'].add([v.index], 1.0, 'REPLACE')
    o.parent = arm
    o.modifiers.new('arm', 'ARMATURE').object = arm
    mat = bpy.data.materials.new('gloves_synth'); mat.diffuse_color = (0.03, 0.03, 0.035, 1)
    me.materials.append(mat)
    return o
