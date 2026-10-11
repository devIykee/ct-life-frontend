"""Stage 2: build wardrobe parts for one base family on its unchanged skeleton.

usage: Blender -b ... -P cast_wardrobe.py -- <fam> [partName ...]
Reads EVID/families/<fam>/base.blend; writes EVID/families/<fam>/wardrobe.blend, maps/<part>.npz and
wardrobe.json (slot, hides, needs, budgets per part).

Garments are derived from the authored Snow/Rain garment and skin surfaces (CC-BY derivatives) or
built procedurally here (CT Life originals, CC0). Poly Haven props are CC0 (see art/LICENSES.md).
"""
import bpy, bmesh, sys, json, math, time
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix, kdtree
from mathutils.bvhtree import BVHTree
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cast_common import *

args = sys.argv[sys.argv.index('--') + 1:]
fam = args[0]
ONLY = set(args[1:])
cfg = FAMILIES[fam]
d = fam_dir(fam)
(d / 'maps').mkdir(exist_ok=True)
meta = json.loads((d / 'meta.json').read_text())
bpy.ops.wm.open_mainfile(filepath=str(d / 'base.blend'), load_ui=False, use_scripts=False)
scene = bpy.context.scene
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rig.data.pose_position = 'REST'
BONES = meta['bones']
P = {o.name[len(f'{fam}_base_'):]: o for o in bpy.data.objects if o.name.startswith(f'{fam}_base_')}
BH = {k: Vector(v) for k, v in meta['bone_heads'].items()}
BT = {k: Vector(v) for k, v in meta['bone_tails'].items()}
SUB = SUB_IDS

# ------------------------------------------------------------------ anatomy landmarks (rest pose)
hco = mesh_arrays(P['head'])
hsub_v = None
eyeL, eyeR = BH['Eye.L'], BH['Eye.R']
EYE_Z = (eyeL.z + eyeR.z) / 2
EYE_Y = (eyeL.y + eyeR.y) / 2
HEAD_TOP = float(hco[:, 2].max())
FACE_FRONT = float(hco[:, 1].min())
NECK_Z = BH['Neck'].z
HEAD_Z = BH['Head'].z
CHEST_Z = BH['Chest'].z
HIPS_Z = BH['Hips'].z
KNEE_Z = BH['Calf.L'].z
ANKLE_Z = BH['Foot.L'].z
lvl = hco[np.abs(hco[:, 2] - (EYE_Z + 0.01)) < 0.01]
HEAD_HALF_W = float(np.abs(lvl[:, 0]).max()) if len(lvl) else 0.08
cranium = hco[hco[:, 2] > EYE_Z + 0.02]
HEAD_BACK = float(cranium[:, 1].max())
HEAD_CX = 0.0
HEAD_CY = (FACE_FRONT + HEAD_BACK) / 2
crown = hco[(hco[:, 2] > EYE_Z) & (np.abs(hco[:, 0]) < HEAD_HALF_W * 0.8)]
SKULL_HALF_W = float(np.percentile(np.abs(hco[(hco[:, 2] > EYE_Z + 0.03)][:, 0]), 98))
S = (HEAD_TOP - EYE_Z) / 0.105   # head size scale relative to a nominal adult
neck_slice = hco[np.abs(hco[:, 2] - (NECK_Z + 0.03)) < 0.01]
NECK_RX = float(np.percentile(np.abs(neck_slice[:, 0]), 95)) if len(neck_slice) else 0.06
NECK_CY = float(np.median(neck_slice[:, 1])) if len(neck_slice) else BH['Neck'].y
NECK_RY = float(np.percentile(np.abs(neck_slice[:, 1] - NECK_CY), 95)) if len(neck_slice) else 0.06
chin_pts = hco[(np.abs(hco[:, 0]) < 0.01) & (hco[:, 1] < EYE_Y - 0.01) & (hco[:, 2] < EYE_Z - 0.04)]
CHIN_Z = float(chin_pts[chin_pts[:, 1] < np.percentile(chin_pts[:, 1], 30)][:, 2].min()) if len(chin_pts) else EYE_Z - 0.11
meta['chin_z'] = CHIN_Z
log('landmarks', dict(EYE_Z=EYE_Z, HEAD_TOP=HEAD_TOP, FACE_FRONT=FACE_FRONT, HEAD_BACK=HEAD_BACK, NECK_Z=NECK_Z,
                      HEAD_HALF_W=HEAD_HALF_W, SKULL_HALF_W=SKULL_HALF_W, CHIN_Z=CHIN_Z, KNEE_Z=KNEE_Z, S=S))

# ------------------------------------------------------------------ face data helpers
def face_data(o):
    me = o.data
    n = len(me.polygons)
    c = np.empty(n * 3, dtype=np.float32); me.polygons.foreach_get('center', c)
    nr = np.empty(n * 3, dtype=np.float32); me.polygons.foreach_get('normal', nr)
    W = weights_matrix(o, BONES)
    dom_v = W.argmax(axis=1)
    dom = np.array([BONES[np.bincount(dom_v[list(p.vertices)]).argmax()] for p in me.polygons])
    return {'c': c.reshape(-1, 3), 'n': nr.reshape(-1, 3), 'sub': get_face_attr(o, 'sub'),
            'region': get_face_attr(o, 'region'), 'dom': dom, 'W': W}

def is_arm(dom):
    return np.array([x.startswith(('UpperArm', 'Shoulder', 'Forearm', 'Hand', 'Thumb', 'Index', 'Middle', 'Ring', 'Pinky')) for x in dom])

def derive(src, name, mask=None, offset=0.0, smooth=0, sub=None, keep_sub=False):
    o = duplicate(src, name)
    o.parent = rig
    for m in o.modifiers:
        if m.type == 'ARMATURE':
            m.object = rig
    if mask is not None:
        keep = set(np.nonzero(mask)[0].tolist())
        keep_faces(o, lambda f: f.index in keep)
    if offset or smooth:
        bm = bmesh.new(); bm.from_mesh(o.data)
        bm.normal_update()
        for _ in range(smooth):
            bmesh.ops.smooth_vert(bm, verts=[v for v in bm.verts if not v.is_boundary], factor=0.5,
                                  use_axis_x=True, use_axis_y=True, use_axis_z=True)
        bm.normal_update()
        if offset:
            for v in bm.verts:
                v.co += v.normal * offset
        bm.to_mesh(o.data); bm.free(); o.data.update()
    if sub is not None and not keep_sub:
        face_attr(o, 'sub', [sub] * len(o.data.polygons))
    if 'region' not in o.data.attributes:
        face_attr(o, 'region', default=0)
    return o

def new_mesh_obj(name, bm, sub):
    me = bpy.data.meshes.new(name)
    if not bm.loops.layers.uv:
        bm.loops.layers.uv.new('AtlasUV')
    bm.normal_update()
    bm.to_mesh(me)
    if me.uv_layers and me.uv_layers[0].name != 'AtlasUV':
        me.uv_layers[0].name = 'AtlasUV'
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    o.parent = rig
    for p in me.polygons:
        p.use_smooth = True
    cur = get_face_attr(o, 'sub') if 'sub' in me.attributes else np.zeros(len(me.polygons), dtype=np.int32)
    cur[cur == 0] = sub   # keep sub ids assigned in the bmesh (e.g. hood rim lining)
    face_attr(o, 'sub', cur)
    face_attr(o, 'region', default=0)
    o.modifiers.new('Game skin', 'ARMATURE').object = rig
    return o

# weight sources: body surfaces
_wsrc = None
def weight_sources():
    global _wsrc
    if _wsrc is None:
        pts, Ws = [], []
        for k in ('top', 'bottom', 'skin', 'legs', 'head', 'shoes'):
            if k in P:
                pts.append(mesh_arrays(P[k])); Ws.append(weights_matrix(P[k], BONES))
        pts = np.concatenate(pts); Ws = np.concatenate(Ws)
        kd = kdtree.KDTree(len(pts))
        for i, c in enumerate(pts):
            kd.insert(c, i)
        kd.balance()
        _wsrc = (kd, Ws)
    return _wsrc

def weights_nearest(o, k=5, mix=None, max_infl=4):
    """mix(co) -> (bone, factor) to blend toward a single bone (e.g. Hips for skirts)."""
    kd, Ws = weight_sources()
    co = mesh_arrays(o)
    W = np.zeros((len(co), len(BONES)), dtype=np.float32)
    for i, c in enumerate(co):
        hits = kd.find_n(c, k)
        ws = np.array([1 / max(dd, 1e-4) ** 2 for _, _, dd in hits])
        for (cc, j, dd), w in zip(hits, ws):
            W[i] += Ws[j] * w
        W[i] /= ws.sum()
        if mix:
            b, f = mix(c)
            if f > 0:
                W[i] *= (1 - f)
                W[i, BONES.index(b)] += f
    set_weights_matrix(o, W, BONES, max_infl)

def rigid(o, bones):
    """bones: {name: weight}"""
    W = np.zeros((len(o.data.vertices), len(BONES)), dtype=np.float32)
    for b, w in bones.items():
        W[:, BONES.index(b)] = w
    set_weights_matrix(o, W, BONES, 4)

def join(objs, name):
    objs = [o for o in objs if o is not None]
    select_only(objs)
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.object
    o.name = name; o.data.name = name
    for m in list(o.modifiers):
        if m.type != 'ARMATURE':
            o.modifiers.remove(m)
    arms = [m for m in o.modifiers if m.type == 'ARMATURE']
    for m in arms[1:]:
        o.modifiers.remove(m)
    if not arms:
        o.modifiers.new('Game skin', 'ARMATURE')
    o.modifiers[0].object = rig
    o.parent = rig
    return o

def cyl_uv(o, faces_mask=None, center=(0, 0), axis_z=True, layer='AtlasUV', scale_v=1.0):
    """Cylindrical UVs around a vertical axis (seam at the back, +Y)."""
    me = o.data
    l = me.uv_layers.get(layer) or me.uv_layers.new(name=layer)
    co = mesh_arrays(o)
    cx, cy = center
    for p in me.polygons:
        if faces_mask is not None and not faces_mask[p.index]:
            continue
        us = []
        for li in p.loop_indices:
            v = co[me.loops[li].vertex_index]
            a = math.atan2(v[0] - cx, -(v[1] - cy))  # 0 at front, +-pi at back
            us.append(a / (2 * math.pi) + 0.5)
        if max(us) - min(us) > 0.5:
            us = [u + 1 if u < 0.5 else u for u in us]
        for li, u in zip(p.loop_indices, us):
            v = co[me.loops[li].vertex_index]
            l.data[li].uv = (u * 0.6, v[2] * scale_v)

def smart_uv(o, angle=60, only_new=False):
    select_only([o])
    me = o.data
    if 'AtlasUV' not in me.uv_layers:
        me.uv_layers.new(name='AtlasUV')
    me.uv_layers.active = me.uv_layers['AtlasUV']
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=0.02, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')

