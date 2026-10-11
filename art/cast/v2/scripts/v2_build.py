"""Cast v2 base builder: adapter -> CTS-normalised parts -> per-part slot atlas (painted-toon albedo +
tint masks) -> tiers -> sockets + fitted starter accessories -> FBX/PNG/JSON per base.

usage: Blender -b --factory-startup --disable-autoexec -P v2_build.py -- <base_id> [<base_id> ...]
Outputs: art/cast/v2/export/bases/<base_id>/..., EVID/v2/bases/<base_id>.blend
"""
import bpy, bmesh, sys, json, math, time, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(1, str(Path(__file__).resolve().parents[2] / 'scripts'))
from v2_common import *
import v2_adapters as A
import v2_geo as G
import v2_spec as SPEC
import cast_common as C1          # v1 helpers: decimate_to, weights, png io, dilate

RES = 1024                        # authoring atlas; hero ships 1024, crowd 512 (box-downsampled)
GRID = 8
SLOT = {  # (x, y, w, h) cells on an 8x8 grid
    'head': (0, 4, 4, 4), 'hair': (4, 6, 4, 2), 'top': (0, 1, 4, 3), 'bottom': (4, 3, 4, 3),
    'feet': (4, 1, 2, 2), 'acc': (6, 1, 2, 2), 'body': (0, 0, 8, 1),
}
PART_SLOT = {'head': 'head', 'eyes': 'head', 'brows': 'head', 'hair': 'hair', 'top': 'top', 'bottom': 'bottom',
             'feet': 'feet', 'body': 'body', 'acc': 'acc'}
PART_SHARE = {'head': 0.30, 'eyes': 0.02, 'brows': 0.02, 'hair': 0.13, 'top': 0.22, 'bottom': 0.15, 'feet': 0.06, 'body': 0.30, 'acc': 0.05}

def slot_rect(s):
    x, y, w, h = SLOT[s]
    return x / GRID, y / GRID, w / GRID, h / GRID

def slot_px(s, res):
    x, y, w, h = SLOT[s]
    c = res // GRID
    return y * c, (y + h) * c, x * c, (x + w) * c

# ------------------------------------------------------------------ normalise
def normalise(arm, meshes):
    """Apply transforms, put soles on z=0, keep the armature at identity; meshes parented to it."""
    bpy.context.view_layer.update()
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    C1.select_only([arm])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in meshes:
        C1.select_only([o])
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    zmin = min(min((v.co.z for v in o.data.vertices), default=0) for o in meshes)
    if abs(zmin) > 1e-5:
        for o in meshes:
            for v in o.data.vertices:
                v.co.z -= zmin
        C1.select_only([arm]); bpy.ops.object.mode_set(mode='EDIT')
        for b in arm.data.edit_bones:
            b.head.z -= zmin; b.tail.z -= zmin
        bpy.ops.object.mode_set(mode='OBJECT')
    for o in meshes:
        o.parent = arm
        o.matrix_parent_inverse.identity()
        if not any(m.type == 'ARMATURE' for m in o.modifiers):
            o.modifiers.new('arm', 'ARMATURE')
        for m in o.modifiers:
            if m.type == 'ARMATURE':
                m.object = arm
    names = {b.name for b in arm.data.bones}
    missing = [b for b in CTS_BONES if b not in names]
    assert not missing, f'armature is not CTS: missing {missing[:8]}'

# ------------------------------------------------------------------ split into part objects
def split_parts(meshes):
    """One object per part id (merging meshes of the same part)."""
    by = {}
    for o in meshes:
        pid = A.get_face_int(o, 'part')
        for p in np.unique(pid):
            c = C1.duplicate(o, f'__p{p}_{o.name}')
            C1.keep_faces(c, lambda f, pid=pid, p=p: pid[f.index] == p)
            if len(c.data.polygons):
                by.setdefault(A.PART_NAME[int(p)], []).append(c)
            else:
                bpy.data.objects.remove(c, do_unlink=True)
        bpy.data.objects.remove(o, do_unlink=True)
    parts = {}
    for name, objs in by.items():
        for o in objs:   # keep the UV set the material samples (render-active) as 'SrcUV'; drop others
            uvs = o.data.uv_layers
            if uvs:
                want = next((A.material_uv_name(sl.material) for sl in o.material_slots if A.material_uv_name(sl.material)), None)
                keep = uvs.get(want) if want else None
                keep = keep or next((l for l in uvs if l.active_render), uvs[0])
                for l in [l.name for l in uvs if l.name != keep.name]:
                    uvs.remove(uvs[l])
                uvs[0].name = 'SrcUV'
        C1.select_only(objs)
        if len(objs) > 1:
            bpy.ops.object.join()
        o = bpy.context.object
        o.name = o.data.name = f'part_{name}'
        parts[name] = o
    return parts

