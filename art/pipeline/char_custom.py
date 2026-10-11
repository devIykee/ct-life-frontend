"""Add CT-specific custom garments/accessories to a master character (procedural, made by us = CC0).

Usage: art/pipeline/blender.sh art/pipeline/char_custom.py -- male|female
Reads/writes art/raw/work/char_<sex>_master.blend.

Every generated part:
  * is a mesh named part.<key> skinned to the CTL skeleton (max 4 influences)
  * has a procedural material 'ctl_proc.<key>' (baked later into its texture tile)
  * carries custom props ctl_slot / ctl_source='ctlife:procedural' / ctl_tile
"""
import bpy, bmesh, sys, os, json, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C

SEX = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else 'male'
path = os.path.join(C.WORK, f'char_{SEX}_master.blend')
bpy.ops.wm.open_mainfile(filepath=path)
rig = bpy.data.objects[f'CTL_Rig_{SEX}']
body = bpy.data.objects['CTL_Body']
REG = json.loads(body['ctl_regions'])
B = rig.data.bones


def log(*a):
    print('[char_custom]', *a, flush=True)


def bone_head(n):
    return rig.matrix_world @ B[n].head_local


def bone_tail(n):
    return rig.matrix_world @ B[n].tail_local


# ------------------------------------------------------------------ measurements
bm_body = bmesh.new()
bm_body.from_mesh(body.data)
bm_body.verts.ensure_lookup_table()
bm_body.faces.ensure_lookup_table()
reg_layer = bm_body.faces.layers.int.get('ctl_region')
head_pts = [v.co.copy() for f in bm_body.faces if REG[f[reg_layer]] == 'head' for v in f.verts]
hx = [p.x for p in head_pts]; hy = [p.y for p in head_pts]; hz = [p.z for p in head_pts]
eye_z = bone_head('eye_l').z
above = [p for p in head_pts if p.z > eye_z - 0.01]
HEAD = dict(xmin=min(p.x for p in above), xmax=max(p.x for p in above), ymin=min(p.y for p in above), ymax=max(p.y for p in above),
            zmax=max(hz), eye_z=eye_z)
HEAD['cx'] = (HEAD['xmin'] + HEAD['xmax']) / 2
HEAD['cy'] = (HEAD['ymin'] + HEAD['ymax']) / 2
log('head', {k: round(v, 3) for k, v in HEAD.items()})
bvh_body = BVHTree.FromBMesh(bm_body)


def vg_dict(o):
    return {vg.index: vg.name for vg in o.vertex_groups}


def copy_weights_nearest(dst, src=body, bones=None, falloff=0.0):
    """Data-transfer style: copy deform weights from nearest body surface point (barycentric)."""
    names = vg_dict(src)
    me = src.data
    dst.vertex_groups.clear()
    groups = {}
    for v in dst.data.vertices:
        p = dst.matrix_world @ v.co
        loc, nor, fi, dist = bvh_body.find_nearest(p)
        if fi is None:
            continue
        f = bm_body.faces[fi]
        # barycentric-ish: inverse-distance weights of face verts
        ws = []
        for bv in f.verts:
            d = (bv.co - loc).length + 1e-5
            ws.append((1.0 / d, bv.index))
        tot = sum(w for w, _ in ws)
        acc = {}
        for w, vi in ws:
            for g in me.vertices[vi].groups:
                n = names[g.group]
                if n in C.BONE_NAMES:
                    acc[n] = acc.get(n, 0) + g.weight * w / tot
        if bones:
            acc = {k: x for k, x in acc.items() if k in bones} or {bones[0]: 1.0}
        top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
        t = sum(x for _, x in top) or 1
        for n, x in top:
            if n not in groups:
                groups[n] = dst.vertex_groups.new(name=n)
            groups[n].add([v.index], x / t, 'REPLACE')


def set_weights(o, mapping_fn):
    o.vertex_groups.clear()
    groups = {}
    for v in o.data.vertices:
        wd = mapping_fn(o.matrix_world @ v.co)
        tot = sum(wd.values()) or 1
        for n, x in sorted(wd.items(), key=lambda kv: -kv[1])[:4]:
            if x <= 0:
                continue
            if n not in groups:
                groups[n] = o.vertex_groups.new(name=n)
            groups[n].add([v.index], x / tot, 'REPLACE')


def finish(o, key, slot, tile, mat):
    o.name = o.data.name = 'part.' + key
    o.parent = rig
    o.matrix_parent_inverse = Matrix.Identity(4)
    m = o.modifiers.new('Armature', 'ARMATURE')
    m.object = rig
    o['ctl_slot'] = slot
    o['ctl_source'] = 'ctlife:procedural'
    o['ctl_tile'] = tile
    o.data.materials.clear()
    o.data.materials.append(mat)
    for p in o.data.polygons:
        p.use_smooth = True
    log('part', key, 'tris', sum(len(p.vertices) - 2 for p in o.data.polygons))
    return o


