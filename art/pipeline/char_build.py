"""Build a CT Life master character (.blend) from MPFB (MakeHuman, CC0 assets).

Usage (repo root):
  BLENDER_USER_RESOURCES=art/raw/blender_user \
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P art/pipeline/char_build.py -- male|female

Output: art/raw/work/char_<sex>_master.blend containing
  CTL_Rig_<sex>   the shared CT Life humanoid skeleton (30 bones, T-pose rest, mitten hands, jaw, eyes)
  CTL_Body        body proxy (with face attribute 'ctl_region' + 'cover.<part>' vertex groups)
  CTL_Eyes, CTL_Brows, CTL_Lashes   (brows/lashes are only used to paint the skin texture)
  part.<key>      every MPFB garment / hair from ctl_config, fitted + skinned to the same skeleton
"""
import bpy, bmesh, sys, os, json, math, addon_utils
from mathutils import Vector, Matrix, Quaternion

sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C

SEX = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else 'male'
CFG = C.BODIES[SEX]

bpy.ops.wm.read_factory_settings(use_empty=True)
addon_utils.enable('bl_ext.user_default.mpfb', default_set=True)
from bl_ext.user_default.mpfb.services.humanservice import HumanService
from bl_ext.user_default.mpfb.services.objectservice import ObjectService
from bl_ext.user_default.mpfb.entities.clothes.mhclo import Mhclo


def log(*a):
    print('[char_build]', *a, flush=True)


# ------------------------------------------------------------------ 1. MPFB human
parts = C.MPFB_PARTS[SEX]
info = {
    "name": "CTL",
    "phenotype": CFG['phenotype'],
    "rig": "game_engine",
    "eyes": "low-poly/low-poly.mhclo",
    "eyebrows": CFG['eyebrows'],
    "eyelashes": CFG['eyelashes'],
    "tongue": "", "teeth": "", "hair": "",
    "proxy": CFG['proxy'],
    "targets": C.STYLE_TARGETS + CFG['extra_targets'],
    "clothes": [p[0] for p in parts.values()],
    "skin_mhmat": CFG['skin'], "skin_material_type": "MAKESKIN",
    "eyes_material_type": "MAKESKIN",
    "skin_material_settings": {}, "eyes_material_settings": {}, "expressions": [],
}
settings = HumanService.get_default_deserialization_settings()
settings["subdiv_levels"] = 0
basemesh = HumanService.deserialize_from_dict(info, settings)
rig = basemesh.parent
log('human created', basemesh.name, rig.name)

hair_objs = {}
for key, path in C.MPFB_HAIRS[SEX].items():
    full = os.path.join(C.MPFB_DATA, 'hair', path)
    o = HumanService.add_mhclo_asset(full, basemesh, asset_type="Hair", subdiv_levels=0, material_type="MAKESKIN")
    hair_objs['hair_' + key] = o
log('hairs', list(hair_objs))

# identify objects
def asset_src(o):
    return o.get('MPFB_GEN_asset_source') or o.get('mpfb_asset_source') or ''

objs = [o for o in bpy.data.objects if o.type == 'MESH' and o is not basemesh]
for o in objs:
    log('obj', o.name, ObjectService.get_object_type(o), dict((k, o[k]) for k in o.keys() if 'asset' in k.lower()))

def by_type(t):
    return [o for o in objs if ObjectService.get_object_type(o) == t]

proxy = by_type('Proxymeshes')[0]
eyes = by_type('Eyes')[0]
brows = by_type('Eyebrows')[0]
lashes = by_type('Eyelashes')[0]
part_objs = {}
for key, (path, slot, tile) in parts.items():
    stem = os.path.basename(path).replace('.mhclo', '')
    cand = [o for o in by_type('Clothes') if o.name.endswith('.' + stem) or o.name.endswith(stem)]
    if not cand:
        raise RuntimeError('missing part ' + key + ' ' + stem)
    part_objs[key] = cand[0]
part_objs.update(hair_objs)

