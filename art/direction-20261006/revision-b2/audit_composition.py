"""CPU-only source geometry/projection audit; not Unity navigation, lighting or visual acceptance."""
import hashlib,itertools,json,math,re
from pathlib import Path
HERE=Path(__file__).resolve().parent; ROOT=HERE.parents[2]
source=(HERE/'CityDirectionCandidateB2.cs').read_text()
manifest=json.loads((ROOT/'art/processed/environment/manifest.json').read_text())
measured=json.loads((HERE/'source-bounds.json').read_text())
snapshot=json.loads((ROOT/'client/Assets/Resources/Contract/config_snapshot.json').read_text())
spots=snapshot['ids']['spots']['venues']['timeline_square']['spots']
pitch,yaw=map(math.radians,(27,-12));camera=(1.65,5.90,-21.78);aspect=2670/1200;tangent=math.tan(math.radians(21))
right=(math.cos(yaw),0,-math.sin(yaw));up=(math.sin(yaw)*math.sin(pitch),math.cos(pitch),math.cos(yaw)*math.sin(pitch));forward=(math.sin(yaw)*math.cos(pitch),-math.sin(pitch),math.cos(yaw)*math.cos(pitch))
def project(v):
 d=[v[i]-camera[i] for i in range(3)];q=[sum(d[i]*axis[i] for i in range(3)) for axis in (right,up,forward)]
 return [.5+.5*q[0]/q[2]/tangent/aspect,.5+.5*q[1]/q[2]/tangent]
def transform(v,x,y,z,angle=0,scale=1):
 a=math.radians(angle);return [x+scale*(v[0]*math.cos(a)+v[2]*math.sin(a)),y+scale*v[1],z+scale*(-v[0]*math.sin(a)+v[2]*math.cos(a))]
def bounds(points):return [[min(p[i] for p in points),max(p[i] for p in points)] for i in range(len(points[0]))]
def asset_corners(asset):
 if asset in measured:
  r=measured[asset];lo,hi=r['blender_min'],r['blender_max'];return list(itertools.product((lo[0],hi[0]),(lo[2],hi[2]),(-hi[1],-lo[1])))
 s=manifest[asset]['size_m'];return list(itertools.product((-s[0]/2,s[0]/2),(0,s[2]),(-s[1]/2,s[1]/2)))
rows=[];solids=[]
def add(asset,x,z,angle=0,y=0,scale=1,solid=False):
 pts=[transform(v,x,y,z,angle,scale) for v in asset_corners(asset)];b=bounds(pts)
 row={'id':asset,'position':[x,y,z],'yaw':angle,'scale':scale,'world_aabb':b,'projected_aabb':bounds([project(v) for v in pts]),'bounds_source':'Blender imported LOD0' if asset in measured else 'manifest size, centered footprint assumption','solid':solid}
 rows.append(row)
 if solid:solids.append((asset,b))
num=r'(-?(?:\d+(?:\.\d+)?|\.\d+)f?)'
for m in re.finditer(r'Add\("([^\"]+)",\s*'+num+r',\s*'+num+r'([^;]*?)\);',source):
 asset,x,z,options=m.groups()
 if asset not in manifest:continue
 kw={k:float(v.rstrip('f')) for k,v in re.findall(r'(yaw|y|scale):\s*'+num,options)}
 add(asset,float(x.rstrip('f')),float(z.rstrip('f')),angle=kw.get('yaw',0),y=kw.get('y',0),scale=kw.get('scale',1),solid='solid: true' in options)
for x,z,angle in re.findall(r'CafeTable\('+num+r',\s*'+num+r',\s*'+num+r'\);',source):
 x,z,angle=[float(v.rstrip('f')) for v in (x,z,angle)]
 add('home_table_roundsmall',x,z,angle=angle,scale=.85,solid=True)
 add('home_chair_1',x-.91,z,angle=90+angle,scale=1.08,solid=True)
 add('home_chair_1',x+.91,z,angle=-90+angle,scale=1.08,solid=True)
# The moved baseline props/obstacles are explicit; new full garden source shapes are kept out of the seat corridor.
for x,z in [(-15.7,-11.3),(12.2,-8.5),(-13.8,1.7),(14,3),(-8.2,11.4),(8.5,11.6),(-18.5,11.4),(18.6,11.4)]:solids.append(('garden',[[x-1.05,x+1.05],[0,2],[z-1.05,z+1.05]]))
solids.extend([('fountain',[[-2.25,2.25],[0,2],[5.25,9.75]]),('ad_kiosk',[[-2.55,-1.05],[0,2],[-4.2,-3.6]]),('crypto_atm',[[1.8,2.6],[0,2],[-4.35,-3.45]])])
clearances=[]
for sp in spots:
 candidates=[]
 for name,b in solids:
  dx=max(b[0][0]-sp['x'],sp['x']-b[0][1],0);dz=max(b[2][0]-sp['z'],sp['z']-b[2][1],0)
  candidates.append((math.hypot(dx,dz),name))
 d,n=min(candidates);clearances.append({'spot':sp['id'],'nearest_solid':n,'distance_to_solid_xz_m':d})
