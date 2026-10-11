"""Texture library for the cast: slot crops, recolours, skin tones, hair colours, procedural prints.
Pure numpy over raw (non-colour-managed) sRGB-encoded floats. Row 0 = bottom (Blender order).
"""
import json, math
import numpy as np
from pathlib import Path
from cast_common import *

RES = 2048  # authoring layout resolution

SKIN_TONES = {
    'deep': '#3b2418', 'dark': '#55331f', 'brown': '#74472b', 'warm': '#8f5e3c', 'tan': '#a8754f',
    'olive': '#a87d55', 'light': '#b47f5c', 'fair': '#c08b69', 'rekt_pink': '#d99a92',
}
HAIR_COLORS = {
    'black': '#17120f', 'darkbrown': '#2f2016', 'brown': '#4f3322', 'auburn': '#6e3420', 'ginger': '#a2532a',
    'blonde': '#b8935c', 'platinum': '#d9d0bd', 'grey': '#87827d', 'white': '#d8d4cc', 'pink': '#d8609f',
    'blue': '#3b67c9', 'green': '#3c9c62', 'purple': '#6f47b8', 'red': '#b52a2a',
}

class SlotTex:
    """Slot-sized texture set for one part: base/normal/ms (+emis), masks and 3D lookup maps."""
    def __init__(self, slot, base, normal, ms, cov, sub, region, pos, nrm, emis=None, ao=None):
        self.slot, self.base, self.normal, self.ms = slot, base, normal, ms
        self.cov, self.sub, self.region, self.pos, self.nrm = cov, sub, region, pos, nrm
        self.emis = emis if emis is not None else np.zeros_like(base)
        self.ao = ao if ao is not None else np.ones(base.shape[:2], dtype=np.float32)

    def copy(self):
        return SlotTex(self.slot, self.base.copy(), self.normal.copy(), self.ms.copy(), self.cov.copy(), self.sub.copy(),
                       self.region.copy(), self.pos, self.nrm, self.emis.copy(), self.ao)

    def mask(self, *names):
        ids = [SUB_IDS[n] for n in names]
        return self.cov & np.isin(self.sub, ids)

_cache = {}

def load_family_maps(fam, prefix='base'):
    key = (fam, prefix)
    if key in _cache:
        return _cache[key]
    d = fam_dir(fam)
    m = {
        'base': read_image(d / f'{prefix}_BaseColor.png', 3),
        'normal': read_image(d / f'{prefix}_Normal.png', 3),
        'ms': read_image(d / f'{prefix}_MetallicSmoothness.png', 4),
        'pos': np.load(d / f'{prefix}_pos.npy').astype(np.float32) * 2 - 1,
        'nrm': np.load(d / f'{prefix}_nrm.npy').astype(np.float32) * 2 - 1,
        'id': np.load(d / f'{prefix}_id.npy'),
    }
    _cache[key] = m
    return m

def crop(fam, slot, prefix='base', subs=None):
    if prefix != 'base':
        key = (fam, prefix)
        if key not in _cache:
            z = np.load(fam_dir(fam) / 'maps' / f'{prefix}.npz')
            _cache[key] = {k: z[k] for k in z.files}
        z = _cache[key]
        sub = z['sub'].astype(np.int32)
        cov = sub > 0
        ao = z['ao'].astype(np.float32) / 255 if 'ao' in z else None
        if ao is not None:
            # AO ~0 marks buried or (formerly) inward-wound faces, not real contact shadow
            ao = np.where(ao < 0.2, 0.85, ao)
        return SlotTex(slot, z['base'].astype(np.float32) / 255, z['normal'].astype(np.float32) / 255,
                       z['ms'].astype(np.float32) / 255, cov, sub, z['region'].astype(np.int32),
                       z['pos'].astype(np.float32), z['nrm'].astype(np.float32), ao=ao)
    m = load_family_maps(fam, prefix)
    r0, r1, c0, c1 = slot_px(slot, RES)
    sl = (slice(r0, r1), slice(c0, c1))
    sub = m['id'][sl][:, :, 0].astype(np.int32)
    cov = sub > 0
    if subs is not None:
        cov = cov & np.isin(sub, list(subs))
    return SlotTex(slot, m['base'][sl].copy(), m['normal'][sl].copy(), m['ms'][sl].copy(), cov, sub,
                   m['id'][sl][:, :, 1].astype(np.int32), m['pos'][sl], m['nrm'][sl])

# ------------------------------------------------------------------ colour ops