# ------------------------------------------------------------------ 2. facial joints from the basemesh
for m in basemesh.modifiers:
    if m.type == 'MASK':
        m.show_viewport = False
dg = bpy.context.evaluated_depsgraph_get()
bm_eval = basemesh.evaluated_get(dg)
me_eval = bm_eval.to_mesh()
mw = basemesh.matrix_world
def group_center(name):
    gi = basemesh.vertex_groups[name].index
    pts = [mw @ me_eval.vertices[v.index].co for v in basemesh.data.vertices if any(g.group == gi and g.weight > 0.5 for g in v.groups)]
    return sum(pts, Vector()) / len(pts)
J = {
    'jaw_head': group_center('joint-mouth'),
    'jaw_tail': mw @ me_eval.vertices[991].co,
    'eye_l': group_center('joint-l-eye'),
    'eye_r': group_center('joint-r-eye'),
    'eye_l_t': group_center('joint-l-eye-target'),
    'eye_r_t': group_center('joint-r-eye-target'),
}
bm_eval.to_mesh_clear()
log('joints', {k: tuple(round(x, 3) for x in v) for k, v in J.items()})

# jaw weights (default MPFB rig) -> basemesh -> proxy (weighted barycentric through the .proxy mapping)
wdef = json.load(open(os.path.join(C.MPFB_SYS, 'rigs', 'standard', 'weights.default.json')))['weights']
jaw_w = {int(i): float(w) for i, w in wdef['jaw']}
px = Mhclo()
px.load(os.path.join(C.MPFB_DATA, 'proxymeshes', CFG['proxy']))
pj = proxy.vertex_groups.new(name='jaw')
for vi, d in px.verts.items():
    w = sum(jaw_w.get(v, 0.0) * ww for v, ww in zip(d['verts'], d['weights']))
    if w > 0.01 and vi < len(proxy.data.vertices):
        pj.add([vi], min(1.0, w), 'REPLACE')

# ------------------------------------------------------------------ 3. drop basemesh, masks; keep cover groups
for o in [proxy]:
    for m in list(o.modifiers):
        if m.type == 'MASK':
            o.modifiers.remove(m)
for vg in proxy.vertex_groups:
    if vg.name.startswith('Delete.'):
        vg.name = 'cover.' + vg.name[len('Delete.'):]
bpy.data.objects.remove(basemesh, do_unlink=True)
for o in objs:
    for m in list(o.modifiers):
        if m.type not in ('ARMATURE',):
            o.modifiers.remove(m)
    if o.data.shape_keys:
        o.shape_key_clear()

# ------------------------------------------------------------------ 4. skeleton surgery
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = rig.data.edit_bones
for s in 'lr':
    # mitten: index_01 spans to the middle fingertip, thumb_01 to the thumb tip
    tip = eb[f'middle_03_{s}'].tail.copy()
    eb[f'index_01_{s}'].head = (eb[f'index_01_{s}'].head + eb[f'middle_01_{s}'].head + eb[f'ring_01_{s}'].head) / 3
    eb[f'index_01_{s}'].tail = tip
    eb[f'thumb_01_{s}'].tail = eb[f'thumb_03_{s}'].tail.copy()
for name in list(eb.keys()):
    if name in C.FINGER_MERGE and C.FINGER_MERGE[name] != name:
        eb.remove(eb[name])
head_b = eb['head']
def add_bone(name, h, t, parent):
    b = eb.new(name)
    b.head, b.tail = h, t
    b.parent = parent
    b.use_deform = True
    return b
add_bone('jaw', J['jaw_head'], J['jaw_tail'], head_b)
for s in 'lr':
    h = J['eye_' + s]
    t = h + (J['eye_' + s + '_t'] - h).normalized() * 0.025
    add_bone('eye_' + s, h, t, head_b)
bpy.ops.object.mode_set(mode='OBJECT')
for old, new in C.MPFB_RENAME.items():
    if old in rig.data.bones:
        rig.data.bones[old].name = new  # renames vertex groups on children automatically
rig.name = rig.data.name = f'CTL_Rig_{SEX}'

