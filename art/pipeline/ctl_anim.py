"""Animation helpers for the CT Life skeleton: layered-action builder, FBX clip exporter, and a small
pose language (directions, deltas, two-bone IK) used to hand-key emotes on top of retargeted base clips.

Character space = the rig's armature space: front is -Y, the character's left is +X, up is +Z.
"""
import bpy, math, os
from mathutils import Matrix, Quaternion, Vector, Euler

FPS = 30
# parents must come before children; head/neck before arms so IK targets can follow the head
SOLVE_ORDER = ['root', 'pelvis', 'spine_01', 'spine_02', 'spine_03', 'neck_01', 'Head', 'jaw', 'eye_l', 'eye_r',
               'clavicle_l', 'upperarm_l', 'lowerarm_l', 'hand_l', 'index_01_l', 'thumb_01_l',
               'clavicle_r', 'upperarm_r', 'lowerarm_r', 'hand_r', 'index_01_r', 'thumb_01_r',
               'thigh_l', 'calf_l', 'foot_l', 'ball_l', 'thigh_r', 'calf_r', 'foot_r', 'ball_r']


class Rig:
    def __init__(self, obj):
        self.obj = obj
        self.rest = {b.name: b.matrix_local.copy() for b in obj.data.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in obj.data.bones}
        self.length = {b.name: b.length for b in obj.data.bones}
        self.order = [b for b in SOLVE_ORDER if b in self.rest] + [b for b in self.rest if b not in SOLVE_ORDER]

    def rest_rel(self, b):
        p = self.parent[b]
        return (self.rest[p].inverted() @ self.rest[b]) if p else self.rest[b]

    def sample(self, action, frame):
        """Armature-space pose matrices of `action` at `frame`."""
        o = self.obj
        o.animation_data_create()
        o.animation_data.action = action
        if action.slots:
            o.animation_data.action_slot = action.slots[0]
        bpy.context.scene.frame_set(int(frame))
        return {pb.name: pb.matrix.copy() for pb in o.pose.bones}

    def to_basis(self, mats):
        """Armature-space matrices -> (local quaternion, local translation) per bone."""
        out = {}
        for b in self.order:
            p = self.parent[b]
            pm = mats[p] if p else Matrix.Identity(4)
            basis = self.rest_rel(b).inverted() @ pm.inverted() @ mats[b]
            out[b] = (basis.to_quaternion(), basis.translation.copy())
        return out


def build_action(rig, name, basis_frames, loop, source, loc_bones=('pelvis',)):
    """basis_frames: list of {bone: (quat, loc)}. Writes a layered action (Blender 4.4+ API)."""
    a = bpy.data.actions.new(name)
    slot = a.slots.new(id_type='OBJECT', name=rig.obj.name)
    cb = a.layers.new('Layer').strips.new(type='KEYFRAME').channelbag(slot, ensure=True)
    n = len(basis_frames)
    prev = {}
    quats = {b: [] for b in rig.order}
    for fr in basis_frames:
        for b in rig.order:
            q = fr[b][0].copy()
            if b in prev and prev[b].dot(q) < 0:
                q.negate()
            prev[b] = q
            quats[b].append(q)

    def put(path, idx, vals, group):
        fc = cb.fcurves.new(path, index=idx, group_name=group)
        fc.keyframe_points.add(n)
        co = []
        for i, v in enumerate(vals):
            co += [i + 1, v]
        fc.keyframe_points.foreach_set('co', co)
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'
        fc.update()
    for b in rig.order:
        for i in range(4):
            put(f'pose.bones["{b}"].rotation_quaternion', i, [q[i] for q in quats[b]], b)
    for b in loc_bones:
        for i in range(3):
            put(f'pose.bones["{b}"].location', i, [fr[b][1][i] for fr in basis_frames], b)
    a.use_fake_user = True
    a['ctl_loop'] = bool(loop)
    a['ctl_source'] = source
    return a


def bind_proxy(rig_obj):
    """A 1-triangle mesh skinned to the pelvis. Without a skinned mesh the FBX exporter writes no bind pose and
    the importers (Blender at least) take the first animated frame as the skeleton rest pose."""
    name = '_bind_proxy'
    o = bpy.data.objects.get(name)
    if o:
        return o
    me = bpy.data.meshes.new(name)
    p = rig_obj.data.bones['pelvis'].head_local
    me.from_pydata([p + Vector((0.01, 0, 0)), p + Vector((-0.01, 0, 0)), p + Vector((0, 0, 0.01))], [], [(0, 1, 2)])
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = rig_obj
    g = o.vertex_groups.new(name='pelvis')
    g.add([0, 1, 2], 1.0, 'REPLACE')
    m = o.modifiers.new('Armature', 'ARMATURE')
    m.object = rig_obj
    return o


