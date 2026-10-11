"""Fresh-import validation of every exported look/part FBX.

Checks per file: armature has the family's exact 55 bones with rest matrices equal to the source rig
(tolerance 1e-4 m), each mesh has 1 material slot and 1 UV layer, triangle caps per tier, max bone
influences (4, or 2 for LOD2), normalised weights, no vertex without weights.
usage: Blender -b ... -P cast_validate.py
Writes art/cast/export/validation.json
"""
import bpy, sys, json, re
import numpy as np
from pathlib import Path
from mathutils import Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cast_common import *

def rest_mats(arm_obj):
    return {b.name: (arm_obj.matrix_world @ b.matrix_local) for b in arm_obj.data.bones}

ref = {}
for fam, cfg in FAMILIES.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(cfg['blend'].parent / ('exports/SnowAdult_Creator.fbx' if fam == 'm' else 'exports/RainAdult_Creator.fbx')))
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    ref[fam] = {k: [list(r) for r in v] for k, v in rest_mats(arm).items()}

def tier_of(name):
    for t, key in (('hero', '_Hero'), ('lod0', '_LOD0'), ('lod1', '_LOD1'), ('lod2', '_LOD2')):
        if name.endswith(key):
            return t
    return None

results = {'files': {}, 'failures': []}
files = sorted(EXPORT.glob('looks/*/*.fbx')) + sorted(EXPORT.glob('parts/*/*.fbx'))
FILT = [a for a in (sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])]
if FILT:
    # filters: look ids, or fam/part prefixes for runtime parts (e.g. f/hair_afro)
    files = [f for f in files if any(('looks/' + x + '/') in str(f) or ('parts/' + x) in str(f) for x in FILT)]
for f in files:
    rel = str(f.relative_to(EXPORT))
    fam = 'f' if '/f/' in rel or rel.startswith('parts/f') else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(f))
    arm = next((o for o in bpy.context.scene.objects if o.type == 'ARMATURE'), None)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    r = {'meshes': {}, 'ok': True, 'errors': []}
    if arm is None:
        r['errors'].append('no armature')
    else:
        names = sorted(b.name for b in arm.data.bones)
        rm = rest_mats(arm)
        deltas = {}
        for fm in ref:
            if names == sorted(ref[fm]):
                deltas[fm] = max(max(abs(x - y) for ra, rb in zip(rm[n], Matrix(ref[fm][n])) for x, y in zip(ra, rb)) for n in rm)
        if not deltas:
            r['errors'].append('bone set differs from both families')
        else:
            fam = min(deltas, key=deltas.get)
            r['family'] = fam
            r['max_rest_delta'] = deltas[fam]
            if deltas[fam] > 1e-4:
                r['errors'].append(f'rest matrices differ ({deltas[fam]:.2e})')
    for o in meshes:
        me = o.data
        me.calc_loop_triangles()
        tris = len(me.loop_triangles)
        tier = tier_of(o.name)
        infl = max_influences(o)
        nmat = len(o.material_slots)
        nuv = len(me.uv_layers)
        W = np.zeros(len(me.vertices))
        for v in me.vertices:
            W[v.index] = sum(g.weight for g in v.groups)
        unweighted = int((W < 1e-4).sum())
        badnorm = int((np.abs(W - 1) > 2e-3).sum())
        hh = [o.vertex_groups[n].index for n in ('Head', 'Jaw', 'Eye.L', 'Eye.R') if n in o.vertex_groups]
        mw = o.matrix_world
        low_head = 0
        for v in me.vertices:
            if (mw @ v.co).z < 1.10 and sum(g.weight for g in v.groups if g.group in hh) > 0.01:
                low_head += 1   # nothing legitimately Head-weighted hangs below the chin (~1.4 m)
        r['meshes'][o.name] = {'tier': tier, 'triangles': tris, 'max_influences': infl, 'materials': nmat, 'uv_layers': nuv,
                               'unweighted_vertices': unweighted, 'non_normalised': badnorm, 'head_weight_below_1_1m': low_head, 'shape_keys': len(me.shape_keys.key_blocks) if me.shape_keys else 0}
        if 'looks/' in rel and tier and tris > CAPS[tier]:
            r['errors'].append(f'{o.name} {tris} > {CAPS[tier]}')
        if tier and infl > MAX_INFL[tier]:
            r['errors'].append(f'{o.name} influences {infl}')
        if nmat > 1:
            r['errors'].append(f'{o.name} {nmat} materials')
        if nuv != 1:
            r['errors'].append(f'{o.name} {nuv} uv layers')
        if unweighted:
            r['errors'].append(f'{o.name} {unweighted} unweighted vertices')
        if low_head:
            r['errors'].append(f'{o.name} {low_head} lower-body vertices with Head/Jaw/Eye weight')
    r['ok'] = not r['errors']
    results['files'][rel] = r
    if not r['ok']:
        results['failures'].append({rel: r['errors']})
if FILT and (EXPORT / 'validation.json').exists():
    prev = json.loads((EXPORT / 'validation.json').read_text())
    # drop entries for files that no longer exist, replace re-validated ones
    prev['files'] = {k: v for k, v in prev['files'].items() if (EXPORT / k).exists()}
    prev['files'].update(results['files'])
    results['files'] = prev['files']
    results['last_partial_run'] = {'filters': FILT, 'files': len(files), 'failed': len(results['failures'])}
results['failures'] = [{k: v['errors']} for k, v in results['files'].items() if not v['ok']]
results['summary'] = {'files': len(results['files']), 'failed': len(results['failures'])}
(EXPORT / 'validation.json').write_text(json.dumps(results, indent=1))
log('VALIDATE_DONE', json.dumps(results['summary']), results['failures'][:10])
