"""Character materials and the mesh-level fixes for the key art.

Material = the game's URP Lit inputs, rebuilt in Cycles: BaseColor (the look's default tints are
baked into it, the same pixels the Masked shader reproduces for this look), Normal, and the
MetallicSmoothness map (R = metallic, A = smoothness, Unity's convention), plus key-art-only
touches: soft subsurface on skin (skin region from the game's TintMask0 R channel), glossy eyes,
real lenses.
"""
import bmesh
import bpy
from mathutils import Vector

from ka_common import LOOKS, REPO

MASKS = REPO / 'client/Assets/Art/characters/palettes/humans'


def _img(path, non_color=False):
    im = bpy.data.images.load(str(path), check_existing=True)
    if non_color:
        im.colorspace_settings.name = 'Non-Color'
    return im


def character_material(mesh, look, normal_strength=0.75, sss=0.12):
    mat = mesh.data.materials[0]
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    b = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(b.outputs[0], out.inputs[0])
    uv = nt.nodes.new('ShaderNodeUVMap')
    uv.uv_map = mesh.data.uv_layers[0].name

    def tex(path, nc=False):
        t = nt.nodes.new('ShaderNodeTexImage')
        t.image = _img(path, nc)
        t.interpolation = 'Cubic'
        nt.links.new(uv.outputs[0], t.inputs[0])
        return t

    base = tex(LOOKS / look / f'{look}_BaseColor.png')
    nt.links.new(base.outputs['Color'], b.inputs['Base Color'])
    nrm = tex(LOOKS / look / f'{look}_Normal.png', True)
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nm.uv_map = uv.uv_map
    nm.inputs['Strength'].default_value = normal_strength
    nt.links.new(nrm.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs[0], b.inputs['Normal'])
    ms = tex(LOOKS / look / f'{look}_MetallicSmoothness.png', True)
    sep = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(ms.outputs['Color'], sep.inputs[0])
    nt.links.new(sep.outputs['Red'], b.inputs['Metallic'])
    rough = nt.nodes.new('ShaderNodeMapRange')          # roughness = 1 - smoothness, kept off mirror-gloss
    rough.inputs['From Min'].default_value = 0.0
    rough.inputs['From Max'].default_value = 1.0
    rough.inputs['To Min'].default_value = 0.95
    rough.inputs['To Max'].default_value = 0.42
    nt.links.new(ms.outputs['Alpha'], rough.inputs['Value'])
    # cloth and leather keep a soft sheen (smoothness 1 -> 0.36); metals (gold chain, studs) stay glossier
    met = nt.nodes.new('ShaderNodeMath'); met.operation = 'MULTIPLY_ADD'
    met.inputs[1].default_value = -0.22
    nt.links.new(sep.outputs['Red'], met.inputs[0])
    nt.links.new(rough.outputs['Result'], met.inputs[2])
    nt.links.new(met.outputs[0], b.inputs['Roughness'])
    # skin: subsurface, a softer and lower specular
    mask = tex(MASKS / f'{look}_Mask0.png', True)
    msep = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(mask.outputs['Color'], msep.inputs[0])
    sw = nt.nodes.new('ShaderNodeMath'); sw.operation = 'MULTIPLY'; sw.inputs[1].default_value = sss
    nt.links.new(msep.outputs['Red'], sw.inputs[0])
    nt.links.new(sw.outputs[0], b.inputs['Subsurface Weight'])
    b.inputs['Subsurface Radius'].default_value = (1.0, 0.45, 0.25)
    b.inputs['Subsurface Scale'].default_value = 0.012
    spec = nt.nodes.new('ShaderNodeMapRange')
    spec.inputs['To Min'].default_value = 0.22     # cloth / leather: low sheen so blacks stay black
    spec.inputs['To Max'].default_value = 0.32     # skin
    nt.links.new(msep.outputs['Red'], spec.inputs['Value'])
    nt.links.new(spec.outputs['Result'], b.inputs['Specular IOR Level'])
    return mat, base, b


def _components(bm):
    seen, comps = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, comp = [v], []
        seen.add(v.index)
        while stack:
            a = stack.pop()
            comp.append(a)
            for e in a.link_edges:
                o = e.other_vert(a)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        comps.append(comp)
    return comps


def _face_set(mesh, pred_comp):
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    faces = set()
    for comp in _components(bm):
        co = [v.co for v in comp]
        mn = Vector([min(c[i] for c in co) for i in range(3)])
        mx = Vector([max(c[i] for c in co) for i in range(3)])
        if pred_comp(mn, mx, len(comp)):
            for v in comp:
                for f in v.link_faces:
                    faces.add(f.index)
    bm.free()
    return faces


def _assign(mesh, faces, mat):
    mesh.data.materials.append(mat)
    idx = len(mesh.data.materials) - 1
    for f in faces:
        mesh.data.polygons[f].material_index = idx
    return len(faces)


def eye_faces(mesh):
    """Faces fully owned by the Eye.L / Eye.R bones (eyeball, iris ring and pupil cap)."""
    gl, gr = mesh.vertex_groups['Eye.L'].index, mesh.vertex_groups['Eye.R'].index
    owned = []
    for v in mesh.data.vertices:
        owned.append(any(g.group in (gl, gr) and g.weight > 0.99 for g in v.groups))
    return {p.index for p in mesh.data.polygons if all(owned[i] for i in p.vertices)}


