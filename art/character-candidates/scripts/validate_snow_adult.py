"""Read-only FBX round-trip and mesh audit for the single Snow proof."""
import bpy,json,math,bmesh
from pathlib import Path
from mathutils import Vector,kdtree
root=Path(__file__).resolve().parents[1];out=root/'snow-adult-proof'
bpy.ops.wm.open_mainfile(filepath=str(out/'snow_adult_candidate.blend'),use_scripts=False)
def data(ob):
 ob.data.calc_loop_triangles()
 b=bmesh.new();b.from_mesh(ob.data)
 result={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles),'max_influences':max(sum(g.weight>1e-5 for g in v.groups) for v in ob.data.vertices),'unweighted_vertices':sum(not any(g.weight>1e-5 for g in v.groups) for v in ob.data.vertices),'weight_sum_max_error':max(abs(1-sum(g.weight for g in v.groups if g.weight>1e-5)) for v in ob.data.vertices),'boundary_edges':sum(e.is_boundary for e in b.edges),'nonmanifold_edges':sum(not e.is_manifold for e in b.edges),'loose_vertices':sum(not v.link_faces for v in b.verts),'morphs':[k.name for k in ob.data.shape_keys.key_blocks] if ob.data.shape_keys else [],'materials':len(ob.material_slots)}
 b.free();return result
source={};points={};audit={}
for ob in bpy.data.objects:
 if ob.name.startswith('Adult_') and ob.type=='MESH':
  audit[ob.name]=data(ob)
  if ob.name.startswith('Adult_LOD0_'):
   source[ob.name]=audit[ob.name];points[ob.name]=[ob.matrix_world@v.co for v in ob.data.vertices]
source_bones={b.name for b in bpy.data.objects['SnowAdult_Rig'].data.bones}
# Armature's object transform remains identity in the saved T pose.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(out/'snow_adult_lod0_candidate.fbx'),use_anim=False)
result={'source_audit':audit,'roundtrip_meshes':{},'fbx_bytes':(out/'snow_adult_lod0_candidate.fbx').stat().st_size,'bone_count':0,'missing_bones':[],'validation_scope':'Blender FBX export/import; Unity import, material equivalence and device performance untested.'}
for ob in bpy.data.objects:
 if ob.type=='ARMATURE':
  result['bone_count']=len(ob.data.bones);result['missing_bones']=sorted(source_bones-{b.name for b in ob.data.bones})
 if ob.type!='MESH':continue
 r=data(ob);r['armature_modifiers']=[m.object.name for m in ob.modifiers if m.type=='ARMATURE' and m.object]
 if ob.name in points:
  tree=kdtree.KDTree(len(points[ob.name]))
  for i,p in enumerate(points[ob.name]):tree.insert(p,i)
  tree.balance();dist=[tree.find(ob.matrix_world@v.co)[2] for v in ob.data.vertices]
  r['nearest_rest_vertex_max_delta_m']=max(dist);r['nearest_rest_vertex_rms_delta_m']=math.sqrt(sum(d*d for d in dist)/len(dist))
  r['triangles_match']=r['triangles']==source[ob.name]['triangles']
  r['morphs_match']=[k for k in r['morphs'] if k!='Basis']==[k for k in source[ob.name]['morphs'] if k!='Basis']
 result['roundtrip_meshes'][ob.name]=r
result['checks']={'27_bones':result['bone_count']==27,'all_bones_retained':not result['missing_bones'],'10_mesh_parts':len(result['roundtrip_meshes'])==10,'triangles_retained':all(r.get('triangles_match') for r in result['roundtrip_meshes'].values()),'rest_geometry_within_1e_5m':all(r.get('nearest_rest_vertex_max_delta_m',1)<1e-5 for r in result['roundtrip_meshes'].values()),'all_vertices_weighted':all(r['unweighted_vertices']==0 for r in result['roundtrip_meshes'].values()),'max_four_influences':all(r['max_influences']<=4 for r in result['roundtrip_meshes'].values()),'normalized_weights':all(r['weight_sum_max_error']<1e-5 for r in result['roundtrip_meshes'].values()),'shape_keys_retained':all(r.get('morphs_match') for r in result['roundtrip_meshes'].values())}
(out/'fbx-validation.json').write_text(json.dumps(result,indent=2))
print('FBX_CHECKS',json.dumps(result['checks']))
for k,v in result['source_audit'].items():
 if k.endswith('_body'):print(k,v)
