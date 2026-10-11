"""Kit contact sheet: every LOD0 FBX in the given processed folders rendered (Eevee) in a 3/4 view,
auto-framed, labelled with id / size / triangle count.

Usage: art/pipeline/blender.sh art/pipeline/env_preview.py -- <out.png> <dir[,dir...]> [cols=6] [title]
dirs are relative to art/processed (e.g. environment/terminal_cafe,props).
"""
import bpy, sys, os, json, math, re, tempfile, shutil
from mathutils import Vector
sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
import ctl_render as R
from montage import montage

args = sys.argv[sys.argv.index('--') + 1:]
OUTP, DIRS = args[0], args[1].split(',')
COLS = int(args[2]) if len(args) > 2 else 6
TITLE = args[3] if len(args) > 3 else None
CELL = 480
man = json.load(open(os.path.join(C.PROCESSED, 'environment', 'manifest.json')))
tmp = tempfile.mkdtemp()
tiles = []
files = []
for d in DIRS:
    for f in sorted(os.listdir(os.path.join(C.PROCESSED, d))):
        if f.endswith('.fbx'):
            files.append(os.path.join(C.PROCESSED, d, f))

for path in files:
    iid = os.path.splitext(os.path.basename(path))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = []
    for o in list(bpy.data.objects):
        if o.type != 'MESH':
            continue
        nm = re.sub(r'\.\d+$', '', o.name)
        if re.search(r'_LOD[12]$', nm):
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        meshes.append(o)
        for m in o.data.materials:
            if not m or not m.use_nodes:
                continue
            for n in m.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image and 'palette' in n.image.name.lower():
                    n.interpolation = 'Closest'
            b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if b and m.name.startswith('KEEP_'):
                # show content surfaces as a neutral 'screen'/'ad' placeholder
                b.inputs['Base Color'].default_value = (0.08, 0.12, 0.2, 1) if 'screen' in m.name else (0.95, 0.95, 0.95, 1)
                if 'neon' in m.name or 'glow' in m.name or 'softbox' in m.name or 'on_air' in m.name or 'ore_glow' in m.name:
                    b.inputs['Emission Strength'].default_value = 2.0
            if b:
                # alpha-clipped foliage
                tn = next((n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
                if tn and tn.image and tn.image.depth == 32 and 'leaves' in tn.image.name.lower() or (tn and 'leaves' in (tn.image.name.lower() if tn.image else '')):
                    m.node_tree.links.new(tn.outputs['Alpha'], b.inputs['Alpha'])
                    try:
                        m.surface_render_method = 'DITHERED'
                    except Exception:
                        pass
    if not meshes:
        continue
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    mn = Vector([min(p[i] for p in pts) for i in range(3)])
    mx = Vector([max(p[i] for p in pts) for i in range(3)])
    ctr = (mn + mx) / 2
    size = (mx - mn).length
    R.setup_scene(res=(CELL, CELL), samples=24, bg_top=(0.99, 0.95, 0.89), bg_bottom=(0.85, 0.89, 0.95), strength=0.5)
    g = R.add_ground(size=max(40, size * 6), color=(0.9, 0.88, 0.84), z=mn.z - 0.001)
    R.add_sun(rot_deg=(50, 0, -35), energy=2.2, color=(1.0, 0.93, 0.82), angle_deg=10)
    R.add_area(tuple(ctr + Vector((-size, -size, size))), tuple(ctr), energy=60 * size * size + 20, size=size + 1)
    d = Vector((1.0, -1.35, 0.95)).normalized()
    dist = size * 1.25 + 0.3
    R.add_camera(tuple(ctr + d * dist), tuple(ctr), lens=45)
    p = os.path.join(tmp, iid + '.png')
    R.render(p)
    info = man.get(iid, {})
    sz = info.get('size_m')
    lab = f"{iid}  {info.get('tris', ['?'])[0]} tris" + (f"  {sz[0]:.1f}x{sz[1]:.1f}x{sz[2]:.1f}m" if sz else '')
    tiles.append((p, lab))

montage(OUTP, COLS, CELL, tiles, title=TITLE, font_size=14)
shutil.rmtree(tmp, ignore_errors=True)
