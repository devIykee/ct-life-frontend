"""World candidate numeric gate; native Unity/device visual and thermal gates are separate."""
import bpy,sys,json,hashlib,numpy as np
from pathlib import Path
from collections import Counter
from mathutils import kdtree
out=Path(__file__).resolve().parent;root=out.parent;sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
from snow_atlas_validation import validate_atlas
ex=out/'exports';source=root/'vincent-adult-production';topology=json.loads((out/'world-topology.json').read_text());morphs=['BlinkLeft','BlinkRight','Smile','BrowUp','MouthOpen']
def skeleton(rig):return {b.name:{'parent':b.parent.name if b.parent else None,'rest':list(sum((list(row) for row in rig.matrix_world@b.matrix_local),[]))} for b in rig.data.bones}
def apply(name,rig,mesh):
 poses.reset(rig)
 if mesh.data.shape_keys:
  for k in mesh.data.shape_keys.key_blocks:k.value=0
 if name in ['relaxed','wave','walk','run','sit']:
  for s,sign in [('L',1),('R',-1)]:
   poses.rot(rig,'UpperArm.'+s,(0,1,0),42*sign);poses.rot(rig,'Forearm.'+s,(0,0,1),-8*sign)
 if name=='wave':
  poses.rot(rig,'UpperArm.L',(0,1,0),-25);poses.rot(rig,'Forearm.L',(0,1,0),-90)
 if name in ['walk','run','sit']:
  for s,sign in [('L',1),('R',-1)]:
   poses.rot(rig,'Thigh.'+s,(1,0,0),-65 if name=='sit' else (40 if name=='run' else 25)*sign);poses.rot(rig,'Calf.'+s,(1,0,0),85 if name=='sit' else (60 if name=='run' else 25)*(1 if s=='L' else .3));poses.rot(rig,'Foot.'+s,(1,0,0),-20 if s=='L' else 10)
 if name in ['run','fist']:
  for s,sign in [('L',1),('R',-1)]:
   for digit in ['Thumb','Index','Middle','Ring','Pinky']:
    for j,angle in [(1,30),(2,65),(3,35)]:poses.rot(rig,f'{digit}{j}.{s}',(0,1,0),angle*sign)
 if name=='head_only':poses.rot(rig,'Head',(0,0,1),40)
 if name in morphs and mesh.data.shape_keys:mesh.data.shape_keys.key_blocks[name].value=1
 bpy.context.view_layer.update()
def points(mesh):
 bpy.context.view_layer.update();ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());return np.array([mesh.matrix_world@v.co for v in ev.data.vertices],dtype=np.float64)
def difference(a,b):
 kd=kdtree.KDTree(len(a))
 for i,p in enumerate(a):kd.insert(p,i)
 kd.balance();return max(kd.find(p)[2] for p in b)
def signature(mesh):
 me=mesh.data;me.calc_loop_triangles();uv=me.uv_layers.active.data
 return Counter(tuple(sorted(tuple(round(float(x),5) for x in (*me.vertices[vi].co,*uv[li].uv)) for vi,li in zip(t.vertices,t.loops))) for t in me.loop_triangles)
