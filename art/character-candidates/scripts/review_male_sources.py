import bpy,sys,json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];out=root/'male-source-audit';(out/'renders').mkdir(exist_ok=True)
for name in sys.argv[sys.argv.index('--')+1:]:
 bpy.ops.wm.open_mainfile(filepath=str(root/'raw'/name/(name+'.blend')),use_scripts=False)
 scene=bpy.context.scene
 if name=='phil':
  included=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('GEO-phil_') and o.visible_get() and not any(s in o.name for s in ['helper','square','distressed','scalp','groom'])]
 elif name=='rex':
  included=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('GEO-rex_') and o.visible_get() and not any(s in o.name for s in ['helper','sharp','distressed','scalp','groom'])]
 else:included=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('GEO-vincent_') and o.visible_get() and 'UV_copy' not in o.name]
 for o in bpy.data.objects:
  if o.type in ['MESH','CURVES','CURVE','SURFACE','META','VOLUME']:o.hide_render=o not in included
  if o.type=='LIGHT':o.hide_render=True
 for o in included:
  if o.animation_data:
   for d in o.animation_data.drivers:
    if 'modifiers[' in d.data_path:d.mute=True
  for m in o.modifiers:
   if m.type=='SUBSURF':m.levels=1;m.render_levels=1;m.show_viewport=True;m.show_render=True
   if m.type=='PARTICLE_SYSTEM':m.show_render=False;m.show_viewport=False
 # Render the supplied pose/rest geometry; no library code or add-on is enabled.
 bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get()
 points=[];counts=[]
 for o in included:
  ev=o.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles();counts.append({'name':o.name,'triangles':len(me.loop_triangles),'vertices':len(me.vertices)})
  points.extend(o.matrix_world@v.co for v in me.vertices);ev.to_mesh_clear()
 lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)]);height=hi.z-lo.z;centre=(lo+hi)*.5
 scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=900;scene.render.resolution_y=1100;scene.render.resolution_percentage=100;scene.render.image_settings.media_type='IMAGE';scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
 scene.render.use_simplify=False
 world=bpy.data.worlds.new('Male source review studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.14,.17,1);world.node_tree.nodes['Background'].inputs[1].default_value=.45;scene.world=world
 for label,loc,energy,size,color in [('Key',(3,-4,5),450,4,(1,.9,.8)),('Fill',(-3,-2,2.5),300,3,(.72,.85,1)),('Rim',(1,3,3.5),450,3,(1,.87,.76))]:
  d=bpy.data.lights.new(label,'AREA');d.energy=energy;d.shape='DISK';d.size=size;d.color=color;o=bpy.data.objects.new(label,d);scene.collection.objects.link(o);o.location=Vector(loc)*height/1.8;o.rotation_euler=(centre-o.location).to_track_quat('-Z','Y').to_euler()
 d=bpy.data.cameras.new('Source review camera');cam=bpy.data.objects.new('Source review camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO'
 for view,target,scale in [('front',centre,height*1.12),('face',Vector((centre.x,centre.y,hi.z-height*.135)),height*.36),('side',centre,height*1.12)]:
  cam.location=target+Vector((height*3,0,height*.05) if view=='side' else (0,-height*3,height*.04));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=scale;scene.render.filepath=str(out/'renders'/f'{name}_{view}.png');bpy.ops.render.render(write_still=True)
 (out/(name+'-render-report.json')).write_text(json.dumps({'source':name,'scope':'Actual source geometry and materials, embedded scripts disabled; one subdivision level for visual comparison only. Not a mobile export.','bounds':[list(lo),list(hi)],'height':height,'triangles':sum(x['triangles'] for x in counts),'parts':counts},indent=2)+'\n')
 print('MALE_REVIEW_DONE',name,flush=True)
