import bpy,sys,json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
import argparse
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=root/'unity-snow');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
out=args.output.resolve();(out/'renders').mkdir(exist_ok=True);bpy.ops.wm.open_mainfile(filepath=str(out/'SnowAdult_Unity.blend'),use_scripts=False)
scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig'];scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1000;scene.render.resolution_y=1200;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_depth='8'
world=bpy.data.worlds.new('Unity asset review');world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes.get('Background').inputs[1].default_value=.4;scene.world=world
for name,loc,energy,size,color in [('Key',(3,-4,5),420,4,(1,.88,.76)),('Fill',(-3,-2,2.5),260,3,(.7,.83,1)),('Rim',(1,3,3.5),500,3,(1,.86,.72))]:
 d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Review Camera');cam=bpy.data.objects.new('Review Camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
def render(name,pos,target,scale,pose='relaxed',res=(1000,1200)):
 if pose=='wave':poses.wave(rig);poses.rot(rig,'Hand.L',(1,0,0),-78)
 else:poses.relaxed(rig)
 bpy.context.view_layer.update();cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res;scene.render.filepath=str(out/'renders'/(name+'.png'));bpy.ops.render.render(write_still=True);print('REVIEW_READY',name,flush=True)
render('creator_front',(0,-5,1.6),(0,0,.93),2.08)
render('creator_face_front',(0,-4,1.7),(0,-.03,1.65),.64)
render('creator_three_quarter',(2,-5,1.8),(0,0,.93),2.08)
render('shoulder_sleeve',(1.6,-3,1.8),(.15,-.02,1.37),.61,res=(1000,850))
render('shoes',(1,-2,.8),(0,-.06,.09),.48,res=(1100,750))
render('creator_wave',(.4,-5,1.9),(0,0,1.02),2.30,pose='wave')
print('UNITY_ASSET_REVIEW_RENDERED')
