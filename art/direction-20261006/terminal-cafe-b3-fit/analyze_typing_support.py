"""Read native Unity OBJ geometry only; no mesh, clip, furniture, or anchor mutation.

Vertical ray witnesses compare corrected body undersides to actual imported chair-seat
triangles. They are sampled surface evidence, not full continuous collision proof.
"""
from pathlib import Path
import gzip,json,hashlib
import numpy as np
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
POSE=ROOT/'art/direction-20261006/terminal-cafe-b2/seating-amendment/native-b2-pose-review'
GOALS=ROOT/'art/character-candidates/motion-review-20261006/native-seated-goals-v1'

def pieces(path):
    out=[];v=[];f=[];offset=0
    for line in gzip.open(path,'rt'):
        a=line.split()
        if not a:continue
        if a[0]=='v':
            if f:out.append((np.array(v),np.array(f)));offset+=len(v);v=[];f=[]
            v.append([float(x) for x in a[1:4]])
        elif a[0]=='f':f.append([int(x.split('/')[0])-1-offset for x in a[1:4]])
    if v:out.append((np.array(v),np.array(f)))
    return out

def vertical(points,triangles,lower,upper,highest=False):
    result=np.full(len(points),-np.inf if highest else np.inf)
    for a,b,c in triangles:
        ax,az=a[[0,2]];bx,bz=b[[0,2]];cx,cz=c[[0,2]]
        denom=(bz-cz)*(ax-cx)+(cx-bx)*(az-cz)
        if abs(denom)<1e-12:continue
        low=np.minimum.reduce([a[[0,2]],b[[0,2]],c[[0,2]]])-1e-7
        high=np.maximum.reduce([a[[0,2]],b[[0,2]],c[[0,2]]])+1e-7
        mask=((points>=low)&(points<=high)).all(axis=1);idx=np.where(mask)[0]
        if not len(idx):continue
        x,z=points[idx].T
        u=((bz-cz)*(x-cx)+(cx-bx)*(z-cz))/denom
        v=((cz-az)*(x-cx)+(ax-cx)*(z-cz))/denom
        w=1-u-v;y=u*a[1]+v*b[1]+w*c[1]
        valid=(u>=-1e-7)&(v>=-1e-7)&(w>=-1e-7)&(y>=lower)&(y<=upper)
        idx=idx[valid];y=y[valid]
        result[idx]=(np.maximum if highest else np.minimum)(result[idx],y)
    return result

def summaries(a):
    return {str(p):float(np.percentile(a,p)) for p in [0,1,5,25,50,75,95,99,100]} if len(a) else None

chair,faces=pieces(POSE/'snow-work-SnowAdult_CreatorMesh-00.obj.gz')[1]
chair-=np.array([-7.5,0,3]);ct=chair[faces]
norm=np.cross(ct[:,1]-ct[:,0],ct[:,2]-ct[:,0]);ny=np.abs(norm[:,1])/np.maximum(np.linalg.norm(norm,axis=1),1e-12)
seat_faces=ct[(ny>.90)&(ct.mean(axis=1)[:,1]>.40)&(ct.mean(axis=1)[:,1]<.49)]
xs=np.arange(-.205,.206,.008);zs=np.arange(-.52,.01,.008)
points=np.array([(x,z) for x in xs for z in zs])
records=[];input_hashes={}
for path in sorted(GOALS.glob('*_act_type_SeatedContact_Goals-candidate-tier*-phase*.obj.gz')):
    bv,bf=pieces(path)[0];bt=bv[bf]
    bt=bt[(bt.min(axis=1)[:,1]<.65)&(bt.max(axis=1)[:,1]>.30)&(bt.max(axis=1)[:,2]>-.60)&(bt.min(axis=1)[:,2]<.12)]
    body_y=vertical(points,bt,.30,.65)
    input_hashes[path.name]=hashlib.sha256(path.read_bytes()).hexdigest()
    for dz in [-.15,-.20,-.25]:
        seat=seat_faces.copy();seat[:,:,2]+=dz
        seat_y=vertical(points,seat,.40,.49,highest=True)
        valid=np.isfinite(body_y)&np.isfinite(seat_y)
        gap=body_y[valid]-seat_y[valid]
        # Posterior bearing patch: eliminate front-of-seat thigh taper and side hem fringes.
        central=valid&(np.abs(points[:,0])<=.145)&(points[:,1]>=-.42)&(points[:,1]<=-.15)
        central_gap=body_y[central]-seat_y[central]
        witness=points[valid][np.argmin(gap)] if len(gap) else None
        records.append({'look':path.name.split('_')[0],'file':path.name,'chair_delta_z_m':dz,
          'paired_rays':int(valid.sum()),'bearing_patch_rays':int(central.sum()),
          'whole_polygon_gap_percentiles_m':summaries(gap),'bearing_patch_gap_percentiles_m':summaries(central_gap),
          'body_underside_bearing_y_percentiles_m':summaries(body_y[central]),
          'seat_support_y_percentiles_m':summaries(seat_y[valid]),
          'penetration_more_than_2mm_rays':int((gap<-.002).sum()),
          'contact_within_5mm_rays':int((np.abs(gap)<=.005).sum()),
          'lowest_gap_xz':witness.tolist() if witness is not None else None})
result={'status':'native_typing_v1_surface_witnesses_foot_residual_failed_not_acceptance',
 'coordinate_frame':'exact avatar root local metres; chair imported B2 world export converted by its immutable anchor (-7.5,0,3)',
 'chair_support_triangle_count':len(seat_faces),'ray_grid_spacing_m':.008,
 'limitations':['Body surfaces sampled at 8 exported phases per look/LOD, not all 241 dense time samples.','Vertical witnesses do not replace full triangle/collision contact, back clearance, or native rendered recheck.','Posterior bearing region x±.145 z[-.42,-.15] is geometric selection, not verified pressure or body-part weight mapping.','No actor, mesh, clip, furniture or anchor has been altered.'],
 'native_input_sha256':input_hashes,'records':records}
(HERE/'typing-support-v1-provisional.json').write_text(json.dumps(result,indent=2)+'\n')
for look in ['Snow','Rain']:
 for dz in [-.15,-.20,-.25]:
  rr=[r for r in records if r['look']==look and r['chair_delta_z_m']==dz]
  print(look,'dz',dz,'cases',len(rr),'min_gap_range',[min(r['bearing_patch_gap_percentiles_m']['0'] for r in rr),max(r['bearing_patch_gap_percentiles_m']['0'] for r in rr)],'p05_range',[min(r['bearing_patch_gap_percentiles_m']['5'] for r in rr),max(r['bearing_patch_gap_percentiles_m']['5'] for r in rr)],'seat_y',rr[0]['seat_support_y_percentiles_m']['100'])
