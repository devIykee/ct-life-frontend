import bpy,sys,math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];out=root/'unity-snow/animations';(out/'renders').mkdir(exist_ok=True);bpy.ops.wm.open_mainfile(filepath=str(out/'SnowAdult_RelaxedIdle.blend'),use_scripts=False);scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig']
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_depth='8'
world=bpy.data.worlds.new('Snow idle review');world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes.get('Background').inputs[1].default_value=.4;scene.world=world
for name,loc,energy,size in [('Key',(3,-4,4),480,4),('Fill',(-3,-2,2),260,3),('Rim',(1,3,3),400,3)]:
 d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.size=size;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.9))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Idle camera');cam=bpy.data.objects.new('Idle camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.0001));floor=bpy.context.object;floor.name='REVIEW_FLOOR';m=bpy.data.materials.new('Review floor');m.diffuse_color=(.17,.19,.21,1);floor.data.materials.append(m)
sequence=out/'renders/sequence';sequence.mkdir(exist_ok=True)
cam.location=(.4,-4,1.4);cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=2.12;scene.render.resolution_x=540;scene.render.resolution_y=720
for index in range(72):
 f=1+index*2.5;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update();scene.render.filepath=str(sequence/f'idle_{index:04d}.png');bpy.ops.render.render(write_still=True)
print('IDLE_LOOP_FRAMES_READY',flush=True)
