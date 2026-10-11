import bpy,math,json
from pathlib import Path
from mathutils import Vector,Quaternion
root=Path(__file__).resolve().parents[1];out=root/'snow-adult-proof';bpy.ops.wm.open_mainfile(filepath=str(out/'snow_adult_candidate.blend'),use_scripts=False)
s=bpy.context.scene;r=bpy.data.objects['SnowAdult_Rig'];s.render.resolution_x=900;s.render.resolution_y=900
for n,d in [('UpperArm.L',76),('UpperArm.R',-76)]:
 q=r.data.bones[n].matrix_local.to_quaternion();r.pose.bones[n].rotation_quaternion=q.inverted()@Quaternion((0,1,0),math.radians(d))@q
bpy.context.view_layer.update()
for lod in ['REF','LOD0']:
 for o in bpy.data.objects:
  if o.type=='MESH' and o.name.startswith('Adult_'):o.hide_render=not o.name.startswith('Adult_'+lod+'_');o.hide_viewport=o.hide_render
 ob=bpy.data.objects['Adult_'+lod+'_body'];dg=bpy.context.evaluated_depsgraph_get();ev=ob.evaluated_get(dg)
 idxs={g.index for g in ob.vertex_groups if g.name in ['Hand.L','Fingers.L']}
 ps=[ob.matrix_world@ev.data.vertices[v.index].co for v in ob.data.vertices if sum(g.weight for g in v.groups if g.group in idxs)>.9]
 target=sum(ps,Vector())/len(ps);s.camera.location=target+Vector((.7,-3,.2));s.camera.rotation_euler=(target-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.ortho_scale=.25
 s.render.filepath=str(out/'renders'/('adult_'+lod.lower()+'_hand.png'));bpy.ops.render.render(write_still=True)
