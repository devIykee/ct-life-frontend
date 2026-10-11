"""Read-only fresh FBX import audit for cast v3 (never exports or modifies assets).
Blender -b --factory-startup --disable-autoexec -P v3_validate.py -- --output <dir>
Checks every v3 FBX: one armature with the exact CTS hierarchy, sockets on complete characters, tier suffixes,
influence caps (<=4, <=2 at LOD2, accessories rigid), no unweighted / non-normalised vertices, no Head weight low on
the body, one material + one UV per mesh, ONE material name per character (family material / base material incl.
accessories), triangle totals vs metadata and caps. Heroes reuse v2 FBX: their sha256 pins are re-checked.
Blender-import evidence only: not Unity Humanoid, animation or visual acceptance."""
import argparse, hashlib, json, sys, time
from pathlib import Path
import bpy
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import v3_common as K
from v3_common import CTS_BONES, CTS_PARENT, SOCKETS, CAPS, MAX_INFL

EXPORT = K.EXPORT

def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def tier_of(name):
    for tier, suffix in (('hero', '_Hero'), ('lod0', '_LOD0'), ('lod1', '_LOD1'), ('lod2', '_LOD2')):
        if name.endswith(suffix):
            return tier
    return None

def context(path):
    rel = path.relative_to(EXPORT).as_posix()
    parts = rel.split('/')
    if parts[0] == 'families':
        fam = parts[1]
        fm = json.loads((EXPORT / 'families' / fam / f'{fam}.json').read_text())
        heights = [json.loads((EXPORT / 'bases' / b / f'{b}.json').read_text())['height_m'] for b in fm['bases']]
        slot_key = path.stem.split('__', 1)[1]
        slot, key = slot_key.split('_', 1)
        src = fm['library'][slot][key]
        return {'kind': 'family_part', 'rel': rel, 'height': max(heights), 'expect_tiers': set(CAPS), 'material': f'{fam}_Mat',
                'triangles': src['triangles']}
    bid = parts[1]
    meta = json.loads((EXPORT / 'bases' / bid / f'{bid}.json').read_text())
    fam = meta.get('family')
    mat = f'{fam}_Mat' if fam else f'{bid}_Mat'
    if parts[2] == 'accessories':
        src = next((v for v in meta['accessories'].values() if v['fbx'] == rel), None)
        return {'kind': 'accessory', 'rel': rel, 'height': meta['height_m'], 'material': mat,
                'triangles': src['triangles'] if src else None}
    if parts[2] == 'parts':
        src = next((v for v in meta['parts'].values() if v['fbx'] == rel), None)
        return {'kind': 'part', 'rel': rel, 'height': meta['height_m'], 'expect_tiers': set(CAPS), 'material': mat,
                'triangles': src['triangles'] if src else None}
    hero = path.stem.endswith('_Hero')
    return {'kind': 'base', 'rel': rel, 'height': meta['height_m'], 'expect_tiers': {'hero'} if hero else {'lod0', 'lod1', 'lod2'},
            'material': mat, 'tiers_meta': meta['tiers']}

