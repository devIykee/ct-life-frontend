"""Fresh FBX round-trip gate for the adult male, including five facial morphs.
This is a Blender gate. Native Unity retarget/material and actual-device checks remain separate.
"""
import bpy,json,hashlib,sys,numpy as np
from pathlib import Path
from collections import Counter
from mathutils import kdtree
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
from snow_atlas_validation import validate_atlas
out=root/'vincent-adult-production';ex=out/'exports';tiers=json.loads((out/'tier-manifest.json').read_text());names=['BlinkLeft','BlinkRight','Smile','BrowUp','MouthOpen']
def skeleton(rig):return {b.name:{'parent':b.parent.name if b.parent else None,'rest':list(sum((list(row) for row in rig.matrix_world@b.matrix_local),[]))} for b in rig.data.bones}
def points(mesh):
 ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());return [mesh.matrix_world@v.co for v in ev.data.vertices]
def apply(name,rig,mesh):
 poses.reset(rig)
 for k in mesh.data.shape_keys.key_blocks:k.value=0
 if name=='arm_leg_digits':
  for s,sign in [('L',1),('R',-1)]:
   poses.rot(rig,'UpperArm.'+s,(0,1,0),-25 if s=='L' else -42);poses.rot(rig,'Forearm.'+s,(0,1,0),-90 if s=='L' else 15)
   poses.rot(rig,'Thigh.'+s,(1,0,0),-35*sign);poses.rot(rig,'Calf.'+s,(1,0,0),55 if s=='L' else 12);poses.rot(rig,'Foot.'+s,(1,0,0),-20 if s=='L' else 10)
   for digit in ['Thumb','Index','Middle','Ring','Pinky']:
    for j,angle in [(1,15),(2,35),(3,20)]:poses.rot(rig,f'{digit}{j}.{s}',(0,1,0),angle*sign)
 elif name=='head_only':poses.rot(rig,'Head',(0,0,1),40)
 elif name in names:mesh.data.shape_keys.key_blocks[name].value=1
 bpy.context.view_layer.update()
def difference(a,b):
 kd=kdtree.KDTree(len(a))
 for i,p in enumerate(a):kd.insert(p,i)
 kd.balance();return max(kd.find(p)[2] for p in b)
def tri_uv_signature(mesh):
 me=mesh.data;me.calc_loop_triangles();uv=me.uv_layers.active.data
 return Counter(tuple(sorted(tuple(round(float(x),5) for x in (*me.vertices[vi].co,*uv[li].uv)) for vi,li in zip(t.vertices,t.loops))) for t in me.loop_triangles)
