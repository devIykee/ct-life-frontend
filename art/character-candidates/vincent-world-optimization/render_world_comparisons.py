"""Actual same-size source/world geometry comparisons, not an in-game capture."""
import bpy,sys,math,json
from pathlib import Path
from mathutils import Vector
out=Path(__file__).resolve().parent;root=out.parent;sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_WorldAtlas.blend'),use_scripts=False);rig=bpy.data.objects['VincentAdult_Rig'];near=bpy.data.objects['VincentAdult_WorldNearMesh'];far=bpy.data.objects['VincentAdult_WorldFarMesh']
with bpy.data.libraries.load(str(root/'vincent-adult-production'/'VincentAdult_Tiers.blend'),link=False) as (a,b):b.objects=['VincentAdult_CreatorMesh','VincentAdult_LOD2Mesh']
high,oldfar=b.objects
for ob in [high,oldfar]:
 bpy.context.scene.collection.objects.link(ob);ob.parent=rig;ob.matrix_parent_inverse.identity();ob.matrix_world.identity()
 for m in ob.modifiers:
  if m.type=='ARMATURE':m.object=rig
sources=[high,oldfar,near,far];labels=['Creator 76,772','Prior far 42,630','Near 16,557 /4w','Far 6,801 /2w']
for o in sources:
 o.hide_render=True
 if o.data.shape_keys:
  for k in o.data.shape_keys.key_blocks:k.value=0
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_percentage=100;scene.render.image_settings.media_type='IMAGE';scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False;scene.render.use_simplify=False
world=bpy.data.worlds.new('World comparison studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes['Background'].inputs[1].default_value=.45;scene.world=world
for name,loc,energy,size,color in [('Key',(6,-12,15),4500,12,(1,.9,.8)),('Fill',(-9,-7,9),2600,10,(.72,.85,1)),('Rim',(5,10,12),4200,10,(1,.87,.76))]:
 d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.9))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Comparison camera');cam=bpy.data.objects.new('Comparison camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
textmat=bpy.data.materials.new('Caption');textmat.use_nodes=True;t=textmat.node_tree;t.nodes.clear();emit=t.nodes.new('ShaderNodeEmission');emit.inputs[0].default_value=(.85,.88,.91,1);output=t.nodes.new('ShaderNodeOutputMaterial');t.links.new(emit.outputs[0],output.inputs['Surface'])
def pose(mode):
 poses.reset(rig)
 for s,sign in [('L',1),('R',-1)]:
  poses.rot(rig,'UpperArm.'+s,(0,1,0),42*sign);poses.rot(rig,'Forearm.'+s,(0,0,1),-8*sign)
 if mode=='stress':
  poses.rot(rig,'UpperArm.L',(0,1,0),-25);poses.rot(rig,'Forearm.L',(0,1,0),-90);poses.rot(rig,'Thigh.L',(1,0,0),-30);poses.rot(rig,'Calf.L',(1,0,0),55)
  for s,sign in [('L',1),('R',-1)]:
   for digit in ['Index','Middle','Ring','Pinky']:
    for j,angle in [(1,15),(2,30),(3,20)]:poses.rot(rig,f'{digit}{j}.{s}',(0,1,0),angle*sign)
 bpy.context.view_layer.update()
def render(name,heightpx,width,height,back=False,mode='relaxed',city=True):
 pose(mode);target=Vector((0,0,.85));direction=Vector((.45,1 if back else -1,.8 if city else .025)).normalized();cam.location=target+direction*10;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();q=cam.rotation_euler.to_quaternion();right=q@Vector((1,0,0));up=q@Vector((0,1,0));forward=q@Vector((0,0,-1));deps=bpy.context.evaluated_depsgraph_get();bounds=[up.dot(high.matrix_world@v.co) for v in high.evaluated_get(deps).data.vertices];span=max(bounds)-min(bounds);d.ortho_scale=span*width/heightpx;verticalcentre=(max(bounds)+min(bounds))/2;camera_centre=up*verticalcentre;cam.location=camera_centre-direction*(-10);cam.rotation_euler=(camera_centre-cam.location).to_track_quat('-Z','Y').to_euler()
 # Keep camera aimed correctly after moving its centre; right/up derive from unchanged direction.
 d.ortho_scale=span*width/heightpx;world_width=d.ortho_scale;vertical_scale=d.ortho_scale*height/width;objects=[]
 for i,(ob,label) in enumerate(zip(sources,labels)):
  geo=bpy.data.meshes.new_from_object(ob.evaluated_get(deps),depsgraph=deps);clone=bpy.data.objects.new(label,geo);scene.collection.objects.link(clone);clone.location=right*((i-1.5)*world_width/4);objects.append(clone)
  font=bpy.data.curves.new(label,'FONT');font.body=label;font.align_x='CENTER';font.size=vertical_scale*12/height;font.extrude=0;txt=bpy.data.objects.new(label+' caption',font);scene.collection.objects.link(txt);txt.location=camera_centre+right*((i-1.5)*world_width/4)-up*vertical_scale*.46-forward*.4;txt.rotation_euler=cam.rotation_euler;txt.data.materials.append(textmat);objects.append(txt)
 scene.render.resolution_x=width;scene.render.resolution_y=height;scene.render.filepath=str(out/'renders'/name);bpy.ops.render.render(write_still=True)
 for ob in objects:bpy.data.objects.remove(ob,do_unlink=True)
 print('WORLD_RENDER_DONE',name,flush=True)
render('city-160px.png',160,1000,220)
render('far-64px.png',64,760,110)
render('distant-32px.png',32,660,76)
render('scrutiny-front.png',490,1800,620,city=False)
render('scrutiny-back.png',490,1800,620,back=True,city=False)
render('stress.png',490,1800,620,mode='stress',city=False)
(out/'render-context.json').write_text(json.dumps({'scope':'Actual Blender geometry/material comparison; not Unity/device capture.','columns':labels,'heights_px':[160,64,32],'camera':'High three-quarter city-like elevation, matched projected creator body height; all columns same orthographic scale. Additional490px scrutiny/stress uses near frontal view.','limitation':'Actual game uses perspective, different light/ASTC/mips; native proof still required.'},indent=2))
