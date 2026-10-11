"""Hand-authored key-art poses on the cast rigs (CTS skeleton, Unity-Humanoid names).

Posed in the character's own frame (rig at the origin, facing -Y, character's left = +X), then
the rig object is placed and turned. Every number here was tuned against full-size renders.
"""
from mathutils import Vector

from ka_common import (aim, curl, head, ik2, local, orient, rot_world, thumb, twist, upd, wmat)

ARM_HINGE = (0, 0, 1)            # upper-arm local Z is the elbow hinge in the T-pose
LEG_HINGE = (1, 0, 0)            # thigh local X is the knee hinge
FLEX = {'L': -1, 'R': 1}


def arm_ik(arm, s, hand, elbow_pole):
    return ik2(arm, f'UpperArm.{s}', f'Forearm.{s}', f'Hand.{s}', hand, elbow_pole, ARM_HINGE, FLEX[s])


def leg_ik(arm, s, ankle, knee_pole):
    return ik2(arm, f'Thigh.{s}', f'Calf.{s}', f'Foot.{s}', ankle, knee_pole, LEG_HINGE, 1)


def foot_flat(arm, s, yaw_dir=(0, -1, 0), toe_down=0.0):
    """Put the foot back to its rest attitude (sole flat on the ground), facing yaw_dir."""
    rest = arm.data.bones[f'Foot.{s}'].matrix_local.to_3x3()
    y = rest.col[1].copy()
    z = rest.col[2].copy()
    # yaw the rest frame about world Z to face yaw_dir
    fwd = Vector((y.x, y.y, 0)).normalized()
    want = Vector((yaw_dir[0], yaw_dir[1], 0)).normalized()
    q = fwd.rotation_difference(want)
    orient(arm, f'Foot.{s}', q @ y, q @ z)
    if toe_down:
        local(arm, f'Foot.{s}', x=toe_down)
    trest = arm.data.bones[f'Toes.{s}'].matrix_local.to_3x3()
    orient(arm, f'Toes.{s}', q @ trest.col[1], q @ trest.col[2])


def shift_hips(arm, d):
    """Move the pelvis in armature space (the Hips bone's own axes are tilted; X right, Z up here)."""
    pb = arm.pose.bones['Hips']
    M = pb.matrix.copy()
    M.translation = M.translation + Vector(d)
    pb.matrix = M
    upd()


def relaxed_fingers(arm, s, k=1.0):
    curl(arm, s, 'Index', 8 * k, 14 * k, 8 * k, spread=2)
    curl(arm, s, 'Middle', 12 * k, 18 * k, 10 * k)
    curl(arm, s, 'Ring', 16 * k, 22 * k, 12 * k, spread=-2)
    curl(arm, s, 'Pinky', 20 * k, 26 * k, 14 * k, spread=-5)


def fist(arm, s):
    curl(arm, s, 'Index', 78, 100, 62)
    curl(arm, s, 'Middle', 84, 102, 62)
    curl(arm, s, 'Ring', 88, 102, 60)
    curl(arm, s, 'Pinky', 92, 100, 58)


def pose_female(arm, p):
    """Rain-family KOL: weight on her left hip, phone in her right hand at chest height."""
    # pelvis: shift onto the left leg, left hip up, slight turn
    shift_hips(arm, p.get('hip_shift', (0.0, 0.0, 0.0)))
    rot_world(arm, 'Hips', (0, 1, 0), p.get('hip_tilt', -6))
    rot_world(arm, 'Hips', (0, 0, 1), p.get('hip_turn', -6))
    # torso counter-curve, chest turned back toward the phone
    rot_world(arm, 'Spine', (0, 1, 0), p.get('spine_tilt', 7))
    rot_world(arm, 'Chest', (0, 1, 0), p.get('chest_tilt', 3))
    rot_world(arm, 'Spine', (0, 0, 1), p.get('spine_turn', 4))
    local(arm, 'Chest', x=p.get('chest_fwd', 4))
    # legs: left (support) under the hip, right relaxed and forward with a soft knee
    for s in 'LR':
        ank = Vector(p[f'ankle_{s}'])
        leg_ik(arm, s, ank, p.get(f'knee_{s}', (0, -1, 0)))
        if p.get(f'foot_dir_{s}', (0, -1, 0)) is None:      # relaxed leg: the foot follows the calf (heel up)
            if p.get(f'toe_{s}'):
                local(arm, f'Foot.{s}', x=p[f'toe_{s}'])
        else:
            foot_flat(arm, s, p.get(f'foot_dir_{s}', (0, -1, 0)), p.get(f'toe_{s}', 0))
    # phone arm (her right)
    for s in 'LR':
        rot_world(arm, f'Shoulder.{s}', (0, 1, 0), p.get(f'clav_{s}', 0))
    arm_ik(arm, 'R', p['hand_R'], p['elbow_R'])
    orient(arm, 'Hand.R', p['handR_y'], p['handR_z'])
    # hand on hip (her left)
    arm_ik(arm, 'L', p['hand_L'], p['elbow_L'])
    orient(arm, 'Hand.L', p['handL_y'], p['handL_z'])
    # head: look down at the phone with a little tilt
    local(arm, 'Neck', x=p.get('neck_x', 10), z=p.get('neck_z', 0))
    local(arm, 'Head', x=p.get('head_x', 10), y=p.get('head_y', 0), z=p.get('head_z', 0))
    # fingers: right wraps the phone, left rests on the hip
    for f, a in p['grip_R'].items():
        curl(arm, 'R', f, *a)
    for f, a in p['grip_L'].items():
        curl(arm, 'L', f, *a)
    thumb(arm, 'R', *p.get('thumb_R', (15, 15, 20)))
    thumb(arm, 'L', *p.get('thumb_L', (5, 10, 10)))


def pose_male(arm, p):
    """Snow-family KOL: full-body win. Wide planted stance, chest out, arms up in a V, fists."""
    shift_hips(arm, p.get('hip_shift', (0.0, 0.0, 0.0)))
    rot_world(arm, 'Hips', (0, 0, 1), p.get('hip_turn', 0))
    rot_world(arm, 'Hips', (0, 1, 0), p.get('hip_tilt', 0))
    rot_world(arm, 'Hips', (1, 0, 0), p.get('hip_pitch', 0))
    for s in 'LR':
        leg_ik(arm, s, Vector(p[f'ankle_{s}']), p.get(f'knee_{s}', (0, -1, 0)))
        foot_flat(arm, s, p.get(f'foot_dir_{s}', (0, -1, 0)))
    # chest out, back arched
    local(arm, 'Spine', x=p.get('spine_x', -6), z=p.get('spine_z', 0))
    local(arm, 'Chest', x=p.get('chest_x', -8), y=p.get('chest_y', 0))
    for s in 'LR':
        rot_world(arm, f'Shoulder.{s}', (0, 1, 0), p[f'clav_{s}'])
        arm_ik(arm, s, p[f'hand_{s}'], p[f'elbow_{s}'])
        orient(arm, f'Hand.{s}', p[f'hand{s}_y'], p[f'hand{s}_z'])
        fist(arm, s)
        thumb(arm, s, *p.get(f'thumb_{s}', (45, 35, 35)))
    local(arm, 'Neck', x=p.get('neck_x', -4))
    local(arm, 'Head', x=p.get('head_x', -8), z=p.get('head_z', 0), y=p.get('head_y', 0))
    local(arm, 'Jaw', x=p.get('jaw', 0))
