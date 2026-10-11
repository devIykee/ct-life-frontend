import bpy,json
from pathlib import Path
r=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(r/'raw/rain/Rain v3.3/rain_v3.2.blend'),use_scripts=False)
a=bpy.data.objects['RIG-rain'];bpy.context.view_layer.update();d={}
for n in ['DEF-Head','DEF-Neck','DEF-Hair_Ponytail1','DEF-Hair_Ponytail2','DEF-Hair_Ponytail3','DEF-Hair_Ponytail4','DEF-Scarf1','DEF-Scarf2','DEF-Scarf3']:
 b=a.data.bones[n];p=a.pose.bones[n];d[n]={'rest_head':list(b.head_local),'rest_tail':list(b.tail_local),'pose_head':list(p.head),'pose_tail':list(p.tail),'matrix_delta':max(abs(b.matrix_local[i][j]-p.matrix[i][j]) for i in range(4) for j in range(4))}
(r/'rain-adult-production/native-hair-pose.json').write_text(json.dumps(d,indent=2));print(json.dumps(d,indent=2))
