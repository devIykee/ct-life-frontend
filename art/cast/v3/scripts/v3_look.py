"""Shared preview look for cast v3 renders: one lighting rig, one toon shader (3-band ramp, warm rim,
screen-space AO) fed by either a baked atlas or the source's flat material colours, arms-down pose,
fixed ortho cameras. Blender preview of the intended runtime shader, not Unity output."""
import bpy, math
import numpy as np
from mathutils import Vector, Matrix

KEY = (-0.45, -0.62, 0.64)

def reset(bg=(0.62, 0.70, 0.82)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.view_settings.view_transform = 'Standard'
    sc.render.film_transparent = False
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (*bg, 1)
    w.node_tree.nodes['Background'].inputs[1].default_value = 0.55
    sun = bpy.data.lights.new('sun', 'SUN'); sun.energy = 3.2; sun.color = (1.0, 0.95, 0.86)
    so = bpy.data.objects.new('sun', sun); sc.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    cam = bpy.data.cameras.new('cam'); cam.type = 'ORTHO'
    co = bpy.data.objects.new('cam', cam); sc.collection.objects.link(co); sc.camera = co
    try:
        sc.eevee.use_gtao = True
        sc.eevee.gtao_distance = 0.25
    except Exception:
        pass
    sc.render.image_settings.file_format = 'PNG'
    return sc, co

def _lin(rgb):
    rgb = np.asarray(rgb, dtype=np.float64)
    return np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)

def hex_lin(h):
    h = h.lstrip('#')
    return tuple(float(x) for x in _lin([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])) + (1.0,)

