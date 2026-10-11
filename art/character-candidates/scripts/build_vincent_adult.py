"""Vincent v1: one independent adult game-rig proof. Raw and production Snow/Rain are read-only.
Bakes evaluated source surface and actual source facial control poses; transfers torso weights
from the source rig's own low proxy, retains high-resolution hand/digit skinning.
"""
import bpy,bmesh,json,math,re,hashlib,numpy as np
from pathlib import Path
from collections import defaultdict
from mathutils import Vector
from mathutils.bvhtree import BVHTree
root=Path(__file__).resolve().parents[1];out=root/'vincent-adult-production';out.mkdir(exist_ok=True)
source=root/'raw/vincent/vincent.blend';source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False)
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
sr=bpy.data.objects['RIG-Vincent']
if sr.animation_data:sr.animation_data.action=None
source_parts=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('GEO-vincent_') and o.visible_get() and 'UV_copy' not in o.name and 'cornea' not in o.name]
for o in bpy.data.objects:
 if o.animation_data:
  for d in o.animation_data.drivers:
   if 'modifiers[' in d.data_path:d.mute=True
 for m in o.modifiers:
  if m.type=='SUBSURF':
   m.levels=0 if 'button_' in o.name else 1;m.render_levels=m.levels;m.show_viewport=True;m.show_render=True
bpy.context.view_layer.update()
# Rest joints and evaluated source neutral surface use the same original coordinate frame.
sb={b.name:(sr.matrix_world@b.head_local,sr.matrix_world@b.tail_local) for b in sr.data.bones}
def ref(n,tail=False):return sb[n][int(tail)].copy()
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
HEAD_SCALE=.82;OVERALL_SCALE=1.08;anchor=Vector((0,-.022,1.337))
def adult(v):
 p=Vector(v);w=smooth(1.29,1.385,p.z);return p.lerp(anchor+(p-anchor)*HEAD_SCALE,w)*OVERALL_SCALE
# Capture complete evaluated facial deformations, not just corrective-key values.
head=bpy.data.objects['GEO-vincent_head'];baseline={b.name:b.matrix_basis.copy() for b in sr.pose.bones}
def face_points():
 sr.update_tag();bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get();ev=head.evaluated_get(dg);me=ev.to_mesh();a=np.empty(len(me.vertices)*3,dtype=np.float32);me.vertices.foreach_get('co',a);ev.to_mesh_clear();return a.reshape(-1,3)
face_samples={'Basis':face_points()};recipes={
 'BlinkLeft':{'eyelid_up_ctrl_L':(0,0,-.03),'eyelid_low_ctrl_L':(0,0,.009)},
 'BlinkRight':{'eyelid_up_ctrl_R':(0,0,-.03),'eyelid_low_ctrl_R':(0,0,.009)},
 'Smile':{'mouth_corner_L':(-.018,-.012,.018),'mouth_corner_R':(.018,-.012,.018)},
 'BrowUp':{'brow_ctrl_L':(0,0,.018),'brow_ctrl_R':(0,0,.018)},
 'MouthOpen':{'mouth_ctrl':(0,-.025,-.022)},
}
for label,recipe in recipes.items():
 for b in sr.pose.bones:b.matrix_basis=baseline[b.name]
 for bn,loc in recipe.items():sr.pose.bones[bn].location=loc
 face_samples[label]=face_points()
for b in sr.pose.bones:b.matrix_basis=baseline[b.name]
sr.update_tag();bpy.context.view_layer.update()
bones=[]
def add(n,h,t,parent=None):
 h=adult(h);t=adult(t);assert (h-t).length>.001,n;bones.append((n,h,t,parent))
