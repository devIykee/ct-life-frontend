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
    return normalise_meta(json.load(open(p))) if os.path.exists(p) else {}


def normalise_meta(m):
    """Two meta.json shapes exist: the fetcher's {name, display, url, lic} and the raw Sketchfab API model (user and license
    as Python-repr dicts, viewerUrl). Return the first shape."""
    import ast
    if 'display' in m or 'lic' in m:
        return m
    def d(v):
        if isinstance(v, str) and v.startswith('{'):
            try:
                return ast.literal_eval(v)
            except (ValueError, SyntaxError):
                return {}
        return v or {}
    u, l = d(m.get('user')), d(m.get('license'))
    return dict(m, display=u.get('displayName') or u.get('username'), user=u.get('username'), url=m.get('viewerUrl', ''),
                lic=l.get('label') or l.get('fullName') or '?')


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


def retex(cfg):
    """Replace whole SOURCE images before atlasing: cfg retex = [(image_name, png path relative to art/)], same size and
    orientation as stored (our own signage over a model's real-world ads and fleet names)."""
    art = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
    for name, rel in cfg.get('retex', []):
        im = bpy.data.images.get(name)
        if im is None or im.size[0] == 0:
            W.log('retex: no image', name)
            continue
        new = bpy.data.images.load(os.path.join(art, rel))
        if tuple(new.size) != tuple(im.size):
            new.scale(im.size[0], im.size[1])
        im.pixels.foreach_set(np.array(new.pixels[:], np.float32))
        bpy.data.images.remove(new)
        W.log('retextured', name, rel)


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
    retex(cfg)
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
    if kind == 'tree' and cfg.get('pack_lods'):
        return process_tree_pack_lods(cfg, d, td, base)
    if cfg['mode'] == 'named':
        # one asset per entry of cfg['groups'] (name -> top-level glTF node names), for packs whose props sit too close
        # together for the bounding-box clusters (a hydrant inside a ring of bollards, a dumpster beside its posts)
        named = []
        for nm, roots in cfg['groups'].items():
            g = [o for o in objs if any(a.split('.')[0] in roots for a in ancestors(o))]
            if g:
                named.append((nm, g))
            else:
                W.log('named group empty', nm, roots)
        names = [nm for nm, _ in named]
        groups = [g for _, g in named]
    else:
        groups = [objs] if cfg['mode'] == 'whole' else clusters(objs, cfg.get('tol', 0.3))
        min_xy = cfg.get('min_xy', 0.15)
        groups = [g for g in groups if (lambda b: math.hypot(b[1].x - b[0].x, b[1].y - b[0].y))(W.world_bounds(g)) >= min_xy]
        # names follow the cluster order before 'skip', so dropping one keeps every other asset's id
        names = [cfg['prefix'] if len(groups) == 1 else f'{cfg["prefix"]}_{i + 1:02d}' for i in range(len(groups))]
        keep = [i for i in range(len(groups)) if i not in set(cfg.get('skip', []))]
        groups, names = [groups[i] for i in keep], [names[i] for i in keep]
    W.log(cfg['prefix'], len(groups), 'assets from', len(objs), 'meshes')
    if kind == 'building':
        for name, g in zip(names, groups):
            r = W.building_lods(name, g, td, os.path.join(d, name + '.fbx'), door=cfg.get('door', False), yaw=(None if cfg.get('yaw') == 'auto' else cfg.get('yaw', 0.0)),
                                lod0=cfg.get('lod0', 15000), lod1=cfg.get('lod1', 4000), card=cfg.get('card', True))
            if cfg.get('nonmetal'):
                zero_metal(os.path.join(td, f'{name}_MaskMap.png'))
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


def zero_metal(path):
    """Mask map R (metallic) to 0: packs whose glTF materials keep the default metallicFactor 1 on brick and concrete
    render near-black under the PBR kit shader (no metal reflections to show)."""
    if not os.path.exists(path):
        return
    im = bpy.data.images.load(path)
    a = np.empty(im.size[0] * im.size[1] * 4, np.float32)
    im.pixels.foreach_get(a)
    a = a.reshape(-1, 4)
    a[:, 0] = 0.0
    im.pixels.foreach_set(a.ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)
    W.log('metallic zeroed', os.path.basename(path))


def ancestors(o):
    out = []
    while o.parent is not None:
        o = o.parent
        out.append(o.name)
    return out


def _base_color_image(mat):
    """The image feeding a material's Principled base colour (glTF import: Image Texture -> Base Color)."""
    for n in mat.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            for l in mat.node_tree.links:
                if l.to_node == n and l.to_socket.name == 'Base Color':
                    fn = l.from_node
                    for _ in range(4):
                        if fn.type == 'TEX_IMAGE':
                            return fn.image
                        ins = [x for x in fn.inputs if x.is_linked]
                        if not ins:
                            break
                        fn = ins[0].links[0].from_node
    return None


