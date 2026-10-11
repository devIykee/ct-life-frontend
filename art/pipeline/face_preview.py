"""Head-and-shoulders portraits (Eevee) of assembled characters / baked NPCs, one tile each.

Usage: art/pipeline/blender.sh art/pipeline/face_preview.py -- <out_dir> <spec,spec,...>
spec = <sex>:<outfit>  or  npc:<name>      -> writes <out_dir>/<spec with : -> _>.png
"""
import bpy, sys, os, math
from mathutils import Vector
sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
import ctl_render as R
import ctl_assemble
from ctl_assemble import assemble, assemble_npc, relaxed

args = sys.argv[sys.argv.index('--') + 1:]
OUTD, SPECS = args[0], args[1].split(',')
os.makedirs(OUTD, exist_ok=True)
for spec in SPECS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ctl_assemble.MATS.clear()
    keys = {}
    if '@' in spec:
        spec_main, kv = spec.split('@', 1)
        for pair in kv.split('+'):
            k, _, v = pair.partition('=')
            keys[k] = float(v or 1)
    else:
        spec_main = spec
    kind, name = spec_main.split(':')
    if kind == 'npc':
        arm, _, man = assemble_npc(name, 0)
    else:
        arm, _ = assemble(kind, C.OUTFITS[kind][name], 0)
    relaxed(arm, seed=0)
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.data.shape_keys:
            for kb in o.data.shape_keys.key_blocks[1:]:
                kb.value = keys.get(kb.name, 0.0)
    pb = arm.pose.bones['Head']
    pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    head = arm.matrix_world @ pb.head
    tail = arm.matrix_world @ pb.tail
    ctr = head + (tail - head) * 0.35 + Vector((0, 0, -0.03))
    R.setup_scene(res=(520, 620), samples=64, bg_top=(0.99, 0.93, 0.85), bg_bottom=(0.84, 0.88, 0.95), strength=0.5)
    R.add_backdrop(width=20, color=(0.9, 0.87, 0.82))
    R.add_sun(rot_deg=(55, 0, -30), energy=1.8, color=(1.0, 0.9, 0.8), angle_deg=8)
    R.add_area(tuple(ctr + Vector((-1.2, -1.8, 0.8))), tuple(ctr), energy=120, size=1.5, color=(1.0, 0.97, 0.94))
    R.add_area(tuple(ctr + Vector((1.5, -0.8, 0.3))), tuple(ctr), energy=40, size=1.0, color=(0.85, 0.9, 1.0))
    R.add_camera(tuple(ctr + Vector((0.2, -0.92, 0.05))), tuple(ctr + Vector((0, 0, -0.05))), lens=85)
    R.render(os.path.join(OUTD, spec.replace(':', '_').replace('@', '__').replace('=', '').replace('+', '_') + '.png'))
