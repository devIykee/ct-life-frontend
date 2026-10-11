"""Cast v3 catalogue: manifest_v3.json, runtime_part_map_v3.json, customizer_schema_v3.json, cts_humanoid_map.json,
validation copy. Plain Python (no Blender); reads the exported base/family JSONs and a fresh-import receipt.
python3 v3_manifest.py --validation <dir>/validation_v3.json"""
import argparse, hashlib, json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import v3_common as K
import v3_spec as S
from v3_common import EXPORT, CAPS, MAX_INFL, SOCKETS, TINT_REGIONS, MASK_CHANNEL, HUMANOID, CTS_BONES, CTS_PARENT, CTS_DEFORM

def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def read(p):
    return json.loads(Path(p).read_text())

def asset(rel, root=EXPORT):
    p = (root / rel)
    if p.resolve().is_relative_to(root.resolve()) and p.is_file():
        return p
    raise ValueError('Missing or unsafe asset: ' + rel)

def load_bases():
    out = {}
    for bid in list(S.MODULAR) + list(S.UBC) + list(S.HEROES):
        out[bid] = read(EXPORT / 'bases' / bid / f'{bid}.json')
    return out

def load_families():
    return {f: read(EXPORT / 'families' / f / f'{f}.json') for f in S.FAMILIES}

# ------------------------------------------------------------------ composition rules (art-side resolver)
HEAD_SOCKETS = {'face', 'head_top'}
ACCESSORY_RULE = ('Hero and LOD0 show every selected accessory (selection must fit the caps); LOD1 keeps accessories in selection '
                  'order while the character stays within the LOD1 cap; LOD2 shows none.')

def compose(base, families, selection=None):
    """selection: {'parts': {slot: key}, 'hair': ubc style|'none', 'facial': [..], 'accessories': [..]}.
    Returns parts (fbx list), triangles per tier, over_budget, hidden. Raises ValueError on invalid input."""
    sel = selection or {}
    fam = base.get('family')
    files, tris = [], {t: 0 for t in CAPS}
    hides = set()
    if fam:
        F = families[fam]
        parts = {s: v.split('/')[1] for s, v in base['default_parts'].items()}
        for s, k in sel.get('parts', {}).items():
            if s not in parts:
                raise ValueError(f'unknown slot {s}')
            if k not in F['library'][s]:
                raise ValueError(f'{k} is not a {fam} {s} part')
            parts[s] = k
        if parts['feet'] not in F['compatibility']['bottom_feet'][parts['bottom']]:
            raise ValueError(f"feet {parts['feet']} leave a gap under bottom {parts['bottom']}")
        if parts['bottom'] not in F['compatibility']['top_bottom'][parts['top']]:
            raise ValueError(f"bottom {parts['bottom']} leaves a gap under top {parts['top']}")
        if sel.get('hair') or sel.get('facial'):
            raise ValueError('modular families choose hair through the head part')
        for s, k in parts.items():
            e = F['library'][s][k]; files.append(e['fbx'])
            for t in CAPS:
                tris[t] += e['triangles'][t]
        chosen = parts
    else:
        if sel.get('parts'):
            raise ValueError('this base has no swappable outfit parts')
        lib = base.get('hair_library', {})
        chosen = [k for k, p in base['parts'].items() if not p.get('kind')]
        hair = sel.get('hair')
        if hair is not None:
            if hair != 'none' and hair not in lib.get('hair', {}):
                raise ValueError('unknown hairstyle for this base')
            chosen = [p for p in chosen if p != 'hair']
            if hair != 'none':
                chosen.append(lib['hair'][hair]['part'])
        for f in sel.get('facial', []):
            if f not in lib.get('facial', {}):
                raise ValueError('unknown facial hair for this base')
            chosen.append(lib['facial'][f]['part'])
        for k in chosen:
            files.append(base['parts'][k]['fbx'])
            for t in CAPS:
                tris[t] += base['parts'][k]['triangles'][t]
    socks = set()
    dropped = {'lod1': [], 'lod2': []}
    accs = sel.get('accessories', [])
    if len(set(accs)) != len(accs):
        raise ValueError('duplicate accessory')
    for a in accs:
        if a not in base['accessories']:
            raise ValueError(f'unknown accessory {a}')
        acc = base['accessories'][a]
        if acc['socket'] in socks:
            raise ValueError('two accessories use the same socket')
        socks.add(acc['socket'])
        fl = acc.get('flags', {})
        if fl.get('hides_hair'):
            hides.add('hair')
        files.append(acc['fbx'])
        at = acc['triangles']
        n = {t: (at[t] if isinstance(at, dict) else at) for t in CAPS}
        for t in ('hero', 'lod0'):         # all accessories at Hero/LOD0 (over_budget reported)
            tris[t] += n[t]
        if tris['lod1'] + n['lod1'] <= CAPS['lod1']:   # LOD1: keep accessories in selection order while they fit
            tris['lod1'] += n['lod1']
        else:
            dropped['lod1'].append(a)
        dropped['lod2'].append(a)          # LOD2: no accessories (crowd distance)
    return {'parts': chosen, 'files': files, 'triangles': tris, 'hidden': sorted(hides), 'accessories_dropped': dropped,
            'over_budget': {t: n - CAPS[t] for t, n in tris.items() if n > CAPS[t]}}

