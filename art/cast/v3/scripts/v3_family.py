"""Cast v3 modular family builder (Quaternius Ultimate Modular Men / Women).

One family = one CTS skeleton (built from the pack's shared rig) + one 2048 atlas + a part library
(head [face+hair/headwear], top, bottom, feet, extras) in which every part fits every character of the family.
Each source character becomes a distinct base (its own default parts and default look). Tintable regions are
stored as shade x neutral reference, so mixed parts re-colour consistently under one material.

usage: Blender -b --factory-startup --disable-autoexec -P v3_family.py -- umm|umw
Outputs: art/cast/v3/export/families/<fam>/..., art/cast/v3/export/bases/<base>/..., EVID/v3/families/<fam>.blend
"""
import bpy, bmesh, sys, json, math, time, hashlib
import numpy as np
from collections import Counter
from pathlib import Path
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
import v3_common as K
from v3_common import (EXPORT, EVID, TIERS, CAPS, MAX_INFL, CTS_BONES, SOCKETS, REGION_ID, MASK_CHANNEL, TINT_REGIONS,
                       STARTER_ACCESSORIES, SLOT_CAPS, TN, HAIR_COLORS, CLOTH_COLORS, HAT_COLORS, log)
import v2_build as B
import v2_adapters as A
import v2_geo as G
import cast_common as C1
import v3_spec as S

RES = 2048
REF_HEX = '#9a9a9a'                          # neutral reference stored for every tintable texel
TINTABLE = set(TINT_REGIONS)
SLOT_PART = {'head': 'head', 'top': 'top', 'bottom': 'bottom', 'feet': 'feet', 'extra': 'acc'}
OUTDIR = None

SMOOTH_ANGLE = 60.0   # Quaternius ships flat-shaded (every edge sharp); smooth-by-angle matches UBC/heroes under the toon ramp

def smooth_by_angle(o):
    """replace authored flat normals by smooth-by-angle normals (hard edges kept above SMOOTH_ANGLE)"""
    me = o.data
    with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o]):
        if me.has_custom_normals:
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(SMOOTH_ANGLE))

def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()

# ------------------------------------------------------------------ rig
def mapping_for(fam, src):
    names = {b.name for b in src.data.bones}
    m = {'Root': 'root', 'Hips': 'pelvis', 'Abdomen': 'spine_01', 'Torso': 'spine_02', 'Chest': 'spine_03',
         'Neck': 'neck_01', 'Head': 'Head'}
    for S_, s in (('L', 'l'), ('R', 'r')):
        m.update({f'Shoulder.{S_}': f'clavicle_{s}', f'UpperArm.{S_}': f'upperarm_{s}', f'LowerArm.{S_}': f'lowerarm_{s}',
                  f'Hand.{S_}': f'hand_{s}', f'UpperLeg.{S_}': f'thigh_{s}', f'LowerLeg.{S_}': f'calf_{s}', f'Foot.{S_}': f'foot_{s}'})
        first = 2 if f'Index2.{S_}' in names and f'Index1.{S_}' not in names else 1
        for f, q in (('Index', 'index'), ('Middle', 'middle'), ('Ring', 'ring'), ('Pinky', 'pinky')):
            for j in range(3):
                m[f'{f}{first + j}.{S_}'] = f'{q}_0{j + 1}_{s}'
        tfirst = 2 if f'Thumb1.{S_}' not in names else 1
        for j in range(3):
            if f'Thumb{tfirst + j}.{S_}' in names:
                m[f'Thumb{tfirst + j}.{S_}'] = f'thumb_0{j + 1}_{s}'
    missing = [k for k in m if k not in names]
    assert not missing, f'{fam}: source bones missing {missing}'
    return m

