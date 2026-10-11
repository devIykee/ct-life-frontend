"""Cast specification: part registry, texture recipes (procedural, CT parody prints) and the looks.

Parody rule: no real logos, brands, NFT PFPs or real people. All prints are original text/marks.
"""
import json, math
import numpy as np
from cast_common import *
import cast_tex as T

BASE_PARTS = {
    'head': 'HEAD', 'hair': 'HAIR', 'skin': 'SKIN', 'legs': 'SKIN', 'top': 'TOP', 'bottom': 'BOTTOM',
    'shoes': 'SHOES', 'scarf': 'ACC_C',
}
_reg = {}
_meta = {}

def registry(fam):
    if fam not in _reg:
        p = fam_dir(fam) / 'wardrobe.json'
        _reg[fam] = json.loads(p.read_text()) if p.exists() else {}
    return _reg[fam]

def meta(fam):
    if fam not in _meta:
        _meta[fam] = json.loads((fam_dir(fam) / 'meta.json').read_text())
    return _meta[fam]

def part_info(fam, part):
    if part in BASE_PARTS:
        hides = []
        return {'slot': BASE_PARTS[part], 'src': 'base', 'hides': hides, 'needs': []}
    r = registry(fam)[part]
    return {'slot': r['slot'], 'src': part, 'hides': r['hides'], 'needs': r['needs']}

# kept for cast_looks compatibility
class _PI(dict):
    def get(self, part, default=None):
        return {'hides': []}
PART_INFO = _PI()

def hides_for(look):
    h = set()
    for part in look['parts']:
        h |= set(part_info(look['fam'], part)['hides'])
        if part.startswith(('top', 'outer')) and look.get('kind') != 'identity':
            h.add('waist')  # hanging tops cover the trouser waistband (region 7 on bottoms)
    return h

def resolved_parts(look):
    """Parts including implicit needs (e.g. shorts need skin legs)."""
    parts = dict(look['parts'])
    for part in list(parts):
        for n in part_info(look['fam'], part)['needs']:
            parts.setdefault(n, {})
    return parts

# ================================================================== colour/pattern helpers
L = T.lin
def C(h):
    return L(T.hexrgb(h))

def fine_noise(t, f=900, seed=1):
    return T.noise3(t.pos, f, seed=seed, octaves=2)