TINT_RESOLUTION = ('modular families: tints = default_tints of the selected parts (feet, bottom, top, head order) <- base look_defaults '
                   '<- user tints; brows = brow_color(final hair) unless the user sets brows. UBC/heroes: base look_defaults <- user tints.')

def resolve_tints(base, families, parts=None, user=None):
    t = {}
    fam = base.get('family')
    if fam:
        sel = {s: v.split('/')[1] for s, v in base['default_parts'].items()}
        sel.update(parts or {})
        for s in ('feet', 'bottom', 'top', 'head'):
            t.update(families[fam]['library'][s][sel[s]].get('default_tints', {}))
    t.update({k: v for k, v in base['look_defaults'].items() if k in TINT_REGIONS})
    user = user or {}
    t.update(user)
    if 'hair' in user and 'brows' not in user and 'brows' in t:
        t['brows'] = K.brow_color(user['hair'] if user['hair'].startswith('#') else K.HAIR_COLORS[user['hair']])
    return t

# ------------------------------------------------------------------ presets (players + NPC roster, 50/50)
def presets(bases):
    f = [b for b, m in bases.items() if m['gender'] == 'f']
    m = [b for b, x in bases.items() if x['gender'] == 'm']
    players = []
    for i in range(max(len(f), len(m))):
        for lst in (f, m) if i % 2 == 0 else (m, f):
            if i < len(lst):
                players.append(lst[i])
    tones = [f'skin_{i:02d}' for i in range(1, 11)]
    npc = []
    arche = ['anon', 'whale', 'degen', 'kol_caller', 'builder', 'sniper_bot', 'laser_eyes_maxi', 'cabal_insider',
             'trencher', 'airdrop_farmer', 'rug_dev', 'vc', 'ai_agent', 'mooner', 'retired_whale', 'degen']
    hair = ['black', 'dark_brown', 'blonde', 'pink', 'ginger', 'platinum', 'blue', 'auburn', 'green', 'purple', 'brown', 'grey']
    cloth = ['black', 'charcoal', 'white', 'navy', 'denim', 'khaki', 'olive', 'red', 'orange', 'gold', 'teal', 'purple', 'pink', 'sol_purple', 'sol_green']
    def pick(gender, arch, k):
        pool = [b for b, x in bases.items() if x['gender'] == gender and arch in x.get('archetypes', []) and not x.get('rare_masked_look')]
        pool = pool or [b for b, x in bases.items() if x['gender'] == gender and not x.get('rare_masked_look')]
        return sorted(pool)[k % len(pool)]
    for i, a in enumerate(arche):
        for g in ('f', 'm'):
            k = len(npc)
            b = pick(g, a, i)
            npc.append({'id': f'npc_{a}_{g}{i:02d}', 'archetype': a, 'gender': g, 'base': b, 'skin': tones[(k * 3 + i) % 10],
                        'tints': {'hair': hair[(k * 5) % len(hair)], 'top': cloth[(k * 7) % len(cloth)], 'bottom': cloth[(k * 11 + 3) % len(cloth)]}})
    return {'player_bases_order': players, 'npc_presets': npc,
            'rules': ['Interleave F/M; never put a regional/cultural look first (none of the v3 bases is region-coded).',
                      'Skin tones cycle through all 10 tones; each tone appears >= 3 times in the NPC roster.',
                      'Rare masked CC-BY heroes are excluded from random NPC fills (special NPCs only).']}

