"""Re-import an exported mocap FBX, verify it numerically and render a contact sheet. Blender 5.1 headless:

  Blender -b --factory-startup -noaudio --python tools/anim/fbx_check_render_blender.py -- \
      --fbx clip.fbx --ref clip_ref.json --out sheet.png --report report.json [--cells 8] [--views side,34]

--ref (written by tools/anim/build_mocap_clips.py) holds, in BVH/FBX coordinates (Y-up metres):
  names: joint names; frames: per output frame joint world positions; rest: rest-pose joint positions;
  end_len: {leaf joint: end-site length}; labels: per output frame text; title.
Checks: take count/name, frame count, every joint's world position per frame vs --ref (max error), and the
armature rest (edit-bone heads) vs the ASF rest pose. Rendering: Workbench, capsules between joint positions
(left side blue, right side red, centre grey, head = sphere + yellow nose along the head's forward axis),
0.5 m floor checker, orthographic camera tracking the pelvis.
"""
import argparse
import json
import math
import os
import sys
import tempfile

import bpy
import numpy as np
from mathutils import Matrix, Vector


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--fbx', required=True)
    ap.add_argument('--ref', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--report', required=True)
    ap.add_argument('--cells', type=int, default=8)
    ap.add_argument('--views', default='side,34')
    ap.add_argument('--cell-w', type=int, default=300)
    ap.add_argument('--cell-h', type=int, default=380)
    return ap.parse_args(argv)


def y_up_to_blender(p):
    return Vector((p[0], -p[2], p[1]))


def clean():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.actions, bpy.data.armatures, bpy.data.curves,
                 bpy.data.cameras):
        for d in list(coll):
            coll.remove(d)


def side_of(name):
    if name in ('lowerback', 'lowerneck'):
        return 'c'
    if name[0] == 'l':
        return 'l'
    if name[0] == 'r' and name != 'root':
        return 'r'
    return 'c'


COLORS = {'l': (0.18, 0.42, 0.95, 1.0), 'r': (0.92, 0.22, 0.18, 1.0), 'c': (0.75, 0.75, 0.72, 1.0),
          'nose': (1.0, 0.82, 0.1, 1.0)}


def capsule_mesh(name, segs, radius_of, spheres):
    """segs: [(a, b, key)], spheres: [(center, r, key)] -> one mesh object per colour key."""
    import bmesh
    objs = []
    by_key = {}
    for a, b, k in segs:
        by_key.setdefault(k, ([], []))[0].append((a, b))
    for c, r, k in spheres:
        by_key.setdefault(k, ([], []))[1].append((c, r))
    for k, (ss, sp) in by_key.items():
        bm = bmesh.new()
        for a, b in ss:
            d = b - a
            L = d.length
            if L < 1e-5:
                continue
            r = radius_of(L)
            q = d.normalized().to_track_quat('Z', 'Y')
            M = Matrix.Translation((a + b) * 0.5) @ q.to_matrix().to_4x4()
            bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=r, radius2=r * 0.8, depth=L, matrix=M)
            bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=r, matrix=Matrix.Translation(a))
        for c, r in sp:
            bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=r, matrix=Matrix.Translation(c))
        me = bpy.data.meshes.new(name + '_' + k)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(name + '_' + k, me)
        ob.color = COLORS[k]
        bpy.context.scene.collection.objects.link(ob)
        objs.append(ob)
    return objs


def floor(center, size=8.0, tile=0.5):
    import bmesh
    obs = []
    for parity, col in ((0, (0.86, 0.86, 0.84, 1)), (1, (0.70, 0.71, 0.72, 1))):
        bm = bmesh.new()
        n = int(size / tile)
        x0 = math.floor(center.x / tile) * tile - size / 2
        y0 = math.floor(center.y / tile) * tile - size / 2
        for i in range(n):
            for j in range(n):
                xi, yj = x0 + i * tile, y0 + j * tile
                if (int(round(xi / tile)) + int(round(yj / tile))) % 2 != parity:
                    continue
                vs = [bm.verts.new((xi, yj, 0)), bm.verts.new((xi + tile, yj, 0)),
                      bm.verts.new((xi + tile, yj + tile, 0)), bm.verts.new((xi, yj + tile, 0))]
                bm.faces.new(vs)
        me = bpy.data.meshes.new('floor%d' % parity)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new('floor%d' % parity, me)
        ob.color = col
        bpy.context.scene.collection.objects.link(ob)
        obs.append(ob)
    return obs