poseset=['rest','relaxed','wave','walk','run','sit','fist','head_only'];report={'scope':'Isolated world meshes; hand-authored stress poses, not native shared animation; no crowd/thermal acceptance.','tiers':{},'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in ex.iterdir() if p.suffix in ['.fbx','.png']}}
# Source→candidate retained-vertex skinning differences quantify the far2weight tradeoff.
bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_WorldAtlas.blend'),use_scripts=False);rig=bpy.data.objects['VincentAdult_Rig'];canon=skeleton(rig)
with bpy.data.libraries.load(str(source/'VincentAdult_AtlasHighReference.blend'),link=False) as (a,b):b.objects=['VincentAdult_CreatorMesh']
ref=b.objects[0];bpy.context.scene.collection.objects.link(ref);ref.parent=rig;ref.matrix_parent_inverse.identity();ref.matrix_world.identity()
# Match the frozen creator's explicit Basis built from retained source vertex coordinates.
ref.data.shape_keys.key_blocks['Basis'].data.foreach_set('co',np.array([v.co[:] for v in ref.data.vertices],dtype=np.float32).ravel())
for m in ref.modifiers:
 if m.type=='ARMATURE':m.object=rig
for tier in ['WorldNear','WorldFar']:
 mesh=bpy.data.objects['VincentAdult_'+tier+'Mesh'];old=np.array([a.value for a in mesh.data.attributes['source_vertex_index'].data],dtype=np.int32);deformation={}
 for name in poseset:
  apply(name,rig,ref);expected=points(ref)[old];apply(name,rig,mesh);actual=points(mesh);delta=np.linalg.norm(expected-actual,axis=1);deformation[name]={'maximum_m':float(delta.max()),'p95_m':float(np.percentile(delta,95)),'p99_m':float(np.percentile(delta,99)),'over5mm_vertices':int((delta>.005).sum())}
 report['tiers'][tier]={'retained_vertex_skinning_vs_full4weight_source':deformation}
# Independent fresh FBX checks, explicit neutral+nonzero shape checks and source bind equality.
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(source/'exports'/'VincentAdult_Creator.fbx'),use_anim=False);canonical_fbx=skeleton(next(o for o in bpy.data.objects if o.type=='ARMATURE'))
first=None
for tier in ['WorldNear','WorldFar']:
 bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_WorldAtlas.blend'),use_scripts=False);mesh=bpy.data.objects['VincentAdult_'+tier+'Mesh'];rig=bpy.data.objects['VincentAdult_Rig'];bones=skeleton(rig);sig=signature(mesh);names=morphs if tier=='WorldNear' else [];samples={}
 for name in poseset+names:apply(name,rig,mesh);samples[name]=points(mesh)
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(ex/f'VincentAdult_{tier}.fbx'),use_anim=False);meshes=[o for o in bpy.data.objects if o.type=='MESH'];rigs=[o for o in bpy.data.objects if o.type=='ARMATURE'];assert len(meshes)==len(rigs)==1;mesh=meshes[0];rig=rigs[0];mesh.data.attributes.new(name='source_part_id',type='INT',domain='POINT');fresh=skeleton(rig);keys=[k.name for k in mesh.data.shape_keys.key_blocks if k.name!='Basis'] if mesh.data.shape_keys else [];defaults={k.name:k.value for k in mesh.data.shape_keys.key_blocks if k.name!='Basis'} if mesh.data.shape_keys else {};deltas={};nonzero={}
 for name in poseset+names:
  apply(name,rig,mesh);p=points(mesh);deltas[name]=max(difference(samples[name],p),difference(p,samples[name]))
  if name in names:nonzero[name]=float(np.linalg.norm(p-neutral,axis=1).max())
  if name=='rest':neutral=p.copy()
 apply('rest',rig,mesh);groups={g.index:g.name for g in mesh.vertex_groups};digits={n:sum(any(groups[g.group]==n and g.weight>1e-5 for g in v.groups) for v in mesh.data.vertices) for n in [f'{d}{j}.{s}' for s in ['L','R'] for d in ['Thumb','Index','Middle','Ring','Pinky'] for j in [1,2,3]]};mesh.data.calc_loop_triangles();rest_delta=max(abs(a-b) for n in fresh for a,b in zip(fresh[n]['rest'],bones[n]['rest']));shared=0 if first is None else max(abs(a-b) for n in fresh for a,b in zip(fresh[n]['rest'],first[n]['rest']));first=fresh if first is None else first;uv=validate_atlas(mesh.data);limit=2 if tier=='WorldFar' else 4
 checks={'one_mesh_material':len(mesh.material_slots)==1,'55_same_named_bones':set(fresh)==set(canon) and len(fresh)==55,'same_parents':all(fresh[n]['parent']==canon[n]['parent'] for n in fresh),'source_rest_joint_positions_under10micrometres':max(abs(fresh[n]['rest'][i]-bones[n]['rest'][i]) for n in fresh for i in [3,7,11])<1e-5,'shared_world_rest_bind':shared<1e-7,'same_rest_bind_as_frozen_creator_fbx':max(abs(a-b) for n in fresh for a,b in zip(fresh[n]['rest'],canonical_fbx[n]['rest']))<1e-7,'rest_and_uv_signature_match':sig==signature(mesh),'roundtrip_surface_under20micrometres':max(deltas.values())<2e-5,'explicit_morph_set':set(keys)==set(names),'all_morph_defaults_zero':all(v==0 for v in defaults.values()),'near_morphs_have_actual_surface_deformation':all(v>.00001 for v in nonzero.values()),'all30_digits_weighted':min(digits.values())>0,'weights_normalized_within_limit':all(1<=len(v.groups)<=limit and abs(sum(g.weight for g in v.groups)-1)<1e-5 for v in mesh.data.vertices),'no_head_weight_below1_3m':all(not any(groups[g.group]=='Head' and g.weight>1e-7 for g in v.groups) for v,p in zip(mesh.data.vertices,neutral) if p[2]<1.3),'source_sole_within1mm':abs(float(neutral[:,2].min()))<.001,'zero_atlas_interior_overlap':uv['overlapping_interior_samples']==0,'tri_count_matches':len(mesh.data.loop_triangles)==topology['tiers'][tier]['triangles']}
 report['tiers'][tier].update({'checks':checks,'triangles':len(mesh.data.loop_triangles),'fresh_fbx_vertices':len(mesh.data.vertices),'rest_matrix_max_delta':rest_delta,'rest_joint_position_max_axis_delta_m':max(abs(fresh[n]['rest'][i]-bones[n]['rest'][i]) for n in fresh for i in [3,7,11]),'canonical_creator_fbx_bind_matrix_max_delta':max(abs(a-b) for n in fresh for a,b in zip(fresh[n]['rest'],canonical_fbx[n]['rest'])),'shared_matrix_delta':shared,'sample_roundtrip_max_delta_m':deltas,'morph_default_weights':defaults,'morph_surface_delta_max_m':nonzero,'digit_weighted_vertices':digits,'uv_audit':uv,'sole_z_m':float(neutral[:,2].min()),'sha256':hashlib.sha256((ex/f'VincentAdult_{tier}.fbx').read_bytes()).hexdigest()});print('WORLD_FRESH_GATE',tier,json.dumps(checks),flush=True)
report['all_pass']=all(all(t['checks'].values()) for t in report['tiers'].values());(out/'world-export-validation.json').write_text(json.dumps(report,indent=2)+'\n');print('WORLD_GATE',report['all_pass'],flush=True);assert report['all_pass']
