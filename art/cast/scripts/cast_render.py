"""Contact sheets for the cast (actual exported meshes + atlases, Eevee, relaxed arms-down pose).

usage:
  Blender -b ... -P cast_render.py -- tiers <fam> <lookId>            # original vs hero/lod0/lod1/lod2
  Blender -b ... -P cast_render.py -- lineup <sheetName> <lookId> ...  # full body + head close-ups
"""
import bpy, sys, json, math, os
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cast_common import *
import cast_tex as T

args = sys.argv[sys.argv.index('--') + 1:]
mode = args[0]
TMP = EVID / 'work' / 'render_tmp'
TMP.mkdir(parents=True, exist_ok=True)
BG = (0.16, 0.19, 0.25)

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            continue
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.render.film_transparent = False
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    bgn = w.node_tree.nodes['Background']; bgn.inputs[0].default_value = (*BG, 1); bgn.inputs[1].default_value = 0.9
    def area(name, loc, energy, size, color=(1, 1, 1)):
        l = bpy.data.lights.new(name, 'AREA'); l.energy = energy; l.size = size; l.color = color
        o = bpy.data.objects.new(name, l); sc.collection.objects.link(o); o.location = loc
        d = Vector((0, 0, 1.2)) - Vector(loc)
        o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    area('key', (-2.2, -3.5, 3.2), 900, 3.0, (1, 0.96, 0.9))
    area('fill', (2.8, -3.0, 1.8), 380, 3.0, (0.9, 0.95, 1))
    area('rim', (0.5, 3.5, 3.0), 500, 2.0)
    cam = bpy.data.cameras.new('cam'); cam.type = 'ORTHO'
    co = bpy.data.objects.new('cam', cam); sc.collection.objects.link(co); sc.camera = co
    return sc, co

def pose_relaxed(rig):
    """Arms down ~75 degrees below horizontal, slight elbow bend."""
    rig.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    for side, s in (('L', 1), ('R', -1)):
        for bname, tgt in ((f'UpperArm.{side}', Vector((s * 0.24, 0.03, -0.97))),):
            pb = rig.pose.bones[bname]
            head = pb.head.copy()
            cur = (pb.tail - pb.head).normalized()
            q = cur.rotation_difference(tgt.normalized())
            pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
            bpy.context.view_layer.update()
        pb = rig.pose.bones[f'Forearm.{side}']
        head = pb.head.copy()
        q = Matrix.Rotation(math.radians(-12), 4, Vector((1, 0, 0)))
        pb.matrix = Matrix.Translation(head) @ q @ Matrix.Translation(-head) @ pb.matrix
        bpy.context.view_layer.update()

def append_look(blend, names):
    with bpy.data.libraries.load(str(blend), link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in names or n.endswith('_Rig')]
    objs = [o for o in dst.objects if o]
    for o in objs:
        bpy.context.scene.collection.objects.link(o)
    return objs

def render_obj(sc, cam, mesh, rig, kind, path, res=(360, 720)):
    for o in sc.objects:
        if o.type == 'MESH':
            o.hide_render = o is not mesh
    sc.render.resolution_x, sc.render.resolution_y = res
    bb = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    co = np.array([mesh.matrix_world @ v.co for v in ev.data.vertices])
    zmin, zmax = co[:, 2].min(), co[:, 2].max()
    if kind == 'body':
        h = zmax - zmin
        cam.data.ortho_scale = h * 1.08
        cam.location = (float((co[:, 0].min() + co[:, 0].max()) / 2) if (co[:, 0].max() - co[:, 0].min()) < 1.2 else float(np.median(co[:, 0])), -6, zmin + h / 2)
    else:
        hb = rig.pose.bones['Head']
        hp = rig.matrix_world @ hb.head
        cam.data.ortho_scale = 0.40
        cam.location = (hp.x, -6, hp.z + 0.08)
    cam.rotation_euler = (math.radians(90), 0, 0)
    if kind == 'head3q':
        hb = rig.pose.bones['Head']; hp = rig.matrix_world @ hb.head
        r = 6
        cam.location = (hp.x - r * math.sin(math.radians(30)), -r * math.cos(math.radians(30)), hp.z + 0.08)
        cam.rotation_euler = (math.radians(90), 0, math.radians(-30))
    sc.render.filepath = str(path)
    sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    return read_image(path, 3)

