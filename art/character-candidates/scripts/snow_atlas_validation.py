"""Raster-check UV triangle interiors across the entire atlas before baking.
This catches overlapping UV areas at the stated resolution; it is not a proof against
subpixel overlap. Shared triangle edges are excluded by strict barycentric margins.
"""
import numpy as np

def validate_atlas(mesh,size=2048):
 mesh.calc_loop_triangles();uv=mesh.uv_layers['AtlasUV'].data;ids=mesh.attributes['source_part_id'].data
 owner=np.full((size,size),-1,dtype=np.int32);part=np.full((size,size),-1,dtype=np.int8);overlaps={};overlap_pairs={};degenerate=0
 for ti,t in enumerate(mesh.loop_triangles):
  p=np.array([uv[i].uv[:] for i in t.loops])*size;lo=np.maximum(np.floor(p.min(0)).astype(int),0);hi=np.minimum(np.ceil(p.max(0)).astype(int),size-1)
  if np.any(hi<lo):continue
  v0=p[1]-p[0];v1=p[2]-p[0];den=v0[0]*v1[1]-v1[0]*v0[1]
  if abs(den)<1e-9:degenerate+=1;continue
  x,y=np.meshgrid(np.arange(lo[0],hi[0]+1)+.5,np.arange(lo[1],hi[1]+1)+.5);dx=x-p[0,0];dy=y-p[0,1];a=(dx*v1[1]-dy*v1[0])/den;b=(v0[0]*dy-v0[1]*dx)/den
  inside=(a>1e-4)&(b>1e-4)&(a+b<1-1e-4);reg=owner[lo[1]:hi[1]+1,lo[0]:hi[0]+1];pr=part[lo[1]:hi[1]+1,lo[0]:hi[0]+1];pid=ids[t.vertices[0]].value
  crossed=inside&(reg>=0)
  if crossed.any():
   for previous in np.unique(reg[crossed]):
    key=f'{int(previous)}-{ti}';overlap_pairs[key]=overlap_pairs.get(key,0)+int((crossed&(reg==previous)).sum())
   for prev in np.unique(pr[crossed]):
    key='-'.join(map(str,sorted([int(prev),pid])));overlaps[key]=overlaps.get(key,0)+int((crossed&(pr==prev)).sum())
  reg[inside]=ti;pr[inside]=pid
 uvmin=[min(u.uv[i] for u in uv) for i in range(2)];uvmax=[max(u.uv[i] for u in uv) for i in range(2)]
 return {'raster_resolution':[size,size],'strict_interior_barycentric_margin':.0001,'overlapping_interior_samples':sum(overlaps.values()),'overlap_samples_by_source_part_pair':overlaps,'overlapping_triangle_pairs':overlap_pairs,'coverage_pixels':int((owner>=0).sum()),'coverage_fraction':float((owner>=0).mean()),'degenerate_uv_triangles':degenerate,'uv_min':uvmin,'uv_max':uvmax,'hidden_faces':sum(p.hide for p in mesh.polygons),'hidden_vertices':sum(v.hide for v in mesh.vertices),'limitation':'Raster test checks texel centres, not a mathematical proof against subpixel overlap.'}
