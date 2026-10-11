"""Fresh-FBX source equivalence and correction gates, no Unity or canonical writes."""
import bpy,json,hashlib,sys,numpy as np
from pathlib import Path
from collections import Counter
from mathutils import kdtree
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
base=root/'rain-adult-production';out=root/'rain-weight-correction'
def import_asset(path):
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False);mesh=next(o for o in bpy.data.objects if o.type=='MESH');rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');return mesh,rig
def skeleton(rig):return {b.name:{'parent':b.parent.name if b.parent else None,'rest':list(sum((list(r) for r in b.matrix_local),[]))} for b in rig.data.bones}
def points(mesh):return [mesh.matrix_world@v.co for v in mesh.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices]
def mesh_signature(o):
 me=o.data;me.calc_loop_triangles();uv=me.uv_layers.active.data;triangles=[]
 for t in me.loop_triangles:
  corners=[]
  for vi,li in zip(t.vertices,t.loops):corners.append(tuple(round(x,6) for x in (*me.vertices[vi].co,*uv[li].uv)))
  triangles.append(tuple(sorted(corners)))
 return Counter(triangles)
def apply(name,rig):
 poses.reset(rig)
 if name=='ankle_hip_stress':
  poses.rot(rig,'Head',(0,0,1),35)
  for s,sign in [('L',1),('R',-1)]:
   poses.rot(rig,'Thigh.'+s,(1,0,0),-35*sign);poses.rot(rig,'Calf.'+s,(1,0,0),55 if s=='L' else 12);poses.rot(rig,'Foot.'+s,(1,0,0),-30 if s=='L' else 20);poses.rot(rig,'Toes.'+s,(1,0,0),25)
 elif name=='head_turn_only':poses.rot(rig,'Head',(0,0,1),45)
 bpy.context.view_layer.update()
def difference(a,b):
 kd=kdtree.KDTree(len(a))
 for i,p in enumerate(a):kd.insert(p,i)
 kd.balance();return max(kd.find(p)[2] for p in b)
report={'scope':'Fresh Blender FBX comparison against original frozen FBX and new .blend poses; native Unity same-phase movement review remains required.','tiers':{}}
for tier in ['Creator','LOD1','LOD2']:
 file=f'RainAdult_{tier}.fbx';old,orig=import_asset(base/'exports'/file);signature=mesh_signature(old);old_bones=skeleton(orig);old_count=len(old.data.vertices);old_tri=len(old.data.loop_triangles);old_material=len(old.material_slots)
 bpy.ops.wm.open_mainfile(filepath=str(out/'RainAdult_WeightsCorrected.blend'),use_scripts=False);source=bpy.data.objects[f'RainAdult_{tier}Mesh'];rig=bpy.data.objects['RainAdult_Rig'];samples={}
 for pose in ['rest','ankle_hip_stress','head_turn_only']:apply(pose,rig);samples[pose]=points(source)
 mesh,rig=import_asset(out/'exports'/file);bones=skeleton(rig);pose_deltas={}
 for pose,reference in samples.items():apply(pose,rig);pose_deltas[pose]=difference(reference,points(mesh))
 apply('rest',rig);rest_points=points(mesh);group={g.index:g.name for g in mesh.vertex_groups};bad=[v.index for v,p in zip(mesh.data.vertices,rest_points) if p.z<1.15 and any(group[g.group]=='Head' and g.weight>1e-7 for g in v.groups)]
 rest_delta=max(abs(x-y) for n in bones for x,y in zip(bones[n]['rest'],old_bones[n]['rest']))
 checks={'same_rest_surface_triangle_uv_multiset':signature==mesh_signature(mesh),'same_rest_skeleton':bones.keys()==old_bones.keys() and rest_delta<1e-7 and all(bones[n]['parent']==old_bones[n]['parent'] for n in bones),'same55bones':len(bones)==55,'same_triangle_count':old_tri==len(mesh.data.loop_triangles),'same_vertex_count':old_count==len(mesh.data.vertices),'one_material':len(mesh.material_slots)==old_material==1,'max4_normalized_weights':all(1<=len(v.groups)<=4 and abs(sum(g.weight for g in v.groups)-1)<1e-5 for v in mesh.data.vertices),'zero_head_influence_below1_15m':not bad,'all_exported_pose_surfaces_within10micrometres':max(pose_deltas.values())<1e-5}
 # Head-only rotation must leave ankles and hips exactly where the rest pose placed them.
 p0=rest_points;apply('head_turn_only',rig);p1=points(mesh);lower_delta=max((a-b).length for a,b in zip(p0,p1) if a.z<1.15);checks['head_turn_cannot_move_lower_body']=lower_delta<1e-7
 report['tiers'][tier]={'checks':checks,'rest_matrix_max_delta':rest_delta,'triangles':old_tri,'vertices':old_count,'pose_surface_max_deltas_m':pose_deltas,'head_rotation_lower_body_max_delta_m':lower_delta,'sha256':hashlib.sha256((out/'exports'/file).read_bytes()).hexdigest()};print('FRESH_WEIGHT_VALIDATION',tier,json.dumps(checks),flush=True)
report['all_pass']=all(all(r['checks'].values()) for r in report['tiers'].values());(out/'fbx-export-validation.json').write_text(json.dumps(report,indent=2)+'\n');assert report['all_pass'];print('RAIN_WEIGHT_FRESH_FBX_ALL_PASS',flush=True)