def new_obj(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def uv_unwrap(o, method='smart', angle=66, margin=0.02):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    if method == 'smart':
        bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin, area_weight=0.0)
    else:
        bpy.ops.uv.cube_project(cube_size=1.0)
        bpy.ops.uv.pack_islands(margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')


# ------------------------------------------------------------------ procedural materials
def proc_material(key, base_rgb, details=(), rough=0.8, emit=None):
    """Node material whose Base Color is baked to the part tile later.
    details: list of dicts {type: 'stripe'|'box'|'noise'|'zip', ...} evaluated in object space."""
    m = bpy.data.materials.new('ctl_proc.' + key)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value = rough
    tc = nt.nodes.new('ShaderNodeTexCoord')
    # subtle fabric noise
    noise = nt.nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 180.0
    noise.inputs['Detail'].default_value = 4.0
    nt.links.new(tc.outputs['Object'], noise.inputs['Vector'])
    ramp = nt.nodes.new('ShaderNodeMapRange')
    ramp.inputs['To Min'].default_value = 0.9
    ramp.inputs['To Max'].default_value = 1.06
    nt.links.new(noise.outputs['Fac'], ramp.inputs['Value'])
    col = nt.nodes.new('ShaderNodeRGB')
    col.name = 'base_rgb'
    col.outputs[0].default_value = (*srgb(base_rgb), 1)
    mul = nt.nodes.new('ShaderNodeMix')
    mul.data_type = 'RGBA'
    mul.blend_type = 'MULTIPLY'
    mul.inputs['Factor'].default_value = 1.0
    nt.links.new(col.outputs[0], mul.inputs[6])
    nt.links.new(ramp.outputs['Result'], mul.inputs[7])
    out = mul.outputs[2]
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['Object'], sep.inputs[0])
    for d in details:
        mask = None
        if d['type'] == 'box':          # axis-aligned box in object space
            parts = []
            for ax, (lo, hi) in zip('XYZ', d['bounds']):
                if lo is None:
                    continue
                gt = nt.nodes.new('ShaderNodeMath'); gt.operation = 'GREATER_THAN'
                lt = nt.nodes.new('ShaderNodeMath'); lt.operation = 'LESS_THAN'
                nt.links.new(sep.outputs[ax], gt.inputs[0]); gt.inputs[1].default_value = lo
                nt.links.new(sep.outputs[ax], lt.inputs[0]); lt.inputs[1].default_value = hi
                mm = nt.nodes.new('ShaderNodeMath'); mm.operation = 'MULTIPLY'
                nt.links.new(gt.outputs[0], mm.inputs[0]); nt.links.new(lt.outputs[0], mm.inputs[1])
                parts.append(mm)
            mask = parts[0]
            for p in parts[1:]:
                mm = nt.nodes.new('ShaderNodeMath'); mm.operation = 'MULTIPLY'
                nt.links.new(mask.outputs[0], mm.inputs[0]); nt.links.new(p.outputs[0], mm.inputs[1])
                mask = mm
        if d['type'] == 'stripes':      # periodic stripes along an axis
            mm = nt.nodes.new('ShaderNodeMath'); mm.operation = 'MULTIPLY'
            nt.links.new(sep.outputs[d['axis']], mm.inputs[0]); mm.inputs[1].default_value = d['freq']
            fr = nt.nodes.new('ShaderNodeMath'); fr.operation = 'FRACT'
            nt.links.new(mm.outputs[0], fr.inputs[0])
            lt = nt.nodes.new('ShaderNodeMath'); lt.operation = 'LESS_THAN'
            nt.links.new(fr.outputs[0], lt.inputs[0]); lt.inputs[1].default_value = d['duty']
            mask = lt
        if mask is None:
            continue
        c2 = nt.nodes.new('ShaderNodeRGB')
        c2.outputs[0].default_value = (*srgb(d['rgb']), 1)
        mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
        if d.get('multiply'):
            mix.blend_type = 'MULTIPLY'
        nt.links.new(mask.outputs[0], mix.inputs['Factor'])
        nt.links.new(out, mix.inputs[6])
        nt.links.new(c2.outputs[0], mix.inputs[7])
        out = mix.outputs[2]
    nt.links.new(out, bsdf.inputs['Base Color'])
    if emit:
        bsdf.inputs['Emission Color'].default_value = (*srgb(emit), 1)
        bsdf.inputs['Emission Strength'].default_value = 2.0
    m['ctl_bake_socket'] = 'Base Color'
    return m


