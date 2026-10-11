"""Shared helpers for the CT Life lookdev scenes (Blender 5.1, Eevee).

Imported by timeline_square.py and terminal_cafe.py. All source assets are CC0 (see LICENSES.md).
"""
import math
import os
import random

import bpy
import mathutils
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src")
TEX = os.path.join(HERE, "tex")
QDC = os.path.join(SRC, "quaternius_downtown_city_megakit", "Exports", "glTF (Godot)")
QN = os.path.join(SRC, "quaternius_stylized_nature_megakit", "glTF")
QH = os.path.join(SRC, "quaternius_background_posed_humans")
QC = os.path.join(SRC, "quaternius_cars", "FBX")
PH = os.path.join(SRC, "polyhaven", "models")
PHT = os.path.join(SRC, "polyhaven", "textures")
HDRI = os.path.join(SRC, "polyhaven", "hdri")

_cache = {}


# ------------------------------------------------------------------------------ scene
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _cache.clear()
    lib = bpy.data.collections.new("LIB")
    bpy.context.scene.collection.children.link(lib)
    bpy.context.view_layer.layer_collection.children["LIB"].exclude = True
    return lib


def lib():
    return bpy.data.collections["LIB"]


def link(obj, coll=None):
    (coll or bpy.context.scene.collection).objects.link(obj)
    return obj


def bounds(objs):
    pts = []
    for o in objs:
        if o.type == "MESH":
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


def _move_to(coll, objs):
    for o in objs:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        coll.objects.link(o)


