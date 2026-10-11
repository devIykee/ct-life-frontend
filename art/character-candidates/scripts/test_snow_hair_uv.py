import bpy,sys,numpy as np,json
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));from snow_atlas_validation import validate_atlas
bpy.ops.wm.open_mainfile(filepath=str(root/'unity-snow/calf-quality-candidate/atlas-debug.blend'),use_scripts=False);m=bpy.data.objects['SnowAdult_CreatorMesh'].data;m.calc_loop_triangles();uv=m.uv_layers['AtlasUV'].data;t=m.loop_triangles[15374];target=uv[t.loops[1]].uv.copy();vid=t.vertices[1];connected=[];neighbors=[]
for poly in m.polygons:
 loops=list(poly.loop_indices)
 for i,li in enumerate(loops):
  if m.loops[li].vertex_index==vid and (uv[li].uv-target).length<1e-7:
   connected.append(li);neighbors.extend([tuple(uv[loops[(i-1)%len(loops)]].uv),tuple(uv[loops[(i+1)%len(loops)]].uv)])
new=np.array(list(set(neighbors))).mean(0);print('HAIR_UV_RELAX',vid,list(target),list(new),'neighbor_count',len(set(neighbors)),flush=True)
for li in connected:uv[li].uv=new
report=validate_atlas(m);print('AFTER',json.dumps(report),flush=True)