def srgb(hexstr):
    if isinstance(hexstr, (tuple, list)):
        return hexstr
    c = [int(hexstr[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


# ------------------------------------------------------------------ shells from the body
UNDER = {
    'male': ['tee', 'polo', 'sweater', 'casual_denim', 'casual_long', 'casual_stripe', 'casual_white', 'casual_jacket'],
    'female': ['tee', 'camisole', 'tank', 'sweater', 'casual_jeans', 'casual_shorts', 'sport'],
}[SEX]
UNDER_BOTTOMS = {
    'male': ['cargo', 'slacks', 'shorts', 'casual_denim', 'casual_long', 'casual_white'],
    'female': ['cargo', 'slacks', 'shorts', 'casual_jeans', 'sport'],
}[SEX]


def bvh_of(o):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.transform(o.matrix_world)
    t = BVHTree.FromBMesh(bm)
    bm.free()
    return t


UNDER_BVH = [bvh_of(bpy.data.objects['part.' + k]) for k in UNDER if 'part.' + k in bpy.data.objects]


def outside_offset(p, n, min_off, margin, bvhs, reach=0.12):
    """Distance along n from body point p that clears every garment in bvhs by margin."""
    best = min_off
    for t in bvhs:
        hit = t.ray_cast(p - n * 0.004, n, reach)
        if hit[0] is not None:
            d = (hit[0] - p).dot(n)
            # a hit can be the far side of a sleeve: only count the near shell
            best = max(best, d + margin)
    return best


def shell(regions, offset, key, zmin=None, zmax=None, smooth=2, solid=0.006, extra=None, over=None, margin=0.012):
    bm = bmesh.new()
    bm.from_mesh(body.data)
    rl = bm.faces.layers.int.get('ctl_region')
    bm.normal_update()
    nrm = {v: v.normal.copy() for v in bm.verts}  # keyed by BMVert: bmesh ops clobber .index
    kill = []
    for f in bm.faces:
        r = REG[f[rl]]
        keep = r in regions
        c = f.calc_center_median()
        if keep and zmin is not None and c.z < zmin - 0.06:
            keep = False
        if keep and zmax is not None and c.z > zmax + 0.06:
            keep = False
        if keep and extra is not None and not extra(f, c, r):
            keep = False
        if not keep:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    # clean straight hems: bisect at zmin / zmax and drop what is outside
    for z, below in ((zmin, True), (zmax, False)):
        if z is None:
            continue
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z), plane_no=(0, 0, 1))
        for v in bm.verts:
            if v not in nrm:
                # new vertex on the cut: normal from its edge neighbours
                nb = [e.other_vert(v) for e in v.link_edges if e.other_vert(v) in nrm]
                nrm[v] = sum((nrm[x] for x in nb), Vector()).normalized() if nb else Vector((0, 0, 1))
        dead = [f for f in bm.faces if (f.calc_center_median().z < z) == below]
        bmesh.ops.delete(bm, geom=dead, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    base = {v: v.co.copy() for v in bm.verts}
    need = {}
    for v in bm.verts:
        n = nrm[v]
        need[v] = outside_offset(v.co.copy(), n, offset, margin, over) if over else offset
    offs = dict(need)
    # smooth the offset field so the shell doesn't follow every wrinkle of the garments below
    for _ in range(4):
        new = {}
        for v in bm.verts:
            nb = [e.other_vert(v) for e in v.link_edges]
            new[v] = max(offs[v], sum(offs[x] for x in nb) / len(nb)) if nb else offs[v]
        offs = new
    for v in bm.verts:
        v.co += nrm[v] * offs[v]
    for _ in range(smooth):
        bmesh.ops.smooth_vert(bm, verts=[v for v in bm.verts if not v.is_boundary], factor=0.5,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    # smoothing pulls convex areas (shoulders) back in: re-guarantee the clearance
    for v in bm.verts:
        d = (v.co - base[v]).dot(nrm[v])
        if d < need[v]:
            v.co += nrm[v] * (need[v] - d)
    o = new_obj('tmp.' + key, bm)
    copy_weights_nearest(o)
    if solid:
        sm = o.modifiers.new('Solidify', 'SOLIDIFY')
        sm.thickness = solid
        sm.offset = -1
        sm.use_rim = True
        sm.use_even_offset = True
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=sm.name)
    return o


SIDE = lambda c: 'l' if c.x > 0 else 'r'

# --- fleece vest (VC): torso only, over a shirt -----------------------------------------
vest = shell(['chest', 'belly', 'hips'], 0.02, 'vest',
             zmin=bone_head('pelvis').z + 0.02 if SEX == 'male' else bone_head('pelvis').z + 0.04,
             zmax=bone_head('neck_01').z + 0.015, over=UNDER_BVH, margin=0.01)
uv_unwrap(vest)
finish(vest, 'fleece_vest', 'outer', 512,
       proc_material('fleece_vest', '23324A', details=[
           {'type': 'box', 'bounds': [(-0.006, 0.006), (None, None), (None, None)], 'rgb': '9AA4B2'},            # zipper
           {'type': 'box', 'bounds': [(0.05, 0.11), (-1, 0), (bone_head('spine_03').z + 0.02, bone_head('spine_03').z + 0.045)], 'rgb': 'F2F2F2'},  # chest logo patch
       ]))

# --- trench coat (Sleuth): torso + sleeves + skirt to the knee -----------------------------
coat = shell(['chest', 'belly', 'hips', 'shoulder_l', 'shoulder_r', 'upperarm_l', 'upperarm_r', 'forearm_l', 'forearm_r'],
             0.022, 'coat', zmax=bone_head('neck_01').z + 0.02, smooth=3, over=UNDER_BVH, margin=0.015,
             zmin=bone_head('pelvis').z - 0.08)
# skirt: rings that follow the silhouette of hips/legs (+ worn trousers) from hip to knee
pel = bone_head('pelvis')
knee_z = bone_head('calf_l').z + 0.05
top_z = pel.z + 0.06   # overlaps the torso shell so there is no gap at the waist
sil_src = [v.co.copy() for v in bm_body.verts]
for k in UNDER_BOTTOMS:
    o = bpy.data.objects.get('part.' + k)
    if o:
        sil_src += [o.matrix_world @ v.co for v in o.data.vertices]
N, K = 32, 7
bm = bmesh.new()
rings = []
for k in range(K + 1):
    t = k / K
    z = top_z + (knee_z - top_z) * t
    band = [p for p in sil_src if abs(p.z - z) < 0.025]
    rad = [0.0] * N
    for p in band:
        a = math.atan2(p.y, p.x) % (2 * math.pi)
        i = int(a / (2 * math.pi) * N) % N
        rad[i] = max(rad[i], math.hypot(p.x, p.y))
    # fill gaps (between the legs) with the convex envelope: max of neighbours
    for _ in range(N):
        rad = [max(rad[i], 0.5 * (rad[i - 1] + rad[(i + 1) % N]) * 0.98) for i in range(N)]
    rad = [0.5 * rad[i] + 0.25 * (rad[i - 1] + rad[(i + 1) % N]) for i in range(N)]
    flare = 0.006 + 0.06 * t * t
    ring = []
    for i in range(N):
        a = 2 * math.pi * (i + 0.5) / N
        r = rad[i] + 0.018 + flare
        ring.append(bm.verts.new((math.cos(a) * r, math.sin(a) * r, z)))
    rings.append(ring)
for k in range(K):
    for i in range(N):
        a = 2 * math.pi * (i + 0.5) / N
        if abs(a - 1.5 * math.pi) < 0.1 and k > 2:   # front slit (-Y)
            continue
        bm.faces.new((rings[k][i], rings[k][(i + 1) % N], rings[k + 1][(i + 1) % N], rings[k + 1][i]))
skirt = new_obj('tmp.skirt', bm)
sm = skirt.modifiers.new('Solidify', 'SOLIDIFY'); sm.thickness = 0.006; sm.offset = -1; sm.use_rim = True
bpy.context.view_layer.objects.active = skirt
bpy.ops.object.modifier_apply(modifier=sm.name)
rx = max(abs(p.x) for p in sil_src if abs(p.z - top_z) < 0.03)

def skirt_w(p):
    t = max(0.0, min(1.0, (top_z - p.z) / (top_z - knee_z)))
    side = 'thigh_l' if p.x > 0 else 'thigh_r'
    s = min(1.0, abs(p.x) / rx * 1.6)
    return {'pelvis': (1 - t) + t * (1 - s) * 0.6, side: t * (0.4 + 0.6 * s)}
set_weights(skirt, skirt_w)
bpy.ops.object.select_all(action='DESELECT')
coat.select_set(True); skirt.select_set(True)
bpy.context.view_layer.objects.active = coat
bpy.ops.object.join()
uv_unwrap(coat)
finish(coat, 'trench_coat', 'outer', 512,
       proc_material('trench_coat', 'B8925E', details=[
           {'type': 'box', 'bounds': [(None, None), (None, None), (pel.z + 0.07, pel.z + 0.11)], 'rgb': '8C6A3F'},  # belt
           {'type': 'box', 'bounds': [(-0.035, -0.02), (-1, 0), (None, None)], 'rgb': '9C7A4B'},                    # placket
       ]))

# --- hoodie hood (down): rolled collar around the neck + fabric pouch on the upper back ---
nk = bone_head('neck_01')
sp3 = bone_head('spine_03')
neck_pts = [v.co for f in bm_body.faces if REG[f[reg_layer]] == 'neck' for v in f.verts]
nrx = max(abs(p.x) for p in neck_pts) + 0.03
SWEATER_BVH = [bvh_of(bpy.data.objects['part.sweater'])]
bm = bmesh.new()
segs, tube = 22, 8
R_ring = nrx + 0.03
r_tube = 0.03
ncx = sum(p.x for p in neck_pts) / len(neck_pts)
ncy = sum(p.y for p in neck_pts) / len(neck_pts)
nz = nk.z - 0.035
rings = []
for i in range(segs + 1):
    th = math.radians(205 - 230 * i / segs)          # through the back (+Y); open at the front
    center = Vector((ncx + math.cos(th) * R_ring, ncy + math.sin(th) * R_ring * 0.95, nz))
    radial = Vector((math.cos(th), math.sin(th), 0))
    back = max(0.0, math.sin(th))
    rt = r_tube * (0.75 + 0.5 * back)
    ring = []
    for j in range(tube):
        b = 2 * math.pi * j / tube
        off = radial * math.cos(b) * rt + Vector((0, 0, 1)) * math.sin(b) * rt * 1.2
        ring.append(bm.verts.new(center + off))
    rings.append(ring)
for i in range(segs):
    for j in range(tube):
        bm.faces.new((rings[i][j], rings[i][(j + 1) % tube], rings[i + 1][(j + 1) % tube], rings[i + 1][j]))
for ring in (rings[0], rings[-1]):
    bm.faces.new(ring)
collar = new_obj('tmp.collar', bm)
set_weights(collar, lambda p: {'neck_01': 0.45, 'spine_03': 0.55})
pouch = shell(['chest', 'neck'], 0.03, 'pouch', zmin=sp3.z - 0.12, zmax=nz + 0.02,
              extra=lambda f, c, r: c.y > ncy + 0.01 and abs(c.x) < 0.14, over=SWEATER_BVH, margin=0.018, smooth=3, solid=0.01)
bpy.ops.object.select_all(action='DESELECT')
collar.select_set(True); pouch.select_set(True)
bpy.context.view_layer.objects.active = collar
bpy.ops.object.join()
hood = collar

hood['ctl_join_into'] = 'sweater'
uv_unwrap(hood)
finish(hood, 'hood', 'top_addon', 256, proc_material('hood', '2B2B30'))

# --- hard hat (ORE miner) ------------------------------------------------------------------
W = (HEAD['xmax'] - HEAD['xmin']) / 2 + 0.018
D = (HEAD['ymax'] - HEAD['ymin']) / 2 + 0.02
top = HEAD['zmax'] + 0.022
base_z = HEAD['eye_z'] + 0.045
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=1.0)
for v in list(bm.verts):
    if v.co.z < -0.05:
        pass
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -0.01], context='VERTS')
for v in bm.verts:
    v.co = Vector((HEAD['cx'] + v.co.x * W, HEAD['cy'] + v.co.y * D, base_z + max(0, v.co.z) * (top - base_z)))
