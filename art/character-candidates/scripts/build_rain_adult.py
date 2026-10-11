"""One Rain source-quality adult derivative; original source and frozen Snow are read-only."""
import bpy,json,re,math,hashlib
from pathlib import Path
from collections import defaultdict
from mathutils import Vector
root=Path(__file__).resolve().parents[1];out=root/'rain-adult-production';out.mkdir(exist_ok=True)
source=root/'raw/rain/Rain v3.3/rain_v3.2.blend';source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False)
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
sr=bpy.data.objects['RIG-rain'];sr.data.pose_position='REST'
sb={b.name:(sr.matrix_world@b.head_local,sr.matrix_world@b.tail_local) for b in sr.data.bones}
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
HEAD_SCALE=.80;LEG_SCALE=1.12;HIP=.8211357
anchor=sb['DEF-Head'][0]
def adult(v):
 p=Vector(v);orig=p.copy();w=smooth(1.265,1.395,p.z);p=p.lerp(anchor+(p-anchor)*HEAD_SCALE,w);p.z+=min(orig.z,HIP)*(LEG_SCALE-1);return p
def ref(n,tail=False):return sb[n][int(tail)]
bones=[]
def add(n,h,t,parent=None):bones.append((n,adult(h),adult(t),parent))
add('Root',(0,0,0),(0,0,.1))
add('Hips',ref('DEF-Pelvis'),ref('DEF-Spine1',True),'Root')
add('Spine',ref('DEF-Spine1'),ref('DEF-Spine2',True),'Hips')
add('Chest',ref('DEF-Spine3'),ref('DEF-Spine3',True),'Spine')
add('Neck',ref('DEF-Neck'),ref('DEF-Neck',True),'Chest')
add('Head',ref('DEF-Head'),ref('DEF-Head',True),'Neck')
add('Jaw',ref('DEF-Jaw'),ref('DEF-Jaw',True),'Head')
for s in ['L','R']:
 add('Eye.'+s,ref('DEF-Eye.'+s),ref('DEF-Eye.'+s,True),'Head')
 add('Shoulder.'+s,ref('DEF-Clavicle.'+s),ref('DEF-Clavicle.'+s,True),'Chest')
 add('UpperArm.'+s,ref('DEF-Upperarm1.'+s),ref('DEF-Upperarm2.'+s,True),'Shoulder.'+s)
 add('Forearm.'+s,ref('DEF-Forearm1.'+s),ref('DEF-Forearm2.'+s,True),'UpperArm.'+s)
 add('Hand.'+s,ref('DEF-Hand.'+s),ref('DEF-Middle2.'+s),'Forearm.'+s)
 for digit in ['Thumb','Index','Middle','Ring','Pinky']:
  for j in range(1,4):
   src=f'DEF-{digit}{j if digit=="Thumb" else j+1}.{s}'
   add(f'{digit}{j}.{s}',ref(src),ref(src,True),'Hand.'+s if j==1 else f'{digit}{j-1}.{s}')
 add('Thigh.'+s,ref('DEF-Thigh1.'+s),ref('DEF-Thigh2.'+s,True),'Hips')
 add('Calf.'+s,ref('DEF-Shin1.'+s),ref('DEF-Shin2.'+s,True),'Thigh.'+s)
 add('Foot.'+s,ref('DEF-Foot.'+s),ref('DEF-Foot.'+s,True),'Calf.'+s)
 add('Toes.'+s,ref('DEF-Toe.'+s),ref('DEF-Toe.'+s,True),'Foot.'+s)
arm=bpy.data.armatures.new('RainAdult_GameSkeleton');rig=bpy.data.objects.new('RainAdult_Rig',arm);scene.collection.objects.link(rig)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for n,h,t,parent in bones:
 b=arm.edit_bones.new(n);b.head=h;b.tail=t
 if parent:b.parent=arm.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True
names={b[0] for b in bones};remap_log={}
def remap(n,p):
 if not n.startswith('DEF-'):return None
 side='.'+n[-1] if n.endswith(('.L','.R')) else ''
 for match,target in [('Clavicle','Shoulder'),('Upperarm','UpperArm'),('Forearm','Forearm'),('Hand','Hand'),('Thigh','Thigh'),('Shin','Calf'),('Foot','Foot'),('Toe','Toes')]:
  if match in n:return target+side
 m=re.search(r'DEF-(Index|Middle|Ring|Pinky)(\d)',n)
 if m:return 'Hand'+side if int(m[2])==1 else m[1]+str(int(m[2])-1)+side
 if 'Thumb' in n:
  m=re.search(r'Thumb[_]?(\d)',n);return 'Thumb'+(m[1] if m else '1')+side
 if n=='DEF-Pelvis':return 'Hips'
 if n in ['DEF-Spine1','DEF-Spine2']:return 'Spine'
 if n=='DEF-Spine3':return 'Chest'
 if n=='DEF-Neck':return 'Neck'
 if 'Scarf' in n:return 'Chest'
 return 'Head'
