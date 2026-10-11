import bpy,gzip,json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
out=Path(__file__).resolve().parent;root=out/'native-shared';rows=[]
for region in sorted(root.glob('*-regions.json')):
 stem=region.name.removesuffix('-regions.json');r=json.loads(region.read_text())
 for path in sorted(root.glob(stem+'-phase*.obj.gz')):
  vs=[];faces=[]
  with gzip.open(path,'rt') as f:
   for l in f:
    a=l.split()
    if a[0]=='v':vs.append(Vector(tuple(float(n) for n in a[1:])))
    elif a[0]=='f':faces.append(tuple(int(n.split('/')[0])-1 for n in a[1:]))
  thigh=set(r['thigh']);tf=[f for f in faces if all(i in thigh for i in f)];tb=BVHTree.FromPolygons(vs,tf,all_triangles=True)
  row={'file':path.name,'triangles':len(faces),'thigh_triangles':len(tf),'hands':{}}
  for side in ['leftHand','rightHand']:
   hand=set(r[side]);hf=[f for f in faces if all(i in hand for i in f)];hb=BVHTree.FromPolygons(vs,hf,all_triangles=True)
   pairs=hb.overlap(tb);nearest=[tb.find_nearest(vs[i])[3] for i in hand]
   row['hands'][side]={'hand_triangles':len(hf),'bvh_overlap_triangle_pairs':len(pairs),'minimum_hand_vertex_to_thigh_triangle_distance_m':min(nearest)}
  rows.append(row)
report={'scope':'Native same-phase hand/thigh meshes. Zero BVH overlapping triangle pairs means no surface crossings between the selected regions. Nonzero pairs alone require geometric/visual review. Vertex-to-triangle distance is not the exact minimum triangle-to-triangle distance. Regions use >0.5 combined bone weights and omit boundary-crossing triangles; not a whole-body collision proof.','rows':rows}
(out/'native-shared-surface-analysis.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
