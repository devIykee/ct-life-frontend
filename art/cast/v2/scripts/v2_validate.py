"""Read-only fresh FBX import audit. Never exports or modifies source assets.
Blender -b --factory-startup --disable-autoexec -P v2_validate.py -- --output /evidence/run
Proves Blender-import structure only, not Unity retargeting, animation or visual acceptance.
"""
import argparse, hashlib, json, sys, time
from pathlib import Path
import bpy
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from v2_common import EXPORT, CTS_BONES, CTS_PARENT, SOCKETS, CAPS, MAX_INFL

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def tier_of(name):
    for tier, suffix in (('hero','_Hero'),('lod0','_LOD0'),('lod1','_LOD1'),('lod2','_LOD2')):
        if name.endswith(suffix): return tier
    return None

def inspect(path):
    rel = path.relative_to(EXPORT).as_posix()
    accessory, part = '/accessories/' in rel, '/parts/' in rel
    bid = path.parts[-3] if accessory or part else path.parent.name
    meta = json.loads((EXPORT/'bases'/bid/(bid+'.json')).read_text())
    height = meta['height_m']  # full-body height, also for isolated parts
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    r = {'sha256':sha(path),'errors':[],'meshes':{},'tier_totals':{},'base_id':bid}
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if len(arms) != 1:
        r['errors'].append('expected exactly one armature, observed '+str(len(arms)))
    else:
        par = {b.name:b.parent.name if b.parent else None for b in arms[0].data.bones}
        r['skeleton'] = {'missing':sorted(set(CTS_BONES)-set(par)), 'extra':sorted(set(par)-set(CTS_BONES)),
                         'wrong_parent':[b for b in CTS_BONES if b in par and par[b]!=CTS_PARENT[b]]}
        if any(r['skeleton'].values()): r['errors'].append('skeleton differs from CTS')
        if not accessory and not part:
            socks = {o.name:o for o in bpy.context.scene.objects if o.type=='EMPTY' and o.name.startswith('socket_')}
            r['sockets'] = sorted(socks)
            for name,spec in SOCKETS.items():
                obj = socks.get('socket_'+name)
                if obj is None or obj.parent_type!='BONE' or obj.parent_bone!=spec['bone']:
                    r['errors'].append('socket missing or wrong bone: '+name)
    for o in bpy.context.scene.objects:
        if o.type!='MESH': continue
        me=o.data; me.calc_loop_triangles(); tier=tier_of(o.name)
        weights=np.array([sum(g.weight for g in v.groups) for v in me.vertices])
        head={o.vertex_groups['Head'].index} if 'Head' in o.vertex_groups else set()
        low=[(v.index,tuple(o.matrix_world@v.co)) for v in me.vertices if not accessory and (o.matrix_world@v.co).z<.6*height
             and sum(g.weight for g in v.groups if g.group in head)>.01]
        m={'tier':tier,'triangles':len(me.loop_triangles),'vertices':len(me.vertices),
           'max_influences':max((sum(g.weight>1e-5 for g in v.groups) for v in me.vertices),default=0),
           'materials':len(o.material_slots),'uv_layers':len(me.uv_layers),'unweighted':int((weights<1e-4).sum()),
           'non_normalised':int((np.abs(weights-1)>.002).sum()),'head_weight_low':len(low),'head_weight_low_examples':low[:8]}
        r['meshes'][o.name]=m
        if tier: r['tier_totals'][tier]=r['tier_totals'].get(tier,0)+m['triangles']
        elif not accessory: r['errors'].append(o.name+': no known tier suffix')
        if m['max_influences']>(1 if accessory else MAX_INFL.get(tier,4)):
            r['errors'].append(o.name+': influence cap exceeded')
        for key in ('unweighted','non_normalised','head_weight_low'):
            if m[key]: r['errors'].append(f'{o.name}: {key}={m[key]}')
        if m['materials']!=1 or m['uv_layers']!=1:
            r['errors'].append(f'{o.name}: expected one material/UV, got {m["materials"]}/{m["uv_layers"]}')
    if not r['meshes']: r['errors'].append('no meshes')
    if not accessory:
        expected=set(CAPS) if part else ({'hero'} if path.stem.endswith('_Hero') else {'lod0','lod1','lod2'})
        if set(r['tier_totals'])!=expected: r['errors'].append('missing/unexpected tiers')
        if not part:
            for tier,total in r['tier_totals'].items():
                if total>CAPS[tier]: r['errors'].append(f'combined tier {tier}: {total}>{CAPS[tier]}')
                if total!=meta['tiers'][tier]['triangles']: r['errors'].append(f'tier {tier} disagrees with base metadata')
        else:
            source=next((v for v in meta['parts'].values() if v['fbx']==rel),None)
            if source is None or r['tier_totals']!=source['triangles']: r['errors'].append('part triangles disagree with metadata')
    else:
        source=next((v for v in meta['accessories'].values() if v['fbx']==rel),None)
        if source is None or sum(m['triangles'] for m in r['meshes'].values())!=source['triangles']:
            r['errors'].append('accessory triangles disagree with metadata')
    r['ok']=not r['errors']; return r

def main():
    global EXPORT
    parser=argparse.ArgumentParser(); parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--export-root',type=Path,default=EXPORT)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    EXPORT=args.export_root.resolve()
    args.output.mkdir(parents=True,exist_ok=True); dest=args.output/'validation_v2.json'
    if dest.exists(): raise FileExistsError('Preserve existing receipt: '+str(dest))
    files=sorted(EXPORT.glob('bases/*/*.fbx'))+sorted(EXPORT.glob('bases/*/parts/*.fbx'))+sorted(EXPORT.glob('bases/*/accessories/*.fbx'))
    originals={p.relative_to(EXPORT).as_posix():sha(p) for p in (EXPORT/'bases').rglob('*') if p.is_file()}
    start=time.time()
    res={'revision':'fresh-import-20261009','input_root':str(EXPORT),'scope':'Blender imported geometry and rig structure; no Unity or motion acceptance',
         'blender_version':bpy.app.version_string,'validator_sha256':sha(Path(__file__)),
         'input_hashes':originals,'files':{},'failures':[]}
    for i,f in enumerate(files):
        rel=f.relative_to(EXPORT).as_posix()
        try: r=inspect(f)
        except Exception as exc: r={'sha256':sha(f),'ok':False,'errors':[type(exc).__name__+': '+str(exc)]}
        res['files'][rel]=r
        if not r['ok']: res['failures'].append({rel:r['errors']})
        print(f'[v2-validate] {i+1}/{len(files)} {rel} ok={r["ok"]}',flush=True)
    after={p.relative_to(EXPORT).as_posix():sha(p) for p in (EXPORT/'bases').rglob('*') if p.is_file()}
    res['source_drift']=[k for k in sorted(set(originals)|set(after)) if originals.get(k)!=after.get(k)]
    res['summary']={'files':len(files),'failed':len(res['failures']),'seconds':round(time.time()-start,3)}
    res['all_pass']=bool(files) and not res['failures'] and not res['source_drift']
    dest.write_text(json.dumps(res,indent=2)+'\n')
    print('[v2-validate]',json.dumps(res['summary']),'all_pass',res['all_pass'],flush=True)
    if not res['all_pass']: raise RuntimeError('Cast fresh import contains failures; see '+str(dest))

if __name__=='__main__': main()