add('Root',(0,0,0),(0,0,.1))
add('Hips',ref('pelvis_def'),ref('spine_1_def',True),'Root')
add('Spine',ref('spine_1_def'),ref('spine_2_def',True),'Hips')
add('Chest',ref('spine_3_def'),ref('neck_1_def'),'Spine')
add('Neck',ref('neck_1_def'),ref('head_def_mstr'),'Chest')
add('Head',ref('head_def_mstr'),(0,-.01949,1.65),'Neck')
add('Jaw',ref('maxi'),ref('maxi',True),'Head')
for s in ['L','R']:
 add('Eye.'+s,ref('eye_def_'+s),ref('eye_def_'+s,True),'Head')
 add('Shoulder.'+s,ref('clavi_def_'+s),ref('arm_def_'+s),'Chest')
 add('UpperArm.'+s,ref('arm_def_'+s),ref('forearm_def_'+s),'Shoulder.'+s)
 add('Forearm.'+s,ref('forearm_def_'+s),ref('hand_def_'+s),'UpperArm.'+s)
 add('Hand.'+s,ref('hand_def_'+s),ref('fing_mid_2_def_'+s),'Forearm.'+s)
 for digit,prefix in [('Thumb','thumb'),('Index','ind'),('Middle','mid'),('Ring','ring'),('Pinky','lit')]:
  for j in range(1,4):
   src=f'fing_{prefix}_{j if digit=="Thumb" else j+1}_def_{s}'
   h,t=ref(src),ref(src,True)
   # BlenRig's thumb metacarpal points toward the wrist; game proximal points outward.
   if digit=='Thumb' and j==1:h,t=t,h
   add(f'{digit}{j}.{s}',h,t,'Hand.'+s if j==1 else f'{digit}{j-1}.{s}')
 add('Thigh.'+s,ref('thigh_def_'+s),ref('shin_def_'+s),'Hips')
 add('Calf.'+s,ref('shin_def_'+s),ref('foot_def_'+s),'Thigh.'+s)
 add('Foot.'+s,ref('foot_def_'+s),ref('toe_1_def_'+s),'Calf.'+s)
 add('Toes.'+s,ref('toe_1_def_'+s),ref('toe_2_def_'+s,True),'Foot.'+s)
arm=bpy.data.armatures.new('VincentAdult_GameSkeleton');rig=bpy.data.objects.new('VincentAdult_Rig',arm);scene.collection.objects.link(rig)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for n,h,t,parent in bones:
 b=arm.edit_bones.new(n);b.head=h;b.tail=t
 if parent:b.parent=arm.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True
names={b[0] for b in bones};mapping_log={}
def remap(n):
 if not sr.data.bones.get(n):return None
 side='.'+n[-1] if n.endswith(('_L','_R')) else ''
 m=re.match(r'fing_(thumb|ind|mid|ring|lit)_(\d)_def_[LR]$',n)
 if m:
  digit={'thumb':'Thumb','ind':'Index','mid':'Middle','ring':'Ring','lit':'Pinky'}[m[1]];j=int(m[2]);return f'{digit}{j if digit=="Thumb" else j-1}{side}' if digit=='Thumb' or j>1 else 'Hand'+side
 m=re.match(r'fing_(thumb|ind|mid|ring|lit)_fix_(up|low)_(\d)_[LR]$',n)
 if m:
  digit={'thumb':'Thumb','ind':'Index','mid':'Middle','ring':'Ring','lit':'Pinky'}[m[1]];return f'{digit}{min(3,int(m[3]))}{side}'
 for prefix,dest in [('hand_','Hand'),('forearm_','Forearm'),('arm_','UpperArm'),('clavi_','Shoulder'),('thigh_','Thigh'),('shin_','Calf'),('foot_','Foot'),('toe_','Toes')]:
  if n.startswith(prefix):return dest+side
 if n.startswith('pelvis'):return 'Hips'
 if n.startswith(('spine_1','spine_2')):return 'Spine'
 if n.startswith('spine_3'):return 'Chest'
 if n.startswith('neck_'):return 'Neck'
 return None

def weights_of(o,v):
 result=defaultdict(float)
 for g in v.groups:
  n=o.vertex_groups[g.group].name;dst=remap(n)
  if dst in names and g.weight>1e-7:result[dst]+=g.weight;mapping_log[n]=dst
 return dict(result)
# Source proxy carries authored cloth/limb weights hidden behind the high film mesh cage.
proxy=bpy.data.objects['PROXY-vincent_body'];proxy.hide_set(False);proxy.hide_viewport=False
for m in proxy.modifiers:
 if m.type=='SUBSURF':m.show_viewport=False
bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get();pev=proxy.evaluated_get(dg);pme=bpy.data.meshes.new_from_object(pev,preserve_all_data_layers=True,depsgraph=dg);pme.calc_loop_triangles()
pcoords=[proxy.matrix_world@v.co for v in pme.vertices];pweights=[weights_of(proxy,v) for v in pme.vertices];ptris=[tuple(t.vertices) for t in pme.loop_triangles];bvh=BVHTree.FromPolygons(pcoords,ptris,all_triangles=True)
def transferred(p):
 co,no,index,distance=bvh.find_nearest(p);tri=ptris[index];a,b,c=(pcoords[i] for i in tri);v0=b-a;v1=c-a;v2=co-a;d00=v0.dot(v0);d01=v0.dot(v1);d11=v1.dot(v1);d20=v2.dot(v0);d21=v2.dot(v1);den=d00*d11-d01*d01
 if abs(den)<1e-18:bs=(1,0,0)
 else:
  v=(d11*d20-d01*d21)/den;w=(d00*d21-d01*d20)/den;bs=(1-v-w,v,w)
 result=defaultdict(float)
 for i,factor in zip(tri,bs):
  for name,w in pweights[i].items():result[name]+=max(0,factor)*w
 return dict(result),distance
