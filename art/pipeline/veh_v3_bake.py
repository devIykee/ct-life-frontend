"""CT Life vehicles v3: texture atlas baking (Cycles) shared by veh_v3_process.py.

bake_atlas(objs, out_dir, prefix, size=2048, paint_mats=(), neutral_paint=True)
  objs        LOD0 mesh objects to bake (glass objects/materials must be excluded beforehand)
  -> writes <prefix>_BaseColor.png, <prefix>_Normal.png, <prefix>_MaskMap.png (R metallic, G AO, B paint mask, A smoothness)
     and returns their paths. Every object gets a new UV layer 'UVAtlas' (made active + active-render) and the original
     materials are left in place (the caller swaps them for the atlas materials).

Spec/gloss materials (glTF KHR_materials_pbrSpecularGlossiness, imported as Base Color + Specular Tint) are converted to
metal/rough with the Khronos formula. Paint materials are baked neutral (luminance normalised to a light grey) so URP's
_BaseColor can tint them, and flagged in the mask map's B channel.
"""
import bpy, os, math
import numpy as np

BAKE_ATTR = 'ctl_bake_src'


def _principled(m):
    if not m or not m.node_tree:
        return None
    return next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)


def _output(m):
    outs = [n for n in m.node_tree.nodes if n.type == 'OUTPUT_MATERIAL']
    for n in outs:
        if n.is_active_output:
            return n
    return outs[0] if outs else m.node_tree.nodes.new('ShaderNodeOutputMaterial')


def _source(nt, sock):
    """(socket_or_None, default_value) feeding a Principled input."""
    if sock is None:
        return None, None
    if sock.is_linked:
        return sock.links[0].from_socket, None
    return None, sock.default_value


def _setup_cycles(samples=1):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'METAL'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        sc.cycles.device = 'GPU'
    except Exception:
        sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = False
    if sc.world is None:
        sc.world = bpy.data.worlds.new('BakeWorld')
    sc.world.light_settings.distance = 0.35      # AO reach: panel gaps and wheel wells, not the whole hull
    sc.render.bake.margin = 6
    sc.render.bake.margin_type = 'EXTEND'


def make_atlas_uv(objs, margin=0.003):
    for o in objs:
        uv = o.data.uv_layers.get('UVAtlas') or o.data.uv_layers.new(name='UVAtlas')
        o.data.uv_layers.active = uv
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=margin, area_weight=1.0, scale_to_bounds=False)
    try:
        bpy.ops.uv.pack_islands(rotate=True, margin=margin, shape_method='CONCAVE')
    except TypeError:
        bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')


def _pin_source_uvs(mats, objs):
    """Image textures without an explicit UV map read the active-render layer; pin them to the original layer."""
    first = {}
    for o in objs:
        names = [l.name for l in o.data.uv_layers if l.name != 'UVAtlas']
        for s in o.material_slots:
            if s.material and names:
                first.setdefault(s.material.name, names[0])
    for m in mats:
        nt = m.node_tree
        for n in list(nt.nodes):
            if n.type == 'TEX_IMAGE' and not n.inputs['Vector'].is_linked:
                uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = first.get(m.name, 'UVMap')
                nt.links.new(uvn.outputs['UV'], n.inputs['Vector'])
            if n.type == 'UVMAP' and n.uv_map == '':
                n.uv_map = first.get(m.name, 'UVMap')


def _emit_pass(mats, getter):
    """Rewire each material's output to an Emission shader fed by getter(material, principled) -> (socket|None, value)."""
    saved = []
    for m in mats:
        nt = m.node_tree; out = _output(m)
        old = out.inputs['Surface'].links[0].from_socket if out.inputs['Surface'].is_linked else None
        em = nt.nodes.new('ShaderNodeEmission'); em.name = '__bake_emit'
        em.inputs['Strength'].default_value = 1.0
        sock, val = getter(m, _principled(m))
        if sock is not None:
            nt.links.new(sock, em.inputs['Color'])
        else:
            if isinstance(val, (int, float)):
                val = (val, val, val, 1.0)
            em.inputs['Color'].default_value = tuple(val)[:3] + (1.0,)
        nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
        saved.append((m, out, old, em))
    return saved


def _restore(saved):
    for m, out, old, em in saved:
        nt = m.node_tree
        if old is not None:
            nt.links.new(old, out.inputs['Surface'])
        nt.nodes.remove(em)