def build_cts(src, mapping):
    mw = src.matrix_world
    pos = {}
    for sn, cn in mapping.items():
        b = src.data.bones[sn]
        pos[cn] = (mw @ b.head_local, mw @ b.tail_local)
    names = {b.name for b in src.data.bones}
    for S_, s in (('L', 'l'), ('R', 'r')):
        if f'thumb_03_{s}' not in pos:          # men: two thumb joints -> split the distal segment
            h, t = pos[f'thumb_02_{s}']
            mid = h.lerp(t, 0.5)
            pos[f'thumb_02_{s}'] = (h, mid); pos[f'thumb_03_{s}'] = (mid, t)
        for f in ('Index', 'Middle', 'Ring', 'Pinky'):
            q = f.lower()
            end = f'{f}4.{S_}_end'
            if end in names:
                b = src.data.bones[end]
                pos[f'{q}_04_leaf_{s}'] = (mw @ b.head_local, mw @ b.tail_local)
        fe = f'Foot.{S_}_end'
        if fe in names:
            toe = mw @ src.data.bones[fe].head_local
            ank = pos[f'foot_{s}'][0]
            ball = Vector((ank.x, ank.y + 0.62 * (toe.y - ank.y), max(0.02, toe.z)))
            pos[f'foot_{s}'] = (ank, ball)
            pos[f'ball_{s}'] = (ball, Vector((ank.x, toe.y, ball.z)))
    if 'Head_end' in names:
        h = pos['Head'][0]
        pos['Head'] = (h, mw @ src.data.bones['Head_end'].head_local)
    sa = bpy.data.armatures.new('__src'); so = bpy.data.objects.new('__src', sa)
    bpy.context.scene.collection.objects.link(so); bpy.context.view_layer.objects.active = so
    bpy.ops.object.mode_set(mode='EDIT')
    for n, (h, t) in pos.items():
        eb = sa.edit_bones.new(n); eb.head = h; eb.tail = t if (t - h).length > 1e-4 else h + Vector((0, 0, 0.02))
    bpy.ops.object.mode_set(mode='OBJECT')
    cts = A.build_cts_from_source(so, {n: n for n in pos})
    bpy.data.objects.remove(so, do_unlink=True)
    return cts

def rig_signature(src):
    return hashlib.sha1(json.dumps([(b.name, [round(x, 4) for x in b.head_local], [round(x, 4) for x in b.tail_local])
                                    for b in src.data.bones]).encode()).hexdigest()

# ------------------------------------------------------------------ classification
def comps_of(me, faces):
    parent = {}
    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for fi in faces:
        vs = me.polygons[fi].vertices
        for v in vs:
            parent.setdefault(v, v)
        r0 = find(vs[0])
        for v in vs[1:]:
            r = find(v)
            if r != r0:
                parent[r] = r0
    out = {}
    for fi in faces:
        out.setdefault(find(me.polygons[fi].vertices[0]), []).append(fi)
    return list(out.values())

