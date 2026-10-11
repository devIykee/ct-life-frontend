"""Find exact remaining simplified UV folds, protect nearby original vertices next pass."""
import bpy,sys,json,hashlib,numpy as np
from pathlib import Path
from collections import defaultdict
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));from snow_atlas_validation import validate_atlas
out=root/'vincent-adult-production';bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_AtlasHighReference.blend'),use_scripts=False);source=bpy.data.objects['VincentAdult_CreatorMesh'];source.data.calc_loop_triangles();adj=defaultdict(set)
for t in source.data.loop_triangles:
 for v in t.vertices:adj[v].update(t.vertices)
# Bake clearing makes unused RGB black but alpha opaque; alpha is not a valid atlas-coverage test.
image=bpy.data.images.load(str(out/'exports'/'VincentAdult_BaseColor.png'),check_existing=False);pixels=np.array(image.pixels[:],dtype=np.float32).reshape(2048,2048,4);valid=pixels[:,:,:3].max(axis=2)>1e-6

def raster(me,t):
 uv=me.uv_layers['AtlasUV'].data;p=np.array([uv[i].uv[:] for i in t.loops])*2048;lo=np.maximum(np.floor(p.min(0)).astype(int),0);hi=np.minimum(np.ceil(p.max(0)).astype(int),2047)
 if np.any(hi<lo):return None
 v0=p[1]-p[0];v1=p[2]-p[0];den=v0[0]*v1[1]-v1[0]*v0[1]
 if abs(den)<1e-9:return None
 x,y=np.meshgrid(np.arange(lo[0],hi[0]+1)+.5,np.arange(lo[1],hi[1]+1)+.5);dx=x-p[0,0];dy=y-p[0,1];a=(dx*v1[1]-dy*v1[0])/den;b=(v0[0]*dy-v0[1]*dx)/den
 return lo,hi,(a>1e-4)&(b>1e-4)&(a+b<1-1e-4)
# Preserve intentionally black original surfaces, e.g. pupils, independently of padding.
for t in source.data.loop_triangles:
 sample=raster(source.data,t)
 if sample is None:continue
 lo,hi,inside=sample;region=valid[lo[1]:hi[1]+1,lo[0]:hi[0]+1];region[inside]=True
bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_Tiers.blend'),use_scripts=False);p=out/'uv-protection-overrides.json';overrides=json.loads(p.read_text()) if p.exists() else {};report={}
for tier in ['Creator','LOD1','LOD2']:
 mesh=bpy.data.objects[f'VincentAdult_{tier}Mesh'];audit=validate_atlas(mesh.data);(out/f'{tier.lower()}-uv-validation.json').write_text(json.dumps(audit,indent=2)+'\n');report[tier]=audit['overlapping_interior_samples'];bad={int(x) for pair in audit['overlapping_triangle_pairs'] for x in pair.split('-')};blank_samples=0;blank_triangles=[]
 for ti,t in enumerate(mesh.data.loop_triangles):
  sample=raster(mesh.data,t)
  if sample is None:continue
  lo,hi,inside=sample;missing=inside&~valid[lo[1]:hi[1]+1,lo[0]:hi[0]+1]
  if missing.any():bad.add(ti);blank_samples+=int(missing.sum());blank_triangles.append(ti)
 report[tier]={'overlap_samples':audit['overlapping_interior_samples'],'blank_atlas_samples':blank_samples};audit['blank_atlas_samples']=blank_samples;audit['blank_atlas_triangles']=blank_triangles;(out/f'{tier.lower()}-uv-validation.json').write_text(json.dumps(audit,indent=2)+'\n')
 original=mesh.data.attributes['source_vertex_index'].data;bad_original={original[v].value for ti in bad for v in mesh.data.loop_triangles[ti].vertices};expanded=bad_original|{j for v in bad_original for j in adj[v]};previous=set(overrides.get(tier,[]));overrides[tier]=sorted(previous|expanded);print('UV_FOLD_LOCAL_GATE',tier,report[tier],'prior_protection',len(previous),'new_protection',len(set(overrides[tier])-previous),flush=True)
p.write_text(json.dumps(overrides,indent=2)+'\n');(out/'tier-uv-gate.json').write_text(json.dumps({'all_pass':not any(any(v.values()) for v in report.values()),'samples':report,'fbx_sha256':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in (out/'exports').glob('*.fbx')},'blend_sha256':hashlib.sha256((out/'VincentAdult_Tiers.blend').read_bytes()).hexdigest()},indent=2)+'\n')
