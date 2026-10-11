# /// script
# dependencies = ["pillow", "numpy"]
# ///
"""Cast v3 hero material pass (texture level; v2 hero geometry/FBX unchanged and hash-pinned).

Why the v2 heroes looked washed out: (1) the v2 preview re-hued whole outfits (every texel of a 'top'/'bottom'
region) to ONE colour = the region median, flattening multi-colour suits to grey; (2) low-contrast/desaturated
source albedos; (3) hero_balaclava baked a uniform white emission map. Fix here:
  * colour grade per hero (contrast around the median, saturation lift, black/white point) so heroes sit with the
    flat-colour Quaternius cast under the same toon shader;
  * tint regions re-derived per texel: in each body slot the dominant colour cluster becomes the tintable fabric
    (top/bottom/shoes), the second cluster 'accent', everything else stays authored (visors, straps, prints);
  * bogus emission dropped (emissive area > 20 % of the body = baked white factor, not a light source).
usage: uv run v3_heroes.py
"""
import json, hashlib
from pathlib import Path
import numpy as np
from PIL import Image

REPO = Path('/Users/mac/Documents/codes/projects/ct-life')
V2E = REPO / 'art/cast/v2/export'
V3E = REPO / 'art/cast/v3/export'
HEROES = {
    # contrast, saturation, gamma (mid-tone lift <1 brightens), look defaults (cloth palette names or hex)
    'hero_assassin': {'contrast': 1.18, 'sat': 1.22, 'gamma': 0.95, 'look': {}},
    'hero_gynoid': {'contrast': 1.1, 'sat': 1.0, 'gamma': 1.0, 'gain': 0.6, 'look': {'top': '#b9bfcf', 'bottom': '#b9bfcf', 'shoes': '#b9bfcf'}},
    'hero_hazmat': {'contrast': 1.05, 'sat': 1.1, 'gamma': 1.0, 'regions': 'v2', 'look': {'top': '#4b5139', 'bottom': '#3d4231'}},
    'hero_balaclava': {'contrast': 1.2, 'sat': 1.25, 'gamma': 0.92, 'look': {'top': '#1f2c4a', 'bottom': '#3a3d44', 'accent': '#c8102e', 'skin': '#8e6145'}},
}
GRID = 8
SLOT = {'head': (0, 4, 4, 4), 'hair': (4, 6, 4, 2), 'top': (0, 1, 4, 3), 'bottom': (4, 3, 4, 3), 'feet': (4, 1, 2, 2),
        'acc': (6, 1, 2, 2), 'body': (0, 0, 8, 1)}
REGION_CH = {'skin': (0, 0), 'hair': (0, 1), 'top': (0, 2), 'bottom': (0, 3), 'shoes': (1, 0), 'accent': (1, 1), 'brows': (1, 2), 'hat': (1, 3)}

def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)

def lum(c):
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722

def load(p, mode):
    return np.asarray(Image.open(p).convert(mode), np.float32) / 255

def save(p, a):
    a = np.clip(np.rint(a * 255), 0, 255).astype(np.uint8)
    Image.fromarray(a, 'RGBA' if a.shape[2] == 4 else 'RGB').save(p, optimize=True)

def slot_mask(name, res):
    x, y, w, h = SLOT[name]; c = res // GRID
    m = np.zeros((res, res), bool)
    # image row 0 = top; slot y counts from the bottom
    r0, r1 = res - (y + h) * c, res - y * c
    m[r0:r1, x * c:(x + w) * c] = True
    return m

def kmeans(X, k, iters=20, seed=0):
    rng = np.random.default_rng(seed)
    C = X[rng.choice(len(X), size=min(k, len(X)), replace=False)]
    for _ in range(iters):
        d = ((X[:, None, :] - C[None, :, :]) ** 2).sum(-1)
        lab = d.argmin(1)
        for j in range(len(C)):
            if (lab == j).any():
                C[j] = X[lab == j].mean(0)
    return C

def hexs(c):
    return '#%02x%02x%02x' % tuple(int(round(float(x) * 255)) for x in np.clip(c, 0, 1))

