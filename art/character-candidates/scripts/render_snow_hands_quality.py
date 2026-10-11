import bpy,math,json,sys
from pathlib import Path
from mathutils import Vector,Matrix
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
out=root/'snow-hand-quality';(out/'renders').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(out/'snow_adult_candidate.blend'),use_scripts=False)
# Re-running the proof must not accumulate old lights, cameras or proxy geometry.
for ob in list(bpy.data.objects):
 if ob.type in ['LIGHT','CAMERA'] or ob.name.startswith('PROOF_ONLY_'):bpy.data.objects.remove(ob,do_unlink=True)
scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig'];scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1000;scene.render.resolution_y=1200;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
w=bpy.data.worlds.new('Hand quality studio');w.use_nodes=True;w.node_tree.nodes.get('Background').inputs[0].default_value=(.12,.14,.17,1);w.node_tree.nodes.get('Background').inputs[1].default_value=.4;scene.world=w
for label,loc,energy,size,color in [('Key',(3,-4,5),420,4,(1,.88,.76)),('Fill',(-3,-2,2.5),260,3,(.7,.83,1)),('Rim',(1,3,3.5),500,3,(1,.86,.72))]:
 d=bpy.data.lights.new(label,'AREA');d.energy=energy;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(label,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Hand proof camera');cam=bpy.data.objects.new('Hand proof camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
def mat(name,color,metallic=0,roughness=.4):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;n=m.node_tree.nodes.get('Principled BSDF');n.inputs['Base Color'].default_value=(*color,1);n.inputs['Metallic'].default_value=metallic;n.inputs['Roughness'].default_value=roughness;return m
case=mat('Diagnostic phone / case',(.028,.032,.039),.5,.25);screen=mat('Diagnostic phone / screen',(.055,.19,.23),.2,.22)
phones=[]
def box(name,center,size,material,bevel):
 bpy.ops.mesh.primitive_cube_add(size=1,location=center);ob=bpy.context.object;ob.name=name;ob.scale=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);ob.data.materials.append(material)
 m=ob.modifiers.new('Round phone edges','BEVEL');m.width=bevel;m.segments=3;bpy.context.view_layer.objects.active=ob;bpy.ops.object.modifier_apply(modifier=m.name)
 for p in ob.data.polygons:p.use_smooth=True
 phones.append((ob,ob.matrix_world.copy()));return ob
box('PROOF_ONLY_PhoneCase',poses.PHONE_CENTER,poses.PHONE_SIZE,case,.004)
box('PROOF_ONLY_PhoneScreen',poses.PHONE_CENTER+Vector((0,0,-.0055)),(.062,.137,.001),screen,.002)
# Small speaker marks the top without a brand or imported asset.
box('PROOF_ONLY_PhoneSpeaker',poses.PHONE_CENTER+Vector((0,-.061,-.0062)),(.018,.002,.0004),case,.0007)

def visible(tier,phone=False):
 for o in bpy.data.objects:
  if o.type=='MESH' and o.name.startswith('Adult_'):o.hide_render=not o.name.startswith('Adult_'+tier+'_');o.hide_viewport=o.hide_render
 for o,m in phones:o.hide_render=not phone;o.hide_viewport=not phone;o.matrix_world=poses.phone_matrix(rig)@m

def camera(pos,target,scale):cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale

def render(name,tier='LOD0',pose='relaxed',close=False):
 if pose=='phone':poses.phone_grip(rig,not close)
 elif pose=='wave':
  poses.wave(rig,not close)
  if not close:poses.rot(rig,'Hand.L',(1,0,0),-78)
 else:poses.relaxed(rig)
 bpy.context.view_layer.update();visible(tier,pose=='phone');bpy.context.view_layer.update()
 scene.render.resolution_x=1000;scene.render.resolution_y=1200
 if close and pose=='relaxed':
  ob=bpy.data.objects['Adult_'+tier+'_body'];ev=ob.evaluated_get(bpy.context.evaluated_depsgraph_get());ids={g.index for g in ob.vertex_groups if g.name=='Hand.L' or (g.name.endswith('.L') and any(g.name.startswith(x) for x in poses.DIGITS))}
  ps=[ev.data.vertices[v.index].co for v in ob.data.vertices if sum(g.weight for g in v.groups if g.group in ids)>.9];target=sum(ps,Vector())/len(ps);camera(target+Vector((.7,-3,.2)),target,.25);scene.render.resolution_x=900;scene.render.resolution_y=900
 elif close:
  target=poses.PHONE_CENTER if pose=='phone' else Vector((.824,-.014,1.40));camera(target+Vector((.20,-.12,-.32)),target,.28);scene.render.resolution_x=1100;scene.render.resolution_y=1000
 else:camera((.5,-5,1.7),(0,0,.92),2.12)
 if not close and pose=='phone':camera((-2.7,-4.5,2.1),(0,-.12,.96),2.12)
 if not close and pose=='wave':camera((.4,-5,1.9),(0,0,1.02),2.3)
 scene.render.filepath=str(out/'renders'/(name+'.png'));bpy.ops.render.render(write_still=True)

render('city_hand_relaxed',close=True)
render('creator_hand_relaxed',tier='CREATOR',close=True)
render('city_phone_grip_close',pose='phone',close=True)
render('creator_phone_grip_close',tier='CREATOR',pose='phone',close=True)
render('city_phone_grip',pose='phone')
render('creator_phone_grip',tier='CREATOR',pose='phone')
render('city_open_wave_close',pose='wave',close=True)
render('city_open_wave',pose='wave')
render('creator_relaxed',tier='CREATOR')
render('city_relaxed')
pose_data={}
for n,fn in [('PhoneGrip',poses.phone_grip),('OpenWave',poses.wave)]:
 fn(rig)
 if n=='OpenWave':poses.rot(rig,'Hand.L',(1,0,0),-78)
 pose_data[n]=poses.world_rotations(rig)
(out/'gesture-poses.json').write_text(json.dumps(pose_data,indent=2))
poses.reset(rig);bpy.context.view_layer.update();visible('LOD0');bpy.ops.wm.save_as_mainfile(filepath=str(out/'snow_adult_candidate.blend'))
for tier in ['LOD0','CREATOR']:
 visible(tier);bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
 for ob in bpy.data.objects:
  if ob.type=='MESH' and ob.name.startswith('Adult_'+tier+'_'):ob.select_set(True)
 bpy.ops.export_scene.fbx(filepath=str(out/('snow_'+tier.lower()+'_hands_candidate.fbx')),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True)
# Open the editable blend in a reviewable creator phone-grip pose. FBXs above remain rest-pose exports.
poses.phone_grip(rig);bpy.context.view_layer.update();visible('CREATOR',True);camera((-2.7,-4.5,2.1),(0,-.12,.96),2.12)
rig['review_note']='Creator phone-grip review pose; FBX files export rest geometry. Reset pose transforms for T pose.'
bpy.ops.wm.save_as_mainfile(filepath=str(out/'snow_adult_candidate.blend'))
print('HAND_QUALITY_RENDERED_AND_EXPORTED')