def toon_tail(nt, col, emission=None, ao=True, rim=0.35):
    """col: linear base-colour socket -> lit toon emission shader linked to a new material output."""
    N, Lk = nt.nodes, nt.links
    out = N.new('ShaderNodeOutputMaterial')
    geo = N.new('ShaderNodeNewGeometry')
    dot = N.new('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'
    Lk.new(geo.outputs['Normal'], dot.inputs[0]); dot.inputs[1].default_value = KEY
    half = N.new('ShaderNodeMath'); half.operation = 'MULTIPLY_ADD'; Lk.new(dot.outputs['Value'], half.inputs[0])
    half.inputs[1].default_value = 0.5; half.inputs[2].default_value = 0.5
    ramp = N.new('ShaderNodeValToRGB'); Lk.new(half.outputs[0], ramp.inputs['Fac'])
    r = ramp.color_ramp; r.interpolation = 'EASE'
    r.elements[0].position = 0.25; r.elements[0].color = (0.46, 0.46, 0.56, 1)
    r.elements[1].position = 0.72; r.elements[1].color = (1.05, 1.0, 0.94, 1)
    e = r.elements.new(0.47); e.color = (0.74, 0.72, 0.78, 1)
    lit = N.new('ShaderNodeMix'); lit.data_type = 'RGBA'; lit.blend_type = 'MULTIPLY'; lit.inputs['Factor'].default_value = 1
    Lk.new(col, lit.inputs['A']); Lk.new(ramp.outputs['Color'], lit.inputs['B'])
    cur = lit.outputs['Result']
    if ao:
        aon = N.new('ShaderNodeAmbientOcclusion'); aon.samples = 8; aon.inputs['Distance'].default_value = 0.12
        aor = N.new('ShaderNodeMapRange'); Lk.new(aon.outputs['AO'], aor.inputs['Value'])
        aor.inputs['To Min'].default_value = 0.70; aor.inputs['To Max'].default_value = 1.0
        aom = N.new('ShaderNodeMix'); aom.data_type = 'RGBA'; aom.blend_type = 'MULTIPLY'; aom.inputs['Factor'].default_value = 1
        Lk.new(cur, aom.inputs['A']); Lk.new(aor.outputs['Result'], aom.inputs['B'])
        cur = aom.outputs['Result']
    lw = N.new('ShaderNodeLayerWeight'); lw.inputs['Blend'].default_value = 0.25
    rm = N.new('ShaderNodeMath'); rm.operation = 'MULTIPLY'; Lk.new(lw.outputs['Facing'], rm.inputs[0]); rm.inputs[1].default_value = rim
    addr = N.new('ShaderNodeMix'); addr.data_type = 'RGBA'; addr.blend_type = 'ADD'; Lk.new(rm.outputs[0], addr.inputs['Factor'])
    Lk.new(cur, addr.inputs['A']); addr.inputs['B'].default_value = (1.0, 0.85, 0.65, 1)
    em = N.new('ShaderNodeEmission'); Lk.new(addr.outputs['Result'], em.inputs['Color'])
    final = em
    if emission is not None:
        em2 = N.new('ShaderNodeEmission'); Lk.new(emission, em2.inputs['Color']); em2.inputs['Strength'].default_value = 2.5
        add = N.new('ShaderNodeAddShader'); Lk.new(em.outputs[0], add.inputs[0]); Lk.new(em2.outputs[0], add.inputs[1])
        final = add
    Lk.new(final.outputs[0], out.inputs['Surface'])

def toon_flat(name, rgba_lin):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    rgb = nt.nodes.new('ShaderNodeRGB'); rgb.outputs[0].default_value = tuple(rgba_lin[:3]) + (1,)
    toon_tail(nt, rgb.outputs[0])
    return m

def toon_image(name, albedo_path, uv='AtlasUV', emission_path=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = uv
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(str(albedo_path), check_existing=True)
    t.image.colorspace_settings.name = 'sRGB'
    nt.links.new(uvn.outputs['UV'], t.inputs['Vector'])
    em = None
    if emission_path:
        e = nt.nodes.new('ShaderNodeTexImage'); e.image = bpy.data.images.load(str(emission_path), check_existing=True)
        nt.links.new(uvn.outputs['UV'], e.inputs['Vector']); em = e.outputs['Color']
    toon_tail(nt, t.outputs['Color'], em)
    return m

def source_flat_toon(obj, cache):
    """Replace each material slot with the unified toon shader driven by that material's flat base colour."""
    for i, sl in enumerate(obj.material_slots):
        src = sl.material
        key = src.name if src else '__none'
        if key not in cache:
            c = (0.8, 0.8, 0.8, 1)
            if src is not None:
                c = tuple(src.diffuse_color)
                if src.node_tree:
                    b = next((n for n in src.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
                    if b is not None and not b.inputs['Base Color'].is_linked:
                        c = tuple(b.inputs['Base Color'].default_value)
            cache[key] = toon_flat('toon_' + key, c)
        sl.material = cache[key]

def pose_arms_down(rig, upper=('upperarm_l', 'upperarm_r'), target=0.96):
    rig.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    for name, sgn in ((upper[0], 1), (upper[1], -1)):
        pb = rig.pose.bones[name]
        head = pb.head.copy(); cur = (pb.tail - pb.head).normalized()
        q = cur.rotation_difference(Vector((sgn * 0.26, 0.04, -target)))
        pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
        bpy.context.view_layer.update()

def frame(sc, cam, meshes, kind='body', res=(360, 720), head_point=None, az=-18, el=8, pad=1.08, head_scale=0.46):
    deps = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in meshes:
        if o.hide_render:
            continue
        ev = o.evaluated_get(deps)
        mw = o.matrix_world
        co = np.empty(len(ev.data.vertices) * 3, dtype=np.float32); ev.data.vertices.foreach_get('co', co)
        co = co.reshape(-1, 3)
        M = np.array(mw)
        pts.append(co @ M[:3, :3].T + M[:3, 3])
    P = np.concatenate(pts)
    zmin, zmax = float(P[:, 2].min()), float(P[:, 2].max())
    cx = float(np.median(P[:, 0]))
    sc.render.resolution_x, sc.render.resolution_y = res
    if kind == 'body':
        h = zmax - zmin
        cam.data.ortho_scale = h * pad * max(1.0, res[0] / res[1]) if res[0] > res[1] else h * pad
        tgt = Vector((cx, 0, zmin + h / 2))
    else:
        cam.data.ortho_scale = head_scale
        tgt = Vector(head_point)
        az, el = (-30, 18) if az == -18 else (az, el)
    d = Vector((math.sin(math.radians(az)) * math.cos(math.radians(el)), -math.cos(math.radians(az)) * math.cos(math.radians(el)),
                math.sin(math.radians(el))))
    cam.location = tgt + d * 8
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    return P

def render(sc, path):
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
