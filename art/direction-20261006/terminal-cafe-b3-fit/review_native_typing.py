"""Final persisted-FK native combined furniture surfaces; no actor/furniture mutation."""
from pathlib import Path
import hashlib,json
import numpy as np
from mesh_geometry import pieces,chair_intersection_count
from surface_witnesses import vertical,summaries
HERE=Path(__file__).resolve().parent;SOURCE=HERE/'native-typing-fit-review-v2';origin=np.array([-7.5,0,3]);rows=[]
for path in sorted(SOURCE.glob('*.obj.gz')):
 data=pieces(path);assert len(data)==4,(path,len(data))
 data=[(v-origin,f)for v,f in data];body,chair,desk,keyboard=data;bv,bf=body;cv,cf=chair;bt=bv[bf];ct=cv[cf]
 n=np.cross(ct[:,1]-ct[:,0],ct[:,2]-ct[:,0]);ny=abs(n[:,1])/np.maximum(np.linalg.norm(n,axis=1),1e-12)
 seat=ct[(ny>.9)&(ct.mean(1)[:,1]>.4)&(ct.mean(1)[:,1]<.49)]
 points=np.array([(x,z)for x in np.arange(-.205,.206,.006)for z in np.arange(-.493,-.070,.006)])
 seat_y=vertical(points,seat,.4,.49,highest=True);subset=bt[(bt.min(1)[:,1]<.7)&(bt.max(1)[:,1]>.3)&(bt.max(1)[:,2]>-.6)&(bt.min(1)[:,2]<.12)]
 body_y=vertical(points,subset,.3,.7);valid=np.isfinite(body_y)&np.isfinite(seat_y);central=valid&(abs(points[:,0])<=.145)&(points[:,1]>=-.42)&(points[:,1]<=-.15)
 row={'file':path.name,'look':path.name.split('-')[0],'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'crossed_furniture_triangles':{},'posterior_gap_percentiles_m':summaries(body_y[central]-seat_y[central]),'seat_penetrations_beyond_2mm':int(((body_y[valid]-seat_y[valid])<-.002).sum()),'whole_body_floor_min_y_m':float(bv[:,1].min())}
 for name,target in [('chair',chair),('desk',desk),('keyboard',keyboard)]:row['crossed_furniture_triangles'][name]=chair_intersection_count(body,target,0,min_y=-.01,max_y=1.2)
 rows.append(row)
assert len(rows)==48
receipt=SOURCE/'receipt.json';j=json.loads(receipt.read_text());assert j['protected_drift']==[] and j['status']=='captured_not_combined_furniture_accepted'
result={'status':'bounded_discrete_persisted_FK_furniture_geometry_review','native_receipt_sha256':hashlib.sha256(receipt.read_bytes()).hexdigest(),'coordinate_frame':'actual native exports relative to immutable terminal_1(-7.5,0,3)','rows':rows,'limitations':['Eight phases and all3body LODs across bothlooks; not continuous-time full-volume collision proof','Actual native source chair/desk plus exact keyboard target; no source/furniture/clip changes','Edge-triangle crossing tests do not prove coplanar or enclosed-volume nonoverlap absent','Keyboard submillimetre source-skin contact is reviewed separately; no new pose adjustment proposed without visible failure','Final seated controller transition and full room/phone acceptance remain separate']}
(HERE/'native-persisted-typing-furniture-surfaces.json').write_text(json.dumps(result,indent=2)+'\n')
for look in ['snow','rain']:
 r=[x for x in rows if x['look']==look]
 print(look,'cases',len(r),'crossingranges',{k:[min(x['crossed_furniture_triangles'][k]for x in r),max(x['crossed_furniture_triangles'][k]for x in r)]for k in ['chair','desk','keyboard']},'seatpen',sum(x['seat_penetrations_beyond_2mm']for x in r),'solerange',[min(x['whole_body_floor_min_y_m']for x in r),max(x['whole_body_floor_min_y_m']for x in r)])
