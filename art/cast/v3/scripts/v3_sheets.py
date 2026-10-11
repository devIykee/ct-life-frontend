# /// script
# dependencies = ["pillow"]
# ///
"""Compose the v3 review sheets from render indexes.
uv run v3_sheets.py <sheet> [...]   sheets: check, lineup, combos, custom, closeups
Writes JPGs to art/cast/v3/previews/ (review copies) and the evidence folder."""
import json, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

EVID = Path('/Users/mac/Documents/codes/projects/ct-life-evidence/cast-hermes-20261010')
R = EVID / 'v3' / 'renders'
EXPORT = Path('/Users/mac/Documents/codes/projects/ct-life/art/cast/v3/export')
PREV = Path('/Users/mac/Documents/codes/projects/ct-life/art/cast/v3/previews')
sys.path.insert(0, str(Path(__file__).resolve().parent))
import v3_spec as S

def font(size, bold=False):
    for p in (['/System/Library/Fonts/Supplemental/Arial Bold.ttf'] if bold else []) + ['/System/Library/Fonts/Supplemental/Arial.ttf']:
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            pass
    return ImageFont.load_default()

BG = (22, 26, 36)
def idx(name):
    return {c['id']: c for c in json.loads((R / name / 'index.json').read_text())}

def meta(bid):
    p = EXPORT / 'bases' / bid / f'{bid}.json'
    return json.loads(p.read_text()) if p.exists() else {}

def grid(cells, cols, cw, title=None, cap=46, pad=8, fs=(16, 12)):
    """cells: [(images[list of paths], label, sub)] -> PIL image"""
    F1, F2, FT = font(fs[0], True), font(fs[1]), font(24, True)
    rendered = []
    for ims, lab, sub in cells:
        st = []
        for p in ims:
            im = Image.open(p).convert('RGB'); st.append(im.resize((cw, round(im.height * cw / im.width)), Image.LANCZOS))
        rendered.append((st, lab, sub))
    ch = max(sum(i.height for i in st) for st, *_ in rendered) + cap
    rows = (len(rendered) + cols - 1) // cols
    top = 44 if title else pad
    W = cols * (cw + pad) + pad; H = top + rows * (ch + pad)
    img = Image.new('RGB', (W, H), BG); d = ImageDraw.Draw(img)
    if title:
        d.text((pad + 2, 10), title, fill=(236, 238, 244), font=FT)
    for k, (st, lab, sub) in enumerate(rendered):
        r, c = divmod(k, cols); x = pad + c * (cw + pad); y = top + r * (ch + pad); yy = y
        for im in st:
            img.paste(im, (x, yy)); yy += im.height
        d.text((x + 3, yy + 3), lab, fill=(244, 245, 248), font=F1)
        d.text((x + 3, yy + 23), sub, fill=(165, 178, 198), font=F2)
    return img

def stack(parts, title, footer, out_name):
    FT, FF = font(30, True), font(13)
    W = max(p.width for p in parts)
    H = 60 + sum(p.height for p in parts) + 30
    img = Image.new('RGB', (W, H), BG); d = ImageDraw.Draw(img)
    d.text((10, 14), title, fill=(240, 242, 246), font=FT)
    y = 60
    for p in parts:
        img.paste(p, (0, y)); y += p.height
    d.text((10, H - 22), footer, fill=(150, 156, 168), font=FF)
    PREV.mkdir(parents=True, exist_ok=True)
    for dst in (PREV / out_name, EVID / out_name):
        img.save(dst, quality=87)
    print(PREV / out_name, img.size)

FOOT = 'Blender EEVEE preview of the intended toon shader (tint masks x palette, 3-band ramp, rim, AO), arms-down pose. Not Unity, not device.'

def files(rec, views):
    return [rec['files'][v] for v in views if v in rec.get('files', {})]

def sheet_check():
    I = idx('check')
    cells = [(files(r, ['body', 'head']), r['id'], f"{r.get('triangles', 0):,} tris") for r in I.values() if 'files' in r]
    stack([grid(cells, 8, 230)], 'v3 CHECK: modular families + mixes', FOOT, 'cast_v3_check.jpg')

def label_for(bid):
    m = meta(bid)
    fam = {'umm': 'Quaternius UMM CC0', 'umw': 'Quaternius UMW CC0'}.get(m.get('family'), 'UBC CC0' if bid.startswith('ubc') else 'CC-BY hero, rare')
    return m.get('label', bid), fam

