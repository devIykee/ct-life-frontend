import bpy,json,sys,math,hashlib
from pathlib import Path
from mathutils import Matrix,Vector
root=Path(__file__).resolve().parents[1];out=root/'unity-snow/animations';canonical=root/'unity-snow';manifest=json.loads((out/'idle-manifest.json').read_text())
bpy.ops.wm.open_mainfile(filepath=str(out/'SnowAdult_RelaxedIdle.blend'),use_scripts=False);scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig'];mesh=bpy.data.objects['SnowAdult_CreatorMesh']
# Save expected bone skinning matrices for all frames, rather than assuming a pass from names.
def skinning(r):return {b.name:r.matrix_world@r.pose.bones[b.name].matrix@b.matrix_local.inverted()@r.matrix_world.inverted() for b in r.data.bones}
frames=[1,16,31,46,61,76,91,106,121,136,151,166,181];expected={}
for f in frames:scene.frame_set(f);bpy.context.view_layer.update();expected[f]=skinning(rig)
# FBX without skin clusters uses the current Model transform as its rest pose.
# A non-exported frame0 supplies canonical T-rest while scene range1..181 stays idle.
scene.frame_set(0)
for b in rig.pose.bones:
 b.rotation_mode='QUATERNION';b.rotation_quaternion=(1,0,0,0);b.location=(0,0,0);b.scale=(1,1,1)
 b.keyframe_insert(data_path='rotation_quaternion',frame=0,group=b.name);b.keyframe_insert(data_path='location',frame=0,group=b.name);b.keyframe_insert(data_path='scale',frame=0,group=b.name)
scene.frame_set(0);bpy.context.view_layer.update();bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=rig
fbx=out/'SnowAdult_RelaxedIdle.fbx';bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',path_mode='STRIP')
# Canonical and animated exports must carry the same actual FBX rest hierarchy.
def rest(r):return {b.name:{'parent':b.parent.name if b.parent else None,'matrix':r.matrix_world@b.matrix_local} for b in r.data.bones}
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(canonical/'exports/SnowAdult_Creator.fbx'),use_anim=False);canonical_rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');canonical_rest=rest(canonical_rig)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(fbx),use_anim=True,anim_offset=0);rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');actual_rest=rest(rig);act=rig.animation_data.action;scene=bpy.context.scene
matrix_delta=lambda a,b:max(abs(x-y) for ra,rb in zip(a,b) for x,y in zip(ra,rb))
rest_delta=max(matrix_delta(actual_rest[n]['matrix'],canonical_rest[n]['matrix']) for n in canonical_rest)
pose_deltas={}
for f in frames:
 scene.frame_set(f);bpy.context.view_layer.update();sk=skinning(rig);pose_deltas[f]=max(matrix_delta(sk[n],expected[f][n]) for n in sk)
scene.frame_set(1);bpy.context.view_layer.update();first=skinning(rig);scene.frame_set(181);bpy.context.view_layer.update();last=skinning(rig);loop_delta=max(matrix_delta(first[n],last[n]) for n in first)
curves=[]
for layer in act.layers:
 for strip in layer.strips:
  for bag in strip.channelbags:curves.extend(bag.fcurves)
rotations={fc.data_path.split('"')[1] for fc in curves if fc.data_path.startswith('pose.bones[') and fc.data_path.endswith(('rotation_quaternion','rotation_euler'))}
digit_ranges={}
for n in manifest['digit_bones']:
 digitcurves=[fc for fc in curves if f'pose.bones["{n}"]' in fc.data_path and 'rotation_' in fc.data_path];digit_ranges[n]=max([max(k.co.y for k in fc.keyframe_points)-min(k.co.y for k in fc.keyframe_points) for fc in digitcurves],default=0)
checks={'one_canonical_skin_bind_reference_mesh':len([o for o in bpy.data.objects if o.type=='MESH'])==1,'one_armature':len([o for o in bpy.data.objects if o.type=='ARMATURE'])==1,'same55bone_names':set(actual_rest)==set(canonical_rest) and len(actual_rest)==55,'same_parents':all(actual_rest[n]['parent']==canonical_rest[n]['parent'] for n in canonical_rest),'same_canonical_FBX_rest_matrices':rest_delta<1e-7,'13_sampled_skinning_matrices_match':max(pose_deltas.values())<1e-5,'exact_closed_loop_within_1e_6':loop_delta<1e-6,'six_second_action':abs((act.frame_range[1]-act.frame_range[0])/scene.render.fps-6)<1e-5,'30_explicit_digit_rotation_tracks':set(manifest['digit_bones']).issubset(rotations),'30_digits_have_real_motion':all(v>1e-5 for v in digit_ranges.values()),'source_soles_stay_grounded':max(abs(v) for v in manifest['sole_z_range_m'])<1e-6,'source_foot_joints_do_not_drift':manifest['maximum_foot_joint_drift_m']<1e-7,'canonical_import_bundle_unchanged':all(hashlib.sha256((canonical/rel).read_bytes()).hexdigest()==h for rel,h in manifest['source_import_bundle_sha256'].items())}
report={'checks':checks,'all_pass':all(checks.values()),'fbx':fbx.name,'fbx_bytes':fbx.stat().st_size,'sha256':hashlib.sha256(fbx.read_bytes()).hexdigest(),'imported_action_name':act.name,'imported_frame_range':list(act.frame_range),'fps':scene.render.fps,'canonical_rest_max_matrix_delta':rest_delta,'sampled_skinning_matrix_deltas':pose_deltas,'loop_skinning_matrix_delta':loop_delta,'digit_rotation_curve_ranges':digit_ranges,'rotation_keyed_bones':sorted(rotations),'scope':'Blender fresh skinned-reference animation FBX equivalence + source mesh loop/soles. Unity Humanoid finger-muscle interpretation, clip and device acceptance remains with coordinating agent.'};(out/'idle-export-validation.json').write_text(json.dumps(report,indent=2));print('IDLE_FBX_VALIDATION',json.dumps(report),flush=True);assert report['all_pass']
