import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
for variant,path in [('compiled',root/'unity-snow/SnowAdult_Unity.blend'),('source',root/'snow-hand-quality/snow_adult_candidate.blend')]:
 bpy.ops.wm.open_mainfile(filepath=str(path),use_scripts=False)
 for o in bpy.data.objects:
  if o.type!='MESH' or not(o.name=='SnowAdult_CreatorMesh' or o.name.startswith('Adult_REF_')):continue
  m=o.data
  print(variant,o.name,'hidden faces',sum(p.hide for p in m.polygons),'vertices',sum(v.hide for v in m.vertices),'UV layers',[(x.name,x.active_render) for x in m.uv_layers],flush=True)
  if 'source_part_id' in m.attributes:
   ids=m.attributes['source_part_id'].data;print('HIDDEN_PARTS',{part:sum(p.hide and ids[p.vertices[0]].value==part for p in m.polygons) for part in range(10)})
