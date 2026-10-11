"""BVH -> FBX for Unity (armature only, baked, one take). Blender 5.1 headless:

  Blender -b --factory-startup -noaudio --python tools/anim/bvh_to_fbx_blender.py -- \
      --bvh clip.bvh --out clip.fbx --take clip_id [--rest-bvh cmu_104_rest.bvh] [--armature cmu_104]

* BVH is Y-up metres (tools/anim/cmu_asf_amc.py). Imported with axis_forward='-Z', axis_up='Y' (BVH +Z -> Blender
  -Y = Blender "front", BVH +Y -> Blender +Z) and exported with the same axis pair, so FBX coordinates equal the BVH
  coordinates (Y-up, rest pose faces +Z, metres; apply_scale_options='FBX_SCALE_ALL' like art/pipeline/ctl_anim.py).
* Take = scene name = --take; keys start at frame 0, one key per BVH frame (bake step 1, no simplification).
* --rest-bvh: the rest frame (ASF all-zero pose) is keyed at frame -1, OUTSIDE the exported take range, and the scene
  is parked on frame -1 while exporting. The FBX exporter writes each node's static Lcl transform from the pose at the
  current frame, so the file's default (non-animated) skeleton pose is the ASF rest pose while the take holds only
  motion. (An armature-only FBX has no bind pose; importers fall back to these node transforms.)
"""
import argparse
import os
import sys
import tempfile

import bpy
from bpy_extras import anim_utils


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--bvh', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--take', required=True)
    ap.add_argument('--rest-bvh')
    ap.add_argument('--armature', default='cmu')
    ap.add_argument('--simplify', type=float, default=0.0, help='bake_anim_simplify_factor (0 = keep every key)')
    return ap.parse_args(argv)


def split_bvh(path):
    txt = open(path).read()
    head, motion = txt.split('MOTION', 1)
    lines = [l for l in motion.strip().splitlines() if l.strip()]
    assert lines[0].startswith('Frames:') and lines[1].startswith('Frame Time:')
    return head, float(lines[1].split(':')[1]), lines[2:]


def main():
    a = args()
    head, ftime, rows = split_bvh(a.bvh)
    n = len(rows)
    src = a.bvh
    rest = bool(a.rest_bvh)
    if rest:
        rhead, _, rrows = split_bvh(a.rest_bvh)
        if rhead.split() != head.split():
            raise SystemExit('rest BVH hierarchy differs from clip hierarchy')
        fd, src = tempfile.mkstemp(suffix='.bvh')
        with os.fdopen(fd, 'w') as f:
            f.write(head + 'MOTION\nFrames: %d\nFrame Time: %.8f\n' % (n + 1, ftime))
            f.write('\n'.join([rrows[0]] + rows) + '\n')

    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    sc = bpy.context.scene
    fps = round(1.0 / ftime)
    sc.render.fps, sc.render.fps_base = fps, 1.0
    sc.unit_settings.system, sc.unit_settings.scale_length = 'METRIC', 1.0

    bpy.ops.import_anim.bvh(filepath=src, target='ARMATURE', global_scale=1.0, frame_start=1, use_fps_scale=False,
                            update_scene_fps=False, update_scene_duration=False, rotate_mode='QUATERNION',
                            axis_forward='-Z', axis_up='Y')
    if rest:
        os.remove(src)
    ob = bpy.context.view_layer.objects.active
    assert ob and ob.type == 'ARMATURE', ob
    ob.name = a.armature
    ob.data.name = a.armature
    ad = ob.animation_data
    act = ad.action
    act.name = a.take
    # importer keys BVH frame k (0-based) at Blender frame 1+k; move motion frame 0 to Blender frame 0
    shift = -2.0 if rest else -1.0
    cb = anim_utils.action_get_channelbag_for_slot(act, ad.action_slot)
    for fc in cb.fcurves:
        for kp in fc.keyframe_points:
            kp.co.x += shift
            kp.handle_left.x += shift
            kp.handle_right.x += shift
        fc.update()

    sc.name = a.take
    sc.frame_start, sc.frame_end = 0, n - 1
    sc.frame_set(-1 if rest else 0)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=a.out, use_selection=True, object_types={'ARMATURE'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=False, armature_nodetype='NULL',
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=a.simplify, use_custom_props=False)
    print('FBX_OK %s frames=%d fps=%d take=%s rest_static=%s bones=%d' %
          (a.out, n, fps, a.take, rest, len(ob.data.bones)))


main()
