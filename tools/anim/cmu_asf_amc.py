#!/usr/bin/env python3
"""CMU (Acclaim) ASF/AMC reader, forward kinematics, BVH writer and BVH self-check. Pure Python 3.

Conventions (CMU FAQ http://mocap.cs.cmu.edu/faqs.php + the Acclaim format as read by CMU's amc viewer):
  * lengths / root positions are in ASF units; metres = units * (1.0/0.45) * 2.54/100 (0.45 = ':units length').
  * capture rate 120 Hz.
  * every bone has a local frame C = Rz(az)·Ry(ay)·Rx(ax) (ASF 'axis ax ay az XYZ', degrees, column vectors).
  * AMC values are degrees about the bone's local axes, applied in ASF 'dof' order (first listed = applied first):
    'dof rx ry rz' -> M = Rz·Ry·Rx.  Root: values in ':root order', rotation applied in ':root axis' order.
  * world frame of bone i: W_i = W_parent · C_parent^-1 · C_i · M_i  (root: W = C_root · M_root),
    bone i runs from the end of its parent to  end_i = start_i + W_i · C_i^-1 · direction_i · length_i.

BVH layout written here (all rest orientations identity, world-aligned, metres, Y up, rest pose faces +Z):
  * one BVH joint per ASF bone, placed at the START of the bone; OFFSET = parent bone vector
    (zero for children of 'root'); leaf bones get 'End Site' = own bone vector.
  * channels 'Zrotation Yrotation Xrotation' -> local rotation R = Rz·Ry·Rx = C_i · M_i · C_i^-1.
  * root: 'Xposition Yposition Zposition' absolute (OFFSET 0 0 0), then rotation as above.

CLI:
  cmu_asf_amc.py bvh     --asf S.asf --amc S_nn.amc --out x.bvh [--start F --end F] [--step 4] [--align start|end|none]
  cmu_asf_amc.py rest    --asf S.asf --out S_rest.bvh
  cmu_asf_amc.py check   --asf S.asf --amc S_nn.amc --bvh x.bvh     (FK of the BVH vs FK of ASF/AMC)
  cmu_asf_amc.py metrics --asf S.asf --amc S_nn.amc --out m.csv     (per-source-frame analysis table)
"""
import argparse
import math
import sys

UNITS_TO_M = (1.0 / 0.45) * 2.54 / 100.0  # = 0.0564444..., CMU FAQ
SRC_FPS = 120.0
IDENT = ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0))


# --------------------------------------------------------------------------------------------- 3x3 math
def rot(axis, deg):
    r = math.radians(deg)
    c, s = math.cos(r), math.sin(r)
    a = axis.lower()
    if a == 'x':
        return ((1.0, 0.0, 0.0), (0.0, c, -s), (0.0, s, c))
    if a == 'y':
        return ((c, 0.0, s), (0.0, 1.0, 0.0), (-s, 0.0, c))
    if a == 'z':
        return ((c, -s, 0.0), (s, c, 0.0), (0.0, 0.0, 1.0))
    raise ValueError(axis)


def mm(a, b):
    return tuple(tuple(a[i][0] * b[0][j] + a[i][1] * b[1][j] + a[i][2] * b[2][j] for j in range(3)) for i in range(3))


def mv(a, v):
    return (a[0][0] * v[0] + a[0][1] * v[1] + a[0][2] * v[2],
            a[1][0] * v[0] + a[1][1] * v[1] + a[1][2] * v[2],
            a[2][0] * v[0] + a[2][1] * v[1] + a[2][2] * v[2])


def mt(a):
    return tuple(tuple(a[j][i] for j in range(3)) for i in range(3))