# ------------------------------------------------------------------ atlas UVs per slot
def slot_uvs(parts):
    """AtlasUV per part slot. Source UV islands are reused (artist seams, few islands) and packed into the
    slot; meshes without usable source UVs are smart-projected."""
    groups = {}
    for name, o in parts.items():
        groups.setdefault(PART_SLOT[name], []).append(o)
    for slot, objs in groups.items():
        if slot == 'hair' and all(A.get_face_int(o, 'hair_set').max() > 0 for o in objs):
            for o in objs:
                hair_uv(o)
            continue
        for o in objs:
            uvs = o.data.uv_layers
            if 'AtlasUV' in uvs:
                uvs.remove(uvs['AtlasUV'])
            l = uvs.new(name='AtlasUV')
            src = uvs.get('SrcUV')
            ok = False
            if src is not None:
                arr = np.empty(len(o.data.loops) * 2, dtype=np.float32); src.data.foreach_get('uv', arr)
                a2 = arr.reshape(-1, 2)
                ok = float(np.ptp(a2[:, 0])) > 1e-3 and float(np.ptp(a2[:, 1])) > 1e-3
                if ok:
                    l.data.foreach_set('uv', arr)
            o['src_islands'] = ok
            uvs.active = l
        C1.select_only(objs)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.reveal(select=True)
        bpy.ops.mesh.select_all(action='SELECT')
        if not all(o.get('src_islands') for o in objs):
            bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0, scale_to_bounds=False)
        bpy.ops.uv.select_all(action='SELECT')
        bpy.ops.uv.average_islands_scale()
        bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=True, scale=True, merge_overlap=False,
                                margin_method='FRACTION', margin=0.006)
        bpy.ops.object.mode_set(mode='OBJECT')
        x, y, w, h = slot_rect(slot)
        for o in objs:
            l = o.data.uv_layers['AtlasUV']
            arr = np.empty(len(o.data.loops) * 2, dtype=np.float32); l.data.foreach_get('uv', arr); arr = arr.reshape(-1, 2)
            arr[:, 0] = x + np.clip(arr[:, 0], 0, 1) * w
            arr[:, 1] = y + np.clip(arr[:, 1], 0, 1) * h
            l.data.foreach_set('uv', arr.ravel())

def hair_uv(o):
    """UBC hair library: SrcUV -> AtlasUV by a fixed linear map into the half of the hair slot that holds
    the style's source texture (set 1 = T_Hair_1, set 2 = T_Hair_2), so every style fits every UBC atlas."""
    uvs = o.data.uv_layers
    if 'AtlasUV' in uvs:
        uvs.remove(uvs['AtlasUV'])
    src = uvs.get('SrcUV') or next((l for l in uvs if l.active_render), uvs[0])
    arr = np.empty(len(o.data.loops) * 2, dtype=np.float32); src.data.foreach_get('uv', arr); arr = arr.reshape(-1, 2)
    hs = A.get_face_int(o, 'hair_set')
    lset = np.zeros(len(o.data.loops), dtype=np.int32)
    for p in o.data.polygons:
        lset[p.loop_start:p.loop_start + p.loop_total] = hs[p.index]
    x, y, w, h = slot_rect('hair')
    u = np.clip(arr[:, 0], 0, 1); v = np.clip(arr[:, 1], 0, 1)
    out = np.stack([x + (lset - 1).clip(0, 1) * w / 2 + u * w / 2, y + v * h], 1).astype(np.float32)
    l = uvs.new(name='AtlasUV'); l.data.foreach_set('uv', out.ravel()); uvs.active = l
    log('HAIRUV', o.name, 'src range', np.round(arr.min(0), 3).tolist(), np.round(arr.max(0), 3).tolist())