def _scalar_to_color(nt, sock):
    """Float socket -> grey colour socket."""
    if sock is None or sock.type != 'VALUE':
        return sock
    c = nt.nodes.new('ShaderNodeCombineColor')
    for i in range(3):
        nt.links.new(sock, c.inputs[i])
    return c.outputs['Color']


def _get_input(name):
    def g(m, b):
        if b is None:
            return None, (0.0 if name != 'Base Color' else (0.5, 0.5, 0.5, 1))
        s = b.inputs.get(name)
        sock, val = _source(m.node_tree, s)
        if sock is not None:
            return _scalar_to_color(m.node_tree, sock), None
        if name == 'Base Color' and val is not None:
            return None, val
        return None, val
    return g


def _image(name, size, non_color=True, alpha=False):
    img = bpy.data.images.get(name)
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(name, size, size, alpha=alpha, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = 'Non-Color'
    return img


def _set_target(mats, img):
    for m in mats:
        nt = m.node_tree
        n = nt.nodes.get('__bake_target') or nt.nodes.new('ShaderNodeTexImage')
        n.name = '__bake_target'; n.image = img
        nt.nodes.active = n


def _bake(objs, kind, img, mats, samples=1):
    _set_target(mats, img)
    bpy.context.scene.cycles.samples = samples
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if kind == 'NORMAL':
        bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', use_clear=True, uv_layer='UVAtlas')
    elif kind == 'AO':
        bpy.ops.object.bake(type='AO', use_clear=True, uv_layer='UVAtlas')
    else:
        bpy.ops.object.bake(type='EMIT', use_clear=True, uv_layer='UVAtlas')
    a = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
    return a


def _lum(c):
    return np.sqrt(0.299 * c[..., 0] ** 2 + 0.587 * c[..., 1] ** 2 + 0.114 * c[..., 2] ** 2)


def _srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _lin_to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def bake_atlas(objs, out_dir, prefix, size=2048, paint_mats=(), specgloss_mats=(), paint_target=0.92, ao_samples=160):
    os.makedirs(out_dir, exist_ok=True)
    _setup_cycles()
    mats = []
    for o in objs:
        for s in o.material_slots:
            if s.material and s.material not in mats and s.material.node_tree:
                mats.append(s.material)
    _pin_source_uvs(mats, objs)
    make_atlas_uv(objs)
    # ID pass: paint mask, spec/gloss mask
    def id_getter(flag_names):
        return lambda m, b: (None, 1.0 if m.name in flag_names else 0.0)
    passes = {}
    for key, getter in (('base', _get_input('Base Color')), ('metal', _get_input('Metallic')), ('rough', _get_input('Roughness')),
                        ('spec', _get_input('Specular Tint')), ('paint', id_getter(set(paint_mats))), ('sg', id_getter(set(specgloss_mats)))):
        if key == 'spec' and not specgloss_mats:
            continue
        if key == 'sg' and not specgloss_mats:
            continue
        img = _image('__bake_' + key, size, non_color=True)
        saved = _emit_pass(mats, getter)
        passes[key] = _bake(objs, 'EMIT', img, mats)[..., :3].copy()
        _restore(saved)
        print('[bake]', prefix, key, flush=True)
    emissive = []
    for m in mats:
        b = _principled(m)
        if b is None:
            continue
        st = b.inputs.get('Emission Strength'); ec = b.inputs.get('Emission Color')
        if st is not None and (st.is_linked or st.default_value > 0) and ec is not None and (ec.is_linked or max(ec.default_value[:3]) > 0.01):
            emissive.append(m.name)
    emis = None
    if emissive:
        img = _image('__bake_emit', size, non_color=True)
        emis = _bake(objs, 'EMIT', img, mats)[..., :3].copy()
        if emis.max() < 0.02:
            emis = None
        print('[bake]', prefix, 'emission from', emissive, flush=True)
    img = _image('__bake_ao', size, non_color=True)
    ao = _bake(objs, 'AO', img, mats, samples=ao_samples)[..., 0].copy()
    print('[bake]', prefix, 'ao', flush=True)
    img = _image('__bake_normal', size, non_color=True)
    nrm = _bake(objs, 'NORMAL', img, mats, samples=1)[..., :3].copy()
    print('[bake]', prefix, 'normal', flush=True)
    # emission-baked colours come out linear (Non-Color target = raw values); Base Color sources are sRGB textures already
    # decoded to linear by Blender, so 'base' and 'spec' are linear here.
    base = passes['base']; metal = passes['metal'][..., 0]; rough = passes['rough'][..., 0]
    paint = passes['paint'][..., 0] > 0.5
    if os.environ.get('CTL_DEBUG'):
        for k, v in passes.items():
            sel = paint if paint.any() else np.ones(paint.shape, bool)
            print('[dbg]', k, 'paint-region mean', np.round(v[sel].mean(axis=0), 3), 'all mean', np.round(v.reshape(-1, 3).mean(axis=0), 3), flush=True)
    if 'sg' in passes:
        sgm = passes['sg'][..., 0] > 0.5
        d = base; sp = passes['spec']
        a = 0.04
        one_minus = 1 - sp.max(axis=-1)
        pd, ps = _lum(d), _lum(sp)
        b = pd * one_minus / (1 - a) + ps - 2 * a
        c = a - ps
        disc = np.maximum(b * b - 4 * a * c, 0)
        mtl = np.clip((-b + np.sqrt(disc)) / (2 * a), 0, 1)
        mtl = np.where(ps < a, 0, mtl)
        from_d = d * (one_minus / (1 - a) / np.maximum(1 - mtl, 1e-4))[..., None]
        from_s = (sp - a * (1 - mtl)[..., None]) / np.maximum(mtl, 1e-4)[..., None]
        bc = np.clip(from_d + (from_s - from_d) * (mtl ** 2)[..., None], 0, 1)
        base = np.where(sgm[..., None], bc, base)
        metal = np.where(sgm, mtl, metal)
    # neutral paint: keep detail, normalise the paint's mean luminance to paint_target (linear), desaturate
    paint_rgb = None
    if paint.any():
        # pixels in a paint material whose hue is far from the paint's (painted-on windows, stripes, decals) stay as they are
        # (hue is read from the spec colour for spec/gloss materials, whose base colour is black)
        hs = np.where(sgm[..., None], passes['spec'], base) if 'sg' in passes else base
        nrm_c = hs / np.maximum(hs.sum(axis=-1, keepdims=True), 1e-4)
        med_c = np.median(nrm_c[paint], axis=0)
        rel_chroma = (hs.max(axis=-1) - hs.min(axis=-1)) / np.maximum(hs.max(axis=-1), 1e-3)
        med_rc = float(np.median(rel_chroma[paint]))
        off_hue = (np.abs(nrm_c - med_c).sum(axis=-1) > 0.12) & (rel_chroma > max(0.25, 2.0 * med_rc)) & (hs.max(axis=-1) > 0.004)
        paint = paint & ~off_hue
        # car paint is a coated dielectric with metallic flakes, not a mirror: clamp so it catches light on a phone
        metal = np.where(paint, np.minimum(metal, 0.5), metal)
        rough = np.where(paint, np.maximum(rough, 0.25), rough)
        lum = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
        mean = float(np.median(lum[paint])) if paint.any() else 1
        mean_rgb = np.median(base[paint], axis=0)
        paint_rgb = [float(x) for x in _lin_to_srgb(np.array(mean_rgb))]
        if mean < 0.02:     # near-black paint (e.g. the hovercar's black gloss): no detail worth keeping, bake it flat
            neutral = np.full(lum.shape, float(_srgb_to_lin(np.array(paint_target))))
        else:
            neutral = np.clip(lum / max(mean, 1e-3) * _srgb_to_lin(np.array(paint_target)), 0, 1)
        base = np.where(paint[..., None], np.repeat(neutral[..., None], 3, axis=-1), base)
    # de-noise: AO (sampling noise) everywhere; metallic-flake noise in the paint (reads as grain on a phone)
    ao = 1.0 - 0.6 * (1.0 - _blur(ao, 1))
    if paint.any():
        metal = np.where(paint, _blur(_blur(metal, 2), 2), metal)
        rough = np.where(paint, _blur(_blur(rough, 2), 2), rough)
    H, W = base.shape[:2]
    out = {}
    def save(name, rgba):
        img = bpy.data.images.new(prefix + '_' + name, W, H, alpha=True)
        img.colorspace_settings.name = 'Non-Color'
        img.pixels = rgba.astype(np.float32).ravel()
        path = os.path.join(out_dir, f'{prefix}_{name}.png')
        img.filepath_raw = path; img.file_format = 'PNG'
        img.save()
        out[name] = path
        return path
    ones = np.ones((H, W), np.float32)
    save('BaseColor', np.dstack([_lin_to_srgb(base), ones]))
    save('Normal', np.dstack([nrm, ones]))
    if emis is not None:
        save('Emission', np.dstack([_lin_to_srgb(np.clip(emis, 0, 1)), ones]))
    save('MaskMap', np.dstack([np.clip(metal, 0, 1), np.clip(ao, 0, 1), paint.astype(np.float32), np.clip(1 - rough, 0, 1)]))
    for m in mats:
        n = m.node_tree.nodes.get('__bake_target')
        if n:
            m.node_tree.nodes.remove(n)
    return out, paint_rgb


def atlas_materials(tex, paint_rgb=None):
    """Body (+ Paint) materials reading the baked atlas through UVAtlas; Paint multiplies its tint over the neutral paint."""
    def mk(name, tint=None):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree; b = _principled(m)
        uv = nt.nodes.new('ShaderNodeUVMap'); uv.uv_map = 'UVAtlas'
        def tx(path, non_color):
            n = nt.nodes.new('ShaderNodeTexImage'); n.image = bpy.data.images.load(path, check_existing=True)
            n.image.colorspace_settings.name = 'Non-Color' if non_color else 'sRGB'
            nt.links.new(uv.outputs['UV'], n.inputs['Vector'])
            return n
        bc = tx(tex['BaseColor'], False); mm = tx(tex['MaskMap'], True); nm = tx(tex['Normal'], True)
        col = bc.outputs['Color']
        if tint is not None:
            mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
            mix.inputs['Factor'].default_value = 1.0
            nt.links.new(col, _sock(mix.inputs, 'A')); _sock(mix.inputs, 'B').default_value = tuple(tint) + (1,)
            col = _sock(mix.outputs, 'Result')
        # AO darkens base colour for the review render only (Unity uses the mask map's G as occlusion)
        sep = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(mm.outputs['Color'], sep.inputs['Color'])
        ao = nt.nodes.new('ShaderNodeMix'); ao.data_type = 'RGBA'; ao.blend_type = 'MULTIPLY'; ao.inputs['Factor'].default_value = 1.0
        nt.links.new(col, _sock(ao.inputs, 'A')); nt.links.new(sep.outputs['Green'], _sock(ao.inputs, 'B'))
        nt.links.new(_sock(ao.outputs, 'Result'), b.inputs['Base Color'])
        nt.links.new(sep.outputs['Red'], b.inputs['Metallic'])
        inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
        nt.links.new(mm.outputs['Alpha'], inv.inputs[1]); nt.links.new(inv.outputs['Value'], b.inputs['Roughness'])
        mm.image.alpha_mode = 'CHANNEL_PACKED'
        if 'Emission' in tex:
            em = tx(tex['Emission'], False)
            nt.links.new(em.outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = 1.0
        nmap = nt.nodes.new('ShaderNodeNormalMap'); nmap.uv_map = 'UVAtlas'
        nt.links.new(nm.outputs['Color'], nmap.inputs['Color']); nt.links.new(nmap.outputs['Normal'], b.inputs['Normal'])
        return m
    body = mk('Body')
    paint = mk('Paint', paint_rgb_lin(paint_rgb)) if paint_rgb is not None else None
    return body, paint


def _sock(coll, name):
    """ShaderNodeMix has float/vector/colour sockets sharing names; pick the RGBA one."""
    for x in coll:
        if x.name == name and x.type == 'RGBA':
            return x
    return coll[name]


def _blur(a, r=1):
    out = a.copy(); n = 1
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx or dy:
                out += np.roll(np.roll(a, dy, 0), dx, 1); n += 1
    return out / n


def paint_rgb_lin(srgb):
    return [float(x) for x in _srgb_to_lin(np.array(srgb) * 1.0 / max(1e-3, 1.0))]


def glass_material(rgb=(0.11, 0.15, 0.2), alpha=0.5):
    m = bpy.data.materials.new('Windows')
    m.use_nodes = True
    b = _principled(m)
    b.inputs['Base Color'].default_value = (*rgb, 1)
    b.inputs['Alpha'].default_value = alpha
    b.inputs['Roughness'].default_value = 0.05
    b.inputs['Metallic'].default_value = 0.1
    try:
        m.surface_render_method = 'BLENDED'
    except Exception:
        m.blend_method = 'BLEND'
    return m
