"""World v3: process downloaded Sketchfab packs (world_v3_config.PACKS) into Seeker-ready FBX + manifests.

Usage: Blender -b --factory-startup -P world_v3_process.py -- [uid|prefix,...] [--family v3_props]
In:  ct-life/art/raw/sketchfab/<uid>/<uid>.glb + meta.json (title, creator, url, licence)
Out: art/processed/world_v3/<family>/<name>.fbx, textures/, manifest.json (merged per family)

Per kind:
  building  part-wise LODs (facade cards + per-part decimation) 15k / 4k / baked box (world_v3_lib.building_lods)
  prop      pack atlas (one 2048 set per pack), LOD0 <= 2k, LOD1 <= 500 (per-config overrides)
  rock      as prop
  tree      own materials kept (tiling bark, cut-out leaves, <=1024 px), LOD0 <= 5k, LOD1 <= 1.2k, LOD2 = crossed billboard
  interior  one room, pack atlas, <= 40k tris, no LODs
  animal    converted as is (armature + all actions baked), textures <= 1024
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(__file__))
import numpy as np
from mathutils import Vector, Matrix
import world_v3_lib as W
import world_v3_config as CFG

RAW = os.environ.get('CTL_SF_RAW', '/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab')
OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'processed', 'world_v3'))

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
SEL = args[0].split(',') if args and not args[0].startswith('--') else None
FAM = args[args.index('--family') + 1] if '--family' in args else None


def meta(uid):
    p = os.path.join(RAW, uid, 'meta.json')
    return json.load(open(p)) if os.path.exists(p) else {}


def credit(m):
    return (f'"{m.get("name", "?")}" by {m.get("display") or m.get("user", "?")} ({m.get("url", "")}), '
            f'{m.get("lic", "?")}; modified by CT Life (split, decimated, atlased)')


def lic_note(m):
    l = m.get('lic', '?')
    return l + (' [Unverified: Fab/Sketchfab Standard terms for crypto/NFT use]' if 'Standard' in l else '')


def clusters(objs, tol=0.3):
    """Union-find on world XY bounding boxes (expanded by tol) -> list of object lists, sorted by x then y."""
    boxes = []
    for o in objs:
        mn, mx = W.world_bounds([o])
        boxes.append((mn, mx))
    par = list(range(len(objs)))

    def f(i):
        while par[i] != i:
            par[i] = par[par[i]]
            i = par[i]
        return i
    order = sorted(range(len(objs)), key=lambda i: boxes[i][0].x)
    for a_i, i in enumerate(order):
        mni, mxi = boxes[i]
        for j in order[a_i + 1:]:
            mnj, mxj = boxes[j]
            if mnj.x > mxi.x + tol:
                break
            if mnj.y <= mxi.y + tol and mni.y <= mxj.y + tol:
                par[f(i)] = f(j)
    groups = {}
    for i in range(len(objs)):
        groups.setdefault(f(i), []).append(objs[i])
    out = list(groups.values())
    out.sort(key=lambda g: (round(W.world_bounds(g)[0].x, 0), round(W.world_bounds(g)[0].y, 0)))
    return out


def mesh_objs():
    return [o for o in bpy.data.objects if o.type == 'MESH' and o.data.polygons]


def rescale(cfg):
    """cfg height (m, of the whole download) or scale: uniform scale on the glTF roots."""
    ms = mesh_objs()
    if not ms or not (cfg.get('height') or cfg.get('scale')):
        return
    mn, mx = W.world_bounds(ms)
    s = cfg.get('scale') or cfg['height'] / max(1e-6, mx.z - mn.z)
    for o in bpy.data.objects:
        if o.parent is None:
            o.scale = o.scale * s
            o.location = o.location * s
    bpy.context.view_layer.update()
    W.log('rescaled by', round(s, 4))


def drop_mats(cfg):
    """Delete faces that use the listed source materials (e.g. a pack's own sidewalk slab)."""
    names = set(cfg.get('drop_mats', []))
    if not names:
        return
    import bmesh
    for o in mesh_objs():
        idx = [i for i, sl in enumerate(o.material_slots) if sl.material and sl.material.name in names]
        if not idx:
            continue
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in idx], context='FACES')
        bm.to_mesh(o.data)
        bm.free()
    for o in [o for o in mesh_objs()]:
        pass
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and not o.data.polygons:
            bpy.data.objects.remove(o)
    W.log('dropped materials', sorted(names))


