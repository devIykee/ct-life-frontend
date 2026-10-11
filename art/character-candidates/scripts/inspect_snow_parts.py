import bpy,json,bmesh
from pathlib import Path
p=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(p/'snow-adult-proof/snow_adult_candidate.blend'),use_scripts=False)
r={}
for ob in bpy.data.objects:
 if not ob.name.startswith('Adult_REF'):continue
 bm=bmesh.new();bm.from_mesh(ob.data);visited=set();cs=[]
 for v in bm.verts:
  if v in visited:continue
  stack=[v];vs=set()
  while stack:
   x=stack.pop()
   if x in visited:continue
   visited.add(x);vs.add(x);stack.extend(e.other_vert(x) for e in x.link_edges)
  if vs:
   faces=set(f for x in vs for f in x.link_faces)
   cs.append({'verts':len(vs),'faces':len(faces),'bounds':[[round(min(v.co[a] for v in vs),4),round(max(v.co[a] for v in vs),4)] for a in range(3)],'mats':list(set(f.material_index for f in faces))})
 r[ob.name]={'vertices':len(ob.data.vertices),'polys':len(ob.data.polygons),'components':sorted(cs,key=lambda c:-c['verts'])[:20],'boundary_verts':sum(v.is_boundary for v in bm.verts)}
 bm.free()
(p/'snow-adult-proof/component-inspection.json').write_text(json.dumps(r,indent=2))
print(json.dumps(r))