# brim
ring_in = [v for v in bm.verts if abs(v.co.z - base_z) < 1e-4]
ring_in.sort(key=lambda v: math.atan2(v.co.y - HEAD['cy'], v.co.x - HEAD['cx']))
outer = []
for v in ring_in:
    d = Vector((v.co.x - HEAD['cx'], v.co.y - HEAD['cy'], 0))
    front = max(0.0, -d.normalized().y)  # brim larger at the front (-Y)
    outer.append(bm.verts.new(v.co + d.normalized() * (0.025 + 0.035 * front) + Vector((0, 0, -0.008))))
n = len(ring_in)
for i in range(n):
    bm.faces.new((ring_in[i], ring_in[(i + 1) % n], outer[(i + 1) % n], outer[i]))
hat = new_obj('tmp.hardhat', bm)
sm = hat.modifiers.new('Solidify', 'SOLIDIFY'); sm.thickness = 0.006; sm.offset = 1
bpy.context.view_layer.objects.active = hat
bpy.ops.object.modifier_apply(modifier=sm.name)
set_weights(hat, lambda p: {'Head': 1.0})
uv_unwrap(hat)
finish(hat, 'hard_hat', 'hat', 256, proc_material('hard_hat', 'F5B21B', rough=0.35, details=[
    {'type': 'box', 'bounds': [(-0.012, 0.012), (None, None), (base_z + 0.03, 9)], 'rgb': 'E39A0E'},   # ridge
    {'type': 'box', 'bounds': [(-0.03, 0.03), (-1, HEAD['cy'] - D * 0.6), (base_z + 0.02, base_z + 0.06)], 'rgb': '3B3B3B'},  # ORE badge
]))
hat['ctl_hair_mode'] = 'hide'

# --- headset (Community Mod / streamer) ----------------------------------------------------
ear_z = HEAD['eye_z'] - 0.015
ear_y = HEAD['cy'] + 0.01
hw = (HEAD['xmax'] - HEAD['xmin']) / 2
bm = bmesh.new()
# band: arc over the head in the XZ plane
band_pts = []
segs = 16
for i in range(segs + 1):
    a = math.pi * i / segs
    band_pts.append(Vector((math.cos(a) * (hw + 0.03), ear_y, ear_z + 0.03 + math.sin(a) * (HEAD['zmax'] + 0.025 - ear_z - 0.03))))
