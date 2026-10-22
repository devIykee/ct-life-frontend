"""World v3: merge the per-family manifests into art/processed/world_v3/manifest.json in the island-kit format
(the same keys IslandV2Kit / dress.py read from art/processed/world/manifest.json):
  id -> fbx (relative to art/processed), tris [LOD0, LOD1, LOD2], size_m [w, d, h] (Blender x, y, z = Unity x, z, y),
        materials (FBX material names), textures (paths relative to art/processed), tags (incl. 'kit:<k>', kind), plus
        kind, source, source_uid, licence, credit, door_main (enterable shells), lod2_material.
Run: python3 art/pipeline/world_v3_manifest.py
"""
import json, os
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'processed', 'world_v3')
out = {}
for fam in sorted(os.listdir(ROOT)):
    p = os.path.join(ROOT, fam, 'manifest.json')
    if not os.path.isfile(p):
        continue
    for k, v in json.load(open(p)).items():
        v = dict(v)
        v['family'] = fam
        v['textures'] = [t if '/' in t else f'world_v3/{fam}/textures/{t}' for t in v.get('textures', [])]
        tags = list(v.get('tags', []))
        if v.get('kind') and v['kind'] not in tags:
            tags.append(v['kind'])
        v['tags'] = tags
        out[k] = v
json.dump(out, open(os.path.join(ROOT, 'manifest.json'), 'w'), indent=1)
kinds = {}
for v in out.values():
    kinds[v.get('kind', '?')] = kinds.get(v.get('kind', '?'), 0) + 1
print('world_v3 manifest:', len(out), 'assets', kinds)
