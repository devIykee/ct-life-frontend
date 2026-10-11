"""Retarget Quaternius Universal Animation Library clips (CC0) onto the CT Life skeleton and export
one FBX per clip (armature only, in place, 30 fps).

Usage: art/pipeline/blender.sh art/pipeline/anim_retarget.py -- [male|female] [clip,clip,...]
Method: both skeletons are T-pose at rest and use the same Unreal-mannequin bone names, so each bone's
armature-space rotation delta (pose * rest^-1) is transferred onto the target rest; the pelvis
translation is scaled by the hip-height ratio. Finger chains collapse onto the mitten bones.
"""
import bpy, sys, os, json, math, re
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
from ctl_anim import export_clip as _export_clip, Rig, floor_lift

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
SEX = argv[0] if argv else 'male'
ONLY = argv[1].split(',') if len(argv) > 1 else None
OUT = os.path.join(C.PROCESSED, 'animations')
os.makedirs(OUT, exist_ok=True)

ANIM_BLEND = os.path.join(C.WORK, f'anims_{SEX}.blend')
if ONLY and os.path.exists(ANIM_BLEND):
    # partial re-run: keep every other retargeted/hand-keyed action already in the work file
    bpy.ops.wm.open_mainfile(filepath=ANIM_BLEND)
    for cid in ONLY:
        if cid in bpy.data.actions:
            bpy.data.actions.remove(bpy.data.actions[cid])
    for a in list(bpy.data.actions):          # stale source-library actions from earlier runs
        if '|' in a.name:
            bpy.data.actions.remove(a)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = 30


def log(*a):
    print('[anim_retarget]', *a, flush=True)


# target rig
if f'CTL_Rig_{SEX}' in bpy.data.objects:
    tgt = bpy.data.objects[f'CTL_Rig_{SEX}']
else:
    with bpy.data.libraries.load(os.path.join(C.WORK, f'char_{SEX}_master.blend')) as (src, dst):
        dst.objects = [f'CTL_Rig_{SEX}']
    tgt = dst.objects[0]
    sc.collection.objects.link(tgt)
tgt.animation_data_clear()
for b in tgt.data.bones:
    b.use_deform = True


def import_lib(path):
    before_o, before_a = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, ignore_leaf_bones=False)
    arm = next(o for o in bpy.data.objects if o not in before_o and o.type == 'ARMATURE')
    for o in [o for o in bpy.data.objects if o not in before_o and o.type == 'MESH']:
        bpy.data.objects.remove(o, do_unlink=True)
    acts = {re.sub(r'\.\d+$', '', a.name.split('|')[-1]): a for a in bpy.data.actions if a not in before_a}
    return arm, acts


LIBS = {'UAL1': import_lib(C.UAL1), 'UAL2': import_lib(C.UAL2)}
log('libraries', {k: len(v[1]) for k, v in LIBS.items()})

RIG = Rig(tgt)
T_BONES = [b.name for b in tgt.data.bones]          # hierarchy order (parents first)
T_REST = {b.name: b.matrix_local.copy() for b in tgt.data.bones}
T_PARENT = {b.name: (b.parent.name if b.parent else None) for b in tgt.data.bones}