def vadd(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def vsub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def vscale(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def vlen(a):
    return math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def euler_apply(order, degs):
    """order e.g. 'XYZ' / ['rx','rz'] = order of application (first applied first) -> R_last ... R_first."""
    m = IDENT
    for ax, d in zip(order, degs):
        m = mm(rot(ax[-1], d), m)
    return m


def mat_to_zyx(m):
    """Inverse of R = Rz(z)·Ry(y)·Rx(x); returns degrees (z, y, x)."""
    sy = -m[2][0]
    sy = max(-1.0, min(1.0, sy))
    y = math.asin(sy)
    if abs(sy) < 0.9999999:
        x = math.atan2(m[2][1], m[2][2])
        z = math.atan2(m[1][0], m[0][0])
    else:  # gimbal: fold everything into z
        x = 0.0
        z = math.atan2(-m[0][1], m[1][1])
    return math.degrees(z), math.degrees(y), math.degrees(x)


def _wrap_near(a, ref):
    return a + 360.0 * round((ref - a) / 360.0)


def compat_zyx(e, prev):
    """Pick the equivalent ZYX triple closest to prev (both solutions, 360-wrapped) for smooth curves."""
    if prev is None:
        return e
    z, y, x = e
    best, bestd = None, None
    for cand in ((z, y, x), (z + 180.0, 180.0 - y, x + 180.0)):
        c = tuple(_wrap_near(cand[i], prev[i]) for i in range(3))
        d = sum((c[i] - prev[i]) ** 2 for i in range(3))
        if bestd is None or d < bestd:
            best, bestd = c, d
    return best


# --------------------------------------------------------------------------------------------- ASF
class Bone:
    __slots__ = ('name', 'direction', 'length', 'axis', 'axis_order', 'dof', 'parent', 'children', 'C', 'Cinv')

    def __init__(self, name):
        self.name = name
        self.direction = (0.0, 0.0, 0.0)
        self.length = 0.0
        self.axis = (0.0, 0.0, 0.0)
        self.axis_order = 'XYZ'
        self.dof = []
        self.parent = None
        self.children = []
        self.C = IDENT
        self.Cinv = IDENT

    def vec_m(self):
        """rest bone vector in metres (world-aligned)."""
        return vscale(self.direction, self.length * UNITS_TO_M)


class Skeleton:
    def __init__(self):
        self.name = ''
        self.length_unit = 1.0
        self.angle_unit = 'deg'
        self.root_order = []          # e.g. ['TX','TY','TZ','RX','RY','RZ']
        self.root_axis = 'XYZ'
        self.root_position = (0.0, 0.0, 0.0)
        self.root_orientation = (0.0, 0.0, 0.0)
        self.bones = {}               # name -> Bone (includes 'root')
        self.order = []               # DFS order starting with 'root'

    @classmethod
    def load(cls, path):
        sk = cls()
        root = Bone('root')
        sk.bones['root'] = root
        section = None
        cur = None
        in_limits = False
        with open(path, 'r', errors='replace') as f:
            lines = f.read().splitlines()
        for raw in lines:
            line = raw.strip()
            if not line or line.startswith('#'):
                continue
            if line.startswith(':'):
                parts = line.split()
                section = parts[0][1:].lower()
                if section == 'name' and len(parts) > 1:
                    sk.name = parts[1]
                continue
            parts = line.split()
            key = parts[0].lower()
            if section == 'units':
                if key == 'length':
                    sk.length_unit = float(parts[1])
                elif key == 'angle':
                    sk.angle_unit = parts[1].lower()
            elif section == 'root':
                if key == 'order':
                    sk.root_order = [p.upper() for p in parts[1:]]
                elif key == 'axis':
                    sk.root_axis = parts[1].upper()
                elif key == 'position':
                    sk.root_position = tuple(float(p) for p in parts[1:4])
                elif key == 'orientation':
                    sk.root_orientation = tuple(float(p) for p in parts[1:4])
            elif section == 'bonedata':
                if key == 'begin':
                    cur = None
                    in_limits = False
                elif key == 'end':
                    cur = None
                    in_limits = False
                elif key == 'name':
                    cur = Bone(parts[1])
                    sk.bones[cur.name] = cur
                    in_limits = False
                elif cur is None:
                    continue
                elif key == 'direction':
                    cur.direction = tuple(float(p) for p in parts[1:4])
                    in_limits = False
                elif key == 'length':
                    cur.length = float(parts[1])
                    in_limits = False
                elif key == 'axis':
                    cur.axis = tuple(float(p) for p in parts[1:4])
                    cur.axis_order = parts[4].upper() if len(parts) > 4 else 'XYZ'
                    in_limits = False
                elif key == 'dof':
                    cur.dof = [p.lower() for p in parts[1:]]
                    in_limits = False
                elif key == 'limits':
                    in_limits = True
                elif in_limits:
                    pass
            elif section == 'hierarchy':
                if key in ('begin', 'end'):
                    continue
                parent = sk.bones[parts[0]]
                for cname in parts[1:]:
                    ch = sk.bones[cname]
                    ch.parent = parent
                    parent.children.append(ch)
        if sk.angle_unit != 'deg':
            raise ValueError('only degree ASF supported')
        if abs(sk.length_unit - 0.45) > 1e-9:
            raise ValueError('CMU scale factor assumes :units length 0.45, got %r' % sk.length_unit)
        for b in sk.bones.values():
            if b.name == 'root':
                b.C = euler_apply(sk.root_axis, sk.root_orientation)
            else:
                b.C = euler_apply(b.axis_order, b.axis)
            b.Cinv = mt(b.C)
        sk.order = []
        stack = [root]
        while stack:
            b = stack.pop()
            sk.order.append(b.name)
            stack.extend(reversed(b.children))
        return sk

    def leaves(self):
        return [n for n in self.order if not self.bones[n].children]


# --------------------------------------------------------------------------------------------- AMC
def load_amc(path):
    """-> list of (frame_number, {bone: [values]})."""
    frames = []
    cur = None
    with open(path, 'r', errors='replace') as f:
        for raw in f:
            line = raw.strip()
            if not line or line.startswith('#') or line.startswith(':'):
                continue
            parts = line.split()
            if len(parts) == 1 and parts[0].isdigit():
                cur = {}
                frames.append((int(parts[0]), cur))
                continue
            if cur is None:
                continue
            cur[parts[0]] = [float(p) for p in parts[1:]]
    return frames


def rest_frame(sk):
    vals = {'root': [0.0] * len(sk.root_order)}
    for n, b in sk.bones.items():
        if n != 'root' and b.dof:
            vals[n] = [0.0] * len(b.dof)
    return vals


def root_pose(sk, vals):
    """-> (position_m, M_root) from AMC root values."""
    v = vals.get('root', [0.0] * len(sk.root_order))
    named = dict(zip(sk.root_order, v))
    pos = tuple(named.get(k, 0.0) * UNITS_TO_M for k in ('TX', 'TY', 'TZ'))
    rot_vals = [named.get('R' + ax, 0.0) for ax in sk.root_axis]
    return pos, euler_apply(sk.root_axis, rot_vals)


def bone_motion(b, vals):
    v = vals.get(b.name)
    if not b.dof or v is None:
        return IDENT
    rot_dofs = [(d, x) for d, x in zip(b.dof, v) if d in ('rx', 'ry', 'rz')]
    if len(rot_dofs) != len([d for d in b.dof if d in ('rx', 'ry', 'rz')]):
        raise ValueError('bad dof count for ' + b.name)
    if any(d not in ('rx', 'ry', 'rz') for d in b.dof):
        raise ValueError('translation/length dofs not supported (%s)' % b.name)
    return euler_apply([d for d, _ in rot_dofs], [x for _, x in rot_dofs])


def fk_asf(sk, vals):
    """Direct Acclaim FK (reference): -> {bone: (start_m, end_m, W)} with W the bone's local frame in world."""
    out = {}
    pos, Mroot = root_pose(sk, vals)  # AMC TX TY TZ are absolute (CMU ':root position' is 0 0 0)
    root = sk.bones['root']
    Wroot = mm(root.C, Mroot)
    out['root'] = (pos, pos, Wroot)
    for n in sk.order[1:]:
        b = sk.bones[n]
        p = b.parent
        ps, pe, pW = out[p.name]
        W = mm(mm(mm(pW, p.Cinv), b.C), bone_motion(b, vals))
        local_dir = mv(b.Cinv, b.direction)
        end = vadd(pe, vscale(mv(W, local_dir), b.length * UNITS_TO_M))
        out[n] = (pe, end, W)
    return out


def bvh_local_rotations(sk, vals):
    """-> {bone: R_local} world-aligned local rotations (R = C M C^-1); root includes its own rotation."""
    out = {}
    _, Mroot = root_pose(sk, vals)
    root = sk.bones['root']
    out['root'] = mm(mm(root.C, Mroot), root.Cinv)
    for n in sk.order[1:]:
        b = sk.bones[n]
        out[n] = mm(mm(b.C, bone_motion(b, vals)), b.Cinv)
    return out


def bvh_global_rotations(sk, local):
    g = {'root': local['root']}
    for n in sk.order[1:]:
        g[n] = mm(g[sk.bones[n].parent.name], local[n])
    return g


# --------------------------------------------------------------------------------------------- analysis helpers
def heading_deg(fk):
    """Yaw of the pelvis 'forward' (= hip axis L->R crossed with up). 0 = facing +Z, +90 = facing +X (a LEFT turn
    from +Z, since the CMU character's left side is +X when facing +Z in this right-handed Y-up frame)."""
    lh = fk['lhipjoint'][1]
    rh = fk['rhipjoint'][1]
    L = vsub(lh, rh)
    fwd = cross(L, (0.0, 1.0, 0.0))
    return math.degrees(math.atan2(fwd[0], fwd[2]))


def yaw_matrix(deg):
    return rot('y', deg)


class Align:
    """Rigid world transform applied to a clip: p' = Ry(yaw) · (p - pivot_xz)."""

    def __init__(self, yaw_deg=0.0, pivot=(0.0, 0.0, 0.0)):
        self.yaw = yaw_deg
        self.R = yaw_matrix(yaw_deg)
        self.pivot = (pivot[0], 0.0, pivot[2])

    def p(self, v):
        return mv(self.R, vsub(v, self.pivot))

    def to_json(self):
        return {'yaw_deg_applied': round(self.yaw, 4),
                'xz_offset_subtracted_m': [round(self.pivot[0], 5), round(self.pivot[2], 5)]}


def make_align(sk, frames, mode, idx_first, idx_last):
    if mode == 'none':
        return Align()
    vals = frames[idx_first][1] if mode == 'start' else frames[idx_last][1]
    fk = fk_asf(sk, vals)
    h = heading_deg(fk)
    first_fk = fk_asf(sk, frames[idx_first][1]) if mode != 'start' else fk
    return Align(-h, first_fk['root'][0])


# --------------------------------------------------------------------------------------------- BVH writer
def _fmt(x):
    s = '%.6f' % x
    if s.startswith('-') and float(s) == 0.0:
        s = s[1:]
    return s


def select_frames(frames, start=None, end=None, step=4):
    """frames: AMC list. start/end are AMC frame NUMBERS (120 Hz, inclusive). -> list of indices."""
    nums = [f[0] for f in frames]
    s = nums[0] if start is None else start
    e = nums[-1] if end is None else end
    return [i for i, n in enumerate(nums) if s <= n <= e and (n - s) % step == 0]


def write_bvh(sk, frame_vals, path, fps, align=None):
    """frame_vals: list of AMC value dicts (already resampled). align: Align or None."""
    align = align or Align()
    lines = ['HIERARCHY']

    def joint(n, depth):
        b = sk.bones[n]
        ind = '  ' * depth
        if n == 'root':
            lines.append('ROOT root')
            lines.append(ind + '{')
            lines.append(ind + '  OFFSET 0.000000 0.000000 0.000000')
            lines.append(ind + '  CHANNELS 6 Xposition Yposition Zposition Zrotation Yrotation Xrotation')
        else:
            lines.append(ind + 'JOINT ' + n)
            lines.append(ind + '{')
            off = (0.0, 0.0, 0.0) if b.parent.name == 'root' else b.parent.vec_m()
            lines.append(ind + '  OFFSET ' + ' '.join(_fmt(c) for c in off))
            lines.append(ind + '  CHANNELS 3 Zrotation Yrotation Xrotation')
        if b.children:
            for c in b.children:
                joint(c.name, depth + 1)
        else:
            lines.append(ind + '  End Site')
            lines.append(ind + '  {')
            lines.append(ind + '    OFFSET ' + ' '.join(_fmt(c) for c in b.vec_m()))
            lines.append(ind + '  }')
        lines.append(ind + '}')

    joint('root', 0)
    lines.append('MOTION')
    lines.append('Frames: %d' % len(frame_vals))
    lines.append('Frame Time: %.8f' % (1.0 / fps))
    prev = {}
    for vals in frame_vals:
        pos, _ = root_pose(sk, vals)
        pos = align.p(pos)
        loc = bvh_local_rotations(sk, vals)
        loc['root'] = mm(align.R, loc['root'])
        row = [_fmt(c) for c in pos]
        for n in sk.order:
            e = compat_zyx(mat_to_zyx(loc[n]), prev.get(n))
            prev[n] = e
            row.extend(_fmt(c) for c in e)
        lines.append(' '.join(row))
    with open(path, 'w') as f:
        f.write('\n'.join(lines) + '\n')


# --------------------------------------------------------------------------------------------- BVH reader + FK (independent)
def read_bvh(path):
    toks = open(path).read().split()
    i = 0
    joints = []  # dicts: name, parent, offset, channels, end (offset or None)
    stack = []
    chan_total = 0
    while True:
        t = toks[i]
        if t == 'HIERARCHY':
            i += 1
        elif t in ('ROOT', 'JOINT'):
            j = {'name': toks[i + 1], 'parent': stack[-1] if stack else None, 'offset': None,
                 'channels': [], 'end': None, 'ci': chan_total}
            joints.append(j)
            stack.append(len(joints) - 1)
            i += 2
        elif t == 'End':  # End Site { OFFSET x y z }
            assert toks[i + 1] == 'Site' and toks[i + 2] == '{' and toks[i + 3] == 'OFFSET'
            joints[stack[-1]]['end'] = tuple(float(x) for x in toks[i + 4:i + 7])
            assert toks[i + 7] == '}'
            i += 8
        elif t == '{':
            i += 1
        elif t == '}':
            stack.pop()
            i += 1
        elif t == 'OFFSET':
            joints[stack[-1]]['offset'] = tuple(float(x) for x in toks[i + 1:i + 4])
            i += 4
        elif t == 'CHANNELS':
            n = int(toks[i + 1])
            joints[stack[-1]]['channels'] = toks[i + 2:i + 2 + n]
            joints[stack[-1]]['ci'] = chan_total
            chan_total += n
            i += 2 + n
        elif t == 'MOTION':
            break
        else:
            raise ValueError('unexpected token %r' % t)
    assert toks[i + 1] == 'Frames:'
    nframes = int(toks[i + 2])
    assert toks[i + 3] == 'Frame' and toks[i + 4] == 'Time:'
    ftime = float(toks[i + 5])
    data = [float(x) for x in toks[i + 6:]]
    assert len(data) == nframes * chan_total, (len(data), nframes, chan_total)
    frames = [data[k * chan_total:(k + 1) * chan_total] for k in range(nframes)]
    return joints, frames, ftime


def bvh_fk(joints, row):
    """-> {name: world_pos}, {name+'_end': end_site_pos}. Channels applied in listed order: R = R1·R2·R3."""
    pos, glob, out = {}, {}, {}
    for idx, j in enumerate(joints):
        vals = row[j['ci']:j['ci'] + len(j['channels'])]
        t = [0.0, 0.0, 0.0]
        R = IDENT
        for ch, v in zip(j['channels'], vals):
            if ch.endswith('position'):
                t['XYZ'.index(ch[0])] = v
            else:
                R = mm(R, rot(ch[0], v))
        if j['parent'] is None:
            p = vadd(j['offset'], tuple(t))
            G = R
        else:
            pj = joints[j['parent']]['name']
            p = vadd(pos[pj], mv(glob[pj], vadd(j['offset'], tuple(t))))
            G = mm(glob[pj], R)
        pos[j['name']] = p
        glob[j['name']] = G
        out[j['name']] = p
        if j['end'] is not None:
            out[j['name'] + '_end'] = vadd(p, mv(G, j['end']))
    return out


def self_check(sk, frame_vals, bvh_path, align=None):
    """Max |BVH FK - ASF FK| in metres over all frames/joints (joint i <-> start of bone i, end site <-> bone end)."""
    align = align or Align()
    joints, rows, _ = read_bvh(bvh_path)
    assert len(rows) == len(frame_vals)
    worst = 0.0
    worst_at = None
    for k, (vals, row) in enumerate(zip(frame_vals, rows)):
        ref = fk_asf(sk, vals)
        got = bvh_fk(joints, row)
        for n in sk.order:
            s, e, _ = ref[n]
            pairs = [(got[n], align.p(s))]
            if (n + '_end') in got:
                pairs.append((got[n + '_end'], align.p(e)))
            for g, r in pairs:
                d = vlen(vsub(g, r))
                if d > worst:
                    worst, worst_at = d, (k, n)
    return worst, worst_at


def bvh_positions(bvh_path):
    """-> (joint names, [[ (x,y,z) per joint ] per frame]) world joint positions of a BVH (Y-up metres)."""
    joints, rows, _ = read_bvh(bvh_path)
    names = [j['name'] for j in joints]
    out = []
    for row in rows:
        p = bvh_fk(joints, row)
        out.append([p[n] for n in names])
    return names, out


def rest_root_height(sk, ankle_floor_y):
    """root height that puts the rest-pose ankles (start of lfoot/rfoot) at ankle_floor_y."""
    fk = fk_asf(sk, rest_frame(sk))
    return ankle_floor_y - min(fk['lfoot'][0][1], fk['rfoot'][0][1])


# --------------------------------------------------------------------------------------------- metrics
METRIC_COLS = ['frame', 't', 'root_x', 'root_y', 'root_z', 'speed_h', 'heading', 'yaw_rate', 'lankle_y', 'rankle_y',
               'ltoe_y', 'rtoe_y', 'lfoot_spd', 'rfoot_spd', 'head_y', 'chest_fwd_y', 'chest_up_y', 'pelvis_up_y',
               'torso_pitch', 'limb_spd']


def metrics(sk, frames, smooth=6):
    """Per source frame metrics (metres, degrees, seconds). smooth = half window (frames) for derivatives."""
    rows = []
    for num, vals in frames:
        fk = fk_asf(sk, vals)
        G = bvh_global_rotations(sk, bvh_local_rotations(sk, vals))
        chest_fwd = mv(G['thorax'], (0.0, 0.0, 1.0))
        chest_up = mv(G['thorax'], (0.0, 1.0, 0.0))
        pelvis_up = mv(G['lowerback'], (0.0, 1.0, 0.0))
        rp = fk['root'][0]
        rows.append({'frame': num, 't': (num - frames[0][0]) / SRC_FPS,
                     'root_x': rp[0], 'root_y': rp[1], 'root_z': rp[2],
                     'heading_raw': heading_deg(fk),
                     'lankle_y': fk['lfoot'][0][1], 'rankle_y': fk['rfoot'][0][1],
                     'ltoe_y': fk['ltoes'][1][1], 'rtoe_y': fk['rtoes'][1][1],
                     '_lankle': fk['lfoot'][0], '_rankle': fk['rfoot'][0],
                     '_rel': [vsub(fk[n][1], rp) for n in sk.order[1:]],
                     'head_y': fk['head'][1][1],
                     'chest_fwd_y': chest_fwd[1], 'chest_up_y': chest_up[1], 'pelvis_up_y': pelvis_up[1],
                     'torso_pitch': math.degrees(math.acos(max(-1.0, min(1.0, chest_up[1]))))})
    # unwrap heading
    prev = None
    for r in rows:
        h = r.pop('heading_raw')
        if prev is not None:
            h = _wrap_near(h, prev)
        r['heading'] = h
        prev = h
    n = len(rows)
    for i, r in enumerate(rows):
        a, b = max(0, i - smooth), min(n - 1, i + smooth)
        dt = (b - a) / SRC_FPS
        ra, rb = rows[a], rows[b]
        r['speed_h'] = math.hypot(rb['root_x'] - ra['root_x'], rb['root_z'] - ra['root_z']) / dt if dt else 0.0
        r['yaw_rate'] = (rb['heading'] - ra['heading']) / dt if dt else 0.0
        r['lfoot_spd'] = vlen(vsub(rb['_lankle'], ra['_lankle'])) / dt if dt else 0.0
        r['rfoot_spd'] = vlen(vsub(rb['_rankle'], ra['_rankle'])) / dt if dt else 0.0
        r['limb_spd'] = (sum(vlen(vsub(p, q)) for p, q in zip(rb['_rel'], ra['_rel'])) / len(ra['_rel']) / dt
                         if dt else 0.0)
    for r in rows:
        r.pop('_lankle')
        r.pop('_rankle')
        r.pop('_rel')
    return rows


def write_csv(rows, path):
    with open(path, 'w') as f:
        f.write(','.join(METRIC_COLS) + '\n')
        for r in rows:
            f.write(','.join(('%d' % r[c]) if c == 'frame' else ('%.4f' % r[c]) for c in METRIC_COLS) + '\n')


# --------------------------------------------------------------------------------------------- CLI
def convert(asf, amc, out, start=None, end=None, step=4, align_mode='none', check=True):
    sk = Skeleton.load(asf)
    frames = load_amc(amc)
    idx = select_frames(frames, start, end, step)
    if not idx:
        raise SystemExit('empty frame window')
    al = make_align(sk, frames, align_mode, idx[0], idx[-1])
    vals = [frames[i][1] for i in idx]
    write_bvh(sk, vals, out, SRC_FPS / step, al)
    res = {'bvh': out, 'frames_out': len(idx), 'fps': SRC_FPS / step,
           'src_first': frames[idx[0]][0], 'src_last': frames[idx[-1]][0], 'align': al.to_json()}
    if check:
        err, at = self_check(sk, vals, out, al)
        res['selfcheck_max_err_m'] = err
        res['selfcheck_worst'] = at
    return res


def convert_rest(asf, out, root_y=0.0):
    """ASF rest pose (all AMC rotations zero), one frame. root_y (m) lifts the root so the feet stand on the
    floor (the AMC root TY is the only non-zero value)."""
    sk = Skeleton.load(asf)
    vals = [rest_frame(sk)]
    if 'TY' in sk.root_order:
        vals[0]['root'][sk.root_order.index('TY')] = root_y / UNITS_TO_M
    write_bvh(sk, vals, out, 30.0)
    err, at = self_check(sk, vals, out)
    return {'bvh': out, 'frames_out': 1, 'selfcheck_max_err_m': err, 'selfcheck_worst': at}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='cmd', required=True)
    b = sub.add_parser('bvh')
    b.add_argument('--asf', required=True)
    b.add_argument('--amc', required=True)
    b.add_argument('--out', required=True)
    b.add_argument('--start', type=int, help='first AMC frame number (120 Hz, inclusive)')
    b.add_argument('--end', type=int, help='last AMC frame number (120 Hz, inclusive)')
    b.add_argument('--step', type=int, default=4, help='keep every Nth source frame (4: 120 -> 30 fps)')
    b.add_argument('--align', choices=('none', 'start', 'end'), default='none',
                   help='rigidly move the clip so the root starts at x=z=0 and the pelvis faces +Z at the '
                        'first (start) or last (end) output frame')
    r = sub.add_parser('rest')
    r.add_argument('--asf', required=True)
    r.add_argument('--out', required=True)
    r.add_argument('--root-y', type=float, default=0.0, help='root height in metres (default 0 = AMC all-zero)')
    c = sub.add_parser('check')
    c.add_argument('--asf', required=True)
    c.add_argument('--amc', required=True)
    c.add_argument('--bvh', required=True)
    c.add_argument('--start', type=int)
    c.add_argument('--end', type=int)
    c.add_argument('--step', type=int, default=4)
    c.add_argument('--align', choices=('none', 'start', 'end'), default='none')
    m = sub.add_parser('metrics')
    m.add_argument('--asf', required=True)
    m.add_argument('--amc', required=True)
    m.add_argument('--out', required=True)
    a = ap.parse_args(argv)
    if a.cmd == 'bvh':
        res = convert(a.asf, a.amc, a.out, a.start, a.end, a.step, a.align)
        print(res)
        if res['selfcheck_max_err_m'] >= 0.001:
            sys.exit('SELF-CHECK FAILED: %.6f m' % res['selfcheck_max_err_m'])
    elif a.cmd == 'rest':
        print(convert_rest(a.asf, a.out, a.root_y))
    elif a.cmd == 'check':
        sk = Skeleton.load(a.asf)
        frames = load_amc(a.amc)
        idx = select_frames(frames, a.start, a.end, a.step)
        al = make_align(sk, frames, a.align, idx[0], idx[-1])
        err, at = self_check(sk, [frames[i][1] for i in idx], a.bvh, al)
        print('max error %.3e m (%.6f mm) at %r' % (err, err * 1000, at))
        sys.exit(0 if err < 0.001 else 1)
    elif a.cmd == 'metrics':
        sk = Skeleton.load(a.asf)
        write_csv(metrics(sk, load_amc(a.amc)), a.out)
        print('wrote', a.out)


if __name__ == '__main__':
    main()
