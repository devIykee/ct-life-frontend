"""World v3: contact render of processed FBX assets (one row per call) in neutral daylight.

Usage: Blender -b --factory-startup -P world_v3_preview.py -- <out.png> <lod 0|1|2> <a.fbx> [b.fbx ...]
"""
import bpy, sys, math, os
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
out, lod, files = a[0], int(a[1]), a[2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
x = 0.0
H = 0
for f in files:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=f)
    new = [o for o in bpy.data.objects if o not in before]
    keep = [o for o in new if o.type == 'MESH' and o.name.endswith(f'_LOD{lod}')] or [o for o in new if o.type == 'MESH'][:1]
    for o in new:
        if o.type == 'MESH' and o not in keep:
            bpy.data.objects.remove(o)
    for o in keep:
        o.parent = None
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        w = max(p.x for p in bb) - min(p.x for p in bb)
        H = max(H, max(p.z for p in bb))
        o.location.x += x + w / 2 - (max(p.x for p in bb) + min(p.x for p in bb)) / 2
        x += w + 2.0
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 1800, 700
g = bpy.data.objects.new('ground', bpy.data.meshes.new('g'))
import bmesh
bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=400); bm.to_mesh(g.data); bm.free()
sc.collection.objects.link(g)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam)
cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(x, H * 2.7) * 1.05
cam.location = (x / 2 - 1, -60, H * 0.5 + 18); cam.rotation_euler = (math.radians(73), 0, 0)
sc.camera = cam
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.data.angle = math.radians(2)
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35)); sc.collection.objects.link(sun)
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (0.55, 0.68, 0.9, 1); bg.inputs[1].default_value = 0.9
sc.view_settings.view_transform = 'AgX'
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
