"""Face pass helpers: stylised eye textures, facial landmarks, and procedural blend shapes
(blink + a minimal lip-sync set) computed from geometry, so they work on any LOD / bake.

All coordinates are the rig's rest (T-pose) armature space = object space of the exported meshes
(front is -Y, the character's left is +X, up is +Z).
"""
import math
import numpy as np
from mathutils import Vector

# ------------------------------------------------------------------ stylised eyes (made by us, CC0)
# MakeHuman eye UV layout: two eyeballs, iris centres at UV (0.288, 0.292) and (0.705, 0.702).
IRIS_UV = ((0.288, 0.292), (0.705, 0.702))
IRIS_R = 0.128          # stock MakeHuman ~0.112 -> ~15 % larger iris
PUPIL_R = 0.045
LASH_UV = (0.02, 0.92, 0.10, 1.0)   # dark corner of the eye tile used by the eyelash geometry
EYE_COLOURS = {          # name -> (iris main, iris outer ring)  sRGB hex
    'brown': ('6B4426', '24150C'),
    'darkbrown': ('3E2716', '140B06'),
    'blue': ('3D7CC9', '15294A'),
    'green': ('4E9152', '18361C'),
    'grey': ('7E8E9C', '2E3740'),
    'hazel': ('8C6A2E', '33240E'),
}


def _hex(h):
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], np.float32)


