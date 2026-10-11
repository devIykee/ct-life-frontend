"""Recompute modular base look_defaults from the stored authored colours + the current v3_spec looks (plain Python,
no Blender; geometry/textures untouched). python3 v3_relook.py"""
import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import v3_common as K
import v3_spec as S

PAL = {t['id']: t['hex'] for t in json.loads((K.V2EXPORT / 'skin_palette.json').read_text())['tones']}

def resolve(region, v):
    if v.startswith('#'):
        return v
    if region in ('hair', 'brows'):
        return K.HAIR_COLORS[v]
    if region == 'hat':
        return K.HAT_COLORS.get(v) or K.CLOTH_COLORS[v]
    return K.CLOTH_COLORS[v]

for bid, spec in S.MODULAR.items():
    p = K.EXPORT / 'bases' / bid / f'{bid}.json'
    meta = json.loads(p.read_text())
    authored = meta['tint_authored']
    lk = dict(authored)
    for r, v in spec['look'].items():
        if r != 'skin' and r in authored:
            lk[r] = resolve(r, v)
    if 'hair' in lk and 'brows' in lk:
        lk['brows'] = K.brow_color(lk['hair'])
    lk['skin'] = PAL[spec['look']['skin']]
    new = {'skin_tone': spec['look']['skin'], **lk}
    if new != meta['look_defaults'] or meta['archetypes'] != spec['archetypes'] or meta['label'] != spec['label']:
        meta['look_defaults'] = new; meta['archetypes'] = spec['archetypes']; meta['label'] = spec['label']
        p.write_text(json.dumps(meta, indent=1))
        print('updated', bid)

# per-part default tints: the source character's look for every region the part carries (used when a swapped-in part brings a
# region the wearer's base look does not define, e.g. a hard hat on a bare-headed base). Rule (manifest tint_resolution):
# tints = part defaults (feet, bottom, top, head order) <- base look_defaults <- user tints; brows follow the final hair.
for fam in S.FAMILIES:
    fp = K.EXPORT / 'families' / fam / f'{fam}.json'
    F = json.loads(fp.read_text())
    changed = False
    for slot, entries in F['library'].items():
        for key, e in entries.items():
            src = e.get('source_character')
            if not src:
                continue
            look = json.loads((K.EXPORT / 'bases' / src / f'{src}.json').read_text())['look_defaults']
            dt = {r: look[r] for r in e.get('regions', {}) if r in look and r in K.TINT_REGIONS}
            if e.get('default_tints') != dt:
                e['default_tints'] = dt; changed = True
    if changed:
        fp.write_text(json.dumps(F, indent=1))
        print('part default tints', fam)