parts=[];stats=[];distances=[]
for src in source_parts:
 part=src.name.removeprefix('GEO-vincent_');ev=src.evaluated_get(dg);me=bpy.data.meshes.new_from_object(ev,preserve_all_data_layers=True,depsgraph=dg)
 ob=src.copy();ob.data=me;ob.name='Vincent_REF_'+part;scene.collection.objects.link(ob);ob.parent=None;ob.animation_data_clear()
 for m in list(ob.modifiers):ob.modifiers.remove(m)
 indices=[p.material_index for p in me.polygons];me.materials.clear()
 for slot in src.material_slots:me.materials.append(slot.material)
 for p,i in zip(me.polygons,indices):p.material_index=i;p.hide=False;p.use_smooth=True
 # Spectacle lenses/cornea are transparent reflection overlays, not iris colour.
 # Remove only lens polygons for the mobile opaque material proof; retain frames and actual eyeballs.
 lens_removed=0
 if part=='eyeglasses':
  bm=bmesh.new();bm.from_mesh(me);doomed=[f for f in bm.faces if 'glass_glass' in me.materials[f.material_index].name or me.materials[f.material_index].name=='vincent_glasses_glass'];lens_removed=len(doomed);bmesh.ops.delete(bm,geom=doomed,context='FACES');bm.to_mesh(me);bm.free()
 # Retain original local/generated coordinates for procedural eye/cloth shaders after joining.
 original_coordinates=[v.co.copy() for v in me.vertices]
 tc=src.data.texspace_location.copy();ts=src.data.texspace_size.copy()
 generated=me.attributes.new(name='source_generated',type='FLOAT_VECTOR',domain='POINT')
 objco=me.attributes.new(name='source_object',type='FLOAT_VECTOR',domain='POINT')
 for i,p in enumerate(original_coordinates):
  generated.data[i].vector=Vector([.5+(p[j]-tc[j])/(2*ts[j]) if abs(ts[j])>1e-8 else .5 for j in range(3)])
  objco.data[i].vector=p
 weights=[];direct=0;transfers=0
 for v in me.vertices:
  p=src.matrix_world@v.co
  if part=='body':
   ws=weights_of(src,v)
   # Detailed authored palm/finger groups stay exact. Cage-only torso uses source proxy interpolation.
   if not ws or sum(ws.values())<.25:ws,distance=transferred(p);distances.append(distance);transfers+=1
   else:direct+=1
  elif part.startswith('button_'):ws,distance=transferred(p);distances.append(distance);transfers+=1
  elif part.startswith('eyeball_'):ws={'Eye.'+part[-1]:1}
  elif part=='head':
   neck=1-smooth(1.317,1.403,p.z);ws={'Neck':neck,'Head':1-neck}
  else:ws={'Head':1}
  assert ws,(part,v.index,tuple(p));weights.append(ws);v.co=adult(p);v.hide=False
 if part=='body':
  # Film-cage transitions become ordinary skin weights. Smooth only the shoulder seam zone;
  # do not alter detailed hands, hips, face, geometry or the source file.
  adjacent=[set() for _ in me.vertices]
  for edge in me.edges:
   a,b=edge.vertices;adjacent[a].add(b);adjacent[b].add(a)
  eligible=[v.index for v in me.vertices if 1.15<v.co.z<1.46 and .025<abs(v.co.x)<.34]
  for iteration in range(6):
   updated=list(weights)
   for i in eligible:
    if not adjacent[i]:continue
    value=defaultdict(float)
    for n,w in weights[i].items():value[n]+=.65*w
    for j in adjacent[i]:
     for n,w in weights[j].items():value[n]+=.35*w/len(adjacent[i])
    updated[i]=dict(value)
   weights=updated
 ob.matrix_world.identity();ob.parent=rig;ob.matrix_parent_inverse.identity();ob.hide_viewport=False;ob.hide_render=False;ob.hide_set(False);ob.vertex_groups.clear();gs={n:ob.vertex_groups.new(name=n) for n in sorted(names)}
 for i,ws in enumerate(weights):
  ws=sorted([(n,w) for n,w in ws.items() if w>1e-7],key=lambda a:-a[1])[:4];total=sum(w for _,w in ws);assert total>0
  for n,w in ws:gs[n].add([i],w/total,'REPLACE')
 if part=='head':
  assert len(me.vertices)==len(face_samples['Basis']);basis=ob.shape_key_add(name='Basis');assert max((v.co-adult(src.matrix_world@Vector(p))).length for v,p in zip(me.vertices,face_samples['Basis']))<1e-5
  for label,pts in face_samples.items():
   if label=='Basis':continue
   key=ob.shape_key_add(name=label);key.value=0
   for v,p in zip(key.data,pts):v.co=adult(src.matrix_world@Vector(p))
 mod=ob.modifiers.new('Game skin','ARMATURE');mod.object=rig
 me.calc_loop_triangles();stats.append({'part':part,'vertices':len(me.vertices),'triangles':len(me.loop_triangles),'direct_source_weight_vertices':direct,'proxy_transfer_vertices':transfers,'removed_lens_faces':lens_removed,'materials':[m.name for m in me.materials]});parts.append(ob)
