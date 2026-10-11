"""Author ONE relaxed Snow idle; only writes the separate animations directory.
Canonical mesh/map/LOD exports and raw sources remain unchanged.
"""
import bpy,sys,math,json,hashlib
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
canonical=root/'unity-snow';out=canonical/'animations';out.mkdir(exist_ok=True)
frozen={str(p.relative_to(canonical)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (canonical/'exports').glob('*')}
bpy.ops.wm.open_mainfile(filepath=str(canonical/'SnowAdult_Unity.blend'),use_scripts=False);scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig'];mesh=bpy.data.objects['SnowAdult_CreatorMesh'];rig.animation_data_clear();poses.reset(rig)
scene.render.fps=30;scene.frame_start=1;scene.frame_end=181
source_rest={b.name:{'parent':b.parent.name if b.parent else None,'matrix':[x for row in b.matrix_local for x in row]} for b in rig.data.bones}

def idle(t):
 poses.reset(rig)
 phase=2*math.pi*t/6;breath=math.sin(phase);drift=math.sin(phase+.7)
 # Lower-body/rest feet stay fixed. Slow thoracic pitch carries the breathing motion.
 poses.rot(rig,'Spine',(1,0,0),-.10+.10*breath)
 poses.rot(rig,'Chest',(1,0,0),-.25+.32*breath)
 poses.rot(rig,'Chest',(0,0,1),.10*drift,True)
 poses.rot(rig,'Neck',(1,0,0),.16-.15*breath)
 poses.rot(rig,'Head',(1,0,0),.18-.20*breath)
 poses.rot(rig,'Head',(0,0,1),-.08*drift,True)
 for side,sign in [('L',1),('R',-1)]:
  poses.rot(rig,'UpperArm.'+side,(0,1,0),sign*(80+.18*breath))
  poses.rot(rig,'Forearm.'+side,(0,0,1),sign*(-7+.20*breath))
  # Neutral wrists; relaxed fingers have progressively more curl toward the little finger.
  poses.rot(rig,'Hand.'+side,(0,1,0),sign*1.5)
  for digit,angles in {'Index':(8,12,8),'Middle':(10,15,10),'Ring':(12,17,12),'Pinky':(14,19,14)}.items():
   for j,a in enumerate(angles,1):poses.rot(rig,f'{digit}{j}.{side}',(0,1,0),sign*(a+.35*breath))
  poses.rot(rig,'Thumb1.'+side,(0,0,1),sign*5)
  poses.rot(rig,'Thumb1.'+side,(0,1,0),sign*(2+.15*breath),True)
  poses.rot(rig,'Thumb2.'+side,(0,1,0),sign*(5+.25*breath))
  poses.rot(rig,'Thumb3.'+side,(0,1,0),sign*(7+.25*breath))
 bpy.context.view_layer.update()

for frame in range(1,182):
 scene.frame_set(frame);idle((frame-1)/30)
 for b in rig.pose.bones:
  b.keyframe_insert(data_path='rotation_quaternion',frame=frame,group=b.name)
  b.keyframe_insert(data_path='location',frame=frame,group=b.name)
  b.keyframe_insert(data_path='scale',frame=frame,group=b.name)
act=rig.animation_data.action;act.name='SnowAdult_RelaxedIdle'
curves=[]
for layer in act.layers:
 for strip in layer.strips:
  for channelbag in strip.channelbags:
   curves.extend(channelbag.fcurves)
for fc in curves:
 for key in fc.keyframe_points:key.interpolation='LINEAR'
# Explicitly sample mesh feet, fingers, joint bounds and exact loop closure.
def snapshot(frame):
 scene.frame_set(frame);bpy.context.view_layer.update();ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());points=[mesh.matrix_world@v.co for v in ev.data.vertices]
 return {'points':points,'bones':{b.name:(rig.matrix_world@b.matrix).copy() for b in rig.pose.bones}}
first=snapshot(1);last=snapshot(181);loop_delta=max((a-b).length for a,b in zip(first['points'],last['points']))
max_foot_drift=0;sole_range=[1e9,-1e9];hand_range={s:[] for s in ['L','R']}
ids=mesh.data.attributes['source_part_id'].data;shoe_indices=[v.index for v in mesh.data.vertices if ids[v.index].value in [7,8,9]]
for frame in range(1,182):
 snap=snapshot(frame);sole=min(snap['points'][i].z for i in shoe_indices);sole_range=[min(sole_range[0],sole),max(sole_range[1],sole)]
 for side in ['L','R']:
  max_foot_drift=max(max_foot_drift,(snap['bones']['Foot.'+side].translation-first['bones']['Foot.'+side].translation).length)
  hand_range[side].append(list(snap['bones']['Hand.'+side].translation))
assert max_foot_drift<1e-7;assert max(abs(v) for v in sole_range)<1e-6;assert loop_delta<1e-6
scene.frame_set(1);bpy.context.view_layer.update();report={'name':act.name,'fps':30,'frames':[1,181],'duration_seconds':6,'source_creator':'../SnowAdult_Unity.blend','source_rest':source_rest,'skeleton_bones':len(source_rest),'digit_bones':[b.name for b in rig.data.bones if any(b.name.startswith(x) for x in poses.DIGITS)],'curves':len(curves),'rotation_keyed_bones':sorted({fc.data_path.split('"')[1] for fc in curves if fc.data_path.endswith('rotation_quaternion')}),'each_curve_key_count':sorted(set(len(fc.keyframe_points) for fc in curves)),'loop_max_vertex_delta_m':loop_delta,'maximum_foot_joint_drift_m':max_foot_drift,'sole_z_range_m':sole_range,'foot_joint_x_separation_m':abs(first['bones']['Foot.L'].translation.x-first['bones']['Foot.R'].translation.x),'hip_joint_x_separation_m':abs(first['bones']['Thigh.L'].translation.x-first['bones']['Thigh.R'].translation.x),'source_import_bundle_sha256':frozen,'status':'Authored candidate. Blender actual-pose render and fresh-FBX checks pending; Unity Humanoid/digit import belongs to root.','limits':['No facial, gaze or blink animation claimed.','FK body/hand loop only; does not change locomotion gait or gesture clips.']}
(out/'idle-manifest.json').write_text(json.dumps(report,indent=2));(out/'unity-humanoid-map.json').write_bytes((canonical/'unity-humanoid-map.json').read_bytes())
bpy.ops.wm.save_as_mainfile(filepath=str(out/'SnowAdult_RelaxedIdle.blend'))
for rel,h in frozen.items():assert hashlib.sha256((canonical/rel).read_bytes()).hexdigest()==h
print('IDLE_AUTHORED',json.dumps({k:v for k,v in report.items() if k not in ['source_rest','source_import_bundle_sha256','rotation_keyed_bones']}),flush=True)