def lens_faces(mesh, eye_z):
    """Glasses lenses: thin, flat loose parts sitting in front of the eyes."""
    def pred(mn, mx, n):
        return (mx.y - mn.y) < 0.006 and mx.y < -0.11 and abs((mn.x + mx.x) / 2) < 0.08 \
            and (mx.x - mn.x) > 0.03 and mn.z > eye_z - 0.05 and mx.z < eye_z + 0.05
    return _face_set(mesh, pred)


def eye_material(src_mat):
    m = src_mat.copy()
    m.name = src_mat.name + '_eye'
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    for l in list(b.inputs['Roughness'].links) + list(b.inputs['Metallic'].links) + list(b.inputs['Subsurface Weight'].links) \
            + list(b.inputs['Normal'].links) + list(b.inputs['Specular IOR Level'].links):
        m.node_tree.links.remove(l)
    b.inputs['Roughness'].default_value = 0.35
    b.inputs['Metallic'].default_value = 0.0
    b.inputs['Subsurface Weight'].default_value = 0.0
    b.inputs['Specular IOR Level'].default_value = 0.5
    b.inputs['Coat Weight'].default_value = 1.0       # wet cornea: a crisp highlight over the painted iris
    b.inputs['Coat Roughness'].default_value = 0.03
    b.inputs['Coat IOR'].default_value = 1.376
    return m


def clear_lens_material():
    """Clear prescription lens: transparent with a faint Fresnel reflection."""
    m = bpy.data.materials.new('lens_clear')
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    tr = nt.nodes.new('ShaderNodeBsdfTransparent')
    gl = nt.nodes.new('ShaderNodeBsdfGlossy'); gl.inputs['Roughness'].default_value = 0.03
    fr = nt.nodes.new('ShaderNodeLayerWeight'); fr.inputs['Blend'].default_value = 0.12
    mx = nt.nodes.new('ShaderNodeMixShader')
    nt.links.new(fr.outputs['Fresnel'], mx.inputs[0])
    nt.links.new(tr.outputs[0], mx.inputs[1])
    nt.links.new(gl.outputs[0], mx.inputs[2])
    nt.links.new(mx.outputs[0], out.inputs[0])
    return m


def shades_material(src_mat):
    m = src_mat.copy()
    m.name = 'shades_lens'
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    for l in list(b.inputs['Roughness'].links) + list(b.inputs['Normal'].links):
        m.node_tree.links.remove(l)
    b.inputs['Roughness'].default_value = 0.08
    b.inputs['Coat Weight'].default_value = 1.0
    b.inputs['Coat Roughness'].default_value = 0.02
    return m


def smile(mesh, corners, front_y=-0.06):
    """Lift mouth corners in the rest mesh with a smooth falloff (no blend shapes on these heads).

    corners: (x, z, lift, back, radius). Only face-front vertices of the head move.
    """
    vs = mesh.data.vertices
    for cx, cz, lift, back, r in corners:
        near = [v for v in vs if abs(v.co.x - cx) < r and abs(v.co.z - cz) < r and v.co.y < front_y]
        if not near:
            continue
        ymin = min(v.co.y for v in near)
        c = min((v for v in near if v.co.y < ymin + 0.02), key=lambda v: (v.co.x - cx) ** 2 + (v.co.z - cz) ** 2).co.copy()
        for v in vs:
            d = (v.co - c).length
            if d < r and v.co.y < c.y + 0.012:
                w = (1 - (d / r) ** 2) ** 2
                v.co.z += lift * w
                v.co.y += back * w
                v.co.x += (0.25 * lift if cx > 0 else -0.25 * lift) * w   # corners widen a touch as they rise
    mesh.data.update()


def boot_fill(mesh, mat, z_top, color):
    """Rain boots are a separate open shaft over the shin: the skin inside them showed through the
    shaft's open top and its ankle gap. Paint the shin inside the boot dark (a vertex attribute
    mixed over the albedo), so the opening reads as the boot's lining instead of a hole."""
    attr = mesh.data.color_attributes.new('boot_fill', 'FLOAT_COLOR', 'POINT')
    skin = mesh.vertex_groups
    calf = {skin['Calf.L'].index, skin['Calf.R'].index, skin['Foot.L'].index, skin['Foot.R'].index}
    for v in mesh.data.vertices:
        on_leg = any(g.group in calf and g.weight > 0.2 for g in v.groups)
        w = 1.0 if (on_leg and v.co.z < z_top) else 0.0
        attr.data[v.index].color = (w, w, w, 1)
    nt = mat.node_tree
    b = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    base_link = b.inputs['Base Color'].links[0]
    src = base_link.from_socket
    ca = nt.nodes.new('ShaderNodeVertexColor'); ca.layer_name = 'boot_fill'
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    nt.links.new(ca.outputs['Color'], mix.inputs['Factor'])
    nt.links.new(src, mix.inputs[6])
    mix.inputs[7].default_value = (*color, 1)
    nt.links.new(mix.outputs[2], b.inputs['Base Color'])
    # no subsurface glow in the lining
    sss = b.inputs['Subsurface Weight'].links[0].from_node
    m2 = nt.nodes.new('ShaderNodeMath'); m2.operation = 'MULTIPLY'
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
    nt.links.new(ca.outputs['Color'], inv.inputs[1])
    nt.links.new(sss.outputs[0], m2.inputs[0]); nt.links.new(inv.outputs[0], m2.inputs[1])
    nt.links.new(m2.outputs[0], b.inputs['Subsurface Weight'])
