from pathlib import Path
import sys,json,hashlib,ast
import numpy as np
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent;FIT=ROOT/'art/direction-20261006/terminal-cafe-b3-fit';sys.path.insert(0,str(FIT));import mesh_geometry as mg
src=(FIT/'analyze_typing_support.py').read_text();tree=ast.parse(src);fn=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='vertical');exec(compile(ast.Module(body=[fn],type_ignores=[]),'<vertical>','exec'))
F=FIT/'native-typing-fit-review-v3/furniture-surfaces';paths={'chair':F/'shared-office-chair-actual.obj.gz','desk':F/'shared-desk-actual.obj.gz'};targets={k:mg.pieces(p)[0]for k,p in paths.items()};H=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();sources={str(p.relative_to(ROOT)):H(p)for p in paths.values()};yaw=[];shifts=[]
for look in ['snow','rain']:
 p=ROOT/'art/character-candidates/motion-review-20261006/native-cafe-seat-controller-v1'/(look+'-stand-48-tier0.obj.gz');v,f=mg.pieces(p)[0];sources[str(p.relative_to(ROOT))]=H(p)
 for x,z,deg in [(0,.05,a)for a in range(0,91,10)]+[(x,.05,90)for x in [.15,.30,.45,.60]]:
  r=np.radians(deg);rot=np.array([[np.cos(r),0,np.sin(r)],[0,1,0],[-np.sin(r),0,np.cos(r)]]);body=(v@rot.T+[x,0,z],f)
  yaw.append({'look':look,'root_xz':[x,z],'yaw_degrees':deg,'crossings':{k:mg.chair_intersection_count(body,t,0,min_y=-.01,max_y=1.3)for k,t in targets.items()}})
 print(look,'yaw done',flush=True)
 cv,cf=targets['chair'];ct=cv[cf];n=np.cross(ct[:,1]-ct[:,0],ct[:,2]-ct[:,0]);ny=np.abs(n[:,1])/np.maximum(np.linalg.norm(n,axis=1),1e-12);seat=ct[(ny>.9)&(ct.mean(1)[:,1]>.4)&(ct.mean(1)[:,1]<.49)]
 points=np.array([(x,z)for x in np.arange(-.14,.141,.01)for z in np.arange(-.42,-.149,.01)])
 for p in sorted((ROOT/'art/character-candidates/motion-review-20261006/native-cafe-typing-strategies-v1').glob(look+'-bridge-exit-*-tier0.obj.gz')):
  bv,bf=mg.pieces(p)[0];bt=bv[bf];body_y=vertical(points,bt,.30,.65);sources[str(p.relative_to(ROOT))]=H(p)
  for dz in [0,-.05,-.08,-.12]:
   ss=seat.copy();ss[:,:,2]+=dz;seat_y=vertical(points,ss,.4,.49,highest=True);valid=np.isfinite(body_y)&np.isfinite(seat_y);g=body_y[valid]-seat_y[valid]
   shifts.append({'look':look,'pose':p.name,'chair_delta_z':dz,'chair_crossings':mg.chair_intersection_count((bv,bf),(cv,cf),dz,min_y=-.01,max_y=1.3),'body_rays':int(np.isfinite(body_y).sum()),'supported_rays':int(valid.sum()),'gap_min_m':float(g.min())if len(g)else None,'gap_p05_m':float(np.percentile(g,5))if len(g)else None,'gap_median_m':float(np.median(g))if len(g)else None,'gap_le_8mm_rays':int(((g>=-.002)&(g<=.008)).sum())})
 print(look,'shift done',flush=True)
result={'status':'coarse_route_and_shared_chair_alternatives_not_acceptance','tier':0,'source_sha256':sources,'yaw_sweep':yaw,'shared_chair_shift':shifts,'limitations':['Discrete native settled idle rotated/translated, no turning gait or rise authoring.','Shared chair translation is only a read-only proposed geometry comparison; no chair asset/anchor/approach changed.','Bridge exit OBJ snapshots are accepted controller strategy source, not complete moving-player behavior.','Posterior geometric rays X±.14/Z−.42..−.15 are not bone-weighted pressure mapping.','Surface crossing test excludes neither enclosed nor coplanar penetration; no continuous collision proof.']};(HERE/'quick-exit-alternatives.json').write_text(json.dumps(result,indent=2)+'\n')
print('YAW',json.dumps(yaw),flush=True)
for look in ['snow','rain']:
 for dz in [0,-.05,-.08,-.12]:
  a=[r for r in shifts if r['look']==look and r['chair_delta_z']==dz];print(look,dz,'max_crossings',max(r['chair_crossings']for r in a),'nonzero',[(r['pose'],r['chair_crossings'])for r in a if r['chair_crossings']],'initial',a[0],flush=True)
