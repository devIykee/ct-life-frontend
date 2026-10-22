"""World v3: quick look at a raw Sketchfab glb (3/4 ortho, auto-framed) with cluster numbers printed to the log.
Usage: Blender -b --factory-startup -P world_v3_rawview.py -- <in.glb> <out.png>"""
import bpy, sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=a[0])
import world_v3_lib as W
sys.argv = [sys.argv[0], '--']
ms = [o for o in bpy.data.objects if o.type == 'MESH']
mn, mx = W.world_bounds(ms)
c = (mn + mx) / 2
size = (mx - mn).length
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 1400, 900
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam)
cam.data.type = 'ORTHO'; cam.data.ortho_scale = size * 0.95
d = Vector((0.55, -1.0, 0.45)).normalized()
cam.location = c + d * size * 2
cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
cam.data.clip_end = size * 10
sc.camera = cam
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(45), 0, math.radians(-30)); sc.collection.objects.link(sun)
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.6, 0.65, 0.75, 1)
sc.view_settings.view_transform = 'AgX'
sc.render.filepath = a[1]
bpy.ops.render.render(write_still=True)
print('[raw] bounds', [round(x, 2) for x in mn], [round(x, 2) for x in mx], 'meshes', len(ms))
