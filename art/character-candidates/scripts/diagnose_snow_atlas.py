import bpy,json,numpy as np
from pathlib import Path
root=Path(__file__).resolve().parents[1];out=root/'unity-snow/calf-quality-candidate';out.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(root/'unity-snow/SnowAdult_Unity.blend'),use_scripts=False)
m=bpy.data.objects['SnowAdult_CreatorMesh'].data;m.calc_loop_triangles();uv=m.uv_layers.active.data;ids=m.attributes['source_part_id'].data;size=1024;masks=np.zeros((10,size,size),dtype=bool)
for t in m.loop_triangles:
 p=np.array([uv[i].uv[:] for i in t.loops])*size;lo=np.maximum(np.floor(p.min(0)).astype(int),0);hi=np.minimum(np.ceil(p.max(0)).astype(int),size-1)
 if np.any(hi<lo):continue
 x,y=np.meshgrid(np.arange(lo[0],hi[0]+1)+.5,np.arange(lo[1],hi[1]+1)+.5);v0=p[1]-p[0];v1=p[2]-p[0];den=v0[0]*v1[1]-v1[0]*v0[1]
 if abs(den)<1e-9:continue
 dx=x-p[0,0];dy=y-p[0,1];a=(dx*v1[1]-dy*v1[0])/den;b=(v0[0]*dy-v0[1]*dx)/den;inside=(a>1e-4)&(b>1e-4)&(a+b<1-1e-4)
 masks[ids[t.vertices[0]].value,lo[1]:hi[1]+1,lo[0]:hi[0]+1]|=inside
report={'coverage_pixels':masks.sum((1,2)).tolist(),'cross_part_overlap_pixels':{f'{a}-{b}':int((masks[a]&masks[b]).sum()) for a in range(10) for b in range(a+1,10) if (masks[a]&masks[b]).any()}}
print('ATLAS_OVERLAP',json.dumps(report),flush=True)
bpy.ops.wm.open_mainfile(filepath=str(root/'snow-hand-quality/snow_adult_candidate.blend'),use_scripts=False)
report['source_uv_layers']={o.name:[{'name':l.name,'render':l.active_render,'range':[[min(u.uv[i] for u in l.data),max(u.uv[i] for u in l.data)] for i in range(2)]} for l in o.data.uv_layers] for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('Adult_REF_')}
def tree_info(t):
 return [{'name':n.name,'type':n.type,'uv':getattr(n,'uv_map',None),'image':n.image.name if n.type=='TEX_IMAGE' and n.image else None,'vec_links':[x.from_node.name+':'+x.from_socket.name for x in n.inputs['Vector'].links] if 'Vector' in n.inputs else None,'group':tree_info(n.node_tree) if n.type=='GROUP' and n.node_tree else None} for n in t.nodes if n.type in ['TEX_IMAGE','GROUP','UVMAP','TEX_COORD','NORMAL_MAP']]
report['pants_materials']={s.material.name:tree_info(s.material.node_tree) for s in bpy.data.objects['Adult_REF_pants'].material_slots}
(out/'atlas-diagnosis.json').write_text(json.dumps(report,indent=2));print('DIAGNOSIS_READY',flush=True)