def debrand(cfg):
    """Paint out real brand names in SOURCE images before atlasing: cfg debrand = [(image_name, (u0, v0, u1, v1))],
    box in normalised coordinates of the image as stored (top-down), filled with the median of a thin ring around it."""
    for name, (u0, v0, u1, v1) in cfg.get('debrand', []):
        im = bpy.data.images.get(name)
        if im is None or im.size[0] == 0:
            W.log('debrand: no image', name)
            continue
        w, h = im.size
        a = np.empty(w * h * 4, np.float32)
        im.pixels.foreach_get(a)
        a = a.reshape(h, w, 4)
        x0, x1 = int(u0 * w), int(u1 * w)
        r0, r1 = int((1 - v1) * h), int((1 - v0) * h)      # Blender rows are bottom-up
        rp = 3
        ring = np.concatenate([a[max(0, r0 - rp):r0, x0:x1].reshape(-1, 4), a[r1:r1 + rp, x0:x1].reshape(-1, 4)])
        a[r0:r1, x0:x1] = np.median(ring, axis=0)
        im.pixels.foreach_set(a.ravel())
        W.log('debranded', name, (x0, r0, x1, r1))


def process_pack(uid, cfg):
    W.reset()
    path = os.path.join(RAW, uid, uid + '.glb')
    if cfg.get('path'):
        path = os.path.join(os.path.dirname(RAW), cfg['path'])
    if not os.path.exists(path):
        W.log('missing', uid)
        return {}
    W.import_glb(path)
    rescale(cfg)
    debrand(cfg)
    drop_mats(cfg)
    m = cfg.get('meta') or meta(uid)
    fam = cfg['family']
    d = os.path.join(OUT, fam)
    td = os.path.join(d, 'textures')
    os.makedirs(td, exist_ok=True)
    kind = cfg['kind']
    base = dict(source=m.get('url', ''), source_uid=uid, licence=lic_note(m), credit=credit(m))
    man = {}
    if kind == 'animal':
        return {cfg['prefix']: process_animal(uid, cfg, d, td, base)}
    objs = mesh_objs()
    groups = [objs] if cfg['mode'] == 'whole' else clusters(objs, cfg.get('tol', 0.3))
    min_xy = cfg.get('min_xy', 0.15)
    groups = [g for g in groups if (lambda b: math.hypot(b[1].x - b[0].x, b[1].y - b[0].y))(W.world_bounds(g)) >= min_xy]
    for k in cfg.get('skip', []):
        if k < len(groups):
            groups[k] = None
    groups = [g for g in groups if g]
    W.log(cfg['prefix'], len(groups), 'assets from', len(objs), 'meshes')
    names = [cfg['prefix'] if len(groups) == 1 else f'{cfg["prefix"]}_{i + 1:02d}' for i in range(len(groups))]
    if kind == 'building':
        for name, g in zip(names, groups):
            r = W.building_lods(name, g, td, os.path.join(d, name + '.fbx'), door=cfg.get('door', False), yaw=(None if cfg.get('yaw') == 'auto' else cfg.get('yaw', 0.0)),
                                lod0=cfg.get('lod0', 15000), lod1=cfg.get('lod1', 4000), card=cfg.get('card', True))
            r.update(fbx=f'world_v3/{fam}/{name}.fbx', kind='building', tags=['building', 'kit:v3', 'pack:' + cfg['prefix']], **base)
            man[name] = r
        return man
    # props / rocks / trees / interiors: parts per asset, one atlas (or kept materials) per pack
    assets = []
    for name, g in zip(names, groups):
        parts = W.copy_parts(g, name)
        size = W.parts_to_ground(parts, cfg.get('yaw', 0.0))
        assets.append((name, parts, size))
    allparts = [p for _, ps, _ in assets for p in ps]
    if kind == 'tree':
        at = W.copy_materials(allparts, td, cfg['prefix'], max_px=1024)
    else:
        at = W.build_atlas(allparts[0], cfg['prefix'], td, extra_objs=allparts[1:])
    lod0 = cfg.get('lod0', {'prop': 2000, 'rock': 2000, 'tree': 5000, 'interior': 40000}[kind])
    lod1 = cfg.get('lod1', {'prop': 500, 'rock': 500, 'tree': 1200, 'interior': 0}[kind])
    for name, parts, size in assets:
        l1src = [W.dup(p, p.name + '_l1') for p in parts] if lod1 else []
        red0 = W.decimate_parts(parts, lod0, min_part=0.0 if kind != 'interior' else 0.03, min_tris=4,
                                planar=kind not in ('tree',))
        l0 = W.join_parts(red0, name + '_LOD0' if lod1 else name)
        if W.tris(l0) > lod0 * 1.15:
            l0b = W.lod_under(l0, lod0, l0.name + 'x', planar_first=False)
            nm = l0.name
            bpy.data.objects.remove(l0)
            l0 = l0b
            l0.name = l0.data.name = nm
        out = [l0]
        if lod1:
            red1 = W.decimate_parts(l1src, lod1, min_part=0.0, min_tris=4, planar=kind not in ('tree',))
            l1 = W.join_parts(red1, name + '_LOD1')
            if W.tris(l1) > lod1 * 1.15:
                l1b = W.lod_under(l1, lod1, name + '_LOD1x', planar_first=False)
                bpy.data.objects.remove(l1)
                l1 = l1b
                l1.name = l1.data.name = name + '_LOD1'
            out.append(l1)
        if kind == 'tree':
            out.append(W.billboard(l0, name, td))
        W.export_fbx(out, os.path.join(d, name + '.fbx'))
        tags = [kind, 'kit:v3', 'pack:' + cfg['prefix']]
        man[name] = dict(fbx=f'world_v3/{fam}/{name}.fbx', kind=kind, tris=[W.tris(o) for o in out],
                         size_m=[round(x, 3) for x in size], materials=at['materials'],
                         textures=at['textures'] + ([f'{name}_billboard.png'] if kind == 'tree' else []), tags=tags, **base)
        W.log(name, man[name]['tris'], man[name]['size_m'])
        for o in out:
            bpy.data.objects.remove(o)
    return man