def lin(c):
    return srgb_to_lin(c)

def tone_shift(rgb, mask, target_hex, src_ref=None, strength=1.0):
    """Multiplicative per-channel skin-tone transfer in linear light (keeps blush/lip variation)."""
    l = lin(rgb)
    m = mask.astype(bool)
    if not m.any():
        return rgb
    ref = src_ref if src_ref is not None else np.median(l[m], axis=0)
    tgt = lin(hexrgb(target_hex))
    # per-channel power transfer keeps shading/blush but avoids blowing out highlights on big shifts
    rel = np.clip(l[m] / np.maximum(ref, 1e-4)[None, :], 0, 4) ** 0.8
    out = l.copy()
    out[m] = np.clip(l[m] * (1 - strength) + strength * tgt[None, :] * rel, 0, 1)
    return lin_to_srgb(out)

def hair_recolor(rgb, mask, target_hex, ref=None):
    """Hair: keep strand luminance detail, compress range for very dark sources."""
    l = lin(rgb)
    m = mask.astype(bool)
    if not m.any():
        return rgb
    L = luminance(l)
    r = ref if ref is not None else float(np.median(L[m]))
    shade = np.clip(L / max(r, 1e-4), 0, 3) ** 0.85
    tgt = lin(hexrgb(target_hex))
    out = l.copy()
    out[m] = np.clip(tgt[None, :] * shade[m][:, None], 0, 1)
    return lin_to_srgb(out)

def fabric_recolor(rgb, mask, target_hex, ref=None, detail=1.0):
    l = lin(rgb)
    m = mask.astype(bool)
    if not m.any():
        return rgb
    L = luminance(l)
    r = ref if ref is not None else float(np.median(L[m]))
    shade = np.clip(L / max(r, 1e-4), 0, 2.5) ** detail
    tgt = lin(hexrgb(target_hex))
    out = l.copy()
    out[m] = np.clip(tgt[None, :] * shade[m][:, None], 0, 1)
    return lin_to_srgb(out)

def shade_of(t, mask):
    """Relative fabric shading (folds/AO) of a SlotTex region in linear luminance."""
    L = luminance(lin(t.base))
    m = mask.astype(bool)
    r = float(np.median(L[m])) if m.any() else 0.5
    return np.clip(L / max(r, 1e-4), 0, 2.5)

def paint(t, mask, rgb_hex_or_arr, shade=None, alpha=1.0):
    """Paint a flat colour (or per-texel sRGB array) multiplied by shade into t.base under mask."""
    m = mask.astype(bool) if mask.dtype != np.float32 else mask > 0.5
    col = hexrgb(rgb_hex_or_arr) if isinstance(rgb_hex_or_arr, str) else rgb_hex_or_arr
    l = lin(t.base)
    c = lin(col)
    if c.ndim == 1:
        c = np.broadcast_to(c, l.shape)
    s = shade if shade is not None else np.ones(l.shape[:2], dtype=np.float32)
    new = np.clip(c * s[..., None], 0, 1)
    a = (alpha if np.isscalar(alpha) else alpha[..., None])
    l[m] = (l * (1 - a) + new * a)[m] if not np.isscalar(alpha) else (l[m] * (1 - a) + new[m] * a)
    t.base = lin_to_srgb(l)

def set_ms(t, mask, metal=0.0, smooth=0.3):
    m = mask.astype(bool)
    t.ms[m, 0] = metal
    t.ms[m, 1] = 1
    t.ms[m, 2] = 0
    t.ms[m, 3] = smooth

