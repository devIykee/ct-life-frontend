"""Bake ready-made NPCs: every part merged into ONE skinned mesh per LOD with ONE 1024 atlas.

Usage: art/pipeline/blender.sh art/pipeline/char_npc.py -- [npc,npc,...]
Needs char_export.py to have run for both sexes (uses the exported texture tiles).
Output: art/processed/characters/npc/<npc>.fbx (+ <npc>.png atlas) and npc/manifest.json
"""
import bpy, bmesh, sys, os, json
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
import ctl_tex as T
import ctl_face as CF
from ctl_mesh import dup, decimate, clean_weights, ensure_skinned, export_fbx, tris, select_only

ONLY = sys.argv[sys.argv.index('--') + 1].split(',') if '--' in sys.argv and len(sys.argv) > sys.argv.index('--') + 1 else None
OUT = os.path.join(C.PROCESSED, 'characters', 'npc')
os.makedirs(OUT, exist_ok=True)


def log(*a):
    print('[char_npc]', *a, flush=True)


def pack(tiles, size=1024):
    """Shelf-pack square power-of-two tiles (largest first). tiles: {key: px}. Returns {key: (x, y, s)} or None."""
    order = sorted(tiles.items(), key=lambda kv: -kv[1])
    out, x, y, shelf = {}, 0, 0, 0
    for k, s in order:
        if x + s > size:
            x, y, shelf = 0, y + shelf, 0
        if y + s > size:
            return None
        out[k] = (x, y, s)
        x += s
        shelf = max(shelf, s)
    return out