prev = None
for i, p in enumerate(band_pts):
    ring = [bm.verts.new(p + Vector((0, dy, dz))) for dy, dz in ((-0.014, -0.004), (0.014, -0.004), (0.014, 0.004), (-0.014, 0.004))]
    if prev:
        for j in range(4):
            bm.faces.new((prev[j], prev[(j + 1) % 4], ring[(j + 1) % 4], ring[j]))
    prev = ring
cups = []
for s in (1, -1):
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=14, radius1=0.042, radius2=0.038, depth=0.03,
                              matrix=Matrix.Translation((s * (hw + 0.022), ear_y, ear_z)) @ Matrix.Rotation(math.radians(90), 4, 'Y'))
# mic boom from the left cup towards the mouth
mouth = Vector((0.0, HEAD['ymin'] - 0.01, HEAD['eye_z'] - 0.085))
start = Vector((hw + 0.03, ear_y - 0.02, ear_z - 0.02))
v = mouth - start
r = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.005, radius2=0.005, depth=v.length * 0.9,
                          matrix=Matrix.Translation(start + v * 0.45) @ v.to_track_quat('Z', 'Y').to_matrix().to_4x4())
bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=0.012, matrix=Matrix.Translation(start + v * 0.92))
hs = new_obj('tmp.headset', bm)
set_weights(hs, lambda p: {'Head': 1.0})
uv_unwrap(hs, method='cube')
finish(hs, 'headset', 'acc', 256, proc_material('headset', '1E1E24', rough=0.4, details=[
    {'type': 'box', 'bounds': [(hw + 0.035, 1), (None, None), (None, None)], 'rgb': '5865F2'},   # blurple cup face
    {'type': 'box', 'bounds': [(-1, -(hw + 0.035)), (None, None), (None, None)], 'rgb': '5865F2'},
]))

# --- ring light on a selfie stick (Caller/KOL, streamer), held in the right hand ------------
hand = bone_head('hand_r')
hand_t = bone_tail('hand_r')
grip = hand + (hand_t - hand) * 0.55 + Vector((0, 0, -0.02))
bm = bmesh.new()
stick_top = grip + Vector((0, -0.02, 0.32))
v = stick_top - grip
bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.012, radius2=0.009, depth=v.length,
                      matrix=Matrix.Translation(grip + v * 0.5) @ v.to_track_quat('Z', 'Y').to_matrix().to_4x4())
ring_c = stick_top + Vector((0, 0, 0.13))
# torus (ring light) in the YZ plane: its normal runs along the hand axis, so when the arm points
# forward the light faces back at the character (selfie/streamer pose)
R_, r_ = 0.13, 0.016
segs, tube = 28, 8
verts = []
for i in range(segs):
    a = 2 * math.pi * i / segs
    row = []
    for j in range(tube):
        b = 2 * math.pi * j / tube
        y = (R_ + r_ * math.cos(b)) * math.cos(a)
        z = (R_ + r_ * math.cos(b)) * math.sin(a)
        x = r_ * math.sin(b)
        row.append(bm.verts.new(ring_c + Vector((x, y, z))))
    verts.append(row)
for i in range(segs):
    for j in range(tube):
        bm.faces.new((verts[i][j], verts[i][(j + 1) % tube], verts[(i + 1) % segs][(j + 1) % tube], verts[(i + 1) % segs][j]))
# phone in the middle
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(ring_c) @ Matrix.Diagonal((0.008, 0.07, 0.14, 1)))
rl = new_obj('tmp.ringlight', bm)
set_weights(rl, lambda p: {'hand_r': 1.0})
uv_unwrap(rl, method='cube')
finish(rl, 'ring_light', 'held', 256, proc_material('ring_light', '1C1C1C', rough=0.4, details=[
    {'type': 'box', 'bounds': [(None, None), (None, None), (ring_c.z - R_ - 0.03, ring_c.z + R_ + 0.03)], 'rgb': 'FFF6E8'},
    {'type': 'box', 'bounds': [(None, None), (ring_c.y - 0.036, ring_c.y + 0.036), (ring_c.z - 0.072, ring_c.z + 0.072)], 'rgb': '15151A'},
], emit='FFF6E8'))

# --- gold chain (Giveaway Guy / degen) -------------------------------------------------------
chest_z = bone_head('spine_03').z
neck_base = bone_head('neck_01')
bm = bmesh.new()
segs, tube = 36, 6
pts = []
front_y = min(p.y for p in neck_pts) - 0.004
for i in range(segs):
    a = 2 * math.pi * i / segs
    x = math.sin(a) * (nrx + 0.02)
    yb = max(p.y for p in neck_pts) + 0.01
    y = (front_y - 0.05) if math.cos(a) > 0 else yb
    t = (math.cos(a) + 1) / 2   # 1 at front
    z = neck_base.z - 0.01 - t * 0.12
    yy = yb + (front_y - 0.035 - yb) * t
    p = Vector((x, yy, z))
    loc, nor, fi, dist = bvh_body.find_nearest(p)
    if loc is not None and t > 0.2:
        p = loc + nor * 0.012
    pts.append(p)
rings = []
for i, p in enumerate(pts):
    tng = (pts[(i + 1) % segs] - pts[i - 1]).normalized()
    side = tng.cross(Vector((0, 0, 1))).normalized()
    up = side.cross(tng).normalized()
    rings.append([bm.verts.new(p + (side * math.cos(2 * math.pi * j / tube) + up * math.sin(2 * math.pi * j / tube)) * 0.005) for j in range(tube)])
for i in range(segs):
    for j in range(tube):
        bm.faces.new((rings[i][j], rings[i][(j + 1) % tube], rings[(i + 1) % segs][(j + 1) % tube], rings[(i + 1) % segs][j]))