def height_to_normal(h, strength=2.0):
    gy, gx = np.gradient(h)
    n = np.dstack([-gx * strength, -gy * strength, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n * 0.5 + 0.5

def front_center(t, z, facing=-1, mask=None, band=0.06):
    m = (t.cov if mask is None else mask) & (np.abs(t.pos[..., 2] - z) < band) & (np.abs(t.pos[..., 0]) < 0.06)
    m &= (t.nrm[..., 1] * facing > 0.5)
    if not m.any():
        return None
    return np.array([0.0, float(np.median(t.pos[..., 1][m])), z])

def pattern(t, v, mask):
    """Linear-light colour per texel for a fabric recipe."""
    base = C(v.get('color', '#808080'))
    cols = [C(c) for c in v.get('colors', [])]
    p = t.pos
    out = np.broadcast_to(base, t.base.shape).copy()
    kind = v.get('pattern')
    if kind == 'stripes':
        f = v.get('freq', 22)
        band = (np.floor(p[..., 2] * f) % 2).astype(bool)
        out[band] = cols[0] if cols else base * 0.4
    elif kind == 'pinstripe':
        x = (p[..., 0] + p[..., 1] * 0.3) * 60
        line = np.abs(x - np.round(x)) < 0.06
        out[line] = cols[0] if cols else base * 1.6
    elif kind == 'plaid':
        f = v.get('freq', 16)
        a = (np.floor(p[..., 2] * f) % 2).astype(np.float32)
        b = (np.floor((p[..., 0] + p[..., 1]) * f) % 2).astype(np.float32)
        c1 = cols[0] if cols else base * 0.5
        c2 = cols[1] if len(cols) > 1 else base * 0.75
        w = (a + b)[..., None] / 2
        out = base * (1 - w) + c1 * w
        thin = (np.abs((p[..., 2] * f * 2) % 2 - 1) < 0.08) | (np.abs(((p[..., 0] + p[..., 1]) * f * 2) % 2 - 1) < 0.08)
        out[thin] = c2
    elif kind == 'floral':
        n1 = T.noise3(p, 40, seed=11)
        n2 = T.noise3(p, 40, seed=23)
        petals = n1 > 0.66
        leaves = (n2 > 0.64) & ~petals
        out[leaves] = cols[1] if len(cols) > 1 else C('#2e7d4f')
        out[petals] = cols[0] if cols else C('#f7f1e1')
        centre = n1 > 0.80
        out[centre] = cols[2] if len(cols) > 2 else C('#f2b632')
    elif kind == 'camo':
        n1 = T.noise3(p, 18, seed=5); n2 = T.noise3(p, 26, seed=9)
        out[n1 > 0.58] = cols[0] if cols else base * 0.6
        out[n2 > 0.62] = cols[1] if len(cols) > 1 else base * 1.4
    elif kind == 'ankara':
        # original wax-print-style motif: concentric rings on a staggered lattice + diamonds
        s = v.get('scale', 0.07)
        u = p[..., 0] / s + 0.5 * (np.floor(p[..., 2] / s) % 2)
        w = p[..., 2] / s
        fu, fw = u - np.round(u), w - np.round(w)
        r = np.sqrt(fu ** 2 + fw ** 2)
        ring = (np.floor(r * 7) % 2).astype(bool) & (r < 0.45)
        dia = (np.abs(fu) + np.abs(fw)) > 0.62
        c0 = cols[0] if cols else C('#f2b632')
        c1 = cols[1] if len(cols) > 1 else C('#1a3d8f')
        c2 = cols[2] if len(cols) > 2 else C('#e2582b')
        out[ring] = c0
        out[dia] = c1
        out[(r < 0.12)] = c2
    elif kind == 'denim':
        n = T.noise3(p, 300, seed=4, octaves=1)
        tw = (np.sin((p[..., 0] + p[..., 2]) * 900) * 0.5 + 0.5)
        out = base * (0.85 + 0.2 * n[..., None] + 0.08 * tw[..., None])
    elif kind == 'knit':
        rib = np.sin(np.arctan2(p[..., 0], -p[..., 1]) * 60) * 0.5 + 0.5
        out = base * (0.82 + 0.25 * rib[..., None])
    elif kind == 'monogram':
        g = (np.floor(p[..., 0] * 30) + np.floor(p[..., 2] * 30)) % 2
        dots = (np.abs((p[..., 0] * 30) % 1 - 0.5) < 0.18) & (np.abs((p[..., 2] * 30) % 1 - 0.5) < 0.18) & (g == 0)
        out[dots] = cols[0] if cols else C('#c9a24b')
    elif kind == 'straw':
        w = np.sin(p[..., 0] * 700) * np.sin(p[..., 1] * 700 + p[..., 2] * 300)
        out = base * (0.85 + 0.2 * (w[..., None] * 0.5 + 0.5))
    elif kind == 'hivis':
        bands = (np.abs(p[..., 2] - v['band_z'][0]) < 0.018) | (np.abs(p[..., 2] - v['band_z'][1]) < 0.018)
        out[bands] = C('#c9ccd0')
    elif kind == 'gradient':
        z0, z1 = float(p[..., 2][mask].min()) if mask.any() else 0, float(p[..., 2][mask].max()) if mask.any() else 1
        tz = np.clip((p[..., 2] - z0) / max(z1 - z0, 1e-3), 0, 1)[..., None]
        out = base * (1 - tz) + (cols[0] if cols else base * 0.3) * tz
    return out

def stamp(t, text, center, size, color, facing=(0, -1, 0), emissive=False, mask=None):
    if center is None:
        return
    u, vv, ok = T.decal_coords(t, center, size, facing)
    if mask is not None:
        ok &= mask
    H, W = 70, max(60, 60 * len(text))
    mk = T.text_mask(text, H, W)
    uu = np.clip(u * (W - 1), 0, W - 1).astype(int)
    vvv = np.clip((1 - vv) * (H - 1), 0, H - 1).astype(int)
    hit = ok & (mk[vvv, uu] > 0.5)
    lb = L(t.base)
    lb[hit] = C(color) * 0.95
    t.base = T.lin_to_srgb(lb)
    t.normal[hit] = (0.5, 0.5, 1.0)
    if emissive:
        t.emis[hit] = T.hexrgb(color)

def shape_mask(t, center, size, facing, fn):
    if center is None:
        return np.zeros(t.cov.shape, bool)
    u, vv, ok = T.decal_coords(t, center, size, facing)
    return ok & fn(u, vv)

def set_ms_kind(t, mask, kind):
    table = {'cotton': (0, .22), 'denim': (0, .2), 'leather': (0, .5), 'satin': (0, .45), 'fleece': (0, .08),
             'wool': (0, .12), 'linen': (0, .18), 'plastic': (0, .5), 'metal': (1, .72), 'gold': (1, .8),
             'rubber': (0, .35), 'straw': (0, .15), 'screen': (0, .85), 'hivis': (0, .3), 'knit': (0, .08)}
    m, s = table.get(kind, (0, .25))
    T.set_ms(t, mask, m, s)

# ================================================================== part texture recipes
ORIG = {SUB_IDS[k] for k in ('tee', 'tank', 'pants', 'jeans', 'shoe', 'sole', 'detail', 'scarf', 'hair', 'ponytail', 'strand', 'band')}

def fabric(t, v, fam, part):
    if v.get('pattern') == 'hivis' or v.get('kind') == 'hivis':
        bh0 = meta(fam)['bone_heads']
        v = dict(v, band_z=v.get('band_z', (bh0['Chest'][2] - 0.02, bh0['Hips'][2] + 0.10)))
    orig = t.cov & np.isin(t.sub, list(ORIG))
    new = t.cov & ~orig
    lb = L(t.base)
    Lum = T.luminance(lb)
    ref = float(np.median(Lum[orig])) if orig.any() else 0.5
    shade_o = np.clip(Lum / max(ref, 1e-4), 0.25, 1.8) ** v.get('detail', 0.9)
    nf = fine_noise(t, 700, seed=hash(part) % 97)
    shade_n = np.clip(t.ao, 0.2, 1) ** 0.9 * (0.94 + 0.12 * nf)
    shade = np.where(orig, shade_o, shade_n)
    col = pattern(t, v, t.cov)
    out = col * shade[..., None]
    accent = C(v['accent']) if v.get('accent') else None
    def paint_sub(names, colr, sh=None):
        m = t.mask(*names)
        if m.any():
            out[m] = colr * (shade[m][:, None] if sh is None else sh[m][:, None])
        return m
    if accent is not None:
        paint_sub(['cuff', 'collar'], accent)
    lining = C(v['lining']) if v.get('lining') else C(v.get('color', '#808080')) * 0.55
    paint_sub(['lining', 'hood_in'], lining)
    # shirt front (dickey) + tie
    dk = t.mask('dickey')
    if dk.any():
        out[dk] = C(v.get('shirt', '#f2f2ee')) * shade[dk][:, None]
        if v.get('tie'):
            tie = dk & (np.abs(t.pos[..., 0]) < 0.012 + 0.012 * np.clip((meta(fam)['bone_heads']['Neck'][2] - t.pos[..., 2]) / 0.25, 0, 1))
            out[tie] = C(v['tie']) * shade[tie][:, None]
    t.base = T.lin_to_srgb(np.where(t.cov[..., None], out, L(t.base)))
    # normals: authored where original, fabric weave where new
    h = T.noise3(t.pos, 1400, seed=2, octaves=1) * 0.6 + fine_noise(t, 500, 3) * 0.4
    nn = height_to_normal(h, v.get('bump', 1.5))
    t.normal[new] = nn[new]
    set_ms_kind(t, t.cov, v.get('kind', 'cotton'))
    if v.get('kind') == 'hivis':
        T.set_ms(t, t.cov & (np.abs(t.pos[..., 2] - v['band_z'][0]) < 0.018) | t.cov & (np.abs(t.pos[..., 2] - v['band_z'][1]) < 0.018), 0.3, 0.7)
    feats = v.get('features', [])
    bh = meta(fam)['bone_heads']
    chest_z = bh['Chest'][2]
    hips_z = bh['Hips'][2]
    neck_z = bh['Neck'][2]
    if 'placket' in feats or 'buttons' in feats:
        pl = t.cov & (np.abs(t.pos[..., 0]) < 0.006) & (t.nrm[..., 1] < -0.4) & ~dk
        lb2 = L(t.base); lb2[pl] *= 0.72; t.base = T.lin_to_srgb(lb2)
        for i in range(6):
            z = neck_z - 0.06 - 0.075 * i
            c = front_center(t, z, mask=t.cov & ~dk, band=0.02)
            if c is not None:
                bm_ = shape_mask(t, c, (0.016, 0.016), (0, -1, 0), lambda u, vv: (u - .5) ** 2 + (vv - .5) ** 2 < .2)
                lb2 = L(t.base); lb2[bm_] = C(v.get('button', '#e8e2d0')); t.base = T.lin_to_srgb(lb2)
    if 'pocket' in feats:  # hoodie kangaroo pocket
        c = front_center(t, hips_z + 0.06)
        m = shape_mask(t, c, (0.26, 0.12), (0, -1, 0), lambda u, vv: (np.abs(u - .5) < .48) & (vv < .95))
        edge = m & ~shape_mask(t, c, (0.25, 0.11), (0, -1, 0), lambda u, vv: (np.abs(u - .5) < .47) & (vv < .93))
        lb2 = L(t.base); lb2[edge] *= 0.6; lb2[m] *= 0.92; t.base = T.lin_to_srgb(lb2)
    if 'drawstrings' in feats:
        for sx in (-0.03, 0.03):
            m = t.cov & (np.abs(t.pos[..., 0] - sx) < 0.004) & (t.pos[..., 2] < neck_z - 0.01) & (t.pos[..., 2] > neck_z - 0.13) & (t.nrm[..., 1] < -0.3)
            lb2 = L(t.base); lb2[m] = C(v.get('string', '#f0f0f0')); t.base = T.lin_to_srgb(lb2)
    if 'zip' in feats:
        m = t.cov & (np.abs(t.pos[..., 0]) < 0.004) & (t.nrm[..., 1] < -0.3)
        lb2 = L(t.base); lb2[m] = C('#b9bcc2'); t.base = T.lin_to_srgb(lb2); T.set_ms(t, m, 1, .6)
    if 'belt' in feats:
        m = t.cov & (np.abs(t.pos[..., 2] - (hips_z + 0.09)) < 0.022)
        lb2 = L(t.base); lb2[m] = C(v.get('belt', '#5a3b22')) * 0.9; t.base = T.lin_to_srgb(lb2)
    if 'lapels' in feats:
        # darker satin facing along the front opening edges
        m = t.cov & (np.abs(np.abs(t.pos[..., 0]) - 0.055 * np.clip((t.pos[..., 2] - chest_z + 0.15) / 0.25, 0, 1)) < 0.014) & (t.nrm[..., 1] < -0.4) & (t.pos[..., 2] > chest_z - 0.12) & ~dk
        lb2 = L(t.base); lb2[m] *= 0.7; t.base = T.lin_to_srgb(lb2)
    if 'embroidery' in feats:
        r = np.sqrt(t.pos[..., 0] ** 2 + ((t.pos[..., 2] - neck_z + 0.02) * 0.8) ** 2)
        m = t.cov & (t.nrm[..., 1] < -0.2) & (np.abs(r - 0.085) < 0.018) & (t.pos[..., 2] < neck_z + 0.01)
        m2 = t.cov & (t.nrm[..., 1] < -0.2) & (np.abs(t.pos[..., 0]) < 0.01) & (t.pos[..., 2] < neck_z - 0.05) & (t.pos[..., 2] > chest_z - 0.12)
        pat = (np.sin(t.pos[..., 0] * 400) * np.sin(t.pos[..., 2] * 400) > 0)
        lb2 = L(t.base); lb2[(m | m2) & pat] = C(v.get('thread', '#d9b44a')); lb2[(m | m2) & ~pat] *= 0.8; t.base = T.lin_to_srgb(lb2)
        T.set_ms(t, (m | m2) & pat, 0.6, 0.6)
    if 'side_stripes' in feats:
        m = t.cov & (np.abs(np.abs(t.pos[..., 0]) - float(np.percentile(np.abs(t.pos[..., 0][t.cov]), 97))) < 0.015)
        lb2 = L(t.base); lb2[m] = C(v.get('stripe', '#ffffff')); t.base = T.lin_to_srgb(lb2)
    if 'torn' in feats:
        n = T.noise3(t.pos, 45, seed=13)
        holes = t.cov & (n > 0.74) & (t.pos[..., 2] > 0.2)
        rim = t.cov & (n > 0.70) & ~holes
        lb2 = L(t.base); lb2[holes] = C(v.get('under', '#d58a7a')) * 0.8; lb2[rim] *= 0.55; t.base = T.lin_to_srgb(lb2)
        t.normal[holes] = (0.5, 0.5, 1.0)
    if 'ripped_knees' in feats:
        kz = bh['Calf.L'][2]
        n = T.noise3(t.pos, 120, seed=17)
        m = t.cov & (np.abs(t.pos[..., 2] - kz - 0.02) < 0.035) & (t.nrm[..., 1] < -0.2) & (n > 0.45)
        lb2 = L(t.base); lb2[m] = C(v.get('under', '#c49268')) * 0.9; t.base = T.lin_to_srgb(lb2)
    if 'socks' in feats:
        m = t.mask('legs') if 'legs' in SUB_IDS else None
    if 'patch' in feats:  # small chest-left logo patch
        c = front_center(t, chest_z + 0.07)
        if c is not None:
            c = c + np.array([0.06, 0, 0])
            m = shape_mask(t, c, (0.05, 0.035), (0, -1, 0), lambda u, vv: (np.abs(u - .5) < .45) & (np.abs(vv - .5) < .4))
            mtn = shape_mask(t, c, (0.05, 0.035), (0, -1, 0), lambda u, vv: (vv - .2) < (0.6 - np.abs(u - .5) * 1.6))
            lb2 = L(t.base); lb2[m] = C(v.get('patch_bg', '#1b1f2a')); lb2[m & mtn] = C(v.get('patch_fg', '#e8e8e8')); t.base = T.lin_to_srgb(lb2)
    for pr in v.get('prints', []):
        where = pr.get('where', 'chest')
        z = {'chest': chest_z + 0.05, 'belly': hips_z + 0.12, 'back': chest_z + 0.02, 'leg': bh['Calf.L'][2] + 0.2}[where]
        facing = (0, 1, 0) if where == 'back' else (0, -1, 0)
        c = front_center(t, z, facing=1 if where == 'back' else -1)
        if where == 'leg' and c is None:
            continue
        w = pr.get('w', 0.05 * max(2, len(pr['text'])) * 0.6)
        stamp(t, pr['text'], c, (w, pr.get('h', 0.07)), pr.get('color', '#ffffff'), facing, pr.get('glow', False), mask=t.cov & ~dk)
    return t

def head_tex(look, fam):
    t = T.crop(fam, 'HEAD')
    v = look['parts'].get('head', {}) or {}
    face = t.mask('face')
    mt = meta(fam)
    bh = mt['bone_heads']
    ez = (bh['Eye.L'][2] + bh['Eye.R'][2]) / 2
    ex = abs(bh['Eye.L'][0])
    tone = look.get('skin_tone')
    if tone == 'rekt_pink':
        t.base = T.tone_shift(t.base, face, T.SKIN_TONES['light'], src_ref=skin_ref(fam))
    elif tone:
        t.base = T.tone_shift(t.base, face, T.SKIN_TONES[tone], src_ref=skin_ref(fam))
    if look.get('hair_color'):
        t.base = T.hair_recolor(t.base, t.mask('brows'), T.HAIR_COLORS[look['hair_color']])
    p, n = t.pos, t.nrm
    front = n[..., 1] < -0.2
    lb = L(t.base)
    if v.get('flush') or tone == 'rekt_pink':
        cheeks = face & front & (((np.abs(p[..., 0]) - ex * 1.05) ** 2 + (p[..., 2] - ez + 0.035) ** 2) < 0.03 ** 2)
        whole = face
        lb[whole] = lb[whole] * np.array([1.12, 0.86, 0.9])
        lb[cheeks] = lb[cheeks] * np.array([1.15, 0.8, 0.85])
    if v.get('stubble') and look.get('hair_color'):
        cz = mt.get('chin_z', ez - 0.09)
        jaw = face & (p[..., 2] < ez - 0.05) & (p[..., 2] > cz - 0.03) & (p[..., 1] < bh['Head'][1] + 0.02)
        lip = (np.abs(p[..., 0]) < 0.026) & (np.abs(p[..., 2] - (ez - 0.075)) < 0.008)
        nz = T.noise3(p, 1500, seed=8, octaves=1)
        a = (0.45 * (nz > 0.45))[..., None]
        hc = C(T.HAIR_COLORS[look['hair_color']])
        m = jaw & ~lip
        lb[m] = (lb * (1 - a) + hc * a)[m]
    if v.get('tears'):
        for sx in (-1, 1):
            m = face & front & (np.abs(p[..., 0] - sx * ex) < 0.0045) & (p[..., 2] < ez - 0.012) & (p[..., 2] > ez - 0.07)
            lb[m] = lb[m] * 0.5 + C('#a9d8f5') * 0.5
            t.ms[m, 3] = 0.85
    if v.get('eyebags'):
        for sx in (-1, 1):
            m = face & front & (((p[..., 0] - sx * ex) / 0.02) ** 2 + ((p[..., 2] - ez + 0.018) / 0.008) ** 2 < 1)
            lb[m] *= np.array([0.78, 0.72, 0.8])
    t.base = T.lin_to_srgb(lb)
    if v.get('iris'):
        eyes = t.mask('eyes')
        el = L(t.base)
        Lm = T.luminance(el)
        iris = eyes & (Lm < 0.35) & (Lm > 0.012)
        tgt = C({'brown': '#5a3a22', 'darkbrown': '#2e1d12', 'green': '#4f7a3a', 'hazel': '#7b6232', 'grey': '#6f7c86', 'blue': '#3d78b5', 'amber': '#a8742a'}[v['iris']])
        ref = np.median(el[iris], axis=0) if iris.any() else tgt
        el[iris] = np.clip(el[iris] / np.maximum(ref, 1e-3) * tgt, 0, 1)
        t.base = T.lin_to_srgb(el)
    if v.get('laser_iris'):
        eyes = t.mask('eyes')
        el = L(t.base); Lm = T.luminance(el)
        iris = eyes & (Lm < 0.35)
        el[iris] = C('#ff2a1a'); t.base = T.lin_to_srgb(el); t.emis[iris] = T.hexrgb('#ff2a1a')
    return t

_ref = {}
def skin_ref(fam):
    if fam not in _ref:
        t = T.crop(fam, 'HEAD')
        _ref[fam] = np.median(L(t.base)[t.mask('face')], axis=0)
    return _ref[fam]

def skin_tex(look, fam):
    t = T.crop(fam, 'SKIN')
    arms = t.mask('arms')
    legs = t.mask('legs')
    if legs.any():
        ref = np.median(L(t.base)[arms], axis=0)
        n = T.noise3(t.pos, 30, seed=3)
        col = T.lin_to_srgb(np.clip(ref[None, None, :] * (0.94 + 0.12 * n[..., None]), 0, 1))
        t.base[legs] = col[legs]
        t.normal[legs] = (0.5, 0.5, 1.0)
        T.set_ms(t, legs, 0.0, 0.35)
    tone = look.get('skin_tone')
    if tone:
        tgt = T.SKIN_TONES['light' if tone == 'rekt_pink' else tone]
        t.base = T.tone_shift(t.base, arms | legs, tgt, src_ref=skin_ref(fam))
    socks = look.get('socks')
    if socks and legs.any():
        bh = meta(fam)['bone_heads']
        m = legs & (t.pos[..., 2] < bh['Foot.L'][2] + 0.12)
        lb = L(t.base); lb[m] = C(socks) * (0.9 + 0.1 * T.noise3(t.pos, 200, 5)[m][:, None]); t.base = T.lin_to_srgb(lb)
    return t

def hair_tex(look, fam, part):
    t = T.crop(fam, 'HAIR', 'base' if part == 'hair' else part)
    hc = look.get('hair_color')
    v = look['parts'].get(part, {}) or {}
    if part == 'hair':
        if hc:
            t.base = T.hair_recolor(t.base, t.cov & (t.sub != SUB_IDS['band']), T.HAIR_COLORS[hc])
        return t
    col = C(T.HAIR_COLORS[hc or 'black'])
    p = t.pos
    nz = T.noise3(p, 900, seed=21, octaves=2)
    lb = L(t.base)
    orig = t.cov & np.isin(t.sub, [SUB_IDS['hair'], SUB_IDS['ponytail'], SUB_IDS['strand']])
    if orig.any():
        t.base = T.hair_recolor(t.base, orig, T.HAIR_COLORS[hc or 'black'])
        lb = L(t.base)
    scalp = t.mask('scalp')
    if scalp.any():
        tone = C(T.SKIN_TONES.get(look.get('skin_tone') or 'warm', '#9a6644')) if look.get('skin_tone') != 'rekt_pink' else C('#d9ad8c')
        dens = v.get('density', 0.85)
        a = np.clip(dens + 0.2 * (nz - 0.5), 0, 1)[..., None]
        mix = tone * (1 - a) + col * a
        if v.get('cornrows'):
            rows = np.abs(np.sin(p[..., 0] * 140)) > 0.35
            mix = np.where(rows[..., None], col * (0.8 + 0.3 * nz[..., None]), tone * 0.8)
        lb[scalp] = mix[scalp] * np.clip(t.ao[scalp], 0.4, 1)[:, None]
        t.normal[scalp] = height_to_normal(nz, 1.0)[scalp]
        T.set_ms(t, scalp, 0, 0.2)
    for sname, freq in (('afro', 900), ('loc', 300), ('hair', 600)):
        m = t.mask(sname) & ~orig
        if m.any():
            nn = T.noise3(p, freq, seed=31, octaves=2)
            lb[m] = col[None, :] * (0.65 + 0.6 * nn[m][:, None]) * np.clip(t.ao[m], 0.35, 1)[:, None]
            t.normal[m] = height_to_normal(nn, 3.0)[m]
            T.set_ms(t, m, 0, 0.3)
    t.base = T.lin_to_srgb(lb)
    return t

def beard_tex(look, fam, part):
    t = T.crop(fam, 'BEARD', part)
    col = C(T.HAIR_COLORS[look.get('hair_color') or 'black'])
    nz = T.noise3(t.pos, 1100, seed=41, octaves=2)
    lb = L(t.base)
    m = t.cov
    lb[m] = col[None, :] * (0.6 + 0.65 * nz[m][:, None]) * np.clip(t.ao[m], 0.4, 1)[:, None]
    t.base = T.lin_to_srgb(lb)
    t.normal[m] = height_to_normal(nz, 3.0)[m]
    T.set_ms(t, m, 0, 0.2)
    return t

ACC_DEFAULT = {'acc_a': '#d0d0d0', 'acc_b': '#555a62', 'acc_c': '#888888', 'acc_d': '#444444', 'acc_dark': '#1d1f24',
               'acc_metal': '#d9a93b', 'acc_glow': '#ff2a1a', 'acc_glow2': '#2a6bff', 'acc_screen': '#1e3a5f',
               'acc_strap': '#2b4fd8', 'acc_lens': '#14161b', 'acc_tex': None}

def acc_tex(look, fam, part, slot):
    t = T.crop(fam, slot, part)
    v = dict(look['parts'].get(part, {}) or {})
    lb = L(t.base)
    nz = T.noise3(t.pos, 600, seed=5, octaves=2)
    for name, default in ACC_DEFAULT.items():
        m = t.mask(name)
        if not m.any():
            continue
        if name == 'acc_tex':
            if v.get('tint'):
                lb[m] = lb[m] * C(v['tint']) / max(float(np.median(T.luminance(lb[m]))), 0.05) * 0.5
            continue
        col = C(v.get(name, default))
        pat = v.get('pattern_' + name)
        if pat:
            col = pattern(t, dict(pat, color=v.get(name, default)), m)[m]
        else:
            col = np.broadcast_to(col, (int(m.sum()), 3))
        lb[m] = col * (np.clip(t.ao[m], 0.35, 1) * (0.93 + 0.12 * nz[m]))[:, None]
        if name in ('acc_glow', 'acc_glow2'):
            lb[m] = col
            t.emis[m] = T.lin_to_srgb(col)
            T.set_ms(t, m, 0, 0.6)
        elif name == 'acc_screen':
            T.set_ms(t, m, 0, 0.85)
            t.emis[m] = T.lin_to_srgb(col) * 0.8
        elif name == 'acc_metal':
            T.set_ms(t, m, 1.0, 0.75)
        elif name in ('acc_lens',):
            T.set_ms(t, m, 0.3, 0.9)
        else:
            T.set_ms(t, m, v.get('metal', 0.0), v.get('smooth', 0.35))
        t.normal[m] = (0.5, 0.5, 1.0)
    t.base = T.lin_to_srgb(lb)
    for pr in v.get('prints', []):
        m = t.mask(pr.get('on', 'acc_a'))
        facing = np.array(pr.get('facing', (0, -1, 0)), dtype=np.float32)
        sel = m & ((t.nrm @ facing) > 0.5)
        if not sel.any():
            continue
        c = np.median(t.pos[sel], axis=0)
        if 'offset' in pr:
            c = c + np.array(pr['offset'])
        stamp(t, pr['text'], c, (pr.get('w', 0.08), pr.get('h', 0.04)), pr.get('color', '#ffffff'), tuple(facing),
              pr.get('glow', False), mask=sel)
    return t

def part_tex(look, part):
    fam = look['fam']
    info = part_info(fam, part)
    slot = info['slot']
    if part == 'head':
        return head_tex(look, fam)
    if part in ('skin', 'legs'):
        return skin_tex(look, fam)
    if part == 'hair' or part.startswith('hair_'):
        return hair_tex(look, fam, part)
    if part.startswith('beard_'):
        return beard_tex(look, fam, part)
    if part.startswith(('top', 'outer', 'bottom', 'shoes', 'scarf')):
        t = T.crop(fam, slot, info['src'])
        v = look['parts'].get(part) or {}
        if not v:
            return t
        return fabric(t, v, fam, part)
    return acc_tex(look, fam, part, slot)

def look_textures(look, fam):
    texs = {}
    parts = resolved_parts(look)
    for part in parts:
        slot = part_info(fam, part)['slot']
        if slot in texs and part in ('legs',):
            continue
        if slot in texs:
            raise RuntimeError(f"{look['id']}: slot {slot} used twice ({part})")
        texs[slot] = part_tex(dict(look, parts=parts), part)
    return list(texs.values())

# ================================================================== looks
def L_(i, fam, parts, kind='npc', **kw):
    d = {'id': i, 'fam': fam, 'kind': kind, 'parts': parts}
    d.update(kw)
    return d

JEANS = lambda c='#3b5a8a', **k: dict({'color': c, 'pattern': 'denim', 'kind': 'denim'}, **k)
TEE = lambda c, **k: dict({'color': c, 'kind': 'cotton'}, **k)
PR = lambda text, where='chest', color='#ffffff', **k: dict({'text': text, 'where': where, 'color': color}, **k)
SNEAK = lambda c='#f2f2f2', **k: dict({'color': c, 'kind': 'leather', 'detail': 0.6}, **k)

LOOKS = [
    # ---------------------------------------------------------- existing looks, tiered (identity unchanged)
    L_('snow_everyday', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top': {}, 'bottom': {}, 'shoes': {}}, kind='identity',
       label='Snow / Everyday', tiers=['hero', 'lod0', 'lod1', 'lod2']),
    L_('rain_breeze', 'f', {'head': {}, 'hair': {}, 'skin': {}, 'top': {}, 'bottom': {}, 'shoes': {}, 'scarf': {}}, kind='identity',
       label='Rain / Breeze', tiers=['hero', 'lod0', 'lod1', 'lod2']),
]


# ------------------------------------------------------------------ shared recipes
def hoodie(c, *prints, **k):
    return dict({'color': c, 'kind': 'fleece', 'features': ['pocket', 'drawstrings'], 'prints': list(prints)}, **k)
def suit(c, tie=None, shirt='#f2f2ee', **k):
    return dict({'color': c, 'kind': 'wool', 'features': ['lapels', 'buttons'], 'tie': tie, 'shirt': shirt, 'button': '#141414'}, **k)
def trousers(c, kind='cotton', **k):
    return dict({'color': c, 'kind': kind, 'detail': 0.8}, **k)
def cap(c, text=None, tc='#ffffff', bill=None):
    d = {'acc_a': c, 'acc_b': bill or c}
    if text:
        d['prints'] = [{'text': text, 'color': tc, 'w': 0.075, 'h': 0.03}]
    return d
def badge(text, strap='#2b4fd8', card='#f4f4f4', tc='#111111'):
    return {'acc_a': card, 'acc_strap': strap, 'prints': [{'text': text, 'color': tc, 'w': 0.06, 'h': 0.02}]}
ANKARA = {'pattern': 'ankara', 'colors': ['#f2b632', '#1a3d8f', '#e2582b'], 'kind': 'cotton'}

NPC = 'npc'
LOOKS += [
    # ---------------------------------------------------------- 28 contract archetypes (+2 spec extras)
    L_('npc_town_crier', 'm', {'head': {'iris': 'brown'}, 'hair_buzz': {}, 'skin': {}, 'top_long': TEE('#c8102e', prints=[PR('JUST IN')]),
        'bottom': JEANS('#1c1c1f'), 'shoes': SNEAK(), 'hat_siren': {'acc_a': '#1d1f24', 'acc_b': '#c8102e'}, 'hand_megaphone': {}},
       archetype='town_crier', label='Town Crier', skin_tone='warm', hair_color='black', body='stocky', face={'jaw_wide': .4}, scale=1.02),
    L_('npc_giveaway_guy', 'm', {'head': {'iris': 'darkbrown'}, 'hair_afro': {}, 'beard_goatee': {}, 'skin': {}, 'top': TEE('#f4f4f2', prints=[PR('DROP WALLET', color='#111111', w=0.24)]),
        'bottom': trousers('#6b6e75'), 'shoes': SNEAK(), 'acc_gold_chain': {}, 'hand_phone': {'acc_screen': '#3a7bd5'}},
       archetype='giveaway_guy', label='Giveaway Guy', skin_tone='dark', hair_color='black', face={'cheeks_full': .4}),
    L_('npc_reply_guy', 'm', {'head': {'iris': 'blue'}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie': hoodie('#b9a68a', PR('THIS', color='#2b2b2b')),
        'bottom': trousers('#7d6f52'), 'shoes': SNEAK('#9a9ca2'), 'hat_cap': cap('#f2f2f2', 'GM', '#1d1f24'), 'hand_phone': {'acc_screen': '#3a7bd5'}},
       archetype='reply_guy', label='Reply Guy', skin_tone='fair', hair_color='brown', body='slim', face={'chin_long': .4}, scale=0.97),
    L_('npc_perp_degen', 'm', {'head': {'eyebags': True, 'iris': 'brown'}, 'hair': {}, 'skin': {}, 'top_hoodie': hoodie('#121316', PR('50X', color='#2ee66b')),
        'bottom': trousers('#202126'), 'shoes': SNEAK('#d62839'), 'acc_headset': {'acc_a': '#202126', 'acc_dark': '#0c0c0e', 'acc_b': '#2ee66b'}, 'hand_energy': {'acc_a': '#2ee66b'}},
       archetype='perp_degen_npc', label='Perp Degen', skin_tone='olive', hair_color='black', body='heavy'),
    L_('npc_rekt_guy', 'm', {'head': {'tears': True, 'flush': True}, 'hair': {}, 'skin': {}, 'top': TEE('#f08aa8', features=['torn'], under='#e3a49a'),
        'bottom': JEANS('#4d6a96', features=['ripped_knees'], under='#e3a49a'), 'shoes': SNEAK('#8d8f95')},
       archetype='rekt_guy', label='Rekt Guy', skin_tone='rekt_pink', hair_color='brown', body='slim', face={'brow_low': -.5}, scale=0.98),
    L_('npc_wallet_tracker', 'f', {'head': {'iris': 'darkbrown'}, 'hair_long': {}, 'skin': {}, 'top_long': TEE('#1d2b4f', prints=[PR('1K-15M', w=0.16)]),
        'bottom': JEANS('#16171b'), 'shoes_boots': SNEAK('#1a1412'), 'hat_cap': cap('#16171b', 'WOTCHA', '#f2b632'), 'acc_binoculars': {}},
       archetype='wallet_tracker', label='Wallet Tracker', skin_tone='deep', hair_color='black'),
    L_('npc_doomposter', 'm', {'head': {'eyebags': True, 'iris': 'grey'}, 'hair_buzz': {}, 'beard_full': {}, 'skin': {}, 'top_hoodie_up': hoodie('#141416', PR('ITS SO OVER', color='#8a8f99', w=0.2)),
        'bottom': JEANS('#4a4d55'), 'shoes': SNEAK('#141416'), 'acc_rain_cloud': {'acc_a': '#3d4047', 'acc_b': '#7fb8e8'}},
       archetype='doomposter', label='Doomposter', skin_tone='fair', hair_color='darkbrown', face={'brow_low': .8}),
    L_('npc_spaces_host', 'f', {'head': {'iris': 'hazel'}, 'hair_afro': {}, 'skin': {}, 'top_tee': TEE('#9b7fd4', prints=[PR('GM GM', w=0.14)]),
        'bottom_skirt': trousers('#1d1f24'), 'shoes': SNEAK(), 'hat_headphones': {'acc_a': '#f4f4f4', 'acc_dark': '#1d1f24'}},
       archetype='spaces_host', label='Spaces Host', skin_tone='warm', hair_color='black', body='curvy'),
    L_('npc_ore_miner', 'm', {'head': {'iris': 'brown'}, 'hair_buzz': {}, 'beard_full': {}, 'skin': {}, 'top_long': TEE('#5b5f66'),
        'outer_vest': {'color': '#ff7a1a', 'pattern': 'hivis', 'kind': 'hivis', 'features': ['zip']},
        'bottom': trousers('#6f6446', features=[]), 'shoes_boots': SNEAK('#5a3b22'), 'hat_hardhat': {'acc_a': '#f2b705', 'acc_b': '#d99a00', 'prints': [{'text': 'ORE', 'color': '#2b2b2b', 'w': 0.07, 'h': 0.03}]},
        'hand_pickaxe': {}},
       archetype='ore_miner', label='ORE Miner', skin_tone='tan', hair_color='darkbrown', body='broad', scale=1.03),
    L_('npc_alpha_caller', 'm', {'head': {'stubble': True}, 'hair': {}, 'skin': {}, 'top_jacket': suit('#16171b', shirt='#2a2c31', kind='leather'),
        'bottom': trousers('#16171b'), 'shoes': SNEAK(), 'acc_shades': {}, 'acc_anime_mask': {'acc_a': '#f7f3ee', 'acc_dark': '#c8102e',
        'prints': [{'on': 'acc_a', 'text': '100X', 'color': '#c8102e', 'w': 0.05, 'h': 0.025, 'facing': (1, 0, 0)}]}},
       archetype='alpha_caller', label='Alpha Caller', skin_tone='olive', hair_color='black', face={'jaw_wide': .2}),
    L_('npc_cabal_insider', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie_up': hoodie('#3b1f5c'), 'bottom': trousers('#141416'),
        'shoes': SNEAK('#141416'), 'acc_shades': {}, 'acc_pins': {}},
       archetype='cabal_insider', label='Cabal Insider', skin_tone='light', hair_color='black', body='slim', face={'nose_big': .4}),
    L_('npc_rug_dev', 'm', {'head': {'stubble': True, 'eyebags': True}, 'hair': {}, 'skin': {}, 'top_hoodie': hoodie('#4a4d55', PR('NO INSIDERS', w=0.2)),
        'bottom': trousers('#4b5320'), 'shoes': SNEAK('#141416'), 'acc_headset': {'acc_a': '#141416', 'acc_dark': '#0c0c0e', 'acc_b': '#ff3b3b'}, 'hand_burner': {}},
       archetype='rug_dev', label='Rug Dev (livestream)', skin_tone='warm', hair_color='darkbrown', face={'face_round': .5}),
    L_('npc_sniper_bot', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_long': TEE('#141416'),
        'outer_vest': {'color': '#2a2d33', 'kind': 'plastic', 'pattern': 'camo', 'colors': ['#1d1f24', '#3a3e46'], 'features': ['zip']},
        'bottom': trousers('#1d1f24'), 'shoes_boots': SNEAK('#141416'), 'acc_visor': {'acc_glow': '#28e0ff'}, 'acc_drone': {'acc_a': '#28e0ff', 'acc_glow': '#28e0ff'}},
       archetype='sniper_bot', label='Sniper Bot', skin_tone='brown', hair_color='black', body='broad'),
    L_('npc_sleuth', 'm', {'head': {'iris': 'green'}, 'hair': {}, 'beard_full': {}, 'skin': {}, 'top_coat': {'color': '#a8865a', 'kind': 'cotton', 'features': ['belt', 'buttons'], 'shirt': '#e8e2d6', 'tie': '#3a2a1a', 'belt': '#7a5a36'},
        'bottom': trousers('#3b2f25'), 'shoes': SNEAK('#3b2618'), 'acc_glasses': {}, 'hand_magnifier': {}},
       archetype='sleuth', label='Sleuth', skin_tone='fair', hair_color='grey', face={'chin_long': .5, 'nose_big': .3}, scale=1.03),
    L_('npc_auditor', 'f', {'head': {'iris': 'brown'}, 'hair_long': {}, 'skin': {}, 'top_jacket': suit('#22304d'), 'bottom': trousers('#5a5f6a'),
        'shoes': SNEAK('#141416'), 'acc_lanyard': badge('AUDIT', '#c8102e'), 'hand_clipboard': {}, 'acc_glasses': {}},
       archetype='auditor', label='Auditor', skin_tone='tan', hair_color='brown'),
    L_('npc_vc', 'm', {'head': {'iris': 'blue'}, 'hair': {}, 'skin': {}, 'top_long': TEE('#a9c4e8', features=['placket', 'buttons'], button='#f4f4f4'),
        'outer_vest': {'color': '#1f2c45', 'kind': 'fleece', 'features': ['zip', 'patch']}, 'bottom': trousers('#c2ad84'),
        'shoes': SNEAK('#5a3b22'), 'acc_glasses': {}},
       archetype='vc', label='VC (fleece vest)', skin_tone='light', hair_color='brown', face={'jaw_wide': .3}, body='average'),
    L_('npc_farmer', 'm', {'head': {'iris': 'brown'}, 'hair_buzz': {}, 'beard_goatee': {}, 'skin': {}, 'top_shirt': {'color': '#b5332e', 'pattern': 'plaid', 'colors': ['#3b1c1a', '#e8d9b0'], 'features': ['placket', 'buttons']},
        'bottom': JEANS(), 'shoes_boots': SNEAK('#5a3b22'), 'hat_straw': {'acc_a': '#d8b46a', 'acc_b': '#7a2e22', 'pattern_acc_a': {'pattern': 'straw'}}, 'hand_watering_can': {}},
       archetype='farmer', label='Airdrop Farmer', skin_tone='brown', hair_color='grey', body='stocky'),
    L_('npc_sybil_farmer', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top': TEE('#8a8d92', prints=[PR('SYBIL 0042', w=0.2, color='#1d1f24')]),
        'bottom': JEANS('#5a6e8f'), 'shoes': SNEAK('#8d8f95'), 'hat_straw': {'acc_a': '#cfae68', 'acc_b': '#3a3e46', 'pattern_acc_a': {'pattern': 'straw'}}, 'acc_shades': {}, 'hand_phone': {}},
       archetype='sybil_farmer', label='Sybil Farmer (clone)', skin_tone='warm', hair_color='black', body='slim', scale=0.88,
       notes='Spawned as 10 small clones: same look, runtime scale 0.6-0.9.'),
    L_('npc_fake_rich_larp', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top': TEE('#111214', pattern='monogram', colors=['#c9a24b']),
        'bottom': trousers('#ecebe6'), 'shoes': SNEAK('#f4f4f2'), 'acc_gold_chain': {}, 'acc_shades': {}, 'hand_carkey': {}},
       archetype='fake_rich_larp', label='Fake-Rich LARP', skin_tone='tan', hair_color='black', face={'jaw_wide': .35}),
    L_('npc_dubai_relocator', 'f', {'head': {'iris': 'blue'}, 'hair_long': {}, 'skin': {}, 'top_shirt': {'color': '#f2eee3', 'kind': 'linen', 'features': ['placket', 'buttons']},
        'bottom': trousers('#d8c8a8', kind='linen'), 'shoes': SNEAK('#b48a5a'), 'acc_shades': {}},
       archetype='dubai_relocator', label='Dubai Relocator', skin_tone='light', hair_color='blonde', body='slim'),
    L_('npc_ai_agent', 'm', {'skin': {}, 'top_jacket': suit('#2c2f36', tie='#28e07a', shirt='#141416'), 'bottom': trousers('#2c2f36'), 'shoes': SNEAK('#141416'),
        'acc_monitor_head': {'acc_dark': '#d9d6cc', 'acc_b': '#3a3e46', 'acc_screen': '#0b2a12',
                             'prints': [{'on': 'acc_screen', 'text': 'I AM', 'color': '#3cff7a', 'w': 0.12, 'h': 0.045, 'glow': True, 'offset': (0, 0, 0.03)},
                                        {'on': 'acc_screen', 'text': 'LIQUIDITY', 'color': '#3cff7a', 'w': 0.2, 'h': 0.04, 'glow': True, 'offset': (0, 0, -0.025)}]}},
       archetype='ai_agent', label='AI Agent', skin_tone='tan'),
    L_('npc_drainer', 'm', {'head': {'stubble': True}, 'hair': {}, 'skin': {}, 'top_shirt': TEE('#11504a', features=['placket'], prints=[PR('SUPPORT', w=0.13)]),
        'bottom': trousers('#141416'), 'shoes': SNEAK(), 'acc_lanyard': badge('STAFF', '#11504a'), 'hat_cap': cap('#11504a', 'HELP')},
       archetype='drainer', label='Drainer (fake support)', skin_tone='tan', hair_color='black', body='slim', face={'nose_big': -.3}),
    L_('npc_community_mod', 'f', {'head': {}, 'hair_buzz': {'cornrows': True}, 'skin': {}, 'top_tee': TEE('#2e8b57', prints=[PR('MOD', w=0.1)]),
        'bottom': JEANS('#16171b'), 'shoes': SNEAK(), 'acc_headset': {'acc_a': '#1d1f24', 'acc_dark': '#0c0c0e', 'acc_b': '#2e8b57'}},
       archetype='community_mod', label='Community Mod', skin_tone='brown', hair_color='black', body='stocky'),
    L_('npc_thread_boy', 'm', {'head': {'iris': 'grey'}, 'hair': {}, 'skin': {}, 'top': TEE('#f4f4f2', prints=[PR('1/', color='#1d1f24', w=0.07)]),
        'bottom': trousers('#22304d'), 'shoes': SNEAK(), 'acc_glasses': {}, 'hand_notebook': {'acc_a': '#2b4fd8', 'acc_b': '#f4f4f2'}},
       archetype='thread_boy', label='Thread Boy', skin_tone='light', hair_color='blonde', body='slim', face={'chin_long': .3}),
    L_('npc_engagement_farmer', 'f', {'head': {'iris': 'amber'}, 'hair_afro': {}, 'skin': {}, 'top_tee': TEE('#ff4fa3', prints=[PR('REPLY HI', w=0.17)]),
        'bottom_skirt': trousers('#f4f4f2'), 'shoes': SNEAK(), 'acc_ring_light': {'acc_glow': '#fff6e8', 'acc_b': '#1d1f24'}},
       archetype='engagement_farmer', label='Engagement Farmer', skin_tone='dark', hair_color='darkbrown', body='curvy'),
    L_('npc_normie', 'm', {'head': {'iris': 'blue'}, 'hair': {}, 'skin': {}, 'top_shirt': {'color': '#2a8fb8', 'pattern': 'floral', 'colors': ['#fff4e0', '#1e6b45', '#f2b632']},
        'bottom_shorts': trousers('#c8b48a'), 'shoes': SNEAK(), 'hat_bucket': {'acc_a': '#e9dcc0', 'acc_b': '#e9dcc0'}, 'acc_camera': {}},
       archetype='normie', label='Normie (tourist)', skin_tone='fair', hair_color='brown', body='heavy', socks='#f4f4f4', scale=0.99),
    L_('npc_doxxed_founder', 'm', {'head': {}, 'hair_buzz': {}, 'beard_goatee': {}, 'skin': {}, 'top_jacket': suit('#1b2540', tie='#c9a227'),
        'bottom': trousers('#1b2540', kind='wool'), 'shoes': SNEAK('#141416')},
       archetype='doxxed_founder', label='Doxxed Founder (suit)', skin_tone='deep', hair_color='black', face={'jaw_wide': .2}, scale=1.02),
    L_('npc_prediction_shark', 'f', {'head': {'iris': 'green'}, 'hair': {}, 'skin': {}, 'top_shirt': {'color': '#f2f2ee', 'pattern': 'stripes', 'colors': ['#1f7a4d'], 'freq': 30, 'features': ['placket']},
        'bottom': trousers('#141416'), 'shoes': SNEAK(), 'hat_cap': cap('#1f7a4d', 'ODDS'), 'hand_slip': {'acc_a': '#f4f1e6'}},
       archetype='prediction_shark_npc', label='Prediction Shark', skin_tone='olive', hair_color='auburn'),
    L_('npc_laser_eyes_maxi', 'm', {'head': {'laser_iris': True}, 'hair_buzz': {}, 'beard_full': {}, 'skin': {}, 'top': TEE('#f7931a', prints=[PR('HFSP', w=0.11)]),
        'bottom': JEANS(), 'shoes': SNEAK(), 'acc_laser_eyes': {'acc_glow': '#ff2a1a', 'acc_dark': '#1d1f24'}},
       archetype='laser_eyes_maxi', label='Laser-Eyes Maxi', skin_tone='warm', hair_color='darkbrown', body='stocky',
       notes='Spec archetype without a contract ID (proposed id laser_eyes_maxi).'),
    L_('npc_chain_tribalist_a', 'm', {'head': {}, 'hair_locs': {}, 'skin': {}, 'top': TEE('#6b3fd1', features=['side_stripes'], stripe='#2ee6a6', prints=[PR('400', w=0.12), PR('FAST', 'back', w=0.14)]),
        'bottom_shorts': trousers('#141416'), 'shoes': SNEAK()},
       archetype='chain_tribalist', label='Chain Tribalist (purple)', skin_tone='dark', hair_color='black', body='broad', socks='#f4f4f4',
       notes='Spec pair without a contract ID (proposed id chain_tribalist).'),
    L_('npc_chain_tribalist_b', 'f', {'head': {}, 'hair': {}, 'skin': {}, 'top_tee': TEE('#2f5fe0', features=['side_stripes'], stripe='#f4f4f4', prints=[PR('2', w=0.06), PR('L2', 'back', w=0.08)]),
        'bottom_shorts': trousers('#141416'), 'shoes': SNEAK()},
       archetype='chain_tribalist', label='Chain Tribalist (blue)', skin_tone='fair', hair_color='ginger', socks='#f4f4f4'),
]

