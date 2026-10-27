"""Locomotion v3: retarget real mocap (CMU ASF/AMC -> BVH, 100STYLE Xsens BVH) onto a CT Life character rig, make
loops seamless and in place, plant the feet with leg IK, relax the hands, keep the arms off the body, export one FBX
per clip (armature + 1-tri bind proxy, 30 fps) and a JSON report with stride / contact measurements.

  Blender -b --factory-startup -P tools/anim/loco_v3/retarget_v3_blender.py -- --job job.json

job.json: {"target": {"name": "snow", "fbx": ".../SnowAdult_Creator.fbx", "kind": "h55"|"cts"},
           "out_dir": "...", "report": "...json", "clips": [ClipSpec, ...]}
ClipSpec: {"id", "bvh", "src": "cmu"|"s100", "mode": "loop"|"oneshot"|"turn",
           "window": [t0, t1] (seconds into the BVH),  # loop: search region; oneshot/turn: exact span
           "cycles": 1|2 (loop only), "period": expected cycle seconds (loop only),
           "keep_translation": bool (oneshot), "arm_clear": metres, "hand_follow": 0..1}

Method (per output frame, armature space; target rest is the character's T-pose, front -Y, left +X, up +Z):
  W_t(b) = D_s(b) . A(b) . R_t(b)       D_s = world rotation of the mapped source bone since ITS rest,
                                        A   = rest alignment (swing of the target rest bone onto the source rest
                                              bone; two-vector frames for hips and chest), R_t = target rest.
  Hips position and ankle goals = source positions scaled by the leg-length ratio about the standing ankle height;
  analytic two-bone leg IK reaches the scaled ankle goals (knee plane from FK, foot keeps its source orientation).
Loops: the cycle [ta, tb] is chosen where the source pose (joints relative to hips, heading removed) and velocity best
repeat; heading is rotated so travel is -Y; the mean travel velocity is subtracted (in place: planted feet travel
backward at exactly the clip's ground speed); the remaining end-start residual (rotations, hips, goals) is spread
linearly over the cycle so frame N == frame 0.
"""
import bpy, sys, os, json, math, argparse
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

FPS = 30
UP = Vector((0, 0, 1))


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--job', required=True)
    return ap.parse_args(argv)


def log(*a):
    print('[loco_v3]', *a, flush=True)


# ------------------------------------------------------------------------------------------------ bone maps
# target bone -> (source rotation bone, alignment): alignment is ('dir', src_from, src_to, tgt_to) using joint
# heads (None = bone tail), or ('frame', src_lat_l, src_lat_r, src_up_from, src_up_to, tgt_lat_l, tgt_lat_r, tgt_up_to)
H55 = dict(hips='Hips', spine='Spine', chest='Chest', neck='Neck', head='Head',
           sh_l='Shoulder.L', ua_l='UpperArm.L', fa_l='Forearm.L', hand_l='Hand.L',
           sh_r='Shoulder.R', ua_r='UpperArm.R', fa_r='Forearm.R', hand_r='Hand.R',
           th_l='Thigh.L', ca_l='Calf.L', ft_l='Foot.L', to_l='Toes.L',
           th_r='Thigh.R', ca_r='Calf.R', ft_r='Foot.R', to_r='Toes.R',
           fingers_l=['Thumb%d.L', 'Index%d.L', 'Middle%d.L', 'Ring%d.L', 'Pinky%d.L'],
           fingers_r=['Thumb%d.R', 'Index%d.R', 'Middle%d.R', 'Ring%d.R', 'Pinky%d.R'], root='Root')
CTS = dict(hips='pelvis', spine='spine_01', spine2='spine_02', chest='spine_03', neck='neck_01', head='Head',
           sh_l='clavicle_l', ua_l='upperarm_l', fa_l='lowerarm_l', hand_l='hand_l',
           sh_r='clavicle_r', ua_r='upperarm_r', fa_r='lowerarm_r', hand_r='hand_r',
           th_l='thigh_l', ca_l='calf_l', ft_l='foot_l', to_l='ball_l',
           th_r='thigh_r', ca_r='calf_r', ft_r='foot_r', to_r='ball_r',
           fingers_l=['thumb_0%d_l', 'index_0%d_l', 'middle_0%d_l', 'ring_0%d_l', 'pinky_0%d_l'],
           fingers_r=['thumb_0%d_r', 'index_0%d_r', 'middle_0%d_r', 'ring_0%d_r', 'pinky_0%d_r'], root='root')

# source skeletons: logical joint -> bone name
SRC = {
    'cmu': dict(hips='root', spine='upperback', chest='thorax', neck='lowerneck', head='head', spine_base='lowerback',
                neck_top='upperneck',
                sh_l='lclavicle', ua_l='lhumerus', fa_l='lradius', hand_l='lhand', wrist_l='lwrist', fing_l='lfingers',
                sh_r='rclavicle', ua_r='rhumerus', fa_r='rradius', hand_r='rhand', wrist_r='rwrist', fing_r='rfingers',
                th_l='lfemur', ca_l='ltibia', ft_l='lfoot', to_l='ltoes',
                th_r='rfemur', ca_r='rtibia', ft_r='rfoot', to_r='rtoes'),
    # 100STYLE (Xsens MVN BVH): filled in from the hierarchy (see S100_NAMES)
}
CTL_NAMES = dict(hips='pelvis', spine='spine_02', chest='spine_03', neck='neck_01', head='Head', spine_base='spine_01',
                 neck_top='Head', sh_l='clavicle_l', ua_l='upperarm_l', fa_l='lowerarm_l', hand_l='hand_l',
                 sh_r='clavicle_r', ua_r='upperarm_r', fa_r='lowerarm_r', hand_r='hand_r',
                 th_l='thigh_l', ca_l='calf_l', ft_l='foot_l', to_l='ball_l',
                 th_r='thigh_r', ca_r='calf_r', ft_r='foot_r', to_r='ball_r')
S100_NAMES = dict(hips='Hips', spine='Chest', chest='Chest3', neck='Neck', head='Head', spine_base='Chest',
                  neck_top='Head',
                  sh_l='LeftCollar', ua_l='LeftShoulder', fa_l='LeftElbow', hand_l='LeftWrist', wrist_l='LeftWrist',
                  fing_l=None,
                  sh_r='RightCollar', ua_r='RightShoulder', fa_r='RightElbow', hand_r='RightWrist', wrist_r='RightWrist',
                  fing_r=None,
                  th_l='LeftHip', ca_l='LeftKnee', ft_l='LeftAnkle', to_l='LeftToe',
                  th_r='RightHip', ca_r='RightKnee', ft_r='RightAnkle', to_r='RightToe')