report={'scope':'Fresh Blender FBX equivalence, weights, UVs and actual posed/morphed vertex surfaces. Native Unity own-Humanoid retarget/material and device review still required.','tiers':{},'texture_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in ex.glob('*.png')}}
coverage=json.loads((out/'tier-uv-gate.json').read_text());assert coverage['all_pass']
for f in ex.glob('*.fbx'):assert coverage['fbx_sha256'][f.name]==hashlib.sha256(f.read_bytes()).hexdigest()
first_bones=None
for tier in ['Creator','LOD1','LOD2']:
 bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_Tiers.blend'),use_scripts=False);mesh=bpy.data.objects[f'VincentAdult_{tier}Mesh'];rig=bpy.data.objects['VincentAdult_Rig'];source_bones=skeleton(rig);source_signature=tri_uv_signature(mesh)
 uv_audit=validate_atlas(mesh.data);(out/f'{tier.lower()}-uv-validation.json').write_text(json.dumps(uv_audit,indent=2)+'\n')
 samples={}
 for pose in ['rest','arm_leg_digits','head_only',*names]:apply(pose,rig,mesh);samples[pose]=points(mesh)
 apply('rest',rig,mesh);source_height=max(p.z for p in samples['rest']);source_min=min(p.z for p in samples['rest'])
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(ex/f'VincentAdult_{tier}.fbx'),use_anim=False);meshes=[o for o in bpy.data.objects if o.type=='MESH'];rigs=[o for o in bpy.data.objects if o.type=='ARMATURE'];assert len(meshes)==len(rigs)==1;mesh=meshes[0];rig=rigs[0]
 bones=skeleton(rig);pose_deltas={};shape_keys=[k.name for k in mesh.data.shape_keys.key_blocks if k.name!='Basis']
 for pose,reference in samples.items():apply(pose,rig,mesh);actual=points(mesh);pose_deltas[pose]=max(difference(reference,actual),difference(actual,reference))
 apply('rest',rig,mesh);rest=points(mesh);groups={g.index:g.name for g in mesh.vertex_groups};lower_head=[v.index for v,p in zip(mesh.data.vertices,rest) if p.z<1.30 and any(groups[g.group]=='Head' and g.weight>1e-7 for g in v.groups)]
 digit_names=[f'{d}{j}.{s}' for s in ['L','R'] for d in ['Thumb','Index','Middle','Ring','Pinky'] for j in [1,2,3]];digit_counts={n:sum(any(groups[g.group]==n and g.weight>1e-5 for g in v.groups) for v in mesh.data.vertices) for n in digit_names}
 apply('head_only',rig,mesh);turned=points(mesh);lower_delta=max((a-b).length for a,b in zip(rest,turned) if a.z<1.30)
 rest_delta=max(abs(x-y) for n in bones for x,y in zip(bones[n]['rest'],source_bones[n]['rest']))
 translations=[];angles=[];scales=[]
 for n in bones:
  a=np.array(bones[n]['rest']).reshape(4,4);b=np.array(source_bones[n]['rest']).reshape(4,4);translations.append(float(np.linalg.norm(a[:3,3]-b[:3,3])));scales.append(float(np.max(np.abs(np.linalg.norm(a[:3,:3],axis=0)-np.linalg.norm(b[:3,:3],axis=0)))))
  ua,_,va=np.linalg.svd(a[:3,:3]);ub,_,vb=np.linalg.svd(b[:3,:3]);relative=(ua@va).T@(ub@vb);angles.append(float(np.degrees(np.arccos(np.clip((np.trace(relative)-1)/2,-1,1)))))
 shared_delta=0 if first_bones is None else max(abs(x-y) for n in bones for x,y in zip(bones[n]['rest'],first_bones[n]['rest']))
 if first_bones is None:first_bones=bones
 mesh.data.calc_loop_triangles()
 checks={'one_mesh_one_material':len(meshes)==len(mesh.material_slots)==1,'exact55bone_names':set(bones)==set(source_bones) and len(bones)==55,'same_bone_parents':all(bones[n]['parent']==source_bones[n]['parent'] for n in bones),'source_rest_joint_positions_within10micrometres':max(translations)<1e-5,'source_rest_rotations_within0_01degree':max(angles)<.01,'source_rest_scales_within1e_5':max(scales)<1e-5,'same_rest_matrices_across_tiers':shared_delta<1e-7,'exact5_named_morphs':set(shape_keys)==set(names),'all30_digits_weighted':min(digit_counts.values())>0,'max4_normalized_weights':all(1<=len(v.groups)<=4 and abs(sum(g.weight for g in v.groups)-1)<1e-5 for v in mesh.data.vertices),'zero_head_weight_below1_30m':not lower_head,'head_turn_keeps_lower_body':lower_delta<1e-7,'pose_and_morph_surface_roundtrip_under20micrometres':max(pose_deltas.values())<2e-5,'triangle_count_matches':len(mesh.data.loop_triangles)==tiers['tiers'][tier]['triangles'],'rest_surface_and_uv_signature_matches':source_signature==tri_uv_signature(mesh),'zero_atlas_interior_overlap_samples':uv_audit['overlapping_interior_samples']==0,'zero_blank_atlas_samples':coverage['samples'][tier]['blank_atlas_samples']==0,'sole_grounded':abs(source_min)<1e-6,'height_matches':abs(source_height-1.7892162799835205)<.003}
 report['tiers'][tier]={'checks':checks,'sha256':hashlib.sha256((ex/f'VincentAdult_{tier}.fbx').read_bytes()).hexdigest(),'triangles':len(mesh.data.loop_triangles),'fbx_vertices_after_uv_seams':len(mesh.data.vertices),'rest_matrix_max_delta':rest_delta,'rest_joint_position_max_delta_m':max(translations),'rest_rotation_max_delta_degrees':max(angles),'rest_scale_max_delta':max(scales),'shared_rest_matrix_max_delta':shared_delta,'sample_surface_max_deltas_m':pose_deltas,'head_turn_lower_body_delta_m':lower_delta,'digit_weighted_vertex_counts':digit_counts,'atlas_overlap_samples':uv_audit['overlapping_interior_samples']};print('VINCENT_FRESH_FBX',tier,json.dumps(checks),flush=True)
report['all_pass']=all(all(r['checks'].values()) for r in report['tiers'].values());(out/'fbx-export-validation.json').write_text(json.dumps(report,indent=2)+'\n');print('VINCENT_EXPORT_GATE',report['all_pass'],flush=True);assert report['all_pass']
