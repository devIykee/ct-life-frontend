#!/usr/bin/env python3
"""Generate the character section of art/LICENSES.md from the pipeline config + MPFB pack metadata.

Usage: python3 art/pipeline/licenses_gen.py
Rewrites the block between <!-- BEGIN characters --> and <!-- END characters --> in art/LICENSES.md.
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C

DATE = '2026-10-05'
PACKS = os.path.join(C.MPFB_DATA, 'packs')
pack_index = {}
for f in sorted(os.listdir(PACKS)):
    if f.endswith('.json'):
        for name, meta in json.load(open(os.path.join(PACKS, f))).items():
            pack_index[name] = (f[:-5], meta)

used = {}  # asset -> where used


def use(asset, where):
    used.setdefault(asset, set()).add(where)


for sex, cfg in C.BODIES.items():
    use(cfg['proxy'].split('/')[0], f'{sex} body mesh (proxy)')
    use(cfg['eyebrows'].split('/')[0], f'{sex} skin texture (eyebrows baked in)')
    use(cfg['eyelashes'].split('/')[0], f'{sex} skin texture (lash line baked in)')
    use(cfg['skin'].split('/')[0], f'{sex} skin texture detail')
    use('low-poly', f'{sex} eyes mesh')
for name in set(C.EYE_COLORS.values()):
    use(name, 'eye textures')
for sex, parts in C.MPFB_PARTS.items():
    for key, (path, slot, tile) in parts.items():
        use(path.split('/')[0], f'{sex} part `{key}` ({slot})')
for sex, hairs in C.MPFB_HAIRS.items():
    for key, path in hairs.items():
        use(path.split('/')[0], f'{sex} hair `hair_{key}`')

rows = []
for asset, where in sorted(used.items()):
    pack, meta = pack_index.get(asset, ('makehuman_system_assets', {}))
    author = meta.get('author') or 'MakeHuman team'
    lic = meta.get('license') or 'CC0'
    src = meta.get('source') or ''
    url = f'http://static.makehumancommunity.org/assets/assetpacks/{pack}.html'
    rows.append(f'| MakeHuman asset `{asset}` (pack `{pack}`) | {url} | {lic} | {author} | {DATE} | {"; ".join(sorted(where))} |')

block = [
    '<!-- BEGIN characters -->',
    '## Characters and animations',
    '',
    '| Name | Source URL | Licence | Author | Downloaded | Used for |',
    '| --- | --- | --- | --- | --- | --- |',
    f'| MPFB 2.0.17 (Blender add-on, tool only, not shipped) | https://extensions.blender.org/add-ons/mpfb/ | GPL-3.0 (code); generated meshes and all MakeHuman core assets CC0 per http://static.makehumancommunity.org/about/license.html | Joel Palmius / MakeHuman Community | {DATE} | generating bodies, fitting clothes, rigging (game_engine rig) |',
    f'| MakeHuman base mesh hm08 + targets (shipped inside MPFB 2.0.17) | https://extensions.blender.org/add-ons/mpfb/ | CC0 | MakeHuman team | {DATE} | male/female body shapes, rig weights, jaw weights |',
    f'| Quaternius — Universal Base Characters [Standard] (hairstyles only) | https://quaternius.itch.io/universal-base-characters (via https://quaternius.com/packs/universalbasecharacters.html) | CC0 1.0 (License_Standard.txt in zip) | Quaternius | {DATE} | hair_buzzed, hair_parted, hair_long, hair_buns, hair_beard (fitted onto our heads; textures T_Hair_1/2 recoloured) |',
    f'| Quaternius — Universal Animation Library [Standard] v3.0 | https://quaternius.itch.io/universal-animation-library | CC0 1.0 (License.txt in zip) | Quaternius (animations with Gonzalo Furnier) | {DATE} | retargeted clips listed in art/processed/animations/clips.json (source = "UAL1") |',
    f'| Quaternius — Universal Animation Library 2 [Standard] | https://quaternius.itch.io/universal-animation-library-2 | CC0 1.0 (License.txt in zip) | Quaternius | {DATE} | retargeted clips listed in clips.json (source = "UAL2") |',
    f'| CT Life procedural parts (fleece vest, trench coat, hood, hard hat, headset, ring light, gold chain, siren hat, baseball cap, shades/laser eyes, ORE backpack, straw hat, lanyard, fox mask, phone, megaphone, binoculars) | art/pipeline/char_custom.py | CC0 (made by us) | CT Life assets agent | {DATE} | modular parts + NPC presets |',
    f'| CT Life hand-keyed emotes | art/pipeline/anim_emotes.py | CC0 (made by us) | CT Life assets agent | {DATE} | clips marked "hand-keyed" in clips.json |',
] + rows + ['<!-- END characters -->']

path = os.path.join(C.ART, 'LICENSES.md')
text = open(path).read() if os.path.exists(path) else (
    '# Art licences\n\nEvery third-party file used under art/ is listed here. Rule: CC0 first, CC-BY only if nothing '
    'CC0 fits (with attribution). Banned: Synty, Unity Asset Store, ActorCore, Mixamo, Ready Player Me.\n\n'
    '<!-- BEGIN characters -->\n<!-- END characters -->\n\n<!-- BEGIN environment -->\n<!-- END environment -->\n')
text = re.sub(r'<!-- BEGIN characters -->.*?<!-- END characters -->', '\n'.join(block), text, flags=re.S)
open(path, 'w').write(text)
print('wrote', path, len(rows), 'MakeHuman asset rows')


# ================================================================== environment section
import env_config as EC

inv = json.load(open(os.path.join(C.RAW, 'env_inventory.json')))
packs = {p['local_path'].replace('art/raw/', ''): p for p in inv['packs']}
env_man = json.load(open(os.path.join(C.PROCESSED, 'environment', 'manifest.json')))
used_by_pack = {}
for it in EC.ITEMS:
    if it['id'] not in env_man:
        continue
    pk = it['src'].split('::')[0]
    used_by_pack.setdefault(pk, []).append(it['id'])
rows = []
for pk, ids in sorted(used_by_pack.items()):
    if pk.startswith('ctlife_custom'):
        continue
    p = packs.get(pk)
    if not p:
        rows.append(f'| {pk} | (see art/raw/env_inventory.json) | CC0 | | {DATE} | {", ".join(sorted(ids))} |')
        continue
    rows.append(f"| {p['name']} | {p['source_page']} (download: {str(p['download_url']).split(' ')[0]}) | {p['licence']} | {p['author']} | "
                f"{p['date_downloaded']} | {', '.join(sorted(ids))} |")
# Poly Haven textures used by the procedural build kit (env_custom.py)
ph = json.load(open(os.path.join(C.RAW, 'polyhaven', '_manifest.json')))
ph_used = {'wood_floor': 'floor_wood_2m', 'floor_tiles_06': 'floor_tiles_2m', 'painted_plaster_wall': 'wall_plaster_*_2m',
           'red_brick': 'wall_brick_*_2m', 'marble_01': 'floor_marble_2m', 'rectangular_paving': 'plaza_paving_2m',
           'hexagonal_concrete_paving': 'plaza_hexpaving_2m', 'concrete_floor_02': 'floor_concrete_2m', 'metal_plate': 'floor_metal_2m'}
for a in ph:
    if a['id'] in ph_used:
        authors = ', '.join(a.get('authors', {}).keys())
        rows.append(f"| Poly Haven texture `{a['name']}` (1k diffuse only) | {a['page']} | CC0 | {authors} | {DATE} | {ph_used[a['id']]} |")
own = sorted(i for i in env_man if EC.ITEMS and any(it['id'] == i and it['src'].startswith('ctlife_custom') for it in EC.ITEMS))
# every other downloaded pack (brief: every downloaded file gets a row)
for pk, p in sorted(packs.items()):
    if pk in used_by_pack:
        continue
    note = 'downloaded, not used'
    if 'polypizza' in pk:
        note = 'downloaded, NOT used: [Unverified] licence is only the uploader declaration on an aggregator'
    rows.append(f"| {p['name']} | {p['source_page']} | {p['licence']} | {p['author']} | {p['date_downloaded']} | {note} |")
for z in sorted(os.listdir(os.path.join(C.RAW, 'mpfb'))):
    if z.endswith('_cc0.zip'):
        pk = z[:-8]
        rows.append(f"| MakeHuman asset pack `{pk}` (zip) | http://static.makehumancommunity.org/assets/assetpacks/{pk}.html | CC0 | MakeHuman community (per-asset authors in the pack json) | {DATE} | installed for MPFB; assets actually used are listed in the characters table |")
rows.append(f"| CT Life procedural environment pieces (art/pipeline/env_custom.py) | made in this repo | CC0 (made by us) | CT Life assets agent | {DATE} | {', '.join(own)} |")
rows.append(f"| CT Life city palette `ctl_palette.png` + recoloured `*_steel.png`, `BlossomTree_Leaves` (tint of Quaternius MapleTree_Leaves_BW, CC0) | art/pipeline/env_process.py | CC0 (derived from CC0) | CT Life assets agent | {DATE} | every palettised mesh, ORE refinery, tree_blossom_* |")
block = ['<!-- BEGIN environment -->', '## Environment, props and nature', '',
         'Rejected / not used: poly.pizza picks (aggregator; licence only uploader-declared -> replaced by our own stage/mic/speaker models), '
         'Quaternius Downtown City buildings (too heavy for mobile: 18-45k tris, 12 materials), Synty / Unity Asset Store / Mixamo (banned).', '',
         '| Name | Source URL | Licence | Author | Downloaded | Used for (asset ids) |', '| --- | --- | --- | --- | --- | --- |'] + rows + ['<!-- END environment -->']
text = open(path).read()
text = re.sub(r'<!-- BEGIN environment -->.*?<!-- END environment -->', '\n'.join(block), text, flags=re.S)
open(path, 'w').write(text)
print('environment rows', len(rows))
