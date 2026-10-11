"""Aggregate per-family look manifests + part indexes into art/cast/export/manifest.json.
Plain python3 (no Blender). usage: python3 art/cast/scripts/cast_manifest.py
"""
import json, hashlib
from pathlib import Path

REPO = Path('/Users/mac/Documents/codes/projects/ct-life')
EVID = Path('/Users/mac/Documents/codes/projects/ct-life-evidence/cast/families')
EXPORT = REPO / 'art/cast/export'
CAPS = {'hero': 12000, 'lod0': 6000, 'lod1': 3000, 'lod2': 1200}

contract = json.loads((REPO / 'contract/ids/archetypes.json').read_text())['archetypes']
contract_ids = sorted({a['id'] for a in contract})

looks = {}
for fam in ('m', 'f'):
    p = EVID / fam / f'manifest_{fam}.json'
    if p.exists():
        looks.update(json.loads(p.read_text()))
parts = {}
for fam in ('m', 'f'):
    p = EXPORT / 'parts' / fam / 'parts.json'
    if p.exists():
        parts[fam] = json.loads(p.read_text())

problems = []
for lid, e in looks.items():
    for tier, t in e['tiers'].items():
        if t['triangles'] > CAPS[tier]:
            problems.append(f'{lid} {tier} {t["triangles"]} > {CAPS[tier]}')
        if t['max_influences'] > (2 if tier == 'lod2' else 4):
            problems.append(f'{lid} {tier} influences {t["max_influences"]}')
    for k, f in e['files'].items():
        fp = REPO / f
        if not fp.exists():
            problems.append(f'{lid} missing {f}')
        else:
            e.setdefault('sha256', {})[Path(f).name] = hashlib.sha256(fp.read_bytes()).hexdigest()

binding = {}
for lid, e in looks.items():
    a = e.get('archetype')
    if a:
        binding.setdefault(a, []).append(lid)
        e['archetype_in_contract'] = a in contract_ids
        if a not in contract_ids:
            e['archetype_status'] = 'proposed id (not in contract/ids/archetypes.json)'
missing = [c for c in contract_ids if c not in binding]

manifest = {
    'version': '2026-10-08',
    'status': 'Functional cast for integration; looks await the user\'s art approval.',
    'families': {
        'm': {'base': 'Snow v4 adult derivative', 'rig': 'SnowAdult_Rig', 'skeleton': '55 bones, unchanged from art/character-candidates/unity-snow (unity-humanoid-map.json applies)',
              'credit': 'Snow Rig (CC) Blender Foundation | studio.blender.org', 'licence': 'CC-BY 4.0', 'source': 'https://studio.blender.org/characters/snow/v4/'},
        'f': {'base': 'Rain v3 adult derivative', 'rig': 'RainAdult_Rig', 'skeleton': '55 bones, unchanged from art/character-candidates/rain-adult-production (unity-humanoid-map.json applies)',
              'credit': 'Rain Rig (CC) Blender Foundation | studio.blender.org', 'licence': 'CC-BY 4.0', 'source': 'https://studio.blender.org/characters/rain/v3/'},
    },
    'budgets': {'triangles': CAPS, 'max_bone_influences': {'hero': 4, 'lod0': 4, 'lod1': 4, 'lod2': 2},
                'materials_per_look': 1, 'atlas': {'hero_looks': 1024, 'crowd_looks': 512}},
    'unity_import': {
        'fbx': 'Scale 1, forward -Z, up Y (Blender export axis_forward=-Z, axis_up=Y, FBX_SCALE_UNITS). Rig: Humanoid. '
               'Build each family Avatar once from the existing creator FBX / unity-humanoid-map.json and use Copy From Other Avatar for every look and part of that family. '
               'Meshes named <look>_LOD0/_LOD1/_LOD2 in <look>.fbx create a LODGroup automatically; <look>_Hero.fbx is the near/creator tier.',
        'textures': 'BaseColor sRGB; Normal = tangent-space OpenGL (+Y), import as Normal Map; MetallicSmoothness linear (R metal, A smoothness), '
                    'disable sRGB, alpha is data (not transparency); Emission sRGB when present. URP/Lit opaque, one material per look.',
        'scale': 'Per-look uniform root scale for height variety is in looks[*].scale (skeleton unchanged).',
        'runtime_parts': 'art/cast/export/parts/<fam>/parts.json: every part on the same rig, slot UVs in a fixed 8x8 atlas layout. '
                         'Merge chosen parts into one SkinnedMeshRenderer, copy each part\'s slot texture rect into one atlas (Graphics.CopyTexture), '
                         'drop body chunks listed in hides_body_regions, tint by the Mask sub ids.',
    },
    'contract_binding': {'archetype_to_looks': binding, 'contract_ids_without_look': missing,
                         'proposed_ids_not_in_contract': sorted(a for a in binding if a not in contract_ids)},
    'looks': looks,
    'parts': {fam: {'index': f'parts/{fam}/parts.json', 'count': len(p['parts'])} for fam, p in parts.items()},
    'validation_problems': problems,
}
(EXPORT / 'manifest.json').write_text(json.dumps(manifest, indent=1))
kinds = {}
for e in looks.values():
    kinds[e.get('kind')] = kinds.get(e.get('kind'), 0) + 1
print('looks', len(looks), kinds, 'contract ids covered', len(contract_ids) - len(missing), '/', len(contract_ids), 'missing', missing)
print('problems', problems[:20])
