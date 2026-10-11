"""Native B3 full-body chair/table surface audit; no source mutation."""
from pathlib import Path
import json,hashlib
import numpy as np
from mesh_geometry import pieces,chair_intersection_count
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2];FOLDER=HERE/'native-social-fit-review'
rows=[]
for path in sorted(FOLDER.glob('*.obj.gz')):
 pieces_here=pieces(path)
 assert len(pieces_here)==3
 body,chair,table=pieces_here
 relative=np.array([-6,0,-4])
 bv,bf=body;cv,cf=chair;tv,tf=table
 bv=bv-relative;cv=cv-relative;tv=tv-relative
 row={'file':path.name,'look':path.name.split('-')[0],'body_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
 'chair_crossings_all_heights':chair_intersection_count((bv,bf),(cv,cf),0,min_y=-.01,max_y=1.2),
 'body_front_z_m':float(bv[:,2].max()),'chair_top_source_matches_450mm':bool(np.any(abs(cv[:,1]-.45)<1e-5)),
 'table_translation_tests':[]}
 for delta in [0,-.15,-.20]:
  row['table_translation_tests'].append({'delta_z_m':delta,'triangle_crossings':chair_intersection_count((bv,bf),(tv,tf),delta,min_y=-.01,max_y=1.2),
   'full_bounds_front_separation_m':float(tv[:,2].min()+delta-bv[:,2].max())})
 rows.append(row)
assert len(rows)==48
(HERE/'native-social-furniture-surfaces.json').write_text(json.dumps({'status':'discrete_triangle_and_bounds_checks_not_full_contact_acceptance','notes':['Actual native B3 chair/table and body meshes,8phases3LODs2looks','Triangle edges test all chair/table heights; enclosed volumes and coplanar overlap not proven absent','Table deltas are unimplemented composition comparisons; actor roots/chairs/anchors unchanged','Positive full-bounds separation is conservative in world Z,not measured reachable hand contact'], 'rows':rows},indent=2)+'\n')
for look in ['snow','rain']:
 r=[x for x in rows if x['look']==look]
 print(look,'chair_crossings',sum(x['chair_crossings_all_heights']for x in r))
 for d in [0,-.15,-.20]:
  tests=[t for x in r for t in x['table_translation_tests']if t['delta_z_m']==d]
  print(d,'table_crossings',sum(t['triangle_crossings']for t in tests),'z_gap_range',min(t['full_bounds_front_separation_m']for t in tests),max(t['full_bounds_front_separation_m']for t in tests))