def process_tree_pack_lods(cfg, d, td, base):
    """Tree packs that ship their own LODs (LOLIPOP maples: <tree>/<tree>_LOD0.._LOD2 with bark + leaf-cluster meshes,
    <tree>_Billboard_LOD3; each level 20 m along -X): one asset per picked tree, scaled to its target height. Our LOD0 is
    the most detailed pack level within lod0 tris, LOD1 the next level (decimated under lod1 if needed), LOD2 the
    crossed billboard. Materials: the pack's shared bark (tiling) and leaf-cluster (cut-out) textures as
    <prefix>_bark / <prefix>_leaves, base colour only (the kit tree shader)."""
    objs = mesh_objs()
    by_tree = {}
    for o in objs:
        lod = o.parent.name if o.parent else ''
        tree = o.parent.parent.name if o.parent and o.parent.parent else ''
        by_tree.setdefault(tree, {}).setdefault(lod, []).append(o)
    # shared materials -> two named materials with saved base colour maps
    renamed = {}
    for o in objs:
        for sl in o.material_slots:
            m = sl.material
            if not m or m.name in renamed or 'Billboard' in m.name or m.name == 'Checker':
                continue
            role = 'bark' if 'Bark' in m.name else 'leaves' if 'Cluster' in m.name else None
            if role is None:
                continue
            im = _base_color_image(m)
            fn = f'{cfg["prefix"]}_{role}.png'
            if im is not None:
                arr = W._img_array(im, min(1024, im.size[0]))
                W.save_png(arr, os.path.join(td, fn), 4)
                W.log('tree texture', m.name, '->', fn, im.size[:], 'alpha min', round(float(arr[..., 3].min()), 3))
            renamed[m.name] = f'{cfg["prefix"]}_{role}'
            m.name = renamed[m.name]
    man = {}
    lod0_t, lod1_t = cfg.get('lod0', 5500), cfg.get('lod1', 2200)
    for tree, target_h in cfg['pick'].items():
        levels = by_tree.get(tree)
        if not levels:
            W.log('tree missing', tree)
            continue
        keys = sorted(k for k in levels if '_LOD' in k and 'Billboard' not in k)      # <tree>_LOD0, _LOD1, _LOD2
        ttri = [sum(W.tris(o) for o in levels[k]) for k in keys]
        i0 = next((i for i, t in enumerate(ttri) if t <= lod0_t), len(keys) - 1)
        i1 = min(i0 + 1, len(keys) - 1)
        name = f'{cfg["prefix"]}_{tree.split("_", 1)[1].lower()}'                      # Acer_medium_1 -> lp_maple_medium_1
        out = []
        ref = None
        for li, k in ((0, keys[i0]), (1, keys[i1])):
            parts = W.copy_parts(levels[k], f'{name}_l{li}')
            j = W.join_parts(parts, f'{name}_LOD{li}')
            vs = np.array([v.co[:] for v in j.data.vertices])
            if ref is None:
                # the trunk's foot: the lowest 0.4 m of the level, its XY centre
                low = vs[vs[:, 2] < vs[:, 2].min() + 0.4]
                ref = (low[:, 0].mean(), low[:, 1].mean(), vs[:, 2].min(), vs[:, 2].max() - vs[:, 2].min())
                s = (target_h / ref[3]) if target_h else 1.0
            else:
                low = vs[vs[:, 2] < vs[:, 2].min() + 0.4]
            cx, cy = low[:, 0].mean(), low[:, 1].mean()
            j.data.transform(Matrix.Translation(Vector((-cx, -cy, -vs[:, 2].min()))))
            j.data.transform(Matrix.Scale(s, 4))
            j.data.update()
            if li == 1 and W.tris(j) > lod1_t * 1.15:
                jj = W.lod_under(j, lod1_t, j.name + 'x', planar_first=False)
                bpy.data.objects.remove(j)
                j = jj
                j.name = j.data.name = f'{name}_LOD1'
            out.append(j)
        out.append(W.billboard(out[0], name, td))
        vs = np.array([v.co[:] for v in out[0].data.vertices])
        size = (vs.max(0) - vs.min(0)).tolist()
        W.export_fbx(out, os.path.join(d, name + '.fbx'))
        mats = [f'{cfg["prefix"]}_bark', f'{cfg["prefix"]}_leaves']
        man[name] = dict(fbx=f'world_v3/{os.path.basename(d)}/{name}.fbx', kind='tree', tris=[W.tris(o) for o in out],
                         size_m=[round(x, 3) for x in size], materials=mats,
                         textures=[f'{cfg["prefix"]}_bark.png', f'{cfg["prefix"]}_leaves.png', f'{name}_billboard.png'],
                         tags=['tree', 'kit:v3', 'pack:' + cfg['prefix']], pack_levels=[keys[i0], keys[i1]], **base)
        W.log(name, man[name]['tris'], man[name]['size_m'], 'scale', round(s, 3), 'from', keys[i0], keys[i1])
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
