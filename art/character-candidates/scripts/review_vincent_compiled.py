"""Actual adult male rig/morph proof, never a generated model substitute."""
import bpy,sys,json,math
from pathlib import Path
from mathutils import Vector,Quaternion
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
out=root/'vincent-adult-production';(out/'renders').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_Unity.blend'),use_scripts=False)
scene=bpy.context.scene;rig=bpy.data.objects['VincentAdult_Rig'];head=bpy.data.objects['VincentAdult_CreatorMesh'];parts=[o for o in bpy.data.objects if o.type=='MESH'];height=max(v.co.z for o in parts for v in o.data.vertices)
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=900;scene.render.resolution_y=1100;scene.render.resolution_percentage=100;scene.render.image_settings.media_type='IMAGE';scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False;scene.render.use_simplify=False
world=bpy.data.worlds.new('Adult male proof studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes['Background'].inputs[1].default_value=.45;scene.world=world
centre=Vector((0,0,height*.5))
for label,loc,energy,size,color in [('Key',(3,-4,5),450,4,(1,.9,.8)),('Fill',(-3,-2,2.5),300,3,(.72,.85,1)),('Rim',(1,3,3.5),450,3,(1,.87,.76))]:
 d=bpy.data.lights.new(label,'AREA');d.energy=energy;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(label,d);scene.collection.objects.link(o);o.location=Vector(loc)*height/1.8;o.rotation_euler=(centre-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Adult male proof camera');cam=bpy.data.objects.new('Adult male proof camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
def reset():
 poses.reset(rig)
 for k in head.data.shape_keys.key_blocks:k.value=0
 bpy.context.view_layer.update()
def relaxed():
 reset();poses.rot(rig,'UpperArm.L',(0,1,0),42);poses.rot(rig,'UpperArm.R',(0,1,0),-42)
 poses.rot(rig,'Forearm.L',(0,0,1),-8);poses.rot(rig,'Forearm.R',(0,0,1),8)
def render(name,target,scale,side=False):
 bpy.context.view_layer.update();cam.location=target+Vector((height*3,0,height*.025) if side else (0,-height*3,height*.025));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=scale;scene.render.filepath=str(out/'renders'/f'creator_atlas_{name}.png');bpy.ops.render.render(write_still=True)
selected=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['front','face','side','blink','smile','mouth','deform']
for name in selected:
 relaxed()
 if name in ['front','side']:render(name,centre,height*1.12,name=='side')
 elif name in ['face','blink','smile','brow','mouth']:
  if name=='blink':head.data.shape_keys.key_blocks['BlinkLeft'].value=1;head.data.shape_keys.key_blocks['BlinkRight'].value=1
  if name=='smile':head.data.shape_keys.key_blocks['Smile'].value=1
  if name=='brow':head.data.shape_keys.key_blocks['BrowUp'].value=1
  if name=='mouth':head.data.shape_keys.key_blocks['MouthOpen'].value=1
  render(name,Vector((0,-.025,height-.19)),.52)
 elif name=='hand':
  reset();render(name,rig.data.bones['Index1.L'].head_local+Vector((-.025,0,0)),.30)
 elif name=='deform':
  poses.rot(rig,'UpperArm.L',(0,1,0),-25);poses.rot(rig,'Forearm.L',(0,1,0),-90)
  poses.rot(rig,'Thigh.L',(1,0,0),-30);poses.rot(rig,'Calf.L',(1,0,0),55)
  for s,sign in [('L',1),('R',-1)]:
   for digit in ['Index','Middle','Ring','Pinky']:
    for j,angle in [(1,15),(2,30),(3,20)]:poses.rot(rig,f'{digit}{j}.{s}',(0,1,0),angle*sign)
  render(name,centre,height*1.12)
 print('VINCENT_REVIEW_DONE',name,flush=True)
