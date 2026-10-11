import bpy,json
from pathlib import Path
r=Path(__file__).resolve().parents[1];bpy.ops.wm.open_mainfile(filepath=str(r/'snow-hand-quality/snow_adult_candidate.blend'),use_scripts=False)
a={}
for o in bpy.data.objects:
 if not o.name.startswith('Adult_REF_'):continue
 for m in o.data.materials:
  outs=[{'name':n.name,'target':n.target,'active':n.is_active_output,'surface':[(l.from_node.name,l.from_node.type) for l in n.inputs['Surface'].links]} for n in m.node_tree.nodes if n.type=='OUTPUT_MATERIAL']
  a[m.name]={'outputs':outs,'principled':[{'name':n.name,'base':[(l.from_node.name,l.from_socket.name) for l in n.inputs['Base Color'].links],'normal':[(l.from_node.name,l.from_socket.name) for l in n.inputs['Normal'].links]} for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED']}
print(json.dumps(a,indent=2))
(r/'unity-snow/source-material-outputs.json').write_text(json.dumps(a,indent=2))
