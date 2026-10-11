"""Read native exported meshes. Measures candidates only; does not alter source geometry.

Triangle intersections are surface evidence. Zero intersections do not prove collision-free
volumes, ergonomic comfort or runtime approach. Three mesh phases supplement full-cycle JSON.
"""
from pathlib import Path
import gzip, json
import numpy as np

HERE=Path(__file__).resolve().parent
DATA=HERE/'native-b2-pose-review'

def pieces(path):
    rows=[];v=[];f=[];offset=0
    for line in gzip.open(path,'rt'):
        a=line.split()
        if not a:continue
        if a[0]=='v':
            if f:rows.append((np.array(v),np.array(f)));offset+=len(v);v=[];f=[]
            v.append([float(x) for x in a[1:]])
        elif a[0]=='f':f.append([int(x)-1-offset for x in a[1:]])
    if v:rows.append((np.array(v),np.array(f)))
    return rows

def segment_hits(p,q,triangles):
    d=q-p;e1=triangles[:,1]-triangles[:,0];e2=triangles[:,2]-triangles[:,0]
    h=np.cross(d,e2);a=np.einsum('ij,ij->i',e1,h);valid=np.abs(a)>1e-8
    inv=np.zeros(len(a));inv[valid]=1/a[valid]
    s=p-triangles[:,0];u=inv*np.einsum('ij,ij->i',s,h)
    qv=np.cross(s,e1);vv=inv*(qv@d);t=inv*np.einsum('ij,ij->i',e2,qv)
    return valid & (u>=0)&(vv>=0)&(u+vv<=1)&(t>=0)&(t<=1)

def chair_intersection_count(body,chair,dz):
    bv,bf=body;cv,cf=chair;cv=cv.copy();cv[:,2]+=dz
    bt=bv[bf];ct=cv[cf]
    # Limit to seat/back height: feet may intersect the floor independently.
    ct=ct[(ct.max(axis=1)[:,1]>.32)&(ct.min(axis=1)[:,1]<1.1)]
    lo,hi=cv.min(axis=0)-1e-4,cv.max(axis=0)+1e-4
    bt=bt[((bt.max(axis=1)>=lo).all(axis=1))&((bt.min(axis=1)<=hi).all(axis=1))]
    if not len(bt):return 0
    bmin,bmax=bt.min(axis=1),bt.max(axis=1)
    hits=0
    for c in ct:
        lower,upper=c.min(axis=0)-1e-5,c.max(axis=0)+1e-5
        nearby=bt[((bmax>=lower).all(axis=1))&((bmin<=upper).all(axis=1))]
        if not len(nearby):continue
        if any(segment_hits(c[i],c[(i+1)%3],nearby).any() for i in range(3)):
            hits+=1;continue
        # Reverse direction catches a small body surface passing through a broad chair face.
        a,b,c0=c;e1=b-a;e2=c0-a
        p=nearby[:,[0,1,2]].reshape(-1,3);q=nearby[:,[1,2,0]].reshape(-1,3);d=q-p
        h=np.cross(d,e2);det=h@e1;valid=np.abs(det)>1e-8;inv=np.zeros(len(det));inv[valid]=1/det[valid]
        s=p-a;u=inv*np.einsum('ij,ij->i',s,h);qv=np.cross(s,e1);v=inv*np.einsum('ij,ij->i',d,qv);t=inv*(qv@e2)
        if (valid&(u>=0)&(v>=0)&(u+v<=1)&(t>=0)&(t<=1)).any():hits+=1
    return hits

results=[]
for case in ['snow-social','snow-work','rain-social','rain-work']:
    data=json.loads((DATA/(case+'.json')).read_text());foot=np.array(data['root_world'])
    captures=[]
    for path in sorted(DATA.glob(case+'-*CreatorMesh-*.obj.gz')):
        body,chair,table=pieces(path);body=(body[0]-foot,body[1]);chair=(chair[0]-foot,chair[1]);table=(table[0]-foot,table[1])
        bv=body[0];cv=chair[0];tv=table[0]
        # Geometric forearm/hand envelope is explicitly not a bone-weighted hand selector.
        reach=bv[(bv[:,1]>.58)&(bv[:,1]<.88)&(np.abs(bv[:,0])>.1)&(bv[:,2]>.05)]
        rows=[]
        for shift in [0,-.05,-.10,-.15,-.20,-.25]:
            rows.append({'chair_delta_z_m':shift,'intersecting_chair_seat_back_triangles':chair_intersection_count(body,chair,shift)})
        captures.append({'file':path.name,'chair_variants':rows,
            'chair_bounds_local':[cv.min(axis=0).tolist(),cv.max(axis=0).tolist()],
            'table_bounds_local':[tv.min(axis=0).tolist(),tv.max(axis=0).tolist()],
            'geometric_reach_envelope_max_z_m':float(reach[:,2].max()) if len(reach) else None})
    def values(key):return [s[key] for f in data['frames'] for s in f['skins'] if 'CreatorMesh' in s['skin'] and s[key] is not None]
    results.append({'case':case,'root':foot.tolist(),'full_loop_samples':len(data['frames']),
        'foot_min_y_range_m':[min(values('foot_surface_min_y_m')),max(values('foot_surface_min_y_m'))],
        'torso_back_relative_z_range_m':[min(values('torso_back_min_world_z_m'))-foot[2],max(values('torso_back_min_world_z_m'))-foot[2]],
        'captures':captures})
out={'status':'native_mesh_measurement_not_final_furniture_selection',
     'limitations':['Chair sweep uses three exported native mesh phases per case, not every continuous sample.','Intersection count excludes low chair feet; zero is not a volume/clearance guarantee.','A geometric hand-height envelope is not a bone-weighted hand position.','Feet/pose vertical mismatch remains a motion question; no actor/root/source change is made.'], 'cases':results}
(HERE/'furniture-fit-analysis.json').write_text(json.dumps(out,indent=2)+'\n')
for row in results:
    print(row['case'], 'foot',row['foot_min_y_range_m'])
    for cap in row['captures']:print(cap['file'],[(r['chair_delta_z_m'],r['intersecting_chair_seat_back_triangles']) for r in cap['chair_variants']],'reach',cap['geometric_reach_envelope_max_z_m'])
