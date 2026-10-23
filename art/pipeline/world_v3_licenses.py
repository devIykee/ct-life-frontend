"""World v3: (re)write the world-v3 section of art/LICENSES.md from the processed manifests + raw meta.json files.
Run: python3 art/pipeline/world_v3_licenses.py"""
import json, os, re
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
LIC = os.path.join(REPO, 'art', 'LICENSES.md')
MAN = os.path.join(REPO, 'art', 'processed', 'world_v3', 'manifest.json')
RAW = os.environ.get('CTL_SF_RAW', '/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab')
man = json.load(open(MAN))
by_src = {}
for k, v in man.items():
    key = v.get('source_uid') or v.get('source')
    by_src.setdefault(key, dict(v=v, ids=[]))['ids'].append(k)
rows = []
for key, d in by_src.items():
    v = d['v']
    m = {}
    mp = os.path.join(RAW, str(key), 'meta.json')
    if os.path.exists(mp):
        import sys
        sys.path.insert(0, HERE)
        m = json.load(open(mp))
        if 'display' not in m and 'lic' not in m:
            import ast
            u = m.get('user'); u = ast.literal_eval(u) if isinstance(u, str) and u.startswith('{') else (u or {})
            m = dict(m, display=u.get('displayName') or u.get('username'), user=u.get('username'))
    name = m.get('name') or re.sub(r'^"|".*$', '', v.get('credit', '')) or key
    author = m.get('display') or m.get('user') or ''
    lic = v.get('licence', '')
    if 'Free Standard' in lic or 'Standard' in lic:
        lic = 'Sketchfab "Free Standard" (Fab Standard licence; text not readable, fab.com blocked the fetch) [Unverified: crypto/NFT terms]'
    ids = sorted(d['ids'])
    used = ', '.join(ids[:6]) + (f' (+{len(ids) - 6} more)' if len(ids) > 6 else '')
    rows.append(f'| "{name}" — credit: {v.get("credit", "")} | {v.get("source", "")} | {lic} | {author} | 2026-10-22 | '
                f'art/processed/world_v3/{v.get("family", "")}/: {used} |')
sec = ['<!-- BEGIN world-v3-20261022 -->', '## World v3 semi-real kit (2026-10-22, art/processed/world_v3)', '',
       'Sketchfab downloads via the user\'s logged-in browser (blanket approval relayed by the coordinator); raw files and meta.json in '
       'ct-life/art/raw/sketchfab/<uid>/ (gitignored). All modified by CT Life (split, decimated, atlased). CC-BY items need the credit line in-game.', '',
       '| Name | Source URL | Licence | Author | Downloaded | Used for |', '| --- | --- | --- | --- | --- | --- |'] + rows + ['<!-- END world-v3-20261022 -->']
txt = open(LIC).read()
if '<!-- BEGIN world-v3-20261022 -->' in txt:
    txt = re.sub(r'<!-- BEGIN world-v3-20261022 -->.*?<!-- END world-v3-20261022 -->', '\n'.join(sec), txt, flags=re.S)
else:
    txt = txt.rstrip('\n') + '\n\n' + '\n'.join(sec) + '\n'
open(LIC, 'w').write(txt)
print('LICENSES world-v3 rows:', len(rows))