def build(name, spec):
    sex, parts, hair, skin, eyes = spec
    man = json.load(open(os.path.join(C.PROCESSED, 'characters', sex, 'manifest.json')))
    base = os.path.join(C.PROCESSED, 'characters', sex)
    bpy.ops.wm.open_mainfile(filepath=os.path.join(C.WORK, f'char_{sex}_master.blend'))
    rig = bpy.data.objects[f'CTL_Rig_{sex}']
    body = bpy.data.objects['CTL_Body']
    REG = json.loads(body['ctl_regions'])
    for b in rig.data.bones:
        b.use_deform = True
    parts = dict(parts)
    hat_mode = next((man['parts'][p].get('hair_mode') for p in parts if man['parts'][p].get('hair_mode')), None)
    if hair and hat_mode != 'hide':
        parts['hair_buzzed' if hat_mode == 'short' else hair[0]] = hair[1]
    hides = set()
    for p in parts:
        hides.update(man['parts'][p]['hides_regions'])
    # texture tiles
    tex = {'skin': (os.path.join(base, man['body']['skin_textures'][skin]), 512),
           'eyes': (os.path.join(base, man['body']['eye_textures'][eyes]), 256)}
    for p, v in parts.items():
        info = man['parts'][p]
        tex[p] = (os.path.join(base, info['textures'].get(v) or info['textures'][info['default']]), info['tile'])
    sizes = {k: s for k, (_, s) in tex.items()}
    layout = pack(sizes)
    while layout is None:  # shrink the biggest non-skin tile until everything fits
        k = max((k for k in sizes if k != 'skin'), key=lambda k: sizes[k])
        sizes[k] //= 2
        layout = pack(sizes)
    atlas = np.zeros((1024, 1024, 3), np.float32)
    for k, (x, y, s) in layout.items():
        img = T.load(tex[k][0], 3)
        atlas[y:y + s, x:x + s] = T.resize(img, s, s)
    # objects: body (minus hidden regions) + eyes + parts
    srcs = []
    if 'hoodie' in parts and 'part.hoodie' not in bpy.data.objects:
        sw = bpy.data.objects['part.sweater']
        hd = sw.copy(); hd.data = sw.data.copy(); hd.name = 'part.hoodie'
        bpy.context.scene.collection.objects.link(hd); hd['ctl_slot'] = 'top'
    b = dup(body, 'npc_body')
    bm = bmesh.new(); bm.from_mesh(b.data)
    bm.normal_update()
    rl = bm.faces.layers.int.get('ctl_region')
    # static outfit -> cull every body face that a part covers completely (exact, no poke-through)
    from mathutils.bvhtree import BVHTree
    trees = []
    for p in parts:
        if man['parts'][p]['slot'] in ('hair', 'facial_hair', 'hat', 'glasses', 'acc', 'held', 'neck', 'face', 'back', 'top_addon'):
            continue
        pb = bmesh.new(); pb.from_mesh(bpy.data.objects['part.' + p].data)
        trees.append(BVHTree.FromBMesh(pb)); pb.free()
    covered = set()
    for v in bm.verts:
        for t in trees:
            if t.ray_cast(v.co - v.normal * 0.003, v.normal, 0.05)[0] is not None:
                covered.add(v.index)
                break
    # erode by one ring so faces at garment edges (armpits, collars, hems) stay -> no see-through gaps
    bm.verts.ensure_lookup_table()
    core = {i for i in covered if all(e.other_vert(bm.verts[i]).index in covered for e in bm.verts[i].link_edges)}
    kill = [f for f in bm.faces if REG[f[rl]] in hides or all(v.index in core for v in f.verts)]
    # the covered border that stays gets pulled 7 mm inwards -> no skin poking through sleeves when posed
    for i in covered - core:
        v = bm.verts[i]
        v.co -= v.normal * 0.007
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(b.data); bm.free()
    srcs.append(('skin', b, 'body'))
    FL = CF.landmarks(rig, bpy.data.objects['CTL_Eyes'], body)
    eyes_obj, lash_idx = CF.eyes_with_lashes(bpy.data.objects['CTL_Eyes'], bpy.data.objects['CTL_Lashes'], body, 'npc_eyes')
    srcs.append(('eyes', eyes_obj, 'eyes'))
    for p in parts:
        srcs.append((p, dup(bpy.data.objects['part.' + p], 'npc_' + p), man['parts'][p]['slot']))
    # remap UVs into the atlas (FBX/Blender UV origin = bottom-left; numpy row 0 = top)
    for k, o, slot in srcs:
        x, y, s = layout[k]
        u0, v0, sc = x / 1024, 1 - (y + s) / 1024, s / 1024
        uv = o.data.uv_layers.active.data
        a = np.zeros(len(uv) * 2, np.float32)
        uv.foreach_get('uv', a)
        a = np.clip(a, 0.0005, 0.9995)
        a[0::2] = u0 + a[0::2] * sc
        a[1::2] = v0 + a[1::2] * sc
        uv.foreach_set('uv', a)
        o.data.uv_layers.active.name = 'UVMap'
    # LODs per source then join per level
    budgets = {'body': (None, 0.55, 0.3), 'eyes': (None, None, None)}
    lods = [[], [], []]
    for k, o, slot in srcs:
        for i in range(3):
            d = dup(o, f'{name}_{k}_{i}')
            if slot == 'body':
                r = C.LOD_BUDGET['body'][i]
                if i > 0 and r:
                    decimate(d, ratio=r)
                elif i == 0:   # crowd LOD0: thin the mitten hands only
                    attr = d.data.attributes['ctl_region']
                    regs = json.loads(body['ctl_regions'])
                    thin = set()
                    for p_ in d.data.polygons:
                        if regs[attr.data[p_.index].value].startswith('hand_'):
                            thin.update(p_.vertices)
                    if thin:
                        vg = d.vertex_groups.new(name='_thin'); vg.add(list(thin), 1.0, 'REPLACE')
                        m = d.modifiers.new('Dec', 'DECIMATE'); m.ratio = 0.85; m.vertex_group = '_thin'; m.use_symmetry = True
                        select_only([d], d)
                        bpy.ops.object.modifier_move_to_index(modifier='Dec', index=0)
                        bpy.ops.object.modifier_apply(modifier='Dec')
            elif slot != 'eyes':
                decimate(d, target_tris=int(C.LOD_BUDGET.get(slot, (1500, 750, 300))[i] * C.NPC_LOD_SCALE[i]))
            clean_weights(d, 2 if i == 2 else 4)
            ensure_skinned(d, rig)
            d.data.materials.clear()
            lods[i].append(d)
    # crowd budget: shrink the clothing/hair/accessories of LOD0 until the whole NPC fits NPC_LOD0_CAP
    for _ in range(3):
        total = sum(tris(d) for d in lods[0])
        if total <= C.NPC_LOD0_CAP:
            break
        fixed = sum(tris(d) for (k, o, slot), d in zip(srcs, lods[0]) if slot in ('body', 'eyes'))
        var = [d for (k, o, slot), d in zip(srcs, lods[0]) if slot not in ('body', 'eyes')]
        factor = max(0.3, (C.NPC_LOD0_CAP - fixed) / max(1, sum(tris(d) for d in var)) * 0.98)
        for d in var:
            decimate(d, target_tris=max(40, int(tris(d) * factor)))
            clean_weights(d, 4)
    # blend shapes: blink + lip-sync on the body, blink on the lashes (LOD0 and LOD1)
    shape_names = []
    for i in (0, 1):
        for (k, o, slot), d in zip(srcs, lods[i]):
            if slot == 'body':
                got = CF.add_face_shapes(d, FL, mouth=True)
                shape_names = shape_names or got
            elif slot == 'eyes':
                CF.add_blink_only(d, FL, lash_idx)
    mat = bpy.data.materials.new(f'M_npc_{name}')
    mat.use_nodes = True
    png = os.path.join(OUT, f'{name}.png')
    T.save(atlas, png)
    n = mat.node_tree.nodes.new('ShaderNodeTexImage')
    n.image = bpy.data.images.load(png)
    mat.node_tree.links.new(n.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    finals = []
    for i, objs in enumerate(lods):
        select_only(objs, objs[0])
        bpy.ops.object.join()
        j = bpy.context.view_layer.objects.active
        j.name = j.data.name = f'{name}_LOD{i}'
        j.data.materials.clear()
        j.data.materials.append(mat)
        clean_weights(j, 2 if i == 2 else 4)
        finals.append(j)
    fbx = os.path.join(OUT, f'{name}.fbx')
    export_fbx(fbx, finals, rig)
    info = {'fbx': f'{name}.fbx', 'atlas': f'{name}.png', 'sex': sex, 'tris': [tris(f) for f in finals],
            'parts': parts, 'skin': skin, 'eyes': eyes, 'hidden_regions': sorted(hides), 'blend_shapes': shape_names,
            'emissive': any((p, None) in C.EMISSIVE or (p, v) in C.EMISSIVE for p, v in parts.items()),
            'atlas_layout': {k: list(v) for k, v in layout.items()}}
    log(name, info['tris'])
    return info


mp = os.path.join(OUT, 'manifest.json')
manifest = json.load(open(mp)) if os.path.exists(mp) else {}
for name, spec in C.NPCS.items():
    if ONLY and name not in ONLY:
        continue
    manifest[name] = build(name, spec)
json.dump(manifest, open(mp, 'w'), indent=1)
log('manifest', mp)