def import_target(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, ignore_leaf_bones=False, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE')
    meshes = [o for o in new if o.type == 'MESH']
    for o in new:
        if o.type not in ('ARMATURE', 'MESH'):
            bpy.data.objects.remove(o, do_unlink=True)
    arm.animation_data_clear()
    return arm, meshes


def import_fbx_source(path, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, ignore_leaf_bones=False, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    ob = next(o for o in new if o.type == 'ARMATURE')
    for o in new:
        if o is not ob:
            bpy.data.objects.remove(o, do_unlink=True)
    ob.name = name
    act = ob.animation_data.action
    f0, f1 = act.frame_range
    return ob, float(bpy.context.scene.render.fps), int(f1 - f0) + 1, f0


def import_bvh(path, name):
    before = set(bpy.data.objects)
    bpy.ops.import_anim.bvh(filepath=path, target='ARMATURE', global_scale=1.0, frame_start=1, use_fps_scale=False,
                            update_scene_fps=False, update_scene_duration=False, rotate_mode='QUATERNION',
                            axis_forward='-Z', axis_up='Y')
    ob = next(o for o in bpy.data.objects if o not in before)
    ob.name = name
    hdr = open(path).read().split('MOTION', 1)[1].split('\n', 4)
    ft = float([l for l in hdr if l.startswith('Frame Time')][0].split(':')[1])
    nfr = int([l for l in hdr if l.startswith('Frames')][0].split(':')[1])
    return ob, 1.0 / ft, nfr


# ------------------------------------------------------------------------------------------------ math helpers
def swing(a, b):
    return a.normalized().rotation_difference(b.normalized())


def frame_from(lat, up):
    """orthonormal frame (columns: lateral(+X), forward(-Y)... ) from a lateral (L - R) and an up vector."""
    x = lat.normalized()
    z = (up - x * up.dot(x)).normalized()
    y = z.cross(x)
    m = Matrix((x, y, z)).transposed()
    return m


def yaw_q(deg):
    return Quaternion(UP, math.radians(deg))


def heading_of(lat):
    """yaw (deg) of the facing direction for a lateral vector (L - R); 0 = facing -Y."""
    f = lat.cross(UP)
    return math.degrees(math.atan2(f.x, -f.y))


class Source:
    def __init__(self, ob, fps, nfr, names, unit, f0=1.0):
        self.ob, self.fps, self.nfr, self.n, self.unit, self.f0 = ob, fps, nfr, names, unit, f0
        self.rest = {b.name: b.matrix_local.copy() for b in ob.data.bones}
        self.mw = ob.matrix_world.copy()

    def set_time(self, t):
        f = self.f0 + t * self.fps
        fi = int(math.floor(f))
        bpy.context.scene.frame_set(fi, subframe=f - fi)

    def head_rest(self, key):
        return (self.mw @ self.rest[self.n[key]].translation) * self.unit

    def tail_rest(self, key):
        b = self.ob.data.bones[self.n[key]]
        return (self.mw @ b.tail_local) * self.unit

    def rot_rest(self, key):
        return (self.mw.to_3x3() @ self.rest[self.n[key]].to_3x3()).to_quaternion()

    def sample(self):
        """world rotation (quat) and head position per logical joint at the current scene time"""
        out = {}
        mw3 = self.mw.to_3x3()
        for k, bn in self.n.items():
            if bn is None or bn not in self.ob.pose.bones:
                continue
            pb = self.ob.pose.bones[bn]
            m = pb.matrix
            out[k] = ((mw3 @ m.to_3x3()).to_quaternion(), (self.mw @ m.translation) * self.unit,
                      (self.mw @ pb.tail) * self.unit)
        return out


class Target:
    def __init__(self, arm, meshes, kind):
        self.arm, self.meshes, self.kind = arm, meshes, kind
        self.m = H55 if kind == 'h55' else CTS
        bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.order = [b.name for b in bones]   # parents first (Blender keeps hierarchy order)
        self.order.sort(key=lambda n: self.depth(n))

    def depth(self, n):
        d = 0
        while self.parent[n]:
            n = self.parent[n]
            d += 1
        return d

    def head(self, key):
        return self.rest[self.m[key]].translation.copy()

    def tail(self, key):
        return self.arm.data.bones[self.m[key]].tail_local.copy()

    def rrest(self, bn):
        return self.rest[bn].to_3x3().to_quaternion()

    def rest_rel(self, bn):
        p = self.parent[bn]
        return (self.rest[p].inverted() @ self.rest[bn]) if p else self.rest[bn].copy()

    def fingers(self, side):
        out = []
        for pat in self.m['fingers_' + side]:
            chain = [pat % i for i in (1, 2, 3)]
            out.append([c for c in chain if c in self.rest])
        return out


# ------------------------------------------------------------------------------------------------ retarget core
CHAIN = ['hips', 'spine', 'chest', 'neck', 'head', 'sh_l', 'ua_l', 'fa_l', 'hand_l', 'sh_r', 'ua_r', 'fa_r', 'hand_r',
         'th_l', 'ca_l', 'ft_l', 'to_l', 'th_r', 'ca_r', 'ft_r', 'to_r']


def alignments(S, T, yaw0):
    """A(b) per logical joint: maps the target rest onto the (yaw-aligned) source rest."""
    Y = yaw_q(yaw0)
    sh = lambda k: Y @ S.head_rest(k)
    st = lambda k: Y @ S.tail_rest(k)
    A = {}
    # hips / chest: two-vector frames
    Fs = frame_from(sh('th_l') - sh('th_r'), sh('chest') - sh('hips'))
    Ft = frame_from(T.head('th_l') - T.head('th_r'), T.head('chest') - T.head('hips'))
    A['hips'] = (Fs @ Ft.inverted()).to_quaternion()
    Fs = frame_from(sh('ua_l') - sh('ua_r'), sh('neck') - sh('chest'))
    Ft = frame_from(T.head('ua_l') - T.head('ua_r'), T.head('neck') - T.head('chest'))
    A['chest'] = (Fs @ Ft.inverted()).to_quaternion()
    Fs = frame_from(sh('ua_l') - sh('ua_r'), sh('chest') - sh('spine_base'))
    Ft = frame_from(T.head('ua_l') - T.head('ua_r'), T.head('chest') - T.head('spine'))
    A['spine'] = (Fs @ Ft.inverted()).to_quaternion()
    pairs = {  # key: (src_from, src_to, tgt_to)   (None = own tail)
        'neck': ('neck', 'head', 'head'), 'head': ('head', None, None),
        'sh_l': ('sh_l', 'ua_l', 'ua_l'), 'ua_l': ('ua_l', 'fa_l', 'fa_l'), 'fa_l': ('fa_l', 'hand_l', 'hand_l'),
        'sh_r': ('sh_r', 'ua_r', 'ua_r'), 'ua_r': ('ua_r', 'fa_r', 'fa_r'), 'fa_r': ('fa_r', 'hand_r', 'hand_r'),
        'hand_l': ('hand_l', None, None), 'hand_r': ('hand_r', None, None),
        'th_l': ('th_l', 'ca_l', 'ca_l'), 'ca_l': ('ca_l', 'ft_l', 'ft_l'), 'ft_l': ('ft_l', 'to_l', 'to_l'),
        'th_r': ('th_r', 'ca_r', 'ca_r'), 'ca_r': ('ca_r', 'ft_r', 'ft_r'), 'ft_r': ('ft_r', 'to_r', 'to_r'),
        'to_l': ('to_l', None, None), 'to_r': ('to_r', None, None),
    }
    for k, (a, b, c) in pairs.items():
        ds = (sh(b) if b else st(a)) - sh(a)
        dt = (T.head(c) if c else T.tail(k)) - T.head(k)
        A[k] = swing(dt, ds)
    return A


def leg_len_src(S):
    return (S.head_rest('ca_l') - S.head_rest('th_l')).length + (S.head_rest('ft_l') - S.head_rest('ca_l')).length


def leg_len_tgt(T):
    return (T.head('ca_l') - T.head('th_l')).length + (T.head('ft_l') - T.head('ca_l')).length


def sample_source(S, times, yaw0):
    Y = yaw_q(yaw0)
    out = []
    for t in times:
        S.set_time(t)
        s = S.sample()
        out.append({k: (Y @ q, Y @ p, Y @ tl) for k, (q, p, tl) in s.items()})
    return out


def pose_feature(fr):
    """joint positions relative to hips with the hips heading removed (for loop matching)."""
    hp = fr['hips'][1]
    lat = fr['th_l'][1] - fr['th_r'][1]
    h = heading_of(lat)
    Yi = yaw_q(-h)
    keys = ['ca_l', 'ft_l', 'to_l', 'ca_r', 'ft_r', 'to_r', 'fa_l', 'hand_l', 'fa_r', 'hand_r', 'head', 'chest']
    return [Yi @ (fr[k][1] - hp) for k in keys if k in fr] + [Vector((0, 0, hp.z))]


def feat_dist(a, b):
    return sum((x - y).length_squared for x, y in zip(a, b))


def choose_cycle(S, spec, yaw0):
    t0, t1 = spec['window']
    P = spec['period'] * spec.get('cycles', 1)
    dt = spec.get('search_dt', 1.0 / 120.0)
    ts = [t0 + i * dt for i in range(int((t1 - t0) / dt) + 1)]
    fr = sample_source(S, ts, yaw0)
    feats = [pose_feature(f) for f in fr]
    best = None
    tol = spec.get('period_tol', 0.15)
    lo, hi = int(round(P * (1 - tol) / dt)), int(round(P * (1 + tol) / dt))
    for i in range(0, len(ts)):
        for j in range(i + lo, min(len(ts), i + hi + 1)):
            if j + 2 >= len(ts) or i < 2:
                continue
            d = feat_dist(feats[i], feats[j])
            # velocity term (pose change over 2 samples)
            va = [x - y for x, y in zip(feats[i + 2], feats[i - 2])]
            vb = [x - y for x, y in zip(feats[j + 2], feats[j - 2])]
            d += 4.0 * sum((x - y).length_squared for x, y in zip(va, vb))
            if best is None or d < best[0]:
                best = (d, ts[i], ts[j])
    return best


def solve_frame(S_fr, T, A, scale, z_src, z_tgt, offset, hand_follow=0.5, widen=0.0):
    """returns armature-space orientation quats per target bone name + hips head + ankle goals"""
    m = T.m
    W = {}
    Rr = lambda key: T.rrest(m[key])
    for key in CHAIN:
        if key not in S_fr or key not in A or key not in m:
            continue
        qs = S_fr[key][0]
        # D = q_pose . q_rest^-1 (source); rest quats were captured yaw-aligned in S_rest_q
        D = qs @ S_REST_Q[key].inverted()
        W[m[key]] = D @ A[key] @ Rr(key)
    # hands: soften toward the forearm-following orientation (CMU/Xsens hands are short, noisy segments)
    for side in ('l', 'r'):
        fa, hd = m['fa_' + side], m['hand_' + side]
        if fa in W and hd in W:
            follow = W[fa] @ (T.rrest(fa).inverted() @ T.rrest(hd))
            W[hd] = follow.slerp(W[hd], 1.0 - hand_follow)
    if 'spine2' in m and m['spine'] in W and m['chest'] in W:
        W[m['spine2']] = (W[m['spine']] @ T.rrest(m['spine']).inverted()).slerp(
            W[m['chest']] @ T.rrest(m['chest']).inverted(), 0.5) @ T.rrest(m['spine2'])
    sp = lambda p: Vector(((p.x - offset.x) * scale, (p.y - offset.y) * scale, (p.z - z_src) * scale + z_tgt))
    # place the hips from the source HIP JOINTS (pelvis roots differ a lot between skeletons: CMU's root sits ~10 cm
    # above its hip joints, Snow's 5 cm)
    mid = (S_fr['th_l'][1] + S_fr['th_r'][1]) * 0.5
    o_t = T.head('hips') - (T.head('th_l') + T.head('th_r')) * 0.5
    hq = W[m['hips']] @ T.rrest(m['hips']).inverted()
    hips = sp(mid) + hq @ o_t
    if widen:
        # open the hips by the same amount, so the knees move out with the feet (the leg IK keeps the knee plane of the
        # FK thigh: widening only the ankle goals would bend the knees inward)
        leg = (T.head('ca_l') - T.head('th_l')).length + (T.head('ft_l') - T.head('ca_l')).length
        ang = math.asin(max(-0.5, min(0.5, widen / leg)))
        fwd = hq @ Vector((0, 1, 0))
        for s_, sg in (('l', -1.0), ('r', 1.0)):
            th = m['th_' + s_]
            if th in W:
                W[th] = Quaternion(fwd, sg * ang) @ W[th]
    goals = {s: sp(S_fr['ft_' + s][1]) for s in ('l', 'r')}
    # keep each foot under ITS hip joint as in the source: shift the goals sideways by the hip-width difference
    # (a bulky rig with hip joints 21 cm out would otherwise cross its feet on a source's 9 cm hips)
    lat = hq @ Vector((1, 0, 0))
    lat.z = 0
    if lat.length > 1e-6:
        lat.normalize()
        for s_, sg in (('l', 1.0), ('r', -1.0)):
            t_half = abs((T.head('th_' + s_) - T.head('hips')).x)
            s_half = abs((S_HALF_HIP[0])) * scale
            goals[s_] = goals[s_] + lat * (sg * (t_half - s_half + widen))
    return W, hips, goals


S_REST_Q = {}
S_HALF_HIP = [0.0]


def fk(T, W, hips_pos):
    """armature-space 4x4 per bone from orientation quats W (missing -> rest-relative to parent)."""
    M = {}
    for bn in T.order:
        p = T.parent[bn]
        rr = T.rest_rel(bn)
        if p is None:
            M[bn] = T.rest[bn].copy()
            continue
        head = (M[p] @ rr).translation
        if bn == T.m['hips']:
            head = hips_pos
        if bn in W:
            M[bn] = Matrix.Translation(head) @ W[bn].to_matrix().to_4x4()
        else:
            M[bn] = M[p] @ rr
    return M


def leg_ik(T, W, M, side, goal):
    m = T.m
    th, ca, ft = m['th_' + side], m['ca_' + side], m['ft_' + side]
    H = M[th].translation
    K = M[ca].translation
    F = M[ft].translation
    l1 = (T.rest[ca].translation - T.rest[th].translation).length
    l2 = (T.rest[ft].translation - T.rest[ca].translation).length
    d = goal - H
    dl = min(d.length, (l1 + l2) * 0.9999)
    dn = d.normalized()
    # knee plane from FK
    pole = (K - H) - dn * (K - H).dot(dn)
    if pole.length < 1e-6:
        pole = Vector((0, -1, 0))
    pole.normalize()
    a = (l1 * l1 + dl * dl - l2 * l2) / (2 * l1 * dl)
    a = max(-1.0, min(1.0, a))
    ang = math.acos(a)
    Kn = H + dn * (l1 * math.cos(ang)) + pole * (l1 * math.sin(ang))
    Fn = H + dn * dl
    q1 = swing(K - H, Kn - H)
    W[th] = q1 @ W[th]
    # calf after thigh rotation
    K_after = Kn
    F_after = H + q1 @ (F - H)
    q2 = swing(F_after - K_after, Fn - K_after)
    W[ca] = q2 @ (q1 @ W[ca])
    return dl < d.length - 1e-4   # True = goal out of reach (clamped)


def relaxed_hands(T, W, curl=1.0):
    """relaxed finger curl (deg per joint, index->pinky increasing), thumb slightly in."""
    base = {0: (8, 14, 10), 1: (16, 24, 14), 2: (20, 28, 16), 3: (24, 32, 18), 4: (28, 36, 20)}
    for side in ('l', 'r'):
        hand = T.m['hand_' + side]
        if hand not in W:
            continue
        Wh = W[hand]
        hr = T.rrest(hand)
        # palm normal (rest, armature space): down (-Z) for a T-pose palms-down hand
        for fi, chain in enumerate(T.fingers(side)):
            if not chain:
                continue
            # rest-relative chain orientations, re-expressed under the posed hand
            prev_rest_q = hr
            prev_w = Wh
            for j, bn in enumerate(chain):
                rel = prev_rest_q.inverted() @ T.rrest(bn)
                w_rest_like = prev_w @ rel
                d_rest = (T.arm.data.bones[bn].tail_local - T.arm.data.bones[bn].head_local).normalized()
                if fi == 0:
                    # thumb: rotate toward the palm centre (about the hand's long axis, slight)
                    axis_rest = d_rest.cross(Vector((0, 0, -1))).normalized()
                    ang = (6, 10, 8)[j] * curl
                else:
                    axis_rest = d_rest.cross(Vector((0, 0, -1))).normalized()
                    ang = base[fi][j] * curl
                # axis in posed space: rotate the rest axis by the posed bone's rest->pose delta
                delta = w_rest_like @ T.rrest(bn).inverted()
                axis = delta @ axis_rest
                W[bn] = Quaternion(axis, math.radians(ang)) @ w_rest_like
                prev_rest_q = T.rrest(bn)
                prev_w = W[bn]


def to_basis(T, M):
    out = {}
    for bn in T.order:
        p = T.parent[bn]
        pm = M[p] if p else Matrix.Identity(4)
        b = T.rest_rel(bn).inverted() @ pm.inverted() @ M[bn]
        out[bn] = (b.to_quaternion(), b.translation.copy())
    return out


def write_action(T, name, frames_basis):
    arm = T.arm
    a = bpy.data.actions.new(name)
    slot = a.slots.new(id_type='OBJECT', name=arm.name)
    cb = a.layers.new('Layer').strips.new(type='KEYFRAME').channelbag(slot, ensure=True)
    n = len(frames_basis)
    # frame -1 = rest (outside the take) so the FBX node defaults are the T-pose
    rows = [{bn: (Quaternion(), Vector()) for bn in T.order}] + frames_basis
    hips = T.m['hips']
    prev = {}
    for bn in T.order:
        qs = []
        for fr in rows:
            q = fr[bn][0].copy()
            if bn in prev and prev[bn].dot(q) < 0:
                q.negate()
            prev[bn] = q
            qs.append(q)
        for i in range(4):
            fc = cb.fcurves.new(f'pose.bones["{bn}"].rotation_quaternion', index=i, group_name=bn)
            fc.keyframe_points.add(len(rows))
            co = []
            for k, q in enumerate(qs):
                co += [k - 1, q[i]]
            fc.keyframe_points.foreach_set('co', co)
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'
            fc.update()
        if bn == hips:
            for i in range(3):
                fc = cb.fcurves.new(f'pose.bones["{bn}"].location', index=i, group_name=bn)
                fc.keyframe_points.add(len(rows))
                co = []
                for k, fr in enumerate(rows):
                    co += [k - 1, fr[bn][1][i]]
                fc.keyframe_points.foreach_set('co', co)
                for kp in fc.keyframe_points:
                    kp.interpolation = 'LINEAR'
                fc.update()
    return a


def bind_proxy(T):
    name = '_bind_proxy'
    o = bpy.data.objects.get(name)
    if o:
        return o
    me = bpy.data.meshes.new(name)
    p = T.rest[T.m['hips']].translation
    me.from_pydata([p + Vector((0.01, 0, 0)), p + Vector((-0.01, 0, 0)), p + Vector((0, 0, 0.01))], [], [(0, 1, 2)])
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = T.arm
    g = o.vertex_groups.new(name=T.m['hips'])
    g.add([0, 1, 2], 1.0, 'REPLACE')
    mo = o.modifiers.new('Armature', 'ARMATURE')
    mo.object = T.arm
    return o


def export_fbx(T, action, n, path):
    sc = bpy.context.scene
    proxy = bind_proxy(T)
    arm = T.arm
    arm.animation_data_create()
    arm.animation_data.action = action
    arm.animation_data.action_slot = action.slots[0]
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    sc.frame_start, sc.frame_end = 0, n - 1
    sc.frame_set(-1)
    for o in T.meshes:
        o.select_set(False)
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    proxy.select_set(True)
    bpy.context.view_layer.objects.active = arm
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=False, armature_nodetype='NULL',
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0)


# ------------------------------------------------------------------------------------------------ measurements
def foot_track(T, Ms, side):
    m = T.m
    return [(M[m['ft_' + side]].translation.copy(), M[m['to_' + side]].translation.copy()) for M in Ms]





def stance_speed(T, Ms, dur):
    """backward speed (m/s, +=backward along +Y) of each planted foot in an in-place loop: toe within 2 cm of its
    lowest height, central 60% of the longest run, least squares on ankle and toe Y (same idea as Unity's
    ClipMeasure.GroundSpeed)."""
    n = len(Ms) - 1
    dt = dur / n
    res = {}
    for side in ('l', 'r'):
        tr = foot_track(T, Ms[:-1], side)
        toe_z = [t[1].z for t in tr]
        mn = min(toe_z)
        ok = [z - mn <= 0.02 for z in toe_z]
        best, bl = -1, 0
        for s0 in range(n):
            if not ok[s0] or ok[s0 - 1]:
                continue
            L = 0
            while L < n and ok[(s0 + L) % n]:
                L += 1
            if L > bl:
                best, bl = s0, L
        if bl < 3:
            res[side] = None
            continue
        a, b = best + int(bl * 0.2), best + int(bl * 0.8)
        sp = []
        for idx in (0, 1):
            xs, ys = [], []
            for k in range(a, b + 1):
                xs.append(k * dt)
                ys.append(tr[k % n][idx].y)
            mx, my = sum(xs) / len(xs), sum(ys) / len(ys)
            den = sum((x - mx) ** 2 for x in xs)
            if den > 0:
                sp.append(sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / den)
        seg = [tr[k % n][0].x for k in range(a, b + 1)]
        res[side] = {'speed': sum(sp) / len(sp) if sp else None, 'stance_frames': bl,
                     'lateral_drift_m': max(seg) - min(seg)}
    return res


def sole_clearance(T, Ms, dur):
    m = T.m
    out = []
    for M in Ms:
        out.append(min(M[m['to_l']].translation.z, M[m['to_r']].translation.z, M[m['ft_l']].translation.z,
                       M[m['ft_r']].translation.z))
    return min(out), max(out)


# ------------------------------------------------------------------------------------------------ arm clearance
class BodyProfile:
    """Elliptic cross-section profiles of the torso / thighs from the skinned mesh in rest, per bone, in that bone's
    rest frame (slices along the bone's Y axis), plus arm radii. Used to test arm sample points against the posed
    body (each point is mapped into each body bone's rest frame with that bone's current transform)."""

    def __init__(self, T):
        self.T = T
        body = max(T.meshes, key=lambda o: len(o.data.vertices))
        vg = {g.index: g.name for g in body.vertex_groups}
        mw = body.matrix_world
        m = T.m
        self.bones = [m['hips'], m['spine'], m['chest']] + ([m['spine2']] if 'spine2' in m else [])
        self.thigh = {'l': m['th_l'], 'r': m['th_r']}
        want = set(self.bones) | set(self.thigh.values()) | {m[k] for k in ('ua_l', 'fa_l', 'hand_l', 'ua_r', 'fa_r', 'hand_r')}
        pts = {b: [] for b in want}
        for v in body.data.vertices:
            for g in v.groups:
                n = vg.get(g.group)
                if n in want and g.weight > 0.5:
                    pts[n].append(mw @ v.co)
        self.prof = {}
        for b in self.bones + list(self.thigh.values()):
            inv = T.rest[b].inverted()
            loc = [inv @ p for p in pts[b]]
            sl = {}
            for q in loc:
                k = int(math.floor(q.y / 0.02))
                sl.setdefault(k, []).append(q)
            prof = {}
            for k, qs in sl.items():
                if len(qs) < 6:
                    continue
                xs = [q.x for q in qs]
                zs = [q.z for q in qs]
                cx, cz = (max(xs) + min(xs)) / 2, (max(zs) + min(zs)) / 2
                prof[k] = (cx, cz, (max(xs) - min(xs)) / 2, (max(zs) - min(zs)) / 2)
            self.prof[b] = prof
        self.radius = {}
        for key in ('ua_l', 'fa_l', 'hand_l', 'ua_r', 'fa_r', 'hand_r'):
            b = m[key]
            bone = T.arm.data.bones[b]
            h, t = bone.head_local, bone.tail_local
            d = (t - h).normalized()
            rs = []
            for p in pts[b]:
                v = p - h
                rs.append((v - d * v.dot(d)).length)
            rs.sort()
            self.radius[key] = rs[len(rs) // 2] if rs else 0.035

    def depth(self, b, M, p, r):
        """penetration depth (m, >0 = inside) of a sphere (p, r) into the body part b at pose M."""
        q = M[b].inverted() @ p
        k = int(math.floor(q.y / 0.02))
        pr = self.prof[b].get(k)
        if not pr:
            return -1.0
        cx, cz, a, c = pr
        a, c = a + r, c + r
        e = ((q.x - cx) / a) ** 2 + ((q.z - cz) / c) ** 2
        if e >= 1.0:
            return -1.0
        return (1.0 - math.sqrt(e)) * min(a, c)

    def arm_points(self, M, side):
        m = self.T.m
        out = []
        for key, f in (('ua', 0.6), ('fa', 0.0), ('fa', 0.5), ('fa', 1.0), ('hand', 0.5)):
            b = m[key + '_' + side]
            bone = self.T.arm.data.bones[b]
            ln = bone.length
            p = M[b] @ Vector((0, ln * f, 0))
            out.append((p, self.radius[key + '_' + side] * (0.8 if key == 'hand' else 1.0), key))
        return out

    def worst(self, M, side):
        w = -1.0
        for p, r, key in self.arm_points(M, side):
            parts = self.bones + ([self.thigh[side]] if key in ('fa', 'hand') else [])
            if key == 'ua':
                parts = [self.T.m['chest']] + ([self.T.m['spine2']] if 'spine2' in self.T.m else [])
            for b in parts:
                w = max(w, self.depth(b, M, p, r))
        return w


class MeshClash:
    def __init__(self, T):
        self.body = max(T.meshes, key=lambda o: len(o.data.vertices))
        me = self.body.data
        vg = {g.index: g.name for g in self.body.vertex_groups}
        m = T.m
        fing = {s: set(b for c in T.fingers(s) for b in c) for s in ('l', 'r')}
        arm = {s: {m['fa_' + s], m['hand_' + s]} | fing[s] for s in ('l', 'r')}
        trunk = {m['hips'], m['spine'], m['chest'], m['th_l'], m['th_r']} | ({m['spine2']} if 'spine2' in m else set())
        w_arm = {s: [0.0] * len(me.vertices) for s in ('l', 'r')}
        w_tr = [0.0] * len(me.vertices)
        for v in me.vertices:
            for g in v.groups:
                n = vg.get(g.group)
                for s in ('l', 'r'):
                    if n in arm[s]:
                        w_arm[s][v.index] += g.weight
                if n in trunk:
                    w_tr[v.index] += g.weight
        self.arm_polys = {s: [list(p.vertices) for p in me.polygons if all(w_arm[s][i] > 0.5 for i in p.vertices)]
                          for s in ('l', 'r')}
        self.trunk_polys = [list(p.vertices) for p in me.polygons if all(w_tr[i] > 0.7 for i in p.vertices)]

    def coords(self, T, W, h, spec, hands_done):
        W2 = dict(W)
        if not hands_done:
            relaxed_hands(T, W2, spec.get('finger_curl', 1.0))
        basis = to_basis(T, fk(T, W2, h))
        arm = T.arm
        if arm.animation_data and arm.animation_data.action:
            arm.animation_data.action = None
        for bn, (q, t) in basis.items():
            pb = arm.pose.bones[bn]
            pb.rotation_mode = 'QUATERNION'
            pb.rotation_quaternion = q
            pb.location = t
        dg = bpy.context.evaluated_depsgraph_get()
        dg.update()
        ev = self.body.evaluated_get(dg)
        me = ev.to_mesh()
        mw = self.body.matrix_world
        co = [mw @ v.co for v in me.vertices]
        ev.to_mesh_clear()
        return co

    def count(self, T, W, h, side, spec, hands_done=False):
        co = self.coords(T, W, h, spec, hands_done)
        a = BVHTree.FromPolygons(co, self.arm_polys[side])
        b = BVHTree.FromPolygons(co, self.trunk_polys)
        return len(a.overlap(b))


def rotate_arm(T, W, M, side, ang_deg):
    """abduct the whole arm chain (upper arm and below) about the shoulder, away from the body."""
    if abs(ang_deg) < 1e-3:
        return
    m = T.m
    ua = m['ua_' + side]
    sh = M[ua].translation
    arm_dir = (M[m['fa_' + side]].translation - sh).normalized()
    chest = M[m['chest']]
    out = (sh - chest.translation)
    out.z = 0
    if out.length < 1e-6:
        return
    axis = arm_dir.cross(out.normalized())
    if axis.length < 1e-6:
        return
    R = Quaternion(axis.normalized(), math.radians(ang_deg))
    chain = [ua, m['fa_' + side], m['hand_' + side]] + [b for c in T.fingers(side) for b in c]
    for b in chain:
        if b in W:
            W[b] = R @ W[b]


def smooth_max(vals, rad=4):
    """dilate then box-blur a per-frame correction curve (cyclic if loop)."""
    n = len(vals)
    d = [max(vals[max(0, i - rad):i + rad + 1]) for i in range(n)]
    return [sum(d[max(0, i - rad):i + rad + 1]) / len(d[max(0, i - rad):i + rad + 1]) for i in range(n)]


def process_clip(T, prof, spec, out_dir):
    src_kind = spec['src']
    names = {'cmu': SRC['cmu'], 's100': S100_NAMES, 'ctl': CTL_NAMES}[src_kind]
    if spec['bvh'].lower().endswith('.fbx'):
        ob, fps, nfr, f0 = import_fbx_source(spec['bvh'], 'SRC_' + spec['id'])
    else:
        (ob, fps, nfr), f0 = import_bvh(spec['bvh'], 'SRC_' + spec['id']), 1.0
    unit = spec.get('unit', 1.0)
    S = Source(ob, fps, nfr, {k: v for k, v in names.items() if v and v in ob.data.bones}, unit, f0)
    if spec.get('window') is None:
        spec['window'] = [0.0, (nfr - 1) / fps]
    yaw0 = -heading_of(S.head_rest('th_l') - S.head_rest('th_r'))
    Y0 = yaw_q(yaw0)
    S_REST_Q.clear()
    for k in S.n:
        S_REST_Q[k] = Y0 @ S.rot_rest(k)
    A = alignments(S, T, yaw0)
    S_HALF_HIP[0] = (S.head_rest('th_l') - S.head_rest('th_r')).length * 0.5
    scale = leg_len_tgt(T) / leg_len_src(S)
    mode = spec['mode']
    rep = {'id': spec['id'], 'source_bvh': spec['bvh'], 'mode': mode, 'scale_leg_ratio': round(scale, 4)}
    if mode == 'loop':
        d, ta, tb = choose_cycle(S, spec, yaw0)
        rep['cycle_match_cost'] = round(d, 5)
    else:
        ta, tb = spec['window']
    N = max(2, int(round((tb - ta) * FPS)))
    times = [ta + i * (tb - ta) / N for i in range(N + 1)]
    rep['source_window_s'] = [round(ta, 4), round(tb, 4)]
    rep['source_frames'] = [int(round(ta * fps)), int(round(tb * fps))]
    rep['source_fps'] = fps
    frs = sample_source(S, times, yaw0)
    # heading: loops face their travel (or mean facing when ~stationary); one-shots face -Y at their first frame
    hp0, hpN = frs[0]['hips'][1], frs[-1]['hips'][1]
    trav = hpN - hp0
    trav.z = 0
    dur = tb - ta
    if mode == 'loop' and trav.length / dur > 0.3:
        yaw = -math.degrees(math.atan2(trav.x, -trav.y))
    else:
        if mode == 'loop':
            lat = sum((f['th_l'][1] - f['th_r'][1] for f in frs), Vector())
        else:
            k = spec.get('face_frames', 6)
            lat = sum((f['th_l'][1] - f['th_r'][1] for f in frs[:k]), Vector())
        lat.z = 0
        yaw = -heading_of(lat)
    Yh = yaw_q(yaw)
    origin = Vector((hp0.x, hp0.y, 0))
    v = (Yh @ trav) / dur if mode == 'loop' else Vector()
    for i, f in enumerate(frs):
        t = times[i] - ta
        sub = v * t if mode == 'loop' else Vector()
        for k, (q, p, tl) in list(f.items()):
            f[k] = (Yh @ q, Yh @ (p - origin) - sub, Yh @ (tl - origin) - sub)
    if mode == 'oneshot' and spec.get('straighten', True):
        # starts / stops: remove the actor's small drift in heading and sideways travel (progressively, smoothstep),
        # so the clip ends facing and travelling exactly along its start heading (-Y)
        k = spec.get('face_frames', 6)
        latA = sum((f['th_l'][1] - f['th_r'][1] for f in frs[:k]), Vector())
        latB = sum((f['th_l'][1] - f['th_r'][1] for f in frs[-k:]), Vector())
        latA.z = latB.z = 0
        dyaw = heading_of(latB) - heading_of(latA)
        dyaw = (dyaw + 180) % 360 - 180
        n_ = len(frs) - 1
        for i, f in enumerate(frs):
            x = i / n_
            w = x * x * (3 - 2 * x)
            Rq = yaw_q(-dyaw * w)
            piv = Vector((0, 0, 0))
            for kk, (q, p, tl) in list(f.items()):
                f[kk] = (Rq @ q, Rq @ (p - piv) + piv, Rq @ (tl - piv) + piv)
        xe = frs[-1]['hips'][1].x - frs[0]['hips'][1].x
        for i, f in enumerate(frs):
            x = i / n_
            w = x * x * (3 - 2 * x)
            sh = Vector((-xe * w, 0, 0))
            for kk, (q, p, tl) in list(f.items()):
                f[kk] = (q, p + sh, tl + sh)
        rep['straightened'] = {'yaw_removed_deg': round(dyaw, 1), 'lateral_removed_m': round(xe, 3)}
    rep['travel_speed_src_mps'] = round(v.length, 4)
    # floor: standing ankle height of the source
    ank = sorted(min(f['ft_l'][1].z, f['ft_r'][1].z) for f in frs)
    z_src = spec.get('z_src', ank[max(0, len(ank) // 10)])
    z_tgt = T.head('ft_l').z
    # step_widen_m (motion pass 2026-10-26): CMU 16_22 lands its feet ~15 cm apart (11 cm at the narrowest) on these
    # cartoon-proportioned bodies with thick legs, which reads as knees closing; a measured healthy adult step width is
    # ~18 cm. Each ankle goal moves outward by this much; the leg IK opens the hips to reach it.
    widen = spec.get('step_widen_m', 0.0)
    sols = [solve_frame(f, T, A, scale, z_src, z_tgt, Vector(), spec.get('hand_follow', 0.5), widen) for f in frs]
    if widen:
        rep['step_widen_m'] = widen
    if mode == 'loop':
        W0, h0, g0 = sols[0]
        WN, hN, gN = sols[-1]
        dq = {b: (WN[b] @ W0[b].inverted()) for b in W0}
        dh = hN - h0
        dg = {s: gN[s] - g0[s] for s in g0}
        rep['closure_residual'] = {'hips_m': round(dh.length, 4),
                                   'max_rot_deg': round(max(math.degrees(q.angle) if q.angle <= math.pi else 360 - math.degrees(q.angle) for q in dq.values()), 2),
                                   'worst_bones': sorted(((round(math.degrees(min(q.angle, 2 * math.pi - q.angle)), 1), b) for b, q in dq.items()), reverse=True)[:4]}
        for i, (W, h, g) in enumerate(sols):
            c = i / N
            for b in W:
                W[b] = Quaternion().slerp(dq[b].inverted(), c) @ W[b]
            sols[i] = (W, h - dh * c, {s: g[s] - dg[s] * c for s in g})
    # level the gaze: mocap actors watch the floor of the capture volume; lift the head (60%) / neck (40%) so the mean
    # head pitch over the clip is spec['gaze_deg'] (default -4: slightly down), only when it was lower than that
    hb, nb = T.m['head'], T.m['neck']
    fwd0 = Vector((0, -1, 0))
    pitches = []
    for W, h, g in sols:
        f = (W[hb] @ T.rrest(hb).inverted()) @ fwd0
        pitches.append(math.degrees(math.asin(max(-1, min(1, f.z)))))
    mean_p = sum(pitches) / len(pitches)
    want = spec.get('gaze_deg', -4.0)
    rep['head_pitch_mean_deg_src'] = round(mean_p, 1)
    up_lim = spec.get('gaze_up_limit_deg', 4.0)
    corr_deg = 0.0
    if mean_p < want - 2:
        corr_deg = min(spec.get('max_gaze_lift_deg', 15.0), want - mean_p)
    elif mean_p > up_lim + 2:
        corr_deg = max(-15.0, up_lim - mean_p)          # stargazing actors: bring the head down to the limit
    if corr_deg and spec.get('level_gaze', True):
        for W, h, g in sols:
            lat = (W[T.m['chest']] @ T.rrest(T.m['chest']).inverted()) @ Vector((1, 0, 0))
            lat.z = 0
            lat.normalize()
            # a positive rotation about +X (the character's left) pitches a -Y facing head DOWN: lift = negative
            W[nb] = Quaternion(lat, math.radians(-corr_deg * 0.4)) @ W[nb]
            W[hb] = Quaternion(lat, math.radians(-corr_deg)) @ W[hb]
        rep['gaze_lift_deg'] = round(corr_deg, 1)
    if mode == 'loop' and spec.get('phase', 'l_plant') == 'l_plant' and v.length > 0.3:
        # start every locomotion loop at the LEFT foot plant (walk / jog / sprint share phase 0 -> they blend in sync)
        u = sols[:N]
        gz = [g['l'].z for _, _, g in u]
        mn = min(gz)
        k = next((i for i in range(N) if gz[i] - mn <= 0.02 and gz[i - 1] - mn > 0.02), 0)
        u = u[k:] + u[:k]
        sols = u + [(dict(u[0][0]), u[0][1].copy(), {s_: x.copy() for s_, x in u[0][2].items()})]
        rep['phase_shift_frames'] = k
    # hips height: CMU (ASF marker skeletons) and Xsens knees never fully extend (CMU 16_22 knee flexion never
    # drops below ~18 deg, real gait reaches ~5 deg at terminal stance), which reads as a crouched, bent-knee walk.
    # Shift the whole clip's hips so the most extended PLANTED leg reaches spec['leg_extension'] of full length
    # (0.997 ~ 9 deg knee flexion); a swing foot reaching forward may clamp harmlessly.
    l1 = (T.head('ca_l') - T.head('th_l')).length
    l2 = (T.head('ft_l') - T.head('ca_l')).length
    gz = {s_: min(g[s_].z for _, _, g in sols) for s_ in ('l', 'r')}
    dmax = 0.0
    for i, (W, h, g) in enumerate(sols):
        M = fk(T, W, h)
        for s_ in ('l', 'r'):
            gp = sols[i - 1][2][s_] if i > 0 else g[s_]
            planted = g[s_].z - gz[s_] < 0.03 and (g[s_] - gp).length * FPS < 0.6 + (v.length if mode == 'loop' else 0)
            if planted:
                dmax = max(dmax, (g[s_] - M[T.m['th_' + s_]].translation).length)
    lift = spec.get('leg_extension', 0.997) * (l1 + l2) - dmax
    lift = max(-0.08, min(spec.get('max_hips_raise', 0.04), lift))
    sols = [(W, h + Vector((0, 0, lift)), g) for W, h, g in sols]
    rep['hips_raised_m'] = round(lift, 4)
    # knee calibration offset (CMU ASF): straighten planted knees by spec['knee_offset_deg'] by raising the hips per
    # frame (only as far as every planted leg allows; smoothed), so stance legs extend like real gait (~5 deg)
    off = spec.get('knee_offset_deg', 0.0)
    if off > 0:
        def flex(d):
            c = (l1 * l1 + l2 * l2 - d * d) / (2 * l1 * l2)
            return 180.0 - math.degrees(math.acos(max(-1.0, min(1.0, c))))

        def dist(th):
            return math.sqrt(max(0.0, l1 * l1 + l2 * l2 - 2 * l1 * l2 * math.cos(math.radians(180.0 - th))))
        raise_ = []
        for i, (W, h, g) in enumerate(sols):
            M = fk(T, W, h)
            r = None
            for s_ in ('l', 'r'):
                gp = sols[i - 1][2][s_] if i > 0 else g[s_]
                planted = g[s_].z - gz[s_] < 0.03 and (g[s_] - gp).length * FPS < 0.6 + (v.length if mode == 'loop' else 0)
                if not planted:
                    continue
                hj = M[T.m['th_' + s_]].translation
                d = (g[s_] - hj).length
                th = flex(min(d, l1 + l2))
                want = max(min(th, 4.0), th - off * max(0.0, min(1.0, (60.0 - th) / 20.0)))
                dz = (hj - g[s_]).normalized().z
                rr = (dist(want) - d) / max(0.3, dz)
                r = rr if r is None else min(r, rr)
            raise_.append(r)
        # frames with no planted leg: interpolate between neighbours
        n_ = len(raise_)
        known = [i for i, r in enumerate(raise_) if r is not None]
        if known:
            for i in range(n_):
                if raise_[i] is None:
                    lo_ = max([k for k in known if k < i], default=None)
                    hi_ = min([k for k in known if k > i], default=None)
                    if lo_ is None:
                        raise_[i] = raise_[hi_]
                    elif hi_ is None:
                        raise_[i] = raise_[lo_]
                    else:
                        f_ = (i - lo_) / (hi_ - lo_)
                        raise_[i] = raise_[lo_] * (1 - f_) + raise_[hi_] * f_
            raise_ = [max(0.0, min(0.035, r)) for r in raise_]
            sm = [sum(raise_[max(0, i - 2):i + 3]) / len(raise_[max(0, i - 2):i + 3]) for i in range(n_)]
            if mode == 'loop':
                sm[-1] = sm[0]
            sols = [(W, h + Vector((0, 0, sm[i])), g) for i, (W, h, g) in enumerate(sols)]
            rep['knee_offset_hips_raise_m'] = [round(min(sm), 4), round(max(sm), 4)]
    gain = spec.get('arm_swing_gain', 1.0)
    if abs(gain - 1.0) > 1e-3:
        m_ = T.m
        for s_ in ('l', 'r'):
            sh, ua, fa, hd = m_['sh_' + s_], m_['ua_' + s_], m_['fa_' + s_], m_['hand_' + s_]
            if not all(b_ in sols[0][0] for b_ in (sh, ua)):
                continue
            rel = [W[sh].inverted() @ W[ua] for W, _, _ in sols]
            ref = rel[0]
            acc = [0.0, 0.0, 0.0, 0.0]
            for q in rel:
                q2 = q if q.dot(ref) >= 0 else -q
                for j in range(4):
                    acc[j] += q2[j]
            mean = Quaternion(acc).normalized()
            for i, (W, h, g) in enumerate(sols):
                dev = rel[i] @ mean.inverted()
                if dev.w < 0:
                    dev = -dev
                ax, ang = dev.to_axis_angle()
                new_rel = Quaternion(ax, ang * gain) @ mean
                old_ua = W[ua]
                W[ua] = W[sh] @ new_rel
                # the forearm and hand keep their pose relative to the upper arm
                for child in (fa, hd):
                    if child in W:
                        W[child] = W[ua] @ (old_ua.inverted() @ W[child])
        rep['arm_swing_gain'] = gain
    # pose: FK -> leg IK -> arm clearance -> hands -> FK
    Ms, clamp, pen_before, pen_after, arm_corr = [], 0, [], [], []
    corr = {'l': [], 'r': []}
    stage = []
    for W, h, g in sols:
        M = fk(T, W, h)
        for s in ('l', 'r'):
            if leg_ik(T, W, M, s, g[s]):
                clamp += 1
            M = fk(T, W, h)
        stage.append((W, h, M))
    # arm clearance: real triangle intersections between forearm/hand/fingers and torso/thighs on the posed mesh;
    # the arm is abducted about the shoulder only as far as needed (2 deg steps, max 12), then smoothed over time
    clash = MeshClash(T) if spec.get('arm_fix', True) else None
    max_ang = spec.get('arm_max_deg', 12)
    for s_ in ('l', 'r'):
        for W, h, M in stage:
            need = 0
            if clash is not None:
                c0 = clash.count(T, W, h, s_, spec)
                pen_before.append(c0)
                if c0:
                    need = max_ang
                    for ang in range(2, max_ang + 1, 2):
                        W2 = dict(W)
                        rotate_arm(T, W2, M, s_, ang)
                        if not clash.count(T, W2, h, s_, spec):
                            need = ang
                            break
            corr[s_].append(need)
        corr[s_] = smooth_max(corr[s_], 2) if any(corr[s_]) else corr[s_]
    frames_basis = []
    for i, (W, h, M) in enumerate(stage):
        for s in ('l', 'r'):
            rotate_arm(T, W, M, s, corr[s][i])
        relaxed_hands(T, W, spec.get('finger_curl', 1.0))
        M = fk(T, W, h)
        if clash is not None:
            pen_after.append(clash.count(T, W, h, 'l', spec, hands_done=True) + clash.count(T, W, h, 'r', spec, hands_done=True))
        Ms.append(M)
        frames_basis.append(to_basis(T, M))
    rep['arm_abduction_added_deg_max'] = {s: round(max(corr[s]), 1) for s in corr}
    rep['arm_body_intersections'] = {'frames_before': sum(1 for x in pen_before if x),
                                     'frames_after': sum(1 for x in pen_after if x),
                                     'tri_pairs_before_max': max(pen_before or [0]), 'tri_pairs_after_max': max(pen_after or [0])}
    rep['ik_goal_clamped_frames'] = clamp
    out_dur = N / FPS
    rep['frames'] = N + 1
    rep['fps'] = FPS
    rep['duration_s'] = round(out_dur, 4)
    rep['time_warp'] = round(dur / out_dur, 4)
    if mode == 'loop':
        mpc = v.length * dur * scale
        rep['metres_per_cycle'] = round(mpc, 4)
        rep['native_speed_mps'] = round(mpc / out_dur, 4)
        st = stance_speed(T, Ms, out_dur)
        rep['stance_foot_backward_speed_mps'] = {k: (round(x['speed'], 4) if x and x['speed'] is not None else None)
                                                 for k, x in st.items()}
        rep['stance_frames'] = {k: (x['stance_frames'] if x else None) for k, x in st.items()}
    else:
        hm = T.m['hips']
        p0, p1 = Ms[0][hm].translation, Ms[-1][hm].translation
        rep['hips_displacement_m'] = [round(p1.x - p0.x, 4), round(p1.y - p0.y, 4)]
        f0 = (Ms[0][T.m['th_l']].translation - Ms[0][T.m['th_r']].translation)
        f1 = (Ms[-1][T.m['th_l']].translation - Ms[-1][T.m['th_r']].translation)
        rep['yaw_change_deg'] = round(heading_of(f1) - heading_of(f0), 1)
        spd = [((Ms[i + 1][hm].translation - Ms[i][hm].translation) * FPS) for i in range(len(Ms) - 1)]
        rep['hips_speed_mps_per_frame'] = [round(math.hypot(s.x, s.y), 3) for s in spd]
    lo, hi = sole_clearance(T, Ms, out_dur)
    rep['lowest_foot_joint_z_m'] = round(lo, 4)
    act = write_action(T, spec['id'], frames_basis)
    path = os.path.join(out_dir, spec['id'] + '.fbx')
    export_fbx(T, act, N + 1, path)
    rep['fbx'] = path
    rep['fbx_bytes'] = os.path.getsize(path)
    bpy.data.objects.remove(ob, do_unlink=True)
    return rep


def main():
    a = args()
    job = json.load(open(a.job))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    arm, meshes = import_target(job['target']['fbx'])
    T = Target(arm, meshes, job['target'].get('kind', 'h55'))
    prof = BodyProfile(T)
    log('target', arm.name, 'leg', round(leg_len_tgt(T), 4), 'radii', {k: round(v, 3) for k, v in prof.radius.items()})
    reports = []
    for spec in job['clips']:
        log('clip', spec['id'])
        r = process_clip(T, prof, spec, job['out_dir'])
        r['target'] = job['target']['name']
        log(json.dumps({k: v for k, v in r.items() if k != 'hips_speed_mps_per_frame'}))
        reports.append(r)
    prev = []
    if os.path.exists(job['report']):
        prev = [x for x in json.load(open(job['report'])) if x['id'] not in {r['id'] for r in reports}]
    json.dump(prev + reports, open(job['report'], 'w'), indent=1)
    log('DONE', len(reports))


main()
