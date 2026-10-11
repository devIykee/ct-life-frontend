import bpy,math,json
from pathlib import Path
from mathutils import Vector,Quaternion
root=Path(__file__).resolve().parents[1];out=root/'snow-adult-proof';(out/'renders').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(out/'snow_adult_candidate.blend'),use_scripts=False)
scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig']
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1000;scene.render.resolution_y=1200;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
world=bpy.data.worlds.new('Candidate studio');world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes.get('Background').inputs[1].default_value=.4;scene.world=world
for label,loc,energy,size,color in [('Key',(3,-4,5),420,4,(1,.88,.76)),('Fill',(-3,-2,2.5),260,3,(.7,.83,1)),('Rim',(1,3,3.5),500,3,(1,.86,.72))]:
 d=bpy.data.lights.new(label,'AREA');d.energy=energy;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(label,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
camd=bpy.data.cameras.new('Proof Camera');cam=bpy.data.objects.new('Proof Camera',camd);scene.collection.objects.link(cam);scene.camera=cam;camd.type='ORTHO'
def visible(lod):
 for o in bpy.data.objects:
  if o.type=='MESH' and o.name.startswith('Adult_'):
   active=o.name.startswith('Adult_'+lod+'_');o.hide_render=not active;o.hide_viewport=not active

def reset():
 rig.location=(0,0,0)
 for p in rig.pose.bones:p.rotation_mode='QUATERNION';p.rotation_quaternion=Quaternion();p.location=(0,0,0);p.scale=(1,1,1)

def rot(n,axis,deg):
 b=rig.data.bones[n];q=b.matrix_local.to_quaternion();rig.pose.bones[n].rotation_quaternion=q.inverted()@Quaternion(Vector(axis),math.radians(deg))@q

def pose(name):
 reset()
 if name!='tpose':
  rot('UpperArm.L',(0,1,0),76);rot('UpperArm.R',(0,1,0),-76)
  rot('Forearm.L',(0,0,1),-10);rot('Forearm.R',(0,0,1),10)
 if name=='arm_raise':
  rot('UpperArm.L',(0,1,0),-55);rot('Forearm.L',(0,0,1),-25)
 if name=='walk_pose':
  rot('Thigh.L',(1,0,0),-28);rot('Calf.L',(1,0,0),18);rot('Thigh.R',(1,0,0),20);rot('Calf.R',(1,0,0),28)
  rot('Foot.L',(1,0,0),10);rot('Forearm.L',(0,0,1),-22);rot('Forearm.R',(0,0,1),32)
 if name=='sit_pose':
  rot('Thigh.L',(1,0,0),-88);rot('Calf.L',(1,0,0),90);rot('Thigh.R',(1,0,0),-88);rot('Calf.R',(1,0,0),90)
  rot('Forearm.L',(0,0,1),-65);rot('Forearm.R',(0,0,1),65);rig.location.z=-.43
 bpy.context.view_layer.update()

def render(name,face=False,lod='LOD0',p='neutral'):
 visible(lod);pose(p)
 if face:
  cam.location=(.3,-4,1.7);target=Vector((0,-.03,1.65));camd.ortho_scale=.66
 else:
  cam.location=(.5,-5,1.7);target=Vector((0,0,.92));camd.ortho_scale=2.12
 if p=='sit_pose':cam.location=(2.7,-4.5,2.1);target=Vector((0,-.1,.7));camd.ortho_scale=1.85
 if p in ['walk_pose','arm_raise']:cam.location=(2,-5,2.1);target=Vector((0,0,.97));camd.ortho_scale=2.12
 cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(out/'renders'/(name+'.png'));bpy.ops.render.render(write_still=True)
render('adult_high_reference',lod='REF',p='tpose')
render('adult_high_relaxed',lod='REF')
render('adult_high_face',lod='REF',face=True)
render('adult_lod0_tpose',p='tpose')
render('adult_lod0_relaxed')
render('adult_lod0_face',face=True)
render('adult_lod0_arm_raise',p='arm_raise')
render('adult_lod0_walk_pose',p='walk_pose')
render('adult_lod0_sit_pose',p='sit_pose')
render('adult_lod1_relaxed',lod='LOD1')
render('adult_lod2_relaxed',lod='LOD2')
reset();visible('LOD0');bpy.ops.wm.save_as_mainfile(filepath=str(out/'snow_adult_candidate.blend'))
# Geometry/weights/morphs candidate FBX; source shaders are not a baked game material.
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
for o in bpy.data.objects:
 if o.type=='MESH' and o.name.startswith('Adult_LOD0_'):o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(out/'snow_adult_lod0_candidate.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True)
print('SNOW_PROOF_RENDERED_AND_EXPORTED')
