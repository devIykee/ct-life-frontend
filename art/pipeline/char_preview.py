"""Character lineup contact sheets, rendered with Eevee from the EXPORTED FBX + PNG tiles
(so the preview shows exactly what Unity receives).

Usage: art/pipeline/blender.sh art/pipeline/char_preview.py -- <sex> <out.png> [outfit,outfit,...] [--heads]
"""
import bpy, sys, os, json, math, re
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
import ctl_render as R

args = sys.argv[sys.argv.index('--') + 1:]
SEXES = args[0].split(',')
OUTP = args[1]
SEL = args[2].split(',') if len(args) > 2 and not args[2].startswith('--') else None
HEADS = '--heads' in args
bpy.ops.wm.read_factory_settings(use_empty=True)


def log(*a):
    print('[char_preview]', *a, flush=True)


from ctl_assemble import assemble, assemble_npc, relaxed, align, import_fbx, material


def label(text, loc, size=0.075, color=(0.12, 0.12, 0.16)):
    t = R.add_text(text, loc, size=size, color=color)
    return t


# ------------------------------------------------------------------ scene
items = []
NPC = SEXES == ['npc']
if NPC:
    SEXES = []
    for n in (SEL or list(C.NPCS)):
        items.append(('npc', n, None))
for sex in SEXES:
    outfits = C.OUTFITS[sex]
    names = SEL or list(outfits)
    for n in names:
        if n in outfits:
            items.append((sex, n, outfits[n]))
spacing = 0.95 if not HEADS else 0.42
N = len(items)
for i, (sex, n, od) in enumerate(items):
    x = (i - (N - 1) / 2) * spacing
    if sex == 'npc':
        arm, tr, man = assemble_npc(n, x)
        relaxed(arm, holds_light='ring_light' in man['parts'], seed=i)
        lab, sub = n.replace('_', ' ').title(), f'NPC · 1 mesh · 1 atlas · LOD0 {tr/1000:.1f}k tris'
    else:
        arm, tr = assemble(sex, od, x)
        relaxed(arm, holds_light='ring_light' in od['parts'], seed=i)
        lab, sub = od.get('label', n), f'{sex} · LOD0 {tr/1000:.1f}k tris'
    if not HEADS:
        label(lab, (x, -0.25, 2.04), size=0.085, color=(0.0, 0.0, 0.0))
        label(sub, (x, -0.25, 1.94), size=0.055, color=(0.05, 0.05, 0.08))
    log(sex, n, tr)

R.setup_scene(res=(3200, 1150) if not HEADS else (3200, 900), samples=96,
              bg_top=(0.99, 0.93, 0.84), bg_bottom=(0.80, 0.86, 0.95), strength=0.45)
R.add_backdrop(width=max(30, N * spacing + 10), color=(0.88, 0.85, 0.80))
R.add_sun(rot_deg=(52, 0, -28), energy=1.9, color=(1.0, 0.9, 0.78), angle_deg=6)
R.add_area((-6, -10, 6), (0, 0, 1), energy=1200, size=10, color=(0.85, 0.9, 1.0))
width = N * spacing + 0.6
if HEADS:
    d = width / (36 / 85) * 1.0
    R.add_camera((0, -d, 1.62), (0, 0, 1.55), lens=85)
else:
    d = width / (36 / 85)
    R.add_camera((0, -d, 3.4), (0, 0, 1.0), lens=85)
os.makedirs(os.path.dirname(OUTP), exist_ok=True)
R.render(OUTP)
