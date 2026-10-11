import bpy,json,sys,math,hashlib,struct
from pathlib import Path
from mathutils import kdtree
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
import argparse
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=root/'rain-adult-production');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
out=args.output.resolve();ex=out/'exports';bpy.ops.wm.open_mainfile(filepath=str(out/'RainAdult_Unity.blend'),use_scripts=False)
mesh=bpy.data.objects['RainAdult_CreatorMesh'];rig=bpy.data.objects['RainAdult_Rig'];bones={b.name for b in rig.data.bones};mesh.data.calc_loop_triangles();tri=len(mesh.data.loop_triangles);expected_height=json.loads((out/'manifest.json').read_text())['geometry']['height_metres']
def points(ob):return [ob.matrix_world@v.co for v in ob.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices]
def pose(name,rig):
 if name=='Tpose':poses.reset(rig)
 elif name=='PhoneGrip':poses.phone_grip(rig)
 else:poses.wave(rig);poses.rot(rig,'Hand.L',(1,0,0),-78)
 bpy.context.view_layer.update()
def compare(a,b):
 tree=kdtree.KDTree(len(a))
 for i,p in enumerate(a):tree.insert(p,i)
 tree.balance();ds=[tree.find(p)[2] for p in b];return {'max_m':max(ds),'rms_m':math.sqrt(sum(d*d for d in ds)/len(ds))}
source={}
for n in ['Tpose','PhoneGrip','OpenWave']:pose(n,rig);source[n]=points(mesh)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(ex/'RainAdult_Creator.fbx'),use_anim=False)
meshes=[o for o in bpy.data.objects if o.type=='MESH'];armatures=[o for o in bpy.data.objects if o.type=='ARMATURE'];assert len(meshes)==1 and len(armatures)==1
mesh=meshes[0];rig=armatures[0];mesh.data.calc_loop_triangles();pose('Tpose',rig);p=points(mesh);bounds=[[min(v[i] for v in p),max(v[i] for v in p)] for i in range(3)]
checks={'one_skinned_mesh':len(meshes)==1 and len([m for m in mesh.modifiers if m.type=='ARMATURE' and m.object==rig])==1,'one_material':len(mesh.material_slots)==1,'55_bones':len(rig.data.bones)==55,'bone_names_match':bones=={b.name for b in rig.data.bones},'triangles_match':len(mesh.data.loop_triangles)==tri,'sole_z_zero':abs(bounds[2][0])<1e-5,'height_matches_manifest':abs(bounds[2][1]-bounds[2][0]-expected_height)<1e-5,'one_atlas_uv':len(mesh.data.uv_layers)==1,'uvs_inside_atlas':all(-1e-5<=x<=1.00001 for u in mesh.data.uv_layers.active.data for x in u.uv),'max_four_influences':all(1<=sum(g.weight>1e-5 for g in v.groups)<=4 for v in mesh.data.vertices),'normalized_weights':all(abs(1-sum(g.weight for g in v.groups))<1e-5 for v in mesh.data.vertices)}
deltas={}
for n in source:pose(n,rig);deltas[n]=compare(source[n],points(mesh));checks[n+'_geometry_within_1e_5m']=deltas[n]['max_m']<1e-5
textures={}
for f in sorted(ex.glob('*.png')):
 raw=f.read_bytes();w,h=struct.unpack('>II',raw[16:24]);textures[f.name]={'width':w,'height':h,'bit_depth':raw[24],'png_color_type':raw[25],'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()};checks[f.stem+'_2048']=w==2048 and h==2048
report={'all_pass':all(checks.values()),'sha256':hashlib.sha256((ex/'RainAdult_Creator.fbx').read_bytes()).hexdigest(),'checks':checks,'posed_geometry_deltas':deltas,'imported_bounds_blender_m':bounds,'fbx_bytes':(ex/'RainAdult_Creator.fbx').stat().st_size,'triangles':tri,'textures':textures,'scope':'Blender fresh FBX import/rest-and-gesture geometry; Unity Humanoid/material/device verification belongs to coordinating agent.'}
(out/'export-validation.json').write_text(json.dumps(report,indent=2));print('UNITY_EXPORT_CHECKS',json.dumps(checks));print('MAX_DELTA',max(d['max_m'] for d in deltas.values()))

assert report['all_pass'],checks
