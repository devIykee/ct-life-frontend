"""Stage 1: split a compiled base character (Snow or Rain) into wardrobe-ready parts, re-pack every
part into the fixed cast atlas slot layout and re-sample the original baked maps into it.

usage: Blender -b --factory-startup --disable-autoexec -P cast_extract.py -- <fam>
Outputs (outside the repo): EVID/families/<fam>/{base.blend, base_*.png, *.npy, meta.json}
"""
import bpy, bmesh, sys, json, math
import numpy as np
from pathlib import Path
from mathutils import Vector, kdtree
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cast_common import *

fam = sys.argv[sys.argv.index('--') + 1]
cfg = FAMILIES[fam]
out = fam_dir(fam)
RES = 2048
bpy.ops.wm.open_mainfile(filepath=str(cfg['blend']), load_ui=False, use_scripts=False)
scene = bpy.context.scene
src = bpy.data.objects[cfg['mesh']]
rig = bpy.data.objects[cfg['rig']]
rig.data.pose_position = 'REST'
BONES = [b.name for b in rig.data.bones]
me = src.data
pid = np.zeros(len(me.vertices), dtype=np.int32)
me.attributes['source_part_id'].data.foreach_get('value', pid)
face_pid = np.array([pid[p.vertices[0]] for p in me.polygons], dtype=np.int32)

# ------------------------------------------------------------------ split into parts
parts = {}
for part in sorted({p for p, _ in cfg['parts'].values() if p}):
    ids = {i for i, (p, _) in cfg['parts'].items() if p == part}
    o = duplicate(src, f'{fam}_base_{part}')
    fp = face_pid.copy()
    keep = set(np.nonzero(np.isin(fp, list(ids)))[0].tolist())
    keep_faces(o, lambda f: f.index in keep)
    # sub id per face
    vp = np.zeros(len(o.data.vertices), dtype=np.int32)
    o.data.attributes['source_part_id'].data.foreach_get('value', vp)
    subs = [SUB_IDS[cfg['parts'][int(vp[p.vertices[0]])][1]] for p in o.data.polygons]
    face_attr(o, 'sub', subs)
    face_attr(o, 'region', default=0)
    parts[part] = o
    log('part', part, tri_count(o))
bpy.data.objects.remove(src, do_unlink=True)

