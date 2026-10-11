import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[1];out=root/'unity-snow/calf-quality-candidate'
for kind in ['blend','fbx']:
 if kind=='blend':bpy.ops.wm.open_mainfile(filepath=str(out/'SnowAdult_LODs.blend'),use_scripts=False);mesh=bpy.data.objects['SnowAdult_LOD2Mesh']
 else:bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(out/'exports/SnowAdult_LOD2.fbx'),use_anim=False);mesh=next(o for o in bpy.data.objects if o.type=='MESH')
 mesh.data.calc_loop_triangles();bad=[]
 for v in mesh.data.vertices:
  groups=[(mesh.vertex_groups[g.group].name,g.weight) for g in v.groups]
  if len([w for n,w in groups if w>1e-5])>4 or abs(sum(w for n,w in groups)-1)>1e-5:bad.append({'i':v.index,'co':list(v.co),'weights':groups})
 print('FAILDETAIL',kind,'verts',len(mesh.data.vertices),'tri',len(mesh.data.loop_triangles),'badweights',len(bad),'samples',bad[:8],'mods',[(m.name,m.type) for m in mesh.modifiers],flush=True)