ch = new_obj('tmp.chain', bm)
set_weights(ch, lambda p: {'spine_03': 0.7, 'neck_01': 0.3})
uv_unwrap(ch, method='cube')
finish(ch, 'gold_chain', 'neck', 256, proc_material('gold_chain', 'E8B931', rough=0.25, details=[
    {'type': 'stripes', 'axis': 'X', 'freq': 120, 'duty': 0.5, 'rgb': 'B8861A'}]))

# --- siren hat (Town Crier) ------------------------------------------------------------------
bm = bmesh.new()
cap_r = (HEAD['xmax'] - HEAD['xmin']) / 2 + 0.012
bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=cap_r, radius2=cap_r * 0.92, depth=0.05,
                      matrix=Matrix.Translation((HEAD['cx'], HEAD['cy'], HEAD['zmax'] - 0.02)))
bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.035, radius2=0.035, depth=0.03,
                      matrix=Matrix.Translation((HEAD['cx'], HEAD['cy'], HEAD['zmax'] + 0.02)))
bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.04,
                          matrix=Matrix.Translation((HEAD['cx'], HEAD['cy'], HEAD['zmax'] + 0.05)) @ Matrix.Diagonal((1, 1, 1.25, 1)))
sh = new_obj('tmp.siren', bm)
set_weights(sh, lambda p: {'Head': 1.0})
uv_unwrap(sh, method='cube')
finish(sh, 'siren_hat', 'hat', 256, proc_material('siren_hat', '2A2A33', rough=0.4, details=[
    {'type': 'box', 'bounds': [(None, None), (None, None), (HEAD['zmax'] + 0.025, 9)], 'rgb': 'FF2A2A'}], emit=None))
sh['ctl_hair_mode'] = 'short'


# --- baseball cap (CT staple; hair goes short under it) -------------------------------------
W2 = (HEAD['xmax'] - HEAD['xmin']) / 2 + 0.01
D2 = (HEAD['ymax'] - HEAD['ymin']) / 2 + 0.004
cap_base = HEAD['eye_z'] + 0.05
cap_top = HEAD['zmax'] + 0.012
cy2 = HEAD['cy'] + 0.012
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=1.0)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -0.01], context='VERTS')
for v in bm.verts:
    v.co = Vector((HEAD['cx'] + v.co.x * W2, cy2 + v.co.y * D2, cap_base + max(0, v.co.z) * (cap_top - cap_base)))
# bill: a curved plate sticking out at the front (-Y)
rows = []
for k in range(4):
    t = k / 3
    row = []
    for i in range(9):
        a = math.radians(-60 + 120 * i / 8)
        r_in = 1.0
        x = math.sin(a) * W2 * (1.0 - 0.15 * t)
        y = cy2 - math.cos(a) * D2 - t * 0.075
        z = cap_base + 0.004 - 0.01 * t * t - 0.006 * abs(math.sin(a))
        row.append(bm.verts.new((HEAD['cx'] + x, y, z)))
    rows.append(row)
for k in range(3):
    for i in range(8):
        bm.faces.new((rows[k][i], rows[k][i + 1], rows[k + 1][i + 1], rows[k + 1][i]))
capo = new_obj('tmp.cap', bm)
sm = capo.modifiers.new('Solidify', 'SOLIDIFY'); sm.thickness = 0.005; sm.offset = 1
bpy.context.view_layer.objects.active = capo
bpy.ops.object.modifier_apply(modifier=sm.name)
set_weights(capo, lambda p: {'Head': 1.0})
uv_unwrap(capo)
finish(capo, 'baseball_cap', 'hat', 256, proc_material('baseball_cap', '1F2A44', rough=0.7, details=[
    {'type': 'box', 'bounds': [(-0.025, 0.025), (-1, cy2 - D2 * 0.75), (cap_base + 0.02, cap_base + 0.055)], 'rgb': 'F2C230'},  # logo patch
    {'type': 'box', 'bounds': [(-0.004, 0.004), (None, None), (cap_top - 0.006, 9)], 'rgb': '111111'},                              # button
]))
capo['ctl_hair_mode'] = 'short'

# --- shades / laser eyes (Laser-Eyes Maxi) ------------------------------------------------------
ez = HEAD['eye_z']
brow_pts = [v.co for v in bm_body.verts if abs(v.co.z - (ez + 0.005)) < 0.012 and abs(v.co.x) < 0.06]
front_y = min(p.y for p in brow_pts) - 0.012
ex = abs(bone_head('eye_l').x)
bm = bmesh.new()
for sx in (1, -1):
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((sx * ex, front_y, ez - 0.004)) @ Matrix.Diagonal((0.052, 0.006, 0.034, 1)))
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((0, front_y, ez + 0.012)) @ Matrix.Diagonal((0.03, 0.006, 0.008, 1)))  # bridge
hw2 = (HEAD['xmax'] - HEAD['xmin']) / 2 + 0.006
for sx in (1, -1):
    y1 = HEAD['cy'] + 0.02
    mid = Vector((sx * hw2, (front_y + y1) / 2, ez + 0.01))
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(mid) @ Matrix.Diagonal((0.006, abs(y1 - front_y), 0.008, 1)))
sg = new_obj('tmp.shades', bm)
set_weights(sg, lambda p: {'Head': 1.0})
uv_unwrap(sg, method='cube')
finish(sg, 'shades', 'glasses', 256, proc_material('shades', '101014', rough=0.15))

# --- glowing ORE backpack (ORE-only cosmetic) ---------------------------------------------------
sp3 = bone_head('spine_03'); sp2 = bone_head('spine_02')
back_pts = [v.co for v in bm_body.verts if sp2.z < v.co.z < sp3.z + 0.05 and abs(v.co.x) < 0.1]
back_y = max(p.y for p in back_pts)
UNDER_ALL = UNDER_BVH
bm = bmesh.new()
pack_c = Vector((0, back_y + 0.1, (sp2.z + sp3.z) / 2 + 0.02))
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(pack_c) @ Matrix.Diagonal((0.28, 0.13, 0.34, 1)))
bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.03, segments=2, affect='EDGES')
# crystals sticking out of the top flap
for i, (dx, h, tilt) in enumerate(((-0.07, 0.11, -0.3), (0.0, 0.16, 0.0), (0.075, 0.09, 0.35))):
    m = Matrix.Translation(pack_c + Vector((dx, 0.01, 0.17 + h * 0.4))) @ Matrix.Rotation(tilt, 4, 'Y')
    bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.035, radius2=0.0, depth=h, matrix=m)
