"""Read-only trace of every source weighted group participating in the Rain conversion.
Prepare exact valid-bone replay weights for the three parts containing stale DEF groups.
"""
import bpy,json,re
from pathlib import Path
from collections import defaultdict
root=Path(__file__).resolve().parents[1];out=root/'rain-weight-correction';out.mkdir(exist_ok=True)
base=root/'rain-adult-production';old=json.loads((base/'geometry-manifest.json').read_text())['source_weight_mapping'];keep=[x['part'] for x in json.loads((base/'geometry-manifest.json').read_text())['parts']]
bpy.ops.wm.open_mainfile(filepath=str(root/'raw/rain/Rain v3.3/rain_v3.2.blend'),use_scripts=False);rig=bpy.data.objects['RIG-rain'];rig.data.pose_position='REST'
records=[];weights={};valid_bones={b.name for b in rig.data.bones if b.use_deform}
def normalized(ws):
 ws=sorted([(n,w) for n,w in ws.items() if w>1e-7],key=lambda x:-x[1])[:4];total=sum(w for _,w in ws);assert total>0;return {n:w/total for n,w in ws}
for part in keep:
 ob=bpy.data.objects['GEO-rain-'+part];ob.hide_viewport=False;ob.hide_set(False)
 for m in ob.modifiers:
  if m.type=='SUBSURF':m.show_viewport=False
 bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get();ev=ob.evaluated_get(dg);me=bpy.data.meshes.new_from_object(ev,preserve_all_data_layers=True,depsgraph=dg)
 used=defaultdict(lambda:{'count':0,'sum':0});group_names={g.index:g.name for g in ob.vertex_groups}
 before=[];after=[];affected=[]
 for v in me.vertices:
  ow=defaultdict(float);nw=defaultdict(float);bad=[]
  for g in v.groups:
   n=group_names[g.group]
   if g.weight<=1e-7:continue
   used[n]['count']+=1;used[n]['sum']+=g.weight
   if n in old:
    ow[old[n]]+=g.weight
    if n in valid_bones:nw[old[n]]+=g.weight
    else:bad.append(n)
  if part in ['body','jeans','shoes']:
   before.append(normalized(ow));after.append(normalized(nw));affected.append(bad)
 for n,row in used.items():
  bone=rig.data.bones.get(n);row.update({'part':part,'source_group':n,'source_bone_exists':bone is not None,'source_bone_deforms':bool(bone and bone.use_deform),'old_target':old.get(n),'decision':'ignore_non_bone_mask' if not n.startswith('DEF-') else ('discard_stale_non_bone_group' if n not in valid_bones else ('explicit_part_override' if part.startswith(('hair','gum')) or part in ['eyebrows','eyelashes','tongue','eyes','scarf'] else 'retain_source_deform_mapping'))});records.append(row)
 if part in ['body','jeans','shoes']:weights[part]={'before':before,'after':after,'removed_groups_per_vertex':affected}
 bpy.data.meshes.remove(me)
report={'source':'raw/rain/Rain v3.3/rain_v3.2.blend','method':'Evaluate the original REST geometry with its existing modifiers and subdiv disabled, exactly as the source-quality conversion. Only bones that exist and use_deform=True can contribute to Armature skinning. Mask/side/stale groups are not deform bones.','records':records,'stale_weighted_deform_groups':sorted({r['source_group'] for r in records if r['decision']=='discard_stale_non_bone_group'}),'stale_group_part_occurrences':sum(r['decision']=='discard_stale_non_bone_group' for r in records),'zero_unknown_valid_bone_targets':all(r['old_target'] for r in records if r['source_bone_deforms'] and r['source_group'].startswith('DEF-')),'note':'DEF-Spine4 belongs to an unused body_nomask object and is absent from the shipped evaluated body; not a live shipped Head-weight defect.'}
(out/'weighted-source-trace.json').write_text(json.dumps(report,indent=2));(out/'source-weight-replay.json').write_text(json.dumps(weights,separators=(',',':')));print('RAIN_SOURCE_WEIGHT_AUDIT',len(records),len(report['stale_weighted_deform_groups']),report['zero_unknown_valid_bone_targets'],{p:len(v['after']) for p,v in weights.items()},flush=True)
