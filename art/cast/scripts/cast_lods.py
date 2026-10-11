"""Stage 3: build hand-budgeted tiers (hero/lod0/lod1/lod2) for every part of a family.

usage: Blender -b ... -P cast_lods.py -- <fam> [part ...]
Reads EVID/families/<fam>/{base.blend, wardrobe.blend}; writes EVID/families/<fam>/lods.blend
Each output object is named '<fam>|<part>|<tier>' and keeps SlotUV, sub/region attributes,
custom normals transferred from the full-resolution part and <= MAX_INFL weights.
"""
import bpy, bmesh, sys, json
import numpy as np
from pathlib import Path
from mathutils import Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cast_common import *

args = sys.argv[sys.argv.index('--') + 1:]
fam = args[0]
only = set(args[1:])
d = fam_dir(fam)
meta = json.loads((d / 'meta.json').read_text())
BONES = meta['bones']

# Budgets: (hero, lod0, lod1, lod2) per part kind. Keys are part-name prefixes.
ALLOC = {
    'head':   (3600, 1700, 800, 320),
    'hair':   (1500, 700, 330, 130),
    'skin':   (2000, 900, 420, 150),
    'legs':   (600, 300, 140, 60),
    'top':    (1700, 800, 400, 160),
    'outer':  (900, 420, 200, 80),
    'bottom': (1300, 600, 300, 120),
    'shoes':  (700, 300, 140, 60),
    'beard':  (500, 220, 100, 40),
    'scarf':  (600, 300, 150, 60),
    'acc':    (600, 300, 150, 60),
}
SYMMETRIC = {'head', 'skin', 'legs', 'top', 'bottom', 'shoes', 'outer', 'beard'}

def kind_of(part):
    for k in ALLOC:
        if part == k or part.startswith(k + '_'):
            return k
    return 'acc'

bpy.ops.wm.open_mainfile(filepath=str(d / 'base.blend'), load_ui=False, use_scripts=False)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rig.data.pose_position = 'REST'
wpath = d / 'wardrobe.blend'
if wpath.exists():
    with bpy.data.libraries.load(str(wpath), link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith(f'{fam}_w_')]
    for o in dst.objects:
        if o is None:
            continue
        bpy.context.scene.collection.objects.link(o)
        o.parent = rig
        for m in o.modifiers:
            if m.type == 'ARMATURE':
                m.object = rig

sources = {}
for o in list(bpy.data.objects):
    if o.type != 'MESH':
        continue
    for pre in (f'{fam}_base_', f'{fam}_w_'):
        if o.name.startswith(pre):
            sources[o.name[len(pre):]] = o
if only:
    sources = {k: v for k, v in sources.items() if k in only}
log('parts', sorted(sources))

eyeL = Vector(meta['bone_heads']['Eye.L'])
eyeR = Vector(meta['bone_heads']['Eye.R'])

def add_protect(o, part):
    """Vertex group 'protect' (1 = keep detail) for the face; used with invert in decimate."""
    g = o.vertex_groups.get('protect') or o.vertex_groups.new(name='protect')
    co = mesh_arrays(o)
    w = np.zeros(len(co), dtype=np.float32)
    if part == 'head':
        ez = (eyeL.z + eyeR.z) / 2
        front = co[:, 1].min()
        for e in (eyeL, eyeR):
            dist = np.linalg.norm(co - np.array(e), axis=1)
            w = np.maximum(w, np.clip(1 - (dist - 0.022) / 0.02, 0, 1))
        mouth = (np.abs(co[:, 0]) < 0.05) & (co[:, 2] > ez - 0.11) & (co[:, 2] < ez + 0.01) & (co[:, 1] < front + 0.045)
        w = np.maximum(w, mouth.astype(np.float32))
    elif part == 'skin':
        # keep fingers separable at the higher tiers
        W = weights_matrix(o, BONES)
        fing = [i for i, n in enumerate(BONES) if n.startswith(('Thumb', 'Index', 'Middle', 'Ring', 'Pinky'))]
        w = np.clip(W[:, fing].sum(axis=1) * 1.5, 0, 1) * 0.6
    for i, x in enumerate(w.tolist()):
        if x > 0:
            g.add([i], x, 'REPLACE')
    return 'protect'

