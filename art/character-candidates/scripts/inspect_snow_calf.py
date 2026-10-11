"""Read existing sources, render comparison only; never overwrite delivered models/maps."""
import bpy,sys
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
out=root/'unity-snow/calf-quality-candidate';(out/'inspection').mkdir(parents=True,exist_ok=True)
for variant,path in [('compiled',root/'unity-snow/SnowAdult_Unity.blend'),('retained_source',root/'snow-hand-quality/snow_adult_candidate.blend')]:
 bpy.ops.wm.open_mainfile(filepath=str(path),use_scripts=False);scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig'];poses.relaxed(rig)
 for ob in bpy.data.objects:
  if ob.type=='MESH':
   visible=ob.name=='SnowAdult_CreatorMesh' if variant=='compiled' else ob.name.startswith('Adult_REF_')
   ob.hide_render=not visible;ob.hide_viewport=False;ob.hide_set(False)
 scene.render.engine='BLENDER_EEVEE';scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_depth='8'
 world=bpy.data.worlds.new('Calf inspection');world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes.get('Background').inputs[1].default_value=.4;scene.world=world
 for name,loc,energy,size in [('Key',(3,4,4),500,4),('Fill',(-3,2,2),300,3),('Rim',(1,-3,3),400,3)]:
  d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.size=size;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.8))-o.location).to_track_quat('-Z','Y').to_euler()
 d=bpy.data.cameras.new('Calf camera');cam=bpy.data.objects.new('Calf camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
 for view,pos,target,scale,res in [('back',(0,4,1.2),(0,0,.93),2.0,(700,1100)),('calves',(0,4,.45),(0,.02,.30),.65,(1000,900)),('side',(.9,2,.45),(.08,.02,.30),.65,(1000,900))]:
  cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res;scene.render.filepath=str(out/'inspection'/f'{variant}_{view}.png');bpy.context.view_layer.update();bpy.ops.render.render(write_still=True);print('CALF_VIEW_READY',variant,view,flush=True)
