import sys,pathlib,json,numpy as np,hashlib
root=pathlib.Path(__file__).resolve().parents[4];sys.path.insert(0,str(root/'art/direction-20261006/terminal-cafe-b3-fit'))
from mesh_geometry import pieces,segment_hits,chair_intersection_count
from surface_witnesses import vertical
fit=root/'art/direction-20261006/terminal-cafe-b3-fit/native-typing-fit-review-v3/furniture-surfaces';targets={n:pieces(fit/f'shared-{f}-actual.obj.gz')[0]for n,f in [('chair','office-chair'),('desk','desk')]};focus=np.array([-7.5,0,3])
def pairhits(body,target):
 bv,bf=body;cv,cf=target;bt=bv[bf];ct=cv[cf];bmin=bt.min(1);bmax=bt.max(1);pairs=[]
 for ci,c in enumerate(ct):
  ix=np.where((bmax>=c.min(0)-1e-5).all(1)&(bmin<=c.max(0)+1e-5).all(1))[0]
  if not len(ix):continue
  near=bt[ix];hit=np.zeros(len(ix),dtype=bool)
  for i in range(3):hit|=segment_hits(c[i],c[(i+1)%3],near)
  for j,tri in enumerate(near):
   if not hit[j]:hit[j]=any(segment_hits(tri[k],tri[(k+1)%3],c[None]).any()for k in range(3))
  pairs += [(ci,int(bi))for bi in ix[hit]]
 return pairs
rows=[]
for sample,target in [(1,'chair'),(7,'desk')]:
 for case in ['snow-local-exit-stand','snow-released-exit']:
  for tier in range(3):
   path=pathlib.Path('/tmp/ct-cafe-continuous-exit-v5d-clock-pictures-20261007')/f'{case}-{sample}-tier{tier}.obj.gz';v,f=pieces(path)[0];body=(v,f);cv,cf=targets[target];pairs=pairhits(body,targets[target]);bi=sorted({b for a,b in pairs});ci=sorted({a for a,b in pairs});pts=v[f[bi]].reshape(-1,3);tri=cv[cf[ci]]
   row={'case':case,'sample':sample,'tier':tier,'body_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'target':target,'crossed_furniture_indices':ci,'body_triangle_indices':bi,'body_triangle_bounds_min':pts.min(0).tolist(),'body_triangle_bounds_max':pts.max(0).tolist(),'furniture_triangles':tri.tolist()}
   if target=='chair':
    grid=np.array([[x,z]for x in np.arange(-.2,.201,.002)for z in np.arange(-.35,-.1,.002)]);ys=vertical(grid,cv[cf],.40,.49,True);yb=vertical(grid,v[f],.30,.55);valid=np.isfinite(ys)&np.isfinite(yb);g=yb[valid]-ys[valid];ii=np.argmin(g);row['vertical_support_grid_step_m']=.002;row['min_vertical_gap_m']=float(g[ii]);row['minimum_gap_xz']=grid[valid][ii].tolist();row['negative_grid_rays']=int((g<0).sum());row['tested_grid_rays']=int(valid.sum())
   else:
    # Contacting body triangle barycentric samples, then vertical slab inclusion near tabletop; not global solid distance.
    samples=[]
    for a,b,c in v[f[bi]]:
     for u in range(21):
      for w in range(21-u):samples.append(a+(b-a)*(u/20)+(c-a)*(w/20))
    samples=np.array(samples);top=vertical(samples[:,[0,2]],cv[cf],.70,.75,True);bottom=vertical(samples[:,[0,2]],cv[cf],.70,.75);valid=np.isfinite(top)&np.isfinite(bottom)&(samples[:,1]<top)&(samples[:,1]>bottom+.000001)
    inside=samples[valid];row['sampled_contact_points_inside_tabletop_vertical_slab']=len(inside)
    if len(inside):
     down=top[valid]-inside[:,1];up=inside[:,1]-bottom[valid];row['max_depth_below_upper_surface_m']=float(down.max());row['max_depth_above_lower_surface_m']=float(up.max());row['max_nearest_vertical_exit_m']=float(np.minimum(down,up).max());row['inside_bounds_min']=inside.min(0).tolist();row['inside_bounds_max']=inside.max(0).tolist()
    row['static_clearance_diagnostics']=[]
    for direction in [np.array([1,0,0]),np.array([0,0,-1])]:
     first=None
     for mm in range(0,61,5):
      moved=(v+direction*(mm/1000),f)
      if chair_intersection_count(moved,targets['desk'],0,min_y=-.01,max_y=1.3)==0 and chair_intersection_count(moved,targets['chair'],0,min_y=-.01,max_y=1.3)==0:first=mm;break
     row['static_clearance_diagnostics'].append({'direction':direction.tolist(),'first_clear_sample_mm':first,'step_mm':5,'limit_mm':60})
   rows.append(row)
   print(case,sample,tier,target,'contactbounds',row['body_triangle_bounds_min'],row['body_triangle_bounds_max'],'depth',row.get('min_vertical_gap_m',row.get('max_depth_below_upper_surface_m')),'clearance',row.get('static_clearance_diagnostics'))
p=root/'art/direction-20261006/terminal-cafe-final/exit-spatial-review/current-clock-two-contact-measurements.json';p.write_text(json.dumps({'scope':'Read-only two observed contact phases, two Snow local cases and all3LODs. Static translations are geometric diagnostics, not authored motion or route acceptance.','coordinate_frame':'OBJ is already exported relative to terminal_1(-7.5,0,3); no second translation applied. Exact unchanged native chair/desk target','rows':rows},indent=2)+'\n')
