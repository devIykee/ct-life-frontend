"""Read-only component diagnosis. Output only the explicitly supplied evidence JSON."""
import bpy,json,sys
from collections import Counter
from pathlib import Path
from mathutils import Vector

def bounds(points):
 return [[min(p[k] for p in points),max(p[k] for p in points)] for k in range(3)] if points else []
def inspect(label):
 bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();rows=[]
 for o in bpy.context.scene.objects:
  if o.type!='MESH':continue
  names={g.index:g.name for g in o.vertex_groups};adj=[set() for _ in o.data.vertices]
  for e in o.data.edges:a,b=e.vertices;adj[a].add(b);adj[b].add(a)
  visited=set();comps=[]
  evaluated=o.evaluated_get(deps);mesh=evaluated.to_mesh()
  for i in range(len(adj)):
   if i in visited:continue
   todo=[i];visited.add(i);ids=[]
   while todo:
    at=todo.pop();ids.append(at)
    for n in adj[at]:
     if n not in visited:visited.add(n);todo.append(n)
   if len(ids)<3:continue
   weight=Counter()
   for n in ids:
    for g in o.data.vertices[n].groups:weight[names.get(g.group,'?')]+=g.weight
   comps.append({'vertices':len(ids),'vertex_bounds_world':bounds([o.matrix_world@o.data.vertices[n].co for n in ids]),
                 'evaluated_bounds_world':bounds([evaluated.matrix_world@mesh.vertices[n].co for n in ids]) if len(mesh.vertices)==len(o.data.vertices) else None,
                 'weight_sum_top':weight.most_common(4)})
  rows.append({'object':o.name,'materials':[m.name if m else None for m in o.data.materials],
               'vertices':len(o.data.vertices),'matrix_world':[list(x) for x in o.matrix_world],
               'components':sorted(comps,key=lambda c:c['vertices'],reverse=True)})
  evaluated.to_mesh_clear()
 arms=[]
 for arm in bpy.context.scene.objects:
  if arm.type!='ARMATURE':continue
  arms.append({'object':arm.name,'pose_position':arm.data.pose_position,'head_bones':{b.name:{'rest_head':list(arm.matrix_world@b.head_local),'pose_head':list(arm.matrix_world@arm.pose.bones[b.name].head)} for b in arm.data.bones if 'head' in b.name.lower()}})
 return {'label':label,'meshes':rows,'arms':arms}
args=sys.argv[sys.argv.index('--')+1:];dest=Path(args[0]);assert not dest.exists()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath='/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab/h26_tactical_red_visor/game_ready_low_poly_character_tactical.glb')
records=[inspect('source_import_pose')]
for o in bpy.context.scene.objects:
 if o.type=='ARMATURE':o.data.pose_position='REST';o.animation_data_clear()
records.append(inspect('source_rest_used_by_adapter'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath='/Users/mac/Documents/codes/projects/ct-life/art/cast/v2/export/bases/hero_tactical/parts/hero_tactical__head.fbx')
records.append(inspect('existing_export_head_part'))
dest.write_text(json.dumps({'blender':bpy.app.version_string,'scope':'import only, no files exported','records':records},indent=2)+'\n')
print('WROTE',dest,flush=True)
