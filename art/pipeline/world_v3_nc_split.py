"""World v3: split Sketchfab "Neighbourhood City Modular lowpoly" (golukumar, Free Standard) into kit assets.

Usage: Blender -b --factory-startup -P world_v3_nc_split.py -- [--only buildings|props|trees] [--doors nc_building_01,...]
In:  ct-life/art/raw/sketchfab/6a0e3b97a8a54f2a909d351322916293/<uid>.glb
Out: art/processed/world_v3/nc_buildings|nc_props|nc_trees/<name>.fbx + textures/ + manifest.json

Grouping: every mesh belongs to its glTF ancestor 3 levels below the root (one sub-model of the kitbash). Groups are
classified by bounds/name, de-duplicated by (tris, size) signature, and named nc_building_NN / nc_prop_<kind>_NN /
nc_tree_NN. Excluded: the BMW E46 car (real brand), road/sidewalk slabs (the island has its own road network).
"""
import bpy, sys, os, re, json, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib
import world_v3_lib as W
importlib.reload(W)
from mathutils import Vector, Matrix
import numpy as np

UID = '6a0e3b97a8a54f2a909d351322916293'
RAW = os.environ.get('CTL_SF_RAW', '/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab')
OUT = os.path.join(os.path.dirname(__file__), '..', 'processed', 'world_v3')
OUT = os.path.abspath(OUT)
CREDIT = ('"Neighbourhood City Modular lowpoly" by golukumar (https://sketchfab.com/mortalityrexotable), '
          'Sketchfab Free Standard licence; modified by CT Life (split, decimated, atlased)')
SRC_URL = 'https://sketchfab.com/3d-models/neighbourhood-city-modular-lowpoly-' + UID

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ONLY = args[args.index('--only') + 1].split(',') if '--only' in args else ['buildings', 'props', 'trees']
DOORS = args[args.index('--doors') + 1].split(',') if '--doors' in args else []

KINDS = [  # (regex on group/mesh names, kind)
    (r'BMW', None),
    (r'globeLight', 'lamp_globe'), (r'victorianLight', 'lamp_victorian'), (r'arcLight', 'lamp_arc'),
    (r'largeLight_LED', 'lamp_led'), (r'largeLight_longDouble', 'lamp_double'), (r'largeLight_small', 'lamp_small'),
    (r'largeLights', 'lamp_large'), (r'solarPanel', 'lamp_solar'), (r'streetLights', 'lamp'),
    (r'BarricadeA', 'barricade_a'), (r'BarricadeB', 'barricade_b'), (r'TrafficBarrierBSign', 'barrier_sign'),
    (r'TrafficBarrier', 'barrier'), (r'TrafficCone', 'cone'), (r'TrafficDrum', 'drum'), (r'Delineator', 'delineator'),
    (r'Verticade', 'verticade'), (r'SM_Light', 'work_light'), (r'Acer|Pot_9|Plant', 'tree'),
    (r'Split_Ac|Window_AC', 'ac_unit'),
]


def kind_of(names):
    s = ' '.join(names)
    for rx, k in KINDS:
        if re.search(rx, s):
            return k
    return 'misc'