def noise3(p, scale, seed=0, octaves=3):
    """Cheap smooth value noise over 3D positions (HxWx3) -> HxW in [0,1]."""
    rng = np.random.default_rng(seed)
    out = np.zeros(p.shape[:2], dtype=np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        f = scale * (2 ** o)
        k = rng.normal(size=(6, 3)).astype(np.float32)
        ph = rng.uniform(0, 6.283, size=6).astype(np.float32)
        acc = np.zeros(p.shape[:2], dtype=np.float32)
        for i in range(6):
            acc += np.sin((p @ k[i]) * f + ph[i])
        out += amp * (acc / 6)
        tot += amp
        amp *= 0.5
    return np.clip(out / tot * 0.9 + 0.5, 0, 1)

# ------------------------------------------------------------------ text / decals

def text_mask(text, h, w, scale=1):
    """Render text with the repo's 5x7 bitmap font into an (h,w) mask (top-left origin)."""
    import sys
    sys.path.insert(0, str(REPO / 'art/pipeline'))
    import bitfont
    G = bitfont._G
    glyph_w = 6
    n = len(text)
    raw = np.zeros((7, n * glyph_w), dtype=np.float32)
    for i, ch in enumerate(text.upper()):
        g = G.get(ch, G[' '])
        for r, row in enumerate(g):
            for c, bit in enumerate(row):
                if bit == '1':
                    raw[r, i * glyph_w + c] = 1
    raw = raw[:, :-1] if raw.shape[1] > 1 else raw
    # nearest resample to (h, w)
    ys = (np.arange(h) * raw.shape[0] / h).astype(int)
    xs = (np.arange(w) * raw.shape[1] / w).astype(int)
    return raw[ys][:, xs]

def decal_coords(t, center, size, facing=(0, -1, 0), up=(0, 0, 1)):
    """Planar projection coords (u,v in [0,1]) of texels near a 3D point, facing a direction.
    Returns u, v arrays and a validity mask."""
    c = np.array(center, dtype=np.float32)
    f = np.array(facing, dtype=np.float32); f /= np.linalg.norm(f)
    upv = np.array(up, dtype=np.float32)
    right = np.cross(upv, f); right /= np.linalg.norm(right)
    upv = np.cross(f, right)
    d = t.pos - c
    u = (d @ right) / size[0] + 0.5
    v = (d @ upv) / size[1] + 0.5
    face = (t.nrm @ f) > 0.25
    ok = t.cov & face & (u >= 0) & (u <= 1) & (v >= 0) & (v <= 1) & (np.abs(d @ f) < 0.12)
    return u, v, ok

def stamp_text(t, text, center, size, color_hex, facing=(0, -1, 0), emissive=False, flip=False):
    u, v, ok = decal_coords(t, center, size, facing)
    H, W = 70, max(6 * len(text) * 10, 60)
    mk = text_mask(text, H, W)
    uu = np.clip(((1 - u) if flip else u) * (W - 1), 0, W - 1).astype(int)
    vv = np.clip((1 - v) * (H - 1), 0, H - 1).astype(int)
    hit = ok & (mk[vv, uu] > 0.5)
    paint(t, hit, color_hex, shade_of(t, t.cov) ** 0.5)
    if emissive:
        t.emis[hit] = hexrgb(color_hex)
    return hit

def stamp_shape(t, fn, center, size, color_hex, facing=(0, -1, 0), shade=True):
    """fn(u, v) -> bool mask in decal space."""
    u, v, ok = decal_coords(t, center, size, facing)
    hit = ok & fn(u, v)
    paint(t, hit, color_hex, shade_of(t, t.cov) ** 0.5 if shade else None)
    return hit

# ------------------------------------------------------------------ composition

def _down(img, cov, f):
    """Coverage-weighted box downsample by integer factor f."""
    if f == 1:
        return img, cov.astype(np.float32)
    h, w = cov.shape
    c = cov.astype(np.float32)
    ci = c.reshape(h // f, f, w // f, f).sum(axis=(1, 3))
    acc = (img * c[..., None]).reshape(h // f, f, w // f, f, -1).sum(axis=(1, 3))
    out = acc / np.maximum(ci[..., None], 1e-6)
    return out, (ci > 0).astype(np.float32)

def compose_atlas(slot_texs, res_out=1024):
    """Assemble slot textures into full atlases (base, normal, ms, emis) at res_out."""
    f = RES // res_out
    base = np.zeros((res_out, res_out, 3), dtype=np.float32); base[:] = 0.5
    nrm = np.zeros((res_out, res_out, 3), dtype=np.float32); nrm[:] = (0.5, 0.5, 1.0)
    ms = np.zeros((res_out, res_out, 4), dtype=np.float32); ms[:, :, 1] = 1; ms[:, :, 3] = 0.25
    emis = np.zeros((res_out, res_out, 3), dtype=np.float32)
    for t in slot_texs:
        r0, r1, c0, c1 = slot_px(t.slot, res_out)
        for arr, dst in ((t.base, base), (t.normal, nrm), (t.ms, ms), (t.emis, emis)):
            small, c = _down(arr, t.cov, f)
            dst[r0:r1, c0:c1] = dilate(small, c > 0, 10)
    n = nrm * 2 - 1
    n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-6)
    return {'base': base, 'normal': n * 0.5 + 0.5, 'ms': ms, 'emis': emis}
