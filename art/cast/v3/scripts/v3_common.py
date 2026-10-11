"""Cast v3 shared definitions. Builds on v2 (CTS skeleton, sockets, budgets, palettes) and adds:
  * two more tint regions in the previously reserved mask1 channels: brows (B) and hat/headwear (A);
  * family layout (one atlas + one skeleton shared by all characters of a Quaternius modular family);
  * v3 paths (art/cast/v3, evidence in ct-life-evidence/cast-hermes-20261010).
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'v2' / 'scripts'))
sys.path.insert(1, str(Path(__file__).resolve().parents[2] / 'scripts'))
import v2_common as V2C
from v2_common import (CTS_PARENT, CTS_BONES, CTS_DEFORM, HUMANOID, SOCKETS, TIERS, CAPS, MAX_INFL, HAIR_COLORS,
                       CLOTH_COLORS, log)

REPO = V2C.REPO
V2 = V2C.V2
V2EXPORT = V2C.EXPORT
V3 = REPO / 'art/cast/v3'
EXPORT = V3 / 'export'
PREVIEWS = V3 / 'previews'
EVID = Path('/Users/mac/Documents/codes/projects/ct-life-evidence/cast-hermes-20261010')
RAW_Q = V2C.RAW_Q
UMM_DIR = RAW_Q / 'ultimate_modular_men/Humanoid Rig/Individual Characters/FBX'
UMW_DIR = RAW_Q / 'ultimate_modular_women/Humanoid Rigs/Individual Characters/FBX'

# ---------------------------------------------------------------- tint regions (v2 + brows + hat)
# mask0: R skin, G hair, B top, A bottom; mask1: R shoes, G accent, B brows, A hat/headwear
TINT_REGIONS = ['skin', 'hair', 'top', 'bottom', 'shoes', 'accent', 'brows', 'hat']
REGION_ID = {'none': 0, 'skin': 1, 'hair': 2, 'top': 3, 'bottom': 4, 'shoes': 5, 'accent': 6, 'eyes': 7, 'brows': 8, 'hat': 9}
MASK_CHANNEL = {'skin': (0, 0), 'hair': (0, 1), 'top': (0, 2), 'bottom': (0, 3), 'shoes': (1, 0), 'accent': (1, 1),
                'brows': (1, 2), 'hat': (1, 3)}
STARTER_ACCESSORIES = ['pixel_face_mask', 'visor', 'helmet', 'laser_eyes', 'chain', 'headphones', 'small_wings', 'shoulder_drone']

HAT_COLORS = {'yellow': '#e8b81c', 'orange': '#f07a22', 'black': '#1c1d22', 'white': '#f0f0ea', 'red': '#c8102e',
              'sol_purple': '#8a4dff', 'teal': '#1aa6a6', 'khaki': '#b9a27a', 'grey': '#6b6e74'}

def brow_color(hair_hex):
    """Default brow tint derived from the hair tint: same hue, luminance capped so blonde/platinum/pastel
    hair never produces white brows (the v2 defect). Linear-light; returns sRGB hex."""
    import numpy as np
    h = hair_hex.lstrip('#')
    c = np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    L = float(lin @ [0.2126, 0.7152, 0.0722])
    target = min(L * 0.55, 0.075) if L > 0.02 else L
    lin = lin * (target / max(L, 1e-4))
    # keep brows slightly warm/neutral (never saturated pink/blue at full strength)
    grey = float(lin @ [0.2126, 0.7152, 0.0722])
    lin = grey + (lin - grey) * 0.6
    s = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.clip(lin, 0, 1) ** (1 / 2.4) - 0.055)
    return '#%02x%02x%02x' % tuple(int(round(float(x) * 255)) for x in np.clip(s, 0, 1))

# ---------------------------------------------------------------- family atlas layout (16x16 grid of cells)
# Each character owns a 4x5-cell block: head 4x2 (top), top 4x2, bottom 2x1, feet 2x1 (bottom row).
# The 4x4-cell block x12-15, y2-5 is the v2 accessory palette slot ([0.75,1]x[0.125,0.375]) so v2 accessory UVs
# and pixel faces keep working unchanged.
FGRID = 16
BLOCK_W, BLOCK_H = 4, 5
ACC_BLOCK = (12, 2, 4, 4)

FAMILY_BLOCKS = [(0, 0), (4, 0), (8, 0), (0, 6), (4, 6), (8, 6), (12, 6), (0, 11), (4, 11), (8, 11), (12, 11)]

def family_blocks(n):
    """Block origins (x, y) in cells for n characters (+ family extras such as the backpack). The 11 blocks
    avoid the accessory block (x12-15, y2-5)."""
    ax, ay, aw, ah = ACC_BLOCK
    for x, y in FAMILY_BLOCKS:
        assert not (x < ax + aw and ax < x + BLOCK_W and y < ay + ah and ay < y + BLOCK_H), (x, y)
        assert x + BLOCK_W <= FGRID and y + BLOCK_H <= FGRID
    if len(FAMILY_BLOCKS) < n:
        raise ValueError(f'family atlas holds {len(FAMILY_BLOCKS)} blocks, need {n}')
    return FAMILY_BLOCKS[:n]

def part_rect(block, slot):
    """UV rect (u0, v0, w, h) of a part slot inside a character block (UV origin bottom-left)."""
    bx, by = block
    sub = {'head': (0, 3, 4, 2), 'top': (0, 1, 4, 2), 'bottom': (0, 0, 2, 1), 'feet': (2, 0, 2, 1),
           'extra': (0, 0, 4, 5)}[slot]
    x, y, w, h = sub
    c = 1 / FGRID
    return ((bx + x) * c, (by + y) * c, w * c, h * c)

# per-slot triangle caps per tier for modular families: any head+top+bottom+feet combination stays below
# the tier cap with room for one accessory; family extras (backpack) are budgeted like accessories
SLOT_CAPS = {
    'hero': {'head': 4000, 'top': 3800, 'bottom': 1600, 'feet': 650, 'extra': 1500},
    'lod0': {'head': 1900, 'top': 2100, 'bottom': 900, 'feet': 450, 'extra': 600},
    'lod1': {'head': 950, 'top': 1050, 'bottom': 450, 'feet': 230, 'extra': 300},
    'lod2': {'head': 380, 'top': 420, 'bottom': 180, 'feet': 90, 'extra': 120},
}
TN = {'hero': 'Hero', 'lod0': 'LOD0', 'lod1': 'LOD1', 'lod2': 'LOD2'}
