import bpy,json
from pathlib import Path
r=Path(__file__).resolve().parents[1];bpy.ops.wm.open_mainfile(filepath=str(r/'rain-adult-production/RainAdult_SourceQuality.blend'),use_scripts=False)
def tree(t):
 return {'name':t.name,'nodes':[{'name':n.name,'type':n.type,'image':n.image.name if n.type=='TEX_IMAGE' and n.image else None,'inputs':{i.name: {'linked':[(l.from_node.name,l.from_socket.name) for l in i.links],'value':str(getattr(i,'default_value',''))} for i in n.inputs},'group':tree(n.node_tree) if n.type=='GROUP' and n.node_tree else None} for n in t.nodes]}
d={}
for n in ['MAT-rain.eyes','MAT-rain.scarf']:d[n]=tree(bpy.data.materials[n].node_tree)
(r/'rain-adult-production/shader-inspection.json').write_text(json.dumps(d,indent=2));print('SHADERS_DONE')
