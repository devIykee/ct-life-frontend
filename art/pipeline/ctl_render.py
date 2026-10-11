"""Shared Eevee preview helpers for CT Life contact sheets (Blender 5.1, headless)."""
import bpy, math, mathutils

def engine_id():
    items = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    for cand in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        if cand in items:
            return cand
    return items[0]

def setup_scene(res=(1600, 900), samples=64, film_transparent=False, bg_top=(0.98, 0.86, 0.72), bg_bottom=(0.55, 0.72, 0.92), strength=0.9):
    sc = bpy.context.scene
    sc.render.engine = engine_id()
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = film_transparent
    try:
        sc.eevee.taa_render_samples = samples
    except Exception:
        pass
    for attr, val in (('use_shadows', True), ('use_raytracing', True), ('use_gtao', True)):
        try:
            setattr(sc.eevee, attr, val)
        except Exception:
            pass
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.view_settings.exposure = 0.0
    # gradient world
    w = bpy.data.worlds.new('CTL_World') if not sc.world else sc.world
    sc.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputWorld')
    bg = nt.nodes.new('ShaderNodeBackground')
    grad = nt.nodes.new('ShaderNodeValToRGB')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    tc = nt.nodes.new('ShaderNodeTexCoord')
    mr = nt.nodes.new('ShaderNodeMapRange')
    mr.inputs['From Min'].default_value = -0.2
    mr.inputs['From Max'].default_value = 0.8
    nt.links.new(tc.outputs['Generated'], sep.inputs[0])   # world: Generated = view direction
    nt.links.new(sep.outputs['Z'], mr.inputs['Value'])
    nt.links.new(mr.outputs['Result'], grad.inputs['Fac'])
    grad.color_ramp.elements[0].color = (*bg_bottom, 1)
    grad.color_ramp.elements[1].color = (*bg_top, 1)
    nt.links.new(grad.outputs['Color'], bg.inputs['Color'])
    bg.inputs['Strength'].default_value = strength
    nt.links.new(bg.outputs['Background'], out.inputs['Surface'])
    return sc

def add_sun(rot_deg=(50, 0, 35), energy=3.5, color=(1.0, 0.92, 0.8), angle_deg=8):
    d = bpy.data.lights.new('CTL_Sun', 'SUN')
    d.energy = energy
    d.color = color
    d.angle = math.radians(angle_deg)
    o = bpy.data.objects.new('CTL_Sun', d)
    bpy.context.scene.collection.objects.link(o)
    o.rotation_euler = [math.radians(a) for a in rot_deg]
    return o

def add_area(loc, target, energy=400, size=4, color=(1, 1, 1)):
    d = bpy.data.lights.new('CTL_Area', 'AREA')
    d.energy = energy
    d.size = size
    d.color = color
    o = bpy.data.objects.new('CTL_Area', d)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    look_at(o, target)
    return o

def look_at(obj, target):
    direction = mathutils.Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()

def add_camera(loc, target, lens=50, ortho_scale=None):
    c = bpy.data.cameras.new('CTL_Cam')
    c.lens = lens
    if ortho_scale:
        c.type = 'ORTHO'
        c.ortho_scale = ortho_scale
    o = bpy.data.objects.new('CTL_Cam', c)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    look_at(o, target)
    bpy.context.scene.camera = o
    return o

def add_ground(size=60, color=(0.82, 0.8, 0.76), z=0.0):
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, z))
    g = bpy.context.active_object
    g.name = 'CTL_Ground'
    m = bpy.data.materials.new('CTL_Ground')
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = 0.9
    g.data.materials.append(m)
    return g

def add_text(text, loc, size=0.15, color=(0.1, 0.1, 0.12), rot=(90, 0, 0), align='CENTER', emission=0.0):
    cu = bpy.data.curves.new('CTL_Text', 'FONT')
    cu.body = text
    cu.size = size
    cu.align_x = align
    o = bpy.data.objects.new('CTL_Text', cu)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = [math.radians(a) for a in rot]
    m = bpy.data.materials.new('CTL_TextMat')
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*color, 1)
    if emission:
        b.inputs['Emission Color'].default_value = (*color, 1)
        b.inputs['Emission Strength'].default_value = emission
    cu.materials.append(m)
    return o

def render(path):
    sc = bpy.context.scene
    sc.render.filepath = path
    sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    print('RENDERED', path)


def add_backdrop(width=40, color=(0.88, 0.85, 0.8), wall_y=3.0, radius=2.5, height=14, front=-40):
    """Seamless studio cyclorama: floor that curves up into a back wall (no horizon line)."""
    import bmesh
    prof = [(front, 0.0), (wall_y - radius, 0.0)]
    for i in range(1, 13):
        a = math.radians(90 * i / 12)
        prof.append((wall_y - radius + math.sin(a) * radius, radius - math.cos(a) * radius))
    prof.append((wall_y, height))
    bm = bmesh.new()
    rows = []
    for x in (-width / 2, width / 2):
        rows.append([bm.verts.new((x, y, z)) for y, z in prof])
    for i in range(len(prof) - 1):
        bm.faces.new((rows[0][i], rows[1][i], rows[1][i + 1], rows[0][i + 1]))
    me = bpy.data.meshes.new('CTL_Backdrop')
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new('CTL_Backdrop', me)
    bpy.context.scene.collection.objects.link(o)
    for p in me.polygons:
        p.use_smooth = True
    m = bpy.data.materials.new('CTL_Backdrop')
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = 0.95
    me.materials.append(m)
    return o
