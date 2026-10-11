from pathlib import Path
import json
import numpy as np
from mesh_geometry import pieces,chair_intersection_count
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
POSE=ROOT/'art/direction-20261006/terminal-cafe-b2/seating-amendment/native-b2-pose-review'
GOALS=ROOT/'art/character-candidates/motion-review-20261006/native-seated-goals-v1'
cv,cf=pieces(POSE/'snow-social-SnowAdult_CreatorMesh-00.obj.gz')[1];cv-=np.array([-6,0,-4])
rows=[]
for p in sorted(GOALS.glob('*_act_sit_SeatedContact_Goals-candidate-tier0-phase*.obj.gz')):
 body=pieces(p)[0]
 for dz in [-.12,-.15,-.18,-.20,-.22,-.25]:
  rows.append({'look':p.name.split('_')[0],'file':p.name,'chair_delta_z_m':dz,'intersecting_chair_triangles':chair_intersection_count(body,(cv,cf),dz)})
(HERE/'corrected-social-intersections.json').write_text(json.dumps({'status':'sampled_surface_intersections_not_continuous_clearance','rows':rows},indent=2)+'\n')
for look in ['Snow','Rain']:
 print(look,[(dz,[r['intersecting_chair_triangles']for r in rows if r['look']==look and r['chair_delta_z_m']==dz])for dz in [-.12,-.15,-.18,-.20,-.22,-.25]])
