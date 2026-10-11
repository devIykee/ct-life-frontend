"""Read-only source glove/rig diagnosis. No exports, source modification or Unity work."""
import bpy,json,sys,hashlib
from pathlib import Path
from collections import Counter
from mathutils import Vector
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
import v2_adapters as A
SOURCE=Path('/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab/h26_tactical_red_visor/game_ready_low_poly_character_tactical.glb')
EXPECTED='e25e0ac0520ef669c324af2de88b9cb614dd01eeef8d6ed7db917f5f6f682f20'

def bound(pts):return [[min(p[i] for p in pts),max(p[i] for p in pts)] for i in range(3)] if pts else []
def record(arm,o,label):
 bpy.context.view_layer.update();dep=bpy.context.evaluated_depsgraph_get();ev=o.evaluated_get(dep);me=ev.to_mesh()
 pts=[o.matrix_world@v.co for v in o.data.vertices];evaluated=[ev.matrix_world@v.co for v in me.vertices]
 groups=[]
 for g in o.vertex_groups:
  weighted=[(v.index,w.weight) for v in o.data.vertices for w in v.groups if w.group==g.index and w.weight>1e-5]
  if not weighted:continue
  weight=sum(w for _,w in weighted);center=sum((pts[i]*w for i,w in weighted),Vector())/weight;b=arm.data.bones.get(g.name)
  groups.append({'name':g.name,'cts':A.map_rig_family(arm).get(g.name),'vertex_count':len(weighted),'weight_sum':weight,
    'centroid':list(center),'bounds':bound([pts[i] for i,w in weighted]),
    'rest_head':list(arm.matrix_world@b.head_local) if b else None,'rest_tail':list(arm.matrix_world@b.tail_local) if b else None,
    'pose_head':list(arm.matrix_world@arm.pose.bones[g.name].head) if b else None,
    'rest_matrix':[[float(x) for x in row] for row in arm.matrix_world@b.matrix_local] if b else None})
 adj=[set() for _ in pts]
 for e in o.data.edges:a,b=e.vertices;adj[a].add(b);adj[b].add(a)
 seen=set();components=[]
 for start in range(len(pts)):
  if start in seen:continue
  todo=[start];seen.add(start);ids=[]
  while todo:
   at=todo.pop();ids.append(at)
   for n in adj[at]:
    if n not in seen:seen.add(n);todo.append(n)
  weights=Counter()
  for i in ids:
   for w in o.data.vertices[i].groups:weights[o.vertex_groups[w.group].name]+=w.weight
  components.append({'vertices':len(ids),'vertex_ids':ids,'bounds':bound([pts[i] for i in ids]),'weights':weights})
 parents=[];parent=o
 while parent is not None:
  parents.append({'name':parent.name,'type':parent.type,'matrix_world':[list(row) for row in parent.matrix_world]});parent=parent.parent
 result={'label':label,'name':o.name,'mesh_name':o.data.name,'materials':[m.name for m in o.data.materials],
  'vertices':len(pts),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'bounds':bound(pts),'evaluated_bounds':bound(evaluated),
  'groups':groups,'components':sorted(components,key=lambda x:x['vertices'],reverse=True),'parent_chain':parents,
  'vertices_world':[list(p) for p in pts],'normals_world':[list(o.matrix_world.to_3x3()@v.normal) for v in o.data.vertices]}
 ev.to_mesh_clear();return result

def main():
 dest=Path(sys.argv[sys.argv.index('--')+1]);assert not dest.exists()
 assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==EXPECTED
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(SOURCE))
 arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 gloves=[o for o in bpy.context.scene.objects if o.type=='MESH' and [m.name for m in o.data.materials]==['gloves']]
 if len(gloves)!=1:raise ValueError('Expected exactly one authored glove mesh')
 o=gloves[0];records=[record(arm,o,'source_original_pose')]
 arm.data.pose_position='REST';arm.animation_data_clear();records.append(record(arm,o,'source_rest'))
 mapping=A.map_rig_family(arm)
 bones={n:{'cts':mapping.get(n),'parent':b.parent.name if b.parent else None,'head':list(arm.matrix_world@b.head_local),
  'tail':list(arm.matrix_world@b.tail_local),'matrix':[list(r) for r in arm.matrix_world@b.matrix_local]} for n,b in arm.data.bones.items() if 'hand' in n.lower() or 'forearm' in n.lower()}
 dest.parent.mkdir(parents=True,exist_ok=True);dest.write_text(json.dumps({'scope':'source import only','source_sha256':EXPECTED,'blender':bpy.app.version_string,'records':records,'hand_bones':bones},indent=2)+'\n')
 print('written',dest,flush=True)
if __name__=='__main__':main()