def transfer_normals(dst, src):
    m = dst.modifiers.new('nrm', 'DATA_TRANSFER')
    m.object = src
    m.use_loop_data = True
    m.data_types_loops = {'CUSTOM_NORMAL'}
    m.loop_mapping = 'POLYINTERP_NEAREST'
    select_only([dst])
    with bpy.context.temp_override(object=dst, active_object=dst):
        bpy.ops.object.modifier_move_to_index(modifier=m.name, index=0)
        bpy.ops.object.modifier_apply(modifier=m.name)

def hair_hull(o, src, target, voxel):
    """Far tiers of strand-clump hair: voxel hull -> smooth -> decimate, then transfer slot UVs and
    weights from the full-resolution hair. Avoids the spiky shards plain collapse produces."""
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    rm = o.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = voxel
    select_only([o])
    with bpy.context.temp_override(object=o, active_object=o):
        bpy.ops.object.modifier_apply(modifier=rm.name)
    bm = bmesh.new(); bm.from_mesh(o.data)
    for _ in range(2):
        bmesh.ops.smooth_vert(bm, verts=list(bm.verts), factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(o.data); bm.free()
    # keep the hull on the authored outer hair surface (smoothing shrinks it into the scalp)
    sw = o.modifiers.new('sw', 'SHRINKWRAP'); sw.target = src; sw.wrap_method = 'NEAREST_SURFACEPOINT'
    sw.wrap_mode = 'OUTSIDE_SURFACE'; sw.offset = 0.0025
    with bpy.context.temp_override(object=o, active_object=o):
        bpy.ops.object.modifier_apply(modifier=sw.name)
    decimate_to(o, target, symmetry=False)
    o.vertex_groups.clear()
    for g in src.vertex_groups:
        o.vertex_groups.new(name=g.name)
    for l in list(o.data.uv_layers):
        o.data.uv_layers.remove(l)
    o.data.uv_layers.new(name='SlotUV')
    dt = o.modifiers.new('dt', 'DATA_TRANSFER'); dt.object = src
    dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
    dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
    dt.use_loop_data = True; dt.data_types_loops = {'UV', 'CUSTOM_NORMAL'}; dt.loop_mapping = 'POLYINTERP_NEAREST'
    dt.layers_uv_select_src = 'SlotUV'; dt.layers_uv_select_dst = 'SlotUV'
    with bpy.context.temp_override(object=o, active_object=o):
        bpy.ops.object.modifier_apply(modifier=dt.name)
    # keep UVs inside the hair slot (any texel there is hair-coloured after dilation)
    x, y, w, h = slot_rect('HAIR')
    arr = np.empty(len(o.data.loops) * 2, dtype=np.float32)
    o.data.uv_layers['SlotUV'].data.foreach_get('uv', arr)
    arr = arr.reshape(-1, 2)
    arr[:, 0] = np.clip(arr[:, 0], x + 0.004, x + w - 0.004); arr[:, 1] = np.clip(arr[:, 1], y + 0.004, y + h - 0.004)
    o.data.uv_layers['SlotUV'].data.foreach_set('uv', arr.ravel())
    face_attr(o, 'sub', [SUB_IDS['hair']] * len(o.data.polygons))
    face_attr(o, 'region', default=0)
    o.modifiers.new('Game skin', 'ARMATURE').object = rig
    for p in o.data.polygons:
        p.use_smooth = True
    return tri_count(o)

def _seg_dist(p, segs):
    best = None
    for h, t in segs:
        ab = t - h
        u = max(0.0, min(1.0, (p - h).dot(ab) / max(ab.length_squared, 1e-9)))
        c = h + ab * u
        d = (p - c).length
        if best is None or d < best[0]:
            best = (d, c)
    return best[1]

def fix_orientation(o, part):
    """Generated tubes (hems, boot shafts, skin legs) and hat shells can be wound inward; Unity culls
    back faces, so orient them outward from the nearest bone axis (hats: from the head axis, flat
    faces up). Double-sided parts are left alone."""
    subs = get_face_attr(o, 'sub')
    BHd = {k: Vector(v) for k, v in meta['bone_heads'].items()}
    BTd = {k: Vector(v) for k, v in meta['bone_tails'].items()}
    def segs(prefixes):
        return [(BHd[n], BTd[n]) for n in BHd if n.split('.')[0] in prefixes]
    leg_segs = segs(('Thigh', 'Calf', 'Foot'))
    hem_segs = segs(('Hips', 'Spine', 'Thigh'))
    head = BHd['Head']
    targets = {SUB_IDS['hem']: hem_segs, SUB_IDS['shaft']: leg_segs, SUB_IDS['legs']: leg_segs}
    hat = part.startswith('hat_') and part != 'hat_gele'
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    flip = []
    for f in bm.faces:
        sid = subs[f.index]
        c = f.calc_center_median(); n = f.normal
        if sid in targets:
            r = c - _seg_dist(c, targets[sid])
            if n.dot(r) < 0:
                flip.append(f)
        elif hat:
            if abs(n.z) > 0.7:
                if n.z < 0 and c.z > head.z:
                    flip.append(f)
            else:
                r = Vector((c.x - head.x, c.y - head.y, 0))
                if n.dot(r) < 0:
                    flip.append(f)
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip, flip_multires=False)
        bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    return len(flip)

