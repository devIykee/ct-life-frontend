"""Weight-only Rain candidate: remove groups that are absent from the source deform rig.
No canonical writes, no geometry/UV/rest/material edits. Source-group replay proves origin.
"""
import bpy,json,hashlib,zipfile,numpy as np
from pathlib import Path
from collections import defaultdict
from mathutils import Vector
from mathutils.bvhtree import BVHTree
root=Path(__file__).resolve().parents[1];base=root/'rain-adult-production';out=root/'rain-weight-correction';ex=out/'exports';ex.mkdir(parents=True,exist_ok=True);(out/'archive').mkdir(exist_ok=True)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
inputs=[base/'RainAdult_Unity.blend',base/'RainAdult_LODs.blend',base/'manifest.json',base/'lod-manifest.json',base/'bundle-ready.json',*sorted((base/'exports').glob('*'))]
inputs+=[root/'rain-cloth-correction/exports/RainAdult_MetallicSmoothness.png'] if (root/'rain-cloth-correction/exports/RainAdult_MetallicSmoothness.png').exists() else []
frozen={str(p.relative_to(root)):sha(p) for p in inputs if p.is_file()}
archive=out/'archive/rain-before-weight-correction.zip'
if not archive.exists():
 with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
  for p in inputs:
   if p.is_file():z.write(p,str(p.relative_to(root)))
  z.writestr('input-sha256.json',json.dumps(frozen,indent=2))
replay=json.loads((out/'source-weight-replay.json').read_text());trace=json.loads((out/'weighted-source-trace.json').read_text());assert trace['zero_unknown_valid_bone_targets']
bpy.ops.wm.open_mainfile(filepath=str(base/'RainAdult_LODs.blend'),use_scripts=False);rig=bpy.data.objects['RainAdult_Rig'];bones={b.name for b in rig.data.bones}
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update()
records=json.loads((base/'manifest.json').read_text())['source_parts'];pnames={r['part_id']:r['part'] for r in records};creator=bpy.data.objects['RainAdult_CreatorMesh'];meshes=[bpy.data.objects[n] for n in ['RainAdult_CreatorMesh','RainAdult_LOD1Mesh','RainAdult_LOD2Mesh']]
def geometry_digest(o):
 me=o.data;h=hashlib.sha256()
 for collection,property,width,dtype in [(me.vertices,'co',3,np.float32),(me.loops,'vertex_index',1,np.int32),(me.polygons,'loop_start',1,np.int32),(me.polygons,'loop_total',1,np.int32),(me.uv_layers.active.data,'uv',2,np.float32),(me.corner_normals,'vector',3,np.float32)]:
  a=np.empty(len(collection)*width,dtype=dtype);collection.foreach_get(property,a);h.update(a.tobytes())
 return h.hexdigest()
def ws(o,v):return {o.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>1e-7 and o.vertex_groups[g.group].name in bones}
def set_ws(o,index,weights):
 weights=sorted([(n,max(0,w)) for n,w in weights.items() if w>1e-7],key=lambda x:-x[1])[:4];total=sum(w for _,w in weights);assert total>0
 for group_index in [g.group for g in o.data.vertices[index].groups]:o.vertex_groups[group_index].remove([index])
 for n,w in weights:o.vertex_groups[n].add([index],w/total,'REPLACE')
old_weights={o.name:[ws(o,v) for v in o.data.vertices] for o in meshes};digests={o.name:geometry_digest(o) for o in meshes};rest={b.name:{'parent':b.parent.name if b.parent else None,'matrix':[list(r) for r in b.matrix_local]} for b in rig.data.bones}
# Source part ids keep creator vertex order. Assert replay equivalence before changing anything.
part_indices=defaultdict(list)
for v in creator.data.vertices:part_indices[pnames[creator.data.attributes['source_part_id'].data[v.index].value]].append(v.index)
replay_error=0;creator_changed=[];delta={}
for part in ['body','jeans','shoes']:
 indices=part_indices[part];raw=replay[part];assert len(indices)==len(raw['before']),(part,len(indices),len(raw['before']))
 for local,i in enumerate(indices):
  before=old_weights[creator.name][i];expected=raw['before'][local]
  error=max(abs(before.get(n,0)-expected.get(n,0)) for n in set(before)|set(expected));replay_error=max(replay_error,error)
  assert error<1e-4,(part,local,i,error,before,expected)
  if not raw['removed_groups_per_vertex'][local]:continue
  after=raw['after'][local];set_ws(creator,i,after);new=ws(creator,creator.data.vertices[i]);delta[i]={n:new.get(n,0)-before.get(n,0) for n in set(new)|set(before)};creator_changed.append(i)
