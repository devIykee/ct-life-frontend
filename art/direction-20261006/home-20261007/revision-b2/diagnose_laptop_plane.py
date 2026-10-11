"""Read-only existing native mesh decode. Reproduces world-origin classification instability; no Unity/Assets writes."""
import pathlib,re,struct,json,hashlib
import numpy as np
root=pathlib.Path(__file__).resolve().parents[4]
path=root/'client/Assets/Art/Candidates/ParentsBasement20261007A/Lighting/Meshes/447f5f775bc834196aff9479b96d89bc_7203688626665590847_0_uv2.asset'
t=path.read_text();n=int(re.search(r'm_VertexCount: (\d+)',t)[1]);data=bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',t)[1]);channels=[tuple(map(int,c)) for c in re.findall(r'- stream: (\d+)\n\s+offset: (\d+)\n\s+format: (\d+)\n\s+dimension: (\d+)',t)];assert all(c[0]==0 and c[2]==0 for c in channels)
stride=max(o+4*d for s,o,f,d in channels if d)
vs=np.array([struct.unpack_from('<fff',data,i*stride) for i in range(n)],dtype=np.float32)+np.array([3.95,.76,3.65],dtype=np.float32)
ids=np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',t)[1]),dtype='<u2').reshape(-1,3)
def group(relative):
 groups=[]
 for a,b,c in vs[ids]:
  cross=np.cross(b-a,c-a);area=np.float32(np.linalg.norm(cross)*np.float32(.5))
  if area<1e-6:continue
  normal=cross/(area*np.float32(2));d=normal@a
  if -normal[2]<.6 or abs(normal[1])>.75:continue
  g=next((g for g in groups if g['n']@normal>.99999 and (all(abs(g['n']@(v-g['pts'][0]))<.00002 for v in [a,b,c]) if relative else abs(g['d']-d)<.00002)),None)
  if g is None:g={'n':normal,'d':d,'area':np.float32(0),'pts':[]};groups.append(g)
  g['area']+=area;g['pts']+=list([a,b,c])
 result=[]
 for g in groups:
  if not .009<=g['area']<=.14:continue
  pts=np.array(g['pts']);normal=g['n'];right=np.array([1,0,0],dtype=np.float32)-normal*normal[0];right/=np.linalg.norm(right);up=np.array([0,1,0],dtype=np.float32)-normal*normal[1];up/=np.linalg.norm(up);up-=right*(up@right);up/=np.linalg.norm(up)
  rectangle=float(np.ptp(pts@right)*np.ptp(pts@up));error=abs(rectangle-float(g['area']))/rectangle
  result.append({'area_m2':float(g['area']),'rectangle_area_m2':rectangle,'error_fraction':error,'same_rectangle_gate_pass':error<=.035,'max_relative_plane_residual_m':float(max(abs((pts-pts[0])@normal))),'normal':normal.tolist(),'world_plane_distance':float(g['d'])})
 return sorted(result,key=lambda r:-r['area_m2'])
old=group(False);new=group(True)
assert not old[0]['same_rectangle_gate_pass'];assert new[0]['same_rectangle_gate_pass']
result={'scope':'CPU float32 reproduction from exact native A serialized mesh. B2 native build remains pending.','source':str(path.relative_to(root)),'source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'world_translation':[3.95,.76,3.65],'old_origin_distance':old,'new_relative_vertex_residual':new,'unchanged_coplanarity_m':.00002,'unchanged_rectangle_error_fraction_max':.035}
(pathlib.Path(__file__).parent/'laptop-plane-diagnosis.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
