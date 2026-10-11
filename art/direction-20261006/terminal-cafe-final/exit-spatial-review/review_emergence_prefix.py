from pathlib import Path
import sys,inspect,json,hashlib,time
import numpy as np
ROOT=Path(__file__).resolve().parents[4];FIT=ROOT/'art/direction-20261006/terminal-cafe-b3-fit';sys.path.insert(0,str(FIT));import mesh_geometry as mg
code=inspect.getsource(mg.chair_intersection_count).replace('chair_intersection_count','crossed_triangles').replace('return 0','return np.empty((0,3,3))').replace('hits = 0','hits = []').replace('for c in ct:','for ci,c in enumerate(ct):').replace('hits += 1','hits.append(ci)').replace('return hits','return ct[hits]');segment_hits=mg.segment_hits;exec(code,globals())
SOURCE=ROOT/'art/character-candidates/motion-review-20261006/native-cafe-emergence-source-v1';native=json.loads((SOURCE/'receipt.json').read_text());assert native['capture_pass'] and native['protected_drift']==[]
f=FIT/'native-typing-fit-review-v3/furniture-surfaces';paths={'chair':f/'shared-office-chair-actual.obj.gz','desk':f/'shared-desk-actual.obj.gz'};targets={k:mg.pieces(p)[0]for k,p in paths.items()};sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();rows=[]
for case in native['cases']:
 for phase in range(0,85,12):
  for tier in range(3):
   p=SOURCE/(case['id']+'-'+str(phase).zfill(3)+'-tier'+str(tier)+'.obj.gz');body=mg.pieces(p)[0];tests={}
   for name,target in targets.items():
    hit=crossed_triangles(body,target,0,min_y=-.01,max_y=1.3)
    tests[name]={'crossed_triangle_count':len(hit),'crossed_target_bounds_min':hit.min((0,1)).tolist()if len(hit)else None,'crossed_target_bounds_max':hit.max((0,1)).tolist()if len(hit)else None}
   rows.append({'case':case['id'],'frame':phase,'time_s':phase/120,'tier':tier,'body_sha256':sha(p),'body_floor_min_y':float(body[0][:,1].min()),'furniture':tests})
 print(case['id'],'done',flush=True)
assert len(rows)==192
summary={}
for case in native['cases']:
 r=[x for x in rows if x['case']==case['id']];summary[case['id']]={k:{'min':min(x['furniture'][k]['crossed_triangle_count']for x in r),'max':max(x['furniture'][k]['crossed_triangle_count']for x in r),'frames_with_crossing':sorted(set(x['frame']for x in r if x['furniture'][k]['crossed_triangle_count']>0))}for k in targets}
result={'status':'source_prefix_furniture_intersection_evidence_not_exit_acceptance','native_source_receipt_sha256':sha(SOURCE/'receipt.json'),'targets_sha256':{str(p.relative_to(ROOT)):sha(p)for p in paths.values()},'time_range_seconds':[0,.7],'sample_spacing_seconds':.1,'cases':8,'body_LODs_per_case':3,'sample_count':len(rows),'summary':summary,'rows':rows,'limitations':['Actual native source clip candidates retain known foot defects; no source case is accepted','Discrete surface crossing only, not continuous or enclosed/coplanar volume proof','No actor, clip, root, furniture or runtime change; no motion smoothing can be accepted from this audit','Corrected planted feet may change lower-body contact and need a fresh exact exported mesh audit']}
(Path(__file__).parent/'emergence-source-prefix-surfaces.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(summary,indent=2),flush=True)
