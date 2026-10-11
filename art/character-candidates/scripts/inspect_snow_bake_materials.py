import bpy,json
from pathlib import Path
r=Path(__file__).resolve().parents[1];bpy.ops.wm.open_mainfile(filepath=str(r/'snow-hand-quality/snow_adult_candidate.blend'),use_scripts=False)
for o in bpy.data.objects:
 if not o.name.startswith('Adult_REF_'):continue
 print('MESH',o.name,'UV',[(x.name,x.active_render) for x in o.data.uv_layers])
 for mat in o.data.materials:
  print('MAT',mat.name,'nodes',[(n.name,n.type,getattr(n,'uv_map','')) for n in mat.node_tree.nodes if n.type not in ['FRAME','REROUTE']])
  print('LINKS',[(l.from_node.name,l.from_socket.name,l.to_node.name,l.to_socket.name) for l in mat.node_tree.links])