def retarget(lib, clip, cid, loop):
    src, acts = LIBS[lib]
    act = acts[clip]
    src.animation_data_create()
    src.animation_data.action = act
    if hasattr(src.animation_data, 'action_slot') and act.slots:
        src.animation_data.action_slot = act.slots[0]
    f0, f1 = (int(round(x)) for x in act.frame_range)
    S_REST = {b.name: b.matrix_local.copy() for b in src.data.bones}
    hip_scale = T_REST['pelvis'].translation.z / S_REST['pelvis'].translation.z
    frames = list(range(f0, f1 + 1))
    keys = {b: [] for b in T_BONES}
    loc_keys = []
    for f in frames:
        sc.frame_set(f)
        pose = {}
        for b in T_BONES:
            sb = src.pose.bones.get(b)
            if sb is None:
                continue
            dq = sb.matrix.to_quaternion() @ S_REST[b].to_quaternion().inverted()
            pose[b] = dq @ T_REST[b].to_quaternion()
        # hierarchy solve -> armature-space matrices
        arm_mats = {}
        for b in T_BONES:
            p = T_PARENT[b]
            rest_rel = (T_REST[p].inverted() @ T_REST[b]) if p else T_REST[b]
            parent_pose = arm_mats[p] if p else Matrix.Identity(4)
            head = (parent_pose @ rest_rel).translation
            if b == 'pelvis':
                sp = src.pose.bones['pelvis']
                head = T_REST['pelvis'].translation + (sp.matrix.translation - S_REST['pelvis'].translation) * hip_scale
            if b in pose:
                M = Matrix.Translation(head) @ pose[b].to_matrix().to_4x4()
            else:
                M = parent_pose @ rest_rel
            arm_mats[b] = M
        # floor contact: our MakeHuman bodies are thicker than the UAL mannequin -> lift lying poses clear of z=0
        lift = floor_lift(RIG, arm_mats)
        if lift > 0:
            for b in T_BONES:
                if b != 'root':
                    arm_mats[b] = Matrix.Translation((0, 0, lift)) @ arm_mats[b]
        basis = RIG.to_basis(arm_mats)
        for b in T_BONES:
            q = basis[b][0]
            if keys[b] and keys[b][-1].dot(q) < 0:
                q.negate()
            keys[b].append(q)
        loc_keys.append(basis['pelvis'][1])
    # build the action (layered-action API, Blender 4.4+)
    a = bpy.data.actions.new(cid)
    slot = a.slots.new(id_type='OBJECT', name=tgt.name)
    strip = a.layers.new('Layer').strips.new(type='KEYFRAME')
    cb = strip.channelbag(slot, ensure=True)
    n = len(frames)
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
    for b in T_BONES:
        dp = f'pose.bones["{b}"].rotation_quaternion'
        for i in range(4):
            put(dp, i, [q[i] for q in keys[b]], b)
    for i in range(3):
        put('pose.bones["pelvis"].location', i, [v[i] for v in loc_keys], 'pelvis')
    a.use_fake_user = True
    a['ctl_loop'] = loop
    a['ctl_source'] = f'Quaternius UAL ({lib}) {clip}'
    return a, n


def export_clip(action, path):
    _export_clip(tgt, action, path)


if __name__ == '__main__':
    index_path = os.path.join(OUT, 'clips.json')
    index = json.load(open(index_path)) if os.path.exists(index_path) else {}
    for cid, (lib, clip, loop) in C.UAL_CLIPS.items():
        if ONLY and cid not in ONLY:
            continue
        if clip not in LIBS[lib][1]:
            log('MISSING', cid, lib, clip)
            continue
        act, n = retarget(lib, clip, cid, loop)
        path = os.path.join(OUT, f'{cid}.fbx')
        if SEX == 'male':
            export_clip(act, path)
            index[cid] = {'fbx': f'{cid}.fbx', 'frames': n, 'fps': 30, 'seconds': round((n - 1) / 30, 2), 'loop': loop,
                          'source': f'Quaternius Universal Animation Library {lib[-1]} (Standard, CC0): {clip}',
                          'skeleton': 'CTL (male rig; Humanoid-retargets to female)', 'root_motion': False}
        log(cid, clip, n, 'frames')
    if SEX == 'male':
        json.dump(index, open(index_path, 'w'), indent=1)
    # keep the retargeted actions for previews / procedural emotes
    for o in list(bpy.data.objects):
        if o is not tgt and o.name != '_bind_proxy':
            bpy.data.objects.remove(o, do_unlink=True)
    for a in bpy.data.actions:
        if a.get('ctl_loop') is not None:
            a.use_fake_user = True
    for a in list(bpy.data.actions):
        if a.users == 0 and not a.get('ctl_loop') is not None:
            pass
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(C.WORK, f'anims_{SEX}.blend'))
    log('saved actions', os.path.join(C.WORK, f'anims_{SEX}.blend'))