def main():
    W.reset()
    W.import_glb(os.path.join(RAW, UID, UID + '.glb'))
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    groups = {}
    for o in meshes:
        chain = []
        p = o.parent
        while p:
            chain.append(p)
            p = p.parent
        chain = chain[::-1]
        key = chain[3].name if len(chain) > 3 else o.name
        groups.setdefault(key, []).append(o)
    log_rows = []
    buildings, props, trees = [], [], []
    for key, objs in groups.items():
        mn, mx = W.world_bounds(objs)
        size = mx - mn
        t = sum(W.tris(o) for o in objs)
        names = [key] + [o.name for o in objs]
        for o in objs:
            q = o.parent
            while q:
                names.append(q.name)
                q = q.parent
        k = kind_of(names)
        if k is None:
            continue
        if size.z < 0.25 and (size.x > 2.5 or size.y > 2.5):
            continue                                    # road / sidewalk slabs
        if size.z < 3.5 and max(size.x, size.y) > 8.0:
            continue                                    # kerb / barrier runs (sidewalk kit, not a prop)
        if size.z < 0.12:
            continue
        if size.z > 3.5 and size.x * size.y > 20 and k not in ('tree',):
            buildings.append((key, objs, size, t))
        elif k == 'tree':
            trees.append((key, objs, size, t))
        else:
            props.append((key, objs, size, t, k))
    # de-duplicate by signature
    def sig(size, t):
        s = sorted([round(size.x, 1), round(size.y, 1)])
        return (t, s[0], s[1], round(size.z, 1))
    man_b, man_p, man_t = {}, {}, {}
    if 'buildings' in ONLY:
        seen = set()
        n = 0
        for key, objs, size, t in sorted(buildings, key=lambda b: -b[3]):
            if sig(size, t) in seen:
                continue
            seen.add(sig(size, t))
            n += 1
            name = f'nc_building_{n:02d}'
            man_b[name] = process_building(name, objs, size)
        W.write_manifest(os.path.join(OUT, 'nc_buildings', 'manifest.json'), man_b)
    if 'props' in ONLY:
        seen = {}
        for key, objs, size, t, k in sorted(props, key=lambda p: (p[4], -p[3])):
            s = sig(size, t)
            if s in seen:
                continue
            seen[s] = 1
            cnt = sum(1 for v in man_p.values() if v['kind'] == k) + 1
            name = f'nc_prop_{k}_{cnt:02d}'
            man_p[name] = dict(objs=objs, kind=k)
        man_p = process_family(man_p, 'nc_props', lod1=500, lod0=2000)
        W.write_manifest(os.path.join(OUT, 'nc_props', 'manifest.json'), man_p)
    if 'trees' in ONLY:
        seen = set()
        man = {}
        for key, objs, size, t in sorted(trees, key=lambda p: -p[3]):
            if sig(size, t) in seen:
                continue
            seen.add(sig(size, t))
            man[f'nc_tree_{len(man) + 1:02d}'] = dict(objs=objs, kind='tree_planter')
        man_t = process_family(man, 'nc_trees', lod1=1200, lod0=5000, keep_materials=True)
        W.write_manifest(os.path.join(OUT, 'nc_trees', 'manifest.json'), man_t)
    W.log('done', len(man_b), 'buildings', len(man_p), 'props', len(man_t), 'trees')


def facade_yaw(o):
    """Yaw so the longer horizontal side runs along X (frontage) -> front faces -Y."""
    vs = np.array([v.co[:] for v in o.data.vertices])
    sx, sy = np.ptp(vs[:, 0]), np.ptp(vs[:, 1])
    return 90.0 if sy > sx * 1.05 else 0.0


def process_building(name, objs, size):
    fam = 'nc_buildings'
    d = os.path.join(OUT, fam)
    man = W.building_lods(name, objs, os.path.join(d, 'textures'), os.path.join(d, name + '.fbx'), door=name in DOORS)
    man.update(fbx=f'world_v3/{fam}/{name}.fbx', kind='building', source=SRC_URL, source_uid=UID,
               licence='Sketchfab Free Standard [Unverified: crypto/NFT terms]', credit=CREDIT,
               tags=['building', 'kit:nc', 'midrise'] + (['enterable'] if name in DOORS else []))
    return man


def process_family(items, fam, lod1, lod0, keep_materials=False):
    d = os.path.join(OUT, fam)
    srcs = {}
    for name, it in items.items():
        o = W.join_copy(it['objs'], name + '_src')
        W.clean(o)
        it['size'] = W.to_ground_origin(o, facade_yaw(o))
        srcs[name] = o
    if keep_materials:
        at = W.copy_materials(list(srcs.values()), os.path.join(d, 'textures'), fam, max_px=1024)
    else:
        first, rest = list(srcs.values())[0], list(srcs.values())[1:]
        at = W.build_atlas(first, fam, os.path.join(d, 'textures'), extra_objs=rest)
    out = {}
    for name, o in srcs.items():
        l0 = W.decimate_copy(o, lod0, name + '_LOD0', planar_first=False)
        l1 = W.decimate_copy(o, lod1, name + '_LOD1', planar_first=False)
        lods = [l0, l1]
        if keep_materials:                                   # trees: crossed-quad billboard as LOD2
            lods.append(W.billboard(l0, name, os.path.join(d, 'textures')))
        W.export_fbx(lods, os.path.join(d, name + '.fbx'))
        out[name] = dict(fbx=f'world_v3/{fam}/{name}.fbx', kind=items[name]['kind'], tris=[W.tris(x) for x in lods],
                         size_m=[round(x, 3) for x in items[name]['size']], materials=at['materials'],
                         textures=at['textures'] + ([f'{name}_billboard.png'] if keep_materials else []), source=SRC_URL, source_uid=UID,
                         licence='Sketchfab Free Standard [Unverified: crypto/NFT terms]', credit=CREDIT,
                         tags=['prop', 'kit:nc', items[name]['kind']])
        W.log(name, out[name]['tris'], out[name]['size_m'])
        bpy.data.objects.remove(o)
    return out


main()
