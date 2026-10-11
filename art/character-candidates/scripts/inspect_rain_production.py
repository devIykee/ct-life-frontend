import bpy,json
from pathlib import Path
r=Path(__file__).resolve().parents[1];out=r/'rain-adult-production';out.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(r/'raw/rain/Rain v3.3/rain_v3.2.blend'),use_scripts=False)
a=bpy.data.objects['RIG-rain']
d={'bones':{b.name:{'head':list(a.matrix_world@b.head_local),'tail':list(a.matrix_world@b.tail_local),'parent':b.parent.name if b.parent else None} for b in a.data.bones if b.use_deform},'mesh_bounds':{},'source_actions':[x.name for x in bpy.data.actions]}
for o in bpy.data.objects:
 if o.type=='MESH' and o.name.startswith('GEO-rain'):
  pts=[o.matrix_world@v.co for v in o.data.vertices];d['mesh_bounds'][o.name]={'min':[min(p[i] for p in pts) for i in range(3)],'max':[max(p[i] for p in pts) for i in range(3)],'hidden':o.hide_render,'modifiers':[(m.type,m.show_viewport,m.show_render) for m in o.modifiers]}
(out/'source-inspection.json').write_text(json.dumps(d,indent=2))
print('RAIN_INSPECT_DONE')
