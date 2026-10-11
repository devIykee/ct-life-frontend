import bpy,math,sys,json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
out=root/'rain-adult-production';(out/'renders_lods').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(out/'RainAdult_LODs.blend'),use_scripts=False)
scene=bpy.context.scene;rig=bpy.data.objects['RainAdult_Rig'];poses.relaxed(rig)
for side,sign in [('L',1),('R',-1)]:
 for digit,angles in {'Index':(8,12,8),'Middle':(10,15,10),'Ring':(12,17,12),'Pinky':(14,19,14)}.items():
  for j,a in enumerate(angles,1):poses.rot(rig,f'{digit}{j}.{side}',(0,1,0),sign*a)
 for j,a in [(1,5),(2,8),(3,8)]:poses.rot(rig,f'Thumb{j}.{side}',(0,1,0),sign*a)
bpy.context.view_layer.update()
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=900;scene.render.resolution_y=1100;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
world=bpy.data.worlds.new('Rain review studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes['Background'].inputs[1].default_value=.4;scene.world=world
for name,loc,energy,size,color in [('Key',(3,-4,5),420,4,(1,.88,.76)),('Fill',(-3,-2,2.5),260,3,(.7,.83,1)),('Rim',(1,3,3.5),500,3,(1,.86,.72))]:
 d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Rain review camera');cam=bpy.data.objects.new('Rain review camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
views={'front':((0,-5,1.1),(0,0,.84),1.95),'back':((0,5,1.1),(0,0,.84),1.95),'side':((5,0,1.1),(0,0,.84),1.95),'face':((.25,-4,1.65),(0,-.03,1.51),.59),'hand':((1,-2,.91),(.23,-.10,.81),.31)}
meshes=[bpy.data.objects['RainAdult_CreatorMesh'],bpy.data.objects['RainAdult_LOD1Mesh'],bpy.data.objects['RainAdult_LOD2Mesh']]
for level,mesh in enumerate(meshes):
 for ob in meshes:ob.hide_render=ob!=mesh
 for name in ['front','back','face','side','hand']:
  loc,target,scale=views[name];cam.location=loc;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=scale;scene.render.filepath=str(out/'renders_lods'/f'lod{level}_{name}.png');bpy.ops.render.render(write_still=True)
poses.reset(rig);print('RAIN_LODS_REVIEW_DONE')
