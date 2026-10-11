import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(next((root/'raw/snow').rglob('*.blend'))),use_scripts=False)
r=bpy.data.objects['RIG-Snow']
out={'matrix':list(map(list,r.matrix_world)),'bones':[],'meshes':[]}
for b in r.data.bones:
 if b.use_deform:
  out['bones'].append({'name':b.name,'parent':b.parent.name if b.parent else None,'head':list(r.matrix_world@b.head_local),'tail':list(r.matrix_world@b.tail_local)})
for o in bpy.data.objects:
 if o.type=='MESH' and o.name.startswith('GEO-snow'):
  out['meshes'].append({'name':o.name,'hide_render':o.hide_render,'matrix':list(map(list,o.matrix_world)),'modifiers':[(m.name,m.type) for m in o.modifiers],'shape_keys':list(o.data.shape_keys.key_blocks.keys()) if o.data.shape_keys else []})
(root/'snow-rig-map.json').write_text(json.dumps(out,indent=2))
for b in out['bones']:
 if not any(x in b['name'].lower() for x in ['lid','brow','lip','cheek','forehead','nose','temple','ear','squint','chin','teeth','tongue']):print(b)
