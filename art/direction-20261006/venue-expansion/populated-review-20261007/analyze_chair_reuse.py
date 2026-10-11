from pathlib import Path
import json,hashlib,gzip,sys,ast
import numpy as np
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent
FIT=ROOT/'art/direction-20261006/terminal-cafe-b3-fit';sys.path.insert(0,str(FIT));import mesh_geometry as mg
fn=next(n for n in ast.parse((FIT/'analyze_typing_support.py').read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='vertical');exec(compile(ast.Module(body=[fn],type_ignores=[]),'<vertical>','exec'))
H=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def read(p):
 v=[];f=[]
 with (gzip.open(p,'rt')if str(p).endswith('.gz')else p.open())as stream:
  for line in stream:
   a=line.split()
   if a and a[0]=='v':v.append(list(map(float,a[1:4])))
   if a and a[0]=='f':f.append([int(x.split('/')[0])-1 for x in a[1:4]])
 return np.array(v),np.array(f)
chairs={k:read(HERE/'native-evidence'/(k+'-chair.obj'))for k in ['audience','speaker']};sources={str(p.relative_to(ROOT)):H(p)for p in(HERE/'native-evidence').glob('*')}
receipt=json.loads((HERE/'native-evidence/receipt.json').read_text());rows=[]
points=np.array([(x,z)for x in np.arange(-.14,.141,.01)for z in np.arange(-.42,-.149,.01)])
def evaluate(body,chair):
 bv,bf=body;cv,cf=chair;ct=cv[cf];normal=np.cross(ct[:,1]-ct[:,0],ct[:,2]-ct[:,0]);ny=np.abs(normal[:,1])/np.maximum(np.linalg.norm(normal,axis=1),1e-12)
 seat=ct[(ny>.90)&(np.abs(ct.mean(1)[:,1]-.425)<.0001)];seat_y=vertical(points,seat,.40,.45,highest=True);body_y=vertical(points,bv[bf],.30,.63);valid=np.isfinite(seat_y)&np.isfinite(body_y);g=body_y[valid]-seat_y[valid]
 regions={}
 for name,(low,high)in{'feet_and_legs':(-.02,.425),'seat_back_and_arms':(.425,.85),'above_back':(.85,1.5)}.items():regions[name]=mg.chair_intersection_count(body,chair,0,min_y=low,max_y=high)
 return {'chair_triangle_crossings':mg.chair_intersection_count(body,chair,0,min_y=-.02,max_y=1.5),'overlapping_vertical_band_counts':regions,'floor_min_m':float(bv[:,1].min()),'posterior_body_rays':int(np.isfinite(body_y).sum()),'posterior_supported_rays':int(valid.sum()),'support_gap_min_m':float(g.min())if len(g)else None,'support_gap_p05_m':float(np.percentile(g,5))if len(g)else None,'support_gap_median_m':float(np.median(g))if len(g)else None,'support_gap_within_8mm_rays':int(((g>=-.002)&(g<=.008)).sum())}
for capture in receipt['captures']:
 if not capture['label'].endswith('-side'):continue
 for pose in capture['poses']:
  if not pose['meshFile']:continue
  p=HERE/'native-evidence'/pose['meshFile'];v,f=read(p);a=np.radians(pose['yaw']);rot=np.array([[np.cos(a),0,np.sin(a)],[0,1,0],[-np.sin(a),0,np.cos(a)]]);v=(v-np.array(pose['root']))@rot
  family='speaker'if pose['spot'].startswith('stage_')else'audience'
  result={'kind':'actual_current_canonical_capture','look':pose['look'],'spot':pose['spot'],'family':family,'normalized_time':pose['normalizedTime'],'tier':0,'root_transform_removed_from_native_world_export':True,**evaluate((v,f),chairs[family])};rows.append(result);print(result,flush=True)
CANDIDATE=ROOT/'art/character-candidates/motion-review-20261006/native-seated-body-fit-v1'
for look in ['Snow','Rain']:
 for p in sorted(CANDIDATE.glob(look+'_SocialBodyFit-candidate-tier*-phase*.obj.gz')):
  sources[str(p.relative_to(ROOT))]=H(p);v,f=read(p)
  result={'kind':'unchanged_cafe_social_candidate_readonly_projection','look':look.lower(),'family':'audience','tier':int(p.name.split('-tier')[1][0]),'phase':int(p.name.split('-phase')[1][:3]),**evaluate((v,f),chairs['audience'])};rows.append(result)
 print(look,'candidate rows',sum(r['kind'].startswith('unchanged')and r['look']==look.lower()for r in rows),flush=True)
 # Independent native speaker chair is equivalent after root-yaw removal to submicrometre precision; sample first candidate against it too.
 p=CANDIDATE/(look+'_SocialBodyFit-candidate-tier0-phase000.obj.gz');rows.append({'kind':'unchanged_cafe_social_candidate_speaker_control','look':look.lower(),'family':'speaker','tier':0,'phase':0,**evaluate(read(p),chairs['speaker'])})
cv,cf=chairs['audience'];sv,sf=chairs['speaker']
report={'status':'unchanged_cafe_social_reuse_rejected_by_actual_chair_surfaces','source_sha256':sources,'chair_top_y_m':.425,'chair_bounds':{'min':cv.min(0).tolist(),'max':cv.max(0).tolist()},'same_order_speaker_geometry_max_vertex_delta_m':float(np.linalg.norm(cv-sv,axis=1).max()),'speaker_topology_identical':bool(np.array_equal(cf,sf)),'immutable_roots_and_furniture_preserved':True,'cases':rows,'limitations':['Discrete native mesh samples, not continuous animation or complete inside-volume penetration detection.','Crossing counts count chair triangles struck by either mesh edge set; counts in vertical bands can overlap.','Posterior vertical rays are geometric witnesses, not bone-weighted pressure or full body support tests.','Café candidate meshes are evaluated at unchanged Studio anchor-local origin/yaw; no Studio native animation/controller assignment occurred.','No root shift, foot warp, collider change, runtime profile, room geometry or registration was applied.']}
(HERE/'studio-chair-reuse-audit.json').write_text(json.dumps(report,indent=2)+'\n')
for look in ['snow','rain']:
 r=[x for x in rows if x['kind']=='unchanged_cafe_social_candidate_readonly_projection'and x['look']==look];print('SUMMARY',look,'samples',len(r),'crossing range',min(x['chair_triangle_crossings']for x in r),max(x['chair_triangle_crossings']for x in r),'support min/max',min(x['support_gap_min_m']for x in r if x['support_gap_min_m']is not None),max(x['support_gap_min_m']for x in r if x['support_gap_min_m']is not None),'within8mmmax',max(x['support_gap_within_8mm_rays']for x in r),flush=True)
