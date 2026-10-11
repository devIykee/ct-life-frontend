import bpy,bmesh,json
from pathlib import Path
root=Path(__file__).resolve().parents[1];bpy.ops.wm.open_mainfile(filepath=str(root/'unity-snow/calf-quality-candidate/SnowAdult_Unity.blend'),use_scripts=False);m=bpy.data.objects['SnowAdult_CreatorMesh'].data;bm=bmesh.new();bm.from_mesh(m);ids=bm.verts.layers.int['source_part_id']
for part in [3,4]:
 vs=[v for v in bm.verts if v[ids]==part];boundary=[v for v in vs if v.is_boundary];print('NAPE',part,'verts',len(vs),'boundary',len(boundary),'boundary_bounds',[[min(v.co[i] for v in boundary),max(v.co[i] for v in boundary)] for i in range(3)] if boundary else None,'hair_low_counts',{z:len([v for v in vs if v.co.z<z]) for z in [1.58,1.60,1.62,1.64,1.66,1.68,1.70,1.72]},flush=True)
bm.free()