def double_sided(o, inner_sub, inset=0.0012):
    """Append a flipped copy so open cloth/hair shells render from both sides."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    sub_l = bm.faces.layers.int.get('sub')
    geom = bmesh.ops.duplicate(bm, geom=list(bm.faces) + list(bm.edges) + list(bm.verts))
    newf = [g for g in geom['geom'] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.reverse_faces(bm, faces=newf, flip_multires=False)
    for f in newf:
        if sub_l:
            f[sub_l] = inner_sub
    bm.normal_update()
    for v in {v for f in newf for v in f.verts}:
        v.co += v.normal * inset  # normals now point inward: move the back layer inside
    bm.to_mesh(o.data); bm.free(); o.data.update()

# ------------------------------------------------------------------ primitives
def bm_sphere(bm, center, radii, seg=16, rings=10, keep=None):
    geom = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0, calc_uvs=True)
    verts = geom['verts']
    for v in verts:
        v.co = Vector((center[0] + v.co.x * radii[0], center[1] + v.co.y * radii[1], center[2] + v.co.z * radii[2]))
    if keep:
        kill = [f for f in {f for v in verts for f in v.link_faces} if not keep(f.calc_center_median())]
        bmesh.ops.delete(bm, geom=kill, context='FACES')
    return [v for v in verts if v.is_valid]

def bm_tube(bm, rings, seg=16, close_top=False, close_bottom=False):
    """rings: list of (cx, cy, z, rx, ry). Returns ring vertex lists (UVs set later)."""
    vs = []
    for (cx, cy, z, rx, ry) in rings:
        row = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            row.append(bm.verts.new((cx + rx * math.sin(a), cy - ry * math.cos(a), z)))
        vs.append(row)
    for i in range(len(vs) - 1):
        for k in range(seg):
            k2 = (k + 1) % seg
            bm.faces.new([vs[i][k], vs[i + 1][k], vs[i + 1][k2], vs[i][k2]])
    if close_top:
        bm.faces.new(list(reversed(vs[-1])))
    if close_bottom:
        bm.faces.new(vs[0])
    return vs

def bm_box(bm, center, size, rot=None):
    geom = bmesh.ops.create_cube(bm, size=1.0)
    m = Matrix.Diagonal((size[0], size[1], size[2], 1))
    if rot:
        m = rot.to_4x4() @ m
    for v in geom['verts']:
        v.co = (m @ v.co) + Vector(center)
    return geom['verts']

def bm_cyl(bm, p0, p1, r0, r1=None, seg=12, caps=True):
    r1 = r0 if r1 is None else r1
    p0, p1 = Vector(p0), Vector(p1)
    axis = (p1 - p0)
    L = axis.length
    q = Vector((0, 0, 1)).rotation_difference(axis.normalized())
    geom = bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r0, radius2=r1, depth=L)
    for v in geom['verts']:
        v.co = q @ (v.co + Vector((0, 0, L / 2))) + p0
    return geom['verts']

def bm_torus(bm, center, R, r, seg=24, rseg=8, normal=(0, 0, 1), scale=(1, 1, 1), arc=1.0):
    q = Vector((0, 0, 1)).rotation_difference(Vector(normal).normalized())
    rows = []
    n_major = seg if arc >= 1.0 else seg + 1
    for i in range(n_major):
        a = 2 * math.pi * arc * i / seg
        ca, sa = math.cos(a), math.sin(a)
        row = []
        for j in range(rseg):
            b = 2 * math.pi * j / rseg
            x = (R + r * math.cos(b)) * ca * scale[0]
            y = (R + r * math.cos(b)) * sa * scale[1]
            z = r * math.sin(b) * scale[2]
            row.append(bm.verts.new(q @ Vector((x, y, z)) + Vector(center)))
        rows.append(row)
    nm = len(rows) if arc >= 1.0 else len(rows) - 1
    for i in range(nm):
        i2 = (i + 1) % len(rows)
        for j in range(rseg):
            j2 = (j + 1) % rseg
            bm.faces.new([rows[i][j], rows[i2][j], rows[i2][j2], rows[i][j2]])
    return rows

def assign_sub(bm, verts_or_faces, sub):
    l = bm.faces.layers.int.get('sub') or bm.faces.layers.int.new('sub')
    faces = [x for x in verts_or_faces if x.is_valid]
    if faces and isinstance(faces[0], bmesh.types.BMVert):
        faces = {f for v in faces for f in v.link_faces}
    for f in faces:
        f[l] = sub

def prop_obj(name, build, sub_default=SUB['acc_a']):
    """build(bm) creates geometry and assigns 'sub' face ints."""
    bm = bmesh.new()
    bm.faces.layers.int.new('sub')
    bm.loops.layers.uv.new('AtlasUV')
    build(bm)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    o.parent = rig
    o.modifiers.new('Game skin', 'ARMATURE').object = rig
    if 'region' not in me.attributes:
        face_attr(o, 'region', default=0)
    subs = get_face_attr(o, 'sub')
    subs[subs == 0] = sub_default
    face_attr(o, 'sub', subs)
    for p in me.polygons:
        p.use_smooth = True
    return o


def orient_out(bm, faces, axis_xy=None, center=None):
    """Flip faces so normals point away from a vertical axis (axis_xy) or a point (center)."""
    flip = []
    for f in faces:
        if not f.is_valid:
            continue
        c = f.calc_center_median()
        if center is not None:
            r = c - Vector(center)
        else:
            r = Vector((c.x - axis_xy[0], c.y - axis_xy[1], 0))
        if f.normal.dot(r) < 0:
            flip.append(f)
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip, flip_multires=False)
    bm.normal_update()

def push_outside(o, target, margin, zmin=-1e9, zmax=1e9):
    """Move vertices of o that are inside/too close to target's surface out to margin."""
    bm = bmesh.new(); bm.from_mesh(target.data); bvh = BVHTree.FromBMesh(bm); bm.free()
    n = 0
    for v in o.data.vertices:
        if not (zmin < v.co.z < zmax):
            continue
        loc, nrm, idx, dist = bvh.find_nearest(v.co)
        if loc is None or dist > 0.10:
            continue
        h = (v.co - loc).dot(nrm)
        if h < margin:
            v.co = v.co + nrm * (margin - h)
            n += 1
    o.data.update()
    return n


def hem_vertex_mask(o):
    subs = get_face_attr(o, 'sub')
    m = np.zeros(len(o.data.vertices), dtype=bool)
    for p in o.data.polygons:
        if subs[p.index] == SUB['hem']:
            m[list(p.vertices)] = True
    return m

def dress_weights(o, z_top, z_hem, vmask=None, calf=False):
    """Long hems (coats, kaftans, skirts, wrappers): blend Hips -> Thigh.L/R (soft split at the centre
    line) with depth below z_top, and toward the calves below the knee for long garments, so the hem
    follows the legs when walking or sitting instead of hanging from the pelvis."""
    W = weights_matrix(o, BONES)
    co = mesh_arrays(o)
    iH = BONES.index('Hips')
    iT = (BONES.index('Thigh.L'), BONES.index('Thigh.R'))
    iC = (BONES.index('Calf.L'), BONES.index('Calf.R'))
    xs = 1.0 if BH['Thigh.L'].x > 0 else -1.0
    span = max(z_top - z_hem, 0.05)
    n = 0
    for i, c in enumerate(co):
        if c[2] >= z_top or (vmask is not None and not vmask[i]):
            continue
        t = min(1.0, max(0.0, (z_top - c[2]) / span))
        a = 0.8 * min(1.0, t / 0.7) ** 0.8
        u = min(1.0, max(0.0, (c[0] * xs + 0.035) / 0.07)); sL = u * u * (3 - 2 * u)
        cs = 0.0
        if calf:
            cs = 0.6 * min(1.0, max(0.0, (KNEE_Z - c[2]) / max(KNEE_Z - ANKLE_Z, 0.05)))
        new = np.zeros(len(BONES), dtype=np.float32)
        new[iH] = 1 - a
        new[iT[0]] = a * (1 - cs) * sL; new[iT[1]] = a * (1 - cs) * (1 - sL)
        new[iC[0]] = a * cs * sL; new[iC[1]] = a * cs * (1 - sL)
        b = min(1.0, t / 0.15)
        W[i] = (1 - b) * W[i] + b * new
        n += 1
    set_weights_matrix(o, W, BONES, 4)
    return n

# ------------------------------------------------------------------ registry + bake
REG = {}
BUILT = []

def register(o, name, slot, budget, hides=(), needs=(), notes='', license='derivative-ccby', density=None):
    o.name = f'{fam}_w_{name}'; o.data.name = o.name
    o['slot'] = slot; o['budget'] = list(budget)
    for p in o.data.polygons:
        p.use_smooth = True
    for lname in [l.name for l in o.data.uv_layers if l.name not in ('AtlasUV', 'SrcUV')]:
        o.data.uv_layers.remove(o.data.uv_layers[lname])
    pack_slot([o], slot, density=density)
    REG[name] = {'slot': slot, 'hides': list(hides), 'needs': list(needs), 'budget': list(budget),
                 'triangles_full': tri_count(o), 'notes': notes, 'license': license,
                 'subs': sorted({SUB_NAME.get(int(s), str(s)) for s in get_face_attr(o, 'sub')})}
    BUILT.append(o)
    log('part', name, slot, REG[name]['triangles_full'])
    return o

def load_raw(path):
    im = bpy.data.images.load(str(path), check_existing=True)
    im.colorspace_settings.name = 'Non-Color'
    im.alpha_mode = 'CHANNEL_PACKED'
    return im
OLD = {k: load_raw(p) for k, p in cfg['tex'].items()}

def bake_material(images, uv_name):
    mat = bpy.data.materials.new('__bake')
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    outn = nt.nodes.new('ShaderNodeOutputMaterial')
    emis = nt.nodes.new('ShaderNodeEmission')
    nt.links.new(emis.outputs[0], outn.inputs['Surface'])
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = uv_name
    tex = {}
    for k, im in images.items():
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = im
        nt.links.new(uvn.outputs['UV'], t.inputs['Vector']); tex[k] = t
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    a_sub = nt.nodes.new('ShaderNodeAttribute'); a_sub.attribute_type = 'GEOMETRY'; a_sub.attribute_name = 'sub'
    a_reg = nt.nodes.new('ShaderNodeAttribute'); a_reg.attribute_type = 'GEOMETRY'; a_reg.attribute_name = 'region'
    def mad(sock):
        m = nt.nodes.new('ShaderNodeVectorMath'); m.operation = 'MULTIPLY_ADD'
        nt.links.new(sock, m.inputs[0]); m.inputs[1].default_value = (.5, .5, .5); m.inputs[2].default_value = (.5, .5, .5)
        return m.outputs[0]
    srcs = {'pos': mad(geo.outputs['Position']), 'nrm': mad(geo.outputs['Normal'])}
    cid = nt.nodes.new('ShaderNodeCombineXYZ')
    d1 = nt.nodes.new('ShaderNodeMath'); d1.operation = 'DIVIDE'; nt.links.new(a_sub.outputs['Fac'], d1.inputs[0]); d1.inputs[1].default_value = 64
    d2 = nt.nodes.new('ShaderNodeMath'); d2.operation = 'DIVIDE'; nt.links.new(a_reg.outputs['Fac'], d2.inputs[0]); d2.inputs[1].default_value = 8
    nt.links.new(d1.outputs[0], cid.inputs['X']); nt.links.new(d2.outputs[0], cid.inputs['Y']); cid.inputs['Z'].default_value = 1
    srcs['id'] = cid.outputs[0]
    if 'base' in tex:
        srcs['base'] = tex['base'].outputs['Color']
    if 'normal' in tex:
        srcs['normal'] = tex['normal'].outputs['Color']
    if 'ms' in tex:
        c = nt.nodes.new('ShaderNodeCombineXYZ'); sp = nt.nodes.new('ShaderNodeSeparateColor')
        nt.links.new(tex['ms'].outputs['Color'], sp.inputs['Color']); nt.links.new(sp.outputs['Red'], c.inputs['X'])
        nt.links.new(tex['ms'].outputs['Alpha'], c.inputs['Y']); srcs['ms'] = c.outputs[0]
    target = nt.nodes.new('ShaderNodeTexImage')
    return mat, emis, target, srcs

def bake_part(o, ao=True):
    slot = o['slot']
    b = duplicate(o, '__bk')
    b.hide_render = False
    b.modifiers.clear()
    b.data.uv_layers.active = b.data.uv_layers['SlotUV']
    mat, emis, target, srcs = bake_material(OLD, 'AtlasUV')
    b.data.materials.clear(); b.data.materials.append(mat)
    for p in b.data.polygons:
        p.material_index = 0
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'
    scene.render.bake.use_selected_to_active = False
    res = {}
    r0, r1, c0, c1 = slot_px(slot, 2048)
    for kind in ('id', 'base', 'normal', 'ms', 'pos', 'nrm', 'ao'):
        if kind == 'ao' and not ao:
            continue
        im = new_float_image('__bk_' + kind, 2048)
        target.image = im
        for n in mat.node_tree.nodes:
            n.select = False
        target.select = True; mat.node_tree.nodes.active = target
        select_only([b])
        if kind == 'ao':
            scene.cycles.samples = 12
            for oo in scene.objects:
                if oo.type == 'MESH':
                    oo.hide_render = not (oo is b or oo.name.startswith(f'{fam}_base_'))
            bpy.ops.object.bake(type='AO', use_clear=True, margin=8, uv_layer='SlotUV')
        else:
            scene.cycles.samples = 1
            mat.node_tree.links.new(srcs[kind], emis.inputs['Color'])
            bpy.ops.object.bake(type='EMIT', use_clear=True, margin=0 if kind == 'id' else 8, uv_layer='SlotUV')
        res[kind] = image_array(im)[r0:r1, c0:c1].copy()
        bpy.data.images.remove(im)
    bpy.data.objects.remove(b, do_unlink=True)
    bpy.data.materials.remove(mat)
    out = {
        'base': np.clip(res['base'][:, :, :3] * 255, 0, 255).astype(np.uint8),
        'normal': np.clip(res['normal'][:, :, :3] * 255, 0, 255).astype(np.uint8),
        'ms': np.clip(np.dstack([res['ms'][:, :, 0], np.ones_like(res['ms'][:, :, 0]), np.zeros_like(res['ms'][:, :, 0]), res['ms'][:, :, 1]]) * 255, 0, 255).astype(np.uint8),
        'pos': (res['pos'][:, :, :3] * 2 - 1).astype(np.float16),
        'nrm': (res['nrm'][:, :, :3] * 2 - 1).astype(np.float16),
        'sub': np.rint(res['id'][:, :, 0] * 64).astype(np.uint8),
        'region': np.rint(res['id'][:, :, 1] * 8).astype(np.uint8),
    }
    if 'ao' in res:
        out['ao'] = np.clip(res['ao'][:, :, 0] * 255, 0, 255).astype(np.uint8)
    np.savez_compressed(d / 'maps' / f'{o.name[len(fam) + 3:]}.npz', **out)

# ================================================================== GARMENTS
FD = {k: face_data(P[k]) for k in P}
top_fd = FD['top']
top_arm = is_arm(top_fd['dom'])
skin_fd = FD['skin']
R = REGION
F = fam == 'f'

def arm_shell(offset, regions=('forearm', 'upperarm', 'torso'), sub=SUB['sleeve'], cut_elbow=False):
    reg = skin_fd['region']
    m = np.isin(reg, [R[r] for r in regions if r in R])
    if not F:
        m &= reg != R['torso']
    if cut_elbow:
        # short sleeve: upper arm faces closer to the shoulder than the elbow (plus shoulders)
        W = skin_fd['W']
        c = skin_fd['c']
        keep = np.zeros(len(c), dtype=bool)
        for side in ('L', 'R'):
            sh, el = BH[f'UpperArm.{side}'], BH[f'Forearm.{side}']
            ax = el - sh
            t = ((c - np.array(sh)) @ np.array(ax)) / ax.length_squared
            keep |= (t < 0.55) & (np.sign(c[:, 0]) == (1 if side == 'L' else -1))
        m &= keep | (reg == R['torso'])
    o = derive(P['skin'], '__sleeve', m, offset=offset, sub=sub)
    # cuffs at the sleeve end
    fd = face_data(o)
    hand_w = fd['W'][:, [i for i, n in enumerate(BONES) if n.startswith('Hand')]].sum(1) if not cut_elbow else None
    if hand_w is not None:
        cuff = np.zeros(len(o.data.polygons), dtype=bool)
        for p in o.data.polygons:
            if hand_w[list(p.vertices)].max() > 0.15:
                cuff[p.index] = True
        s = get_face_attr(o, 'sub'); s[cuff] = SUB['cuff']; face_attr(o, 'sub', s)
    return o

