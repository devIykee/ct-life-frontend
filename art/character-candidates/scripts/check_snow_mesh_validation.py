import bpy
from pathlib import Path
p=Path(__file__).resolve().parents[1];bpy.ops.wm.open_mainfile(filepath=str(p/'snow-adult-proof/snow_adult_candidate.blend'),use_scripts=False)
for o in bpy.data.objects:
 if o.name.startswith('Adult_LOD0_'):
  print('MESH',o.name,'BEFORE',len(o.data.polygons));print('VALIDATED',o.data.validate(verbose=True,clean_customdata=False));print('AFTER',len(o.data.polygons))