for o in list(bpy.data.objects):
 if o not in parts and o!=rig:bpy.data.objects.remove(o,do_unlink=True)
# Ground the complete candidate once; all morphs share the same translation.
offset=-min(v.co.z for o in parts for v in o.data.vertices)
for o in parts:
 if o.data.shape_keys:
  for key in o.data.shape_keys.key_blocks:
   for v in key.data:v.co.z+=offset
 else:
  for v in o.data.vertices:v.co.z+=offset
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for b in arm.edit_bones:
 if b.name!='Root':b.head.z+=offset;b.tail.z+=offset
bpy.ops.object.mode_set(mode='OBJECT')
for t in list(bpy.data.texts):
 if t.name.endswith('.py'):bpy.data.texts.remove(t)
texture_dir=out/'source-textures';texture_dir.mkdir(exist_ok=True)
for im in bpy.data.images:
 if im.packed_file:
  target=texture_dir/(Path(im.filepath).name or (im.name+'.png'));target.write_bytes(im.packed_file.data);im.unpack(method='REMOVE');im.filepath=str(target);im.reload();im.pack()
 elif im.source in ['FILE','TILED'] and im.filepath:im.filepath=bpy.path.abspath(im.filepath)
height=max(v.co.z for o in parts for v in o.data.vertices)
report={'status':'Adult source-quality mesh/rig proof. Expressions and body weights require visual pose review before export.','source':'https://studio.blender.org/characters/vincent/v1/','source_sha256':source_hash,'head_scale':HEAD_SCALE,'uniform_scale':OVERALL_SCALE,'shoulder_weight_smoothing':'Six local35-percent neighbor-average passes at shoulder transition only; detailed fingers excluded.','height_m':height,'ground_offset_m':offset,'bones':len(bones),'triangles':sum(p['triangles'] for p in stats),'vertices':sum(p['vertices'] for p in stats),'parts':stats,'max_skin_influences':4,'source_weight_mapping':mapping_log,'proxy_transfer_distance_m':{'maximum':max(distances),'p95':float(np.percentile(distances,95))},'facial_control_recipes':recipes,'facial_keys':list(recipes),'facial_source':'Evaluated original bone/cage/corrective output, baked to five independent head morphs. Source remains unchanged.','material_change':'Transparent cornea and spectacle-lens reflection overlays excluded for opaque game atlas; actual eye colour mesh and glasses frame retained.','limitations':['No Unity import/device acceptance.','No animation/controller/customization claim.','Facial morph values are initial sampled controls pending actual visual review.','Current source materials are not yet the final one-material atlas.','Film twist/soft-tissue/garment correctives are reduced to a55-bone linear game rig; deformation samples must be reviewed.']}
(out/'geometry-manifest.json').write_text(json.dumps(report,indent=2)+'\n')
mp=json.loads((root/'unity-snow/unity-humanoid-map.json').read_text());mp['skeleton_object']=rig.name;mp['notes']=['Own Vincent source proportions and rest joints; never copy Snow or placeholder Avatar.','Source A-pose must be canonicalized to Humanoid T-pose by Unity importer without changing bind geometry.','Five baked source facial morphs are independent from Humanoid muscles; Jaw bone is not weighted.','No Unity/device validation yet.'];(out/'unity-humanoid-map.json').write_text(json.dumps(mp,indent=2)+'\n')
bpy.ops.wm.save_as_mainfile(filepath=str(out/'VincentAdult_SourceQuality.blend'))
assert hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
print('VINCENT_ADULT_BUILT',report['triangles'],height,report['proxy_transfer_distance_m'],flush=True)
