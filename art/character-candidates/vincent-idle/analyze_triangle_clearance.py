"""Finite-triangle surface distance on actual native poses. No canonical/model mutation."""
import bpy,gzip,json,math,time
from pathlib import Path
from mathutils import Vector
from mathutils.geometry import closest_point_on_tri,intersect_ray_tri
from mathutils.bvhtree import BVHTree
out=Path(__file__).resolve().parent;root=out/'native-shared';TOLERANCE=.002

def segment_distance(a,b,c,d):
 u=b-a;v=d-c;w=a-c;aa=u.dot(u);bb=u.dot(v);cc=v.dot(v);dd=u.dot(w);ee=v.dot(w)
 if aa<1e-20:return (a-(c+v*max(0,min(1,ee/cc)))).length if cc>1e-20 else (a-c).length
 if cc<1e-20:return (c-(a+u*max(0,min(1,-dd/aa)))).length
 den=aa*cc-bb*bb;s=max(0,min(1,(bb*ee-cc*dd)/den)) if den>1e-20 else 0
 t=(bb*s+ee)/cc
 if t<0:t=0;s=max(0,min(1,-dd/aa))
 elif t>1:t=1;s=max(0,min(1,(bb-dd)/aa))
 return ((a+s*u)-(c+t*v)).length

def segment_crosses(a,b,t):
 d=b-a
 if d.length_squared<1e-20:return False
 p=intersect_ray_tri(t[0],t[1],t[2],d,a,True)
 return p is not None and -1e-7<=(p-a).dot(d)/d.length_squared<=1+1e-7

def tri_distance(a,b):
 ea=[(a[i],a[(i+1)%3]) for i in range(3)];eb=[(b[i],b[(i+1)%3]) for i in range(3)]
 if any(segment_crosses(x,y,b) for x,y in ea) or any(segment_crosses(x,y,a) for x,y in eb):return 0.
 ds=[(p-closest_point_on_tri(p,*b)).length for p in a]+[(p-closest_point_on_tri(p,*a)).length for p in b]
 ds += [segment_distance(x,y,u,v) for x,y in ea for u,v in eb]
 return min(ds)
# Independent analytic fixtures, including an edge piercing the interior of another triangle.
a=[Vector(p) for p in [(0,0,0),(1,0,0),(0,1,0)]]
assert abs(tri_distance(a,[p+Vector((0,0,.003)) for p in a])-.003)<1e-6
assert tri_distance(a,[Vector(p) for p in [(.25,.25,-1),(.25,.25,1),(.75,.25,0)]])<1e-7
assert abs(segment_distance(Vector((0,0,0)),Vector((1,0,0)),Vector((.5,-1,.001)),Vector((.5,1,.001)))-.001)<1e-6
rows=[];started=time.perf_counter()
for region in sorted(root.glob('*-regions.json')):
 stem=region.name.removesuffix('-regions.json');r=json.loads(region.read_text())
 for path in sorted(root.glob(stem+'-phase*.obj.gz')):
  vs=[];faces=[]
  with gzip.open(path,'rt') as f:
   for l in f:
    x=l.split()
    if x[0]=='v':vs.append(Vector(tuple(float(n) for n in x[1:])))
    elif x[0]=='f':faces.append(tuple(int(n.split('/')[0])-1 for n in x[1:]))
  region_set=set(r['thigh']);tf=[f for f in faces if any(i in region_set for i in f)];tv=[tuple(vs[i] for i in f) for f in tf];tb=BVHTree.FromPolygons(vs,tf,all_triangles=True)
  row={'file':path.name,'thigh_triangles_including_boundary':len(tf),'hands':{}}
  for side in ['leftHand','rightHand']:
   hand=set(r[side]);hf=[f for f in faces if any(i in hand for i in f)]
   best=min(tb.find_nearest(vs[i])[3] for i in {i for f in hf for i in f});tested=0;closest=None
   for hi,f in enumerate(hf):
    tri=tuple(vs[i] for i in f);centre=sum(tri,Vector())/3;radius=max((p-centre).length for p in tri)
    # Any pair closer than best must have a target point inside this sphere.
    if tb.find_nearest(centre)[3]-radius>best:continue
    for location,normal,ti,distance in tb.find_nearest_range(centre,radius+best+1e-7):
     tested+=1;distance=tri_distance(tri,tv[ti])
     if distance<best:best=distance;closest={'hand_triangle':list(f),'thigh_triangle':list(tf[ti])}
     if best<1e-8:break
    if best<1e-8:break
   row['hands'][side]={'surface_feature_distance_m':best,'passes_2mm_margin':best>=TOLERANCE,'hand_triangles_including_boundary':len(hf),'exact_triangle_pairs_tested':tested,'closest_pair':closest}
  rows.append(row);print(path.name,{k:round(v['surface_feature_distance_m']*1000,3) for k,v in row['hands'].items()},flush=True)
coverage={region.name.removesuffix('-regions.json'):sum(row['file'].startswith(region.name.removesuffix('-regions.json')+'-phase') for row in rows) for region in root.glob('*-regions.json')}
complete=len(coverage)==4 and all(count==13 for count in coverage.values())
report={'all_pass':complete and all(h['passes_2mm_margin'] for r in rows for h in r['hands'].values()),'coverage_complete':complete,'phases_per_skin':coverage,'engineering_surface_margin_m':TOLERANCE,'expected_unique_phases_per_skin':12,'expected_phases_with_loop_endpoint_per_skin':13,'actual_files':len(rows),'scope':'Minimum finite-triangle feature distance on selected hand and hip/thigh surface regions, including triangles crossing the >0.5 bone-weight selection boundary. Tests vertex-face, edge-edge and edge-through-face crossings. BVH sphere pruning bounds possible closer pairs. A 2 mm engineering margin is independent of the prior10 mm axis proxy. Selected regions are not closed volumes: this does not prove whole-body containment/collision correctness, transitions, or unsampled times.','analytic_fixtures_pass':True,'elapsed_analysis_seconds':time.perf_counter()-started,'rows':rows}
(out/'native-shared-triangle-clearance.json').write_text(json.dumps(report,indent=2));print('TRIANGLE_CLEARANCE',report['all_pass'],len(rows),flush=True)
