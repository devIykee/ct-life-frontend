import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[1];bpy.ops.wm.open_mainfile(filepath=str(root/'unity-snow/calf-quality-candidate/atlas-debug.blend'),use_scripts=False);m=bpy.data.objects['SnowAdult_CreatorMesh'].data;m.calc_loop_triangles()
for i in [15374,15375,15376,15400,15401]:
 t=m.loop_triangles[i];p=m.polygons[t.polygon_index];print('UV_TRI',i,'POLY',p.index,'UV',[list(m.uv_layers['AtlasUV'].data[j].uv) for j in t.loops],'VERTS',[list(m.vertices[j].co) for j in t.vertices],'SOURCEUV',[list(m.uv_layers['SourceUV'].data[j].uv) for j in t.loops],'POLYLOOPS',list(p.loop_indices),flush=True)
