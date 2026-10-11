"""Validate both quality tiers, including hand surfaces and two posed FBX round trips."""
import bpy,json,math,sys
from pathlib import Path
from mathutils import kdtree
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
out=root/'snow-hand-quality';pose_defs=json.loads((out/'gesture-poses.json').read_text())
def mesh_data(ob):
 ob.data.calc_loop_triangles();return {'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles),'max_influences':max(sum(g.weight>1e-5 for g in v.groups) for v in ob.data.vertices),'unweighted_vertices':sum(not any(g.weight>1e-5 for g in v.groups) for v in ob.data.vertices),'max_weight_error':max(abs(1-sum(g.weight for g in v.groups)) for v in ob.data.vertices),'morphs':[k.name for k in ob.data.shape_keys.key_blocks if k.name!='Basis'] if ob.data.shape_keys else [],'materials':len(ob.material_slots)}
def points(ob):
 ev=ob.evaluated_get(bpy.context.evaluated_depsgraph_get());return [ob.matrix_world@v.co for v in ev.data.vertices]
def distances(source,actual):
 tree=kdtree.KDTree(len(source))
 for i,p in enumerate(source):tree.insert(p,i)
 tree.balance();ds=[tree.find(p)[2] for p in actual];return {'max_delta_m':max(ds),'rms_delta_m':math.sqrt(sum(d*d for d in ds)/len(ds))}
def loop_surface(ob):
 uv=ob.data.uv_layers.active.data
 return {(tuple(round(x,7) for x in ob.data.vertices[l.vertex_index].co),tuple(round(x,7) for x in uv[l.index].uv)) for l in ob.data.loops}
results={}
for tier in ['LOD0','CREATOR']:
 bpy.ops.wm.open_mainfile(filepath=str(out/'snow_adult_candidate.blend'),use_scripts=False);rig=bpy.data.objects['SnowAdult_Rig'];poses.reset(rig)
 obs=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('Adult_'+tier+'_')]
 for ob in obs:ob.hide_viewport=False;ob.hide_set(False)
 bpy.context.view_layer.update();source={ob.name:mesh_data(ob) for ob in obs};source_points={'Tpose':{ob.name:points(ob) for ob in obs}}
 bones={b.name for b in rig.data.bones};digit_names=[f'{d}{j}.{side}' for side in ['L','R'] for d in poses.DIGITS for j in range(1,4)]
 body=bpy.data.objects['Adult_'+tier+'_body'];group_ids={g.name:g.index for g in body.vertex_groups}
 counts={n:sum(any(g.group==group_ids[n] and g.weight>1e-5 for g in v.groups) for v in body.data.vertices) for n in digit_names}
 reference=bpy.data.objects['Adult_REF_body'];a=loop_surface(reference);b=loop_surface(body)
 surface={'reference_triangles':mesh_data(reference)['triangles'],'tier_triangles':source[body.name]['triangles'],'all_position_uv_pairs_from_reference':b<=a,'loop_pairs_tier':len(b),'loop_pairs_reference':len(a),'source_exposed_arm_hand_surface_preserved':b==a}
 for name,rotations in pose_defs.items():
  poses.apply_world_rotations(rig,rotations);bpy.context.view_layer.update();source_points[name]={ob.name:points(ob) for ob in obs}
 bpy.ops.wm.read_factory_settings(use_empty=True);path=out/('snow_'+tier.lower()+'_hands_candidate.fbx');bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
 irig=next(o for o in bpy.data.objects if o.type=='ARMATURE');iobs=[o for o in bpy.data.objects if o.type=='MESH'];imported={o.name:mesh_data(o) for o in iobs};checks={
 '55_bones':len(irig.data.bones)==55,'bone_names_retained':bones=={b.name for b in irig.data.bones},'10_mesh_parts':len(iobs)==10,
 'triangles_retained':all(imported[n]['triangles']==s['triangles'] for n,s in source.items()),'morphs_retained':all(imported[n]['morphs']==s['morphs'] for n,s in source.items()),
 'max_four_influences':all(x['max_influences']<=4 for x in imported.values()),'all_vertices_weighted':all(x['unweighted_vertices']==0 for x in imported.values()),'weights_normalized':all(x['max_weight_error']<1e-5 for x in imported.values()),
 'all_30_digit_bones_weighted':all(c>0 for c in counts.values()),'hand_geometry_uv_preserved':surface['source_exposed_arm_hand_surface_preserved']}
 deltas={}
 for name in ['Tpose',*pose_defs]:
  if name=='Tpose':poses.reset(irig)
  else:poses.apply_world_rotations(irig,pose_defs[name])
  bpy.context.view_layer.update();deltas[name]={o.name:distances(source_points[name][o.name],points(o)) for o in iobs}
  checks[name+'_within_1e_5m']=all(x['max_delta_m']<1e-5 for x in deltas[name].values())
 results[tier]={'fbx_bytes':path.stat().st_size,'source_meshes':source,'imported_meshes':imported,'digit_weight_vertex_counts':counts,'hand_surface':surface,'posed_geometry_deltas':deltas,'checks':checks}
 print(tier,json.dumps(checks))
(out/'export-validation.json').write_text(json.dumps({'scope':'Blender FBX round-trip in rest/phone-grip/wave poses. No Unity import or device acceptance. Diagnostic phone excluded from avatar FBX.','tiers':results},indent=2))
