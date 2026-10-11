import bpy,sys
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
p=root/'snow-hand-quality/snow_adult_candidate.blend';bpy.ops.wm.open_mainfile(filepath=str(p),use_scripts=False)
r=bpy.data.objects['SnowAdult_Rig'];poses.phone_grip(r);bpy.context.view_layer.update()
for ob in bpy.data.objects:
 if ob.type=='MESH' and ob.name.startswith('Adult_'):ob.hide_render=not ob.name.startswith('Adult_CREATOR_');ob.hide_viewport=ob.hide_render
 if ob.name.startswith('PROOF_ONLY_'):ob.hide_render=False;ob.hide_viewport=False;ob.matrix_world=poses.phone_matrix(r)@ob.matrix_world
cam=bpy.context.scene.camera;cam.location=(-2.7,-4.5,2.1);cam.rotation_euler=(Vector((0,-.12,.96))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=2.12
r['review_note']='Creator phone-grip review pose; FBX files export rest geometry. Reset pose transforms for T pose.'
bpy.ops.wm.save_as_mainfile(filepath=str(p))