# ---------------------------------------------------------- player wardrobe combinations (tribe presets)
P_ = 'player'
LOOKS += [
    L_('player_m_trencher', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie': hoodie('#121316', PR('SEND IT', w=0.16)), 'bottom': trousers('#1d1f24'),
        'shoes': SNEAK(), 'hat_cap': cap('#121316', 'POMP', '#2ee66b')}, kind=P_, tribe='trencher', label='Trencher', skin_tone='brown', hair_color='black', tiers=TIERS),
    L_('player_m_perp', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top': TEE('#0d0d0f', prints=[PR('MAX LONG', color='#2ee66b', w=0.18)]), 'bottom_shorts': trousers('#202126'),
        'shoes': SNEAK(), 'acc_headset': {'acc_a': '#202126', 'acc_b': '#2ee66b'}}, kind=P_, tribe='perp_degen', label='Perp Degen', skin_tone='tan', hair_color='black', socks='#f4f4f4', tiers=TIERS),
    L_('player_m_shark', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top_shirt': {'color': '#f4f4f2', 'kind': 'linen', 'features': ['placket', 'buttons']}, 'bottom': trousers('#22304d'),
        'shoes': SNEAK('#5a3b22'), 'acc_shades': {}, 'hand_slip': {}}, kind=P_, tribe='prediction_shark', label='Prediction Shark', skin_tone='olive', hair_color='darkbrown', tiers=TIERS),
    L_('player_m_farmer', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top_long': TEE('#5d6b3a'), 'bottom': trousers('#6f6446'), 'shoes_boots': SNEAK('#5a3b22'),
        'hat_bucket': {'acc_a': '#5d6b3a', 'acc_b': '#5d6b3a'}, 'acc_backpack': {'acc_a': '#3a3e46', 'acc_b': '#f2b632'}}, kind=P_, tribe='airdrop_farmer', label='Airdrop Farmer', skin_tone='fair', hair_color='ginger', tiers=TIERS),
    L_('player_m_kol', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top_jacket': suit('#f2f2ee', shirt='#141416'), 'bottom': trousers('#141416'), 'shoes': SNEAK(),
        'acc_shades': {}, 'acc_gold_chain': {}}, kind=P_, tribe='caller_kol', label='Caller / KOL', skin_tone='dark', hair_color='black', tiers=TIERS),
    L_('player_m_builder', 'm', {'head': {}, 'hair_afro': {}, 'skin': {}, 'top_hoodie': hoodie('#5b5f66', PR('HACK', w=0.11)), 'bottom': JEANS(), 'shoes': SNEAK('#8d8f95'),
        'acc_glasses': {}, 'acc_backpack': {'acc_a': '#1d1f24', 'acc_b': '#2b4fd8'}}, kind=P_, tribe='builder', label='Builder (hackathon)', skin_tone='deep', hair_color='black', tiers=TIERS),
    L_('player_m_anon', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie_up': hoodie('#141416'), 'bottom': trousers('#141416'), 'shoes': SNEAK('#141416'),
        'acc_shades': {}}, kind=P_, tribe='anon', label='Anon', skin_tone='light', hair_color='black', tiers=TIERS),
    L_('player_m_lagos_kaftan', 'm', {'head': {}, 'hair_buzz': {}, 'beard_goatee': {}, 'skin': {}, 'top_kaftan': {'color': '#1d5c4a', 'kind': 'cotton', 'features': ['embroidery'], 'thread': '#d9b44a'},
        'bottom': trousers('#1d5c4a'), 'shoes': SNEAK('#5a3b22'), 'hat_fila': dict({'acc_a': '#1d5c4a'}, pattern_acc_a=ANKARA)}, kind=P_, label='Lagos kaftan + fila', skin_tone='deep', hair_color='black', tiers=TIERS),
    L_('player_m_tradfi', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top_jacket': suit('#5a5f6a', tie='#2b4fd8'), 'bottom': trousers('#5a5f6a', kind='wool'), 'shoes': SNEAK('#141416'),
        'acc_lanyard': badge('COIN2050', '#141416')}, kind=P_, label='TradFi suit (conference week)', skin_tone='light', hair_color='brown', tiers=TIERS),
    L_('player_m_coat', 'm', {'head': {}, 'hair': {}, 'beard_full': {}, 'skin': {}, 'top_coat': {'color': '#b48a5a', 'kind': 'wool', 'features': ['buttons'], 'shirt': '#141416'},
        'bottom': trousers('#141416'), 'shoes_boots': SNEAK('#141416'), 'hat_beanie': {'acc_a': '#2a2a2a', 'acc_b': '#2a2a2a', 'pattern_acc_a': {'pattern': 'knit'}}}, kind=P_, label='Camel coat + beanie', skin_tone='olive', hair_color='darkbrown', tiers=TIERS),
    L_('player_m_skr', 'm', {'head': {}, 'hair_locs': {}, 'skin': {}, 'top': TEE('#f4f4f2', prints=[PR('GM.SKR', color='#1d1f24', w=0.15)]), 'bottom': JEANS(), 'shoes': SNEAK()},
       kind=P_, label='.skr tee', skin_tone='dark', hair_color='black', tiers=TIERS),
    L_('player_f_trencher', 'f', {'head': {}, 'hair': {}, 'skin': {}, 'top_hoodie': hoodie('#b4a0e5', PR('TRENCHES', w=0.17, color='#2b2b2b')), 'bottom_shorts': JEANS(),
        'shoes': SNEAK()}, kind=P_, tribe='trencher', label='Trencher', skin_tone='warm', hair_color='brown', tiers=TIERS),
    L_('player_f_lagos_ankara', 'f', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_tee': dict(ANKARA, color='#e2582b'), 'bottom_wrap': dict(ANKARA, color='#e2582b'),
        'shoes': SNEAK('#b48a5a'), 'hat_gele': dict({'acc_a': '#f2b632', 'acc_b': '#f2b632'}, pattern_acc_b=ANKARA)}, kind=P_, label='Lagos ankara + gele', skin_tone='deep', hair_color='black', tiers=TIERS),
    L_('player_f_blazer', 'f', {'head': {}, 'hair_long': {}, 'skin': {}, 'top_jacket': suit('#e9dfcc', shirt='#141416'), 'bottom': trousers('#141416'), 'shoes': SNEAK('#141416'),
        'acc_lanyard': badge('CHECKPOINT', '#6b3fd1')}, kind=P_, label='Blazer + Checkpoint lanyard', skin_tone='light', hair_color='black', tiers=TIERS),
    L_('player_f_kol', 'f', {'head': {}, 'hair_long': {}, 'skin': {}, 'top_tee': TEE('#141416', prints=[PR('ALPHA', w=0.13)]), 'bottom_skirt': trousers('#141416', kind='leather'),
        'shoes_boots': SNEAK('#141416'), 'acc_shades': {}, 'acc_gold_chain': {}}, kind=P_, tribe='caller_kol', label='Caller / KOL', skin_tone='tan', hair_color='blonde', tiers=TIERS),
    L_('player_f_builder', 'f', {'head': {}, 'hair_buzz': {'cornrows': True}, 'skin': {}, 'top_long': TEE('#5b5f66', prints=[PR('SHIP IT', w=0.15)]), 'bottom': JEANS(),
        'shoes': SNEAK(), 'acc_glasses': {}}, kind=P_, tribe='builder', label='Builder', skin_tone='brown', hair_color='black', tiers=TIERS),
    L_('player_f_anon', 'f', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie_up': hoodie('#4b5320'), 'bottom': trousers('#141416'), 'shoes': SNEAK('#141416'),
        'acc_shades': {}}, kind=P_, tribe='anon', label='Anon', skin_tone='olive', hair_color='black', tiers=TIERS),
    L_('player_f_perp', 'f', {'head': {}, 'hair_afro': {}, 'skin': {}, 'top_tee': TEE('#d62839', prints=[PR('50X', w=0.1)]), 'bottom_shorts': trousers('#141416'),
        'shoes': SNEAK(), 'acc_headset': {'acc_a': '#141416', 'acc_b': '#d62839'}}, kind=P_, tribe='perp_degen', label='Perp Degen', skin_tone='dark', hair_color='black', socks='#f4f4f4', tiers=TIERS),
    L_('player_f_farmer', 'f', {'head': {}, 'hair_long': {}, 'skin': {}, 'top_shirt': {'color': '#2f6b3a', 'pattern': 'plaid', 'colors': ['#14301b', '#e8d9b0'], 'features': ['placket']},
        'bottom': JEANS(), 'shoes_boots': SNEAK('#5a3b22'), 'hat_straw': {'acc_a': '#d8b46a', 'acc_b': '#2f6b3a', 'pattern_acc_a': {'pattern': 'straw'}}}, kind=P_, tribe='airdrop_farmer', label='Airdrop Farmer', skin_tone='fair', hair_color='auburn', tiers=TIERS),
    L_('player_f_shark', 'f', {'head': {}, 'hair': {}, 'skin': {}, 'top_long': TEE('#f4f4f2'), 'outer_vest': {'color': '#141416', 'kind': 'fleece', 'features': ['zip']},
        'bottom': trousers('#22304d'), 'shoes': SNEAK(), 'hat_cap': cap('#22304d', 'ODDS')}, kind=P_, tribe='prediction_shark', label='Prediction Shark', skin_tone='tan', hair_color='darkbrown', tiers=TIERS),
    L_('player_f_coat', 'f', {'head': {}, 'hair': {}, 'skin': {}, 'top_coat': {'color': '#8c1c2b', 'kind': 'wool', 'features': ['buttons', 'belt'], 'belt': '#5a1220'},
        'bottom': trousers('#141416'), 'shoes_boots': SNEAK('#141416')}, kind=P_, label='Red coat', skin_tone='olive', hair_color='black', tiers=TIERS),
    L_('player_f_lagos_kaftan', 'f', {'head': {}, 'hair_afro': {}, 'skin': {}, 'top_kaftan': dict(ANKARA, color='#1a3d8f', colors=['#f2b632', '#e2582b', '#f4f4f2']),
        'bottom': trousers('#1a3d8f'), 'shoes': SNEAK('#b48a5a')}, kind=P_, label='Ankara kaftan', skin_tone='brown', hair_color='black', tiers=TIERS),
]