def collar_band(height=0.035, gap=0.018, flare=0.012, z0=None, rx_add=0.012, sub=SUB['collar'], front_open=True):
    """Band around the neck base; angle 0 = front (-Y). Optional front opening of `gap` metres."""
    z0 = NECK_Z + 0.005 if z0 is None else z0
    bm = bmesh.new(); bm.faces.layers.int.new('sub'); uvl = bm.loops.layers.uv.new('AtlasUV')
    seg = 24
    ga = (gap / max(NECK_RX, 0.03)) if front_open else 0.0
    rows = []
    for i, (dz, add) in enumerate(((0, rx_add), (height * 0.55, rx_add + flare * 0.4), (height, rx_add + flare))):
        row = []
        for k in range(seg + 1):
            a = ga + (2 * math.pi - 2 * ga) * k / seg
            row.append(bm.verts.new((math.sin(a) * (NECK_RX + add), NECK_CY - math.cos(a) * (NECK_RY + add), z0 + dz)))
        rows.append(row)
    for i in range(len(rows) - 1):
        for k in range(seg):
            f = bm.faces.new([rows[i][k], rows[i][k + 1], rows[i + 1][k + 1], rows[i + 1][k]])
            for li, (ii, kk) in zip(f.loops, ((i, k), (i, k + 1), (i + 1, k + 1), (i + 1, k))):
                li[uvl].uv = (kk / seg * 3.0, ii * 0.25)
    if not front_open:
        bmesh.ops.remove_doubles(bm, verts=[r[0] for r in rows] + [r[-1] for r in rows], dist=1e-5)
    o = new_mesh_obj('__collar', bm, sub)
    bm.free()
    weights_nearest(o)
    double_sided(o, SUB['lining'])
    return o

