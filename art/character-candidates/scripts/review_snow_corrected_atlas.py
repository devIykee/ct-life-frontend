"""Whole-look atlas review: front/back/both sides plus calf deformation, all real meshes."""
import bpy,sys
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
out=root/'unity-snow/calf-quality-candidate';renders=out/'renders';renders.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(out/'SnowAdult_LODs.blend'),use_scripts=False);scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig'];poses.relaxed(rig)
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_depth='8'
world=bpy.data.worlds.new('Complete atlas review');world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes.get('Background').inputs[1].default_value=.4;scene.world=world
for name,loc,energy,size in [('Key',(3,4,4),500,4),('Fill',(-3,2,2),300,3),('Rim',(1,-3,3),400,3)]:
 d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.size=size;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.8))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Atlas camera');cam=bpy.data.objects.new('Atlas camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
views=[('front',(0,-4,1.2),(0,0,.93),2.0,(700,1100)),('back',(0,4,1.2),(0,0,.93),2.0,(700,1100)),('left',(4,0,1.2),(0,0,.93),2.0,(700,1100)),('right',(-4,0,1.2),(0,0,.93),2.0,(700,1100)),('calves',(0,4,.45),(0,.02,.30),.65,(1000,900)),('side',(.9,2,.45),(.08,.02,.30),.65,(1000,900)),('face',(0,-4,1.7),(0,-.03,1.65),.64,(800,900))]
meshes=[bpy.data.objects[n] for n in ['SnowAdult_CreatorMesh','SnowAdult_LOD1Mesh','SnowAdult_LOD2Mesh']]
for i,mesh in enumerate(meshes):
 for ob in meshes:ob.hide_render=ob!=mesh
 poses.relaxed(rig)
 for view,pos,target,scale,res in views:
  if i and view not in ['back','side','calves']:continue
  cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res;scene.render.filepath=str(renders/f'lod{i}_{view}.png');bpy.context.view_layer.update();bpy.ops.render.render(write_still=True);print('ATLAS_REVIEW_READY',i,view,flush=True)
 poses.relaxed(rig);poses.rot(rig,'Thigh.L',(1,0,0),-35);poses.rot(rig,'Calf.L',(1,0,0),65)
 cam.location=(1.3,4,1.0);cam.rotation_euler=(Vector((.03,.04,.44))-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=1.05;scene.render.resolution_x=1000;scene.render.resolution_y=1100;scene.render.filepath=str(renders/f'lod{i}_bent_knee.png');bpy.context.view_layer.update();bpy.ops.render.render(write_still=True)
print('CORRECTED_ATLAS_COMPLETE_REVIEW_READY',flush=True)
