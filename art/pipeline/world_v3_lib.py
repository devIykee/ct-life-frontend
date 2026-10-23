"""World v3 shared Blender helpers (Sketchfab semi-real kit -> Seeker-ready FBX).

Conventions (match env_process.py so the island builder can swap kits):
  * metres, Blender Z up, model FRONT faces Blender -Y (Unity +Z after the -Z-forward FBX export), origin at ground centre
  * LODs are sibling objects <id>_LOD0/_LOD1/_LOD2 in one FBX (same as art/processed/world)
  * one atlas set per asset or family: <set>_BaseColor.png (RGBA), <set>_Normal.png (OpenGL +Y), <set>_MaskMap.png
    (R metallic, G occlusion, B 0, A smoothness -> URP Lit: assign to both Metallic Map and Occlusion Map)
"""
import bpy, bmesh, os, math, json
import numpy as np
from mathutils import Vector, Matrix


def log(*a):
    print('[wv3]', *a, flush=True)


# ------------------------------------------------------------------ scene
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def world_bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


def join_copy(objs, name):
    """Duplicate meshes (keeping the source scene intact), apply world transforms, join into one object."""
    copies = []
    for o in objs:
        if o.type != 'MESH':
            continue
        c = o.copy()
        c.data = o.data.copy()
        c.parent = None
        c.matrix_world = o.matrix_world.copy()
        bpy.context.scene.collection.objects.link(c)
        copies.append(c)
    if not copies:
        return None
    bpy.ops.object.select_all(action='DESELECT')
    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(copies) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = o.data.name = name
    # keep only the first UV map
    while len(o.data.uv_layers) > 1:
        o.data.uv_layers.remove(o.data.uv_layers[-1])
    for a in list(o.data.color_attributes):
        o.data.color_attributes.remove(a)
    return o


def to_ground_origin(o, yaw_deg=0.0):
    """Rotate about Z by yaw (so the facade faces -Y), then put the origin at the footprint centre, ground level."""
    if yaw_deg:
        o.data.transform(Matrix.Rotation(math.radians(yaw_deg), 4, 'Z'))
    vs = np.empty(len(o.data.vertices) * 3, np.float32)
    o.data.vertices.foreach_get('co', vs)
    vs = vs.reshape(-1, 3)
    mn, mx = vs.min(0), vs.max(0)
    off = Vector(((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, mn[2]))
    o.data.transform(Matrix.Translation(-off))
    o.location = (0, 0, 0)
    o.data.update()
    return [float(mx[0] - mn[0]), float(mx[1] - mn[1]), float(mx[2] - mn[2])]


def clean(o, merge=0.0004):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-5)
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


# ------------------------------------------------------------------ material sampling
def _img_array(img, size):
    """Image -> float32 HxWx4 (row 0 = bottom), resized (nearest/box) to size x size."""
    if img is None or img.size[0] == 0:
        return None
    img.reload() if img.packed_file is None and img.filepath and not img.has_data else None
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    a = px.reshape(h, w, 4)
    if (w, h) != (size, size):
        ys = (np.arange(size) * h / size).astype(int)
        xs = (np.arange(size) * w / size).astype(int)
        a = a[ys][:, xs]
    return a


def _linked_image(sock, depth=6):
    """Follow a socket upstream to the first Image Texture; return (image, channel-hint)."""
    if not sock.is_linked:
        return None, None
    n = sock.links[0].from_node
    out = sock.links[0].from_socket.name
    hint = None
    for _ in range(depth):
        if n.type == 'TEX_IMAGE':
            return n.image, hint or out
        if n.type in ('SEPARATE_COLOR', 'SEPRGB', 'SEPARATE_RGB'):
            hint = out
        nxt = None
        for i in n.inputs:
            if i.is_linked and i.type in ('RGBA', 'VECTOR', 'VALUE'):
                if n.type == 'NORMAL_MAP' and i.name != 'Color':
                    continue
                nxt = i
                break
        if nxt is None:
            return None, None
        out = nxt.links[0].from_socket.name
        n = nxt.links[0].from_node
    return None, None


