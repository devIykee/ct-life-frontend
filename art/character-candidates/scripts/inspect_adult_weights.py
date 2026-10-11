import bpy,json
from pathlib import Path
from collections import defaultdict
root=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root/'snow-adult-proof/snow_adult_candidate.blend'),use_scripts=False)
for name in ['Adult_REF_pants','Adult_REF_shirt','Adult_LOD0_pants','Adult_LOD0_shirt','Adult_REF_body']:
 o=bpy.data.objects[name];groups=defaultdict(lambda:[0,100,-100])
 for v in o.data.vertices:
  for g in v.groups:
   if g.weight>.01:
    n=o.vertex_groups[g.group].name;d=groups[n];d[0]+=1;d[1]=min(d[1],v.co.z);d[2]=max(d[2],v.co.z)
 print(name,dict(groups))
