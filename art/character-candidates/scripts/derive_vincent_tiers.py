"""Creator/world meshes from one real atlas reference, with portable facial morphs.
Partwise QEM; exact hand/opening masks, source-normal transfer, same rest/atlas.
"""
import bpy,bmesh,json,sys,hashlib,shutil,numpy as np
from pathlib import Path
from mathutils import kdtree
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
out=root/'vincent-adult-production';ex=out/'exports';reference=out/'VincentAdult_AtlasHighReference.blend'
if not reference.exists():
 shutil.copy2(out/'VincentAdult_Unity.blend',reference);shutil.copy2(out/'manifest.json',out/'high-reference-manifest.json');shutil.copy2(ex/'VincentAdult_Creator.fbx',out/'archive'/'VincentAdult_AtlasHighReference.fbx')
bpy.ops.wm.open_mainfile(filepath=str(reference),use_scripts=False)
source=bpy.data.objects['VincentAdult_CreatorMesh'];source.name='VincentAdult_AtlasHighReference';rig=bpy.data.objects['VincentAdult_Rig'];poses.reset(rig);bpy.context.view_layer.update();manifest=json.loads((out/'high-reference-manifest.json').read_text());bones={b.name for b in rig.data.bones}
immutable={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [reference,*ex.glob('*.png')]};source.hide_render=True
with bpy.data.libraries.load(str(out/'VincentAdult_AtlasBakeSource.blend'),link=False) as (available,loaded):
 loaded.meshes=['VincentAdult_CreatorGeometry']
bake_mesh=loaded.meshes[0];assert len(bake_mesh.vertices)==len(source.data.vertices)
assert max((a.co-b.co).length for a,b in zip(bake_mesh.vertices,source.data.vertices))==0
teeth_vertices={vi for p in bake_mesh.polygons if 'teeth' in bake_mesh.materials[p.material_index].name for vi in p.vertices}
attr=source.data.attributes.new(name='source_teeth',type='BOOLEAN',domain='POINT');attr.data.foreach_set('value',[i in teeth_vertices for i in range(len(source.data.vertices))]);bpy.data.meshes.remove(bake_mesh)
shape_names=[k.name for k in source.data.shape_keys.key_blocks if k.name!='Basis']
basis=np.array([v.co[:] for v in source.data.shape_keys.key_blocks['Basis'].data],dtype=np.float32)
for i,n in enumerate(shape_names):
 delta=np.array([v.co[:] for v in source.data.shape_keys.key_blocks[n].data],dtype=np.float32)-basis
 attr=source.data.attributes.new(name=f'facial_delta_{i}',type='FLOAT_VECTOR',domain='POINT');attr.data.foreach_set('vector',delta.ravel())