def material_layers(mat, size):
    """-> dict base(HxWx4 sRGB-encoded values as stored), normal(HxWx3 0..1), mask(HxWx4 R metal G ao B 0 A smooth), blend."""
    H = size
    base = np.ones((H, H, 4), np.float32) * 0.8
    base[..., 3] = 1
    nrm = np.zeros((H, H, 3), np.float32)
    nrm[..., :] = (0.5, 0.5, 1.0)
    mask = np.zeros((H, H, 4), np.float32)
    mask[..., 1] = 1.0
    mask[..., 3] = 0.5
    blend = False
    if mat is None or not mat.node_tree:
        return dict(base=base, normal=nrm, mask=mask, blend=False)
    bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        return dict(base=base, normal=nrm, mask=mask, blend=False)
    img, _ = _linked_image(bsdf.inputs['Base Color'])
    if img:
        a = _img_array(img, H)
        if a is not None:
            base = a.copy()
            if img.colorspace_settings.name.lower().startswith('linear') or img.is_float:
                base[..., :3] = np.where(base[..., :3] <= 0.0031308, base[..., :3] * 12.92, 1.055 * np.power(np.clip(base[..., :3], 0, None), 1 / 2.4) - 0.055)
    else:
        c = bsdf.inputs['Base Color'].default_value
        lin = np.array(c[:3])
        srgb = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1 / 2.4) - 0.055)
        base[..., :3] = srgb
    # alpha
    if bsdf.inputs['Alpha'].is_linked:
        blend = True
    elif bsdf.inputs['Alpha'].default_value < 0.99:
        blend = True
        base[..., 3] = bsdf.inputs['Alpha'].default_value
    if not blend:
        base[..., 3] = 1.0
    # normal
    if bsdf.inputs['Normal'].is_linked:
        img, _ = _linked_image(bsdf.inputs['Normal'])
        a = _img_array(img, H) if img else None
        if a is not None:
            nrm = a[..., :3].copy()
    # metal / rough (glTF: metallicRoughness image, B = metal, G = rough)
    met = bsdf.inputs['Metallic']
    rough = bsdf.inputs['Roughness']
    img, ch = _linked_image(rough)
    if img:
        a = _img_array(img, H)
        if a is not None:
            ci = {'Red': 0, 'R': 0, 'Green': 1, 'G': 1, 'Blue': 2, 'B': 2}.get(ch, 1)
            mask[..., 3] = 1.0 - a[..., ci]
    else:
        mask[..., 3] = 1.0 - rough.default_value
    img, ch = _linked_image(met)
    if img:
        a = _img_array(img, H)
        if a is not None:
            ci = {'Red': 0, 'R': 0, 'Green': 1, 'G': 1, 'Blue': 2, 'B': 2}.get(ch, 2)
            mask[..., 0] = a[..., ci]
    else:
        mask[..., 0] = met.default_value
    # occlusion: glTF importer puts it in a 'glTF Material Output' / 'glTF Settings' group
    for n in mat.node_tree.nodes:
        if n.type == 'GROUP' and n.node_tree and 'gltf' in n.node_tree.name.lower():
            s = n.inputs.get('Occlusion')
            if s and s.is_linked:
                img, ch = _linked_image(s)
                a = _img_array(img, H) if img else None
                if a is not None:
                    mask[..., 1] = a[..., 0]
    return dict(base=base, normal=nrm, mask=mask, blend=blend)


