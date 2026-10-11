import bpy,json,sys,math
from pathlib import Path
root=Path(__file__).resolve().parents[1];out=root/'male-source-audit'
for name in sys.argv[sys.argv.index('--')+1:]:
 p=root/'raw'/name/(name+'.blend')
 bpy.ops.wm.open_mainfile(filepath=str(p),use_scripts=False)
 r={'source':str(p),'blender_file_version':list(bpy.data.version),'scene_frame':bpy.context.scene.frame_current,'objects':[],'rigs':[],'images':[],'actions':[]}
 for o in bpy.data.objects:
  if o.type=='MESH':
   o.data.calc_loop_triangles();r['objects'].append({'name':o.name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'dimensions':list(o.dimensions),'hide_render':o.hide_render,'visible':o.visible_get(),'groups':len(o.vertex_groups),'material_names':[s.material.name if s.material else None for s in o.material_slots],'shape_keys':list(o.data.shape_keys.key_blocks.keys()) if o.data.shape_keys else [],'modifiers':[{'name':m.name,'type':m.type,'visible':m.show_viewport,'render':m.show_render,'levels':getattr(m,'levels',None),'object':getattr(getattr(m,'object',None),'name',None)} for m in o.modifiers]})
  if o.type=='ARMATURE':r['rigs'].append({'name':o.name,'bones':len(o.data.bones),'deform_bones':[b.name for b in o.data.bones if b.use_deform],'pose_position':o.data.pose_position,'action':o.animation_data.action.name if o.animation_data and o.animation_data.action else None})
 for i in bpy.data.images:r['images'].append({'name':i.name,'path':i.filepath,'size':list(i.size),'source':i.source,'packed':bool(i.packed_file or i.packed_files),'path_exists':Path(bpy.path.abspath(i.filepath)).exists() if i.filepath else False})
 for a in bpy.data.actions:r['actions'].append({'name':a.name,'frame_range':list(a.frame_range),'asset':bool(a.asset_data)})
 (out/(name+'-geometry.json')).write_text(json.dumps(r,indent=2)+'\n')
 print('INSPECTED',name,len(r['objects']),[(x['name'],x['bones'],len(x['deform_bones'])) for x in r['rigs']],len(r['images']),len(r['actions']),flush=True)