def hem_extend(o, length, flare, sub=SUB['hem'], zcut=None):
    """Extrude the lowest boundary loop of a garment down by `length`, flaring outward."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    bnd = [e for e in bm.edges if e.is_boundary]
    # group boundary edges into loops; pick the lowest loop centred on the body (the torso hem)
    loops, seen = [], set()
    for e in bnd:
        if e.index in seen:
            continue
        stack, comp = [e], []
        seen.add(e.index)
        while stack:
            x = stack.pop(); comp.append(x)
            for v in x.verts:
                for e2 in v.link_edges:
                    if e2.is_boundary and e2.index not in seen:
                        seen.add(e2.index); stack.append(e2)
        loops.append(comp)
    def loop_stats(comp):
        vs = {v for e in comp for v in e.verts}
        xs = [v.co.x for v in vs]; zs = [v.co.z for v in vs]
        return (max(xs) - min(xs), sum(zs) / len(zs), abs(sum(xs) / len(xs)))
    cand = [c for c in loops if loop_stats(c)[0] > 0.18 and loop_stats(c)[2] < 0.05]
    low = min(cand, key=lambda c: loop_stats(c)[1])
    sub_l = bm.faces.layers.int.get('sub')
    uvl = bm.loops.layers.uv.get('AtlasUV')
    new_faces = []
    steps = max(1, int(length / 0.05))
    edges = low
    cx = sum(v.co.x for e in low for v in e.verts) / (2 * len(low))
    cy = sum(v.co.y for e in low for v in e.verts) / (2 * len(low))
    for s in range(steps):
        r = bmesh.ops.extrude_edge_only(bm, edges=edges)
        nv = [g for g in r['geom'] if isinstance(g, bmesh.types.BMVert)]
        for v in nv:
            dx, dy = v.co.x - cx, v.co.y - cy
            dl = math.hypot(dx, dy) or 1
            v.co.z -= length / steps
            v.co.x += dx / dl * flare / steps
            v.co.y += dy / dl * flare / steps
        nf = [g for g in r['geom'] if isinstance(g, bmesh.types.BMFace)]
        new_faces += nf
        edges = [g for g in r['geom'] if isinstance(g, bmesh.types.BMEdge) and g.is_boundary and all(v in nv for v in g.verts)]
    for f in new_faces:
        if sub_l:
            f[sub_l] = sub
    bm.normal_update()
    # consistent winding across torso + hem (radial tests fail on the flared first ring), then make
    # each island face outward by majority vote around the body axis
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.faces.index_update()
    seen = set()
    for f0 in bm.faces:
        if f0.index in seen:
            continue
        isl, stack = [], [f0]
        seen.add(f0.index)
        while stack:
            f = stack.pop(); isl.append(f)
            for e in f.edges:
                for f2 in e.link_faces:
                    if f2.index not in seen:
                        seen.add(f2.index); stack.append(f2)
        vote = 0.0
        for f in isl:
            c_ = f.calc_center_median()
            vote += f.normal.dot(Vector((c_.x - cx, c_.y - cy, 0))) * f.calc_area()
        if vote < 0:
            bmesh.ops.reverse_faces(bm, faces=isl, flip_multires=False)
    bm.normal_update()
    bm.to_mesh(o.data); bm.free(); o.data.update()
    mask = get_face_attr(o, 'sub') == sub
    cyl_uv(o, mask, center=(cx, cy))
    return o

def dickey():
    """Shirt front filling a neckline (from neck/chest skin), for jackets/coats/kaftans."""
    pieces = []
    for k in ('head', 'skin'):
        fd = FD[k]
        m = (fd['c'][:, 2] < NECK_Z + 0.02) & (fd['c'][:, 2] > NECK_Z - 0.30) & (fd['n'][:, 1] < -0.2) & (np.abs(fd['c'][:, 0]) < 0.14)
        if k == 'head':
            m &= fd['sub'] == SUB['face']
        if k == 'skin':
            m &= fd['region'] == R['torso']
        if m.any():
            pieces.append(derive(P[k], '__dickey_' + k, m, offset=0.0025, sub=SUB['dickey']))
    return pieces

_skin_bvh = None
def conform_sleeves(o, arm_offset):
    """Pull the authored short sleeves onto the arm surface (+offset) so long sleeves continue
    without a visible hem ridge."""
    global _skin_bvh
    if _skin_bvh is None:
        bm = bmesh.new(); bm.from_mesh(P['skin'].data); _skin_bvh = BVHTree.FromBMesh(bm); bm.free()
    W = weights_matrix(o, BONES)
    arm_idx = [i for i, n in enumerate(BONES) if n.startswith(('UpperArm', 'Shoulder', 'Forearm'))]
    aw = W[:, arm_idx].sum(1)
    for v in o.data.vertices:
        if aw[v.index] < 0.25:
            continue
        side = 'L' if v.co.x > 0 else 'R'
        sh, el = BH[f'UpperArm.{side}'], BH[f'Forearm.{side}']
        ax = el - sh
        t = (v.co - sh).dot(ax) / ax.length_squared
        f = min(1.0, max(0.0, (t - 0.05) / 0.25)) * min(1.0, aw[v.index] * 1.4)
        if f <= 0:
            continue
        loc, nrm, idx, dist = _skin_bvh.find_nearest(v.co)
        if loc is None:
            continue
        v.co = v.co.lerp(loc + nrm * (arm_offset + 0.0025), f)
    o.data.update()

def torso_shell(offset, smooth=1, sleeves=True, sub=None, conform=None):
    m = np.ones(len(top_fd['c']), dtype=bool) if sleeves else ~top_arm
    if F:
        zt = float(top_fd['c'][:, 2].min()) + 0.03
        m = m & ~((top_fd['n'][:, 2] < -0.45) & (top_fd['c'][:, 2] < zt))
    o = derive(P['top'], '__torso', m, offset=offset, smooth=smooth, sub=sub, keep_sub=sub is None)
    if conform is not None and not F:
        conform_sleeves(o, conform)
    return o

def build_tops():
    hides_long = ['forearm', 'upperarm'] + (['torso'] if F else [])
    # 1. long sleeve tee / sweater
    if want('top_long'):
        t = torso_shell(0.002, 0, conform=0.005)
        register(join([t, arm_shell(0.005)], 'x'), 'top_long', 'TOP', (1900, 950, 450, 180), hides=hides_long,
                 notes='Long sleeves from arm skin surface; torso from authored garment.')
    # 2. short sleeve tee (Rain: tank + shoulders + short sleeves; Snow: base tee)
    if want('top_tee') and F:
        t = torso_shell(0.0015, 0)
        register(join([t, arm_shell(0.004, cut_elbow=True)], 'x'), 'top_tee', 'TOP', (1800, 900, 430, 170),
                 hides=['torso'], notes='Tank + shoulder caps + short sleeves.')
    # 3. hoodie (hood down)
    if want('top_hoodie') or want('top_hoodie_up'):
        for up in (False, True):
            nm = 'top_hoodie_up' if up else 'top_hoodie'
            if not want(nm):
                continue
            t = torso_shell(0.008, 2, conform=0.010)
            s = arm_shell(0.010)
            hood = build_hood(up)
            pocket = None
            parts = [t, s, hood]
            register(join(parts, 'x'), nm, 'TOP', (2200, 1050, 500, 200), hides=hides_long,
                     notes='Hoodie shell; hood ' + ('up (hides hair)' if up else 'down'))
    # 4. collared shirt (short sleeve; untucked)
    if want('top_shirt'):
        if F:
            t = torso_shell(0.003, 1)
            parts = [t, arm_shell(0.006, cut_elbow=True)]
            hides = ['torso']
        else:
            parts = [torso_shell(0.003, 1)]
            hides = []
        o = join(parts, 'x')
        hem_extend(o, 0.05, 0.01)
        register(join([o, collar_band(0.028, 0.02, 0.012, z0=NECK_Z + 0.01, rx_add=0.007)], 'x'), 'top_shirt', 'TOP', (1900, 950, 450, 180), hides=hides,
                 notes='Camp/linen/hawaiian shirt geometry: collar band + untucked hem.')
    # 5. jacket / blazer
    if want('top_jacket'):
        o = join([torso_shell(0.011, 2, conform=0.012), arm_shell(0.012)], 'x')
        hem_extend(o, 0.10, 0.018)
        log('jacket hem pushed', push_outside(o, P['bottom'], 0.012, zmax=HIPS_Z + 0.25))
        register(join([o, collar_band(0.026, 0.035, 0.008, z0=NECK_Z + 0.012, rx_add=0.008)] + dickey(), 'x'), 'top_jacket', 'TOP', (2300, 1100, 520, 210),
                 hides=hides_long, notes='Suit jacket over shirt-front (dickey) with collar band.')
    # 6. long coat (detective / trench)
    if want('top_coat'):
        o = join([torso_shell(0.013, 2, conform=0.014), arm_shell(0.014)], 'x')
        hem_extend(o, max(0.2, (HIPS_Z - KNEE_Z) * 0.95 + 0.06), 0.07)
        log('coat hem pushed', push_outside(o, P['bottom'], 0.014, zmax=HIPS_Z + 0.25))
        _hm = hem_vertex_mask(o)
        _zt = float(top_fd['c'][:, 2].min())
        log('coat hem weights', dress_weights(o, _zt, float(mesh_arrays(o)[_hm][:, 2].min()), _hm))
        register(join([o, collar_band(0.045, 0.035, 0.02, z0=NECK_Z + 0.008, rx_add=0.012)] + dickey(), 'x'), 'top_coat', 'TOP', (2500, 1200, 560, 220),
                 hides=hides_long, notes='Knee-length coat; skirt weights blended toward Hips.')
    # 7. kaftan / senator style tunic
    if want('top_kaftan'):
        o = join([torso_shell(0.010, 2, conform=0.014), arm_shell(0.014)], 'x')
        hem_extend(o, max(0.2, (HIPS_Z - KNEE_Z) * 0.85), 0.03)
        log('kaftan hem pushed', push_outside(o, P['bottom'], 0.012, zmax=HIPS_Z + 0.25))
        _hm = hem_vertex_mask(o)
        _zt = float(top_fd['c'][:, 2].min())
        log('kaftan hem weights', dress_weights(o, _zt, float(mesh_arrays(o)[_hm][:, 2].min()), _hm))
        register(join([o] + dickey(), 'x'), 'top_kaftan', 'TOP', (2400, 1150, 540, 210), hides=hides_long,
                 notes='Senator/kaftan-style long tunic (round neck, embroidery painted).')

def build_hood(up):
    bm = bmesh.new(); bm.faces.layers.int.new('sub'); bm.loops.layers.uv.new('AtlasUV')
    if not up:
        c = (0, NECK_CY + NECK_RY + 0.012, NECK_Z - 0.012)
        def keep(p):
            return p.y > c[1] - 0.006 and p.z < c[2] + 0.055
        bm_sphere(bm, c, (NECK_RX + 0.038, 0.042, 0.075), 16, 10, keep)
        o = new_mesh_obj('__hood', bm, SUB['hood'])
        bm.free()
        smart_uv(o)
        rigid(o, {'Neck': 0.4, 'Chest': 0.6})
        double_sided(o, SUB['hood_in'])
        return o
    return build_hood_up(bm)

def build_hood_up(bm):
    """Fabric hood worn up: roomy crown with a soft peak and centre seam, an oval opening framing the
    face with a rolled rim, folds on the back/sides and a drape that settles onto the shoulders."""
    sub_l = bm.faces.layers.int['sub']
    top = HEAD_TOP + 0.028 * S
    hd = (HEAD_BACK - FACE_FRONT) / 2
    side = max(SKULL_HALF_W + 0.03, HEAD_HALF_W + 0.014)
    z_bot = NECK_Z - 0.075
    SEG, ROWS = 36, 18
    rng = np.random.default_rng(11)
    phase = rng.uniform(0, 6.28, 3)
    grid = []
    for i in range(ROWS + 1):
        t = i / ROWS                       # 0 = drape bottom, 1 = crown
        z = z_bot + (top - z_bot) * t
        if z > EYE_Z:                      # crown: ellipsoid cap over the skull
            u = (z - EYE_Z) / (top + 0.014 - EYE_Z)
            k = math.sqrt(max(0.0, 1 - u ** 2.2))
            rx, ry = side * k, (hd + 0.03) * k
            cy = HEAD_CY + 0.012 - 0.01 * u
        elif z > NECK_Z + 0.02:            # around the face / ears
            rx, ry = side, hd + 0.03
            cy = HEAD_CY + 0.014
        else:                              # drape onto the neck/shoulders
            u = (NECK_Z + 0.02 - z) / (NECK_Z + 0.02 - z_bot)
            rx = side + (NECK_RX + 0.10 - side) * u ** 1.2
            ry = hd + 0.03 + (NECK_RY + 0.09 - hd - 0.03) * u ** 1.2
            cy = HEAD_CY + 0.014 + (NECK_CY + 0.012 - HEAD_CY - 0.014) * u
        row = []
        for k_ in range(SEG):
            a = 2 * math.pi * k_ / SEG           # 0 = front (-Y)
            back = 0.5 - 0.5 * math.cos(a)       # 0 front .. 1 back
            fold_w = (0.25 + 0.75 * back) * (1.0 if z < EYE_Z else 0.4)
            fold = 0.035 * fold_w * (0.6 * math.sin(5 * a + phase[0] + 4 * t) + 0.4 * math.sin(9 * a + phase[1]))
            r = 1 + fold
            x = math.sin(a) * rx * r
            y = cy - math.cos(a) * ry * r
            zz = z
            if z > EYE_Z:                        # centre seam ridge and a soft peak at the front-top
                x_seam = abs(math.sin(a)) * rx
                if x_seam < 0.012:
                    zz += 0.004 * (1 - x_seam / 0.012)
            row.append(bm.verts.new((x, y, zz)))
        grid.append(row)
    capv = bm.verts.new((0, HEAD_CY + 0.004, top + 0.004))
    faces = []
    for i in range(ROWS):
        for k_ in range(SEG):
            k2 = (k_ + 1) % SEG
            faces.append(bm.faces.new([grid[i + 1][k_], grid[i][k_], grid[i][k2], grid[i + 1][k2]]))
    for k_ in range(SEG):
        faces.append(bm.faces.new([capv, grid[ROWS][k_], grid[ROWS][(k_ + 1) % SEG]]))
    bm.normal_update()
    orient_out(bm, faces, center=(0, HEAD_CY, (top + NECK_Z) / 2))
    # face opening: oval in front, framed from the forehead to below the chin
    oz_top, oz_bot = EYE_Z + 0.062 * S, CHIN_Z - 0.018
    oc = (oz_top + oz_bot) / 2
    oh = (oz_top - oz_bot) / 2
    ow = SKULL_HALF_W * 0.98
    kill = []
    for f in bm.faces:
        c = f.calc_center_median()
        if c.y < HEAD_CY - 0.02 and (c.x / ow) ** 2 + ((c.z - oc) / oh) ** 2 < 1.0:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    # smooth the opening rim, then roll it inward (visible lining thickness)
    rim = [e for e in bm.edges if e.is_boundary and min(v.co.z for v in e.verts) > z_bot + 0.01]
    rim_v = {v for e in rim for v in e.verts}
    for _ in range(4):
        new = {}
        for v in rim_v:
            nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(nb) == 2:
                new[v] = v.co * 0.5 + (nb[0].co + nb[1].co) * 0.25
        for v, co in new.items():
            v.co = co
    r = bmesh.ops.extrude_edge_only(bm, edges=rim)
    nv = [g for g in r['geom'] if isinstance(g, bmesh.types.BMVert)]
    for v in nv:
        d = Vector((v.co.x * 0.15, v.co.y - (HEAD_CY + 0.01), 0)).normalized()
        v.co += -d * 0.010 + Vector((0, 0.006, 0))
    for g in r['geom']:
        if isinstance(g, bmesh.types.BMFace):
            g[sub_l] = SUB['hood_in']
    bm.normal_update()
    o = new_mesh_obj('__hood', bm, SUB['hood'])
    bm.free()
    smart_uv(o)
    # stay clear of head (ears) and the hoodie torso under the drape
    push_outside(o, P['head'], 0.010)
    push_outside(o, P['top'], 0.016, zmax=NECK_Z + 0.02)
    # weights: Head over the crown, Neck below, torso weights in the drape
    co = mesh_arrays(o)
    Wh = np.zeros((len(co), len(BONES)), dtype=np.float32)
    t = np.clip((co[:, 2] - NECK_Z) / max(EYE_Z - NECK_Z, 0.05), 0, 1)
    Wh[:, BONES.index('Head')] = t
    Wh[:, BONES.index('Neck')] = 1 - t
    kd, Ws = weight_sources()
    a_ = np.clip((co[:, 2] - (NECK_Z - 0.07)) / 0.07, 0, 1)
    W = np.zeros_like(Wh)
    for i, c in enumerate(co):
        if a_[i] >= 1:
            W[i] = Wh[i]
            continue
        hits = kd.find_n(c, 5)
        ws = np.array([1 / max(dd, 1e-4) ** 2 for _, _, dd in hits])
        wn = sum(Ws[j] * w for (_, j, _), w in zip(hits, ws)) / ws.sum()
        W[i] = a_[i] * Wh[i] + (1 - a_[i]) * wn
    set_weights_matrix(o, W, BONES, 4)
    double_sided(o, SUB['hood_in'], inset=0.006)
    return o

def build_outer():
    if want('outer_vest'):
        m = ~top_arm
        t = derive(P['top'], '__vest', m, offset=0.018 if not F else 0.016, smooth=3, sub=SUB['new'])
        o = join([t, collar_band(0.034, 0.004, 0.005, rx_add=0.016, sub=SUB['collar'])], 'x')
        register(o, 'outer_vest', 'OUTER', (1000, 450, 220, 90), notes='Fleece/puffer/tactical vest shell over any top.')

def build_bottoms():
    bfd = FD['bottom']
    if want('bottom_shorts'):
        o = derive(P['bottom'], '__shorts')
        bm = bmesh.new(); bm.from_mesh(o.data)
        zc = KNEE_Z + 0.07
        bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces), dist=1e-5,
                               plane_co=(0, 0, zc), plane_no=(0, 0, 1), clear_inner=True)
        bm.normal_update()
        for v in bm.verts:
            if v.co.z < zc + 0.08:
                f = 1 - (v.co.z - zc) / 0.08
                v.co += v.normal * 0.007 * f
        bm.to_mesh(o.data); bm.free(); o.data.update()
        register(o, 'bottom_shorts', 'BOTTOM', (1100, 520, 260, 100), needs=['legs'], notes='Shorts cut from authored trousers above the knee.')
    if want('bottom_skirt') or want('bottom_wrap'):
        for nm, hem_z, flare, famp in (('bottom_skirt', KNEE_Z + 0.06, 0.09, 0.07), ('bottom_wrap', ANKLE_Z + 0.09, 0.04, 0.035)):
            if not want(nm):
                continue
            pts = mesh_arrays(P['bottom'])
            ztop = pts[:, 2].max() - 0.015
            prof = []
            n = 12
            maxr = [0, 0]
            for i in range(n):
                z = ztop + (hem_z - ztop) * i / (n - 1)
                s_ = pts[np.abs(pts[:, 2] - z) < 0.025]
                if len(s_) < 8:
                    s_ = pts[np.argsort(np.abs(pts[:, 2] - z))[:60]]
                cy = float((s_[:, 1].min() + s_[:, 1].max()) / 2)
                rx = float(np.abs(s_[:, 0]).max()) + 0.012
                ry = float((s_[:, 1].max() - s_[:, 1].min()) / 2) + 0.014
                maxr = [max(maxr[0], rx), max(maxr[1], ry)]
                t = i / (n - 1)
                prof.append((cy, z, maxr[0] + flare * t ** 1.6, maxr[1] + flare * 0.7 * t ** 1.6, t))
            rng = np.random.default_rng(5 if nm == 'bottom_skirt' else 9)
            ph = rng.uniform(0, 6.28, 3)
            SEG = 40
            bm = bmesh.new(); sub_l = bm.faces.layers.int.new('sub'); bm.loops.layers.uv.new('AtlasUV')
            rows = []
            for cy, z, rx, ry, t in prof:
                amp = famp * t ** 1.3
                row = []
                for k_ in range(SEG):
                    a = 2 * math.pi * k_ / SEG
                    fold = 0.65 * math.sin(9 * a + ph[0]) + 0.35 * math.sin(14 * a + ph[1] + 2 * t)
                    r = 1 + amp * fold
                    zz = z + 0.010 * t * t * math.sin(5 * a + ph[2])
                    row.append(bm.verts.new((math.sin(a) * rx * r, cy - math.cos(a) * ry * r, zz)))
                rows.append(row)
            body = []
            for i in range(len(rows) - 1):
                for k_ in range(SEG):
                    k2 = (k_ + 1) % SEG
                    body.append(bm.faces.new([rows[i][k_], rows[i + 1][k_], rows[i + 1][k2], rows[i][k2]]))
            # hem thickness: fold the hem back up inside by 2.5 cm, 6 mm in
            last = rows[-1]
            cyl = prof[-1][0]
            inner = []
            for v in last:
                d = Vector((v.co.x, v.co.y - cyl, 0)).normalized()
                inner.append(bm.verts.new(v.co - d * 0.011 + Vector((0, 0, 0.022))))
            lining = []
            for k_ in range(SEG):
                k2 = (k_ + 1) % SEG
                lining.append(bm.faces.new([last[k_], inner[k_], inner[k2], last[k2]]))
            bm.normal_update()
            orient_out(bm, body, axis_xy=(0, cyl))
            # fold-back faces point inward (seen from below / inside)
            flip = [f for f in lining if f.normal.dot(Vector((f.calc_center_median().x, f.calc_center_median().y - cyl, 0))) > 0]
            if flip:
                bmesh.ops.reverse_faces(bm, faces=flip, flip_multires=False)
            for f in body:
                f[sub_l] = SUB['skirt']
            for f in lining:
                f[sub_l] = SUB['lining']
            o = new_mesh_obj('__skirt', bm, SUB['skirt'])
            subs = [SUB['skirt']] * len(body) + [SUB['lining']] * len(lining)
            bm.free()
            face_attr(o, 'sub', subs)
            cyl_uv(o, center=(0, prof[0][0]))
            push_outside(o, P['bottom'], 0.008)
            weights_nearest(o)
            log(nm, 'hem weights', dress_weights(o, HIPS_Z - 0.02, hem_z, calf=(nm == 'bottom_wrap')))
            register(o, nm, 'BOTTOM', (1000, 520, 250, 90), needs=['legs'],
                     notes='Procedural skirt/wrapper with folds, flare and a turned-back hem (CT Life original).', license='cc0-ctlife')

def build_shoes():
    if want('shoes_boots'):
        pts = mesh_arrays(P['bottom'])
        sco = mesh_arrays(P['shoes'])
        pieces = [derive(P['shoes'], '__bootfoot', keep_sub=True)]
        for side, sg in (('L', 1), ('R', -1)):
            sp = sco[sco[:, 0] * sg > 0]
            z0 = float(np.percentile(sp[:, 2], 92)) - 0.02
            z1 = z0 + 0.20 * S
            rings = []
            for i in range(6):
                z = z0 + (z1 - z0) * i / 5
                s = pts[(pts[:, 0] * sg > 0) & (np.abs(pts[:, 2] - z) < 0.02)]
                if len(s) < 6:
                    s = pts[(pts[:, 0] * sg > 0)][np.argsort(np.abs(pts[(pts[:, 0] * sg > 0)][:, 2] - z))[:40]]
                cx, cy = float(np.median(s[:, 0])), float(np.median(s[:, 1]))
                rx = float(np.percentile(np.abs(s[:, 0] - cx), 95)) + 0.009
                ry = float(np.percentile(np.abs(s[:, 1] - cy), 95)) + 0.009
                rings.append((cx, cy, z, rx, ry))
            # top rim fold
            cx, cy, z, rx, ry = rings[-1]
            rings.append((cx, cy, z + 0.004, rx - 0.004, ry - 0.004))
            bm = bmesh.new(); bm.faces.layers.int.new('sub'); bm.loops.layers.uv.new('AtlasUV')
            bm_tube(bm, rings, seg=14)
            sh = new_mesh_obj('__shaft' + side, bm, SUB['shaft']); bm.free()
            cyl_uv(sh, center=(rings[0][0], rings[0][1]))
            weights_nearest(sh)
            pieces.append(sh)
        register(join(pieces, 'x'), 'shoes_boots', 'SHOES', (900, 400, 180, 80), notes='Authored shoe + boot shaft.')

# ------------------------------------------------------------------ hair & beards
def scalp_mask(fd, lo=0.025, back_lo=-0.06):
    c, n = fd['c'], fd['n']
    front = c[:, 1] < EYE_Y + 0.01
    hairline = np.where(front, EYE_Z + lo + 0.035 * S, EYE_Z + back_lo * S)
    ears = (np.abs(c[:, 0]) > SKULL_HALF_W * 0.9) & (c[:, 2] < EYE_Z + 0.03)
    temple = (np.abs(c[:, 0]) > SKULL_HALF_W * 0.75) & (c[:, 2] < EYE_Z + 0.025) & (c[:, 1] < EYE_Y + 0.03)
    return (fd['sub'] == SUB['face']) & (c[:, 2] > hairline) & ~ears & ~temple & (n[:, 2] > -0.5)

def build_hair():
    hfd = FD['head']
    if want('hair_buzz') or want('hair_locs'):
        sc = derive(P['head'], '__buzz', scalp_mask(hfd), offset=0.0018, sub=SUB['scalp'])
        if want('hair_buzz'):
            register(duplicate(sc, '__buzz2'), 'hair_buzz', 'HAIR', (700, 330, 160, 70),
                     notes='Close crop/buzz/cornrow base: scalp shell from the authored head.')
        if want('hair_locs'):
            bm = bmesh.new(); bm.faces.layers.int.new('sub'); bm.loops.layers.uv.new('AtlasUV')
            rng = np.random.default_rng(7)
            n = 18
            for i in range(n):
                a = -math.pi * 0.95 + 1.9 * math.pi * (i + 0.5) / n
                ring = 0.85 + 0.1 * (i % 2)
                x0 = math.sin(a) * SKULL_HALF_W * ring
                y0 = HEAD_CY - math.cos(a) * (HEAD_BACK - FACE_FRONT) / 2 * ring
                if y0 < FACE_FRONT + 0.05 and abs(x0) < SKULL_HALF_W * 0.5:
                    continue  # keep the face clear
                z0 = HEAD_TOP - 0.03 * S
                L = (0.20 + 0.06 * rng.random()) * S
                dirv = Vector((x0 * 0.6, (y0 - HEAD_CY) * 0.4 + 0.02, -1)).normalized()
                p1 = Vector((x0 * 1.25, y0 + (0.02 if y0 > HEAD_CY else -0.01), z0 - 0.05 * S))
                p2 = p1 + dirv * L
                vs = bm_cyl(bm, p1, p2, 0.011 * S, 0.008 * S, seg=6, caps=False)
                assign_sub(bm, vs, SUB['loc'])
            lo = new_mesh_obj('__locs', bm, SUB['loc']); bm.free()
            smart_uv(lo)
            co = mesh_arrays(lo)
            W = np.zeros((len(co), len(BONES)), dtype=np.float32)
            t = np.clip((HEAD_TOP - 0.06 - co[:, 2]) / 0.2, 0, 1)
            W[:, BONES.index('Head')] = 1 - 0.5 * t
            W[:, BONES.index('Neck')] = 0.5 * t
            set_weights_matrix(lo, W, BONES, 4)
            register(join([derive(sc, '__buzz3'), lo], 'x'), 'hair_locs', 'HAIR', (1500, 700, 330, 130),
                     notes='Shoulder-length locs (CT Life procedural) over scalp shell.', license='mixed')
        bpy.data.objects.remove(sc, do_unlink=True)
    if want('hair_afro'):
        bm = bmesh.new(); bm.faces.layers.int.new('sub'); bm.loops.layers.uv.new('AtlasUV')
        bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1.0, calc_uvs=False)
        # Hair volume = scalp surface offset by a thickness field that is 0 at the hairline and grows
        # to a rounded puff (thicker on top/back), broken up by clumps and coils.
        c = Vector((0, HEAD_CY + 0.004, EYE_Z + 0.012 * S))
        hb = bmesh.new(); hb.from_mesh(P['head'].data); bvh = BVHTree.FromBMesh(hb); hb.free()
        rng = np.random.default_rng(3)
        nb = 110
        bd = rng.normal(size=(nb, 3)); bd /= np.linalg.norm(bd, axis=1, keepdims=True)
        bw = rng.uniform(0.14, 0.26, nb)
        ba = rng.uniform(0.006, 0.016, nb) * S
        def hairline(x, y):
            a = abs(math.atan2(x, -(y - HEAD_CY)))           # 0 front .. pi back
            pts_ = [(0.0, EYE_Z + 0.050 * S), (0.55, EYE_Z + 0.044 * S), (0.95, EYE_Z + 0.024 * S),
                    (1.45, EYE_Z + 0.030 * S), (1.85, EYE_Z + 0.010 * S), (2.5, EYE_Z - 0.045 * S), (math.pi, EYE_Z - 0.075 * S)]
            for (a0, z0), (a1, z1) in zip(pts_, pts_[1:]):
                if a <= a1:
                    u = (a - a0) / (a1 - a0)
                    return z0 + (z1 - z0) * (u * u * (3 - 2 * u))
            return pts_[-1][1]
        kill_v = []
        for v in bm.verts:
            d = v.co.normalized()
            hit, nrm, idx, dist = bvh.ray_cast(c, d, 0.4)
            if hit is None:
                loc, nrm, idx, dist = bvh.find_nearest(c + d * 0.12)
                hit = loc
            h = hairline(hit.x, hit.y)
            if hit.z < h - 0.006:
                kill_v.append(v)
                v.co = hit
                continue
            back = max(0.0, d.y)
            front = max(0.0, -d.y)
            T = (0.030 + 0.030 * max(0.0, d.z) + 0.012 * back) * S * (1 - 0.35 * front)
            ramp = min(1.0, max(0.0, (hit.z - h) / ((0.05 + 0.08 * front) * S))) ** (1 + 0.6 * front)
            ramp = ramp * ramp * (3 - 2 * ramp)
            dn = np.array(d)
            bump = float((ba * np.exp(-((1 - np.clip(bd @ dn, -1, 1)) / (bw ** 2 / 2)))).sum())
            bump = min(bump, 0.022 * S)
            coil = 0.0025 * S * math.sin(41 * d.x + 3) * math.sin(37 * d.y + 1) * math.sin(43 * d.z + 2)
            v.co = hit + d * (0.004 + ramp * (T + bump + coil))
        kill = list({f for v in kill_v for f in v.link_faces})
        bmesh.ops.delete(bm, geom=kill, context='FACES')
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
        for v in bm.verts:   # final clearance from the head surface
            loc, nrm, idx, dist = bvh.find_nearest(v.co)
            if loc is not None and (v.co - loc).dot(nrm) < 0.003:
                v.co = loc + nrm * 0.004
        bm.normal_update()
        orient_out(bm, list(bm.faces), center=tuple(c))
        o = new_mesh_obj('__afro', bm, SUB['afro']); bm.free()
        smart_uv(o)
        rigid(o, {'Head': 1.0})
        sc2 = derive(P['head'], '__afroscalp', scalp_mask(hfd, lo=0.0), offset=0.0015, sub=SUB['scalp'])
        register(join([o, sc2], 'x'), 'hair_afro', 'HAIR', (1500, 700, 330, 130),
                 notes='Afro with clumped silhouette, tapered hairline and coil texture (CT Life procedural) + scalp shell.', license='mixed')
    if want('hair_long') and F:
        hair = P['hair']
        m = FD['hair']['sub'] != SUB['ponytail']
        top = derive(hair, '__longtop', m, keep_sub=True)
        bm = bmesh.new(); bm.faces.layers.int.new('sub'); bm.loops.layers.uv.new('AtlasUV')
        rows = []
        n_r, seg = 7, 12
        z0 = EYE_Z + 0.02 * S
        z1 = CHEST_Z - 0.02
        for i in range(n_r):
            t = i / (n_r - 1)
            z = z0 + (z1 - z0) * t
            row = []
            for k in range(seg + 1):
                a = -math.pi * 0.62 + 1.24 * math.pi * k / seg
                rx = SKULL_HALF_W + 0.012 + 0.05 * t
                ry = (HEAD_BACK - HEAD_CY) + 0.01 + 0.03 * t
                y = HEAD_CY + 0.01 + math.cos(a) * ry + 0.04 * t * t
                row.append(bm.verts.new((math.sin(a) * rx, y, z)))
            rows.append(row)
        for i in range(n_r - 1):
            for k in range(seg):
                bm.faces.new([rows[i][k], rows[i][k + 1], rows[i + 1][k + 1], rows[i + 1][k]])
        cur = new_mesh_obj('__curtain', bm, SUB['hair']); bm.free()
        smart_uv(cur)
        co = mesh_arrays(cur)
        W = np.zeros((len(co), len(BONES)), dtype=np.float32)
        t = np.clip((z0 - co[:, 2]) / max(z0 - z1, 0.1), 0, 1)
        W[:, BONES.index('Head')] = np.clip(1 - 1.6 * t, 0, 1)
        W[:, BONES.index('Neck')] = np.clip(1 - np.abs(t - 0.55) * 2.2, 0, 1)
        W[:, BONES.index('Chest')] = np.clip(1.6 * t - 0.6, 0, 1)
        W[W.sum(1) == 0, BONES.index('Neck')] = 1
        set_weights_matrix(cur, W, BONES, 4)
        double_sided(cur, SUB['hair'])
        register(join([top, cur], 'x'), 'hair_long', 'HAIR', (1600, 760, 350, 140), notes='Authored top hair + long back curtain (procedural).', license='mixed')

def build_beards():
    if F:
        return
    hfd = FD['head']
    c, n = hfd['c'], hfd['n']
    lip = (np.abs(c[:, 0]) < 0.028 * S) & (np.abs(c[:, 2] - (EYE_Z - 0.075 * S)) < 0.009 * S) & (c[:, 1] < EYE_Y)
    nose_bottom = EYE_Z - 0.052 * S
    base = (hfd['sub'] == SUB['face']) & (c[:, 2] < nose_bottom) & (c[:, 2] > CHIN_Z - 0.035 * S) & ~lip
    base &= (c[:, 1] < HEAD_CY + 0.01) & (np.abs(c[:, 0]) < SKULL_HALF_W * 0.97)
    under = (c[:, 2] < CHIN_Z + 0.005) & (n[:, 2] < -0.3) & (c[:, 1] > EYE_Y + 0.03)
    base &= ~under | (c[:, 1] < EYE_Y + 0.03)
    if want('beard_full'):
        o = derive(P['head'], '__beard', base, offset=0.004, sub=SUB['beard'])
        bm = bmesh.new(); bm.from_mesh(o.data); bm.normal_update()
        for v in bm.verts:
            chin_f = max(0.0, 1 - abs(v.co.x) / (0.05 * S)) * max(0.0, 1 - (v.co.z - CHIN_Z) / (0.04 * S))
            v.co += v.normal * 0.005 * chin_f
        bm.to_mesh(o.data); bm.free()
        register(o, 'beard_full', 'BEARD', (500, 220, 100, 40), notes='Full beard shell from the authored jaw.')
    if want('beard_goatee'):
        m = base & (np.abs(c[:, 0]) < 0.03 * S)
        o = derive(P['head'], '__goatee', m, offset=0.004, sub=SUB['beard'])
        register(o, 'beard_goatee', 'BEARD', (260, 130, 60, 24), notes='Goatee + moustache shell.')

# ------------------------------------------------------------------ accessories (CT Life originals, CC0)
HB = BH['Head']
def head_rigid(o):
    rigid(o, {'Head': 1.0})

def build_accessories():
    hw = SKULL_HALF_W
    hd = (HEAD_BACK - FACE_FRONT) / 2
    brim_z = EYE_Z + 0.040 * S
    def hat(name, build, slot='ACC_A', budget=(600, 300, 150, 60), notes=''):
        if not want(name):
            return
        o = prop_obj('__' + name, build)
        smart_uv(o)
        head_rigid(o)
        register(o, name, slot, budget, notes=notes, license='cc0-ctlife')

    def hard_hat(bm):
        dome = bm_sphere(bm, (0, HEAD_CY, brim_z), (hw + 0.022, hd + 0.022, 0.135 * S), 20, 12, lambda p: p.z >= brim_z - 1e-4)
        assign_sub(bm, dome, SUB['acc_a'])
        rows = bm_torus(bm, (0, HEAD_CY - 0.01, brim_z), 1.0, 0.006, 24, 6, scale=(hw + 0.045, hd + 0.05, 1))
        assign_sub(bm, [v for r in rows for v in r], SUB['acc_a'])
        ridge = bm_box(bm, (0, HEAD_CY, brim_z + 0.118 * S), (0.022, hd * 1.7, 0.03))
        assign_sub(bm, ridge, SUB['acc_b'])
    hat('hat_hardhat', hard_hat, notes='ORE miner hard hat.')

    def straw(bm):
        cr = bm_tube(bm, [(0, HEAD_CY, brim_z - 0.01, hw + 0.012, hd + 0.012), (0, HEAD_CY, brim_z + 0.06, hw + 0.004, hd + 0.004),
                          (0, HEAD_CY, brim_z + 0.095, hw - 0.01, hd - 0.012)], seg=20, close_top=True)
        assign_sub(bm, [v for r in cr for v in r], SUB['acc_a'])
        band = bm_tube(bm, [(0, HEAD_CY, brim_z + 0.0, hw + 0.016, hd + 0.016), (0, HEAD_CY, brim_z + 0.022, hw + 0.012, hd + 0.012)], seg=20)
        assign_sub(bm, [v for r in band for v in r], SUB['acc_b'])
        rows = []
        for ri, (r, dz) in enumerate(((1.0, 0.0), (1.9, -0.012), (2.5, -0.035))):
            rows.append([bm.verts.new((math.sin(2 * math.pi * k / 24) * (hw + 0.012) * r, HEAD_CY - math.cos(2 * math.pi * k / 24) * (hd + 0.012) * r, brim_z - 0.008 + dz)) for k in range(24)])
        for i in range(2):
            for k in range(24):
                k2 = (k + 1) % 24
                f = bm.faces.new([rows[i][k], rows[i][k2], rows[i + 1][k2], rows[i + 1][k]])
        assign_sub(bm, [v for r in rows for v in r], SUB['acc_a'])
    hat('hat_straw', straw, notes='Farmer straw hat.')

    def beanie_geo(bm, sub=SUB['acc_a'], low=0.0):
        dome = bm_sphere(bm, (0, HEAD_CY + 0.004, brim_z - low), (hw + 0.016, hd + 0.016, 0.12 * S), 20, 12, lambda p: p.z >= brim_z - low - 1e-4)
        assign_sub(bm, dome, sub)
        cuff = bm_tube(bm, [(0, HEAD_CY + 0.004, brim_z - low - 0.004, hw + 0.022, hd + 0.022), (0, HEAD_CY + 0.004, brim_z - low + 0.04, hw + 0.02, hd + 0.02)], seg=20)
        assign_sub(bm, [v for r in cuff for v in r], SUB['acc_b'])
    def siren(bm):
        beanie_geo(bm)
        top = brim_z + 0.115 * S
        base = bm_cyl(bm, (0, HEAD_CY, top - 0.01), (0, HEAD_CY, top + 0.025), 0.034, 0.032, seg=14)
        assign_sub(bm, base, SUB['acc_dark'])
        dome = bm_sphere(bm, (0, HEAD_CY, top + 0.025), (0.03, 0.03, 0.045), 14, 8, lambda p: p.z >= top + 0.024)
        l = bm.faces.layers.int['sub']
        for f in {f for v in dome for f in v.link_faces}:
            f[l] = SUB['acc_glow'] if f.calc_center_median().x >= 0 else SUB['acc_glow2']
    hat('hat_siren', siren, notes='Town Crier beanie with a siren light (emissive).')
    hat('hat_beanie', lambda bm: beanie_geo(bm), notes='Knit beanie.')

    def cap(bm):
        dome = bm_sphere(bm, (0, HEAD_CY + 0.006, brim_z - 0.012), (hw + 0.012, hd + 0.012, 0.088 * S), 20, 12, lambda p: p.z >= brim_z - 0.0121)
        assign_sub(bm, dome, SUB['acc_a'])
        rows = []
        for j, t in enumerate((0.0, 0.5, 1.0)):
            row = []
            for k in range(9):
                a = -0.55 * math.pi + 1.1 * math.pi * k / 8
                rr = 1 + 0.75 * t * math.cos(a * 0.9)
                x = math.sin(a) * (hw + 0.013) * (1 + 0.15 * t)
                y = HEAD_CY - math.cos(a) * (hd + 0.013) * rr
                row.append(bm.verts.new((x, y, brim_z - 0.012 - 0.010 * t - 0.004 * abs(math.sin(a)))))
            rows.append(row)
        for i in range(2):
            for k in range(8):
                bm.faces.new([rows[i][k], rows[i][k + 1], rows[i + 1][k + 1], rows[i + 1][k]])
        assign_sub(bm, [v for r in rows for v in r], SUB['acc_b'])
        btn = bm_sphere(bm, (0, HEAD_CY + 0.006, brim_z - 0.012 + 0.088 * S), (0.008, 0.008, 0.005), 8, 4)
        assign_sub(bm, btn, SUB['acc_b'])
    hat('hat_cap', cap, notes='Snapback cap (front print per look).')

    def bucket(bm):
        cr = bm_tube(bm, [(0, HEAD_CY, brim_z - 0.01, hw + 0.016, hd + 0.016), (0, HEAD_CY, brim_z + 0.085, hw - 0.002, hd - 0.004)], seg=20, close_top=True)
        assign_sub(bm, [v for r in cr for v in r], SUB['acc_a'])
        br = bm_tube(bm, [(0, HEAD_CY, brim_z - 0.045, hw + 0.075, hd + 0.075), (0, HEAD_CY, brim_z - 0.008, hw + 0.016, hd + 0.016)], seg=20)
        assign_sub(bm, [v for r in br for v in r], SUB['acc_b'])
    hat('hat_bucket', bucket, notes='Tourist bucket hat.')

    def fila(bm):
        cr = bm_tube(bm, [(0, HEAD_CY, brim_z - 0.012, hw + 0.014, hd + 0.014), (0.004, HEAD_CY, brim_z + 0.06, hw + 0.018, hd + 0.016),
                          (0.012, HEAD_CY, brim_z + 0.09, hw + 0.012, hd + 0.01)], seg=20, close_top=True)
        assign_sub(bm, [v for r in cr for v in r], SUB['acc_a'])
        for v in cr[-1]:
            v.co.z -= 0.035 * max(0.0, v.co.x / (hw + 0.02))  # soft fold to one side
    hat('hat_fila', fila, notes='Fila-style soft cap (Yoruba menswear reference).')

    def gele(bm):
        sub_l = bm.faces.layers.int['sub']
        seg, nr = 28, 6
        rows = []
        for j in range(nr):
            t = j / (nr - 1)
            row = []
            for k_ in range(seg):
                a = 2 * math.pi * k_ / seg
                back = 0.5 - 0.5 * math.cos(a)
                pleat = 1 + 0.07 * math.sin(3 * a + 5 * t) * (0.4 + 0.6 * t)
                dome = math.sqrt(max(0.0, 1 - (max(0.0, t - 0.55) / 0.45) ** 2 * 0.65))
                rx_ = (hw + 0.018 + 0.016 * t) * pleat * dome
                ry_ = (hd + 0.018 + 0.020 * t) * pleat * dome
                z = brim_z - 0.035 + (HEAD_TOP + 0.012 - brim_z + 0.035) * t + 0.025 * back * t
                row.append(bm.verts.new((math.sin(a) * rx_, HEAD_CY + 0.006 - math.cos(a) * ry_, z)))
            rows.append(row)
        wrap = []
        for j in range(nr - 1):
            for k_ in range(seg):
                k2 = (k_ + 1) % seg
                wrap.append(bm.faces.new([rows[j][k_], rows[j][k2], rows[j + 1][k2], rows[j + 1][k_]]))
        topc = bm.verts.new((0, HEAD_CY + 0.016, HEAD_TOP + 0.034))
        for k_ in range(seg):
            wrap.append(bm.faces.new([rows[-1][k_], rows[-1][(k_ + 1) % seg], topc]))
        bm.normal_update()
        orient_out(bm, wrap, center=(0, HEAD_CY, brim_z))
        for f in wrap:
            f[sub_l] = SUB['acc_a']
        fans = []
        # accordion-pleated fan crests (main, then a smaller one behind for volume)
        for (R, tilt, yoff, zoff, span) in ((0.165 * S, -18, 0.012, 0.055, (12, 168)), (0.125 * S, -48, 0.045, 0.045, (22, 158))):
            npl, nrad = 22, 4
            q = Matrix.Rotation(math.radians(tilt), 3, 'X')
            o3 = Vector((0, HEAD_CY + yoff, brim_z + zoff))
            grid = []
            for ri in range(nrad + 1):
                rr = R * (0.22 + 0.78 * ri / nrad)
                row = []
                for k_ in range(npl + 1):
                    ang = math.radians(span[0] + (span[1] - span[0]) * k_ / npl)
                    depth = (0.010 + 0.012 * ri / nrad) * (1 if k_ % 2 == 0 else -1)
                    p = Vector((math.cos(ang) * rr, depth, math.sin(ang) * rr * 0.78))
                    row.append(bm.verts.new(q @ p + o3))
                grid.append(row)
            for ri in range(nrad):
                for k_ in range(npl):
                    fans.append(bm.faces.new([grid[ri][k_], grid[ri][k_ + 1], grid[ri + 1][k_ + 1], grid[ri + 1][k_]]))
        for f in fans:
            f[sub_l] = SUB['acc_b']
        # knot at the back with two short tails
        kc = Vector((0, HEAD_BACK + 0.012, brim_z + 0.02))
        knot = bm_sphere(bm, tuple(kc), (0.034, 0.024, 0.026), 12, 8)
        for f in {f for v in knot for f in v.link_faces}:
            f[sub_l] = SUB['acc_a']
        for sx in (-1, 1):
            tail = bm_cyl(bm, kc + Vector((sx * 0.012, 0.004, -0.01)), kc + Vector((sx * 0.03, 0.012, -0.095)), 0.016, 0.022, seg=6, caps=False)
            for v in tail:
                v.co.y = kc.y + 0.004 + (v.co.y - kc.y - 0.004) * 0.3   # flatten into a fabric strip
            for f in {f for v in tail for f in v.link_faces}:
                f[sub_l] = SUB['acc_a']
        bm.normal_update()
    if want('hat_gele'):
        o = prop_obj('__gele', gele); smart_uv(o); head_rigid(o)
        # only the fan sheets need a back side
        bmd = bmesh.new(); bmd.from_mesh(o.data)
        subl = bmd.faces.layers.int['sub']
        fan = [f for f in bmd.faces if f[subl] == SUB['acc_b']]
        geom = bmesh.ops.duplicate(bmd, geom=fan)
        newf = [g for g in geom['geom'] if isinstance(g, bmesh.types.BMFace)]
        bmesh.ops.reverse_faces(bmd, faces=newf, flip_multires=False)
        bmd.normal_update()
        for v in {v for f in newf for v in f.verts}:
            v.co -= v.normal * 0.0015
        bmd.to_mesh(o.data); bmd.free(); o.data.update()
        push_outside(o, P['head'], 0.012)
        register(o, 'hat_gele', 'ACC_A', (1000, 460, 210, 80), notes='Gele: pleated wrap, accordion fan crests, knot and tails at the back (Yoruba womenswear reference).', license='cc0-ctlife')

    def headphones(bm, mic=False, one=False):
        rows = bm_torus(bm, (0, HEAD_CY, EYE_Z + 0.01), 1.0, 0.009, 20, 6, normal=(0, 1, 0), scale=(hw + 0.03, (HEAD_TOP - EYE_Z) + 0.03, 1), arc=0.5)
        for r in rows:
            for v in r:
                v.co.z = EYE_Z + 0.01 + abs(v.co.z - (EYE_Z + 0.01))
        assign_sub(bm, [v for r in rows for v in r], SUB['acc_dark'])
        for sg in ((1, -1) if not one else (1,)):
            cup = bm_cyl(bm, (sg * (hw + 0.012), HEAD_CY + 0.005, EYE_Z - 0.02), (sg * (hw + 0.045), HEAD_CY + 0.005, EYE_Z - 0.02), 0.042 if not mic else 0.03, seg=14)
            assign_sub(bm, cup, SUB['acc_a'])
        if mic:
            m = bm_cyl(bm, (hw + 0.03, HEAD_CY, EYE_Z - 0.03), (0.035, FACE_FRONT - 0.005, EYE_Z - 0.075 * S), 0.004, seg=6)
            assign_sub(bm, m, SUB['acc_dark'])
            tip = bm_sphere(bm, (0.035, FACE_FRONT - 0.005, EYE_Z - 0.075 * S), (0.009, 0.009, 0.009), 8, 5)
            assign_sub(bm, tip, SUB['acc_b'])
    hat('hat_headphones', headphones, notes='Over-ear headphones (Spaces Host).')
    if want('acc_headset'):
        o = prop_obj('__headset', lambda bm: headphones(bm, mic=True, one=True)); smart_uv(o); head_rigid(o)
        register(o, 'acc_headset', 'ACC_C', (500, 250, 120, 50), notes='Single-ear headset with mic (Community Mod).', license='cc0-ctlife')

    def monitor(bm):
        cz = EYE_Z + 0.005
        box = bm_box(bm, (0, HEAD_CY - 0.005, cz), (0.30 * S, 0.24 * S, 0.25 * S))
        assign_sub(bm, box, SUB['acc_dark'])
        scr = bm_box(bm, (0, HEAD_CY - 0.12 * S - 0.004, cz + 0.004), (0.24 * S, 0.01, 0.18 * S))
        assign_sub(bm, scr, SUB['acc_screen'])
        neck = bm_cyl(bm, (0, NECK_CY, NECK_Z - 0.06), (0, NECK_CY, cz - 0.12 * S), NECK_RX * 0.75, seg=10)
        assign_sub(bm, neck, SUB['acc_b'])
    if want('acc_monitor_head'):
        o = prop_obj('__monitor', monitor); smart_uv(o)
        co = mesh_arrays(o)
        W = np.zeros((len(co), len(BONES)), dtype=np.float32)
        t = np.clip((co[:, 2] - (NECK_Z - 0.04)) / 0.08, 0, 1)
        W[:, BONES.index('Head')] = t; W[:, BONES.index('Neck')] = 1 - t
        set_weights_matrix(o, W, BONES, 4)
        register(o, 'acc_monitor_head', 'ACC_A', (700, 300, 140, 60), notes='AI Agent: terminal for a head (replaces head + hair).', license='cc0-ctlife')

    # ---- face (ACC_B)
    lens_y = FACE_FRONT + 0.012 * S
    ex = abs(eyeL.x)
    def frames(bm, lens_r=0.022, shape=(1.15, 0.85), thick=0.0035, lens=True, sub_lens=SUB['acc_lens']):
        for sg in (1, -1):
            c = (sg * ex, lens_y - 0.003, EYE_Z - 0.002)
            rows = bm_torus(bm, c, lens_r, thick, 18, 5, normal=(0, 1, 0), scale=(shape[0], shape[1], 1))
            assign_sub(bm, [v for r in rows for v in r], SUB['acc_dark'])
            if lens:
                verts = []
                center = bm.verts.new(c)
                ring = [bm.verts.new((c[0] + math.cos(2 * math.pi * k / 18) * lens_r * shape[0], c[1] - 0.002, c[2] + math.sin(2 * math.pi * k / 18) * lens_r * shape[1])) for k in range(18)]
                for k in range(18):
                    f = bm.faces.new([center, ring[(k + 1) % 18], ring[k]])
                assign_sub(bm, [center] + ring, sub_lens)
            tm = bm_box(bm, (sg * (hw + 0.004), (lens_y + EYE_Y + 0.05) / 2, EYE_Z + 0.002), (0.004, abs(EYE_Y + 0.05 - lens_y), 0.005))
            assign_sub(bm, tm, SUB['acc_dark'])
        br = bm_box(bm, (0, lens_y - 0.004, EYE_Z + 0.004), (ex * 2 - lens_r * 2.1, 0.004, 0.004))
        assign_sub(bm, br, SUB['acc_dark'])
    def face_acc(name, build, budget=(500, 250, 120, 50), notes=''):
        if not want(name):
            return
        o = prop_obj('__' + name, build); smart_uv(o); head_rigid(o)
        register(o, name, 'ACC_B', budget, notes=notes, license='cc0-ctlife')
    face_acc('acc_shades', lambda bm: frames(bm, 0.024, (1.25, 0.78), 0.0022), notes='Dark shades.')
    face_acc('acc_glasses', lambda bm: frames(bm, 0.02, (1.0, 1.0), 0.003, lens=False), notes='Round nerd glasses (frames only).')
    def laser(bm):
        frames(bm, 0.022, (1.1, 0.8), lens=True, sub_lens=SUB['acc_glow'])
        for sg in (1, -1):
            v = bm_cyl(bm, (sg * ex, lens_y - 0.006, EYE_Z - 0.002), (sg * ex * 1.6, lens_y - 0.40, EYE_Z - 0.03), 0.006, 0.022, seg=10, caps=True)
            assign_sub(bm, v, SUB['acc_glow'])
    face_acc('acc_laser_eyes', laser, budget=(600, 300, 140, 60), notes='Laser eyes: glowing lenses + beams (emissive).')
    def visor(bm):
        rows = []
        for j, dz in enumerate((-0.022, 0.022)):
            row = []
            for k in range(13):
                a = -0.42 * math.pi + 0.84 * math.pi * k / 12
                row.append(bm.verts.new((math.sin(a) * (hw + 0.018), HEAD_CY - math.cos(a) * (hd + 0.012), EYE_Z + dz)))
            rows.append(row)
        for k in range(12):
            bm.faces.new([rows[0][k], rows[0][k + 1], rows[1][k + 1], rows[1][k]])
        assign_sub(bm, [v for r in rows for v in r], SUB['acc_glow'])
    if want('acc_visor'):
        o = prop_obj('__visor', visor); smart_uv(o); head_rigid(o); double_sided(o, SUB['acc_dark'])
        register(o, 'acc_visor', 'ACC_B', (300, 150, 70, 30), notes='HUD visor (emissive).', license='cc0-ctlife')
    def mask(bm):
        c = Vector((hw + 0.005, HEAD_CY - 0.01, EYE_Z + 0.03))
        vs = bm_sphere(bm, (0, 0, 0), (0.065, 0.03, 0.08), 16, 10, lambda p: p.y <= 0.002)
        rot = Matrix.Rotation(math.radians(-70), 3, 'Z')
        for v in vs:
            if v.is_valid:
                v.co = rot @ v.co + c
        assign_sub(bm, [v for v in vs if v.is_valid], SUB['acc_a'])
        strap = bm_torus(bm, (0, HEAD_CY, EYE_Z + 0.03), 1.0, 0.004, 20, 4, scale=(hw + 0.012, hd + 0.012, 1))
        assign_sub(bm, [v for r in strap for v in r], SUB['acc_dark'])
    if want('acc_anime_mask'):
        o = prop_obj('__mask', mask); smart_uv(o); head_rigid(o); double_sided(o, SUB['acc_a'])
        register(o, 'acc_anime_mask', 'ACC_C', (500, 250, 120, 50), notes='Anime fox-style mask worn on the side of the head (texture per look).', license='cc0-ctlife')

    # ---- neck / chest (ACC_C)
    chest_front = float(mesh_arrays(P['top'])[:, 1].min())
    def chain(bm):
        rows = bm_torus(bm, (0, NECK_CY - 0.02, NECK_Z - 0.07), 1.0, 0.006, 28, 6, normal=(0, -0.45, 1), scale=(NECK_RX + 0.05, NECK_RY + 0.07, 1))
        assign_sub(bm, [v for r in rows for v in r], SUB['acc_metal'])
        coin = bm_cyl(bm, (0, chest_front - 0.012, NECK_Z - 0.17), (0, chest_front - 0.02, NECK_Z - 0.17), 0.028, seg=16)
        assign_sub(bm, coin, SUB['acc_metal'])
    def chest_acc(name, build, budget=(500, 250, 120, 50), notes='', bones=None):
        if not want(name):
            return
        o = prop_obj('__' + name, build); smart_uv(o)
        weights_nearest(o) if bones is None else rigid(o, bones)
        register(o, name, 'ACC_C', budget, notes=notes, license='cc0-ctlife')
    chest_acc('acc_gold_chain', chain, bones={'Chest': 0.7, 'Neck': 0.3}, notes='Gold chain + coin pendant.')
    def lanyard(bm):
        bz = CHEST_Z + 0.02
        for sg in (1, -1):
            v = bm_box(bm, (sg * 0.045, (NECK_CY + chest_front) / 2 - 0.01, (NECK_Z + bz) / 2 + 0.03), (0.012, 0.004, NECK_Z - bz + 0.06),
                       rot=Matrix.Rotation(sg * 0.35, 3, 'Y') @ Matrix.Rotation(-0.35, 3, 'X'))
            assign_sub(bm, v, SUB['acc_strap'])
        rows = bm_torus(bm, (0, NECK_CY + 0.005, NECK_Z - 0.01), 1.0, 0.003, 20, 4, normal=(0, -0.4, 1), scale=(NECK_RX + 0.012, NECK_RY + 0.015, 1))
        assign_sub(bm, [v for r in rows for v in r], SUB['acc_strap'])
        card = bm_box(bm, (0, chest_front - 0.006, bz - 0.01), (0.075, 0.004, 0.10))
        assign_sub(bm, card, SUB['acc_a'])
    chest_acc('acc_lanyard', lanyard, bones={'Chest': 0.8, 'Neck': 0.2}, notes='Lanyard + badge (text per look: STAFF, AUDIT, SUPPORT, VIP).')
    def pins(bm):
        for i in range(3):
            v = bm_cyl(bm, (0.07 + 0.022 * i, chest_front + 0.012, CHEST_Z + 0.03 - 0.01 * i), (0.07 + 0.022 * i, chest_front, CHEST_Z + 0.03 - 0.01 * i), 0.009, seg=10)
            assign_sub(bm, v, SUB['acc_metal'])
    chest_acc('acc_pins', pins, bones={'Chest': 1.0}, budget=(200, 100, 40, 20), notes='Matching cabal pins.')

    # ---- back (BACK)
    back_y = float(mesh_arrays(P['top'])[:, 1].max())
    def back_acc(name, build, bones, budget=(700, 300, 150, 60), notes=''):
        if not want(name):
            return
        o = prop_obj('__' + name, build); smart_uv(o); rigid(o, bones)
        register(o, name, 'BACK', budget, notes=notes, license='cc0-ctlife')
    def ring_light(bm):
        cx, cy, cz = 0.30, FACE_FRONT - 0.30, EYE_Z - 0.02
        rows = bm_torus(bm, (cx, cy, cz), 0.12, 0.016, 24, 6, normal=(0, 1, 0))
        assign_sub(bm, [v for r in rows for v in r], SUB['acc_glow'])
        ph = bm_box(bm, (cx, cy + 0.005, cz), (0.07, 0.008, 0.14))
        assign_sub(bm, ph, SUB['acc_dark'])
        pole = bm_cyl(bm, (0.16, back_y + 0.02, CHEST_Z), (cx, cy + 0.02, cz - 0.12), 0.008, seg=8)
        assign_sub(bm, pole, SUB['acc_dark'])
        pole2 = bm_cyl(bm, (cx, cy + 0.02, cz - 0.12), (cx, cy, cz - 0.12), 0.008, seg=8)
        assign_sub(bm, pole2, SUB['acc_dark'])
        pack = bm_box(bm, (0, back_y + 0.06, CHEST_Z - 0.04), (0.26, 0.11, 0.32))
        assign_sub(bm, pack, SUB['acc_b'])
    back_acc('acc_ring_light', ring_light, {'Chest': 1.0}, budget=(800, 380, 180, 70), notes='Streamer rig: backpack + ring light with phone (emissive).')
    def backpack(bm):
        pack = bm_box(bm, (0, back_y + 0.07, CHEST_Z - 0.06), (0.28, 0.13, 0.38))
        assign_sub(bm, pack, SUB['acc_a'])
        pk = bm_box(bm, (0, back_y + 0.14, CHEST_Z - 0.12), (0.2, 0.03, 0.16))
        assign_sub(bm, pk, SUB['acc_b'])
    back_acc('acc_backpack', backpack, {'Chest': 0.7, 'Spine': 0.3}, budget=(300, 150, 70, 30), notes='Backpack.')
    def drone(bm):
        c = Vector((0.26, back_y + 0.05, HEAD_TOP + 0.10))
        body = bm_box(bm, c, (0.10, 0.10, 0.035)); assign_sub(bm, body, SUB['acc_dark'])
        for sx, sy in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            p = c + Vector((sx * 0.085, sy * 0.085, 0.01))
            arm = bm_cyl(bm, c, p, 0.006, seg=6); assign_sub(bm, arm, SUB['acc_dark'])
            rot = bm_cyl(bm, p + Vector((0, 0, 0.004)), p + Vector((0, 0, 0.010)), 0.05, seg=16); assign_sub(bm, rot, SUB['acc_a'])
        led = bm_box(bm, c + Vector((0, -0.051, 0)), (0.03, 0.004, 0.01)); assign_sub(bm, led, SUB['acc_glow'])
    back_acc('acc_drone', drone, {'Chest': 0.5, 'Head': 0.5}, budget=(600, 280, 130, 50), notes='Sniper bot drone hovering over the shoulder.')
    def cloud(bm):
        c = Vector((0, HEAD_CY, HEAD_TOP + 0.22))
        for dx, dz, r in ((0, 0.02, 0.09), (-0.09, -0.01, 0.07), (0.09, -0.01, 0.075), (0.045, 0.045, 0.06), (-0.05, 0.04, 0.06)):
            s = bm_sphere(bm, c + Vector((dx, 0, dz)), (r, r * 0.8, r * 0.75), 12, 8); assign_sub(bm, s, SUB['acc_a'])
        for i in range(7):
            x = -0.11 + 0.037 * i
            dr = bm_cyl(bm, (x, HEAD_CY + (0.02 if i % 2 else -0.02), HEAD_TOP + 0.13 - 0.02 * (i % 3)), (x, HEAD_CY, HEAD_TOP + 0.06 - 0.02 * (i % 3)), 0.003, 0.0015, seg=5)
            assign_sub(bm, dr, SUB['acc_b'])
    back_acc('acc_rain_cloud', cloud, {'Head': 1.0}, budget=(600, 280, 130, 50), notes='Doomposter personal rain cloud.')

    # ---- hand-held (HAND), right hand grip
    hand = BH['Hand.R']; mid = BH['Middle1.R']; idx = BH['Index1.R']; pky = BH['Pinky1.R']
    hdir = (mid - hand).normalized()
    across = (idx - pky).normalized()
    pn = hdir.cross(across).normalized()
    if pn.z > 0:
        pn = -pn
    grip = hand + hdir * (mid - hand).length * 0.85 + pn * 0.03
    def hand_acc(name, build, budget=(500, 250, 120, 50), notes=''):
        if not want(name):
            return
        o = prop_obj('__' + name, build); smart_uv(o); rigid(o, {'Hand.R': 1.0})
        register(o, name, 'HAND', budget, notes=notes, license='cc0-ctlife')
    def oriented_box(bm, c, size, xaxis, zaxis):
        x = Vector(xaxis).normalized(); z = Vector(zaxis).normalized(); y = z.cross(x).normalized(); z = x.cross(y)
        rot = Matrix((x, y, z)).transposed()
        return bm_box(bm, c, size, rot=rot)
    def phone(bm, flip=False):
        b = oriented_box(bm, grip + pn * 0.004, (0.15 if not flip else 0.10, 0.075 if not flip else 0.055, 0.009), hdir, pn)
        assign_sub(bm, b, SUB['acc_dark'])
        s = oriented_box(bm, grip + pn * 0.0095, (0.135 if not flip else 0.045, 0.065 if not flip else 0.045, 0.001), hdir, pn)
        assign_sub(bm, s, SUB['acc_screen'])
    hand_acc('hand_phone', phone, budget=(120, 60, 30, 12), notes='Smartphone (screen emissive).')
    hand_acc('hand_burner', lambda bm: phone(bm, True), budget=(120, 60, 30, 12), notes='Burner flip phone.')
    def notebook(bm):
        b = oriented_box(bm, grip + pn * 0.01, (0.20, 0.15, 0.018), hdir, pn); assign_sub(bm, b, SUB['acc_a'])
        pg = oriented_box(bm, grip + pn * 0.0195, (0.19, 0.14, 0.002), hdir, pn); assign_sub(bm, pg, SUB['acc_b'])
    hand_acc('hand_notebook', notebook, budget=(60, 36, 24, 12), notes='Notebook ("1/ here is why").')
    hand_acc('hand_slip', lambda bm: assign_sub(bm, oriented_box(bm, grip + pn * 0.004, (0.12, 0.07, 0.002), hdir, pn), SUB['acc_a']), budget=(24, 12, 12, 12), notes='Betting slip.')
    def cup(bm):
        c = grip + pn * 0.035
        v = bm_cyl(bm, c - Vector((0, 0, 0.055)), c + Vector((0, 0, 0.055)), 0.034, 0.040, seg=14); assign_sub(bm, v, SUB['acc_a'])
        lid = bm_cyl(bm, c + Vector((0, 0, 0.055)), c + Vector((0, 0, 0.068)), 0.042, 0.036, seg=14); assign_sub(bm, lid, SUB['acc_b'])
    hand_acc('hand_coffee', cup, budget=(140, 70, 36, 16), notes='GM coffee cup.')
    def cash(bm):
        for i in range(5):
            ax = (Matrix.Rotation(math.radians(-30 + 15 * i), 3, pn) @ hdir)
            b = oriented_box(bm, grip + pn * (0.004 + 0.0015 * i) + ax * 0.05, (0.15, 0.065, 0.0015), ax, pn); assign_sub(bm, b, SUB['acc_a'])
    hand_acc('hand_cash', cash, budget=(60, 60, 30, 12), notes='Fan of cash (fake-rich).')
    def key(bm):
        b = oriented_box(bm, grip + pn * 0.012, (0.06, 0.035, 0.016), hdir, pn); assign_sub(bm, b, SUB['acc_dark'])
        r = bm_torus(bm, grip + pn * 0.012 + hdir * 0.04, 0.012, 0.002, 10, 4, normal=tuple(pn)); assign_sub(bm, [v for x in r for v in x], SUB['acc_metal'])
    hand_acc('hand_carkey', key, budget=(120, 60, 30, 12), notes='Rented supercar key fob.')
    def can(bm):
        c = grip + pn * 0.035
        v = bm_cyl(bm, c - Vector((0, 0, 0.06)), c + Vector((0, 0, 0.06)), 0.03, seg=14); assign_sub(bm, v, SUB['acc_a'])
    hand_acc('hand_energy', can, budget=(80, 50, 28, 14), notes='Energy drink can.')

def build_polyhaven():
    """CC0 Poly Haven props: decimated, re-baked into this family's slot and rigidly bound."""
    dl = EVID / 'downloads'
    hand = BH['Hand.R']; mid = BH['Middle1.R']; idx = BH['Index1.R']; pky = BH['Pinky1.R']
    hdir = (mid - hand).normalized(); across = (idx - pky).normalized()
    pn = hdir.cross(across).normalized()
    if pn.z > 0:
        pn = -pn
    grip = hand + hdir * (mid - hand).length * 0.85 + pn * 0.03
    chest_front = float(mesh_arrays(P['top'])[:, 1].min())
    specs = {
        # name: (asset, slot, bone weights, placement fn(obj) -> matrix, keep mesh names, budget)
        'hand_megaphone': ('Megaphone_01', 'HAND', {'Hand.R': 1.0}, (450, 220, 100, 40)),
        'hand_magnifier': ('magnifying_glass_01', 'HAND', {'Hand.R': 1.0}, (300, 150, 70, 30)),
        'hand_pickaxe': ('picke_dirty_01', 'HAND', {'Hand.R': 1.0}, (300, 150, 70, 30)),
        'hand_clipboard': ('clipboard', 'HAND', {'Hand.R': 1.0}, (200, 100, 50, 20)),
        'hand_watering_can': ('watering_can_metal_01', 'HAND', {'Hand.R': 1.0}, (400, 200, 90, 36)),
        'acc_binoculars': ('binoculars', 'ACC_C', {'Chest': 0.8, 'Neck': 0.2}, (500, 240, 110, 44)),
        'acc_camera': ('Camera_01', 'ACC_C', {'Chest': 0.8, 'Neck': 0.2}, (500, 240, 110, 44)),
    }
    for name, (asset, slot, bones, budget) in specs.items():
        if not want(name):
            continue
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(next((dl / asset).glob('*.gltf'))))
        objs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
        if asset in ('Camera_01', 'binoculars'):
            objs = [o for o in objs if 'strap' not in o.name]
        for o in objs:
            o.parent = None
        select_only(objs)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if len(objs) > 1:
            bpy.ops.object.join()
        o = bpy.context.object
        for e in [x for x in bpy.data.objects if x not in before and x is not o]:
            bpy.data.objects.remove(e, do_unlink=True)
        co = mesh_arrays(o)
        mn, mx = co.min(0), co.max(0)
        ctr = (mn + mx) / 2
        # orientation per asset: build a world matrix
        if asset == 'Megaphone_01':        # long axis X (bell at -X); handle below
            # bell (-X local) along the fingers, handle (local -Z) through the fist (across the knuckles)
            ex_ = -hdir; ez_ = -across; ey_ = ez_.cross(ex_).normalized(); ez_ = ex_.cross(ey_)
            R3 = Matrix((ex_, ey_, ez_)).transposed().to_4x4()
            Mx = Matrix.Translation(grip + pn * 0.01) @ R3 @ Matrix.Translation(Vector((-ctr[0] * 0.3, -ctr[1], -(mn[2] + 0.12 * (mx[2] - mn[2])))))
        elif asset == 'magnifying_glass_01':  # handle along -Z, lens at +Z
            q = Vector((0, 0, 1)).rotation_difference(hdir)
            Mx = Matrix.Translation(grip + pn * 0.01 - hdir * 0.05) @ q.to_matrix().to_4x4() @ Matrix.Translation(Vector((-ctr[0], -ctr[1], -mn[2])))
        elif asset == 'picke_dirty_01':     # shaft along Z
            q = Vector((0, 0, 1)).rotation_difference(across)
            Mx = Matrix.Translation(grip + pn * 0.012) @ q.to_matrix().to_4x4() @ Matrix.Diagonal((0.8, 0.8, 0.8, 1)) @ Matrix.Translation(Vector((-ctr[0], -ctr[1], -0.15)))
        elif asset in ('clipboard',):       # flat in XY
            q = Vector((0, 0, 1)).rotation_difference(pn)
            Mx = Matrix.Translation(grip + pn * 0.01) @ q.to_matrix().to_4x4() @ Matrix.Translation(Vector((-ctr[0], -ctr[1], -mn[2])))
        elif asset == 'watering_can_metal_01':
            Mx = Matrix.Translation(grip + Vector((0, 0, -0.17))) @ Matrix.Diagonal((0.8, 0.8, 0.8, 1)) @ Matrix.Translation(Vector((-ctr[0], -ctr[1], -mn[2])))
        else:                               # chest-worn camera/binoculars facing forward (-Y)
            sz = mx - mn
            Mx = Matrix.Translation(Vector((0.0, chest_front - 0.045, CHEST_Z - 0.07))) @ Matrix.Rotation(math.radians(180), 4, 'Z') @ Matrix.Translation(Vector((-ctr[0], -ctr[1], -ctr[2])))
            if asset == 'binoculars':
                Mx = Matrix.Translation(Vector((0.0, chest_front - 0.05, CHEST_Z - 0.06))) @ Matrix.Rotation(math.radians(90), 4, 'X') @ Matrix.Translation(Vector((-ctr[0], -ctr[1], -ctr[2])))
        o.data.transform(Mx)
        o.matrix_world = Matrix.Identity(4)
        # bake source: the asset's own principled material (base colour + normal + arm) -> slot
        decimate_to(o, max(budget[0] * 2, 1200), symmetry=False)
        uv0 = o.data.uv_layers[0].name
        o.data.uv_layers[0].name = 'SrcUV'
        o.data.uv_layers.new(name='AtlasUV')
        smart_uv(o, 66)
        face_attr(o, 'sub', [SUB['acc_tex']] * len(o.data.polygons))
        face_attr(o, 'region', default=0)
        o.parent = rig
        o.modifiers.clear()
        o.modifiers.new('Game skin', 'ARMATURE').object = rig
        rigid(o, bones)
        o['ph_asset'] = asset
        register(o, name, slot, budget, notes=f'Poly Haven CC0 asset {asset} (decimated, re-baked).', license='cc0-polyhaven')
        # replace generic bake with the asset's own textures later (see bake_polyhaven)