def main():
    a = args()
    ref = json.load(open(a.ref))
    names = ref['names']
    clean()
    sc = bpy.context.scene
    sc.render.fps, sc.render.fps_base = int(ref.get('fps', 30)), 1.0
    bpy.ops.import_scene.fbx(filepath=a.fbx, use_anim=True, anim_offset=0.0, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, force_connect_children=False,
                             axis_forward='-Z', axis_up='Y', global_scale=1.0)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    assert len(arm) == 1, [o.name for o in bpy.data.objects]
    arm = arm[0]
    report = {'fbx': os.path.basename(a.fbx), 'fbx_bytes': os.path.getsize(a.fbx),
              'objects': sorted(o.name for o in bpy.data.objects),
              'actions': [ac.name for ac in bpy.data.actions],
              'armature_object': arm.name, 'armature_scale': list(arm.scale),
              'bones': len(arm.data.bones)}
    act = arm.animation_data.action if arm.animation_data else None
    report['action_frame_range'] = list(act.frame_range) if act else None
    missing = [n for n in names if n not in arm.pose.bones]
    report['missing_bones'] = missing
    extra = [b.name for b in arm.data.bones if b.name not in names]
    report['extra_bones'] = extra

    # ---- rest pose (edit-bone heads) vs ASF rest
    mw = arm.matrix_world
    rest_err = 0.0
    for n, p in zip(names, ref['rest']):
        if n in arm.data.bones:
            rest_err = max(rest_err, ((mw @ arm.data.bones[n].head_local) - y_up_to_blender(p)).length)
    report['rest_pose_max_err_m'] = rest_err

    # ---- per-frame joint positions vs reference
    f0 = int(round(act.frame_range[0])) if act else 0
    nfr = len(ref['frames'])
    worst, worst_at = 0.0, None
    poses = []
    for k in range(nfr):
        sc.frame_set(f0 + k)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = arm.evaluated_get(dg)
        mw = ev.matrix_world
        heads = {n: mw @ ev.pose.bones[n].head for n in names}
        ymat = {n: (mw @ ev.pose.bones[n].matrix).to_3x3() for n in names}
        D = {n: (ev.pose.bones[n].matrix @ arm.data.bones[n].matrix_local.inverted()).to_3x3() for n in names}
        poses.append((heads, ymat, D))
        for n, p in zip(names, ref['frames'][k]):
            e = (heads[n] - y_up_to_blender(p)).length
            if e > worst:
                worst, worst_at = e, (k, n)
    report['frames_checked'] = nfr
    report['frame_pos_max_err_m'] = worst
    report['frame_pos_worst'] = worst_at
    sc.frame_set(f0)

    # ---- render
    parents = ref['parents']
    end_len = ref['end_len']
    ncell = min(a.cells, nfr)
    idx = [round(i * (nfr - 1) / max(1, ncell - 1)) for i in range(ncell)] if ncell > 1 else [0]
    views = a.views.split(',')
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'OBJECT'
    sh.show_shadows = True
    sh.shadow_intensity = 0.35
    sh.show_cavity = False
    sc.display.render_aa = '8'
    sc.render.resolution_x, sc.render.resolution_y = a.cell_w, a.cell_h
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.world = sc.world or bpy.data.worlds.new('w')
    sc.world.color = (0.97, 0.97, 0.96)
    arm.hide_render = True

    face = ref.get('facing_blender', [0.0, -1.0, 0.0])  # character front at the alignment frame
    fwd = Vector(face).normalized()
    left = Vector((0, 0, 1)).cross(fwd)  # Blender Z-up: rest faces -Y, left side is +X
    right = -left
    view_dirs = {'side': right, 'front': fwd, '34': (fwd + right).normalized(), 'back34': (-fwd + right).normalized()}
    cam_data = bpy.data.cameras.new('cam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = ref.get('ortho_scale', 2.5)
    cam = bpy.data.objects.new('cam', cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    txt_curve = bpy.data.curves.new('label', 'FONT')
    txt_curve.size = cam_data.ortho_scale * 0.045
    txt = bpy.data.objects.new('label', txt_curve)
    txt.color = (0.1, 0.1, 0.1, 1)
    sc.collection.objects.link(txt)
    txt.parent = cam
    S = cam_data.ortho_scale  # ortho_scale spans the larger render dimension (sensor_fit AUTO)
    big = max(a.cell_w, a.cell_h)
    half_w, half_h = S / 2 * a.cell_w / big, S / 2 * a.cell_h / big
    txt.location = (-half_w * 0.94, half_h * 0.86, -1.0)

    tmpdir = tempfile.mkdtemp()
    cells = {}
    for vi, vname in enumerate(views):
        vd = view_dirs[vname]
        for ci, k in enumerate(idx):
            heads, ymat, D = poses[k]
            segs = []
            for n in names:
                p = parents.get(n)  # segment parent joint -> joint = the parent's bone
                if p is not None:
                    segs.append((heads[p], heads[n], side_of(p)))
            for n, L in end_len.items():
                if n == 'head':
                    continue
                ydir = ymat[n].col[1].normalized()
                segs.append((heads[n], heads[n] + ydir * L, side_of(n)))
            hd = ymat['head'].col[1].normalized()
            hc = heads['head'] + hd * end_len.get('head', 0.18) * 0.55
            nose_dir = (D['head'] @ Vector((0.0, -1.0, 0.0))).normalized()
            segs.append((hc, hc + nose_dir * 0.16, 'nose'))
            spheres = [(hc, 0.1, 'c')]
            objs = capsule_mesh('fig', segs, lambda L: 0.028, spheres)
            pel = heads['root']
            fl = floor(pel)
            target = Vector((pel.x, pel.y, ref.get('cam_z', 0.9)))
            elev = math.radians(18.0 if vname != 'side' else 10.0)
            dvec = Vector((vd.x * math.cos(elev), vd.y * math.cos(elev), math.sin(elev)))
            cam.location = target + dvec * 10.0
            cam.rotation_euler = (-dvec).to_track_quat('-Z', 'Y').to_euler()
            txt_curve.body = '%s  %s' % (ref['labels'][k], {'34': '3/4 front-right', 'side': 'side (from right)', 'front': 'front'}.get(vname, vname))
            sc.render.filepath = os.path.join(tmpdir, 'c_%d_%d.png' % (vi, ci))
            bpy.ops.render.render(write_still=True)
            cells[(vi, ci)] = sc.render.filepath
            for o in objs + fl:
                me = o.data
                bpy.data.objects.remove(o)
                bpy.data.meshes.remove(me)

    # ---- compose sheet
    title_h = 34
    W, H = a.cell_w * len(idx), a.cell_h * len(views)
    sheet = np.ones((H + title_h, W, 4), dtype=np.float32)
    for (vi, ci), path in cells.items():
        img = bpy.data.images.load(path)
        px = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(img.size[1], img.size[0], 4)  # bottom-up rows
        y0 = (len(views) - 1 - vi) * a.cell_h
        sheet[y0:y0 + a.cell_h, ci * a.cell_w:(ci + 1) * a.cell_w] = px
        # cell border
        sheet[y0:y0 + a.cell_h, ci * a.cell_w, :3] = 0.55
        bpy.data.images.remove(img)
    # title band via a rendered text strip
    sc.render.resolution_x, sc.render.resolution_y = W, title_h
    for o in [o for o in bpy.data.objects if o.name != 'cam' and o.name != 'label' and o.type != 'ARMATURE']:
        bpy.data.objects.remove(o)
    cam.location = (0, 0, 50)
    cam.rotation_euler = (0, 0, 0)
    cam_data.ortho_scale = 10.0
    txt.parent = None
    txt_curve.size = 10.0 * title_h / W * 0.62
    txt_curve.body = ref.get('title', '')
    txt.location = (-4.95, -txt_curve.size * 0.35, 40.0)
    sc.render.filepath = os.path.join(tmpdir, 'title.png')
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(sc.render.filepath)
    px = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    sheet[H:H + title_h, :] = px.reshape(img.size[1], img.size[0], 4)
    out = bpy.data.images.new('sheet', W, H + title_h, alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    out.filepath_raw = a.out
    out.file_format = 'PNG'
    out.save()
    report['sheet'] = a.out
    report['sheet_frames'] = [ref['labels'][k] for k in idx]
    with open(a.report, 'w') as f:
        json.dump(report, f, indent=1)
    print('CHECK_REPORT ' + json.dumps(report))


main()
