"""Source-only cafe geometry audit; no Unity scene, NavMesh, or image acceptance is implied."""
import json,math,itertools,hashlib
from pathlib import Path
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
m=json.loads((ROOT/'art/processed/environment/manifest.json').read_text());measured=json.loads((HERE/'source-bounds.json').read_text())
snapshot=json.loads((ROOT/'client/Assets/Resources/Contract/config_snapshot.json').read_text());spots=snapshot['ids']['spots']['venues']['terminal_cafe']['spots'];solids=[]
def asset_bounds(asset,x,z,yaw=0,scale=1):
 if asset in measured:
  lo,hi=measured[asset]['blender_min'],measured[asset]['blender_max'];corners=itertools.product((lo[0],hi[0]),(lo[2],hi[2]),(-hi[1],-lo[1]))
 else:
  w,d,h=m[asset]['size_m'];corners=itertools.product((-w/2,w/2),(0,h),(-d/2,d/2))
 a=math.radians(yaw);p=[[x+scale*(v[0]*math.cos(a)+v[2]*math.sin(a)),scale*v[1],z+scale*(-v[0]*math.sin(a)+v[2]*math.cos(a))] for v in corners]
 return [[min(v[i] for v in p),max(v[i] for v in p)] for i in range(3)]
def add(asset,x,z,yaw=0,scale=1):solids.append({'id':asset,'bounds':asset_bounds(asset,x,z,yaw,scale),'measured':asset in measured})
def box(name,x,z,w,d):solids.append({'id':name,'bounds':[[x-w/2,x+w/2],[0,2.4],[z-d/2,z+d/2]],'measured':True})
for spot in spots:
 if spot['kind']=='terminal':add('home_desk',spot['x'],spot['z']+.86)
 if spot['kind']=='seat':add('home_table_roundsmall',spot['x'],spot['z']+1.20)
for z in [4.,8.]:
 for x in [-5,5]:add('houseplant_tall',x,z,scale=.85)
for i in range(9):add('home_kitchen_cabinet1',-8.9,-7.15+i*.61,90)
add('home_kitchen_fridge',-11.35,-2.2,90)
box('Backbar return',-10.35,-1.34,3.2,.14)
for x in [-11.83,11.83]:
 box('Reading shelf backing',x,.7,.1,1.65)
 add('houseplant_tall',-10.65 if x<0 else 10.65,7.6,scale=.95)
for side in [-1,1]:
 add('home_couch_medium1',side*5.6,-7,90 if side<0 else -90)
 add('home_table_roundsmall',side*4.12,-7,scale=.70)
 add('home_light_floor1',side*6.5,-8.15)
 add('home_houseplant_1',side*6.65,-5.2,scale=.80)
clear=[]
for spot in spots:
 distances=[]
 for row in solids:
  b=row['bounds'];dx=max(b[0][0]-spot['x'],spot['x']-b[0][1],0);dz=max(b[2][0]-spot['z'],spot['z']-b[2][1],0)
  distances.append((math.hypot(dx,dz),row['id']))
 dist,name=min(distances);clear.append({'spot':spot['id'],'distance_to_nearest_solid_xz_m':dist,'nearest_solid':name})
assert min(r['distance_to_nearest_solid_xz_m'] for r in clear)>.50,clear
pitch,yaw=map(math.radians,(30,-12));distance=10.8;fov=45;fy=.3;fh=1;aspect=2670/1200;t=math.tan(math.radians(fov/2));q=(2*fy-1)*t
ahead=-(q*distance+fh*(math.cos(pitch)+q*math.sin(pitch)))/(math.sin(pitch)-q*math.cos(pitch))
f=(math.sin(yaw)*math.cos(pitch),-math.sin(pitch),math.cos(yaw)*math.cos(pitch));right=(math.cos(yaw),0,-math.sin(yaw));up=(math.sin(yaw)*math.sin(pitch),math.cos(pitch),math.cos(yaw)*math.sin(pitch))
camera=(math.sin(yaw)*ahead-f[0]*distance,fh-f[1]*distance,-8+math.cos(yaw)*ahead-f[2]*distance)
def project(v):
 delta=[v[i]-camera[i] for i in range(3)];dots=[sum(delta[i]*axis[i] for i in range(3)) for axis in (right,up,f)];return [.5+.5*dots[0]/dots[2]/t/aspect,.5+.5*dots[1]/dots[2]/t]
