"""Fit Quaternius 'Universal Base Characters' hairstyles (CC0, opaque stylised hair) onto a CT Life master head.

Usage: art/pipeline/blender.sh art/pipeline/char_hair.py -- male|female
The UBC heads have different proportions, so every hair is mapped with a per-axis affine fit
(scalp bounding box above the eyes; for beards the lower face), then pushed 3 mm outside the scalp.
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


def log(*a):
    print('[char_hair]', *a, flush=True)


def head_metrics(pts, eye_z, front_sign):
    """pts: world-space head vertices. Returns dict of reference measures."""
    scalp = [p for p in pts if p.z > eye_z + 0.02]
    m = dict(xmin=min(p.x for p in scalp), xmax=max(p.x for p in scalp),
             ymin=min(p.y for p in scalp), ymax=max(p.y for p in scalp),
             top=max(p.z for p in pts), eye=eye_z)
    low = [p for p in pts if p.z < eye_z - 0.02 and (p.y - (m['ymin'] + m['ymax']) / 2) * front_sign > 0]
    m['chin'] = min(p.z for p in low) if low else eye_z - 0.12
    face = [p for p in pts if eye_z - 0.09 < p.z < eye_z - 0.03]
    m['jaw_w'] = max(p.x for p in face) - min(p.x for p in face) if face else (m['xmax'] - m['xmin']) * 0.8
    m['front'] = min(p.y for p in pts) if front_sign < 0 else max(p.y for p in pts)
    return m


# ---- target head (MPFB body)
eye_t = (rig.matrix_world @ rig.data.bones['eye_l'].head_local).z
me = body.data
head_vis = set()
attr = me.attributes['ctl_region']
for p in me.polygons:
    if REG[attr.data[p.index].value] == 'head':
        head_vis.update(p.vertices)
T = head_metrics([body.matrix_world @ me.vertices[i].co for i in head_vis], eye_t, -1)
log('target head', {k: round(v, 3) for k, v in T.items()})
bmb = bmesh.new()
bmb.from_mesh(me)
bmb.transform(body.matrix_world)
bvh = BVHTree.FromBMesh(bmb)

# ---- source head (UBC)
src_fbx = os.path.join(C.UBC, 'Base Characters', 'Unity',
                       'Superhero_Male_FullBody.fbx' if SEX == 'male' else 'Superhero_Female_FullBody.fbx')
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=src_fbx)
new = [o for o in bpy.data.objects if o not in before]
src_body = next(o for o in new if o.type == 'MESH' and 'superhero' in o.name.lower())
src_eyes = next(o for o in new if o.type == 'MESH' and o.name.startswith('Eyes'))
src_arm = next(o for o in new if o.type == 'ARMATURE')
eyes_pts = [src_eyes.matrix_world @ v.co for v in src_eyes.data.vertices]
eye_s = sum((p.z for p in eyes_pts)) / len(eyes_pts)
eye_cy = sum((p.y for p in eyes_pts)) / len(eyes_pts)
hg = src_body.vertex_groups.get('Head')
src_head = [src_body.matrix_world @ v.co for v in src_body.data.vertices
            if any(g.group == hg.index and g.weight > 0.5 for g in v.groups)]
hc_y = sum(p.y for p in src_head) / len(src_head)
# rotate the source 180 deg about Z when it faces +Y (CT Life / MPFB characters face -Y)
ROT = Matrix.Rotation(math.pi, 4, 'Z') if eye_cy > hc_y else Matrix.Identity(4)
src_head = [ROT @ p for p in src_head]
S = head_metrics(src_head, eye_s, -1)
log('source head', {k: round(v, 3) for k, v in S.items()}, 'rotated', eye_cy > hc_y)
for o in new:
    bpy.data.objects.remove(o, do_unlink=True)


def make_map(beard=False):
    scx, scy = (S['xmin'] + S['xmax']) / 2, (S['ymin'] + S['ymax']) / 2
    tcx, tcy = (T['xmin'] + T['xmax']) / 2, (T['ymin'] + T['ymax']) / 2
    if beard:
        sx = T['jaw_w'] / S['jaw_w']
        sz = (T['eye'] - T['chin']) / (S['eye'] - S['chin'])
        return lambda p: Vector(((p.x - scx) * sx + tcx, (p.y - S['front']) * sx + T['front'], (p.z - S['eye']) * sz + T['eye']))
    sx = (T['xmax'] - T['xmin']) / (S['xmax'] - S['xmin'])
    sy = (T['ymax'] - T['ymin']) / (S['ymax'] - S['ymin'])
    sz = (T['top'] - T['eye']) / (S['top'] - S['eye'])
    return lambda p: Vector(((p.x - scx) * sx + tcx, (p.y - scy) * sy + tcy, (p.z - S['eye']) * sz + T['eye']))


hair_dir = os.path.join(C.UBC, 'Hairstyles', 'Rigged to Head Bone', 'FBX (Unity)')
tex_dir = os.path.join(C.UBC, 'Hairstyles', 'Textures')
for key, stem in C.QUAT_HAIRS[SEX].items():
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(hair_dir, stem + '.fbx'))
    new = [o for o in bpy.data.objects if o not in before]
    src = next(o for o in new if o.type == 'MESH')
    mp = make_map(beard=(key == 'beard'))
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.transform(ROT @ src.matrix_world)
    for v in bm.verts:
        v.co = mp(v.co)
    # push out of the scalp/face
    margin = 0.002 if key == 'beard' else 0.003
    for v in bm.verts:
        loc, nor, fi, d = bvh.find_nearest(v.co)
        if loc is None or d > 0.03:
            continue
        s = (v.co - loc).dot(nor)
        if s < margin:
            v.co += nor * (margin - s)
    me2 = bpy.data.meshes.new('part.hair_' + key)
    bm.to_mesh(me2)
    bm.free()
    mat_name = src.data.materials[0].name if src.data.materials else 'MI_Hair_1'
    tex = 'T_Hair_2_BaseColor.png' if 'Hair_2' in mat_name else 'T_Hair_1_BaseColor.png'
    o = bpy.data.objects.new('part.hair_' + key, me2)
    bpy.context.scene.collection.objects.link(o)
    for x in new:
        bpy.data.objects.remove(x, do_unlink=True)
    o.parent = rig
    o.matrix_parent_inverse = Matrix.Identity(4)
    g = o.vertex_groups.new(name='Head')
    g.add([v.index for v in me2.vertices], 1.0, 'REPLACE')
    if key == 'beard':
        # lower beard follows the jaw a little
        jz = (rig.matrix_world @ rig.data.bones['jaw'].tail_local).z
        gj = o.vertex_groups.new(name='jaw')
        for v in me2.vertices:
            t = max(0.0, min(1.0, (T['eye'] - 0.07 - v.co.z) / 0.05))
            if t > 0:
                gj.add([v.index], t * 0.6, 'REPLACE')
                g.add([v.index], 1 - t * 0.6, 'REPLACE')
    m = o.modifiers.new('Armature', 'ARMATURE')
    m.object = rig
    mat = bpy.data.materials.new('quat.' + key)
    mat.use_nodes = True
    img = bpy.data.images.load(os.path.join(tex_dir, tex), check_existing=True)
    n = mat.node_tree.nodes.new('ShaderNodeTexImage')
    n.image = img
    bsdf = mat.node_tree.nodes['Principled BSDF']
    mat.node_tree.links.new(n.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.6
    me2.materials.append(mat)
    for p in me2.polygons:
        p.use_smooth = True
    o['ctl_slot'] = 'facial_hair' if key == 'beard' else 'hair'
    o['ctl_source'] = 'quaternius:UBC/Hairstyles/' + stem
    o['ctl_texture'] = os.path.join(tex_dir, tex)
    o['ctl_tile'] = 256
    log('hair', key, stem, 'tris', sum(len(p.vertices) - 2 for p in me2.polygons), 'tex', tex)

bmb.free()
bpy.ops.wm.save_as_mainfile(filepath=path)
log('saved', path)
