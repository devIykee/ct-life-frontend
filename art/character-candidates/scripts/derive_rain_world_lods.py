"""Derive world LODs from the frozen compiled atlas asset; never write LOD0.
Hard masks preserve hands/facial features and garment openings. No source rebake.
"""
import bpy,bmesh,json,sys,hashlib
from pathlib import Path
from mathutils import kdtree
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
import argparse
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=root/'rain-adult-production');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
out=args.output.resolve();ex=out/'exports';bpy.ops.wm.open_mainfile(filepath=str(out/'RainAdult_Unity.blend'),use_scripts=False)
source=bpy.data.objects['RainAdult_CreatorMesh'];rig=bpy.data.objects['RainAdult_Rig'];poses.reset(rig);bpy.context.view_layer.update();manifest=json.loads((out/'manifest.json').read_text());bones={b.name for b in rig.data.bones}
immutable={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [out/'RainAdult_Unity.blend',ex/'RainAdult_Creator.fbx',*ex.glob('*.png')]}
source.hide_render=True
budgets={1:{'body':11000,'head':9500,'eyes':1000,'eyebrows':350,'eyelashes':472,'hair_main':3000,'hair_ponytail':800,'hair_strand':700,'hairband':500,'top':2800,'jeans':3000,'scarf':2000,'shoes':4500,'gums_lower':800,'gums_upper':800,'tongue':200},2:{'body':10400,'head':8800,'eyes':700,'eyebrows':300,'eyelashes':472,'hair_main':2600,'hair_ponytail':600,'hair_strand':600,'hairband':300,'top':2200,'jeans':2000,'scarf':1500,'shoes':3500,'gums_lower':350,'gums_upper':350,'tongue':150}}
def ntris(o):o.data.calc_loop_triangles();return len(o.data.loop_triangles)
def select(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o

def partcopy(record,lod):
 ob=source.copy();ob.data=source.data.copy();ob.name=f'BUILD_LOD{lod}_{record["part"]}';bpy.context.scene.collection.objects.link(ob);ob.hide_render=False
 bm=bmesh.new();bm.from_mesh(ob.data);ids=bm.verts.layers.int['source_part_id'];bmesh.ops.delete(bm,geom=[v for v in bm.verts if v[ids]!=record['part_id']],context='VERTS');bm.to_mesh(ob.data);bm.free();ob.data.update();return ob

def reduce(ob,part,target,lod):
 if part=='top':
  # Solidify copies original faces first, reversed inner faces next, then opening rims.
  # Keep authored exterior and rim thickness; discard only the hidden inner shell.
  n=json.loads((out/'top-topology.json').read_text())['source_base_polygons']
  bm=bmesh.new();bm.from_mesh(ob.data);bm.faces.ensure_lookup_table();assert len(bm.faces)>2*n
  bmesh.ops.delete(bm,geom=list(bm.faces)[n:2*n],context='FACES')
  bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
  bm.to_mesh(ob.data);bm.free();ob.data.update()
 before=ntris(ob);normal_ref=ob.copy();normal_ref.data=ob.data.copy();normal_ref.name='TEMP_NORMAL_REFERENCE';bpy.context.scene.collection.objects.link(normal_ref);normal_ref.hide_render=True
 bm=bmesh.new();bm.from_mesh(ob.data);bm.verts.ensure_lookup_table()
 protected=set()
 if part=='body':
  protected={v.index for v in bm.verts if abs(v.co.x)>.51 or v.co.z<.24}
  protected.update(v.index for e in bm.edges if e.is_boundary for v in e.verts)
 if part=='head':
  protected={v.index for v in bm.verts if
   (abs(abs(v.co.x)-.038)<.033 and 1.516<v.co.z<1.592 and v.co.y<-.060) or
   (abs(v.co.x)<.025 and 1.503<v.co.z<1.563 and v.co.y<-.100) or
   (abs(v.co.x)<.036 and 1.455<v.co.z<1.505 and v.co.y<-.075) or
   (abs(v.co.x)>.084 and 1.465<v.co.z<1.620) or
   (v.co.y>.018 and 1.485<v.co.z<1.58)}
  boundary={v for e in bm.edges if e.is_boundary for v in e.verts};protected.update(v.index for v in boundary);protected.update(e.other_vert(v).index for v in boundary for e in v.link_edges)
 if part=='shoes':protected.update(v.index for v in bm.verts if v.co.z<1e-5)
 if part=='hair_main':protected.update(v.index for v in bm.verts if v.co.z<1.59)
 if part=='hair_ponytail':protected.update(v.index for v in bm.verts if v.co.y<.105)
 if part in ['top','jeans','scarf','shoes']:
  boundary={v for e in bm.edges if e.is_boundary for v in e.verts}
  protected.update(v.index for v in boundary)
  if part in ['top','scarf']:protected.update(e.other_vert(v).index for v in boundary for e in v.link_edges)
 protected_points=[tuple(ob.data.vertices[i].co) for i in protected]
 bm.free();group=ob.vertex_groups.new(name='LOD_COLLAPSE_ALLOWED');allow=[v.index for v in ob.data.vertices if v.index not in protected]
 if allow:group.add(allow,1,'REPLACE')
 if target<before:
  mod=ob.modifiers.new('Budget with preserved detail','DECIMATE');mod.decimate_type='COLLAPSE';mod.ratio=target/before;mod.use_collapse_triangulate=True;mod.vertex_group=group.name;mod.vertex_group_factor=1.0
  select(ob);bpy.ops.object.modifier_move_to_index(modifier=mod.name,index=0);bpy.ops.object.modifier_apply(modifier=mod.name)
 ob.vertex_groups.remove(ob.vertex_groups.get('LOD_COLLAPSE_ALLOWED'))
 # Restore source surface shading on surviving topology; no new normal bake or UVs.
 if target<before:
  normals=ob.modifiers.new('Source surface normals','DATA_TRANSFER');normals.object=normal_ref;normals.use_loop_data=True;normals.data_types_loops={'CUSTOM_NORMAL'};normals.loop_mapping='POLYINTERP_NEAREST'
  select(ob);bpy.ops.object.modifier_move_to_index(modifier=normals.name,index=0);bpy.ops.object.modifier_apply(modifier=normals.name)
 bpy.data.objects.remove(normal_ref,do_unlink=True)
 if part.startswith('shoes'):
  for v in ob.data.vertices:
   if v.co.z<0:v.co.z=0
 # Reduction interpolates weights; retain at most four and renormalize to export contract.
 max_removed=0
 for v in ob.data.vertices:
  ws=sorted([(g.group,g.weight) for g in v.groups if ob.vertex_groups[g.group].name in bones and g.weight>1e-6],key=lambda t:t[1],reverse=True);kept=ws[:4];max_removed=max(max_removed,sum(w for _,w in ws[4:]));total=sum(w for _,w in kept);assert total>0
  for group_index in [g.group for g in v.groups]:ob.vertex_groups[group_index].remove([v.index])
  for idx,w in kept:ob.vertex_groups[idx].add([v.index],w/total,'REPLACE')
 tree=kdtree.KDTree(len(ob.data.vertices))
 for v in ob.data.vertices:tree.insert(v.co,v.index)
 tree.balance();delta=max([tree.find(p)[2] for p in protected_points],default=0);assert delta<1e-7,(part,delta)
 # Preserve atlas UVs through modifier interpolation; no repacking or image changes.
 assert len(ob.data.uv_layers)==1 and ob.data.uv_layers[0].name=='AtlasUV'
 return {'part':part,'source_triangles':before,'target_triangles':target,'actual_triangles':ntris(ob),'protected_vertices':len(protected_points),'protected_position_delta_m':delta,'max_pruned_weight':max_removed}

result={'source':'RainAdult_Unity.blend','method':'Per-part QEM collapse with zero-collapse hand/face/opening masks; source atlas reused without repacking or rebaking.','frozen_creator_sha256':immutable,'tiers':{}}
for lod in [1,2]:
 parts=[];records=[]
 for record in manifest['source_parts']:
  ob=partcopy(record,lod);parts.append(ob);records.append(reduce(ob,record['part'],budgets[lod][record['part']],lod));print('PART',lod,records[-1],flush=True)
 bpy.ops.object.select_all(action='DESELECT')
 for ob in parts:ob.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();mesh=bpy.context.object;mesh.name=f'RainAdult_LOD{lod}Mesh';mesh.data.name=f'RainAdult_LOD{lod}Geometry';mesh.parent=rig;mesh.matrix_parent_inverse.identity();mesh.matrix_world.identity()
 bm=bmesh.new();bm.from_mesh(mesh.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(mesh.data);bm.free();mesh.data.update()
 # One shared material and one armature modifier, exactly like the frozen creator.
 assert len(mesh.material_slots)==1
 mesh.data.calc_loop_triangles();tier={'fbx':f'exports/RainAdult_LOD{lod}.fbx','mesh':mesh.name,'triangles':ntris(mesh),'vertices':len(mesh.data.vertices),'parts':records,'bone_names':sorted(bones),'height_m':max(v.co.z for v in mesh.data.vertices)-min(v.co.z for v in mesh.data.vertices),'sole_z_m':min(v.co.z for v in mesh.data.vertices),'material':'RainAdult_Atlas','max_influences':max(len(v.groups) for v in mesh.data.vertices)}
 result['tiers'][f'LOD{lod}']=tier
 bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.fbx(filepath=str(ex/f'RainAdult_LOD{lod}.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,path_mode='STRIP')
 mesh.hide_render=True
 print('LOD_READY',lod,tier['triangles'],tier['vertices'],flush=True)
poses.reset(rig);source.hide_render=False;bpy.ops.wm.save_as_mainfile(filepath=str(out/'RainAdult_LODs.blend'))
for name,h in immutable.items():assert hashlib.sha256((out/name if name.endswith('.blend') else ex/name).read_bytes()).hexdigest()==h,name
(out/'lod-manifest.json').write_text(json.dumps(result,indent=2));print('LODS_DERIVED_CREATOR_UNCHANGED',flush=True)