def export_clip(rig_obj, action, path):
    sc = bpy.context.scene
    proxy = bind_proxy(rig_obj)
    rig_obj.animation_data_create()
    rig_obj.animation_data.action = action
    rig_obj.animation_data.action_slot = action.slots[0]
    for pb in rig_obj.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    f0, f1 = action.frame_range
    sc.frame_start, sc.frame_end = int(f0), int(f1)
    bpy.ops.object.select_all(action='DESELECT')
    rig_obj.select_set(True)
    proxy.select_set(True)
    bpy.context.view_layer.objects.active = rig_obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=True, armature_nodetype='NULL',
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0)


# ------------------------------------------------------------------ pose language
def D(x, y, z, roll=0.0):
    """absolute bone direction (character space), optional roll (deg) about the new axis"""
    return ('dir', Vector((x, y, z)).normalized(), roll)


def R(rx=0.0, ry=0.0, rz=0.0):
    """delta rotation (deg) about character X (pitch), Y (roll), Z (yaw) at the bone head"""
    return ('rot', Vector((rx, ry, rz)))


def L(rx=0.0, ry=0.0, rz=0.0):
    """delta rotation (deg) about the bone's own local axes (finger curls, palm roll)"""
    return ('local', Vector((rx, ry, rz)))


def P(x=0.0, y=0.0, z=0.0):
    """pelvis translation offset (m)"""
    return ('loc', Vector((x, y, z)))


def LR(x=0.0, y=0.0, z=0.0, rx=0.0, ry=0.0, rz=0.0):
    """pelvis translation (m) + rotation (deg, character axes) in one spec"""
    return ('locrot', Vector((x, y, z)), Vector((rx, ry, rz)))


def IK(landmark, offset, pole):
    """two-bone IK for an arm/leg: put the end (wrist/ankle) at landmark + offset, elbow/knee towards pole"""
    return ('ik', landmark, Vector(offset), Vector(pole).normalized())


def mirror(pose):
    """left<->right mirror of a pose dict"""
    out = {}
    for b, spec in pose.items():
        nb = b[:-2] + ('_r' if b.endswith('_l') else '_l') if b[-2:] in ('_l', '_r') else b
        k = spec[0]
        if k == 'dir':
            v = spec[1]
            out[nb] = ('dir', Vector((-v.x, v.y, v.z)), -spec[2])
        elif k == 'rot':
            v = spec[1]
            out[nb] = ('rot', Vector((v.x, -v.y, -v.z)))
        elif k == 'local':
            out[nb] = spec
        elif k == 'loc':
            out[nb] = ('loc', Vector((-spec[1].x, spec[1].y, spec[1].z)))
        elif k == 'locrot':
            out[nb] = ('locrot', Vector((-spec[1].x, spec[1].y, spec[1].z)), Vector((spec[2].x, -spec[2].y, -spec[2].z)))
        elif k == 'ik':
            lm = spec[1]
            lm2 = lm[:-2] + ('_r' if lm.endswith('_l') else '_l') if lm[-2:] in ('_l', '_r') else lm
            o = spec[2]
            out[nb] = ('ik', lm2, Vector((-o.x, o.y, o.z)), Vector((-spec[3].x, spec[3].y, spec[3].z)))
    return out


def both(pose):
    d = dict(pose)
    d.update(mirror(pose))
    return d


def _smooth(t):
    return t * t * (3 - 2 * t)


def _lerp_spec(a, b, t):
    if a[0] != b[0]:
        return b if t > 0.5 else a
    k = a[0]
    if k == 'dir':
        v = a[1].lerp(b[1], t)
        if v.length < 1e-6:
            v = b[1]
        return ('dir', v.normalized(), a[2] + (b[2] - a[2]) * t)
    if k in ('rot', 'local', 'loc'):
        return (k, a[1].lerp(b[1], t))
    if k == 'locrot':
        return (k, a[1].lerp(b[1], t), a[2].lerp(b[2], t))
    if k == 'ik':
        if a[1] != b[1]:
            return b if t > 0.5 else a
        return ('ik', a[1], a[2].lerp(b[2], t), a[3].lerp(b[3], t).normalized())
    return b


def spec_at(keys, bone, t, loop_dur=None):
    """keys: [(time, pose)]; returns the interpolated spec for bone at time t (or None)."""
    ks = [(kt, p[bone]) for kt, p in keys if bone in p]
    if not ks:
        return None
    if t <= ks[0][0]:
        return ks[0][1]
    for (t0, s0), (t1, s1) in zip(ks, ks[1:]):
        if t0 <= t <= t1:
            u = (t - t0) / max(1e-6, t1 - t0)
            return _lerp_spec(s0, s1, _smooth(u))
    return ks[-1][1]


AXES = {'x': Vector((1, 0, 0)), 'y': Vector((0, 1, 0)), 'z': Vector((0, 0, 1))}


