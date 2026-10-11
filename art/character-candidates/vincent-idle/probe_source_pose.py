import bpy,sys,json,numpy as np
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
bpy.ops.wm.open_mainfile(filepath=str(root/'vincent-adult-production/VincentAdult_Tiers.blend'),use_scripts=False)
rig=bpy.data.objects['VincentAdult_Rig'];mesh=bpy.data.objects['VincentAdult_CreatorMesh'];rig.animation_data_clear()
for o in [o for o in bpy.data.objects if o.type=='MESH']:
 if o.data.shape_keys:
  o.data.shape_keys.animation_data_clear()
  for k in o.data.shape_keys.key_blocks:k.value=0
names={g.index:g.name for g in mesh.vertex_groups}
def select(wanted):return np.array([v.index for v in mesh.data.vertices if sum(g.weight for g in v.groups if wanted(names[g.group]))>.5])
hand={side:select(lambda n:n=='Hand.'+side or n.endswith('.'+side) and any(n.startswith(d) for d in poses.DIGITS)) for side in ['L','R']}
thigh=select(lambda n:n=='Hips' or n.startswith('Thigh.'))
rows=[]
for angle in [34,36,38,40,42,44,46,48]:
 poses.reset(rig)
 for side,sign in [('L',1),('R',-1)]:
  poses.rot(rig,'UpperArm.'+side,(0,1,0),sign*angle);poses.rot(rig,'Forearm.'+side,(0,0,1),sign*-8)
  for digit,angles in {'Index':(6,8,5),'Middle':(8,10,7),'Ring':(10,13,9),'Pinky':(12,15,11)}.items():
   for j,a in enumerate(angles,1):poses.rot(rig,f'{digit}{j}.{side}',(0,1,0),sign*a)
 bpy.context.view_layer.update();ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());v=np.array([mesh.matrix_world@p.co for p in ev.data.vertices]);row={'angle':angle,'sole':float(v[:,2].min()),'hand':{}}
 for side,sign in [('L',1),('R',-1)]:
  hp=v[hand[side]];tp=v[thigh];tp=tp[(tp[:,2]>=hp[:,2].min()-.04)&(tp[:,2]<=hp[:,2].max()+.04)]
  row['hand'][side]={'joint':list((rig.matrix_world@rig.pose.bones['Hand.'+side].matrix).translation),'lateral_gap':float((hp[:,0]*sign).min()-(tp[:,0]*sign).max()),'surface_min':hp.min(0).tolist(),'surface_max':hp.max(0).tolist()}
 rows.append(row)
report={'note':'Source-pose surface measurements; lateral proxy is not an exact mesh-intersection test. No source file saved.','rows':rows,'rest_bones':{b.name:list((rig.matrix_world@b.matrix_local).translation) for b in rig.data.bones}}
(root/'vincent-idle/source-pose-grid.json').write_text(json.dumps(report,indent=2));print(json.dumps(rows),flush=True)