# straps
for sx in (1, -1):
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((sx * 0.085, back_y + 0.012, sp3.z + 0.02)) @ Matrix.Diagonal((0.04, 0.018, 0.26, 1)))
bp = new_obj('tmp.backpack', bm)
set_weights(bp, lambda p: {'spine_03': 0.75, 'spine_02': 0.25})
uv_unwrap(bp, method='cube')
finish(bp, 'ore_backpack', 'back', 256, proc_material('ore_backpack', '3A3F4A', rough=0.6, details=[
    {'type': 'box', 'bounds': [(None, None), (None, None), (pack_c.z + 0.17, 9)], 'rgb': 'FFB547'},   # glowing ore
    {'type': 'box', 'bounds': [(-0.05, 0.05), (pack_c.y + 0.06, 1), (pack_c.z - 0.04, pack_c.z + 0.04)], 'rgb': 'E39A0E'},  # ORE badge
], emit='FFB547'))

# --- straw hat (Airdrop farmer) -------------------------------------------------------------------
bm = bmesh.new()
crown_r = (HEAD['xmax'] - HEAD['xmin']) / 2 + 0.016
base_z = HEAD['eye_z'] + 0.055
bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=crown_r, radius2=crown_r * 0.82, depth=HEAD['zmax'] + 0.05 - base_z,
                      matrix=Matrix.Translation((HEAD['cx'], HEAD['cy'] + 0.01, (base_z + HEAD['zmax'] + 0.05) / 2)))
bmesh.ops.create_cone(bm, cap_ends=True, segments=32, radius1=crown_r + 0.12, radius2=crown_r + 0.01, depth=0.03,
                      matrix=Matrix.Translation((HEAD['cx'], HEAD['cy'] + 0.01, base_z + 0.01)))
sh2 = new_obj('tmp.straw', bm)
set_weights(sh2, lambda p: {'Head': 1.0})
uv_unwrap(sh2, method='cube')
finish(sh2, 'straw_hat', 'hat', 256, proc_material('straw_hat', 'E2C27A', rough=0.9, details=[
    {'type': 'stripes', 'axis': 'Z', 'freq': 160, 'duty': 0.35, 'rgb': 'C9A35C'},
    {'type': 'box', 'bounds': [(None, None), (None, None), (base_z + 0.03, base_z + 0.055)], 'rgb': 'B8322A'},   # band
]))
sh2['ctl_hair_mode'] = 'short'

# --- lanyard with badge (Auditor / conference) -----------------------------------------------------
bm = bmesh.new()
segs = 30
pts = []
nb = bone_head('neck_01')
for i in range(segs + 1):
    t = i / segs
    a = math.pi * 2 * t
    x = math.sin(a) * (nrx + 0.015)
    fr = (math.cos(a) + 1) / 2
    y = ncy + (math.cos(a) * -1) * 0.075 - fr * 0.0
    z = nb.z - 0.03 - fr * fr * 0.17
    p = Vector((x, y, z))
    n_ = (p - Vector((0, ncy, p.z))).normalized() if fr < 0.5 else Vector((0, -1, 0))
    d = outside_offset(p - n_ * 0.05, n_, 0.0, 0.006, UNDER_BVH + [bvh_body], reach=0.2)
    pts.append(p - n_ * 0.05 + n_ * max(d, 0.05))
prev = None
for i, p in enumerate(pts):
    tng = (pts[min(i + 1, segs)] - pts[max(i - 1, 0)]).normalized()
    side = tng.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
    ring = [bm.verts.new(p + side * 0.008), bm.verts.new(p - side * 0.008)]
    if prev:
        bm.faces.new((prev[0], prev[1], ring[1], ring[0]))
    prev = ring
badge_c = pts[segs // 2] + Vector((0, -0.006, -0.05))
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(badge_c) @ Matrix.Diagonal((0.06, 0.004, 0.085, 1)))
ly = new_obj('tmp.lanyard', bm)
sm = ly.modifiers.new('Solidify', 'SOLIDIFY'); sm.thickness = 0.002
bpy.context.view_layer.objects.active = ly
bpy.ops.object.modifier_apply(modifier=sm.name)
set_weights(ly, lambda p: {'spine_03': 0.8, 'neck_01': 0.2})
uv_unwrap(ly, method='cube')
finish(ly, 'lanyard', 'neck', 256, proc_material('lanyard', 'D9342B', rough=0.6, details=[
    {'type': 'box', 'bounds': [(badge_c.x - 0.031, badge_c.x + 0.031), (None, None), (badge_c.z - 0.043, badge_c.z + 0.043)], 'rgb': 'F4F4F0'},
    {'type': 'box', 'bounds': [(badge_c.x - 0.022, badge_c.x + 0.022), (None, None), (badge_c.z + 0.012, badge_c.z + 0.03)], 'rgb': '2B6CD6'},
]))

# --- fox mask (Alpha Caller "anime mask") -----------------------------------------------------------
mask = shell(['head'], 0.012, 'mask', zmin=HEAD['eye_z'] - 0.075, zmax=HEAD['eye_z'] + 0.05,
             extra=lambda f, c, r: c.y < HEAD['cy'] - 0.035 and abs(c.x) < 0.075, solid=0.004, smooth=2)
