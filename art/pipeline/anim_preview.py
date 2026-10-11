"""Filmstrip contact sheet of exported animation clips played on an assembled CT Life character.

Usage: art/pipeline/blender.sh art/pipeline/anim_preview.py -- <out.png> <clip,clip,...> [frames_per_clip=5] [sex=male] [outfit]
Loads art/processed/animations/<clip>.fbx (exactly what Unity gets) and renders N evenly spaced frames.
"""
import bpy, sys, os, json, math, re, tempfile, shutil
sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
import ctl_render as R
from ctl_assemble import assemble, import_fbx

args = sys.argv[sys.argv.index('--') + 1:]
OUTP, CLIPS = args[0], args[1].split(',')
NF = int(args[2]) if len(args) > 2 else 5
SEX = args[3] if len(args) > 3 else 'male'
OUTFIT = args[4] if len(args) > 4 else 'anon_hoodie'
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = 30
arm, _ = assemble(SEX, C.OUTFITS[SEX][OUTFIT], 0)
R.setup_scene(res=(420, 560), samples=32, bg_top=(0.99, 0.94, 0.86), bg_bottom=(0.82, 0.87, 0.95), strength=0.45)
R.add_backdrop(width=30, color=(0.88, 0.85, 0.8))
R.add_sun(rot_deg=(52, 0, -28), energy=1.9, color=(1.0, 0.9, 0.78))
R.add_area((-4, -6, 5), (0, 0, 1), energy=500, size=6, color=(0.85, 0.9, 1.0))
R.add_camera((1.5, -3.6, 1.45), (0, 0, 0.92), lens=40)
tmp = tempfile.mkdtemp()
tiles = []
for clip in CLIPS:
    objs = import_fbx(os.path.join(C.PROCESSED, 'animations', clip + '.fbx'))
    src = next(o for o in objs if o.type == 'ARMATURE')
    act = src.animation_data.action
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    arm.animation_data_create()
    arm.animation_data.action = act
    if act.slots:
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = act.frame_range
    for i in range(NF):
        f = f0 + (f1 - f0) * i / max(1, NF - 1)
        sc.frame_set(int(round(f)))
        p = os.path.join(tmp, f'{clip}_{i}.png')
        R.render(p)
        tiles.append((p, f'{clip} f{int(round(f))}'))
from montage import montage
montage(OUTP, NF, 420, tiles, title=None, font_size=18)
shutil.rmtree(tmp, ignore_errors=True)
