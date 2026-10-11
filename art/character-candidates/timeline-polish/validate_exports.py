import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector
p=Path(__file__).resolve().parent
rows=[]
for filename in ['plaza_fountain_polish.fbx','timeline_plaza_surface.fbx']:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.fbx(filepath=str(p/filename))
 for o in bpy.data.objects:
  if o.type!='MESH':continue
  m=o.data;m.calc_loop_triangles();points=[o.matrix_world@v.co for v in m.vertices]
  lo=[min(v[i] for v in points) for i in range(3)];hi=[max(v[i] for v in points) for i in range(3)]
  row={'file':filename,'mesh':o.name,'triangles':len(m.loop_triangles),'slots':len(m.materials),'world_min':lo,'world_max':hi,'degenerate_triangles':sum(1 for t in m.loop_triangles if t.area<1e-10),'uv_loops':len(m.uv_layers.active.data) if m.uv_layers.active else 0,'finite':all(math.isfinite(a) for v in points for a in v)}
  assert row['finite'] and row['degenerate_triangles']==0 and row['uv_loops']==len(m.loops),row
  if filename.startswith('plaza_fountain'):
   assert row['slots']==2 and row['triangles']<= (600 if 'LOD1' in o.name else 1500),row
   assert abs(lo[2])<.0001 and abs(hi[0]-lo[0]-4.8)<.0001,row
  else:assert row['triangles']==42 and row['slots']==1 and abs(lo[2]-.008)<.0001,row
  rows.append(row)
(p/'export-validation.json').write_text(json.dumps({'all_pass':True,'blender_version':bpy.app.version_string,'meshes':rows},indent=2))
print('EXPORTED_MESH_VALIDATION_PASS',rows)
