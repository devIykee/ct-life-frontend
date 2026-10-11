"""One reversible Snow adult-proportion / runtime-rig exploration. Not a production avatar."""
import bpy,math,json,re,bmesh
from pathlib import Path
from collections import defaultdict
from mathutils import Vector
root=Path(__file__).resolve().parents[1];out=root/'snow-adult-proof';out.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(next((root/'raw/snow').rglob('*.blend'))),use_scripts=False)
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
source_rig=bpy.data.objects['RIG-Snow']
source_bones={b.name:(source_rig.matrix_world@b.head_local,source_rig.matrix_world@b.tail_local) for b in source_rig.data.bones}
# Preserve head modelling while reducing its relative size. Stretch legs, not the face.
HEAD_SCALE=.76;LEG_SCALE=1.09;HIP=.89;NECK_BLEND=(1.38,1.50)
eye_centers={s:source_bones['DEF-Eye.'+s][0] for s in ['L','R']}
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def adult(p,eye_region=False):
 p=Vector(p);orig=p.copy()
 # A restrained orbital correction keeps the authored eye shape while reducing its scale.
 if eye_region and p.y<-.055:
  e=eye_centers['L' if p.x>=0 else 'R'];d=((p.x-e.x)**2+(p.z-e.z)**2)**.5
  f=1-.12*(1-smooth(.035,.065,d));p=e+(p-e)*f
 headblend=smooth(*NECK_BLEND,orig.z)
 anchor=Vector((0,-.011,1.47));shrunk=anchor+(p-anchor)*HEAD_SCALE
 p=p.lerp(shrunk,headblend)
 # Broad shoulders and torso remain subtle; arms use the same deformation as their joints.
 xwidth=1.045*(1-headblend)+headblend
 p.x*=xwidth
 p.z+=min(orig.z,HIP)*(LEG_SCALE-1)
 return p
# New 27-bone hierarchy: 24 body/root + jaw and eye bones.
def ref(n,tail=False):return source_bones[n][1 if tail else 0].copy()
bones=[]
def add(n,h,t,parent=None):bones.append((n,adult(h),adult(t),parent))
add('Root',(0,0,0),(0,0,.12))
add('Hips',ref('DEF-Hips'),ref('DEF-Spine',True),'Root')
add('Spine',ref('DEF-Spine'),ref('DEF-RibCage',True),'Hips')
add('Chest',ref('DEF-Chest'),ref('DEF-Chest',True),'Spine')
add('Neck',ref('DEF-Neck'),ref('DEF-Neck',True),'Chest')
add('Head',ref('DEF-Head'),ref('DEF-Head',True),'Neck')
add('Jaw',(0,-.025,1.545),(0,-.128,1.487),'Head')
for side in ['L','R']:
 add('Eye.'+side,ref('DEF-Eye.'+side),ref('DEF-Eye.'+side,True),'Head')
 add('Shoulder.'+side,ref('DEF-Shoulder.'+side),ref('DEF-Shoulder.'+side,True),'Chest')
 add('UpperArm.'+side,ref('DEF-UpperArm_1.'+side),ref('DEF-UpperArm_2.'+side,True),'Shoulder.'+side)
 add('Forearm.'+side,ref('DEF-Forearm_1.'+side),ref('DEF-Forearm_2.'+side,True),'UpperArm.'+side)
 add('Hand.'+side,ref('DEF-Wrist.'+side),ref('DEF-Finger_Middle1.'+side),'Forearm.'+side)
 add('Fingers.'+side,ref('DEF-Finger_Middle1.'+side),ref('DEF-Finger_Middle3.'+side,True),'Hand.'+side)
 add('Thigh.'+side,ref('DEF-Thigh_1.'+side),ref('DEF-Thigh_2.'+side,True),'Hips')
 add('Calf.'+side,ref('DEF-Knee_1.'+side),ref('DEF-Knee_2.'+side,True),'Thigh.'+side)
 add('Foot.'+side,ref('DEF-Foot.'+side),ref('DEF-Foot.'+side,True),'Calf.'+side)
 add('Toes.'+side,ref('DEF-Toes.'+side),ref('DEF-Toes.'+side,True),'Foot.'+side)
arm=bpy.data.armatures.new('SnowAdult_GameSkeleton');rig=bpy.data.objects.new('SnowAdult_Rig',arm);bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for n,h,t,par in bones:
 b=arm.edit_bones.new(n);b.head=h;b.tail=t
 if par:b.parent=arm.edit_bones[par]
bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True
allnames={b[0] for b in bones}
def remap(n,p):
 if not n.startswith('DEF-'):return None
 s='.'+n[-1] if n.endswith(('.L','.R')) else ''
 if 'Shoulder' in n:return 'Shoulder'+s
 if 'UpperArm' in n:return 'UpperArm'+s
 if 'Forearm' in n:return 'Forearm'+s
 if 'Wrist' in n:return 'Hand'+s
 if 'Finger' in n:return ('Hand' if 'Thumb' in n or 'Carpal' in n else 'Fingers')+s
 if 'Thigh' in n:return 'Thigh'+s
 if 'Knee' in n:return 'Calf'+s
 if 'Foot' in n:return 'Foot'+s
 if 'Toes' in n:return 'Toes'+s
 if 'Hip' in n:return 'Hips'
 if n in ['DEF-Spine','DEF-RibCage']:return 'Spine'
 if n=='DEF-Chest':return 'Chest'
 if n=='DEF-Neck':return 'Neck'
 return 'Head'
# Component budgets retain detail around the face, hands and hair silhouette.
budgets={
 'head':(1800,800,300),'hair_base':(675,350,110),'body':(1050,500,200),
 'shirt':(580,250,120),'pants':(620,360,150),'shoes_base':(380,140,80),
 'shoes_bottom':(60,40,20),'shoes_parts':(50,30,15),'eyebrows':(60,40,20),'eyes':(700,180,60)}
keep={'GEO-snow-'+x:x for x in budgets};built=[];stats={'head_scale':HEAD_SCALE,'leg_scale':LEG_SCALE,'eye_scale':.88,'rig_bones':len(bones),'lods':{},'warnings':[]}
for original_name,part in keep.items():
 src=bpy.data.objects[original_name];src.hide_viewport=False;src.hide_set(False)
 for m in src.modifiers:
  if m.type=='SUBSURF':m.show_viewport=False
 dg=bpy.context.evaluated_depsgraph_get();ev=src.evaluated_get(dg)
 mesh=bpy.data.meshes.new_from_object(ev,preserve_all_data_layers=True,depsgraph=dg)
 obj=src.copy();obj.data=mesh;obj.name='Adult_REF_'+part;bpy.context.scene.collection.objects.link(obj)
 obj.parent=None;obj.matrix_world=src.matrix_world.copy();obj.animation_data_clear()
 for m in list(obj.modifiers):obj.modifiers.remove(m)
 # The mesh material slots may be object-linked in the source; carry their effective material.
 material_indices=[p.material_index for p in mesh.polygons]
 mesh.materials.clear()
 for slot in src.material_slots:mesh.materials.append(slot.material)
 for poly,idx in zip(mesh.polygons,material_indices):poly.material_index=idx
 group_names={g.index:g.name for g in src.vertex_groups}
 weights=[]
 for v in mesh.vertices:
  p=src.matrix_world@v.co;mapping=defaultdict(float)
  for g in v.groups:
   dest=remap(group_names.get(g.group,''),p)
   if dest in allnames:mapping[dest]+=g.weight
  if part=='eyes':mapping={('Eye.L' if p.x>=0 else 'Eye.R'):1}
  if not mapping:mapping={'Head' if p.z>1.39 else 'Hips':1}
  weights.append(sorted(mapping.items(),key=lambda x:-x[1])[:4])
  v.co=adult(p,eye_region=part in ['head','eyes'])
 obj.matrix_world.identity();obj.parent=rig;obj.matrix_parent_inverse.identity();obj.vertex_groups.clear()
 groups={n:obj.vertex_groups.new(name=n) for n in allnames}
 for i,ws in enumerate(weights):
  total=sum(w for _,w in ws)
  for n,w in ws:
   if w>0:groups[n].add([i],w/total,'REPLACE')
 mod=obj.modifiers.new('Game skin','ARMATURE');mod.object=rig
 for p in mesh.polygons:p.use_smooth=True
 built.append(obj);obj.hide_render=True;obj.hide_viewport=True
 mesh.calc_loop_triangles();base_tri=len(mesh.loop_triangles)
 for level,target in enumerate(budgets[part]):
  lo=obj.copy();lo.data=mesh.copy();lo.name=f'Adult_LOD{level}_{part}';bpy.context.scene.collection.objects.link(lo)
  lo.hide_viewport=False;lo.hide_render=level!=0;lo.hide_set(False)
  for m in list(lo.modifiers):lo.modifiers.remove(m)
  bpy.context.view_layer.objects.active=lo;bpy.ops.object.select_all(action='DESELECT');lo.select_set(True)
  dec=lo.modifiers.new('Component budget','DECIMATE');dec.ratio=min(1,target/base_tri);dec.use_collapse_triangulate=True
  if part=='eyes' and level==0:dec.decimate_type='UNSUBDIV';dec.iterations=2
  bpy.ops.object.modifier_apply(modifier=dec.name)
  bm=bmesh.new();bm.from_mesh(lo.data)
  bmesh.ops.dissolve_degenerate(bm,dist=1e-7,edges=list(bm.edges))
  loose=[v for v in bm.verts if not v.link_faces]
  if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
  bmesh.ops.triangulate(bm,faces=list(bm.faces))
  bm.to_mesh(lo.data);bm.free();lo.data.validate(verbose=False,clean_customdata=False);lo.data.update();lo.data.calc_loop_triangles()
  # Preserve the detailed source's shading while retaining the measured low-resolution silhouette.
  normals=lo.modifiers.new('Source surface normals','DATA_TRANSFER');normals.object=obj;normals.use_loop_data=True;normals.data_types_loops={'CUSTOM_NORMAL'};normals.loop_mapping='POLYINTERP_NEAREST'
  bpy.ops.object.modifier_apply(modifier=normals.name)
  # Collapse can interpolate weights; trim again after simplification.
  for v in lo.data.vertices:
   ws=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-5],key=lambda x:-x[1]);total=sum(w for _,w in ws[:4])
   for idx,_ in ws[4:]:lo.vertex_groups[idx].remove([v.index])
   for idx,w in ws[:4]:lo.vertex_groups[idx].add([v.index],w/total,'REPLACE')
  mod=lo.modifiers.new('Game skin','ARMATURE');mod.object=rig
  lo.hide_viewport=level!=0;built.append(lo)
  stats['lods'].setdefault(str(level),[]).append({'part':part,'triangles':len(lo.data.loop_triangles),'vertices':len(lo.data.vertices),'max_influences':max(sum(g.weight>1e-5 for g in v.groups) for v in lo.data.vertices)})
