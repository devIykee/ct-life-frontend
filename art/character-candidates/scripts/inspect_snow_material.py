import bpy
from pathlib import Path
p=Path(__file__).resolve().parents[1];bpy.ops.wm.open_mainfile(filepath=str(p/'snow-adult-proof/snow_adult_candidate.blend'),use_scripts=False)
o=bpy.data.objects['Adult_LOD0_body']
for m in o.data.materials:
 print('MAT',m.name)
 for n in m.node_tree.nodes:
  if n.type=='TEX_IMAGE': print(n.name,n.image.name if n.image else None)
 for l in m.node_tree.links:print(l.from_node.name,l.from_socket.name,'->',l.to_node.name,l.to_socket.name)
d=o.modifiers.new('test','DECIMATE')
for n in ['vertex_group','vertex_group_factor','invert_vertex_group']:print(n,d.bl_rna.properties[n].description)
