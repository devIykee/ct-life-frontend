from pathlib import Path
import sys,json,hashlib
import numpy as np
ROOT=Path(__file__).resolve().parents[4];FIT=ROOT/'art/direction-20261006/terminal-cafe-b3-fit';sys.path.insert(0,str(FIT))
from mesh_geometry import pieces,chair_intersection_count
H=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
CHAIR=FIT/'native-typing-fit-review-v3/furniture-surfaces/shared-office-chair-actual.obj.gz';DESK=FIT/'native-typing-fit-review-v3/furniture-surfaces/shared-desk-actual.obj.gz';targets={'chair':pieces(CHAIR)[0],'desk':pieces(DESK)[0]}
rows=[];sources={str(p.relative_to(ROOT)):H(p)for p in [CHAIR,DESK]}
for look in ['snow','rain']:
 p=ROOT/'art/character-candidates/motion-review-20261006/native-cafe-seat-controller-v1'/(look+'-stand-48-tier0.obj.gz');v,f=pieces(p)[0];sources[str(p.relative_to(ROOT))]=H(p)
 for x in [0,.15,.3,.45,.6,.75,1.35]:
  offset=np.array([x,0,-.1*x/1.35]); body=(v+offset,f)
  rows.append({'look':look,'root_offset':offset.tolist(),'source_static_idle_only':True,'crossed_furniture_triangles':{k:chair_intersection_count(body,t,0,min_y=-.01,max_y=1.3)for k,t in targets.items()}})
result={'status':'read_only_discrete_static_standing_sweep_not_transition_or_walk_acceptance','coordinate_frame':'terminal_1 anchor local, original chair/desk source surfaces unchanged','source_hashes':sources,'native_furniture_bounds':{k:{'min':v.min(0).tolist(),'max':v.max(0).tolist()}for k,(v,f)in targets.items()},'samples':rows,'limitations':['Static settled idle from prior native controller review, translated at discrete samples; not standing-up animation or gait','No new runtime/furniture/anchor transform is implemented','All-LOD, feet trajectory and intermediate animated contact remain motion-native responsibilities','Surface crossing test cannot exclude coplanar/enclosed overlap','Current Cafe bake radius .5m differs from Local agent .3m; a nearer endpoint requires native SamplePosition/complete path proof and is not selected here']}
(Path(__file__).parent/'static-standing-sweep.json').write_text(json.dumps(result,indent=2)+'\n')
for row in rows:print(row['look'],row['root_offset'],row['crossed_furniture_triangles'])
