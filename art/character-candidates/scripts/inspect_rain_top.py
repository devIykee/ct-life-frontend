import bpy,json
from pathlib import Path
r=Path(__file__).resolve().parents[1];bpy.ops.wm.open_mainfile(filepath=str(r/'raw/rain/Rain v3.3/rain_v3.2.blend'),use_scripts=False)
o=bpy.data.objects['GEO-rain-top'];d={'source_base_polygons':len(o.data.polygons),'source_base_vertices':len(o.data.vertices),'modifiers':[m.type for m in o.modifiers]};(r/'rain-adult-production/top-topology.json').write_text(json.dumps(d,indent=2));print(d)
