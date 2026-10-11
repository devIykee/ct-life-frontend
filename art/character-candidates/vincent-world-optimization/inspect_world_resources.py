"""Reloaded texture/material/geometry resource audit for isolated Vincent world tiers."""
import bpy,json,numpy as np,hashlib,math
from pathlib import Path
out=Path(__file__).resolve().parent;bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_WorldAtlas.blend'),use_scripts=False);report={'scope':'Source-side estimates. Unity can split vertices differently; actual device resident memory/performance remains unmeasured.','tiers':{}}
def astc6(n):
 total=0
 while True:
  total+=math.ceil(n/6)**2*16
  if n==1:return total
  n=max(1,n//2)
for tier in ['WorldNear','WorldFar']:
 o=bpy.data.objects['VincentAdult_'+tier+'Mesh'];me=o.data;me.calc_loop_triangles();uv=me.uv_layers.active.data;unique={(int(l.vertex_index),*[round(float(x),6) for x in uv[l.index].uv],*[round(float(x),6) for x in me.corner_normals[l.index].vector]) for l in me.loops};textures={}
 for label in ['BaseColor','Normal','MetallicSmoothness']:
  im=me.materials[0].node_tree.nodes[label].image;p=Path(bpy.path.abspath(im.filepath));actual=np.array(im.pixels[:],dtype=np.float32).reshape(im.size[1],im.size[0],4);expected=out/'exports'/f'VincentAdult_{tier}_{label}.png';textures[label]={'image_source':im.source,'path':str(p.relative_to(out)),'exact_export_file':p.resolve()==expected.resolve(),'size':list(im.size),'color_space':im.colorspace_settings.name,'loaded_max_rgba':actual.max(axis=(0,1)).tolist(),'loaded_mean_rgba':actual.mean(axis=(0,1)).tolist(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'png_bytes':p.stat().st_size,'estimated_astc6x6_full_mips_bytes':astc6(im.size[0])};assert im.source=='FILE' and p.resolve()==expected.resolve() and float(actual[:,:,:3].max())>.1
 triangles=len(me.loop_triangles);vertex_est=len(unique);morphcount=5 if tier=='WorldNear' else 0;report['tiers'][tier]={'triangles':triangles,'source_vertices':len(me.vertices),'split_vertex_estimate_uv_normal':vertex_est,'max_weight_count':max(len(v.groups) for v in me.vertices),'material_count':len(o.material_slots),'bone_count':len(bpy.data.objects['VincentAdult_Rig'].data.bones),'uv_layers':[u.name for u in me.uv_layers],'morph_count':morphcount,'estimated_vertex_index_bytes_80byte_vertex_16bit_indices':vertex_est*80+triangles*3*2,'estimated_dense_morph_bytes_36bytes_per_shape_vertex':vertex_est*morphcount*36,'textures':textures,'estimated_three_map_astc6x6_full_mips_bytes':sum(t['estimated_astc6x6_full_mips_bytes'] for t in textures.values())}
report['all_pass']=all(t['material_count']==1 and t['uv_layers']==['AtlasUV'] and all(x['exact_export_file'] for x in t['textures'].values()) for t in report['tiers'].values());(out/'world-resource-audit.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2),flush=True)