# Remove all source scene objects only in this derived in-memory scene; originals on disk remain untouched.
for ob in list(bpy.data.objects):
 if ob not in built and ob!=rig:bpy.data.objects.remove(ob,do_unlink=True)
# One limited identity shape and one consistent body/build shape demonstrate extensibility, not a finished editor.
for ob in built:
 part=ob.name.split('_',2)[2]
 if 'LOD0' not in ob.name:continue
 ob.shape_key_add(name='Basis')
 if part=='head':
  key=ob.shape_key_add(name='JawWidth_Subtle')
  for v,k in zip(ob.data.vertices,key.data):
   mask=(1-smooth(1.61,1.72,v.co.z))*smooth(1.49,1.55,v.co.z)
   k.co.x=v.co.x*(1+.07*mask)
 elif part in ['shirt','pants','body']:
  key=ob.shape_key_add(name='Build_Subtle')
  for v,k in zip(ob.data.vertices,key.data):
   mask=smooth(.80,.98,v.co.z)*(1-smooth(1.32,1.50,v.co.z))*(1-smooth(.20,.36,abs(v.co.x)))
   k.co.x=v.co.x*(1+.07*mask);k.co.y=v.co.y*(1+.05*mask)
# All images stay linked to original source files. No repacking of large textures.
for t in list(bpy.data.texts):
 if t.name.endswith('.py'):bpy.data.texts.remove(t)
bpy.context.view_layer.objects.active=rig
stats['lod_totals']={k:sum(p['triangles'] for p in v) for k,v in stats['lods'].items()}
stats['limitations']=['Material/texture atlas not baked; original source shaders retained for quality comparison.','Facial rig reduced to rigid head + eye bones; full expressions not rebuilt.','Two subtle identity/build test shapes only; garment extremes and LOD morph transfer not validated.','Automated component simplification, not finished manual retopology.','Unity Humanoid import and device performance untested.']
(out/'metrics.json').write_text(json.dumps(stats,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'snow_adult_candidate.blend'))
print('ADULT_PROOF_BUILT',stats['lod_totals'],'bones',len(bones))