# Only the tiny stale forearm contributions need a delta field on reduced topology.
# Hip/ankle Head contributions are fully removed directly, preserving surviving LOD weights' ratios.
creator.data.calc_loop_triangles();body_ids=set(part_indices['body']);body_triangles=[tuple(t.vertices) for t in creator.data.loop_triangles if all(i in body_ids for i in t.vertices)];coordinates=[v.co.copy() for v in creator.data.vertices];body_tree=BVHTree.FromPolygons(coordinates,body_triangles,all_triangles=True)
def body_delta(p):
 co,no,index,distance=body_tree.find_nearest(p);tri=body_triangles[index];a,b,c=(coordinates[i] for i in tri);u=b-a;v=c-a;w=co-a;den=u.dot(u)*v.dot(v)-u.dot(v)**2
 if abs(den)<1e-20:weights=(1,0,0)
 else:
  beta=(w.dot(u)*v.dot(v)-w.dot(v)*u.dot(v))/den;gamma=(w.dot(v)*u.dot(u)-w.dot(u)*u.dot(v))/den;weights=(1-beta-gamma,beta,gamma)
 d=defaultdict(float)
 for i,f in zip(tri,weights):
  for n,x in delta.get(i,{}).items():d[n]+=max(0,f)*x
 return dict(d),distance
for ob in meshes[1:]:
 for v in ob.data.vertices:
  part=pnames[ob.data.attributes['source_part_id'].data[v.index].value];old=old_weights[ob.name][v.index]
  if part in ['jeans','shoes'] and old.get('Head',0)>0:set_ws(ob,v.index,{n:w for n,w in old.items() if n!='Head'})
  elif part=='body':
   d,distance=body_delta(v.co)
   if max([abs(x) for x in d.values()],default=0)>1e-7:set_ws(ob,v.index,{n:old.get(n,0)+d.get(n,0) for n in set(old)|set(d)})
report={'status':'Blender anatomical correction candidate; native identical-phase visual review still required.','method':'Ignore stale vertex groups whose source deform bone does not exist. Creator replays exact original evaluated source weights, filters invalid bones, then normalizes/prunes4. Jeans/shoes LODs remove invalid Head and normalize existing anatomical weights. Tiny stale forearm contribution deltas are interpolated from creator onto body LODs. Geometry, UVs, corner normals, material and55-bone bind remain unchanged.','source_trace':'weighted-source-trace.json','stale_source_group_count':len(trace['stale_weighted_deform_groups']),'original_creator_weight_replay_max_error':replay_error,'canonical_hashes_before':frozen,'archive':str(archive.relative_to(out)),'archive_sha256':sha(archive),'tiers':{},'checks':{}}
for ob in meshes:
 tier='Creator' if ob==creator else ('LOD1' if 'LOD1' in ob.name else 'LOD2');by=defaultdict(lambda:{'changed_vertices':0,'removed_head_weight_sum':0,'max_head_before':0,'head_vertices_after':0});changed=0;unknown=0
 for v in ob.data.vertices:
  old=old_weights[ob.name][v.index];new=ws(ob,v);part=pnames[ob.data.attributes['source_part_id'].data[v.index].value];error=max(abs(old.get(n,0)-new.get(n,0)) for n in set(old)|set(new))
  if error>1e-7:changed+=1;by[part]['changed_vertices']+=1
  if part in ['body','jeans','shoes']:
   by[part]['removed_head_weight_sum']+=old.get('Head',0)-new.get('Head',0);by[part]['max_head_before']=max(by[part]['max_head_before'],old.get('Head',0));by[part]['head_vertices_after']+=int(new.get('Head',0)>1e-7)
  assert 1<=len(new)<=4,(ob.name,v.index,new);assert abs(sum(new.values())-1)<1e-5,(ob.name,v.index,new,sum(new.values()))
  if part in ['body','top','jeans','shoes']:assert new.get('Head',0)<1e-7,(ob.name,part,v.index)
 assert geometry_digest(ob)==digests[ob.name],ob.name
 ob.data.calc_loop_triangles();report['tiers'][tier]={'mesh':ob.name,'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles),'changed_vertices':changed,'parts':dict(by),'geometry_uv_normals_sha256':digests[ob.name],'geometry_uv_normals_identical':True,'max_influences':max(len(ws(ob,v)) for v in ob.data.vertices)}
 bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
 target=ex/f'RainAdult_{tier}.fbx';bpy.ops.export_scene.fbx(filepath=str(target),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,path_mode='STRIP');report['tiers'][tier]['fbx']='exports/'+target.name;report['tiers'][tier]['sha256']=sha(target)
report['rest_skeleton']=rest
report['checks']['all_geometry_uv_normals_unchanged']=all(geometry_digest(o)==digests[o.name] for o in meshes)
report['checks']['exact_55_bone_rest_unchanged']=rest=={b.name:{'parent':b.parent.name if b.parent else None,'matrix':[list(r) for r in b.matrix_local]} for b in rig.data.bones}
report['checks']['canonical_assets_unchanged']=frozen=={p:sha(root/p) for p in frozen}
report['checks']['no_unmapped_valid_source_deform_bones']=trace['zero_unknown_valid_bone_targets'];report['checks']['all_head_weights_removed_from_body_garments_feet']=all(x['head_vertices_after']==0 for t in report['tiers'].values() for p,x in t['parts'].items() if p in ['body','jeans','shoes'])
report['all_pass']=all(report['checks'].values());assert report['all_pass'];bpy.ops.wm.save_as_mainfile(filepath=str(out/'RainAdult_WeightsCorrected.blend'));(out/'weight-correction-validation.json').write_text(json.dumps(report,indent=2)+'\n');print('RAIN_WEIGHT_CANDIDATE',json.dumps({t:{'changed':v['changed_vertices'],'parts':v['parts']} for t,v in report['tiers'].items()}),flush=True)