def generate(validation_path, out=EXPORT):
    val = read(validation_path)
    bases = load_bases(); fams = load_families()
    pal = read(K.V2EXPORT / 'skin_palette.json')
    refs = {}
    for bid, b in bases.items():
        root = K.V2EXPORT if b.get('geometry_root') else EXPORT
        for k in ('crowd_fbx', 'hero_fbx'):
            refs[(root, b['files'][k])] = 1
        for p in b['parts'].values():
            refs[(root, p['fbx'])] = 1
        for a in b['accessories'].values():
            refs[(root if not a.get('family_part') else EXPORT, a['fbx'])] = 1
        for t in b['textures'].values():
            refs[(EXPORT, t)] = 1
    for f, F in fams.items():
        for slot in F['library'].values():
            for e in slot.values():
                refs[(EXPORT, e['fbx'])] = 1
        for t in F['textures'].values():
            refs[(EXPORT, t)] = 1
    inventory = {}
    for (root, rel) in sorted(refs, key=lambda x: (str(x[0]), x[1])):
        p = asset(rel, root)
        key = rel if root == EXPORT else '../../v2/export/' + rel
        inventory[key] = {'sha256': sha(p), 'bytes': p.stat().st_size}
    unref = sorted(p.relative_to(EXPORT).as_posix() for p in EXPORT.rglob('*') if p.is_file() and p.suffix in ('.fbx', '.png')
                   and p.relative_to(EXPORT).as_posix() not in inventory)
    catalogue = {}
    for bid, b in bases.items():
        catalogue[bid] = {'label': b['label'], 'gender': b['gender'], 'family': b.get('family'), 'archetypes': b['archetypes'],
                          'rare_masked_look': bool(b.get('rare_masked_look')), 'licence': b['licence'],
                          'default_assembly': compose(b, fams)}
    arch = {}
    for bid, b in bases.items():
        for a in b['archetypes']:
            arch.setdefault(a, []).append(bid)
    combos = []
    for c in S.COMBOS:
        r = compose(bases[c['base']], fams, {'parts': c.get('parts', {}), 'accessories': c['accessories']})
        combos.append({**c, 'gender': bases[c['base']]['gender'], 'assembly': r})
    nf = sum(b['gender'] == 'f' for b in bases.values()); nm = sum(b['gender'] == 'm' for b in bases.values())
    pre = presets(bases)
    manifest = {
        'version': 'v3-20261010', 'status': 'art_export_validated_in_blender_not_unity_accepted',
        'decision_umm_umw': 'Quaternius Ultimate Modular Men/Women are used as DISTINCT BASES (each pack = one family: one CTS skeleton, '
                            'one shared atlas/material, every head/top/bottom/feet part swaps inside the family). Not refitted onto UBC bodies: '
                            'refitting would bring back two faces for the whole cast and needs garment re-modelling between very different body '
                            'proportions. Evidence: style test + proportion overlay (cast-hermes-20261010/style_test_sheet.jpg).',
        'skeleton': 'cts_humanoid_map.json', 'customizer_schema': 'customizer_schema_v3.json', 'runtime_part_map': 'runtime_part_map_v3.json',
        'validation': {'path': 'validation_v3.json', 'sha256': sha(validation_path), 'all_pass': val['all_pass'], 'summary': val['summary'],
                       'failures': val['failures']},
        'budgets': {'triangles': CAPS, 'max_bone_influences': MAX_INFL, 'family_slot_caps': K.SLOT_CAPS,
                    'materials_per_character': 1, 'atlas': {'family': 2048, 'ubc_and_heroes': 1024},
                    'accessory_rule': ACCESSORY_RULE,
                    'scope': 'per complete visible character incl. accessories; compose() reports over_budget and accessories_dropped'},
        'palettes': {'skin': pal['tones'], 'skin_method': pal['method'], 'hair': K.HAIR_COLORS, 'cloth': K.CLOTH_COLORS, 'hat': K.HAT_COLORS,
                     'brows': 'derived from hair: same hue, linear luminance min(0.55 x hair, 0.075), 60 % saturation (v3_common.brow_color)'},
        'tint_resolution': TINT_RESOLUTION, 'tint_regions': TINT_REGIONS, 'mask_channels': {r: {'texture': f'TintMask{k}', 'channel': 'RGBA'[c]} for r, (k, c) in MASK_CHANNEL.items()},
        'tint_formula': 'sequential per region: out = lerp(out, tint * min(luminance(out) / max(0.03, luminance(tint_reference[region])), 1.6), mask); '
                        'luminance in linear light; masks non-colour; family atlases store tintable texels as shade x #9a9a9a so every part '
                        'of a family re-colours identically',
        'sockets': SOCKETS, 'starter_accessories': K.STARTER_ACCESSORIES + ['backpack (umm family extra)'], 'dropped_accessories': ['tv_head'],
        'families': {f: {k: F[k] for k in ('label', 'gender', 'textures', 'material', 'atlas_size', 'tint_reference_hex', 'bases',
                                          'compatibility', 'excluded_characters', 'bone_mapping_source_to_cts', 'rig_signature_sha1')}
                     | {'library': {s: {k: {kk: e.get(kk) for kk in ('fbx', 'triangles', 'source_character', 'z_range_m', 'regions', 'default_tints')}
                                       for k, e in v.items()}
                                    for s, v in F['library'].items()}} for f, F in fams.items()},
        'bases': bases, 'catalogue': catalogue, 'archetype_suggestions': arch, 'ct_hero_combos': combos, 'presets': pre,
        'balance': {'bases': len(bases), 'female': nf, 'male': nm,
                    'npc_presets': {'f': sum(p['gender'] == 'f' for p in pre['npc_presets']), 'm': sum(p['gender'] == 'm' for p in pre['npc_presets'])},
                    'combos': {'f': sum(c['gender'] == 'f' for c in combos), 'm': sum(c['gender'] == 'm' for c in combos)}},
        'excluded': {**S.HERO_EXCLUDED, **{f'umm:{k}': v for k, v in S.FAMILIES['umm']['excluded'].items()},
                     **{f'umw:{k}': v for k, v in S.FAMILIES['umw']['excluded'].items()}},
        'inventory': {'referenced_files': len(inventory), 'assets': inventory, 'unreferenced_v3_files': unref},
        'provenance': '../provenance/licence-verification-20261010.json'}
    partmap = {'version': 'art-runtime-map-v3-20261010', 'purpose': 'Unity integration input; not a shipped UI or server catalogue',
               'current_wire_fields': ['skinTone', 'headId', 'hairId', 'topId', 'bottomId', 'shoesId', 'accId'],
               'wire_status': 'no aliases registered: integrator must add baseId (or encode it in headId) before modular looks can travel',
               'bases': {}}
    for bid, b in bases.items():
        fam = b.get('family')
        e = {'family': fam, 'gender': b['gender'], 'geometry_root': b.get('geometry_root', 'art/cast/v3/export'),
             'complete_default': b['files'], 'material': b.get('material'), 'textures': b['textures'], 'tint_reference': b['tint_reference'],
             'look_defaults': b['look_defaults'], 'sockets': b['sockets'],
             'accessories': {k: {'fbx': a['fbx'], 'socket': a['socket'], 'bone': a['bone'], 'flags': a.get('flags', {})} for k, a in b['accessories'].items()},
             'wire_head_id': None}
        if fam:
            F = fams[fam]
            e['slots'] = {'Head': f"head/{b['default_parts']['head'].split('/')[1]}", 'Top': b['default_parts']['top'],
                          'Bottom': b['default_parts']['bottom'], 'Shoes': b['default_parts']['feet']}
            e['options'] = {s: sorted(F['library'][s]) for s in ('head', 'top', 'bottom', 'feet')}
            e['compatibility'] = F['compatibility']
            e['hair'] = 'hair/headwear is part of the head option (Quaternius heads carry their hair/hat); hair colour is a tint'
        else:
            e['slots'] = {'parts': list(b['parts'])}
            e['hair_styles'] = b.get('hair_library', {}).get('hair', {}); e['facial_hair'] = b.get('hair_library', {}).get('facial', {})
            e['options'] = 'hair library only; no outfit swaps'
        partmap['bases'][bid] = e
    schema = {'version': 3, 'scope': 'art-side customiser data proposal (Snapchat/Bitmoji-style); NOT the backend Look schema',
              'look': {'base': 'manifest base id (fixes gender, family, skeleton proportions)',
                       'skin': 'skin_01..skin_10 from this export skin_palette (fair -> deep); UI ids collide with older hexes: translate explicitly',
                       'parts': '{head, top, bottom, feet}: keys from the base family library (modular families only); omitted = base default',
                       'hair': 'UBC only: hair_library key or none; modular families pick hair via parts.head',
                       'facial': 'UBC m only: additive facial hair list',
                       'tints': '{skin, hair, top, bottom, shoes, accent, hat, brows}: palette names or #hex; brows default = brow_color(hair)',
                       'accessories': 'list of base accessory keys; one per socket; flags hides_hair/occludes_face/emissive'},
              'validation': ['reject parts from another family or a non-modular base', 'reject bottom/feet and top/bottom pairs listed as gaps',
                             'reject two accessories on one socket', 'sum the selected tier of every visible part + accessories and gate on budgets.triangles',
                             'resolve completely or refuse: never keep a stale default actor when a part is missing'],
              'tint_formula': manifest['tint_formula'], 'tint_resolution': TINT_RESOLUTION, 'mask_channels': manifest['mask_channels'],
              'examples': [{'base': c['base'], 'skin': c['skin'], 'parts': c.get('parts', {}), 'tints': c['tints'], 'accessories': c['accessories'],
                            'label': c['label']} for c in S.COMBOS]}
    humanoid = {'skeleton': 'CTS', 'bones': CTS_BONES, 'parent': CTS_PARENT, 'deform_bones': CTS_DEFORM, 'unity_humanoid': HUMANOID,
                'notes': ['Same hierarchy/names for every v3 base (UBC, both Quaternius modular families, v2 hero geometry).',
                          'Bone positions differ per family/base: create one Humanoid Avatar per family (umm, umw) and per UBC/hero base; '
                          'all characters of a family share one Avatar and one bind pose, so their parts are interchangeable.',
                          'Blender: characters face -Y, *_l is +X, metres; FBX exported -Z forward / Y up. Verify native facing in Unity.',
                          'Leaf bones (*_leaf, root) do not deform. Modular families have no toe weights (ball_* is a placeholder joint).']}
    out.mkdir(parents=True, exist_ok=True)
    for name, data in (('manifest_v3.json', manifest), ('runtime_part_map_v3.json', partmap), ('customizer_schema_v3.json', schema),
                       ('cts_humanoid_map.json', humanoid)):
        (out / name).write_text(json.dumps(data, indent=1) + '\n')
    if Path(validation_path).resolve() != (out / 'validation_v3.json').resolve():
        (out / 'validation_v3.json').write_bytes(Path(validation_path).read_bytes())
    print(json.dumps({'bases': len(bases), 'female': nf, 'male': nm, 'referenced_files': len(inventory), 'unreferenced': len(unref)}))

if __name__ == '__main__':
    ap = argparse.ArgumentParser(); ap.add_argument('--validation', required=True, type=Path)
    generate(ap.parse_args().validation)