def classify(o, slot, fam, ovr):
    me = o.data
    mats = [(m.name.split('.')[0] if m else '') for m in me.materials]
    F = S.FAMILIES[fam]
    regs = []
    note = {}
    if slot == 'head':
        rules = dict(F['head_regions']); rules.update(ovr.get('head', {}))
        for p in me.polygons:
            regs.append(rules.get(mats[p.material_index], 'hat'))
        # eyes / brows by connected component (some packs share one material for eyes+brows or hair+brows)
        mw = o.matrix_world
        cen = [mw @ p.center for p in me.polygons]
        ymid = float(np.mean([c.y for c in cen]))
        def stats(comp):
            P = np.array([tuple(cen[i]) for i in comp]); return P.mean(0), len(comp)
        eye_comps = [c for c in comps_of(me, [i for i, r in enumerate(regs) if r == 'eyes']) if len(c) <= 40]
        if eye_comps:
            zs = [stats(c)[0][2] for c in eye_comps]
            if max(zs) - min(zs) > 0.012:
                mid = (max(zs) + min(zs)) / 2
                for c, z in zip(eye_comps, zs):
                    if z > mid:
                        for i in c:
                            regs[i] = 'brows'
                eye_comps = [c for c, z in zip(eye_comps, zs) if z <= mid]
            eye_z = float(np.mean([stats(c)[0][2] for c in eye_comps]))
            moved = 0
            for c in comps_of(me, [i for i, r in enumerate(regs) if r == 'hair']):
                m_, n = stats(c)
                if n <= 80 and m_[1] < ymid and eye_z + 0.004 < m_[2] < eye_z + 0.07 and abs(m_[0]) < 0.09:
                    for i in c:
                        regs[i] = 'brows'
                    moved += n
            note = {'eye_z': round(eye_z, 4), 'brow_faces_from_hair': moved}
    elif slot == 'extra':
        cnt = Counter(mats[p.material_index] for p in me.polygons)
        main = cnt.most_common(1)[0][0]
        regs = ['accent' if mats[p.material_index] == main else 'none' for p in me.polygons]
    else:
        rules = ovr.get({'top': 'body', 'bottom': 'legs', 'feet': 'feet'}[slot], {})
        main = {'top': 'top', 'bottom': 'bottom', 'feet': 'shoes'}[slot]
        cnt = Counter(mats[p.material_index] for p in me.polygons
                      if not mats[p.material_index].startswith('Skin') and mats[p.material_index] not in rules)
        order = [k for k, _ in cnt.most_common()]
        tot = sum(cnt.values())
        for p in me.polygons:
            mn = mats[p.material_index]
            if mn in rules:
                r = rules[mn]
            elif mn.startswith('Skin'):
                r = 'skin'
            elif order and mn == order[0]:
                r = main
            elif slot == 'top' and len(order) > 1 and mn == order[1] and cnt[mn] >= 0.15 * tot:
                r = 'accent'
            else:
                r = 'none'
            regs.append(r)
    A.set_face_int(o, 'region', [REGION_ID[r] for r in regs])
    A.set_face_int(o, 'part', A.PART_ID[SLOT_PART[slot]])
    return Counter(regs), note

# ------------------------------------------------------------------ UVs
def atlas_uv(o, rect):
    uvs = o.data.uv_layers
    for l in [l.name for l in uvs]:
        uvs.remove(uvs[l])
    uvs.new(name='AtlasUV'); uvs.active = uvs['AtlasUV']
    C1.select_only([o])
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.reveal(select=True)
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02, scale_to_bounds=False)
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=True, scale=True, merge_overlap=False,
                            margin_method='FRACTION', margin=0.008)
    bpy.ops.object.mode_set(mode='OBJECT')
    x, y, w, h = rect
    l = o.data.uv_layers['AtlasUV']
    arr = np.empty(len(o.data.loops) * 2, dtype=np.float32); l.data.foreach_get('uv', arr); arr = arr.reshape(-1, 2)
    arr[:, 0] = x + np.clip(arr[:, 0], 0, 1) * w
    arr[:, 1] = y + np.clip(arr[:, 1], 0, 1) * h
    l.data.foreach_set('uv', arr.ravel())

# ------------------------------------------------------------------ colour helpers
def hexs(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0, 1)
    return '#%02x%02x%02x' % tuple(int(round(float(x) * 255)) for x in c[:3])

def resolve_tint(region, v):
    if v is None:
        return None
    if v.startswith('#'):
        return v
    if region in ('hair', 'brows'):
        return HAIR_COLORS[v]
    if region == 'hat':
        return HAT_COLORS.get(v) or CLOTH_COLORS[v]
    return CLOTH_COLORS[v]

def skin_hex(tone):
    pal = {t['id']: t['hex'] for t in json.loads((K.V2EXPORT / 'skin_palette.json').read_text())['tones']}
    return pal[tone]