def _rot_about_head(M, q):
    h = M.translation.copy()
    R4 = q.to_matrix().to_4x4()
    return Matrix.Translation(h) @ R4 @ Matrix.Translation(-h) @ M


def _align(M, d, roll_deg, bone_len):
    cur = (M.to_3x3() @ Vector((0, 1, 0))).normalized()   # Blender bones point along local +Y
    q = cur.rotation_difference(d)
    M2 = _rot_about_head(M, q)
    if roll_deg:
        M2 = _rot_about_head(M2, Quaternion(d, math.radians(roll_deg)))
    return M2


def _two_bone(S, T, a, b, pole):
    v = T - S
    d = min(max(v.length, 1e-4), (a + b) * 0.999)
    dirv = v.normalized()
    cos_a = (a * a + d * d - b * b) / (2 * a * d)
    cos_a = max(-1.0, min(1.0, cos_a))
    sin_a = math.sqrt(1 - cos_a * cos_a)
    perp = pole - dirv * pole.dot(dirv)
    if perp.length < 1e-5:
        perp = Vector((0, 0, -1)) - dirv * dirv.z
    perp.normalize()
    E = S + dirv * (a * cos_a) + perp * (a * sin_a)
    T2 = S + dirv * d
    return (E - S).normalized(), (T2 - E).normalized()


def solve_pose(rig, base_mats, pose_specs, weight):
    """Apply interpolated specs on top of base matrices. Returns new armature-space matrices."""
    out = {}
    pending_dir = {}
    for b in rig.order:
        p = rig.parent[b]
        local = (base_mats[p].inverted() @ base_mats[b]) if p else base_mats[b]
        M0 = (out[p] @ local) if p else local.copy()
        spec = pose_specs.get(b)
        M = M0
        if b in pending_dir:
            M = _align(M0, pending_dir.pop(b), 0.0, rig.length[b])
        if spec:
            k = spec[0]
            if k == 'dir':
                M = _align(M0, spec[1], spec[2], rig.length[b])
            elif k == 'rot':
                e = spec[1]
                q = Euler((math.radians(e.x), math.radians(e.y), math.radians(e.z)), 'XYZ').to_quaternion()
                M = _rot_about_head(M0, q)
            elif k == 'local':
                e = spec[1]
                q = Euler((math.radians(e.x), math.radians(e.y), math.radians(e.z)), 'XYZ').to_quaternion()
                M = M0 @ q.to_matrix().to_4x4()
            elif k == 'loc':
                M = Matrix.Translation(spec[1]) @ M0
            elif k == 'locrot':
                e = spec[2]
                q = Euler((math.radians(e.x), math.radians(e.y), math.radians(e.z)), 'XYZ').to_quaternion()
                M = Matrix.Translation(spec[1]) @ _rot_about_head(M0, q)
            elif k == 'ik':
                lm = out.get(spec[1]) or base_mats[spec[1]]
                target = lm.translation + spec[2]
                child = {'upperarm_l': 'lowerarm_l', 'upperarm_r': 'lowerarm_r', 'thigh_l': 'calf_l', 'thigh_r': 'calf_r'}[b]
                d1, d2 = _two_bone(M0.translation, target, rig.length[b], rig.length[child], spec[3])
                M = _align(M0, d1, 0.0, rig.length[b])
                pending_dir[child] = d2
        if M is not M0 and weight < 1.0:
            q = M0.to_quaternion().slerp(M.to_quaternion(), weight)
            tr = M0.translation.lerp(M.translation, weight)
            M = Matrix.Translation(tr) @ q.to_matrix().to_4x4()
        out[b] = M
    return out


# approximate flesh radius around each bone (m): keeps lying / kneeling poses above the floor
BONE_RADIUS = {'pelvis': 0.13, 'spine_01': 0.13, 'spine_02': 0.13, 'spine_03': 0.14, 'neck_01': 0.07, 'Head': 0.11,
               'clavicle_l': 0.07, 'clavicle_r': 0.07, 'upperarm_l': 0.05, 'upperarm_r': 0.05, 'lowerarm_l': 0.045,
               'lowerarm_r': 0.045, 'hand_l': 0.03, 'hand_r': 0.03, 'thigh_l': 0.08, 'thigh_r': 0.08, 'calf_l': 0.06,
               'calf_r': 0.06, 'foot_l': 0.0, 'foot_r': 0.0, 'ball_l': 0.0, 'ball_r': 0.0}


def floor_lift(rig, mats, floor=0.0):
    """How far the whole pose must be raised so no bone segment (with its flesh radius) is below the floor."""
    low = 1e9
    for b, rad in BONE_RADIUS.items():
        if b not in mats:
            continue
        M = mats[b]
        h = M.translation
        t = M @ Vector((0, rig.length[b], 0))
        low = min(low, h.z - rad, t.z - rad)
    return max(0.0, floor - low)