def inspect(path):
    ctx = context(path)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    r = {'sha256': sha(path), 'kind': ctx['kind'], 'errors': [], 'meshes': {}, 'tier_totals': {}, 'materials': []}
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if len(arms) != 1:
        r['errors'].append(f'expected exactly one armature, observed {len(arms)}')
    else:
        par = {b.name: b.parent.name if b.parent else None for b in arms[0].data.bones}
        sk = {'missing': sorted(set(CTS_BONES) - set(par)), 'extra': sorted(set(par) - set(CTS_BONES)),
              'wrong_parent': [b for b in CTS_BONES if b in par and par[b] != CTS_PARENT[b]]}
        if any(sk.values()):
            r['errors'].append('skeleton differs from CTS'); r['skeleton'] = sk
        if ctx['kind'] == 'base':
            socks = {o.name: o for o in bpy.context.scene.objects if o.type == 'EMPTY' and o.name.startswith('socket_')}
            r['sockets'] = sorted(socks)
            for name, spec in SOCKETS.items():
                o = socks.get('socket_' + name)
                if o is None or o.parent_type != 'BONE' or o.parent_bone != spec['bone']:
                    r['errors'].append('socket missing or wrong bone: ' + name)
    acc = ctx['kind'] == 'accessory'
    for o in bpy.context.scene.objects:
        if o.type != 'MESH':
            continue
        me = o.data; me.calc_loop_triangles(); tier = tier_of(o.name)
        w = np.array([sum(g.weight for g in v.groups) for v in me.vertices])
        head = {o.vertex_groups['Head'].index} if 'Head' in o.vertex_groups else set()
        low = sum(1 for v in me.vertices if not acc and (o.matrix_world @ v.co).z < .6 * ctx['height']
                  and sum(g.weight for g in v.groups if g.group in head) > .01)
        m = {'tier': tier, 'triangles': len(me.loop_triangles), 'vertices': len(me.vertices),
             'max_influences': max((sum(g.weight > 1e-5 for g in v.groups) for v in me.vertices), default=0),
             'materials': [s.material.name if s.material else None for s in o.material_slots], 'uv_layers': len(me.uv_layers),
             'unweighted': int((w < 1e-4).sum()), 'non_normalised': int((np.abs(w - 1) > .002).sum()), 'head_weight_low': low}
        r['meshes'][o.name] = m
        r['materials'] += [x for x in m['materials'] if x not in r['materials']]
        if tier:
            r['tier_totals'][tier] = r['tier_totals'].get(tier, 0) + m['triangles']
        elif not acc:
            r['errors'].append(o.name + ': no known tier suffix')
        if m['max_influences'] > (1 if acc else MAX_INFL.get(tier, 4)):
            r['errors'].append(o.name + ': influence cap exceeded')
        for k in ('unweighted', 'non_normalised', 'head_weight_low'):
            if m[k]:
                r['errors'].append(f'{o.name}: {k}={m[k]}')
        if len(m['materials']) != 1 or m['uv_layers'] != 1:
            r['errors'].append(f"{o.name}: expected one material/UV, got {len(m['materials'])}/{m['uv_layers']}")
    if not r['meshes']:
        r['errors'].append('no meshes')
    mats = {x.split('.')[0] for x in r['materials'] if x}
    if mats != {ctx['material']}:
        r['errors'].append(f"material names {sorted(mats)} != expected single material {ctx['material']}")
    if ctx['kind'] == 'base':
        if set(r['tier_totals']) != ctx['expect_tiers']:
            r['errors'].append('missing/unexpected tiers')
        for t, n in r['tier_totals'].items():
            if n > CAPS[t]:
                r['errors'].append(f'combined tier {t}: {n}>{CAPS[t]}')
            if n != ctx['tiers_meta'][t]['triangles']:
                r['errors'].append(f'tier {t} disagrees with base metadata ({n} vs {ctx["tiers_meta"][t]["triangles"]})')
    elif ctx['kind'] in ('part', 'family_part'):
        if set(r['tier_totals']) != ctx['expect_tiers']:
            r['errors'].append('missing/unexpected tiers')
        if ctx['triangles'] is None or r['tier_totals'] != ctx['triangles']:
            r['errors'].append(f"part triangles disagree with metadata ({r['tier_totals']} vs {ctx['triangles']})")
    else:
        if ctx['triangles'] is None or sum(m['triangles'] for m in r['meshes'].values()) != ctx['triangles']:
            r['errors'].append('accessory triangles disagree with metadata')
    r['ok'] = not r['errors']
    return r

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--output', required=True, type=Path)
    a = ap.parse_args(sys.argv[sys.argv.index('--') + 1:])
    a.output.mkdir(parents=True, exist_ok=True)
    dest = a.output / 'validation_v3.json'
    if dest.exists():
        raise FileExistsError('Preserve existing receipt: ' + str(dest))
    files = (sorted(EXPORT.glob('bases/*/*.fbx')) + sorted(EXPORT.glob('bases/*/parts/*.fbx')) +
             sorted(EXPORT.glob('bases/*/accessories/*.fbx')) + sorted(EXPORT.glob('families/*/parts/*.fbx')))
    inputs = {p.relative_to(EXPORT).as_posix(): sha(p) for p in EXPORT.rglob('*') if p.is_file() and p.suffix in ('.fbx', '.png', '.json')
              and p.name != 'validation_v3.json'}
    res = {'revision': 'v3-fresh-import-20261010', 'input_root': str(EXPORT), 'blender_version': bpy.app.version_string,
           'validator_sha256': sha(Path(__file__)), 'scope': 'Blender import structure only; not Unity Humanoid, animation or visual acceptance',
           'input_hashes': inputs, 'files': {}, 'failures': []}
    t0 = time.time()
    for i, f in enumerate(files):
        rel = f.relative_to(EXPORT).as_posix()
        try:
            r = inspect(f)
        except Exception as exc:
            r = {'sha256': sha(f), 'ok': False, 'errors': [f'{type(exc).__name__}: {exc}']}
        res['files'][rel] = r
        if not r['ok']:
            res['failures'].append({rel: r['errors']})
        print(f'[v3-validate] {i + 1}/{len(files)} {rel} ok={r["ok"]}', flush=True)
    # heroes: v2 geometry pins
    heroes = {}
    for p in sorted(EXPORT.glob('bases/hero_*/hero_*.json')):
        m = json.loads(p.read_text())
        bad = [rel for rel, h in m['pinned_v2_files_sha256'].items() if sha(K.V2EXPORT / rel) != h]
        heroes[m['id']] = {'pinned_files': len(m['pinned_v2_files_sha256']), 'drift': bad}
        if bad:
            res['failures'].append({m['id']: ['v2 geometry pin drift: ' + ', '.join(bad)]})
    res['hero_v2_pins'] = heroes
    after = {p.relative_to(EXPORT).as_posix(): sha(p) for p in EXPORT.rglob('*') if p.is_file() and p.suffix in ('.fbx', '.png', '.json')
             and p.name != 'validation_v3.json'}
    res['source_drift'] = [k for k in sorted(set(inputs) | set(after)) if inputs.get(k) != after.get(k)]
    res['summary'] = {'fbx_files': len(files), 'failed': len(res['failures']), 'seconds': round(time.time() - t0, 1),
                      'by_kind': {}}
    for rel, r in res['files'].items():
        k = r.get('kind', 'error'); s = res['summary']['by_kind'].setdefault(k, {'files': 0, 'failed': 0})
        s['files'] += 1; s['failed'] += 0 if r['ok'] else 1
    res['all_pass'] = bool(files) and not res['failures'] and not res['source_drift']
    dest.write_text(json.dumps(res, indent=1) + '\n')
    print('[v3-validate]', json.dumps(res['summary']), 'all_pass', res['all_pass'], flush=True)

if __name__ == '__main__':
    main()