def paste_hair_textures(alb, m0, m1, hair_hex):
    """Fill the hair slot with both UBC hair textures (box-downsampled), pre-coloured with the base's default
    hair colour; the whole slot is tint region 'hair'."""
    r0, r1, c0, c1 = slot_px('hair', RES)
    half = (c1 - c0) // 2
    tgt = C1.srgb_to_lin(C1.hexrgb(hair_hex))
    for k in (1, 2):
        im = bpy.data.images.load(str(A.UBC_HAIR / f'T_Hair_{k}_BaseColor.png'), check_existing=True)
        W, H = im.size
        px = np.empty(W * H * 4, dtype=np.float32); im.pixels.foreach_get(px); px = px.reshape(H, W, 4)[:, :, :3]
        fy, fx = H // (r1 - r0), W // half
        small = px[:fy * (r1 - r0), :fx * half].reshape(r1 - r0, fy, half, fx, 3).mean((1, 3))
        lin = C1.srgb_to_lin(np.clip(small, 0, 1))
        L = C1.luminance(lin)
        lin = L[..., None] / max(float(np.median(L)), 1e-3) * tgt
        cc = c0 + (k - 1) * half
        alb[r0:r1, cc:cc + half] = C1.lin_to_srgb(np.clip(lin, 0, 1))
    m0[r0:r1, c0:c1] = (0, 1, 0, 0)
    m1[r0:r1, c0:c1] = 0

