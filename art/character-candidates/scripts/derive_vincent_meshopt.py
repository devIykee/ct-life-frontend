"""Index-only, UV-aware adult Vincent reduction using verified meshoptimizer1.3.
Every retained position, UV, skin weight and facial shape sample is copied from the source.
The runtime does not depend on this offline MIT tool.
"""
import bpy,json,sys,hashlib,ctypes,numpy as np
from pathlib import Path
from collections import Counter,defaultdict
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
out=root/'vincent-adult-production';ex=out/'exports';reference=out/'VincentAdult_AtlasHighReference.blend'
bpy.ops.wm.open_mainfile(filepath=str(reference),use_scripts=False);source=bpy.data.objects['VincentAdult_CreatorMesh'];source.name='VincentAdult_AtlasHighReference';rig=bpy.data.objects['VincentAdult_Rig'];poses.reset(rig);bpy.context.view_layer.update();me=source.data;me.calc_loop_triangles();manifest=json.loads((out/'high-reference-manifest.json').read_text());shape_names=[k.name for k in me.shape_keys.key_blocks if k.name!='Basis'];co=np.array([v.co[:] for v in me.vertices],dtype=np.float32);morphs={n:np.array([v.co[:] for v in me.shape_keys.key_blocks[n].data],dtype=np.float32) for n in shape_names};weights={v.index:[(source.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in me.vertices};ids=[x.value for x in me.attributes['source_part_id'].data];uv=me.uv_layers['AtlasUV'].data;normals=me.corner_normals
with bpy.data.libraries.load(str(out/'VincentAdult_AtlasBakeSource.blend'),link=False) as (available,loaded):loaded.meshes=['VincentAdult_CreatorGeometry']
bake_mesh=loaded.meshes[0];assert len(bake_mesh.vertices)==len(me.vertices);teeth={vi for p in bake_mesh.polygons if 'teeth' in bake_mesh.materials[p.material_index].name for vi in p.vertices};preserve_surfaces={vi for p in bake_mesh.polygons if any(n in bake_mesh.materials[p.material_index].name for n in ['shoes','belt_buckle']) for vi in p.vertices};bpy.data.meshes.remove(bake_mesh)
immutable={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [reference,*ex.glob('*.png')]}
# Pose deltas participate in simplification cost so skinning regions retain useful samples.
posed=[]
for mode in [0,1]:
 poses.reset(rig)
 for s,sign in [('L',1),('R',-1)]:
  poses.rot(rig,'UpperArm.'+s,(0,1,0),(42 if mode==0 else -25)*sign);poses.rot(rig,'Forearm.'+s,(0,1,0),(-8 if mode==0 else -85)*sign)
  poses.rot(rig,'Thigh.'+s,(1,0,0),(-30 if mode else 0)*sign);poses.rot(rig,'Calf.'+s,(1,0,0),50 if mode else 0)
 bpy.context.view_layer.update();posed.append(np.array([v.co[:] for v in source.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices],dtype=np.float32)-co)
poses.reset(rig);bpy.context.view_layer.update();source.hide_render=True
normal_image=bpy.data.images.load(str(ex/'VincentAdult_Normal.png'),check_existing=False);normal_image.colorspace_settings.name='Non-Color';source.material_slots[0].material.node_tree.nodes['Normal'].image=normal_image
lib=ctypes.CDLL('/tmp/ct-meshoptimizer/libmeshoptimizer-v1.3.dylib');fn=lib.meshopt_simplifyWithAttributes;U=ctypes.POINTER(ctypes.c_uint);F=ctypes.POINTER(ctypes.c_float);B=ctypes.POINTER(ctypes.c_ubyte);Z=ctypes.c_size_t;fn.restype=Z;fn.argtypes=[U,U,Z,F,Z,Z,F,Z,F,Z,B,Z,ctypes.c_float,ctypes.c_uint,F]
def ptr(a,t):return a.ctypes.data_as(t)
budgets={'Creator':{'body':31000,'head':32984,'eyeball_L':1600,'eyeball_R':1600,'eyeglasses':2000,'hair':4500,'button':230},'LOD1':{'body':24000,'head':18000,'eyeball_L':1000,'eyeball_R':1000,'eyeglasses':1500,'hair':3000,'button':150},'LOD2':{'body':21000,'head':10000,'eyeball_L':600,'eyeball_R':600,'eyeglasses':1000,'hair':2000,'button':100}}
prepared=[]
for rec in manifest['source_parts']:
 part=rec['part'];tris=[t for t in me.loop_triangles if ids[t.vertices[0]]==rec['part_id']];edges=Counter();adj=defaultdict(set)
 for t in tris:
  for a,b in [(t.vertices[0],t.vertices[1]),(t.vertices[1],t.vertices[2]),(t.vertices[2],t.vertices[0])]:edges[tuple(sorted((a,b)))]+=1;adj[a].add(b);adj[b].add(a)
 boundary={v for edge,count in edges.items() if count==1 for v in edge};protected=set(boundary)
 if part in ['body','head']:protected.update(j for v in boundary for j in adj[v])
 if part=='body':
  protected.update(v for v in adj if v in preserve_surfaces)
  protected.update(v for v in adj if any(n.startswith(('Hand.','Thumb','Index','Middle','Ring','Pinky')) and w>.001 for n,w in weights[v]))
  protected.update(v for v in adj if co[v,2]<.001)
 if part=='head':
  protected.update(v for v in adj if v not in teeth and ((abs(abs(co[v,0])-.029)<.022 and 1.614<co[v,2]<1.639 and co[v,1]<-.094) or (abs(co[v,0])<.042 and 1.526<co[v,2]<1.554 and co[v,1]<-.115) or (abs(co[v,0])<.018 and 1.560<co[v,2]<1.627 and co[v,1]<-.140) or (abs(co[v,0])>.085 and 1.568<co[v,2]<1.658)))
 if part=='hair':protected.update(v for v in adj if co[v,2]<1.646 and co[v,1]<-.016)
 table={};old=[];uvs=[];ns=[];idx=[]
 for t in tris:
  for vi,li in zip(t.vertices,t.loops):
   u=tuple(uv[li].uv);n=tuple(normals[li].vector);key=(vi,*u,*[round(x,6) for x in n]);j=table.get(key)
   if j is None:j=len(old);table[key]=j;old.append(vi);uvs.append(u);ns.append(n)
   idx.append(j)
 old=np.array(old,dtype=np.int32);pos=np.ascontiguousarray(co[old]);uvs=np.array(uvs,dtype=np.float32);ns=np.array(ns,dtype=np.float32);attrs=np.ascontiguousarray(np.concatenate([uvs,ns,*[morphs[n][old]-co[old] for n in shape_names],*[p[old] for p in posed]],axis=1));metric=np.array([2,2,.1,.1,.1]+[1]*21,dtype=np.float32);assert attrs.shape[1]==len(metric)==26;locks=np.array([int(int(v) in protected) for v in old],dtype=np.uint8);indices=np.array(idx,dtype=np.uint32)
 prepared.append((rec,old,pos,uvs,ns,attrs,metric,locks,indices,protected));print('PREPARED',part,len(tris),len(old),len(protected),flush=True)
overrides_path=out/'uv-protection-overrides.json';overrides=json.loads(overrides_path.read_text()) if overrides_path.exists() else {}
result={'source':reference.name,'tool':'meshoptimizer1.3 (MIT), offline only','method':'Index-only attribute-aware simplification; split UV/normal vertices, UV/normal/five facial deltas/two skinned-pose deltas in error metric. Exact locked hands, lid/lip landmarks, garment boundaries and soles. All retained vertex attributes come from original source samples.','frozen_reference_sha256':immutable,'tiers':{}}
for tier in ['Creator','LOD1','LOD2']:
 allold=[];alluv=[];allnormal=[];allpid=[];faces=[];records=[]
 for rec,old,pos,uvs,ns,attrs,metric,locks,indices,protected in prepared:
  extra=set(overrides.get(tier,[]))&set(map(int,old));locked=protected|extra;locks=np.array([int(int(v) in locked) for v in old],dtype=np.uint8)
  part=rec['part'];target=budgets[tier]['button' if part.startswith('button') else part];dst=np.empty_like(indices);error=ctypes.c_float(0);n=len(indices)
  if target*3<n:
   n=fn(ptr(dst,U),ptr(indices,U),len(indices),ptr(pos,F),len(pos),12,ptr(attrs,F),attrs.shape[1]*4,ptr(metric,F),len(metric),ptr(locks,B),target*3,{'Creator':.004,'LOD1':.008,'LOD2':.012}[tier],4|16|256,ctypes.byref(error));output=dst[:n]
  else:output=indices
  used,remap=np.unique(output,return_inverse=True);offset=len(allold);allold.extend(old[used].tolist());alluv.extend(uvs[used].tolist());allnormal.extend(ns[used].tolist());allpid.extend([rec['part_id']]*len(used));faces.extend((remap.reshape(-1,3)+offset).tolist());missing=locked-set(int(v) for v in old[used]);assert not missing,(part,'protected removed',len(missing));records.append({'part':part,'source_triangles':len(indices)//3,'target_triangles':target,'actual_triangles':len(output)//3,'protected_source_vertices':len(locked),'local_uv_repair_vertices':len(extra),'meshoptimizer_absolute_error_m':error.value});print('PART',tier,records[-1],flush=True)
 old=np.array(allold,dtype=np.int32);geometry=bpy.data.meshes.new(f'VincentAdult_{tier}Geometry');geometry.from_pydata(co[old].tolist(),[],faces);geometry.update();mesh=bpy.data.objects.new(f'VincentAdult_{tier}Mesh',geometry);bpy.context.scene.collection.objects.link(mesh);mesh.parent=rig;mesh.matrix_parent_inverse.identity();mesh.matrix_world.identity();geometry.materials.append(source.material_slots[0].material)
 for p in geometry.polygons:p.use_smooth=True
 geometry.normals_split_custom_set_from_vertices(allnormal);layer=geometry.uv_layers.new(name='AtlasUV');layer.data.foreach_set('uv',np.array([alluv[loop.vertex_index] for loop in geometry.loops],dtype=np.float32).ravel());layer.active_render=True
 attr=geometry.attributes.new(name='source_part_id',type='INT',domain='POINT');attr.data.foreach_set('value',allpid);attr=geometry.attributes.new(name='source_vertex_index',type='INT',domain='POINT');attr.data.foreach_set('value',old)
 for g in source.vertex_groups:mesh.vertex_groups.new(name=g.name)
 for i,si in enumerate(old):
  for n,w in weights[int(si)]:mesh.vertex_groups[n].add([i],w,'REPLACE')
 arm=mesh.modifiers.new('Vincent own skin','ARMATURE');arm.object=rig;mesh.shape_key_add(name='Basis')
 for n in shape_names:mesh.shape_key_add(name=n).data.foreach_set('co',morphs[n][old].ravel())
 geometry.calc_loop_triangles();entry={'fbx':f'exports/VincentAdult_{tier}.fbx','mesh':mesh.name,'triangles':len(geometry.loop_triangles),'vertices':len(geometry.vertices),'parts':records,'bone_names':sorted(b.name for b in rig.data.bones),'height_m':float(np.ptp(co[old,2])),'sole_z_m':float(co[old,2].min()),'material':'VincentAdult_Atlas','max_influences':max(len(v.groups) for v in geometry.vertices),'facial_morphs':shape_names}
 bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.export_scene.fbx(filepath=str(ex/f'VincentAdult_{tier}.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=False,path_mode='STRIP');entry['sha256']=hashlib.sha256((ex/f'VincentAdult_{tier}.fbx').read_bytes()).hexdigest();result['tiers'][tier]=entry;mesh.hide_render=True;print('TIER_READY',tier,entry['triangles'],entry['vertices'],flush=True)
bpy.data.objects.remove(source,do_unlink=True);poses.reset(rig);bpy.data.objects['VincentAdult_CreatorMesh'].hide_render=False;bpy.ops.wm.save_as_mainfile(filepath=str(out/'VincentAdult_Tiers.blend'))
for name,h in immutable.items():assert hashlib.sha256((out/name if name.endswith('.blend') else ex/name).read_bytes()).hexdigest()==h,name
(out/'tier-manifest.json').write_text(json.dumps(result,indent=2)+'\n');print('VINCENT_MESHOPT_TIERS_DERIVED_REFERENCE_UNCHANGED',flush=True)
