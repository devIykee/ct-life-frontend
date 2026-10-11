"""Geometry helpers for cast v2: bmesh primitives, base measurement (head / sockets) and the starter
accessory set (CT Life originals, CC0). Accessories are built parametrically from each base's own head
and torso measurements so they fit every base on the shared CTS skeleton."""
import bpy, bmesh, math
import numpy as np
from mathutils import Vector, Matrix
from v2_common import *

# ------------------------------------------------------------------ primitives
def bm_sphere(bm, center, radii, seg=16, rings=10, keep=None):
    geom = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    verts = geom['verts']
    for v in verts:
        v.co = Vector((center[0] + v.co.x * radii[0], center[1] + v.co.y * radii[1], center[2] + v.co.z * radii[2]))
    if keep:
        kill = [f for f in {f for v in verts for f in v.link_faces} if not keep(f.calc_center_median())]
        bmesh.ops.delete(bm, geom=kill, context='FACES')
    return [v for v in verts if v.is_valid]

def bm_box(bm, center, size, rot=None):
    geom = bmesh.ops.create_cube(bm, size=1.0)
    m = Matrix.Diagonal((size[0], size[1], size[2], 1))
    if rot is not None:
        m = rot.to_4x4() @ m
    for v in geom['verts']:
        v.co = (m @ v.co) + Vector(center)
    return geom['verts']

def bm_cyl(bm, p0, p1, r0, r1=None, seg=12, caps=True):
    r1 = r0 if r1 is None else r1
    p0, p1 = Vector(p0), Vector(p1)
    axis = p1 - p0
    q = Vector((0, 0, 1)).rotation_difference(axis.normalized())
    geom = bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r0, radius2=r1, depth=axis.length)
    for v in geom['verts']:
        v.co = q @ (v.co + Vector((0, 0, axis.length / 2))) + p0
    return geom['verts']

def bm_torus(bm, center, R, r, seg=24, rseg=8, normal=(0, 0, 1), scale=(1, 1, 1), arc=1.0):
    q = Vector((0, 0, 1)).rotation_difference(Vector(normal).normalized())
    rows = []
    n = seg if arc >= 1.0 else seg + 1
    for i in range(n):
        a = 2 * math.pi * arc * i / seg
        row = []
        for j in range(rseg):
            b = 2 * math.pi * j / rseg
            x = (R + r * math.cos(b)) * math.cos(a) * scale[0]
            y = (R + r * math.cos(b)) * math.sin(a) * scale[1]
            z = r * math.sin(b) * scale[2]
            row.append(bm.verts.new(q @ Vector((x, y, z)) + Vector(center)))
        rows.append(row)
    nm = len(rows) if arc >= 1.0 else len(rows) - 1
    for i in range(nm):
        i2 = (i + 1) % len(rows)
        for j in range(rseg):
            j2 = (j + 1) % rseg
            bm.faces.new([rows[i][j], rows[i2][j], rows[i2][j2], rows[i][j2]])
    return [v for r_ in rows for v in r_]

def tag(bm, verts_or_faces, mat):
    """Assign a palette material index to faces touching these verts (or these faces)."""
    items = [x for x in verts_or_faces if x.is_valid]
    faces = items if items and isinstance(items[0], bmesh.types.BMFace) else {f for v in items for f in v.link_faces}
    for f in faces:
        f.material_index = mat

# ------------------------------------------------------------------ measurement
def bone_world(arm, name, tail=False):
    b = arm.data.bones[name]
    return arm.matrix_world @ (b.tail_local if tail else b.head_local)

def measure(arm, body_objs):
    """Head/torso metrics in Blender space (character faces -Y). Uses vertices whose dominant weight is
    Head (head) and spine_03/clavicles (upper torso)."""
    pts_head, pts_chest = [], []
    for o in body_objs:
        gi = {g.index: g.name for g in o.vertex_groups}
        mw = o.matrix_world
        for v in o.data.vertices:
            if not v.groups:
                continue
            g = max(v.groups, key=lambda g: g.weight)
            n = gi.get(g.group)
            p = mw @ v.co
            if n == 'Head' and g.weight > 0.5:
                pts_head.append(p)
            elif n in ('spine_03', 'spine_02', 'clavicle_l', 'clavicle_r') and g.weight > 0.4:
                pts_chest.append(p)
    H = np.array(pts_head) if pts_head else np.zeros((1, 3))
    C = np.array(pts_chest) if pts_chest else np.zeros((1, 3))
    head = bone_world(arm, 'Head'); neck = bone_world(arm, 'neck_01')
    top = float(H[:, 2].max()); bot = float(H[:, 2].min())
    eye_z = bot + 0.58 * (top - bot)
    band = H[np.abs(H[:, 2] - eye_z) < 0.25 * (top - bot)]
    hw = float(np.percentile(np.abs(band[:, 0] - head.x), 96)) if len(band) else 0.08
    front = float(band[:, 1].min()) if len(band) else head.y - 0.1
    back = float(H[:, 1].max())
    cy = (front + back) / 2
    m = {
        'head_center': [float(head.x), cy, float(eye_z)], 'top_z': top, 'chin_z': bot, 'eye_z': float(eye_z),
        'half_w': hw, 'half_d': (back - front) / 2, 'front_y': front, 'back_y': back,
        'neck_z': float(neck.z), 'neck_y': float(neck.y),
        'chest_front_y': float(C[:, 1].min()), 'chest_back_y': float(C[:, 1].max()),
        'chest_z': float(bone_world(arm, 'spine_03').z), 'chest_half_w': float(np.percentile(np.abs(C[:, 0]), 95)),
        'shoulder_l': list(bone_world(arm, 'upperarm_l')), 'shoulder_r': list(bone_world(arm, 'upperarm_r')),
        'hand_l': list((bone_world(arm, 'hand_l') + bone_world(arm, 'middle_01_l')) / 2),
        'hand_r': list((bone_world(arm, 'hand_r') + bone_world(arm, 'middle_01_r')) / 2),
    }
    return m