def process_animal(uid, cfg, d, td, base):
    name = cfg['prefix']
    # textures <= 1024, saved next to the FBX
    for im in list(bpy.data.images):
        if im.size[0] == 0:
            continue
        sz = min(1024, im.size[0])
        arr = W._img_array(im, sz)
        p = os.path.join(td, f'{name}_{len([x for x in os.listdir(td) if x.startswith(name)]):02d}.png')
        W.save_png(arr, p, 4)
        cs = im.colorspace_settings.name
        im.filepath_raw = p
        im.colorspace_settings.name = cs
    objs = [o for o in bpy.data.objects if o.type in ('MESH', 'ARMATURE')]
    meshes = [o for o in objs if o.type == 'MESH']
    tris = sum(W.tris(o) for o in meshes)
    mn, mx = W.world_bounds(meshes)
    acts = [a.name for a in bpy.data.actions]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(d, name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'ARMATURE'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             path_mode='RELATIVE', mesh_smooth_type='FACE')
    W.log(name, 'animal', tris, 'tris', len(acts), 'actions', acts[:12])
    return dict(fbx=f'world_v3/{os.path.basename(d)}/{name}.fbx', kind='animal', tris=[tris],
                size_m=[round(mx.x - mn.x, 3), round(mx.y - mn.y, 3), round(mx.z - mn.z, 3)], actions=acts,
                tags=['animal', 'kit:v3'], **base)


def main():
    by_fam = {}
    for uid, cfg in CFG.PACKS.items():
        if SEL and uid not in SEL and cfg['prefix'] not in SEL:
            continue
        if FAM and cfg['family'] != FAM:
            continue
        if cfg.get('exclude'):
            continue
        try:
            man = process_pack(uid, cfg)
        except Exception as e:
            import traceback
            traceback.print_exc()
            W.log('FAILED', uid, cfg['prefix'], e)
            continue
        by_fam.setdefault(cfg['family'], {}).update(man)
        W.write_manifest(os.path.join(OUT, cfg['family'], 'manifest.json'), man)
    W.log('done', {k: len(v) for k, v in by_fam.items()})


main()
