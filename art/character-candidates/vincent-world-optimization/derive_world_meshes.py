"""Isolated Vincent world topology experiment; frozen creator bundle is read-only.
Index-only surface reduction before atlas packing. Original source attributes retained.
"""
import bpy,sys,json,hashlib,ctypes,numpy as np
from pathlib import Path
from collections import Counter
out=Path(__file__).resolve().parent;root=out.parent;src=root/'vincent-adult-production';sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
reference=src/'VincentAdult_AtlasBakeSource.blend';bpy.ops.wm.open_mainfile(filepath=str(reference),use_scripts=False)
source=bpy.data.objects['VincentAdult_CreatorMesh'];rig=bpy.data.objects['VincentAdult_Rig'];poses.reset(rig)
for k in source.data.shape_keys.key_blocks:k.value=0
bpy.context.view_layer.update();me=source.data;me.calc_loop_triangles();co=np.array([v.co[:] for v in me.vertices],dtype=np.float32);morph_names=[k.name for k in me.shape_keys.key_blocks if k.name!='Basis'];morphs={n:np.array([v.co[:] for v in me.shape_keys.key_blocks[n].data],dtype=np.float32) for n in morph_names};weights={v.index:[(source.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in me.vertices};parts=[x.value for x in me.attributes['source_part_id'].data];uv=me.uv_layers['SourceUV'].data;normal=me.corner_normals
# Skin deformation contributes to simplification cost, including curled digit motion.
posed=[]
for mode in range(2):
 poses.reset(rig)
 for s,sign in [('L',1),('R',-1)]:
  poses.rot(rig,'UpperArm.'+s,(0,1,0),(42 if mode==0 else -25)*sign);poses.rot(rig,'Forearm.'+s,(0,1,0),(-8 if mode==0 else -85)*sign)
  poses.rot(rig,'Thigh.'+s,(1,0,0),(-30 if mode else 0)*sign);poses.rot(rig,'Calf.'+s,(1,0,0),50 if mode else 0)
  if mode:
   for digit in ['Index','Middle','Ring','Pinky']:
    for j,angle in [(1,15),(2,30),(3,20)]:poses.rot(rig,f'{digit}{j}.{s}',(0,1,0),angle*sign)
 bpy.context.view_layer.update();posed.append(np.array([v.co[:] for v in source.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices],dtype=np.float32)-co)
poses.reset(rig);bpy.context.view_layer.update()
# Same portable cloth adaptation already inspected on creator; other source shaders unchanged.
for mat in me.materials:
 if not any(x in mat.name for x in ['vincent_shirt','vincent_pants','vincent_socks']):continue
 for node in mat.node_tree.nodes:
  if node.type=='DISPLACEMENT':
   inp=node.inputs['Scale'];assert not inp.is_linked
   inp.default_value*=.12
lib=ctypes.CDLL('/tmp/ct-meshoptimizer/libmeshoptimizer-v1.3.dylib');fn=lib.meshopt_simplifyWithAttributes;U=ctypes.POINTER(ctypes.c_uint);F=ctypes.POINTER(ctypes.c_float);B=ctypes.POINTER(ctypes.c_ubyte);Z=ctypes.c_size_t;fn.restype=Z;fn.argtypes=[U,U,Z,F,Z,Z,F,Z,F,Z,B,Z,ctypes.c_float,ctypes.c_uint,F]
def ptr(a,t):return a.ctypes.data_as(t)
budgets={
 'WorldNear':{'body_skin':3600,'shirt':1800,'pants':1800,'belt_buckle':60,'belt':250,'socks':180,'shoes':700,'shoes_bright':250,'button':40,'eyeball':320,'glasses':700,'hair':1100,'head_skin':4200,'head_hair':150,'teeth':450},
 'WorldFar':{'body_skin':1500,'shirt':850,'pants':950,'belt_buckle':24,'belt':120,'socks':90,'shoes':340,'shoes_bright':100,'button':12,'eyeball':120,'glasses':300,'hair':500,'head_skin':1600,'head_hair':80,'teeth':100}}
# Material interface vertices must not collapse independently on adjacent surfaces.
vertex_mats={}
for poly in me.polygons:
 for vi in poly.vertices:vertex_mats.setdefault(vi,set()).add(poly.material_index)
material_borders={vi for vi,mats in vertex_mats.items() if len(mats)>1}
groups=[]
for part,mi in sorted(set((parts[t.vertices[0]],t.material_index) for t in me.loop_triangles)):
 name=me.materials[mi].name.removeprefix('BAKE_vincent_');key=('body_skin' if part==0 else 'head_skin') if name=='skin' else ('head_hair' if part==9 else 'hair') if name=='hair' else 'button' if part in [1,2,3,4] else name
 uses_uv=any(n.type=='UVMAP' and any(o.links for o in n.outputs) for n in me.materials[mi].node_tree.nodes)
 ts=[t for t in me.loop_triangles if parts[t.vertices[0]]==part and t.material_index==mi];table={};old=[];lis=[];uvs=[];ns=[];idx=[]
 for t in ts:
  for vi,li in zip(t.vertices,t.loops):
   u=tuple(uv[li].uv) if uses_uv else (0.,0.);n=tuple(normal[li].vector);k=(vi,*u);j=table.get(k)
   if j is None:j=len(old);table[k]=j;old.append(vi);lis.append(li);uvs.append(u);ns.append(n)
   idx.append(j)
 old=np.array(old,dtype=np.int32);pos=np.ascontiguousarray(co[old]);uvs=np.array(uvs,dtype=np.float32);ns=np.array(ns,dtype=np.float32);indices=np.array(idx,dtype=np.uint32)
 groups.append((part,mi,key,old,np.array(lis,dtype=np.int32),pos,uvs,ns,indices))
report={'source_reference_sha256':hashlib.sha256(reference.read_bytes()).hexdigest(),'method':'Index-only source-surface simplification before independent world atlas packing. No source-position movement. Near retains five source morphs; far facial morphs omitted deliberately.','tiers':{}}
for tier in budgets:
 allold=[];allli=[];allns=[];faces=[];fmat=[];records=[];limits=2 if tier=='WorldFar' else 4
 for part,mi,key,old,lis,pos,uvs,ns,indices in groups:
  if tier=='WorldFar' and key=='teeth':continue
  attrs=np.ascontiguousarray(np.concatenate([uvs,ns,*([morphs[n][old]-co[old] for n in morph_names] if tier=='WorldNear' else []),*[p[old] for p in posed]],axis=1));metric=np.array([.2,.2,.025,.025,.025]+[1]*(attrs.shape[1]-5),dtype=np.float32);locks=np.array([1 if int(vi) in material_borders else 0 for vi in old],dtype=np.uint8)
  # Preserve exact ground plane while allowing its planar triangulation to simplify.
  target=budgets[tier][key];dst=np.empty_like(indices);error=ctypes.c_float(0)
  n=fn(ptr(dst,U),ptr(indices,U),len(indices),ptr(pos,F),len(pos),12,ptr(attrs,F),attrs.shape[1]*4,ptr(metric,F),len(metric),ptr(locks,B),target*3,.010 if tier=='WorldNear' else .025,4|16|256|(32 if tier=='WorldFar' else 0),ctypes.byref(error));output=dst[:n];used,remap=np.unique(output,return_inverse=True);offset=len(allold);allold.extend(old[used].tolist());allli.extend(lis[used].tolist());allns.extend(ns[used].tolist());faces.extend((remap.reshape(-1,3)+offset).tolist());fmat.extend([mi]*(n//3));record={'source_part':part,'material':me.materials[mi].name,'triangles':n//3,'target':target,'error_m':error.value};records.append(record);print('GROUP',tier,record,flush=True)
 seen_faces=set();keep=[]
 for fi,face in enumerate(faces):
  key=tuple(sorted(face))
  if key not in seen_faces:seen_faces.add(key);keep.append(fi)
 faces=[faces[i] for i in keep];fmat=[fmat[i] for i in keep]
 old=np.array(allold,dtype=np.int32);lis=np.array(allli,dtype=np.int32);geo=bpy.data.meshes.new(tier+'Geometry');geo.from_pydata(co[old].tolist(),[],faces);geo.update();mesh=bpy.data.objects.new('VincentAdult_'+tier+'Mesh',geo);bpy.context.scene.collection.objects.link(mesh);mesh.parent=rig;mesh.matrix_parent_inverse.identity();mesh.matrix_world.identity()
 for mat in me.materials:geo.materials.append(mat)
 for p,mi in zip(geo.polygons,fmat):p.use_smooth=True;p.material_index=mi
 geo.normals_split_custom_set_from_vertices(allns)
 for layer in me.uv_layers:
  if layer.name=='AtlasUV':continue
  new=geo.uv_layers.new(name=layer.name);data=np.array([layer.data[int(li)].uv[:] for li in lis],dtype=np.float32);new.data.foreach_set('uv',np.array([data[l.vertex_index] for l in geo.loops],dtype=np.float32).ravel())
 geo.uv_layers.active=geo.uv_layers['SourceUV'];geo.uv_layers['SourceUV'].active_render=True
 for name in ['source_generated','source_object']:
  attr=geo.attributes.new(name=name,type='FLOAT_VECTOR',domain='POINT');attr.data.foreach_set('vector',np.array([me.attributes[name].data[int(i)].vector[:] for i in old],dtype=np.float32).ravel())
 for name in ['Col','specular']:
  a=me.color_attributes.get(name)
  if a:
   attr=geo.color_attributes.new(name=name,type=a.data_type,domain='CORNER');attr.data.foreach_set('color',np.array([a.data[int(lis[l.vertex_index])].color[:] for l in geo.loops],dtype=np.float32).ravel())
 a=geo.attributes.new(name='source_vertex_index',type='INT',domain='POINT');a.data.foreach_set('value',old)
 a=geo.attributes.new(name='source_part_id',type='INT',domain='POINT');a.data.foreach_set('value',[parts[int(i)] for i in old])
 for g in source.vertex_groups:mesh.vertex_groups.new(name=g.name)
 weight_trim={'affected_vertices':0,'max_discarded_weight':0,'sum_discarded_weight':0}
 for i,si in enumerate(old):
  w=sorted(weights[int(si)],key=lambda x:-x[1]);kept=w[:limits];discard=sum(x[1] for x in w[limits:]);weight_trim['affected_vertices']+=int(discard>0);weight_trim['max_discarded_weight']=max(weight_trim['max_discarded_weight'],discard);weight_trim['sum_discarded_weight']+=discard;total=sum(x[1] for x in kept)
  for n,value in kept:mesh.vertex_groups[n].add([i],value/total,'REPLACE')
 arm=mesh.modifiers.new('Vincent own skin','ARMATURE');arm.object=rig
 if tier=='WorldNear':
  mesh.shape_key_add(name='Basis').value=0
  for n in morph_names:k=mesh.shape_key_add(name=n);k.data.foreach_set('co',morphs[n][old].ravel());k.value=0
 geo.calc_loop_triangles();report['tiers'][tier]={'mesh':mesh.name,'triangles':len(geo.loop_triangles),'vertices':len(geo.vertices),'bones':55,'max_influences':limits,'facial_morphs':morph_names if tier=='WorldNear' else [],'weight_trim':weight_trim,'parts':records,'sole_z_m':float(co[old,2].min()),'height_m':float(np.ptp(co[old,2]))};mesh.hide_render=True
bpy.data.objects.remove(source,do_unlink=True);poses.reset(rig);bpy.context.view_layer.update();bpy.ops.wm.save_as_mainfile(filepath=str(out/'VincentAdult_WorldSource.blend'));(out/'world-topology.json').write_text(json.dumps(report,indent=2)+'\n');print('WORLD_TOPOLOGY_READY',json.dumps({k:{n:v[n] for n in ['triangles','vertices','sole_z_m']} for k,v in report['tiers'].items()}),flush=True)