def save_png(arr, path, channels=4):
    """arr HxWxC float (row 0 = bottom) -> PNG via Blender image (no colour management on save)."""
    h, w = arr.shape[:2]
    name = os.path.basename(path)
    img = bpy.data.images.new(name, w, h, alpha=(channels == 4))
    img.colorspace_settings.name = 'Non-Color'   # set BEFORE writing pixels (changing it later resets the buffer)
    full = np.ones((h, w, 4), np.float32)
    full[..., :arr.shape[2]] = arr[..., :min(4, arr.shape[2])]
    img.pixels.foreach_set(np.clip(full, 0, 1).ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


# ------------------------------------------------------------------ atlas
def _shelf(items, size):
    items = sorted(items, key=lambda t: -t[0])
    out, x, y, row = {}, 0, 0, 0
    for c, m in items:
        if x + c > size:
            x, y = 0, y + row
            row = 0
        if y + c > size:
            return None
        out[m] = (x, y, c)
        x += c
        row = max(row, c)
    return out


def atlas_layout(mats_weight, size=2048, pad=8):
    """Square power-of-two cells sized by surface-area share, then grown/shrunk to fill the atlas. -> {mat: (x, y, cell)}."""
    total = sum(mats_weight.values()) or 1
    items = []
    for m, wgt in mats_weight.items():
        share = wgt / total
        cell = 1024 if share > 0.35 else 512 if share > 0.06 else 256 if share > 0.01 else 128
        items.append([cell, m])
    while _shelf(items, size) is None:                 # shrink the biggest until it fits
        big = max(c for c, _ in items)
        for it in items:
            if it[0] == big:
                it[0] = max(32, it[0] // 2)
    while True:                                        # grow the smallest-share-biggest cells while it still fits
        grown = False
        for it in sorted(items, key=lambda t: -mats_weight[t[1]]):
            if it[0] >= size // (1 if len(items) == 1 else 2):
                continue
            it[0] *= 2
            if _shelf(items, size) is None:
                it[0] //= 2
            else:
                grown = True
        if not grown:
            break
    return _shelf(items, size)


def build_atlas(o, set_name, tex_dir, size=2048, pad=6, extra_objs=()):
    """Bake every material of o (and extra_objs that share the same materials) into one atlas set; remap UVs.
    Opaque and alpha-blended materials end up in two materials: M_<set> and M_<set>_glass."""
    objs = [o] + list(extra_objs)
    mats = []
    weight = {}
    for ob in objs:
        for p in ob.data.polygons:
            m = ob.material_slots[p.material_index].material if ob.material_slots else None
            k = m.name if m else '_none'
            if k not in weight:
                weight[k] = 0
                mats.append(m)
            weight[k] += p.area
    lay = atlas_layout(weight, size)
    B = np.zeros((size, size, 4), np.float32)
    N = np.zeros((size, size, 3), np.float32)
    N[..., :] = (0.5, 0.5, 1.0)
    M = np.zeros((size, size, 4), np.float32)
    M[..., 1] = 1
    M[..., 3] = 0.5
    blend = {}
    for m in mats:
        k = m.name if m else '_none'
        x, y, c = lay[k]
        L = material_layers(m, c)
        B[y:y + c, x:x + c] = L['base']
        N[y:y + c, x:x + c] = L['normal']
        M[y:y + c, x:x + c] = L['mask']
        blend[k] = L['blend']
    os.makedirs(tex_dir, exist_ok=True)
    paths = {k: os.path.join(tex_dir, f'{set_name}_{k}.png') for k in ('BaseColor', 'Normal', 'MaskMap')}
    save_png(B, paths['BaseColor'])
    save_png(N, paths['Normal'], 3)
    save_png(M, paths['MaskMap'])
    mat_op = make_material(f'M_{set_name}', paths, blend=False)
    mat_gl = make_material(f'M_{set_name}_glass', paths, blend=True) if any(blend.values()) else None
    # UV remap
    for ob in objs:
        me = ob.data
        uvl = me.uv_layers.active or me.uv_layers.new()
        uv = np.empty(len(me.loops) * 2, np.float32)
        uvl.data.foreach_get('uv', uv)
        uv = uv.reshape(-1, 2)
        names = [ob.material_slots[i].material.name if ob.material_slots[i].material else '_none' for i in range(len(ob.material_slots))] or ['_none']
        new_idx = np.zeros(len(me.polygons), np.int32)
        for p in me.polygons:
            k = names[p.material_index] if p.material_index < len(names) else '_none'
            x, y, c = lay[k]
            sl = slice(p.loop_start, p.loop_start + p.loop_total)
            u = uv[sl]
            # bring the poly into the 0..1 tile it mostly lives in
            off = np.floor(u.mean(0))
            u = np.clip(u - off, 0.0, 1.0)
            inset = pad / c
            u = inset + u * (1 - 2 * inset)
            uv[sl, 0] = (x + u[:, 0] * c) / size
            uv[sl, 1] = (y + u[:, 1] * c) / size
            new_idx[p.index] = 1 if (blend.get(k) and mat_gl) else 0
        uvl.data.foreach_set('uv', uv.ravel())
        ob.data.materials.clear()
        ob.data.materials.append(mat_op)
        if mat_gl:
            ob.data.materials.append(mat_gl)
        me.polygons.foreach_set('material_index', new_idx)
        me.update()
    return dict(materials=[mat_op.name] + ([mat_gl.name] if mat_gl else []), textures=[os.path.basename(p) for p in paths.values()], layout={k: v for k, v in lay.items()})


def make_material(name, paths, blend=False):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    b = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(b.outputs[0], out.inputs[0])

    def tex(p, cs):
        t = nt.nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.load(p, check_existing=True)
        t.image.colorspace_settings.name = cs
        return t
    tb = tex(paths['BaseColor'], 'sRGB')
    nt.links.new(tb.outputs['Color'], b.inputs['Base Color'])
    if blend:
        nt.links.new(tb.outputs['Alpha'], b.inputs['Alpha'])
        m.surface_render_method = 'BLENDED' if hasattr(m, 'surface_render_method') else None
    tm = tex(paths['MaskMap'], 'Non-Color')
    sep = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(tm.outputs['Color'], sep.inputs[0])
    nt.links.new(sep.outputs['Red'], b.inputs['Metallic'])
    inv = nt.nodes.new('ShaderNodeMath')
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    nt.links.new(tm.outputs['Alpha'], inv.inputs[1])
    nt.links.new(inv.outputs[0], b.inputs['Roughness'])
    # AO multiplies base colour for preview renders (Unity reads it from MaskMap G)
    tn = tex(paths['Normal'], 'Non-Color')
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nt.links.new(tn.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    return m


# ------------------------------------------------------------------ LODs
def decimate_copy(o, target_tris, name, planar_first=True):
    d = o.copy()
    d.data = o.data.copy()
    d.name = d.data.name = name
    bpy.context.scene.collection.objects.link(d)
    cur = tris(d)
    if cur > target_tris:
        bpy.ops.object.select_all(action='DESELECT')
        d.select_set(True)
        bpy.context.view_layer.objects.active = d
        if planar_first:
            mod = d.modifiers.new('pl', 'DECIMATE')
            mod.decimate_type = 'DISSOLVE'
            mod.angle_limit = math.radians(1.0)
            mod.delimit = {'UV', 'MATERIAL', 'SEAM'}
            bpy.ops.object.modifier_apply(modifier=mod.name)
            bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.mesh.quads_convert_to_tris()
            bpy.ops.object.mode_set(mode='OBJECT')
        cur = tris(d)
        if cur > target_tris:
            mod = d.modifiers.new('dc', 'DECIMATE')
            mod.decimate_type = 'COLLAPSE'
            mod.ratio = max(0.01, target_tris / cur)
            mod.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=mod.name)
    return d


def cull_small(o, min_size):
    """Delete loose parts whose bounding box diagonal is below min_size (LOD1/LOD2 clutter removal)."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    seen = set()
    kill = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack = [v]
        part = []
        seen.add(v.index)
        while stack:
            x = stack.pop()
            part.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen:
                    seen.add(y.index)
                    stack.append(y)
        cs = np.array([p.co[:] for p in part])
        if np.linalg.norm(cs.max(0) - cs.min(0)) < min_size:
            kill.extend(part)
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


# ------------------------------------------------------------------ export
def export_fbx(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = [o for o in objs if o.type == 'MESH'][0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        path_mode='RELATIVE', embed_textures=False, bake_anim=False)


def write_manifest(path, entries):
    old = json.load(open(path)) if os.path.exists(path) else {}
    old.update(entries)
    json.dump(old, open(path, 'w'), indent=1)


def _fill_black(img, iters=64):
    """Bake misses come out black: grow valid texels into them (simple iterative dilation)."""
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    a = a.reshape(h, w, 4)
    valid = a[..., :3].sum(2) > 0.01
    if valid.all() or not valid.any():
        return
    for _ in range(iters):
        if valid.all():
            break
        acc = np.zeros_like(a[..., :3])
        cnt = np.zeros((h, w), np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sv = np.roll(valid, (dy, dx), (0, 1))
            sa = np.roll(a[..., :3], (dy, dx), (0, 1))
            acc += sa * sv[..., None]
            cnt += sv
        grow = (~valid) & (cnt > 0)
        a[grow, :3] = acc[grow] / cnt[grow][:, None]
        valid = valid | grow
    img.pixels.foreach_set(a.ravel())


def _nonmetal_copy(o):
    """Bake helper: Cycles' diffuse colour pass is base*(1-metallic), so metal parts bake black. Give the bake source
    copies of its materials with Metallic forced to 0."""
    for i, slot in enumerate(o.material_slots):
        m = slot.material
        if not m:
            continue
        c = m.copy()
        b = next((n for n in c.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if b:
            for l in list(b.inputs['Metallic'].links):
                c.node_tree.links.remove(l)
            b.inputs['Metallic'].default_value = 0.0
        o.material_slots[i].material = c


# ------------------------------------------------------------------ LOD2 box with a baked facade
def box_lod(src, name, tex_dir, set_name, px=512, shrink=0.0):
    """5-face box (no bottom) at src bounds; Cycles bakes src's diffuse colour onto it. -> object (10 tris)."""
    import bmesh as _bm
    vs = np.array([v.co[:] for v in src.data.vertices])
    mn, mx = vs.min(0), vs.max(0)
    me = bpy.data.meshes.new(name)
    bm = _bm.new()
    _bm.ops.create_cube(bm, size=1.0)
    for f in list(bm.faces):
        if f.normal.z < -0.9:
            bm.faces.remove(f)
    for v in bm.verts:
        v.co = Vector([mn[i] + (v.co[i] + 0.5) * (mx[i] - mn[i]) for i in range(3)])
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.02, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    img = bpy.data.images.new(f'{set_name}_lod2_BaseColor', px, px, alpha=False)
    m = bpy.data.materials.new(f'M_{set_name}_lod2')
    m.use_nodes = True
    nt = m.node_tree
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = img
    nt.nodes.active = t
    b = nt.nodes.get('Principled BSDF')
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.85
    o.data.materials.append(m)
    sc = bpy.context.scene
    eng = sc.render.engine
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = 4
    sc.cycles.device = 'CPU'
    sc.render.bake.use_selected_to_active = True
    sc.render.bake.cage_extrusion = 0.05
    sc.render.bake.max_ray_distance = max(mx - mn) * 0.6
    sc.render.bake.use_pass_direct = False
    sc.render.bake.use_pass_indirect = False
    sc.render.bake.use_pass_color = True
    sc.render.bake.margin = 4
    # rays start on the box and travel inward: flip the cage by baking from a slightly larger box
    bsrc = src.copy()
    bsrc.data = src.data.copy()
    bpy.context.scene.collection.objects.link(bsrc)
    bsrc.data.materials.clear() if False else None
    _nonmetal_copy(bsrc)
    bpy.ops.object.select_all(action='DESELECT')
    bsrc.select_set(True)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.bake(type='DIFFUSE')
    bpy.data.objects.remove(bsrc)
    sc.render.engine = eng
    _fill_black(img)
    os.makedirs(tex_dir, exist_ok=True)
    p = os.path.join(tex_dir, f'{set_name}_lod2_BaseColor.png')
    img.filepath_raw = p
    img.file_format = 'PNG'
    img.save()
    return o


# ------------------------------------------------------------------ door opening
def cut_door(o, fp, width=1.3, height=2.3, through=False):
    """Boolean a door opening into the front (-Y) wall at x=0: the wall is found by a ray at 1.2 m going +Y.
    Adds an Empty 'door_main' (parented) at the opening's floor centre, +Y pointing into the building.
    through: the opening also goes through the backing box behind the facade (an enterable door: a streamed interior
    room stands behind it, so it must not end on the 1.2 m vestibule)."""
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())
    start = Vector((0.0, -fp[1] / 2 - 1.0, 1.2))
    hit = bvh.ray_cast(start, Vector((0, 1, 0)), fp[1] + 2)
    wy = hit[0].y if hit[0] else -fp[1] / 2
    if through:
        # an enterable door: no boolean tunnel (its reveal faces ran 2 m into the building and walled off the room's hall);
        # the faces in the wall plane (the facade or the backing box's front) are split on the opening's edges and the
        # part inside it deleted, so the streamed room behind shows through a clean hole
        import bmesh as _bm
        bm = _bm.new(); bm.from_mesh(o.data)
        x0, x1, z1 = -width / 2, width / 2, 0.02 + height

        def wall():
            return [f for f in bm.faces if abs(f.normal.y) > 0.9 and abs(f.calc_center_median().y - wy) < 0.08
                    and min(v.co.x for v in f.verts) < x1 and max(v.co.x for v in f.verts) > x0 and min(v.co.z for v in f.verts) < z1]
        for co, no in (((x0, 0, 0), (1, 0, 0)), ((x1, 0, 0), (1, 0, 0)), ((0, 0, z1), (0, 0, 1))):
            fs = wall()
            _bm.ops.bisect_plane(bm, geom=list({e for f in fs for e in f.edges}) + fs + list({v for f in fs for v in f.verts}), plane_co=co, plane_no=no)
        gone = [f for f in wall() if x0 - 0.005 < f.calc_center_median().x < x1 + 0.005 and f.calc_center_median().z < z1]
        # and whatever the model has inside the room's volume behind the opening (a recessed lobby door, its jambs): the
        # streamed room stands there (11.5 x 10.3 x 3.4 m behind the door)
        gone += [f for f in bm.faces if f not in gone and abs(f.calc_center_median().x) < 6.2 and wy + 0.02 < f.calc_center_median().y < wy + 10.3
                 and f.calc_center_median().z < 3.4]
        _bm.ops.delete(bm, geom=list(set(gone)), context='FACES')
        bm.to_mesh(o.data); bm.free()
        e = bpy.data.objects.new('door_main', None)
        e.empty_display_type = 'ARROWS'
        e.location = (0.0, wy, 0.0)
        bpy.context.scene.collection.objects.link(e)
        e.parent = o
        log('door opened (planar) at y', round(wy, 2), 'faces removed', len(gone))
        return e
    me = bpy.data.meshes.new('door_cutter')
    import bmesh as _bm
    bm = _bm.new()
    _bm.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        depth_in = 2.2 if through else 0.7
        v.co = Vector((v.co.x * width, wy + (depth_in - 0.7) / 2 + v.co.y * (depth_in + 0.7), 0.02 + (v.co.z + 0.5) * height))
    bm.to_mesh(me)
    bm.free()
    c = bpy.data.objects.new('door_cutter', me)
    bpy.context.scene.collection.objects.link(c)
    mod = o.modifiers.new('door', 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.object = c
    mod.solver = 'EXACT'
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(c)
    e = bpy.data.objects.new('door_main', None)
    e.empty_display_type = 'ARROWS'
    e.location = (0.0, wy, 0.0)
    e.rotation_euler = (0, 0, 0)
    bpy.context.scene.collection.objects.link(e)
    e.parent = o
    log('door cut at y', round(wy, 2))
    return e


# ------------------------------------------------------------------ keep materials (tiling bark, cut-out leaves)
def copy_materials(objs, tex_dir, fam, max_px=1024):
    os.makedirs(tex_dir, exist_ok=True)
    mats, texs = [], []
    for o in objs:
        for s in o.material_slots:
            m = s.material
            if not m or m.name in mats:
                continue
            mats.append(m.name)
            for n in m.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image and not n.image.name.startswith(fam + '_'):
                    im = n.image
                    w, h = im.size
                    sz = min(max_px, w)
                    arr = _img_array(im, sz)
                    fn = f'{fam}_{len(texs):02d}.png'
                    p = os.path.join(tex_dir, fn)
                    save_png(arr, p, 4)
                    cs = im.colorspace_settings.name
                    im.name = fn[:-4]
                    im.filepath_raw = p
                    im.colorspace_settings.name = cs
                    texs.append(fn)
    return dict(materials=mats, textures=texs)


def lod_under(o, target, name, cull=0.0, planar_first=True):
    """Decimate a copy until it is at or under target tris (collapse stalls at UV seams, so iterate with lower ratios)."""
    src = o
    if cull:
        src = o.copy()
        src.data = o.data.copy()
        bpy.context.scene.collection.objects.link(src)
        cull_small(src, cull)
    d = decimate_copy(src, target, name, planar_first)
    for _ in range(6):
        t = tris(d)
        if t <= target:
            break
        bpy.ops.object.select_all(action='DESELECT')
        d.select_set(True)
        bpy.context.view_layer.objects.active = d
        mod = d.modifiers.new('dc', 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = max(0.05, target / t * 0.9)
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    if src is not o:
        bpy.data.objects.remove(src)
    if tris(d) > target and cull and cull < 4:
        bpy.data.objects.remove(d)
        return lod_under(o, target, name, cull=cull * 1.6, planar_first=planar_first)
    return d



# ------------------------------------------------------------------ part-wise workflow (kitbash buildings)
def copy_parts(objs, prefix):
    """One world-space copy per source mesh object (keeps the artist's part boundaries: a window, an AC unit...)."""
    parts = []
    for i, o in enumerate(objs):
        if o.type != 'MESH' or not o.data.polygons:
            continue
        c = o.copy()
        c.data = o.data.copy()
        c.parent = None
        c.matrix_world = o.matrix_world.copy()
        c.name = f'{prefix}_p{i:04d}'
        bpy.context.scene.collection.objects.link(c)
        c.data.transform(c.matrix_world)
        c.matrix_world = Matrix.Identity(4)
        while len(c.data.uv_layers) > 1:
            c.data.uv_layers.remove(c.data.uv_layers[-1])
        for a in list(c.data.color_attributes):
            c.data.color_attributes.remove(a)
        parts.append(c)
    return parts


def parts_to_ground(parts, yaw_deg=0.0):
    R = Matrix.Rotation(math.radians(yaw_deg), 4, 'Z')
    for p in parts:
        p.data.transform(R)
    vs = np.concatenate([np.array([v.co[:] for v in p.data.vertices]) for p in parts])
    mn, mx = vs.min(0), vs.max(0)
    T = Matrix.Translation(-Vector(((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, mn[2])))
    for p in parts:
        p.data.transform(T)
        p.data.update()
    return [float(mx[0] - mn[0]), float(mx[1] - mn[1]), float(mx[2] - mn[2])]


def join_parts(parts, name):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = o.data.name = name
    return o


def dup(o, name):
    d = o.copy()
    d.data = o.data.copy()
    d.name = d.data.name = name
    bpy.context.scene.collection.objects.link(d)
    return d


def card_parts(parts, set_name, tex_dir, px_per_m=110, size=2048, thin=0.45, min_tris=40, min_area=0.12, max_depth=3.0):
    """Thin, dense facade parts (windows with frames/grilles/curtains, louvres, AC fronts, signs) become single quads whose
    colour is baked (Cycles, selected-to-active) from the full building, so rays also see the wall behind grille holes.
    -> (kept_parts, cards_object or None). cards use M_<set>_cards with <set>_cards_BaseColor.png."""
    import bmesh as _bm
    allv = np.concatenate([np.array([v.co[:] for v in p.data.vertices]) for p in parts])
    cen = (allv.min(0) + allv.max(0)) / 2
    cards, keep = [], []
    for p in parts:
        vs = np.array([v.co[:] for v in p.data.vertices])
        mn, mx = vs.min(0), vs.max(0)
        dims = mx - mn
        ax = int(np.argmin(dims[:2]))
        other = 1 - ax
        area = dims[other] * dims[2]
        has_alpha = any(poly.material_index == 1 for poly in p.data.polygons)
        if not has_alpha and dims[ax] < thin and dims[ax] < 0.45 * dims[other] and tris(p) >= min_tris and area >= min_area and dims[2] > 0.2 and dims[2] < 4.0:
            cards.append([p, ax, 0.0, mn, mx])
        else:
            keep.append(p)
    # outward side = the side whose rays (from just outside the part's bbox) escape the building
    from mathutils.bvhtree import BVHTree
    allp = join_parts([dup(p, p.name + '_v') for p in parts], set_name + '_vis')
    bvh = BVHTree.FromObject(allp, bpy.context.evaluated_depsgraph_get())
    bpy.data.objects.remove(allp)
    for c in cards:
        p, ax, _, mn, mx = c
        other = 1 - ax
        score = {}
        for sg in (1.0, -1.0):
            esc = 0
            for fu in (0.25, 0.5, 0.75):
                for fv in (0.25, 0.5, 0.75):
                    o_ = [0.0, 0.0, 0.0]
                    o_[ax] = (mx[ax] + 0.02) if sg > 0 else (mn[ax] - 0.02)
                    o_[other] = mn[other] + fu * (mx[other] - mn[other])
                    o_[2] = mn[2] + fv * (mx[2] - mn[2])
                    d_ = [0.0, 0.0, 0.0]
                    d_[ax] = sg
                    hit = bvh.ray_cast(Vector(o_), Vector(d_), 40.0)
                    esc += 1 if hit[0] is None else 0
            score[sg] = esc
        c[2] = 1.0 if score[1.0] >= score[-1.0] else -1.0
        if max(score.values()) == 0:
            c[2] = None
    for c in [c for c in cards if c[2] is None]:
        keep.append(c[0])
    cards = [tuple(c) for c in cards if c[2] is not None]
    log(set_name, 'cards', len(cards), 'of', len(parts), 'parts;', sum(tris(c[0]) for c in cards), 'tris carded')
    if not cards:
        return parts, None
    rects = [(max(4, (mx[1 - ax] - mn[1 - ax]) * px_per_m), max(4, (mx[2] - mn[2]) * px_per_m)) for p, ax, s, mn, mx in cards]
    scale = min(3.0, math.sqrt(0.7 * size * size / max(1.0, sum(w * h for w, h in rects))))
    while True:
        order = sorted(range(len(rects)), key=lambda i: -rects[i][1])
        pos, x, y, row, ok = {}, 0, 0, 0, True
        for i in order:
            w, h = int(rects[i][0] * scale) + 4, int(rects[i][1] * scale) + 4
            if x + w > size:
                x, y, row = 0, y + row, 0
            if y + h > size:
                ok = False
                break
            pos[i] = (x + 2, y + 2, w - 4, h - 4)
            x += w
            row = max(row, h)
        if ok:
            break
        scale *= 0.92
    log(set_name, 'cards texel density', round(px_per_m * scale), 'px/m')
    me = bpy.data.meshes.new(set_name + '_cards')
    bm = _bm.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    for i, (p, ax, sign, mn, mx) in enumerate(cards):
        other = 1 - ax
        X, Y, W_, H_ = pos[i]
        fc = mx[ax] + 0.012 if sign > 0 else mn[ax] - 0.012
        vs_, uvs = [], []
        for (u, v) in ((0, 0), (1, 0), (1, 1), (0, 1)):
            c = [0.0, 0.0, 0.0]
            c[ax] = fc
            c[other] = mn[other] + u * (mx[other] - mn[other])
            c[2] = mn[2] + v * (mx[2] - mn[2])
            vs_.append(bm.verts.new(c))
            uvs.append(((X + u * W_) / size, (Y + v * H_) / size))
        f = bm.faces.new(vs_)
        for l, uv in zip(f.loops, uvs):
            l[uvl].uv = uv
        f.normal_update()
        n = np.array(f.normal[:])
        out = np.zeros(3)
        out[ax] = sign
        if np.dot(n, out) < 0:
            f.normal_flip()
            # flipping reverses loop order; mirror U so the baked image is not mirrored
            for l in f.loops:
                pass
    bm.to_mesh(me)
    bm.free()
    co = bpy.data.objects.new(set_name + '_cards', me)
    bpy.context.scene.collection.objects.link(co)
    bad = 0
    for poly, (p_, ax_, sg_, mn_, mx_) in zip(me.polygons, cards):
        if poly.normal[ax_] * sg_ < 0:
            bad += 1
    log(set_name, 'cards facing inward:', bad)
    full = join_parts([dup(p, p.name + '_b') for p in parts], set_name + '_bakesrc')
    # the bake must not hit glass / alpha-cut leaves (Cycles bake ignores alpha): drop material-1 faces from the source
    import bmesh as _bm2
    bm2 = _bm2.new()
    bm2.from_mesh(full.data)
    _bm2.ops.delete(bm2, geom=[f for f in bm2.faces if f.material_index == 1], context='FACES')
    # make the source double-sided: Sketchfab exports often have flipped faces, and bake rays skip back faces
    dd = _bm2.ops.duplicate(bm2, geom=list(bm2.faces))
    _bm2.ops.reverse_faces(bm2, faces=[g for g in dd['geom'] if isinstance(g, _bm2.types.BMFace)])
    bm2.to_mesh(full.data)
    bm2.free()
    _nonmetal_copy(full)
    img = bpy.data.images.new(set_name + '_cards_BaseColor', size, size, alpha=False)
    m = bpy.data.materials.new('M_' + set_name + '_cards')
    m.use_nodes = True
    nt = m.node_tree
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = img
    nt.nodes.active = t
    b = nt.nodes.get('Principled BSDF')
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.4
    co.data.materials.append(m)
    sc = bpy.context.scene
    eng = sc.render.engine
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = 4
    sc.render.bake.use_selected_to_active = True
    sc.render.bake.cage_extrusion = 0.03
    sc.render.bake.max_ray_distance = max_depth
    sc.render.bake.use_pass_direct = False
    sc.render.bake.use_pass_indirect = False
    sc.render.bake.use_pass_color = True
    sc.render.bake.margin = 3
    bpy.ops.object.select_all(action='DESELECT')
    full.select_set(True)
    co.select_set(True)
    bpy.context.view_layer.objects.active = co
    r_ = bpy.ops.object.bake(type='DIFFUSE')
    px_ = np.empty(size * size * 4, np.float32)
    img.pixels.foreach_get(px_)
    log(set_name, 'cards bake', r_, 'mean', px_.reshape(-1, 4)[:, :3].mean(0).round(3))
    sc.render.engine = eng
    os.makedirs(tex_dir, exist_ok=True)
    pth = os.path.join(tex_dir, f'{set_name}_cards_BaseColor.png')
    img.filepath_raw = pth
    img.file_format = 'PNG'
    img.save()
    bpy.data.objects.remove(full)
    for p, *_ in cards:
        bpy.data.objects.remove(p)
    return keep, co


def decimate_parts(parts, target, min_part=0.0, min_tris=12, planar=True, big_floor=0.3):
    """Same collapse ratio for every part (floored at min_tris) so the total lands near target; parts smaller than
    min_part (bbox diagonal, m) are dropped. Returns new list (inputs are modified in place / removed)."""
    out = []
    for p in parts:
        vs = np.array([v.co[:] for v in p.data.vertices])
        if min_part and np.linalg.norm(vs.max(0) - vs.min(0)) < min_part:
            bpy.data.objects.remove(p)
            continue
        out.append(p)
    if planar:
        for p in out:
            m = p.modifiers.new('pl', 'DECIMATE')
            m.decimate_type = 'DISSOLVE'
            m.angle_limit = math.radians(1.5)
            m.delimit = {'UV', 'MATERIAL', 'SEAM', 'SHARP'}
            p.modifiers.new('tri', 'TRIANGULATE')
        _apply_all(out)
    T = [tris(p) for p in out]
    # big structural parts (walls, shutters, slabs) keep at least half their tris: collapsing them wrecks their UVs
    big = []
    for p in out:
        vs = np.array([v.co[:] for v in p.data.vertices])
        d_ = np.sort(vs.max(0) - vs.min(0))
        big.append(d_[2] * d_[1] > 3.0)
    floor = [max(min_tris, (big_floor * t if b else 0)) for t, b in zip(T, big)]
    if sum(T) > target:
        lo, hi = 0.0, 1.0
        for _ in range(30):
            r = (lo + hi) / 2
            s_ = sum(min(t, max(f, r * t)) for t, f in zip(T, floor))
            lo, hi = (r, hi) if s_ < target else (lo, r)
        for p, t, f in zip(out, T, floor):
            keep = min(t, max(f, lo * t))
            if keep < t * 0.97:
                m = p.modifiers.new('d', 'DECIMATE')
                m.ratio = keep / t
                m.use_collapse_triangulate = True
        _apply_all(out)
    return out


def _apply_all(objs):
    bpy.ops.object.select_all(action='DESELECT')
    objs = [o for o in objs if o.modifiers]
    if not objs:
        return
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.convert(target='MESH')


def facade_yaw_parts(parts):
    """Front = the side with the most facade detail (part tris within 1.2 m of that side). Returns yaw so it faces -Y."""
    vs = np.concatenate([np.array([v.co[:] for v in p.data.vertices]) for p in parts])
    mn, mx = vs.min(0), vs.max(0)
    score = {0.0: 0, 90.0: 0, 180.0: 0, 270.0: 0}
    for p in parts:
        pv = np.array([v.co[:] for v in p.data.vertices])
        a, b = pv.min(0), pv.max(0)
        t = tris(p)
        if a[1] - mn[1] < 1.2: score[0.0] += t       # -Y side already front
        if mx[0] - b[0] < 1.2: score[270.0] += t     # +X side -> rotate -90
        if mx[1] - b[1] < 1.2: score[180.0] += t
        if a[0] - mn[0] < 1.2: score[90.0] += t      # -X side -> rotate +90 ... (rotation maps -X to -Y)
    best = max(score, key=lambda k: score[k])
    log('facade scores', {k: v for k, v in score.items()}, '->', best)
    return best


def building_lods(name, objs, tex_dir, fbx_path, door=False, yaw=None, lod0=15000, lod1=6500, card=True):
    """Kitbash building -> LOD0 (cards + per-part decimation), LOD1, LOD2 (baked box); atlas set; FBX. -> manifest dict."""
    parts = copy_parts(objs, name)
    if yaw is None:
        yaw = facade_yaw_parts(parts)
    fp = parts_to_ground(parts, yaw)
    at = build_atlas(parts[0], name, tex_dir, extra_objs=parts[1:])
    src_tris = sum(tris(p) for p in parts)
    if card:
        keep, cards = card_parts(parts, name, tex_dir)
    else:
        keep, cards = parts, None
    nc = tris(cards) if cards else 0
    l1src = [dup(p, p.name + '_l1') for p in keep]
    cards1 = dup(cards, name + '_c1') if cards else None
    red0 = decimate_parts(keep, lod0 - nc, min_part=0.12)
    l0 = join_parts(red0 + ([cards] if cards else []), name + '_LOD0')
    # LOD1 (2026-10-22): parts under 0.8 m used to be dropped (window panes, frames) and an over-budget LOD1 got one
    # global collapse to 4k: walls tore open and windows became holes, so buildings read see-through at mid range.
    # Now only bits under 0.5 m go (window panes stay), walls keep 30 % of their triangles, and the budget is 6.5k without a
    # global collapse (a few hundred over is better than a hole).
    red1 = decimate_parts(l1src, max(1000, lod1 - nc), min_part=0.5, min_tris=6, big_floor=0.3)
    l1 = join_parts(red1 + ([cards1] if cards1 else []), name + '_LOD1')
    if tris(l1) > lod1 * 1.25:
        log(name, 'LOD1 over budget', tris(l1), '>', lod1)
    l2 = box_lod(l0, name + '_LOD2', tex_dir, name)
    # a backing box inside the shell (2026-10-22): the far box, shrunk 1.2 m inside the bounds, joined into LOD0 and
    # LOD1 as their last material slot, so a gap in a kitbash wall shows the baked
    # facade behind it instead of the sky: the buildings no longer read see-through
    def backing(target, keep_name):
        b = dup(l2, keep_name + '_backing')
        vs = np.array([v.co[:] for v in b.data.vertices])
        mn, mx = vs.min(0), vs.max(0)
        c = (mn + mx) / 2
        half = (mx - mn) / 2
        sh = np.minimum(1.2, 0.2 * (mx - mn))
        for v in b.data.vertices:
            for i in (0, 1):
                v.co[i] = c[i] + np.sign(v.co[i] - c[i]) * max(0.2, half[i] - sh[i])
            if v.co[2] > c[2]:
                v.co[2] = mx[2] - min(0.5, 0.1 * (mx[2] - mn[2]))
        return join_parts([target, b], keep_name)
    l1 = backing(l1, name + '_LOD1')
    l0 = backing(l0, name + '_LOD0')        # a door opens onto a 1.2 m deep vestibule, not onto the sky
    out = [l0, l1, l2]
    extra = {}
    if door:
        e = cut_door(l0, fp, **(door if isinstance(door, dict) else {}))
        out.append(e)
        extra['door_main'] = [round(e.location.x, 3), round(e.location.y, 3), 0.0]
    export_fbx(out, fbx_path)
    mats = at['materials'] + ([f'M_{name}_cards'] if cards else []) + [f'M_{name}_lod2']   # the backing box's slot last
    texs = at['textures'] + ([f'{name}_cards_BaseColor.png'] if cards else []) + [f'{name}_lod2_BaseColor.png']
    log(name, 'src', src_tris, 'tris', [tris(x) for x in (l0, l1, l2)], 'size', [round(x, 2) for x in fp])
    res = dict(tris=[tris(x) for x in (l0, l1, l2)], size_m=[round(x, 3) for x in fp], materials=mats,
               lod2_material=f'M_{name}_lod2', textures=texs, source_tris=src_tris, **extra)
    for o in out:
        if o.type == 'MESH':
            bpy.data.objects.remove(o)
    return res


def billboard(src, name, tex_dir, px=512):
    """Crossed-quad impostor (4 tris): orthographic EEVEE render of src (side view) with transparent film."""
    vs = np.array([src.matrix_world @ v.co for v in src.data.vertices])
    mn, mx = vs.min(0), vs.max(0)
    w = max(mx[0] - mn[0], mx[1] - mn[1])
    h = mx[2] - mn[2]
    sc = bpy.context.scene
    hidden = [o for o in sc.objects if o is not src and o.type == 'MESH' and not o.hide_render]
    for o in hidden:
        o.hide_render = True
    cam = bpy.data.objects.new('bbcam', bpy.data.cameras.new('bbcam'))
    sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = max(w, h) * 1.02
    cam.location = (0, -50, (mn[2] + mx[2]) / 2)
    cam.rotation_euler = (math.radians(90), 0, 0)
    sun = bpy.data.objects.new('bbsun', bpy.data.lights.new('bbsun', 'SUN'))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(45), 0, math.radians(-30))
    sc.collection.objects.link(sun)
    if not sc.world:
        sc.world = bpy.data.worlds.new('bbw')
    sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.6, 0.65, 0.7, 1)
    sc.camera = cam
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = px
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.view_settings.view_transform = 'Standard'
    p = os.path.join(tex_dir, f'{name}_billboard.png')
    sc.render.filepath = p
    bpy.ops.render.render(write_still=True)
    for o in hidden:
        o.hide_render = False
    bpy.data.objects.remove(cam)
    bpy.data.objects.remove(sun)
    s = max(w, h) * 1.02
    zc = (mn[2] + mx[2]) / 2 - mn[2]
    me = bpy.data.meshes.new(name + '_LOD2')
    verts, faces, uvs = [], [], []
    for k, ang in enumerate((0.0, 90.0)):
        a = math.radians(ang)
        dx, dy = math.cos(a) * s / 2, math.sin(a) * s / 2
        base = len(verts)
        verts += [(-dx, -dy, zc - s / 2), (dx, dy, zc - s / 2), (dx, dy, zc + s / 2), (-dx, -dy, zc + s / 2)]
        faces.append((base, base + 1, base + 2, base + 3))
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new(name='UVMap')
    for poly in me.polygons:
        for li, (u, v) in zip(poly.loop_indices, ((0, 0), (1, 0), (1, 1), (0, 1))):
            uvl.data[li].uv = (u, v)
    o = bpy.data.objects.new(name + '_LOD2', me)
    sc.collection.objects.link(o)
    m = bpy.data.materials.new(f'M_{name}_billboard')
    m.use_nodes = True
    t = m.node_tree.nodes.new('ShaderNodeTexImage')
    t.image = bpy.data.images.load(p)
    b = m.node_tree.nodes.get('Principled BSDF')
    m.node_tree.links.new(t.outputs['Color'], b.inputs['Base Color'])
    m.node_tree.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
    me.materials.append(m)
    return o