def process(bid, P):
    src = V2E / 'bases' / bid
    meta = json.loads((src / f'{bid}.json').read_text())
    alb = load(src / f'{bid}_Albedo.png', 'RGB')
    m0 = load(src / f'{bid}_TintMask0.png', 'RGBA'); m1 = load(src / f'{bid}_TintMask1.png', 'RGBA')
    res = alb.shape[0]
    acc = slot_mask('acc', res)
    body = ~acc
    lin = to_lin(alb)
    # --- colour grade (body only)
    L = lum(lin); med = float(np.median(L[body]))
    Ln = np.clip(((L / max(med, 1e-4)) ** P['contrast']) * med, 0, 1) ** P['gamma']
    lin2 = lin * (Ln / np.maximum(L, 1e-5))[..., None]
    g = lum(lin2)[..., None]
    lin2 = np.clip(g + (lin2 - g) * P['sat'], 0, 1) * P.get('gain', 1.0)   # gain < 1: pull paper-white suits off the clip
    out = np.where(body[..., None], lin2, lin)
    # --- regions per texel (body slots); head keeps its v2 skin/eye regions
    m0n = np.zeros_like(m0); m1n = np.zeros_like(m1)
    m0n[..., 0] = m0[..., 0]                        # skin from v2 (head/face skin where present)
    m1n[acc] = m1[acc]                              # accessory palette accent swatch
    regions_report = {}
    refs = {}
    slots = (('top', 'top'), ('bottom', 'bottom'), ('feet', 'shoes'), ('body', None))
    if P.get('regions') == 'v2':
        m0n = m0.copy(); m1n = m1.copy(); slots = ()
        for r, (k, ch) in REGION_CH.items():
            sel = (m0 if k == 0 else m1)[..., ch] > 0.5
            sel &= body
            if sel.sum() > 100 and r != 'skin':
                refs.setdefault(r, []).append(np.median(to_srgb(out[sel]), axis=0))
        regions_report['mode'] = 'v2 region masks kept (whole suit tintable); references re-measured on the graded albedo'
    for slot, main in slots:
        sm = slot_mask(slot, res) & (m0[..., 0] < 0.5)
        if sm.sum() < 100:
            continue
        X = to_srgb(out[sm]).reshape(-1, 3)
        sub = X[::max(1, len(X) // 20000)]
        C = kmeans(sub.copy(), 4)
        lab = ((X[:, None, :] - C[None]) ** 2).sum(-1).argmin(1)
        cnt = np.bincount(lab, minlength=len(C))
        order = np.argsort(-cnt)
        assign = np.full(len(X), '', dtype=object)
        regmap = {}
        if main:
            regmap[order[0]] = main
            if len(order) > 1 and cnt[order[1]] > 0.18 * cnt.sum() and np.abs(C[order[1]] - C[order[0]]).sum() > 0.15:
                regmap[order[1]] = 'accent'
        else:   # 'body' strip (arm/leg remnants): dominant -> top
            regmap[order[0]] = 'top'
        idx = np.argwhere(sm)
        for j, r in regmap.items():
            sel = lab == j
            k, ch = REGION_CH[r]
            tgt = m0n if k == 0 else m1n
            tgt[idx[sel, 0], idx[sel, 1], ch] = 1.0
            refs.setdefault(r, []).append(np.median(X[sel], axis=0))
        regions_report[slot] = {'clusters': [hexs(C[j]) for j in order], 'share': [round(float(cnt[j] / cnt.sum()), 3) for j in order],
                                'assigned': {hexs(C[j]): r for j, r in regmap.items()}}
    tint_ref = {r: hexs(np.median(np.array(v), axis=0)) for r, v in refs.items()}
    skin_sel = m0[..., 0] > 0.5
    skin_note = 'no skin texels'
    if skin_sel.sum() > 50:
        sc = np.median(to_srgb(out[skin_sel]), axis=0)
        sl = float(lum(to_lin(sc[None]))[0]); warm = float(sc[0] - sc[2])
        if sl > 0.12 and warm > 0.06:
            tint_ref['skin'] = hexs(sc); skin_note = f'skin kept ({hexs(sc)})'
        else:   # masked heroes: v2 'skin' texels are mask/visor material, never re-hue them with a skin tone
            m0n[..., 0] = 0; skin_note = f'skin mask dropped (median {hexs(sc)} is not skin)'
    regions_report['skin'] = skin_note
    # --- emission
    em_out = None
    emp = src / f'{bid}_Emission.png'
    em_report = 'none'
    if emp.exists():
        E = load(emp, 'RGB')
        lit = (E.max(-1) > 0.08) & body
        frac = float(lit.sum() / max(1, body.sum()))
        if frac > 0.20:
            E[body] = 0
            em_report = f'dropped body emission ({frac:.0%} of the body texels were emissive: baked white factor)'
        else:
            em_report = f'kept ({frac:.1%} of body texels emissive)'
        em_out = E
    dst = V3E / 'bases' / bid
    dst.mkdir(parents=True, exist_ok=True)
    tex = {'albedo': dst / f'{bid}_v3_Albedo.png', 'mask0': dst / f'{bid}_v3_TintMask0.png', 'mask1': dst / f'{bid}_v3_TintMask1.png'}
    save(tex['albedo'], to_srgb(out)); save(tex['mask0'], m0n); save(tex['mask1'], m1n)
    if em_out is not None and em_out.max() > 0.02:
        tex['emission'] = dst / f'{bid}_v3_Emission.png'; save(tex['emission'], em_out)
    look = dict(tint_ref)
    look.update(P['look'])
    v2files = {k: v for k, v in meta['files'].items()}
    pinned = {rel: sha(V2E / rel) for rel in list(v2files.values()) + [p['fbx'] for p in meta['parts'].values()]
              + [a['fbx'] for k, a in meta['accessories'].items() if k != 'tv_head']}
    out_meta = {
        'id': bid, 'label': meta['label'], 'gender': meta['gender'], 'family': None, 'source': meta['source'],
        'licence': meta['licence'], 'credit_required': True, 'archetypes': meta['archetypes'], 'rare_masked_look': True,
        'geometry': 'v2 export, unchanged (paths relative to art/cast/v2/export, sha256-pinned)', 'geometry_root': 'art/cast/v2/export',
        'files': v2files, 'parts': meta['parts'], 'tiers': meta['tiers'], 'sockets': meta['sockets'],
        'accessories': {k: a for k, a in meta['accessories'].items() if k != 'tv_head'},
        'textures': {k: str(v.relative_to(V3E)) for k, v in tex.items()},
        'tint_reference': tint_ref, 'look_defaults': look,
        'material_pass': {'contrast': P['contrast'], 'saturation': P['sat'], 'gamma': P['gamma'], 'gain': P.get('gain', 1.0), 'regions': regions_report,
                          'emission': em_report, 'v2_inputs': {k: sha(src / f'{bid}_{k}.png') for k in ('Albedo', 'TintMask0', 'TintMask1')}},
        'pinned_v2_files_sha256': pinned, 'height_m': meta['height_m'], 'measurements': meta.get('measurements')}
    (dst / f'{bid}.json').write_text(json.dumps(out_meta, indent=1))
    print(bid, json.dumps({'refs': tint_ref, 'emission': em_report}))

for bid, P in HEROES.items():
    process(bid, P)
