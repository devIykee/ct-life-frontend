import bpy,json,sys,math,hashlib
from pathlib import Path
from mathutils import kdtree
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
import argparse
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=root/'unity-snow');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
out=args.output.resolve();ex=out/'exports'
def pose(name,rig):
 if name=='Tpose':poses.reset(rig)
 elif name=='PhoneGrip':poses.phone_grip(rig)
 elif name=='OpenWave':poses.wave(rig);poses.rot(rig,'Hand.L',(1,0,0),-78)
 elif name=='Seated':
  poses.relaxed(rig)
  for side in ['L','R']:poses.rot(rig,'Thigh.'+side,(1,0,0),-78);poses.rot(rig,'Calf.'+side,(1,0,0),83)
 bpy.context.view_layer.update()
def points(ob):return [ob.matrix_world@v.co for v in ob.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices]
def compare(a,b):
 tree=kdtree.KDTree(len(a))
 for i,p in enumerate(a):tree.insert(p,i)
 tree.balance();ds=[tree.find(p)[2] for p in b];return {'max_m':max(ds),'rms_m':math.sqrt(sum(d*d for d in ds)/len(ds))}
def skeleton(rig):return {b.name:{'parent':b.parent.name if b.parent else None,'matrix':[x for row in b.matrix_local for x in row],'head':list(b.head_local),'tail':list(b.tail_local)} for b in rig.data.bones}
def nearest_set(a,b):return compare(b,a)['max_m']
# Compare exported skeletons in the same fresh-FBX coordinate convention.
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(ex/'SnowAdult_Creator.fbx'),use_anim=False);creator_bones=skeleton(next(o for o in bpy.data.objects if o.type=='ARMATURE'))
report={'tiers':{},'scope':'Fresh Blender FBX import; Unity LODGroup, Humanoid retarget and device rendering owned by coordinating agent.'}
for level in [1,2]:
 bpy.ops.wm.open_mainfile(filepath=str(out/'SnowAdult_LODs.blend'),use_scripts=False);mesh=bpy.data.objects[f'SnowAdult_LOD{level}Mesh'];ref=bpy.data.objects['SnowAdult_CreatorMesh'];rig=bpy.data.objects['SnowAdult_Rig'];mesh.data.calc_loop_triangles();tris=len(mesh.data.loop_triangles);source={}
 # Verify hand positions, weights and UV samples from exact frozen creator surface.
 def samples(o):
  ids=o.data.attributes['source_part_id'].data;uv=o.data.uv_layers['AtlasUV'].data;result={}
  for loop in o.data.loops:
   v=o.data.vertices[loop.vertex_index]
   if ids[v.index].value!=0 or abs(v.co.x)<=.70:continue
   key=tuple(round(x,6) for x in v.co);result.setdefault(key,set()).add(tuple(round(x,6) for x in uv[loop.index].uv))
  return result
 a=samples(ref);b=samples(mesh);hand_same=all(k in b and vals.issubset(b[k]) for k,vals in a.items())
 for name in ['Tpose','PhoneGrip','OpenWave','Seated']:pose(name,rig);source[name]=points(mesh)
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(ex/f'SnowAdult_LOD{level}.fbx'),use_anim=False);meshes=[o for o in bpy.data.objects if o.type=='MESH'];rigs=[o for o in bpy.data.objects if o.type=='ARMATURE'];assert len(meshes)==1 and len(rigs)==1
 mesh=meshes[0];rig=rigs[0];mesh.data.calc_loop_triangles();pose('Tpose',rig);p=points(mesh);bounds=[[min(v[i] for v in p),max(v[i] for v in p)] for i in range(3)];bones=skeleton(rig);same_names=bones.keys()==creator_bones.keys();delta=max(abs(a-b) for n in bones for a,b in zip(bones[n]['matrix'],creator_bones[n]['matrix']))
 checks={'one_skinned_mesh':len(meshes)==1 and len([m for m in mesh.modifiers if m.type=='ARMATURE' and m.object==rig])==1,'one_material':len(mesh.material_slots)==1,'55_bones':len(bones)==55,'same_bone_names_as_creator':same_names,'same_bone_hierarchy_as_creator':all(bones[n]['parent']==creator_bones[n]['parent'] for n in bones),'same_rest_matrices_as_creator':delta<1e-7,'triangles_match':len(mesh.data.loop_triangles)==tris,'sole_z_zero':abs(bounds[2][0])<1e-5,'one_atlas_uv':len(mesh.data.uv_layers)==1,'uvs_inside_atlas':all(-1e-5<=x<=1.00001 for u in mesh.data.uv_layers.active.data for x in u.uv),'max_four_influences':all(1<=sum(g.weight>1e-5 for g in v.groups)<=4 for v in mesh.data.vertices),'normalized_weights':all(abs(1-sum(g.weight for g in v.groups))<1e-5 for v in mesh.data.vertices),'detailed_hand_positions_and_atlas_uv_samples_unchanged':hand_same}
 deltas={}
 for name in source:pose(name,rig);deltas[name]=compare(source[name],points(mesh));checks[name+'_geometry_within_1e_5m']=deltas[name]['max_m']<1e-5
 report['tiers'][f'LOD{level}']={'checks':checks,'triangles':tris,'fbx_bytes':(ex/f'SnowAdult_LOD{level}.fbx').stat().st_size,'rest_matrix_max_delta':delta,'posed_geometry_deltas':deltas,'imported_bounds_blender_m':bounds,'protected_hand_position_samples':len(a)};print('VALIDATION',level,json.dumps(checks),flush=True)
report['all_pass']=all(all(t['checks'].values()) for t in report['tiers'].values());(out/'lod-export-validation.json').write_text(json.dumps(report,indent=2));assert report['all_pass'];print('WORLD_LOD_ALL_CHECKS_PASS',flush=True)