# ------------------------------------------------------------------ bake
def bake_maps(objs, scene):
    """albedo (source base colour), region id, ao, emission -> numpy at RES."""
    bk = []
    for o in objs:
        b = C1.duplicate(o, '__bk_' + o.name)
        b.modifiers.clear(); b.hide_render = False
        bk.append(b)
    C1.select_only(bk)
    bpy.ops.object.join()
    B = bpy.context.object
    B.data.uv_layers.active = B.data.uv_layers['AtlasUV']
    res = {}
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'
    scene.render.bake.use_selected_to_active = False
    orig_mats = list(B.data.materials)
    def target_image(kind):
        im = C1.new_float_image('__bk_' + kind, RES)
        return im
    def run(kind, build_mat, btype='EMIT', samples=1):
        im = target_image(kind)
        mats = []
        for i, src in enumerate(orig_mats if orig_mats else [None]):
            m = build_mat(src)
            nt = m.node_tree
            t = nt.nodes.new('ShaderNodeTexImage'); t.image = im
            for n in nt.nodes:
                n.select = False
            t.select = True; nt.nodes.active = t
            mats.append(m)
        # assign in place: Mesh.materials.clear() resets every face's material_index to 0
        if not B.data.materials:
            B.data.materials.append(mats[0])
        for i, m in enumerate(mats):
            B.data.materials[i] = m
        if kind == 'albedo' and __import__('os').environ.get('V2_DEBUG'):
            for src, m in zip(orig_mats, mats):
                log('BAKEMAT', src.name if src else None, [(n.name, n.image.name if n.image else None) for n in m.node_tree.nodes if n.type == 'TEX_IMAGE'])
        scene.cycles.samples = samples
        C1.select_only([B])
        bpy.ops.object.bake(type=btype, use_clear=True, margin=2, uv_layer='AtlasUV')
        res[kind] = C1.image_array(im).copy()
        bpy.data.images.remove(im)
        for m in mats:
            bpy.data.materials.remove(m)
        for i, m in enumerate(orig_mats):
            B.data.materials[i] = m
    def emission_of(sock_fn):
        def build(src):
            m = bpy.data.materials.new('__b'); m.use_nodes = True
            nt = m.node_tree
            for n in list(nt.nodes):
                nt.nodes.remove(n)
            out = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission')
            nt.links.new(em.outputs[0], out.inputs['Surface'])
            sock_fn(nt, em, src)
            return m
        return build
    def base_col(nt, em, src):
        # replicate whatever feeds the source Base Color (textures, vertex-colour mixes, flat values)
        # inside the bake material; everything is evaluated in linear and converted to sRGB afterwards
        if src is None or not src.node_tree:
            c = A.material_base_color(src)
            em.inputs['Color'].default_value = (*c, 1)
            return
        sbsdf = next((n for n in src.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if sbsdf is None or not sbsdf.inputs['Base Color'].is_linked:
            c = A.material_base_color(src)
            em.inputs['Color'].default_value = (*c, 1)
            return
        mapping = {}
        def clone(node):
            if node.name in mapping:
                return mapping[node.name]
            n2 = nt.nodes.new(node.bl_idname)
            for attr in ('image', 'interpolation', 'blend_type', 'data_type', 'operation', 'layer_name', 'attribute_name',
                         'attribute_type', 'uv_map', 'clamp_factor', 'clamp_result', 'use_clamp', 'factor_mode', 'mode'):
                if hasattr(node, attr):
                    try:
                        setattr(n2, attr, getattr(node, attr))
                    except Exception:
                        pass
            if node.type == 'TEX_IMAGE':
                uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'SrcUV'
                nt.links.new(uvn.outputs['UV'], n2.inputs['Vector'])
            for i, inp in enumerate(node.inputs):
                if i < len(n2.inputs):
                    try:
                        n2.inputs[i].default_value = inp.default_value
                    except Exception:
                        pass
                    if inp.is_linked and not (node.type == 'TEX_IMAGE' and inp.name == 'Vector'):
                        l0 = inp.links[0]
                        s2 = clone(l0.from_node)
                        out_i = list(l0.from_node.outputs).index(l0.from_socket)
                        nt.links.new(s2.outputs[out_i], n2.inputs[i])
            mapping[node.name] = n2
            return n2
        l0 = sbsdf.inputs['Base Color'].links[0]
        n2 = clone(l0.from_node)
        nt.links.new(n2.outputs[list(l0.from_node.outputs).index(l0.from_socket)], em.inputs['Color'])
    def region_col(nt, em, src):
        a = nt.nodes.new('ShaderNodeAttribute'); a.attribute_type = 'GEOMETRY'; a.attribute_name = 'region'
        d = nt.nodes.new('ShaderNodeMath'); d.operation = 'DIVIDE'; d.inputs[1].default_value = 16
        nt.links.new(a.outputs['Fac'], d.inputs[0]); nt.links.new(d.outputs[0], em.inputs['Color'])
    def part_col(nt, em, src):
        a = nt.nodes.new('ShaderNodeAttribute'); a.attribute_type = 'GEOMETRY'; a.attribute_name = 'part'
        d = nt.nodes.new('ShaderNodeMath'); d.operation = 'DIVIDE'; d.inputs[1].default_value = 16
        nt.links.new(a.outputs['Fac'], d.inputs[0]); nt.links.new(d.outputs[0], em.inputs['Color'])
    def emis_col(nt, em, src):
        c = (0, 0, 0)
        if src is not None and src.node_tree:
            bsdf = next((n for n in src.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if bsdf is not None and bsdf.inputs['Emission Strength'].default_value > 0:
                c = tuple(bsdf.inputs['Emission Color'].default_value[:3])
        em.inputs['Color'].default_value = (*c, 1)
    # albedo: Cycles DIFFUSE colour pass on copies of the original materials (textures, vertex-colour
    # mixes and flat colours are evaluated by Cycles itself; result is linear)
    def orig_copy(src):
        if src is None:
            m = bpy.data.materials.new('__b'); m.use_nodes = True
            return m
        m = src.copy()
        for n in m.node_tree.nodes if m.node_tree else []:
            if n.type == 'UVMAP':
                n.uv_map = 'SrcUV'
            if n.type == 'TEX_IMAGE' and not n.inputs['Vector'].is_linked:
                u = m.node_tree.nodes.new('ShaderNodeUVMap'); u.uv_map = 'SrcUV'
                m.node_tree.links.new(u.outputs['UV'], n.inputs['Vector'])
        return m
    scene.render.bake.use_pass_direct = False; scene.render.bake.use_pass_indirect = False; scene.render.bake.use_pass_color = True
    run('albedo', orig_copy, btype='DIFFUSE')
    run('region', emission_of(region_col))
    run('part', emission_of(part_col))
    run('emis', emission_of(emis_col))
    # AO (contact shadow for the painted look); other base objects visible to rays
    def ao_mat(src):
        m = bpy.data.materials.new('__ao'); m.use_nodes = True
        return m
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            o.hide_render = o is not B
    run('ao', ao_mat, btype='AO', samples=16)
    bpy.data.objects.remove(B, do_unlink=True)
    for k in ('albedo', 'emis'):
        res[k][:, :, :3] = C1.lin_to_srgb(res[k][:, :, :3])   # bakes are linear; textures ship sRGB
    reg = np.rint(res['region'][:, :, 0] * 16).astype(np.int32)
    cov = res['part'][:, :, 0] > 0.01
    return res, reg, cov

def painted(albedo, ao, cov):
    """Painted-toon albedo: soft AO, gentle saturation lift, warm/cool split (shadows cooler)."""
    a = np.clip(albedo[:, :, :3], 0, 1)
    lin = C1.srgb_to_lin(a)
    o = np.clip(ao[:, :, :1], 0, 1)
    shade = 0.72 + 0.28 * o
    lin = lin * shade
    L = C1.luminance(lin)[..., None]
    lin = L + (lin - L) * 1.12                       # saturation +12 %
    cool = np.array([0.94, 0.97, 1.06]); warm = np.array([1.03, 1.0, 0.96])
    lin = lin * (cool * (1 - o) + warm * o)
    out = C1.lin_to_srgb(np.clip(lin, 0, 1))
    return out

def masks_from_region(reg, cov):
    m0 = np.zeros(reg.shape + (4,), dtype=np.float32)
    m1 = np.zeros(reg.shape + (4,), dtype=np.float32)
    for name, (k, ch) in MASK_CHANNEL.items():
        sel = (reg == REGION_ID[name]) & cov
        (m0 if k == 0 else m1)[sel, ch] = 1.0
    return m0, m1

# ------------------------------------------------------------------ tiers
def tiers_for(parts, reserve=None):
    """Per-part triangle budgets per tier. `reserve` = {part: tris} raises a part's share to the largest
    swappable alternative (hair library) or adds an optional part (facial hair), so any combination fits."""
    full = {n: C1.tri_count(o) for n, o in parts.items()}
    for n, t in (reserve or {}).items():
        full[n] = max(full.get(n, 0), t)
    total = sum(full.values())
    out = {}
    for tier in TIERS:
        cap = CAPS[tier] * 0.94
        if total <= cap:
            out[tier] = dict(full)
            continue
        f = cap / total
        out[tier] = {n: max(24, int(t * f)) for n, t in full.items()}
    return full, out

def make_tier(o, target, tier, arm):
    t = C1.duplicate(o, f'{o.name}_{tier}')
    t.parent = arm
    if C1.tri_count(t) > target:
        C1.decimate_to(t, target, symmetry=False)
    for p in t.data.polygons:
        p.use_smooth = True
    C1.limit_weights(t, [b for b in CTS_BONES], MAX_INFL[tier])
    for l in [l.name for l in t.data.uv_layers if l.name != 'AtlasUV']:
        t.data.uv_layers.remove(t.data.uv_layers[l])
    return t

# ------------------------------------------------------------------ export helpers
FBX = dict(use_selection=True, object_types={'MESH', 'ARMATURE', 'EMPTY'}, add_leaf_bones=False, use_armature_deform_only=False,
           bake_anim=False, axis_forward='-Z', axis_up='Y', global_scale=1, apply_unit_scale=True,
           apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=False, path_mode='STRIP', use_tspace=False)

def export(objs, arm, path, extra=()):
    C1.select_only(list(objs) + [arm] + list(extra), arm)
    bpy.ops.export_scene.fbx(filepath=str(path), **FBX)

def make_material(name, atlas_paths):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes['Principled BSDF']
    uv = nt.nodes.new('ShaderNodeUVMap'); uv.uv_map = 'AtlasUV'
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(str(atlas_paths['albedo']), check_existing=True)
    nt.links.new(uv.outputs['UV'], t.inputs['Vector']); nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.85
    if atlas_paths.get('emission'):
        e = nt.nodes.new('ShaderNodeTexImage'); e.image = bpy.data.images.load(str(atlas_paths['emission']), check_existing=True)
        nt.links.new(uv.outputs['UV'], e.inputs['Vector']); nt.links.new(e.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = 1.5
    return m

# ------------------------------------------------------------------ main per base
def build_base(base_id):
    t0 = time.time()
    spec = SPEC.BASES[base_id]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    arm, meshes = A.ADAPTERS[spec['adapter']](base_id)
    extras = list(A.EXTRAS) if spec.get('hair_library') else []
    arm.name = 'CTS'; arm.data.name = 'CTS'
    normalise(arm, meshes + [e['obj'] for e in extras])
    parts = split_parts(meshes)
    slot_uvs(parts)
    res, reg, cov = bake_maps(list(parts.values()), scene)
    if spec.get('split_dark_from_skin'):
        # UBC bodies: underwear is painted into the skin texture/vertex colours -> reclassify per texel
        a = res['albedo'][:, :, :3]
        lum = C1.luminance(C1.srgb_to_lin(a)); sat = a.max(2) - a.min(2)
        pid = np.rint(res['part'][:, :, 0] * 16).astype(np.int32)
        dark = (reg == REGION_ID['skin']) & (lum < 0.035) & (sat < 0.08) & cov
        reg[dark & (pid == A.PART_ID['top'])] = REGION_ID['top']
        reg[dark & (pid != A.PART_ID['top'])] = REGION_ID['bottom']
    alb = painted(res['albedo'], res['ao'], cov)
    alb = C1.dilate(alb, cov, 8)
    m0, m1 = masks_from_region(reg, cov)
    emis = np.clip(res['emis'][:, :, :3], 0, 1)
    paint_acc_slot(alb, emis, m1)
    look = spec.get('look', {})
    if spec.get('hair_library'):
        paste_hair_textures(alb, m0, m1, HAIR_COLORS[look.get('hair', 'dark_brown')])
    out = EXPORT / 'bases' / base_id
    out.mkdir(parents=True, exist_ok=True)
    paths = {'albedo': out / f'{base_id}_Albedo.png', 'mask0': out / f'{base_id}_TintMask0.png', 'mask1': out / f'{base_id}_TintMask1.png'}
    C1.write_png(paths['albedo'], alb)
    C1.write_png(paths['mask0'], m0)
    C1.write_png(paths['mask1'], m1)
    if emis.max() > 0.02:
        paths['emission'] = out / f'{base_id}_Emission.png'
        C1.write_png(paths['emission'], emis)
    # region default colours (mean albedo inside each region) for the tint shader
    defaults = {}
    for name in TINT_REGIONS:
        sel = (reg == REGION_ID[name]) & cov
        if sel.any():
            defaults[name] = '#%02x%02x%02x' % tuple(int(x * 255) for x in np.median(alb[sel], axis=0))
    # measurements + sockets
    meas = G.measure(arm, list(parts.values()))
    sockets = G.socket_points(meas)
    empties = []
    for sname, p in sockets.items():
        e = bpy.data.objects.new(f'socket_{sname}', None); e.empty_display_size = 0.04
        scene.collection.objects.link(e)
        bone = SOCKETS[sname]['bone']
        e.parent = arm; e.parent_type = 'BONE'; e.parent_bone = bone
        bpy.context.view_layer.update()
        e.matrix_world = Matrix.Translation(Vector(p))
        empties.append(e)
    # tiers + material
    mat = make_material(f'{base_id}_Mat', paths)
    reserve = {}
    for e in extras:
        reserve[e['kind']] = max(reserve.get(e['kind'], 0), C1.tri_count(e['obj']))
    full, budgets = tiers_for(parts, reserve)
    tier_objs = {t: [] for t in TIERS}
    for tier in TIERS:
        for n, o in parts.items():
            t = make_tier(o, budgets[tier][n], tier, arm)
            t.data.materials.clear(); t.data.materials.append(mat)
            tier_objs[tier].append(t)
    info = {}
    files = {}
    # merged look (all default parts) per tier, named for Unity's LOD group
    merged = {}
    TN = {'hero': 'Hero', 'lod0': 'LOD0', 'lod1': 'LOD1', 'lod2': 'LOD2'}
    for tier in TIERS:
        cs = [C1.duplicate(o, '__m') for o in tier_objs[tier]]
        C1.select_only(cs)
        if len(cs) > 1:
            bpy.ops.object.join()
        mo = bpy.context.object
        mo.name = mo.data.name = f'{base_id}_{TN[tier]}'
        for md in list(mo.modifiers):
            mo.modifiers.remove(md)
        mo.modifiers.new('arm', 'ARMATURE').object = arm
        mo.parent = arm
        merged[tier] = mo
        info[tier] = {'triangles': C1.tri_count(mo), 'vertices': len(mo.data.vertices), 'max_influences': C1.max_influences(mo)}
        assert info[tier]['triangles'] <= CAPS[tier], (base_id, tier, info[tier])
    export([merged[t] for t in ('lod0', 'lod1', 'lod2')], arm, out / f'{base_id}.fbx', empties)
    export([merged['hero']], arm, out / f'{base_id}_Hero.fbx', empties)
    files['crowd_fbx'] = f'bases/{base_id}/{base_id}.fbx'; files['hero_fbx'] = f'bases/{base_id}/{base_id}_Hero.fbx'
    # swappable parts (all tiers in one FBX per part)
    part_files = {}
    for i, n in enumerate(parts):
        objs = []
        for tier in TIERS:
            o = tier_objs[tier][i]
            o.name = o.data.name = f'{base_id}__{n}_{TN[tier]}'
            objs.append(o)
        p = out / 'parts' / f'{base_id}__{n}.fbx'; p.parent.mkdir(exist_ok=True)
        export(objs, arm, p)
        part_files[n] = {'fbx': f'bases/{base_id}/parts/{p.name}', 'slot': PART_SLOT[n],
                         'triangles': {t: C1.tri_count(tier_objs[t][i]) for t in TIERS}}
    # hair library (UBC): every extra style / facial hair as its own swappable part FBX
    library = {}
    if spec.get('hair_library'):
        dflt = next(h for h in A.UBC_HAIR_DEFAULT[base_id] if not h.startswith('Eyebrows'))
        library = {'hair': {dflt: {'part': 'hair', 'fbx': part_files['hair']['fbx'], 'default': True,
                                   'fit': 'native' if A.UBC_HAIR_FIT[dflt] == base_id else 'cross-fitted'}}, 'facial': {}}
        for e in extras:
            o = e['obj']
            for md in list(o.modifiers):
                if md.type != 'ARMATURE':
                    o.modifiers.remove(md)
            uvs = o.data.uv_layers
            keep = next((l for l in uvs if l.active_render), uvs[0])
            for ln in [l.name for l in uvs if l.name != keep.name]:
                uvs.remove(uvs[ln])
            uvs[0].name = 'SrcUV'
            hair_uv(o)
            key = f"{e['kind']}_{e['style']}"
            objs = []
            for tier in TIERS:
                t = make_tier(o, budgets[tier][e['kind']], tier, arm)
                t.data.materials.clear(); t.data.materials.append(mat)
                t.name = t.data.name = f'{base_id}__{key}_{TN[tier]}'
                objs.append(t)
            p = out / 'parts' / f'{base_id}__{key}.fbx'
            export(objs, arm, p)
            part_files[key] = {'fbx': f'bases/{base_id}/parts/{p.name}', 'slot': 'hair', 'kind': e['kind'], 'style': e['style'],
                               'triangles': {t: C1.tri_count(x) for t, x in zip(TIERS, objs)}}
            library[e['kind']][e['style']] = {'part': key, 'fbx': part_files[key]['fbx'], 'default': False,
                                              'fit': 'native' if A.UBC_HAIR_FIT[e['style']] == base_id else 'cross-fitted'}
            bpy.data.objects.remove(o, do_unlink=True)
    # fitted starter accessories (rigid to their socket bone, palette-coloured in the shared acc slot)
    acc_files = {}
    for name in G.STARTER_ACCESSORIES:
        bm, sock, flags = G.build_accessory(name, meas)
        me = bpy.data.meshes.new(f'{base_id}__acc_{name}')
        bm.to_mesh(me); bm.free()
        ao = bpy.data.objects.new(me.name, me); scene.collection.objects.link(ao)
        ao.parent = arm
        acc_uv(ao, name)
        bone = SOCKETS[sock]['bone']
        vg = ao.vertex_groups.new(name=bone); vg.add(list(range(len(me.vertices))), 1.0, 'REPLACE')
        ao.modifiers.new('arm', 'ARMATURE').object = arm
        ao.data.materials.clear(); ao.data.materials.append(acc_material())
        p = out / 'accessories' / f'{base_id}__{name}.fbx'; p.parent.mkdir(exist_ok=True)
        export([ao], arm, p)
        acc_files[name] = {'fbx': f'bases/{base_id}/accessories/{p.name}', 'socket': sock, 'bone': bone, 'flags': flags,
                           'triangles': C1.tri_count(ao)}
    socket_json = {}
    for e in empties:
        b = arm.data.bones[e.parent_bone]
        local = (arm.matrix_world @ b.matrix_local).inverted() @ e.matrix_world
        socket_json[e.name[7:]] = {'bone': e.parent_bone, 'humanoid': SOCKETS[e.name[7:]]['humanoid'],
                                   'position_character_m': [round(x, 4) for x in e.matrix_world.translation],
                                   'offset_in_bone_space_m': [round(x, 4) for x in local.translation]}
    meta = {'id': base_id, 'label': spec['label'], 'gender': spec['gender'], 'source': spec['source'],
            'licence': spec['licence'], 'archetypes': spec.get('archetypes', []), 'tiers': info, 'files': files,
            'textures': {k: f'bases/{base_id}/{Path(v).name}' for k, v in paths.items()},
            'tint_defaults': defaults, 'look_defaults': look, 'hair_library': library, 'parts': part_files, 'accessories': acc_files, 'sockets': socket_json,
            'measurements': {k: (round(v, 4) if isinstance(v, float) else [round(x, 4) for x in v]) for k, v in meas.items()},
            'height_m': round(max(v.co.z for v in merged['lod0'].data.vertices), 3)}
    if spec.get('source_component_exclusions'):
        meta['source_component_exclusions'] = spec['source_component_exclusions']
    (out / f'{base_id}.json').write_text(json.dumps(meta, indent=1))
    EVID.joinpath('bases').mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(EVID / 'bases' / f'{base_id}.blend'), compress=True)
    log('BASE', base_id, json.dumps(info), round(time.time() - t0, 1), 's')

# ------------------------------------------------------------------ accessory palette (shared acc slot)
# acc slot (256x256 at 1024): lower half = 4x2 colour swatches, upper half = two 16x16 pixel faces
PIXEL_FACES = {
    'mask': ['................', '................', '...##......##...', '...##......##...', '...##......##...',
             '................', '................', '................', '................', '..#..........#..',
             '...#........#...', '....########....', '................', '................', '................', '................'],
    'tv':   ['................', '................', '..####....####..', '..#..#....#..#..', '..####....####..',
             '................', '................', '.......##.......', '................', '................',
             '...##########...', '...#........#...', '...##########...', '................', '................', '................'],
}

def acc_rect(kind, idx=0):
    """UV rect (u0, v0, u1, v1) of a swatch (kind='swatch', idx 0..7) or pixel face (kind='face', idx 0/1)."""
    x0, y0, w, h = slot_rect('acc')
    if kind == 'swatch':
        cw, ch = w / 4, h / 4
        u0 = x0 + (idx % 4) * cw; v0 = y0 + (idx // 4) * ch
        return u0, v0, u0 + cw, v0 + ch
    u0 = x0 + idx * w / 2; v0 = y0 + h / 2
    return u0, v0, u0 + w / 2, v0 + h / 2

def paint_acc_slot(alb, emis, m1):
    for i, name in enumerate(G.ACC_MATS):
        u0, v0, u1, v1 = acc_rect('swatch', i)
        r0, r1, c0, c1 = int(v0 * RES), int(v1 * RES), int(u0 * RES), int(u1 * RES)
        col = C1.hexrgb(ACC_COLORS[name])
        alb[r0:r1, c0:c1] = col
        if name in ('acc_glow', 'acc_screen'):
            emis[r0:r1, c0:c1] = col
        if name == 'acc_accent':
            m1[r0:r1, c0:c1, 1] = 1.0      # accessory accent colour is tintable
    for k, (fname, rows) in enumerate(PIXEL_FACES.items()):
        u0, v0, u1, v1 = acc_rect('face', k)
        r0, r1, c0, c1 = int(v0 * RES), int(v1 * RES), int(u0 * RES), int(u1 * RES)
        n = r1 - r0
        dark = C1.hexrgb('#0e1116'); lit = C1.hexrgb(ACC_COLORS['acc_screen'])
        img = np.zeros((n, c1 - c0, 3), dtype=np.float32); img[:] = dark
        e = np.zeros_like(img)
        cell = n // 16
        for ry, row in enumerate(rows):
            for cx_, ch_ in enumerate(row):
                if ch_ == '#':
                    y = n - (ry + 1) * cell
                    img[y:y + cell - 1, cx_ * cell:(cx_ + 1) * cell - 1] = lit
                    e[y:y + cell - 1, cx_ * cell:(cx_ + 1) * cell - 1] = lit
        alb[r0:r1, c0:c1] = img
        emis[r0:r1, c0:c1] = e

ACC_COLORS = {'acc_dark': '#1d1f24', 'acc_light': '#eef0f2', 'acc_metal': '#b9bec6', 'acc_gold': '#e0a72e',
              'acc_glow': '#ff3b30', 'acc_screen': '#38c6ff', 'acc_accent': '#8a4dff', 'acc_lens': '#0e1116'}

def acc_uv(o, name=None):
    """Faces -> palette swatch centres; screen faces of the face mask / TV head get the pixel-face image
    by planar projection (front view)."""
    me = o.data
    l = me.uv_layers.new(name='AtlasUV')
    screen = G.AM['acc_screen']
    scr = [p for p in me.polygons if p.material_index == screen and name in ('pixel_face_mask', 'tv_head')]
    if scr:
        xs = [me.vertices[v].co.x for p in scr for v in p.vertices]; zs = [me.vertices[v].co.z for p in scr for v in p.vertices]
        x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
        u0, v0, u1, v1 = acc_rect('face', 0 if name == 'pixel_face_mask' else 1)
    for p in me.polygons:
        if p in scr or (scr and p.material_index == screen):
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                fu = (co.x - x0) / max(x1 - x0, 1e-6); fv = (co.z - z0) / max(z1 - z0, 1e-6)
                l.data[li].uv = (u0 + (0.01 + 0.98 * fu) * (u1 - u0), v0 + (0.01 + 0.98 * fv) * (v1 - v0))
            continue
        u0_, v0_, u1_, v1_ = acc_rect('swatch', p.material_index)
        for li in p.loop_indices:
            l.data[li].uv = ((u0_ + u1_) / 2, (v0_ + v1_) / 2)

_acc_mat = None
def acc_material():
    global _acc_mat
    try:
        _acc_mat.name
    except (ReferenceError, AttributeError):
        _acc_mat = bpy.data.materials.new('acc_palette')
    return _acc_mat

if __name__ == '__main__':
    ids = sys.argv[sys.argv.index('--') + 1:]
    for b in ids:
        build_base(b)