def bake_polyhaven(o):
    """Bake a Poly Haven asset's own material into the slot (base colour EMIT, tangent NORMAL bake)."""
    slot = o['slot']
    asset = o['ph_asset']
    texdir = EVID / 'downloads' / asset / 'textures'
    b = duplicate(o, '__bkph'); b.modifiers.clear(); b.hide_render = False
    b.data.uv_layers['SrcUV'].active_render = True
    b.data.uv_layers.active = b.data.uv_layers['SlotUV']
    mats = list(b.data.materials)
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 1
    res = {}
    r0, r1, c0, c1 = slot_px(slot, 2048)
    for kind in ('base', 'normal', 'arm', 'id', 'pos', 'nrm'):
        im = new_float_image('__ph_' + kind, 2048)
        for m in b.data.materials:
            nt = m.node_tree
            for n in nt.nodes:
                n.select = False
            t = nt.nodes.new('ShaderNodeTexImage'); t.image = im; t.select = True; nt.nodes.active = t
            bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            outn = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
            if kind in ('base', 'arm', 'id', 'pos', 'nrm'):
                em = nt.nodes.new('ShaderNodeEmission')
                if kind == 'base':
                    inp = bsdf.inputs['Base Color']
                    if inp.is_linked:
                        nt.links.new(inp.links[0].from_socket, em.inputs['Color'])
                    else:
                        em.inputs['Color'].default_value = inp.default_value
                elif kind == 'arm':
                    cx = nt.nodes.new('ShaderNodeCombineXYZ')
                    for dst, src in (('X', 'Metallic'), ('Y', 'Roughness')):
                        inp = bsdf.inputs[src]
                        if inp.is_linked:
                            nt.links.new(inp.links[0].from_socket, cx.inputs[dst])
                        else:
                            cx.inputs[dst].default_value = inp.default_value
                    nt.links.new(cx.outputs[0], em.inputs['Color'])
                else:
                    geo = nt.nodes.new('ShaderNodeNewGeometry')
                    vm = nt.nodes.new('ShaderNodeVectorMath'); vm.operation = 'MULTIPLY_ADD'
                    vm.inputs[1].default_value = (.5, .5, .5); vm.inputs[2].default_value = (.5, .5, .5)
                    if kind == 'id':
                        vm.inputs[0].default_value = (SUB['acc_tex'] / 64 * 2 - 1, -1, 1)
                    else:
                        nt.links.new(geo.outputs['Position' if kind == 'pos' else 'Normal'], vm.inputs[0])
                    nt.links.new(vm.outputs[0], em.inputs['Color'])
                nt.links.new(em.outputs[0], outn.inputs['Surface'])
        select_only([b])
        if kind == 'normal':
            for m in b.data.materials:
                bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
                outn = next(n for n in m.node_tree.nodes if n.type == 'OUTPUT_MATERIAL')
                m.node_tree.links.new(bsdf.outputs[0], outn.inputs['Surface'])
            bpy.ops.object.bake(type='NORMAL', use_clear=True, margin=0 if kind == 'id' else 8, uv_layer='SlotUV', normal_space='TANGENT')
        else:
            bpy.ops.object.bake(type='EMIT', use_clear=True, margin=0 if kind == 'id' else 8, uv_layer='SlotUV')
        res[kind] = image_array(im)[r0:r1, c0:c1].copy()
        bpy.data.images.remove(im)
    bpy.data.objects.remove(b, do_unlink=True)
    sub = np.where(res['id'][:, :, 0] > 0.01, SUB['acc_tex'], 0).astype(np.uint8)
    ms = np.dstack([res['arm'][:, :, 0], np.ones_like(sub, dtype=np.float32), np.zeros_like(sub, dtype=np.float32), 1 - res['arm'][:, :, 1]])
    np.savez_compressed(d / 'maps' / f'{o.name[len(fam) + 3:]}.npz',
                        base=np.clip(res['base'][:, :, :3] * 255, 0, 255).astype(np.uint8),
                        normal=np.clip(res['normal'][:, :, :3] * 255, 0, 255).astype(np.uint8),
                        ms=np.clip(ms * 255, 0, 255).astype(np.uint8),
                        pos=(res['pos'][:, :, :3] * 2 - 1).astype(np.float16), nrm=(res['nrm'][:, :, :3] * 2 - 1).astype(np.float16),
                        sub=sub, region=np.zeros_like(sub), ao=np.full(sub.shape, 255, np.uint8))
    for m in list(o.data.materials):
        pass
    o.data.materials.clear()

