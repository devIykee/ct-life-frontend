"""Assemble a CT Life character in Blender from the exported FBX parts + PNG tiles (used by previews)."""
import bpy, os, json, math, re
from mathutils import Vector, Matrix
import ctl_config as C


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_custom_normals=True)
    return [o for o in bpy.data.objects if o not in before]


MATS = {}


def material(tex_path, kind):
    key = (tex_path, kind)
    if key in MATS:
        return MATS[key]
    m = bpy.data.materials.new(os.path.basename(tex_path))
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = bpy.data.images.load(tex_path, check_existing=True)
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = {'skin': 0.5, 'eyes': 0.15, 'hair': 0.55}.get(kind, 0.75)
    if kind == 'skin':
        b.inputs['Subsurface Weight'].default_value = 0.12
        b.inputs['Subsurface Radius'].default_value = (0.9, 0.35, 0.2)
        b.inputs['Subsurface Scale'].default_value = 0.01
    if kind == 'emissive':
        nt.links.new(t.outputs['Color'], b.inputs['Emission Color'])
        b.inputs['Emission Strength'].default_value = 0.0
    MATS[key] = m
    return m


def assemble(sex, od, x):
    base = os.path.join(C.PROCESSED, 'characters', sex)
    man = json.load(open(os.path.join(base, 'manifest.json')))
    objs = import_fbx(os.path.join(base, man['body']['fbx']))
    arm = next(o for o in objs if o.type == 'ARMATURE')
    parts = dict(od['parts'])
    hat_mode = None
    for p in parts:
        hm = man['parts'][p].get('hair_mode')
        if hm:
            hat_mode = hm
    if od.get('hair') and hat_mode != 'hide':
        h = od['hair'][0]
        if hat_mode == 'short' and 'hair_buzzed' in man['parts']:
            h = 'hair_buzzed'
        parts[h] = od['hair'][1]
    elif hat_mode == 'short' and 'hair_buzzed' in man['parts']:
        parts['hair_buzzed'] = 'black'
    hides = set()
    for p in parts:
        hides.update(man['parts'][p]['hides_regions'])
    tris = 0
    keep = []
    for o in objs:
        if o.type != 'MESH':
            continue
        nm = re.sub(r'\.\d+$', '', o.name)
        if not nm.endswith('_LOD0') or any(nm == f'body_{r}_LOD0' for r in hides):
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        if o.name.startswith('eyes'):
            o.data.materials.clear(); o.data.materials.append(material(os.path.join(base, man['body']['eye_textures'][od.get('eyes', 'brown')]), 'eyes'))
        else:
            o.data.materials.clear(); o.data.materials.append(material(os.path.join(base, man['body']['skin_textures'][od['skin']]), 'skin'))
        tris += len(o.data.polygons)
        keep.append(o)
    for p, variant in parts.items():
        info = man['parts'][p]
        new = import_fbx(os.path.join(base, info['fbx']))
        a2 = next(o for o in new if o.type == 'ARMATURE')
        for o in new:
            if o.type != 'MESH':
                continue
            if not o.name.startswith(p + '_LOD0'):
                bpy.data.objects.remove(o, do_unlink=True)
                continue
            mw = o.matrix_world.copy()
            o.parent = arm
            o.matrix_world = mw
            for m in o.modifiers:
                if m.type == 'ARMATURE':
                    m.object = arm
            tex = info['textures'].get(variant) or info['textures'][info['default']]
            kind = 'hair' if info['slot'] in ('hair', 'facial_hair') else ('emissive' if p == 'ring_light' else 'cloth')
            o.data.materials.clear(); o.data.materials.append(material(os.path.join(base, tex), kind))
            tris += len(o.data.polygons)
        bpy.data.objects.remove(a2, do_unlink=True)
    arm.location.x += x
    arm['ctl_tris'] = tris
    return arm, tris


def align(arm, bname, world_dir):
    pb = arm.pose.bones[bname]
    d_arm = (arm.matrix_world.to_3x3().inverted() @ Vector(world_dir)).normalized()
    cur = (pb.tail - pb.head).normalized()
    q = cur.rotation_difference(d_arm)
    M = Matrix.Translation(pb.head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pb.head)
    pb.matrix = M @ pb.matrix
    bpy.context.view_layer.update()


def relaxed(arm, holds_light=False, seed=0):
    for s, sg in (('l', 1), ('r', -1)):
        if s == 'r' and holds_light:
            align(arm, 'upperarm_r', (-0.25, -0.45, -0.85))
            align(arm, 'lowerarm_r', (-0.15, -0.75, 0.62))
            align(arm, 'hand_r', (-0.1, -0.85, 0.5))
            align(arm, 'index_01_r', (-0.1, -0.85, 0.5))
            continue
        align(arm, f'upperarm_{s}', (sg * 0.2, 0.03, -0.98))
        align(arm, f'lowerarm_{s}', (sg * 0.1, -0.22, -0.97))
        align(arm, f'hand_{s}', (sg * 0.05, -0.12, -0.99))
        align(arm, f'index_01_{s}', (sg * 0.03, -0.18, -0.98))
    # tiny head/weight-shift variety
    pb = arm.pose.bones['Head']
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (0, 0, math.radians(((seed * 37) % 21) - 10))




def assemble_npc(name, x):
    """Import a baked NPC (single mesh + atlas) and keep LOD0."""
    base = os.path.join(C.PROCESSED, 'characters', 'npc')
    man = json.load(open(os.path.join(base, 'manifest.json')))[name]
    objs = import_fbx(os.path.join(base, man['fbx']))
    arm = next(o for o in objs if o.type == 'ARMATURE')
    tr = 0
    for o in objs:
        if o.type != 'MESH':
            continue
        nm = re.sub(r'\.\d+$', '', o.name)
        if not nm.endswith('_LOD0'):
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        o.data.materials.clear()
        o.data.materials.append(material(os.path.join(base, man['atlas']), 'skin'))
        tr = len(o.data.polygons)
    arm.location.x += x
    return arm, tr, man
