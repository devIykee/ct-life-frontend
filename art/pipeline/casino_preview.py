"""The Rug Room (R3): look-dev render of the processed casino props with their atlases (alpha-blended glass), to check
the slot retexture reads the right way round and the sizes against a 1.8 m figure.
Usage: art/pipeline/blender.sh art/pipeline/casino_preview.py -- <out.png> [close]"""
import bpy, sys, os, math
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
OUTP = a[0]; CLOSE = len(a) > 1 and a[1] == 'close'
D = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'processed', 'world_v3', 'v3_casino'))
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene


def mat(name, prefix, blend):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(os.path.join(D, 'textures', prefix + '_BaseColor.png'))
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.35
    if blend:
        nt.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
        m.blend_method = 'BLEND' if hasattr(m, 'blend_method') else None
    return m


x = 0.0
items = ['cs_slot_machine'] if CLOSE else ['cs_slot_machine', 'cs_roulette_table', 'cs_poker_table', 'cs_blackjack_table']
for n in items:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(D, n + '.fbx'))
    new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    keep = [o for o in new if not o.name.endswith('_LOD1')]
    for o in new:
        if o not in keep: bpy.data.objects.remove(o)
    for o in keep:
        for i, sl in enumerate(o.material_slots):
            gl = sl.material and 'glass' in sl.material.name
            sl.material = mat(n + ('_g' if gl else ''), n, gl)
        bpy.context.view_layer.update()
        mn = min((o.matrix_world @ Vector(c)).x for c in o.bound_box); mx = max((o.matrix_world @ Vector(c)).x for c in o.bound_box)
        o.location.x += x - mn
        w = mx - mn
    x += w + 0.5
# a 1.8 m reference post
bpy.ops.mesh.primitive_cylinder_add(radius=0.12, depth=1.8, location=(-0.5, 0, 0.9))
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
if CLOSE:
    cam.location = (1.0, -2.6, 1.3); target = Vector((1.0, 0, 1.1)); cam.data.lens = 40
else:
    cam.location = (x / 2, -7.5, 3.2); target = Vector((x / 2, 0, 0.6)); cam.data.lens = 35
cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(20)); sc.collection.objects.link(sun)
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.35, 0.35, 0.4, 1)
sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x, sc.render.resolution_y = 1400, 900
sc.view_settings.view_transform = 'AgX'; sc.render.filepath = OUTP
bpy.ops.render.render(write_still=True)
print('[casino_preview]', OUTP)
