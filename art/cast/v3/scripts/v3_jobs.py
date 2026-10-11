"""Write render job JSONs for the v3 sheets (plain Python; reads v3_spec and exported base JSONs).
python3 v3_jobs.py <name> [...]  names: check, lineup, combos, custom, closeups"""
import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import v3_spec as S

EVID = Path('/Users/mac/Documents/codes/projects/ct-life-evidence/cast-hermes-20261010')
JOBS = EVID / 'v3' / 'jobs'
EXPORT = Path('/Users/mac/Documents/codes/projects/ct-life/art/cast/v3/export')

def interleave(a, b):
    out = []
    for i in range(max(len(a), len(b))):
        if i < len(a): out.append(a[i])
        if i < len(b): out.append(b[i])
    return out

def lineup_order():
    f = [c[1] for c in S.FAMILIES['umw']['characters']] + ['ubc_f'] + [h for h, v in S.HEROES.items() if v['gender'] == 'f']
    m = [c[1] for c in S.FAMILIES['umm']['characters']] + ['ubc_m'] + [h for h, v in S.HEROES.items() if v['gender'] == 'm']
    return interleave(f, m)

def kind_of(bid):
    return 'family' if bid in S.MODULAR else ('ubc' if bid in S.UBC else 'hero')

def job_check():
    cells = [{'id': b, 'base': b, 'kind': 'family'} for b in S.MODULAR]
    cells += [{'id': 'mix_m1', 'base': 'umm_suit', 'parts': {'head': 'punk', 'feet': 'tee'}, 'tints': {'hair': 'pink'}},
              {'id': 'mix_m2', 'base': 'umm_hoodie', 'parts': {'head': 'hiker', 'bottom': 'swat', 'feet': 'swat'}, 'accessories': ['backpack']},
              {'id': 'mix_f1', 'base': 'umw_tee', 'parts': {'head': 'punk', 'top': 'suit'}, 'tints': {'hair': 'blue'}},
              {'id': 'acc_m', 'base': 'umm_tee', 'accessories': ['laser_eyes', 'chain']},
              {'id': 'acc_f', 'base': 'umw_cyber', 'accessories': ['visor', 'shoulder_drone']}]
    for c in cells:
        c.setdefault('kind', 'family')
    return {'name': 'check', 'cells': cells}

def job_lineup():
    cells = []
    for b in lineup_order():
        cells.append({'id': b, 'base': b, 'kind': kind_of(b), 'views': ['body', 'head']})
    return {'name': 'lineup', 'cells': cells}

def job_combos():
    cells = []
    for c in S.COMBOS:
        cells.append({'id': c['id'], 'base': c['base'], 'kind': kind_of(c['base']), 'skin': c['skin'], 'tints': c['tints'],
                      'accessories': c['accessories'], 'parts': c.get('parts', {}), 'views': ['body', 'head']})
    return {'name': 'combos', 'cells': cells}