def socket_points(m):
    """Character-space socket positions (Blender metres)."""
    cx, cy, ez = m['head_center']
    return {
        'head_top': (cx, cy, m['top_z']),
        'face': (cx, m['front_y'], m['eye_z']),
        'neck': (0.0, m['chest_front_y'] + 0.01, m['neck_z'] - 0.02),
        'back': (0.0, m['chest_back_y'], m['chest_z']),
        'shoulder_l': (m['shoulder_l'][0], m['shoulder_l'][1], m['shoulder_l'][2] + 0.06),
        'shoulder_r': (m['shoulder_r'][0], m['shoulder_r'][1], m['shoulder_r'][2] + 0.06),
        'hand_l': tuple(m['hand_l']), 'hand_r': tuple(m['hand_r']),
    }

# ------------------------------------------------------------------ starter accessories
# palette material slots used by accessories (resolved to colours in the atlas/tint step)
ACC_MATS = ['acc_dark', 'acc_light', 'acc_metal', 'acc_gold', 'acc_glow', 'acc_screen', 'acc_accent', 'acc_lens']
AM = {n: i for i, n in enumerate(ACC_MATS)}

def build_accessory(name, m):
    """Return (bmesh, socket, flags) for a starter accessory fitted to measurement m."""
    bm = bmesh.new()
    cx, cy, ez = m['head_center']
    hw, hd, top, chin = m['half_w'], m['half_d'], m['top_z'], m['chin_z']
    hh = top - chin
    flags = {}
    if name == 'pixel_face_mask':
        # curved plate over the face with a pixel-grid screen (pixels painted in the atlas)
        rows = []
        for j in range(6):
            v = j / 5
            z = chin + 0.10 * hh + v * 0.62 * hh
            row = []
            for i in range(9):
                u = -1 + 2 * i / 8
                ang = u * 0.95
                r = hw * 1.06
                row.append(bm.verts.new((cx + math.sin(ang) * r, cy - math.cos(ang) * (hd * 1.04) - 0.006, z)))
            rows.append(row)
        for j in range(5):
            for i in range(8):
                f = bm.faces.new([rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]])
                f.material_index = AM['acc_screen']
        strap = bm_torus(bm, (cx, cy, ez), 1.0, 0.004, 24, 4, scale=(hw * 1.05, hd * 1.05, 1))
        tag(bm, strap, AM['acc_dark'])
        flags = {'occludes_face': True}
        return bm, 'face', flags
    if name == 'tv_head':
        w, d, h = hw * 2.5, hd * 2.3, hh * 1.05
        c = Vector((cx, cy, chin + h / 2 - 0.01))
        box = bm_box(bm, c, (w, d, h)); tag(bm, box, AM['acc_light'])
        scr = bm_box(bm, c + Vector((0, -d / 2 - 0.002, 0.005)), (w * 0.78, 0.008, h * 0.66)); tag(bm, scr, AM['acc_screen'])
        for sx in (-1, 1):
            ant = bm_cyl(bm, c + Vector((sx * w * 0.2, 0, h / 2)), c + Vector((sx * w * 0.38, 0.02, h / 2 + 0.09)), 0.004, 0.003, seg=6)
            tag(bm, ant, AM['acc_metal'])
        knob = bm_cyl(bm, c + Vector((w * 0.42, -d / 2, -h * 0.3)), c + Vector((w * 0.42, -d / 2 - 0.012, -h * 0.3)), 0.012, seg=10); tag(bm, knob, AM['acc_dark'])
        flags = {'replaces_head': True, 'hides_hair': True, 'occludes_face': True}
        return bm, 'head_top', flags
    if name == 'visor':
        rows = []
        for j, dz in enumerate((-0.026, 0.0, 0.026)):
            row = []
            for i in range(13):
                a = -0.48 * math.pi + 0.96 * math.pi * i / 12
                row.append(bm.verts.new((cx + math.sin(a) * (hw + 0.02), cy - math.cos(a) * (hd + 0.016), ez + dz)))
            rows.append(row)
        for j in range(2):
            for i in range(12):
                f = bm.faces.new([rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]])
                f.material_index = AM['acc_glow']
        band = bm_torus(bm, (cx, cy, ez + 0.03), 1.0, 0.005, 24, 4, scale=(hw + 0.012, hd + 0.012, 1)); tag(bm, band, AM['acc_dark'])
        flags = {'double_sided': True}
        return bm, 'face', flags
    if name == 'helmet':
        c = (cx, cy + 0.004, ez + 0.01)
        shell = bm_sphere(bm, c, (hw + 0.035, hd + 0.035, (top - ez) + 0.035), 20, 14, keep=lambda p: p.z > chin - 0.02)
        tag(bm, shell, AM['acc_accent'])
        # dark visor window on the front
        vis = [f for f in bm.faces if f.calc_center_median().y < cy - hd * 0.55 and chin + 0.15 * hh < f.calc_center_median().z < ez + 0.06]
        tag(bm, vis, AM['acc_lens'])
        stripe = [f for f in bm.faces if abs(f.calc_center_median().x - cx) < 0.018 and f.calc_center_median().z > ez + 0.05]
        tag(bm, stripe, AM['acc_light'])
        flags = {'hides_hair': True, 'occludes_face': True}
        return bm, 'head_top', flags
    if name == 'laser_eyes':
        ex = hw * 0.42
        for sx in (-1, 1):
            p0 = Vector((cx + sx * ex, m['front_y'] - 0.004, ez))
            glow = bm_sphere(bm, p0, (0.016, 0.006, 0.016), 10, 6); tag(bm, glow, AM['acc_glow'])
            beam = bm_cyl(bm, p0 + Vector((0, -0.004, 0)), p0 + Vector((sx * 0.08, -0.55, -0.05)), 0.007, 0.028, seg=10)
            tag(bm, beam, AM['acc_glow'])
        flags = {'emissive': True}
        return bm, 'face', flags
    if name == 'chain':
        nz = m['neck_z']
        ring = bm_torus(bm, (0, (m['neck_y'] + m['chest_front_y']) / 2, nz - 0.06), 1.0, 0.006, 28, 6,
                        normal=(0, -0.5, 1), scale=(m['chest_half_w'] * 0.55, (m['neck_y'] - m['chest_front_y']) * 0.9 + 0.03, 1))
        tag(bm, ring, AM['acc_gold'])
        pend = bm_cyl(bm, (0, m['chest_front_y'] - 0.008, nz - 0.17), (0, m['chest_front_y'] - 0.018, nz - 0.17), 0.028, seg=16)
        tag(bm, pend, AM['acc_gold'])
        return bm, 'neck', {}
    if name == 'headphones':
        band = bm_torus(bm, (cx, cy, ez + 0.01), 1.0, 0.009, 20, 6, normal=(0, 1, 0), scale=(hw + 0.03, (top - ez) + 0.03, 1), arc=0.5)
        for v in band:
            v.co.z = ez + 0.01 + abs(v.co.z - (ez + 0.01))
        tag(bm, band, AM['acc_dark'])
        for sx in (-1, 1):
            cup = bm_cyl(bm, (cx + sx * (hw + 0.008), cy + 0.004, ez - 0.012), (cx + sx * (hw + 0.042), cy + 0.004, ez - 0.012), 0.042, seg=14)
            tag(bm, cup, AM['acc_accent'])
        return bm, 'head_top', {}
    if name == 'small_wings':
        bz = m['chest_z'] + 0.11; by = m['chest_back_y'] + 0.01
        for sx in (-1, 1):
            rows = []
            for j in range(4):
                t = j / 3
                row = []
                for i in range(6):
                    u = i / 5
                    x = sx * (0.03 + 0.26 * u)
                    z = bz - 0.02 + 0.24 * u * (1 - 0.45 * u) - 0.20 * t * (1 - 0.5 * u) - 0.025 * math.sin(u * 3.1)
                    y = by + 0.03 + 0.16 * u + 0.012 * math.sin(j * 1.7 + i)
                    row.append(bm.verts.new((x, y, z)))
                rows.append(row)
            for j in range(3):
                for i in range(5):
                    q = [rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]]
                    f = bm.faces.new(q if sx > 0 else q[::-1])
                    f.material_index = AM['acc_light']
        return bm, 'back', {'double_sided': True}
    if name == 'shoulder_drone':
        s = Vector(m['shoulder_r']) + Vector((-0.06, 0.02, 0.22))
        body = bm_box(bm, s, (0.10, 0.10, 0.035)); tag(bm, body, AM['acc_dark'])
        for ax, ay in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            p = s + Vector((ax * 0.08, ay * 0.08, 0.01))
            arm_ = bm_cyl(bm, s, p, 0.006, seg=6); tag(bm, arm_, AM['acc_dark'])
            rot = bm_cyl(bm, p + Vector((0, 0, 0.004)), p + Vector((0, 0, 0.010)), 0.045, seg=14); tag(bm, rot, AM['acc_accent'])
        eye = bm_sphere(bm, s + Vector((0, -0.052, -0.004)), (0.016, 0.006, 0.012), 10, 6); tag(bm, eye, AM['acc_glow'])
        return bm, 'shoulder_r', {'emissive': True}
    raise KeyError(name)

