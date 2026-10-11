import bpy,json
from pathlib import Path
r=Path(__file__).resolve().parents[1];out=r/'unity-snow';bpy.ops.wm.open_mainfile(filepath=str(out/'SnowAdult_Unity.blend'),use_scripts=False)
o=bpy.data.objects['SnowAdult_CreatorMesh'];m=o.data;m.calc_loop_triangles();ids=m.attributes['source_part_id'].data;parts=json.loads((out/'manifest.json').read_text())['source_parts'];names={r['part_id']:r['part'] for r in parts};result={'parts':parts,'body_regions':{},'head_regions':{}}
for threshold in [.58,.62,.66,.68,.70,.72,.74]:
 result['body_regions'][str(threshold)]=sum(1 for t in m.loop_triangles if names[ids[t.vertices[0]].value]=='body' and max(abs(m.vertices[i].co.x) for i in t.vertices)>threshold)
for z,y in [(1.50,-.05),(1.52,-.065),(1.54,-.075),(1.55,-.075),(1.53,-.085)]:
 result['head_regions'][f'{z},{y}']=sum(1 for t in m.loop_triangles if names[ids[t.vertices[0]].value]=='head' and any(m.vertices[i].co.z>z and m.vertices[i].co.y<y for i in t.vertices))
print(json.dumps(result));(out/'lod-region-inspection.json').write_text(json.dumps(result,indent=2))
