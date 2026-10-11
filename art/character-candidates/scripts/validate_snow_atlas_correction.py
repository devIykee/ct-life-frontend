import bpy,json,hashlib,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1];out=root/'unity-snow/calf-quality-candidate';sys.path.insert(0,str(root/'scripts'));from snow_atlas_validation import validate_atlas

def snapshot(path):
 bpy.ops.wm.open_mainfile(filepath=str(path),use_scripts=False);m=bpy.data.objects['SnowAdult_CreatorMesh'];rig=bpy.data.objects['SnowAdult_Rig']
 return {'positions':[tuple(v.co) for v in m.data.vertices],'faces':[tuple(p.vertices) for p in m.data.polygons],'weights':[{m.vertex_groups[g.group].name:g.weight for g in v.groups} for v in m.data.vertices],'bones':{b.name:{'parent':b.parent.name if b.parent else None,'matrix':tuple(x for row in b.matrix_local for x in row)} for b in rig.data.bones}}
old=snapshot(root/'unity-snow/SnowAdult_Unity.blend');new=snapshot(out/'SnowAdult_Unity.blend');checks={key+'_unchanged':old[key]==new[key] for key in old};frozen=json.loads((root/'unity-snow/artifact-manifest.json').read_text());frozenchecks={r['path']:hashlib.sha256((root/'unity-snow'/r['path']).read_bytes()).hexdigest()==r['sha256'] for r in frozen['files']}
checks['all_frozen_delivery_files_byte_identical']=all(frozenchecks.values());uv=validate_atlas(bpy.data.objects['SnowAdult_CreatorMesh'].data);checks['zero_atlas_overlap_texel_samples']=uv['overlapping_interior_samples']==0;checks['zero_edit_hidden_faces']=uv['hidden_faces']==0
report={'checks':checks,'frozen_file_checks':frozenchecks,'atlas_validation':uv,'scope':'Same creator mesh positions, polygon topology, weights and bind/rest matrices; target UV and atlas maps deliberately replaced. Frozen original deliveries retained.'};(out/'correction-validation.json').write_text(json.dumps(report,indent=2));print('CORRECTION_CHECKS',json.dumps(checks));assert all(checks.values())
