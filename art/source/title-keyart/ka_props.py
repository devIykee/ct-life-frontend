"""Original props for the key art (modelled here, no external assets)."""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from ka_common import wmat


def _rounded_rect(bm, hw, hh, r, seg, z):
    verts = []
    for cx, cy, a0 in ((hw - r, hh - r, 0), (-hw + r, hh - r, 90), (-hw + r, -hh + r, 180), (hw - r, -hh + r, 270)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            verts.append(bm.verts.new((cx + r * math.cos(a), cy + r * math.sin(a), z)))
    return verts


def _slab(name, hw, hh, r, thick, seg=10, bevel=0.0012):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    ring = _rounded_rect(bm, hw, hh, r, seg, -thick / 2)
    face = bm.faces.new(ring)
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    top = [v for v in ext['geom'] if isinstance(v, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0, thick), verts=top)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if bevel:
        m = ob.modifiers.new('bevel', 'BEVEL')
        m.width, m.segments, m.limit_method = bevel, 3, 'ANGLE'
        m.harden_normals = True
    for p in me.polygons:
        p.use_smooth = True
    return ob


def _mat(name, color, rough, metal=0.0, coat=0.0, emit=None, emit_strength=0.0):
    m = bpy.data.materials.new(name)
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    b.inputs['Coat Weight'].default_value = coat
    if emit:
        b.inputs['Emission Color'].default_value = (*emit, 1)
        b.inputs['Emission Strength'].default_value = emit_strength
    return m


def _screen_material():
    """Dark glass over a dim, cool UI glow: a soft vertical gradient plus faint feed rows."""
    m = bpy.data.materials.new('phone_screen')
    nt = m.node_tree
    b = nt.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (0.004, 0.005, 0.008, 1)
    b.inputs['Roughness'].default_value = 0.04
    b.inputs['Coat Weight'].default_value = 1.0
    b.inputs['Coat Roughness'].default_value = 0.02
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['Generated'], sep.inputs[0])
    ramp = nt.nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].color = (0.10, 0.20, 0.55, 1)
    ramp.color_ramp.elements[1].color = (0.45, 0.30, 0.85, 1)
    nt.links.new(sep.outputs['Y'], ramp.inputs[0])
    # feed rows: a sine band along the long axis, kept faint
    mul = nt.nodes.new('ShaderNodeMath'); mul.operation = 'MULTIPLY'; mul.inputs[1].default_value = 40.0
    nt.links.new(sep.outputs['Y'], mul.inputs[0])
    sin = nt.nodes.new('ShaderNodeMath'); sin.operation = 'SINE'
    nt.links.new(mul.outputs[0], sin.inputs[0])
    rows = nt.nodes.new('ShaderNodeMapRange')
    rows.inputs['From Min'].default_value = -1; rows.inputs['To Min'].default_value = 0.55; rows.inputs['To Max'].default_value = 1.0
    nt.links.new(sin.outputs[0], rows.inputs['Value'])
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 1.0
    nt.links.new(ramp.outputs['Color'], mix.inputs[6])
    nt.links.new(rows.outputs['Result'], mix.inputs[7])
    nt.links.new(mix.outputs[2], b.inputs['Emission Color'])
    b.inputs['Emission Strength'].default_value = 2.2
    return m


def make_phone(arm, bone, along=0.058, below=0.020, flip=False, roll_deg=0.0, slide=0.0, scale=1.0):
    """A generic modern phone: 72 x 151 x 8 mm rounded slab, satin graphite frame and back,
    dark glass screen with a thin bezel and a faint glow, a small two-lens camera island.
    Placed in the palm of `bone` (hand) and parented to it so it rides the pose.
    Hand frame: Y = fingers, Z = back of the hand (palm is -Z). Screen faces -Z (away from the palm).
    """
    hw, hh, t = 0.036, 0.0755, 0.0080
    body = _slab('phone_body', hw, hh, 0.010, t, bevel=0.0014)
    body.data.materials.append(_mat('phone_frame', (0.045, 0.045, 0.055), 0.32, metal=0.85))
    scr = _slab('phone_screen', hw - 0.0016, hh - 0.0016, 0.0086, 0.0004, bevel=0)
    scr.location.z = -(t / 2 + 0.0001)          # screen side is -Z of the phone
    scr.data.materials.append(_screen_material())
    back = _slab('phone_back', hw - 0.0012, hh - 0.0012, 0.0088, 0.0004, bevel=0)
    back.location.z = t / 2 + 0.0001
    back.data.materials.append(_mat('phone_back_glass', (0.11, 0.09, 0.20), 0.22, coat=1.0))
    cam = _slab('phone_cam', 0.0135, 0.0135, 0.0055, 0.0018, seg=6, bevel=0.0006)
    cam.location = (-hw + 0.019, hh - 0.019, t / 2 + 0.0009)
    cam.data.materials.append(_mat('phone_cam', (0.03, 0.03, 0.04), 0.2, metal=0.6, coat=1.0))
    lens_mat = _mat('phone_lens', (0.0, 0.0, 0.0), 0.05, coat=1.0)
    parts = [scr, back, cam]
    for dx, dy in ((-0.0048, 0.0048), (0.0048, -0.0048)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.0035, depth=0.0012,
                                            location=(cam.location.x + dx, cam.location.y + dy, t / 2 + 0.0022))
        ln = bpy.context.active_object
        ln.data.materials.append(lens_mat)
        parts.append(ln)
    for p in parts:
        p.parent = body
    # place in the palm
    M = wmat(arm, bone)
    X, Y, Z = (M.to_3x3() @ Vector(v) for v in ((1, 0, 0), (0, 1, 0), (0, 0, 1)))
    centre = M.translation + Y * along - Z * below + X * slide
    long_axis = -X if not flip else X
    pz = Z.normalized()                      # phone +Z (back) toward the palm, screen (-Z) away
    py = (long_axis - pz * long_axis.dot(pz)).normalized()
    py = Matrix.Rotation(math.radians(roll_deg), 3, pz) @ py
    px = py.cross(pz)
    R = Matrix((px, py, pz)).transposed().to_4x4()
    R.translation = centre
    body.parent = arm
    body.parent_type = 'BONE'
    body.parent_bone = bone
    body.matrix_world = R @ Matrix.Scale(scale, 4)
    return body, scr