# merge finger weights on every mesh
meshes = [proxy, eyes, brows, lashes] + list(part_objs.values())
for o in meshes:
    vgs = o.vertex_groups
    for src, dst in C.FINGER_MERGE.items():
        if src == dst or src not in vgs:
            continue
        si = vgs[src].index
        dvg = vgs.get(dst) or vgs.new(name=dst)
        for v in o.data.vertices:
            for g in v.groups:
                if g.group == si and g.weight > 0:
                    cur = 0.0
                    for g2 in v.groups:
                        if g2.group == dvg.index:
                            cur = g2.weight
                    dvg.add([v.index], min(1.0, cur + g.weight), 'REPLACE')
        vgs.remove(vgs[src])

# jaw takes its share from Head on the body
hv = proxy.vertex_groups['Head']
jv = proxy.vertex_groups['jaw']
for v in proxy.data.vertices:
    gw = {g.group: g.weight for g in v.groups}
    if jv.index in gw and hv.index in gw:
        j = gw[jv.index]
        hv.add([v.index], max(0.0, gw[hv.index] * (1 - j)), 'REPLACE')

# eyes: one mesh holding both eyes -> eye_l (+X) / eye_r (-X)
for vg in list(eyes.vertex_groups):
    eyes.vertex_groups.remove(vg)
gl, gr = eyes.vertex_groups.new(name='eye_l'), eyes.vertex_groups.new(name='eye_r')
for v in eyes.data.vertices:
    (gl if (eyes.matrix_world @ v.co).x > 0 else gr).add([v.index], 1.0, 'REPLACE')

# ------------------------------------------------------------------ 5. T-pose
bpy.ops.object.mode_set(mode='POSE')
def align_bone(pb, direction):
    cur = (pb.tail - pb.head)
    q = cur.normalized().rotation_difference(direction.normalized())
    M = Matrix.Translation(pb.head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pb.head)
    pb.matrix = M @ pb.matrix
    bpy.context.view_layer.update()
for s, sign in (('l', 1), ('r', -1)):
    d = Vector((sign, 0, 0))
    for bn in (f'upperarm_{s}', f'lowerarm_{s}', f'hand_{s}', f'index_01_{s}'):
        align_bone(rig.pose.bones[bn], d)
bpy.ops.object.mode_set(mode='OBJECT')
for o in meshes:
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    for m in o.modifiers:
        if m.type == 'ARMATURE':
            bpy.ops.object.modifier_apply(modifier=m.name)
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')

# ------------------------------------------------------------------ 6. weights: deform bones only, max 4, normalised
deform = set(C.BONE_NAMES)
def clean_weights(o, max_inf=4):
    keep_names = deform | {vg.name for vg in o.vertex_groups if vg.name.startswith('cover.') }
    for vg in list(o.vertex_groups):
        if vg.name not in keep_names:
            o.vertex_groups.remove(vg)
    bone_idx = {vg.index for vg in o.vertex_groups if vg.name in deform}
    for v in o.data.vertices:
        ws = sorted([(g.weight, g.group) for g in v.groups if g.group in bone_idx and g.weight > 0.0], reverse=True)
        keep = ws[:max_inf]
        tot = sum(w for w, _ in keep) or 1.0
        for w, gi in ws[max_inf:]:
            o.vertex_groups[gi].remove([v.index])
        for w, gi in keep:
            o.vertex_groups[gi].add([v.index], w / tot, 'REPLACE')
        if not keep:
            # orphan vertex: bind to nearest sensible bone later (Head for hair, pelvis otherwise)
            pass
for o in meshes:
    clean_weights(o)
    o.parent = rig
    o.matrix_parent_inverse = Matrix.Identity(4)
    mod = next((m for m in o.modifiers if m.type == 'ARMATURE'), None) or o.modifiers.new('Armature', 'ARMATURE')
    mod.object = rig

# hairs that came without weights -> Head
for key, o in part_objs.items():
    if key.startswith('hair_'):
        for v in o.data.vertices:
            if not any(o.vertex_groups[g.group].name in deform for g in v.groups):
                (o.vertex_groups.get('Head') or o.vertex_groups.new(name='Head')).add([v.index], 1.0, 'REPLACE')