def label(img, text, y0, h, color=(0.92, 0.94, 0.97), scale_h=None):
    H, W, _ = img.shape
    th = scale_h or max(10, int(h * 0.55))
    tw = int(th * 6 / 7 * len(text) / 1.0 * 0.86)
    tw = min(tw, W - 8)
    m = T.text_mask(text, th, max(tw, 6))
    x0 = max(4, (W - m.shape[1]) // 2)
    r0 = y0 + (h - th) // 2
    # numpy rows: 0 = bottom; text_mask row 0 = top
    for i in range(th):
        row = r0 + (th - 1 - i)
        if 0 <= row < H:
            sel = m[i] > 0.5
            img[row, x0:x0 + m.shape[1]][sel] = color

def save_jpg(arr, path):
    h, w, _ = arr.shape
    im = bpy.data.images.new('sheet', width=w, height=h, alpha=False, float_buffer=False)
    im.colorspace_settings.name = 'Non-Color'
    px = np.ones((h, w, 4), dtype=np.float32); px[:, :, :3] = np.clip(arr, 0, 1)
    im.pixels.foreach_set(px.ravel())
    sc = bpy.context.scene
    sc.render.image_settings.file_format = 'JPEG'
    sc.render.image_settings.quality = 90
    im.filepath_raw = str(path); im.file_format = 'JPEG'
    im.save()
    bpy.data.images.remove(im)

CW, BH, HH, LH = 300, 600, 300, 52

def grid_sheet(cells, title, path, ncols, footer=''):
    """cells: list of (label_lines, body_img, head_img); rows of ncols cells."""
    nrows = (len(cells) + ncols - 1) // ncols
    rowh = BH + HH + LH
    W = CW * ncols
    H = 70 + rowh * nrows + 30
    img = np.zeros((H, W, 3), dtype=np.float32); img[:] = (0.09, 0.11, 0.15)
    for i, (lines, body, hd) in enumerate(cells):
        r, c = divmod(i, ncols)
        x = c * CW
        top = H - 70 - r * rowh
        img[top - BH:top, x:x + CW] = body
        img[top - BH - HH:top - BH, x:x + CW] = hd
        y = top - BH - HH
        for j, ln in enumerate(lines[:2]):
            label(img[:, x:x + CW], ln, y - (j + 1) * (LH // 2), LH // 2, scale_h=14 if j == 0 else 11,
                  color=(0.95, 0.96, 0.98) if j == 0 else (0.7, 0.76, 0.84))
    label(img, title, H - 64, 60, scale_h=26)
    if footer:
        label(img, footer, 4, 24, scale_h=11, color=(0.6, 0.64, 0.7))
    save_jpg(img, path)
    log('SHEET', path, len(cells), 'cells')

FOOT = 'SNOW/RAIN RIG (CC) BLENDER FOUNDATION STUDIO.BLENDER.ORG CC-BY 4.0. CT LIFE ADAPTATION. BLENDER EEVEE RENDER OF EXPORTED MESHES'

def load_manifest():
    man = {}
    for fam in FAMILIES:
        p = fam_dir(fam) / f'manifest_{fam}.json'
        if p.exists():
            man.update(json.loads(p.read_text()))
    return man

def clear_meshes(sc):
    for o in list(sc.objects):
        if o.type in ('MESH', 'ARMATURE'):
            bpy.data.objects.remove(o, do_unlink=True)

def tier_cells(sc, cam, fam, lid):
    cfg = FAMILIES[fam]
    cells = []
    with bpy.data.libraries.load(str(cfg['blend']), link=False) as (src, dst):
        dst.objects = [cfg['mesh'], cfg['rig']]
    orig, orig_rig = dst.objects
    for o in (orig, orig_rig):
        sc.collection.objects.link(o)
    for im in bpy.data.images:
        for k, p in cfg['tex'].items():
            if Path(im.filepath).name == Path(p).name:
                im.filepath = str(p)
    pose_relaxed(orig_rig)
    orig.modifiers['Game skin'].object = orig_rig
    names = [f'{lid}_{t}' for t in ('Hero', 'LOD0', 'LOD1', 'LOD2')]
    with bpy.data.libraries.load(str(fam_dir(fam) / 'looks.blend'), link=False) as (src, dst):
        dst.objects = [n for n in names if n in src.objects] + [cfg['rig']]
    meshes = [o for o in dst.objects if o and o.type == 'MESH']
    base_rig = next(o for o in dst.objects if o and o.type == 'ARMATURE')
    for o in dst.objects:
        sc.collection.objects.link(o)
    pose_relaxed(base_rig)
    entries = [('ORIGINAL', orig, orig_rig)]
    for m in meshes:
        m.parent = base_rig
        m.modifiers['Game skin'].object = base_rig
        entries.append((m.name.split('_')[-1].upper(), m, base_rig))
    bpy.context.view_layer.update()
    for name, m, r in entries:
        b = render_obj(sc, cam, m, r, 'body', TMP / f'{lid}_{name}_b.png', (CW, BH))
        hd = render_obj(sc, cam, m, r, 'head', TMP / f'{lid}_{name}_h.png', (CW, HH))
        cells.append(([f'{lid.upper()} {name}', f'{tri_count(m):,} TRIS'], b, hd))
    clear_meshes(sc)
    return cells

if mode == 'tiers':
    # args: tiers fam:lookId [fam:lookId ...] -> one LOD comparison sheet (one row per look)
    sc, cam = reset_scene()
    cells = []
    for spec in args[1:]:
        fam, lid = spec.split(':')
        cells += tier_cells(sc, cam, fam, lid)
    grid_sheet(cells, 'LOD COMPARISON - ORIGINAL CREATOR MESH VS HERO / LOD0 / LOD1 / LOD2', PREVIEWS / 'cast_lod_comparison.jpg', 5, FOOT)

elif mode == 'looks':
    # args: looks <out.jpg> <title> fam:lookId:Tier ... -> those looks at that tier (body + 3/4 head), one sheet
    out, title = Path(args[1]), args[2]
    sc, cam = reset_scene()
    man = load_manifest()
    cells = []
    for spec in args[3:]:
        fam, lid, tier = spec.split(':')
        e = man[lid]
        with bpy.data.libraries.load(str(fam_dir(fam) / 'looks.blend'), link=False) as (src, dst):
            dst.objects = [f'{lid}_{tier}', FAMILIES[fam]['rig']]
        m, r = dst.objects
        sc.collection.objects.link(m); sc.collection.objects.link(r)
        m.parent = r
        m.modifiers['Game skin'].object = r
        sv = e.get('scale', 1.0)
        r.scale = (sv, sv, sv)
        pose_relaxed(r)
        bpy.context.view_layer.update()
        b = render_obj(sc, cam, m, r, 'body', TMP / f'{lid}_{tier}_b.png', (CW, BH))
        hd = render_obj(sc, cam, m, r, 'head3q', TMP / f'{lid}_{tier}_h.png', (CW, HH))
        cells.append(([lid.upper()[:28], f'{tier} {tri_count(m):,} TRIS'], b, hd))
        clear_meshes(sc)
    grid_sheet(cells, title, out, 4, FOOT)

elif mode == 'family':
    # args: family <fam> -> every look of that family on one sheet (full body + 3/4 head)
    fam = args[1]
    sc, cam = reset_scene()
    man = load_manifest()
    order = {'identity': 0, 'npc': 1, 'player': 2}
    ids = sorted([k for k, e in man.items() if e['family'] == fam], key=lambda k: (order.get(man[k].get('kind'), 3), k))
    cells = []
    for lid in ids:
        e = man[lid]
        tier = 'Hero' if 'hero' in e['tiers'] else 'LOD0'
        with bpy.data.libraries.load(str(fam_dir(fam) / 'looks.blend'), link=False) as (src, dst):
            dst.objects = [f'{lid}_{tier}', FAMILIES[fam]['rig']]
        m, r = dst.objects
        sc.collection.objects.link(m); sc.collection.objects.link(r)
        m.parent = r
        m.modifiers['Game skin'].object = r
        sv = e.get('scale', 1.0)
        r.scale = (sv, sv, sv)
        pose_relaxed(r)
        bpy.context.view_layer.update()
        b = render_obj(sc, cam, m, r, 'body', TMP / f'{lid}_b.png', (CW, BH))
        hd = render_obj(sc, cam, m, r, 'head3q', TMP / f'{lid}_h.png', (CW, HH))
        t0 = e['tiers']['lod0']['triangles']
        arch = e.get('archetype') or e.get('tribe') or e.get('kind')
        cells.append(([(e.get('label') or lid).upper()[:26], f'{(arch or "").upper()[:16]} {tier} LOD0 {t0:,}'], b, hd))
        clear_meshes(sc)
    title = {'m': 'CT CAST - SNOW-DERIVED FAMILY (M)', 'f': 'CT CAST - RAIN-DERIVED FAMILY (F)'}[fam]
    grid_sheet(cells, title, PREVIEWS / f'cast_lineup_{fam}.jpg', 8, FOOT)
