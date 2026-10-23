"""CT Life vehicles v3: review sheets + licence rows from the per-vehicle manifests.

Usage: Blender -b --factory-startup -P art/pipeline/veh_v3_sheets.py -- [--licenses]
Writes ct-life-evidence/vehicles-v3-20261021/sheet.png (every processed vehicle) and old-vs-new.png (current
client/Assets/Art/vehicles FBX renders in old/ next to their replacements). With --licenses, replaces the
<!-- BEGIN vehicles-v3-20261021 --> block at the end of art/LICENSES.md with one row per manifest.
"""
import sys, os, json, glob
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import montage

REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(REPO, 'art', 'processed', 'vehicles_v3')
EVID = '/Users/mac/Documents/codes/projects/ct-life-evidence/vehicles-v3-20261021'
ORDER = ['hovercar', 'compact', 'sedan', 'suv', 'sports', 'pickup', 'taxi', 'van', 'police', 'heli', 'bike', 'scooter']
OLD_NEW = [('compact', 'compact'), ('suv', 'suv'), ('sports', 'sports'), ('heli', 'heli'), ('drone', 'hovercar')]

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
mans = {}
for name in ORDER:
    p = os.path.join(OUT, name, 'manifest.json')
    if os.path.exists(p):
        mans[name] = json.load(open(p))

items = []
for name, m in mans.items():
    png = os.path.join(EVID, name + '.png')
    if os.path.exists(png):
        t = m['tris']
        items.append((png, f"{name} {t['LOD0'] // 1000}k/{t['LOD1'] // 1000}k/{t['LOD2'] / 1000:.1f}k"))
montage.montage(os.path.join(EVID, 'sheet.png'), 4, 640, items, title='CT Life vehicles v3 - same lookdev, LOD0/1/2 tris', font_size=18)

pairs = []
for old, new in OLD_NEW:
    po, pn = os.path.join(EVID, 'old', old + '.png'), os.path.join(EVID, new + '.png')
    if os.path.exists(po) and os.path.exists(pn):
        pairs += [(po, f'OLD {old}'), (pn, f'NEW {new}')]
montage.montage(os.path.join(EVID, 'old-vs-new.png'), 2, 720, pairs, title='Current (left) vs v3 replacement (right)', font_size=22)

if '--licenses' in argv:
    lic_path = os.path.join(REPO, 'art', 'LICENSES.md')
    text = open(lic_path).read()
    begin, end = '<!-- BEGIN vehicles-v3-20261021 -->', '<!-- END vehicles-v3-20261021 -->'
    rows = []
    for name, m in mans.items():
        sa = ' The modified model stays CC-BY-SA 4.0 (ShareAlike): redistributing it, or a derivative, must keep that licence.' if 'ShareAlike' in m['licence'] else ''
        rows.append(f"| \"{m['title']}\" — credit: {m['credit']} | {m['url']} | {m['licence']} (Sketchfab API v3, read {m['licence_checked'][-10:]}){sa} | "
                    f"{m['creator']} | 2026-10-21 | `art/processed/vehicles_v3/{name}/` ({m['class']}); modified by CT Life (decimated, re-pivoted, atlas) |")
    block = '\n'.join([begin, '## Vehicles v3 (2026-10-21, art/processed/vehicles_v3)', '',
                       'Sketchfab downloads through the user\'s logged-in Chrome (blanket approval relayed by the coordinator); raw GLB + meta.json in '
                       'ct-life/art/raw/sketchfab/<uid>/ (gitignored). Credit lines are also in each manifest.json. Brand badges removed '
                       '(DZ *_Badges meshes, scooter sticker); all designs are the authors\' fictional cars.', '',
                       '| Name | Source URL | Licence | Author | Downloaded | Used for |', '| --- | --- | --- | --- | --- | --- |'] + rows + [end])
    if begin in text:
        a = text.index(begin); b = text.index(end) + len(end)
        text = text[:a] + block + text[b:]
    else:
        text = text.rstrip('\n') + '\n\n' + block + '\n'
    open(lic_path, 'w').write(text)
    print('[sheets] licences:', len(rows), 'rows')
print('[sheets] done', len(items), 'vehicles,', len(pairs) // 2, 'pairs')
