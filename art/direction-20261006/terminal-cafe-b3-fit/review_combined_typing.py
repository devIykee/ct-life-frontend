"""Final transient hand/body exports vs frozen shared physical workstation targets.

CPU surface evidence only; actual native placement and baked-clip replay remain gates.
"""
from pathlib import Path
import hashlib,json
import numpy as np
from mesh_geometry import pieces,chair_intersection_count
from surface_witnesses import vertical,summaries
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
BODY=ROOT/'art/character-candidates/motion-review-20261006/native-typing-work-hands-v2'
targets={'chair':HERE/'adult_depth_48cm-office-target.obj.gz','desk':HERE/'shared-typing-desk-target.obj.gz','keyboard':HERE/'keyboard/shared-typing-keyboard-target.obj.gz'}
geometry={name:pieces(path)[0] for name,path in targets.items()}
hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()for p in targets.values()}
cv,cf=geometry['chair'];ct=cv[cf]
n=np.cross(ct[:,1]-ct[:,0],ct[:,2]-ct[:,0]);ny=abs(n[:,1])/np.maximum(np.linalg.norm(n,axis=1),1e-12)
seat=ct[(ny>.9)&(ct.mean(1)[:,1]>.4)&(ct.mean(1)[:,1]<.49)]
points=np.array([(x,z)for x in np.arange(-.205,.206,.006)for z in np.arange(-.493,-.070,.006)])
seat_y=vertical(points,seat,.4,.49,highest=True)
rows=[]
for path in sorted(BODY.glob('*-candidate-tier*-phase*.obj.gz')):
 bv,bf=pieces(path)[0];bt=bv[bf]
 row={'file':path.name,'look':path.name.split('-')[0],'crossed_furniture_triangles':{},'sole_min_y_m':float(bv[:,1].min())}
 for name,target in geometry.items(): row['crossed_furniture_triangles'][name]=chair_intersection_count((bv,bf),target,0,min_y=-.01,max_y=1.2)
 subset=bt[(bt.min(1)[:,1]<.70)&(bt.max(1)[:,1]>.30)&(bt.max(1)[:,2]>-.60)&(bt.min(1)[:,2]<.12)]
 body_y=vertical(points,subset,.30,.70)
 valid=np.isfinite(body_y)&np.isfinite(seat_y)
 central=valid&(abs(points[:,0])<=.145)&(points[:,1]>=-.42)&(points[:,1]<=-.15)
 row['posterior_gap_percentiles_m']=summaries(body_y[central]-seat_y[central])
 row['seat_ray_penetrations_beyond_2mm']=int(((body_y[valid]-seat_y[valid])<-.002).sum())
 rows.append(row);hashes[str(path.relative_to(ROOT))]=hashlib.sha256(path.read_bytes()).hexdigest()
assert len(rows)==48
result={'status':'sampled_combined_native_pose_vs_frozen_target_surfaces_not_final_acceptance','input_sha256':hashes,'rows':rows,'limitations':['Eight exported phases per look and three LODs; not continuous-time collision proof','Actual native body geometry includes calibrated transient OnAnimatorIK hand goals, not yet persistent baked-clip replay','Furniture targets are CPU transforms of native chair/desk plus frozen licensed keyboard, not yet combined native scene placement','Edge-triangle crossings do not prove enclosed volumes or coplanar overlap absent','Keyboard submillimetre contact must be assessed with geometric clearance and render, not a blanket zero-crossing rule','No actor root or contract anchor changes; same furniture for both looks']}
(HERE/'combined-typing-hand-v2-surfaces.json').write_text(json.dumps(result,indent=2)+'\n')
for look in ['snow','rain']:
 r=[x for x in rows if x['look']==look]
 print(look,'samples',len(r),'crossings',{n:[min(x['crossed_furniture_triangles'][n]for x in r),max(x['crossed_furniture_triangles'][n]for x in r)]for n in targets},'seat_penetrations',sum(x['seat_ray_penetrations_beyond_2mm']for x in r),'sole_range',[min(x['sole_min_y_m']for x in r),max(x['sole_min_y_m']for x in r)])