report = {}
out_objs = []
for part, src in sorted(sources.items()):
    k = kind_of(part)
    budgets = dict(zip(TIERS, ALLOC[k]))
    override = src.get('budget')
    if override:
        budgets = dict(zip(TIERS, list(override)))
    for p in src.data.polygons:
        p.use_smooth = True
    if part == 'legs':
        kz = meta['bone_heads']['Calf.L'][2] + 0.16
        keep_faces(src, lambda f: f.calc_center_median().z < kz)
    flipped = fix_orientation(src, part)
    if part.startswith('bottom'):
        # waistband under hanging tops: region 'waist' (hidden whenever a top/outer layer is worn)
        hem_z = meta['parts']['top']['bbox'][0][2] + 0.02
        reg = get_face_attr(src, 'region')
        for p in src.data.polygons:
            if p.center.z > hem_z:
                reg[p.index] = REGION['waist']
            elif p.center.z > hem_z - 0.105:
                reg[p.index] = REGION['hip']   # hidden under hem-extended tops (jacket/coat/kaftan)
        face_attr(src, 'region', reg)
    report[part] = {'flipped_faces': flipped} if flipped else {}
    prev = None
    for tier in TIERS:
        o = duplicate(src, f'{fam}|{part}|{tier}')
        o.parent = rig
        for m in o.modifiers:
            if m.type == 'ARMATURE':
                m.object = rig
        grp = None
        if k in ('head', 'skin'):
            grp = add_protect(o, k)
        n0 = tri_count(o)
        target = budgets[tier]
        if part == 'hair' and tier in ('lod1', 'lod2'):
            n = hair_hull(o, src, target, 0.009 if tier == 'lod1' else 0.014)
        elif n0 > target:
            # decimate from the previous tier when possible (keeps silhouettes nested)
            mod_factor = {'hero': 3.0, 'lod0': 2.0, 'lod1': 1.0, 'lod2': 0.5}[tier]
            n = decimate_to(o, target, protect_group=grp, protect_factor=mod_factor, symmetry=k in SYMMETRIC)
        else:
            n = n0
        if 'protect' in o.vertex_groups:
            o.vertex_groups.remove(o.vertex_groups['protect'])
        transfer_normals(o, src)
        limit_weights(o, BONES, MAX_INFL[tier])
        for lname in [l.name for l in o.data.uv_layers if l.name != 'SlotUV']:
            o.data.uv_layers.remove(o.data.uv_layers[lname])
        o.data.uv_layers['SlotUV'].active_render = True
        report[part][tier] = {'triangles': tri_count(o), 'vertices': len(o.data.vertices), 'max_influences': max_influences(o)}
        out_objs.append(o)
    log(part, report[part])
    bpy.data.objects.remove(src, do_unlink=True)