parts=[];stats=[]
keep=['body','head','eyes','eyebrows','eyelashes','hair_main','hair_ponytail','hair_strand','hairband','top','jeans','scarf','shoes','gums_upper','gums_lower','tongue']
for part in keep:
 src=bpy.data.objects['GEO-rain-'+part];src.hide_viewport=False;src.hide_set(False)
 for m in src.modifiers:
  if m.type=='SUBSURF':
   m.show_viewport=False
 dg=bpy.context.evaluated_depsgraph_get();ev=src.evaluated_get(dg);me=bpy.data.meshes.new_from_object(ev,preserve_all_data_layers=True,depsgraph=dg)
 ob=src.copy();ob.data=me;ob.name='Rain_REF_'+part;scene.collection.objects.link(ob);ob.parent=None;ob.animation_data_clear()
 for m in list(ob.modifiers):ob.modifiers.remove(m)
 idx=[p.material_index for p in me.polygons];me.materials.clear()
 for s in src.material_slots:me.materials.append(s.material)
 for p,i in zip(me.polygons,idx):p.material_index=i;p.hide=False;p.use_smooth=True
 groupnames={g.index:g.name for g in src.vertex_groups};ws=[];fallback=0
 for v in me.vertices:
  p=src.matrix_world@v.co;mapping=defaultdict(float)
  for g in v.groups:
   gn=groupnames.get(g.group,'');dest=remap(gn,p)
   if dest in names:mapping[dest]+=g.weight;remap_log[gn]=dest
  if part=='eyes':mapping={('Eye.L' if p.x>=0 else 'Eye.R'):1}
  if part.startswith(('hair','gum')) or part in ['eyebrows','eyelashes','tongue']:mapping={'Head':1}
  if part=='scarf':
   neck=smooth(1.255,1.32,p.z);mapping={'Chest':1-neck,'Neck':neck}
  if not mapping:mapping={'Head' if p.z>1.28 else 'Hips':1};fallback+=1
  ws.append(sorted(mapping.items(),key=lambda x:-x[1])[:4]);v.co=adult(p);v.hide=False
  if part=='hair_ponytail':
   base=adult((0,.105,1.582));distance=max(0,v.co.y-base.y);radius=.104;angle=min(distance/radius,math.radians(78));remaining=max(0,distance-radius*angle);cross=v.co.z-base.z
   v.co.y=base.y+radius*math.sin(angle)+remaining*math.cos(angle)-cross*math.sin(angle)
   v.co.z=base.z-radius*(1-math.cos(angle))-remaining*math.sin(angle)+cross*math.cos(angle)
  if part=='scarf':
   # Small normal clearance on copied cloth; retain original garment's detailed surface.
   radial=Vector((v.co.x,v.co.y+.012,0));radial.normalize();v.co+=radial*(.006*smooth(1.235,1.29,p.z))
 ob.matrix_world.identity();ob.parent=rig;ob.matrix_parent_inverse.identity();ob.vertex_groups.clear();gs={n:ob.vertex_groups.new(name=n) for n in sorted(names)}
 for i,weights in enumerate(ws):
  total=sum(w for _,w in weights)
  for n,w in weights:
   if w>0:gs[n].add([i],w/total,'REPLACE')
 mod=ob.modifiers.new('Game skin','ARMATURE');mod.object=rig
 me.calc_loop_triangles();stats.append({'part':part,'triangles':len(me.loop_triangles),'vertices':len(me.vertices),'fallback_vertices':fallback,'max_influences':max(len(w) for w in ws),'materials':[m.name for m in me.materials]});parts.append(ob)
for ob in list(bpy.data.objects):
 if ob not in parts and ob!=rig:bpy.data.objects.remove(ob,do_unlink=True)
offset=-min(v.co.z for ob in parts for v in ob.data.vertices)
for ob in parts:
 for v in ob.data.vertices:v.co.z+=offset
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for b in arm.edit_bones:
 if b.name!='Root':b.head.z+=offset;b.tail.z+=offset
bpy.ops.object.mode_set(mode='OBJECT')
for t in list(bpy.data.texts):
 if t.name.endswith('.py'):bpy.data.texts.remove(t)
for im in bpy.data.images:
 if im.source in ['FILE','TILED'] and im.filepath:im.filepath=bpy.path.abspath(im.filepath)
height=max(v.co.z for ob in parts for v in ob.data.vertices)
report={'source':str(source.relative_to(root)),'source_sha256':source_hash,'head_scale':HEAD_SCALE,'leg_scale':LEG_SCALE,'height_m':height,'ground_offset_m':offset,'ponytail_change':'Authored downward78-degree arc with intact attachment; rigid Head binding, no secondary dynamics.','scarf_change':'Source base geometry retained andup to6mm radial collar clearance; collar blends Chest/Neck.','bone_count':len(bones),'triangles':sum(p['triangles'] for p in stats),'vertices':sum(p['vertices'] for p in stats),'parts':stats,'source_weight_mapping':remap_log,'limitations':['Source-quality geometry retained: no decimation or final atlas yet.','Face compiled rigid for this first geometry review; authored facial source preserved separately, not claimed animated.','Hair and scarf source secondary motion is not represented by the 55-bone proof.','No Unity import or device validation.','Material shaders and source UVs retained for quality comparison.']}
(out/'geometry-manifest.json').write_text(json.dumps(report,indent=2))
# Human enum mapping names match Snow but transforms belong only to Rain's source proportions.
mapdata=json.loads((root/'unity-snow/unity-humanoid-map.json').read_text());mapdata['skeleton_object']='RainAdult_Rig';(out/'unity-humanoid-map.json').write_text(json.dumps(mapdata,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'RainAdult_SourceQuality.blend'))
assert hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
print('RAIN_ADULT_BUILT',len(bones),report['triangles'],height,flush=True)
