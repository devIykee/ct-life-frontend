import bpy,sys,math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];out=root/'rain-adult-production/animations';(out/'renders').mkdir(exist_ok=True);bpy.ops.wm.open_mainfile(filepath=str(out/'RainAdult_RelaxedIdle.blend'),use_scripts=False);scene=bpy.context.scene;rig=bpy.data.objects['RainAdult_Rig']
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_depth='8'
world=bpy.data.worlds.new('Rain idle review');world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes.get('Background').inputs[1].default_value=.4;scene.world=world
for name,loc,energy,size in [('Key',(3,-4,4),480,4),('Fill',(-3,-2,2),260,3),('Rim',(1,3,3),400,3)]:
 d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.size=size;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.9))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Idle camera');cam=bpy.data.objects.new('Idle camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.0001));floor=bpy.context.object;floor.name='REVIEW_FLOOR';m=bpy.data.materials.new('Review floor');m.diffuse_color=(.17,.19,.21,1);floor.data.materials.append(m)
views=[('front',(0,-4,1.4),(0,0,.86),1.94,(900,1200)),('side',(4,-.3,1.3),(0,0,.86),1.94,(900,1200)),('three_quarter',(2,-4,1.5),(0,0,.86),1.94,(900,1200)),('back',(0,4,1.3),(0,0,.86),1.94,(900,1200)),('left_hand',(.9,-2,.95),(.28,-.03,.79),.43,(1100,1100)),('hand_side',(2,0,.85),(.28,-.03,.79),.43,(1100,1100)),('feet',(1,-3,.45),(0,-.04,.13),.66,(1100,800))]
scene.frame_set(1)
for name,pos,target,scale,res in views:
 cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res;scene.render.filepath=str(out/'renders'/f'{name}.png');bpy.context.view_layer.update();bpy.ops.render.render(write_still=True);print('IDLE_RENDER_READY',name,flush=True)
for frame in [1,91,181]:
 scene.frame_set(frame);cam.location=(0,-4,1.4);cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=2.12;scene.render.resolution_x=650;scene.render.resolution_y=900;scene.render.filepath=str(out/'renders'/f'loop_{frame:03d}.png');bpy.context.view_layer.update();bpy.ops.render.render(write_still=True)
print('IDLE_REVIEW_COMPLETE',flush=True)