def want(name):
    return not ONLY or name in ONLY

# ------------------------------------------------------------------ run
t0 = time.time()
build_tops()
build_outer()
build_bottoms()
build_shoes()
build_hair()
build_beards()
build_accessories()
build_polyhaven()
log('geometry built', len(BUILT), round(time.time() - t0, 1), 's')
for o in BUILT:
    if 'ph_asset' in o.keys():
        bake_polyhaven(o)
    else:
        bake_part(o)
    log('baked', o.name)
# keep only base parts, rig and wardrobe parts in the saved file
keep = set(BUILT) | set(P.values()) | {rig}
for o in list(bpy.data.objects):
    if o not in keep:
        bpy.data.objects.remove(o, do_unlink=True)
for o in P.values():
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.outliner.orphans_purge(do_recursive=True)
regp = d / 'wardrobe.json'
old = json.loads(regp.read_text()) if regp.exists() and ONLY else {}
old.update(REG)
regp.write_text(json.dumps(old, indent=1))
meta_out = json.loads((d / 'meta.json').read_text()); meta_out['chin_z'] = CHIN_Z
(d / 'meta.json').write_text(json.dumps(meta_out, indent=1))
outp = d / ('wardrobe.blend' if not ONLY else 'wardrobe_partial.blend')
if ONLY and (d / 'wardrobe.blend').exists():
    # merge into the existing wardrobe file: replace same-named parts
    bpy.ops.wm.save_as_mainfile(filepath=str(d / '__w_new.blend'), compress=True)
    bpy.ops.wm.open_mainfile(filepath=str(d / 'wardrobe.blend'), load_ui=False, use_scripts=False)
    names = [f'{fam}_w_{n}' for n in REG]
    for o in list(bpy.data.objects):
        if o.name in names:
            bpy.data.objects.remove(o, do_unlink=True)
    rig2 = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    with bpy.data.libraries.load(str(d / '__w_new.blend'), link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in names]
    for o in dst.objects:
        if o:
            scene2 = bpy.context.scene
            scene2.collection.objects.link(o)
            o.parent = rig2
            for m in o.modifiers:
                if m.type == 'ARMATURE':
                    m.object = rig2
    for o in list(bpy.data.objects):
        if o.type == 'ARMATURE' and o is not rig2:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.outliner.orphans_purge(do_recursive=True)
    outp = d / 'wardrobe.blend'
    (d / '__w_new.blend').unlink()
bpy.ops.wm.save_as_mainfile(filepath=str(outp), compress=True)
log('WARDROBE_DONE', fam, sorted(REG), round(time.time() - t0, 1), 's')