# Garment layers over the trousers: push top/outer vertices in the hip band outside the bottom of
# the same tier (Rain's tank is partly tucked into her jeans; decimation also creates poke-through).
from mathutils.bvhtree import BVHTree
MARGIN = {'hero': 0.006, 'lod0': 0.008, 'lod1': 0.010, 'lod2': 0.014}
hip_z = meta['bone_heads']['Hips'][2]
knee_z = meta['bone_heads']['Calf.L'][2]
for tier in TIERS:
    b = bpy.data.objects.get(f'{fam}|bottom|{tier}')
    if b is None:
        continue
    bm = bmesh.new(); bm.from_mesh(b.data); bvh = BVHTree.FromBMesh(bm); bm.free()
    for o in out_objs:
        pn, pt = o.name.split('|')[1], o.name.split('|')[2]
        if pt != tier or not (pn.startswith('top_') or pn.startswith('outer_')):
            continue
        moved = 0
        for v in o.data.vertices:
            if not (knee_z - 0.05 < v.co.z < hip_z + 0.28):
                continue
            loc, nrm, idx, dist = bvh.find_nearest(v.co)
            if loc is None or dist > 0.08:
                continue
            h = (v.co - loc).dot(nrm)
            if h < MARGIN[tier]:
                v.co = v.co + nrm * (MARGIN[tier] - h)
                moved += 1
        o.data.update()
        if moved:
            report.setdefault(pn, {}).setdefault(tier, {})['pushed_out_of_bottom'] = moved

for o in list(bpy.data.objects):
    if o.type == 'MESH' and o not in out_objs:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.outliner.orphans_purge(do_recursive=True)
if not only:
    bpy.ops.wm.save_as_mainfile(filepath=str(d / 'lods.blend'), compress=True)
    (d / 'lods_report.json').write_text(json.dumps(report, indent=1))
else:
    # merge the rebuilt parts into the existing lods.blend (other parts untouched)
    bpy.ops.wm.save_as_mainfile(filepath=str(d / '__lods_new.blend'), compress=True)
    names = [o.name for o in out_objs]
    bpy.ops.wm.open_mainfile(filepath=str(d / 'lods.blend'), load_ui=False, use_scripts=False)
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and '|' in o.name and o.name.split('|')[1] in only:
            bpy.data.objects.remove(o, do_unlink=True)
    rig2 = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    with bpy.data.libraries.load(str(d / '__lods_new.blend'), link=False) as (src_, dst_):
        dst_.objects = [n for n in src_.objects if n in names]
    for o in dst_.objects:
        if o:
            bpy.context.scene.collection.objects.link(o)
            o.parent = rig2
            for m in o.modifiers:
                if m.type == 'ARMATURE':
                    m.object = rig2
    for o in list(bpy.data.objects):
        if o.type == 'ARMATURE' and o is not rig2:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.outliner.orphans_purge(do_recursive=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(d / 'lods.blend'), compress=True)
    (d / '__lods_new.blend').unlink()
    rp = d / 'lods_report.json'
    old = json.loads(rp.read_text()) if rp.exists() else {}
    old.update(report)
    rp.write_text(json.dumps(old, indent=1))
log('LODS_DONE', fam, len(out_objs))
