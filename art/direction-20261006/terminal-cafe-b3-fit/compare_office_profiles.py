from pathlib import Path
import json,gzip
import numpy as np
from mesh_geometry import pieces,chair_intersection_count
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
OLD=ROOT/'art/direction-20261006/terminal-cafe-b2/seating-amendment/native-b2-pose-review/snow-work-SnowAdult_CreatorMesh-00.obj.gz'
GOALS=ROOT/'art/character-candidates/motion-review-20261006/native-seated-goals-v1'
v,f=pieces(OLD)[1];v-=np.array([-7.5,0,3]);rows=[]
profiles=[('original_depth_rear',1,-.37),('adult_depth_48cm',.8,-.31)]
for name,depth,center in profiles:
 cv=v.copy();cv[:,2]=(cv[:,2]+.10)*depth+center
 for p in sorted(GOALS.glob('*_act_type_SeatedContact_Goals-candidate-tier0-phase*.obj.gz')):
  count=chair_intersection_count(pieces(p)[0],(cv,f),0)
  rows.append({'profile':name,'depth_scale':depth,'center_z':center,'file':p.name,'look':p.name.split('_')[0],'intersecting_chair_triangles':count})
 with gzip.open(HERE/(name+'-office-target.obj.gz'),'wt')as out:
  for pt in cv:out.write('v '+' '.join(f'{x:.9g}'for x in pt)+'\n')
  for tri in f:out.write('f '+' '.join(str(int(x)+1)for x in tri)+'\n')
(HERE/'office-profile-comparison.json').write_text(json.dumps({'status':'typing_v1_geometry_provisional','rows':rows},indent=2)+'\n')
for name,depth,center in profiles:
 for look in['Snow','Rain']:print(name,look,[r['intersecting_chair_triangles']for r in rows if r['profile']==name and r['look']==look])