# ------------------------------------------------------------------ 7. body regions (face attribute)
REGION_OF_BONE = {
    'Head': 'head', 'jaw': 'head', 'eye_l': 'head', 'eye_r': 'head', 'neck_01': 'neck',
    'spine_03': 'chest', 'clavicle_l': 'chest', 'clavicle_r': 'chest',
    'spine_02': 'belly', 'spine_01': 'belly', 'pelvis': 'hips', 'root': 'hips',
}
for s in 'lr':
    REGION_OF_BONE.update({f'upperarm_{s}': f'upperarm_{s}', f'lowerarm_{s}': f'forearm_{s}', f'hand_{s}': f'hand_{s}',
                           f'thumb_01_{s}': f'hand_{s}', f'index_01_{s}': f'hand_{s}', f'thigh_{s}': f'thigh_{s}',
                           f'calf_{s}': f'calf_{s}', f'foot_{s}': f'foot_{s}', f'ball_{s}': f'foot_{s}'})
REGIONS = ['head', 'neck', 'chest', 'belly', 'hips']
for s in 'lr':
    REGIONS += [f'shoulder_{s}', f'upperarm_{s}', f'forearm_{s}', f'hand_{s}', f'thighup_{s}', f'thigh_{s}', f'calf_{s}', f'foot_{s}']
bones = rig.data.bones
def region_for_point(p, weights):
    bname = max(weights.items(), key=lambda kv: kv[1])[0]
    r = REGION_OF_BONE.get(bname, 'hips')
    side = bname[-1]
    if bname.startswith('upperarm_'):
        b = bones[bname]
        t = (p - b.head_local).dot((b.tail_local - b.head_local).normalized()) / b.length
        return f'shoulder_{side}' if t < 0.45 else f'upperarm_{side}'
    if bname.startswith('thigh_'):
        b = bones[bname]
        t = (p - b.head_local).dot((b.tail_local - b.head_local).normalized()) / b.length
        return f'thighup_{side}' if t < 0.45 else f'thigh_{side}'
    return r
me = proxy.data
names = {vg.index: vg.name for vg in proxy.vertex_groups}
attr = me.attributes.new('ctl_region', 'INT', 'FACE')
for poly in me.polygons:
    acc = {}
    for vi in poly.vertices:
        for g in me.vertices[vi].groups:
            n = names[g.group]
            if n in deform:
                acc[n] = acc.get(n, 0) + g.weight
    attr.data[poly.index].value = REGIONS.index(region_for_point(poly.center, acc)) if acc else 0
proxy['ctl_regions'] = json.dumps(REGIONS)

# ------------------------------------------------------------------ 8. names, metadata, save
proxy.name = 'CTL_Body'
eyes.name = 'CTL_Eyes'
brows.name = 'CTL_Brows'
lashes.name = 'CTL_Lashes'
for key, o in part_objs.items():
    o.name = 'part.' + key
    if key.startswith('hair_'):
        o['ctl_slot'] = 'hair'
        o['ctl_source'] = 'mpfb:hair/' + C.MPFB_HAIRS[SEX][key[5:]]
        o['ctl_tile'] = 256
    else:
        path, slot, tile = parts[key]
        o['ctl_slot'] = slot
        o['ctl_source'] = 'mpfb:clothes/' + path
        o['ctl_tile'] = tile
for o in meshes:
    o.data.name = o.name
rig['ctl_sex'] = SEX
os.makedirs(C.WORK, exist_ok=True)
out = os.path.join(C.WORK, f'char_{SEX}_master.blend')
bpy.ops.wm.save_as_mainfile(filepath=out)
log('saved', out, 'bones', len(rig.data.bones), [b.name for b in rig.data.bones])
for o in meshes:
    log(' mesh', o.name, 'tris', sum(len(p.vertices) - 2 for p in o.data.polygons), 'groups', len(o.vertex_groups))