# These are explicit quality targets, not a licence to collapse protected surfaces.
budgets={'Creator':{'body':31000,'head':32984,'eyeball_L':1600,'eyeball_R':1600,'eyeglasses':2000,'hair':4500,'button':230},'LOD1':{'body':24000,'head':18000,'eyeball_L':1000,'eyeball_R':1000,'eyeglasses':1500,'hair':3000,'button':150},'LOD2':{'body':21000,'head':10000,'eyeball_L':600,'eyeball_R':600,'eyeglasses':1000,'hair':2000,'button':100}}
def ntris(o):o.data.calc_loop_triangles();return len(o.data.loop_triangles)
def select(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o

def partcopy(record,tier):
 ob=source.copy();ob.data=source.data.copy();ob.name=f'BUILD_{tier}_{record["part"]}';bpy.context.scene.collection.objects.link(ob);ob.hide_render=False;ob.shape_key_clear()
 bm=bmesh.new();bm.from_mesh(ob.data);ids=bm.verts.layers.int['source_part_id'];bmesh.ops.delete(bm,geom=[v for v in bm.verts if v[ids]!=record['part_id']],context='VERTS');bm.to_mesh(ob.data);bm.free();ob.data.update();return ob

def reduce(ob,part,target,tier):
 before=ntris(ob);normal_ref=ob.copy();normal_ref.data=ob.data.copy();normal_ref.name='TEMP_NORMAL_REFERENCE';bpy.context.scene.collection.objects.link(normal_ref);normal_ref.hide_render=True
 bm=bmesh.new();bm.from_mesh(ob.data);bm.verts.ensure_lookup_table();protected=set();boundary={v for e in bm.edges if e.is_boundary for v in e.verts};protected.update(v.index for v in boundary)
 if part in ['body','head']:protected.update(e.other_vert(v).index for v in boundary for e in v.link_edges)
 # A collapse may otherwise join distant atlas corners and produce visible foreign material marks.
 uv=bm.loops.layers.uv['AtlasUV'];seam={v for v in bm.verts if len({tuple(round(x,7) for x in loop[uv].uv) for loop in v.link_loops})>1}
 protected.update(v.index for v in seam)

 if part=='body':
  protected.update(v.index for v in ob.data.vertices if any(ob.vertex_groups[g.group].name.startswith(('Hand.','Thumb','Index','Middle','Ring','Pinky')) and g.weight>.001 for g in v.groups))
  protected.update(v.index for v in bm.verts if v.co.z<.001)
 if part=='head':
  # Eyelid rims, lip closure, nose tip/nostrils and ear silhouettes keep their full source positions.
  protected.update(v.index for v in bm.verts if not ob.data.attributes['source_teeth'].data[v.index].value and (
   (abs(abs(v.co.x)-.029)<.022 and 1.614<v.co.z<1.639 and v.co.y<-.094) or
   (abs(v.co.x)<.042 and 1.526<v.co.z<1.554 and v.co.y<-.115) or
   (abs(v.co.x)<.018 and 1.560<v.co.z<1.627 and v.co.y<-.140) or
   (abs(v.co.x)>.085 and 1.568<v.co.z<1.658)))
 if part=='hair':protected.update(v.index for v in bm.verts if v.co.z<1.646 and v.co.y<-.016)
 protected_points=[tuple(ob.data.vertices[i].co) for i in protected];bm.free()
 group=ob.vertex_groups.new(name='LOD_COLLAPSE_ALLOWED');allow=[v.index for v in ob.data.vertices if v.index not in protected]
 if allow:group.add(allow,1,'REPLACE')
 if target<before:
  mod=ob.modifiers.new('Budget with preserved detail','DECIMATE');mod.decimate_type='COLLAPSE';mod.ratio=target/before;mod.use_collapse_triangulate=True;mod.vertex_group=group.name;mod.vertex_group_factor=1.0
  select(ob);bpy.ops.object.modifier_move_to_index(modifier=mod.name,index=0);bpy.ops.object.modifier_apply(modifier=mod.name)
 ob.vertex_groups.remove(ob.vertex_groups.get('LOD_COLLAPSE_ALLOWED'))
 if target<before:
  normals=ob.modifiers.new('Source surface normals','DATA_TRANSFER');normals.object=normal_ref;normals.use_loop_data=True;normals.data_types_loops={'CUSTOM_NORMAL'};normals.loop_mapping='POLYINTERP_NEAREST'
  select(ob);bpy.ops.object.modifier_move_to_index(modifier=normals.name,index=0);bpy.ops.object.modifier_apply(modifier=normals.name)
 bpy.data.objects.remove(normal_ref,do_unlink=True)
 max_removed=0
 for v in ob.data.vertices:
  ws=sorted([(g.group,g.weight) for g in v.groups if ob.vertex_groups[g.group].name in bones and g.weight>1e-6],key=lambda t:t[1],reverse=True);kept=ws[:4];max_removed=max(max_removed,sum(w for _,w in ws[4:]));total=sum(w for _,w in kept);assert total>0
  for index in [g.group for g in v.groups]:ob.vertex_groups[index].remove([v.index])
  for idx,w in kept:ob.vertex_groups[idx].add([v.index],w/total,'REPLACE')
 tree=kdtree.KDTree(len(ob.data.vertices))
 for v in ob.data.vertices:tree.insert(v.co,v.index)
 tree.balance();delta=max([tree.find(p)[2] for p in protected_points],default=0);assert delta<1e-7,(part,delta)
 assert len(ob.data.uv_layers)==1 and ob.data.uv_layers[0].name=='AtlasUV'
 for i in range(len(shape_names)):assert ob.data.attributes.get(f'facial_delta_{i}'),(part,i)
 return {'part':part,'source_triangles':before,'target_triangles':target,'actual_triangles':ntris(ob),'protected_vertices':len(protected_points),'atlas_seam_vertices':len(seam),'protected_position_delta_m':delta,'max_pruned_weight':max_removed}

result={'source':reference.name,'method':'Partwise QEM with exact hands/eyelids/lips/ears/openings; source normals and atlas reused. Facial deltas interpolated as point attributes during collapse, then restored as five shape keys.','frozen_reference_sha256':immutable,'tiers':{}}
for tier in ['Creator','LOD1','LOD2']:
 parts=[];records=[]
 for record in manifest['source_parts']:
  ob=partcopy(record,tier);parts.append(ob);records.append(reduce(ob,record['part'],budgets[tier]['button' if record['part'].startswith('button') else record['part']],tier));print('PART',tier,records[-1],flush=True)
 bpy.ops.object.select_all(action='DESELECT')
 for ob in parts:ob.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();mesh=bpy.context.object;mesh.name=f'VincentAdult_{tier}Mesh';mesh.data.name=f'VincentAdult_{tier}Geometry';mesh.parent=rig;mesh.matrix_parent_inverse.identity();mesh.matrix_world.identity()
 assert len(mesh.material_slots)==1
 base=mesh.shape_key_add(name='Basis');co=np.array([v.co[:] for v in mesh.data.vertices],dtype=np.float32)
 for i,n in enumerate(shape_names):
  attr=mesh.data.attributes[f'facial_delta_{i}'];delta=np.array([x.vector[:] for x in attr.data],dtype=np.float32);key=mesh.shape_key_add(name=n);key.data.foreach_set('co',(co+delta).ravel());mesh.data.attributes.remove(attr)
 # Keep only useful vertex metadata; source coordinate bake attributes are no longer sampled.
 for a in list(mesh.data.attributes):
  if a.name.startswith('source_') and a.name!='source_part_id':mesh.data.attributes.remove(a)
 mesh.data.calc_loop_triangles();entry={'fbx':f'exports/VincentAdult_{tier}.fbx','mesh':mesh.name,'triangles':ntris(mesh),'vertices':len(mesh.data.vertices),'parts':records,'bone_names':sorted(bones),'height_m':max(v.co.z for v in mesh.data.vertices)-min(v.co.z for v in mesh.data.vertices),'sole_z_m':min(v.co.z for v in mesh.data.vertices),'material':'VincentAdult_Atlas','max_influences':max(len(v.groups) for v in mesh.data.vertices),'facial_morphs':shape_names}
 result['tiers'][tier]=entry
 bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.fbx(filepath=str(ex/f'VincentAdult_{tier}.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=False,path_mode='STRIP')
 entry['sha256']=hashlib.sha256((ex/f'VincentAdult_{tier}.fbx').read_bytes()).hexdigest();mesh.hide_render=True;print('TIER_READY',tier,entry['triangles'],entry['vertices'],flush=True)
# High reference has its own immutable blend; keep only the three runtime tiers in the delivered blend.
bpy.data.objects.remove(source,do_unlink=True);poses.reset(rig);bpy.data.objects['VincentAdult_CreatorMesh'].hide_render=False;bpy.ops.wm.save_as_mainfile(filepath=str(out/'VincentAdult_Tiers.blend'))
for name,h in immutable.items():assert hashlib.sha256((out/name if name.endswith('.blend') else ex/name).read_bytes()).hexdigest()==h,name
(out/'tier-manifest.json').write_text(json.dumps(result,indent=2)+'\n');print('VINCENT_TIERS_DERIVED_REFERENCE_UNCHANGED',flush=True)