# ---------------------------------------------------------- updates from docs/research/ct-now-2026-10 (archetypes + fashion)
# Fashion there is mostly inference (direction, not fact). Parody only: no real logos/brands/people.
OVR = [
    L_('npc_town_crier', 'm', {'head': {'iris': 'brown'}, 'hair_buzz': {}, 'skin': {}, 'top_long': TEE('#c8102e', prints=[PR('JUST IN')]),
        'bottom': JEANS('#1c1c1f'), 'shoes': SNEAK(), 'hat_siren': {'acc_a': '#1d1f24', 'acc_b': '#c8102e'}, 'hand_megaphone': {},
        'acc_lanyard': badge('PRESS', '#c8102e')},
       archetype='town_crier', label='Town Crier (JUST IN)', skin_tone='warm', hair_color='black', body='stocky', face={'jaw_wide': .4}, scale=1.02),
    L_('npc_giveaway_guy', 'm', {'head': {'iris': 'darkbrown'}, 'hair_buzz': {}, 'beard_goatee': {}, 'skin': {}, 'top': TEE('#f4f4f2', prints=[PR('DROP WALLET', color='#111111', w=0.24)]),
        'bottom': trousers('#6b6e75'), 'shoes': SNEAK(), 'acc_gold_chain': {}, 'hand_phone': {'acc_screen': '#3a7bd5'}, 'hat_cap': cap('#f2b632', 'GIVEAWAY', '#111111')},
       archetype='giveaway_guy', label='Giveaway Guy', skin_tone='dark', hair_color='black', face={'cheeks_full': .4},
       notes='Dossier: gold chain, two phones, fresh cap. Second phone not modelled (one hand prop per look).'),
    L_('npc_alpha_caller', 'm', {'head': {'stubble': True}, 'hair': {}, 'skin': {}, 'top_hoodie': hoodie('#16171b', PR('100X', color='#c8102e', w=0.11)),
        'bottom': trousers('#16171b'), 'shoes': SNEAK(), 'acc_shades': {}, 'acc_anime_mask': {'acc_a': '#f7f3ee', 'acc_dark': '#c8102e',
        'prints': [{'on': 'acc_a', 'text': 'CA', 'color': '#c8102e', 'w': 0.04, 'h': 0.025, 'facing': (1, 0, 0)}]}},
       archetype='alpha_caller', label='Alpha Caller', skin_tone='olive', hair_color='black', face={'jaw_wide': .2}),
    L_('npc_rug_dev', 'm', {'head': {'stubble': True, 'eyebags': True}, 'hair': {}, 'skin': {}, 'top_hoodie': hoodie('#4a4d55', PR('NO INSIDERS', w=0.2)),
        'bottom': trousers('#4b5320'), 'shoes': SNEAK('#141416'), 'acc_ring_light': {'acc_glow': '#d6f0ff', 'acc_b': '#1d1f24'}, 'hand_burner': {}},
       archetype='rug_dev', label='Rug Dev / Livestream Dev', skin_tone='warm', hair_color='darkbrown', face={'face_round': .5},
       notes='Dossier: hoodie, burner phone, webcam rig + LED strip (ring-light rig used as the stream rig).'),
    L_('npc_perp_degen', 'm', {'head': {'eyebags': True, 'iris': 'brown'}, 'hair': {}, 'skin': {}, 'top': TEE('#121316', prints=[PR('50X', color='#2ee66b', w=0.09)]),
        'bottom': trousers('#202126'), 'shoes': SNEAK('#d62839'), 'acc_headset': {'acc_a': '#202126', 'acc_dark': '#0c0c0e', 'acc_b': '#2ee66b'}, 'hand_energy': {'acc_a': '#2ee66b'}},
       archetype='perp_degen_npc', label='Perp Degen (compression tee)', skin_tone='olive', hair_color='black', body='heavy'),
    L_('npc_laser_eyes_maxi', 'm', {'head': {'laser_iris': True}, 'hair_buzz': {}, 'beard_full': {}, 'skin': {}, 'top_jacket': suit('#24262c', tie='#f7931a'),
        'bottom': trousers('#24262c', kind='wool'), 'shoes': SNEAK('#141416'), 'acc_laser_eyes': {'acc_glow': '#ff2a1a', 'acc_dark': '#1d1f24'}},
       archetype='laser_eyes_maxi', label='Laser-Eyes Maxi (orange tie)', skin_tone='warm', hair_color='darkbrown', body='stocky',
       notes='Spec archetype without a contract ID (proposed id laser_eyes_maxi).'),
    L_('npc_auditor', 'f', {'head': {'iris': 'brown'}, 'hair_long': {}, 'skin': {}, 'top_long': TEE('#f2f2ee', features=['placket', 'buttons']),
        'outer_vest': {'color': '#c9f12a', 'pattern': 'hivis', 'kind': 'hivis', 'features': ['zip']},
        'bottom': trousers('#5a5f6a'), 'shoes': SNEAK('#141416'), 'acc_lanyard': badge('AUDIT', '#c8102e'), 'hand_clipboard': {}, 'acc_glasses': {}},
       archetype='auditor', label='Auditor (hi-vis + lanyard)', skin_tone='tan', hair_color='brown'),
    L_('npc_vc', 'm', {'head': {'iris': 'blue'}, 'hair': {}, 'skin': {}, 'top_long': TEE('#a9c4e8', features=['placket', 'buttons'], button='#f4f4f4'),
        'outer_vest': {'color': '#1f2c45', 'kind': 'fleece', 'features': ['zip', 'patch']}, 'bottom': trousers('#c2ad84'),
        'shoes': SNEAK('#5a3b22'), 'acc_lanyard': badge('VC ROW', '#1f2c45')},
       archetype='vc', label='VC (fleece vest over oxford)', skin_tone='light', hair_color='brown', face={'jaw_wide': .3}),
    L_('npc_spaces_host', 'f', {'head': {'iris': 'hazel'}, 'hair_afro': {}, 'skin': {}, 'top_hoodie': hoodie('#9b7fd4', PR('GM GM', w=0.14)),
        'bottom_skirt': trousers('#1d1f24'), 'shoes': SNEAK(), 'hat_headphones': {'acc_a': '#f4f4f4', 'acc_dark': '#1d1f24'}},
       archetype='spaces_host', label='Spaces Host', skin_tone='warm', hair_color='black', body='curvy'),
    L_('npc_normie', 'm', {'head': {'iris': 'blue'}, 'hair': {}, 'skin': {}, 'top_shirt': {'color': '#2a8fb8', 'pattern': 'floral', 'colors': ['#fff4e0', '#1e6b45', '#f2b632']},
        'bottom_shorts': trousers('#c8b48a'), 'shoes': SNEAK(), 'hat_bucket': {'acc_a': '#e9dcc0', 'acc_b': '#e9dcc0'}, 'acc_camera': {}, 'hand_phone': {'acc_screen': '#ffffff'}},
       archetype='normie', label='Normie (tourist, new phone)', skin_tone='fair', hair_color='brown', body='heavy', socks='#f4f4f4', scale=0.99),
    L_('npc_doxxed_founder', 'm', {'head': {}, 'hair_buzz': {}, 'beard_goatee': {}, 'skin': {}, 'top_jacket': suit('#1b2540', shirt='#f4f4f2'),
        'bottom': trousers('#1b2540', kind='wool'), 'shoes': SNEAK(), 'acc_lanyard': badge("WE'RE HIRING", '#2b4fd8')},
       archetype='doxxed_founder', label='Doxxed Founder (suit + lanyard)', skin_tone='deep', hair_color='black', face={'jaw_wide': .2}, scale=1.02,
       notes='Task brief asks for a suit; dossier says startup tee + lanyard. Suit with hiring lanyard; tee variant = player_m_skr parts.'),
    L_('player_m_trencher', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie': hoodie('#121316', PR('SEND IT', w=0.16)), 'bottom': trousers('#1d1f24'),
        'shoes': SNEAK(), 'acc_headset': {'acc_a': '#121316', 'acc_b': '#2ee66b'}}, kind=P_, tribe='trencher', label='Trencher (hoodie, joggers, headset)', skin_tone='brown', hair_color='black', tiers=TIERS),
    L_('player_m_lagos_kaftan', 'm', {'head': {}, 'hair_buzz': {}, 'beard_goatee': {}, 'skin': {}, 'top_kaftan': {'color': '#1d5c4a', 'kind': 'cotton', 'features': ['embroidery'], 'thread': '#d9b44a'},
        'bottom': trousers('#1d5c4a'), 'shoes': SNEAK(), 'hat_fila': dict({'acc_a': '#1d5c4a'}, pattern_acc_a=ANKARA), 'hand_carkey': {}},
       kind=P_, label='Lagos senator/kaftan + fila', skin_tone='deep', hair_color='black', tiers=TIERS),
    L_('player_m_anon', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie_up': hoodie('#141416'), 'bottom': trousers('#141416'), 'shoes': SNEAK('#141416'),
        'acc_shades': {}, 'acc_anime_mask': {'acc_a': '#f7f3ee', 'acc_dark': '#6b3fd1'}}, kind=P_, tribe='anon', label='Anon (hood, mask)', skin_tone='light', hair_color='black', tiers=TIERS),
]
NEW = [  # dossier "new" archetypes (no contract IDs yet: proposals)
    L_('npc_odds_crier', 'f', {'head': {}, 'hair': {}, 'skin': {}, 'top_shirt': TEE('#f2f2ee', pattern='stripes', colors=['#1f7a4d'], freq=26, features=['placket']),
        'bottom': trousers('#141416'), 'shoes': SNEAK(), 'hat_cap': cap('#1f7a4d', 'YES 68'), 'hand_slip': {}},
       archetype='odds_crier', label='Odds Crier', skin_tone='brown', hair_color='black', notes='Dossier new archetype; proposed id odds_crier.'),
    L_('npc_treasury_chairman', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top_jacket': suit('#30343c', shirt='#f4f4f2'), 'bottom': trousers('#30343c', kind='wool'),
        'shoes': SNEAK(), 'hat_cap': cap('#c9a227', 'COIN', '#141416')},
       archetype='treasury_chairman', label='Treasury Chairman', skin_tone='light', hair_color='grey', body='heavy', face={'jaw_wide': .4}, notes='Proposed id treasury_chairman.'),
    L_('npc_tradfi_suit', 'f', {'head': {}, 'hair_long': {}, 'skin': {}, 'top_jacket': suit('#1b2540', shirt='#f4f4f2'), 'bottom': trousers('#1b2540', kind='wool'),
        'shoes': SNEAK(), 'acc_pins': {}},
       archetype='tradfi_suit', label='TradFi Suit (no tie, white sneakers)', skin_tone='olive', hair_color='darkbrown', notes='Proposed id tradfi_suit.'),
    L_('npc_cto_lead', 'm', {'head': {}, 'hair_afro': {}, 'skin': {}, 'top_hoodie': hoodie('#f2b632', PR('CTO', color='#141416', w=0.1)), 'bottom': JEANS(), 'shoes': SNEAK()},
       archetype='cto_lead', label='CTO Lead', skin_tone='dark', hair_color='black', notes='Proposed id cto_lead.'),
    L_('npc_clip_farmer', 'f', {'head': {}, 'hair_buzz': {'cornrows': True}, 'skin': {}, 'top_tee': TEE('#2ee6a6', prints=[PR('CLIP IT', color='#141416', w=0.15)]),
        'bottom_shorts': JEANS('#16171b'), 'shoes': SNEAK(), 'hand_phone': {'acc_screen': '#ff4fa3'}},
       archetype='clip_farmer', label='Clip Farmer', skin_tone='deep', hair_color='black', notes='Proposed id clip_farmer; gimbal approximated by phone.'),
    L_('npc_copy_trade_victim', 'm', {'head': {'tears': True}, 'hair': {}, 'skin': {}, 'top': TEE('#d62839', prints=[PR('-90', w=0.08)]), 'bottom': JEANS(),
        'shoes': SNEAK(), 'hand_phone': {'acc_screen': '#d62839'}},
       archetype='copy_trade_victim', label='Copy-Trade Victim', skin_tone='light', hair_color='black', body='slim', notes='Proposed id copy_trade_victim.'),
    L_('npc_hiring_bait_farmer', 'f', {'head': {}, 'hair_long': {}, 'skin': {}, 'top_jacket': suit('#6b7280', shirt='#f4f4f2'), 'bottom': trousers('#141416'),
        'shoes': SNEAK('#141416'), 'acc_lanyard': badge('HIRING', '#d62839'), 'hand_clipboard': {}},
       archetype='hiring_bait_farmer', label='Hiring-Bait Farmer', skin_tone='warm', hair_color='black', notes='Proposed id hiring_bait_farmer.'),
    L_('npc_web3_jobs_hustler', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_shirt': dict(ANKARA, color='#1a3d8f', features=['placket']), 'bottom': trousers('#141416'),
        'shoes': SNEAK(), 'acc_ring_light': {'acc_glow': '#fff6e8', 'acc_b': '#1d1f24'}, 'hand_phone': {}},
       archetype='web3_jobs_hustler', label='Web3 Jobs Hustler (Lagos)', skin_tone='deep', hair_color='black', notes='Proposed id web3_jobs_hustler; Ankara shirt.'),
    L_('npc_celebrity_coin_founder', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top_jacket': suit('#141416', shirt='#f4f4f2'), 'bottom': trousers('#141416'),
        'shoes': SNEAK(), 'acc_shades': {}, 'acc_gold_chain': {}},
       archetype='celebrity_coin_founder', label='Celebrity Coin Founder', skin_tone='tan', hair_color='blonde', face={'jaw_wide': .5}, notes='Proposed id; parody, no real likeness.'),
    L_('npc_tip_coin_pilgrim', 'f', {'head': {}, 'hair': {}, 'skin': {}, 'top_tee': TEE('#f2b632', features=['side_stripes'], stripe='#141416', prints=[PR('10', w=0.07, color='#141416'), PR('FAN', 'back', w=0.1, color='#141416')]),
        'bottom': JEANS(), 'shoes': SNEAK()},
       archetype='tip_coin_pilgrim', label='Tip-Coin Pilgrim (fan jersey)', skin_tone='olive', hair_color='brown', notes='Proposed id tip_coin_pilgrim.'),
    L_('npc_holder_rewards_farmer', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top': TEE('#2e8b57', prints=[PR('HODL', w=0.1)]), 'bottom': trousers('#6f6446'),
        'shoes_boots': SNEAK('#5a3b22'), 'hat_straw': {'acc_a': '#d8b46a', 'acc_b': '#2e8b57', 'pattern_acc_a': {'pattern': 'straw'}}, 'hand_watering_can': {'tint': '#d9a93b'}},
       archetype='holder_rewards_farmer', label='Holder-Rewards Farmer', skin_tone='warm', hair_color='black', body='stocky', notes='Proposed id; gold-tinted can.'),
    L_('npc_altseason_prophet', 'm', {'head': {}, 'hair_afro': {}, 'beard_full': {}, 'skin': {}, 'top': TEE('#3b1f5c', prints=[PR('ALTSZN', color='#f2b632', w=0.15)]),
        'bottom': JEANS(), 'shoes': SNEAK()},
       archetype='altseason_prophet', label='Altseason Prophet', skin_tone='brown', hair_color='grey', notes='Proposed id; crystal ball not modelled.'),
    L_('npc_quantum_doomer', 'm', {'head': {'eyebags': True}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie_up': hoodie('#3a3e46', lining='#c9ccd0'), 'bottom': trousers('#3a3e46'),
        'shoes': SNEAK('#141416'), 'acc_glasses': {}},
       archetype='quantum_doomer', label='Quantum Doomer (tinfoil-lined hood)', skin_tone='fair', hair_color='brown', body='slim', notes='Proposed id quantum_doomer.'),
    L_('npc_privacy_maxi', 'f', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_hoodie': hoodie('#0d0d0f'), 'bottom': trousers('#0d0d0f'), 'shoes': SNEAK('#0d0d0f'),
        'acc_pins': {}, 'acc_shades': {}},
       archetype='privacy_maxi', label='Privacy Maxi (all black)', skin_tone='light', hair_color='platinum', notes='Proposed id privacy_maxi.'),
    L_('npc_side_event_hopper', 'f', {'head': {}, 'hair': {}, 'skin': {}, 'top_tee': TEE('#ff7a1a', prints=[PR('COIN2050', w=0.17)]), 'bottom': JEANS(),
        'shoes': SNEAK(), 'acc_lanyard': badge('VIP', '#141416'), 'acc_backpack': {'acc_a': '#e9dcc0', 'acc_b': '#ff7a1a'}},
       archetype='side_event_hopper', label='Side-Event Hopper', skin_tone='tan', hair_color='black', notes='Proposed id; tote approximated by backpack, one lanyard.'),
    L_('npc_ticket_code_shiller', 'm', {'head': {}, 'hair': {}, 'skin': {}, 'top': TEE('#6b3fd1', prints=[PR('STAFF', w=0.12)]), 'bottom': JEANS('#16171b'),
        'shoes': SNEAK(), 'acc_lanyard': badge('CODE 25', '#6b3fd1')},
       archetype='ticket_code_shiller', label='Ticket-Code Shiller', skin_tone='olive', hair_color='black', notes='Proposed id ticket_code_shiller.'),
    L_('npc_hackathon_grinder', 'f', {'head': {'eyebags': True}, 'hair_afro': {}, 'skin': {}, 'top_hoodie': hoodie('#2b4fd8', PR('HACK', w=0.11)), 'bottom': JEANS(),
        'shoes': SNEAK(), 'hand_energy': {'acc_a': '#ff4fa3'}},
       archetype='hackathon_grinder', label='Hackathon Grinder', skin_tone='dark', hair_color='black', notes='Proposed id hackathon_grinder.'),
    L_('npc_lp_army_farmer', 'm', {'head': {}, 'hair_buzz': {}, 'skin': {}, 'top_long': TEE('#4b5320', pattern='camo', colors=['#2f3517', '#6b6f3a']),
        'bottom': trousers('#2f3517'), 'shoes_boots': SNEAK('#141416'), 'hat_cap': cap('#4b5320', 'LP', '#f2b632')},
       archetype='lp_army_farmer', label='LP Army Farmer', skin_tone='tan', hair_color='black', body='broad', notes='Proposed id lp_army_farmer.'),
    L_('npc_seeker_farmer', 'f', {'head': {}, 'hair_long': {}, 'skin': {}, 'top_tee': TEE('#141416', prints=[PR('GM.SKR', color='#2ee6a6', w=0.15)]), 'bottom': JEANS(),
        'shoes': SNEAK(), 'hand_phone': {'acc_dark': '#2b2d33', 'acc_screen': '#2ee6a6'}},
       archetype='seeker_farmer', label='Seeker Farmer / SKR Maxi', skin_tone='brown', hair_color='black', notes='Proposed id seeker_farmer; generic phone, no Seeker branding.'),
    L_('npc_hermit_crew', 'm', {'skin': {}, 'head': {}, 'hair_buzz': {}, 'top_hoodie_up': hoodie('#141416'), 'bottom': trousers('#141416'), 'shoes': SNEAK('#141416'),
        'acc_visor': {'acc_glow': '#141416', 'acc_dark': '#141416'}},
       archetype='hermit_crew', label='Hermit Crew (faceless)', skin_tone='brown', hair_color='black', notes='Proposed id hermit_crew; opaque black visor = no face.'),
]
_ovr = {l['id']: l for l in OVR}
LOOKS = [_ovr.get(l['id'], l) for l in LOOKS] + NEW
