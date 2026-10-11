import pathlib,re,struct,json, numpy as np
base=pathlib.Path(__file__).resolve().parents[4]; m=json.loads((base/'art/processed/environment/manifest.json').read_text()); out=[]
def mesh(f):
 t=f.read_text();n=int(re.search(r'm_VertexCount: (\d+)',t)[1]); data=bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',t)[1]);cs=re.findall(r'- stream: (\d+)\n\s+offset: (\d+)\n\s+format: (\d+)\n\s+dimension: (\d+)',t);cs=[tuple(map(int,c)) for c in cs]; assert all(c[0]==0 and c[2]==0 for c in cs); stride=max(o+4*d for s,o,f,d in cs if d);vs=np.array([struct.unpack_from('<fff',data,i*stride) for i in range(n)]);fmt=int(re.search(r'm_IndexFormat: (\d+)',t)[1]);ids=np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',t)[1]),dtype='<u2' if fmt==0 else '<u4').reshape(-1,3);return vs,ids
for id in ['home_tv_modern','laptop','home_bookshelf']:
 meta=(base/'client/Assets/Art'/ (m[id]['fbx']+'.meta')).read_text();guid=re.search(r'guid: (\w+)' ,meta)[1];files=list((base/'client/Assets/Art/Candidates/ParentsBasement20261007A/Lighting/Meshes').glob(guid+'*'))
 files=[f for f in files if f.suffix=='.asset'];assert len(files)==1,(id,files)
 vs,tri=mesh(files[0]);groups=[]
 for face in vs[tri]:
  a,b,c=face;cross=np.cross(b-a,c-a);area=np.linalg.norm(cross)/2
  if area<1e-6:continue
  normal=cross/(area*2);d=normal@a
  use=normal[1]>.9 if id=='home_bookshelf' else -normal[2]>.6 and abs(normal[1])<.75
  if not use:continue
  g=next((g for g in groups if g['normal']@normal>.99999 and abs(g['distance']-d)<.00002),None)
  if g is None:g={'normal':normal,'distance':float(d),'area':0,'pts':[]};groups.append(g)
  g['area']+=area;g['pts']+=list(face)
 for g in groups:
  pts=np.array(g.pop('pts'));g['bounds']=[pts.min(axis=0).tolist(),pts.max(axis=0).tolist()];g['normal']=g['normal'].tolist();g['area']=float(g['area'])
 out.append({'id':id,'mesh':str(files[0].relative_to(base)),'bounds':[vs.min(axis=0).tolist(),vs.max(axis=0).tolist()],'planes':sorted(groups,key=lambda g:-g['area'])})
print(json.dumps(out,indent=2));(base/'art/direction-20261006/home-20261007/revision-b/native-source-plane-preflight.json').write_text(json.dumps({'scope':'Read-only decoded existing native A mesh assets. No B import or render.','objects':out},indent=2)+'\n')