def job_custom():
    tones = [f'skin_{i:02d}' for i in range(1, 11)]
    cells = []
    # A: skin ramp on one F and one M head
    for i, t in enumerate(tones):
        cells.append({'id': f'skin_f_{t}', 'base': 'umw_tee', 'kind': 'family', 'skin': t, 'views': ['face']})
    for i, t in enumerate(tones):
        cells.append({'id': f'skin_m_{t}', 'base': 'umm_tee', 'kind': 'family', 'skin': t, 'views': ['face']})
    # B: hairstyles (= family heads) with hair colours, on a fixed body
    hair_cols = ['black', 'blonde', 'pink', 'ginger', 'platinum', 'blue', 'auburn', 'green', 'purple', 'dark_brown']
    fem = [c[1].split('_', 1)[1] for c in S.FAMILIES['umw']['characters']]
    mal = [c[1].split('_', 1)[1] for c in S.FAMILIES['umm']['characters']]
    for i, h in enumerate(fem):
        cells.append({'id': f'hair_f_{h}', 'base': 'umw_tee', 'kind': 'family', 'parts': {'head': h}, 'skin': tones[(i * 3) % 10],
                      'tints': {'hair': hair_cols[i % 10]}, 'views': ['head']})
    for i, h in enumerate(mal):
        cells.append({'id': f'hair_m_{h}', 'base': 'umm_tee', 'kind': 'family', 'parts': {'head': h}, 'skin': tones[(i * 7 + 2) % 10],
                      'tints': {'hair': hair_cols[(i + 4) % 10]}, 'views': ['head']})
    # C: outfit mixing on one face per family (tops x bottoms x shoes from different characters, recoloured)
    mixes_f = [('cyber', 'tee', 'punk', {'top': 'sol_purple'}), ('suit', 'punk', 'suit', {'top': 'red'}), ('hiker', 'tactical', 'tactical', {}),
               ('worker', 'tee', 'hiker', {'accent': 'sol_green'}), ('punk', 'suit', 'punk', {'top': 'black', 'bottom': 'white'}),
               ('tee', 'hiker', 'hiker', {'top': 'teal'})]
    mixes_m = [('hoodie', 'swat', 'swat', {'top': 'sol_green'}), ('suit', 'tee', 'tee', {'top': 'white'}), ('punk', 'hiker', 'hiker', {}),
               ('worker', 'suit', 'suit', {'accent': 'pink'}), ('beach', 'farmer', 'farmer', {'top': 'orange'}),
               ('tee', 'punk', 'punk', {'top': 'purple'})]
    for i, (t, b, f, tn) in enumerate(mixes_f):
        cells.append({'id': f'mix_f{i}', 'base': 'umw_tee', 'kind': 'family', 'parts': {'top': t, 'bottom': b, 'feet': f},
                      'skin': tones[(i * 4 + 1) % 10], 'tints': tn, 'views': ['body']})
    for i, (t, b, f, tn) in enumerate(mixes_m):
        cells.append({'id': f'mix_m{i}', 'base': 'umm_tee', 'kind': 'family', 'parts': {'top': t, 'bottom': b, 'feet': f},
                      'skin': tones[(i * 3 + 5) % 10], 'tints': tn, 'views': ['body']})
    # D: UBC hair library with the new brow channel (incl. platinum)
    for i, (b, h, col) in enumerate([('ubc_f', 'Hair_BuzzedFemale', 'platinum'), ('ubc_f', 'Hair_Buns', 'pink'), ('ubc_m', 'Hair_Buzzed', 'platinum'),
                                      ('ubc_m', 'Hair_Long', 'blonde')]):
        cells.append({'id': f'ubc_hair_{i}', 'base': b, 'kind': 'ubc', 'hair': h, 'tints': {'hair': col}, 'skin': tones[(i * 3 + 1) % 10],
                      'views': ['head']})
    return {'name': 'custom', 'cells': cells}

def job_closeups():
    cells = []
    for b, sk in (('ubc_m', 'skin_06'), ('ubc_f', 'skin_03')):
        cells.append({'id': f'{b}_v2_neck', 'base': b, 'kind': 'ubc_v2', 'skin': sk, 'views': ['neck', 'body']})
        cells.append({'id': f'{b}_v3_neck', 'base': b, 'kind': 'ubc', 'skin': sk, 'views': ['neck', 'body']})
    cells.append({'id': 'ubc_f_v2_brows', 'base': 'ubc_f', 'kind': 'ubc_v2', 'hair': 'Hair_BuzzedFemale', 'tints': {'hair': 'platinum'},
                  'skin': 'skin_07', 'views': ['face']})
    cells.append({'id': 'ubc_f_v3_brows', 'base': 'ubc_f', 'kind': 'ubc', 'hair': 'Hair_BuzzedFemale', 'tints': {'hair': 'platinum'},
                  'skin': 'skin_07', 'views': ['face']})
    cells.append({'id': 'ubc_m_v2_brows', 'base': 'ubc_m', 'kind': 'ubc_v2', 'skin': 'skin_03', 'views': ['face']})
    cells.append({'id': 'ubc_m_v3_brows', 'base': 'ubc_m', 'kind': 'ubc', 'skin': 'skin_03', 'views': ['face']})
    for h in S.HEROES:
        cells.append({'id': f'{h}_v2', 'base': h, 'kind': 'hero_v2', 'views': ['body']})
        cells.append({'id': f'{h}_v3', 'base': h, 'kind': 'hero', 'views': ['body']})
    return {'name': 'closeups', 'cells': cells}

if __name__ == '__main__':
    JOBS.mkdir(parents=True, exist_ok=True)
    for name in sys.argv[1:]:
        job = globals()['job_' + name]()
        p = JOBS / f'{name}.json'
        p.write_text(json.dumps(job, indent=1))
        print(p, len(job['cells']))
