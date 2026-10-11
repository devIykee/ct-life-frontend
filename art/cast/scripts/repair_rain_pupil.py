"""Restore Rain's accidentally removed right pupil cap without rebuilding the cast.

The normal-only cleanup in cast_extract.py deleted one original front cap. The
surviving left cap has the same shape and atlas color. Mirror only its four vertices
and two triangles, with right-eye weights. Existing geometry, UVs and custom normals
are asserted unchanged. Output is a review candidate, never an automatic Unity copy.

Blender -b --factory-startup --disable-autoexec -P repair_rain_pupil.py -- <output-dir>
"""
import bpy, json, sys, hashlib
import numpy as np
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cast_common import REPO, EVID, select_only
from cast_eye_surface import restore_missing_rain_pupil

out = Path(sys.argv[sys.argv.index('--') + 1])
out.mkdir(parents=True, exist_ok=True)
manifest = json.loads((REPO / 'client/Assets/Art/characters/cast/manifest.json').read_text())
looks = {k:v for k,v in manifest['looks'].items() if v['family'] == 'f'}
source = EVID / 'families/f/looks.blend'
bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rig.data.pose_position = 'REST'
report = {'source': str(source), 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'meshes': [], 'files': []}




exp = dict(use_selection=True, object_types={'MESH','ARMATURE'}, add_leaf_bones=False, use_armature_deform_only=True,
           bake_anim=False, axis_forward='-Z', axis_up='Y', global_scale=1, apply_unit_scale=True,
           apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=False, path_mode='STRIP', use_tspace=False)
for look,entry in looks.items():
    selected = {}
    for tier in entry['tiers']:
        name = look + '_' + {'hero':'Hero','lod0':'LOD0','lod1':'LOD1','lod2':'LOD2'}[tier]
        obj = bpy.data.objects.get(name)
        assert obj is not None, name
        report['meshes'].append(restore_missing_rain_pupil(obj)); selected[tier] = obj
    folder = out/'looks'/look; folder.mkdir(parents=True,exist_ok=True)
    for key,objs,suffix in [('crowd_fbx',[selected[t]for t in ('lod0','lod1','lod2') if t in selected],''),
                            ('hero_fbx',[selected['hero']] if 'hero' in selected else [],'_Hero')]:
        if not objs: continue
        select_only(objs+[rig],rig); path=folder/(look+suffix+'.fbx')
        bpy.ops.export_scene.fbx(filepath=str(path),**exp)
        report['files'].append({'path':str(path.relative_to(out)),'source_key':key,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
(out/'pupil-repair-report.json').write_text(json.dumps(report,indent=2)+'\n')
print('PUPIL_REPAIR_CANDIDATE',len(report['meshes']),'meshes',len(report['files']),'FBX files',flush=True)