# ------------------------------------------------------------------ build
def build_family(fam):
    t0 = time.time()
    F = S.FAMILIES[fam]
    srcdir = getattr(K, F['dir'])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    w = bpy.data.worlds.new('w'); scene.world = w
    w.light_settings.distance = 0.30
    out = EXPORT / 'families' / fam
    (out / 'parts').mkdir(parents=True, exist_ok=True)
    chars = F['characters']
    blocks = K.family_blocks(len(chars) + len(F['extras']))
    cts, mapping, sig0 = None, None, None
    parts = {}            # (key, slot) -> object ; key = base id suffix
    info = {'classification': {}, 'sources': {}}
    allmeshes = []
    for ci, (stem, bid, label, arch, ovr, look) in enumerate(chars):
        path = srcdir / f'{stem}.fbx'
        info['sources'][bid] = {'file': str(path.relative_to(K.REPO)), 'sha256': sha(path)}
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(path), automatic_bone_orientation=False, ignore_leaf_bones=False)
        objs = [o for o in bpy.data.objects if o not in before]
        # UMW FBX materials import with Principled Alpha 0 (FBX TransparencyFactor): the Cycles DIFFUSE colour bake then
        # returns black. The packs are opaque by design -> force Alpha 1 on every imported material.
        nalpha = 0
        for o in objs:
            for m in (o.data.materials if o.type == 'MESH' else []):
                b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if m and m.node_tree else None
                if b is not None and b.inputs['Alpha'].default_value < 1.0:
                    b.inputs['Alpha'].default_value = 1.0; nalpha += 1
        if nalpha:
            info.setdefault('source_alpha_forced_opaque', {})[bid] = nalpha
            log('forced opaque', bid, nalpha)
        src = next(o for o in objs if o.type == 'ARMATURE')
        src.data.pose_position = 'REST'
        if src.animation_data:
            src.animation_data.action = None
        bpy.context.view_layer.update()
        if cts is None:
            mapping = mapping_for(fam, src)
            cts = build_cts(src, mapping)
            sig0 = rig_signature(src)
        elif rig_signature(src) != sig0:
            raise RuntimeError(f'{bid}: rig differs from the family rig')
        key = bid.split('_', 1)[1]
        for o in objs:
            if o.type != 'MESH':
                continue
            n = o.name.split('.')[0]
            if n.endswith('_Head'):
                slot = 'head'
            elif n.endswith('_Body'):
                slot = 'top'
            elif n.endswith(('_Legs', '_Pants')):
                slot = 'bottom'
            elif n.endswith('_Feet'):
                slot = 'feet'
            elif n in F['extras']:
                slot = 'extra'
            else:
                raise RuntimeError(f'{bid}: unclassified mesh {o.name}')
            A.retarget_weights(o, src, mapping, cts)
            nfill = A.fill_unweighted(o, cts)
            smooth_by_angle(o)
            regs, note = classify(o, slot, fam, ovr)
            if nfill:
                note['unweighted_vertices_filled'] = nfill
                log('filled unweighted', o.name, nfill)
            pkey = (F['extras'][n]['id'] if slot == 'extra' else key, slot)
            o.name = o.data.name = f'{fam}__{slot}_{pkey[0]}'
            parts[pkey] = o
            o['owner'] = bid
            allmeshes.append(o)
            info['classification'][o.name] = {'regions': dict(regs), **note}
        for o in objs:
            if o.type not in ('MESH',):
                bpy.data.objects.remove(o, do_unlink=True)
    cts.name = cts.data.name = 'CTS'
    B.normalise(cts, allmeshes)
    # atlas UVs: one block per character (+ extras)
    owners = [c[1] for c in chars]
    for (pk, slot), o in parts.items():
        if slot == 'extra':
            bi = len(chars) + list(v['id'] for v in F['extras'].values()).index(pk)
        else:
            bi = owners.index(o['owner'])
        o['block'] = bi
        atlas_uv(o, K.part_rect(blocks[bi], slot))
    # bake (characters spread along X so AO never crosses characters)
    copies = []
    for (pk, slot), o in parts.items():
        c = C1.duplicate(o, '__bake_' + o.name)
        off = 3.0 * (owners.index(o['owner']) if o['owner'] in owners else 0)
        c.data.transform(Matrix.Translation((off, 0, 0)))
        c.modifiers.clear()
        copies.append(c)
    B.RES = RES
    res, reg, cov = B.bake_maps(copies, scene)
    for c in copies:
        bpy.data.objects.remove(c, do_unlink=True)
    flat = np.clip(res['albedo'][:, :, :3], 0, 1)             # source flat colours (sRGB)
    alb = B.painted(res['albedo'], res['ao'], cov)            # painted-toon shading
    # tintable texels: shade x neutral reference (consistent re-colouring of mixed parts)
    ref_lin = C1.srgb_to_lin(C1.hexrgb(REF_HEX))
    L_p = C1.luminance(C1.srgb_to_lin(alb)); L_f = C1.luminance(C1.srgb_to_lin(flat))
    shade = np.clip(L_p / np.maximum(L_f, 1e-4), 0.3, 1.25)
    tint_px = np.zeros(reg.shape, bool)
    for r in TINTABLE:
        tint_px |= (reg == REGION_ID[r])
    tint_px &= cov
    alb_t = alb.copy()
    alb_t[tint_px] = C1.lin_to_srgb(np.clip(ref_lin[None, :] * shade[tint_px][:, None], 0, 1))
    alb_t = C1.dilate(alb_t, cov, 10)
    m0 = np.zeros(reg.shape + (4,), np.float32); m1 = np.zeros(reg.shape + (4,), np.float32)
    for name, (k, ch) in MASK_CHANNEL.items():
        (m0 if k == 0 else m1)[(reg == REGION_ID[name]) & cov, ch] = 1.0
    m0 = C1.dilate(m0, cov, 6); m1 = C1.dilate(m1, cov, 6)
    emis = np.zeros_like(alb_t)
    B.paint_acc_slot(alb_t, emis, m1)
    paths = {'albedo': out / f'{fam}_Albedo.png', 'mask0': out / f'{fam}_TintMask0.png', 'mask1': out / f'{fam}_TintMask1.png',
             'emission': out / f'{fam}_Emission.png'}
    C1.write_png(paths['albedo'], alb_t); C1.write_png(paths['mask0'], m0); C1.write_png(paths['mask1'], m1)
    C1.write_png(paths['emission'], emis)
    rel_tex = {k: str(v.relative_to(EXPORT)) for k, v in paths.items()}
    # authored colours per character/region (from the flat source colours) -> default looks
    authored = {}
    for ci, (stem, bid, *_r) in enumerate(chars):
        bx, by = blocks[ci]
        c = RES // K.FGRID
        sel_block = np.zeros(reg.shape, bool)
        sel_block[by * c:(by + K.BLOCK_H) * c, bx * c:(bx + K.BLOCK_W) * c] = True
        authored[bid] = {}
        for r in TINT_REGIONS:
            sel = sel_block & cov & (reg == REGION_ID[r])
            if sel.sum() > 20:
                authored[bid][r] = hexs(np.median(flat[sel], axis=0))
    # material + tiers + part exports
    mat = B.make_material(f'{fam}_Mat', {'albedo': paths['albedo'], 'emission': paths['emission']})
    tier_objs = {}
    library = {}
    for (pk, slot), o in parts.items():
        objs = []
        for tier in TIERS:
            cap = SLOT_CAPS[tier][slot]
            t = B.make_tier(o, min(cap, C1.tri_count(o)), tier, cts)
            t.data.validate(clean_customdata=False)   # drop decimation duplicates/degenerates now (FBX import would) -> counts match
            t.data.set_sharp_from_angle(angle=math.radians(SMOOTH_ANGLE))
            t.data.materials.clear(); t.data.materials.append(mat)
            t.name = t.data.name = f'{fam}__{slot}_{pk}_{TN[tier]}'
            objs.append(t)
            tier_objs[(pk, slot, tier)] = t
        p = out / 'parts' / f'{fam}__{slot}_{pk}.fbx'
        B.export(objs, cts, p)
        zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
        library.setdefault(slot, {})[pk] = {
            'fbx': str(p.relative_to(EXPORT)), 'source_character': o['owner'],
            'triangles': {t: C1.tri_count(x) for t, x in zip(TIERS, objs)}, 'source_triangles': C1.tri_count(o),
            'z_range_m': [round(min(zs), 4), round(max(zs), 4)],
            'regions': info['classification'][o.name]['regions']}
    # compatibility (vertical coverage between stacked slots)
    compat = {'bottom_feet': {}, 'top_bottom': {}, 'note': 'pairs whose stacked z ranges leave a visible gap are not offered'}
    for bk, b in library['bottom'].items():
        compat['bottom_feet'][bk] = sorted(fk for fk, f in library['feet'].items() if f['z_range_m'][1] >= b['z_range_m'][0] - 0.01)
    for tk, t in library['top'].items():
        compat['top_bottom'][tk] = sorted(bk for bk, b in library['bottom'].items() if b['z_range_m'][1] >= t['z_range_m'][0] - 0.01)
    # bases
    base_ids = []
    for ci, (stem, bid, label, arch, ovr, look) in enumerate(chars):
        key = bid.split('_', 1)[1]
        bout = EXPORT / 'bases' / bid
        (bout / 'accessories').mkdir(parents=True, exist_ok=True)
        merged = {}
        tinfo = {}
        for tier in TIERS:
            cs = [C1.duplicate(tier_objs[(key, s, tier)], '__m') for s in ('head', 'top', 'bottom', 'feet')]
            C1.select_only(cs)
            bpy.ops.object.join()
            mo = bpy.context.object
            mo.name = mo.data.name = f'{bid}_{TN[tier]}'
            for md in list(mo.modifiers):
                mo.modifiers.remove(md)
            mo.modifiers.new('arm', 'ARMATURE').object = cts
            mo.parent = cts
            merged[tier] = mo
            tinfo[tier] = {'triangles': C1.tri_count(mo), 'vertices': len(mo.data.vertices), 'max_influences': C1.max_influences(mo)}
            assert tinfo[tier]['triangles'] <= CAPS[tier], (bid, tier, tinfo[tier])
        meas = G.measure(cts, [tier_objs[(key, s, 'hero')] for s in ('head', 'top')])
        socks = G.socket_points(meas)
        empties = []
        for sname, pnt in socks.items():
            e = bpy.data.objects.new(f'socket_{sname}', None); e.empty_display_size = 0.04
            scene.collection.objects.link(e)
            e.parent = cts; e.parent_type = 'BONE'; e.parent_bone = SOCKETS[sname]['bone']
            bpy.context.view_layer.update()
            e.matrix_world = Matrix.Translation(Vector(pnt))
            empties.append(e)
        B.export([merged[t] for t in ('lod0', 'lod1', 'lod2')], cts, bout / f'{bid}.fbx', empties)
        B.export([merged['hero']], cts, bout / f'{bid}_Hero.fbx', empties)
        socket_json = {}
        for e in empties:
            b = cts.data.bones[e.parent_bone]
            local = (cts.matrix_world @ b.matrix_local).inverted() @ e.matrix_world
            socket_json[e.name[7:]] = {'bone': e.parent_bone, 'humanoid': SOCKETS[e.name[7:]]['humanoid'],
                                       'position_character_m': [round(x, 4) for x in e.matrix_world.translation],
                                       'offset_in_bone_space_m': [round(x, 4) for x in local.translation]}
        accs = {}
        for name in STARTER_ACCESSORIES:
            bm, sock, flags = G.build_accessory(name, meas)
            me = bpy.data.meshes.new(f'{bid}__acc_{name}')
            bm.to_mesh(me); bm.free()
            ao = bpy.data.objects.new(me.name, me); scene.collection.objects.link(ao)
            ao.parent = cts
            B.acc_uv(ao, name)
            bone = SOCKETS[sock]['bone']
            vg = ao.vertex_groups.new(name=bone); vg.add(list(range(len(me.vertices))), 1.0, 'REPLACE')
            ao.modifiers.new('arm', 'ARMATURE').object = cts
            ao.data.materials.clear(); ao.data.materials.append(mat)
            p = bout / 'accessories' / f'{bid}__{name}.fbx'
            B.export([ao], cts, p)
            accs[name] = {'fbx': str(p.relative_to(EXPORT)), 'socket': sock, 'bone': bone, 'flags': flags, 'triangles': C1.tri_count(ao)}
            bpy.data.objects.remove(ao, do_unlink=True)
        for x in F['extras'].values():
            accs[x['id']] = {'family_part': f"extra/{x['id']}", 'fbx': library['extra'][x['id']]['fbx'], 'socket': x['socket'],
                             'bone': SOCKETS[x['socket']]['bone'], 'flags': {'skinned': True},
                             'triangles': library['extra'][x['id']]['triangles']}
        for e in empties:
            bpy.data.objects.remove(e, do_unlink=True)
        # default look: authored colours, overridden by the spec look; brows follow hair
        lk = dict(authored[bid])
        for r, v in look.items():
            if r != 'skin' and r in authored[bid]:
                lk[r] = resolve_tint(r, v)
        if 'hair' in lk and 'brows' in lk:
            lk['brows'] = K.brow_color(lk['hair'])
        lk['skin'] = skin_hex(look['skin'])
        meta = {'id': bid, 'label': label, 'gender': F['gender'], 'family': fam, 'source': S.MODULAR[bid]['source'],
                'source_file': info['sources'][bid], 'licence': 'CC0 1.0 (quaternius.com pack page + bundled License.txt)',
                'archetypes': arch, 'tiers': tinfo,
                'files': {'crowd_fbx': f'bases/{bid}/{bid}.fbx', 'hero_fbx': f'bases/{bid}/{bid}_Hero.fbx'},
                'textures': rel_tex, 'material': f'{fam}_Mat (shared by the whole family)',
                'tint_reference': {r: REF_HEX for r in TINT_REGIONS}, 'tint_authored': authored[bid],
                'look_defaults': {'skin_tone': look['skin'], **lk},
                'default_parts': {s: f'{s}/{key}' for s in ('head', 'top', 'bottom', 'feet')},
                'parts': {s: {'fbx': library[s][key]['fbx'], 'slot': s, 'triangles': library[s][key]['triangles']}
                          for s in ('head', 'top', 'bottom', 'feet')},
                'accessories': accs, 'sockets': socket_json,
                'measurements': {k: (round(v, 4) if isinstance(v, float) else [round(x, 4) for x in v]) for k, v in meas.items()},
                'height_m': round(max((merged['lod0'].matrix_world @ v.co).z for v in merged['lod0'].data.vertices), 3)}
        (bout / f'{bid}.json').write_text(json.dumps(meta, indent=1))
        base_ids.append(bid)
        log('BASE', bid, json.dumps(tinfo))
    fmeta = {'id': fam, 'label': F['label'], 'gender': F['gender'], 'rig_signature_sha1': sig0, 'skeleton': 'CTS (own proportions)',
             'bone_mapping_source_to_cts': mapping, 'textures': rel_tex, 'atlas_size': RES, 'material': f'{fam}_Mat',
             'tint_reference_hex': REF_HEX, 'bases': base_ids, 'library': library, 'compatibility': compat,
             'slot_caps': SLOT_CAPS, 'excluded_characters': F['excluded'], 'classification': info['classification'],
             'sources': info['sources']}
    (out / f'{fam}.json').write_text(json.dumps(fmeta, indent=1))
    (EVID / 'v3' / 'families').mkdir(parents=True, exist_ok=True)
    for o in list(scene.objects):
        if o.type == 'MESH' and o.name.startswith('__'):
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(EVID / 'v3' / 'families' / f'{fam}.blend'), compress=True)
    log('FAMILY', fam, len(base_ids), 'bases', round(time.time() - t0, 1), 's')

if __name__ == '__main__':
    for fam in sys.argv[sys.argv.index('--') + 1:]:
        build_family(fam)