references={k:project(v) for k,v in {'entrance_feet':(0,0,-8),'adult_1_8m_head_proxy':(0,1.8,-8),'left_bistro_table':(-6,.75,-2.8),'right_bistro_table':(6,.75,-2.8),'left_reading_sofa':(-5.6,.896,-7),'right_reading_sofa':(5.6,.896,-7),'coffee_machine':(-8.9,1.2,-5.9),'center':(0,0,0),'pulse_anchor':(0,0,8.5)}.items()}
def hit_segment(bounds,target):
 lo,hi=0.,1.
 for i in range(3):
  delta=target[i]-camera[i]
  if abs(delta)<1e-9:
   if not bounds[i][0]<=camera[i]<=bounds[i][1]:return False
  else:
   enter=(bounds[i][0]-camera[i])/delta;leave=(bounds[i][1]-camera[i])/delta
   if enter>leave:enter,leave=leave,enter
   lo=max(lo,enter);hi=min(hi,leave)
   if lo>hi:return False
 return lo<.999 and hi>0
render_occluders=[r for r in solids if r['id'] not in ['Reading shelf backing','Backbar return']]
render_occluders.append({'id':'Backbar return','bounds':[[-11.95,-8.75],[0,2.9],[-1.41,-1.27]]})
for spot in spots:
 if spot['kind']=='terminal':
  b=asset_bounds('monitor_triple',spot['x'],spot['z']+1.03);b[1]=[v+.76 for v in b[1]]
  render_occluders.append({'id':'monitor at '+spot['id'],'bounds':b})
panels=[('CoffeeIdentity',(0,.48,8.98),5.50,2.40625),('CounterMenu',(-9.63,1.10,-1.44),1.36,1.904)]
panel_rows=[]
for name,(x,y,z),w,h in panels:
 corners=[project((xx,yy,z-.081)) for xx in [x-w/2-.06,x+w/2+.06] for yy in [y-.06,y+h+.06]]
 rect=[[min(p[i] for p in corners),max(p[i] for p in corners)] for i in range(2)]
 samples=[]
 for xx in [x-w*.30,x,x+w*.30]:
  for yy in [y+h*.35,y+h*.65]:
   target=(xx,yy,z-.081);samples.append({'target':target,'aabb_occluders':[r['id'] for r in render_occluders if hit_segment(r['bounds'],target)]})
 panel_rows.append({'name':name,'message_ray_samples':samples,'full_frame_viewport_xy':rect,'entire_frame_in_view':all(lo>=0 and hi<=1 for lo,hi in rect),'estimated_width_native_px':(rect[0][1]-rect[0][0])*2670,'estimated_height_native_px':(rect[1][1]-rect[1][0])*1200})
source=(HERE/'TerminalCafeCandidateB2.cs').read_bytes()
out={'status':'cpu_geometry_check_only_native_review_pending','source_sha256':hashlib.sha256(source).hexdigest(),'measured_source_models':len(measured),'solids':solids,'anchor_clearances':clear,'camera':{'position':camera,'pitch':30,'yaw':-12,'distance':10.8,'fov':45,'feet_viewport_y':.30},'projected_reference_points':references,'adult_proxy_height_px':(references['adult_1_8m_head_proxy'][1]-references['entrance_feet'][1])*1200,'panels':panel_rows,'limitations':['Manual layout audit must be matched to staged receipt; stage still validates native NavMesh paths and exact anchors.','All fixed seats are >0.50 m from furniture solid footprints, but avatar/chair/desk contact requires native animation evidence.','Camera fits the near-entry experience at human scale, not every piece of the full 24x18 room at once.','AABB separation is not evidence of readability, correct material appearance, lightmap quality or live interactions.']}
(HERE/'composition-audit.json').write_text(json.dumps(out,indent=2)+'\n')
print('CAFE_B2_GEOMETRY',len(clear),'anchors; nearest solid',min(r['distance_to_nearest_solid_xz_m'] for r in clear));print('Camera',camera,'adult 1.8m proxy pixel height',out['adult_proxy_height_px']);print(references)