def sheet_lineup():
    I = idx('lineup')
    cells = []
    for bid, r in I.items():
        if 'files' not in r:
            continue
        lab, fam = label_for(bid)
        g = meta(bid).get('gender', '?').upper()
        cells.append((files(r, ['body', 'head']), lab, f"{g}  {r['triangles']:,} tris  {fam}"))
    nf = sum(1 for b in I if meta(b).get('gender') == 'f'); nm = sum(1 for b in I if meta(b).get('gender') == 'm')
    stack([grid(cells, 9, 220)], f'CT LIFE CAST v3: {len(cells)} distinct bases ({nf} F / {nm} M), one skeleton family each, one toon look',
          FOOT + ' Hero tier. Default looks; every modular part swaps within its family.', 'cast_v3_lineup.jpg')

def sheet_combos():
    I = idx('combos')
    by = {c['id']: c for c in S.COMBOS}
    cells = []
    for cid, r in I.items():
        if 'files' not in r:
            continue
        c = by[cid]
        cells.append((files(r, ['body', 'head']), c['label'].upper(), f"{c['base']} + {', '.join(c['accessories'])}"))
    stack([grid(cells, 8, 300, fs=(19, 13))], 'CT LIFE CAST v3: the 8 CT hero combos (base + tints + starter accessories)',
          FOOT, 'cast_v3_ct_combos.jpg')

def sheet_custom():
    I = idx('custom')
    secs = []
    sk = [(files(r, ['face']), k.split('_')[-1].replace('skin', 'skin_'), '') for k, r in I.items() if k.startswith('skin_f_') and 'files' in r]
    secs.append(grid(sk, 10, 170, '1  Skin tones (10, CIEDE2000-even steps, fair -> deep): same face, same lights'))
    sk = [(files(r, ['face']), k.split('_')[-1].replace('skin', 'skin_'), '') for k, r in I.items() if k.startswith('skin_m_') and 'files' in r]
    secs.append(grid(sk, 10, 170))
    hs = [(files(r, ['head']), k.split('_', 2)[2], ' / '.join(f'{a}:{v}' for a, v in r.get('tints', {}).items() if a == 'hair'))
          for k, r in I.items() if k.startswith('hair_') and 'files' in r]
    secs.append(grid(hs, 10, 170, '2  Head / hairstyle library (UMW 9, UMM 10) with hair-colour tints; brows follow hair'))
    mx = [(files(r, ['body']), k, '') for k, r in I.items() if k.startswith('mix_') and 'files' in r]
    secs.append(grid(mx, 12, 142, '3  Outfit mixing inside a family: top x bottom x shoes from different characters, recoloured'))
    ub = [(files(r, ['head']), k, ' '.join(f'{a}:{v}' for a, v in r.get('tints', {}).items() if a in ('hair', 'brows'))) for k, r in I.items()
          if k.startswith('ubc_hair_') and 'files' in r]
    secs.append(grid(ub, 10, 170, '4  UBC hair library with the new brow channel'))
    stack(secs, 'CT LIFE CAST v3: customisation range', FOOT, 'cast_v3_customisation.jpg')

def sheet_closeups():
    I = idx('closeups')
    pairs = []
    for b in ('ubc_m', 'ubc_f'):
        for v in ('neck', 'body'):
            a, z = I.get(f'{b}_v2_neck'), I.get(f'{b}_v3_neck')
            if a and z and 'files' in a and 'files' in z:
                pairs += [(files(a, [v]), f'{b} v2 {v}', 'before'), (files(z, [v]), f'{b} v3 {v}', 'after: weld + texel neckline')]
    for b in ('ubc_f', 'ubc_m'):
        a, z = I.get(f'{b}_v2_brows'), I.get(f'{b}_v3_brows')
        if a and z and 'files' in a and 'files' in z:
            pairs += [(files(a, ['face']), f'{b} v2 brows', 'before (hair tint on brows)'), (files(z, ['face']), f'{b} v3 brows', 'after: brow channel')]
    hs = []
    for h in S.HEROES:
        a, z = I.get(f'{h}_v2'), I.get(f'{h}_v3')
        if a and z and 'files' in a and 'files' in z:
            hs += [(files(a, ['body']), f'{h[5:]} v2', 'before'), (files(z, ['body']), f'{h[5:]} v3', 'after: material pass')]
    stack([grid(pairs, 8, 240, 'UBC defect fixes (same pose, lights, shader)'), grid(hs, 8, 240, 'CC-BY hero material pass')],
          'CT LIFE CAST v3: defect fixes, before / after', FOOT, 'cast_v3_fixes_closeups.jpg')

if __name__ == '__main__':
    for s in sys.argv[1:]:
        globals()['sheet_' + s]()
