import bpy,gzip,sys,math
from pathlib import Path
from mathutils import Vector
out=Path(__file__).resolve().parent;root=out.parent
native='--native' in sys.argv
if native:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 vs=[];uv=[];faces=[]
 with gzip.open(out/'native-shared/creator-VincentAdult_CreatorMesh-phase00.obj.gz','rt') as f:
  for l in f:
   a=l.split()
   if a[0]=='v':vs.append((float(a[1]),-float(a[3]),float(a[2])))
   elif a[0]=='vt':uv.append((float(a[1]),float(a[2])))
   elif a[0]=='f':faces.append(tuple(int(x.split('/')[0])-1 for x in reversed(a[1:])))
 me=bpy.data.meshes.new('Actual native shared idle');me.from_pydata(vs,[],faces);me.update();layer=me.uv_layers.new()
 for l in me.loops:layer.data[l.index].uv=uv[l.vertex_index]
 for p in me.polygons:p.use_smooth=True
 body=bpy.data.objects.new('Actual native shared idle',me);bpy.context.scene.collection.objects.link(body)
 mat=bpy.data.materials.new('Native atlas');mat.use_nodes=True;p=mat.node_tree.nodes['Principled BSDF'];p.inputs['Roughness'].default_value=.65
 tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(root/'vincent-adult-production/exports/VincentAdult_BaseColor.png'));mat.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color']);body.data.materials.append(mat)
else:
 bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_RelaxedIdle.blend'),use_scripts=False);bpy.context.scene.frame_set(1)
 body=bpy.data.objects['VincentAdult_CreatorMesh']
 for o in bpy.data.objects:
  if o.type=='MESH':o.hide_render=o!=body
s=bpy.context.scene;s.render.engine='BLENDER_EEVEE';s.render.resolution_x=1000;s.render.resolution_y=1300;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';s.render.film_transparent=False
s.world=bpy.data.worlds.new('Idle proof studio');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.14,.17,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.6
for name,loc,power,size in [('Key',(3,-4,5),450,4),('Fill',(-3,-2,2.5),300,3),('Rim',(1,3,3.5),450,3)]:
 d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;o=bpy.data.objects.new(name,d);s.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Proof camera');cam=bpy.data.objects.new('Proof camera',d);s.collection.objects.link(cam);s.camera=cam;d.type='ORTHO'
(out/'renders').mkdir(exist_ok=True)
for kind,target,scale,location in [('front',(0,0,.95),2.05,(0,-6,.98)),('hands',(0,-.015,.89),.68,(0,-5,.94)),('side',(0,0,.89),.75,(5,-.2,.98))]:
 cam.location=location;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=scale
 s.render.filepath=str(out/'renders'/f'{"native-shared" if native else "source-candidate"}-{kind}.png');bpy.ops.render.render(write_still=True)