# double-walled garments: drop inner walls (they poke through once the outer wall is reduced)
from mathutils.bvhtree import BVHTree
def remove_inner_walls(o, reach=0.015):
    """Pairs of opposite-facing faces closer than `reach`: drop the one nearer the skeleton."""
    segs = [(rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones
            if b.name.split('.')[0] in ('Hips', 'Spine', 'Chest', 'Neck', 'Shoulder', 'UpperArm', 'Forearm', 'Thigh', 'Calf')]
    H = np.array([s[0] for s in segs]); Tt = np.array([s[1] for s in segs])
    def bone_dist(p):
        p = np.array(p)
        ab = Tt - H
        t = np.clip(((p - H) * ab).sum(1) / np.maximum((ab * ab).sum(1), 1e-9), 0, 1)
        return np.linalg.norm(H + ab * t[:, None] - p, axis=1).min()
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    bvh = BVHTree.FromBMesh(bm)
    centers = [f.calc_center_median() for f in bm.faces]
    inner = set()
    for f in bm.faces:
        c = centers[f.index]; n = f.normal
        loc, nrm, idx, dist = bvh.ray_cast(c - n * 0.0004, -n, reach)
        if idx is None or idx == f.index or nrm.dot(n) > -0.5:
            continue
        inner.add(idx if bone_dist(centers[idx]) < bone_dist(c) else f.index)
    bm.free()
    keep_faces(o, lambda f: f.index not in inner)
    return len(inner)
# Rain's top was built with Solidify: polygons [n, 2n) are the hidden inner shell (see
# character-candidates/rain-adult-production/top-topology.json and derive_rain_world_lods.py).
if fam == 'f':
    n_top = json.loads((CAND / 'rain-adult-production/top-topology.json').read_text())['source_base_polygons']
    assert len(parts['top'].data.polygons) > 2 * n_top
    keep_faces(parts['top'], lambda f: not (n_top <= f.index < 2 * n_top))
    log('rain top inner shell removed; tris now', tri_count(parts['top']))

# eyes: drop rear-facing eyeball faces (never visible behind the lids)
head = parts['head']
subs = get_face_attr(head, 'sub')
rear = {p.index for p in head.data.polygons if subs[p.index] == SUB_IDS['eyes'] and p.normal.y > 0.3}
keep_faces(head, lambda f: f.index not in rear)
log('eyes rear faces removed', len(rear), 'head tris', tri_count(head))

# skin regions by dominant bone
def dominant_region(name):
    if name.startswith(('Hand', 'Thumb', 'Index', 'Middle', 'Ring', 'Pinky')):
        return REGION['hand']
    if name.startswith('Forearm'):
        return REGION['forearm']
    if name.startswith(('UpperArm', 'Shoulder')):
        return REGION['upperarm']
    if name.startswith(('Calf', 'Foot', 'Toes', 'Thigh')):
        return REGION['ankle']
    return REGION['torso']
skin = parts['skin']
W = weights_matrix(skin, BONES)
vdom = W.argmax(axis=1)
regs = []
for p in skin.data.polygons:
    votes = {}
    for v in p.vertices:
        r = dominant_region(BONES[vdom[v]])
        votes[r] = votes.get(r, 0) + 1
    regs.append(max(votes, key=votes.get))
face_attr(skin, 'region', regs)
log('skin regions', {k: regs.count(v) for k, v in REGION.items()})

# ------------------------------------------------------------------ derived skin legs (for shorts/skirts)
bottom = parts['bottom']
shoes = parts['shoes']
bco = mesh_arrays(bottom)
sco = mesh_arrays(shoes)
bones = {b.name: (rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones}
thigh_z = bones['Thigh.L'][0].z
legs_bm = bmesh.new()
uv_l = legs_bm.loops.layers.uv.new('SlotUV')
SEG, RINGS = 16, 24
leg_meta = {}
for side, sign in (('L', 1), ('R', -1)):
    sel = bco[:, 0] * sign > 0.005
    pts = bco[sel]
    shoe_top = float(np.percentile(sco[sco[:, 0] * sign > 0][:, 2], 99))
    z0, z1 = max(shoe_top - 0.02, float(pts[:, 2].min()) + 0.012), thigh_z - 0.10
    rings = []
    for i in range(RINGS):
        z = z0 + (z1 - z0) * i / (RINGS - 1)
        s = pts[np.abs(pts[:, 2] - z) < 0.02]
        if len(s) < 12:
            s = pts[np.argsort(np.abs(pts[:, 2] - z))[:40]]
        cx, cy = np.median(s[:, 0]), np.median(s[:, 1])
        rx = np.percentile(np.abs(s[:, 0] - cx), 92)
        ry = np.percentile(np.abs(s[:, 1] - cy), 92)
        rings.append([z, cx, cy, rx, ry])
    rings = np.array(rings)
    for c in range(1, 5):  # smooth profile along the leg
        rings[1:-1, 1:] = (rings[:-2, 1:] + 2 * rings[1:-1, 1:] + rings[2:, 1:]) / 4
    inset = 0.007
    rings[:, 3:] = np.maximum(rings[:, 3:] - inset, 0.02)
    leg_meta[side] = {'z0': z0, 'z1': z1, 'rings': rings.tolist()}
    verts = []
    vinfo = {}
    for i, (z, cx, cy, rx, ry) in enumerate(rings):
        row = []
        for k in range(SEG):
            a = 2 * math.pi * k / SEG
            # angle 0 = back (+Y) so the UV seam sits behind the leg
            v = legs_bm.verts.new((cx + rx * math.sin(a) * sign, cy + ry * math.cos(a), z))
            vinfo[v] = (i, k)
            row.append(v)
        verts.append(row)
    circ = math.pi * (rings[:, 3].mean() + rings[:, 4].mean())
    for i in range(RINGS - 1):
        for k in range(SEG):
            k2 = (k + 1) % SEG
            quad = [verts[i][k], verts[i][k2], verts[i + 1][k2], verts[i + 1][k]]
            if sign < 0:
                quad.reverse()
            f = legs_bm.faces.new(quad)
            for loop in f.loops:
                ii, kk = vinfo[loop.vert]
                u = kk / SEG
                if k == SEG - 1 and kk == 0:
                    u = 1.0
                loop[uv_l].uv = (u * circ, rings[ii][0] - z0)
legs_me = bpy.data.meshes.new(f'{fam}_base_legs')
legs_bm.normal_update()
legs_bm.to_mesh(legs_me)
legs_bm.free()
legs = bpy.data.objects.new(f'{fam}_base_legs', legs_me)
scene.collection.objects.link(legs)
legs.parent = rig
legs_me.uv_layers.new(name='AtlasUV')
for p in legs_me.polygons:
    p.use_smooth = True
# weights: copy nearest bottom-garment vertex weights
kd = kdtree.KDTree(len(bco))
for i, c in enumerate(bco):
    kd.insert(c, i)
kd.balance()
Wb = weights_matrix(bottom, BONES)
lco = mesh_arrays(legs)
Wl = np.zeros((len(lco), len(BONES)), dtype=np.float32)
for i, c in enumerate(lco):
    hits = kd.find_n(c, 6)
    ws = np.array([1 / max(d, 1e-4) for _, _, d in hits])
    for (co, j, d), w in zip(hits, ws):
        Wl[i] += Wb[j] * w
    Wl[i] /= ws.sum()
set_weights_matrix(legs, Wl, BONES, 4)
legs.modifiers.new('Game skin', 'ARMATURE').object = rig
face_attr(legs, 'sub', [SUB_IDS['legs']] * len(legs_me.polygons))
face_attr(legs, 'region', [REGION['legs']] * len(legs_me.polygons))
parts['legs'] = legs
log('legs tris', tri_count(legs))

# ------------------------------------------------------------------ slot UV packing
SLOT_OF = {'head': 'HEAD', 'hair': 'HAIR', 'skin': 'SKIN', 'legs': 'SKIN', 'top': 'TOP', 'bottom': 'BOTTOM',
           'shoes': 'SHOES', 'scarf': 'ACC_C'}
DENSITY = {SUB_IDS['eyes']: 1.35, SUB_IDS['face']: 1.0, SUB_IDS['brows']: 1.0, SUB_IDS['lashes']: 0.8,
           SUB_IDS['legs']: 0.6, SUB_IDS['arms']: 1.0}

def pack_slot(objs, slot, margin=0.012):
    for o in objs:
        uvs = o.data.uv_layers
        if 'SlotUV' not in uvs:
            l = uvs.new(name='SlotUV')
            src_l = uvs['AtlasUV']
            arr = np.empty(len(o.data.loops) * 2, dtype=np.float32)
            src_l.data.foreach_get('uv', arr)
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
        for p in o.data.polygons:
            f = DENSITY.get(int(subs[p.index]), 1.0)
            if f != 1.0:
                for li in p.loop_indices:
                    l.data[li].uv *= f
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
        assert arr.min() > -1e-4 and arr.max() < 1 + 1e-4, (o.name, arr.min(), arr.max())
        arr[:, 0] = x + np.clip(arr[:, 0], 0, 1) * w
        arr[:, 1] = y + np.clip(arr[:, 1], 0, 1) * h
        l.data.foreach_set('uv', arr.ravel())

groups = {}
for part, o in parts.items():
    groups.setdefault(SLOT_OF[part], []).append(o)
for slot, objs in groups.items():
    pack_slot(objs, slot)
    log('packed', slot, [o.name for o in objs])

# ------------------------------------------------------------------ bake original maps into slot layout
def load_raw(path):
    im = bpy.data.images.load(str(path), check_existing=False)
    im.colorspace_settings.name = 'Non-Color'
    im.alpha_mode = 'CHANNEL_PACKED'
    return im
old = {k: load_raw(p) for k, p in cfg['tex'].items()}

bake_objs = []
for part, o in parts.items():
    b = duplicate(o, '__bake_' + part)
    b.modifiers.clear()
    bake_objs.append(b)
select_only(bake_objs)
bpy.ops.object.join()
B = bpy.context.object
B.data.uv_layers.active = B.data.uv_layers['SlotUV']
mat = bpy.data.materials.new('__bake')
mat.use_nodes = True
nt = mat.node_tree
nodes, links = nt.nodes, nt.links
for n in list(nodes):
    nodes.remove(n)
outn = nodes.new('ShaderNodeOutputMaterial')
emis = nodes.new('ShaderNodeEmission')
links.new(emis.outputs[0], outn.inputs['Surface'])
uvn = nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'AtlasUV'
tex = {}
for k, im in old.items():
    t = nodes.new('ShaderNodeTexImage'); t.image = im; t.interpolation = 'Linear'
    links.new(uvn.outputs['UV'], t.inputs['Vector']); tex[k] = t
geo = nodes.new('ShaderNodeNewGeometry')
attr_sub = nodes.new('ShaderNodeAttribute'); attr_sub.attribute_type = 'GEOMETRY'; attr_sub.attribute_name = 'sub'
attr_reg = nodes.new('ShaderNodeAttribute'); attr_reg.attribute_type = 'GEOMETRY'; attr_reg.attribute_name = 'region'
def vec_mad(sock, mul, add):
    m = nodes.new('ShaderNodeVectorMath'); m.operation = 'MULTIPLY_ADD'
    links.new(sock, m.inputs[0]); m.inputs[1].default_value = (mul,) * 3; m.inputs[2].default_value = (add,) * 3
    return m.outputs[0]
comb_ms = nodes.new('ShaderNodeCombineXYZ')
sep_ms = nodes.new('ShaderNodeSeparateColor'); links.new(tex['ms'].outputs['Color'], sep_ms.inputs['Color'])
links.new(sep_ms.outputs['Red'], comb_ms.inputs['X']); links.new(tex['ms'].outputs['Alpha'], comb_ms.inputs['Y'])
comb_id = nodes.new('ShaderNodeCombineXYZ')
d1 = nodes.new('ShaderNodeMath'); d1.operation = 'DIVIDE'; links.new(attr_sub.outputs['Fac'], d1.inputs[0]); d1.inputs[1].default_value = 32
d2 = nodes.new('ShaderNodeMath'); d2.operation = 'DIVIDE'; links.new(attr_reg.outputs['Fac'], d2.inputs[0]); d2.inputs[1].default_value = 8
links.new(d1.outputs[0], comb_id.inputs['X']); links.new(d2.outputs[0], comb_id.inputs['Y']); comb_id.inputs['Z'].default_value = 1
SOURCES = {'base': tex['base'].outputs['Color'], 'normal': tex['normal'].outputs['Color'], 'ms': comb_ms.outputs[0],
           'pos': vec_mad(geo.outputs['Position'], 0.5, 0.5), 'nrm': vec_mad(geo.outputs['Normal'], 0.5, 0.5),
           'id': comb_id.outputs[0]}
target = nodes.new('ShaderNodeTexImage')
B.data.materials.clear(); B.data.materials.append(mat)
for p in B.data.polygons:
    p.material_index = 0
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 1
scene.render.bake.use_selected_to_active = False
results = {}
for kind, sock in SOURCES.items():
    im = new_float_image('__bake_' + kind, RES)
    target.image = im
    for n in nodes:
        n.select = False
    target.select = True
    nodes.active = target
    links.new(sock, emis.inputs['Color'])
    select_only([B])
    t0 = time.time()
    bpy.ops.object.bake(type='EMIT', use_clear=True, margin=0 if kind == 'id' else 16, uv_layer='SlotUV')
    results[kind] = image_array(im)
    log('baked', kind, round(time.time() - t0, 1), 's')

cov = results['id'][:, :, 3] > 0.5
write_png(out / 'base_BaseColor.png', results['base'][:, :, :3])
write_png(out / 'base_Normal.png', results['normal'][:, :, :3])
ms = np.zeros((RES, RES, 4), dtype=np.float32)
ms[:, :, 0] = results['ms'][:, :, 0]; ms[:, :, 1] = 1; ms[:, :, 3] = results['ms'][:, :, 1]
write_png(out / 'base_MetallicSmoothness.png', ms)
np.save(out / 'base_pos.npy', results['pos'][:, :, :3].astype(np.float16))
np.save(out / 'base_nrm.npy', results['nrm'][:, :, :3].astype(np.float16))
idmap = np.zeros((RES, RES, 3), dtype=np.uint8)
idmap[:, :, 0] = np.rint(results['id'][:, :, 0] * 32).astype(np.uint8)
idmap[:, :, 1] = np.rint(results['id'][:, :, 1] * 8).astype(np.uint8)
idmap[:, :, 2] = cov.astype(np.uint8)
np.save(out / 'base_id.npy', idmap)
bpy.data.objects.remove(B, do_unlink=True)

# ------------------------------------------------------------------ metadata
meta = {'family': fam, 'bones': BONES,
        'bone_heads': {k: list(v[0]) for k, v in bones.items()}, 'bone_tails': {k: list(v[1]) for k, v in bones.items()},
        'parts': {k: {'triangles': tri_count(o), 'vertices': len(o.data.vertices)} for k, o in parts.items()},
        'legs': leg_meta}
for k, o in parts.items():
    c = mesh_arrays(o)
    meta['parts'][k]['bbox'] = [c.min(0).tolist(), c.max(0).tolist()]
hc = mesh_arrays(parts['head'])
meta['head_bbox'] = [hc.min(0).tolist(), hc.max(0).tolist()]
(out / 'meta.json').write_text(json.dumps(meta, indent=1))
for im in list(bpy.data.images):
    if im.name.startswith('__bake') or im in old.values():
        bpy.data.images.remove(im)
for m in list(bpy.data.materials):
    if m.users == 0:
        bpy.data.materials.remove(m)
bpy.ops.outliner.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'base.blend'), compress=True)
log('EXTRACT_DONE', fam, json.dumps(meta['parts']))