assert all(r['distance_to_solid_xz_m']>.65 for r in clearances),clearances
fascias=[]
for name,x,y,z,angle,width,screen in re.findall(r'Fascia\("([^\"]+)",\s*'+num+r',\s*'+num+r',\s*'+num+r',\s*'+num+r',\s*'+num+r',\s*"([^\"]+)"\)',source):
 x,y,z,angle,width=[float(v.rstrip('f')) for v in (x,y,z,angle,width)]
 pts=[transform(v,x,y,z,angle) for v in itertools.product((-width/2,width/2),(0,width/6),(-.081,))]
 pv=bounds([project(v) for v in pts]);fascias.append({'name':name,'position':[x,y,z],'yaw':angle,'width_m':width,'height_m':width/6,'screen_texture':screen,'projected_aabb':pv,'projected_width_px':(pv[0][1]-pv[0][0])*2670,'projected_height_px':(pv[1][1]-pv[1][0])*1200})
 assert all(0<a<b<1 for a,b in pv),fascias[-1]
# Conservative masks read from v7's 2670x1200 screenshot, normalized from lower left.
masks={'left_header_and_actions':[[.015,.155],[.702,.980]],'cash':[[.868,.985],[.863,.980]],'self_care':[[.889,.985],[.751,.870]],'bottom_actions':[[.746,.985],[.035,.195]]}
for fascia in fascias:
 b=fascia['projected_aabb'];fascia['overlapping_hud_masks']=[name for name,m in masks.items() if all(b[i][0]<m[i][1] and b[i][1]>m[i][0] for i in range(2))]
 assert not fascia['overlapping_hud_masks'],fascia
def segment_aabb_hit(start,end,b):
 low,high=0.,1.
 for axis in range(3):
  direction=end[axis]-start[axis]
  if abs(direction)<1e-8:
   if not b[axis][0]<=start[axis]<=b[axis][1]:return False
  else:
   a=(b[axis][0]-start[axis])/direction;c=(b[axis][1]-start[axis])/direction
   low=max(low,min(a,c));high=min(high,max(a,c))
   if low>high:return False
 return low<.999 and high>0
for fascia in fascias:
 x,y,z=fascia['position'];w=fascia['width_m'];h=fascia['height_m']
 targets=[transform(v,x,y,z,fascia['yaw']) for v in itertools.product((-w/2,0,w/2),(0,h/2,h),(-.081,))]
 hits=[]
 for row in rows:
  if not row['id'].startswith('shop_'):continue
  covered=sum(segment_aabb_hit(camera,v,row['world_aabb']) for v in targets)
  if covered:hits.append({'building':row['id'],'sample_rays_blocked':covered,'of':len(targets)})
 fascia['new_building_aabb_occlusion_samples']=hits
 assert not hits,(fascia['name'],hits)
out={'status':'cpu_geometry_and_projection_only_not_native_acceptance','camera_from_log':'/tmp/ct-direction-paired-v4.log','camera':camera,'rotation':[27,-12,0],'fov_y':42,'viewport_px':[2670,1200],'source_sha256':hashlib.sha256(source.encode()).hexdigest(),'coordinate_basis':'Blender x,z,-y converted to Unity x,y,z; import bounds symmetric about ground origin for measured assets. Camera position rounded to log precision.','fascias':fascias,'hud_masks':masks,'placed_geometry':rows,'anchor_clearances':clearances,'limitations':['Projected AABBs bound geometry; they do not establish visible pixel area or exclude silhouette/texture occlusion.','Geometry audit covers new/moved solid exclusions. Existing canonical bench geometry and animated avatars require native seating/route review.','Exact authored anchors are not moved. CPU separation does not substitute for NavMesh stage path validation.','Building upper floors intentionally extend beyond close camera; the review claim is visible ground-floor identity, not complete skyline visibility.']}
(HERE/'composition-audit.json').write_text(json.dumps(out,indent=2)+'\n')
print('CT_B2_CPU_AUDIT',len(rows),'placed source rows;',len(clearances),'anchors clear; minimum clearance',round(min(x['distance_to_solid_xz_m'] for x in clearances),3),'m')
for row in fascias:print(row['name'],row['projected_aabb'],round(row['projected_width_px']),round(row['projected_height_px']),row['overlapping_hud_masks'])