def load(key, path, fix=None, kind=None):
    """Import a file once into a hidden library collection; return the collection."""
    if key in _cache:
        return _cache[key]
    coll = bpy.data.collections.new("A_" + key)
    lib().children.link(coll)
    before = set(bpy.data.objects)
    kind = kind or os.path.splitext(path)[1].lower()
    if kind in (".gltf", ".glb"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif kind == ".fbx":
        bpy.ops.wm.fbx_import(filepath=path)
    elif kind == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    _move_to(coll, new)
    bpy.context.view_layer.update()
    if fix:
        fix(new)
    coll["bounds"] = [list(v) for v in bounds(new)] if any(o.type == "MESH" for o in new) else [[0, 0, 0], [0, 0, 0]]
    _cache[key] = coll
    return coll


def inst(coll, loc=(0, 0, 0), rot=0.0, scale=1.0, name=None, parent=None):
    e = bpy.data.objects.new(name or coll.name, None)
    e.instance_type = "COLLECTION"
    e.instance_collection = coll
    e.location = loc
    e.rotation_euler = (0, 0, rot) if isinstance(rot, (int, float)) else rot
    e.scale = (scale,) * 3 if isinstance(scale, (int, float)) else scale
    link(e)
    if parent:
        e.parent = parent
    return e


# ------------------------------------------------------------------------------ geometry
def mesh_obj(name, verts, faces, mat=None, coll=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    link(o, coll)
    if mat:
        o.data.materials.append(mat)
    return o


def box(name, size, loc, mat=None, bevel=0.0, rot=0.0, coll=None):
    sx, sy, sz = (s / 2 for s in size)
    v = [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
         (-sx, -sy, sz), (sx, -sy, sz), (sx, sy, sz), (-sx, sy, sz)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    o = mesh_obj(name, v, f, mat, coll)
    o.location = loc
    o.rotation_euler = (0, 0, rot) if isinstance(rot, (int, float)) else rot
    if bevel > 0:
        m = o.modifiers.new("bevel", "BEVEL")
        m.width = bevel
        m.segments = 3
        m.limit_method = "ANGLE"
    return o


def plane(name, w, h, loc, rot=(0, 0, 0), mat=None, coll=None):
    """Plane in local XZ (facing -Y) so images read correctly when rot=(0,0,0)."""
    v = [(-w / 2, 0, -h / 2), (w / 2, 0, -h / 2), (w / 2, 0, h / 2), (-w / 2, 0, h / 2)]
    o = mesh_obj(name, v, [(0, 1, 2, 3)], mat, coll)
    uv = o.data.uv_layers.new(name="UVMap")
    for i, c in enumerate([(0, 0), (1, 0), (1, 1), (0, 1)]):
        uv.data[i].uv = c
    o.location = loc
    o.rotation_euler = rot
    return o


def ground(name, x0, y0, x1, y1, z, mat, coll=None, thick=0.0):
    if thick <= 0:
        return mesh_obj(name, [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], [(0, 1, 2, 3)], mat, coll)
    return box(name, (x1 - x0, y1 - y0, thick), ((x0 + x1) / 2, (y0 + y1) / 2, z - thick / 2), mat, coll=coll)


def cylinder(name, r, h, loc, mat=None, seg=32, r_top=None, coll=None):
    r_top = r if r_top is None else r_top
    v, f = [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        v.append((r * math.cos(a), r * math.sin(a), 0))
        v.append((r_top * math.cos(a), r_top * math.sin(a), h))
    for i in range(seg):
        j = (i + 1) % seg
        f.append((2 * i, 2 * j, 2 * j + 1, 2 * i + 1))
    f.append(tuple(2 * i for i in reversed(range(seg))))
    f.append(tuple(2 * i + 1 for i in range(seg)))
    o = mesh_obj(name, v, f, mat, coll)
    o.location = loc
    for p in o.data.polygons:
        p.use_smooth = len(p.vertices) == 4
    return o


def sphere(name, r, loc, mat=None, coll=None, hemi=False):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=24, ring_count=12)
    o = bpy.context.active_object
    o.name = name
    for c in list(o.users_collection):
        c.objects.unlink(o)
    link(o, coll)
    if hemi:
        import bmesh
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
    for p in o.data.polygons:
        p.use_smooth = True
    if mat:
        o.data.materials.append(mat)
    return o


# ------------------------------------------------------------------------------ materials
def _nodes(mat):
    mat.use_nodes = True
    nt = mat.node_tree
    return nt.nodes, nt.links


def mat_color(name, rgb, rough=0.5, metal=0.0, emit=None, emit_strength=0.0, alpha=1.0, spec=0.5, coat=0.0):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    n, l = _nodes(m)
    b = n["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["Specular IOR Level"].default_value = spec
    b.inputs["Coat Weight"].default_value = coat
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = emit_strength
    if alpha < 1:
        b.inputs["Alpha"].default_value = alpha
    return m


def img(path, colorspace="sRGB"):
    im = bpy.data.images.load(path, check_existing=True)
    im.colorspace_settings.name = colorspace
    return im


def mat_pbr(name, tex_id, scale=1.0, tint=None, rough_mul=1.0, coords="OBJECT", rot=0.0, bump=1.0):
    """Poly Haven 1k texture set mapped with object/world coordinates (meters * scale)."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    d = os.path.join(PHT, tex_id)
    m = bpy.data.materials.new(name)
    n, l = _nodes(m)
    b = n["Principled BSDF"]
    tc = n.new("ShaderNodeTexCoord")
    mp = n.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale, scale, scale)
    mp.inputs["Rotation"].default_value = (0, 0, rot)
    l.new(tc.outputs["Object" if coords == "OBJECT" else "UV"], mp.inputs["Vector"])
    if coords == "WORLD":
        geo = n.new("ShaderNodeNewGeometry")
        l.new(geo.outputs["Position"], mp.inputs["Vector"])

    def t(suffix, cs):
        tn = n.new("ShaderNodeTexImage")
        tn.image = img(os.path.join(d, f"{tex_id}_{suffix}_1k.jpg"), cs)
        l.new(mp.outputs["Vector"], tn.inputs["Vector"])
        return tn
    diff = t("diff", "sRGB")
    col = diff.outputs["Color"]
    if tint:
        mix = n.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs["B"].default_value = (*tint, 1)
        l.new(col, mix.inputs["A"])
        col = mix.outputs["Result"]
    l.new(col, b.inputs["Base Color"])
    rough = t("rough", "Non-Color")
    if rough_mul != 1.0:
        mm = n.new("ShaderNodeMath")
        mm.operation = "MULTIPLY"
        mm.inputs[1].default_value = rough_mul
        l.new(rough.outputs["Color"], mm.inputs[0])
        l.new(mm.outputs[0], b.inputs["Roughness"])
    else:
        l.new(rough.outputs["Color"], b.inputs["Roughness"])
    nor = t("nor_gl", "Non-Color")
    nm = n.new("ShaderNodeNormalMap")
    nm.inputs["Strength"].default_value = bump
    l.new(nor.outputs["Color"], nm.inputs["Color"])
    l.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m


def mat_screen(name, path, strength=3.0, rough=0.25, base=0.02, uv_flip=False):
    """Emissive screen/billboard material from a generated PNG."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    n, l = _nodes(m)
    b = n["Principled BSDF"]
    tn = n.new("ShaderNodeTexImage")
    tn.image = img(path)
    tn.interpolation = "Cubic"
    l.new(tn.outputs["Color"], b.inputs["Emission Color"])
    if base > 0:
        mm = n.new("ShaderNodeMix")
        mm.data_type = "RGBA"
        mm.inputs["Factor"].default_value = 1 - base
        mm.inputs["A"].default_value = (0, 0, 0, 1)
        l.new(tn.outputs["Color"], mm.inputs["B"])
        l.new(mm.outputs["Result"], b.inputs["Base Color"])
    b.inputs["Emission Strength"].default_value = strength
    b.inputs["Roughness"].default_value = rough
    return m


def mat_print(name, path, rough=0.6, emit=0.0):
    """Printed (lit) billboard: albedo from image, optional slight self-illumination."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    n, l = _nodes(m)
    b = n["Principled BSDF"]
    tn = n.new("ShaderNodeTexImage")
    tn.image = img(path)
    l.new(tn.outputs["Color"], b.inputs["Base Color"])
    if emit > 0:
        l.new(tn.outputs["Color"], b.inputs["Emission Color"])
        b.inputs["Emission Strength"].default_value = emit
    b.inputs["Roughness"].default_value = rough
    return m


def mat_glass_tower(name, base=(0.05, 0.2, 0.27), frame=(0.62, 0.66, 0.7), floor_h=4.0, haze=(0.98, 0.8, 0.62), haze_dist=(150, 700), mull=3.0):
    """Curtain-wall glass: floor slabs + mullions, per-floor tint variation, fake aerial perspective."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    n, l = _nodes(m)
    b = n["Principled BSDF"]
    geo = n.new("ShaderNodeNewGeometry")
    sep = n.new("ShaderNodeSeparateXYZ")
    l.new(geo.outputs["Position"], sep.inputs[0])

    def math_node(op, a=None, b_=None, va=None, vb=None):
        x = n.new("ShaderNodeMath"); x.operation = op
        if a is not None: l.new(a, x.inputs[0])
        if va is not None: x.inputs[0].default_value = va
        if b_ is not None: l.new(b_, x.inputs[1])
        if vb is not None: x.inputs[1].default_value = vb
        return x.outputs[0]

    def band(src, period, width):
        fr = math_node("FRACT", math_node("DIVIDE", src, vb=period))
        return math_node("LESS_THAN", fr, vb=width)
    slab = band(sep.outputs["Z"], floor_h, 0.10)
    nsep = n.new("ShaderNodeSeparateXYZ")
    l.new(geo.outputs["Normal"], nsep.inputs[0])
    use_x = math_node("GREATER_THAN", math_node("ABSOLUTE", nsep.outputs["Y"]), vb=0.5)  # face along X -> vary with X
    use_y = math_node("GREATER_THAN", math_node("ABSOLUTE", nsep.outputs["X"]), vb=0.5)
    mxy = math_node("MAXIMUM", math_node("MULTIPLY", band(sep.outputs["X"], mull, 0.05), use_x),
                    math_node("MULTIPLY", band(sep.outputs["Y"], mull, 0.05), use_y))
    fr = math_node("MAXIMUM", slab, mxy)
    # per-floor / per-bay variation
    floor_id = math_node("FLOOR", math_node("DIVIDE", sep.outputs["Z"], vb=floor_h))
    bay = math_node("FLOOR", math_node("DIVIDE", math_node("ADD", sep.outputs["X"], sep.outputs["Y"]), vb=mull * 3))
    wn = n.new("ShaderNodeTexWhiteNoise"); wn.noise_dimensions = "2D"
    cmb = n.new("ShaderNodeCombineXYZ")
    l.new(floor_id, cmb.inputs[0]); l.new(bay, cmb.inputs[1])
    l.new(cmb.outputs[0], wn.inputs["Vector"])
    var = n.new("ShaderNodeMix"); var.data_type = "RGBA"
    var.inputs["A"].default_value = (*base, 1)
    var.inputs["B"].default_value = (min(1, base[0] * 1.7 + 0.04), min(1, base[1] * 1.5 + 0.04), min(1, base[2] * 1.4 + 0.04), 1)
    l.new(math_node("MULTIPLY", wn.outputs["Value"], vb=0.8), var.inputs["Factor"])
    col = n.new("ShaderNodeMix"); col.data_type = "RGBA"
    l.new(var.outputs["Result"], col.inputs["A"]); col.inputs["B"].default_value = (*frame, 1)
    l.new(fr, col.inputs["Factor"])
    cam = n.new("ShaderNodeCameraData")
    mr = n.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = haze_dist[0]; mr.inputs["From Max"].default_value = haze_dist[1]
    mr.inputs["To Min"].default_value = 0.0; mr.inputs["To Max"].default_value = 0.55
    l.new(cam.outputs["View Distance"], mr.inputs["Value"])
    hz = n.new("ShaderNodeMix"); hz.data_type = "RGBA"
    hz.inputs["B"].default_value = (*haze, 1)
    l.new(mr.outputs[0], hz.inputs["Factor"]); l.new(col.outputs["Result"], hz.inputs["A"])
    l.new(hz.outputs["Result"], b.inputs["Base Color"])
    b.inputs["Emission Color"].default_value = (*haze, 1)
    l.new(math_node("MULTIPLY", mr.outputs[0], vb=0.25), b.inputs["Emission Strength"])
    l.new(math_node("MULTIPLY_ADD", fr, vb=0.4, b_=None), b.inputs["Roughness"])
    rr = b.inputs["Roughness"].links[0].from_node; rr.inputs[2].default_value = 0.07
    l.new(math_node("MULTIPLY_ADD", fr, vb=-0.4), b.inputs["Metallic"])
    mt = b.inputs["Metallic"].links[0].from_node; mt.inputs[2].default_value = 0.5
    b.inputs["Coat Weight"].default_value = 0.3
    return m


def fix_vertex_color_mix(objs, tints=None, keep_vc_alpha=True):
    """Quaternius glTF materials multiply the base texture by a vertex-colour wear mask that renders
    as red patches outside their engine shaders. Re-wire texture -> Base Color directly."""
    tints = tints or {}
    seen = set()
    for o in objs:
        if o.type != "MESH":
            continue
        for m in o.data.materials:
            if m is None or m.name in seen or not m.use_nodes:
                continue
            seen.add(m.name)
            nt = m.node_tree
            b = next((x for x in nt.nodes if x.type == "BSDF_PRINCIPLED"), None)
            mix = next((x for x in nt.nodes if x.type == "MIX"), None)
            tex = next((x for x in nt.nodes if x.type == "TEX_IMAGE" and any(lk.to_node == mix for lk in x.outputs["Color"].links)), None) if mix else None
            if b is None:
                continue
            if tex is not None:
                src = tex.outputs["Color"]
                for key, tint in tints.items():
                    if key in m.name:
                        mm = nt.nodes.new("ShaderNodeMix"); mm.data_type = "RGBA"; mm.blend_type = "MULTIPLY"
                        mm.inputs["Factor"].default_value = 1.0
                        mm.inputs["B"].default_value = (*tint, 1)
                        nt.links.new(src, mm.inputs["A"]); src = mm.outputs["Result"]
                        break
                nt.links.new(src, b.inputs["Base Color"])


# ------------------------------------------------------------------------------ world/render
def world_hdri(path, strength=1.0, rot_z=0.0, clamp=6.0, tint=(1, 1, 1), sat=1.0):
    w = bpy.data.worlds.new("World")
    bpy.context.scene.world = w
    w.use_nodes = True
    n, l = w.node_tree.nodes, w.node_tree.links
    bg = n["Background"]
    tc = n.new("ShaderNodeTexCoord")
    mp = n.new("ShaderNodeMapping")
    mp.inputs["Rotation"].default_value = (0, 0, rot_z)
    env = n.new("ShaderNodeTexEnvironment")
    env.image = img(path, "Linear Rec.709")
    l.new(tc.outputs["Generated"], mp.inputs["Vector"])
    l.new(mp.outputs["Vector"], env.inputs["Vector"])
    vm = n.new("ShaderNodeVectorMath")
    vm.operation = "MINIMUM"
    vm.inputs[1].default_value = (clamp, clamp, clamp)
    l.new(env.outputs["Color"], vm.inputs[0])
    hs = n.new("ShaderNodeHueSaturation")
    hs.inputs["Saturation"].default_value = sat
    l.new(vm.outputs["Vector"], hs.inputs["Color"])
    mt = n.new("ShaderNodeMix"); mt.data_type = "RGBA"; mt.blend_type = "MULTIPLY"
    mt.inputs["Factor"].default_value = 1.0; mt.inputs["B"].default_value = (*tint, 1)
    l.new(hs.outputs["Color"], mt.inputs["A"])
    l.new(mt.outputs["Result"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = strength
    return w


def sun(energy=4.0, color=(1.0, 0.8, 0.6), elev=25.0, azim=40.0, angle=2.0):
    """azim: compass degrees the light comes FROM (0 = +Y/back, 90 = +X/right)."""
    s = bpy.data.lights.new("Sun", "SUN")
    s.energy = energy
    s.color = color
    s.angle = math.radians(angle)
    o = bpy.data.objects.new("Sun", s)
    link(o)
    a, e = math.radians(azim), math.radians(elev)
    dir_from = Vector((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)))
    o.rotation_euler = (-dir_from).to_track_quat("-Z", "Y").to_euler()
    return o


def camera(loc, target, lens=40.0, shift_y=0.0, name="Cam", dof=None):
    c = bpy.data.cameras.new(name)
    c.lens = lens
    c.shift_y = shift_y
    c.clip_end = 3000
    o = bpy.data.objects.new(name, c)
    link(o)
    o.location = loc
    d = Vector(target) - Vector(loc)
    o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    if dof:
        c.dof.use_dof = True
        c.dof.focus_distance = d.length if dof.get("dist") is None else dof["dist"]
        c.dof.aperture_fstop = dof.get("fstop", 8.0)
    bpy.context.scene.camera = o
    return o


def render_settings(w, h, samples=64, exposure=0.0, look="AgX - Medium High Contrast"):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = w
    sc.render.resolution_y = h
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    ee = sc.eevee
    ee.taa_render_samples = samples
    ee.use_shadows = True
    ee.shadow_ray_count = 2
    ee.shadow_step_count = 8
    ee.use_raytracing = True
    ee.ray_tracing_method = "SCREEN"
    try:
        ee.ray_tracing_options.resolution_scale = "1"
    except Exception:
        pass
    ee.use_fast_gi = True
    ee.fast_gi_method = "GLOBAL_ILLUMINATION"
    ee.fast_gi_distance = 6.0
    ee.shadow_resolution_scale = 1.0
    try:
        ee.shadow_pool_size = "1024"  # MB; the café overflowed the 512 MB default ("Shadow buffer full")
    except Exception as exc:  # enum differs between versions
        print("shadow_pool_size not set:", exc)
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = look
    sc.view_settings.exposure = exposure
    sc.render.film_transparent = False


def render(path):
    sc = bpy.context.scene
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


# ------------------------------------------------------------------------------ people
SKIN = [(0.93, 0.72, 0.58), (0.80, 0.56, 0.40), (0.62, 0.40, 0.26), (0.42, 0.26, 0.16), (0.98, 0.80, 0.68), (0.72, 0.50, 0.34)]
SHIRTS = [(0.85, 0.10, 0.16), (0.05, 0.40, 0.90), (0.98, 0.70, 0.05), (0.12, 0.65, 0.30), (0.92, 0.92, 0.90),
          (0.95, 0.35, 0.55), (0.45, 0.20, 0.80), (0.98, 0.42, 0.08), (0.02, 0.60, 0.62), (0.12, 0.13, 0.18),
          (0.80, 0.82, 0.20), (0.40, 0.70, 0.95), (0.98, 0.60, 0.70), (0.55, 0.85, 0.35)]
PANTS = [(0.10, 0.16, 0.32), (0.22, 0.32, 0.55), (0.12, 0.12, 0.13), (0.75, 0.66, 0.50), (0.40, 0.28, 0.20),
         (0.85, 0.82, 0.76), (0.30, 0.38, 0.25), (0.55, 0.20, 0.25)]
HAIR = [(0.05, 0.035, 0.025), (0.20, 0.11, 0.05), (0.45, 0.25, 0.10), (0.85, 0.65, 0.35), (0.55, 0.15, 0.08), (0.75, 0.75, 0.75), (0.02, 0.02, 0.02)]
SHOES = [(0.95, 0.95, 0.95), (0.08, 0.08, 0.09), (0.40, 0.25, 0.15), (0.85, 0.20, 0.20)]
HUMAN_SCALE = 0.5  # Quaternius posed humans are ~3.7 units tall -> ~1.85 m (slightly heroic, Sims-like)

_head_cache = {}


def _cloth(rgb, rough=0.75, kind="cloth"):
    key = "%s_%.2f_%.2f_%.2f" % (kind, *rgb)
    if key in bpy.data.materials:
        return bpy.data.materials[key]
    m = mat_color(key, rgb, rough=rough, spec=0.35)
    b = m.node_tree.nodes["Principled BSDF"]
    if kind == "skin":
        b.inputs["Subsurface Weight"].default_value = 0.15
        b.inputs["Subsurface Radius"].default_value = (0.4, 0.2, 0.1)
        b.inputs["Subsurface Scale"].default_value = 0.05
        b.inputs["Roughness"].default_value = 0.55
    if kind == "cloth":
        b.inputs["Sheen Weight"].default_value = 0.35
        b.inputs["Sheen Tint"].default_value = (1, 1, 1, 1)
    return m


def _head_of(obj):
    """Head top/centre in object space: largest connected island of 'Skin' faces."""
    if obj.data.name in _head_cache:
        return _head_cache[obj.data.name]
    me = obj.data
    skin_idx = [i for i, m in enumerate(me.materials) if m and m.name.startswith("Skin")]
    faces = [p for p in me.polygons if p.material_index in skin_idx]
    parent = {}

    def find(a):
        while parent.setdefault(a, a) != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for p in faces:
        vs = list(p.vertices)
        for v in vs[1:]:
            ra, rb = find(vs[0]), find(v)
            if ra != rb:
                parent[ra] = rb
    groups = {}
    for v in parent:
        groups.setdefault(find(v), []).append(v)
    # head island = largest island whose mean z is in the upper part of the figure
    zmax = max(v.co.z for v in me.vertices)
    cands = [g for g in groups.values() if sum(me.vertices[i].co.z for i in g) / len(g) > 0.5 * zmax]
    head = max(cands or list(groups.values()), key=len)
    co = [me.vertices[i].co for i in head]
    top = max(c.z for c in co)
    upper = [c for c in co if c.z > top - 0.45]
    cx = sum(c.x for c in upper) / len(upper)
    cy = sum(c.y for c in upper) / len(upper)
    _head_cache[obj.data.name] = (cx, cy, top)
    return _head_cache[obj.data.name]


POSES = {
    "F": ["Standing", "Standing_Hips", "Walking", "LookingUp", "Sitting", "Sitting_Cheering", "Standing_CoveringEyes", "Running", "PickingUp"],
    "M": ["Standing", "Standing_Hips", "Standing_Waving", "Walking", "LookingUp", "Sitting", "Sitting_Cheering", "Standing_CoveringEyes", "Running", "PickingUp"],
}


def pose_path(sex, pose):
    if sex == "F":
        return os.path.join(QH, "Female", "Female Poses", "FBX", f"Female_{pose}.fbx")
    return os.path.join(QH, "Male", "Male Poses", "FBX", f"Male_{pose}.fbx")


def hair_path(sex, i):
    s = "Female" if sex == "F" else "Male"
    return os.path.join(QH, s, f"{s} Hairstyles", "FBX", f"{s}_Hairstyle_{i}.fbx")


def person(sex, pose, loc, rot, rnd, hair=None, shirt=None, pants=None, skin=None, hair_col=None, shoes=None,
           hair_off=(0.0, 0.0, -0.30), scale=HUMAN_SCALE, name="Person"):
    pc = load(f"pose_{sex}_{pose}", pose_path(sex, pose))
    src = next(o for o in pc.objects if o.type == "MESH")
    root = bpy.data.objects.new(name, None)
    link(root)
    root.location = loc
    root.rotation_euler = (0, 0, rot)
    root.scale = (scale,) * 3
    body = bpy.data.objects.new(name + "_body", src.data)
    link(body)
    body.parent = root
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = src.matrix_basis.copy()
    if not src.data.get("smoothed"):
        for p in src.data.polygons:
            p.use_smooth = True
        src.data["smoothed"] = 1
    sm = body.modifiers.new("subsurf", "SUBSURF")
    sm.levels = 1
    sm.render_levels = 1
    cols = {
        "Skin": _cloth(skin or rnd.choice(SKIN), kind="skin"),
        "Shirt": _cloth(shirt or rnd.choice(SHIRTS)),
        "Pants": _cloth(pants or rnd.choice(PANTS)),
        "Shoes": _cloth(shoes or rnd.choice(SHOES), rough=0.5),
        "Hair": _cloth(hair_col or rnd.choice(HAIR), rough=0.45, kind="hair"),
    }
    for i, slot in enumerate(body.material_slots):
        slot.link = "OBJECT"
        base = src.data.materials[i].name.split(".")[0]
        slot.material = cols.get(base, cols["Shirt"])
    has_hair = any(m and m.name.startswith("Hair") for m in src.data.materials)
    if hair is None:
        hair = rnd.randint(1, 4)
    if hair and not has_hair:
        hc = load(f"hair_{sex}_{hair}", hair_path(sex, hair))
        hs = next(o for o in hc.objects if o.type == "MESH")
        hx, hy, top = _head_of(src)
        h = bpy.data.objects.new(name + "_hair", hs.data)
        link(h)
        h.parent = root
        h.matrix_parent_inverse = Matrix.Identity(4)
        h.location = (hx + hair_off[0], hy + hair_off[1], top + hair_off[2])
        h.rotation_euler = hs.rotation_euler
        h.scale = hs.scale
        if not hs.data.get("smoothed"):
            for p in hs.data.polygons:
                p.use_smooth = True
            hs.data["smoothed"] = 1
        hm = h.modifiers.new("subsurf", "SUBSURF")
        hm.levels = 1
        hm.render_levels = 1
        for slot in h.material_slots:
            slot.link = "OBJECT"
            slot.material = cols["Hair"]
    return root


def mat_neon(name, path, strength=8.0):
    """Neon sign on black: emission from the image, alpha from its luminance (black = invisible)."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    n, l = _nodes(m)
    b = n["Principled BSDF"]
    tn = n.new("ShaderNodeTexImage")
    tn.image = img(path)
    b.inputs["Base Color"].default_value = (0, 0, 0, 1)
    l.new(tn.outputs["Color"], b.inputs["Emission Color"])
    b.inputs["Emission Strength"].default_value = strength
    bw = n.new("ShaderNodeRGBToBW")
    l.new(tn.outputs["Color"], bw.inputs["Color"])
    mm = n.new("ShaderNodeMath"); mm.operation = "MULTIPLY"; mm.use_clamp = True; mm.inputs[1].default_value = 4.0
    l.new(bw.outputs["Val"], mm.inputs[0])
    l.new(mm.outputs[0], b.inputs["Alpha"])
    return m


def mat_tex_box(name, base, normal=None, scale=0.5, tint=None, rough=0.8, blend=0.25):
    """Box-projected texture (object coords) for walls etc."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    n, l = _nodes(m)
    b = n["Principled BSDF"]
    tc = n.new("ShaderNodeTexCoord")
    mp = n.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale, scale, scale)
    l.new(tc.outputs["Object"], mp.inputs["Vector"])
    tn = n.new("ShaderNodeTexImage")
    tn.image = img(base)
    tn.projection = "BOX"
    tn.projection_blend = blend
    l.new(mp.outputs["Vector"], tn.inputs["Vector"])
    col = tn.outputs["Color"]
    if tint:
        mx = n.new("ShaderNodeMix"); mx.data_type = "RGBA"; mx.blend_type = "MULTIPLY"
        mx.inputs["Factor"].default_value = 1.0; mx.inputs["B"].default_value = (*tint, 1)
        l.new(col, mx.inputs["A"]); col = mx.outputs["Result"]
    l.new(col, b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = rough
    if normal:
        nt = n.new("ShaderNodeTexImage")
        nt.image = img(normal, "Non-Color")
        nt.projection = "BOX"
        nt.projection_blend = blend
        l.new(mp.outputs["Vector"], nt.inputs["Vector"])
        nm = n.new("ShaderNodeNormalMap")
        l.new(nt.outputs["Color"], nm.inputs["Color"])
        l.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m


def area_light(name, loc, rot, size, energy, color, shape="RECTANGLE", size_y=None):
    lt = bpy.data.lights.new(name, "AREA")
    lt.shape = shape
    lt.size = size
    if size_y:
        lt.size_y = size_y
    lt.energy = energy
    lt.color = color
    o = bpy.data.objects.new(name, lt)
    link(o)
    o.location = loc
    o.rotation_euler = rot
    return o


def point_light(name, loc, energy, color, radius=0.1):
    lt = bpy.data.lights.new(name, "POINT")
    lt.energy = energy
    lt.color = color
    lt.shadow_soft_size = radius
    o = bpy.data.objects.new(name, lt)
    link(o)
    o.location = loc
    return o


def save_blend(path):
    """Unpack glTF-embedded textures into src/_unpacked (gitignored) so the .blend stays small, then save."""
    out = os.path.join(SRC, "_unpacked")
    os.makedirs(out, exist_ok=True)
    for im in bpy.data.images:
        if im.packed_file:
            ext = {"PNG": ".png", "JPEG": ".jpg"}.get(im.file_format, ".png")
            im.filepath = os.path.join(out, bpy.path.clean_name(im.name) + ext)
            im.unpack(method="WRITE_ORIGINAL")
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
