"""Shared helpers for the CT Life title key art (Blender 5.1, headless).

Coordinates: Unity (x, y, z) maps to Blender (x, z, y). Unity is left-handed Y-up, Blender
right-handed Z-up, so swapping Y and Z is the whole conversion (no mirror): the Unity camera
looking down +Z becomes a Blender camera looking down +Y, with +X to the right in both.
"""
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
LOOKS = REPO / 'art/cast/export/looks'
EVID = REPO.parent / 'ct-life-evidence/title-keyart-20261016'
OUT = EVID / 'blender'


def u2b(v):
    """Unity position -> Blender position."""
    return Vector((v[0], v[2], v[1]))


def import_look(look, name):
    """Import a cast look's hero FBX (same mesh, rig and textures as the game's preview prefab)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(LOOKS / look / f'{look}_Hero.fbx'))
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE')
    mesh = next(o for o in new if o.type == 'MESH')
    arm.name, mesh.name = name + '_rig', name + '_body'
    arm.data.pose_position = 'POSE'
    for p in arm.pose.bones:
        p.rotation_mode = 'QUATERNION'
    return arm, mesh


# ---------------------------------------------------------------- posing (FK, world-space)

def upd():
    bpy.context.view_layer.update()


def wmat(arm, bone):
    return arm.matrix_world @ arm.pose.bones[bone].matrix


def head(arm, bone):
    return wmat(arm, bone).translation.copy()


def ydir(arm, bone):
    return (wmat(arm, bone).to_3x3() @ Vector((0, 1, 0))).normalized()


def rot_world(arm, bone, axis, deg):
    """Rotate a pose bone about a world axis through its head; children follow."""
    p = arm.pose.bones[bone]
    M = arm.matrix_world @ p.matrix
    h = M.translation.copy()
    R = Matrix.Rotation(math.radians(deg), 4, Vector(axis).normalized())
    p.matrix = arm.matrix_world.inverted() @ (Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ M)
    upd()


def aim(arm, bone, direction, amount=1.0):
    """Swing a bone so its Y axis points along a world direction (minimal rotation)."""
    cur = ydir(arm, bone)
    q = cur.rotation_difference(Vector(direction).normalized())
    if amount != 1.0:
        q = Quaternion().slerp(q, amount)
    axis, ang = q.to_axis_angle()
    if ang > 1e-6:
        rot_world(arm, bone, axis, math.degrees(ang))


def twist(arm, bone, deg):
    rot_world(arm, bone, ydir(arm, bone), deg)


def local(arm, bone, x=0.0, y=0.0, z=0.0):
    """Add a local (rest-relative) XYZ euler rotation in degrees to a bone's pose."""
    p = arm.pose.bones[bone]
    q = Matrix.Rotation(math.radians(z), 3, 'Z') @ Matrix.Rotation(math.radians(y), 3, 'Y') @ Matrix.Rotation(math.radians(x), 3, 'X')
    p.rotation_quaternion = p.rotation_quaternion @ q.to_quaternion()
    upd()


def place_rig(arm, loc, yaw_deg):
    arm.location = loc
    arm.rotation_mode = 'XYZ'
    arm.rotation_euler = (0, 0, math.radians(yaw_deg))
    upd()


def aim_to(arm, bone, child, point):
    """Swing `bone` so the head of `child` moves onto the line toward `point`."""
    h = head(arm, bone)
    cur = (head(arm, child) - h).normalized()
    q = cur.rotation_difference((Vector(point) - h).normalized())
    axis, ang = q.to_axis_angle()
    if ang > 1e-6:
        rot_world(arm, bone, axis, math.degrees(ang))


def signed_angle(a, b, n):
    a = (a - n * a.dot(n)).normalized()
    b = (b - n * b.dot(n)).normalized()
    return math.degrees(math.atan2(n.dot(a.cross(b)), a.dot(b)))


def ik2(arm, upper, lower, end, target, pole, hinge_local, flex_sign):
    """Analytic two-bone IK in world space.

    upper/lower/end: e.g. UpperArm.L, Forearm.L, Hand.L. pole: world direction the joint
    should point to. hinge_local: the upper bone's local axis that is the joint's hinge in the
    rest pose; flex_sign: +1/-1, which way round that axis is flexion. The upper bone is
    twisted so its hinge lines up with the bend plane, so the joint folds like a hinge.
    """
    H = head(arm, upper)
    a = (head(arm, lower) - H).length
    b = (head(arm, end) - head(arm, lower)).length
    T = Vector(target)
    d = min((T - H).length, (a + b) * 0.9995)
    u = (T - H).normalized()
    v = Vector(pole) - u * Vector(pole).dot(u)
    v.normalize()
    ca = (a * a + d * d - b * b) / (2 * a * d)
    ca = max(-1.0, min(1.0, ca))
    K = H + u * (a * ca) + v * (a * math.sqrt(max(0.0, 1 - ca * ca)))
    aim_to(arm, upper, lower, K)
    # twist the upper bone so the hinge axis is the bend-plane normal
    n = (K - H).cross(T - K)
    if n.length > 1e-6:
        n = n.normalized() * flex_sign
        dirn = (K - H).normalized()
        h = (wmat(arm, upper).to_3x3() @ Vector(hinge_local)).normalized()
        twist(arm, upper, signed_angle(h, n, dirn))
    aim_to(arm, lower, end, T)
    return K


def orient(arm, bone, y_dir, z_dir):
    """Set a bone's world orientation: Y along y_dir, Z as close to z_dir as possible."""
    p = arm.pose.bones[bone]
    M = arm.matrix_world @ p.matrix
    y = Vector(y_dir).normalized()
    z = Vector(z_dir)
    z = (z - y * z.dot(y)).normalized()
    x = y.cross(z)
    R = Matrix((x, y, z)).transposed().to_4x4()
    R.translation = M.translation
    p.matrix = arm.matrix_world.inverted() @ R
    upd()


def curl(arm, side, finger, a1, a2, a3, spread=0.0):
    """Curl a finger (degrees per joint, positive = toward the palm). Local X is the hinge."""
    sz = spread if side == 'L' else -spread      # mirrored rigs: local Z flips sign on the right
    for i, ang in enumerate((a1, a2, a3), 1):
        local(arm, f'{finger}{i}.{side}', x=-ang, z=sz if i == 1 else 0.0)


def thumb(arm, side, z1, z2, z3, x1=0.0):
    """Thumb: local Z folds it across the palm (mirrored sign on the right), X swings it out."""
    k = 1 if side == 'L' else -1
    local(arm, f'Thumb1.{side}', x=x1, z=k * z1)
    local(arm, f'Thumb2.{side}', z=k * z2)
    local(arm, f'Thumb3.{side}', z=k * z3)