def eye_texture(main_hex, ring_hex, size=512):
    """sRGB float image (size, size, 3), row 0 = top (PNG order)."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    u = (xx + 0.5) / size
    v = 1.0 - (yy + 0.5) / size
    img = np.empty((size, size, 3), np.float32)
    img[:] = _hex('F3EEEA')                              # warm white sclera
    main, ring = _hex(main_hex), _hex(ring_hex)
    rng = np.random.default_rng(5)
    for cu, cv in IRIS_UV:
        d = np.hypot(u - cu, v - cv)
        # soft sclera shading towards the edge of the eyeball (depth)
        img *= (1.0 - 0.10 * np.clip((d - 0.2) / 0.15, 0, 1))[:, :, None]
        t = np.clip(d / IRIS_R, 0, 1)
        ang = np.arctan2(v - cv, u - cu)
        fibres = 0.92 + 0.08 * np.sin(ang * 37.0 + rng.random() * 6) * np.sin(ang * 11.0)
        lighter = main * 1.35
        iris = (lighter[None, None, :] * (1 - t[:, :, None]) ** 0.6 * 0.0 + main[None, None, :]) * fibres[:, :, None]
        iris = iris * (1 - 0.35 * (1 - t[:, :, None]))   # slightly darker towards the pupil...
        iris = iris + (lighter - main)[None, None, :] * np.clip(1 - np.abs(t - 0.55) / 0.25, 0, 1)[:, :, None] * 0.6  # ...lighter band
        ring_w = np.clip((t - 0.78) / 0.22, 0, 1)[:, :, None]
        iris = iris * (1 - ring_w) + ring[None, None, :] * ring_w           # dark limbal ring
        inside = (d < IRIS_R)[:, :, None]
        edge = np.clip((IRIS_R - d) / 0.006, 0, 1)[:, :, None]                # anti-aliased edge
        img = np.where(inside, img * (1 - edge) + iris * edge, img)
        img = np.where((d < PUPIL_R)[:, :, None], np.array([0.03, 0.025, 0.025]), img)
        # specular highlights (painted: reads on mobile without reflections)
        for (ox, oy, r, a) in ((-0.045, 0.045, 0.024, 0.95), (0.03, -0.035, 0.011, 0.6)):
            dh = np.hypot(u - (cu + ox), v - (cv + oy))
            hl = np.clip((r - dh) / (r * 0.35), 0, 1)[:, :, None] * a
            img = img * (1 - hl) + np.array([1.0, 1.0, 1.0]) * hl
    u0, v0, u1, v1 = LASH_UV
    m = ((u > u0) & (u < u1) & (v > v0) & (v < v1))[:, :, None]
    img = np.where(m, np.array([0.06, 0.045, 0.04]), img)
    return np.clip(img, 0, 1)


# ------------------------------------------------------------------ landmarks
def landmarks(rig, eyes_obj, body_obj):
    """eye centres/radius from the eyeball mesh, jaw pivot from the jaw bone, lip seam from the face profile."""
    L = {}
    for side, sgn in (('l', 1), ('r', -1)):
        pts = [eyes_obj.matrix_world @ v.co for v in eyes_obj.data.vertices if sgn * (eyes_obj.matrix_world @ v.co).x > 0]
        c = sum(pts, Vector()) / len(pts)
        L['eye_' + side] = c
        L['eye_r_' + side] = max((p - c).length for p in pts)
    jb = rig.data.bones['jaw']
    L['jaw_head'] = rig.matrix_world @ jb.head_local
    L['jaw_tail'] = rig.matrix_world @ jb.tail_local
    ez = L['eye_l'].z
    prof = sorted(((body_obj.matrix_world @ v.co) for v in body_obj.data.vertices
                   if abs((body_obj.matrix_world @ v.co).x) < 0.008 and ez - 0.10 < (body_obj.matrix_world @ v.co).z < ez - 0.03
                   and (body_obj.matrix_world @ v.co).y < L['eye_l'].y + 0.02), key=lambda p: p.z)
    # lip seam = the most recessed front-profile point between the two lip bulges
    front = [p for p in prof if p.y < L['eye_l'].y - 0.005]
    if front:
        lips = [p for p in front if ez - 0.085 < p.z < ez - 0.04]
        seam = max(lips, key=lambda p: p.y) if lips else min(front, key=lambda p: p.y)
    else:
        seam = Vector((0, L['eye_l'].y - 0.02, ez - 0.06))
    L['mouth'] = seam
    L['mouth_w'] = 0.026 if abs(L['eye_l'].x) > 0.033 else 0.024
    return L


# ------------------------------------------------------------------ blend shapes
def _smooth(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def _blink_offsets(pts, L, side, lid_pts=None):
    c, r = L['eye_' + side], L['eye_r_' + side]
    zc = c.z - 0.18 * r                   # where the lids meet
    out = []
    for p in pts:
        d = p - c
        if abs(d.x) > r * 1.35 or d.y > r * 0.3 or d.z < -r * 1.0 or d.z > r * 1.6:
            out.append(None)
            continue
        horiz = 1.0 - _smooth(r * 0.9, r * 1.35, abs(d.x))
        if d.z >= zc - c.z:               # upper lid: drops to the meeting line, falls off towards the brow
            t = (d.z - (zc - c.z)) / (r * 1.6)
            w = (1.0 - _smooth(0.45, 1.0, t)) * horiz
            q = Vector((p.x, p.y, p.z - w * (p.z - zc)))
        else:                              # lower lid: rises a little
            t = ((zc - c.z) - d.z) / (r * 1.0)
            w = (1.0 - _smooth(0.4, 1.0, t)) * horiz * 0.25
            q = Vector((p.x, p.y, p.z + w * (zc - p.z)))
        # keep the lid outside the eyeball
        dq = q - c
        if dq.y < 0 and dq.length < r + 0.0015:
            q = c + dq.normalized() * (r + 0.0015)
        out.append(q - p if w > 1e-4 else None)
    return out


def _mouth_offsets(pts, L, kind):
    m = L['mouth']
    jw = L['jaw_head']
    w = L['mouth_w']
    out = []
    for p in pts:
        d = p - m
        if d.y > 0.035 or abs(d.x) > w * 1.9 or d.z > 0.035 or d.z < -0.075:
            out.append(None)
            continue
        fall = (1.0 - _smooth(w * 1.0, w * 1.9, abs(d.x))) * (1.0 - _smooth(0.015, 0.035, d.y))
        lower = 1.0 if d.z < -0.001 else 0.0
        upper = 1.0 - lower
        jaw_w = lower * (1.0 - _smooth(-0.065, -0.075, d.z)) * fall
        off = Vector((0, 0, 0))
        def jaw_rot(deg):
            a = math.radians(deg) * jaw_w
            rel = p - jw
            ca, sa = math.cos(a), math.sin(a)
            # rotate about the X axis through the jaw pivot (opens downwards/backwards)
            y = rel.y * ca - rel.z * sa
            z = rel.y * sa + rel.z * ca
            return Vector((rel.x, y, z)) + jw - p
        lipw = fall * (1.0 - _smooth(0.012, 0.03, abs(d.z)))
        corner = _smooth(w * 0.35, w * 1.0, abs(d.x)) * lipw
        sx = 1 if d.x >= 0 else -1
        if kind == 'jaw_open':
            off = jaw_rot(10)
        elif kind == 'viseme_A':                    # "ah"
            off = jaw_rot(9) + Vector((-sx * 0.002 * corner, 0, 0))
        elif kind == 'viseme_E':                    # "eh"
            off = jaw_rot(5) + Vector((sx * 0.004 * corner, 0, 0.001 * corner))
        elif kind == 'viseme_I':                    # "ee": wide, nearly closed
            off = jaw_rot(3) + Vector((sx * 0.006 * corner, 0.002 * corner, 0.0015 * corner))
        elif kind == 'viseme_O':                    # "oh": round
            off = jaw_rot(7) + Vector((-sx * 0.006 * corner, -0.003 * lipw, 0))
        elif kind == 'viseme_U':                    # "oo": pucker
            off = jaw_rot(3) + Vector((-sx * 0.009 * corner, -0.005 * lipw, 0))
        elif kind == 'mouth_smile':
            off = Vector((sx * 0.004 * corner, 0.002 * corner, 0.004 * corner))
        elif kind == 'mouth_frown':
            off = Vector((sx * 0.001 * corner, 0, -0.004 * corner))
        out.append(off if off.length > 1e-6 else None)
    return out


SHAPES = ['blink', 'blink_l', 'blink_r', 'jaw_open', 'viseme_A', 'viseme_E', 'viseme_I', 'viseme_O', 'viseme_U',
          'mouth_smile', 'mouth_frown']


def add_face_shapes(obj, L, mouth=True):
    """Adds shape keys to obj (rest pose). Skips keys that move nothing on this mesh."""
    me = obj.data
    mw = obj.matrix_world
    inv = mw.inverted().to_3x3()
    pts = [mw @ v.co for v in me.vertices]
    if not me.shape_keys:
        obj.shape_key_add(name='Basis', from_mix=False)
    added = []
    defs = {}
    bl = _blink_offsets(pts, L, 'l')
    br = _blink_offsets(pts, L, 'r')
    defs['blink_l'] = bl
    defs['blink_r'] = br
    defs['blink'] = [a if a is not None else b for a, b in zip(bl, br)]
    if mouth:
        for k in ('jaw_open', 'viseme_A', 'viseme_E', 'viseme_I', 'viseme_O', 'viseme_U', 'mouth_smile', 'mouth_frown'):
            defs[k] = _mouth_offsets(pts, L, k)
    for name in SHAPES:
        offs = defs.get(name)
        if not offs or not any(o is not None for o in offs):
            continue
        sk = obj.shape_key_add(name=name, from_mix=False)
        sk.value = 0.0
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get('co', co)
        co = co.reshape(-1, 3)
        for i, o in enumerate(offs):
            if o is not None:
                co[i] += np.array(inv @ o)
        sk.data.foreach_set('co', co.ravel())
        added.append(name)
    return added


def eyes_with_lashes(eyes, lashes, body, name):
    """Eyeballs + MPFB eyelash cards as ONE opaque mesh: lash cards are pulled 45 % towards the lid (so they read
    as a defined lash line, not a band) and UV-mapped onto the dark lash corner of the eye tile.
    Returns (object, set of lash vertex indices)."""
    import bpy, bmesh
    from mathutils.bvhtree import BVHTree
    bmb = bmesh.new(); bmb.from_mesh(body.data); bmb.transform(body.matrix_world)
    tree = BVHTree.FromBMesh(bmb)
    bmb.free()
    e = eyes.copy(); e.data = eyes.data.copy(); e.name = e.data.name = name
    l = lashes.copy(); l.data = lashes.data.copy(); l.name = l.data.name = name + '_lash'
    for o in (e, l):
        bpy.context.scene.collection.objects.link(o)
        for k in list(o.keys()):
            del o[k]
        uvs = o.data.uv_layers
        act = uvs.active.name if uvs.active else None
        for uv in list(uvs):
            if uv.name != act:
                uvs.remove(uv)
        if uvs.active:
            uvs.active.name = 'UVMap'
    for v in l.data.vertices:
        p = l.matrix_world @ v.co
        loc, nor, fi, d = tree.find_nearest(p)
        if loc is not None:
            v.co = l.matrix_world.inverted() @ (p + (loc - p) * 0.45)
    # lash cards are dense (368 tris): keep ~130
    dm = l.modifiers.new('Dec', 'DECIMATE'); dm.ratio = 0.35
    bpy.ops.object.select_all(action='DESELECT')
    l.select_set(True); bpy.context.view_layer.objects.active = l
    bpy.ops.object.modifier_apply(modifier='Dec')
    u0, v0, u1, v1 = LASH_UV
    uv = l.data.uv_layers.active.data
    for lp in uv:
        lp.uv = ((u0 + u1) / 2, (v0 + v1) / 2)
    l.data.materials.clear()
    for m in e.data.materials:
        l.data.materials.append(m)
    # lashes ride the Head bone (blink is a blend shape)
    l.vertex_groups.clear()
    g = l.vertex_groups.new(name='Head')
    g.add([v.index for v in l.data.vertices], 1.0, 'REPLACE')
    n_eye = len(e.data.vertices)
    bpy.ops.object.select_all(action='DESELECT')
    e.select_set(True); l.select_set(True)
    bpy.context.view_layer.objects.active = e
    bpy.ops.object.join()
    lash_idx = set(range(n_eye, len(e.data.vertices)))
    return e, lash_idx


def add_blink_only(obj, L, only):
    """blink keys that move only the given vertex indices (lashes inside the eyes mesh)."""
    import numpy as np
    me = obj.data
    mw = obj.matrix_world
    inv = mw.inverted().to_3x3()
    pts = [mw @ v.co for v in me.vertices]
    if not me.shape_keys:
        obj.shape_key_add(name='Basis', from_mix=False)
    bl = _blink_offsets(pts, L, 'l')
    br = _blink_offsets(pts, L, 'r')
    for name, offs in (('blink', [a if a is not None else b for a, b in zip(bl, br)]), ('blink_l', bl), ('blink_r', br)):
        sk = obj.shape_key_add(name=name, from_mix=False)
        sk.value = 0.0
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get('co', co)
        co = co.reshape(-1, 3)
        for i, o in enumerate(offs):
            if o is not None and i in only:
                co[i] += np.array(inv @ o)
        sk.data.foreach_set('co', co.ravel())