uv_unwrap(mask)
finish(mask, 'fox_mask', 'face', 256, proc_material('fox_mask', 'F6F2EA', rough=0.35, details=[
    {'type': 'box', 'bounds': [(0.012, 0.06), (None, None), (HEAD['eye_z'] - 0.006, HEAD['eye_z'] + 0.006)], 'rgb': 'D02A2A'},
    {'type': 'box', 'bounds': [(-0.06, -0.012), (None, None), (HEAD['eye_z'] - 0.006, HEAD['eye_z'] + 0.006)], 'rgb': 'D02A2A'},
    {'type': 'box', 'bounds': [(-0.006, 0.006), (None, None), (HEAD['eye_z'] + 0.02, 9)], 'rgb': 'D02A2A'},
]))


# --- phone in the right palm (Reply Guy, phone anims) -----------------------------------------------
hr, hrt = bone_head('hand_r'), bone_tail('hand_r')
pc = hr + (hrt - hr) * 0.6 + Vector((0, 0, -0.03))
bm = bmesh.new()
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(pc) @ Matrix.Diagonal((0.155, 0.075, 0.009, 1)))
bmesh.ops.bevel(bm, geom=[e for e in bm.edges if abs((e.verts[0].co - e.verts[1].co).z) > 0.005], offset=0.008, segments=2, affect='EDGES')
ph = new_obj('tmp.phone', bm)
set_weights(ph, lambda p: {'hand_r': 1.0})
uv_unwrap(ph, method='cube')
finish(ph, 'phone', 'held', 256, proc_material('phone', '1A1A1F', rough=0.3, details=[
    {'type': 'box', 'bounds': [(pc.x - 0.07, pc.x + 0.07), (pc.y - 0.032, pc.y + 0.032), (-9, pc.z - 0.004)], 'rgb': '4FA3FF'},  # lit screen (faces away from the palm)
], emit='4FA3FF'))

# --- megaphone in the left hand (Town Crier) --------------------------------------------------------
hl, hlt = bone_head('hand_l'), bone_tail('hand_l')
grip = hl + (hlt - hl) * 0.5 + Vector((0, 0, -0.03))
bm = bmesh.new()
axis = Vector((0, -1, 0))
bmesh.ops.create_cone(bm, cap_ends=False, segments=16, radius1=0.035, radius2=0.11, depth=0.26,
                      matrix=Matrix.Translation(grip + Vector((0, -0.08, 0.06))) @ axis.to_track_quat('Z', 'Y').to_matrix().to_4x4())
bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.022, radius2=0.022, depth=0.1,
                      matrix=Matrix.Translation(grip + Vector((0, 0.07, 0.06))) @ axis.to_track_quat('Z', 'Y').to_matrix().to_4x4())
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(grip + Vector((0, 0.0, 0.0))) @ Matrix.Diagonal((0.03, 0.04, 0.11, 1)))
mg = new_obj('tmp.megaphone', bm)
sm = mg.modifiers.new('Solidify', 'SOLIDIFY'); sm.thickness = 0.004
bpy.context.view_layer.objects.active = mg
bpy.ops.object.modifier_apply(modifier=sm.name)
set_weights(mg, lambda p: {'hand_l': 1.0})
uv_unwrap(mg, method='cube')
finish(mg, 'megaphone', 'held', 256, proc_material('megaphone', 'F2F2EE', rough=0.4, details=[
    {'type': 'box', 'bounds': [(None, None), (-9, grip.y - 0.17), (None, None)], 'rgb': 'D9342B'},     # red bell rim
    {'type': 'box', 'bounds': [(None, None), (grip.y - 0.03, 9), (None, None)], 'rgb': '2A2A30'},      # handle
]))

# --- binoculars on a strap (Wallet Tracker) ---------------------------------------------------------
sp3 = bone_head('spine_03')
chest_front = [v.co for v in bm_body.verts if abs(v.co.z - (sp3.z - 0.06)) < 0.02 and abs(v.co.x) < 0.06]
cfy = min(p.y for p in chest_front)
bc = Vector((0, cfy, sp3.z - 0.07))
d = outside_offset(bc + Vector((0, 0.05, 0)), Vector((0, -1, 0)), 0.05, 0.02, UNDER_BVH, reach=0.2)
bc = bc + Vector((0, 0.05, 0)) + Vector((0, -1, 0)) * d
bm = bmesh.new()
for sx in (1, -1):
    m = Matrix.Translation(bc + Vector((sx * 0.032, -0.03, 0))) @ Matrix.Rotation(math.radians(90), 4, 'X')
    bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.026, radius2=0.022, depth=0.1, matrix=m)
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(bc + Vector((0, -0.03, 0))) @ Matrix.Diagonal((0.03, 0.05, 0.02, 1)))
segs = 24
prev = None
for i in range(segs + 1):
    a = math.pi * 2 * i / segs
    fr = (math.cos(a) + 1) / 2
    p = Vector((math.sin(a) * (nrx + 0.012), ncy - math.cos(a) * 0.07, (nb.z - 0.03) * (1 - fr * fr) + (bc.z + 0.02) * fr * fr))
    n_ = (p - Vector((0, ncy, p.z))).normalized()
    dd = outside_offset(p - n_ * 0.05, n_, 0.0, 0.005, UNDER_BVH + [bvh_body], reach=0.2)
    p = p - n_ * 0.05 + n_ * max(dd, 0.05)
    ring = [bm.verts.new(p + Vector((0, 0, 0.006))), bm.verts.new(p - Vector((0, 0, 0.006)))]
    if prev:
        bm.faces.new((prev[0], prev[1], ring[1], ring[0]))
    prev = ring
bi = new_obj('tmp.binoculars', bm)
set_weights(bi, lambda p: {'spine_03': 0.85, 'neck_01': 0.15})
uv_unwrap(bi, method='cube')
finish(bi, 'binoculars', 'neck', 256, proc_material('binoculars', '2B2E33', rough=0.5, details=[
    {'type': 'box', 'bounds': [(None, None), (-9, bc.y - 0.075), (None, None)], 'rgb': '6FA8DC'}]))

bm_body.free()
for o in [o for o in bpy.data.objects if o.name.startswith('tmp.')]:
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.wm.save_as_mainfile(filepath=path)
log('saved', path)
